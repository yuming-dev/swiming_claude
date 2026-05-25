using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace SwimmingScoreboard
{
    // 2026-05-24 P0-D 工作人员管理（5 组）
    public partial class StaffManageWindow : Window
    {
        private readonly ObservableCollection<StaffMember> _staff;
        private readonly List<StaffMember> _backup;
        private CollectionView _view;

        public StaffManageWindow(ObservableCollection<StaffMember> staff) {
            InitializeComponent();
            _staff = staff;
            // 2026-05-25 旧分组名迁移（'组委会'→'组织委员会' 等）
            foreach (var s in _staff) s.Group = StaffGroups.Migrate(s.Group);
            // 2026-05-25 _staff 空 → 按 5 组默认岗位骨架自动播种（姓名留空）
            if (_staff.Count == 0) SeedDefaults();
            _backup = _staff.Select(Clone).ToList();
            StaffGrid.ItemsSource = _staff;
            _view = (CollectionView)CollectionViewSource.GetDefaultView(_staff);
            _view.Filter = FilterPredicate;
            RefreshCountText();
        }

        private void SeedDefaults() {
            foreach (var g in StaffGroups.All) {
                string[] titles;
                if (!StaffGroups.DefaultTitles.TryGetValue(g, out titles)) continue;
                foreach (var t in titles) {
                    _staff.Add(new StaffMember { Group = g, Title = t });
                }
            }
        }

        private bool FilterPredicate(object item) {
            var s = item as StaffMember;
            if (s == null) return false;
            string groupSel = GroupFilterCombo.SelectedItem != null
                ? ((ComboBoxItem)GroupFilterCombo.SelectedItem).Content.ToString() : "全部";
            if (groupSel != "全部" && (s.Group ?? "") != groupSel) return false;
            string q = (SearchBox.Text ?? "").Trim().ToLower();
            if (string.IsNullOrEmpty(q)) return true;
            return (s.Name ?? "").ToLower().Contains(q)
                || (s.Title ?? "").ToLower().Contains(q)
                || (s.Country ?? "").ToLower().Contains(q)
                || (s.Phone ?? "").ToLower().Contains(q);
        }

        private void RefreshCountText() {
            var counts = StaffGroups.All.ToDictionary(g => g, g => _staff.Count(s => (s.Group ?? "") == g));
            CountText.Text = string.Format("总 {0} 人 / 主席团 {1} / 组织委员会 {2} / 工作机构 {3} / 技术及仲裁 {4} / 裁判员 {5}",
                _staff.Count, counts[StaffGroups.Presidium], counts[StaffGroups.OrgCommittee],
                counts[StaffGroups.WorkOrg], counts[StaffGroups.TechArbitration], counts[StaffGroups.Referees]);
        }

        private void GroupFilter_Changed(object sender, SelectionChangedEventArgs e) {
            if (_view != null) _view.Refresh();
        }
        private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) {
            if (_view != null) _view.Refresh();
        }

        private string CurrentGroupSelection() {
            string g = GroupFilterCombo.SelectedItem != null
                ? ((ComboBoxItem)GroupFilterCombo.SelectedItem).Content.ToString() : "全部";
            return g == "全部" ? StaffGroups.Referees : g;   // 默认新增到 裁判员 (最常用)
        }

        private void Add_Click(object sender, RoutedEventArgs e) {
            var sm = new StaffMember { Group = CurrentGroupSelection(), Title = "（请填岗位）", Name = "" };
            _staff.Add(sm);
            StaffGrid.SelectedItem = sm;
            StaffGrid.ScrollIntoView(sm);
            RefreshCountText();
        }

        private void Delete_Click(object sender, RoutedEventArgs e) {
            var sel = StaffGrid.SelectedItems.Cast<StaffMember>().ToList();
            if (sel.Count == 0) { MessageBox.Show("请先在表格中选中要删除的人员", "提示"); return; }
            if (MessageBox.Show(string.Format("确认删除 {0} 名工作人员？", sel.Count),
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            foreach (var s in sel) _staff.Remove(s);
            RefreshCountText();
        }

        private void LoadDefault_Click(object sender, RoutedEventArgs e) {
            string g = CurrentGroupSelection();
            string[] titles;
            if (!StaffGroups.DefaultTitles.TryGetValue(g, out titles) || titles.Length == 0) {
                MessageBox.Show("该组没有预设岗位", "提示"); return;
            }
            int existing = _staff.Count(s => (s.Group ?? "") == g);
            if (existing > 0) {
                if (MessageBox.Show(string.Format("当前 {0} 已有 {1} 条记录。\n是否仍然追加预设岗位（不删除已有项）？", g, existing),
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            }
            int added = 0;
            var alreadyTitles = new HashSet<string>(_staff.Where(s => (s.Group ?? "") == g)
                                                          .Select(s => s.Title ?? ""));
            foreach (var t in titles) {
                if (alreadyTitles.Contains(t)) continue;
                _staff.Add(new StaffMember { Group = g, Title = t, Name = "" });
                added++;
            }
            RefreshCountText();
            MessageBox.Show(string.Format("✔ 已为「{0}」追加 {1} 个预设岗位（姓名待填）", g, added), "完成");
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV 文件|*.csv",
                FileName = "工作人员_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("分组,工作岗位,姓名,性别,裁判等级,工作单位,电话,备注");
            foreach (var s in _staff) {
                sb.AppendLine(string.Join(",", new[] {
                    Esc(s.Group), Esc(s.Title), Esc(s.Name), Esc(s.Gender), Esc(s.RefereeLevel),
                    Esc(s.Country), Esc(s.Phone), Esc(s.Note)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("已导出: " + dlg.FileName, "完成");
        }

        private void ImportCsv_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV 文件|*.csv" };
            if (dlg.ShowDialog() != true) return;
            try {
                var lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                int added = 0;
                for (int i = 1; i < lines.Length; i++) {
                    var parts = ParseCsvLine(lines[i]);
                    if (parts.Length < 2) continue;
                    if (string.IsNullOrEmpty(parts[0]) && string.IsNullOrEmpty(parts[1])) continue;
                    _staff.Add(new StaffMember {
                        Group = StaffGroups.Migrate(parts[0]),
                        Title = parts.Length > 1 ? parts[1] : "",
                        Name = parts.Length > 2 ? parts[2] : "",
                        Gender = parts.Length > 3 ? parts[3] : "",
                        RefereeLevel = parts.Length > 4 ? parts[4] : "",
                        Country = parts.Length > 5 ? parts[5] : "",
                        Phone = parts.Length > 6 ? parts[6] : "",
                        Note = parts.Length > 7 ? parts[7] : ""
                    });
                    added++;
                }
                RefreshCountText();
                MessageBox.Show(string.Format("✔ 已导入 {0} 名", added), "完成");
            } catch (Exception ex) {
                MessageBox.Show("导入失败: " + ex.Message, "错误");
            }
        }

        // 2026-05-25 导出花名册 (Excel) — 每组一个 Sheet，裁判员组含 裁判等级 列
        private void ExportRoster_Click(object sender, RoutedEventArgs e) {
            int perPage = StylePerPage8.IsChecked == true ? 8 : (StylePerPage6.IsChecked == true ? 6 : 4);
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "Excel 工作簿|*.xlsx",
                FileName = "工作人员花名册_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var wb = new XSSFWorkbook();
                foreach (var grp in StaffGroups.All) {
                    var members = _staff.Where(s => (s.Group ?? "") == grp).ToList();
                    string safeName = grp;
                    if (safeName.Length > 31) safeName = safeName.Substring(0, 31);
                    var sheet = wb.CreateSheet(safeName);
                    int r = 0;
                    var titleRow = sheet.CreateRow(r++);
                    titleRow.CreateCell(0).SetCellValue("工作人员管理 < 可选 >  ·  " + grp + "   (样式: " + perPage + " 张/页)");
                    bool isReferees = grp == StaffGroups.Referees;
                    var hr = sheet.CreateRow(r++);
                    hr.CreateCell(0).SetCellValue("序号");
                    hr.CreateCell(1).SetCellValue("工作岗位");
                    hr.CreateCell(2).SetCellValue("姓名");
                    hr.CreateCell(3).SetCellValue("性别");
                    if (isReferees) {
                        hr.CreateCell(4).SetCellValue("裁判等级");
                        hr.CreateCell(5).SetCellValue("电话");
                    } else {
                        hr.CreateCell(4).SetCellValue("电话");
                        hr.CreateCell(5).SetCellValue("工作单位");
                    }
                    hr.CreateCell(6).SetCellValue("备注");
                    int idx = 1;
                    foreach (var s in members) {
                        var row = sheet.CreateRow(r++);
                        row.CreateCell(0).SetCellValue(idx++);
                        row.CreateCell(1).SetCellValue(s.Title ?? "");
                        row.CreateCell(2).SetCellValue(s.Name ?? "");
                        row.CreateCell(3).SetCellValue(s.Gender ?? "");
                        if (isReferees) {
                            row.CreateCell(4).SetCellValue(s.RefereeLevel ?? "");
                            row.CreateCell(5).SetCellValue(s.Phone ?? "");
                        } else {
                            row.CreateCell(4).SetCellValue(s.Phone ?? "");
                            row.CreateCell(5).SetCellValue(s.Country ?? "");
                        }
                        row.CreateCell(6).SetCellValue(s.Note ?? "");
                    }
                    for (int c = 0; c < 7; c++) sheet.AutoSizeColumn(c);
                }
                using (var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write)) wb.Write(fs);
                MessageBox.Show("已导出: " + dlg.FileName + "\n样式: " + perPage + " 张/页", "完成");
            } catch (Exception ex) {
                MessageBox.Show("导出失败: " + ex.Message, "错误");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            // 简单校验：每行必填 分组 + 岗位
            int incomplete = _staff.Count(s => string.IsNullOrEmpty(s.Group) || string.IsNullOrEmpty(s.Title));
            if (incomplete > 0) {
                if (MessageBox.Show(string.Format("有 {0} 行 分组 或 岗位 为空，确认仍然保存？", incomplete),
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            _staff.Clear();
            foreach (var s in _backup) _staff.Add(s);
            DialogResult = false;
            Close();
        }

        private static StaffMember Clone(StaffMember s) {
            return new StaffMember {
                Name = s.Name, Title = s.Title, Group = s.Group,
                Gender = s.Gender, RefereeLevel = s.RefereeLevel,
                Country = s.Country, Phone = s.Phone, Note = s.Note
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
