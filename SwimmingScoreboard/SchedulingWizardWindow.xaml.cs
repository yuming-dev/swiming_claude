using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    // 2026-05-24 秩序册生成向导窗口 — 索美式 5 步工作流
    //   1. 赛次开设计划      (Tab1，本期实现：自动统计 + DataGrid 编辑)
    //   2. 赛次设置方法引导  (Tab2，本期实现：静态说明)
    //   3. 编排比赛日程表    (Tab3，下期：双面板可视化编排)
    //   4. 秩序册可选文档    (Tab4，下期：15 项 CheckList + 出 xlsx)
    //   5. 运动员兼项统计    (Tab5，下期：查询面板)
    public partial class SchedulingWizardWindow : Window
    {
        // ─── 数据源（由调用方传入，引用主程序 _swimmers 等）───
        private readonly ObservableCollection<Swimmer> _swimmers;
        private readonly List<string> _events;
        private readonly List<AgeGroup> _ageGroups;
        private readonly List<string> _genders;
        private readonly PoolConfig _poolConfig;
        private readonly ScoringConfig _scoringConfig;

        // Tab 1 数据
        private ObservableCollection<SchedulingPlanEntry> _planRows = new ObservableCollection<SchedulingPlanEntry>();
        // 记录每行的"自动统计基线"快照，用户手改后可一键撤销
        private Dictionary<string, SchedulingPlanEntry> _autoBaseline = new Dictionary<string, SchedulingPlanEntry>();

        public SchedulingWizardWindow(
            ObservableCollection<Swimmer> swimmers,
            List<string> events,
            List<AgeGroup> ageGroups,
            List<string> genders,
            PoolConfig poolConfig,
            ScoringConfig scoringConfig) {

            InitializeComponent();
            _swimmers = swimmers;
            _events = events ?? new List<string>();
            _ageGroups = ageGroups ?? new List<AgeGroup>();
            _genders = genders ?? new List<string> { "男", "女" };
            _poolConfig = poolConfig ?? new PoolConfig();
            _scoringConfig = scoringConfig ?? new ScoringConfig();

            StagePlanGrid.ItemsSource = _planRows;
            NavList.SelectedIndex = 0;   // 默认进入 Tab 1

            // 初始自动统计一次，免得用户首次进来看到空表
            AutoComputeStages_Click(null, null);
        }

        // ═══════════════════════════════════════════════════════════════
        // 导航
        // ═══════════════════════════════════════════════════════════════
        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            int idx = NavList.SelectedIndex;
            if (idx < 0) return;
            Tab1Panel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            Tab2Panel.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            Tab3Panel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            Tab4Panel.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            Tab5Panel.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;

            string[] titles = {
                "秩序册 < 一键生成 > — 赛次开设计划",
                "赛次设置方法（World Aquatics SW 3.1.1）",
                "编排比赛日程表 — 双面板可视化",
                "秩序册可选文档（15 项）",
                "运动员兼项统计"
            };
            if (idx < titles.Length) TitleText.Text = titles[idx];

            // 进入 Tab 3/4/5 时按需初始化数据
            if (idx == 2) EnterTab3();
            else if (idx == 3) EnterTab4();
            else if (idx == 4) EnterTab5();

            PrevBtn.IsEnabled = idx > 0;
            NextBtn.IsEnabled = idx < 4;
            NextBtn.Content = idx == 3 ? "生成秩序册 ▶" : "下一步 ▶";
        }

        private void Prev_Click(object sender, RoutedEventArgs e) {
            if (NavList.SelectedIndex > 0) NavList.SelectedIndex--;
        }
        private void Next_Click(object sender, RoutedEventArgs e) {
            if (NavList.SelectedIndex < 4) NavList.SelectedIndex++;
        }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        // ═══════════════════════════════════════════════════════════════
        // Tab 1: 赛次开设计划 — 自动统计
        // ═══════════════════════════════════════════════════════════════
        private void AutoComputeStages_Click(object sender, RoutedEventArgs e) {
            _planRows.Clear();
            _autoBaseline.Clear();

            int laneCount = _poolConfig.LaneCount > 0 ? _poolConfig.LaneCount : 8;

            // 三维遍历 组别 × 性别 × 项目（全部组合都出一行，0 报名也显示）
            var ageGroupNames = _ageGroups.Count > 0
                ? _ageGroups.Select(g => g.Name).ToList()
                : new List<string> { "" };

            foreach (var ag in ageGroupNames) {
                foreach (var gender in _genders) {
                    foreach (var ev in _events) {
                        if (string.IsNullOrEmpty(ev)) continue;

                        // 统计该 (组别,性别,项目) 的报名人数（不算接力队员条目）
                        int count = _swimmers.Count(s =>
                            s.EventName == ev &&
                            s.Gender == gender &&
                            (string.IsNullOrEmpty(ag) || s.AgeCategory == ag) &&
                            (s.Notes == null || !s.Notes.StartsWith("接力队员")));

                        var row = ComputeOneRow(ag, gender, ev, count, laneCount);
                        _planRows.Add(row);

                        // 保存快照（深拷贝），用户手改后可撤销
                        _autoBaseline[row.Key] = CloneRow(row);
                    }
                }
            }

            UpdateTab1CountText();
            StatusText.Text = string.Format("已自动统计 {0} 单项（{1} 个组别 × {2} 性别 × {3} 项目）。修改后可撤销。",
                _planRows.Count, ageGroupNames.Count, _genders.Count, _events.Count);
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E"));
        }

        // 按 FINA SW 3.1.1 + 用户配置 SeedHeats 算 4 个阶段的组数 / 录取数
        private SchedulingPlanEntry ComputeOneRow(string ageGroup, string gender, string eventName, int participants, int laneCount) {
            var row = new SchedulingPlanEntry {
                AgeGroup = ageGroup, Gender = gender, EventName = eventName, Participants = participants
            };
            if (participants <= 0) return row;   // 全 0 留空，用户手动开设也行

            var stages = HeatScheduler.GetStages(participants, eventName);
            int prelimsHeats = (int)Math.Ceiling((double)participants / laneCount);

            foreach (var stage in stages) {
                if (stage == "预赛") {
                    row.PrelimHeats = prelimsHeats;
                    row.PrelimCutoff = stages.Contains("半决赛") ? 16 : 8;   // 预赛→半决赛 取 16；预赛→决赛 取 8
                } else if (stage == "次复赛") {
                    row.QuarterHeats = 2;
                    row.QuarterCutoff = 8;
                } else if (stage == "半决赛") {
                    row.SemiHeats = 2;
                    row.SemiCutoff = 8;
                } else if (stage == "决赛") {
                    // 长距离快慢组：多组决赛；其余 1 组
                    if (IsLongDistance(eventName) && participants > laneCount) {
                        row.FinalHeats = prelimsHeats;
                    } else {
                        row.FinalHeats = 1;
                    }
                    row.FinalCutoff = Math.Min(8, participants);
                } else if (stage == "B组决赛") {
                    // 当前 GetStages 不会返回这个；裁判勾选 B 组决赛时另外加。这里预留。
                }
            }
            return row;
        }

        private static bool IsLongDistance(string eventName) {
            if (string.IsNullOrEmpty(eventName)) return false;
            if (eventName.Contains("接力")) return false;
            return eventName.Contains("800米") || eventName.Contains("1500米");
        }

        private static SchedulingPlanEntry CloneRow(SchedulingPlanEntry r) {
            return new SchedulingPlanEntry {
                AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                Participants = r.Participants,
                PrelimHeats = r.PrelimHeats, PrelimCutoff = r.PrelimCutoff,
                QuarterHeats = r.QuarterHeats, QuarterCutoff = r.QuarterCutoff,
                SemiHeats = r.SemiHeats, SemiCutoff = r.SemiCutoff,
                FinalHeats = r.FinalHeats, FinalCutoff = r.FinalCutoff
            };
        }

        private void ResetEdits_Click(object sender, RoutedEventArgs e) {
            if (_autoBaseline.Count == 0) {
                MessageBox.Show("还没有自动统计数据，请先点 「🔄 自动统计赛次」", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show("将所有手动改动恢复到上次自动统计的结果？", "确认撤销",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            for (int i = 0; i < _planRows.Count; i++) {
                var baseline = _autoBaseline.ContainsKey(_planRows[i].Key) ? _autoBaseline[_planRows[i].Key] : null;
                if (baseline == null) continue;
                _planRows[i].PrelimHeats = baseline.PrelimHeats;
                _planRows[i].PrelimCutoff = baseline.PrelimCutoff;
                _planRows[i].QuarterHeats = baseline.QuarterHeats;
                _planRows[i].QuarterCutoff = baseline.QuarterCutoff;
                _planRows[i].SemiHeats = baseline.SemiHeats;
                _planRows[i].SemiCutoff = baseline.SemiCutoff;
                _planRows[i].FinalHeats = baseline.FinalHeats;
                _planRows[i].FinalCutoff = baseline.FinalCutoff;
            }
            StatusText.Text = "已恢复到自动统计基线";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6"));
        }

        private void StagePlanGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) {
            // CellEditEnding 触发时绑定还没刷回 source；用 Dispatcher 延迟更新计数文字
            Dispatcher.BeginInvoke(new Action(UpdateTab1CountText));
        }

        private void UpdateTab1CountText() {
            int totalOpened = _planRows.Count(r =>
                r.PrelimHeats > 0 || r.QuarterHeats > 0 || r.SemiHeats > 0 || r.FinalHeats > 0);
            Tab1CountText.Text = string.Format("共 {0} 单项（含 0 报名 {1} 项；实际开设 {2} 项）",
                _planRows.Count,
                _planRows.Count(r => r.Participants == 0),
                totalOpened);
        }

        // ═══════════════════════════════════════════════════════════════
        // Tab 3: 双面板日程编排器
        // ═══════════════════════════════════════════════════════════════
        // 待分配条目（每个 stage 一行：组别×性别×项目×阶段）
        private ObservableCollection<DistEntry> _distPending = new ObservableCollection<DistEntry>();
        // 全部已分配条目（按 Date + Session 分组显示在右面板）
        private ObservableCollection<DistEntry> _distAssigned = new ObservableCollection<DistEntry>();
        // 右面板当前显示的子集
        private ObservableCollection<DistEntry> _distVisible = new ObservableCollection<DistEntry>();
        // 当前赛事的日期列表（从赛事开始/结束日期 + 天数推断）
        private List<string> _availableDates = new List<string>();

        private void EnterTab3() {
            // 第一次进入时从 Tab 1 派生待分配条目（如果还没有）
            if (_distPending.Count == 0 && _distAssigned.Count == 0) {
                BuildDistPendingFromPlan();
            }
            // 填充日期下拉（从 _swimmers 找现有比赛日期，或者默认今天）
            if (_availableDates.Count == 0) {
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                _availableDates.Add(today);
            }
            DistDayCombo.ItemsSource = null;
            DistDayCombo.ItemsSource = _availableDates;
            if (DistDayCombo.SelectedIndex < 0 && _availableDates.Count > 0) DistDayCombo.SelectedIndex = 0;

            DistPendingGrid.ItemsSource = _distPending;
            DistAssignedGrid.ItemsSource = _distVisible;
            RefreshDistVisible();
            UpdateDistCounters();
        }

        private void BuildDistPendingFromPlan() {
            _distPending.Clear();
            _distAssigned.Clear();
            foreach (var r in _planRows) {
                // 每个 stage 单独一个待分配条目
                if (r.PrelimHeats > 0)
                    _distPending.Add(new DistEntry { AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                        Participants = r.Participants, Heats = r.PrelimHeats, Cutoff = r.PrelimCutoff,
                        Stage = "预赛", MinPerHeat = EstimateMinutesPerHeat(r.EventName) });
                if (r.QuarterHeats > 0)
                    _distPending.Add(new DistEntry { AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                        Participants = r.Participants, Heats = r.QuarterHeats, Cutoff = r.QuarterCutoff,
                        Stage = "次复赛", MinPerHeat = EstimateMinutesPerHeat(r.EventName) });
                if (r.SemiHeats > 0)
                    _distPending.Add(new DistEntry { AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                        Participants = r.Participants, Heats = r.SemiHeats, Cutoff = r.SemiCutoff,
                        Stage = "半决赛", MinPerHeat = EstimateMinutesPerHeat(r.EventName) });
                if (r.FinalHeats > 0)
                    _distPending.Add(new DistEntry { AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                        Participants = r.Participants, Heats = r.FinalHeats, Cutoff = r.FinalCutoff,
                        Stage = "决赛", MinPerHeat = EstimateMinutesPerHeat(r.EventName) });
            }
            ApplyDistSort();
        }

        // 粗估每组用时（分钟），可在右面板手动调整
        private static int EstimateMinutesPerHeat(string ev) {
            if (string.IsNullOrEmpty(ev)) return 5;
            if (ev.Contains("1500")) return 25;
            if (ev.Contains("800")) return 15;
            if (ev.Contains("400")) return 8;
            if (ev.Contains("200")) return 5;
            if (ev.Contains("100")) return 3;
            if (ev.Contains("50")) return 2;
            if (ev.Contains("接力")) return 8;
            return 5;
        }

        private void ApplyDistSort() {
            string sort = DistSortCombo != null && DistSortCombo.SelectedItem != null
                ? ((ComboBoxItem)DistSortCombo.SelectedItem).Content.ToString() : "按比赛项目";
            List<DistEntry> sorted;
            if (sort == "按比赛组别") {
                sorted = _distPending.OrderBy(d => d.AgeGroup ?? "").ThenBy(d => d.Gender ?? "").ThenBy(d => d.EventName ?? "").ThenBy(d => StageOrder(d.Stage)).ToList();
            } else if (sort == "按赛次") {
                sorted = _distPending.OrderBy(d => StageOrder(d.Stage)).ThenBy(d => d.EventName ?? "").ThenBy(d => d.AgeGroup ?? "").ThenBy(d => d.Gender ?? "").ToList();
            } else {
                sorted = _distPending.OrderBy(d => EventOrder(d.EventName)).ThenBy(d => d.AgeGroup ?? "").ThenBy(d => d.Gender ?? "").ThenBy(d => StageOrder(d.Stage)).ToList();
            }
            _distPending.Clear();
            foreach (var d in sorted) _distPending.Add(d);
        }

        private static int StageOrder(string stage) {
            switch (stage) {
                case "预赛": return 1;
                case "次复赛": return 2;
                case "半决赛": return 3;
                case "决赛": return 4;
                case "B组决赛": return 5;
                default: return 9;
            }
        }
        private static int EventOrder(string ev) {
            if (string.IsNullOrEmpty(ev)) return 99;
            // FINA 项目顺序: 自由 → 仰 → 蛙 → 蝶 → 混 → 接力
            int strokeOrder = 99;
            if (ev.Contains("自由泳")) strokeOrder = 1;
            else if (ev.Contains("仰泳")) strokeOrder = 2;
            else if (ev.Contains("蛙泳")) strokeOrder = 3;
            else if (ev.Contains("蝶泳")) strokeOrder = 4;
            else if (ev.Contains("混合泳") || ev.Contains("混")) strokeOrder = 5;
            if (ev.Contains("接力")) strokeOrder = 6;
            // 距离顺序: 50/100/200/400/800/1500
            int dist = 0;
            foreach (var d in new[] { 50, 100, 200, 400, 800, 1500 }) {
                if (ev.Contains(d + "米")) { dist = d; break; }
            }
            return strokeOrder * 10000 + dist;
        }

        private void DistSort_Changed(object sender, SelectionChangedEventArgs e) {
            ApplyDistSort();
        }
        private void DistDay_Changed(object sender, SelectionChangedEventArgs e) {
            RefreshDistVisible();
        }
        private void DistSession_Changed(object sender, SelectionChangedEventArgs e) {
            RefreshDistVisible();
        }

        private void RefreshDistVisible() {
            _distVisible.Clear();
            string day = DistDayCombo != null ? (DistDayCombo.SelectedItem as string) : null;
            string session = DistSessionCombo != null && DistSessionCombo.SelectedItem != null
                ? ((ComboBoxItem)DistSessionCombo.SelectedItem).Content.ToString() : "";
            if (string.IsNullOrEmpty(day) || string.IsNullOrEmpty(session)) return;
            int seq = 0;
            foreach (var d in _distAssigned.Where(x => x.AssignedDate == day && x.AssignedSession == session).OrderBy(x => x.AssignedSortKey)) {
                d.SeqInSession = ++seq;
                _distVisible.Add(d);
            }
        }

        private void UpdateDistCounters() {
            DistPendingText.Text = _distPending.Count.ToString();
            DistAssignedText.Text = _distAssigned.Count.ToString();
        }

        private void DistMoveDown_Click(object sender, RoutedEventArgs e) { MoveSelectedToAssigned(insertAtEnd: true); }
        private void DistMoveUp_Click(object sender, RoutedEventArgs e) { MoveSelectedToAssigned(insertAtEnd: false); }

        private void MoveSelectedToAssigned(bool insertAtEnd) {
            var selected = DistPendingGrid.SelectedItems.OfType<DistEntry>().ToList();
            if (selected.Count == 0) {
                MessageBox.Show("请先在左侧选中要分配的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string day = DistDayCombo.SelectedItem as string;
            string session = DistSessionCombo.SelectedItem != null ? ((ComboBoxItem)DistSessionCombo.SelectedItem).Content.ToString() : "";
            if (string.IsNullOrEmpty(day) || string.IsNullOrEmpty(session)) {
                MessageBox.Show("请先选择比赛日期和时段", "提示"); return;
            }
            int sessionStartMin = GetSessionStartMin(session);
            // 找该时段当前已分配的累计时长
            int curMin = ComputeSessionUsedMinutes(day, session);
            // 找右面板选中位置：若指定 insertAtEnd=false 且有选中，插到选中前；否则追加到末尾
            DistEntry insertBefore = null;
            if (!insertAtEnd && DistAssignedGrid.SelectedItem is DistEntry sel) insertBefore = sel;

            foreach (var d in selected) {
                d.AssignedDate = day;
                d.AssignedSession = session;
                d.AssignedTime = string.Format("{0:D2}:{1:D2}", (sessionStartMin + curMin) / 60, (sessionStartMin + curMin) % 60);
                d.AssignedSortKey = sessionStartMin + curMin;
                curMin += Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + 2; // +2min 项目间隔
                _distAssigned.Add(d);
                _distPending.Remove(d);
            }
            RefreshDistVisible();
            UpdateDistCounters();
        }

        private int ComputeSessionUsedMinutes(string day, string session) {
            int sessionStart = GetSessionStartMin(session);
            int maxOffset = 0;
            foreach (var d in _distAssigned.Where(x => x.AssignedDate == day && x.AssignedSession == session)) {
                int duration = Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + 2;
                int endOffset = (d.AssignedSortKey - sessionStart) + duration;
                if (endOffset > maxOffset) maxOffset = endOffset;
            }
            return maxOffset;
        }

        private static int GetSessionStartMin(string session) {
            // 与主程序 AutoBuildSchedule 默认时段保持一致
            if (session == "上午") return 9 * 60;          // 09:00
            if (session == "下午") return 14 * 60 + 30;    // 14:30
            if (session == "晚上") return 19 * 60 + 30;    // 19:30
            return 9 * 60;
        }

        private void DistAuto_Click(object sender, RoutedEventArgs e) {
            if (_distPending.Count == 0) {
                MessageBox.Show("已无待分配项目", "提示"); return;
            }
            // 自动算法：按 FINA 项目顺序 → 把预赛放上午、半决赛放下午、决赛放晚上（除非编排模式覆盖）
            string mode = DistModeAuto.IsChecked == true ? "自动" :
                          DistModeStrictTime.IsChecked == true ? "强制时段" :
                          DistModeFinalOnly.IsChecked == true ? "强制决赛" : "自动";

            // 排序待分配项目
            var ordered = _distPending.OrderBy(d => EventOrder(d.EventName))
                                       .ThenBy(d => StageOrder(d.Stage))
                                       .ThenBy(d => d.AgeGroup ?? "")
                                       .ThenBy(d => d.Gender ?? "")
                                       .ToList();

            string day = DistDayCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(day)) day = DateTime.Today.ToString("yyyy-MM-dd");

            int morningUsed = ComputeSessionUsedMinutes(day, "上午");
            int afternoonUsed = ComputeSessionUsedMinutes(day, "下午");
            int eveningUsed = ComputeSessionUsedMinutes(day, "晚上");

            foreach (var d in ordered) {
                string session;
                if (mode == "强制决赛") {
                    session = "晚上";
                } else if (mode == "强制时段") {
                    // 强制按当前选择的时段
                    session = DistSessionCombo.SelectedItem != null ? ((ComboBoxItem)DistSessionCombo.SelectedItem).Content.ToString() : "上午";
                } else {
                    // 自动：预赛/次复赛 → 上午；半决赛 → 下午；决赛 → 晚上
                    if (d.Stage == "决赛" || d.Stage == "B组决赛") session = "晚上";
                    else if (d.Stage == "半决赛") session = "下午";
                    else session = "上午";
                }

                int sessionStart = GetSessionStartMin(session);
                int used = session == "上午" ? morningUsed : session == "下午" ? afternoonUsed : eveningUsed;
                int duration = Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + 2;

                d.AssignedDate = day;
                d.AssignedSession = session;
                d.AssignedTime = string.Format("{0:D2}:{1:D2}", (sessionStart + used) / 60, (sessionStart + used) % 60);
                d.AssignedSortKey = sessionStart + used;

                if (session == "上午") morningUsed += duration;
                else if (session == "下午") afternoonUsed += duration;
                else eveningUsed += duration;

                _distAssigned.Add(d);
            }
            _distPending.Clear();
            RefreshDistVisible();
            UpdateDistCounters();
            MessageBox.Show(string.Format("已自动分配 {0} 项到 {1} ({2} 模式)。\n您可以手动微调每项的「分/组」时长或拖回左侧重排。",
                _distAssigned.Count, day, mode), "自动分配完成");
        }

        private void DistMoveBack_Click(object sender, RoutedEventArgs e) {
            var selected = DistAssignedGrid.SelectedItems.OfType<DistEntry>().ToList();
            if (selected.Count == 0) {
                MessageBox.Show("请先在右侧选中要取消分配的项目", "提示"); return;
            }
            foreach (var d in selected) {
                d.AssignedDate = null;
                d.AssignedSession = null;
                d.AssignedTime = null;
                _distAssigned.Remove(d);
                _distPending.Add(d);
            }
            ApplyDistSort();
            RefreshDistVisible();
            UpdateDistCounters();
        }

        private void DistMoveBackAll_Click(object sender, RoutedEventArgs e) {
            if (_distAssigned.Count == 0) return;
            if (MessageBox.Show(string.Format("确认将所有 {0} 项已分配项目移回待分配？", _distAssigned.Count), "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var d in _distAssigned.ToList()) {
                d.AssignedDate = null;
                d.AssignedSession = null;
                d.AssignedTime = null;
                _distPending.Add(d);
            }
            _distAssigned.Clear();
            ApplyDistSort();
            RefreshDistVisible();
            UpdateDistCounters();
        }

        private void DistAssignedGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) {
            // 用户改了 分/组 → 重新计算该时段后续项目的时间
            Dispatcher.BeginInvoke(new Action(RecomputeAssignedTimes));
        }

        private void RecomputeAssignedTimes() {
            string day = DistDayCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(day)) return;
            foreach (var session in new[] { "上午", "下午", "晚上" }) {
                int start = GetSessionStartMin(session);
                int used = 0;
                var inSession = _distAssigned.Where(x => x.AssignedDate == day && x.AssignedSession == session)
                                              .OrderBy(x => x.AssignedSortKey).ToList();
                foreach (var d in inSession) {
                    d.AssignedTime = string.Format("{0:D2}:{1:D2}", (start + used) / 60, (start + used) % 60);
                    d.AssignedSortKey = start + used;
                    used += Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + 2;
                }
            }
            RefreshDistVisible();
        }

        private void DistApply_Click(object sender, RoutedEventArgs e) {
            if (_distAssigned.Count == 0) {
                MessageBox.Show("没有已分配的项目，无法写回主程序赛程", "提示"); return;
            }
            // 把已分配条目转成 ScheduleItem 写回主程序 _schedule
            // 注：此处只通知调用方"用户确认了编排，需要写回"。实际写回逻辑在 OpenSchedulingWizard_Click 端处理。
            // 为简化跨窗口通信，我们把分配结果暴露成 public List 供调用方读取
            AssignedItems = _distAssigned.Select(d => new AssignedScheduleItem {
                Date = d.AssignedDate, Session = d.AssignedSession, Time = d.AssignedTime,
                AgeGroup = d.AgeGroup, Gender = d.Gender, EventName = d.EventName,
                Stage = d.Stage, HeatCount = d.Heats
            }).ToList();
            if (MessageBox.Show(string.Format("将 {0} 项编排写回主程序赛程？\n（旧赛程会被清除）", _distAssigned.Count),
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            ApplyToMainSchedule = true;
            DialogResult = true;
            Close();
        }

        // 调用方读取这些字段把数据写回 _schedule
        public bool ApplyToMainSchedule { get; private set; }
        public List<AssignedScheduleItem> AssignedItems { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        // Tab 4: 秩序册可选文档（15 项 CheckList）
        // ═══════════════════════════════════════════════════════════════
        private List<CheckBox> _docCheckBoxes = new List<CheckBox>();
        private static readonly string[] DocSections = new[] {
            "1. 封面（运动会名称 + 副标题 + 主办承办）",
            "2. 目录",
            "3. 主席团名单",
            "4. 组织委员会名单",
            "5. 大会工作机构名单",
            "6. 技术代表、技术官员、仲裁委员会、裁判员名单",
            "7. 竞赛规程",
            "8. 体育道德风尚奖评选方法",
            "9. 开幕式、闭幕式程序",
            "10. 比赛场地平面图",
            "11. 参赛人员统计表",
            "12. 运动员姓名号码对照表",
            "13. 竞赛日程表",
            "14. 比赛记录（最高纪录）",
            "15. 各项运动员竞赛分组名单"
        };

        private void EnterTab4() {
            if (_docCheckBoxes.Count == 0) {
                DocSectionsPanel.Children.Clear();
                foreach (var name in DocSections) {
                    var cb = new CheckBox {
                        Content = name, IsChecked = true,
                        Margin = new Thickness(0, 4, 0, 4), FontSize = 13
                    };
                    _docCheckBoxes.Add(cb);
                    DocSectionsPanel.Children.Add(cb);
                }
            }
        }

        private void DocSelectAll_Click(object sender, RoutedEventArgs e) {
            foreach (var cb in _docCheckBoxes) cb.IsChecked = true;
        }
        private void DocSelectNone_Click(object sender, RoutedEventArgs e) {
            foreach (var cb in _docCheckBoxes) cb.IsChecked = false;
        }

        private void DocGenerate_Click(object sender, RoutedEventArgs e) {
            var sel = new List<string>();
            for (int i = 0; i < _docCheckBoxes.Count; i++)
                if (_docCheckBoxes[i].IsChecked == true) sel.Add(DocSections[i]);
            if (sel.Count == 0) {
                MessageBox.Show("请至少勾选一项", "提示"); return;
            }
            // MVP 版：写 .xlsx 用 NPOI，按勾选章节生成单一工作簿
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "Excel 工作簿|*.xlsx", Title = "保存秩序册",
                FileName = "秩序册_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                GenerateOrderBookXlsx(dlg.FileName, sel);
                MessageBox.Show(string.Format("秩序册已生成：\n{0}\n\n含 {1} 个章节。", dlg.FileName, sel.Count),
                    "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                MessageBox.Show("生成失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // NPOI 输出（MVP 版：每章一个 Sheet 占位，含表头 + 简单内容）
        private void GenerateOrderBookXlsx(string path, List<string> sections) {
            var wb = new NPOI.XSSF.UserModel.XSSFWorkbook();
            foreach (var sectionTitle in sections) {
                string sheetName = sectionTitle;
                // Excel sheet 名禁止字符 + 最长 31
                foreach (var ch in new[] { '/', '\\', '?', '*', '[', ']', ':' }) sheetName = sheetName.Replace(ch, '_');
                if (sheetName.Length > 31) sheetName = sheetName.Substring(0, 31);
                var sheet = wb.CreateSheet(sheetName);
                int r = 0;
                var headerRow = sheet.CreateRow(r++);
                headerRow.CreateCell(0).SetCellValue(sectionTitle);

                // 章节专用内容
                if (sectionTitle.Contains("参赛人员统计表")) {
                    var hr = sheet.CreateRow(r++);
                    hr.CreateCell(0).SetCellValue("序号"); hr.CreateCell(1).SetCellValue("参赛单位");
                    hr.CreateCell(2).SetCellValue("组别"); hr.CreateCell(3).SetCellValue("男子人数"); hr.CreateCell(4).SetCellValue("女子人数");
                    var grouped = _swimmers
                        .Where(s => s.Notes == null || !s.Notes.StartsWith("接力队员"))
                        .GroupBy(s => (s.Country ?? "") + "|" + (s.AgeCategory ?? ""))
                        .OrderBy(g => g.Key).ToList();
                    int idx = 1;
                    foreach (var g in grouped) {
                        var parts = g.Key.Split('|');
                        int men = g.Count(s => s.Gender == "男");
                        int women = g.Count(s => s.Gender == "女");
                        var rr = sheet.CreateRow(r++);
                        rr.CreateCell(0).SetCellValue(idx++);
                        rr.CreateCell(1).SetCellValue(parts[0]);
                        rr.CreateCell(2).SetCellValue(parts.Length > 1 ? parts[1] : "");
                        rr.CreateCell(3).SetCellValue(men);
                        rr.CreateCell(4).SetCellValue(women);
                    }
                } else if (sectionTitle.Contains("运动员姓名号码对照表")) {
                    var hr = sheet.CreateRow(r++);
                    hr.CreateCell(0).SetCellValue("号码"); hr.CreateCell(1).SetCellValue("姓名");
                    hr.CreateCell(2).SetCellValue("性别"); hr.CreateCell(3).SetCellValue("代表队"); hr.CreateCell(4).SetCellValue("组别");
                    var distinctSw = _swimmers
                        .Where(s => s.Notes == null || (!s.Notes.StartsWith("接力队员") && !s.Notes.StartsWith("接力队 棒次:")))
                        .GroupBy(s => s.BibNumber ?? "").Select(g => g.First())
                        .OrderBy(s => s.BibNumber).ToList();
                    foreach (var s in distinctSw) {
                        var rr = sheet.CreateRow(r++);
                        rr.CreateCell(0).SetCellValue(s.BibNumber ?? "");
                        rr.CreateCell(1).SetCellValue(s.Name ?? "");
                        rr.CreateCell(2).SetCellValue(s.Gender ?? "");
                        rr.CreateCell(3).SetCellValue(s.Country ?? "");
                        rr.CreateCell(4).SetCellValue(s.AgeCategory ?? "");
                    }
                } else if (sectionTitle.Contains("比赛记录")) {
                    var hr = sheet.CreateRow(r++);
                    hr.CreateCell(0).SetCellValue("（请到主程序「纪录管理」Tab 编辑后再导出）");
                } else if (sectionTitle.Contains("各项运动员竞赛分组名单")) {
                    var hr = sheet.CreateRow(r++);
                    hr.CreateCell(0).SetCellValue("（请先在「赛程与分组」完成分组，再导出）");
                } else {
                    // 其它章节留空表给操作员手动填
                    var rr = sheet.CreateRow(r++);
                    rr.CreateCell(0).SetCellValue("（请操作员手动填写此章节内容）");
                }
            }
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                wb.Write(fs);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Tab 5: 运动员兼项统计
        // ═══════════════════════════════════════════════════════════════
        private void EnterTab5() {
            if (MultiEventAgeCombo.Items.Count == 0) {
                MultiEventAgeCombo.Items.Add("全部组别");
                foreach (var g in _ageGroups) MultiEventAgeCombo.Items.Add(g.Name);
                MultiEventAgeCombo.SelectedIndex = 0;
            }
        }

        private void MultiEventCompute_Click(object sender, RoutedEventArgs e) {
            string ageFilter = MultiEventAgeCombo.SelectedItem != null ? MultiEventAgeCombo.SelectedItem.ToString() : "全部组别";
            // 按号码合并同一运动员的多项目；接力代表条目跳过
            var bibGroups = _swimmers
                .Where(s => s.Notes == null || !s.Notes.StartsWith("接力队 棒次:"))   // 排除代表条目
                .Where(s => ageFilter == "全部组别" || s.AgeCategory == ageFilter)
                .GroupBy(s => s.BibNumber ?? "")
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            var rows = new List<MultiEventStatRow>();
            foreach (var g in bibGroups) {
                var first = g.First();
                int indi = g.Count(s => !string.IsNullOrEmpty(s.EventName) && !s.EventName.Contains("接力"));
                int relay = g.Count(s => !string.IsNullOrEmpty(s.EventName) && s.EventName.Contains("接力"));
                var events = g.Select(s => s.EventName).Where(en => !string.IsNullOrEmpty(en)).Distinct().ToList();
                rows.Add(new MultiEventStatRow {
                    BibNumber = first.BibNumber, Name = first.Name, Gender = first.Gender,
                    Country = first.Country, IndividualCount = indi, RelayCount = relay,
                    EventList = string.Join("、", events.ToArray())
                });
            }
            rows = rows.OrderByDescending(r => r.IndividualCount + r.RelayCount).ThenBy(r => r.BibNumber).ToList();
            MultiEventGrid.ItemsSource = rows;
            int over3 = rows.Count(r => r.IndividualCount + r.RelayCount >= 3);
            MultiEventSummaryText.Text = string.Format("共 {0} 名运动员，兼报 ≥3 项 {1} 人；最多 {2} 项",
                rows.Count, over3, rows.Count > 0 ? rows.Max(r => r.IndividualCount + r.RelayCount) : 0);
        }

        private void MultiEventExport_Click(object sender, RoutedEventArgs e) {
            if (MultiEventGrid.ItemsSource == null) {
                MessageBox.Show("请先点 「🔍 统计兼项分布」", "提示"); return;
            }
            var rows = MultiEventGrid.ItemsSource.OfType<MultiEventStatRow>().ToList();
            if (rows.Count == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV 文件|*.csv", Title = "导出兼项统计",
                FileName = "运动员兼项统计_" + DateTime.Now.ToString("yyyyMMdd") + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("号码,姓名,性别,代表队,个人项目数,接力项目数,兼项详情");
            foreach (var r in rows) {
                sb.AppendLine(string.Join(",", new[] {
                    CsvEsc(r.BibNumber), CsvEsc(r.Name), CsvEsc(r.Gender), CsvEsc(r.Country),
                    r.IndividualCount.ToString(), r.RelayCount.ToString(), CsvEsc(r.EventList)
                }));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("已导出: " + dlg.FileName, "完成");
        }

        private static string CsvEsc(string s) {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    // ─── Tab 3 数据模型 ───────────────────────────────────────────────
    public class DistEntry : INotifyPropertyChanged
    {
        public string AgeGroup { get; set; }
        public string Gender { get; set; }
        public string EventName { get; set; }
        public int Participants { get; set; }
        public string ParticipantsLabel { get { return Participants + "人"; } }
        public int Heats { get; set; }
        public int Cutoff { get; set; }
        public string Stage { get; set; }
        private int _minPerHeat = 5;
        public int MinPerHeat { get { return _minPerHeat; } set { _minPerHeat = value; Notify("MinPerHeat"); } }

        // 已分配字段
        public string AssignedDate { get; set; }
        public string AssignedSession { get; set; }
        private string _assignedTime;
        public string AssignedTime { get { return _assignedTime; } set { _assignedTime = value; Notify("AssignedTime"); } }
        public int AssignedSortKey { get; set; }    // = startMin + offsetMin（用于排序）
        private int _seqInSession;
        public int SeqInSession { get { return _seqInSession; } set { _seqInSession = value; Notify("SeqInSession"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // 提供给主程序的已分配条目（持久化用）
    public class AssignedScheduleItem
    {
        public string Date { get; set; }
        public string Session { get; set; }
        public string Time { get; set; }
        public string AgeGroup { get; set; }
        public string Gender { get; set; }
        public string EventName { get; set; }
        public string Stage { get; set; }
        public int HeatCount { get; set; }
    }

    // Tab 5 行模型
    public class MultiEventStatRow
    {
        public string BibNumber { get; set; }
        public string Name { get; set; }
        public string Gender { get; set; }
        public string Country { get; set; }
        public int IndividualCount { get; set; }
        public int RelayCount { get; set; }
        public string EventList { get; set; }
    }

    // ─── Tab 1 数据行模型 ─────────────────────────────────────────────
    public class SchedulingPlanEntry : INotifyPropertyChanged
    {
        private string _ageGroup;
        private string _gender;
        private string _eventName;
        private int _participants;
        private int _prelimHeats, _prelimCutoff;
        private int _quarterHeats, _quarterCutoff;
        private int _semiHeats, _semiCutoff;
        private int _finalHeats, _finalCutoff;

        public string AgeGroup { get { return _ageGroup; } set { _ageGroup = value; Notify("AgeGroup"); } }
        public string Gender { get { return _gender; } set { _gender = value; Notify("Gender"); } }
        public string EventName { get { return _eventName; } set { _eventName = value; Notify("EventName"); } }
        public int Participants { get { return _participants; } set { _participants = value; Notify("Participants"); } }
        public int PrelimHeats { get { return _prelimHeats; } set { _prelimHeats = value; Notify("PrelimHeats"); } }
        public int PrelimCutoff { get { return _prelimCutoff; } set { _prelimCutoff = value; Notify("PrelimCutoff"); } }
        public int QuarterHeats { get { return _quarterHeats; } set { _quarterHeats = value; Notify("QuarterHeats"); } }
        public int QuarterCutoff { get { return _quarterCutoff; } set { _quarterCutoff = value; Notify("QuarterCutoff"); } }
        public int SemiHeats { get { return _semiHeats; } set { _semiHeats = value; Notify("SemiHeats"); } }
        public int SemiCutoff { get { return _semiCutoff; } set { _semiCutoff = value; Notify("SemiCutoff"); } }
        public int FinalHeats { get { return _finalHeats; } set { _finalHeats = value; Notify("FinalHeats"); } }
        public int FinalCutoff { get { return _finalCutoff; } set { _finalCutoff = value; Notify("FinalCutoff"); } }

        // 唯一键（用于 baseline 字典）
        public string Key { get { return (AgeGroup ?? "") + "|" + (Gender ?? "") + "|" + (EventName ?? ""); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
