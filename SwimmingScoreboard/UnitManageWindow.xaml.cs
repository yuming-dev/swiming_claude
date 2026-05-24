using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

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
            CountText.Text = string.Format("共 {0} 个单位", _units.Count);
        }

        private void Add_Click(object sender, RoutedEventArgs e) {
            var u = new Unit { Name = "新单位" + (_units.Count + 1) };
            _units.Add(u);
            UnitGrid.SelectedItem = u;
            UnitGrid.ScrollIntoView(u);
            RefreshCountText();
        }

        private void Delete_Click(object sender, RoutedEventArgs e) {
            var sel = UnitGrid.SelectedItems.Cast<Unit>().ToList();
            if (sel.Count == 0) { MessageBox.Show("请先在表格中选中要删除的单位", "提示"); return; }
            if (MessageBox.Show(string.Format("确认删除选中的 {0} 个单位？\n(只删除元信息，不影响运动员的代表队字段)", sel.Count),
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
            MessageBox.Show(string.Format("✔ 已从报名表自动补全 {0} 个新单位", added), "完成");
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV 文件|*.csv",
                FileName = "参赛单位_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("单位名称,简称,领队,教练,队医,基础分,联系电话,地址,备注");
            foreach (var u in _units) {
                sb.AppendLine(string.Join(",", new[] {
                    Esc(u.Name), Esc(u.ShortName), Esc(u.Leader), Esc(u.Coach), Esc(u.Doctor),
                    u.BasePoints.ToString("0.##"),
                    Esc(u.Phone), Esc(u.Address), Esc(u.Note)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("已导出: " + dlg.FileName, "完成");
        }

        private void ImportCsv_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV 文件|*.csv"
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
                MessageBox.Show(string.Format("✔ 新增 {0} 个，更新 {1} 个", added, updated), "完成");
            } catch (Exception ex) {
                MessageBox.Show("导入失败: " + ex.Message, "错误");
            }
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
