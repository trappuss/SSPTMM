@echo off
setlocal EnableExtensions
rem ---------------------------------------------------------------------------------------------
rem  Steam Workshop UI fork - build, test and run, in one double-click.
rem
rem   1. Finds a .NET 9 SDK. If there isn't one, installs one into .dotnet\ beside this file
rem      (Microsoft's own dotnet-install script: no admin rights, nothing installed system-wide).
rem   2. Builds the app once (that build also writes the pseudo-locale file the tests read).
rem   3. Runs the test suite. A failure stops here and says so.
rem   4. Publishes a self-contained TCFModManager.exe into dist\steam-ui\.
rem   5. Starts it.
rem
rem  Everything it does is also written to steam-ui-build.log beside this file.
rem
rem  The build in dist\steam-ui\ keeps its own Data\ folder (settings, catalog cache) next to its
rem  exe, as the app always does - so it never touches the TCFModManager you already use. Point it
rem  at your SPT folder once in Options, as with a fresh install.
rem ---------------------------------------------------------------------------------------------

rem pushd rather than cd: it also works when this folder is on a network share (\\server\share),
rem where cd cannot go and the relative paths below would otherwise point at C:\Windows.
pushd "%~dp0" || exit /b 1
set "ROOT=%~dp0"
set "LOG=%ROOT%steam-ui-build.log"
set "OUT=%ROOT%dist\steam-ui"
set "LOCALSDK=%ROOT%.dotnet"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

> "%LOG%" echo Steam Workshop UI build - %DATE% %TIME%

call :say "[1/5] Looking for the .NET 9 SDK..."
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

call :say "[2/5] Building the app..."
"%DOTNET%" build "src\TCFModManager.App" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_build

call :say "[3/5] Running the tests..."
"%DOTNET%" test "Tests\TCFModManager.Core.Tests" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_tests
call :say "      All tests passed."

call :say "[4/5] Publishing TCFModManager.exe into dist\steam-ui\ ..."
"%DOTNET%" publish "src\TCFModManager.App" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUT%" --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_build
if not exist "%OUT%\TCFModManager.exe" goto :fail_build

call :say "[5/5] Starting it..."
start "" "%OUT%\TCFModManager.exe"
call :say ""
call :say "Done. The exe is dist\steam-ui\TCFModManager.exe"
call :say "Its own log files are in dist\steam-ui\Data\logs\"
echo.
pause
popd
exit /b 0

:fail_sdk
call :say ""
call :say "Could not install the .NET 9 SDK. The reason is at the end of steam-ui-build.log."
goto :end_fail

:fail_tests
call :say ""
call :say "The tests failed, so nothing was built. steam-ui-build.log names the failing test."
goto :end_fail

:fail_build
call :say ""
call :say "The build failed. The first line containing 'error' in steam-ui-build.log says why."
goto :end_fail

:end_fail
echo.
pause
popd
exit /b 1

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
