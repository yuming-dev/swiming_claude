# ══════════════════════════════════════════════════════════════════
# 安装结果验证 (2026-09-01)
#
# 为什么有这个:
#   之前一直只验"安装包里有没有", 从来没验过"装完之后有没有"。
#   结果 Setup.cs 用的是 Directory.GetFiles(不递归), x64\SQLite.Interop.dll
#   这种子目录里的原生 DLL 根本没被装到机器上 —— 包里有, 装完就没了。
#   运行时报"无法加载 SQLite.Interop.dll: 找不到指定的模块", 竞赛库全程打不开,
#   成绩不入库、没有名次、组排名不生成, 而界面上看不出任何异常。查了很久。
#
# 这个脚本【模拟安装器的复制逻辑】, 检查装完之后关键文件在不在。
#
# 用法: powershell -ExecutionPolicy Bypass -File tools\DbSelfTest\InstallTest.ps1
# ══════════════════════════════════════════════════════════════════

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pkg  = Join-Path $root 'InstallerBuild'

$pass = 0; $fail = 0; $msgs = @()
function Chk($n,$c,$d){ if($c){$script:pass++; Write-Host ("  [PASS] "+$n) -ForegroundColor Green}
  else {$script:fail++; Write-Host ("  [FAIL] "+$n+"  "+$d) -ForegroundColor Red; $script:msgs+=($n+" :: "+$d)} }

if (-not (Test-Path $pkg)) { Write-Host "找不到 InstallerBuild，先打包" -ForegroundColor Red; exit 1 }

# 按 Setup.cs 的映射: 源目录 -> 安装后的目录名
$map = @(
    @{ src='SwimmingScoreboard';   dst='Server' },
    @{ src='RemoteTimingControl';  dst='RemoteTiming' },
    @{ src='ScheduleEditor';       dst='ScheduleEditor' }
)

$tmp = Join-Path $env:TEMP ('insttest_' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $tmp | Out-Null
Write-Host ("模拟安装到: " + $tmp)
Write-Host ""

# 递归复制 —— 与修好后的 Setup.CopyDirDeep 同语义
function CopyDeep($s,$d){
    if (-not (Test-Path $s)) { return }
    New-Item -ItemType Directory -Force $d | Out-Null
    Copy-Item (Join-Path $s '*') $d -Recurse -Force
}

Write-Host "装完之后, 关键文件在不在:"
foreach ($m in $map) {
    $s = Join-Path $pkg $m.src
    if (-not (Test-Path $s)) { continue }
    $d = Join-Path $tmp $m.dst
    CopyDeep $s $d
    # 这三样缺一个, 竞赛库就打不开
    $exe = Get-ChildItem $d -Filter '*.exe' -EA SilentlyContinue | Select-Object -First 1
    Chk ($m.dst + ": 主程序 exe") ($null -ne $exe) "没有 exe"
    Chk ($m.dst + ": System.Data.SQLite.dll") (Test-Path (Join-Path $d 'System.Data.SQLite.dll')) "缺托管层"
    $interop = Join-Path $d 'x64\SQLite.Interop.dll'
    Chk ($m.dst + ": x64\SQLite.Interop.dll") (Test-Path $interop) "★缺原生层 —— 竞赛库会打不开, 成绩全进不了库"
}

try { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } catch {}

Write-Host ""
Write-Host ("结果: " + $pass + " 过 / " + $fail + " failed")
if ($fail -gt 0) { $msgs | ForEach-Object { Write-Host ("  "+$_) -ForegroundColor Red }; exit 1 }
exit 0
