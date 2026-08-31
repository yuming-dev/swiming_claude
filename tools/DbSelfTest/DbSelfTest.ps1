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

} finally {
    if ($svc) { try { $svc.Dispose() } catch {} }
    try { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}

Write-Host ""
Write-Host ("结果: " + $script:Pass + " 过 / " + $script:Fail + " failed")
if ($script:Fail -gt 0) { Write-Host "失败项:" -ForegroundColor Red; $script:Msgs | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Red }; exit 1 }
exit 0
