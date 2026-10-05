@echo off
setlocal EnableExtensions
rem ---------------------------------------------------------------------------------------------
rem  SSPTMM (Steamified SPT Mod Manager) - publish a release on GitHub, in one double-click.
rem
rem   https://github.com/trappuss/SSPTMM
rem
rem   1. Picks what to release: the newest of the steam-workshop-ui.bundle from Claude
rem      ("Claude outputs" or beside this file) and your own steam-workshop-ui branch here.
rem   2. Checks GitHub's main and steam-workshop-ui hold nothing that is not in it, and reads the
rem      version from build\Directory.Build.props - the tag is v plus that version.
rem   3. Builds it in a separate folder under %TEMP% - your own files and branch stay as they are -
rem      runs the tests, publishes SSPTMM.exe and zips it, inside a folder SSPTMM\, as
rem      release\SSPTMM-<version>-win-x64.zip.
rem   4. Shows what it is about to publish and waits for you to type Y. Anything else stops here
rem      with nothing sent; the zip stays in release\ for you to try.
rem   5. Pushes it to GitHub as both steam-workshop-ui and main, tags it, creates the GitHub release
rem      with the zip and this version's section of CHANGELOG.md as its notes, and copies the
rem      wiki\ pages to the wiki.
rem
rem  It never force-pushes. If GitHub has commits this does not, it stops before sending anything.
rem  Safe to run again: a step already done (same commit, same tag, release already there) is
rem  skipped or updated, not repeated.
rem
rem  Needs: Git for Windows; the GitHub CLI (it offers to install it with winget); a .NET 9 SDK
rem  (run SSPTMM-build-and-run.bat once first if this PC has none). Sign in as trappuss when asked.
rem  Everything it does is also written to logs\release.log.
rem ---------------------------------------------------------------------------------------------

pushd "%~dp0" || exit /b 1
set "ROOT=%~dp0"
if not exist "%ROOT%logs" mkdir "%ROOT%logs"
if not exist "%ROOT%release" mkdir "%ROOT%release"
set "LOG=%ROOT%logs\release.log"
set "REL=%ROOT%release"
set "REPO=trappuss/SSPTMM"
set "FORK_URL=https://github.com/trappuss/SSPTMM.git"
set "WIKI_URL=https://github.com/trappuss/SSPTMM.wiki.git"
rem The repository's names before it was renamed: a "fork" remote still pointing at either is moved over.
set "OLD_FORK_URL=https://github.com/trappuss/SSSPTMM.git"
set "OLDER_FORK_URL=https://github.com/trappuss/SPTMM_SteamEdition.git"
rem The build happens in a short path: .NET's build folders nest deep, and the repo folder plus
rem release\ would come close to Windows' 260-character path limit.
set "WT=%TEMP%\SSPTMM-release-src"
set "LOCALSDK=%ROOT%.dotnet"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

> "%LOG%" echo SSPTMM release - %DATE% %TIME%

rem (git, gh and dotnet through CALL throughout: some installs provide them as .cmd wrappers, and
rem a script run without CALL never returns to this one.)

rem ---------------------------------------------------------------- tools
where git >nul 2>nul
if errorlevel 1 (
    call :say "git was not found. Install Git for Windows - https://git-scm.com - then run this again."
    goto :end_fail
)

set "GH="
where gh >nul 2>nul
if not errorlevel 1 set "GH=gh"
if not defined GH if exist "%ProgramFiles%\GitHub CLI\gh.exe" set "GH=%ProgramFiles%\GitHub CLI\gh.exe"
if defined GH goto :have_gh
call :say "The GitHub CLI - gh - is needed to create the release, and it is not on this PC."
where winget >nul 2>nul
if errorlevel 1 goto :no_gh
set "ANSWER="
set /p "ANSWER=Install it now with winget? Type Y and press Enter: "
if defined ANSWER set "ANSWER=%ANSWER:~0,1%"
if defined ANSWER set "ANSWER=%ANSWER:"=%"
if /i not "%ANSWER%"=="Y" goto :no_gh
call :say "Installing the GitHub CLI..."
winget install --id GitHub.cli -e --source winget
if exist "%ProgramFiles%\GitHub CLI\gh.exe" set "GH=%ProgramFiles%\GitHub CLI\gh.exe"
if defined GH goto :have_gh
call :say "It installed, but this window cannot see it yet. Close this window and run this file again."
goto :end_fail
:no_gh
call :say "Install it from https://cli.github.com, then run this again. Nothing was sent."
goto :end_fail
:have_gh

set "DOTNET="
if not exist "%LOCALSDK%\dotnet.exe" goto :dotnet_path
"%LOCALSDK%\dotnet.exe" --list-sdks 2>nul | findstr /b /c:"9." >nul
if not errorlevel 1 set "DOTNET=%LOCALSDK%\dotnet.exe"
:dotnet_path
if defined DOTNET goto :have_dotnet
where dotnet >nul 2>nul
if errorlevel 1 goto :no_dotnet
call dotnet --list-sdks 2>nul | findstr /b /c:"9." >nul
if not errorlevel 1 set "DOTNET=dotnet"
if defined DOTNET goto :have_dotnet
:no_dotnet
call :say "No .NET 9 SDK was found. Run SSPTMM-build-and-run.bat once - it installs one into .dotnet\ -"
call :say "then run this again. Nothing was sent."
goto :end_fail
:have_dotnet

rem ---------------------------------------------------------------- 1. what to release
call :say "[1/6] Picking what to release..."
set "BUNDLE="
if exist "%ROOT%Claude outputs\steam-workshop-ui.bundle" set "BUNDLE=%ROOT%Claude outputs\steam-workshop-ui.bundle"
if not defined BUNDLE if exist "%ROOT%steam-workshop-ui.bundle" set "BUNDLE=%ROOT%steam-workshop-ui.bundle"

set "BRANCH_SHA="
for /f %%c in ('git rev-parse --verify --quiet refs/heads/steam-workshop-ui 2^>nul') do set "BRANCH_SHA=%%c"

set "SEND="
if defined BUNDLE goto :have_bundle
if not defined BRANCH_SHA (
    call :say "There is no steam-workshop-ui.bundle and no steam-workshop-ui branch here - nothing to release."
    goto :end_fail
)
set "SEND=%BRANCH_SHA%"
call :say "      No bundle found - releasing your steam-workshop-ui branch as it is."
goto :remote

:have_bundle
call git fetch --quiet "%BUNDLE%" steam-workshop-ui >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "The bundle could not be read. logs\release.log says why."
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
    call :say "      Your steam-workshop-ui branch is newer than the bundle - releasing your branch."
    goto :remote
)
call git merge-base --is-ancestor %BRANCH_SHA% %BUNDLE_SHA%
if not errorlevel 1 goto :remote

call :say "Your steam-workshop-ui branch and the bundle have each got commits the other has not."
call :say "Nothing was sent. Run SSPTMM-update-and-run.bat first - it says what to do about that."
goto :end_fail

rem ---------------------------------------------------------------- 2. GitHub's side
:remote
call :say "[2/6] Checking GitHub's copy..."
set "HAVE_FORK="
for /f "delims=" %%u in ('git remote get-url fork 2^>nul') do set "HAVE_FORK=%%u"
if not defined HAVE_FORK (
    call git remote add fork "%FORK_URL%" >> "%LOG%" 2>&1
    if errorlevel 1 (
        call :say "Could not add the repository as a remote. logs\release.log says why."
        goto :end_fail
    )
    call :say "      Added the repository to this folder's git as the remote named fork."
    goto :fetch
)
if /i "%HAVE_FORK%"=="%OLDER_FORK_URL%" set "HAVE_FORK=%OLD_FORK_URL%"
rem The same repository written without .git at the end counts as the same.
if /i "%HAVE_FORK%.git"=="%FORK_URL%" goto :fetch
if /i "%HAVE_FORK%"=="%OLD_FORK_URL%" (
    call git remote set-url fork "%FORK_URL%" >> "%LOG%" 2>&1
    if errorlevel 1 (
        call :say "Could not point the fork remote at the renamed repository. logs\release.log says why."
        goto :end_fail
    )
    call :say "      Pointed the fork remote at the renamed repository, SSPTMM."
    goto :fetch
)
if /i not "%HAVE_FORK%"=="%FORK_URL%" (
    call :say "This folder already has a remote named fork, pointing somewhere else:"
    call :say "   %HAVE_FORK%"
    call :say "Nothing was sent. Rename or remove that remote, then run this again."
    goto :end_fail
)

:fetch
call git fetch --quiet fork >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "Could not read the repository on GitHub. logs\release.log says why. Nothing was sent."
    goto :end_fail
)
call :check_behind main
if errorlevel 1 goto :end_fail
call :check_behind steam-workshop-ui
if errorlevel 1 goto :end_fail

rem The version, from the commit being released.
call git show %SEND%:build/Directory.Build.props > "%REL%\props.txt" 2>> "%LOG%"
rem The line is "<Version>1.0.0</Version>", indented: tokens=* trims the indent, then < and >
rem split it as Version / 1.0.0 / /Version.
set "VLINE="
set "VERSION="
for /f "tokens=*" %%l in ('findstr /c:"<Version>" "%REL%\props.txt"') do if not defined VLINE set "VLINE=%%l"
if defined VLINE for /f "tokens=2 delims=<>" %%v in ("%VLINE%") do set "VERSION=%%v"
del /q "%REL%\props.txt" >nul 2>nul
if not defined VERSION (
    call :say "Could not read the version from build\Directory.Build.props. Nothing was sent."
    goto :end_fail
)
set "TAG=v%VERSION%"
set "PKG=SSPTMM-%VERSION%-win-x64"
set "ZIP=%REL%\%PKG%.zip"
rem The zip holds one folder, SSPTMM\: unzipped into the SPT folder it becomes <SPT>\SSPTMM\, a
rem folder of its own, as sp-mod.com expects archives to unzip straight into the SPT root.
set "APPDIR=%REL%\%PKG%\SSPTMM"

rem The tag: fine if it is new, or already on this very commit - a rerun. Anywhere else, stop.
set "TAG_DONE="
set "REMOTE_TAG="
rem An annotated tag is listed twice - the tag, then "^{}" with the commit it points at - so the
rem last line is always the commit.
for /f %%c in ('git ls-remote --tags fork refs/tags/%TAG% "refs/tags/%TAG%^{}" 2^>nul') do set "REMOTE_TAG=%%c"
if not defined REMOTE_TAG goto :tag_local
if /i not "%REMOTE_TAG%"=="%SEND%" (
    call :say "GitHub already has a tag %TAG% on a different commit, %REMOTE_TAG%."
    call :say "Raise the version in build\Directory.Build.props for a new release. Nothing was sent."
    goto :end_fail
)
set "TAG_DONE=1"
:tag_local
set "LOCAL_TAG="
for /f %%c in ('git rev-list -n 1 refs/tags/%TAG% -- 2^>nul') do set "LOCAL_TAG=%%c"
if not defined LOCAL_TAG goto :tag_ok
if /i not "%LOCAL_TAG%"=="%SEND%" (
    call :say "This folder already has a tag %TAG% on a different commit, %LOCAL_TAG%."
    call :say "Raise the version in build\Directory.Build.props for a new release. Nothing was sent."
    goto :end_fail
)
:tag_ok
call :say "      Version %VERSION%, commit %SEND%."

rem ---------------------------------------------------------------- 3. build, test, zip
call :say "[3/6] Building %TAG% in a separate folder..."
call git worktree remove --force "%WT%" >> "%LOG%" 2>&1
if exist "%WT%\" rmdir /s /q "%WT%"
rem -f: a record of this folder left by an interrupted run is reused, not an error. (No "worktree
rem prune": it would also forget any other worktree of yours on a drive that is unplugged.)
call git worktree add -f --detach "%WT%" %SEND% >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "Could not check the commit out into %WT%. logs\release.log says why. Nothing was sent."
    goto :end_fail
)
if not exist "%WT%\CHANGELOG.md" (
    call :say "This commit has no CHANGELOG.md for the release notes. Nothing was sent."
    goto :fail_cleanup
)

rem The release notes: this version's own section of CHANGELOG.md, from its "# SSPTMM <version>"
rem heading to the next version's. Written without a byte-order mark, which would stop GitHub
rem reading the first line as a heading.
set "NOTES=%REL%\notes-%VERSION%.md"
if exist "%NOTES%" del /q "%NOTES%"
call powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference = 'Stop'; $lines = @(Get-Content -LiteralPath ($env:WT + '\CHANGELOG.md') -Encoding UTF8); $start = [Array]::IndexOf($lines, '# SSPTMM ' + $env:VERSION); if ($start -lt 0) { exit 3 }; $end = $lines.Count; for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i] -like '# SSPTMM *') { $end = $i; break } }; [IO.File]::WriteAllLines($env:NOTES, [string[]]$lines[$start..($end - 1)]) }" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_notes
if not exist "%NOTES%" goto :fail_notes

call "%DOTNET%" build "%WT%\src\TCFModManager.App" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_build
call :say "[4/6] Running the tests..."
call "%DOTNET%" test "%WT%\Tests\TCFModManager.Core.Tests" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "The tests failed, so nothing was released. logs\release.log names the failing test."
    goto :fail_cleanup
)
call :say "      All tests passed. Publishing and zipping..."
if exist "%REL%\%PKG%\" rmdir /s /q "%REL%\%PKG%"
if exist "%ZIP%" del /q "%ZIP%"
if exist "%APPDIR%\SSPTMM.exe" goto :fail_in_use
if exist "%ZIP%" goto :fail_in_use
call "%DOTNET%" publish "%WT%\src\TCFModManager.App" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%APPDIR%" --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_build
if not exist "%APPDIR%\SSPTMM.exe" goto :fail_build
rem Debug symbols are for crash dumps here, not for players.
del /q "%APPDIR%\*.pdb" >nul 2>nul
copy /y "%WT%\CHANGELOG.md" "%APPDIR%\CHANGELOG.md" >nul
rem .NET's own zip writer: forward-slash entry names, which every unzip tool reads.
call powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference = 'Stop'; Add-Type -AssemblyName System.IO.Compression.FileSystem; Add-Type -AssemblyName System.IO.Compression; [IO.Compression.ZipFile]::CreateFromDirectory($env:REL + '\' + $env:PKG, $env:ZIP) }" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_zip
if not exist "%ZIP%" goto :fail_zip
set "ZIPBYTES=0"
for %%A in ("%ZIP%") do set "ZIPBYTES=%%~zA"
set /a ZIPMB=ZIPBYTES/1048576

rem ---------------------------------------------------------------- 4. confirm
call :say ""
call :say "Ready to publish SSPTMM %VERSION%:"
call :say "   commit   %SEND%"
call :say "   zip      release\%PKG%.zip - %ZIPMB% MB"
call :say "   GitHub   %REPO%: branches main and steam-workshop-ui, tag %TAG%,"
call :say "            a release named SSPTMM %VERSION% with the zip and its CHANGELOG.md section, and the wiki pages"
call :say ""
call :say "You can try release\%PKG%\SSPTMM\SSPTMM.exe first - it keeps its own settings beside it."
set "GO="
set /p "GO=Type Y and press Enter to publish, or just press Enter to stop: "
rem Only its first letter counts, so "yes" works too and nothing stray after it matters.
rem A quote typed as the answer is dropped; anything else is only ever used inside quotes.
if defined GO set "GO=%GO:~0,1%"
if defined GO set "GO=%GO:"=%"
if /i not "%GO%"=="Y" (
    >> "%LOG%" echo Answer: not Y
    call :say "Stopped. Nothing was sent; the zip is in release\."
    call :cleanup
    goto :end_ok
)

rem ---------------------------------------------------------------- 5. publish
call :say "[5/6] Signing in to GitHub..."
call "%GH%" auth status --hostname github.com >> "%LOG%" 2>&1
if not errorlevel 1 goto :signed_in
call :say "      A browser window opens for you to sign in as trappuss - follow what it says here."
call "%GH%" auth login --hostname github.com --git-protocol https --web
call "%GH%" auth status --hostname github.com >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "Not signed in to the GitHub CLI, so nothing was sent. Run this again to retry."
    goto :fail_cleanup
)
:signed_in
set "GH_LOGIN="
set "GH_ID="
for /f "delims=" %%l in ('call "%GH%" api user --jq ".login" 2^>nul') do set "GH_LOGIN=%%l"
for /f "delims=" %%l in ('call "%GH%" api user --jq ".id" 2^>nul') do set "GH_ID=%%l"
call :say "      Signed in as %GH_LOGIN%."

call :say "      Pushing the code - a GitHub sign-in window may open for git, sign in as trappuss..."
call git push fork %SEND%:refs/heads/steam-workshop-ui >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_push
call git push fork %SEND%:refs/heads/main >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_push
if defined TAG_DONE goto :tag_pushed
if not defined LOCAL_TAG call git tag %TAG% %SEND% >> "%LOG%" 2>&1
call git push fork refs/tags/%TAG% >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_push
:tag_pushed
call :say "      main, steam-workshop-ui and %TAG% are on GitHub."

call :say "      Creating the release and uploading the zip - %ZIPMB% MB, this takes a while..."
call "%GH%" release view %TAG% --repo %REPO% >> "%LOG%" 2>&1
if not errorlevel 1 goto :release_update
call "%GH%" release create %TAG% "%ZIP%" --repo %REPO% --verify-tag --title "SSPTMM %VERSION%" --notes-file "%NOTES%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_release
goto :release_done
:release_update
call :say "      The release is already there - replacing its zip and notes."
call "%GH%" release upload %TAG% "%ZIP%" --repo %REPO% --clobber >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_release
call "%GH%" release edit %TAG% --repo %REPO% --title "SSPTMM %VERSION%" --notes-file "%NOTES%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_release
:release_done

rem ---------------------------------------------------------------- 6. wiki
call :say "[6/6] Updating the wiki..."
if not exist "%WT%\wiki\Home.md" (
    call :say "      This commit has no wiki\ pages - skipped."
    goto :wiki_done
)
if exist "%REL%\wiki\" rmdir /s /q "%REL%\wiki"
call git clone --quiet "%WIKI_URL%" "%REL%\wiki" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_wiki_clone
copy /y "%WT%\wiki\*.md" "%REL%\wiki\" >nul
if errorlevel 1 goto :fail_wiki
call git -C "%REL%\wiki" add -A >> "%LOG%" 2>&1
call git -C "%REL%\wiki" diff --cached --quiet
if not errorlevel 1 (
    call :say "      The wiki already matches - nothing to change."
    goto :wiki_done
)
set "WIKI_NAME=%GH_LOGIN%"
if not defined WIKI_NAME set "WIKI_NAME=trappuss"
set "WIKI_MAIL=%GH_ID%+%GH_LOGIN%@users.noreply.github.com"
if not defined GH_ID set "WIKI_MAIL=%WIKI_NAME%@users.noreply.github.com"
call git -C "%REL%\wiki" -c "user.name=%WIKI_NAME%" -c "user.email=%WIKI_MAIL%" commit --quiet -m "Wiki for SSPTMM %VERSION%" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_wiki
call git -C "%REL%\wiki" push origin HEAD >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_wiki
call :say "      Wiki updated."
:wiki_done

call :cleanup
call :say ""
call :say "Done."
call :say "   Release   https://github.com/%REPO%/releases/tag/%TAG%"
call :say "   Code      https://github.com/%REPO%"
call :say "   Wiki      https://github.com/%REPO%/wiki"
goto :end_ok

rem ---------------------------------------------------------------- failures
:fail_build
call :say "The build failed, so nothing was released. The first line containing 'error' in"
call :say "logs\release.log says why."
goto :fail_cleanup

:fail_notes
call :say "CHANGELOG.md has no section headed '# SSPTMM %VERSION%' for the release notes."
call :say "Add one at the top - see the 1.0.0 section for the layout. Nothing was sent."
goto :fail_cleanup

:fail_in_use
call :say "The last build in release\%PKG%\ could not be cleared - close SSPTMM.exe if it is"
call :say "running from there, then run this again. Nothing was sent."
goto :fail_cleanup

:fail_zip
call :say "Could not zip the build. logs\release.log says why. Nothing was sent."
goto :fail_cleanup

:fail_push
call :say "A push did not go through - the end of logs\release.log says why. Most often a cancelled"
call :say "sign-in. Nothing was force-pushed; run this again once that is sorted."
goto :fail_cleanup

:fail_release
call :say "The code and tag are on GitHub, but the release could not be created or updated - the end"
call :say "of logs\release.log says why. Run this again: it picks up from there."
goto :fail_cleanup

:fail_wiki_clone
call :say "Could not open the wiki. If it was never used, open https://github.com/%REPO%/wiki ,"
call :say "save any first page there, then run this again. The release itself is done."
goto :fail_cleanup

:fail_wiki
call :say "The wiki could not be updated - the end of logs\release.log says why. The release itself"
call :say "is done; run this again to retry the wiki."
goto :fail_cleanup

:fail_cleanup
call :cleanup
goto :end_fail

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

rem ---------------------------------------------------------------- helpers
:check_behind
rem Stops when GitHub's branch %1 has commits that are not in what is being released.
set "THEIRS="
for /f %%c in ('git rev-parse --verify --quiet refs/remotes/fork/%~1 2^>nul') do set "THEIRS=%%c"
if not defined THEIRS exit /b 0
call git merge-base --is-ancestor %THEIRS% %SEND%
if not errorlevel 1 exit /b 0
call :say "GitHub's %~1 has commits that are not in what is being released."
call :say "Nothing was sent. Get those commits into your steam-workshop-ui branch first."
exit /b 1

:cleanup
call git worktree remove --force "%WT%" >> "%LOG%" 2>&1
if exist "%WT%\" rmdir /s /q "%WT%"
exit /b 0

:say
rem The message is echoed through delayed expansion, so a path holding "&" or ")" prints as text
rem instead of running as a command. It is taken into MSG before delayed expansion is on, so a "!"
rem in it survives too.
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
