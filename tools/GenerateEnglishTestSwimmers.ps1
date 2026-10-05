# 2026-10-05 模拟报名程序(英文测试数据生成器)
#
# 目的: 给"English模式下全流程测试"用——生成一批用西式英文姓名/队名的运动员和接力队
# 报名数据, CSV表头按本系统English模式下"导出个人报名模板"/"导出接力报名模板"产生的
# 真实表头拼(Bib,Name,Gender,Team,...,Event1,Time1,...), 可以直接在 Swimmers / Relay Teams
# 两个Tab里用"Import CSV"按钮原样导入, 不用改表头/列序。
#
# 注意: 性别(Gender列)/组别(Group列)/项目名(EventN列)这三类字段在本系统里是贯穿全局做
# 字符串比较的"数据哨兵值"(跟UI语言无关, 即使English模式下界面显示"Men/Women", 存到数据
# 和CSV列里的值依然固定是中文"男/女/混合"/"青年组/少年组"/"50米自由泳"这类——这是本系统
# 既有的设计, 不是没翻译完; 这样一份英文姓名的报名表才能真的被系统认出来、排上日程、
# 参与排名计算)。只有"人"相关的自由文本字段(姓名/队名/备注/电话)才用英文填, 这样
# 生成出来的测试数据能在English模式下验证："western姓名在各种导出文档/打印里排版是否正常"。
#
# 用法:
#   powershell -ExecutionPolicy Bypass -File tools\GenerateEnglishTestSwimmers.ps1 -OutDir <目录>
# 产出:
#   <OutDir>\SwimmerRegistrations_EN_Test.csv   (个人报名, 约30人)
#   <OutDir>\RelayTeams_EN_Test.csv             (接力队, 6支)
# 然后在程序里: Events & Registration -> Swimmers -> Import CSV (选第一个文件)
#             Events & Registration -> Relay Teams -> Import CSV (选第二个文件)

param(
    [string]$OutDir = "$PSScriptRoot\..\scratch_test_data"
)

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

# ---- 本次测试赛事里真实存在的项目/组别(跟 RecordFilterEvent/ResultAgeGroupCombo 现场核对过) ----
$IndividualEvents = @(
    "50米自由泳","100米自由泳","200米自由泳","400米自由泳",
    "50米蛙泳","100米蛙泳","200米蛙泳",
    "50米仰泳","100米仰泳","200米仰泳",
    "50米蝶泳","100米蝶泳","200米蝶泳",
    "200米混合泳"
)
$RelayEvents = @("4x50米自由泳接力","4x100米自由泳接力","4x100米混合泳接力")
$AgeGroups = @("青年组","少年组")
$Genders = @("男","女")

# ---- 西式姓名库(足够随机组合出不重复的全名, 用来验证英文姓名在各类导出/打印文档里排版是否正常) ----
$MaleFirst = @("Liam","Noah","Oliver","Ethan","Lucas","Mason","Logan","James","Benjamin","Henry","Alexander","Jack","Daniel","Owen","Caleb")
$FemaleFirst = @("Emma","Olivia","Ava","Sophia","Isabella","Mia","Charlotte","Amelia","Harper","Evelyn","Abigail","Emily","Elizabeth","Sofia","Victoria")
$LastNames = @("Johnson","Smith","Williams","Brown","Jones","Garcia","Miller","Davis","Rodriguez","Martinez","Anderson","Taylor","Thomas","Moore","Jackson","Martin","Lee","Perez","Thompson","White")
$TeamNames = @("Riverside Swim Club","Northside Aquatics","Blue Dolphins","Golden Wave","Pacific Storm","Summit Swimming","Harbor City Sharks","Lakeside Stingrays","Metro Barracudas","Coral Bay Divers")
$TeamAbbr = @("RSC","NSA","BLD","GWV","PST","SMT","HCS","LKR","MBC","CBD")

function RandomName($gender) {
    $first = if ($gender -eq "男") { $MaleFirst | Get-Random } else { $FemaleFirst | Get-Random }
    $last = $LastNames | Get-Random
    return "$first $last"
}

function RandomTime($eventName) {
    # 粗略按项目距离给一个看起来合理的成绩区间, 不追求竞技精确, 只为能通过 TimeFormatter.Parse
    if ($eventName -match "^50米") { $sec = Get-Random -Minimum 24 -Maximum 40; return ("0:{0:00}.{1:00}" -f $sec, (Get-Random -Minimum 0 -Maximum 99)) }
    if ($eventName -match "^100米") { $sec = Get-Random -Minimum 55 -Maximum 90; $m=[math]::Floor($sec/60); $s=$sec%60; return ("{0}:{1:00}.{2:00}" -f $m,$s,(Get-Random -Minimum 0 -Maximum 99)) }
    if ($eventName -match "^200米") { $sec = Get-Random -Minimum 120 -Maximum 190; $m=[math]::Floor($sec/60); $s=$sec%60; return ("{0}:{1:00}.{2:00}" -f $m,$s,(Get-Random -Minimum 0 -Maximum 99)) }
    $sec = Get-Random -Minimum 250 -Maximum 380; $m=[math]::Floor($sec/60); $s=$sec%60; return ("{0}:{1:00}.{2:00}" -f $m,$s,(Get-Random -Minimum 0 -Maximum 99))
}

# ---- 个人报名 CSV (English模式表头, 跟 MainWindow.xaml.cs SwimmerCsvBaseHeaderKeys 顺序一致) ----
$header = @("Bib","Name","Gender","Team","Age","Birth Date","ID No.","Phone","Notes","Group","Abbr")
for ($n=1; $n -le 8; $n++) { $header += "Event$n"; $header += "Time$n" }
$rows = New-Object System.Collections.Generic.List[string]
$rows.Add(($header -join ","))

$bibSeq = 500
for ($i = 0; $i -lt 30; $i++) {
    $gender = $Genders | Get-Random
    $ageGroup = $AgeGroups | Get-Random
    $age = if ($ageGroup -eq "少年组") { Get-Random -Minimum 11 -Maximum 14 } else { Get-Random -Minimum 15 -Maximum 22 }
    $teamIdx = Get-Random -Minimum 0 -Maximum $TeamNames.Count
    $name = RandomName $gender
    $birthYear = 2026 - $age
    $dob = "{0}-{1:00}-{2:00}" -f $birthYear, (Get-Random -Minimum 1 -Maximum 12), (Get-Random -Minimum 1 -Maximum 28)
    $phone = "1{0}" -f (Get-Random -Minimum 3000000000 -Maximum 3999999999)
    $bibSeq++

    $pickCount = Get-Random -Minimum 1 -Maximum 4
    # @(...) 强制数组——pickCount=1 时 Get-Random 会把结果"收扁"成裸字符串,
    # 不加这层括号下面 $picked[$n-1] 就会变成按字符下标取字符串的第1个字(乱码的源头)
    $picked = @($IndividualEvents | Get-Random -Count $pickCount)
    $cols = @("T$bibSeq", $name, $gender, ($TeamNames[$teamIdx]), $age, $dob, "", $phone, "", $ageGroup, ($TeamAbbr[$teamIdx]))
    for ($n = 1; $n -le 8; $n++) {
        if ($n -le $picked.Count) { $cols += $picked[$n-1]; $cols += (RandomTime $picked[$n-1]) }
        else { $cols += ""; $cols += "" }
    }
    $rows.Add(($cols -join ","))
}
$swimmerPath = Join-Path $OutDir "SwimmerRegistrations_EN_Test.csv"
[System.IO.File]::WriteAllLines($swimmerPath, $rows, (New-Object System.Text.UTF8Encoding($true)))

# ---- 接力队 CSV (English模式表头, 跟 MainWindow.xaml.cs BuildRelayCsvHeader(en) 顺序一致) ----
$rheader = @("Team Name","Group","Event","Gender","Entry",
    "Leg 1 Name","Leg 1 ID No.","Leg 1 DOB",
    "Leg 2 Name","Leg 2 ID No.","Leg 2 DOB",
    "Leg 3 Name","Leg 3 ID No.","Leg 3 DOB",
    "Leg 4 Name","Leg 4 ID No.","Leg 4 DOB","Notes")
$rrows = New-Object System.Collections.Generic.List[string]
$rrows.Add(($rheader -join ","))

for ($i = 0; $i -lt 6; $i++) {
    $gender = $Genders | Get-Random
    $ageGroup = $AgeGroups | Get-Random
    $ev = $RelayEvents | Get-Random
    $teamIdx = Get-Random -Minimum 0 -Maximum $TeamNames.Count
    $teamName = "$($TeamNames[$teamIdx]) Relay $i"
    $cols = @($teamName, $ageGroup, $ev, $gender, (RandomTime "200米"))
    for ($leg = 1; $leg -le 4; $leg++) {
        $legName = RandomName $gender
        $birthYear = 2026 - (Get-Random -Minimum 12 -Maximum 20)
        $dob = "{0}-{1:00}-{2:00}" -f $birthYear, (Get-Random -Minimum 1 -Maximum 12), (Get-Random -Minimum 1 -Maximum 28)
        $cols += $legName; $cols += ""; $cols += $dob
    }
    $cols += ""
    $rrows.Add(($cols -join ","))
}
$relayPath = Join-Path $OutDir "RelayTeams_EN_Test.csv"
[System.IO.File]::WriteAllLines($relayPath, $rrows, (New-Object System.Text.UTF8Encoding($true)))

Write-Host "Generated:"
Write-Host "  $swimmerPath  (30 swimmers)"
Write-Host "  $relayPath  (6 relay teams)"
Write-Host ""
Write-Host "Import in the app (English mode): Events & Registration -> Swimmers -> Import CSV"
Write-Host "                                   Events & Registration -> Relay Teams -> Import CSV"
