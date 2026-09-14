# build_card.ps1 - 生成《现场速查卡》(md -> html -> pdf)
# md2html.ps1 那套 CSS 是给说明书用的(大字号宽行距), 速查卡要塞进 2 页 A4,
# 所以生成 HTML 之后把 <style> 换成 card_print.css 里那套紧凑排版, 再用 Word 导 PDF。
# 用法: powershell -ExecutionPolicy Bypass -File .\build_card.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$md   = Join-Path $root "游泳计时系统_现场速查卡.md"
$html = Join-Path $root "游泳计时系统_现场速查卡.html"
$pdf  = Join-Path $root "游泳计时系统_现场速查卡.pdf"
$cssF = Join-Path $root "card_print.css"

& powershell -ExecutionPolicy Bypass -File (Join-Path $root "md2html.ps1") -mdPath $md -htmlPath $html -title "游泳计时系统 现场速查卡"

$css = ([System.IO.File]::ReadAllText($cssF, [System.Text.Encoding]::UTF8) -replace "`r?`n", " ")
$h   = [System.IO.File]::ReadAllText($html, [System.Text.Encoding]::UTF8)
$re  = [System.Text.RegularExpressions.RegexOptions]::Singleline
$h   = [System.Text.RegularExpressions.Regex]::Replace($h, "<style>.*?</style>", ("<style>" + $css + "</style>"), $re)
[System.IO.File]::WriteAllText($html, $h, [System.Text.Encoding]::UTF8)

$w = New-Object -ComObject Word.Application
$w.Visible = $false
$w.DisplayAlerts = 0
try {
    $d = $w.Documents.Open([string]$html)
    $d.ExportAsFixedFormat([string]$pdf, 17, $false, 1)
    $pages = $d.ComputeStatistics(2)
    $d.Close(0)
    Write-Output ("速查卡已生成: " + $pdf + "  共 " + $pages + " 页")
} finally {
    $w.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($w) | Out-Null
}
