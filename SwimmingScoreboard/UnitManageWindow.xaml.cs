using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace SwimmingScoreboard
{
    // 2026-05-24 P0-3 参赛单位管理 — Unit CRUD + 与 Swimmer.Country 联动
    public partial class UnitManageWindow : Window
    {
        private readonly ObservableCollection<Unit> _units;
        private readonly ObservableCollection<Swimmer> _swimmers;
        private List<Unit> _backup;

        public UnitManageWindow(ObservableCollection<Unit> units, ObservableCollection<Swimmer> swimmers) {
            InitializeComponent();
            _units = units;
            _swimmers = swimmers;
            // 快照备份用于取消还原
            _backup = _units.Select(Clone).ToList();
            UnitGrid.ItemsSource = _units;
            RefreshCountText();
        }

        private void RefreshCountText() {
            CountText.Text = Loc.F("Str_Win_UnitMgmt_CountFmt", _units.Count);
        }

        private void Add_Click(object sender, RoutedEventArgs e) {
            var u = new Unit { Name = Loc.T("Str_Win_UnitMgmt_NewUnitPrefix") + (_units.Count + 1) };
            _units.Add(u);
            UnitGrid.SelectedItem = u;
            UnitGrid.ScrollIntoView(u);
            RefreshCountText();
        }

        private void Delete_Click(object sender, RoutedEventArgs e) {
            var sel = UnitGrid.SelectedItems.Cast<Unit>().ToList();
            if (sel.Count == 0) { MessageBox.Show(Loc.T("Str_Win_UnitMgmt_MsgSelectToDelete"), Loc.T("Str_MsgTitle_Info")); return; }
            if (MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgConfirmDeleteFmt", sel.Count),
                Loc.T("Str_MsgTitle_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            foreach (var u in sel) _units.Remove(u);
            RefreshCountText();
        }

        private void AutoFill_Click(object sender, RoutedEventArgs e) {
            var existing = new HashSet<string>(_units.Where(u => !string.IsNullOrEmpty(u.Name)).Select(u => u.Name));
            int added = 0;
            foreach (var s in _swimmers) {
                if (string.IsNullOrEmpty(s.Country)) continue;
                if (existing.Contains(s.Country)) continue;
                _units.Add(new Unit {
                    Name = s.Country,
                    ShortName = s.CountryShort ?? ""
                });
                existing.Add(s.Country);
                added++;
            }
            RefreshCountText();
            MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgAutoFillDoneFmt", added), Loc.T("Str_MsgTitle_Done"));
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = Loc.T("Str_Win_UnitMgmt_CsvFilter"),
                Title = Loc.T("Str_Win_UnitMgmt_ExportTitle"),
                FileName = Loc.T("Str_FileName_Units") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", UnitXlsxHeader));
            foreach (var u in _units) {
                sb.AppendLine(string.Join(",", new[] {
                    Esc(u.Name), Esc(u.ShortName), Esc(u.Leader), Esc(u.Coach), Esc(u.Doctor),
                    u.BasePoints.ToString("0.##"),
                    Esc(u.Phone), Esc(u.Address), Esc(u.Note)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgExportedFmt", dlg.FileName), Loc.T("Str_MsgTitle_Done"));
        }

        private void ImportCsv_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = Loc.T("Str_Win_UnitMgmt_CsvFilter"),
                Title = Loc.T("Str_Win_UnitMgmt_ImportTitle")
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                int added = 0, updated = 0;
                for (int i = 1; i < lines.Length; i++) {
                    var parts = ParseCsvLine(lines[i]);
                    if (parts.Length < 1 || string.IsNullOrEmpty(parts[0])) continue;
                    var existing = _units.FirstOrDefault(u => u.Name == parts[0]);
                    double bp;
                    double parsedBp = parts.Length > 5 && double.TryParse(parts[5], out bp) ? bp : 0;
                    if (existing == null) {
                        _units.Add(new Unit {
                            Name = parts[0],
                            ShortName = parts.Length > 1 ? parts[1] : "",
                            Leader = parts.Length > 2 ? parts[2] : "",
                            Coach = parts.Length > 3 ? parts[3] : "",
                            Doctor = parts.Length > 4 ? parts[4] : "",
                            BasePoints = parsedBp,
                            Phone = parts.Length > 6 ? parts[6] : "",
                            Address = parts.Length > 7 ? parts[7] : "",
                            Note = parts.Length > 8 ? parts[8] : ""
                        });
                        added++;
                    } else {
                        if (parts.Length > 1) existing.ShortName = parts[1];
                        if (parts.Length > 2) existing.Leader = parts[2];
                        if (parts.Length > 3) existing.Coach = parts[3];
                        if (parts.Length > 4) existing.Doctor = parts[4];
                        if (parts.Length > 5) existing.BasePoints = parsedBp;
                        if (parts.Length > 6) existing.Phone = parts[6];
                        if (parts.Length > 7) existing.Address = parts[7];
                        if (parts.Length > 8) existing.Note = parts[8];
                        updated++;
                    }
                }
                RefreshCountText();
                MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgImportDoneFmt", added, updated), Loc.T("Str_MsgTitle_Done"));
            } catch (Exception ex) {
                MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgImportFailFmt", ex.Message), Loc.T("Str_MsgTitle_Error"));
            }
        }

        // 2026-05-25 用 Excel/WPS 外部编辑 → 回灌
        // 表头与 DataGrid 列一致 (9 列), 严格校验; 回灌成功后整体替换 _units
        private static string[] UnitXlsxHeader {
            get {
                return new[] {
                    Loc.T("Str_Win_UnitMgmt_ColName"), Loc.T("Str_DocC_ColShortName"), Loc.T("Str_Win_UnitMgmt_ColLeader"),
                    Loc.T("Str_Win_UnitMgmt_ColCoach"), Loc.T("Str_Win_UnitMgmt_ColDoctor"), Loc.T("Str_Win_UnitMgmt_ColBasePoints"),
                    Loc.T("Str_DocC_ColPhone"), Loc.T("Str_Win_UnitMgmt_ColAddress"), Loc.T("Str_Col_Notes")
                };
            }
        }
        private string _editTempPath;

        private void EditInExcel_Click(object sender, RoutedEventArgs e) {
            try {
                string tmp = Path.Combine(Path.GetTempPath(),
                    Loc.T("Str_FileName_Units") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                WriteUnitsToXlsx(tmp);
                _editTempPath = tmp;
                try { Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true }); }
                catch (Exception ex) {
                    MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgOpenFailFmt", ex.Message, tmp), Loc.T("Str_MsgTitle_Info"));
                }
                // 模态等待对话框: 编辑完成回灌 / 取消
                var dlg = new Window {
                    Title = Loc.T("Str_Win_UnitMgmt_XlsxTempTitle"), Width = 460, Height = 220,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                    ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.WhiteSmoke
                };
                var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                sp.Children.Add(new System.Windows.Controls.TextBlock {
                    Text = Loc.T("Str_Win_UnitMgmt_XlsxOpenedLine1"),
                    FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6)
                });
                sp.Children.Add(new System.Windows.Controls.TextBlock {
                    Text = Loc.T("Str_Win_UnitMgmt_XlsxSteps"),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = System.Windows.Media.Brushes.DarkSlateGray
                });
                sp.Children.Add(new System.Windows.Controls.TextBlock {
                    Text = tmp, FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
                });
                var btns = new System.Windows.Controls.StackPanel {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                var bOk = new System.Windows.Controls.Button {
                    Content = Loc.T("Str_Win_UnitMgmt_XlsxDoneBtn"), Padding = new Thickness(14, 6, 14, 6),
                    Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#16A34A")),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var bCancel = new System.Windows.Controls.Button {
                    Content = Loc.T("Str_Btn_Cancel"), Padding = new Thickness(14, 6, 14, 6),
                    Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#64748B")),
                    Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0)
                };
                btns.Children.Add(bOk); btns.Children.Add(bCancel);
                sp.Children.Add(btns);
                dlg.Content = sp;
                bOk.Click += delegate { dlg.DialogResult = true; };
                bCancel.Click += delegate { dlg.DialogResult = false; };
                if (dlg.ShowDialog() == true) {
                    ReadbackUnitsFromXlsx(tmp);
                } else {
                    try { File.Delete(tmp); } catch { }
                }
            } catch (Exception ex) {
                MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgLaunchFailFmt", ex.Message), Loc.T("Str_MsgTitle_Error"));
            }
        }

        private void WriteUnitsToXlsx(string path) {
            var wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet(Loc.T("Str_FileName_Units"));
            var headerRow = sheet.CreateRow(0);
            for (int c = 0; c < UnitXlsxHeader.Length; c++)
                headerRow.CreateCell(c).SetCellValue(UnitXlsxHeader[c]);
            int r = 1;
            foreach (var u in _units) {
                var row = sheet.CreateRow(r++);
                row.CreateCell(0).SetCellValue(u.Name ?? "");
                row.CreateCell(1).SetCellValue(u.ShortName ?? "");
                row.CreateCell(2).SetCellValue(u.Leader ?? "");
                row.CreateCell(3).SetCellValue(u.Coach ?? "");
                row.CreateCell(4).SetCellValue(u.Doctor ?? "");
                row.CreateCell(5).SetCellValue(u.BasePoints);
                row.CreateCell(6).SetCellValue(u.Phone ?? "");
                row.CreateCell(7).SetCellValue(u.Address ?? "");
                row.CreateCell(8).SetCellValue(u.Note ?? "");
            }
            for (int c = 0; c < UnitXlsxHeader.Length; c++) sheet.AutoSizeColumn(c);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) wb.Write(fs);
        }

        private void ReadbackUnitsFromXlsx(string path) {
            if (!File.Exists(path)) { MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgTempMissingFmt", path), Loc.T("Str_MsgTitle_Error")); return; }
            IWorkbook wb;
            try {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    wb = new XSSFWorkbook(fs);
            } catch (IOException) {
                MessageBox.Show(Loc.T("Str_Win_UnitMgmt_MsgFileLocked"), Loc.T("Str_MsgTitle_Info")); return;
            } catch (Exception ex) {
                MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgReadFailFmt", ex.Message), Loc.T("Str_MsgTitle_Error")); return;
            }
            var sheet = wb.GetSheetAt(0);
            if (sheet == null) { MessageBox.Show(Loc.T("Str_Win_UnitMgmt_MsgNoSheet"), Loc.T("Str_MsgTitle_Error")); return; }

            // 表头严格校验 (UnitXlsxHeader 现按 Loc.T 现取; 写入与回读发生在同一次操作里,
            // 中间隔着模态"等待"窗口, 用户没法在这期间切语言, 两边天然保持一致)
            var head = sheet.GetRow(0);
            if (head == null || head.LastCellNum < UnitXlsxHeader.Length) {
                MessageBox.Show(Loc.T("Str_Win_UnitMgmt_MsgHeaderCountBad"), Loc.T("Str_MsgTitle_Error"));
                return;
            }
            for (int c = 0; c < UnitXlsxHeader.Length; c++) {
                string actual = head.GetCell(c) != null ? (head.GetCell(c).ToString() ?? "").Trim() : "";
                if (actual != UnitXlsxHeader[c]) {
                    MessageBox.Show(Loc.F(
                        "Str_Win_UnitMgmt_MsgHeaderMismatchFmt",
                        c + 1, UnitXlsxHeader[c], actual), Loc.T("Str_MsgTitle_Error"));
                    return;
                }
            }

            var newUnits = new List<Unit>();
            var skipped = new List<string>();
            var seenNames = new HashSet<string>();
            for (int r = 1; r <= sheet.LastRowNum; r++) {
                var row = sheet.GetRow(r); if (row == null) continue;
                string name = CellStr(row.GetCell(0));
                if (string.IsNullOrEmpty(name)) continue;     // 全空行跳过
                if (seenNames.Contains(name)) {
                    skipped.Add(Loc.F("Str_Win_UnitMgmt_MsgDupNameFmt", r + 1, name));
                    continue;
                }
                seenNames.Add(name);
                double bp = 0;
                var bpCell = row.GetCell(5);
                if (bpCell != null) {
                    try {
                        if (bpCell.CellType == CellType.Numeric) bp = bpCell.NumericCellValue;
                        else double.TryParse(CellStr(bpCell), out bp);
                    } catch { bp = 0; }
                }
                newUnits.Add(new Unit {
                    Name = name,
                    ShortName = CellStr(row.GetCell(1)),
                    Leader = CellStr(row.GetCell(2)),
                    Coach = CellStr(row.GetCell(3)),
                    Doctor = CellStr(row.GetCell(4)),
                    BasePoints = bp,
                    Phone = CellStr(row.GetCell(6)),
                    Address = CellStr(row.GetCell(7)),
                    Note = CellStr(row.GetCell(8))
                });
            }

            // 二次确认：整体替换
            var res = MessageBox.Show(Loc.F(
                "Str_Win_UnitMgmt_MsgConfirmReadbackFmt",
                _units.Count, newUnits.Count, skipped.Count),
                Loc.T("Str_MsgTitle_Confirm"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (res != MessageBoxResult.OK) return;

            _units.Clear();
            foreach (var u in newUnits) _units.Add(u);
            RefreshCountText();

            var sb = new StringBuilder();
            sb.Append(Loc.F("Str_Win_UnitMgmt_MsgReadbackDoneFmt", newUnits.Count));
            if (skipped.Count > 0) {
                sb.AppendLine(Loc.T("Str_Win_UnitMgmt_MsgSkippedDetail"));
                foreach (var s in skipped) sb.AppendLine("• " + s);
            }
            MessageBox.Show(sb.ToString(), Loc.T("Str_MsgTitle_Done"));

            try { File.Delete(path); } catch { }
            _editTempPath = null;
        }

        private static string CellStr(ICell c) {
            if (c == null) return "";
            try {
                if (c.CellType == CellType.Numeric) {
                    if (DateUtil.IsCellDateFormatted(c)) return c.DateCellValue.ToString("yyyy-MM-dd");
                    return c.NumericCellValue.ToString();
                }
                return (c.ToString() ?? "").Trim();
            } catch { return ""; }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            // 还原到打开时的状态
            _units.Clear();
            foreach (var u in _backup) _units.Add(u);
            DialogResult = false;
            Close();
        }

        private static Unit Clone(Unit u) {
            return new Unit {
                Name = u.Name, ShortName = u.ShortName,
                Leader = u.Leader, Coach = u.Coach, Doctor = u.Doctor,
                BasePoints = u.BasePoints,
                Phone = u.Phone, Address = u.Address, Note = u.Note
            };
        }

        private static string Esc(string s) {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string[] ParseCsvLine(string line) {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++) {
                char c = line[i];
                if (inQuotes) {
                    if (c == '"') {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    } else sb.Append(c);
                } else {
                    if (c == ',') { result.Add(sb.ToString()); sb.Length = 0; }
                    else if (c == '"') inQuotes = true;
                    else sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}
