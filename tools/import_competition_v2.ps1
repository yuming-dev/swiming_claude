# 2026-06-04 比赛档案导入工具 v2 (新 62 列竞赛秩序格式)
# 把 3 个 "竞赛秩序(0XOther).xls" 文件合并转成 CompetitionPackage JSON.
# 用法: powershell -ExecutionPolicy Bypass -File .\import_competition_v2.ps1
# 输出: SwimmingScoreboard\bin\x64\Release\Database\{比赛名称}.json

param(
    [string[]]$ExcelPaths = @(
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(01Other).xls',
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(02Other).xls',
        'C:\2026年5月甘肃定西游泳比赛文件夹\比赛定西\2026甘肃U系列定西站竞赛秩序(03Other).xls'
    ),
    [string]$CompetitionName = '2026甘肃省浩沙杯U系列青少年游泳俱乐部联赛（定西站）',
    [string]$StartDate = '2026-06-06',
    [string]$EndDate = '2026-06-07',
    [string]$Location = '定西市全民健身中心',
    [string]$Organizer = '甘肃省游泳协会',
    [string]$HostOrg = '定西市体育运动中心',
    [string]$OutDir = 'C:\代码\swiming_claude\SwimmingScoreboard\bin\x64\Release\Database'
)

$ErrorActionPreference = 'Stop'

foreach ($p in $ExcelPaths) { if (-not (Test-Path $p)) { throw "找不到 Excel: $p" } }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# ─── 工具函数 ─────────────────────────────────────────────────
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
function Calc-AgeFromBirth([string]$birth, [datetime]$ref) {
    if (-not $birth) { return 0 }
    try {
        $b = [datetime]::Parse($birth)
        $age = $ref.Year - $b.Year
        if ($ref.Month -lt $b.Month -or ($ref.Month -eq $b.Month -and $ref.Day -lt $b.Day)) { $age-- }
        return $age
    } catch { return 0 }
}
function Normalize-Gender([string]$g) {
    switch ($g) { '男子' { '男' } '女子' { '女' } '男女' { '男女' } default { if ($g) { $g } else { '混合' } } }
}
# 2026-06-05 schedule.Gender 应从 title (col 4) 解析, 不是 col 7 (col 7 是个人性别).
#   "男女16-18岁组..." → '男女';  "男子8-9岁组..." → '男';  "女子7岁组..." → '女'
function Parse-TitleGender([string]$title) {
    if (-not $title) { return $null }
    if ($title.StartsWith('男女')) { return '男女' }
    if ($title.StartsWith('男子')) { return '男' }
    if ($title.StartsWith('女子')) { return '女' }
    return $null
}

# ─── 读 Excel + 解析 ─────────────────────────────────────────
$startDt = [datetime]::Parse($StartDate)
$swimmers = New-Object System.Collections.Generic.List[object]
$relayTeams = New-Object System.Collections.Generic.List[object]
$relayCounter = 0
$unitSet = New-Object System.Collections.Generic.HashSet[string]
$eventSet = New-Object System.Collections.Generic.HashSet[string]
$ageSet = New-Object System.Collections.Generic.HashSet[string]
$evtAgg = @{}                              # key=Session|EventSeq → 项目聚合 (含 MaxHeat)
$sessionDateMap = @{}                      # SessionN → Date (从 Excel col 1 拿)
$recordsList = New-Object System.Collections.Generic.List[object]
$recordSeen = New-Object System.Collections.Generic.HashSet[string]   # 去重 (项目|性别|组|名称)

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false

try {
    foreach ($xlsPath in $ExcelPaths) {
        Write-Host "[1/4] 读 Excel: $(Split-Path $xlsPath -Leaf)"
        $wb = $excel.Workbooks.Open($xlsPath, 0, $true)
        $sh = $wb.Sheets.Item(1)
        $rng = $sh.UsedRange
        $data = $rng.Value2
        $rowCount = $rng.Rows.Count
        $wb.Close($false)
        Write-Host "    $rowCount 行 x $($rng.Columns.Count) 列"

        for ($r = 2; $r -le $rowCount; $r++) {
            $date     = "$($data[$r, 1])"
            $session  = $data[$r, 2]
            $eventSeq = $data[$r, 3]
            $title    = "$($data[$r, 4])"
            $gender   = "$($data[$r, 7])"
            $age      = "$($data[$r, 8])"
            $dist     = "$($data[$r, 10])"
            $stroke   = "$($data[$r, 11])"
            $stage    = "$($data[$r, 12])"
            $heatNum  = $data[$r, 13]
            $laneNum  = $data[$r, 14]
            $unitFull = "$($data[$r, 16])"
            $unitShort= "$($data[$r, 17])"
            $entryT   = "$($data[$r, 19])"

            # 过滤: 性别 / 距离 / 泳式 都空的行 = 标题或汇总行
            if (-not $session -or -not $dist -or -not $stroke) { continue }
            # 性别 / 组别 必须有 (否则是分隔/汇总行, e.g. R2 在新格式里只有 'title' 字段, 其它都是空)
            if (-not $gender -or -not $age) { continue }

            $genderNorm = Normalize-Gender $gender
            $distNorm   = $dist -replace 'X','x'
            $eventName  = "$distNorm$stroke"
            $isRelay    = ($dist -match '[Xx×]') -or ($title -like '*接力*')

            [void]$unitSet.Add($unitShort)
            [void]$eventSet.Add($eventName)
            [void]$ageSet.Add($age)
            if (-not $sessionDateMap.ContainsKey([int]$session)) { $sessionDateMap[[int]$session] = $date }

            $entrySec = Parse-EntryTime $entryT
            $entryFmt = if ($entrySec -gt 0) { Format-EntryTime $entrySec } else { '' }

            # 2026-06-05 schedule-level gender: 优先从 title 解析 (= 男女并项 用), 否则 用 col 7 单人性别
            $schedGender = Parse-TitleGender $title
            if (-not $schedGender) { $schedGender = $genderNorm }
            $aggKey = "$session|$eventSeq"
            if (-not $evtAgg.ContainsKey($aggKey)) {
                $evtAgg[$aggKey] = @{
                    Session = [int]$session; EventSeq = [int]$eventSeq
                    Gender = $schedGender; AgeGroup = $age
                    EventName = $eventName; Stage = $stage
                    IsRelay = $isRelay; MaxHeat = 0
                }
            }
            if ([int]$heatNum -gt $evtAgg[$aggKey].MaxHeat) { $evtAgg[$aggKey].MaxHeat = [int]$heatNum }

            # ─── 运动员 / 接力队 ──────────────────────────────────────
            if ($isRelay) {
                # 接力: 4 棒姓名 cols 22/30/38/46; 出生 24/32/40/48; 比赛号 27/35/43/51; 分属单位 28/36/44/52
                $legBlocks = @(
                    @{ NameCol=22; BirthCol=24; IdCol=25; BibCol=27; UnitCol=28; CoachCol=29 },
                    @{ NameCol=30; BirthCol=32; IdCol=33; BibCol=35; UnitCol=36; CoachCol=37 },
                    @{ NameCol=38; BirthCol=40; IdCol=41; BibCol=43; UnitCol=44; CoachCol=45 },
                    @{ NameCol=46; BirthCol=48; IdCol=49; BibCol=51; UnitCol=52; CoachCol=53 }
                )
                $legNames = @()
                $legObjs = New-Object System.Collections.Generic.List[object]
                for ($li = 0; $li -lt 4; $li++) {
                    $b = $legBlocks[$li]
                    $nm = "$($data[$r, $b.NameCol])".Trim()
                    if (-not $nm) { continue }
                    $legNames += $nm
                    $brth = "$($data[$r, $b.BirthCol])".Trim()
                    $brthFmt = if ($brth -and $brth -match '\d+-\d+-\d+') { $brth.Substring(0,[Math]::Min(10,$brth.Length)) } else { $brth }
                    $legObjs.Add([pscustomobject]@{
                        LegOrder = $li + 1
                        SwimmerName = $nm
                        SwimmerBibNumber = "$($data[$r, $b.BibCol])".Trim()
                        SwimmerIDNumber = "$($data[$r, $b.IdCol])".Trim()
                        SwimmerBirthDate = $brthFmt
                        ReactionTime = 0.0
                        LegTimeSeconds = 0.0
                    })
                }
                # 不够 4 棒补齐空白
                for ($li = $legObjs.Count; $li -lt 4; $li++) {
                    $legObjs.Add([pscustomobject]@{
                        LegOrder = $li + 1; SwimmerName = ''
                        SwimmerBibNumber = ''; SwimmerIDNumber = ''; SwimmerBirthDate = ''
                        ReactionTime = 0.0; LegTimeSeconds = 0.0
                    })
                }
                $legNameStr = $legNames -join ','
                if (-not $legNameStr) { $legNameStr = "$unitShort 队" }

                $relayCounter++
                $teamBib = ('R' + $relayCounter.ToString('D3'))
                $teamName = if ($unitShort) { $unitShort } else { $unitFull }

                $swimmers.Add([pscustomobject]([ordered]@{
                    Name = $teamName; BibNumber = $teamBib; BirthDate = ''; Age = 0
                    Gender = $genderNorm; Country = $teamName; CountryShort = $teamName
                    IDNumber = ''; Phone = ''
                    Notes = "接力队 棒次:$legNameStr"
                    LegLabel = ''; CSANumber = ''; FINANumber = $null; HealthCertDate = $null
                    EventName = $eventName; CurrentStage = $stage
                    Heat = [int]$heatNum; Lane = [int]$laneNum
                    EntryTime = $entryFmt; EntryTimeSeconds = $entrySec
                    IsQualified = $true; Status = ''; CurrentRank = 0
                    AgeCategory = $age; Results = @()
                    StageAssignments = @{ $stage = @{ Stage = $stage; Heat = [int]$heatNum; Lane = [int]$laneNum;
                                                     EntryTimeSeconds = $entrySec; EntryTime = $entryFmt } }
                }))
                $relayTeams.Add([pscustomobject]([ordered]@{
                    TeamName = $teamName; EventName = $eventName; Gender = $genderNorm; AgeGroup = $age
                    Country = $teamName; Members = @(); Legs = $legObjs.ToArray()
                    EntryTime = $entryFmt; EntryTimeSeconds = $entrySec
                    Heat = [int]$heatNum; Lane = [int]$laneNum
                    FinalTime = 0.0; Rank = 0; Stage = $stage; Status = ''; LegSplits = @()
                    AgeCategoryPending = $false
                }))
            } else {
                # 个人: 只用 姓名1 块 (cols 22-29)
                $name = "$($data[$r, 22])".Trim()
                if (-not $name) { continue }
                $bib    = "$($data[$r, 27])".Trim()
                $idNum  = "$($data[$r, 25])".Trim()
                $birth  = "$($data[$r, 24])".Trim()
                $birthFmt = if ($birth -and $birth -match '\d+-\d+-\d+') { $birth.Substring(0,[Math]::Min(10,$birth.Length)) } else { $birth }
                $calcAge = Calc-AgeFromBirth $birthFmt $startDt
                $swimmers.Add([pscustomobject]([ordered]@{
                    Name = $name; BibNumber = $bib; BirthDate = $birthFmt; Age = $calcAge
                    Gender = $genderNorm; Country = $unitShort; CountryShort = $unitShort
                    IDNumber = $idNum; Phone = ''
                    Notes = ''; LegLabel = ''; CSANumber = ''; FINANumber = $null; HealthCertDate = $null
                    EventName = $eventName; CurrentStage = $stage
                    Heat = [int]$heatNum; Lane = [int]$laneNum
                    EntryTime = $entryFmt; EntryTimeSeconds = $entrySec
                    IsQualified = $true; Status = ''; CurrentRank = 0
                    AgeCategory = $age; Results = @()
                    StageAssignments = @{ $stage = @{ Stage = $stage; Heat = [int]$heatNum; Lane = [int]$laneNum;
                                                     EntryTimeSeconds = $entrySec; EntryTime = $entryFmt } }
                }))
            }

            # ─── 记录 cols 54-62 (3 slot, 每 slot 3 字段: 标识/名称/成绩) ──
            for ($rs = 0; $rs -lt 3; $rs++) {
                $labelCol = 54 + $rs * 3
                $nameCol  = 55 + $rs * 3
                $timeCol  = 56 + $rs * 3
                $rlbl = "$($data[$r, $labelCol])".Trim()
                $rnm  = "$($data[$r, $nameCol])".Trim()
                $rtm  = "$($data[$r, $timeCol])".Trim()
                if (-not $rtm -or -not $rnm) { continue }
                $rkey = "$eventName|$genderNorm|$age|$rlbl|$rnm"
                if ($recordSeen.Add($rkey)) {
                    $recSec = Parse-EntryTime $rtm
                    $recordsList.Add([pscustomobject]@{
                        EventName = $eventName; Gender = $genderNorm; AgeGroup = $age
                        RecordType = $rlbl
                        HolderName = $rnm; HolderCountry = ''
                        Time = $recSec; Date = ''; Location = ''
                    })
                }
            }
        }
        Write-Host "    累计: Swimmer $($swimmers.Count), 项目聚合 $($evtAgg.Count), 单位 $($unitSet.Count), 组别 $($ageSet.Count), 纪录 $($recordsList.Count)"
    }
} finally {
    $excel.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
    [System.GC]::Collect(); [System.GC]::WaitForPendingFinalizers()
}

# ─── 排日程 ─────────────────────────────────────────────────
Write-Host "[2/4] 生成 Schedule"
$sessionTimeStart = @{ 1 = '09:00'; 2 = '14:00'; 3 = '09:00' }   # 兜底 (Excel 没有具体每项时间, 用场次默认)
$schedule = New-Object System.Collections.Generic.List[object]
$sortedAgg = $evtAgg.Values | Sort-Object @{Expression={[int]$_.Session}}, @{Expression={[int]$_.EventSeq}}
foreach ($it in $sortedAgg) {
    $sessDate = if ($sessionDateMap.ContainsKey([int]$it.Session)) { $sessionDateMap[[int]$it.Session] } else { $StartDate }
    $sessTime = if ($sessionTimeStart.ContainsKey([int]$it.Session)) { $sessionTimeStart[[int]$it.Session] } else { '09:00' }
    $sessName = "第$($it.Session)场（$sessDate）"
    $schedule.Add([pscustomobject]([ordered]@{
        SessionNumber = $it.Session; SessionName = $sessName
        Date = $sessDate; Time = $sessTime
        AgeGroup = $it.AgeGroup; EventName = $it.EventName; Gender = $it.Gender
        Stage = $it.Stage; HeatCount = $it.MaxHeat; IsRelay = $it.IsRelay
        DisplayText = "$($it.Gender) $($it.EventName) $($it.Stage)"
    }))
}
Write-Host "    Schedule $($schedule.Count) 条 ($($sessionDateMap.Count) 场)"

# ─── 组别 + 项目 + 单位 ──────────────────────────────────────
Write-Host "[3/4] 整理 组别 / 项目 / 单位"
$ageGroupDefs = @(
    @{Name='6岁组';      MinAge=6;  MaxAge=6 },
    @{Name='7岁组';      MinAge=7;  MaxAge=7 },
    @{Name='8-9岁组';    MinAge=8;  MaxAge=9 },
    @{Name='10-11岁组';  MinAge=10; MaxAge=11 },
    @{Name='12-13岁组';  MinAge=12; MaxAge=13 },
    @{Name='14-15岁组';  MinAge=14; MaxAge=15 },
    @{Name='16-18岁组';  MinAge=16; MaxAge=18 }
)
$ageGroups = $ageGroupDefs | ForEach-Object { [pscustomobject]@{ Name = $_.Name; MinAge = $_.MinAge; MaxAge = $_.MaxAge } }
# Excel 出现但默认表没有的组别, 追加 (按字符串)
foreach ($ag in ($ageSet | Sort-Object)) {
    if (-not ($ageGroups | Where-Object { $_.Name -eq $ag })) {
        $m = [regex]::Match($ag, '(\d+)\D*(\d+)?')
        $mn = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
        $mx = if ($m.Success -and $m.Groups[2].Value) { [int]$m.Groups[2].Value } else { $mn }
        $ageGroups += [pscustomobject]@{ Name = $ag; MinAge = $mn; MaxAge = $mx }
    }
}

# 项目排序
$eventOrder = '50米自由泳','100米自由泳','200米自由泳','400米自由泳','800米自由泳','1500米自由泳',
              '50米仰泳','100米仰泳','200米仰泳','50米蛙泳','100米蛙泳','200米蛙泳',
              '50米蝶泳','100米蝶泳','200米蝶泳',
              '100米个人混合泳','200米个人混合泳','400米个人混合泳',
              '4x50米自由泳接力','4x50米混合泳接力',
              '4x100米自由泳接力','4x100米混合泳接力','4x200米自由泳接力'
$events = New-Object System.Collections.Generic.List[string]
foreach ($e in $eventOrder) { if ($eventSet.Contains($e)) { $events.Add($e) } }
foreach ($e in ($eventSet | Sort-Object)) { if (-not $events.Contains($e)) { $events.Add($e) } }

$units = New-Object System.Collections.Generic.List[object]
foreach ($u in ($unitSet | Sort-Object)) {
    $units.Add([pscustomobject]@{ Name = $u; ShortName = $u; FullName = $u; Leader = ''; Coach = ''; Phone = '' })
}

$heatCounts = @('1组','2组','3组','4组','5组','6组','7组','8组','9组','10组')

# ─── 构建 CompetitionPackage ─────────────────────────────────
Write-Host "[4/4] 写 JSON"
$package = [ordered]@{
    CompetitionName = $CompetitionName; CompetitionMode = 'domestic'; CompetitionRule = 'U系列青少年游泳比赛'
    StartDate = $StartDate; EndDate = $EndDate; Location = $Location
    PoolLength = 50; LaneCount = 10
    Organizer = $Organizer; Host = $HostOrg
    TechnicalDelegate = ''; Referee = ''; Starter = ''; Arbiter = ''; ChiefJudge = ''
    Officials = @()
    Swimmers = $swimmers; RelayTeams = $relayTeams
    Records = $recordsList.ToArray()
    TeamScores = @()
    Schedule = $schedule; Events = $events; AgeGroups = $ageGroups
    Genders = @('男','女','混合','男女'); Stages = @('预赛','半决赛','决赛')
    HeatCounts = $heatCounts; BibRanges = @()
    Units = $units; StaffList = @()
    WizardDraft = $null; LaneCloseSettings = $null; DisputeLog = @{}
    ProgramBook = $null; ResultBook = $null
    DisplayRecordLabel = ''; DisplayRecordTypeName = ''; DisplayRecordOptions = @()
    ConfirmedHeats = @(); ScoringConfig = $null; DurationConfig = $null
}

$outFile = Join-Path $OutDir "$CompetitionName.json"
$json = $package | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($outFile, $json, [System.Text.UTF8Encoding]::new($false))

$sz = (Get-Item $outFile).Length
Write-Host ""
Write-Host "✓ 完成. 比赛档案: $outFile ($('{0:N0}' -f $sz) 字节)"
Write-Host "  Swimmer (含接力队条目): $($swimmers.Count)"
Write-Host "  接力队 RelayTeams: $($relayTeams.Count)"
Write-Host "  Schedule 项数: $($schedule.Count) ($($sessionDateMap.Count) 场)"
Write-Host "  参赛单位: $($units.Count)"
Write-Host "  比赛项目: $($events.Count)"
Write-Host "  组别: $($ageGroups.Count)"
Write-Host "  纪录: $($recordsList.Count)"
Write-Host ""
Write-Host "下次启动 SwimmingScoreboard.exe → 自动加载该比赛档案"

# 写 last_competition.txt
$lastFile = Join-Path (Split-Path $OutDir -Parent) 'last_competition.txt'
[System.IO.File]::WriteAllText($lastFile, $CompetitionName, [System.Text.UTF8Encoding]::new($false))
