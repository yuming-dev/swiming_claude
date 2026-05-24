using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    // 2026-05-24 B 项目成绩统计 — 各项目前 8 名清单
    // 索美式 "项目成绩统计" 画面 (PDF p2-3)
    public partial class EventTop8Window : Window
    {
        private readonly ObservableCollection<Swimmer> _swimmers;
        private readonly List<AgeGroup> _ageGroups;
        private readonly List<string> _genders;
        private readonly List<string> _events;
        private List<EventTop8Row> _lastResult = new List<EventTop8Row>();

        public EventTop8Window(ObservableCollection<Swimmer> swimmers, List<AgeGroup> ageGroups,
            List<string> genders, List<string> events) {
            InitializeComponent();
            _swimmers = swimmers;
            _ageGroups = ageGroups ?? new List<AgeGroup>();
            _genders = genders ?? new List<string> { "男", "女" };
            _events = events ?? new List<string>();

            AgeGroupCombo.Items.Add("全部组别");
            foreach (var g in _ageGroups) AgeGroupCombo.Items.Add(g.Name);
            AgeGroupCombo.SelectedIndex = 0;
        }

        private void Compute_Click(object sender, RoutedEventArgs e) {
            string ageFilter = AgeGroupCombo.SelectedItem != null ? AgeGroupCombo.SelectedItem.ToString() : "全部组别";
            string genderFilter = GenderCombo.SelectedItem != null ? ((ComboBoxItem)GenderCombo.SelectedItem).Content.ToString() : "全部";
            string display = DisplayCombo.SelectedItem != null ? ((ComboBoxItem)DisplayCombo.SelectedItem).Content.ToString() : "姓名 (代表队)";

            var ageGroupNames = _ageGroups.Count > 0 ? _ageGroups.Select(g => g.Name).ToList() : new List<string> { "" };
            var rows = new List<EventTop8Row>();

            foreach (var ag in ageGroupNames) {
                if (ageFilter != "全部组别" && ag != ageFilter) continue;
                foreach (var gender in _genders) {
                    if (genderFilter != "全部" && gender != genderFilter) continue;
                    foreach (var ev in _events) {
                        if (string.IsNullOrEmpty(ev)) continue;

                        // 取该 (组别,性别,项目) 的决赛阶段排名 1-8 的运动员
                        var top8 = _swimmers
                            .Where(s => s.EventName == ev && s.Gender == gender &&
                                        (string.IsNullOrEmpty(ag) || s.AgeCategory == ag) &&
                                        (s.Notes == null || !s.Notes.StartsWith("接力队员")) &&
                                        s.CurrentRank > 0 && s.CurrentRank <= 8 &&
                                        s.Status != "DSQ" && s.Status != "DNS" && s.Status != "DNF")
                            .OrderBy(s => s.CurrentRank).ToList();
                        if (top8.Count == 0) continue;

                        var row = new EventTop8Row { AgeGroup = ag, Gender = gender, EventName = ev };
                        for (int r = 1; r <= 8; r++) {
                            var entries = top8.Where(s => s.CurrentRank == r).ToList();
                            if (entries.Count == 0) continue;
                            var text = string.Join(" / ", entries.Select(s => FormatEntry(s, display)).ToArray());
                            switch (r) {
                                case 1: row.R1 = text; break;
                                case 2: row.R2 = text; break;
                                case 3: row.R3 = text; break;
                                case 4: row.R4 = text; break;
                                case 5: row.R5 = text; break;
                                case 6: row.R6 = text; break;
                                case 7: row.R7 = text; break;
                                case 8: row.R8 = text; break;
                            }
                        }
                        rows.Add(row);
                    }
                }
            }

            // 排序: 组别 → 性别 → 项目 FINA 顺序
            rows = rows.OrderBy(r => r.AgeGroup ?? "").ThenBy(r => GenderOrder(r.Gender)).ThenBy(r => EventOrder(r.EventName)).ToList();
            _lastResult = rows;
            Top8Grid.ItemsSource = rows;
            SummaryText.Text = string.Format("命中 {0} 项有决赛成绩", rows.Count);
        }

        private static string FormatEntry(Swimmer s, string display) {
            string name = s.Name ?? "";
            string country = s.Country ?? "";
            var result = s.GetResultForStage("决赛");
            string time = result != null && result.FinalTime > 0 ? TimeFormatter.Format(result.FinalTime) : "";
            switch (display) {
                case "代表队": return country;
                case "姓名": return name;
                case "姓名 + 成绩": return name + " " + time;
                default: return name + " (" + country + ")";   // "姓名 (代表队)"
            }
        }

        private static int GenderOrder(string g) {
            if (g == "男") return 1;
            if (g == "女") return 2;
            return 3;
        }
        private static int EventOrder(string ev) {
            if (string.IsNullOrEmpty(ev)) return 99;
            int strokeOrder = 99;
            if (ev.Contains("自由泳")) strokeOrder = 1;
            else if (ev.Contains("仰泳")) strokeOrder = 2;
            else if (ev.Contains("蛙泳")) strokeOrder = 3;
            else if (ev.Contains("蝶泳")) strokeOrder = 4;
            else if (ev.Contains("混合泳") || ev.Contains("混")) strokeOrder = 5;
            if (ev.Contains("接力")) strokeOrder = 6;
            int dist = 0;
            foreach (var d in new[] { 50, 100, 200, 400, 800, 1500 }) {
                if (ev.Contains(d + "米")) { dist = d; break; }
            }
            return strokeOrder * 10000 + dist;
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e) {
            if (_lastResult.Count == 0) { MessageBox.Show("请先 🔍 统计", "提示"); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV 文件|*.csv", Title = "导出项目成绩统计",
                FileName = "项目成绩统计_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("组别,性别,项目,第1名,第2名,第3名,第4名,第5名,第6名,第7名,第8名");
            foreach (var r in _lastResult) {
                sb.AppendLine(string.Join(",", new[] {
                    Esc(r.AgeGroup), Esc(r.Gender), Esc(r.EventName),
                    Esc(r.R1), Esc(r.R2), Esc(r.R3), Esc(r.R4),
                    Esc(r.R5), Esc(r.R6), Esc(r.R7), Esc(r.R8)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("已导出: " + dlg.FileName, "完成");
        }

        private void PrintHtml_Click(object sender, RoutedEventArgs e) {
            if (_lastResult.Count == 0) { MessageBox.Show("请先 🔍 统计", "提示"); return; }
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><title>项目成绩统计</title>");
            sb.AppendLine("<style>body{font-family:'Microsoft YaHei',sans-serif;margin:20px;font-size:12px;}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;}th,td{border:1px solid #ddd;padding:5px 8px;text-align:center;}");
            sb.AppendLine("th{background:#1E40AF;color:white;}tr:nth-child(even){background:#F8FAFC;}");
            sb.AppendLine("h1{color:#1E40AF;}td.gold{background:#FEF3C7;font-weight:bold;}td.silver{background:#E5E7EB;}td.bronze{background:#FED7AA;}</style></head><body>");
            sb.AppendLine("<h1>项目成绩统计 — 各项目第 1-8 名</h1>");
            sb.AppendLine("<table><tr><th>组别</th><th>性别</th><th>项目</th><th>第1名</th><th>第2名</th><th>第3名</th><th>第4名</th><th>第5名</th><th>第6名</th><th>第7名</th><th>第8名</th></tr>");
            foreach (var r in _lastResult) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td class='gold'>{3}</td><td class='silver'>{4}</td><td class='bronze'>{5}</td><td>{6}</td><td>{7}</td><td>{8}</td><td>{9}</td><td>{10}</td></tr>\n",
                    He(r.AgeGroup), He(r.Gender), He(r.EventName),
                    He(r.R1), He(r.R2), He(r.R3), He(r.R4), He(r.R5), He(r.R6), He(r.R7), He(r.R8));
            }
            sb.AppendLine("</table></body></html>");
            string tmp = Path.Combine(Path.GetTempPath(), "项目成绩统计_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html");
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            try { Process.Start(tmp); } catch { MessageBox.Show("已生成: " + tmp); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private static string Esc(string s) {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
        private static string He(string s) {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    public class EventTop8Row
    {
        public string AgeGroup { get; set; }
        public string Gender { get; set; }
        public string EventName { get; set; }
        public string R1 { get; set; }
        public string R2 { get; set; }
        public string R3 { get; set; }
        public string R4 { get; set; }
        public string R5 { get; set; }
        public string R6 { get; set; }
        public string R7 { get; set; }
        public string R8 { get; set; }
    }
}
