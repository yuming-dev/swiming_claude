@echo off
chcp 65001 >nul 2>&1
title 游泳赛事管理系统 - 安装程序
echo.
echo  ╔══════════════════════════════════════════════╗
echo  ║       游泳赛事管理与计时系统 安装程序        ║
echo  ║                                              ║
echo  ║  包含：                                      ║
echo  ║    1. 游泳赛事管理主服务器                   ║
echo  ║    2. 远程计时控制台（EXE客户端）            ║
echo  ║    3. Web控制台（HTML浏览器客户端）          ║
echo  ║                                              ║
echo  ║  系统要求：Windows 7 及以上                  ║
echo  ║            .NET Framework 4.0 及以上         ║
echo  ╚══════════════════════════════════════════════╝
echo.

:: 检查 .NET Framework
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4" /v Version >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 未检测到 .NET Framework 4.0，请先安装后再运行此安装程序。
    echo 下载地址：https://dotnet.microsoft.com/download/dotnet-framework/net40
    pause
    exit /b 1
)
echo [√] .NET Framework 4.0 已安装

:: 设置安装目录
set "DEFAULT_DIR=C:\SwimmingTimingSystem"
set /p INSTALL_DIR="请输入安装目录 [默认: %DEFAULT_DIR%]: "
if "%INSTALL_DIR%"=="" set "INSTALL_DIR=%DEFAULT_DIR%"

echo.
echo 安装目录: %INSTALL_DIR%
echo.

:: 创建目录
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if not exist "%INSTALL_DIR%\Server" mkdir "%INSTALL_DIR%\Server"
if not exist "%INSTALL_DIR%\Server\Web" mkdir "%INSTALL_DIR%\Server\Web"
if not exist "%INSTALL_DIR%\Server\Records" mkdir "%INSTALL_DIR%\Server\Records"
if not exist "%INSTALL_DIR%\Server\Database" mkdir "%INSTALL_DIR%\Server\Database"
if not exist "%INSTALL_DIR%\Server\Documents" mkdir "%INSTALL_DIR%\Server\Documents"
if not exist "%INSTALL_DIR%\RemoteControl" mkdir "%INSTALL_DIR%\RemoteControl"

echo [安装] 正在复制主服务器文件...
copy /Y "%~dp0SwimmingScoreboard\SwimmingScoreboard.exe" "%INSTALL_DIR%\Server\" >nul
copy /Y "%~dp0SwimmingScoreboard\Fleck.dll" "%INSTALL_DIR%\Server\" >nul
copy /Y "%~dp0SwimmingScoreboard\Newtonsoft.Json.dll" "%INSTALL_DIR%\Server\" >nul
xcopy /Y /Q "%~dp0SwimmingScoreboard\Web\*.*" "%INSTALL_DIR%\Server\Web\" >nul
xcopy /Y /Q "%~dp0SwimmingScoreboard\Records\*.*" "%INSTALL_DIR%\Server\Records\" >nul

echo [安装] 正在复制远程控制台文件...
copy /Y "%~dp0RemoteTimingControl\RemoteTimingControl.exe" "%INSTALL_DIR%\RemoteControl\" >nul
copy /Y "%~dp0RemoteTimingControl\Newtonsoft.Json.dll" "%INSTALL_DIR%\RemoteControl\" >nul

echo [安装] 正在创建快捷方式...

:: 创建桌面快捷方式 - 主服务器
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\游泳赛事管理主服务器.lnk'); $s.TargetPath = '%INSTALL_DIR%\Server\SwimmingScoreboard.exe'; $s.WorkingDirectory = '%INSTALL_DIR%\Server'; $s.Description = '游泳赛事管理与计时系统 - 主服务器'; $s.Save()"

:: 创建桌面快捷方式 - 远程控制台
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\远程计时控制台.lnk'); $s.TargetPath = '%INSTALL_DIR%\RemoteControl\RemoteTimingControl.exe'; $s.WorkingDirectory = '%INSTALL_DIR%\RemoteControl'; $s.Description = '游泳赛事管理与计时系统 - 远程控制台'; $s.Save()"

:: 创建开始菜单快捷方式
set "STARTMENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\游泳赛事管理系统"
if not exist "%STARTMENU%" mkdir "%STARTMENU%"
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%STARTMENU%\游泳赛事管理主服务器.lnk'); $s.TargetPath = '%INSTALL_DIR%\Server\SwimmingScoreboard.exe'; $s.WorkingDirectory = '%INSTALL_DIR%\Server'; $s.Save()"
powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%STARTMENU%\远程计时控制台.lnk'); $s.TargetPath = '%INSTALL_DIR%\RemoteControl\RemoteTimingControl.exe'; $s.WorkingDirectory = '%INSTALL_DIR%\RemoteControl'; $s.Save()"

:: 创建卸载脚本
(
echo @echo off
echo chcp 65001 ^>nul 2^>^&1
echo title 游泳赛事管理系统 - 卸载
echo echo.
echo echo 确定要卸载游泳赛事管理系统吗？
echo echo 安装目录: %INSTALL_DIR%
echo echo.
echo echo [注意] Database 目录中的赛事数据不会被删除。
echo echo.
echo set /p CONFIRM="输入 Y 确认卸载: "
echo if /i not "%%CONFIRM%%"=="Y" exit /b
echo echo.
echo echo [卸载] 正在删除快捷方式...
echo del /q "%%USERPROFILE%%\Desktop\游泳赛事管理主服务器.lnk" 2^>nul
echo del /q "%%USERPROFILE%%\Desktop\远程计时控制台.lnk" 2^>nul
echo rmdir /s /q "%STARTMENU%" 2^>nul
echo echo [卸载] 正在删除程序文件...
echo del /q "%INSTALL_DIR%\Server\SwimmingScoreboard.exe" 2^>nul
echo del /q "%INSTALL_DIR%\Server\Fleck.dll" 2^>nul
echo del /q "%INSTALL_DIR%\Server\Newtonsoft.Json.dll" 2^>nul
echo rmdir /s /q "%INSTALL_DIR%\Server\Web" 2^>nul
echo rmdir /s /q "%INSTALL_DIR%\Server\Records" 2^>nul
echo del /q "%INSTALL_DIR%\RemoteControl\RemoteTimingControl.exe" 2^>nul
echo del /q "%INSTALL_DIR%\RemoteControl\Newtonsoft.Json.dll" 2^>nul
echo echo.
echo echo [√] 卸载完成。赛事数据保留在 %INSTALL_DIR%\Server\Database\
echo pause
) > "%INSTALL_DIR%\卸载.bat"

:: 保存安装信息
echo %INSTALL_DIR% > "%INSTALL_DIR%\install_info.txt"
echo %date% %time% >> "%INSTALL_DIR%\install_info.txt"

echo.
echo  ╔══════════════════════════════════════════════╗
echo  ║            安装完成！                        ║
echo  ╚══════════════════════════════════════════════╝
echo.
echo  安装目录: %INSTALL_DIR%
echo.
echo  已创建桌面快捷方式:
echo    - 游泳赛事管理主服务器
echo    - 远程计时控制台
echo.
echo  使用说明:
echo    1. 启动"游泳赛事管理主服务器"作为赛事管理和计时服务
echo    2. 在其他电脑启动"远程计时控制台"连接到主服务器
echo    3. 或在浏览器打开 http://主服务器IP:3002/race_control.html
echo.
echo  Web控制台地址（主服务器启动后）:
echo    比赛控制: http://localhost:3002/race_control.html
echo    大屏显示: http://localhost:3002/display.html
echo    排名屏:   http://localhost:3002/leaderboard.html
echo    在线报名: http://localhost:3002/register.html
echo.
pause
