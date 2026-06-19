# build_installer.ps1
# 一次性重新编译并打包游泳赛事管理系统。
# 流程：
#   1. MSBuild 重建 SwimmingScoreboard.sln (Release)
#   2. csc.exe 编译 InstallerApp\{Setup,Uninstall,TimingSimulator,ParamDebugBot}.cs
#   3. 把 5 个 WPF EXE 输出 + Web/Records + 工具 EXE 收集到 InstallerBuild\
# 运行：powershell -ExecutionPolicy Bypass -File .\build_installer.ps1

$ErrorActionPreference = "Stop"
$root = "C:\游泳2026\swiming_claude"
$installerBuild = Join-Path $root "InstallerBuild"

$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) { $msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" }
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

# 2026-05-17 修：PowerShell 不会把 "/out:" 后跟 (Join-Path ...) 当成同一个 token，必须先把
# 输出路径/源文件路径求值到变量再拼，否则 csc 收到 "/out:" 后跟独立参数报 CS2005。
$outSetup = Join-Path $installerBuild "Setup.exe"
$srcSetup = Join-Path $root "InstallerApp\Setup.cs"
& $csc /target:winexe "/out:$outSetup" $winFormsRef $srcSetup
if ($LASTEXITCODE -ne 0) { throw "Setup.cs 编译失败" }

$outUninst = Join-Path $installerBuild "Uninstall.exe"
$srcUninst = Join-Path $root "InstallerApp\Uninstall.cs"
& $csc /target:winexe "/out:$outUninst" $winFormsRef $srcUninst
if ($LASTEXITCODE -ne 0) { throw "Uninstall.cs 编译失败" }

$outSim = Join-Path $installerBuild "TimingSimulator.exe"
$srcSim = Join-Path $root "InstallerApp\TimingSimulator.cs"
& $csc /target:winexe "/out:$outSim" $fullRef $srcSim
if ($LASTEXITCODE -ne 0) { throw "TimingSimulator.cs 编译失败" }

$outBot = Join-Path $installerBuild "ParamDebugBot.exe"
$srcBot = Join-Path $root "InstallerApp\ParamDebugBot.cs"
& $csc /target:winexe "/out:$outBot" $fullRef $srcBot
if ($LASTEXITCODE -ne 0) { throw "ParamDebugBot.cs 编译失败" }

Write-Host "[3/5] 清理旧的 InstallerBuild 子目录 ..."
foreach ($sub in @("SwimmingScoreboard","RemoteTimingControl","RemoteDisplayControl","RegistrationTool","ScheduleEditor")) {
    $p = Join-Path $installerBuild $sub
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    New-Item -ItemType Directory -Path $p | Out-Null
}

Write-Host "[4/5] 拷贝 5 个 WPF EXE 输出 + Web/Records ..."

$excludePats = @(
    '*.pdb','*.xml','*.vshost.exe','*.vshost.exe.config','*.vshost.exe.manifest',
    # 2026-05-21 排除开发机运行 EXE 时产生的用户态 JSON（凭据/记住密码/配置）。
    # 这些是 .gitignore 里的 runtime artifacts，绝不能装到客户机，否则
    # 客户机管理员密码会变成开发机历史密码（而不是首次启动的 admin/admin）。
    '*_credentials.json','*_remember.json',
    'credentials.json','remember.json',          # 主服务器自身凭据 (无前缀)
    'timing_credentials.json','timing_remember.json',
    'editor_credentials.json','editor_remember.json','editor_settings.json','editor_sync.json',
    'register_credentials.json','register_remember.json',
    'display_credentials.json','display_remember.json',
    'RemoteTimingHw.json','remote_lane_close_settings.json',
    'timing_settings.json','timing_connection.json','device_states.json',
    'last_competition.txt','auth_credentials.json',
    'rdc_server.json'
)

# SwimmingScoreboard: 优先 x64\Release
$ssbBin = Join-Path $root "SwimmingScoreboard\bin\x64\Release"
if (-not (Test-Path $ssbBin)) { $ssbBin = Join-Path $root "SwimmingScoreboard\bin\Release" }
Copy-Item (Join-Path $ssbBin "*") (Join-Path $installerBuild "SwimmingScoreboard\") -Recurse -Force -Exclude $excludePats
# 2026-06-12 Web/Records: Release 输出里已带这两个目录, 若直接 Copy-Item 源\Web 目标\Web 会因目标已存在而
#   嵌套成 Web\Web. 故先删目标再从源拷一份干净的; 拷后清掉开发残留备份 (*.bak_* 等).
foreach ($sub in @("Web","Records")) {
    $dstSub = Join-Path $installerBuild "SwimmingScoreboard\$sub"
    if (Test-Path $dstSub) { Remove-Item -Recurse -Force $dstSub }
    Copy-Item (Join-Path $root "SwimmingScoreboard\$sub") $dstSub -Recurse -Force
    Get-ChildItem $dstSub -Recurse -File -Include '*.bak_*','*.bak','*~' | Remove-Item -Force
}
# 2026-06-17 RTC 也开 HTTP 文件服务 + WebSocket Server, 需要同样的 Web/ 目录
foreach ($sub in @("Web","Records")) {
    $dstSub = Join-Path $installerBuild "RemoteTimingControl\$sub"
    if (Test-Path $dstSub) { Remove-Item -Recurse -Force $dstSub }
    Copy-Item (Join-Path $root "SwimmingScoreboard\$sub") $dstSub -Recurse -Force
    Get-ChildItem $dstSub -Recurse -File -Include '*.bak_*','*.bak','*~' | Remove-Item -Force
}
# 2026-06-18 RDC 大屏预览 WebView2 用 file:/// 加载本地 display.html (主服务器 HTTP 8080 需 admin/netsh 注册, 不可靠)
$dstWebRdc = Join-Path $installerBuild "RemoteDisplayControl\Web"
if (Test-Path $dstWebRdc) { Remove-Item -Recurse -Force $dstWebRdc }
Copy-Item (Join-Path $root "SwimmingScoreboard\Web") $dstWebRdc -Recurse -Force
Get-ChildItem $dstWebRdc -Recurse -File -Include '*.bak_*','*.bak','*~' | Remove-Item -Force

# 2026-05-21 删除开发机运行 EXE 时产生的整目录（exclude 模式只过滤文件，不过滤目录）：
#   Database\    开发机的赛事档案 + RawData 原始数据快照 → 装到客户机会覆盖客户数据
#   Documents\   开发机生成过的临时 PDF/DOC 文档
foreach ($strayDir in @("Database","Documents")) {
    $p = Join-Path $installerBuild "SwimmingScoreboard\$strayDir"
    if (Test-Path $p) {
        Remove-Item -Recurse -Force $p
        Write-Host "  ✂  删除 SwimmingScoreboard\$strayDir (开发机残留数据)"
    }
}

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
# 2026-06-18 通讯协议 PDF 一起打包
$protocolSrc = Join-Path $root "Installer\通讯协议.pdf"
if (Test-Path $protocolSrc) { Copy-Item $protocolSrc (Join-Path $installerBuild "通讯协议.pdf") -Force }

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
