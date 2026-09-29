param(
    [string]$root = "C:\游泳2026\swiming_claude",
    [string]$baseName = "游泳赛事管理与计时系统_使用说明书"
)
$html = Join-Path $root ($baseName + ".html")
$pdf = Join-Path $root ($baseName + ".pdf")
$docx = Join-Path $root ($baseName + ".docx")

$w = New-Object -ComObject Word.Application
$w.Visible = $false
$w.DisplayAlerts = 0
$d = $w.Documents.Open([string]$html)
$d.ExportAsFixedFormat([string]$pdf, 17, $false, 1)
# 2026-09-28 改 SaveAs 参数 0(.doc 旧版二进制) -> 12(wdFormatXMLDocument, 即 .docx)，
# 用户明确要 docx 不要 doc；文件名后缀也跟着改。
$d.SaveAs([string]$docx, 12)
$d.Close(0)
$w.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($w) | Out-Null

Write-Output ("PDF: " + $pdf + " (" + (Get-Item $pdf).Length + " bytes)")
Write-Output ("DOCX: " + $docx + " (" + (Get-Item $docx).Length + " bytes)")
