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
    // 2026-05-24 C 各部门成绩公告 — 按代表队/单位生成多份 HTML 公告
    // 索美式 "各部门成绩公告" 画面 (PDF p4)
    public partial class DepartmentBulletinWindow : Window
    {
        private readonly ObservableCollection<Swimmer> _swimmers;
        private readonly List<AgeGroup> _ageGroups;
        private readonly ScoringConfig _scoringConfig;
        private readonly Dictionary<string, Unit> _unitsByName;

        public DepartmentBulletinWindow(ObservableCollection<Swimmer> swimmers, List<AgeGroup> ageGroups,
            ScoringConfig scoringConfig, IEnumerable<Unit> unitEntities) {
            InitializeComponent();
            _swimmers = swimmers;
            _ageGroups = ageGroups ?? new List<AgeGroup>();
            _scoringConfig = scoringConfig ?? new ScoringConfig();
            _unitsByName = new Dictionary<string, Unit>();
            if (unitEntities != null) {
                foreach (var u in unitEntities) {
                    if (u != null && !string.IsNullOrEmpty(u.Name) && !_unitsByName.ContainsKey(u.Name))
                        _unitsByName[u.Name] = u;
                }
            }

            // 提取参赛单位列表 (排除接力代表条目)
            var units = _swimmers
                .Where(s => s.Notes == null || !s.Notes.StartsWith("接力队 棒次:"))
                .Select(s => s.Country ?? "")
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            foreach (var u in units) UnitListBox.Items.Add(u);
            UnitSummaryText.Text = string.Format("共 {0} 个单位", units.Count);
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) {
            UnitListBox.SelectAll();
        }
        private void SelectNone_Click(object sender, RoutedEventArgs e) {
            UnitListBox.UnselectAll();
        }

        private void Generate_Click(object sender, RoutedEventArgs e) {
            var selected = UnitListBox.SelectedItems.Cast<string>().ToList();
            if (selected.Count == 0) { MessageBox.Show("请先勾选要生成公告的单位", "提示"); return; }

            var dlg = new System.Windows.Forms.FolderBrowserDialog {
                Description = "选择公告保存目录",
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            string outDir = Path.Combine(dlg.SelectedPath, "部门成绩公告_" + DateTime.Now.ToString("yyyyMMdd_HHmm"));
            Directory.CreateDirectory(outDir);

            bool scoredOnly = FilterScoredOnly.IsChecked == true;
            bool byAge = ScopeByAgeGroup.IsChecked == true;

            int genCount = 0;
            foreach (var unit in selected) {
                string html = BuildBulletinHtml(unit, scoredOnly, byAge);
                string safeUnit = MakeSafeFileName(unit);
                string path = Path.Combine(outDir, safeUnit + ".html");
                File.WriteAllText(path, html, Encoding.UTF8);
                genCount++;
            }

            GenStatusText.Text = string.Format("✔ 已生成 {0} 份公告到 {1}", genCount, outDir);
            try { Process.Start(outDir); } catch { }
        }

        private string BuildBulletinHtml(string unit, bool scoredOnly, bool byAge) {
            // 该单位的全部条目（排除接力代表）
            var entries = _swimmers
                .Where(s => s.Country == unit)
                .Where(s => s.Notes == null || !s.Notes.StartsWith("接力队 棒次:"))
                .ToList();

            // 单位团体总分
            double teamTotal = 0;
            foreach (var sw in entries) {
                if (sw.CurrentRank <= 0) continue;
                if (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF") continue;
                var r = sw.GetResultForStage("决赛");
                if (r == null || r.FinalTime <= 0) continue;
                bool isRelay = sw.EventName != null && sw.EventName.Contains("接力");
                double pts = isRelay ? _scoringConfig.GetRelayPoint(sw.CurrentRank) : _scoringConfig.GetIndividualPoint(sw.CurrentRank);
                if (pts <= 0) continue;
                double coeff = _scoringConfig.GetAgeCoefficient(sw.AgeCategory ?? "");
                teamTotal += pts * coeff;
            }
            // 2026-05-24 P0-E 基础分（参与分）— 在团体总分上加
            double basePoints = 0;
            Unit baseInfo;
            if (_unitsByName.TryGetValue(unit, out baseInfo)) basePoints = baseInfo.BasePoints;
            teamTotal += basePoints;

            // 按运动员归档 (BibNumber)
            var byBib = entries
                .Where(s => !string.IsNullOrEmpty(s.BibNumber))
                .GroupBy(s => s.BibNumber)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><title>" + He(unit) + " 成绩公告</title>");
            sb.AppendLine("<style>body{font-family:'Microsoft YaHei',sans-serif;margin:24px;}");
            sb.AppendLine("h1{color:#1E40AF;border-bottom:3px solid #1E40AF;padding-bottom:8px;}");
            sb.AppendLine("h2{color:#3B82F6;margin-top:24px;}");
            sb.AppendLine(".total{background:#FEF3C7;padding:12px;border-radius:6px;font-size:16px;font-weight:bold;margin:12px 0;}");
            sb.AppendLine("table{border-collapse:collapse;width:100%;margin:8px 0;}");
            sb.AppendLine("th,td{border:1px solid #ddd;padding:6px 10px;text-align:left;font-size:13px;}");
            sb.AppendLine("th{background:#1E40AF;color:white;}tr:nth-child(even){background:#F8FAFC;}");
            sb.AppendLine(".rank1{background:#FEF3C7;font-weight:bold;}.rank2{background:#E5E7EB;}.rank3{background:#FED7AA;}");
            sb.AppendLine(".dsq{color:#DC2626;}.athlete{margin-bottom:20px;}");
            sb.AppendLine(".meta{color:#475569;font-size:12px;}");
            sb.AppendLine(".unitcard{background:#EFF6FF;border:1px solid #93C5FD;border-radius:6px;padding:10px 14px;margin:8px 0 12px 0;}");
            sb.AppendLine(".unitcard span{display:inline-block;margin-right:18px;font-size:13px;color:#1E3A8A;}");
            sb.AppendLine(".unitcard b{color:#0F172A;}</style></head><body>");
            sb.AppendLine("<h1>" + He(unit) + " — 比赛成绩公告</h1>");

            // 单位元信息卡片（领队/教练/电话/地址）
            Unit unitInfo;
            if (_unitsByName.TryGetValue(unit, out unitInfo)) {
                var bits = new List<string>();
                if (!string.IsNullOrEmpty(unitInfo.ShortName)) bits.Add("<span>简称: <b>" + He(unitInfo.ShortName) + "</b></span>");
                if (!string.IsNullOrEmpty(unitInfo.Leader))    bits.Add("<span>领队: <b>" + He(unitInfo.Leader) + "</b></span>");
                if (!string.IsNullOrEmpty(unitInfo.Coach))     bits.Add("<span>教练: <b>" + He(unitInfo.Coach) + "</b></span>");
                if (!string.IsNullOrEmpty(unitInfo.Doctor))    bits.Add("<span>队医: <b>" + He(unitInfo.Doctor) + "</b></span>");
                if (unitInfo.BasePoints > 0)                   bits.Add("<span>基础分: <b>" + unitInfo.BasePoints.ToString("0.##") + "</b></span>");
                if (!string.IsNullOrEmpty(unitInfo.Phone))     bits.Add("<span>联系电话: <b>" + He(unitInfo.Phone) + "</b></span>");
                if (!string.IsNullOrEmpty(unitInfo.Address))   bits.Add("<span>地址: <b>" + He(unitInfo.Address) + "</b></span>");
                if (!string.IsNullOrEmpty(unitInfo.Note))      bits.Add("<span>备注: <b>" + He(unitInfo.Note) + "</b></span>");
                if (bits.Count > 0) {
                    sb.Append("<div class='unitcard'>");
                    foreach (var b in bits) sb.Append(b);
                    sb.AppendLine("</div>");
                }
            }

            sb.AppendLine("<div class='meta'>生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " &nbsp;|&nbsp; 单位运动员条目: " + entries.Count + "</div>");
            string baseTxt = basePoints > 0 ? string.Format(" (含基础分 {0})", basePoints.ToString("0.##")) : "";
            sb.AppendLine("<div class='total'>团体总分: " + teamTotal.ToString("0.##") + " 分" + baseTxt + "</div>");

            // 按组别分段 / 综合
            if (byAge) {
                var ageGroupNames = entries.Select(s => s.AgeCategory ?? "").Distinct().OrderBy(a => a).ToList();
                foreach (var ag in ageGroupNames) {
                    if (string.IsNullOrEmpty(ag)) continue;
                    sb.AppendLine("<h2>组别: " + He(ag) + "</h2>");
                    var bibsInGroup = byBib.Where(g => g.First().AgeCategory == ag).ToList();
                    if (bibsInGroup.Count == 0) { sb.AppendLine("<p class='meta'>(无)</p>"); continue; }
                    RenderAthletes(sb, bibsInGroup, scoredOnly);
                }
            } else {
                sb.AppendLine("<h2>运动员成绩</h2>");
                RenderAthletes(sb, byBib, scoredOnly);
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void RenderAthletes(StringBuilder sb, List<IGrouping<string, Swimmer>> byBib, bool scoredOnly) {
            foreach (var g in byBib.OrderBy(x => x.Key)) {
                var first = g.First();

                // 收集运动员条目（个人 + 接力都算上；接力是该运动员作为队员的接力队条目）
                var items = new List<AthleteResultItem>();
                double personalTotal = 0;
                foreach (var sw in g) {
                    var r = sw.GetResultForStage("决赛");
                    string timeStr = r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "";
                    string status = sw.Status ?? "";
                    bool isRelay = sw.EventName != null && sw.EventName.Contains("接力");
                    double pts = 0;
                    if (sw.CurrentRank > 0 && status != "DSQ" && status != "DNS" && status != "DNF" && r != null && r.FinalTime > 0) {
                        double basePts = isRelay ? _scoringConfig.GetRelayPoint(sw.CurrentRank) : _scoringConfig.GetIndividualPoint(sw.CurrentRank);
                        pts = basePts * _scoringConfig.GetAgeCoefficient(sw.AgeCategory ?? "");
                        personalTotal += pts;
                    }
                    items.Add(new AthleteResultItem {
                        EventName = sw.EventName ?? "", IsRelay = isRelay,
                        Rank = sw.CurrentRank, Time = timeStr, Status = status, Points = pts
                    });
                }
                if (scoredOnly && personalTotal <= 0) continue;

                sb.AppendLine("<div class='athlete'>");
                sb.AppendFormat("<h3 style='color:#0F172A;margin:6px 0;'>{0} &nbsp; <span class='meta'>号码 {1} &nbsp; {2} &nbsp; {3} &nbsp; 本人小计 <b>{4}</b> 分</span></h3>\n",
                    He(first.Name), He(first.BibNumber), He(first.Gender), He(first.AgeCategory),
                    personalTotal.ToString("0.##"));
                sb.AppendLine("<table><tr><th>项目</th><th>类型</th><th>名次</th><th>成绩</th><th>状态</th><th>积分</th></tr>");
                foreach (var it in items.OrderBy(i => i.EventName)) {
                    string cls = "";
                    if (it.Rank == 1) cls = " class='rank1'";
                    else if (it.Rank == 2) cls = " class='rank2'";
                    else if (it.Rank == 3) cls = " class='rank3'";
                    if (it.Status == "DSQ" || it.Status == "DNS" || it.Status == "DNF") cls = " class='dsq'";
                    string rankCell = it.Rank > 0 ? it.Rank.ToString() : "-";
                    sb.AppendFormat("<tr{0}><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td></tr>\n",
                        cls, He(it.EventName), it.IsRelay ? "接力" : "个人", rankCell, He(it.Time), He(it.Status),
                        it.Points > 0 ? it.Points.ToString("0.##") : "");
                }
                sb.AppendLine("</table></div>");
            }
        }

        private static string MakeSafeFileName(string s) {
            if (string.IsNullOrEmpty(s)) return "未命名";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in s) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private static string He(string s) {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private class AthleteResultItem
        {
            public string EventName;
            public bool IsRelay;
            public int Rank;
            public string Time;
            public string Status;
            public double Points;
        }
    }
}
