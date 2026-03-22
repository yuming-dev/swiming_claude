using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    public partial class PromotionQueryWindow : Window
    {
        private ObservableCollection<Swimmer> _swimmers;
        private List<string> _events;
        private PoolConfig _poolConfig;
        private List<Swimmer> _promoted = new List<Swimmer>();
        private string _toStage = "";
        private bool _initialized = false;

        public PromotionQueryWindow(ObservableCollection<Swimmer> swimmers, List<string> events, PoolConfig poolConfig) {
            InitializeComponent();
            _swimmers = swimmers;
            _events = events;
            _poolConfig = poolConfig;
            PopulateEvents();
            _initialized = true;
            UpdateStages();
        }

        // ═══════ 下拉框填充 ═══════
        private void PopulateEvents() {
            EventCombo.Items.Clear();
            var eventSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (!string.IsNullOrEmpty(s.EventName)) eventSet.Add(s.EventName);
            }
            foreach (string ev in eventSet.OrderBy(e => e)) EventCombo.Items.Add(ev);
            if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void UpdateStages() {
            if (!_initialized) return;
            FromStageCombo.Items.Clear();
            string gender = GetGender();
            string eventName = GetEventName();
            if (string.IsNullOrEmpty(eventName)) return;

            // 从运动员成绩记录中提取有成绩的阶段
            var stagesWithResults = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (s.Gender == gender && s.EventName == eventName) {
                    foreach (var r in s.Results) {
                        if (r.FinalTime > 0) stagesWithResults.Add(r.Stage);
                    }
                }
            }

            string[] stageOrder = { "预赛", "复赛", "半决赛" };
            foreach (string st in stageOrder) {
                if (stagesWithResults.Contains(st)) FromStageCombo.Items.Add(st);
            }
            if (FromStageCombo.Items.Count > 0) FromStageCombo.SelectedIndex = FromStageCombo.Items.Count - 1;
            UpdateInfo();
        }

        // ═══════ 事件处理 ═══════
        private void Gender_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateStages(); }
        private void Event_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateStages(); }
        private void Stage_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateInfo(); }
        private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateInfo(); }

        // ═══════ 信息更新 ═══════
        private void UpdateInfo() {
            if (!_initialized) return;
            InfoText.Text = "";
            WarningText.Text = "";
            string gender = GetGender();
            string eventName = GetEventName();
            string fromStage = GetFromStage();
            if (string.IsNullOrEmpty(fromStage)) {
                WarningText.Text = "该项目没有已完成的阶段成绩，请先完成比赛。";
                return;
            }

            // 确定下一阶段
            if (fromStage == "预赛") _toStage = "半决赛";
            else if (fromStage == "复赛") _toStage = "半决赛";
            else if (fromStage == "半决赛") _toStage = "决赛";
            else { _toStage = ""; return; }

            int total = 0, withResults = 0;
            var heats = new HashSet<int>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                var r = s.GetResultForStage(fromStage);
                if (r != null && r.FinalTime > 0) { withResults++; heats.Add(r.Heat); }
                total++;
            }

            int promoCount = 16;
            int.TryParse(CountBox.Text.Trim(), out promoCount);

            InfoText.Text = string.Format("{0} {1} — {2}\n共 {3} 人，{4} 人有成绩，{5} 个小组\n晋级 {6} 人到 {7}",
                gender, eventName, fromStage, total, withResults, heats.Count, promoCount, _toStage);

            if (withResults == 0)
                WarningText.Text = "警告：该阶段没有成绩数据！请先完成比赛。";
            else if (withResults < promoCount)
                WarningText.Text = string.Format("注意：有成绩人数（{0}）少于晋级人数（{1}）", withResults, promoCount);
        }

        // ═══════ 查询晋级名单 ═══════
        private void Query_Click(object sender, RoutedEventArgs e) {
            string gender = GetGender();
            string eventName = GetEventName();
            string fromStage = GetFromStage();
            if (string.IsNullOrEmpty(fromStage)) { MessageBox.Show("请选择赛次"); return; }

            int totalPromo = 16;
            int.TryParse(CountBox.Text.Trim(), out totalPromo);
            int modeIdx = PromotionModeCombo != null ? PromotionModeCombo.SelectedIndex : 0;

            // 收集有成绩的运动员，按组分类
            var heatResults = new Dictionary<int, List<SwimmerResult>>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ") continue;
                var r = s.GetResultForStage(fromStage);
                if (r == null || r.FinalTime <= 0) continue;
                if (!heatResults.ContainsKey(r.Heat)) heatResults[r.Heat] = new List<SwimmerResult>();
                heatResults[r.Heat].Add(new SwimmerResult { Swimmer = s, Result = r });
            }

            // 每组内按成绩排序（相同成绩比较反应时间）
            foreach (var kv in heatResults) {
                kv.Value.Sort((a, b) => {
                    int cmp = a.Result.FinalTime.CompareTo(b.Result.FinalTime);
                    if (cmp != 0) return cmp;
                    return a.Result.StartingBlockTime.CompareTo(b.Result.StartingBlockTime);
                });
                for (int i = 0; i < kv.Value.Count; i++) kv.Value[i].HeatRank = i + 1;
            }

            _promoted.Clear();
            var displayData = new List<object>();

            if (modeIdx == 0) {
                // 总成绩排名前N名
                var all = new List<SwimmerResult>();
                foreach (var kv in heatResults) all.AddRange(kv.Value);
                all.Sort((a, b) => {
                    int cmp = a.Result.FinalTime.CompareTo(b.Result.FinalTime);
                    if (cmp != 0) return cmp;
                    return a.Result.StartingBlockTime.CompareTo(b.Result.StartingBlockTime);
                });
                var selected = all.Take(totalPromo).ToList();
                _promoted = selected.Select(x => x.Swimmer).ToList();
                int rank = 1;
                foreach (var sr in selected) displayData.Add(MakeRow(rank++, sr, "总排名"));

                // 并列检查
                if (selected.Count == totalPromo && all.Count > totalPromo) {
                    double cutoff = selected.Last().Result.FinalTime;
                    int tiedCount = all.Count(x => x.Result.FinalTime == cutoff) - selected.Count(x => x.Result.FinalTime == cutoff);
                    if (tiedCount > 0) {
                        // 再比反应时间后仍并列
                        double cutReact = selected.Last().Result.StartingBlockTime;
                        int stillTied = all.Count(x => x.Result.FinalTime == cutoff && x.Result.StartingBlockTime == cutReact && !selected.Contains(x));
                        if (stillTied > 0)
                            WarningText.Text = string.Format("警告：第{0}名成绩{1}反应{2}s存在并列{3}人！需加赛。", totalPromo, TimeFormatter.Format(cutoff), cutReact.ToString("F2"), stillTied);
                    }
                }
            } else {
                // 每组前N名+成绩递补
                int perHeat = modeIdx + 1;
                var directQualified = new List<SwimmerResult>();
                var remainders = new List<SwimmerResult>();
                foreach (var kv in heatResults.OrderBy(k => k.Key)) {
                    for (int i = 0; i < kv.Value.Count; i++) {
                        if (i < perHeat) {
                            kv.Value[i].Method = string.Format("组{0}第{1}", kv.Key, i + 1);
                            directQualified.Add(kv.Value[i]);
                        } else {
                            remainders.Add(kv.Value[i]);
                        }
                    }
                }
                remainders.Sort((a, b) => {
                    int cmp = a.Result.FinalTime.CompareTo(b.Result.FinalTime);
                    if (cmp != 0) return cmp;
                    return a.Result.StartingBlockTime.CompareTo(b.Result.StartingBlockTime);
                });
                int needed = Math.Max(0, totalPromo - directQualified.Count);
                var supplemented = remainders.Take(needed).ToList();
                foreach (var sr in supplemented) sr.Method = "成绩递补";

                var allPromoted = new List<SwimmerResult>();
                allPromoted.AddRange(directQualified);
                allPromoted.AddRange(supplemented);
                allPromoted.Sort((a, b) => {
                    int cmp = a.Result.FinalTime.CompareTo(b.Result.FinalTime);
                    if (cmp != 0) return cmp;
                    return a.Result.StartingBlockTime.CompareTo(b.Result.StartingBlockTime);
                });
                _promoted = allPromoted.Select(x => x.Swimmer).ToList();
                int rank = 1;
                foreach (var sr in allPromoted) displayData.Add(MakeRow(rank++, sr, sr.Method));

                InfoText.Text += string.Format("\n直接晋级: {0}人（每组前{1}名），成绩递补: {2}人", directQualified.Count, perHeat, supplemented.Count);
            }

            PromotionGrid.ItemsSource = displayData;
            ResultText.Text = _promoted.Count > 0 ? string.Format("共 {0} 人晋级", _promoted.Count) : "未找到成绩";
            ResultText.Foreground = new System.Windows.Media.SolidColorBrush(
                _promoted.Count > 0 ? System.Windows.Media.Colors.Green : System.Windows.Media.Colors.Red);
        }

        private object MakeRow(int rank, SwimmerResult sr, string method) {
            return new {
                Rank = rank,
                BibNumber = sr.Swimmer.BibNumber ?? "",
                Name = sr.Swimmer.Name ?? "",
                Country = sr.Swimmer.Country ?? "",
                Heat = sr.Result.Heat,
                Time = TimeFormatter.Format(sr.Result.FinalTime),
                Reaction = sr.Result.StartingBlockTime > 0 ? sr.Result.StartingBlockTime.ToString("F2") : "",
                Method = method,
                ToStage = _toStage
            };
        }

        // ═══════ 执行晋级 ═══════
        private void Execute_Click(object sender, RoutedEventArgs e) {
            if (_promoted.Count == 0) { MessageBox.Show("请先点击\"查询晋级名单\""); return; }
            if (string.IsNullOrEmpty(_toStage)) { MessageBox.Show("无法确定晋级目标阶段"); return; }

            string eventName = GetEventName();
            string fromStage = GetFromStage();

            if (MessageBox.Show(
                string.Format("确认将 {0} 名运动员从 {1} 晋级到 {2}？\n将按{1}成绩蛇形分组。", _promoted.Count, fromStage, _toStage),
                "确认晋级", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var assignments = HeatScheduler.GenerateHeatsFromResults(_promoted, _poolConfig, eventName, _toStage, fromStage);
            int heatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;

            ResultText.Text = string.Format("已晋级 {0} 人到{1}，{2}组", _promoted.Count, _toStage, heatCount);
            MessageBox.Show(string.Format("已将 {0} 名运动员晋级到 {1}，分为 {2} 组。\n请在赛程树中选择{1}的组次。", _promoted.Count, _toStage, heatCount), "晋级完成");
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        // ═══════ 工具方法 ═══════
        private string GetGender() {
            return GenderCombo != null && GenderCombo.SelectedItem != null ? ((ComboBoxItem)GenderCombo.SelectedItem).Content.ToString() : "男";
        }
        private string GetEventName() {
            return EventCombo != null && EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
        }
        private string GetFromStage() {
            return FromStageCombo != null && FromStageCombo.SelectedItem != null ? FromStageCombo.SelectedItem.ToString() : "";
        }

        private class SwimmerResult {
            public Swimmer Swimmer;
            public LaneResult Result;
            public int HeatRank;
            public string Method = "";
        }
    }
}
