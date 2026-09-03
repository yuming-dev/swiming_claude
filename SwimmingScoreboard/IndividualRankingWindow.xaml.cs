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
            MainWindow.FillGenderCombo(GenderCombo);   // 2026-08-21 按比赛档案的性别表填，不再写死
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
                // 2026-09-03 排除接力队伍条目。原注释写的是"积分挂在队员条目",
                //   但队员条目从头到尾没有成绩 —— 那套派发从来没实现过, 结果是
                //   个人总分榜里接力分【恒为 0】, "接力项目"那一列永远是 0, 看着像故障。
                //   用户定的规则: 个人总分榜只算个人项目, 接力不计入。
                //   所以队伍条目排掉是对的, 下面再明确跳过接力项目。
                .Where(s => s.Notes == null || !s.Notes.StartsWith("接力队 棒次:"))
                .Where(s => ageFilter == "全部组别" || s.AgeCategory == ageFilter)
                .Where(s => genderFilter == "全部" || s.Gender == genderFilter)
                .Where(s => !string.IsNullOrEmpty(s.BibNumber))
                .GroupBy(s => s.BibNumber)
                .ToList();

            var rows = new List<IndividualRankRow>();
            foreach (var g in byBib) {
                var first = g.First();
                double total = 0;
                int indi = 0;   /* 2026-09-03 接力不计入个人总分, 不再统计接力项数 */
                var details = new List<string>();
                foreach (var sw in g) {
                    var result = sw.GetResultForStage("决赛");
                    if (result == null || result.FinalTime <= 0) continue;
                    if (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF") continue;
                    // 2026-08-30 取分用【项目内】名次(见 Swimmer.EventRankFor), 不是组内名次
                    int evRank = sw.EventRankFor("决赛");
                    if (evRank <= 0) continue;
                    // 2026-09-03 个人总分榜【只算个人项目】, 接力不计入(用户定的规则)。
                    //   接力的分算在团体总分里(见 CalculateTeamScores), 不往个人头上摊。
                    if (sw.EventName != null && sw.EventName.Contains("接力")) continue;
                    double pts = _scoringConfig.GetIndividualPoint(evRank);
                    if (pts <= 0) continue;
                    double coeff = _scoringConfig.GetAgeCoefficient(sw.AgeCategory ?? "");
                    double scored = pts * coeff;
                    total += scored;
                    indi++;
                    details.Add(string.Format("{0}(个人):{1}名/{2}分", sw.EventName, evRank, scored.ToString("0.##")));
                }
                if (total <= 0) continue;
                rows.Add(new IndividualRankRow {
                    BibNumber = first.BibNumber, Name = first.Name, Gender = first.Gender,
                    Country = first.Country, AgeCategory = first.AgeCategory,
                    TotalPoints = total, IndividualCount = indi,
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
            sb.AppendLine("名次,号码,姓名,性别,代表队,组别,总积分,个人项目数,项目-名次明细");
            foreach (var r in _lastResult) {
                sb.AppendLine(string.Join(",", new[] {
                    r.Rank.ToString(), Esc(r.BibNumber), Esc(r.Name), Esc(r.Gender), Esc(r.Country), Esc(r.AgeCategory),
                    r.TotalPoints.ToString("0.##"), r.IndividualCount.ToString(), Esc(r.ScoreDetail)
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
            sb.AppendLine("<table><tr align='center'><th>名次</th><th>号码</th><th>姓名</th><th>性别</th><th>代表队</th><th>组别</th><th>总积分</th><th>个人项目</th><th>明细</th></tr>");
            foreach (var r in _lastResult) {
                // 2026-09-03 去掉"接力"那一列后是 9 列, 占位符也要跟着减到 {0}..{8};
                //   多留一个 {9} 会在导出时抛 FormatException(参数不够)。
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td><b>{6}</b></td><td>{7}</td><td>{8}</td></tr>\n",
                    r.Rank, He(r.BibNumber), He(r.Name), He(r.Gender), He(r.Country), He(r.AgeCategory),
                    r.TotalPoints.ToString("0.##"), r.IndividualCount, He(r.ScoreDetail));
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
        public string ScoreDetail { get; set; }
    }
}
