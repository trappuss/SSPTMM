@echo off
setlocal EnableExtensions
rem ---------------------------------------------------------------------------------------------
rem  SSPTMM - run the tests, nothing else. One double-click; nothing is published or started, and
rem  nothing in your SPT folder is touched (every test works in a throwaway folder of its own).
rem
rem   1. Finds the .NET 9 SDK the same way SSPTMM-build-and-run.bat does. If there is none, run
rem      that file once first - it installs one.
rem   2. Builds the app, which writes a file some tests read.
rem   3. Runs the tests for the user\patchers fix and the check after install on their own, so their
rem      result is easy to find: install, update and removal of a mod with a server prepatch on an
rem      SPT 4.1 layout, and the size/contents check of every archive file once it is installed.
rem   4. Runs the whole test suite.
rem
rem  Everything is also written to logs\test.log. A failing test is named there.
rem ---------------------------------------------------------------------------------------------

rem pushd rather than cd: it also works from a network share - see SSPTMM-build-and-run.bat.
pushd "%~dp0" || exit /b 1
set "ROOT=%~dp0"
if not exist "%ROOT%logs" mkdir "%ROOT%logs"
set "LOG=%ROOT%logs\test.log"
set "LOCALSDK=%ROOT%.dotnet"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"

> "%LOG%" echo SSPTMM tests - %DATE% %TIME%

call :say "[1/4] Looking for the .NET 9 SDK..."
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
if not defined DOTNET goto :fail_sdk
call :say "      Found: %DOTNET%"

rem The app's build writes the pseudo-locale file some tests read, as in SSPTMM-build-and-run.bat.
call :say "[2/4] Building the app..."
"%DOTNET%" build "src\TCFModManager.App" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_build

call :say "[3/4] The user\patchers fix and the check after install..."
"%DOTNET%" test "Tests\TCFModManager.Core.Tests" -c Release --nologo --filter "FullyQualifiedName~ForkPrepatchInstallTests|FullyQualifiedName~ForkInstallVerificationTests|FullyQualifiedName~ForkSkippedUserFilesTests" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_focused
call :say "      Passed."

call :say "[4/4] The whole test suite..."
"%DOTNET%" test "Tests\TCFModManager.Core.Tests" -c Release --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail_all
call :say "      All tests passed."
call :say ""
call :say "Done. The full output is in logs\test.log."
goto :end

:fail_sdk
call :say ""
call :say "No .NET 9 SDK found. Run SSPTMM-build-and-run.bat once - it installs one - then run this again."
goto :end_error

:fail_build
call :say ""
call :say "The build failed. The first line containing 'error' in logs\test.log says why."
goto :end_error

:fail_focused
call :say ""
call :say "A test for the user\patchers fix or the check after install FAILED."
call :say "Search logs\test.log for the word Failed - the line names the test."
goto :end_error

:fail_all
call :say ""
call :say "The fix's own tests passed, but another test FAILED."
call :say "Search logs\test.log for the word Failed - the line names the test."
goto :end_error

:end_error
popd
pause
exit /b 1

:end
popd
pause
exit /b 0

:say
rem As in SSPTMM-build-and-run.bat: echoed through delayed expansion, so a path holding "&" or ")"
rem prints as text, and taken into MSG first so a "!" survives.
set "MSG=%~1"
setlocal EnableDelayedExpansion
echo(!MSG!
>> "!LOG!" echo(!MSG!
endlocal
exit /b 0
