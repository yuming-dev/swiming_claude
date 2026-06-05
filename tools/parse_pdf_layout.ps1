# 2026-06-05 用 pdftotext -tsv 输出的坐标信息精准解析竞赛秩序单分组表
# 输出: tools\fenzu_parsed.json  (供 import_competition_v2.ps1 用作 heat/lane 覆盖源)

param(
    [string]$Tsv = 'C:\代码\swiming_claude\tools\fenzu_pdf_text.tsv',
    [string]$OutJson = 'C:\代码\swiming_claude\tools\fenzu_parsed.json'
)

$ErrorActionPreference = 'Stop'

# 1. 读 TSV, 只保留 word 级 (level=5)
$words = New-Object System.Collections.Generic.List[object]
$first = $true
foreach ($line in (Get-Content $Tsv -Encoding UTF8)) {
    if ($first) { $first = $false; continue }   # 跳表头
    $cols = $line -split "`t"
    if ($cols.Count -lt 12) { continue }
    if ($cols[0] -ne '5') { continue }
    $text = $cols[11]
    if ($text -eq '' -or $text -like '###*' -or $text -like '*FlyFish*' -or $text -match '^\d+\s*/\s*17') { continue }
    $words.Add([pscustomobject]@{
        Page = [int]$cols[1]
        Left = [double]$cols[6]
        Top  = [double]$cols[7]
        Width = [double]$cols[8]
        CenterX = [double]$cols[6] + [double]$cols[8] / 2.0
        Text = $text
    })
}

# 2. 按 page + top 桶分行 (同行 Y 相差 <= 4)
$lines = New-Object System.Collections.Generic.List[object]
$cur = $null
foreach ($w in $words | Sort-Object Page, Top, Left) {
    if ($null -eq $cur -or $w.Page -ne $cur.Page -or [Math]::Abs($w.Top - $cur.Top) -gt 4) {
        $cur = @{ Page = $w.Page; Top = $w.Top; Tokens = New-Object System.Collections.Generic.List[object] }
        $lines.Add($cur)
    }
    $cur.Tokens.Add($w)
}

Write-Host ("总词数: " + $words.Count + ", 总行数: " + $lines.Count)

# 3. 解析事件: 按行扫描
$events = New-Object System.Collections.Generic.List[object]
$currentSession = 0
$currentSessionDate = ''
$currentSessionTime = ''
$currentEvent = $null
$laneHeaders = $null         # 当前事件的 (lane → centerX) 映射
$pendingNameRow = $null      # 缓冲: 名字行 (等待 heat 号 + 队名行)
$pendingTeamRow = $null
$pendingAgeRow = $null
$currentHeat = 0

function Title-IsEventStart([string]$text) {
    return ($text -match '^(\d+)\s*\.\s*(.+)$')
}

# 把同一行 tokens 合并 (空格连接)
function Merge-Tokens($tokens) {
    return ($tokens | Sort-Object Left | ForEach-Object { $_.Text }) -join ''
}

# 把 token 数组分到 lane (每 token 单独映射到最近 lane, 同 lane 多 token 合并)
function Tokens-ToLanes($tokens, $laneCenters) {
    # bucket: lane → [(left, text), ...] (保留 X 用于排序)
    $bucket = @{}
    foreach ($t in $tokens) {
        $bestLane = -1; $bestDist = 9999
        foreach ($lk in $laneCenters.Keys) {
            $d = [Math]::Abs($t.CenterX - $laneCenters[$lk])
            if ($d -lt $bestDist) { $bestDist = $d; $bestLane = $lk }
        }
        # 容差 25 (lane 间距 ~51), 超过认为是冗余 token
        if ($bestLane -ge 0 -and $bestDist -lt 25) {
            if (-not $bucket.ContainsKey($bestLane)) { $bucket[$bestLane] = New-Object System.Collections.Generic.List[object] }
            $bucket[$bestLane].Add([pscustomobject]@{ Left = $t.Left; Text = $t.Text })
        }
    }
    $result = @{}
    foreach ($lk in $bucket.Keys) {
        $sorted = $bucket[$lk] | Sort-Object Left
        $result[$lk] = ($sorted | ForEach-Object { $_.Text }) -join ''
    }
    return $result
}

# 提交当前缓冲的 heat (合并 name/team/age) 到 currentEvent
function Flush-Heat() {
    if ($null -eq $script:currentEvent -or $script:currentHeat -le 0) { return }
    if ($null -eq $script:pendingNameRow -and $null -eq $script:pendingTeamRow) { return }
    if ($script:pendingNameRow) { $names = Tokens-ToLanes $script:pendingNameRow.Tokens $script:laneHeaders } else { $names = @{} }
    if ($script:pendingTeamRow) { $teams = Tokens-ToLanes $script:pendingTeamRow.Tokens $script:laneHeaders } else { $teams = @{} }
    if ($script:pendingAgeRow) { $ages = Tokens-ToLanes $script:pendingAgeRow.Tokens $script:laneHeaders } else { $ages = @{} }
    foreach ($lane in $names.Keys) {
        $rawName = $names[$lane]
        $gender = ''
        if ($rawName -match '\(B\)$') { $gender = 'B'; $rawName = $rawName -replace '\(B\)$','' }
        elseif ($rawName -match '\(G\)$') { $gender = 'G'; $rawName = $rawName -replace '\(G\)$','' }
        $rawName = $rawName.Trim()
        if ($teams.ContainsKey($lane)) { $team = $teams[$lane] } else { $team = '' }
        if ($ages.ContainsKey($lane)) { $age = $ages[$lane] } else { $age = '' }
        $script:currentEvent.Lanes.Add([pscustomobject]@{
            Heat = $script:currentHeat
            Lane = $lane
            Name = $rawName
            Team = $team
            SubAge = $age
            Gender = $gender
        }) | Out-Null
    }
    $script:pendingNameRow = $null
    $script:pendingTeamRow = $null
    $script:pendingAgeRow = $null
}

# 状态: WAIT_HEADER | WAIT_NAMES | WAIT_HEAT_OR_TEAMS | WAIT_TEAMS | WAIT_AGE_OR_NEXT
$state = 'WAIT_HEADER'

for ($li = 0; $li -lt $lines.Count; $li++) {
    $line = $lines[$li]
    $rowText = Merge-Tokens $line.Tokens

    # 检测 场次 (第N场 YYYY-MM-DD HH:MM)
    if ($rowText -match '^第(\d+)场') {
        $currentSession = [int]$matches[1]
        # 找日期/时间 (可能在下一行或同行)
        $allTok = ($line.Tokens | Sort-Object Left)
        # 同行扫
        foreach ($t in $allTok) {
            if ($t.Text -match '^\d{4}-\d{2}-\d{2}') { $currentSessionDate = $t.Text }
            if ($t.Text -match '^\d{2}:\d{2}') { $currentSessionTime = $t.Text }
        }
        # 下行扫
        if ($li + 1 -lt $lines.Count) {
            foreach ($t in $lines[$li+1].Tokens) {
                if ($t.Text -match '^\d{4}-\d{2}-\d{2}') { $currentSessionDate = $t.Text }
                if ($t.Text -match '^\d{2}:\d{2}') { $currentSessionTime = $t.Text }
            }
        }
        continue
    }
    # 检测 事件标题: "N . 性别+年龄组 距离+泳式 赛次  X人  Y组"
    # 显式锚定: 距离 + 泳式 + 赛次 (枚举); 用 token 没空格的 merge 文本
    if ($rowText -match '^(\d+)\s*\.\s*(男女|男子|女子)(\d+(?:[-~]\d+)?)岁组(\d+[xX×]?\d*米)([^决预半]+?)(决赛|预赛|半决赛|次复赛|B组决赛)') {
        Flush-Heat
        $evNum = [int]$matches[1]
        $genderPart = $matches[2].Trim()
        $agePart = $matches[3] -replace '~','~'
        $age = "$agePart`岁组"
        $distancePart = $matches[4]
        $strokePart = $matches[5]
        $stagePart = $matches[6]
        # 提取人数 + 组数 from full row text
        $personCount = 0; $heatCount = 1
        $m2 = [regex]::Match($rowText, '(\d+)\s*[人队]')
        if ($m2.Success) { $personCount = [int]$m2.Groups[1].Value }
        $m3 = [regex]::Match($rowText, '(\d+)\s*组')
        if ($m3.Success) { $heatCount = [int]$m3.Groups[1].Value }
        $eventName = ($distancePart -replace 'X','x') + $strokePart
        $newEv = [pscustomobject]@{
            Session = $currentSession
            SessionDate = $currentSessionDate
            SessionTime = $currentSessionTime
            EvNum = $evNum
            Title = $rowText.Trim()
            Gender = $genderPart           # '男子' / '女子' / '男女'
            AgeGroup = $age
            Distance = $distancePart -replace 'X','x'
            Stroke = $strokePart
            Stage = $stagePart
            EventName = $eventName
            PersonCount = $personCount
            HeatCount = $heatCount
            Lanes = New-Object System.Collections.Generic.List[object]
        }
        $events.Add($newEv) | Out-Null
        $script:currentEvent = $newEv
        $script:laneHeaders = $null
        $script:currentHeat = 0
        $script:pendingNameRow = $null
        $script:pendingTeamRow = $null
        $script:pendingAgeRow = $null
        $state = 'WAIT_HEADER'
        continue
    }
    # 检测 lane 表头 "组\道 0 1 2 ... 9"
    if (($rowText -like '*组*道*' -or $line.Tokens[0].Text -eq '组\道')) {
        $script:laneHeaders = @{}
        foreach ($t in $line.Tokens) {
            if ($t.Text -match '^\d$') {
                $script:laneHeaders[[int]$t.Text] = $t.CenterX
            }
        }
        $state = 'WAIT_NAMES'
        continue
    }
    if ($null -eq $script:currentEvent -or $null -eq $script:laneHeaders) { continue }

    # 单独 heat 数字行 (单个 token, 且为 1 位数字, x < 60)
    if ($line.Tokens.Count -eq 1 -and $line.Tokens[0].Text -match '^\d$' -and $line.Tokens[0].Left -lt 60) {
        $script:currentHeat = [int]$line.Tokens[0].Text
        if ($state -eq 'WAIT_HEAT_OR_TEAMS') { $state = 'WAIT_TEAMS' }
        continue
    }

    # 数据行: 包含 '岁组' = age, 否则 = name/team
    $isAge = ($rowText -match '岁组' -and $rowText -notmatch '^第\d+场')

    if ($state -eq 'WAIT_NAMES') {
        $script:pendingNameRow = $line
        $state = 'WAIT_HEAT_OR_TEAMS'
    }
    elseif ($state -eq 'WAIT_HEAT_OR_TEAMS') {
        # 罕见: 名字行下没看到 heat 数字直接看到 teams — 用前一个 heat 号 / 默认 1
        if ($script:currentHeat -le 0) { $script:currentHeat = 1 }
        $script:pendingTeamRow = $line
        $state = 'WAIT_AGE_OR_NEXT'
    }
    elseif ($state -eq 'WAIT_TEAMS') {
        $script:pendingTeamRow = $line
        $state = 'WAIT_AGE_OR_NEXT'
    }
    elseif ($state -eq 'WAIT_AGE_OR_NEXT') {
        if ($isAge) {
            $script:pendingAgeRow = $line
            Flush-Heat
            $state = 'WAIT_NAMES'
        } else {
            # 不带岁组 → 这是下一个 heat 的 名字行. 先 flush 前一 heat, 然后这行作 nameRow
            Flush-Heat
            $script:pendingNameRow = $line
            $state = 'WAIT_HEAT_OR_TEAMS'
        }
    }
}
Flush-Heat

# 最后一个 event 入 list (已在 events 里, 无需追加)

Write-Host ("解析事件: " + $events.Count)
$totalLanes = 0
foreach ($e in $events) { $totalLanes += $e.Lanes.Count }
Write-Host ("总道次: " + $totalLanes)
Write-Host ""
Write-Host "=== 前 3 个事件结构样本 ==="
for ($i = 0; $i -lt [Math]::Min(3, $events.Count); $i++) {
    $e = $events[$i]
    Write-Host ("Ev" + $e.EvNum + " S" + $e.Session + " " + $e.Title)
    foreach ($lane in ($e.Lanes | Sort-Object Heat, Lane)) {
        $extra = if ($lane.SubAge) { " / " + $lane.SubAge } else { "" }
        Write-Host ("    h" + $lane.Heat + " 道" + $lane.Lane + " " + $lane.Name + "(" + $lane.Gender + ") / " + $lane.Team + $extra)
    }
}

# 输出 JSON
$json = $events | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($OutJson, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host ("已写: " + $OutJson)
