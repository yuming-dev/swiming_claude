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
        private ObservableCollection<ScheduleItem> _schedule;
        private List<string> _events;
        private PoolConfig _poolConfig;
        private List<Swimmer> _promoted = new List<Swimmer>();
        private string _toStage = "";
        private bool _initialized = false;

        public PromotionQueryWindow(ObservableCollection<Swimmer> swimmers, List<string> events, PoolConfig poolConfig, ObservableCollection<ScheduleItem> schedule = null) {
            InitializeComponent();
            _swimmers = swimmers;
            _events = events;
            _poolConfig = poolConfig;
            _schedule = schedule ?? new ObservableCollection<ScheduleItem>();
            PopulateAgeGroups();
            PopulateEvents();
            _initialized = true;
            UpdateStages();
        }

        // ═══════ 下拉框填充 ═══════
        private void PopulateAgeGroups() {
            AgeGroupCombo.Items.Clear();
            AgeGroupCombo.Items.Add("全部");
            foreach (var g in AgeGroupRegistry.Groups) AgeGroupCombo.Items.Add(g.Name);
            AgeGroupCombo.SelectedIndex = 0;
        }

        private void PopulateEvents() {
            EventCombo.Items.Clear();
            string ageFilter = GetAgeGroup();
            var eventSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (!string.IsNullOrEmpty(s.EventName) && MatchesAgeFilter(s, ageFilter))
                    eventSet.Add(s.EventName);
            }
            foreach (string ev in eventSet.OrderBy(e => e)) EventCombo.Items.Add(ev);
            if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private string GetAgeGroup() {
            return AgeGroupCombo != null && AgeGroupCombo.SelectedItem != null ? AgeGroupCombo.SelectedItem.ToString() : "全部";
        }

        private bool MatchesAgeFilter(Swimmer s, string ageFilter) {
            if (string.IsNullOrEmpty(ageFilter) || ageFilter == "全部") return true;
            return (s.AgeCategory ?? "") == ageFilter;
        }

        private void UpdateStages() {
            if (!_initialized) return;
            FromStageCombo.Items.Clear();
            string ageFilter = GetAgeGroup();
            string gender = GetGender();
            string eventName = GetEventName();
            if (string.IsNullOrEmpty(eventName)) return;

            // 从运动员成绩记录中提取有成绩的阶段
            var stagesWithResults = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (s.Gender == gender && s.EventName == eventName && MatchesAgeFilter(s, ageFilter)) {
                    foreach (var r in s.Results) {
                        if (r.FinalTime > 0) stagesWithResults.Add(r.Stage);
                    }
                }
            }

            string[] stageOrder = { "预赛", "半决赛" };
            foreach (string st in stageOrder) {
                if (stagesWithResults.Contains(st)) FromStageCombo.Items.Add(st);
            }
            if (FromStageCombo.Items.Count > 0) FromStageCombo.SelectedIndex = FromStageCombo.Items.Count - 1;
            UpdateInfo();
        }

        // ═══════ 事件处理 ═══════
        private void AgeGroup_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) { PopulateEvents(); UpdateStages(); } }
        private void Gender_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateStages(); }
        private void Event_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateStages(); }
        private void Stage_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateInfo(); }

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

            // 2026-06-18 提前取 ageFilter, 用于半决赛检查 (修跨组别误判)
            string ageFilter = GetAgeGroup();

            // 确定下一阶段并设置默认晋级人数
            if (fromStage == "预赛") {
                // 预赛晋级：检查赛程表是否有半决赛 — 按 ageGroup 过滤防跨年龄段污染
                bool hasSemis = false;
                foreach (var sch in _schedule) {
                    if (sch.Gender == gender && sch.EventName == eventName && sch.Stage == "半决赛"
                        && (ageFilter == "全部" || (sch.AgeGroup ?? "") == ageFilter)) {
                        hasSemis = true; break;
                    }
                }
                _toStage = hasSemis ? "半决赛" : "决赛";
            }
            else if (fromStage == "半决赛") _toStage = "决赛";
            else { _toStage = ""; return; }

            // 晋级到半决赛→16人，晋级到决赛→8人
            int defaultPromo = (_toStage == "半决赛") ? 16 : 8;
            CountBox.Text = defaultPromo.ToString();
            int total = 0, withResults = 0;
            var heats = new HashSet<int>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (!MatchesAgeFilter(s, ageFilter)) continue;
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

            // 收集所有有成绩的运动员（不分小组，统一排名；按组别过滤）
            string ageFilter = GetAgeGroup();
            var all = new List<SwimmerResult>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (!MatchesAgeFilter(s, ageFilter)) continue;
                if (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ") continue;
                var r = s.GetResultForStage(fromStage);
                if (r == null || r.FinalTime <= 0) continue;
                all.Add(new SwimmerResult { Swimmer = s, Result = r });
            }

            // 按成绩总排名（成绩相同比较反应时间）
            all.Sort((a, b) => {
                int cmp = a.Result.FinalTime.CompareTo(b.Result.FinalTime);
                if (cmp != 0) return cmp;
                return a.Result.StartingBlockTime.CompareTo(b.Result.StartingBlockTime);
            });

            _promoted.Clear();
            var displayData = new List<object>();

            var selected = all.Take(totalPromo).ToList();
            _promoted = selected.Select(x => x.Swimmer).ToList();
            int rank = 1;
            foreach (var sr in selected) displayData.Add(MakeRow(rank++, sr, "总排名"));

            // 并列检查
            if (selected.Count == totalPromo && all.Count > totalPromo) {
                double cutoff = selected.Last().Result.FinalTime;
                int tiedCount = all.Count(x => x.Result.FinalTime == cutoff) - selected.Count(x => x.Result.FinalTime == cutoff);
                if (tiedCount > 0) {
                    double cutReact = selected.Last().Result.StartingBlockTime;
                    int stillTied = all.Count(x => x.Result.FinalTime == cutoff && x.Result.StartingBlockTime == cutReact && !selected.Contains(x));
                    if (stillTied > 0)
                        WarningText.Text = string.Format("警告：第{0}名成绩{1}反应{2}s存在并列{3}人！需加赛。", totalPromo, TimeFormatter.Format(cutoff), cutReact.ToString("F2"), stillTied);
                }
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
                Reaction = sr.Result.StartingBlockTime != 0 ? sr.Result.StartingBlockTime.ToString("F2") : "",
                Method = method,
                ToStage = _toStage
            };
        }

        // 2026-05-23 C3：B 组决赛复选框变化时自动同步晋级人数为 16
        private void EnableBFinal_Changed(object sender, RoutedEventArgs e) {
            if (EnableBFinalCheck == null || CountBox == null) return;
            if (EnableBFinalCheck.IsChecked == true) {
                CountBox.Text = "16";   // A 组 8 + B 组 8
                if (InfoText != null) {
                    InfoText.Text = "已启用 B 组决赛：第 1-8 名 → 决赛（A 组）；第 9-16 名 → B组决赛（独立排名）。";
                    InfoText.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E40AF"));
                }
            }
        }

        // ═══════ 执行晋级 ═══════
        private void Execute_Click(object sender, RoutedEventArgs e) {
            if (_promoted.Count == 0) { MessageBox.Show("请先点击\"查询晋级名单\""); return; }
            if (string.IsNullOrEmpty(_toStage)) { MessageBox.Show("无法确定晋级目标阶段"); return; }

            string eventName = GetEventName();
            string fromStage = GetFromStage();
            // 2026-05-23 C3：仅在 预赛/半决赛 → 决赛 且选手 ≥9 时才允许 B 组决赛
            bool enableBFinal = EnableBFinalCheck != null && EnableBFinalCheck.IsChecked == true
                                && _toStage == "决赛" && _promoted.Count >= 9;

            string promptMsg;
            if (enableBFinal) {
                int aCnt = Math.Min(8, _promoted.Count);
                int bCnt = _promoted.Count - aCnt;
                promptMsg = string.Format(
                    "确认晋级（启用 B 组决赛）？\n  A 组决赛（决赛阶段）：第 1-{0} 名共 {0} 人；\n  B 组决赛（B组决赛阶段）：第 {1}-{2} 名共 {3} 人。\nB 组单独排名、不与 A 组合并；分别颁奖。",
                    aCnt, aCnt + 1, aCnt + bCnt, bCnt);
            } else {
                promptMsg = string.Format(
                    "确认将 {0} 名运动员从 {1} 晋级到 {2}？\n将按{1}成绩蛇形分组。", _promoted.Count, fromStage, _toStage);
            }
            if (MessageBox.Show(promptMsg, "确认晋级", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            if (enableBFinal) {
                var aGroup = _promoted.Take(8).ToList();
                var bGroup = _promoted.Skip(8).Take(8).ToList();
                HeatScheduler.GenerateHeatsFromResults(aGroup, _poolConfig, eventName, "决赛", fromStage);
                HeatScheduler.GenerateHeatsFromResults(bGroup, _poolConfig, eventName, "B组决赛", fromStage);
                ResultText.Text = string.Format("已晋级 A 组 {0} 人 + B 组 {1} 人", aGroup.Count, bGroup.Count);
                MessageBox.Show(string.Format(
                    "✅ A 组决赛：{0} 人，1 组（决赛 阶段）\n✅ B 组决赛：{1} 人，1 组（B组决赛 阶段）\n\nA/B 两组单独排名、单独颁奖。请在赛程树中分别选择两个阶段的组次。",
                    aGroup.Count, bGroup.Count), "晋级完成（B 组决赛）");
            } else {
                var assignments = HeatScheduler.GenerateHeatsFromResults(_promoted, _poolConfig, eventName, _toStage, fromStage);
                int heatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;
                ResultText.Text = string.Format("已晋级 {0} 人到{1}，{2}组", _promoted.Count, _toStage, heatCount);
                MessageBox.Show(string.Format("已将 {0} 名运动员晋级到 {1}，分为 {2} 组。\n请在赛程树中选择{1}的组次。", _promoted.Count, _toStage, heatCount), "晋级完成");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        // 2026-05-23 C6 决赛弃权递补 (FINA SW + 中国泳协规则)
        // 流程: 扫描 决赛/B组决赛 阶段已分组但 Status ∈ {DSQ,DNS,DNF} 的运动员视为弃权
        //       按原 fromStage 排名找下一位不在 decisive stage 的运动员作为递补
        //       弹窗显示 弃权者 → 递补者 对照表，确认后批量替换 StageAssignment
        // 注: 30 分钟倒计时未实现，由裁判按业务节奏手动按"查决赛弃权 & 递补"即可
        private void CheckSubstitutes_Click(object sender, RoutedEventArgs e) {
            string eventName = GetEventName();
            string fromStage = GetFromStage();
            string gender = GetGender();
            string ageFilter = GetAgeGroup();
            if (string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(fromStage)) {
                MessageBox.Show("请先选择项目和\"上一轮\"赛次"); return;
            }

            // 全员预赛排名（用于找候选）
            var rankings = _swimmers
                .Where(s => s.Gender == gender && s.EventName == eventName && MatchesAgeFilter(s, ageFilter))
                .Select(s => new SwimmerResult { Swimmer = s, Result = s.GetResultForStage(fromStage) })
                .Where(sr => sr.Result != null && sr.Result.FinalTime > 0)
                .OrderBy(sr => sr.Result.FinalTime)
                .ThenBy(sr => sr.Result.StartingBlockTime)
                .ToList();

            // 决赛 & B 组决赛 两个阶段都查
            var decisiveStages = new[] { "决赛", "B组决赛" };
            // 已在 decisive stage 中（任一）的运动员（含弃权者本身）
            var alreadyDecisive = new HashSet<Swimmer>(
                _swimmers.Where(s => decisiveStages.Any(st => s.GetAssignmentForStage(st) != null)));

            var swaps = new List<SubstitutionPair>();
            foreach (var stage in decisiveStages) {
                var giveups = _swimmers
                    .Where(s => s.Gender == gender && s.EventName == eventName && MatchesAgeFilter(s, ageFilter))
                    .Where(s => s.GetAssignmentForStage(stage) != null)
                    .Where(s => s.Status == "DSQ" || s.Status == "DNS" || s.Status == "DNF")
                    .ToList();
                if (giveups.Count == 0) continue;

                // 候选 = 排名表中不在任一 decisive stage 的运动员，按预赛排名递补
                var candQueue = new Queue<Swimmer>(rankings.Select(sr => sr.Swimmer).Where(s => !alreadyDecisive.Contains(s)));
                foreach (var giveup in giveups) {
                    if (candQueue.Count == 0) break;
                    var subin = candQueue.Dequeue();
                    var a = giveup.GetAssignmentForStage(stage);
                    swaps.Add(new SubstitutionPair {
                        Giveup = giveup, SubIn = subin, Stage = stage,
                        Heat = a.Heat, Lane = a.Lane
                    });
                    alreadyDecisive.Add(subin);   // 避免一人占多个空位
                }
            }

            if (swaps.Count == 0) {
                MessageBox.Show("当前 决赛/B组决赛 阶段没有需要递补的弃权者。\n（仅识别 Status=DSQ/DNS/DNF 的已分组运动员）",
                    "无弃权", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 预览
            var preview = new System.Text.StringBuilder();
            preview.AppendLine(string.Format("找到 {0} 项弃权递补:", swaps.Count));
            preview.AppendLine();
            foreach (var sw in swaps) {
                preview.AppendLine(string.Format(
                    "  [{0}] 弃权: {1} ({2})  →  递补: {3}  (第{4}组 {5}道)",
                    sw.Stage, sw.Giveup.Name, sw.Giveup.Status,
                    sw.SubIn.Name, sw.Heat, sw.Lane));
            }
            preview.AppendLine();
            preview.AppendLine("确认执行替换？(原弃权者的决赛分组将被清除)");

            if (MessageBox.Show(preview.ToString(), "弃权递补确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            // 执行
            foreach (var sw in swaps) {
                var rs = sw.SubIn.GetResultForStage(fromStage);
                double t = (rs != null && rs.FinalTime > 0) ? rs.FinalTime : 0;
                string tStr = t > 0 ? TimeFormatter.Format(t) : "";
                sw.SubIn.SetStageAssignment(sw.Stage, sw.Heat, sw.Lane, t, tStr);
                sw.SubIn.CurrentStage = sw.Stage;
                // 清弃权者本阶段的 assignment
                if (sw.Giveup.StageAssignments != null && sw.Giveup.StageAssignments.ContainsKey(sw.Stage))
                    sw.Giveup.StageAssignments.Remove(sw.Stage);
            }

            ResultText.Text = string.Format("已完成 {0} 项弃权递补", swaps.Count);
            ResultText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E"));
            MessageBox.Show(string.Format("已替换 {0} 个决赛位次。\n请回到主界面赛程树查看更新后的组次。",
                swaps.Count), "递补完成");
        }

        // C6 辅助：递补对照条目
        private class SubstitutionPair {
            public Swimmer Giveup { get; set; }
            public Swimmer SubIn { get; set; }
            public string Stage { get; set; }
            public int Heat { get; set; }
            public int Lane { get; set; }
        }

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
        }
    }
}
