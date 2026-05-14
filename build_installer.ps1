# build_installer.ps1
# 一次性重新编译并打包游泳赛事管理系统。
# 流程：
#   1. MSBuild 重建 SwimmingScoreboard.sln (Release)
#   2. csc.exe 编译 InstallerApp\{Setup,Uninstall,TimingSimulator,ParamDebugBot}.cs
#   3. 把 5 个 WPF EXE 输出 + Web/Records + 工具 EXE 收集到 InstallerBuild\
# 运行：powershell -ExecutionPolicy Bypass -File .\build_installer.ps1

$ErrorActionPreference = "Stop"
$root = "C:\代码\swiming_claude"
$installerBuild = Join-Path $root "InstallerBuild"

$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $msbuild)) { throw "未找到 MSBuild: $msbuild" }
if (-not (Test-Path $csc)) { throw "未找到 csc.exe: $csc" }

Write-Host "[1/5] MSBuild 重建 Release ..."
& $msbuild (Join-Path $root "SwimmingScoreboard.sln") -t:Rebuild -nologo -m -p:Configuration=Release -v:minimal
if ($LASTEXITCODE -ne 0) { throw "MSBuild 失败" }

Write-Host "[2/5] 编译 Setup / Uninstall / TimingSimulator / ParamDebugBot ..."
if (-not (Test-Path $installerBuild)) { New-Item -ItemType Directory -Path $installerBuild | Out-Null }
$winFormsRef = "/reference:System.Windows.Forms.dll,System.Drawing.dll"
$fullRef = "/reference:System.Windows.Forms.dll,System.Drawing.dll,System.dll"

& $csc /target:winexe /out:(Join-Path $installerBuild "Setup.exe") $winFormsRef (Join-Path $root "InstallerApp\Setup.cs")
if ($LASTEXITCODE -ne 0) { throw "Setup.cs 编译失败" }
& $csc /target:winexe /out:(Join-Path $installerBuild "Uninstall.exe") $winFormsRef (Join-Path $root "InstallerApp\Uninstall.cs")
if ($LASTEXITCODE -ne 0) { throw "Uninstall.cs 编译失败" }
& $csc /target:winexe /out:(Join-Path $installerBuild "TimingSimulator.exe") $fullRef (Join-Path $root "InstallerApp\TimingSimulator.cs")
if ($LASTEXITCODE -ne 0) { throw "TimingSimulator.cs 编译失败" }
& $csc /target:winexe /out:(Join-Path $installerBuild "ParamDebugBot.exe") $fullRef (Join-Path $root "InstallerApp\ParamDebugBot.cs")
if ($LASTEXITCODE -ne 0) { throw "ParamDebugBot.cs 编译失败" }

Write-Host "[3/5] 清理旧的 InstallerBuild 子目录 ..."
foreach ($sub in @("SwimmingScoreboard","RemoteTimingControl","RemoteDisplayControl","RegistrationTool","ScheduleEditor")) {
    $p = Join-Path $installerBuild $sub
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    New-Item -ItemType Directory -Path $p | Out-Null
}

Write-Host "[4/5] 拷贝 5 个 WPF EXE 输出 + Web/Records ..."

$excludePats = @('*.pdb','*.xml','*.vshost.exe','*.vshost.exe.config','*.vshost.exe.manifest')

# SwimmingScoreboard: 优先 x64\Release
$ssbBin = Join-Path $root "SwimmingScoreboard\bin\x64\Release"
if (-not (Test-Path $ssbBin)) { $ssbBin = Join-Path $root "SwimmingScoreboard\bin\Release" }
Copy-Item (Join-Path $ssbBin "*") (Join-Path $installerBuild "SwimmingScoreboard\") -Recurse -Force -Exclude $excludePats
Copy-Item (Join-Path $root "SwimmingScoreboard\Web") (Join-Path $installerBuild "SwimmingScoreboard\Web") -Recurse -Force
Copy-Item (Join-Path $root "SwimmingScoreboard\Records") (Join-Path $installerBuild "SwimmingScoreboard\Records") -Recurse -Force

foreach ($proj in @("RemoteTimingControl","RemoteDisplayControl","RegistrationTool","ScheduleEditor")) {
    $src = Join-Path $root "$proj\bin\Release"
    if (Test-Path $src) {
        Copy-Item (Join-Path $src "*") (Join-Path $installerBuild $proj) -Recurse -Force -Exclude $excludePats
    } else {
        Write-Warning "未找到 $src - 跳过"
    }
}

$rtsTxt = Join-Path $root "Installer\RemoteTimingControl\RemoteTimingServer.txt"
if (Test-Path $rtsTxt) {
    Copy-Item $rtsTxt (Join-Path $installerBuild "RemoteTimingControl\") -Force
}

$manualSrc = Join-Path $root "Installer\使用说明书.pdf"
if (Test-Path $manualSrc) { Copy-Item $manualSrc (Join-Path $installerBuild "使用说明书.pdf") -Force }

Write-Host "[5/5] 打包完成。InstallerBuild 目录清单："
Get-ChildItem $installerBuild | ForEach-Object {
    if ($_.PSIsContainer) {
        $count = (Get-ChildItem $_.FullName -Recurse -File).Count
        Write-Host ("  [DIR ] {0,-28} {1} 个文件" -f $_.Name, $count)
    } else {
        $size = "{0:N0}" -f $_.Length
        Write-Host ("  [FILE] {0,-28} {1} 字节" -f $_.Name, $size)
    }
}

Write-Host ""
Write-Host "安装包已就绪：$installerBuild\Setup.exe"
Write-Host "运行 Setup.exe 即可安装到目标机器。"
