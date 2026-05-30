@echo off
REM 2026-05-29 run_tests.bat — double-click wrapper for run_tests.ps1
REM Bypasses PowerShell ExecutionPolicy and pauses on exit so the window stays open.
setlocal
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run_tests.ps1" %*
set EC=%ERRORLEVEL%
echo.
if "%EC%"=="0" (
    echo Tests passed. Press any key to close...
) else (
    echo Tests FAILED ^(exit code %EC%^). Press any key to close...
)
pause >nul
exit /b %EC%
