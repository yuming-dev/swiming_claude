# 一键跑全部自动化测试。交付之前必须全绿。
# 用法: powershell -ExecutionPolicy Bypass -File tools\DbSelfTest\RunAll.ps1
$ErrorActionPreference = 'Continue'
$d = $PSScriptRoot
$fail = 0
foreach ($t in @(@('竞赛库规则', 'DbSelfTest.ps1'), @('端到端(起进程+WebSocket+查库)', 'E2ETest.ps1'), @('源码不变量', 'SourceRules.ps1'))) {
    Write-Host ""
    Write-Host ("═══ " + $t[0] + " ═══") -ForegroundColor Cyan
    & (Join-Path $d $t[1])
    if ($LASTEXITCODE -ne 0) { $fail++ }
}
Write-Host ""
if ($fail -gt 0) { Write-Host ("有 " + $fail + " 组测试失败") -ForegroundColor Red; exit 1 }
Write-Host "全部通过" -ForegroundColor Green
exit 0