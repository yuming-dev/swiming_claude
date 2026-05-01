# 简易 Markdown → HTML 转换器（足够本说明书使用）
param([string]$mdPath, [string]$htmlPath, [string]$title = "使用说明书")

$md = Get-Content -Raw -Path $mdPath -Encoding UTF8
$lines = $md -split "`r?`n"
$out = New-Object System.Text.StringBuilder
$inCodeBlock = $false
$inTable = $false
$tableHeaderProcessed = $false
$inList = $false
$listType = ""

function Esc($s) {
    return $s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
}

function Inline($s) {
    # 粗体 **xxx**
    $s = [regex]::Replace($s, '\*\*([^\*]+)\*\*', '<strong>$1</strong>')
    # 斜体 *xxx*
    $s = [regex]::Replace($s, '(?<!\*)\*([^\*]+)\*(?!\*)', '<em>$1</em>')
    # 行内代码 `xxx`
    $s = [regex]::Replace($s, '`([^`]+)`', '<code>$1</code>')
    # 链接 [text](url)
    $s = [regex]::Replace($s, '\[([^\]]+)\]\(([^\)]+)\)', '<a href="$2">$1</a>')
    return $s
}

foreach ($line in $lines) {
    if ($line -match '^```') {
        if ($inCodeBlock) { [void]$out.AppendLine('</pre>'); $inCodeBlock = $false }
        else { [void]$out.AppendLine('<pre>'); $inCodeBlock = $true }
        continue
    }
    if ($inCodeBlock) {
        [void]$out.AppendLine((Esc $line))
        continue
    }
    # 表格
    if ($line -match '^\s*\|') {
        if (-not $inTable) {
            [void]$out.AppendLine('<table>')
            $inTable = $true
            $tableHeaderProcessed = $false
        }
        if ($line -match '^\s*\|[\s\-:|]+\|\s*$') {
            $tableHeaderProcessed = $true
            continue
        }
        $cells = ($line -split '\|')[1..(($line -split '\|').Length - 2)]
        $tag = if ($tableHeaderProcessed) { 'td' } else { 'th' }
        if (-not $tableHeaderProcessed) { [void]$out.AppendLine('<thead><tr>') }
        else { [void]$out.AppendLine('<tr>') }
        foreach ($c in $cells) { [void]$out.AppendLine("  <$tag>" + (Inline (Esc $c.Trim())) + "</$tag>") }
        if (-not $tableHeaderProcessed) { [void]$out.AppendLine('</tr></thead><tbody>') }
        else { [void]$out.AppendLine('</tr>') }
        continue
    } elseif ($inTable) {
        [void]$out.AppendLine('</tbody></table>')
        $inTable = $false
    }
    # 列表
    if ($line -match '^\s*[-*]\s+(.+)') {
        if ($inList -and $listType -ne 'ul') { [void]$out.AppendLine('</ol>'); $inList = $false }
        if (-not $inList) { [void]$out.AppendLine('<ul>'); $inList = $true; $listType = 'ul' }
        [void]$out.AppendLine('<li>' + (Inline (Esc $matches[1])) + '</li>')
        continue
    }
    if ($line -match '^\s*\d+\.\s+(.+)') {
        if ($inList -and $listType -ne 'ol') { [void]$out.AppendLine('</ul>'); $inList = $false }
        if (-not $inList) { [void]$out.AppendLine('<ol>'); $inList = $true; $listType = 'ol' }
        [void]$out.AppendLine('<li>' + (Inline (Esc $matches[1])) + '</li>')
        continue
    }
    if ($inList -and $line -match '^\s*$') {
        [void]$out.AppendLine("</$listType>")
        $inList = $false
    }
    # 标题
    if ($line -match '^(#{1,6})\s+(.+)') {
        $level = $matches[1].Length
        $text = $matches[2]
        [void]$out.AppendLine("<h$level>" + (Inline (Esc $text)) + "</h$level>")
        continue
    }
    # 水平线
    if ($line -match '^---+\s*$') {
        [void]$out.AppendLine('<hr>')
        continue
    }
    # 引用
    if ($line -match '^>\s*(.+)') {
        [void]$out.AppendLine('<blockquote>' + (Inline (Esc $matches[1])) + '</blockquote>')
        continue
    }
    # 段落
    if ($line.Trim() -ne '') {
        [void]$out.AppendLine('<p>' + (Inline (Esc $line)) + '</p>')
    }
}

if ($inList) { [void]$out.AppendLine("</$listType>") }
if ($inTable) { [void]$out.AppendLine('</tbody></table>') }

$css = @'
body { font-family: "Microsoft YaHei", "Source Han Sans CN", sans-serif; max-width: 960px; margin: 30px auto; padding: 0 20px; line-height: 1.7; color: #1f2937; background: #fff; }
h1 { color: #1e3a8a; font-size: 28px; border-bottom: 3px solid #3b82f6; padding-bottom: 12px; margin-top: 30px; }
h2 { color: #1e40af; font-size: 22px; border-bottom: 1px solid #cbd5e1; padding-bottom: 6px; margin-top: 30px; }
h3 { color: #1e3a8a; font-size: 18px; margin-top: 24px; }
h4 { color: #334155; font-size: 16px; margin-top: 18px; }
h5,h6 { color: #475569; font-size: 14px; }
p { margin: 8px 0; }
table { border-collapse: collapse; margin: 12px 0; width: 100%; font-size: 14px; }
th, td { border: 1px solid #cbd5e1; padding: 8px 12px; text-align: left; vertical-align: top; }
th { background: #eff6ff; color: #1e40af; font-weight: bold; }
tr:nth-child(even) td { background: #f8fafc; }
code { background: #f1f5f9; padding: 1px 6px; border-radius: 3px; color: #be185d; font-family: Consolas, monospace; font-size: 90%; }
pre { background: #0f172a; color: #e2e8f0; padding: 14px 16px; border-radius: 6px; overflow-x: auto; font-family: Consolas, monospace; font-size: 13px; }
ul, ol { margin: 8px 0 8px 28px; }
li { margin: 4px 0; }
blockquote { border-left: 4px solid #3b82f6; background: #eff6ff; padding: 6px 14px; margin: 10px 0; color: #1e3a8a; }
hr { border: none; border-top: 2px solid #e2e8f0; margin: 30px 0; }
a { color: #2563eb; text-decoration: none; }
a:hover { text-decoration: underline; }
strong { color: #1e293b; }
@media print {
  body { font-size: 12pt; max-width: 100%; }
  h1 { page-break-before: always; }
  h1:first-child { page-break-before: auto; }
  table, pre, blockquote { page-break-inside: avoid; }
  pre { background: #f8fafc; color: #1f2937; }
}
'@

$body = $out.ToString()
$html = "<!DOCTYPE html><html lang=`"zh`"><head><meta charset=`"utf-8`"><title>$title</title><style>$css</style></head><body>$body</body></html>"
[System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.Encoding]::UTF8)
Write-Output ("HTML written: " + $htmlPath + " (" + (Get-Item $htmlPath).Length + " bytes)")
