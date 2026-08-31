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
    if ($ctx -notmatch 'InRaceNoDbWrite|FlushConfigToDb|_cfgDbPending = false') { $un += ($i+1) }
}
Chk "SaveConfig 都带了""比赛中不写库""的守卫" ($un.Count -eq 0) ("未守卫的行: " + ($un -join ', '))
# 比赛中的成绩写入必须走本机当前组库, 不能过网
$bridge = [IO.File]::ReadAllText((Join-Path $src 'Db\MeetDbBridge.cs'),[Text.Encoding]::UTF8)
Chk "LiveSaveLanes 只写本机(比赛中不过网)" ($bridge -match '_local\.UpdateLane') "LiveSaveLanes 不再只写 _local, 比赛中会过网!"

Write-Host ""
Write-Host ("结果: " + $pass + " 过 / " + $fail + " failed")
if ($fail -gt 0) { $msgs | ForEach-Object { Write-Host ("  "+$_) -ForegroundColor Red }; exit 1 }
exit 0
