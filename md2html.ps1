param([string]$mdPath, [string]$htmlPath, [string]$title)

$md = [System.IO.File]::ReadAllText($mdPath, [System.Text.Encoding]::UTF8)
$lines = $md -split "(`r`n|`n)"

$out = ""
$inCode = $false
$inTable = $false
$tableHeaderDone = $false
$inList = $false
$listType = ""

function EscHtml([string]$s) {
    $s = $s.Replace('&', '&amp;')
    $s = $s.Replace('<', '&lt;')
    $s = $s.Replace('>', '&gt;')
    return $s
}

function InlineMd([string]$s) {
    # bold
    $s = [regex]::Replace($s, '\*\*([^\*]+)\*\*', '<strong>$1</strong>')
    # italic
    $s = [regex]::Replace($s, '(?<!\*)\*([^\*\s][^\*]*?)\*(?!\*)', '<em>$1</em>')
    # code
    $s = [regex]::Replace($s, '`([^`]+)`', '<code>$1</code>')
    # links
    $s = [regex]::Replace($s, '\[([^\]]+)\]\(([^\)]+)\)', '<a href="$2">$1</a>')
    return $s
}

foreach ($raw in $lines) {
    if ($raw -eq "`n" -or $raw -eq "`r`n") { continue }
    $line = $raw

    if ($line -match '^```') {
        if ($inCode) { $out += "</pre>`r`n"; $inCode = $false }
        else { $out += "<pre>`r`n"; $inCode = $true }
        continue
    }
    if ($inCode) {
        $out += (EscHtml $line) + "`r`n"
        continue
    }

    # 表格
    if ($line -match '^\s*\|') {
        if (-not $inTable) { $out += "<table>`r`n"; $inTable = $true; $tableHeaderDone = $false }
        if ($line -match '^\s*\|[\s\-:|]+\|\s*$') { $tableHeaderDone = $true; continue }
        $parts = $line -split '\|'
        $cells = @()
        for ($i = 1; $i -lt ($parts.Length - 1); $i++) { $cells += $parts[$i] }
        if (-not $tableHeaderDone) {
            $out += "<thead><tr>`r`n"
            foreach ($c in $cells) { $out += "  <th>" + (InlineMd (EscHtml $c.Trim())) + "</th>`r`n" }
            $out += "</tr></thead><tbody>`r`n"
        } else {
            $out += "<tr>`r`n"
            foreach ($c in $cells) { $out += "  <td>" + (InlineMd (EscHtml $c.Trim())) + "</td>`r`n" }
            $out += "</tr>`r`n"
        }
        continue
    } elseif ($inTable) {
        $out += "</tbody></table>`r`n"
        $inTable = $false
    }

    # 列表
    if ($line -match '^\s*[-*]\s+(.+)$') {
        $itemText = $matches[1]
        if ($inList -and $listType -ne 'ul') { $out += "</$listType>`r`n"; $inList = $false }
        if (-not $inList) { $out += "<ul>`r`n"; $inList = $true; $listType = 'ul' }
        $out += "<li>" + (InlineMd (EscHtml $itemText)) + "</li>`r`n"
        continue
    }
    if ($line -match '^\s*\d+\.\s+(.+)$') {
        $itemText = $matches[1]
        if ($inList -and $listType -ne 'ol') { $out += "</$listType>`r`n"; $inList = $false }
        if (-not $inList) { $out += "<ol>`r`n"; $inList = $true; $listType = 'ol' }
        $out += "<li>" + (InlineMd (EscHtml $itemText)) + "</li>`r`n"
        continue
    }
    if ($inList -and $line.Trim() -eq '') {
        $out += "</$listType>`r`n"
        $inList = $false
    }

    # 标题
    if ($line -match '^(#{1,6})\s+(.+)$') {
        $level = $matches[1].Length
        $text = $matches[2]
        $out += "<h$level>" + (InlineMd (EscHtml $text)) + "</h$level>`r`n"
        continue
    }

    # 水平线
    if ($line -match '^---+\s*$') {
        $out += "<hr>`r`n"
        continue
    }

    # 引用
    if ($line -match '^>\s*(.+)$') {
        $out += "<blockquote>" + (InlineMd (EscHtml $matches[1])) + "</blockquote>`r`n"
        continue
    }

    # 段落
    if ($line.Trim() -ne '') {
        $out += "<p>" + (InlineMd (EscHtml $line)) + "</p>`r`n"
    }
}

if ($inList) { $out += "</$listType>`r`n" }
if ($inTable) { $out += "</tbody></table>`r`n" }

$css = "body{font-family:'Microsoft YaHei','Source Han Sans CN',sans-serif;max-width:960px;margin:30px auto;padding:0 20px;line-height:1.7;color:#1f2937;background:#fff;}h1{color:#1e3a8a;font-size:28px;border-bottom:3px solid #3b82f6;padding-bottom:12px;margin-top:30px;}h2{color:#1e40af;font-size:22px;border-bottom:1px solid #cbd5e1;padding-bottom:6px;margin-top:30px;}h3{color:#1e3a8a;font-size:18px;margin-top:24px;}h4{color:#334155;font-size:16px;margin-top:18px;}h5,h6{color:#475569;font-size:14px;}p{margin:8px 0;}table{border-collapse:collapse;margin:12px 0;width:100%;font-size:14px;}th,td{border:1px solid #cbd5e1;padding:8px 12px;text-align:left;vertical-align:top;}th{background:#eff6ff;color:#1e40af;font-weight:bold;}tr:nth-child(even) td{background:#f8fafc;}code{background:#f1f5f9;padding:1px 6px;border-radius:3px;color:#be185d;font-family:Consolas,monospace;font-size:90%;}pre{background:#0f172a;color:#e2e8f0;padding:14px 16px;border-radius:6px;overflow-x:auto;font-family:Consolas,monospace;font-size:13px;}ul,ol{margin:8px 0 8px 28px;}li{margin:4px 0;}blockquote{border-left:4px solid #3b82f6;background:#eff6ff;padding:6px 14px;margin:10px 0;color:#1e3a8a;}hr{border:none;border-top:2px solid #e2e8f0;margin:30px 0;}a{color:#2563eb;text-decoration:none;}a:hover{text-decoration:underline;}strong{color:#1e293b;}@media print{body{font-size:11pt;max-width:100%;}h1{page-break-before:always;}h1:first-child{page-break-before:auto;}table,pre,blockquote{page-break-inside:avoid;}pre{background:#f8fafc;color:#1f2937;}}"

$html = "<!DOCTYPE html><html lang=`"zh`"><head><meta charset=`"utf-8`"><title>$title</title><style>$css</style></head><body>$out</body></html>"

[System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.Encoding]::UTF8)
Write-Output ("HTML: " + $htmlPath + " (" + (Get-Item $htmlPath).Length + " bytes)")
