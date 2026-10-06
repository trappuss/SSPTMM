@echo off
setlocal EnableExtensions
rem ---------------------------------------------------------------------------------------------
rem  SSPTMM (Steamified SPT Mod Manager) - update, build, test and run, in one double-click.
rem
rem   1. Brings in Claude's update, if there is one: "Claude outputs\steam-workshop-ui.bundle" (or
rem      one beside this file) goes into your steam-workshop-ui branch - moved forward, or merged
rem      with commits of your own; nothing of yours is rewritten. Local changes that are already
rem      what the update has (a file only a build changed) are set back first so it can land.
rem   2. Finds a .NET 9 SDK. If there isn't one, installs one into .dotnet\ beside this file
rem      (Microsoft's own dotnet-install script: no admin rights, nothing installed system-wide).
rem   3. Builds the app (that build also writes the pseudo-locale file the tests read).
rem   4. Runs the test suite. A failure stops here and says so.
rem   5. Publishes a self-contained SSPTMM.exe into dist\SSPTMM\ (close it first if it is open).
rem   6. Starts it.
rem
rem  SSPTMM-upload-to-github.bat then puts it on GitHub. Everything this does is also written to
rem  logs\build.log.
rem
rem  The build in dist\SSPTMM\ keeps its own Data\ folder (settings, catalog cache) next to its
rem  exe, as the app always does - so it never touches TCF Mod Manager or another copy you already
rem  use. Point it at your SPT folder once in Options, as with a fresh install. A build from before
rem  the rename (dist\steam-ui\) is moved to dist\SSPTMM\ once, Data and all.
rem ---------------------------------------------------------------------------------------------

rem Step 1 can replace this very file, and cmd reads a .bat as it runs it - a file changed under a
rem running copy runs garbage. So it runs from a copy of itself in %TEMP%, told where the folder is.
if /i "%~1"=="--running-copy" goto :running_copy
copy /y "%~f0" "%TEMP%\SSPTMM-build-and-run.running.bat" >nul
if errorlevel 1 (
    echo Could not copy this file to %TEMP% to run it from there.
    pause
    exit /b 1
)
"%TEMP%\SSPTMM-build-and-run.running.bat" --running-copy "%~dp0."
:running_copy

rem pushd rather than cd: it also works when this folder is on a network share (\\server\share),
rem where cd cannot go and the relative paths below would otherwise point at C:\Windows.
pushd "%~2" || exit /b 1
set "ROOT=%CD%\"

if not exist "%ROOT%logs" mkdir "%ROOT%logs"
set "LOG=%ROOT%logs\build.log"
set "OUT=%ROOT%dist\SSPTMM"
set "OLD_OUT=%ROOT%dist\steam-ui"
set "LOCALSDK=%ROOT%.dotnet"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
rem No pager: a long git listing would otherwise wait at a ":" prompt that looks like a hang.
set "GIT_PAGER=cat"

> "%LOG%" echo SSPTMM build - %DATE% %TIME%

rem The folder's name before the app was SSPTMM: moved once, so its settings and caches come along.
if exist "%OLD_OUT%\" if not exist "%OUT%\" (
    move "%OLD_OUT%" "%OUT%" >> "%LOG%" 2>&1
    if errorlevel 1 goto :fail_move
    call :say "      Moved dist\steam-ui to dist\SSPTMM, settings and all."
)

rem ---------------------------------------------------------------- 1. Claude's update
call :say "[1/6] Claude's update..."
call :update
if errorlevel 1 goto :end_fail

call :say "[2/6] Looking for the .NET 9 SDK..."
set "DOTNET="
if exist "%LOCALSDK%\dotnet.exe" (
    "%LOCALSDK%\dotnet.exe" --list-sdks 2>nul | findstr /b /c:"9." >nul
    if not errorlevel 1 set "DOTNET=%LOCALSDK%\dotnet.exe"
)
if not defined DOTNET (
    where dotnet >nul 2>nul
    if not errorlevel 1 (
        dotnet --list-sdks 2>nul | findstr /b /c:"9." >nul
        if not errorlevel 1 set "DOTNET=dotnet"
    )
)
if not defined DOTNET (
    call :say "      No .NET 9 SDK on this PC - installing one into .dotnet\ (one time, about 250 MB)..."
    powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference = 'Stop'; [Net.ServicePointManager]::SecurityProtocol = 'Tls12'; $script = Join-Path $env:TEMP 'dotnet-install.ps1'; Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UseBasicParsing; & $script -Channel 9.0 -InstallDir $env:LOCALSDK }" >> "%LOG%" 2>&1
    if errorlevel 1 goto :fail_sdk
    if not exist "%LOCALSDK%\dotnet.exe" goto :fail_sdk
    set "DOTNET=%LOCALSDK%\dotnet.exe"
)
call :say "      Found: %DOTNET%"

call :say "[3/6] Building the app..."
"%DOTNET%" build "src\TCFModManager.App" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_build

call :say "[4/6] Running the tests..."
"%DOTNET%" test "Tests\TCFModManager.Core.Tests" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_tests
call :say "      All tests passed."

call :say "[5/6] Publishing SSPTMM.exe into dist\SSPTMM\ ..."
"%DOTNET%" publish "src\TCFModManager.App" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUT%" --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_build
if not exist "%OUT%\SSPTMM.exe" goto :fail_build
rem Its name before it became SSPTMM: left over from an earlier build, it would only confuse.
if exist "%OUT%\TCFModManager.exe" del /q "%OUT%\TCFModManager.exe"
if exist "%OUT%\TCFModManager.pdb" del /q "%OUT%\TCFModManager.pdb"

call :say "[6/6] Starting it..."
start "" "%OUT%\SSPTMM.exe"
call :say ""
call :say "Done. The exe is dist\SSPTMM\SSPTMM.exe"
call :say "Its own log files are in dist\SSPTMM\Data\logs\"
echo.
pause
popd
exit /b 0

:fail_move
call :say ""
call :say "Could not move dist\steam-ui to dist\SSPTMM - usually the app is still open. Close it and run this again."
goto :end_fail

:fail_sdk
call :say ""
call :say "Could not install the .NET 9 SDK. The reason is at the end of logs\build.log."
goto :end_fail

:fail_tests
call :say ""
call :say "The tests failed, so nothing was built. logs\build.log names the failing test."
goto :end_fail

:fail_build
call :say ""
call :say "The build failed. The first line containing 'error' in logs\build.log says why."
call :say "If SSPTMM from dist\SSPTMM\ is open, close it first: an open exe can't be replaced."
goto :end_fail

:end_fail
echo.
pause
popd
exit /b 1

:update
rem Brings Claude's bundle into steam-workshop-ui. Errorlevel 1 only when the update was there but
rem could not be applied - then nothing is built, rather than an older version without saying so.
set "BUNDLE="
if exist "%ROOT%Claude outputs\steam-workshop-ui.bundle" set "BUNDLE=%ROOT%Claude outputs\steam-workshop-ui.bundle"
if not defined BUNDLE if exist "%ROOT%steam-workshop-ui.bundle" set "BUNDLE=%ROOT%steam-workshop-ui.bundle"
if not defined BUNDLE (
    call :say "      None here - building what this folder has."
    exit /b 0
)
where git >nul 2>nul
if errorlevel 1 (
    call :say "      git isn't installed, so it can't be applied - install Git for Windows (https://git-scm.com)."
    exit /b 1
)
call git rev-parse --git-dir >nul 2>nul
if errorlevel 1 (
    call :say "      This folder isn't a git repository, so it can't be applied."
    exit /b 1
)
set "CUR="
for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "CUR=%%b"
if /i not "%CUR%"=="steam-workshop-ui" (
    call :say "      This folder is on the branch %CUR%, not steam-workshop-ui - switch back to apply it."
    exit /b 1
)
call git fetch --quiet "%BUNDLE%" steam-workshop-ui >> "%LOG%" 2>&1
if errorlevel 1 (
    call :say "      The bundle could not be read - logs\build.log says why."
    exit /b 1
)
call git merge-base --is-ancestor FETCH_HEAD HEAD
if not errorlevel 1 (
    call :say "      Already in - nothing new."
    exit /b 0
)

rem Files changed here that are already exactly what the update has (line endings aside) - the
rem strings file every build writes, say - would block it for nothing: set back first, with a copy
rem kept in case the update can't be applied after all.
set "SETBACK=%TEMP%\SSPTMM-update-setback.txt"
if exist "%SETBACK%" del /q "%SETBACK%"
for /f "delims=" %%f in ('git diff-files --name-only 2^>nul') do (
    call git diff --quiet --ignore-cr-at-eol FETCH_HEAD -- "%%f"
    if not errorlevel 1 call :setback "%%f"
)

rem Files the update adds that are here already, untracked (a copy Claude put here by hand): set
rem aside as <name>.before-update, and dropped again below when they match.
set "ASIDE=%TEMP%\SSPTMM-update-aside.txt"
if exist "%ASIDE%" del /q "%ASIDE%"
for /f "delims=" %%f in ('git diff --name-only --no-renames --diff-filter=A HEAD FETCH_HEAD 2^>nul') do call :aside "%%f"

set "UNAME="
set "UMAIL="
for /f "delims=" %%n in ('git config user.name 2^>nul') do set "UNAME=%%n"
for /f "delims=" %%m in ('git config user.email 2^>nul') do set "UMAIL=%%m"
if not defined UNAME set "UNAME=trappuss"
if not defined UMAIL set "UMAIL=52510142+trappuss@users.noreply.github.com"

call git merge-base --is-ancestor HEAD FETCH_HEAD
if errorlevel 1 goto :update_merge
call git merge --ff-only --quiet FETCH_HEAD >> "%LOG%" 2>&1
if errorlevel 1 goto :update_failed
goto :update_done
:update_merge
rem Commits of your own here too: merged, so nothing of either side is rewritten.
call git -c "user.name=%UNAME%" -c "user.email=%UMAIL%" merge --no-edit --quiet -m "Merge Claude's update" FETCH_HEAD >> "%LOG%" 2>&1
if errorlevel 1 goto :update_failed
:update_done
if exist "%ASIDE%" for /f "usebackq delims=" %%p in ("%ASIDE%") do call :aside_done "%%p"
if exist "%ASIDE%" del /q "%ASIDE%"
if exist "%SETBACK%" for /f "usebackq delims=" %%p in ("%SETBACK%") do del /q "%%p.update-copy" >nul 2>nul
if exist "%SETBACK%" del /q "%SETBACK%"
for /f "delims=" %%c in ('git rev-parse --short HEAD') do call :say "      Applied - this folder is now at %%c."
exit /b 0

:update_failed
call git rev-parse --verify --quiet MERGE_HEAD >nul 2>nul
if not errorlevel 1 call git merge --abort >> "%LOG%" 2>&1
if exist "%ASIDE%" for /f "usebackq delims=" %%p in ("%ASIDE%") do if not exist "%%p" move /y "%%p.before-update" "%%p" >nul
if exist "%ASIDE%" del /q "%ASIDE%"
if exist "%SETBACK%" for /f "usebackq delims=" %%p in ("%SETBACK%") do move /y "%%p.update-copy" "%%p" >nul
if exist "%SETBACK%" del /q "%SETBACK%"
call :say "      It could not be applied: files changed here that the update also changes. The end of"
call :say "      logs\build.log names them. Nothing was changed and nothing was built - tell Claude."
exit /b 1

:setback
set "P=%~1"
set "P=%P:/=\%"
copy /y "%P%" "%P%.update-copy" >nul 2>nul
if not exist "%P%.update-copy" exit /b 0
>> "%SETBACK%" echo %P%
call git checkout -- "%~1" >> "%LOG%" 2>&1
exit /b 0

:aside
set "P=%~1"
set "P=%P:/=\%"
if not exist "%P%" exit /b 0
call git ls-files --error-unmatch -- "%P%" >nul 2>nul
if not errorlevel 1 exit /b 0
if exist "%P%.before-update" del /q "%P%.before-update"
move /y "%P%" "%P%.before-update" >nul 2>nul
rem Checked by the file, not the errorlevel: still there means it could not be moved.
if exist "%P%" exit /b 0
>> "%ASIDE%" echo %P%
exit /b 0

:aside_done
fc /b "%~1" "%~1.before-update" >nul 2>nul
if not errorlevel 1 (
    del /q "%~1.before-update"
    exit /b 0
)
call :say "      Your own copy of %~1 differs from the update's - kept as %~1.before-update."
exit /b 0

:say
rem The message is echoed through delayed expansion, so a path holding "&" or ")" - a folder
rem named "Tom & Jerry" - prints as text instead of running as a command. It is taken into MSG
rem before delayed expansion is on, so a "!" in it survives too.
set "MSG=%~1"
setlocal EnableDelayedExpansion
echo(!MSG!
>> "!LOG!" echo(!MSG!
endlocal
exit /b 0
