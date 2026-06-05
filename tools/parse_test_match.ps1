# 2026-06-04 解析定西测试赛分组名单 (4 个事件, 40 个泳道条目)
# 数据来源: 微信 OCR + 人工校对; 原 OCR 顺序是道 9 → 0, 这里按 lane 0 → 9 排列
# 输出: Excel (.xlsx) + CompetitionPackage JSON

$ErrorActionPreference = 'Stop'

$CompetitionName = '2026年甘肃省浩沙FAFA杯U系列青少年游泳俱乐部联赛（定西站）实习场'
$Date = '2026-06-05'
$Time = '16:00'
$Location = '实习场'
$OutDir = 'C:\代码\甘肃定西游泳文件夹'

# ─── 事件 + 道次 数据 (lane 0 → 9, 按校对后) ────────────────────────
# 字段: lane, name, gender(男/女), team, ageGroup
$events = @(
    @{
        Session = 1; EventSeq = 1
        Title = '女子12-13岁组 50米蝶泳 决赛'
        Gender = '女'; AgeGroup = '12-13岁组'; Distance = '50米'; Stroke = '蝶泳'; Stage = '决赛'
        Heat = 1
        Lanes = @(
            @{lane=0; name='王紫霏'; gender='女'; team='平凉一飞队';   ageGroup='12-13岁组'},
            @{lane=1; name='鲁锦华'; gender='女'; team='天体游泳队';   ageGroup='12-13岁组'},
            @{lane=2; name='郑美源'; gender='女'; team='天体游泳队';   ageGroup='12-13岁组'},
            @{lane=3; name='冯诗雅'; gender='女'; team='定西爱尚';     ageGroup='12-13岁组'},
            @{lane=4; name='陈嘉熙'; gender='女'; team='北健体育';     ageGroup='12-13岁组'},
            @{lane=5; name='王禹茜'; gender='女'; team='北健体育';     ageGroup='12-13岁组'},
            @{lane=6; name='刘欣怡'; gender='女'; team='临夏燃星';     ageGroup='12-13岁组'},
            @{lane=7; name='李梓煊'; gender='女'; team='天体游泳队';   ageGroup='12-13岁组'},
            @{lane=8; name='何梦涵'; gender='女'; team='袋鼠体育';     ageGroup='12-13岁组'},
            @{lane=9; name='伍启恩'; gender='女'; team='平凉一飞队';   ageGroup='12-13岁组'}
        )
    },
    @{
        Session = 1; EventSeq = 2
        Title = '男子8-9岁组 100米蛙泳 决赛'
        Gender = '男'; AgeGroup = '8-9岁组'; Distance = '100米'; Stroke = '蛙泳'; Stage = '决赛'
        Heat = 1
        Lanes = @(
            @{lane=0; name='陈楷睿';     gender='男'; team='天体游泳队'; ageGroup='8-9岁组'},
            @{lane=1; name='张城溥';     gender='男'; team='袋鼠体育';   ageGroup='8-9岁组'},
            @{lane=2; name='郭正润生';   gender='男'; team='兰州奥体';   ageGroup='8-9岁组'},
            @{lane=3; name='张博翔';     gender='男'; team='北健体育';   ageGroup='8-9岁组'},
            @{lane=4; name='李有铎';     gender='男'; team='北健体育';   ageGroup='8-9岁组'},
            @{lane=5; name='胥隽哲';     gender='男'; team='平凉一飞队'; ageGroup='8-9岁组'},
            @{lane=6; name='胡继平';     gender='男'; team='兰州奥体';   ageGroup='8-9岁组'},
            @{lane=7; name='李锦泽';     gender='男'; team='陇南海豚';   ageGroup='8-9岁组'},
            @{lane=8; name='杜景棋';     gender='男'; team='平凉一飞队'; ageGroup='8-9岁组'},
            @{lane=9; name='段玮柏';     gender='男'; team='自由渡';     ageGroup='8-9岁组'}
        )
    },
    @{
        Session = 1; EventSeq = 3
        Title = '男女12-15岁组 200米混合泳 决赛'
        Gender = '男女'; AgeGroup = '12-15岁组'; Distance = '200米'; Stroke = '混合泳'; Stage = '决赛'
        Heat = 1
        Lanes = @(
            @{lane=0; name='董诗涵';     gender='女'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=1; name='孙芝涵';     gender='女'; team='天体游泳队'; ageGroup='12-13岁组'},
            @{lane=2; name='张铎嘉';     gender='男'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=3; name='魏旭彤';     gender='男'; team='定西爱尚';   ageGroup='14-15岁组'},
            @{lane=4; name='李锦航';     gender='男'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=5; name='王之鉴';     gender='男'; team='天体游泳队'; ageGroup='14-15岁组'},
            @{lane=6; name='罗安轩';     gender='男'; team='天体游泳队'; ageGroup='12-13岁组'},
            @{lane=7; name='杨睿媛';     gender='女'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=8; name='王依宁';     gender='女'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=9; name='王紫霏';     gender='女'; team='平凉一飞队'; ageGroup='12-13岁组'}
        )
    },
    @{
        Session = 1; EventSeq = 4
        Title = '男女12-15岁组 200米仰泳 决赛'
        Gender = '男女'; AgeGroup = '12-15岁组'; Distance = '200米'; Stroke = '仰泳'; Stage = '决赛'
        Heat = 1
        Lanes = @(
            @{lane=0; name='马一丹';     gender='女'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=1; name='伍启恩';     gender='女'; team='平凉一飞队'; ageGroup='12-13岁组'},
            @{lane=2; name='杨睿媛';     gender='女'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=3; name='孙芝涵';     gender='女'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=4; name='魏均晓';     gender='男'; team='白银沫奇';   ageGroup='14-15岁组'},
            @{lane=5; name='魏旭彤';     gender='男'; team='定西爱尚';   ageGroup='14-15岁组'},
            @{lane=6; name='董诗涵';     gender='女'; team='陇南海豚';   ageGroup='12-13岁组'},
            @{lane=7; name='岳佳潞';     gender='女'; team='袋鼠体育';   ageGroup='14-15岁组'},
            @{lane=8; name='廖祯馨';     gender='女'; team='安宁文体馆'; ageGroup='12-13岁组'},
            @{lane=9; name='郑美源';     gender='女'; team='天体游泳队'; ageGroup='12-13岁组'}
        )
    }
)

# ─── 输出 Excel (xlsx, 用 Excel COM) ────────────────────────
Write-Host "[1/2] 输出 Excel ..."
$xlsxPath = Join-Path $OutDir "$CompetitionName.xlsx"
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false; $excel.DisplayAlerts = $false
$wb = $excel.Workbooks.Add()
$sh = $wb.Worksheets.Item(1)
$sh.Name = '分组名单'

# 表头
$headers = @('比赛日期','场次','项次','项名','性别','组别','距离','泳式','赛次','组次','泳道','代表队','姓名','运动员性别(G/B)','运动员组别')
for ($c = 0; $c -lt $headers.Count; $c++) { $sh.Cells.Item(1, $c+1) = $headers[$c] }

$row = 2
foreach ($ev in $events) {
    foreach ($ln in $ev.Lanes) {
        $sh.Cells.Item($row, 1)  = $Date
        $sh.Cells.Item($row, 2)  = $ev.Session
        $sh.Cells.Item($row, 3)  = $ev.EventSeq
        $sh.Cells.Item($row, 4)  = $ev.Title
        $sh.Cells.Item($row, 5)  = $ev.Gender
        $sh.Cells.Item($row, 6)  = $ev.AgeGroup
        $sh.Cells.Item($row, 7)  = $ev.Distance
        $sh.Cells.Item($row, 8)  = $ev.Stroke
        $sh.Cells.Item($row, 9)  = $ev.Stage
        $sh.Cells.Item($row, 10) = $ev.Heat
        $sh.Cells.Item($row, 11) = $ln.lane
        $sh.Cells.Item($row, 12) = $ln.team
        $sh.Cells.Item($row, 13) = $ln.name
        $sh.Cells.Item($row, 14) = $(if ($ln.gender -eq '男') { 'B' } else { 'G' })
        $sh.Cells.Item($row, 15) = $ln.ageGroup
        $row++
    }
}
$sh.Columns.AutoFit() | Out-Null
$wb.SaveAs($xlsxPath, 51)   # 51 = xlsx
$wb.Close($false)
$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
Write-Host "    Excel 输出: $xlsxPath"

# ─── 输出 JSON (CompetitionPackage 格式) ────────────────────────
Write-Host "[2/2] 输出 JSON ..."
$swimmers = New-Object System.Collections.Generic.List[object]
$unitSet = New-Object System.Collections.Generic.HashSet[string]
$eventSet = New-Object System.Collections.Generic.HashSet[string]
$ageSet = New-Object System.Collections.Generic.HashSet[string]
$schedule = New-Object System.Collections.Generic.List[object]

foreach ($ev in $events) {
    $eventName = $ev.Distance + $ev.Stroke
    [void]$eventSet.Add($eventName)
    [void]$ageSet.Add($ev.AgeGroup)
    $schedule.Add([pscustomobject]([ordered]@{
        SessionNumber = $ev.Session
        SessionName = "第$($ev.Session)场（$Date $Time）"
        Date = $Date; Time = $Time
        AgeGroup = $ev.AgeGroup; EventName = $eventName; Gender = $ev.Gender
        Stage = $ev.Stage; HeatCount = $ev.Heat; IsRelay = $false
        DisplayText = "$($ev.Gender) $($ev.AgeGroup) $eventName $($ev.Stage)"
    }))
    foreach ($ln in $ev.Lanes) {
        [void]$unitSet.Add($ln.team)
        [void]$ageSet.Add($ln.ageGroup)
        $swimmers.Add([pscustomobject]([ordered]@{
            Name = $ln.name; BibNumber = ''; BirthDate = ''; Age = 0
            Gender = $ln.gender; Country = $ln.team; CountryShort = $ln.team
            IDNumber = ''; Phone = ''
            Notes = ''; LegLabel = ''; CSANumber = ''; FINANumber = $null; HealthCertDate = $null
            EventName = $eventName; CurrentStage = $ev.Stage
            Heat = $ev.Heat; Lane = $ln.lane
            EntryTime = ''; EntryTimeSeconds = 0.0
            IsQualified = $true; Status = ''; CurrentRank = 0
            AgeCategory = $ln.ageGroup; Results = @()
            StageAssignments = @{
                $ev.Stage = @{ Stage = $ev.Stage; Heat = $ev.Heat; Lane = $ln.lane;
                               EntryTimeSeconds = 0.0; EntryTime = '' }
            }
        }))
    }
}

$ageGroups = @(
    [pscustomobject]@{ Name='8-9岁组';   MinAge=8;  MaxAge=9 }
    [pscustomobject]@{ Name='12-13岁组'; MinAge=12; MaxAge=13 }
    [pscustomobject]@{ Name='14-15岁组'; MinAge=14; MaxAge=15 }
    [pscustomobject]@{ Name='12-15岁组'; MinAge=12; MaxAge=15 }   # schedule 用 (并项)
)

$units = New-Object System.Collections.Generic.List[object]
foreach ($u in ($unitSet | Sort-Object)) {
    $units.Add([pscustomobject]@{ Name=$u; ShortName=$u; FullName=$u; Leader=''; Coach=''; Phone='' })
}

$eventsOrdered = @('50米蝶泳','100米蛙泳','200米混合泳','200米仰泳')
$evList = New-Object System.Collections.Generic.List[string]
foreach ($e in $eventsOrdered) { if ($eventSet.Contains($e)) { $evList.Add($e) } }
foreach ($e in ($eventSet | Sort-Object)) { if (-not $evList.Contains($e)) { $evList.Add($e) } }

$package = [ordered]@{
    CompetitionName = $CompetitionName; CompetitionMode = 'domestic'
    StartDate = $Date; EndDate = $Date; Location = $Location
    PoolLength = 50; LaneCount = 10
    Organizer = '甘肃省游泳协会'; Host = ''
    TechnicalDelegate = ''; Referee = ''; Starter = ''; Arbiter = ''; ChiefJudge = ''
    Officials = @()
    Swimmers = $swimmers; RelayTeams = @()
    Records = @(); TeamScores = @()
    Schedule = $schedule; Events = $evList; AgeGroups = $ageGroups
    Genders = @('男','女','混合','男女'); Stages = @('预赛','半决赛','决赛')
    HeatCounts = @('1组','2组','3组','4组','5组','6组','7组','8组','9组','10组')
    BibRanges = @(); Units = $units; StaffList = @()
    WizardDraft = $null; LaneCloseSettings = $null; DisputeLog = @{}
    ProgramBook = $null; ResultBook = $null
    DisplayRecordLabel = ''; DisplayRecordTypeName = ''; DisplayRecordOptions = @()
    ConfirmedHeats = @(); ScoringConfig = $null; DurationConfig = $null
}

$jsonPath = Join-Path $OutDir "$CompetitionName.json"
$json = $package | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($jsonPath, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "    JSON 输出: $jsonPath ($('{0:N0}' -f (Get-Item $jsonPath).Length) 字节)"

Write-Host ""
Write-Host "完成. 总览:"
Write-Host "  事件: $($events.Count)"
Write-Host "  运动员: $($swimmers.Count)"
Write-Host "  代表队: $($units.Count)"
Write-Host "  项目: $($evList.Count)"
Write-Host "  组别: $($ageGroups.Count)"
