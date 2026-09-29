param(
    [string]$root = "C:\游泳2026\swiming_claude"
)
$html = Join-Path $root "游泳赛事管理与计时系统_使用说明书.html"
$pdf = Join-Path $root "游泳赛事管理与计时系统_使用说明书.pdf"
$doc = Join-Path $root "游泳赛事管理与计时系统_使用说明书.doc"

$w = New-Object -ComObject Word.Application
$w.Visible = $false
$w.DisplayAlerts = 0
$d = $w.Documents.Open([string]$html)
$d.ExportAsFixedFormat([string]$pdf, 17, $false, 1)
$d.SaveAs([string]$doc, 0)
$d.Close(0)
$w.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($w) | Out-Null

Write-Output ("PDF: " + $pdf + " (" + (Get-Item $pdf).Length + " bytes)")
Write-Output ("DOC: " + $doc + " (" + (Get-Item $doc).Length + " bytes)")
