@echo off
setlocal EnableExtensions
rem ---------------------------------------------------------------------------------------------
rem  SSPTMM - send code, docs and wiki changes to GitHub WITHOUT making a new release.
rem
rem   https://github.com/trappuss/SSPTMM
rem
rem  For changes between releases - the README, docs\, the branding art, wiki pages. The version
rem  and its release (zip, tag, notes) are left alone; SSPTMM-release-to-github.bat makes those.
rem
rem   1. Picks what to send, as the release script does: the newest of steam-workshop-ui.bundle from
rem      Claude ("Claude outputs" or beside this file) and your own steam-workshop-ui branch here.
rem   2. Checks GitHub's main and steam-workshop-ui hold nothing that is not in it. If they do (an
rem      edit made on github.com, say), it stops - Claude merges those in for you.
rem   3. Lists the commits it is about to send and waits for you to type Y.
rem   4. Pushes them to main and steam-workshop-ui, and copies the wiki\ pages to the wiki.
rem
rem  It never force-pushes, never tags and never builds. Everything it does is also written to
rem  logs\update-github.log.
rem ---------------------------------------------------------------------------------------------

pushd "%~dp0" || exit /b 1
set "ROOT=%~dp0"
if not exist "%ROOT%logs" mkdir "%ROOT%logs"
if not exist "%ROOT%release" mkdir "%ROOT%release"
set "LOG=%ROOT%logs\update-github.log"
set "REL=%ROOT%release"
set "REPO=trappuss/SSPTMM"
set "FORK_URL=https://github.com/trappuss/SSPTMM.git"
set "WIKI_URL=https://github.com/trappuss/SSPTMM.wiki.git"
rem The wiki commit is made as you, with the private address GitHub gives your account.
set "WIKI_NAME=trappuss"
set "WIKI_MAIL=52510142+trappuss@users.noreply.github.com"

> "%LOG%" echo SSPTMM update GitHub - %DATE% %TIME%

where git >nul 2>nul
if errorlevel 1 (
    call :say "git was not found. Install Git for Windows - https://git-scm.com - then run this again."
    goto :end_fail
)

rem ---------------------------------------------------------------- 1. what to send
call :say "[1/4] Picking what to send..."
set "BUNDLE="
if exist "%ROOT%Claude outputs\steam-workshop-ui.bundle" set "BUNDLE=%ROOT%Claude outputs\steam-workshop-ui.bundle"
if not defined BUNDLE if exist "%ROOT%steam-workshop-ui.bundle" set "BUNDLE=%ROOT%steam-workshop-ui.bundle"

set "BRANCH_SHA="
for /f %%c in ('git rev-parse --verify --quiet refs/heads/steam-workshop-ui 2^>nul') do set "BRANCH_SHA=%%c"

set "SEND="
if defined BUNDLE goto :have_bundle
if not defined BRANCH_SHA (
    call :say "There is no steam-workshop-ui.bundle and no steam-workshop-ui branch here - nothing to send."
    goto :end_fail
)
set "SEND=%BRANCH_SHA%"
call :say "      No bundle found - sending your steam-workshop-ui branch as it is."
goto :remote

:have_bundle
call git fetch --quiet "%BUNDLE%" steam-workshop-ui >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "The bundle could not be read. logs\update-github.log says why."
    goto :end_fail
)
set "BUNDLE_SHA="
for /f %%c in ('git rev-parse FETCH_HEAD') do set "BUNDLE_SHA=%%c"
set "SEND=%BUNDLE_SHA%"

if not defined BRANCH_SHA goto :remote
if "%BRANCH_SHA%"=="%BUNDLE_SHA%" goto :remote
call git merge-base --is-ancestor %BUNDLE_SHA% %BRANCH_SHA%
if not errorlevel 1 (
    set "SEND=%BRANCH_SHA%"
    call :say "      Your steam-workshop-ui branch is newer than the bundle - sending your branch."
    goto :remote
)
call git merge-base --is-ancestor %BRANCH_SHA% %BUNDLE_SHA%
if not errorlevel 1 goto :remote
call :say "Your steam-workshop-ui branch and the bundle have each got commits the other has not."
call :say "Nothing was sent. Run SSPTMM-update-and-run.bat first - it says what to do about that."
goto :end_fail

rem ---------------------------------------------------------------- 2. GitHub's side
:remote
call :say "[2/4] Checking GitHub's copy..."
set "HAVE_FORK="
for /f "delims=" %%u in ('git remote get-url fork 2^>nul') do set "HAVE_FORK=%%u"
if not defined HAVE_FORK (
    call git remote add fork "%FORK_URL%" >> "%LOG%" 2>&1
    if errorlevel 1 (
        call :say "Could not add the repository as a remote. logs\update-github.log says why."
        goto :end_fail
    )
    goto :fetch
)
rem The same repository written without .git at the end counts as the same.
if /i "%HAVE_FORK%.git"=="%FORK_URL%" goto :fetch
if /i not "%HAVE_FORK%"=="%FORK_URL%" (
    call :say "This folder has a remote named fork pointing somewhere else:"
    call :say "   %HAVE_FORK%"
    call :say "Run SSPTMM-release-to-github.bat once - it moves an old address over - then this again."
    goto :end_fail
)
:fetch
call git fetch --quiet fork >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "Could not read the repository on GitHub. logs\update-github.log says why. Nothing was sent."
    goto :end_fail
)
call :check_behind main
if errorlevel 1 goto :end_fail
call :check_behind steam-workshop-ui
if errorlevel 1 goto :end_fail

rem ---------------------------------------------------------------- 3. confirm
set "COUNT=0"
for /f %%n in ('git rev-list --count refs/remotes/fork/main..%SEND% 2^>nul') do set "COUNT=%%n"
call :say ""
call :say "Commits to send to GitHub's main (%COUNT%):"
call git log --oneline --no-decorate refs/remotes/fork/main..%SEND%
call git log --oneline --no-decorate refs/remotes/fork/main..%SEND% >> "%LOG%" 2>&1
call :say ""
call :say "Also: the wiki\ pages of that commit go to the wiki. No release, tag or build is made."
set "GO="
set /p "GO=Type Y and press Enter to send, or just press Enter to stop: "
if defined GO set "GO=%GO:~0,1%"
if defined GO set "GO=%GO:"=%"
if /i not "%GO%"=="Y" (
    >> "%LOG%" echo Answer: not Y
    call :say "Stopped. Nothing was sent."
    goto :end_ok
)

rem ---------------------------------------------------------------- 4. send
call :say "[3/4] Pushing - a GitHub sign-in window may open, sign in as trappuss..."
call git push fork %SEND%:refs/heads/steam-workshop-ui >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_push
call git push fork %SEND%:refs/heads/main >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_push
call :say "      main and steam-workshop-ui are on GitHub."

call :say "[4/4] Updating the wiki..."
call git cat-file -e %SEND%:wiki/Home.md 2>nul
if errorlevel 1 (
    call :say "      This commit has no wiki\ pages - skipped."
    goto :done
)
if exist "%REL%\wiki\" rmdir /s /q "%REL%\wiki"
call git clone --quiet "%WIKI_URL%" "%REL%\wiki" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_wiki
rem Each page straight out of the commit, byte for byte.
for /f "delims=" %%f in ('git ls-tree --name-only %SEND% wiki/') do (
    call git show "%SEND%:%%f" > "%REL%\wiki\%%~nxf"
    if errorlevel 1 goto :fail_wiki
)
call git -C "%REL%\wiki" add -A >> "%LOG%" 2>&1
call git -C "%REL%\wiki" diff --cached --quiet
if not errorlevel 1 (
    call :say "      The wiki already matches - nothing to change."
    goto :done
)
call git -C "%REL%\wiki" -c "user.name=%WIKI_NAME%" -c "user.email=%WIKI_MAIL%" commit --quiet -m "Wiki update" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_wiki
call git -C "%REL%\wiki" push origin HEAD >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_wiki
call :say "      Wiki updated."

:done
call :say ""
call :say "Done."
call :say "   Code   https://github.com/%REPO%"
call :say "   Wiki   https://github.com/%REPO%/wiki"
goto :end_ok

rem ---------------------------------------------------------------- helpers
:check_behind
set "THEIRS="
for /f %%c in ('git rev-parse --verify --quiet refs/remotes/fork/%~1 2^>nul') do set "THEIRS=%%c"
if not defined THEIRS exit /b 0
call git merge-base --is-ancestor %THEIRS% %SEND%
if not errorlevel 1 exit /b 0
call :say "GitHub's %~1 has commits that are not in what is being sent - an edit made on github.com?"
call :say "Nothing was sent. Tell Claude: it merges them in and sends you a new bundle."
exit /b 1

:fail_push
call :say "A push did not go through - the end of logs\update-github.log says why. Most often a"
call :say "cancelled sign-in. Nothing was force-pushed; run this again once that is sorted."
goto :end_fail

:fail_wiki
call :say "The code is on GitHub, but the wiki could not be updated - the end of"
call :say "logs\update-github.log says why. Run this again to retry; the code push is skipped."
goto :end_fail

:say
if "%~1"=="" goto :say_blank
set "MSG=%~1"
setlocal EnableDelayedExpansion
echo(!MSG!
>> "!LOG!" echo(!MSG!
endlocal
exit /b 0
:say_blank
echo.
>> "%LOG%" echo.
exit /b 0

:end_ok
popd
echo.
pause
exit /b 0

:end_fail
popd
echo.
pause
exit /b 1
