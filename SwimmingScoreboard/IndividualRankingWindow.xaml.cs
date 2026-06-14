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
    // 2026-05-24 D 运动员个人总分排名 — 按累积分降序 Top N
    // 索美式 "运动员个人总分排名" 画面 (PDF p5)
    public partial class IndividualRankingWindow : Window
    {
        private readonly ObservableCollection<Swimmer> _swimmers;
        private readonly List<AgeGroup> _ageGroups;
        private readonly ScoringConfig _scoringConfig;
        private List<IndividualRankRow> _lastResult = new List<IndividualRankRow>();

        public IndividualRankingWindow(ObservableCollection<Swimmer> swimmers, List<AgeGroup> ageGroups, ScoringConfig scoringConfig) {
            InitializeComponent();
            _swimmers = swimmers;
            _ageGroups = ageGroups ?? new List<AgeGroup>();
            _scoringConfig = scoringConfig ?? new ScoringConfig();

            AgeGroupCombo.Items.Add("全部组别");
            foreach (var g in _ageGroups) AgeGroupCombo.Items.Add(g.Name);
            AgeGroupCombo.SelectedIndex = 0;
        }

        private void Compute_Click(object sender, RoutedEventArgs e) {
            string ageFilter = AgeGroupCombo.SelectedItem != null ? AgeGroupCombo.SelectedItem.ToString() : "全部组别";
            string genderFilter = GenderCombo.SelectedItem != null ? ((ComboBoxItem)GenderCombo.SelectedItem).Content.ToString() : "全部";
            int topN = 25;
            int.TryParse(TopNBox.Text.Trim(), out topN);
            if (topN < 1) topN = 25;

            // 按号码合并同一运动员的多项目
            var byBib = _swimmers
                .Where(s => s.Notes == null || !s.Notes.StartsWith("接力队 棒次:"))   // 排除接力代表条目（积分挂在队员条目）
                .Where(s => ageFilter == "全部组别" || s.AgeCategory == ageFilter)
                .Where(s => genderFilter == "全部" || s.Gender == genderFilter)
                .Where(s => !string.IsNullOrEmpty(s.BibNumber))
                .GroupBy(s => s.BibNumber)
                .ToList();

            var rows = new List<IndividualRankRow>();
            foreach (var g in byBib) {
                var first = g.First();
                double total = 0;
                int indi = 0, relay = 0;
                var details = new List<string>();
                foreach (var sw in g) {
                    var result = sw.GetResultForStage("决赛");
                    if (result == null || result.FinalTime <= 0) continue;
                    if (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF") continue;
                    if (sw.CurrentRank <= 0) continue;
                    bool isRelay = sw.EventName != null && sw.EventName.Contains("接力");
                    double pts = isRelay ? _scoringConfig.GetRelayPoint(sw.CurrentRank) : _scoringConfig.GetIndividualPoint(sw.CurrentRank);
                    if (pts <= 0) continue;
                    double coeff = _scoringConfig.GetAgeCoefficient(sw.AgeCategory ?? "");
                    double scored = pts * coeff;
                    total += scored;
                    if (isRelay) relay++;
                    else indi++;
                    details.Add(string.Format("{0}({2}):{1}名/{3}分", sw.EventName, sw.CurrentRank, isRelay ? "接力" : "个人", scored.ToString("0.##")));
                }
                if (total <= 0) continue;
                rows.Add(new IndividualRankRow {
                    BibNumber = first.BibNumber, Name = first.Name, Gender = first.Gender,
                    Country = first.Country, AgeCategory = first.AgeCategory,
                    TotalPoints = total, IndividualCount = indi, RelayCount = relay,
                    ScoreDetail = string.Join(" / ", details.ToArray())
                });
            }
            rows = rows.OrderByDescending(r => r.TotalPoints).ThenBy(r => r.BibNumber).Take(topN).ToList();
            int rank = 1;
            double lastPts = -1;
            int lastRank = 0;
            for (int i = 0; i < rows.Count; i++) {
                if (i > 0 && Math.Abs(rows[i].TotalPoints - lastPts) < 0.001) {
                    rows[i].Rank = lastRank;   // 并列
                } else {
                    rows[i].Rank = rank;
                    lastRank = rank;
                    lastPts = rows[i].TotalPoints;
                }
                rank++;
            }
            _lastResult = rows;
            RankGrid.ItemsSource = rows;
            SummaryText.Text = string.Format("命中 {0} 人；列出 Top {1}", _lastResult.Count, rows.Count);
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e) {
            if (_lastResult.Count == 0) { MessageBox.Show("请先 🔍 统计", "提示"); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV 文件|*.csv", Title = "导出个人总分排名",
                FileName = "运动员个人总分排名_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("名次,号码,姓名,性别,代表队,组别,总积分,个人项目数,接力项目数,项目-名次明细");
            foreach (var r in _lastResult) {
                sb.AppendLine(string.Join(",", new[] {
                    r.Rank.ToString(), Esc(r.BibNumber), Esc(r.Name), Esc(r.Gender), Esc(r.Country), Esc(r.AgeCategory),
                    r.TotalPoints.ToString("0.##"), r.IndividualCount.ToString(), r.RelayCount.ToString(), Esc(r.ScoreDetail)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("已导出: " + dlg.FileName, "完成");
        }

        private void PrintHtml_Click(object sender, RoutedEventArgs e) {
            if (_lastResult.Count == 0) { MessageBox.Show("请先 🔍 统计", "提示"); return; }
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><title>运动员个人总分排名</title>");
            sb.AppendLine("<style>body{font-family:'Microsoft YaHei',sans-serif;margin:20px;}table{border-collapse:collapse;width:100%;}");
            sb.AppendLine("th,td{border:1px solid #ddd;padding:6px 10px;text-align:left;}th{background:#1E40AF;color:white;text-align:center;vertical-align:middle;}");
            sb.AppendLine("tr:nth-child(even){background:#F8FAFC;}h1{color:#1E40AF;}</style></head><body>");
            sb.AppendLine("<h1>运动员个人总分排名</h1>");
            sb.AppendLine("<table><tr><th>名次</th><th>号码</th><th>姓名</th><th>性别</th><th>代表队</th><th>组别</th><th>总积分</th><th>个人</th><th>接力</th><th>明细</th></tr>");
            foreach (var r in _lastResult) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td><b>{6}</b></td><td>{7}</td><td>{8}</td><td>{9}</td></tr>\n",
                    r.Rank, He(r.BibNumber), He(r.Name), He(r.Gender), He(r.Country), He(r.AgeCategory),
                    r.TotalPoints.ToString("0.##"), r.IndividualCount, r.RelayCount, He(r.ScoreDetail));
            }
            sb.AppendLine("</table></body></html>");
            string tmp = Path.Combine(Path.GetTempPath(), "运动员个人总分排名_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html");
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

    public class IndividualRankRow
    {
        public int Rank { get; set; }
        public string BibNumber { get; set; }
        public string Name { get; set; }
        public string Gender { get; set; }
        public string Country { get; set; }
        public string AgeCategory { get; set; }
        public double TotalPoints { get; set; }
        public string TotalPointsText { get { return TotalPoints.ToString("0.##"); } }
        public int IndividualCount { get; set; }
        public int RelayCount { get; set; }
        public string ScoreDetail { get; set; }
    }
}
