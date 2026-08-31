# ══════════════════════════════════════════════════════════════════
# 端到端测试 (2026-08-30)
#
# 测的是【真的跑起来的主服务器】: 起进程 -> 连 WebSocket -> 发一组成绩 ->
# 等回执 -> 关进程 -> 查库断言。整条回推链路都在里面, 不经过任何界面操作。
#
# 安全: 把整个 Release 目录复制到 %TEMP% 再跑, 原目录和原库全程不动。
#
# 用法: powershell -ExecutionPolicy Bypass -File tools\DbSelfTest\E2ETest.ps1
# 退出码: 0 = 全过, 1 = 有失败
# ══════════════════════════════════════════════════════════════════

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$srcDir = Join-Path $root 'SwimmingScoreboard\bin\x64\Release'

$script:Pass = 0; $script:Fail = 0; $script:Msgs = @()
function Check($name, $cond, $detail) {
    if ($cond) { $script:Pass++; Write-Host ("  [PASS] " + $name) -ForegroundColor Green }
    else { $script:Fail++; Write-Host ("  [FAIL] " + $name + "  " + $detail) -ForegroundColor Red
           $script:Msgs += ($name + " :: " + $detail) }
}
function Sql($dbPath, $sqlText) {
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath;Version=3;")
    $cn.Open(); $c = $cn.CreateCommand(); $c.CommandText = $sqlText
    $rd = $c.ExecuteReader(); $out = @()
    while ($rd.Read()) { $row = @{}; for ($i=0; $i -lt $rd.FieldCount; $i++) { $row[$rd.GetName($i)] = $rd[$i] }; $out += [pscustomobject]$row }
    $rd.Close(); $c.Dispose(); $cn.Close(); return ,$out
}

# ── 准备: 整个 Release 复制到临时目录 ─────────────────────────────
$tmp = Join-Path $env:TEMP ('e2e_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
Write-Host "复制运行环境到临时目录(原目录不动)…"
Copy-Item $srcDir $tmp -Recurse -Force
$exe = Join-Path $tmp 'SwimmingScoreboard.exe'
Add-Type -Path (Join-Path $tmp 'System.Data.SQLite.dll')
$db = (Get-ChildItem (Join-Path $tmp 'Database') -Filter '*.db' |
       Where-Object { $_.Name -notlike 'current_heat*' -and $_.Name -notlike '*.bak*' } |
       Sort-Object Length -Descending | Select-Object -First 1).FullName
Write-Host ("测试环境: " + $tmp)
Write-Host ""

$proc = $null
try {
    # ── 挑一个【还没有成绩】的组来测 ────────────────────────────
    $cand = Sql $db @"
SELECT he.round_id, he.heat, e.age_group, e.gender, e.event_name, r.stage, COUNT(*) AS n
FROM heat_entries he
JOIN rounds r ON r.id = he.round_id
JOIN entries en ON en.id = he.entry_id
JOIN events e ON e.id = en.event_id
GROUP BY he.round_id, he.heat
HAVING SUM(CASE WHEN he.final_time > 0 THEN 1 ELSE 0 END) = 0 AND COUNT(*) >= 3
LIMIT 1
"@
    if ($cand.Count -eq 0) { Write-Host "找不到空白组可测" -ForegroundColor Red; exit 1 }
    $t = $cand[0]
    $rid = [long]$t.round_id; $heat = [int]$t.heat
    Write-Host ("测试用组: " + $t.age_group + " " + $t.gender + " " + $t.event_name + " " + $t.stage + " 第" + $heat + "组 (" + $t.n + " 人, 原本无成绩)")

    $lanes = Sql $db "SELECT id, lane FROM heat_entries WHERE round_id=$rid AND heat=$heat AND lane IS NOT NULL ORDER BY lane"
    # 故意造两个【一模一样】的成绩, 验并列
    $times = @(30.11, 30.11, 31.25, 32.40, 33.55, 34.60, 35.70, 36.80)
    $lanePayload = @(); $i = 0
    foreach ($ln in $lanes) {
        if ($i -ge $times.Count) { break }
        $lanePayload += [ordered]@{ Lane = [int]$ln.lane; HeatEntryId = [long]$ln.id
                                    FinalTime = $times[$i]; Status = ""; TimingSource = "E2E" }
        $i++
    }
    Write-Host ("  造 " + $lanePayload.Count + " 个成绩, 前两道故意相同(" + $times[0] + ") 验并列")

    # ── 起主服务器 ──────────────────────────────────────────────
    Write-Host "启动主服务器…"
    $proc = Start-Process $exe -PassThru
    $ok = $false
    for ($k = 0; $k -lt 60; $k++) {
        Start-Sleep -Seconds 2
        try { $tc = New-Object Net.Sockets.TcpClient; $tc.Connect('127.0.0.1', 3002); $tc.Close(); $ok = $true; break } catch {}
    }
    Check "主服务器 WebSocket(3002) 起来了" $ok "等了 120 秒还连不上"
    if (-not $ok) { throw "服务器没起来" }

    # ── 连上去, 发身份帧 + 一组成绩 ─────────────────────────────
    $ws = New-Object Net.WebSockets.ClientWebSocket
    $ct = New-Object Threading.CancellationTokenSource 30000
    $ws.ConnectAsync([Uri]"ws://127.0.0.1:3002", $ct.Token).Wait()
    Check "WebSocket 连接建立" ($ws.State -eq 'Open') ("状态=" + $ws.State)

    function SendJson($obj) {
        $json = ($obj | ConvertTo-Json -Depth 12 -Compress)
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $seg = New-Object ArraySegment[byte] -ArgumentList @(,$bytes)
        $ws.SendAsync($seg, [Net.WebSockets.WebSocketMessageType]::Text, $true, $ct.Token).Wait()
    }
    SendJson ([ordered]@{ type = 'TIMING_WEB_IDENTITY'; role = 'e2e-test' })
    Start-Sleep -Seconds 2

    $pushId = ($t.age_group + '_' + $t.gender + '_' + $t.event_name + '_' + $t.stage + '_h' + $heat)
    foreach ($ch in [IO.Path]::GetInvalidFileNameChars()) { $pushId = $pushId.Replace($ch, '_') }
    $live = [ordered]@{ MeetRoundId = $rid; Heat = $heat; AgeGroup = $t.age_group; Gender = $t.gender
                        EventName = $t.event_name; Stage = $t.stage; ResultConfirmed = $true; Lanes = $lanePayload }
    SendJson ([ordered]@{ type = 'HEAT_CONFIRMED_PUSH'; id = $pushId
                          data = [ordered]@{ ageGroup = $t.age_group; gender = $t.gender; eventName = $t.event_name
                                             stage = $t.stage; heat = $heat; results = @(); liveHeat = $live } })
    Write-Host "已发送 HEAT_CONFIRMED_PUSH，等回执…"

    # ── 等回执 ──────────────────────────────────────────────────
    $buf = New-Object byte[] 65536
    $seg2 = New-Object ArraySegment[byte] -ArgumentList @(,$buf)
    $gotAck = $false; $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and -not $gotAck) {
        $task = $ws.ReceiveAsync($seg2, $ct.Token)
        if (-not $task.Wait(10000)) { break }
        $res = $task.Result
        if ($res.Count -le 0) { continue }
        $msg = [Text.Encoding]::UTF8.GetString($buf, 0, $res.Count)
        if ($msg -match 'HEAT_CONFIRMED_ACK') { $gotAck = $true }
    }
    Check "收到主服务器回执 HEAT_CONFIRMED_ACK" $gotAck "30 秒内没收到"

    # ── 参数同步: 发 SET_LANE_CLOSE_SETTINGS, 验主服务器把它存进了库 ──
    Write-Host "发送参数修改(盲表 左2/右2, 关闭 1.75s)…"
    SendJson ([ordered]@{ type = 'TIMING_CMD'; command = 'SET_LANE_CLOSE_SETTINGS'
        data = [ordered]@{ leftBlindWatchCount = 2; rightBlindWatchCount = 2; laneCloseTime = 1.75
                           startBlockCloseDelay = 3.5; splitDisplayTime = 6.25 } })
    Start-Sleep -Seconds 4


    Start-Sleep -Seconds 3
    try { $ws.Dispose() } catch {}

} finally {
    if ($proc -and -not $proc.HasExited) { try { $proc.Kill(); Start-Sleep -Seconds 3 } catch {} }
}

# ── 关掉进程后查库断言 ────────────────────────────────────────────
Write-Host ""
Write-Host "查库断言:"
$after = Sql $db "SELECT lane, final_time, rank, status FROM heat_entries WHERE round_id=$rid AND heat=$heat AND lane IS NOT NULL ORDER BY lane"
$scored = @($after | Where-Object { [double]$_.final_time -gt 0 })
Check "成绩已写进主服务器的库" ($scored.Count -ge 3) ("有成绩的行: " + $scored.Count)

$byTime = $scored | Sort-Object { [double]$_.final_time }
if ($byTime.Count -ge 2) {
    Check "最快者项目名次 = 1" ([int]$byTime[0].rank -eq 1) ("实得 " + $byTime[0].rank)
    $t1 = [double]$byTime[0].final_time; $t2 = [double]$byTime[1].final_time
    if ([Math]::Round($t1*100) -eq [Math]::Round($t2*100)) {
        Check "成绩相同的两人并列同名次" ([int]$byTime[0].rank -eq [int]$byTime[1].rank) ("名次 " + $byTime[0].rank + " vs " + $byTime[1].rank)
        if ($byTime.Count -ge 3) {
            Check "并列之后名次跳号(1,1,3)" ([int]$byTime[2].rank -eq 3) ("第三名实得 " + $byTime[2].rank)
        }
    }
}
$cfg = Sql $db "SELECT value FROM settings WHERE key='timing_settings'"
Check "参数已存进主服务器的库(settings 表)" ($cfg.Count -gt 0) "settings 表里没有 timing_settings"
if ($cfg.Count -gt 0) {
    $o = $cfg[0].value | ConvertFrom-Json
    Check "盲表数量存对了(左2)"  ([int]$o.LeftBlindWatchCount -eq 2)  ("实得 " + $o.LeftBlindWatchCount)
    Check "盲表数量存对了(右2)"  ([int]$o.RightBlindWatchCount -eq 2) ("实得 " + $o.RightBlindWatchCount)
    Check "泳道关闭时间存对了(1.75)" ([double]$o.LaneCloseTime -eq 1.75) ("实得 " + $o.LaneCloseTime)
    Check "出发台延时存对了(3.5)"   ([double]$o.StartBlockCloseDelay -eq 3.5) ("实得 " + $o.StartBlockCloseDelay)
    Check "分段停留存对了(6.25)"    ([double]$o.SplitDisplayTime -eq 6.25) ("实得 " + $o.SplitDisplayTime)
}
$ds = Sql $db "SELECT value FROM settings WHERE key='device_states'"
Check "设备状态也进了库" ($ds.Count -gt 0) "settings 表里没有 device_states"

# 组排名表: 该项目全部组确认后应生成
$er = Sql $db "SELECT rank, athlete_name, final_time FROM event_rankings WHERE round_id=$rid ORDER BY rank"
$pendH = Sql $db "SELECT COUNT(*) AS n FROM heats WHERE round_id=$rid AND COALESCE(state,'') <> 'cancelled' AND confirmed_at IS NULL"
$allDone = ([int]$pendH[0].n -eq 0)
if ($allDone) {
    Check "全部组确认后已生成组排名表" ($er.Count -gt 0) "event_rankings 里没有这个项目"
    if ($er.Count -gt 1) {
        Check "组排名表按名次排好且从 1 开始" ([int]$er[0].rank -eq 1) ("首行名次 " + $er[0].rank)
    }
} else {
    Check "还有组没确认时不生成组排名表(避免过程值被当定稿)" ($er.Count -eq 0) ("未全部确认却已生成 " + $er.Count + " 行")
}

$hc = Sql $db "SELECT confirmed_at FROM heats WHERE round_id=$rid AND heat=$heat"
Check "该组已标记为已确认" ($hc.Count -gt 0 -and $hc[0].confirmed_at -ne [DBNull]::Value -and $hc[0].confirmed_at) "confirmed_at 为空"

try { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch {}

Write-Host ""
Write-Host ("结果: " + $script:Pass + " 过 / " + $script:Fail + " failed")
if ($script:Fail -gt 0) { $script:Msgs | ForEach-Object { Write-Host ("  " + $_) -ForegroundColor Red }; exit 1 }
exit 0
