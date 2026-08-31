# 一键跑全部自动化测试。交付之前必须全绿。
# 用法: powershell -ExecutionPolicy Bypass -File tools\DbSelfTest\RunAll.ps1
$ErrorActionPreference = 'Continue'
$d = $PSScriptRoot
$fail = 0
foreach ($t in @(@('竞赛库规则', 'DbSelfTest.ps1'), @('端到端(起进程+WebSocket+查库)', 'E2ETest.ps1'), @('源码不变量', 'SourceRules.ps1'))) {
    Write-Host ""
    Write-Host ("═══ " + $t[0] + " ═══") -ForegroundColor Cyan
    # 用【子进程】跑: 反射测试会 Assembly.LoadFrom 锁住 exe, 同进程跑完锁不释放,
    # 紧接着打包就会 MSB3027"文件被 PowerShell 锁定"。子进程一退, 锁自然没了。
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $d $t[1])
    if ($LASTEXITCODE -ne 0) { $fail++ }
}
Write-Host ""
if ($fail -gt 0) { Write-Host ("有 " + $fail + " 组测试失败") -ForegroundColor Red; exit 1 }
Write-Host "全部通过" -ForegroundColor Green
exit 0