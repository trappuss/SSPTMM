@echo off
rem ---------------------------------------------------------------------------------------------
rem  SSPTMM - manual test checklist for a release, on a fresh SPT install.
rem  Asks about each step and writes a report to test-reports\. The steps are in
rem  ssptmm-test-checklist.ps1 beside this file.
rem ---------------------------------------------------------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ssptmm-test-checklist.ps1"
if errorlevel 1 echo The checklist stopped early - what was answered so far is in test-reports\.
echo.
pause
