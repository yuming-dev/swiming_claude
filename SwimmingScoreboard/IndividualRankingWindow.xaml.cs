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

            AgeGroupCombo.Items.Add(Loc.T("Str_Win_AwardCert_AllAgeGroups"));
            foreach (var g in _ageGroups) AgeGroupCombo.Items.Add(g.Name);
            AgeGroupCombo.SelectedIndex = 0;
        }

        private void Compute_Click(object sender, RoutedEventArgs e) {
            string ageFilter = AgeGroupCombo.SelectedItem != null ? AgeGroupCombo.SelectedItem.ToString() : Loc.T("Str_Win_AwardCert_AllAgeGroups");
            // 2026-10-05 GenderCombo 用 Tag 存中文哨兵值(展示走 Loc.GenderDisplay), 这里跟着
            // 读 Tag, 比较目标也固定用原始"全部"(不随语言变, 跟 FillGenderCombo 里的 Tag 对应)
            string genderFilter = GenderCombo.SelectedItem != null ? ((ComboBoxItem)GenderCombo.SelectedItem).Tag.ToString() : "全部";
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
                .Where(s => ageFilter == Loc.T("Str_Win_AwardCert_AllAgeGroups") || s.AgeCategory == ageFilter)
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
                    details.Add(Loc.F("Str_Win_IndiRank_DetailFmt", sw.EventName, evRank, scored.ToString("0.##")));
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
            SummaryText.Text = Loc.F("Str_Win_IndiRank_SummaryFmt", _lastResult.Count, rows.Count);
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e) {
            if (_lastResult.Count == 0) { MessageBox.Show(Loc.T("Str_Win_IndiRank_MsgComputeFirst"), Loc.T("Str_MsgTitle_Info")); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = Loc.T("Str_Win_UnitMgmt_CsvFilter"), Title = Loc.T("Str_Win_IndiRank_ExportCsvTitle"),
                FileName = Loc.T("Str_Win_IndiRank_Title") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine(Loc.T("Str_Win_IndiRank_CsvHeader"));
            foreach (var r in _lastResult) {
                sb.AppendLine(string.Join(",", new[] {
                    r.Rank.ToString(), Esc(r.BibNumber), Esc(r.Name), Esc(r.Gender), Esc(r.Country), Esc(r.AgeCategory),
                    r.TotalPoints.ToString("0.##"), r.IndividualCount.ToString(), Esc(r.ScoreDetail)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(Loc.F("Str_Win_UnitMgmt_MsgExportedFmt", dlg.FileName), Loc.T("Str_MsgTitle_Done"));
        }

        private void PrintHtml_Click(object sender, RoutedEventArgs e) {
            if (_lastResult.Count == 0) { MessageBox.Show(Loc.T("Str_Win_IndiRank_MsgComputeFirst"), Loc.T("Str_MsgTitle_Info")); return; }
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><title>" + He(Loc.T("Str_Win_IndiRank_Title")) + "</title>");
            sb.AppendLine("<style>body{font-family:'Microsoft YaHei',sans-serif;margin:20px;}table{border-collapse:collapse;width:100%;}");
            sb.AppendLine("th,td{border:1px solid #ddd;padding:6px 10px;text-align:left;}th{background:#1E40AF;color:white;text-align:center;vertical-align:middle;}");
            sb.AppendLine("tr:nth-child(even){background:#F8FAFC;}h1{color:#1E40AF;}</style></head><body>");
            sb.AppendLine("<h1>" + He(Loc.T("Str_Win_IndiRank_Title")) + "</h1>");
            sb.AppendFormat("<table><tr align='center'><th>{0}</th><th>{1}</th><th>{2}</th><th>{3}</th><th>{4}</th><th>{5}</th><th>{6}</th><th>{7}</th><th>{8}</th></tr>\n",
                He(Loc.T("Str_Results_ColRank")), He(Loc.T("Str_Col_BibNo")), He(Loc.T("Str_Col_Name")), He(Loc.T("Str_Col_Sex")),
                He(Loc.T("Str_Col_Team")), He(Loc.T("Str_Col_Group")), He(Loc.T("Str_Win_IndiRank_ColTotalPoints")),
                He(Loc.T("Str_Win_IndiRank_ColIndiCount")), He(Loc.T("Str_Win_IndiRank_ColDetail")));
            foreach (var r in _lastResult) {
                // 2026-09-03 去掉"接力"那一列后是 9 列, 占位符也要跟着减到 {0}..{8};
                //   多留一个 {9} 会在导出时抛 FormatException(参数不够)。
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td><b>{6}</b></td><td>{7}</td><td>{8}</td></tr>\n",
                    r.Rank, He(r.BibNumber), He(r.Name), He(r.Gender), He(r.Country), He(r.AgeCategory),
                    r.TotalPoints.ToString("0.##"), r.IndividualCount, He(r.ScoreDetail));
            }
            sb.AppendLine("</table></body></html>");
            string tmp = Path.Combine(Path.GetTempPath(), Loc.T("Str_Win_IndiRank_Title") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html");
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            try { Process.Start(tmp); } catch { MessageBox.Show(Loc.F("Str_Win_IndiRank_MsgGeneratedFmt", tmp)); }
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
