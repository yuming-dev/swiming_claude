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
        private string _fromStage = "";
        private string _toStage = "";

        public PromotionQueryWindow(ObservableCollection<Swimmer> swimmers, List<string> events, PoolConfig poolConfig) {
            InitializeComponent();
            _swimmers = swimmers;
            _events = events;
            _poolConfig = poolConfig;
            PopulateEvents();
        }

        private void PopulateEvents() {
            EventCombo.Items.Clear();
            var eventSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (!string.IsNullOrEmpty(s.EventName) && !string.IsNullOrEmpty(s.Gender)) {
                    eventSet.Add(s.Gender + " " + s.EventName);
                }
            }
            foreach (string ev in eventSet.OrderBy(e => e)) EventCombo.Items.Add(ev);
            if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void Event_Changed(object sender, SelectionChangedEventArgs e) { UpdateStages(); }
        private void Stage_Changed(object sender, SelectionChangedEventArgs e) { UpdateInfo(); }
        private void Mode_Changed(object sender, SelectionChangedEventArgs e) { UpdateInfo(); }

        private void UpdateStages() {
            FromStageCombo.Items.Clear();
            string gender = "", eventName = "";
            ParseGenderEvent(out gender, out eventName);
            if (string.IsNullOrEmpty(eventName)) return;

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

        private void UpdateInfo() {
            InfoText.Text = "";
            WarningText.Text = "";
            _fromStage = FromStageCombo.SelectedItem != null ? FromStageCombo.SelectedItem.ToString() : "";
            if (string.IsNullOrEmpty(_fromStage)) {
                WarningText.Text = "该项目没有已完成的阶段成绩。请先完成比赛。";
                return;
            }

            string gender = "", eventName = "";
            ParseGenderEvent(out gender, out eventName);

            // 确定下一阶段
            if (_fromStage == "预赛") _toStage = "半决赛";
            else if (_fromStage == "复赛") _toStage = "半决赛";
            else if (_fromStage == "半决赛") _toStage = "决赛";
            else { _toStage = ""; return; }

            // 统计
            int total = 0, withResults = 0, heatCount = 0;
            var heats = new HashSet<int>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                var r = s.GetResultForStage(_fromStage);
                if (r != null && r.FinalTime > 0) {
                    withResults++;
                    heats.Add(r.Heat);
                }
                total++;
            }
            heatCount = heats.Count;

            int promoCount = 16;
            int.TryParse(CountBox.Text.Trim(), out promoCount);
            string modeName = GetPromotionModeName();

            InfoText.Text = string.Format("{0} {1} — {2}\n共 {3} 人参赛，{4} 人有成绩，{5} 个小组\n晋级模式: {6}，晋级 {7} 人到 {8}",
                gender, eventName, _fromStage, total, withResults, heatCount, modeName, promoCount, _toStage);

            if (withResults == 0) {
                WarningText.Text = "警告：该阶段没有成绩数据！请先完成比赛。";
            } else if (withResults < promoCount) {
                WarningText.Text = string.Format("注意：有成绩人数（{0}）少于晋级人数（{1}）", withResults, promoCount);
            }
        }

        private string GetPromotionModeName() {
            if (PromotionModeCombo == null || PromotionModeCombo.SelectedItem == null) return "总成绩排名";
            return ((ComboBoxItem)PromotionModeCombo.SelectedItem).Content.ToString();
        }

        private int GetPromotionModeIndex() {
            return PromotionModeCombo != null ? PromotionModeCombo.SelectedIndex : 0;
        }

        // ═══════════════════════════════════════════════════════════════
        // FINA晋级规则查询
        // ═══════════════════════════════════════════════════════════════
        private void Query_Click(object sender, RoutedEventArgs e) {
            string gender = "", eventName = "";
            ParseGenderEvent(out gender, out eventName);
            if (string.IsNullOrEmpty(_fromStage)) { MessageBox.Show("请选择数据来源阶段"); return; }

            int totalPromo = 16;
            int.TryParse(CountBox.Text.Trim(), out totalPromo);
            int modeIdx = GetPromotionModeIndex();

            // 获取所有有成绩的运动员，按组分类
            var heatResults = new Dictionary<int, List<SwimmerResult>>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ") continue;
                var r = s.GetResultForStage(_fromStage);
                if (r == null || r.FinalTime <= 0) continue;
                if (!heatResults.ContainsKey(r.Heat)) heatResults[r.Heat] = new List<SwimmerResult>();
                heatResults[r.Heat].Add(new SwimmerResult { Swimmer = s, Result = r });
            }

            // 每组内按成绩排序
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
                // 模式0：总成绩排名前N名
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
                foreach (var sr in selected) {
                    displayData.Add(MakeRow(rank++, sr, "总排名", _toStage));
                }

                // 检查边界并列
                if (selected.Count == totalPromo && all.Count > totalPromo) {
                    double cutoff = selected.Last().Result.FinalTime;
                    var tied = all.Where(x => x.Result.FinalTime == cutoff && !selected.Contains(x)).ToList();
                    if (tied.Count > 0) {
                        WarningText.Text = string.Format("警告：第{0}名成绩 {1} 存在并列{2}人！\n需比较触壁反应时间或加赛决定晋级。",
                            totalPromo, TimeFormatter.Format(cutoff), tied.Count + 1);
                    }
                }
            } else {
                // 模式1-3：每组前N名 + 成绩递补
                int perHeat = modeIdx + 1; // 模式1=前2名, 模式2=前3名, 模式3=前4名

                // 第一步：每组前N名直接晋级
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

                // 第二步：成绩递补（从未直接晋级的运动员中按总成绩补足到totalPromo人）
                remainders.Sort((a, b) => {
                    int cmp = a.Result.FinalTime.CompareTo(b.Result.FinalTime);
                    if (cmp != 0) return cmp;
                    return a.Result.StartingBlockTime.CompareTo(b.Result.StartingBlockTime);
                });

                int needed = totalPromo - directQualified.Count;
                var supplemented = remainders.Take(Math.Max(0, needed)).ToList();
                foreach (var sr in supplemented) sr.Method = "成绩递补";

                // 合并并按成绩排序
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
                foreach (var sr in allPromoted) {
                    displayData.Add(MakeRow(rank++, sr, sr.Method, _toStage));
                }

                // 检查递补边界并列
                if (supplemented.Count > 0 && needed > 0 && remainders.Count > needed) {
                    double cutoff = supplemented.Last().Result.FinalTime;
                    var tied = remainders.Where(x => x.Result.FinalTime == cutoff && !supplemented.Contains(x)).ToList();
                    if (tied.Count > 0) {
                        WarningText.Text += string.Format("\n警告：递补末位成绩 {0} 存在并列{1}人！需比较反应时间或加赛。",
                            TimeFormatter.Format(cutoff), tied.Count + 1);
                    }
                }

                InfoText.Text += string.Format("\n直接晋级: {0}人（每组前{1}名），成绩递补: {2}人", directQualified.Count, perHeat, supplemented.Count);
            }

            PromotionGrid.ItemsSource = displayData;
            ResultText.Text = string.Format("共 {0} 人晋级", _promoted.Count);
            ResultText.Foreground = new System.Windows.Media.SolidColorBrush(
                _promoted.Count > 0 ? System.Windows.Media.Colors.Green : System.Windows.Media.Colors.Red);
        }

        private object MakeRow(int rank, SwimmerResult sr, string method, string toStage) {
            return new {
                Rank = rank,
                BibNumber = sr.Swimmer.BibNumber ?? "",
                Name = sr.Swimmer.Name ?? "",
                Country = sr.Swimmer.Country ?? "",
                Heat = sr.Result.Heat,
                Time = TimeFormatter.Format(sr.Result.FinalTime),
                Reaction = sr.Result.StartingBlockTime > 0 ? sr.Result.StartingBlockTime.ToString("F2") : "",
                Method = method,
                ToStage = toStage
            };
        }

        private void Execute_Click(object sender, RoutedEventArgs e) {
            if (_promoted.Count == 0) { MessageBox.Show("请先点击\"查询\"查看晋级名单"); return; }
            if (string.IsNullOrEmpty(_toStage)) { MessageBox.Show("无法确定晋级目标阶段"); return; }

            string gender = "", eventName = "";
            ParseGenderEvent(out gender, out eventName);

            if (MessageBox.Show(
                string.Format("确认将 {0} 名运动员从 {1} 晋级到 {2}？\n将按{1}成绩蛇形分组。",
                    _promoted.Count, _fromStage, _toStage),
                "确认晋级", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var assignments = HeatScheduler.GenerateHeatsFromResults(_promoted, _poolConfig, eventName, _toStage, _fromStage);
            int heatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;

            ResultText.Text = string.Format("已晋级 {0} 人到{1}，{2}组", _promoted.Count, _toStage, heatCount);
            MessageBox.Show(string.Format("已将 {0} 名运动员晋级到 {1}，分为 {2} 组。\n请在赛程树中选择{1}的组次。",
                _promoted.Count, _toStage, heatCount), "晋级完成");
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private void ParseGenderEvent(out string gender, out string eventName) {
            gender = ""; eventName = "";
            string sel = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            int sp = sel.IndexOf(' ');
            if (sp > 0) { gender = sel.Substring(0, sp); eventName = sel.Substring(sp + 1); }
            else eventName = sel;
        }

        private class SwimmerResult {
            public Swimmer Swimmer;
            public LaneResult Result;
            public int HeatRank;
            public string Method = "";
        }
    }
}
