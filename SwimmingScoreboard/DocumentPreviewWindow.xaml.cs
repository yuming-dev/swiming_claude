using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace SwimmingScoreboard
{
    public partial class DocumentPreviewWindow : Window
    {
        private string _title;
        private string _suggestedName;

        public DocumentPreviewWindow(string title, string html) {
            InitializeComponent();
            _title = title ?? "文档";
            _suggestedName = SafeFileName(_title) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
            TitleText.Text = "文档预览 / 输出 — " + _title;
            HtmlEditor.Text = html ?? "";
            RenderPreview();
        }

        private static string SafeFileName(string s) {
            if (string.IsNullOrEmpty(s)) return "文档";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private string CurrentHtml {
            get { return HtmlEditor.Text ?? ""; }
        }

        private void RenderPreview() {
            try { Preview.NavigateToString(CurrentHtml.Length > 0 ? CurrentHtml : "<html><body></body></html>"); }
            catch (Exception ex) { MessageBox.Show("预览失败：" + ex.Message); }
        }

        private string WriteToTemp(string ext) {
            string tmp = Path.Combine(Path.GetTempPath(), _suggestedName + "." + ext);
            File.WriteAllText(tmp, CurrentHtml, Encoding.UTF8);
            return tmp;
        }

        // 2026-05-25 删除「编辑源码 (HTML)」Tab; Refresh_Click 也不再需要 (XAML 不再引用)
        private void OpenBrowser_Click(object sender, RoutedEventArgs e) {
            try {
                string p = WriteToTemp("html");
                System.Diagnostics.Process.Start(p);
            } catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message); }
        }

        /// <summary>
        /// 2026-09-16 "导出 PDF" 直接出 PDF 文件。
        ///
        /// 原来它只是把 HTML 在浏览器里打开, 再弹一句"请按 Ctrl+P 选 Print to PDF" ——
        /// 按钮叫"导出 PDF", 按下去却没有 PDF, 名不副实(用户点名"成绩册"这个窗口)。
        /// "项目成绩打印"/"按组别批量打印" 09-03 已经改过同一个按钮, 这里补上同一条路:
        /// Edge/Chrome 无头模式 --print-to-pdf, 直接落地一个真正的 PDF 文件。
        /// 这台机器上确实没有 Edge/Chrome 时才退回原来那套, 并明说为什么。
        /// </summary>
        private void ExportPdf_Click(object sender, RoutedEventArgs e) {
            try {
                var dlg = new Microsoft.Win32.SaveFileDialog {
                    Filter = "PDF 文件|*.pdf|所有文件|*.*",
                    FileName = _suggestedName + ".pdf",
                    Title = "导出 PDF"
                };
                if (dlg.ShowDialog() != true) return;
                string tmpHtml = WriteToTemp("html");
                if (MainWindow.TryHtmlToPdf(tmpHtml, dlg.FileName)) {
                    if (MessageBox.Show("已导出：\n" + dlg.FileName + "\n\n是否立即打开？", "导出 PDF",
                            MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        System.Diagnostics.Process.Start(dlg.FileName);
                    return;
                }
                System.Diagnostics.Process.Start(tmpHtml);
                MessageBox.Show("这台机器上没找到 Edge 或 Chrome，无法直接生成 PDF。\n\n"
                    + "已在浏览器中打开，请按 Ctrl+P，打印机选 \"Microsoft Print to PDF\" 另存为 PDF。",
                    "导出 PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            } catch (Exception ex) { MessageBox.Show("导出 PDF 失败：" + ex.Message); }
        }

        /// <summary>
        /// 2026-09-16 DOC 导出补页码。
        ///
        /// 秩序册/成绩册等用的是 CSS3 Paged Media 的 counter(page)(给 Chrome 打 PDF 用,
        /// 已验证有效), 但 Word 打开"HTML 存成 .doc"这条路时走的是它自己的老式 HTML
        /// 导入器, 根本不认 @bottom-center/counter(page) —— 这段 CSS 到 Word 里就是废纸。
        /// Word 认的是它自己那套 mso-* 扩展: @page 关联一个 mso-footer 的 div, 里面用
        /// mso-field-code 放 PAGE/NUMPAGES 域 —— Word 打开/打印时会自己把域换成"第几页/
        /// 共几页", 翻页也跟着自动更新, 效果上等价于 Chrome 那边的页码。
        /// (这段没有实机 Word 环境能验证渲染效果, 是 Word HTML 导入 mso-footer 的标准写法。)
        /// </summary>
        private static string InjectWordPageNumberFooter(string html) {
            if (string.IsNullOrEmpty(html)) return html;
            string result = Regex.Replace(html, "<html[^>]*>",
                "<html xmlns:o='urn:schemas-microsoft-com:office:office' xmlns:w='urn:schemas-microsoft-com:office:word' xmlns='http://www.w3.org/TR/REC-html40'>",
                RegexOptions.IgnoreCase);

            string footerCss = "@page Section1 { mso-footer: f1; } "
                + "div.Section1 { page: Section1; } "
                + "p.MsoFooter, li.MsoFooter, div.MsoFooter { margin:0; text-align:center; font-size:9pt; color:#666; } ";
            int styleClose = result.IndexOf("</style>", StringComparison.OrdinalIgnoreCase);
            if (styleClose >= 0) result = result.Insert(styleClose, footerCss);

            string footerDiv = "<div style='mso-element:footer' id=f1>"
                + "<p class=MsoFooter>第&nbsp;<span style='mso-field-code:\" PAGE \"'></span>"
                + "&nbsp;页&nbsp;共&nbsp;<span style='mso-field-code:\" NUMPAGES \"'></span>&nbsp;页</p></div>";

            int bodyTagIdx = result.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            int bodyOpenEnd = bodyTagIdx >= 0 ? result.IndexOf('>', bodyTagIdx) : -1;
            int bodyCloseIdx = result.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyOpenEnd > 0 && bodyCloseIdx > bodyOpenEnd) {
                result = result.Insert(bodyCloseIdx, "</div>" + footerDiv);
                result = result.Insert(bodyOpenEnd + 1, "<div class=Section1>");
            }
            return result;
        }

        private void ExportDoc_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "Word 文档|*.doc|所有文件|*.*",
                FileName = _suggestedName + ".doc",
                Title = "导出 DOC"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                File.WriteAllText(dlg.FileName, InjectWordPageNumberFooter(CurrentHtml), Encoding.UTF8);
                if (MessageBox.Show("导出完成：\n" + dlg.FileName + "\n\n是否立即打开？", "导出成功",
                                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                    System.Diagnostics.Process.Start(dlg.FileName);
                }
            } catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message); }
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e) { Save(".html", "HTML 文件|*.html|所有文件|*.*"); }

        private void Save(string ext, string filter) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = filter,
                FileName = _suggestedName + ext,
                Title = "导出 " + ext.TrimStart('.').ToUpper()
            };
            if (dlg.ShowDialog() != true) return;
            try {
                File.WriteAllText(dlg.FileName, CurrentHtml, Encoding.UTF8);
                if (MessageBox.Show("导出完成：\n" + dlg.FileName + "\n\n是否立即打开？", "导出成功",
                                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                    System.Diagnostics.Process.Start(dlg.FileName);
                }
            } catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message); }
        }

        private void Print_Click(object sender, RoutedEventArgs e) {
            // WebBrowser 内嵌 IE：通过 mshtml IOleCommandTarget 调用 OLECMDID_PRINT
            try {
                dynamic doc = Preview.Document;
                if (doc != null) doc.execCommand("Print", true, null);
                else MessageBox.Show("文档尚未渲染完成，请稍候再试。");
            } catch (Exception ex) {
                MessageBox.Show("调用打印失败：" + ex.Message + "\n请改用\"在浏览器中打开\"后通过浏览器打印。");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
