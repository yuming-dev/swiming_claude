# 2026-06-01 比赛档案导入工具
# 把"竞赛规程 + 竞赛日程编排 + 单项明细 (xls)"转成游泳赛事管理系统的 CompetitionPackage JSON.
# 用法: powershell -ExecutionPolicy Bypass -File .\import_competition.ps1
# 输出: SwimmingScoreboard\bin\x64\Release\Database\{比赛名称}.json

param(
    [string]$ExcelPath = 'C:\甘肃游泳比赛2026文件夹\2026甘肃U系列定西站单项明细(2026-06-01).xls',
    [string]$CompetitionName = '2026甘肃省浩沙杯U系列青少年游泳俱乐部联赛（定西站）',
    [string]$StartDate = '2026-06-05',
    [string]$EndDate = '2026-06-07',
    [string]$Location = '定西市全民健身中心',
    [string]$Organizer = '甘肃省游泳协会',
    [string]$HostOrg = '定西市体育运动中心',
    [string]$OutDir = 'C:\代码\swiming_claude\SwimmingScoreboard\bin\x64\Release\Database'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ExcelPath)) { throw "找不到 Excel 文件: $ExcelPath" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# ─── 工具函数 ─────────────────────────────────────────────────
function Parse-EntryTime([string]$s) {
    if (-not $s) { return 0.0 }
    $s = $s.Trim()
    if ($s -eq '' -or $s -eq 'NT') { return 0.0 }
    if ($s -match '^(\d+):(\d+(\.\d+)?)$') {
        return [double]$matches[1] * 60.0 + [double]$matches[2]
    }
    if ($s -match '^(\d+(\.\d+)?)$') { return [double]$s }
    return 0.0
}
function Format-EntryTime([double]$sec) {
    if ($sec -le 0) { return '' }
    if ($sec -ge 60) {
        $m = [int]($sec / 60); $r = $sec - $m * 60
        return ('{0}:{1:00.00}' -f $m, $r)
    }
    return ('{0:0.00}' -f $sec)
}
function Calc-AgeFromBirth([string]$birth, [datetime]$ref) {
    if (-not $birth) { return 0 }
    try {
        $b = [datetime]::Parse($birth)
        $age = $ref.Year - $b.Year
        if ($ref.Month -lt $b.Month -or ($ref.Month -eq $b.Month -and $ref.Day -lt $b.Day)) { $age-- }
        return $age
    } catch { return 0 }
}

# ─── 读 Excel ─────────────────────────────────────────────────
Write-Host "[1/4] 读 Excel: $ExcelPath"
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
$wb = $excel.Workbooks.Open($ExcelPath)
$sh = $wb.Sheets.Item(1)
$rng = $sh.UsedRange
$data = $rng.Value2     # 2D array, 1-indexed
$rowCount = $rng.Rows.Count
$wb.Close($false); $excel.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
[System.GC]::Collect(); [System.GC]::WaitForPendingFinalizers()
Write-Host "    Excel: $rowCount 行 x $($rng.Columns.Count) 列"

# ─── 解析每行 → 构造 Swimmer + 收集 Schedule 信息 ──────────────
Write-Host "[2/4] 解析 + 构造 Swimmer / Schedule"
$startDt = [datetime]::Parse($StartDate)
$swimmers = New-Object System.Collections.Generic.List[object]
$relayRows = New-Object System.Collections.Generic.List[object]
$unitSet = New-Object System.Collections.Generic.HashSet[string]
$eventSet = New-Object System.Collections.Generic.HashSet[string]
$ageSet = New-Object System.Collections.Generic.HashSet[string]
# 项目级聚合: key=(场次|项次)
# 用来排日程 + 算 HeatCount + 项目顺序
$evtAgg = @{}

for ($r = 2; $r -le $rowCount; $r++) {
    $session   = $data[$r, 2]
    $eventSeq  = $data[$r, 3]
    $heatNum   = $data[$r, 5]
    $laneNum   = $data[$r, 6]
    $gender    = "$($data[$r, 7])"
    $age       = "$($data[$r, 8])"
    $dist      = "$($data[$r, 9])"
    $stroke    = "$($data[$r, 10])"
    $stage     = "$($data[$r, 11])"
    $unitFull  = "$($data[$r, 12])"
    $unitShort = "$($data[$r, 13])"
    $name      = "$($data[$r, 14])"
    $idNum     = "$($data[$r, 15])"
    $birth     = "$($data[$r, 16])"
    $phone     = "$($data[$r, 17])"
    $athAge    = "$($data[$r, 18])"
    $entryT    = "$($data[$r, 19])"

    if (-not $session -or -not $name -or -not $dist) { continue }

    # 标准化
    $genderNorm = switch ($gender) { '男子' { '男' } '女子' { '女' } '男女' { '混合' } default { '混合' } }
    # 接力距离 (4X50米) → 4x50米; 项目名拼接
    $isRelay = ($dist -match 'X' -or $dist -match 'x')
    $distNorm = $dist -replace 'X','x'
    $eventName = "$distNorm$stroke"

    [void]$unitSet.Add($unitShort)
    [void]$eventSet.Add($eventName)
    [void]$ageSet.Add($age)

    $entrySec = Parse-EntryTime $entryT
    $entryFmt = if ($entrySec -gt 0) { Format-EntryTime $entrySec } else { $entryT }
    $birthFmt = if ($birth -and $birth -match '\d+-\d+-\d+') { $birth.Substring(0,10) } else { $birth }
    $calcAge = if ($athAge -and $athAge -match '^\d+$') { [int]$athAge } else { Calc-AgeFromBirth $birthFmt $startDt }

    # 聚合到 evtAgg, key=(场次)|(项次)
    $aggKey = "$session|$eventSeq"
    if (-not $evtAgg.ContainsKey($aggKey)) {
        $evtAgg[$aggKey] = @{
            Session = [int]$session
            EventSeq = [int]$eventSeq
            Gender = $genderNorm
            AgeGroup = $age
            EventName = $eventName
            Stage = $stage
            IsRelay = $isRelay
            MaxHeat = 0
            EntryCount = 0
        }
    }
    if ([int]$heatNum -gt $evtAgg[$aggKey].MaxHeat) { $evtAgg[$aggKey].MaxHeat = [int]$heatNum }
    $evtAgg[$aggKey].EntryCount++

    $swimmer = [ordered]@{
        Name = $name
        BibNumber = ''
        BirthDate = $birthFmt
        Age = $calcAge
        Gender = $genderNorm
        Country = $unitShort
        CountryShort = $unitShort
        IDNumber = $idNum
        Phone = $phone
        Notes = if ($isRelay) { '接力队员' } else { '' }
        LegLabel = ''
        CSANumber = ''
        FINANumber = $null
        HealthCertDate = $null
        EventName = $eventName
        CurrentStage = $stage
        Heat = [int]$heatNum
        Lane = [int]$laneNum
        EntryTime = $entryFmt
        EntryTimeSeconds = $entrySec
        IsQualified = $true
        Status = ''
        CurrentRank = 0
        AgeCategory = $age
        Results = @()
        StageAssignments = @{
            $stage = @{
                Stage = $stage
                Heat = [int]$heatNum
                Lane = [int]$laneNum
                EntryTimeSeconds = $entrySec
                EntryTime = $entryFmt
            }
        }
    }
    $swimmers.Add([pscustomobject]$swimmer)
}
Write-Host "    Swimmer 记录 $($swimmers.Count) 条, 项目 $($evtAgg.Count) 项, 单位 $($unitSet.Count), 组别 $($ageSet.Count)"

# ─── 排日程: 按 (Session, EventSeq) 排序 + 嵌入 PDF 详细开赛时间 ─
Write-Host "[3/4] 生成 Schedule + 单位/组别/事件 列表"
$sessionMeta = @{
    1 = @{ Date = '2026-06-06'; Name = '第1场（2026-06-06上午）'
        Times = @{ 1='09:00';2='09:05';3='09:10';4='09:22';5='09:37';6='09:40';7='09:43';8='09:46';9='09:52';10='10:04';
                  11='10:13';12='10:19';13='10:25';14='10:28';15='10:31';16='10:37';17='10:43';18='10:46';19='10:50';20='10:54';
                  21='11:06';22='11:14';23='11:18';24='11:26';25='11:34';26='11:38';27='11:42';28='11:47' } }
    2 = @{ Date = '2026-06-06'; Name = '第2场（2026-06-06下午）'
        Times = @{ 1='14:00';2='14:07';3='14:12';4='14:21';5='14:33';6='14:39';7='14:42';8='14:54';9='15:06';10='15:09';
                  11='15:12';12='15:15';13='15:24';14='15:30';15='15:36';16='15:39';17='15:43';18='15:51';19='16:03';20='16:07';
                  21='16:15';22='16:23';23='16:27';24='16:31';25='16:41';26='16:51' } }
    3 = @{ Date = '2026-06-07'; Name = '第3场（2026-06-07上午）'
        Times = @{ 1='09:00';2='09:05';3='09:08';4='09:14';5='09:29';6='09:41';7='09:47';8='09:56';9='10:02';10='10:05';
                  11='10:08';12='10:14';13='10:23';14='10:26';15='10:29';16='10:38';17='10:44';18='10:52';19='11:00';20='11:04';
                  21='11:12';22='11:20';23='11:24';24='11:28';25='11:32';26='11:36';27='11:41';28='11:46' } }
}
# 先转 array, 再用 ScriptBlock 排序 (hashtable 用 Expression 字符串排序失败)
$schedule = New-Object System.Collections.Generic.List[object]
$sortedAgg = $evtAgg.Values | Sort-Object @{Expression={[int]$_.Session}}, @{Expression={[int]$_.EventSeq}}
foreach ($it in $sortedAgg) {
    $meta = $sessionMeta[$it.Session]
    # Times 字典的 key 是 int (1,2,3...), 直接用 int 索引
    $time = if ($meta.Times.ContainsKey([int]$it.EventSeq)) { $meta.Times[[int]$it.EventSeq] } else { '09:00' }
    $sched = [ordered]@{
        SessionNumber = $it.Session
        SessionName = $meta.Name
        Date = $meta.Date
        Time = $time
        AgeGroup = $it.AgeGroup
        EventName = $it.EventName
        Gender = $it.Gender
        Stage = $it.Stage
        HeatCount = $it.MaxHeat
        IsRelay = $it.IsRelay
        DisplayText = "$($it.Gender) $($it.EventName) $($it.Stage)"
    }
    $schedule.Add([pscustomobject]$sched)
}
Write-Host "    Schedule $($schedule.Count) 条 (按场次+项次排序, 每项嵌入 PDF 详细时间)"

# ─── 组别 + 项目 + 单位 ──────────────────────────────────────────
$ageGroupDefs = @(
    @{Name='6岁组';    MinAge=6;  MaxAge=6 },
    @{Name='7岁组';    MinAge=7;  MaxAge=7 },
    @{Name='8-9岁组';  MinAge=8;  MaxAge=9 },
    @{Name='10-11岁组';MinAge=10; MaxAge=11 },
    @{Name='12-13岁组';MinAge=12; MaxAge=13 },
    @{Name='14-15岁组';MinAge=14; MaxAge=15 },
    @{Name='16-18岁组';MinAge=16; MaxAge=18 }
)
$ageGroups = $ageGroupDefs | ForEach-Object { [pscustomobject]@{ Name = $_.Name; MinAge = $_.MinAge; MaxAge = $_.MaxAge } }

# 项目排序: 按距离 → 泳姿
$eventOrder = '50米自由泳','100米自由泳','200米自由泳','400米自由泳',
              '50米仰泳','100米仰泳','200米仰泳',
              '50米蛙泳','100米蛙泳','200米蛙泳',
              '50米蝶泳','100米蝶泳','200米蝶泳',
              '100米个人混合泳','200米混合泳','200米个人混合泳','400米混合泳','400米个人混合泳',
              '4x50米自由泳接力','4x50米混合泳接力','4x100米自由泳接力','4x100米混合泳接力'
$events = New-Object System.Collections.Generic.List[string]
foreach ($e in $eventOrder) { if ($eventSet.Contains($e)) { $events.Add($e) } }
foreach ($e in ($eventSet | Sort-Object)) { if (-not $events.Contains($e)) { $events.Add($e) } }

# 单位 (units): 用 unitShort, 没有领队信息
$units = New-Object System.Collections.Generic.List[object]
foreach ($u in ($unitSet | Sort-Object)) {
    $units.Add([pscustomobject]@{
        Name = $u
        ShortName = $u
        FullName = $u
        Leader = ''
        Coach = ''
        Phone = ''
    })
}

$heatCounts = @('1组','2组','3组','4组','5组','6组','7组','8组')

# ─── 构建 CompetitionPackage ─────────────────────────────────
Write-Host "[4/4] 写 JSON"
$package = [ordered]@{
    CompetitionName = $CompetitionName
    CompetitionMode = 'domestic'
    StartDate = $StartDate
    EndDate = $EndDate
    Location = $Location
    PoolLength = 50
    LaneCount = 10
    Organizer = $Organizer
    Host = $HostOrg
    TechnicalDelegate = ''
    Referee = ''
    Starter = ''
    Arbiter = ''
    ChiefJudge = ''
    Officials = @()
    Swimmers = $swimmers
    RelayTeams = @()
    Records = @()
    TeamScores = @()
    Schedule = $schedule
    Events = $events
    AgeGroups = $ageGroups
    Genders = @('男','女','混合')
    Stages = @('预赛','半决赛','决赛')
    HeatCounts = $heatCounts
    BibRanges = @()
    Units = $units
    StaffList = @()
    WizardDraft = $null
    LaneCloseSettings = $null
    DisputeLog = @{}
    ProgramBook = $null
    ResultBook = $null
    DisplayRecordLabel = ''
    DisplayRecordTypeName = ''
    DisplayRecordOptions = @()
    ConfirmedHeats = @()
    ScoringConfig = $null
    DurationConfig = $null
}

$outFile = Join-Path $OutDir "$CompetitionName.json"
$json = $package | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($outFile, $json, [System.Text.UTF8Encoding]::new($false))

$sz = (Get-Item $outFile).Length
Write-Host ""
Write-Host "✓ 完成. 比赛档案: $outFile ($('{0:N0}' -f $sz) 字节)"
Write-Host "  Swimmer 总数: $($swimmers.Count)"
Write-Host "  Schedule 项数: $($schedule.Count) (3 场)"
Write-Host "  参赛单位: $($units.Count) 个"
Write-Host "  比赛项目: $($events.Count) 项"
Write-Host ""
Write-Host "下次启动 SwimmingScoreboard.exe → 自动加载该比赛档案 (last_competition.txt 也已被更新)"

# 把 last_competition.txt 也写一下, 让主程序启动后自动加载
$lastFile = Join-Path (Split-Path $OutDir -Parent) 'last_competition.txt'
[System.IO.File]::WriteAllText($lastFile, $CompetitionName, [System.Text.UTF8Encoding]::new($false))
