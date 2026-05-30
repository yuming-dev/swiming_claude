@echo off
REM 2026-05-29 build + run HwLapRemainingTest
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat" > nul
if errorlevel 1 (
    echo [ERROR] vcvars64.bat failed
    exit /b 1
)
cd /d "%~dp0"
cl /nologo /utf-8 /W3 /O2 /Fe:test_lap_remaining.exe test_lap_remaining.c > build.log 2>&1
if errorlevel 1 (
    echo [ERROR] cl build failed -- see build.log
    type build.log
    exit /b 1
)
echo [OK] build succeeded
echo.
test_lap_remaining.exe
endlocal
