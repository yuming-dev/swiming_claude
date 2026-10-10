# build_installer.ps1
# 一次性重新编译并打包游泳赛事管理系统。
# 流程：
#   1. MSBuild 重建 SwimmingScoreboard.sln (Release)
#   2. csc.exe 编译 InstallerApp\{Setup,Uninstall,TimingSimulator}.cs
#   3. 把 5 个 WPF EXE 输出 + Web/Records + 工具 EXE 收集到 InstallerBuild\
# 运行：powershell -ExecutionPolicy Bypass -File .\build_installer.ps1
#
# 2026-10-09 加 -Lite 开关：只打运行程序本体(5个EXE+Web/Records+Tools)，跳过
#   VC++运行库(227MB)+Edge离线安装包(203MB)+四份用户手册(PDF/DOCX，约18MB)——
#   这430多MB是"装到没装过这些依赖的客户机"才需要的安全网，日常内部测试包
#   (开发机/测试机本来就有这些东西)带着它纯粹是净重。正式对外发布版本不要
#   加这个开关，带全套。用法：powershell ... .\build_installer.ps1 -Lite
param(
    [switch]$Lite
)
# 2026-09-21 用户明确要求"整理安装包, 去掉不要的部分"——排查发现两类不该进客户包的东西:
#   1. ParamDebugBot.exe(硬件参数调试机器人) 和 Web\test_bot.html(测试机器人网页)都是
#      开发/调试专用工具(使用说明书里查无这两样), 不进包。ParamDebugBot 需要的话在
#      InstallerApp\ParamDebugBot.cs 里, 开发机上单独用 csc 编译即可, 不用每次打包都带上。
#   2. RemoteTimingControl\bin\Release 下由于开发机反复跑过 RTC.exe, 累积了
#      Database\(含真实赛事库和原始计时数据)/Documents\(含真实运动员姓名成绩文档)/
#      Logs\(运行日志)/meet_service.json(开发机自己的服务器连接配置) 这些运行态残留——
#      跟 SwimmingScoreboard 那份"开发机残留数据"是同一个坑, 但清理逻辑只覆盖了
#      SwimmingScoreboard 一个项目, 没覆盖 RemoteTimingControl/ScheduleEditor/
#      RemoteDisplayControl, 于是这批真实赛事数据(甘肃省第十六届运动会青少年组游泳比赛)
#      一直在每个客户安装包里——[3/5]清理步骤和 $excludePats 都已经改成四个项目统一处理。

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

Write-Host "[2/5] 编译 Setup / Uninstall / TimingSimulator ..."
if (-not (Test-Path $installerBuild)) { New-Item -ItemType Directory -Path $installerBuild | Out-Null }
$winFormsRef = "/reference:System.Windows.Forms.dll,System.Drawing.dll"
$fullRef = "/reference:System.Windows.Forms.dll,System.Drawing.dll,System.dll"

# 2026-05-17 修：PowerShell 不会把 "/out:" 后跟 (Join-Path ...) 当成同一个 token，必须先把
# 输出路径/源文件路径求值到变量再拼，否则 csc 收到 "/out:" 后跟独立参数报 CS2005。
$outSetup = Join-Path $installerBuild "Setup.exe"
$srcSetup = Join-Path $root "InstallerApp\Setup.cs"
# 2026-10-08 显式嵌入 requireAdministrator 清单——原来靠 Windows 对"Setup.exe"这类
# 文件名的旧式"安装程序检测"启发式自动提权, 这条启发式在不少现代 Windows 配置下
# 其实是关着的, 导致装机时真正需要管理员权限的操作(netsh http add urlacl)静默失败
# (用户实拍到: 装完 netsh http show urlacl 查出来还是空的)。不再赌启发式。
$manifestSetup = Join-Path $root "InstallerApp\Setup.exe.manifest"
& $csc /target:winexe "/out:$outSetup" "/win32manifest:$manifestSetup" $winFormsRef $srcSetup
if ($LASTEXITCODE -ne 0) { throw "Setup.cs 编译失败" }

$outUninst = Join-Path $installerBuild "Uninstall.exe"
$srcUninst = Join-Path $root "InstallerApp\Uninstall.cs"
& $csc /target:winexe "/out:$outUninst" $winFormsRef $srcUninst
if ($LASTEXITCODE -ne 0) { throw "Uninstall.cs 编译失败" }

$outSim = Join-Path $installerBuild "TimingSimulator.exe"
$srcSim = Join-Path $root "InstallerApp\TimingSimulator.cs"
& $csc /target:winexe "/out:$outSim" $fullRef $srcSim
if ($LASTEXITCODE -ne 0) { throw "TimingSimulator.cs 编译失败" }

Write-Host "[3/5] 清理旧的 InstallerBuild 子目录 ..."
foreach ($sub in @("SwimmingScoreboard","RemoteTimingControl","RemoteDisplayControl","RegistrationTool","ScheduleEditor")) {
    $p = Join-Path $installerBuild $sub
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    New-Item -ItemType Directory -Path $p | Out-Null
}
# 2026-10-10 早期版本误把 CompetitionParamsTemplates 拷到 InstallerBuild 根目录(Setup.exe
#   根本不认这个根目录散放的东西, 见下面"[4/5]"那段说明)——老包留下的这个残留目录清一下,
#   免得每次打包清单都带着一份"装不到客户机"的死文件误导人。
$staleRootTpl = Join-Path $installerBuild "CompetitionParamsTemplates"
if (Test-Path $staleRootTpl) { Remove-Item -Recurse -Force $staleRootTpl; Write-Host "  ✂  删除根目录下的 CompetitionParamsTemplates 残留(应在各 exe 子目录内, 见下文)" }
# 2026-10-09 -Lite 模式下面的步骤会跳过 prereq\运行库/Edge/四份手册/速查卡的拷贝——
#   但如果 InstallerBuild\ 之前跑过一次【非 Lite】的完整打包, 这些文件/目录早就躺在
#   根目录下了, "跳过拷贝"不等于"没有", 不先删掉的话 -Lite 包体积根本不会变小。
if ($Lite) {
    foreach ($staleBig in @("prereq","使用说明书.pdf","使用说明书.docx",
        "Swimming_Meet_Management_System_User_Manual.pdf","Swimming_Meet_Management_System_User_Manual.docx",
        "现场速查卡.pdf")) {
        $sp3 = Join-Path $installerBuild $staleBig
        if (Test-Path $sp3) { Remove-Item -Recurse -Force $sp3; Write-Host "  ✂  删除 $staleBig (-Lite, 上次完整打包的残留)" }
    }
}
# 2026-09-21 ParamDebugBot.exe 不再编译进包(见上面的说明)，但旧版本打的包里这个文件已经
# 躺在 InstallerBuild 根目录——这一层只清子目录，清不到根目录的散文件，得单独删一次，
# 不然它会一直原地不动，看着像"还在用"。
$staleBot = Join-Path $installerBuild "ParamDebugBot.exe"
if (Test-Path $staleBot) { Remove-Item -Force $staleBot; Write-Host "  ✂  删除 ParamDebugBot.exe (调试工具, 不进客户包)" }

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
    'rdc_server.json',
    # 2026-09-21 "设置"页中文/English按钮——语言选择存在本机 language.json，
    # 跟 credentials.json 同一类"开发机自己的偏好，不该原样装到客户机"。
    'language.json',
    # 2026-09-21 开发机跑 RemoteTimingControl.exe 时会在 bin\Release 下生成自己那份
    # "连哪个服务器"配置——同上, 是开发机的连接记录, 不该原样装到客户机。
    'meet_service.json'
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
    # 2026-09-13 顺带清掉 Web 下的开发笔记(*.md), 那是给自己看的, 不该进客户包
    # 2026-09-21 顺带清掉 test_bot.html("测试机器人"网页)——开发/调试专用, 使用说明书
    #   里查无这个功能, 不该进客户包。
    Get-ChildItem $dstSub -Recurse -File -Include '*.bak_*','*.bak','*~','*.md','test_bot.html' | Remove-Item -Force
}
# 2026-06-17 RTC 也开 HTTP 文件服务 + WebSocket Server, 需要同样的 Web/ 目录
foreach ($sub in @("Web","Records")) {
    $dstSub = Join-Path $installerBuild "RemoteTimingControl\$sub"
    if (Test-Path $dstSub) { Remove-Item -Recurse -Force $dstSub }
    Copy-Item (Join-Path $root "SwimmingScoreboard\$sub") $dstSub -Recurse -Force
    # 2026-09-21 顺带清掉 test_bot.html("测试机器人"网页)——开发/调试专用, 使用说明书
    #   里查无这个功能, 不该进客户包。
    Get-ChildItem $dstSub -Recurse -File -Include '*.bak_*','*.bak','*~','*.md','test_bot.html' | Remove-Item -Force
}
# 2026-06-18 RDC 大屏预览 WebView2 用 file:/// 加载本地 display.html (主服务器 HTTP 8080 需 admin/netsh 注册, 不可靠)
$dstWebRdc = Join-Path $installerBuild "RemoteDisplayControl\Web"
if (Test-Path $dstWebRdc) { Remove-Item -Recurse -Force $dstWebRdc }
Copy-Item (Join-Path $root "SwimmingScoreboard\Web") $dstWebRdc -Recurse -Force
Get-ChildItem $dstWebRdc -Recurse -File -Include '*.bak_*','*.bak','*~','*.md','test_bot.html' | Remove-Item -Force

# 2026-05-21 删除开发机运行 EXE 时产生的整目录（exclude 模式只过滤文件，不过滤目录）：
#   Database\    开发机的赛事档案 + RawData 原始数据快照 → 装到客户机会覆盖客户数据
#   Documents\   开发机生成过的临时 PDF/DOC 文档
# 2026-09-13 Logs 一并清掉: 开发机跑出来的日志(20260829.log 这些)会被整个拷进客户包,
#   既是噪音也是泄露。程序自己会建目录, 不用预先放。
foreach ($strayDir in @("Database","Documents","Logs")) {
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
        # 2026-09-21 同上——这几个项目开发机上一样跑过 exe, 一样会攒出 Database\/Documents\/
        #   Logs\ 这类运行态残留(exclude 模式只挡文件, 挡不住整个目录)。之前这段清理只对
        #   SwimmingScoreboard 做, RemoteTimingControl 的开发机残留(含真实赛事库/原始计时
        #   数据/运动员成绩文档)一直在每个客户包里, 这里补齐四个项目统一处理。
        foreach ($strayDir in @("Database","Documents","Logs")) {
            $p2 = Join-Path $installerBuild "$proj\$strayDir"
            if (Test-Path $p2) {
                Remove-Item -Recurse -Force $p2
                Write-Host "  ✂  删除 $proj\$strayDir (开发机残留数据)"
            }
        }
    } else {
        Write-Warning "未找到 $src - 跳过"
    }
}

# 2026-09-28 同一个坑的新变种: 开发机上跑过 RemoteDisplayControl.exe 后, WebView2 会在
#   exe 同目录下建一份 "RemoteDisplayControl.exe.WebView2\" 用户数据(浏览器缓存/LevelDB/
#   证书等, 实测 300+ 个文件), 这是运行时自动生成的, 客户机首次启动会自己重建一份,
#   不该原样打进安装包(既是几十MB的噪音, 也是开发机本地浏览数据)。
$rdcWebView2Profile = Join-Path $installerBuild "RemoteDisplayControl\RemoteDisplayControl.exe.WebView2"
if (Test-Path $rdcWebView2Profile) {
    Remove-Item -Recurse -Force $rdcWebView2Profile
    Write-Host "  ✂  删除 RemoteDisplayControl.exe.WebView2 (开发机残留数据)"
}

# 2026-10-10 对应"赛事管理与报名→比赛参数设置管理"顶部"比赛规则"下拉的三个选项
#   (国际比赛FINA / 国内大赛中国泳协 / U系列青少年游泳比赛)，各自一套"比赛项目/组别/
#   性别/赛次/组数"五张表 × 中/英文 = 3×2×5 = 30 个 CSV，按规则分 3 个子目录。数据来源
#   (均为实查，非猜测)：
#     国际比赛(FINA)   — World Aquatics Swimming Rules 2023-2025 SW14(年龄组=公开组)
#     国内大赛(中国泳协) — 《2026年全国游泳锦标赛竞赛规程》原文 + 竞赛日程(不分年龄组，
#                         单项36+接力9=45项)
#     U系列            — 《2026年全国青少年游泳U系列比赛总决赛》参赛名单(按项目)176页
#                         逐项核实：5个年龄组(U9-U10至U17-U18)共用17个单项，仅U9-U10
#                         不设800/1500自由泳；无接力。(17×5-2)×2性别=166，与名单最后
#                         一个项目号完全吻合。
#   走各自的 CSV 导入入口(这几个入口不认 .xlsx, 只认 .csv)。体积很小(30个文件共约5KB),
#   -Lite 也照常带上, 不受那个开关影响。
#   2026-10-10 【坑】运行时是 AppDomain.CurrentDomain.BaseDirectory\CompetitionParamsTemplates\
#   逐个 exe 自己目录下去找——但 Setup.cs 的 DoInstall() 只认 CopyDirDeep(SwimmingScoreboard→
#   Server / RemoteTimingControl→RemoteControl / ScheduleEditor→ScheduleEditor) 这三个【点名】
#   的源目录(含子目录一起深拷), 从不知道 InstallerBuild 根目录下还有旁的东西。放在
#   InstallerBuild\CompetitionParamsTemplates\(根目录)的话, Setup.exe 永远不会把它搬到
#   C:\SwimmingTimingSystem\ 下任何一个 exe 的目录里——装出来的客户机这个功能会静默失效
#   (下拉切换没反应, 日志里是"模板目录不存在"）。必须放进那三个会被深拷的源目录各自里面,
#   让 CopyDirDeep 把它当成子目录一起带走。RemoteDisplayControl/RegistrationTool 没有
#   "比赛参数设置管理"这个 Tab, 不需要。
$paramTplSrc = Join-Path $root "Installer\CompetitionParamsTemplates"
if (Test-Path $paramTplSrc) {
    foreach ($proj in @("SwimmingScoreboard", "RemoteTimingControl", "ScheduleEditor")) {
        $projDir = Join-Path $installerBuild $proj
        if (Test-Path $projDir) {
            $paramTplDst = Join-Path $projDir "CompetitionParamsTemplates"
            if (Test-Path $paramTplDst) { Remove-Item -Recurse -Force $paramTplDst }
            Copy-Item $paramTplSrc $paramTplDst -Recurse -Force
        }
    }
    Write-Host "  ✓ 比赛参数模板(国际FINA/国内CSA/U系列 × 项目/组别/性别/赛次/组数 × 中/英文) 已收入 Server/RemoteControl/ScheduleEditor 三端"
} else {
    Write-Host "    [提示] 找不到 Installer\CompetitionParamsTemplates\, 安装包不含参数模板" -ForegroundColor Yellow
}

$rtsTxt = Join-Path $root "Installer\RemoteTimingControl\RemoteTimingServer.txt"
if (Test-Path $rtsTxt) {
    Copy-Item $rtsTxt (Join-Path $installerBuild "RemoteTimingControl\") -Force
}

if ($Lite) {
    Write-Host "[Lite] 跳过用户手册 + VC++运行库 + Edge离线安装包 (约430MB) —— 测试包不带"
} else {
$manualSrc = Join-Path $root "Installer\使用说明书.pdf"
if (Test-Path $manualSrc) { Copy-Item $manualSrc (Join-Path $installerBuild "使用说明书.pdf") -Force }
# 2026-09-28 中文版也一并收录 docx(此前只出 PDF; 用户要了中文 docx 之后补上, 跟英文版同一套来源)。
$manualDocxSrc = Join-Path $root "Installer\使用说明书.docx"
if (Test-Path $manualDocxSrc) { Copy-Item $manualDocxSrc (Join-Path $installerBuild "使用说明书.docx") -Force }
# 2026-09-14 现场速查卡(两页 A4) —— 说明书 40 页, 比赛当天没人翻得动。
#   源文件由 build_card.ps1 生成, 这里只负责收进包。
$cardSrc = Join-Path $root "Installer\现场速查卡.pdf"
if (Test-Path $cardSrc) { Copy-Item $cardSrc (Join-Path $installerBuild "现场速查卡.pdf") -Force }
else { Write-Host "    [提示] 找不到 Installer\现场速查卡.pdf, 安装包不含速查卡 (跑一下 build_card.ps1 再拷过去)" -ForegroundColor Yellow }
# 2026-09-28 英文版说明书(PDF+DOCX) —— 源文件由 scratch_manual\build_manual_docs.ps1
#   从 Swimming_Meet_Management_System_User_Manual.html 导出, 这里只负责收进包(同中文版模式)。
foreach ($ext in @("pdf","docx")) {
    $enManualSrc = Join-Path $root "Installer\Swimming_Meet_Management_System_User_Manual.$ext"
    if (Test-Path $enManualSrc) { Copy-Item $enManualSrc (Join-Path $installerBuild "Swimming_Meet_Management_System_User_Manual.$ext") -Force }
    else { Write-Host "    [提示] 找不到 Installer\Swimming_Meet_Management_System_User_Manual.$ext, 安装包不含英文说明书($ext)" -ForegroundColor Yellow }
}

# 2026-09-01 VC++ 运行库随包发。目标机缺它, SQLite.Interop.dll 就加载不了 ——
#   竞赛库全程打不开(成绩不入库/没有名次/组排名不生成), 而界面上看不出任何异常。
#   现场就这么跑了一整天才发现。安装器会静默装一次, 已装过的自动跳过。
# 2026-09-13 prereq\ 不入库(24MB 微软签名二进制, .gitignore 里排掉了), 所以【新克隆
#   出来的工作区里没有它】—— 警告必须把"去哪儿取、放哪儿、叫什么"一次说清楚,
#   不然下一个人只看到一行"找不到", 照样打出一个不含运行库的包。
$prereqSrc = Join-Path $root "prereq"
$vcExe = Join-Path $prereqSrc "vc_redist.x64.exe"
if (Test-Path $vcExe) {
    $prereqDst = Join-Path $installerBuild "prereq"
    New-Item -ItemType Directory -Force $prereqDst | Out-Null
    Copy-Item (Join-Path $prereqSrc "*") $prereqDst -Force
    $mb2 = ((Get-ChildItem $prereqDst | Measure-Object Length -Sum).Sum/1MB)
    # 顺带验一下签名 —— 这东西要装到客户机上, 来路不明的不能往包里放
    $sigOk = "(未校验)"
    try {
        $sg = Get-AuthenticodeSignature $vcExe
        $sigOk = if ($sg.Status -eq 'Valid' -and $sg.SignerCertificate.Subject -like '*Microsoft Corporation*') { "微软签名有效" }
                 else { "[警告] 签名状态 " + $sg.Status + " —— 这个文件来路可疑, 别往客户机上装" }
    } catch { }
    Write-Host ("    运行库已收入安装包: {0:N1} MB  {1}" -f $mb2, $sigOk)
    # 2026-09-29 同一批 prereq 里顺带看一眼 Edge 离线安装包在不在 ——
    #   PDF 导出(TryHtmlToPdf)靠 headless Edge/Chrome, 目标机没装的话导出直接失败。
    $edgeMsi = Join-Path $prereqSrc "MicrosoftEdgeEnterpriseX64.msi"
    if (Test-Path $edgeMsi) {
        $mbEdge = ((Get-Item $edgeMsi).Length/1MB)
        $edgeSigOk = "(未校验)"
        try {
            $sgE = Get-AuthenticodeSignature $edgeMsi
            $edgeSigOk = if ($sgE.Status -eq 'Valid' -and $sgE.SignerCertificate.Subject -like '*Microsoft Corporation*') { "微软签名有效" }
                         else { "[警告] 签名状态 " + $sgE.Status + " —— 这个文件来路可疑, 别往客户机上装" }
        } catch { }
        Write-Host ("    Edge 离线安装包已收入安装包: {0:N1} MB  {1}" -f $mbEdge, $edgeSigOk)
    } else {
        Write-Host "    [提示] prereq\ 下没有 MicrosoftEdgeEnterpriseX64.msi —— PDF导出(TryHtmlToPdf)在没装Edge/Chrome的机器上会失败。" -ForegroundColor Yellow
        Write-Host "           补法: https://edgeupdates.microsoft.com/api/products?view=enterprise 查 Stable/Windows/x64 的 msi Artifact 下载地址" -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "    ╔══════════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "    ║ [警告] 安装包不含 VC++ 运行库                                    ║" -ForegroundColor Yellow
    Write-Host "    ╚══════════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
    Write-Host "      后果: 目标机缺这个运行库时 SQLite.Interop.dll 加载不了, 竞赛库" -ForegroundColor Yellow
    Write-Host "            全程打不开(成绩不入库/没有名次/组排名不生成) —— 现场就这么" -ForegroundColor Yellow
    Write-Host "            跑过一整天才发现。" -ForegroundColor Yellow
    Write-Host "      补法: 下载 https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor Yellow
    Write-Host ("            存成 {0}" -f $vcExe) -ForegroundColor Yellow
    Write-Host "            (目录名 prereq、文件名 vc_redist.x64.exe 都不能改, 脚本认死的)" -ForegroundColor Yellow
    Write-Host "            然后重新跑一遍本脚本。" -ForegroundColor Yellow
    Write-Host ""
}
} # end if (-not $Lite)
# 2026-07-13 通讯协议.pdf 是开发者文档, 不进客户包 (原 2026-06-18 打包这行已移除). 客户包只放 使用说明书.pdf.

# ── 新版模拟计时器(图形界面, 10 道泳池) ───────────────────────────────
# 跟上面 InstallerApp\TimingSimulator.cs 编出来的老工具【不是同一个东西】:
#   TimingSimulator.exe      老的, 命令行/简易版, 名字不动免得说明书和快捷方式失效
#   TimingSimulatorGUI.exe   新的, 10 道泳池图 + 触板/出发台/盲表按钮 +
#                            TCP服务端/TCP客户端/UDP 三种连接方式
# 两者不能同名共存, 所以新的这个改名收进来。
Write-Host "[4.5/5] 编译并收录新版模拟计时器 (tools\TimingSimulator) ..."
$simProj = Join-Path $root "tools\TimingSimulator\TimingSimulator.csproj"
if (Test-Path $simProj) {
    & $msbuild $simProj -t:Rebuild -nologo -p:Configuration=Release -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "新版模拟计时器编译失败" }
    $simExe = Join-Path $root "tools\TimingSimulator\bin\Release\TimingSimulator.exe"
    if (-not (Test-Path $simExe)) { throw "编译完了却找不到 $simExe" }
    # 放进包里的 Tools\ 子目录 —— Setup 会把这个目录整个装到 <安装目录>\Tools\
    # 早期版本把这两个直接放在根目录, 换到 Tools\ 之后要把旧的清掉, 免得一个包里两份
    foreach ($stale in @("TimingSimulatorGUI.exe", "模拟计时器说明.md")) {
        $sp2 = Join-Path $installerBuild $stale
        if (Test-Path $sp2) { Remove-Item $sp2 -Force }
    }
    $toolsDir = Join-Path $installerBuild "Tools"
    if (-not (Test-Path $toolsDir)) { New-Item -ItemType Directory -Path $toolsDir | Out-Null }
    Copy-Item $simExe (Join-Path $toolsDir "TimingSimulatorGUI.exe") -Force
    $simDoc = Join-Path $root "tools\TimingSimulator\README.md"
    if (Test-Path $simDoc) { Copy-Item $simDoc (Join-Path $toolsDir "模拟计时器说明.md") -Force }
    Write-Host "  ✓ TimingSimulatorGUI.exe (新版, 图形界面)"
} else {
    Write-Host "  ! 找不到 $simProj, 跳过新版模拟计时器"
}

Write-Host ("[5/5] 打包完成" + $(if ($Lite) { " (-Lite, 不含运行库/Edge/手册)" } else { "" }) + "。InstallerBuild 目录清单：")
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
