# 源码不变量检查 —— 防止两类"改完看不出来"的错再犯
#   1. 打印表格的表头列数与单元格列数必须一致(加列时最容易漏改另一半, 表格会整体错位)
#   2. 反应时间不能在判罚/弃权(DSQ/DNS/DNF)时还显示
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$src  = Join-Path $root 'SwimmingScoreboard'
$pass = 0; $fail = 0; $msgs = @()
function Chk($n,$c,$d){ if($c){$script:pass++; Write-Host ("  [PASS] "+$n) -ForegroundColor Green}
  else {$script:fail++; Write-Host ("  [FAIL] "+$n+"  "+$d) -ForegroundColor Red; $script:msgs+=($n+" :: "+$d)} }

Write-Host "1. 打印表格 表头列数 vs 单元格列数"
$f = Join-Path $src 'EventResultPrintWindow.xaml.cs'
$t = [IO.File]::ReadAllText($f,[Text.Encoding]::UTF8)
$th = ([regex]::Matches($t,'<th')).Count
$td = ([regex]::Matches($t,'<td')).Count
Chk "项目成绩: 表头与单元格数量成比例(th=$th, td=$td)" ($th -gt 0 -and $td -gt 0 -and [Math]::Abs($th-$td) -le 4) "th=$th td=$td 差得太多, 多半是加列只改了一半"
# 2026-09-03 批量公布也改成了同一套 11 列表格, 同样盯住
$fb = Join-Path $src 'BatchByAgeGroupPrintWindow.xaml.cs'
$tb = [IO.File]::ReadAllText($fb,[Text.Encoding]::UTF8)
$thb = ([regex]::Matches($tb,'<th')).Count
$tdb = ([regex]::Matches($tb,'<td')).Count
Chk "批量公布: 表头与单元格数量成比例(th=$thb, td=$tdb)" ($thb -gt 0 -and $tdb -gt 0 -and [Math]::Abs($thb-$tdb) -le 4) "th=$thb td=$tdb 差得太多, 多半是加列只改了一半"
# 两张表的列序必须一样 —— 用户明确要求"批量公布改成项目成绩的表格格式"
$batchHdrOk = ($tb -match "(?s)名次</th>.{0,200}\{0\}</th>.{0,80}\{1\}</th>.{0,200}号码</th>.{0,120}组别</th>.{0,120}组数</th>.{0,120}道次</th>.{0,200}最终成绩</th>.{0,120}成绩差</th>")
Chk "批量公布的列序与项目成绩一致" $batchHdrOk "批量公布的表头顺序跟项目成绩对不上了"

Write-Host "2. 判罚/弃权不显示反应时间"
$bad = @()
foreach ($file in @('EventResultPrintWindow.xaml.cs','BatchByAgeGroupPrintWindow.xaml.cs','MainWindow.xaml.cs')) {
    $p = Join-Path $src $file
    if (-not (Test-Path $p)) { continue }
    $t2 = [IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)
    $lines = [IO.File]::ReadAllLines($p,[Text.Encoding]::UTF8)
    for ($i=0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'StartingBlockTime\.ToString\("F2"\)') {
            # 同一行或前两行里必须出现 DQ / remark / isDQ 的判断
            $ctx = ($lines[[Math]::Max(0,$i-4)..$i] -join ' ')
            # EventResultPrintWindow 是在【输出行】统一置空的(ReactionTime = isDQ ? "" : reactionPlain),
            # 算出来的中间值带不带守卫无所谓 —— 这种整体置空的写法也认。
            $wholeFileBlanks = ($t2 -match 'isDQ \? "" : reactionPlain')
            if (-not $wholeFileBlanks -and $ctx -notmatch 'isDQ|!dq|IsNullOrEmpty\(remark\)|dq \?|DSQ|返回 ""|isDQ \?') {
                $bad += ($file + ":" + ($i+1))
            }
        }
    }
}
Chk "反应时间的渲染处都带了判罚判断" ($bad.Count -eq 0) (($bad) -join '; ')

Write-Host "3. 比赛进行中不许往主服务器的库写"
$mwp = Join-Path $src 'MainWindow.xaml.cs'
$mw  = [IO.File]::ReadAllText($mwp,[Text.Encoding]::UTF8)
$ls  = [IO.File]::ReadAllLines($mwp,[Text.Encoding]::UTF8)
$un = @()
for ($i=0; $i -lt $ls.Count; $i++) {
    if ($ls[$i] -notmatch '_meetDb\.SaveConfig') { continue }
    # 前 6 行里必须有 InRaceNoDbWrite 守卫, 或者本身就在 FlushConfigToDb(确认后补写)里
    $ctx = ($ls[[Math]::Max(0,$i-14)..$i] -join ' ')
    if ($ctx -notmatch 'InRaceNoDbWrite|FlushConfigToDb|WriteConfigToDbNow|_cfgDbPending = false') { $un += ($i+1) }
}
Chk "SaveConfig 都带了""比赛中不写库""的守卫" ($un.Count -eq 0) ("未守卫的行: " + ($un -join ', '))
# 比赛中的成绩写入必须走本机当前组库, 不能过网
$bridge = [IO.File]::ReadAllText((Join-Path $src 'Db\MeetDbBridge.cs'),[Text.Encoding]::UTF8)
Chk "LiveSaveLanes 只写本机(比赛中不过网)" ($bridge -match '_local\.UpdateLane') "LiveSaveLanes 不再只写 _local, 比赛中会过网!"

Write-Host ""
Write-Host "4. 显示/打印路径不许自己算名次"
$badRank = @()
foreach ($fn in @('EventResultPrintWindow.xaml.cs','BatchByAgeGroupPrintWindow.xaml.cs','PromotionQueryWindow.xaml.cs')) {
    $fp = Join-Path $src $fn
    if (-not (Test-Path $fp)) { continue }
    $lns = [IO.File]::ReadAllLines($fp,[Text.Encoding]::UTF8)
    for ($i=0; $i -lt $lns.Count; $i++) {
        if ($lns[$i] -match 'ResultOrdering\.ComputeRanks' -and $lns[$i] -notmatch '^\s*//') { $badRank += ($fn + ":" + ($i+1)) }
    }
}
# MainWindow 只允许 RankHeatGroup 那一处(确认成绩时算, 结果要写进库)
$mwLns = [IO.File]::ReadAllLines((Join-Path $src 'MainWindow.xaml.cs'),[Text.Encoding]::UTF8)
$mwCalls = 0
for ($i=0; $i -lt $mwLns.Count; $i++) {
    if ($mwLns[$i] -match 'ResultOrdering\.ComputeRanks' -and $mwLns[$i] -notmatch '^\s*//') { $mwCalls++ }
}
Chk "打印/查询窗口不再自己算名次" ($badRank.Count -eq 0) (($badRank) -join ', ')
Chk "MainWindow 只剩确认时算名次那一处" ($mwCalls -le 1) ("还有 $mwCalls 处在算")

Write-Host ""
Write-Host "5. 比赛中的人工改动必须落【当前组库】"
# ══════════════════════════════════════════════════════════════════════
# 2026-09-01 现场吃过的亏:
#   判罚(MarkLaneStatus) 只改内存 + 写 JSON, 没写当前组库。而"确认成绩"回写
#   竞赛库时取的是【当前组库】的快照 —— 于是界面上标了 DNS、日志也打了
#   "取消破/平纪录标识", 库里那一行却还是: 有成绩、占名次、带着 MR。
#   判罚等于没打, 而且从界面上完全看不出来。
#   手输成绩(OverrideLaneTime)、撤销判罚(CancelLaneNote) 是同一个毛病。
#
# 规则: 这三个方法体内必须调 SaveHeatProgress()。
# ══════════════════════════════════════════════════════════════════════
$needSave = @('MarkLaneStatus','OverrideLaneTime','CancelLaneNote')
$missing = @()
foreach ($m in $needSave) {
    $start = -1
    for ($i=0; $i -lt $mwLns.Count; $i++) {
        if ($mwLns[$i] -match ('private void ' + $m + '\(')) { $start = $i; break }
    }
    if ($start -lt 0) { $missing += ($m + '(找不到这个方法)'); continue }
    # 方法体: 从定义行往下, 到下一个 "        private " 为止
    $end = $mwLns.Count - 1
    for ($j=$start+1; $j -lt $mwLns.Count; $j++) {
        if ($mwLns[$j] -match '^        private ') { $end = $j - 1; break }
    }
    $body = ($mwLns[$start..$end] -join "`n")
    if ($body -notmatch 'SaveHeatProgress\(\)') { $missing += $m }
}
Chk "判罚/手输成绩/撤销判罚 都写了当前组库" ($missing.Count -eq 0) (("没写的: " + ($missing -join ', ')))

# 入库边界: 判罚/弃权/试游不许带纪录标识进 heat_entries
$lms = [IO.File]::ReadAllText((Join-Path $src 'Db\LocalMeetService.cs'),[Text.Encoding]::UTF8)
Chk "入库时判罚/弃权/试游的纪录标识被清掉" ($lms -match 'IsUnranked\(ln\.Status\)\)\s*ln\.RecordNote\s*=\s*""') `
    "CommitHeatFrom 里那道'判罚不许带纪录标识'的防线没了"

# 增量刷新的指纹必须带名次 —— 不带就会漏掉"别的组确认导致本组名次变了"
$brg = [IO.File]::ReadAllText((Join-Path $src 'Db\MeetDbBridge.cs'),[Text.Encoding]::UTF8)
Chk "增量刷新指纹带了名次(rk_sum)" ($brg -match 'SUM\(COALESCE\(he\.rank,0\)\) AS rk_sum') `
    "ListHeatStamps 指纹里没有名次: 别的组确认后本组名次变了也不会重读"

# 判定"全部组已确认"必须问库的真身, 不能问联机计时端本机那份空副本
Chk "判定'全部组已确认'走 _meet(库的真身)" ($brg -match '_meet\.GetHeatList\(rid\)') `
    "GenerateEventRankingIfComplete 又只看 _local 了: 联机计时端上永远生成不了组排名表"

Write-Host ""
Write-Host "6. 判罚状态一律看【有效状态】(成绩行优先)"
# ══════════════════════════════════════════════════════════════════════
# 2026-09-01 计时端标 DNS 改两处: swimmer.Status 和 成绩行 res.Status。
#   回推给主服务器的只有成绩行那一份 —— 主服务器上 sw.Status 是空的。
#   于是所有"只看 sw.Status"的地方都把判罚的人当正常人:
#   大屏照显成绩、还挂着 MR、成绩单上占着名次(用户实拍到过)。
#   入口已经把两份对齐(HandleHeatConfirmedPush), 广播这一处也不许再只看 sw.Status。
# ══════════════════════════════════════════════════════════════════════
$bp = -1
for ($i=0; $i -lt $mwLns.Count; $i++) { if ($mwLns[$i] -match 'private List<object> BuildSwimmerPayload') { $bp = $i; break } }
if ($bp -lt 0) {
    Chk "找得到 BuildSwimmerPayload" $false "方法没了?"
} else {
    $bpEnd = [Math]::Min($mwLns.Count-1, $bp+180)
    $bpBody = ($mwLns[$bp..$bpEnd] -join "`n")
    Chk "大屏 payload 的判罚判定走 GetEffectiveStatus" `
        ($bpBody -notmatch 'sw\.Status == "DSQ"' -and $bpBody -match 'GetEffectiveStatus\(sw, result\)') `
        "BuildSwimmerPayload 又只看 sw.Status 了: 回推进来的判罚在大屏上会当正常人显示"
}
# 回推入口必须把成绩行的判罚镜像到运动员对象
$hp = -1
for ($i=0; $i -lt $mwLns.Count; $i++) { if ($mwLns[$i] -match 'private void HandleHeatConfirmedPush') { $hp = $i; break } }
$hpBody = if ($hp -ge 0) { ($mwLns[$hp..([Math]::Min($mwLns.Count-1,$hp+120))] -join "`n") } else { "" }
Chk "回推入口把判罚镜像到 sw.Status" ($hpBody -match 'sw\.Status = pushedStatus') `
    "HandleHeatConfirmedPush 不再镜像判罚: 主服务器上十几处看 sw.Status 的地方会把判罚当正常人"

# 打印本组成绩: 判罚不许印出名次
$hr = -1
for ($i=0; $i -lt $mwLns.Count; $i++) { if ($mwLns[$i] -match 'private string BuildHeatResultsHtml') { $hr = $i; break } }
$hrBody = if ($hr -ge 0) { ($mwLns[$hr..([Math]::Min($mwLns.Count-1,$hr+130))] -join "`n") } else { "" }
Chk "打印本组成绩: 判罚的名次印 '-'" ($hrBody -match 'IsNullOrEmpty\(remark\) \? "-"') `
    "rankCell 又会把判罚的人的名次印出来了"

Write-Host ""
Write-Host "7. TRI(试游) 必须写到【成绩行】上"
# ══════════════════════════════════════════════════════════════════════
# 2026-09-01 原来 res.Status 只在 DSQ/DNS/DNF 分支里赋值, 标 TRI 只改了
#   swimmer.Status。后果一条链子全断: 当前组库没有 TRI -> 竞赛库没有 TRI ->
#   照样占名次; 回推的 lr.Status 也是空的 -> 主服务器完全不知道有 TRI。
#   只有计时端本机内存还留着, 所以确认那一刻大屏是对的, 换个视图就没了。
# ══════════════════════════════════════════════════════════════════════
$mls = -1
for ($i=0; $i -lt $mwLns.Count; $i++) { if ($mwLns[$i] -match 'private void MarkLaneStatus') { $mls = $i; break } }
$mlsBody = if ($mls -ge 0) { ($mwLns[$mls..([Math]::Min($mwLns.Count-1,$mls+180))] -join "`n") } else { "" }
Chk "标 TRI 时写了成绩行的 Status" ($mlsBody -match 'resTri\.Status = "TRI"') `
    "MarkLaneStatus 又只改 swimmer.Status 了: TRI 进不了库, 也传不到主服务器"
Chk "标 TRI 时清掉名次" ($mlsBody -match 'resTri\.Rank = 0') "TRI 还会占名次"

# 本组名次的排除判定必须走有效状态
$rhg = -1
for ($i=0; $i -lt $mwLns.Count; $i++) { if ($mwLns[$i] -match 'private void RankHeatGroup') { $rhg = $i; break } }
$rhgBody = if ($rhg -ge 0) { ($mwLns[$rhg..([Math]::Min($mwLns.Count-1,$rhg+30))] -join "`n") } else { "" }
Chk "本组名次排除判罚/试游走有效状态" ($rhgBody -match 'GetEffectiveStatus\(s, r\)') `
    "RankHeatGroup 又只看 s.Status 了: 回推进来的判罚/试游会被排进名次"

Write-Host ""
Write-Host "8. 项目成绩表的列序"
# 2026-09-02 用户定的列序: 名次 → 姓名/代表队 → 号码 → 组别 → 组数 → 道次 → 最终成绩 …
#   (接力时姓名/代表队对调, 由 epH1/epH2 承担)
#   列【数量】对不代表【顺序】对: 只改表头没改单元格, 数量测试照样过, 但每一列都错位。
#   所以这里按出现先后核一遍。
$erp = [IO.File]::ReadAllText((Join-Path $src 'EventResultPrintWindow.xaml.cs'),[Text.Encoding]::UTF8)
$hdrOk = ($erp -match "(?s)名次</th>.{0,200}\{0\}</th>.{0,80}\{1\}</th>.{0,200}号码</th>.{0,120}组别</th>.{0,120}组数</th>.{0,120}道次</th>.{0,200}最终成绩</th>")
Chk "打印表头列序: 名次→姓名/代表队→号码→组别→组数→道次→最终成绩" $hdrOk "表头顺序被改动了"
# 单元格顺序必须跟表头一致: Rank → c1,c2 → BibNumber,AgeGroup,HeatText,Lane → FinalTime
$cellOk = ($erp -match "(?s)item\.Rank\);.{0,200}c1, c2\);.{0,200}item\.BibNumber, item\.AgeGroup, item\.HeatText, item\.Lane\);.{0,200}item\.FinalTime")
Chk "打印单元格顺序与表头一致" $cellOk "单元格顺序跟表头对不上, 每一列都会错位"

Write-Host ""
Write-Host "9. 组别/性别/项目/赛次/组数 不许写死"
# ══════════════════════════════════════════════════════════════════════
# 2026-09-03 用户定的规矩: 这五项一律跟"比赛参数设置管理"走。
#   写死的后果不是样子难看, 是【选不到】: 用户在设置里加了"A决赛"或改了组别名,
#   那些写死的窗口里根本没有这一项, 功能整个用不了。
# ══════════════════════════════════════════════════════════════════════
$hardStage = @()
$hardGender = @()
foreach ($x in @(@('SwimmingScoreboard','EventResultPrintWindow.xaml'),
                 @('SwimmingScoreboard','BatchByAgeGroupPrintWindow.xaml'),
                 @('SwimmingScoreboard','MainWindow.xaml'))) {
    $p = Join-Path $root (Join-Path $x[0] $x[1])
    if (-not (Test-Path $p)) { continue }
    $lines = [IO.File]::ReadAllLines($p,[Text.Encoding]::UTF8)
    for ($i=0; $i -lt $lines.Count; $i++) {
        # MainWindow 里那几个是"占位默认值", 运行时由 RefillGenderCombos/RefillStageCombos 覆盖,
        # 所以只盯两个弹窗 —— 它们没有任何重填入口
        if ($x[1] -eq 'MainWindow.xaml') { continue }
        if ($lines[$i] -match 'ComboBoxItem Content="(预赛|半决赛|决赛)"') { $hardStage += ($x[1]+":"+($i+1)) }
        if ($lines[$i] -match 'ComboBoxItem Content="(男|女|混合|男女)"' -and $lines[$i] -notmatch 'FillGenderCombo') { $hardGender += ($x[1]+":"+($i+1)) }
    }
}
Chk "弹窗的赛次下拉不写死(走 FillStageCombo)" ($hardStage.Count -eq 0) (($hardStage) -join ', ')
# 两个弹窗必须真的调了填充函数
foreach ($fn in @('EventResultPrintWindow.xaml.cs','BatchByAgeGroupPrintWindow.xaml.cs')) {
    $t3 = [IO.File]::ReadAllText((Join-Path $src $fn),[Text.Encoding]::UTF8)
    Chk ("$fn 调了 FillStageCombo") ($t3 -match 'MainWindow\.FillStageCombo\(') "赛次没按参数设置填"
    Chk ("$fn 调了 FillGenderCombo") ($t3 -match 'MainWindow\.FillGenderCombo\(') "性别没按参数设置填"
}
# 赛程编辑那张表的 性别/赛次 列不许写死
$mwText = [IO.File]::ReadAllText((Join-Path $src 'MainWindow.xaml.cs'),[Text.Encoding]::UTF8)
Chk "赛程编辑表的性别列取自 _genders" ($mwText -notmatch 'gc\.ItemsSource = new string\[\]') "性别列又写死了"
Chk "赛程编辑表的赛次列取自 _stages" ($mwText -notmatch 'sc\.ItemsSource = new string\[\]') "赛次列又写死了"
# 参数变更后必须同步静态表, 否则弹窗和别的 exe 拿不到新配置
Chk "参数变更后刷新 StageRegistry" ($mwText -match 'StageRegistry\.Set\(_stages\)') "NotifyMetadataChanged 没同步赛次表"
Chk "参数变更后推送配置给注册终端" ($mwText -match 'PushMetaListsToRegisterTerminals\(\)') "注册终端收不到新配置"
# 晋级查询的"来源赛次"不许写死
$pq = [IO.File]::ReadAllText((Join-Path $src 'PromotionQueryWindow.xaml.cs'),[Text.Encoding]::UTF8)
Chk "晋级查询的来源赛次取自赛次表" ($pq -match 'StageRegistry\.List') "又写死成 预赛/半决赛 了"

Write-Host ""
Write-Host ("结果: " + $pass + " 过 / " + $fail + " failed")
if ($fail -gt 0) { $msgs | ForEach-Object { Write-Host ("  "+$_) -ForegroundColor Red }; exit 1 }
exit 0
