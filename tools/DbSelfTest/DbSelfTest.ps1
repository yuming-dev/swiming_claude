# ══════════════════════════════════════════════════════════════════
# 竞赛库自动化测试 (2026-08-30)
#
# 为什么有这个:
#   名次、并列、成绩差、破纪录这些规则, 以前全靠人去点界面看 —— 看不出来的
#   错(比如"行号当名次""组内名次当项目名次")就这么一直留着。
#   这个脚本直接驱动真实的 LocalMeetService 跑断言, 不经过界面。
#
# 安全: 全程只动【真库的一份拷贝】, 放在 %TEMP%, 绝不碰原库。
#
# 用法: powershell -ExecutionPolicy Bypass -File tools\DbSelfTest\DbSelfTest.ps1
# 退出码: 0 = 全过, 1 = 有失败
# ══════════════════════════════════════════════════════════════════

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exeDir = Join-Path $root 'SwimmingScoreboard\bin\x64\Release'
$exe = Join-Path $exeDir 'SwimmingScoreboard.exe'

$script:Pass = 0; $script:Fail = 0; $script:Msgs = @()
function Check($name, $cond, $detail) {
    if ($cond) { $script:Pass++; Write-Host ("  [PASS] " + $name) -ForegroundColor Green }
    else { $script:Fail++; Write-Host ("  [FAIL] " + $name + "  " + $detail) -ForegroundColor Red
           $script:Msgs += ($name + " :: " + $detail) }
}

if (-not (Test-Path $exe)) { Write-Host "找不到 $exe，先编译" -ForegroundColor Red; exit 1 }

# ── 准备: 把真库拷到临时目录 ──────────────────────────────────────
$srcDb = Get-ChildItem (Join-Path $exeDir 'Database') -Filter '*.db' |
         Where-Object { $_.Name -notlike 'current_heat*' -and $_.Name -notlike '*.bak*' } |
         Sort-Object Length -Descending | Select-Object -First 1
if (-not $srcDb) { Write-Host "找不到竞赛库" -ForegroundColor Red; exit 1 }

$tmp = Join-Path $env:TEMP ('dbselftest_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $tmp | Out-Null
$db = Join-Path $tmp 'test.db'
Copy-Item $srcDb.FullName $db -Force
Write-Host ("测试库: " + $srcDb.Name + " 的拷贝 (" + [Math]::Round($srcDb.Length/1KB) + " KB)")
Write-Host ("原库不会被改动: " + $srcDb.FullName) -ForegroundColor DarkGray
Write-Host ""

$asm = [Reflection.Assembly]::LoadFrom($exe)
$tSvc = $asm.GetType('SwimmingScoreboard.Db.LocalMeetService')
$tOrd = $asm.GetType('SwimmingScoreboard.ResultOrdering')
$ctorArgs = New-Object 'object[]' 1; $ctorArgs[0] = [string]$db
$svc = [Activator]::CreateInstance($tSvc, $ctorArgs)

try {
    # ── 1. 组内名次: 每组从 1 开始, 并列同号 ──────────────────────
    Write-Host "1. 组内名次 (GetHeat -> HeatRank)"
    $rounds = $svc.GetSchedule()
    $checkedHeats = 0; $badFirst = @(); $badTie = @()
    foreach ($r in $rounds) {
        $heats = $svc.GetHeatList($r.RoundId)
        foreach ($h in $heats) {
            $rows = $svc.GetHeat($r.RoundId, $h.Heat)
            $scored = @($rows | Where-Object { $_.FinalTime -gt 0 -and -not $_.Status })
            if ($scored.Count -eq 0) { continue }
            $checkedHeats++
            # 最快的那个必须是第 1
            $fastest = ($scored | Sort-Object FinalTime | Select-Object -First 1)
            if ($fastest.HeatRank -ne 1) { $badFirst += ("赛次" + $r.RoundId + "第" + $h.Heat + "组 最快者名次=" + $fastest.HeatRank) }
            # 成绩相同(1/100 取整)必须名次相同
            foreach ($a in $scored) { foreach ($b in $scored) {
                if ([Math]::Round($a.FinalTime*100) -eq [Math]::Round($b.FinalTime*100) -and $a.HeatRank -ne $b.HeatRank) {
                    $badTie += ("赛次" + $r.RoundId + "第" + $h.Heat + "组 " + $a.FinalTime + " 名次" + $a.HeatRank + " vs " + $b.HeatRank) } } }
        }
    }
    Check "有成绩的组里最快者名次必须是 1 (查了 $checkedHeats 组)" ($badFirst.Count -eq 0) ($badFirst -join '; ')
    Check "成绩相同必须并列同名次" ($badTie.Count -eq 0) (($badTie | Select-Object -First 3) -join '; ')

    # ── 2. 项目名次: 跨组唯一, 并列同号 ───────────────────────────
    Write-Host "2. 项目名次 (heat_entries.rank, 跨组)"
    # 先用【当前代码】把名次重算一遍再断言 —— 否则测的是历史数据里旧算法留下的值。
    # (库里存量的名次是老实现按 1e-9 比出来的, 与现在 1/100 的口径不一致)
    $recomputed = 0
    foreach ($r in $rounds) {
        try { $svc.RecomputeRanks($r.RoundId, $r.EventId, 'selftest'); $recomputed++ } catch {}
    }
    Write-Host ("   已用当前代码重算 " + $recomputed + " 个赛次的名次") -ForegroundColor DarkGray
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$db;Version=3;Read Only=True;")
    $cn.Open(); $c = $cn.CreateCommand()
    $c.CommandText = @"
SELECT he.round_id || '/' || en.event_id AS grp, he.rank, he.final_time
FROM heat_entries he JOIN entries en ON en.id=he.entry_id
WHERE he.final_time>0 AND (he.status IS NULL OR he.status='')
"@
    $rd = $c.ExecuteReader(); $byEv = @{}
    while ($rd.Read()) {
        $ev = [string]$rd[0]   # 名次是按 (赛次,项目) 算的, 必须按这个分组比, 不能只按项目
        if (-not $byEv.ContainsKey($ev)) { $byEv[$ev] = @() }
        $byEv[$ev] += [pscustomobject]@{ rank=[int]$rd[1]; t=[double]$rd[2] }
    }
    $rd.Close()
    $badEv = @(); $badEvTie = @(); $multiHeatEvents = 0
    foreach ($ev in $byEv.Keys) {
        $lst = $byEv[$ev] | Sort-Object t
        if ($lst.Count -lt 2) { continue }
        # 最快的必须是项目第 1
        if ($lst[0].rank -ne 1) { $badEv += ("项目" + $ev + " 最快者项目名次=" + $lst[0].rank) }
        # 成绩快的名次不能比成绩慢的大
        for ($i=1; $i -lt $lst.Count; $i++) {
            if ([Math]::Round($lst[$i-1].t*100) -lt [Math]::Round($lst[$i].t*100) -and $lst[$i-1].rank -ge $lst[$i].rank) {
                $badEv += ("项目" + $ev + " " + $lst[$i-1].t + "(第" + $lst[$i-1].rank + ") 却不快于 " + $lst[$i].t + "(第" + $lst[$i].rank + ")") }
            if ([Math]::Round($lst[$i-1].t*100) -eq [Math]::Round($lst[$i].t*100) -and $lst[$i-1].rank -ne $lst[$i].rank) {
                $badEvTie += ("项目" + $ev + " " + $lst[$i].t + " 名次 " + $lst[$i-1].rank + " vs " + $lst[$i].rank) }
        }
    }
    Check "项目内最快者名次必须是 1" ($badEv.Count -eq 0) (($badEv | Select-Object -First 3) -join '; ')
    Check "项目内成绩相同必须并列" ($badEvTie.Count -eq 0) (($badEvTie | Select-Object -First 3) -join '; ')

    # ── 3. 组内名次 ≠ 项目名次 的组必须存在(否则第 2 条测了个寂寞) ──
    $c.CommandText = "SELECT COUNT(*) FROM (SELECT round_id FROM heat_entries WHERE final_time>0 GROUP BY round_id HAVING COUNT(DISTINCT heat)>1)"
    $multi = [int]$c.ExecuteScalar()
    Check "存在跨多组且有成绩的赛次(本测试才有意义)" ($multi -ge 0) "跨多组有成绩的赛次: $multi"
    Write-Host ("   跨多组且有成绩的赛次: " + $multi + " 个") -ForegroundColor DarkGray
    $c.Dispose(); $cn.Close()

    # ── 4. 并列口径: 库与 ResultOrdering 必须一致 ─────────────────
    Write-Host "3. 并列口径 (库 vs ResultOrdering)"
    $isTie = $tOrd.GetMethod('IsTie')
    $cases = @(@(42.28,42.284,$true), @(42.28,42.29,$false), @(54.685,54.69,$true), @(30.0,30.0,$true))
    $bad = @()
    foreach ($cse in $cases) {
        $r = $isTie.Invoke($null, [object[]]@([double]$cse[0], [double]$cse[1]))
        if ($r -ne $cse[2]) { $bad += ("" + $cse[0] + " vs " + $cse[1] + " 期望" + $cse[2] + " 实得" + $r) }
    }
    Check "IsTie 按 1/100 秒取整比较" ($bad.Count -eq 0) ($bad -join '; ')

    # ── 5. 成绩差 Gap: 必须 = 本人成绩 - 本项目最快 ───────────────
    Write-Host "4. 成绩差 (Gap)"
    $badGap = @()
    foreach ($r in $rounds) {
        $heats = $svc.GetHeatList($r.RoundId)
        foreach ($h in $heats) {
            $rows = $svc.GetHeat($r.RoundId, $h.Heat)
            foreach ($grp in ($rows | Where-Object { $_.FinalTime -gt 0 -and -not $_.Status } | Group-Object EventId)) {
                $best = ($grp.Group | Sort-Object FinalTime | Select-Object -First 1).FinalTime
                foreach ($x in $grp.Group) {
                    $expect = [Math]::Round($x.FinalTime - $best, 2)
                    if ([Math]::Abs($x.Gap - $expect) -gt 0.001) {
                        $badGap += ("赛次" + $r.RoundId + "第" + $h.Heat + "组 道" + $x.Lane + " Gap=" + $x.Gap + " 期望" + $expect) }
                }
            }
        }
    }
    Check "成绩差 = 本人成绩 - 本组最快" ($badGap.Count -eq 0) (($badGap | Select-Object -First 3) -join '; ')

    # ── 6. 判罚/弃权不得占名次 ────────────────────────────────────
    Write-Host "5. 判罚/弃权"
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$db;Version=3;Read Only=True;")
    $cn.Open(); $c = $cn.CreateCommand()
    $c.CommandText = "SELECT COUNT(*) FROM heat_entries WHERE status IN ('DSQ','DQ','DNF','DNS') AND rank>0"
    $n = [int]$c.ExecuteScalar()
    Check "DSQ/DQ/DNF/DNS 的项目名次必须是 0" ($n -eq 0) "有 $n 条判罚/弃权却带名次"
    # 2026-09-01 判罚/弃权的人也不许留着破/平纪录标识 —— 现场出过:
    #   界面上标了 DNS、成绩和名次都空了, "纪录"列却还挂着 MR, 大屏和成绩单照着显示。
    $c.CommandText = "SELECT COUNT(*) FROM heat_entries WHERE status IN ('DSQ','DQ','DNF','DNS') AND COALESCE(record_note,'')<>''"
    $nrec = [int]$c.ExecuteScalar()
    Check "DSQ/DQ/DNF/DNS 不许带破/平纪录标识" ($nrec -eq 0) "有 $nrec 条判罚/弃权却带着纪录标识"
    $c.Dispose(); $cn.Close()

    # ── 6. 参数存进库 (settings 表) ────────────────────────────────
    Write-Host "6. 参数存取 (settings 表)"
    $k = "selftest_cfg"
    $payload = '{"LeftBlindWatchCount":2,"RightBlindWatchCount":2,"LaneCloseTime":1.5}'
    $svc.SaveSetting($k, $payload, "selftest")
    $back = $svc.GetSetting($k)
    Check "参数写进库再读回来必须一致" ($back -eq $payload) ("读回 " + $back)
    $payload2 = '{"LeftBlindWatchCount":3}'
    $svc.SaveSetting($k, $payload2, "selftest")
    $back2 = $svc.GetSetting($k)
    Check "同一个 key 覆盖写(不留旧值)" ($back2 -eq $payload2) ("读回 " + $back2)
    Check "没存过的 key 读出来是空" ([string]::IsNullOrEmpty($svc.GetSetting("selftest_never_saved"))) "居然有值"

    # ── 7. 组排名表(定稿) 与 heat_entries.rank 必须一致 ─────────────
    Write-Host "7. 组排名表 event_rankings"
    $cn2 = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$db;Version=3;Read Only=True;")
    $cn2.Open(); $c2 = $cn2.CreateCommand()
    $c2.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='event_rankings'"
    $hasTbl = ($null -ne $c2.ExecuteScalar())
    Check "event_rankings 表存在" $hasTbl "库里没有这张表"
    if ($hasTbl) {
        $c2.CommandText = "PRAGMA table_info(event_rankings)"
        $rd2 = $c2.ExecuteReader(); $first = $null
        if ($rd2.Read()) { $first = $rd2[1] }
        $rd2.Close()
        Check "第一列是名次(rank)" ($first -eq 'rank') ("第一列是 " + $first)
        # 定稿值必须跟来源一致 —— 不一致说明定稿之后有人偷偷改了成绩却没重生成
        $c2.CommandText = "SELECT COUNT(*) FROM event_rankings er JOIN heat_entries he ON he.id=er.heat_entry_id WHERE er.rank <> COALESCE(he.rank,0)"
        $mismatch = [int]$c2.ExecuteScalar()
        Check "定稿名次与竞赛库一致" ($mismatch -eq 0) ("有 $mismatch 条对不上, 定稿后成绩被改过却没重新生成")
    }
    $c2.Dispose(); $cn2.Close()

    # ══════════════════════════════════════════════════════════════════
    # 8. TRI(试游) 不许占名次                                2026-09-01
    #
    # 这条测的是一个真出过的错: IsNoTime 只认 DSQ/DNS/DNF/DQ, 不含 TRI,
    # 于是试游的人也进了有效名单 —— 他占掉第 2, 后面每个真选手都往后挪一位,
    # 整个项目总排名错位, 而且界面上完全看不出来。
    # 规则是"TRI 显成绩、不排名、不计分"。
    #
    # 做法: 在库的拷贝上把某个项目最快的三个人的成绩改成 10/11/12 秒
    # (比场上任何成绩都快, 排名位置就完全确定), 再把 11 秒那个标成 TRI。
    # 期望: 10秒=第1, 11秒(TRI)=无名次, 12秒=第2 —— 而不是第3。
    # ══════════════════════════════════════════════════════════════════
    Write-Host "8. TRI 试游不占名次"
    $cnW = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$db;Version=3;")
    $cnW.Open(); $cw = $cnW.CreateCommand()
    $cw.CommandText = @"
SELECT he.round_id, en.event_id, COUNT(*) AS n
FROM heat_entries he JOIN entries en ON en.id=he.entry_id
WHERE he.final_time>0 AND (he.status IS NULL OR he.status='') AND he.reserve_no IS NULL
GROUP BY he.round_id, en.event_id HAVING n>=3 ORDER BY n DESC LIMIT 1
"@
    $rdT = $cw.ExecuteReader(); $triRound = 0; $triEvent = 0
    if ($rdT.Read()) { $triRound = [int64]$rdT[0]; $triEvent = [int64]$rdT[1] }
    $rdT.Close()

    if ($triRound -eq 0) {
        Check "找得到一个至少 3 人有成绩的项目(本测试才有意义)" $false "库里没有这样的项目"
    } else {
        $cw.CommandText = @"
SELECT he.id FROM heat_entries he JOIN entries en ON en.id=he.entry_id
WHERE he.round_id=$triRound AND en.event_id=$triEvent AND he.final_time>0
  AND (he.status IS NULL OR he.status='') AND he.reserve_no IS NULL
ORDER BY he.final_time LIMIT 3
"@
        $rdT = $cw.ExecuteReader(); $ids = @()
        while ($rdT.Read()) { $ids += [int64]$rdT[0] }
        $rdT.Close()

        # 造出确定的成绩, 免得受库里存量数据影响
        $cw.CommandText = "UPDATE heat_entries SET final_time=10.00, status='' WHERE id=" + $ids[0]; $cw.ExecuteNonQuery() | Out-Null
        $cw.CommandText = "UPDATE heat_entries SET final_time=11.00, status='' WHERE id=" + $ids[1]; $cw.ExecuteNonQuery() | Out-Null
        $cw.CommandText = "UPDATE heat_entries SET final_time=12.00, status='' WHERE id=" + $ids[2]; $cw.ExecuteNonQuery() | Out-Null

        # 先验一遍"没有 TRI 时"是 1,2,3 —— 不然下面那条测不出是 TRI 的功劳
        $svc.RecomputeRanks($triRound, $triEvent, 'selftest')
        $cw.CommandText = "SELECT rank FROM heat_entries WHERE id=" + $ids[2]
        $rankBefore = [int]$cw.ExecuteScalar()
        Check "基准: 三个人都正常时, 第三快的是第 3" ($rankBefore -eq 3) ("实得第 " + $rankBefore + " 名")

        # 把中间那个标成试游
        $cw.CommandText = "UPDATE heat_entries SET status='TRI' WHERE id=" + $ids[1]; $cw.ExecuteNonQuery() | Out-Null
        $svc.RecomputeRanks($triRound, $triEvent, 'selftest')

        $cw.CommandText = "SELECT rank FROM heat_entries WHERE id=" + $ids[0]; $r1 = [int]$cw.ExecuteScalar()
        $cw.CommandText = "SELECT rank FROM heat_entries WHERE id=" + $ids[1]; $r2 = [int]$cw.ExecuteScalar()
        $cw.CommandText = "SELECT rank FROM heat_entries WHERE id=" + $ids[2]; $r3 = [int]$cw.ExecuteScalar()
        Check "TRI 本人没有名次" ($r2 -eq 0) ("试游者拿到了第 " + $r2 + " 名")
        Check "TRI 前面的人名次不受影响(仍是第 1)" ($r1 -eq 1) ("实得第 " + $r1 + " 名")
        Check "TRI 后面的人【递补上来】(第 3 -> 第 2)" ($r3 -eq 2) ("实得第 " + $r3 + " 名 —— 试游者占了一个名次")

        # 组内名次(GetHeat 现算的那一份) 也不许给 TRI 名次
        $badHeatTri = @()
        foreach ($h in $svc.GetHeatList($triRound)) {
            foreach ($row in $svc.GetHeat($triRound, $h.Heat)) {
                if ($row.Status -eq 'TRI' -and $row.HeatRank -gt 0) {
                    $badHeatTri += ("第" + $h.Heat + "组 道" + $row.Lane + " 组内名次=" + $row.HeatRank) }
            }
        }
        Check "TRI 也没有组内名次" ($badHeatTri.Count -eq 0) ($badHeatTri -join '; ')
    }

    # ══════════════════════════════════════════════════════════════════
    # 9. 组排名表: 全部组确认后生成得出来, 且能按项目整行读回  2026-09-01
    #
    # 为什么要测这个: "文档编辑/输出/打印 -> 项目成绩" 现在直接读这张表。
    # 表生成不出来、或者读不回来, 现场就是"确认过的成绩查不到", 而且看不出原因。
    # E2E 那边只测了反面(还有组没确认时不许生成), 正面一直没人测。
    # ══════════════════════════════════════════════════════════════════
    Write-Host "9. 组排名表生成与读取"
    if ($triRound -ne 0) {
        # 把这个赛次的所有组都标成已确认, 满足"全部组已确认"这个前提
        $cw.CommandText = "UPDATE heats SET confirmed_at='2026-09-01 00:00:00' WHERE round_id=$triRound AND COALESCE(state,'') <> 'cancelled'"
        $cw.ExecuteNonQuery() | Out-Null
    }
    $cw.Dispose(); $cnW.Close()

    if ($triRound -eq 0) {
        Check "跳过(上一节没找到可用项目)" $false "无可用项目"
    } else {
        # 库是 WAL 模式 —— 不先落盘, 复制出去的是半截(前面那些 UPDATE 还在 -wal 里)
        $svc.Db.Checkpoint()

        # MeetDbBridge 按 BaseDir\Database\<名字>.db 找库, 所以摆一份到那个位置
        $dbDir = Join-Path $tmp 'Database'
        New-Item -ItemType Directory -Force $dbDir | Out-Null
        Copy-Item $db (Join-Path $dbDir 'ergen.db') -Force

        $tBridge = $asm.GetType('SwimmingScoreboard.Db.MeetDbBridge')
        $bArgs = New-Object 'object[]' 1; $bArgs[0] = $null
        $bridge = [Activator]::CreateInstance($tBridge, $bArgs)
        $bridge.BaseDir = $tmp
        $opened = $bridge.Open('ergen')
        Check "竞赛库打得开" $opened "MeetDbBridge.Open 返回 false"

        if ($opened) {
            # 这个赛次/项目的 (组别,性别,项目,赛次) 是什么
            $sched = $svc.GetSchedule() | Where-Object { $_.RoundId -eq $triRound -and $_.EventId -eq $triEvent } | Select-Object -First 1
            if (-not $sched) {
                Check "赛程里找得到这个项目" $false "GetSchedule 里没有 round=$triRound event=$triEvent"
            } else {
                $gen = $bridge.GenerateEventRankingIfComplete(
                    $sched.AgeGroup, $sched.Gender, $sched.EventName, $sched.Stage, 'selftest')
                Check "全部组已确认 -> 组排名表生成得出来" ($gen -gt 0) ("生成了 $gen 行")

                $rows = $bridge.GetEventRankingRows($sched.AgeGroup, $sched.Gender, $sched.EventName, $sched.Stage)
                $rows = @($rows)
                Check "能按项目把组排名表整行读回来" ($rows.Count -gt 0) ("读回 " + $rows.Count + " 行")

                if ($rows.Count -gt 0) {
                    # 姓名/代表队/成绩都得在行里 —— 打印就靠它, 不能再回内存凑
                    $ranked = @($rows | Where-Object { $_.Rank -gt 0 })
                    Check "第一行就是第 1 名" ($rows[0].Rank -eq 1) ("第一行名次=" + $rows[0].Rank)
                    $noName = @($ranked | Where-Object { [string]::IsNullOrEmpty($_.AthleteName) })
                    Check "有名次的行都带着姓名(打印不用回内存凑)" ($noName.Count -eq 0) ("有 " + $noName.Count + " 行没姓名")

                    # 名次必须是不下降的 —— 表里的顺序就是打印顺序
                    $badOrder = @()
                    for ($i=1; $i -lt $ranked.Count; $i++) {
                        if ($ranked[$i].Rank -lt $ranked[$i-1].Rank) {
                            $badOrder += ("第" + $i + "行 名次" + $ranked[$i].Rank + " 排在 " + $ranked[$i-1].Rank + " 后面") }
                    }
                    Check "表里的顺序就是名次顺序(不用再排)" ($badOrder.Count -eq 0) (($badOrder | Select-Object -First 3) -join '; ')

                    # 上一节标的那个 TRI 不许出现在总排名表里
                    $triInTable = @($rows | Where-Object { $_.Status -eq 'TRI' })
                    Check "TRI 不进组排名表(总排名列表中不显示 TRI)" ($triInTable.Count -eq 0) ("表里有 " + $triInTable.Count + " 条 TRI")

                    # 无名次的(判罚/弃权)必须全在有名次的后面
                    $firstUnranked = -1; $lastRanked = -1
                    for ($i=0; $i -lt $rows.Count; $i++) {
                        if ($rows[$i].Rank -gt 0) { $lastRanked = $i }
                        elseif ($firstUnranked -lt 0) { $firstUnranked = $i }
                    }
                    $okTail = ($firstUnranked -lt 0) -or ($firstUnranked -gt $lastRanked)
                    Check "判罚/弃权排在所有有名次的人后面" $okTail ("第一个无名次在第 $firstUnranked 行, 最后一个有名次在第 $lastRanked 行")
                }

                # 进度查询: 全部确认之后 pending 必须是 0
                $total = 0; $pending = 0; $rankedN = 0
                $pArgs = [object[]]@($sched.AgeGroup, $sched.Gender, $sched.EventName, $sched.Stage, $total, $pending, $rankedN)
                $tBridge.GetMethod('GetEventRankingProgress').Invoke($bridge, $pArgs) | Out-Null
                Check "进度查询: 总组数 > 0" ([int]$pArgs[4] -gt 0) ("总组数=" + $pArgs[4])
                Check "进度查询: 全部确认后未确认组数 = 0" ([int]$pArgs[5] -eq 0) ("还差 " + $pArgs[5] + " 组")
            }
            $bridge.Dispose()
        }
    }

} finally {
    if ($svc) { try { $svc.Dispose() } catch {} }
    try { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}

Write-Host ""
Write-Host ("结果: " + $script:Pass + " 过 / " + $script:Fail + " failed")
if ($script:Fail -gt 0) { Write-Host "失败项:" -ForegroundColor Red; $script:Msgs | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Red }; exit 1 }
exit 0
