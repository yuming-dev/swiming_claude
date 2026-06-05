# 2026-06-05 比赛档案导入工具 v3 (PDF 秩序单 为分组权威源)
# 数据流:
#   - 运动员个人信息 (姓名/出生/身份证/比赛号/分属单位/教练) ← 3 个 .xls
#   - 比赛日程 (Session/Date/Time/Event/AgeGroup) ← PDF 秩序单 (fenzu_parsed.json)
#   - 分组 (Heat/Lane) ← PDF 秩序单
#   - 甘肃省纪录 ← 赛会(2026-04-30).xls

param(
    [string[]]$ExcelPaths = @(
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(01Other).xls',
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(02Other).xls',
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(03Other).xls'
    ),
    [string]$FenzuJson = 'C:\代码\swiming_claude\tools\fenzu_parsed.json',
    [string]$RecordsXls = 'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\赛会(2026-04-30).xls',
    [string]$CompetitionName = '2026年甘肃省浩沙FAFA杯U系列青少年游泳俱乐部联赛（定西站）',
    [string]$Location = '定西市全民健身中心',
    [string]$Organizer = '甘肃省游泳协会',
    [string]$HostOrg = '定西市体育运动中心',
    [string]$OutDir = 'C:\代码\swiming_claude\SwimmingScoreboard\bin\x64\Release\Database'
)

$ErrorActionPreference = 'Stop'
foreach ($p in $ExcelPaths) { if (-not (Test-Path $p)) { throw "找不到 Excel: $p" } }
if (-not (Test-Path $FenzuJson)) { throw "找不到 PDF 解析: $FenzuJson" }
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

# ─── 1. 读 .xls 建运动员个人信息主表 (key = "事件名|姓名|队短名") ─────
Write-Host "[1/5] 读 3 个秩序 .xls 建运动员主表"
$athleteMaster = @{}        # key="EventName|Name|TeamShort" → 个人详情
$athletesAll = New-Object System.Collections.Generic.List[object]   # 所有去重运动员 (name+id)
$unitSet = New-Object System.Collections.Generic.HashSet[string]
$eventSet = New-Object System.Collections.Generic.HashSet[string]
$ageSet = New-Object System.Collections.Generic.HashSet[string]
$athletesById = @{}   # bibNumber → 单一运动员记录, 避免重复

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
try {
    foreach ($xlsPath in $ExcelPaths) {
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
            if (-not $title -or -not $dist -or -not $stroke) { continue }
            if (-not $gender -or -not $age) { continue }
            $distNorm = $dist -replace 'X','x'
            $evName = "$distNorm$stroke"
            $isRelay = ($dist -match '[Xx×]') -or ($title -like '*接力*')
            # 用 中文 unitFull 作为代表队 (与 PDF 一致), unitShort 留作 CountryShort 备用
            $teamKey = if ($unitFull) { $unitFull } else { $unitShort }
            [void]$unitSet.Add($teamKey)
            [void]$eventSet.Add($evName)
            [void]$ageSet.Add($age)
            $entrySec = Parse-EntryTime $entryT

            # 个人项目: 1 名运动员 (cols 22-29). 接力: 4 棒 (cols 22/30/38/46)
            $legBlocks = if ($isRelay) {
                @(
                    @{ NameCol=22; BirthCol=24; IdCol=25; BibCol=27; UnitCol=28; CoachCol=29 }
                    @{ NameCol=30; BirthCol=32; IdCol=33; BibCol=35; UnitCol=36; CoachCol=37 }
                    @{ NameCol=38; BirthCol=40; IdCol=41; BibCol=43; UnitCol=44; CoachCol=45 }
                    @{ NameCol=46; BirthCol=48; IdCol=49; BibCol=51; UnitCol=52; CoachCol=53 }
                )
            } else {
                @(@{ NameCol=22; BirthCol=24; IdCol=25; BibCol=27; UnitCol=28; CoachCol=29 })
            }
            foreach ($lb in $legBlocks) {
                $nm = "$($data[$r, $lb.NameCol])".Trim()
                if (-not $nm) { continue }
                $bib = "$($data[$r, $lb.BibCol])".Trim()
                $idn = "$($data[$r, $lb.IdCol])".Trim()
                $birth = "$($data[$r, $lb.BirthCol])".Trim()
                $birthFmt = if ($birth -and $birth -match '\d{4}-\d{2}-\d{2}') { $birth.Substring(0,10) } else { $birth }
                $key = "$evName|$nm|$teamKey"
                if (-not $athleteMaster.ContainsKey($key)) {
                    $athleteMaster[$key] = [pscustomobject]@{
                        Name = $nm; BibNumber = $bib; IDNumber = $idn; BirthDate = $birthFmt
                        Gender = (Normalize-Gender $gender)   # event col 7 (个人项: 准确; 接力 4 名子项: 也准)
                        Country = $teamKey; CountryShort = $unitShort
                        EventName = $evName; AgeCategory = $age
                        IsRelay = $isRelay; EntryTime = (Format-EntryTime $entrySec); EntryTimeSeconds = $entrySec
                        UnitFull = $unitFull
                    }
                }
                # 全局唯一运动员 (按 idNumber 或 bibNumber)
                $uniqKey = if ($idn) { $idn } else { "$nm|$teamKey|$birthFmt" }
                if (-not $athletesById.ContainsKey($uniqKey)) {
                    $athletesById[$uniqKey] = $athleteMaster[$key]
                }
            }
        }
    }
} finally {
    $excel.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
    [System.GC]::Collect(); [System.GC]::WaitForPendingFinalizers()
}
Write-Host ("    athleteMaster: " + $athleteMaster.Count + " 项目报名; 单位 " + $unitSet.Count + "; 项目 " + $eventSet.Count + "; 组别 " + $ageSet.Count)

# ─── 2. 读 fenzu_parsed.json (PDF 秩序单解析结果) ─────
Write-Host "[2/5] 读 PDF 解析结果 (fenzu_parsed.json)"
$pdfEvents = Get-Content $FenzuJson -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Host ("    PDF 事件: " + $pdfEvents.Count)

# ─── 3. 合并: 用 PDF 的 (heat/lane) + .xls 的 (个人信息) 生成 swimmers ─────
Write-Host "[3/5] 合并 PDF 分组 + .xls 个人信息"
$swimmers = New-Object System.Collections.Generic.List[object]
$schedule = New-Object System.Collections.Generic.List[object]
$relayTeams = New-Object System.Collections.Generic.List[object]
$relayCounter = 0
$matched = 0; $unmatched = 0; $unmatchedNames = New-Object System.Collections.Generic.List[string]

foreach ($pe in $pdfEvents) {
    $evName = $pe.EventName
    $age = $pe.AgeGroup
    $schedGender = $pe.Gender   # '男子' / '女子' / '男女'
    $schedGenderNorm = Normalize-Gender $schedGender   # '男' / '女' / '男女'
    $isRelay = $evName -like '*接力*'

    # Schedule 条目
    $sessTime = if ($pe.SessionTime) { $pe.SessionTime } else { '09:00' }
    $sessDate = if ($pe.SessionDate) { $pe.SessionDate } else { '2026-06-06' }
    $schedule.Add([pscustomobject]([ordered]@{
        SessionNumber = $pe.Session; SessionName = "第$($pe.Session)场（$sessDate $sessTime）"
        Date = $sessDate; Time = $sessTime
        AgeGroup = $age; EventName = $evName; Gender = $schedGenderNorm
        Stage = $pe.Stage; HeatCount = $pe.HeatCount; IsRelay = $isRelay
        DisplayText = "$schedGenderNorm $age $evName $($pe.Stage)"
    })) | Out-Null

    foreach ($ln in $pe.Lanes) {
        $rawName = $ln.Name
        if (-not $rawName) { continue }
        if ($rawName -eq 'DNS' -or $rawName -eq 'DSQ' -or $rawName -eq 'DNF') { continue }   # 状态行误匹配

        # 个人项目: 匹配 athleteMaster[evName|name|team]
        # 接力项目: 暂存到 relay (待后续完整处理)
        if (-not $isRelay) {
            # 个人 gender: PDF (B)=男, (G)=女; 若 schedule 是单性别, 也可推断
            $indivGender = ''
            if ($ln.Gender -eq 'B') { $indivGender = '男' }
            elseif ($ln.Gender -eq 'G') { $indivGender = '女' }
            elseif ($schedGenderNorm -eq '男' -or $schedGenderNorm -eq '女') { $indivGender = $schedGenderNorm }
            $key = "$evName|$rawName|$($ln.Team)"
            $details = $athleteMaster[$key]
            if (-not $details) {
                $unmatched++
                $unmatchedNames.Add("$evName / $rawName / $($ln.Team)") | Out-Null
                # 仍然加 swimmer, 但个人信息留空
                $swimmers.Add([pscustomobject]([ordered]@{
                    Name = $rawName; BibNumber = ''; BirthDate = ''; Age = 0
                    Gender = $indivGender; Country = $ln.Team; CountryShort = $ln.Team
                    IDNumber = ''; Phone = ''; Notes = ''
                    LegLabel = ''; CSANumber = ''; FINANumber = $null; HealthCertDate = $null
                    EventName = $evName; CurrentStage = $pe.Stage
                    Heat = $ln.Heat; Lane = $ln.Lane
                    EntryTime = ''; EntryTimeSeconds = 0.0
                    IsQualified = $true; Status = ''; CurrentRank = 0
                    AgeCategory = if ($ln.SubAge) { $ln.SubAge } else { $age }
                    Results = @()
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
                    AgeCategory = if ($ln.SubAge) { $ln.SubAge } else { $age }
                    Results = @()
                    StageAssignments = @{ $pe.Stage = @{ Stage = $pe.Stage; Heat = $ln.Heat; Lane = $ln.Lane; EntryTimeSeconds = $details.EntryTimeSeconds; EntryTime = $details.EntryTime } }
                })) | Out-Null
            }
        }
        # 接力暂以 'team' 字段创建接力队 swimmer (后面再细化棒次)
        else {
            $relayCounter++
            $teamBib = ('R' + $relayCounter.ToString('D3'))
            $teamName = $ln.Team
            # 接力: rawName 可能是单棒选手名, 多棒名字会合并在 ln.Name. 拆开:
            $legNamesArr = @()
            if ($rawName) { $legNamesArr += ($rawName -split '[,，;；]') }
            $legNameStr = $legNamesArr -join ','
            $swimmers.Add([pscustomobject]([ordered]@{
                Name = $teamName; BibNumber = $teamBib; BirthDate = ''; Age = 0
                Gender = $schedGenderNorm; Country = $teamName; CountryShort = $teamName
                IDNumber = ''; Phone = ''
                Notes = "接力队 棒次:$legNameStr"
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
                Country = $teamName; Members = @(); Legs = @()
                EntryTime = ''; EntryTimeSeconds = 0.0
                Heat = $ln.Heat; Lane = $ln.Lane
                FinalTime = 0.0; Rank = 0; Stage = $pe.Stage; Status = ''; LegSplits = @()
                AgeCategoryPending = $false
            })) | Out-Null
        }
    }
}
Write-Host ("    匹配运动员: " + $matched + "; 未匹配: " + $unmatched + " (接力 " + $relayCounter + ")")
Write-Host "    未匹配条目 (前 20):"
foreach ($u in ($unmatchedNames | Select-Object -First 20)) { Write-Host ("        " + $u) }
Write-Host "    athleteMaster 前 5 个键:"
$cnt = 0
foreach ($k in $athleteMaster.Keys) {
    Write-Host ("        " + $k)
    $cnt++; if ($cnt -ge 5) { break }
}

# ─── 4. 读 赛会.xls (甘肃省纪录) ─────
Write-Host "[4/5] 读赛会.xls (甘肃省纪录)"
$recordsList = New-Object System.Collections.Generic.List[object]
if ($RecordsXls -and (Test-Path $RecordsXls)) {
    $excel2 = New-Object -ComObject Excel.Application
    $excel2.Visible = $false; $excel2.DisplayAlerts = $false
    try {
        $wbR = $excel2.Workbooks.Open($RecordsXls, 0, $true)
        $shR = $wbR.Sheets.Item(1)
        $rngR = $shR.UsedRange
        $dataR = $rngR.Value2
        $rowsR = $rngR.Rows.Count
        $wbR.Close($false)
        $recSeen = New-Object System.Collections.Generic.HashSet[string]
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
            if ($recSeen.Add($rkey)) {
                $recordsList.Add([pscustomobject]@{
                    EventName = $eventName; Gender = $genderN; AgeGroup = $ageR
                    RecordType = '甘肃省纪录'
                    HolderName = $nm; HolderCountry = $unit
                    Time = $recSec; TimeInSeconds = $recSec
                    Date = (Excel-DateSerialToIso $dt); Location = $loc
                    Notes = $meet
                })
            }
        }
    } finally {
        $excel2.Quit()
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel2) | Out-Null
    }
}
Write-Host ("    纪录: " + $recordsList.Count + " 条")

# ─── 5. 构建 CompetitionPackage ─────
Write-Host "[5/5] 构建 CompetitionPackage JSON"
$ageGroupDefs = @(
    @{Name='6岁组'; MinAge=6; MaxAge=6 }, @{Name='7岁组'; MinAge=7; MaxAge=7 }
    @{Name='8-9岁组'; MinAge=8; MaxAge=9 }, @{Name='10-11岁组'; MinAge=10; MaxAge=11 }
    @{Name='12-13岁组'; MinAge=12; MaxAge=13 }, @{Name='14-15岁组'; MinAge=14; MaxAge=15 }
    @{Name='16-18岁组'; MinAge=16; MaxAge=18 }
    @{Name='12~15岁组'; MinAge=12; MaxAge=15 }, @{Name='12~18岁组'; MinAge=12; MaxAge=18 }
    @{Name='14~18岁组'; MinAge=14; MaxAge=18 }, @{Name='14-18岁组'; MinAge=14; MaxAge=18 }
)
$ageGroups = $ageGroupDefs | ForEach-Object { [pscustomobject]@{ Name=$_.Name; MinAge=$_.MinAge; MaxAge=$_.MaxAge } }
foreach ($ag in ($ageSet | Sort-Object)) {
    if (-not ($ageGroups | Where-Object { $_.Name -eq $ag })) {
        $m = [regex]::Match($ag, '(\d+)\D*(\d+)?')
        $mn = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
        $mx = if ($m.Success -and $m.Groups[2].Value) { [int]$m.Groups[2].Value } else { $mn }
        $ageGroups += [pscustomobject]@{ Name = $ag; MinAge = $mn; MaxAge = $mx }
    }
}

$eventOrder = '50米自由泳','100米自由泳','200米自由泳','400米自由泳','800米自由泳','1500米自由泳',
              '50米仰泳','100米仰泳','200米仰泳','50米蛙泳','100米蛙泳','200米蛙泳',
              '50米蝶泳','100米蝶泳','200米蝶泳','100米混合泳','200米混合泳','400米混合泳',
              '4x50米自由泳接力','4x50米混合泳接力','4x100米自由泳接力','4x100米混合泳接力'
$events = New-Object System.Collections.Generic.List[string]
foreach ($e in $eventOrder) { if ($eventSet.Contains($e)) { $events.Add($e) } }
foreach ($e in ($eventSet | Sort-Object)) { if (-not $events.Contains($e)) { $events.Add($e) } }

$units = New-Object System.Collections.Generic.List[object]
foreach ($u in ($unitSet | Sort-Object)) {
    $units.Add([pscustomobject]@{ Name=$u; ShortName=$u; FullName=$u; Leader=''; Coach=''; Phone='' })
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
    Schedule = $schedule; Events = $events; AgeGroups = $ageGroups
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
