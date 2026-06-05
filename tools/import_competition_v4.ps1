# 2026-06-05 比赛档案导入工具 v4 (秩序单 .xls 为分组权威源, 替代 v3 的 PDF 解析)
# 数据流:
#   - 比赛日程 + 分组 ← 2026甘肃U系列定西站竞赛秩序单10道20260605.xls (结构化, 直读)
#   - 运动员个人信息 (姓名/出生/身份证/比赛号) ← 3 个秩序(0XOther).xls
#   - 甘肃省纪录 ← 赛会(2026-04-30).xls

param(
    [string]$ScheduleXls = 'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序单10道20260605.xls',
    [string[]]$AthleteXls = @(
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(01Other).xls',
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(02Other).xls',
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(03Other).xls'
    ),
    [string]$RecordsXls = 'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\赛会(2026-04-30).xls',
    [string]$CompetitionName = '2026年甘肃省浩沙FAFA杯U系列青少年游泳俱乐部联赛（定西站）',
    [string]$Location = '定西市全民健身中心',
    [string]$Organizer = '甘肃省游泳协会',
    [string]$HostOrg = '定西市体育运动中心',
    [string]$OutDir = 'C:\代码\swiming_claude\SwimmingScoreboard\bin\x64\Release\Database'
)

$ErrorActionPreference = 'Stop'
foreach ($p in @($ScheduleXls) + $AthleteXls) { if (-not (Test-Path $p)) { throw "找不到: $p" } }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

function Parse-EntryTime([string]$s) {
    if (-not $s) { return 0.0 }
    $s = $s.Trim()
    if ($s -eq '' -or $s -eq 'NT' -or $s -eq '-') { return 0.0 }
    if ($s -match '^(\d+):(\d+(\.\d+)?)$') { return [double]$matches[1] * 60.0 + [double]$matches[2] }
    if ($s -match '^(\d+(\.\d+)?)$') { return [double]$s }
    return 0.0
}
function Format-EntryTime([double]$sec) {
    if ($sec -le 0) { return '' }
    if ($sec -ge 60) { $m = [int]($sec / 60); $r = $sec - $m * 60; return ('{0}:{1:00.00}' -f $m, $r) }
    return ('{0:0.00}' -f $sec)
}
function Normalize-Gender([string]$g) {
    switch ($g) { '男子' { '男' } '女子' { '女' } '男女' { '男女' } default { if ($g) { $g } else { '混合' } } }
}
function Parse-RecordTime($v) {
    if ($null -eq $v) { return 0.0 }
    $s = "$v".Trim()
    if (-not $s) { return 0.0 }
    if ($s -match '^(\d+):(\d+(\.\d+)?)$') { return [double]$matches[1] * 60.0 + [double]$matches[2] }
    if ($s -match '^(\d+(\.\d+)?)$') {
        $n = "$([int][double]$s)"
        if ($n.Length -le 2) { return [double]$n / 100.0 }
        $cc = [int]$n.Substring($n.Length - 2)
        $rest = $n.Substring(0, $n.Length - 2)
        if ($rest.Length -le 2) { $ss = [int]$rest; $mm = 0 }
        else { $ss = [int]$rest.Substring($rest.Length - 2); $mm = [int]$rest.Substring(0, $rest.Length - 2) }
        return $mm * 60.0 + $ss + $cc / 100.0
    }
    return 0.0
}
function Excel-DateSerialToIso($v) {
    if ($null -eq $v) { return '' }
    $s = "$v".Trim()
    if (-not $s) { return '' }
    if ($s -match '^\d{4}-\d{2}-\d{2}') { return $s.Substring(0,10) }
    if ($s -match '^\d+(\.\d+)?$') { try { return ([datetime]::FromOADate([double]$s)).ToString('yyyy-MM-dd') } catch { return '' } }
    return ''
}

# ─── 1. 读 3 个秩序 .xls 建运动员个人信息主表 ─────
Write-Host "[1/4] 读 3 个秩序 .xls 建运动员个人信息表"
$athleteMaster = @{}   # key = "EventName|Name|TeamFull" → 个人详情
$unitFullToShort = @{} # 中文队名 → 短码 (备用)
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
try {
    foreach ($xlsPath in $AthleteXls) {
        Write-Host ("    读: " + (Split-Path $xlsPath -Leaf))
        $wb = $excel.Workbooks.Open($xlsPath, 0, $true)
        $sh = $wb.Sheets.Item(1)
        $rng = $sh.UsedRange
        $data = $rng.Value2
        $rowCount = $rng.Rows.Count
        $wb.Close($false)
        for ($r = 2; $r -le $rowCount; $r++) {
            $title    = "$($data[$r, 4])"
            $gender   = "$($data[$r, 7])"
            $age      = "$($data[$r, 8])"
            $dist     = "$($data[$r, 10])"
            $stroke   = "$($data[$r, 11])"
            $unitFull = "$($data[$r, 16])"   # 中文名 (e.g. '自由渡')
            $unitShort= "$($data[$r, 17])"   # 短码 (e.g. 'ZYD')
            $entryT   = "$($data[$r, 19])"
            if (-not $title -or -not $dist -or -not $stroke -or -not $gender -or -not $age) { continue }
            $distNorm = $dist -replace 'X','x'
            $evName = "$distNorm$stroke"
            $isRelay = ($dist -match '[Xx×]') -or ($title -like '*接力*')
            $teamFull = if ($unitFull) { $unitFull } else { $unitShort }
            if ($unitFull -and $unitShort) { $unitFullToShort[$unitFull] = $unitShort }
            $entrySec = Parse-EntryTime $entryT

            $legBlocks = if ($isRelay) {
                @(
                    @{ NameCol=22; BirthCol=24; IdCol=25; BibCol=27 },
                    @{ NameCol=30; BirthCol=32; IdCol=33; BibCol=35 },
                    @{ NameCol=38; BirthCol=40; IdCol=41; BibCol=43 },
                    @{ NameCol=46; BirthCol=48; IdCol=49; BibCol=51 }
                )
            } else {
                @(@{ NameCol=22; BirthCol=24; IdCol=25; BibCol=27 })
            }
            foreach ($lb in $legBlocks) {
                $nm = "$($data[$r, $lb.NameCol])".Trim() -replace '\s+',''   # 去空格 (e.g. '马 萱' → '马萱')
                if (-not $nm) { continue }
                $bib = "$($data[$r, $lb.BibCol])".Trim()
                $idn = "$($data[$r, $lb.IdCol])".Trim()
                $birth = "$($data[$r, $lb.BirthCol])".Trim()
                $birthFmt = if ($birth -and $birth -match '\d{4}-\d{2}-\d{2}') { $birth.Substring(0,10) } else { $birth }
                $key = "$evName|$nm|$teamFull"
                if (-not $athleteMaster.ContainsKey($key)) {
                    $athleteMaster[$key] = [pscustomobject]@{
                        Name = $nm; BibNumber = $bib; IDNumber = $idn; BirthDate = $birthFmt
                        Gender = (Normalize-Gender $gender)
                        Country = $teamFull; CountryShort = $unitShort
                        EventName = $evName; AgeCategory = $age
                        IsRelay = $isRelay; EntryTime = (Format-EntryTime $entrySec); EntryTimeSeconds = $entrySec
                    }
                }
            }
        }
    }
    Write-Host ("    athleteMaster: " + $athleteMaster.Count + " 项目报名")

    # ─── 2. 读 秩序单 .xls 解析 schedule + heat/lane ─────
    Write-Host "[2/4] 读 秩序单 .xls (权威分组源)"
    $wbS = $excel.Workbooks.Open($ScheduleXls, 0, $true)
    $shS = $wbS.Sheets.Item(1)
    $rngS = $shS.UsedRange
    $rowsS = $rngS.Rows.Count
    $dataS = $rngS.Value2
    $wbS.Close($false)
    Write-Host ("    秩序单: " + $rowsS + " 行")

    $events = New-Object System.Collections.Generic.List[object]
    $currentSession = 0; $currentDate = '2026-06-06'; $currentTime = '09:00'
    $currentEvent = $null
    $state = 'WAIT_HEADER'   # WAIT_HEADER | WAIT_NAMES | WAIT_TEAMS | WAIT_AGE_OR_NEXT_OR_RELAY
    $pendingNames = @{}; $pendingTeams = @{}; $pendingAges = @{}
    $relayLanes = @{}        # lane → [name1, name2, name3, name4]  (接力 4 棒)
    $currentHeat = 0
    $isRelayEvent = $false

    function Flush-Heat-V4 {
        if ($null -eq $script:currentEvent -or $script:currentHeat -le 0) { return }
        if ($script:isRelayEvent) {
            # 接力: relayLanes[lane] = 队员姓名数组, pendingTeams[lane] = 队名
            foreach ($lk in $script:relayLanes.Keys) {
                $members = $script:relayLanes[$lk]
                $team = if ($script:pendingTeams.ContainsKey($lk)) { $script:pendingTeams[$lk] } else { '' }
                if (-not $team -and $members.Count -gt 0) { continue }
                $memberClean = @()
                $memberGenders = @()
                foreach ($m in $members) {
                    $g = ''
                    if ($m -match '\(B\)$') { $g = 'B'; $m = ($m -replace '\(B\)$','').Trim() -replace '\s+','' }
                    elseif ($m -match '\(G\)$') { $g = 'G'; $m = ($m -replace '\(G\)$','').Trim() -replace '\s+','' }
                    else { $m = ($m).Trim() -replace '\s+','' }
                    $memberClean += $m; $memberGenders += $g
                }
                $script:currentEvent.Lanes.Add([pscustomobject]@{
                    Heat = $script:currentHeat
                    Lane = $lk
                    Name = ''   # 接力没有"单名", 队名在 Team
                    Team = $team
                    Members = $memberClean       # 4 棒姓名
                    MemberGenders = $memberGenders  # 4 棒性别
                    SubAge = ''
                    Gender = ''
                    IsRelay = $true
                }) | Out-Null
            }
        } else {
            foreach ($lk in $script:pendingNames.Keys) {
                $rawName = $script:pendingNames[$lk]
                $gender = ''
                if ($rawName -match '\(B\)$') { $gender = 'B'; $rawName = ($rawName -replace '\(B\)$','').Trim() }
                elseif ($rawName -match '\(G\)$') { $gender = 'G'; $rawName = ($rawName -replace '\(G\)$','').Trim() }
                $rawName = $rawName -replace '\s+',''   # 去名字内空格
                if (-not $rawName -or $rawName -eq 'DNS' -or $rawName -eq 'DSQ' -or $rawName -eq 'DNF') { continue }
                $team = if ($script:pendingTeams.ContainsKey($lk)) { $script:pendingTeams[$lk] } else { '' }
                $age  = if ($script:pendingAges.ContainsKey($lk))  { $script:pendingAges[$lk] }  else { '' }
                $script:currentEvent.Lanes.Add([pscustomobject]@{
                    Heat = $script:currentHeat
                    Lane = $lk
                    Name = $rawName
                    Team = $team
                    SubAge = $age
                    Gender = $gender
                    IsRelay = $false
                }) | Out-Null
            }
        }
        $script:pendingNames = @{}; $script:pendingTeams = @{}; $script:pendingAges = @{}
        $script:relayLanes = @{}
    }

    for ($r = 1; $r -le $rowsS; $r++) {
        $c1 = "$($dataS[$r, 1])".Trim()
        # 场次行: "第 1 场    2026-06-06   09:00"
        if ($c1 -match '^第\s*(\d+)\s*场\s+(\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2})') {
            $currentSession = [int]$matches[1]
            $currentDate = $matches[2]; $currentTime = $matches[3]
            continue
        }
        # 事件标题行: col1 = "    N . 性别+年龄组 距离+泳式 赛次  ", col6 = "X人/队", col7 = "Y组"
        $evMatch = [regex]::Match($c1, '^\s*(\d+)\s*\.\s*(男女|男子|女子)(\d+(?:[-~]\d+)?)岁组\s*(\d+[xX×]?\d*米)\s*([^\s决预半]+)\s*(决赛|预赛|半决赛|次复赛|B组决赛)')
        if ($evMatch.Success) {
            Flush-Heat-V4
            $evNum = [int]$evMatch.Groups[1].Value
            $genderPart = $evMatch.Groups[2].Value
            $agePart = $evMatch.Groups[3].Value
            $age = "$agePart`岁组"
            $distancePart = $evMatch.Groups[4].Value -replace 'X','x'
            $strokePart = $evMatch.Groups[5].Value
            $stagePart = $evMatch.Groups[6].Value
            $eventName = "$distancePart$strokePart"
            # 人数 + 组数 在 col 6 / col 7
            $personCountText = "$($dataS[$r, 6])"
            $heatCountText = "$($dataS[$r, 7])"
            $personCount = 0; $heatCount = 1
            $m2 = [regex]::Match($personCountText, '(\d+)\s*[人队]')
            if ($m2.Success) { $personCount = [int]$m2.Groups[1].Value }
            $m3 = [regex]::Match($heatCountText, '(\d+)\s*组')
            if ($m3.Success) { $heatCount = [int]$m3.Groups[1].Value }
            $newEv = [pscustomobject]@{
                Session = $currentSession; SessionDate = $currentDate; SessionTime = $currentTime
                EvNum = $evNum
                Gender = $genderPart
                AgeGroup = $age
                Distance = $distancePart; Stroke = $strokePart; Stage = $stagePart
                EventName = $eventName
                PersonCount = $personCount; HeatCount = $heatCount
                Lanes = New-Object System.Collections.Generic.List[object]
            }
            $events.Add($newEv) | Out-Null
            $script:currentEvent = $newEv
            $script:isRelayEvent = ($eventName -like '*接力*')
            $script:state = 'WAIT_HEADER'
            $script:currentHeat = 0
            continue
        }
        # 道次表头 "组\道"
        if ($c1 -eq '组\道') {
            $script:state = 'WAIT_HEAT'   # 期望下一行是 heat 数字
            continue
        }
        # heat 数字行: col 1 = 单数字 1-9
        if ($c1 -match '^\d$' -and $null -ne $script:currentEvent) {
            if ($script:currentHeat -gt 0) { Flush-Heat-V4 }   # 上一 heat 还没 flush
            $script:currentHeat = [int]$c1
            $script:state = 'WAIT_NAMES'
            # 这一行的 col 2-11 是 lane 0-9 的 内容. 接力: 第1棒姓名; 个人项: 全名
            if ($script:isRelayEvent) {
                for ($cc = 2; $cc -le 11; $cc++) {
                    $v = "$($dataS[$r, $cc])".Trim()
                    if (-not $v) { continue }
                    $lane = $cc - 2
                    if (-not $script:relayLanes.ContainsKey($lane)) { $script:relayLanes[$lane] = New-Object System.Collections.Generic.List[string] }
                    $script:relayLanes[$lane].Add($v) | Out-Null
                }
            } else {
                for ($cc = 2; $cc -le 11; $cc++) {
                    $v = "$($dataS[$r, $cc])".Trim()
                    if (-not $v) { continue }
                    $lane = $cc - 2
                    $script:pendingNames[$lane] = $v
                }
                $script:state = 'WAIT_TEAMS'
            }
            continue
        }
        # 数据行 (col 1 为空, col 2-11 有内容)
        if ($null -ne $script:currentEvent -and $script:currentHeat -gt 0) {
            $hasContent = $false
            for ($cc = 2; $cc -le 11; $cc++) {
                if ("$($dataS[$r, $cc])".Trim()) { $hasContent = $true; break }
            }
            if (-not $hasContent) { continue }   # 跳空行

            # 接力: 多行队员名 + 最后一行队名 + 可选 DNS 标记
            if ($script:isRelayEvent) {
                # 检测这一行是 DNS/DSQ (单 token)
                $allDns = $true; $hasMember = $false
                for ($cc = 2; $cc -le 11; $cc++) {
                    $v = "$($dataS[$r, $cc])".Trim()
                    if (-not $v) { continue }
                    if ($v -ne 'DNS' -and $v -ne 'DSQ' -and $v -ne 'DNF') { $allDns = $false }
                    $hasMember = $true
                }
                if ($allDns -and $hasMember) {
                    # 状态行, 暂不入 lanes (后续可加 Status 字段)
                    continue
                }
                # 还没满 4 棒 → 队员名; 满 4 棒 → 队名
                for ($cc = 2; $cc -le 11; $cc++) {
                    $v = "$($dataS[$r, $cc])".Trim()
                    if (-not $v) { continue }
                    $lane = $cc - 2
                    if (-not $script:relayLanes.ContainsKey($lane)) { $script:relayLanes[$lane] = New-Object System.Collections.Generic.List[string] }
                    if ($script:relayLanes[$lane].Count -lt 4) {
                        # 检测是否带 (B)/(G) — 是 → 队员; 否 → 可能是队名
                        if ($v -match '\(B\)$|\(G\)$') {
                            $script:relayLanes[$lane].Add($v) | Out-Null
                        } else {
                            # 不带 B/G + 名字行已经至少 1 个 (B)/(G) 队员 → 队名
                            $script:pendingTeams[$lane] = $v
                        }
                    } else {
                        $script:pendingTeams[$lane] = $v
                    }
                }
                continue
            }

            # 个人项目: row1=names (已处理 in heat#), row2=teams, row3=optional ages, row4=blank or next heat
            if ($script:state -eq 'WAIT_TEAMS') {
                for ($cc = 2; $cc -le 11; $cc++) {
                    $v = "$($dataS[$r, $cc])".Trim()
                    if (-not $v) { continue }
                    $lane = $cc - 2
                    $script:pendingTeams[$lane] = $v
                }
                $script:state = 'WAIT_AGE_OR_NEXT'
                continue
            }
            if ($script:state -eq 'WAIT_AGE_OR_NEXT') {
                # 是否含 岁组?
                $isAge = $false
                for ($cc = 2; $cc -le 11; $cc++) {
                    if ("$($dataS[$r, $cc])".Trim() -like '*岁组*') { $isAge = $true; break }
                }
                if ($isAge) {
                    for ($cc = 2; $cc -le 11; $cc++) {
                        $v = "$($dataS[$r, $cc])".Trim()
                        if (-not $v) { continue }
                        $script:pendingAges[$cc - 2] = $v
                    }
                    Flush-Heat-V4
                    $script:state = 'WAIT_HEAT'
                    continue
                }
                # 否则: 下一行的 names? 不应该 — names 行会有 col1 = heat 号. 此处先 ignore.
                continue
            }
        }
    }
    # 末尾 flush
    Flush-Heat-V4

} finally {
    $excel.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
    [System.GC]::Collect(); [System.GC]::WaitForPendingFinalizers()
}

$totalLanes = 0; foreach ($e in $events) { $totalLanes += $e.Lanes.Count }
Write-Host ("    解析事件: " + $events.Count + ", 总道次: " + $totalLanes)

# ─── 3. 合并 + 构建 swimmers + schedule ─────
Write-Host "[3/4] 合并 PDF 分组 + .xls 个人信息"
$swimmers = New-Object System.Collections.Generic.List[object]
$schedule = New-Object System.Collections.Generic.List[object]
$relayTeams = New-Object System.Collections.Generic.List[object]
$relayCounter = 0
$matched = 0; $unmatched = 0
$unmatchedSamples = New-Object System.Collections.Generic.List[string]
$unitSet = New-Object System.Collections.Generic.HashSet[string]
$eventSet = New-Object System.Collections.Generic.HashSet[string]
$ageSet = New-Object System.Collections.Generic.HashSet[string]

foreach ($pe in $events) {
    $evName = $pe.EventName
    $age = $pe.AgeGroup
    $schedGender = $pe.Gender
    $schedGenderNorm = Normalize-Gender $schedGender
    [void]$eventSet.Add($evName); [void]$ageSet.Add($age)

    $schedule.Add([pscustomobject]([ordered]@{
        SessionNumber = $pe.Session; SessionName = "第$($pe.Session)场（$($pe.SessionDate) $($pe.SessionTime)）"
        Date = $pe.SessionDate; Time = $pe.SessionTime
        AgeGroup = $age; EventName = $evName; Gender = $schedGenderNorm
        Stage = $pe.Stage; HeatCount = $pe.HeatCount; IsRelay = ($evName -like '*接力*')
        DisplayText = "$schedGenderNorm $age $evName $($pe.Stage)"
    })) | Out-Null

    foreach ($ln in $pe.Lanes) {
        if ($ln.IsRelay) {
            # 接力: 一道 = 一个 team
            $relayCounter++
            $teamBib = ('R' + $relayCounter.ToString('D3'))
            $teamName = $ln.Team
            [void]$unitSet.Add($teamName)
            $legNamesStr = ($ln.Members) -join ','
            $swimmers.Add([pscustomobject]([ordered]@{
                Name = $teamName; BibNumber = $teamBib; BirthDate = ''; Age = 0
                Gender = $schedGenderNorm; Country = $teamName; CountryShort = $teamName
                IDNumber = ''; Phone = ''
                Notes = "接力队 棒次:$legNamesStr"
                LegLabel = ''; CSANumber = ''; FINANumber = $null; HealthCertDate = $null
                EventName = $evName; CurrentStage = $pe.Stage
                Heat = $ln.Heat; Lane = $ln.Lane
                EntryTime = ''; EntryTimeSeconds = 0.0
                IsQualified = $true; Status = ''; CurrentRank = 0
                AgeCategory = $age; Results = @()
                StageAssignments = @{ $pe.Stage = @{ Stage = $pe.Stage; Heat = $ln.Heat; Lane = $ln.Lane; EntryTimeSeconds = 0.0; EntryTime = '' } }
            })) | Out-Null
            $relayTeams.Add([pscustomobject]([ordered]@{
                TeamName = $teamName; EventName = $evName; Gender = $schedGenderNorm; AgeGroup = $age
                Country = $teamName; Members = $ln.Members; Legs = @()
                EntryTime = ''; EntryTimeSeconds = 0.0
                Heat = $ln.Heat; Lane = $ln.Lane
                FinalTime = 0.0; Rank = 0; Stage = $pe.Stage; Status = ''; LegSplits = @()
                AgeCategoryPending = $false
            })) | Out-Null
            continue
        }
        # 个人
        $rawName = $ln.Name
        if (-not $rawName) { continue }
        $indivGender = ''
        if ($ln.Gender -eq 'B') { $indivGender = '男' }
        elseif ($ln.Gender -eq 'G') { $indivGender = '女' }
        elseif ($schedGenderNorm -eq '男' -or $schedGenderNorm -eq '女') { $indivGender = $schedGenderNorm }
        [void]$unitSet.Add($ln.Team)
        $key = "$evName|$rawName|$($ln.Team)"
        $details = $athleteMaster[$key]
        $actualAge = if ($ln.SubAge) { $ln.SubAge } else { $age }
        if (-not $details) {
            $unmatched++
            if ($unmatchedSamples.Count -lt 20) { $unmatchedSamples.Add("$evName / $rawName / $($ln.Team)") | Out-Null }
            $swimmers.Add([pscustomobject]([ordered]@{
                Name = $rawName; BibNumber = ''; BirthDate = ''; Age = 0
                Gender = $indivGender; Country = $ln.Team; CountryShort = $ln.Team
                IDNumber = ''; Phone = ''; Notes = ''
                LegLabel = ''; CSANumber = ''; FINANumber = $null; HealthCertDate = $null
                EventName = $evName; CurrentStage = $pe.Stage
                Heat = $ln.Heat; Lane = $ln.Lane
                EntryTime = ''; EntryTimeSeconds = 0.0
                IsQualified = $true; Status = ''; CurrentRank = 0
                AgeCategory = $actualAge; Results = @()
                StageAssignments = @{ $pe.Stage = @{ Stage = $pe.Stage; Heat = $ln.Heat; Lane = $ln.Lane; EntryTimeSeconds = 0.0; EntryTime = '' } }
            })) | Out-Null
        } else {
            $matched++
            $swimmers.Add([pscustomobject]([ordered]@{
                Name = $details.Name; BibNumber = $details.BibNumber; BirthDate = $details.BirthDate; Age = 0
                Gender = $indivGender; Country = $details.Country; CountryShort = $details.CountryShort
                IDNumber = $details.IDNumber; Phone = ''; Notes = ''
                LegLabel = ''; CSANumber = ''; FINANumber = $null; HealthCertDate = $null
                EventName = $evName; CurrentStage = $pe.Stage
                Heat = $ln.Heat; Lane = $ln.Lane
                EntryTime = $details.EntryTime; EntryTimeSeconds = $details.EntryTimeSeconds
                IsQualified = $true; Status = ''; CurrentRank = 0
                AgeCategory = $actualAge; Results = @()
                StageAssignments = @{ $pe.Stage = @{ Stage = $pe.Stage; Heat = $ln.Heat; Lane = $ln.Lane; EntryTimeSeconds = $details.EntryTimeSeconds; EntryTime = $details.EntryTime } }
            })) | Out-Null
        }
    }
}
Write-Host ("    个人匹配: " + $matched + "; 未匹配: " + $unmatched + "; 接力队: " + $relayCounter)
if ($unmatchedSamples.Count -gt 0) {
    Write-Host "    未匹配样本:"
    foreach ($u in $unmatchedSamples) { Write-Host ("        " + $u) }
}

# ─── 4. 读 赛会 + 构建 Package ─────
Write-Host "[4/4] 读赛会.xls 纪录 + 构建 JSON"
$recordsList = New-Object System.Collections.Generic.List[object]
if (Test-Path $RecordsXls) {
    $excel2 = New-Object -ComObject Excel.Application
    $excel2.Visible = $false; $excel2.DisplayAlerts = $false
    try {
        $wbR = $excel2.Workbooks.Open($RecordsXls, 0, $true)
        $shR = $wbR.Sheets.Item(1)
        $dataR = $shR.UsedRange.Value2
        $rowsR = $shR.UsedRange.Rows.Count
        $wbR.Close($false)
        $seen = New-Object System.Collections.Generic.HashSet[string]
        for ($r = 3; $r -le $rowsR; $r++) {
            $g = "$($dataR[$r, 1])".Trim(); $ageR = "$($dataR[$r, 2])".Trim()
            $dist = "$($dataR[$r, 3])".Trim(); $stroke = "$($dataR[$r, 4])".Trim()
            $rec = $dataR[$r, 6]; $nm = "$($dataR[$r, 7])".Trim(); $unit = "$($dataR[$r, 8])".Trim()
            $meet = "$($dataR[$r, 9])".Trim(); $dt = $dataR[$r, 10]; $loc = "$($dataR[$r, 11])".Trim()
            if (-not $dist -or -not $stroke -or -not $ageR) { continue }
            $distN = $dist -replace 'X','x'
            $eventName = "$distN$stroke"
            $genderN = Normalize-Gender $g
            $recSec = Parse-RecordTime $rec
            if ($recSec -le 0) { continue }
            $rkey = "$eventName|$genderN|$ageR|甘肃省纪录|$nm"
            if ($seen.Add($rkey)) {
                $recordsList.Add([pscustomobject]@{
                    EventName = $eventName; Gender = $genderN; AgeGroup = $ageR
                    RecordType = '甘肃省纪录'; HolderName = $nm; HolderCountry = $unit
                    Time = $recSec; TimeInSeconds = $recSec
                    Date = (Excel-DateSerialToIso $dt); Location = $loc; Notes = $meet
                })
            }
        }
    } finally { $excel2.Quit(); [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel2) | Out-Null }
}

# Age groups + Events + Units
$ageGroups = @(
    [pscustomobject]@{Name='6岁组'; MinAge=6; MaxAge=6}
    [pscustomobject]@{Name='7岁组'; MinAge=7; MaxAge=7}
    [pscustomobject]@{Name='8-9岁组'; MinAge=8; MaxAge=9}
    [pscustomobject]@{Name='10-11岁组'; MinAge=10; MaxAge=11}
    [pscustomobject]@{Name='12-13岁组'; MinAge=12; MaxAge=13}
    [pscustomobject]@{Name='14-15岁组'; MinAge=14; MaxAge=15}
    [pscustomobject]@{Name='16-18岁组'; MinAge=16; MaxAge=18}
    [pscustomobject]@{Name='12~15岁组'; MinAge=12; MaxAge=15}
    [pscustomobject]@{Name='12~18岁组'; MinAge=12; MaxAge=18}
    [pscustomobject]@{Name='14~18岁组'; MinAge=14; MaxAge=18}
)
foreach ($ag in ($ageSet | Sort-Object)) {
    if (-not ($ageGroups | Where-Object { $_.Name -eq $ag })) {
        $m = [regex]::Match($ag, '(\d+)\D*(\d+)?')
        $mn = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
        $mx = if ($m.Success -and $m.Groups[2].Value) { [int]$m.Groups[2].Value } else { $mn }
        $ageGroups += [pscustomobject]@{ Name=$ag; MinAge=$mn; MaxAge=$mx }
    }
}
$eventOrder = '50米自由泳','100米自由泳','200米自由泳','400米自由泳',
              '50米仰泳','100米仰泳','200米仰泳','50米蛙泳','100米蛙泳','200米蛙泳',
              '50米蝶泳','100米蝶泳','200米蝶泳','100米混合泳','200米混合泳','400米混合泳',
              '4x50米自由泳接力','4x50米混合泳接力','4x100米自由泳接力','4x100米混合泳接力'
$eventsList = New-Object System.Collections.Generic.List[string]
foreach ($e in $eventOrder) { if ($eventSet.Contains($e)) { $eventsList.Add($e) } }
foreach ($e in ($eventSet | Sort-Object)) { if (-not $eventsList.Contains($e)) { $eventsList.Add($e) } }

$units = New-Object System.Collections.Generic.List[object]
foreach ($u in ($unitSet | Sort-Object)) {
    if (-not $u) { continue }
    $sh = if ($unitFullToShort.ContainsKey($u)) { $unitFullToShort[$u] } else { $u }
    $units.Add([pscustomobject]@{ Name=$u; ShortName=$sh; FullName=$u; Leader=''; Coach=''; Phone='' })
}

$package = [ordered]@{
    CompetitionName = $CompetitionName; CompetitionMode = 'domestic'
    CompetitionRule = 'U系列青少年游泳比赛'
    StartDate = '2026-06-06'; EndDate = '2026-06-07'; Location = $Location
    PoolLength = 50; LaneCount = 10
    Organizer = $Organizer; Host = $HostOrg
    TechnicalDelegate = ''; Referee = ''; Starter = ''; Arbiter = ''; ChiefJudge = ''
    Officials = @()
    Swimmers = $swimmers; RelayTeams = $relayTeams
    Records = $recordsList.ToArray(); TeamScores = @()
    Schedule = $schedule; Events = $eventsList; AgeGroups = $ageGroups
    Genders = @('男','女','混合','男女'); Stages = @('预赛','半决赛','决赛')
    HeatCounts = @('1组','2组','3组','4组','5组','6组','7组','8组','9组','10组')
    BibRanges = @(); Units = $units; StaffList = @()
    WizardDraft = $null; LaneCloseSettings = $null; DisputeLog = @{}
    ProgramBook = $null; ResultBook = $null
    DisplayRecordLabel = ''; DisplayRecordTypeName = ''; DisplayRecordOptions = @()
    ConfirmedHeats = @(); ScoringConfig = $null; DurationConfig = $null
}
$outFile = Join-Path $OutDir "$CompetitionName.json"
$json = $package | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($outFile, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host ""
Write-Host ("✓ 完成: " + $outFile + "  (" + ((Get-Item $outFile).Length) + " 字节)")
Write-Host ("    Swimmer (含接力队): " + $swimmers.Count)
Write-Host ("    RelayTeams: " + $relayTeams.Count)
Write-Host ("    Schedule: " + $schedule.Count)
Write-Host ("    Units: " + $units.Count)
Write-Host ("    Records: " + $recordsList.Count)
Write-Host ("    个人匹配率: " + $matched + " / " + ($matched + $unmatched))
