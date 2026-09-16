using System;
using System.IO;
using System.Reflection;
using System.Text;
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

        private void ExportDoc_Click(object sender, RoutedEventArgs e) { Save(".doc", "Word 文档|*.doc|所有文件|*.*"); }
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
