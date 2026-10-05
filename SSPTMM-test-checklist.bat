@echo off
rem ---------------------------------------------------------------------------------------------
rem  SSPTMM - manual test checklist for a release, on a fresh SPT install.
rem  Asks about each step and writes a report to test-reports\. The steps are in
rem  tools\ssptmm-test-checklist.ps1.
rem ---------------------------------------------------------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\ssptmm-test-checklist.ps1"
if errorlevel 1 echo The checklist stopped early - what was answered so far is in test-reports\.
echo.
pause
