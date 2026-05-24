using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    // 2026-05-24 A 决赛项目状态总览 — 一眼看出每项目录入/加分/确认进度
    // 索美式 "决赛项目状态" 画面 (PDF p2)
    public partial class FinalsStatusWindow : Window
    {
        private readonly ObservableCollection<Swimmer> _swimmers;
        private readonly List<AgeGroup> _ageGroups;
        private readonly List<string> _genders;
        private readonly List<string> _events;
        private readonly HashSet<string> _confirmedHeats;

        public FinalsStatusWindow(ObservableCollection<Swimmer> swimmers, List<AgeGroup> ageGroups,
            List<string> genders, List<string> events, HashSet<string> confirmedHeats) {
            InitializeComponent();
            _swimmers = swimmers;
            _ageGroups = ageGroups ?? new List<AgeGroup>();
            _genders = genders ?? new List<string> { "男", "女" };
            _events = events ?? new List<string>();
            _confirmedHeats = confirmedHeats ?? new HashSet<string>();
            Refresh();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) { Refresh(); }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private void Refresh() {
            var rows = new List<FinalsStatusRow>();
            var ageGroupNames = _ageGroups.Count > 0 ? _ageGroups.Select(g => g.Name).ToList() : new List<string> { "" };

            foreach (var ag in ageGroupNames) {
                foreach (var gender in _genders) {
                    foreach (var ev in _events) {
                        if (string.IsNullOrEmpty(ev)) continue;

                        var match = _swimmers.Where(s =>
                            s.EventName == ev && s.Gender == gender &&
                            (string.IsNullOrEmpty(ag) || s.AgeCategory == ag) &&
                            (s.Notes == null || !s.Notes.StartsWith("接力队员")) &&
                            s.GetAssignmentForStage("决赛") != null
                        ).ToList();
                        if (match.Count == 0) continue;   // 该项目无决赛报名跳过

                        int finalHeats = match.Max(s => s.GetAssignmentForStage("决赛").Heat);

                        // 统计录入：有 result.FinalTime>0 的人数
                        int withResult = match.Count(s => {
                            var r = s.GetResultForStage("决赛");
                            return r != null && r.FinalTime > 0;
                        });
                        int dnsCount = match.Count(s => s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ");
                        int effectiveTotal = match.Count - dnsCount;   // 有效需录入人数

                        string inputStatus;
                        if (withResult == 0) inputStatus = "✗ 未录入";
                        else if (withResult >= effectiveTotal) inputStatus = "✅ 完成";
                        else inputStatus = string.Format("⏳ {0}/{1}", withResult, effectiveTotal);

                        // 加分状态（CurrentRank > 0 表示已计算名次）
                        int withRank = match.Count(s => s.CurrentRank > 0);
                        string scoringStatus = withRank > 0 ? "✅ 已加分" : "—";

                        // 确认状态（_confirmedHeats key = 组别|性别|项目|赛次|组次）
                        bool allConfirmed = true;
                        bool anyConfirmed = false;
                        for (int h = 1; h <= finalHeats; h++) {
                            string key = string.Format("{0}|{1}|{2}|决赛|{3}", ag, gender, ev, h);
                            if (_confirmedHeats.Contains(key)) anyConfirmed = true;
                            else allConfirmed = false;
                        }
                        string confirmedStatus = !anyConfirmed ? "—" : allConfirmed ? "✅ 全部" : "⏳ 部分";

                        string note = "";
                        if (dnsCount > 0) note += string.Format("DSQ/DNS/DNF {0} 人", dnsCount);

                        rows.Add(new FinalsStatusRow {
                            AgeGroup = ag, Gender = gender, EventName = ev,
                            ParticipantCount = match.Count, FinalHeats = finalHeats,
                            InputStatusText = inputStatus, ScoringStatusText = scoringStatus,
                            ConfirmedStatusText = confirmedStatus, Note = note
                        });
                    }
                }
            }

            StatusGrid.ItemsSource = rows;
            int total = rows.Count;
            int doneInput = rows.Count(r => r.InputStatusText.Contains("完成"));
            int doneConfirm = rows.Count(r => r.ConfirmedStatusText.Contains("全部"));
            SummaryText.Text = string.Format("共 {0} 项决赛 | 录入完成 {1} 项 | 全部确认 {2} 项",
                total, doneInput, doneConfirm);
        }
    }

    public class FinalsStatusRow
    {
        public string AgeGroup { get; set; }
        public string Gender { get; set; }
        public string EventName { get; set; }
        public int ParticipantCount { get; set; }
        public string ParticipantLabel { get { return ParticipantCount + "人"; } }
        public int FinalHeats { get; set; }
        public string InputStatusText { get; set; }
        public string ScoringStatusText { get; set; }
        public string ConfirmedStatusText { get; set; }
        public string Note { get; set; }
    }
}
