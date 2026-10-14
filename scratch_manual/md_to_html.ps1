<#
2026-10-14 轻量 Markdown -> HTML 转换器，专为本仓库两份使用说明书(.md)写的，只支持它们实际用到的
语法子集：# ## ### #### 标题 / **粗体** / *斜体* / `行内代码` / ```代码块``` / 管道表格(含对齐冒号,
但渲染时忽略对齐) / - 无序列表 / > 单行引用 / --- 水平线 / ![说明](路径) 图片 / 空行分段的普通段落。
不支持链接、有序列表、嵌套列表、多行引用——这些本文档都没用到。
样式沿用上一版人工撰写的 HTML 模板(内嵌 CSS)，保证 PDF/DOCX 转换出来观感一致。
#>
param(
    [Parameter(Mandatory=$true)][string]$MdPath,
    [Parameter(Mandatory=$true)][string]$HtmlPath,
    [Parameter(Mandatory=$true)][string]$Lang,   # "zh" or "en"
    [Parameter(Mandatory=$true)][string]$Title,
    [Parameter(Mandatory=$true)][string]$HtmlLangAttr
)

$ErrorActionPreference = 'Stop'

function Escape-Html([string]$s) {
    $s = $s -replace '&', '&amp;'
    $s = $s -replace '<', '&lt;'
    $s = $s -replace '>', '&gt;'
    return $s
}

# 行内转换: 先保护代码span, 再处理粗体/斜体/图片, 最后把代码span换回转义后的<code>
function Convert-Inline([string]$line) {
    $codes = New-Object System.Collections.Generic.List[string]
    # 保护 `...` 行内代码, 用占位符替换, 避免里面的 * 被当成斜体处理
    $line = [regex]::Replace($line, '`([^`]+)`', {
        param($m)
        $codes.Add((Escape-Html $m.Groups[1].Value))
        return "@@CODE" + ($codes.Count - 1) + "@@"
    })
    # 图片 ![caption](path)
    $line = [regex]::Replace($line, '!\[([^\]]*)\]\(([^)]+)\)', {
        param($m)
        $cap = Escape-Html $m.Groups[1].Value
        $src = $m.Groups[2].Value
        return "@@IMG@@$src@@CAP@@$cap@@ENDIMG@@"
    })
    $line = Escape-Html $line
    # 粗体 **text**
    $line = [regex]::Replace($line, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
    # 斜体 *text* (粗体已消耗掉双星号, 剩下的单星号就是斜体, 不会误吃)
    $line = [regex]::Replace($line, '\*([^*]+)\*', '<em>$1</em>')
    # 还原代码span
    for ($i = 0; $i -lt $codes.Count; $i++) {
        $line = $line -replace [regex]::Escape("@@CODE$i@@"), ('<code>' + $codes[$i] + '</code>')
    }
    # 还原图片(此时 caption 已经被 Escape-Html 转义过一次, 不需要再转)
    $line = [regex]::Replace($line, '@@IMG@@([^@]+)@@CAP@@([^@]*)@@ENDIMG@@', {
        param($m)
        $src = $m.Groups[1].Value
        $cap = $m.Groups[2].Value
        # Word 导出PDF/DOCX时不认 CSS 的 figure img{max-width:100%}, 原图是2586px宽实机截图,
        # 不显式限宽的话会在页面右侧出血裁切——直接写 width 属性(像素), Word认这个
        return "<figure><img src=`"$src`" alt=`"$cap`" width=`"680`"><figcaption>$cap</figcaption></figure>"
    })
    return $line
}

function Convert-TableRow([string]$row) {
    $row = $row.Trim()
    $row = $row -replace '^\|', '' -replace '\|$', ''
    return $row -split '\|'
}

$lines = Get-Content -LiteralPath $MdPath -Encoding UTF8
$out = New-Object System.Collections.Generic.List[string]

$i = 0
$n = $lines.Count
$inCode = $false
$codeBuf = New-Object System.Collections.Generic.List[string]
$paraBuf = New-Object System.Collections.Generic.List[string]

function Flush-Para {
    if ($paraBuf.Count -gt 0) {
        $text = ($paraBuf -join ' ')
        $out.Add('<p>' + (Convert-Inline $text) + '</p>')
        $paraBuf.Clear()
    }
}

while ($i -lt $n) {
    $line = $lines[$i]

    # 代码块
    if ($line -match '^```') {
        Flush-Para
        if (-not $inCode) {
            $inCode = $true
            $codeBuf.Clear()
        } else {
            $inCode = $false
            $out.Add('<pre>' + (Escape-Html ($codeBuf -join "`n")) + '</pre>')
        }
        $i++
        continue
    }
    if ($inCode) {
        $codeBuf.Add($line)
        $i++
        continue
    }

    # 空行
    if ($line.Trim() -eq '') {
        Flush-Para
        $i++
        continue
    }

    # 标题
    if ($line -match '^(#{1,4})\s+(.*)$') {
        Flush-Para
        $level = $Matches[1].Length
        $text = Convert-Inline $Matches[2].Trim()
        $out.Add("<h$level>$text</h$level>")
        $i++
        continue
    }

    # 水平线 (纯 --- 独占一行, 跟表格分隔行区分: 表格分隔行含 |)
    if ($line.Trim() -match '^-{3,}$') {
        Flush-Para
        $out.Add('<hr>')
        $i++
        continue
    }

    # 引用 (单行)
    if ($line -match '^>\s?(.*)$') {
        Flush-Para
        $text = Convert-Inline $Matches[1].Trim()
        $out.Add("<blockquote><p>$text</p></blockquote>")
        $i++
        continue
    }

    # 表格: 当前行以 | 开头, 下一行是分隔行 (---|---)
    if ($line.TrimStart() -match '^\|' -and ($i + 1) -lt $n -and $lines[$i+1] -match '^\s*\|?[\s:|-]+\|?\s*$' -and $lines[$i+1] -match '-') {
        Flush-Para
        $headerCells = Convert-TableRow $line
        $i += 2   # 跳过表头和分隔行
        $rows = New-Object System.Collections.Generic.List[object]
        while ($i -lt $n -and $lines[$i].TrimStart() -match '^\|') {
            $rows.Add((Convert-TableRow $lines[$i]))
            $i++
        }
        $tbl = New-Object System.Collections.Generic.List[string]
        $tbl.Add('<table><thead><tr>')
        foreach ($c in $headerCells) { $tbl.Add('<th>' + (Convert-Inline $c.Trim()) + '</th>') }
        $tbl.Add('</tr></thead><tbody>')
        foreach ($r in $rows) {
            $tbl.Add('<tr>')
            foreach ($c in $r) { $tbl.Add('<td>' + (Convert-Inline $c.Trim()) + '</td>') }
            $tbl.Add('</tr>')
        }
        $tbl.Add('</tbody></table>')
        $out.Add(($tbl -join ''))
        continue
    }

    # 无序列表
    if ($line -match '^\s*-\s+(.*)$') {
        Flush-Para
        $items = New-Object System.Collections.Generic.List[string]
        while ($i -lt $n -and $lines[$i] -match '^\s*-\s+(.*)$') {
            $items.Add((Convert-Inline $Matches[1]))
            $i++
        }
        $out.Add('<ul>' + (($items | ForEach-Object { "<li>$_</li>" }) -join '') + '</ul>')
        continue
    }

    # 普通段落行, 累积到换行或遇到其它结构
    $paraBuf.Add($line.Trim())
    $i++
}
Flush-Para

$bodyHtml = ($out -join "`n")

$title = $Title
$htmlLang = $HtmlLangAttr

$css = "body{font-family:'Microsoft YaHei','Source Han Sans CN',sans-serif;max-width:960px;margin:30px auto;padding:0 20px;line-height:1.7;color:#1f2937;background:#fff;}h1{color:#1e3a8a;font-size:28px;border-bottom:3px solid #3b82f6;padding-bottom:12px;margin-top:30px;}h2{color:#1e40af;font-size:22px;border-bottom:1px solid #cbd5e1;padding-bottom:6px;margin-top:30px;}h3{color:#1e3a8a;font-size:18px;margin-top:24px;}h4{color:#334155;font-size:16px;margin-top:18px;}h5,h6{color:#475569;font-size:14px;}p{margin:8px 0;}table{border-collapse:collapse;margin:12px 0;width:100%;font-size:14px;}th,td{border:1px solid #cbd5e1;padding:8px 12px;text-align:left;vertical-align:top;}th{background:#eff6ff;color:#1e40af;font-weight:bold;}tr:nth-child(even) td{background:#f8fafc;}code{background:#f1f5f9;padding:1px 6px;border-radius:3px;color:#be185d;font-family:Consolas,monospace;font-size:90%;}pre{background:#0f172a;color:#e2e8f0;padding:14px 16px;border-radius:6px;overflow-x:auto;font-family:Consolas,monospace;font-size:13px;}ul,ol{margin:8px 0 8px 28px;}li{margin:4px 0;}blockquote{border-left:4px solid #3b82f6;background:#eff6ff;padding:6px 14px;margin:10px 0;color:#1e3a8a;}hr{border:none;border-top:2px solid #e2e8f0;margin:30px 0;}a{color:#2563eb;text-decoration:none;}a:hover{text-decoration:underline;}strong{color:#1e293b;}figure{margin:16px 0;text-align:center;}figure img{max-width:100%;border:1px solid #cbd5e1;border-radius:6px;box-shadow:0 1px 4px rgba(0,0,0,.08);}figcaption{margin-top:6px;font-size:13px;color:#64748b;}@media print{figure{page-break-inside:avoid;}body{font-size:11pt;max-width:100%;}h1{page-break-before:always;}h1:first-child{page-break-before:auto;}table,pre,blockquote{page-break-inside:avoid;}pre{background:#f8fafc;color:#1f2937;}}"

$full = "<!DOCTYPE html><html lang=`"$htmlLang`"><head><meta charset=`"utf-8`"><title>$title</title><style>$css</style></head><body>`n$bodyHtml`n</body></html>"

# 不带 BOM 的 UTF8, 跟仓库里其它网页文件一致
[System.IO.File]::WriteAllText($HtmlPath, $full, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "HTML written: $HtmlPath ($(((Get-Item $HtmlPath).Length)) bytes)"
