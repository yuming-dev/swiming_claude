# 2026-05-29 run_tests.ps1
#   本地一键测试: 编主项目 + LapTestSim + HwLapRemainingTest, 跑两个测试, 汇总通过/失败.
#   用法:
#     PS> .\run_tests.ps1               # 全跑
#     PS> .\run_tests.ps1 -SkipMain     # 跳过主项目编译 (省时间)
#     PS> .\run_tests.ps1 -Quiet        # 只打印汇总
param(
    [switch]$SkipMain = $false,
    [switch]$Quiet    = $false
)

$ErrorActionPreference = 'Continue'
$Root      = 'C:\代码\swiming_claude'
$MSBuild   = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'
$VcVars    = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat'
$LogDir    = Join-Path $Root '.test_logs'
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$Results = @()  # 每项: @{ Step, Status, Detail }

function Add-Result($step, $status, $detail) {
    $script:Results += [pscustomobject]@{ Step=$step; Status=$status; Detail=$detail }
    $tag = if ($status -eq 'PASS') { '[PASS]' } elseif ($status -eq 'FAIL') { '[FAIL]' } else { '[SKIP]' }
    Write-Host ("  {0} {1}: {2}" -f $tag, $step, $detail)
}

Write-Host ""
Write-Host "===================================================="
Write-Host "  Swimming Timing — Local Test Suite"
Write-Host "===================================================="
Write-Host "  Root:  $Root"
Write-Host "  Logs:  $LogDir"
Write-Host ""

# === Step 1: 主项目 SwimmingScoreboard (含 LapAdjustLogic.cs) ===
if ($SkipMain) {
    Add-Result 'Build SwimmingScoreboard' 'SKIP' '-SkipMain'
} else {
    Write-Host "[1/4] 编主项目 SwimmingScoreboard ..."
    $log = Join-Path $LogDir 'main_build.log'
    & $MSBuild "$Root\SwimmingScoreboard\SwimmingScoreboard.csproj" `
        /t:Build /p:Configuration=Release /p:Platform=x64 /v:m /nologo `
        > $log 2>&1
    if ($LASTEXITCODE -eq 0) {
        Add-Result 'Build SwimmingScoreboard' 'PASS' 'Release|x64 OK'
    } else {
        $errs = (Get-Content $log | Select-String -Pattern 'error' -CaseSensitive:$false | Select-Object -First 3) -join "`n  "
        Add-Result 'Build SwimmingScoreboard' 'FAIL' "see $log`n  $errs"
    }
}

# === Step 2: LapTestSim 编 + 跑 ===
Write-Host "[2/4] 编 LapTestSim ..."
$log = Join-Path $LogDir 'lts_build.log'
& $MSBuild "$Root\LapTestSim\LapTestSim.csproj" `
    /t:Build /p:Configuration=Release /v:m /nologo > $log 2>&1
if ($LASTEXITCODE -ne 0) {
    $errs = (Get-Content $log | Select-String -Pattern 'error' -CaseSensitive:$false | Select-Object -First 3) -join "`n  "
    Add-Result 'Build LapTestSim' 'FAIL' "see $log`n  $errs"
} else {
    Add-Result 'Build LapTestSim' 'PASS' 'Release OK'

    Write-Host "[3/4] 跑 LapTestSim ..."
    $log = Join-Path $LogDir 'lts_run.log'
    $exe = "$Root\LapTestSim\bin\Release\LapTestSim.exe"
    & $exe > $log 2>&1
    $exitCode = $LASTEXITCODE
    $output = Get-Content $log -Raw
    # 解析: 汇总: total=400, Ok=157, 防1=84, 防2=1, 防3=145, 防4=13, 无变=0
    # 防 4 二次确认通过率: 13/13 ✓ 全通过
    $sumMatch = [regex]::Match($output, '汇总:\s*total=(\d+),\s*Ok=(\d+),\s*防1=(\d+),\s*防2=(\d+),\s*防3=(\d+),\s*防4=(\d+),\s*无变=(\d+)')
    $confMatch = [regex]::Match($output, '防\s*4\s*二次确认通过率:\s*(\d+)/(\d+)\s*([✓✗])')
    if ($sumMatch.Success -and $confMatch.Success) {
        $total = [int]$sumMatch.Groups[1].Value
        $confPass = [int]$confMatch.Groups[1].Value
        $confTotal = [int]$confMatch.Groups[2].Value
        $confMark = $confMatch.Groups[3].Value
        if ($confMark -eq '✓' -and $confPass -eq $confTotal) {
            Add-Result 'Run LapTestSim' 'PASS' "$total enums OK, 防4 confirm $confPass/$confTotal pass"
        } else {
            Add-Result 'Run LapTestSim' 'FAIL' "防4 confirm $confPass/$confTotal failed"
        }
    } else {
        Add-Result 'Run LapTestSim' 'FAIL' "output parse failed (exit=$exitCode); see $log"
    }
}

# === Step 4: HwLapRemainingTest (C, cl.exe) ===
Write-Host "[4/4] 编 + 跑 HwLapRemainingTest (cl) ..."
$buildLog = Join-Path $LogDir 'hw_build.log'
$runLog   = Join-Path $LogDir 'hw_run.log'
$hwDir    = "$Root\HwLapRemainingTest"
# 用 cmd /c 一次性 vcvars + cl
$cmd = "call `"$VcVars`" >nul 2>&1 && cd /d `"$hwDir`" && cl /nologo /utf-8 /W3 /O2 /Fe:test_lap_remaining.exe test_lap_remaining.c"
$null = cmd /c $cmd 2>&1 | Out-File -FilePath $buildLog -Encoding utf8
$hwExe = "$hwDir\test_lap_remaining.exe"
if (-not (Test-Path $hwExe)) {
    $errs = (Get-Content $buildLog | Select-String -Pattern 'error' -CaseSensitive:$false | Select-Object -First 3) -join "`n  "
    Add-Result 'Build HwLapRemainingTest' 'FAIL' "see $buildLog`n  $errs"
} else {
    Add-Result 'Build HwLapRemainingTest' 'PASS' 'cl /utf-8 OK'
    & $hwExe > $runLog 2>&1
    $hwExit = $LASTEXITCODE
    # 解析: SUMMARY: total=64 pass=64 fail=0
    $hwSum = [regex]::Match((Get-Content $runLog -Raw), 'SUMMARY:\s*total=(\d+)\s*pass=(\d+)\s*fail=(\d+)')
    if ($hwSum.Success) {
        $t = [int]$hwSum.Groups[1].Value
        $p = [int]$hwSum.Groups[2].Value
        $f = [int]$hwSum.Groups[3].Value
        if ($f -eq 0 -and $hwExit -eq 0) {
            Add-Result 'Run HwLapRemainingTest' 'PASS' "$p/$t assertions"
        } else {
            $fails = (Get-Content $runLog | Select-String -Pattern '\[FAIL\]' | Select-Object -First 5) -join "`n  "
            Add-Result 'Run HwLapRemainingTest' 'FAIL' "$f of $t failed (exit=$hwExit)`n  $fails"
        }
    } else {
        Add-Result 'Run HwLapRemainingTest' 'FAIL' "output parse failed (exit=$hwExit); see $runLog"
    }
}

# === Summary ===
Write-Host ""
Write-Host "===================================================="
$pass = ($Results | Where-Object Status -eq 'PASS').Count
$fail = ($Results | Where-Object Status -eq 'FAIL').Count
$skip = ($Results | Where-Object Status -eq 'SKIP').Count
if ($fail -eq 0) {
    Write-Host ("  All passed: {0} step(s), {1} skipped" -f $pass, $skip) -ForegroundColor Green
} else {
    Write-Host ("  FAILED: {0} pass, {1} fail, {2} skip" -f $pass, $fail, $skip) -ForegroundColor Red
    Write-Host ""
    Write-Host "  Failed steps:" -ForegroundColor Red
    $Results | Where-Object Status -eq 'FAIL' | ForEach-Object {
        Write-Host ("    - {0}" -f $_.Step) -ForegroundColor Red
    }
}
Write-Host "===================================================="
exit (& { if ($fail -eq 0) { 0 } else { 1 } })
