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
        private readonly EventDurationConfig _durationConfig;
        // 2026-05-24 参赛单位元信息（领队/教练/联系电话）— 秩序册章节中显示
        private readonly List<Unit> _units;
        // 2026-05-24 P0-D 工作人员（5 组）— 秩序册章节 3-6 用
        private readonly List<StaffMember> _staff;

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
            ScoringConfig scoringConfig,
            EventDurationConfig durationConfig,
            IEnumerable<Unit> units,
            IEnumerable<StaffMember> staff) {

            InitializeComponent();
            _swimmers = swimmers;
            _events = events ?? new List<string>();
            _ageGroups = ageGroups ?? new List<AgeGroup>();
            _genders = genders ?? new List<string> { "男", "女" };
            _poolConfig = poolConfig ?? new PoolConfig();
            _scoringConfig = scoringConfig ?? new ScoringConfig();
            _durationConfig = durationConfig ?? new EventDurationConfig();
            _units = units != null ? units.ToList() : new List<Unit>();
            _staff = staff != null ? staff.ToList() : new List<StaffMember>();

            StagePlanGrid.ItemsSource = _planRows;
            NavList.SelectedIndex = 0;   // 默认进入 Tab 1

            // 2026-05-25 草稿恢复或自动统计一次
            // (InitialDraft 在 Loaded 事件里加载, 因为 caller 设置 InitialDraft 在构造之后)
            Loaded += delegate { RestoreDraftIfAny(); };
            Closing += delegate { SaveDraftIfNotApplied(); };
        }

        private void RestoreDraftIfAny() {
            if (InitialDraft != null && InitialDraft.HasData) {
                _planRows.Clear();
                foreach (var r in InitialDraft.PlanRows) _planRows.Add(CloneRow(r));
                _distPending.Clear();
                foreach (var d in InitialDraft.DistPending) _distPending.Add(CloneDistEntry(d));
                _distAssigned.Clear();
                foreach (var d in InitialDraft.DistAssigned) _distAssigned.Add(CloneDistEntry(d));
                _availableDates = InitialDraft.AvailableDates != null ? new List<string>(InitialDraft.AvailableDates) : new List<string>();
                if (InitialDraft.SessionStartMin != null && InitialDraft.SessionStartMin.Length == 3)
                    _sessionStartMin = (int[])InitialDraft.SessionStartMin.Clone();
                UpdateTab1CountText();
                StatusText.Text = "📂 已恢复上次未保存的草稿（可继续编辑或重新自动统计）";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7C3AED"));
            } else {
                AutoComputeStages_Click(null, null);
            }
        }

        private void SaveDraftIfNotApplied() {
            if (_draftWillClear) {
                // 已确认存盘 → 清空草稿
                if (SaveDraftCallback != null) SaveDraftCallback(new WizardDraft());
                return;
            }
            if (SaveDraftCallback == null) return;
            var d = new WizardDraft {
                PlanRows = _planRows.Select(r => CloneRow(r)).ToList(),
                DistPending = _distPending.Select(x => CloneDistEntry(x)).ToList(),
                DistAssigned = _distAssigned.Select(x => CloneDistEntry(x)).ToList(),
                AvailableDates = new List<string>(_availableDates),
                SessionStartMin = (int[])_sessionStartMin.Clone()
            };
            try { SaveDraftCallback(d); } catch { }
        }

        private static DistEntry CloneDistEntry(DistEntry s) {
            return new DistEntry {
                AgeGroup = s.AgeGroup, Gender = s.Gender, EventName = s.EventName,
                Participants = s.Participants, Heats = s.Heats, Cutoff = s.Cutoff,
                Stage = s.Stage, HeatRange = s.HeatRange, MinPerHeat = s.MinPerHeat,
                AssignedDate = s.AssignedDate, AssignedSession = s.AssignedSession,
                AssignedTime = s.AssignedTime, AssignedSortKey = s.AssignedSortKey,
                SeqInSession = s.SeqInSession
            };
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
                "秩序册制作 < 可选文档 >",
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

            // 三维遍历 组别 × 性别 × 项目
            // 2026-06-02 组别集合 = _ageGroups 配置 ∪ _swimmers 实际报名的 AgeCategory
            //   修复: test_bot 选了"青少年"等 _ageGroups 没列的组别时, 整张表会因 count=0 跳过.
            //   把已报名运动员的 AgeCategory 也并入, 让 100/200/800/接力 不再因组别不匹配漏出.
            var ageGroupNames = new List<string>();
            if (_ageGroups.Count > 0) ageGroupNames.AddRange(_ageGroups.Select(g => g.Name));
            var swimmerAges = _swimmers
                .Where(s => (s.Notes == null || !s.Notes.StartsWith("接力队员")))
                .Select(s => s.AgeCategory ?? "")
                .Distinct();
            foreach (var ag in swimmerAges) {
                if (!ageGroupNames.Contains(ag)) ageGroupNames.Add(ag);
            }
            if (ageGroupNames.Count == 0) ageGroupNames.Add("");

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

                        // 2026-05-25 只列有报名的项目（0 人不进表）
                        if (count <= 0) continue;

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

        // 2026-05-24 P0-1 一键全自动：参数对话框 → 自动统计赛次 → 派生待分配 → 多天分配时段 → 跳转 Tab 3
        private void OneClickGenerate_Click(object sender, RoutedEventArgs e) {
            // 2026-05-26 默认起始日期 = 赛事概览的开始日期 (没填回退到今天)
            // 天数: 优先用赛事概览 (开始/结束日期) 推断, 其次沿用上次 _availableDates, 兜底 3
            DateTime defaultDate;
            if (!DateTime.TryParse(CompetitionStartDate ?? "", out defaultDate)) defaultDate = DateTime.Today;
            int defaultDays;
            DateTime endDate;
            if (DateTime.TryParse(CompetitionStartDate ?? "", out defaultDate)
                && DateTime.TryParse(CompetitionEndDate ?? "", out endDate) && endDate >= defaultDate) {
                defaultDays = (int)(endDate - defaultDate).TotalDays + 1;
                if (defaultDays > 14) defaultDays = 14;
            } else {
                defaultDays = _availableDates != null && _availableDates.Count > 0 ? _availableDates.Count : 3;
            }

            var dlg = new OneClickParamsWindow(defaultDate, defaultDays) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            // 1) 自动统计赛次
            AutoComputeStages_Click(null, null);

            // 把用户配的时段起始时间存进来，供 RecomputeAssignedTimes 后续微调使用
            _sessionStartMin[0] = dlg.ParamMorningStartMin;
            _sessionStartMin[1] = dlg.ParamAfternoonStartMin;
            _sessionStartMin[2] = dlg.ParamEveningStartMin;

            // 2) 准备 _availableDates / 派生待分配
            _availableDates = new List<string>();
            for (int i = 0; i < dlg.ParamDays; i++)
                _availableDates.Add(dlg.ParamStartDate.AddDays(i).ToString("yyyy-MM-dd"));

            _distAssigned.Clear();
            _distPending.Clear();
            BuildDistPendingFromPlan();

            // 3) 多天 × 时段分配
            DistributeAcrossDays(dlg);

            // 4) 切到 Tab 3 显示结果，刷新 day combo
            NavList.SelectedIndex = 2;   // Tab 3
            DistDayCombo.ItemsSource = null;
            DistDayCombo.ItemsSource = _availableDates;
            DistDayCombo.SelectedIndex = 0;
            DistPendingGrid.ItemsSource = _distPending;
            DistAssignedGrid.ItemsSource = _distVisible;
            RefreshDistVisible();
            UpdateDistCounters();

            int leftPending = _distPending.Count;
            string summary;
            if (leftPending == 0) {
                summary = string.Format("✅ 已编排 {0} 项，分配到 {1} 天。\n\n是否进入「秩序册可选文档」生成 .xlsx？",
                    _distAssigned.Count, dlg.ParamDays);
            } else {
                summary = string.Format("⚠ 已编排 {0} 项，但仍有 {1} 项未排上（容量不足）。\n建议：增加比赛天数、放宽每场最长，或手动微调。\n\n是否仍进入「生成 xlsx」？",
                    _distAssigned.Count, leftPending);
            }
            if (MessageBox.Show(summary, "一键全自动完成", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                NavList.SelectedIndex = 3;   // Tab 4
            }
        }

        private void DistributeAcrossDays(OneClickParamsWindow p) {
            // 2026-05-27 排序: 同一 (组别+性别+项目) 内部按 stage (预赛/次复赛/半决赛/决赛) 顺序入队;
            //   不同项目之间按 FINA 项目顺序. 配合下面 stageDayFloor 严格保证同项目后置 stage 不会跑到前置 stage 之前.
            var ordered = _distPending.OrderBy(d => EventOrder(d.EventName))
                                       .ThenBy(d => d.AgeGroup ?? "")
                                       .ThenBy(d => d.Gender ?? "")
                                       .ThenBy(d => StageOrder(d.Stage))
                                       .ToList();

            // 时段容量表：(day, session) → 已用分钟数 / 起始分钟数
            int dayCount = p.ParamDays;
            // sessionSlots[d][0=AM,1=PM,2=EVE] = 已用分钟
            var used = new int[dayCount, 3];

            // 2026-05-27 BUG 修复: 同一 (组别+性别+项目) 的下一 stage 必须排在已分配的最高日 (含) 之后.
            //   旧版 day 循环从 0 开始, 导致预赛因前几天满了被推到 day 2 时, 决赛仍能塞回 day 0 evening → 时序错乱.
            //   stageDayFloor[key] = 该项目目前已经占用的最大日 (含, 允许同天 — 预赛上午+决赛同天晚上是合法的).
            var stageDayFloor = new Dictionary<string, int>();

            foreach (var d in ordered) {
                // 选时段
                int sessionIdx;
                if (p.ParamStrategyFinalOnly) {
                    sessionIdx = 2;   // 全部按晚上决赛
                } else if (d.HeatRange == "慢组") {
                    // 2026-05-24 C5 长距离慢组 → 白天（上午优先；上午满则下午）
                    sessionIdx = 0;
                } else if (d.HeatRange == "快组") {
                    sessionIdx = 2;   // 长距离快组 → 晚间
                } else if (d.Stage == "决赛" || d.Stage == "B组决赛") {
                    sessionIdx = 2;
                } else if (d.Stage == "半决赛") {
                    sessionIdx = 1;
                } else {
                    sessionIdx = 0;
                }

                int duration = Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + _durationConfig.InterEventGapMinutes;

                // 2026-05-27 day 循环最早起点 = 该 (组别+性别+项目) 已占用的最大日 (确保后置 stage 不会跑到前置 stage 之前)
                string evtKey = (d.AgeGroup ?? "") + "|" + (d.Gender ?? "") + "|" + (d.EventName ?? "");
                int minDay = stageDayFloor.ContainsKey(evtKey) ? stageDayFloor[evtKey] : 0;

                // 在该时段从前往后找有空的天 — 严格按预赛在决赛之前的原则放
                int chosenDay = -1;
                int chosenSession = sessionIdx;
                for (int day = minDay; day < dayCount; day++) {
                    if (p.ParamFirstDayMorningSkip && day == 0 && sessionIdx == 0) continue;
                    if (p.ParamLastDayAfternoonSkip && day == dayCount - 1 && sessionIdx == 1) continue;
                    if (used[day, sessionIdx] + duration <= p.ParamMaxSessionMinutes) {
                        chosenDay = day; chosenSession = sessionIdx;
                        break;
                    }
                }
                // 该时段全满 → 尝试相邻时段（晚上→下午→上午 / 上午→下午→晚上）, 同样受 minDay 约束
                if (chosenDay < 0) {
                    int[] order = sessionIdx == 2 ? new[] { 1, 0 } : sessionIdx == 1 ? new[] { 2, 0 } : new[] { 1, 2 };
                    foreach (var sIdx in order) {
                        for (int day = minDay; day < dayCount; day++) {
                            if (p.ParamFirstDayMorningSkip && day == 0 && sIdx == 0) continue;
                            if (p.ParamLastDayAfternoonSkip && day == dayCount - 1 && sIdx == 1) continue;
                            if (used[day, sIdx] + duration <= p.ParamMaxSessionMinutes) {
                                chosenDay = day; chosenSession = sIdx;
                                break;
                            }
                        }
                        if (chosenDay >= 0) break;
                    }
                }
                if (chosenDay < 0) continue;   // 真的排不下，留在 pending

                int sessStart = chosenSession == 0 ? p.ParamMorningStartMin :
                                chosenSession == 1 ? p.ParamAfternoonStartMin : p.ParamEveningStartMin;
                int t = sessStart + used[chosenDay, chosenSession];
                d.AssignedDate = _availableDates[chosenDay];
                d.AssignedSession = chosenSession == 0 ? "上午" : chosenSession == 1 ? "下午" : "晚上";
                d.AssignedTime = string.Format("{0:D2}:{1:D2}", t / 60, t % 60);
                d.AssignedSortKey = t;
                used[chosenDay, chosenSession] += duration;
                _distAssigned.Add(d);
                // 更新该 (组别+性别+项目) 的"下次 stage 最早允许日" = 当前已分配日
                stageDayFloor[evtKey] = Math.Max(minDay, chosenDay);
            }
            // 把已分配的从 pending 移除
            foreach (var d in _distAssigned.ToList()) _distPending.Remove(d);
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

        // 2026-05-25 Tab1 任何 赛次/录取 单元改完 → 标 _planDirty=true
        // EnterTab3 会据此刷新待分配列表，避免 Tab1 改动丢失
        private bool _planDirty;

        private void StagePlanGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) {
            // CellEditEnding 触发时绑定还没刷回 source；用 Dispatcher 延迟更新计数文字
            Dispatcher.BeginInvoke(new Action(() => {
                UpdateTab1CountText();
                _planDirty = true;
                StatusText.Text = "✓ 已自动保存（切到 Tab3 编排时会按最新值刷新待分配）";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E"));
            }));
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
            // 2026-05-25 _planDirty=true (用户改过 Tab1) → 清掉旧待分配重新构建，让 Tab1 改动生效
            if (_planDirty) {
                _distPending.Clear();
                _distAssigned.Clear();
                BuildDistPendingFromPlan();
                _planDirty = false;
            } else if (_distPending.Count == 0 && _distAssigned.Count == 0) {
                BuildDistPendingFromPlan();
            }
            // 2026-05-26 修复: 进 Tab3 时按「赛事概览」开始/结束日期生成日期列表，
            //              一键全自动若已填好 _availableDates 则尊重已有值
            EnsureAvailableDatesFromCompetition();
            DistDayCombo.ItemsSource = null;
            DistDayCombo.ItemsSource = _availableDates;
            if (DistDayCombo.SelectedIndex < 0 && _availableDates.Count > 0) DistDayCombo.SelectedIndex = 0;

            DistPendingGrid.ItemsSource = _distPending;
            DistAssignedGrid.ItemsSource = _distVisible;
            RefreshDistVisible();
            UpdateDistCounters();
            SyncStartTimeBoxToCurrentSession();
        }

        // 2026-05-26 BUG 修复: 自动分配会把所有项目堆到一天。
        //   原因: _availableDates 在非「一键全自动」路径下只塞了 DateTime.Today 一项，
        //         结果 DistAuto_Click 只能选到那一天。
        //   修复: 进 Tab3 前用「赛事概览」开始日期/结束日期生成完整日期列表。
        private void EnsureAvailableDatesFromCompetition() {
            // 已有多于 1 天的列表 (一键全自动建好) → 不动
            if (_availableDates != null && _availableDates.Count > 1) return;
            DateTime start, end;
            bool hasStart = DateTime.TryParse(CompetitionStartDate ?? "", out start);
            bool hasEnd = DateTime.TryParse(CompetitionEndDate ?? "", out end);
            // 都没填 → 退回默认今天
            if (!hasStart && !hasEnd) {
                _availableDates = new List<string> { DateTime.Today.ToString("yyyy-MM-dd") };
                return;
            }
            if (!hasStart) start = end;
            if (!hasEnd) end = start;
            if (end < start) { var tmp = start; start = end; end = tmp; }
            // 上限 14 天兜底，防止用户把日期填错
            int days = (int)(end - start).TotalDays + 1;
            if (days > 14) days = 14;
            var list = new List<string>();
            for (int i = 0; i < days; i++) list.Add(start.AddDays(i).ToString("yyyy-MM-dd"));
            _availableDates = list;
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
                if (r.FinalHeats > 0) {
                    // 2026-05-24 C5 长距离快慢组：800/1500 米决赛 FinalHeats≥2 → 拆成 慢组(白天) + 快组(晚间)
                    if (HeatScheduler.IsLongDistanceFastSlowEvent(r.EventName) && r.FinalHeats >= 2) {
                        _distPending.Add(new DistEntry { AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                            Participants = r.Participants, Heats = r.FinalHeats - 1, Cutoff = r.FinalCutoff,
                            Stage = "决赛", HeatRange = "慢组", MinPerHeat = EstimateMinutesPerHeat(r.EventName) });
                        _distPending.Add(new DistEntry { AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                            Participants = r.Participants, Heats = 1, Cutoff = r.FinalCutoff,
                            Stage = "决赛", HeatRange = "快组", MinPerHeat = EstimateMinutesPerHeat(r.EventName) });
                    } else {
                        _distPending.Add(new DistEntry { AgeGroup = r.AgeGroup, Gender = r.Gender, EventName = r.EventName,
                            Participants = r.Participants, Heats = r.FinalHeats, Cutoff = r.FinalCutoff,
                            Stage = "决赛", MinPerHeat = EstimateMinutesPerHeat(r.EventName) });
                    }
                }
            }
            ApplyDistSort();
        }

        // 每组用时（分钟）— 从 DurationConfig 取（用户可在 "项目用时设置" 编辑、持久化）
        private int EstimateMinutesPerHeat(string ev) {
            return _durationConfig.GetMinutesPerHeat(ev);
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
            SyncStartTimeBoxToCurrentSession();
        }
        private void DistSession_Changed(object sender, SelectionChangedEventArgs e) {
            RefreshDistVisible();
            SyncStartTimeBoxToCurrentSession();
        }
        // 2026-05-25 切换时段/日期时, 顶部 开始时间 TextBox 显示该时段当前生效的起始时间
        private void SyncStartTimeBoxToCurrentSession() {
            if (DistStartTimeBox == null || DistSessionCombo == null) return;
            string session = DistSessionCombo.SelectedItem != null
                ? ((ComboBoxItem)DistSessionCombo.SelectedItem).Content.ToString() : "上午";
            int idx = session == "下午" ? 1 : (session == "晚上" ? 2 : 0);
            int m = _sessionStartMin[idx];
            DistStartTimeBox.Text = string.Format("{0:D2}:{1:D2}", m / 60, m % 60);
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
                curMin += Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + _durationConfig.InterEventGapMinutes; // +2min 项目间隔
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
                int duration = Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + _durationConfig.InterEventGapMinutes;
                int endOffset = (d.AssignedSortKey - sessionStart) + duration;
                if (endOffset > maxOffset) maxOffset = endOffset;
            }
            return maxOffset;
        }

        private int GetSessionStartMin(string session) {
            // 与主程序 AutoBuildSchedule 默认时段保持一致
            if (session == "上午") return _sessionStartMin[0];
            if (session == "下午") return _sessionStartMin[1];
            if (session == "晚上") return _sessionStartMin[2];
            return _sessionStartMin[0];
        }

        // 默认 09:00 / 14:30 / 19:30；一键全自动会覆盖
        private int[] _sessionStartMin = new[] { 9 * 60, 14 * 60 + 30, 19 * 60 + 30 };

        // 2026-05-26 修复: 原实现把所有项目都堆到 DistDayCombo 选中的那一天，
        //              且 _availableDates 在非「一键全自动」路径下只塞了 DateTime.Today
        //              → 全部项目都挤到今天。
        //              改为按 _availableDates 多天平铺，每个时段满了 (>= 240 分钟) 自动滚到下一天。
        //              「强制时段」模式仍然只用单一时段，但会跨多天。
        private void DistAuto_Click(object sender, RoutedEventArgs e) {
            if (_distPending.Count == 0) {
                MessageBox.Show("已无待分配项目", "提示"); return;
            }
            EnsureAvailableDatesFromCompetition();
            if (_availableDates == null || _availableDates.Count == 0) {
                MessageBox.Show("没有可用日期。请先到「赛事管理与报名 → 赛事概览」填好开始/结束日期。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string mode = DistModeAuto.IsChecked == true ? "自动" :
                          DistModeStrictTime.IsChecked == true ? "强制时段" :
                          DistModeFinalOnly.IsChecked == true ? "强制决赛" : "自动";

            // 排序待分配项目
            var ordered = _distPending.OrderBy(d => EventOrder(d.EventName))
                                       .ThenBy(d => StageOrder(d.Stage))
                                       .ThenBy(d => d.AgeGroup ?? "")
                                       .ThenBy(d => d.Gender ?? "")
                                       .ToList();

            int dayCount = _availableDates.Count;
            const int MAX_SESSION_MIN = 240;   // 每时段上限 240 分钟，与 OneClickParamsWindow 默认一致

            // 初始化每 (day, session) 已用分钟数 — 把已分配的也算进去
            var used = new int[dayCount, 3];
            for (int i = 0; i < dayCount; i++) {
                used[i, 0] = ComputeSessionUsedMinutes(_availableDates[i], "上午");
                used[i, 1] = ComputeSessionUsedMinutes(_availableDates[i], "下午");
                used[i, 2] = ComputeSessionUsedMinutes(_availableDates[i], "晚上");
            }

            int unplaced = 0;
            foreach (var d in ordered) {
                int sessionIdx;
                if (mode == "强制决赛") {
                    sessionIdx = 2;   // 晚上
                } else if (mode == "强制时段") {
                    string sel = DistSessionCombo.SelectedItem != null
                        ? ((ComboBoxItem)DistSessionCombo.SelectedItem).Content.ToString() : "上午";
                    sessionIdx = sel == "晚上" ? 2 : sel == "下午" ? 1 : 0;
                } else {
                    if (d.Stage == "决赛" || d.Stage == "B组决赛") sessionIdx = 2;
                    else if (d.Stage == "半决赛") sessionIdx = 1;
                    else sessionIdx = 0;
                }

                int duration = Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + _durationConfig.InterEventGapMinutes;

                // 在目标时段从第 1 天找空位
                int chosenDay = -1, chosenSession = sessionIdx;
                for (int day = 0; day < dayCount; day++) {
                    if (used[day, sessionIdx] + duration <= MAX_SESSION_MIN) {
                        chosenDay = day; break;
                    }
                }
                // 目标时段全满 → 自动/强制决赛模式尝试相邻时段；强制时段保持原时段
                if (chosenDay < 0 && mode != "强制时段") {
                    int[] altOrder = sessionIdx == 2 ? new[] { 1, 0 } : sessionIdx == 1 ? new[] { 2, 0 } : new[] { 1, 2 };
                    foreach (var sIdx in altOrder) {
                        for (int day = 0; day < dayCount; day++) {
                            if (used[day, sIdx] + duration <= MAX_SESSION_MIN) {
                                chosenDay = day; chosenSession = sIdx; break;
                            }
                        }
                        if (chosenDay >= 0) break;
                    }
                }
                if (chosenDay < 0) { unplaced++; continue; }

                string sessName = chosenSession == 0 ? "上午" : chosenSession == 1 ? "下午" : "晚上";
                int sessStart = GetSessionStartMin(sessName);
                int t = sessStart + used[chosenDay, chosenSession];

                d.AssignedDate = _availableDates[chosenDay];
                d.AssignedSession = sessName;
                d.AssignedTime = string.Format("{0:D2}:{1:D2}", t / 60, t % 60);
                d.AssignedSortKey = t;

                used[chosenDay, chosenSession] += duration;
                _distAssigned.Add(d);
            }
            // 已分配的从 pending 移除
            foreach (var d in _distAssigned.ToList()) _distPending.Remove(d);

            RefreshDistVisible();
            UpdateDistCounters();

            int dayUsedCount = _distAssigned.Select(x => x.AssignedDate).Distinct().Count();
            string msg = string.Format("已自动分配 {0} 项到 {1} 天 ({2} 模式)。",
                _distAssigned.Count, dayUsedCount, mode);
            if (unplaced > 0) msg += string.Format("\n⚠ 仍有 {0} 项未排上（容量不足，建议增加比赛天数或拖回左侧重排）。", unplaced);
            else msg += "\n您可以手动微调每项的「分/组」时长或拖回左侧重排。";
            MessageBox.Show(msg, "自动分配完成");
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
            // 2026-05-25 当用户编辑了 "比赛时间" 列 → 以该行新时间为锚, 重排同时段后续项目时间
            // 编辑 "分/组" → 重新计算该时段后续项目的时间
            var col = e.Column;
            var row = e.Row != null ? e.Row.Item as DistEntry : null;
            string header = col != null ? (col.Header as string ?? "") : "";
            if (row != null && header.Contains("比赛时间")) {
                Dispatcher.BeginInvoke(new Action(() => RecomputeAssignedTimesFromAnchor(row)));
            } else {
                Dispatcher.BeginInvoke(new Action(RecomputeAssignedTimes));
            }
        }

        // 2026-05-25 用户改了某行比赛时间 → 用它当 anchor, 同时段后续行时间按 (分/组 × 组数 + 间隔) 累加
        private void RecomputeAssignedTimesFromAnchor(DistEntry anchor) {
            if (anchor == null || string.IsNullOrEmpty(anchor.AssignedSession)) return;
            int anchorMin = ParseHhMm(anchor.AssignedTime, -1);
            if (anchorMin < 0) { RecomputeAssignedTimes(); return; }
            anchor.AssignedSortKey = anchorMin;

            // 同 day+session 的所有条目, 按 AssignedSortKey 排序, 把 anchor 之后的全部重排
            var sameSession = _distAssigned.Where(x => x.AssignedDate == anchor.AssignedDate
                                                    && x.AssignedSession == anchor.AssignedSession)
                                            .OrderBy(x => x.AssignedSortKey).ToList();
            int idx = sameSession.IndexOf(anchor);
            if (idx < 0) { RecomputeAssignedTimes(); return; }
            int t = anchorMin + Math.Max(1, anchor.Heats) * Math.Max(1, anchor.MinPerHeat) + _durationConfig.InterEventGapMinutes;
            for (int i = idx + 1; i < sameSession.Count; i++) {
                var d = sameSession[i];
                d.AssignedTime = string.Format("{0:D2}:{1:D2}", t / 60, t % 60);
                d.AssignedSortKey = t;
                t += Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + _durationConfig.InterEventGapMinutes;
            }
            RefreshDistVisible();
        }
        private static int ParseHhMm(string s, int fallback) {
            if (string.IsNullOrEmpty(s)) return fallback;
            var parts = s.Trim().Split(':');
            int h, m;
            if (parts.Length == 2 && int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m)
                && h >= 0 && h <= 23 && m >= 0 && m <= 59) return h * 60 + m;
            return fallback;
        }

        // 2026-05-25 顶部 "开始时间" TextBox: 回车 / 失焦后用新时间替换当前时段的起始,
        //   该时段全部条目重算时间
        private void DistStartTime_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Return) {
                ApplyStartTimeOverride();
                e.Handled = true;
            }
        }
        private void DistStartTime_LostFocus(object sender, RoutedEventArgs e) {
            ApplyStartTimeOverride();
        }
        private void ApplyStartTimeOverride() {
            int newStart = ParseHhMm(DistStartTimeBox.Text, -1);
            if (newStart < 0) return;
            // 把当前选中时段的起始时间替换为 newStart
            string session = DistSessionCombo.SelectedItem != null
                ? ((ComboBoxItem)DistSessionCombo.SelectedItem).Content.ToString() : "上午";
            int idx = session == "下午" ? 1 : (session == "晚上" ? 2 : 0);
            _sessionStartMin[idx] = newStart;
            RecomputeAssignedTimes();
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
                    used += Math.Max(1, d.Heats) * Math.Max(1, d.MinPerHeat) + _durationConfig.InterEventGapMinutes;
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
                Stage = d.Stage, HeatCount = d.Heats, HeatRange = d.HeatRange
            }).ToList();
            // 2026-05-25 已有赛程时弹覆盖确认
            string warning = string.Format("即将存盘 {0} 项编排到主程序赛程。", _distAssigned.Count);
            if (HasExistingSchedule()) {
                warning += "\n\n⚠ 主程序已有赛程数据，本次操作将 [覆盖] 旧赛程！\n\n确认存盘？";
            } else {
                warning += "\n\n确认存盘？";
            }
            if (MessageBox.Show(warning, "确认赛程存盘", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            ApplyToMainSchedule = true;
            // 存盘成功 → 清除草稿 (避免下次打开看到已应用的旧草稿)
            _draftWillClear = true;
            DialogResult = true;
            Close();
        }

        // 调用方设置：判断主程序当前是否已有赛程
        public Func<bool> HasExistingScheduleProbe { get; set; }
        private bool HasExistingSchedule() {
            try { return HasExistingScheduleProbe != null && HasExistingScheduleProbe(); } catch { return false; }
        }

        // 2026-05-25 草稿: 关窗时序列化整个向导状态供下次打开恢复
        private bool _draftWillClear;
        public Action<WizardDraft> SaveDraftCallback { get; set; }
        public WizardDraft InitialDraft { get; set; }

        // 2026-05-25 编辑/打印 用 — 由 MainWindow 注入比赛元信息, 用于封面/裁判名单等 RTF 模板填充
        public string CompetitionNameInfo { get; set; }
        public string CompetitionLocation { get; set; }
        public string CompetitionStartDate { get; set; }
        public string CompetitionEndDate { get; set; }
        public string CompetitionOrganizer { get; set; }
        // 2026-05-25 主程序当前赛程 (用于生成秩序册第 13/15 章): 若向导内 _distAssigned 为空时回退使用
        public IEnumerable<ScheduleItem> ExistingSchedule { get; set; }

        // 调用方读取这些字段把数据写回 _schedule
        public bool ApplyToMainSchedule { get; private set; }
        public List<AssignedScheduleItem> AssignedItems { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        // Tab 4: 秩序册可选文档（15 项 CheckList）
        // ═══════════════════════════════════════════════════════════════
        // 2026-05-25 改为 RadioButton 单选 — 「编辑/打印」一次只针对 1 个章节
        private List<RadioButton> _docCheckBoxes = new List<RadioButton>();
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
                for (int i = 0; i < DocSections.Length; i++) {
                    var rb = new RadioButton {
                        Content = DocSections[i],
                        IsChecked = (i == 0),   // 默认选中第 1 条
                        GroupName = "DocSectionGroup",
                        Margin = new Thickness(0, 4, 0, 4), FontSize = 13
                    };
                    _docCheckBoxes.Add(rb);
                    DocSectionsPanel.Children.Add(rb);
                }
            }
        }

        // 兼容旧 XAML 引用 — 全选/全不选 在 RadioButton 单选模式下意义不大, 但保留以免 XAML 出错
        private void DocSelectAll_Click(object sender, RoutedEventArgs e) {
            if (_docCheckBoxes.Count > 0) _docCheckBoxes[0].IsChecked = true;
        }
        private void DocSelectNone_Click(object sender, RoutedEventArgs e) {
            foreach (var rb in _docCheckBoxes) rb.IsChecked = false;
        }

        // 2026-05-25 章节文件持久化文件夹: MyDocuments\swim_orderbook\<比赛名>\NN_章节.{rtf|xlsx}
        // 编辑/打印 总是重生覆盖；全本秩序册按钮直接打开此文件夹
        private string GetOrderBookFolder() {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string compName = string.IsNullOrEmpty(CompetitionNameInfo) ? "未命名比赛" : CompetitionNameInfo;
            foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) compName = compName.Replace(ch, '_');
            string dir = System.IO.Path.Combine(baseDir, "swim_orderbook", compName);
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        // 章节文件路径: 01_封面.rtf / 02_目录.xlsx / 06_仲裁裁判员名单.rtf / ...
        private string GetSectionFilePath(string sectionTitle) {
            // 从 "13. 竞赛日程表" 取 "13" 编号 + 取章节名
            int dot = sectionTitle.IndexOf('.');
            string idx = dot > 0 ? sectionTitle.Substring(0, dot).Trim() : "00";
            string namePart = dot > 0 ? sectionTitle.Substring(dot + 1).Trim() : sectionTitle;
            int idxNum; int.TryParse(idx, out idxNum);
            string safeName = namePart;
            foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) safeName = safeName.Replace(ch, '_');
            if (safeName.Length > 30) safeName = safeName.Substring(0, 30);
            // 2026-05-25 仅章节 13 (竞赛日程表) 和 15 (各项分组名单) 用 .xlsx (表格密集), 其他全部 .rtf
            string ext = (sectionTitle.StartsWith("13.") || sectionTitle.StartsWith("15.")) ? ".xlsx" : ".rtf";
            return System.IO.Path.Combine(GetOrderBookFolder(), idxNum.ToString("D2") + "_" + safeName + ext);
        }

        // 生成单个章节到指定路径（按文件扩展名分发到 RTF / Xlsx 生成器）
        private void WriteSectionFile(string sectionTitle, string path) {
            string ext = System.IO.Path.GetExtension(path).ToLower();
            if (ext == ".rtf") {
                string rtf = BuildSectionRtf(sectionTitle);
                System.IO.File.WriteAllText(path, rtf, System.Text.Encoding.GetEncoding("GB2312"));
            } else {
                GenerateOrderBookXlsx(path, new List<string> { sectionTitle });
            }
        }

        // 2026-05-25 通用章节 RTF 分派器
        private string BuildSectionRtf(string sectionTitle) {
            if (sectionTitle.StartsWith("1.")) return BuildCoverRtf();
            if (sectionTitle.StartsWith("2.")) return BuildTocRtf();
            if (sectionTitle.StartsWith("3.")) return BuildStaffGroupRtf("3. 主席团名单", StaffGroups.Presidium);
            if (sectionTitle.StartsWith("4.")) return BuildStaffGroupRtf("4. 组织委员会名单", StaffGroups.OrgCommittee);
            if (sectionTitle.StartsWith("5.")) return BuildStaffGroupRtf("5. 大会工作机构名单", StaffGroups.WorkOrg);
            if (sectionTitle.StartsWith("6.")) return BuildRefereeListRtf();
            if (sectionTitle.StartsWith("7.")) return BuildSimpleTextRtf("7. 竞赛规程",
                "（请操作员手动编辑此章节内容，例如：）\n\n一、主办单位\n二、承办单位\n三、比赛时间和地点\n四、参赛单位\n五、参赛办法\n六、竞赛项目\n七、参赛规定\n八、录取名次及奖励办法\n九、报名及报到\n十、未尽事宜，另行通知");
            if (sectionTitle.StartsWith("8.")) return BuildSimpleTextRtf("8. 体育道德风尚奖评选方法",
                "（请操作员手动编辑此章节内容）");
            if (sectionTitle.StartsWith("9.")) return BuildSimpleTextRtf("9. 开幕式、闭幕式程序",
                "开幕式程序：\n\n1. 升国旗、奏国歌\n2. 运动员入场\n3. 主办单位领导致辞\n4. 运动员代表宣誓\n5. 裁判员代表宣誓\n6. 宣布比赛开始\n\n闭幕式程序：\n\n1. 颁奖典礼\n2. 总裁判长报告比赛成绩\n3. 主办单位领导讲话\n4. 宣布比赛闭幕");
            if (sectionTitle.StartsWith("10.")) return BuildSimpleTextRtf("10. 比赛场地平面图",
                "（请操作员在 WPS / Word 中插入比赛场地平面图图片）");
            if (sectionTitle.StartsWith("11.")) return BuildParticipantsStatsRtf();
            if (sectionTitle.StartsWith("12.")) return BuildBibNameTableRtf();
            if (sectionTitle.StartsWith("14.")) return BuildSimpleTextRtf("14. 比赛记录（最高纪录）",
                "（请到主程序「记录管理」Tab 编辑记录后再生成此章节，或在此手动列出）");
            // 默认占位
            return BuildSimpleTextRtf(sectionTitle, "（请操作员手动填写此章节内容）");
        }

        // 通用 RTF 头 + 字体表（黑体标题、宋体正文）
        private static string RtfHeader() {
            var sb = new StringBuilder();
            sb.Append("{\\rtf1\\ansi\\ansicpg936\\fcharset134\\deff0\n");
            sb.Append("{\\fonttbl{\\f0\\fnil\\fcharset134 ");
            sb.Append(RtfEscape("黑体")); sb.Append(";}{\\f1\\fnil\\fcharset134 ");
            sb.Append(RtfEscape("宋体")); sb.Append(";}}\n");
            sb.Append("\\paperw11906\\paperh16838\\margl1440\\margr1440\\margt1440\\margb1440\n");
            return sb.ToString();
        }

        // 大标题（黑体居中粗体）
        private static string RtfTitle(string title, int fs) {
            return "\\pard\\qc\\sa400\\f0\\fs" + fs + "\\b " + RtfEscape(title) + "\\b0\\par\n";
        }
        // 正文段落
        private static string RtfPara(string text, int fs) {
            return "\\pard\\sa120\\f1\\fs" + fs + " " + RtfEscape(text) + "\\par\n";
        }

        // 2. 目录
        private string BuildTocRtf() {
            var sb = new StringBuilder(RtfHeader());
            sb.Append(RtfTitle("秩 序 册 目 录", 44));
            for (int i = 0; i < DocSections.Length; i++) {
                sb.Append(RtfPara("    " + DocSections[i], 28));
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        // 主席团 / 组委会 / 工作机构 通用人员名单 — 表格形式
        private string BuildStaffGroupRtf(string title, string group) {
            var sb = new StringBuilder(RtfHeader());
            sb.Append(RtfTitle(title, 44));
            var members = _staff.Where(s => (s.Group ?? "") == group).ToList();
            if (members.Count == 0) {
                sb.Append(RtfPara("（暂无人员；请在主程序「工作人员管理」录入）", 24));
            } else {
                // 简单两列输出: 岗位\t姓名
                sb.Append("\\pard\\f1\\fs28\n");
                foreach (var m in members) {
                    string title2 = m.Title ?? "";
                    string name = string.IsNullOrEmpty(m.Name) ? "                " : m.Name;
                    string country = string.IsNullOrEmpty(m.Country) ? "" : " （" + m.Country + "）";
                    sb.Append(RtfEscape(title2 + "：" + name + country) + "\\par\n");
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        // 7-10 简单文本章节
        private static string BuildSimpleTextRtf(string title, string body) {
            var sb = new StringBuilder(RtfHeader());
            sb.Append(RtfTitle(title, 44));
            foreach (var line in body.Split('\n')) sb.Append(RtfPara(line, 26));
            sb.Append("}\n");
            return sb.ToString();
        }

        // 11. 参赛人员统计表 — 按 (代表队, 组别) 汇总男/女人数
        private string BuildParticipantsStatsRtf() {
            var sb = new StringBuilder(RtfHeader());
            sb.Append(RtfTitle("11. 参赛人员统计表", 44));
            var grouped = _swimmers
                .Where(s => s.Notes == null || !s.Notes.StartsWith("接力队员"))
                .GroupBy(s => (s.Country ?? "") + "|" + (s.AgeCategory ?? ""))
                .OrderBy(g => g.Key).ToList();
            if (grouped.Count == 0) {
                sb.Append(RtfPara("（暂无报名运动员）", 24));
            } else {
                sb.Append("\\pard\\f1\\fs24\n");
                sb.Append(RtfEscape("序号\t参赛单位\t组别\t男\t女\t合计") + "\\par\n");
                int idx = 1;
                foreach (var g in grouped) {
                    var parts = g.Key.Split('|');
                    int men = g.Count(s => s.Gender == "男");
                    int women = g.Count(s => s.Gender == "女");
                    sb.Append(RtfEscape(idx + "\t" + parts[0] + "\t" + (parts.Length > 1 ? parts[1] : "") + "\t" + men + "\t" + women + "\t" + (men + women)) + "\\par\n");
                    idx++;
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        // 12. 运动员姓名号码对照表
        private string BuildBibNameTableRtf() {
            var sb = new StringBuilder(RtfHeader());
            sb.Append(RtfTitle("12. 运动员姓名号码对照表", 44));
            var distinctSw = _swimmers
                .Where(s => s.Notes == null || (!s.Notes.StartsWith("接力队员") && !s.Notes.StartsWith("接力队 棒次:")))
                .GroupBy(s => s.BibNumber ?? "").Select(g => g.First())
                .OrderBy(s => s.BibNumber).ToList();
            if (distinctSw.Count == 0) {
                sb.Append(RtfPara("（暂无报名运动员）", 24));
            } else {
                sb.Append("\\pard\\f1\\fs24\n");
                sb.Append(RtfEscape("号码\t姓名\t性别\t代表队\t组别") + "\\par\n");
                foreach (var s in distinctSw) {
                    sb.Append(RtfEscape((s.BibNumber ?? "") + "\t" + (s.Name ?? "") + "\t" + (s.Gender ?? "") + "\t" + (s.Country ?? "") + "\t" + (s.AgeCategory ?? "")) + "\\par\n");
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private void DocEditPrint_Click(object sender, RoutedEventArgs e) {
            string section = null;
            for (int i = 0; i < _docCheckBoxes.Count; i++) {
                if (_docCheckBoxes[i].IsChecked == true) { section = DocSections[i]; break; }
            }
            if (string.IsNullOrEmpty(section)) {
                MessageBox.Show("请先在左侧选中 1 个章节再点 编辑/打印", "提示"); return;
            }
            string path = GetSectionFilePath(section);
            bool willOverwrite = System.IO.File.Exists(path);
            // 总是重生覆盖（用户选择的方案 B）
            try {
                WriteSectionFile(section, path);
            } catch (Exception ex) {
                MessageBox.Show("生成文件失败: " + ex.Message, "错误");
                return;
            }
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                string warn = willOverwrite ? "\n\n⚠ 上次编辑的内容已被最新系统数据覆盖" : "";
                MessageBox.Show("已用 WPS / Office 打开章节:\n  " + section +
                    "\n\n文件位置 (将持久保留):\n  " + path +
                    "\n\n编辑保存后请勿再点本按钮 (会被覆盖)；点「全本秩序册」可打开整个文件夹合订查看。" + warn,
                    "编辑/打印", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                MessageBox.Show("无法启动外部应用: " + ex.Message +
                    "\n\n文件已保存到:\n" + path + "\n请手动打开。", "提示");
            }
        }

        // 2026-05-25 封面模板 RTF (居中大字, A4)
        // 文本: 比赛名称 / 举办单位 / 地点 / 时间; 仿索美 游泳比赛封面模版.doc
        private string BuildCoverRtf() {
            string compName = string.IsNullOrEmpty(CompetitionNameInfo) ? "（请填写比赛名称）" : CompetitionNameInfo;
            string organizer = string.IsNullOrEmpty(CompetitionOrganizer) ? "（举办单位）" : CompetitionOrganizer;
            string location = string.IsNullOrEmpty(CompetitionLocation) ? "XXXX 游泳馆" : CompetitionLocation;
            string dateRange;
            if (!string.IsNullOrEmpty(CompetitionStartDate) && !string.IsNullOrEmpty(CompetitionEndDate)
                && CompetitionStartDate != CompetitionEndDate) {
                dateRange = CompetitionStartDate + " ~ " + CompetitionEndDate;
            } else if (!string.IsNullOrEmpty(CompetitionStartDate)) {
                dateRange = CompetitionStartDate;
            } else {
                dateRange = "XXXX 年 XX 月 XX 日";
            }
            var sb = new StringBuilder();
            sb.Append("{\\rtf1\\ansi\\ansicpg936\\fcharset134\\deff0\n");
            sb.Append("{\\fonttbl{\\f0\\fnil\\fcharset134 ");
            sb.Append(RtfEscape("黑体")); sb.Append(";}{\\f1\\fnil\\fcharset134 ");
            sb.Append(RtfEscape("宋体")); sb.Append(";}}\n");
            sb.Append("\\paperw11906\\paperh16838\\margl1440\\margr1440\\margt2880\\margb1440\n");
            // 顶部留白
            sb.Append("\\pard\\sb2400\\sa0\\par\n");
            // 比赛名称 - 居中大字
            sb.Append("\\pard\\qc\\sa600\\f0\\fs72\\b ");
            sb.Append(RtfEscape(compName));
            sb.Append("\\b0\\par\n");
            // 举办单位
            sb.Append("\\pard\\qc\\sa1200\\f0\\fs32 ");
            sb.Append(RtfEscape("主 办 单 位 ：" + organizer));
            sb.Append("\\par\n");
            // 留白 + 标志位
            sb.Append("\\pard\\qc\\sa1200\\f1\\fs24 ");
            sb.Append(RtfEscape("（举办单位标志图片）"));
            sb.Append("\\par\n");
            // 地点 + 时间 — 底部
            sb.Append("\\pard\\qc\\sa200\\f1\\fs28 ");
            sb.Append(RtfEscape("地点：" + location));
            sb.Append("\\par\n");
            sb.Append("\\pard\\qc\\sa200\\f1\\fs28 ");
            sb.Append(RtfEscape("时间：" + dateRange));
            sb.Append("\\par\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        // 2026-05-25 仲裁委员 + 裁判员名单 RTF; 按 _staff 5 组结构渲染
        // 仲裁委员: 技术及仲裁 组中 Title 含"仲裁"的成员
        // 裁判员各岗位: 裁判员 组按 Title 分类
        private string BuildRefereeListRtf() {
            // 仲裁委员
            var arbiters = _staff.Where(s => (s.Group ?? "") == StaffGroups.TechArbitration
                                            && (s.Title ?? "").Contains("仲裁")
                                            && !string.IsNullOrEmpty(s.Name)).ToList();
            // 裁判员各岗位
            var refs = _staff.Where(s => (s.Group ?? "") == StaffGroups.Referees && !string.IsNullOrEmpty(s.Title)).ToList();
            // 按 Title 分组, 保持原顺序
            var byTitle = new List<KeyValuePair<string, List<StaffMember>>>();
            foreach (var s in refs) {
                var ex = byTitle.FirstOrDefault(p => p.Key == s.Title);
                if (ex.Key == null) {
                    var list = new List<StaffMember> { s };
                    byTitle.Add(new KeyValuePair<string, List<StaffMember>>(s.Title, list));
                } else {
                    ex.Value.Add(s);
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\\rtf1\\ansi\\ansicpg936\\fcharset134\\deff0\n");
            sb.Append("{\\fonttbl{\\f0\\fnil\\fcharset134 ");
            sb.Append(RtfEscape("黑体")); sb.Append(";}{\\f1\\fnil\\fcharset134 ");
            sb.Append(RtfEscape("宋体")); sb.Append(";}}\n");
            sb.Append("\\paperw11906\\paperh16838\\margl1440\\margr1440\\margt1440\\margb1440\n");

            // 标题 - 仲裁委员
            sb.Append("\\pard\\qc\\sa300\\f0\\fs44\\b ");
            sb.Append(RtfEscape("仲 裁 委 员"));
            sb.Append("\\b0\\par\n");
            // 横排显示 5 个（不够留空）
            sb.Append("\\pard\\qc\\sa600\\f1\\fs28 ");
            var arbNames = new List<string>();
            for (int i = 0; i < 5; i++) {
                arbNames.Add(i < arbiters.Count ? arbiters[i].Name : "AAAA");
            }
            sb.Append(RtfEscape(string.Join("    ", arbNames.ToArray())));
            sb.Append("\\par\n");

            // 标题 - 裁判员名单
            sb.Append("\\pard\\qc\\sa400\\f0\\fs44\\b ");
            sb.Append(RtfEscape("裁  判  员  名  单"));
            sb.Append("\\b0\\par\n");

            // 各岗位: 左对齐 "岗位：名字 名字 ..."
            // 单人岗位: 把名字直接接在冒号后; 多人: 用逗号分隔
            if (byTitle.Count == 0) {
                sb.Append("\\pard\\sa200\\f1\\fs24 ");
                sb.Append(RtfEscape("（裁判员名单为空, 请在「工作人员管理」中录入）"));
                sb.Append("\\par\n");
            } else {
                foreach (var kv in byTitle) {
                    sb.Append("\\pard\\sa120\\f1\\fs28 ");
                    string names = string.Join("、", kv.Value.Select(s => s.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToArray());
                    string line = kv.Key + "：" + names;
                    sb.Append(RtfEscape(line));
                    sb.Append("\\par\n");
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        // RTF 转义: 反斜杠/花括号; 非 ASCII 字符按 \uN? 形式输出（GB2312 编码也可, 但 \u 更通用）
        private static string RtfEscape(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (var c in s) {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '{') sb.Append("\\{");
                else if (c == '}') sb.Append("\\}");
                else if (c == '\n') sb.Append("\\line ");
                else if (c < 128) sb.Append(c);
                else {
                    // RTF Unicode 转义: \uN? (N 是有符号 16 位整数; ? 是 fallback char)
                    int code = (int)c;
                    if (code > 32767) code -= 65536;
                    sb.Append("\\u" + code + "?");
                }
            }
            return sb.ToString();
        }

        private void DocGenerate_Click(object sender, RoutedEventArgs e) {
            // 2026-05-25 全本秩序册 = 文件夹模式
            //  扫描 MyDocuments\swim_orderbook\<比赛名>\: 缺哪个章节文件就自动生成, 已存在的(用户可能编辑过)保留
            //  完成后用 Explorer 打开文件夹便于用户逐章查看/打印
            string folder = GetOrderBookFolder();
            int generated = 0, preserved = 0;
            var failures = new List<string>();
            foreach (var section in DocSections) {
                string path = GetSectionFilePath(section);
                if (System.IO.File.Exists(path)) {
                    preserved++;
                    continue;
                }
                try {
                    WriteSectionFile(section, path);
                    generated++;
                } catch (Exception ex) {
                    failures.Add(section + " ← " + ex.Message);
                }
            }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); } catch { }
            var sb = new StringBuilder();
            sb.AppendFormat("✔ 秩序册文件夹已就绪 (共 {0} 章节)\n\n", DocSections.Length);
            sb.AppendFormat("  • 系统自动生成: {0} 章\n", generated);
            sb.AppendFormat("  • 用户已编辑保留: {0} 章\n", preserved);
            if (failures.Count > 0) {
                sb.AppendFormat("  • 生成失败: {0} 章\n", failures.Count);
                foreach (var f in failures) sb.AppendLine("    - " + f);
            }
            sb.AppendLine();
            sb.AppendLine("文件夹路径:");
            sb.AppendLine("  " + folder);
            sb.AppendLine();
            sb.AppendLine("每章一个文件 (NN_章节名.rtf 或 .xlsx), 可用 WPS/Word/Excel 单独编辑并打印。");
            sb.AppendLine("「编辑/打印」按钮会强制覆盖该章节为最新系统数据，请慎用。");
            MessageBox.Show(sb.ToString(), "全本秩序册", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 强制重新生成当前选中章节（用最新系统数据覆盖文件夹里的已编辑版本）
        private void DocForceRegen_Click(object sender, RoutedEventArgs e) {
            string section = null;
            for (int i = 0; i < _docCheckBoxes.Count; i++) {
                if (_docCheckBoxes[i].IsChecked == true) { section = DocSections[i]; break; }
            }
            if (string.IsNullOrEmpty(section)) {
                MessageBox.Show("请先选中要重生的章节", "提示"); return;
            }
            string path = GetSectionFilePath(section);
            bool exists = System.IO.File.Exists(path);
            string msg = exists
                ? "确认用最新系统数据覆盖以下章节文件？\n\n" + section + "\n→ " + path + "\n\n（手工编辑会丢失！）"
                : "确认按最新系统数据生成本章节文件？\n\n" + section + "\n→ " + path;
            if (MessageBox.Show(msg, "强制重生", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            try {
                WriteSectionFile(section, path);
                MessageBox.Show("已重生:\n  " + path, "完成");
            } catch (Exception ex) {
                MessageBox.Show("重生失败: " + ex.Message, "错误");
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

                    // 2026-05-24 在统计表下方追加单位元信息（领队/教练/联系电话）
                    var unitsByName = _units.Where(u => u != null && !string.IsNullOrEmpty(u.Name))
                                            .GroupBy(u => u.Name).ToDictionary(g => g.Key, g => g.First());
                    var allUnits = _swimmers.Where(s => s.Notes == null || !s.Notes.StartsWith("接力队员"))
                                            .Select(s => s.Country ?? "").Where(c => !string.IsNullOrEmpty(c))
                                            .Distinct().OrderBy(c => c).ToList();
                    if (allUnits.Count > 0) {
                        r++; // 空行分隔
                        var titleRow = sheet.CreateRow(r++);
                        titleRow.CreateCell(0).SetCellValue("【单位元信息】（来自 参赛单位管理）");
                        var headerRow2 = sheet.CreateRow(r++);
                        headerRow2.CreateCell(0).SetCellValue("序号");
                        headerRow2.CreateCell(1).SetCellValue("参赛单位");
                        headerRow2.CreateCell(2).SetCellValue("简称");
                        headerRow2.CreateCell(3).SetCellValue("领队");
                        headerRow2.CreateCell(4).SetCellValue("教练");
                        headerRow2.CreateCell(5).SetCellValue("队医");
                        headerRow2.CreateCell(6).SetCellValue("基础分");
                        headerRow2.CreateCell(7).SetCellValue("联系电话");
                        headerRow2.CreateCell(8).SetCellValue("地址");
                        headerRow2.CreateCell(9).SetCellValue("备注");
                        int ui = 1;
                        foreach (var uname in allUnits) {
                            Unit u; unitsByName.TryGetValue(uname, out u);
                            var rr2 = sheet.CreateRow(r++);
                            rr2.CreateCell(0).SetCellValue(ui++);
                            rr2.CreateCell(1).SetCellValue(uname);
                            rr2.CreateCell(2).SetCellValue(u != null ? (u.ShortName ?? "") : "");
                            rr2.CreateCell(3).SetCellValue(u != null ? (u.Leader ?? "") : "");
                            rr2.CreateCell(4).SetCellValue(u != null ? (u.Coach ?? "") : "");
                            rr2.CreateCell(5).SetCellValue(u != null ? (u.Doctor ?? "") : "");
                            rr2.CreateCell(6).SetCellValue(u != null ? u.BasePoints : 0);
                            rr2.CreateCell(7).SetCellValue(u != null ? (u.Phone ?? "") : "");
                            rr2.CreateCell(8).SetCellValue(u != null ? (u.Address ?? "") : "");
                            rr2.CreateCell(9).SetCellValue(u != null ? (u.Note ?? "") : "");
                        }
                    }
                } else if (sectionTitle.Contains("主席团名单") || sectionTitle.Contains("组织委员会")
                           || sectionTitle.Contains("工作机构") || sectionTitle.Contains("大会工作机构")
                           || sectionTitle.Contains("技术") || sectionTitle.Contains("仲裁")
                           || sectionTitle.Contains("裁判员名单")) {
                    // 2026-05-25 P0-D 拉 _staff 列表对应分组（5 组 + 裁判员特殊列）
                    string group;
                    if (sectionTitle.Contains("主席团")) group = StaffGroups.Presidium;
                    else if (sectionTitle.Contains("组织委员会")) group = StaffGroups.OrgCommittee;
                    else if (sectionTitle.Contains("工作机构") || sectionTitle.Contains("大会工作机构")) group = StaffGroups.WorkOrg;
                    else if (sectionTitle.Contains("裁判员")) group = StaffGroups.Referees;
                    else group = StaffGroups.TechArbitration;   // 技术代表 + 仲裁

                    bool isRefereesGrp = (group == StaffGroups.Referees);
                    var hr = sheet.CreateRow(r++);
                    hr.CreateCell(0).SetCellValue("序号"); hr.CreateCell(1).SetCellValue("工作岗位");
                    hr.CreateCell(2).SetCellValue("姓名"); hr.CreateCell(3).SetCellValue("性别");
                    if (isRefereesGrp) {
                        hr.CreateCell(4).SetCellValue("裁判等级");
                        hr.CreateCell(5).SetCellValue("电话");
                    } else {
                        hr.CreateCell(4).SetCellValue("电话");
                        hr.CreateCell(5).SetCellValue("工作单位");
                    }
                    hr.CreateCell(6).SetCellValue("备注");
                    int si = 1;
                    var groupStaff = _staff.Where(s => (s.Group ?? "") == group).ToList();
                    if (groupStaff.Count == 0) {
                        var note = sheet.CreateRow(r++);
                        note.CreateCell(0).SetCellValue("（暂无人员，请在主程序「工作人员管理」中录入此组人员）");
                    } else {
                        foreach (var s in groupStaff) {
                            var rr = sheet.CreateRow(r++);
                            rr.CreateCell(0).SetCellValue(si++);
                            rr.CreateCell(1).SetCellValue(s.Title ?? "");
                            rr.CreateCell(2).SetCellValue(s.Name ?? "");
                            rr.CreateCell(3).SetCellValue(s.Gender ?? "");
                            if (isRefereesGrp) {
                                rr.CreateCell(4).SetCellValue(s.RefereeLevel ?? "");
                                rr.CreateCell(5).SetCellValue(s.Phone ?? "");
                            } else {
                                rr.CreateCell(4).SetCellValue(s.Phone ?? "");
                                rr.CreateCell(5).SetCellValue(s.Country ?? "");
                            }
                            rr.CreateCell(6).SetCellValue(s.Note ?? "");
                        }
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
                } else if (sectionTitle.Contains("竞赛日程表")) {
                    // 2026-05-25 章节 13: 优先用向导内编排 _distAssigned, 没有就用主程序 _schedule
                    var hr = sheet.CreateRow(r++);
                    hr.CreateCell(0).SetCellValue("序号"); hr.CreateCell(1).SetCellValue("日期");
                    hr.CreateCell(2).SetCellValue("时段"); hr.CreateCell(3).SetCellValue("时间");
                    hr.CreateCell(4).SetCellValue("组别"); hr.CreateCell(5).SetCellValue("性别");
                    hr.CreateCell(6).SetCellValue("比赛项目"); hr.CreateCell(7).SetCellValue("赛次");
                    hr.CreateCell(8).SetCellValue("组数"); hr.CreateCell(9).SetCellValue("备注");

                    Func<string, int> sessionRank = s => s == "上午" ? 0 : s == "下午" ? 1 : s == "晚上" ? 2 : 9;
                    int rowNo = 1;
                    if (_distAssigned != null && _distAssigned.Count > 0) {
                        var ordered = _distAssigned.OrderBy(x => x.AssignedDate ?? "")
                                                    .ThenBy(x => sessionRank(x.AssignedSession ?? ""))
                                                    .ThenBy(x => x.AssignedSortKey).ToList();
                        foreach (var d in ordered) {
                            var rr = sheet.CreateRow(r++);
                            rr.CreateCell(0).SetCellValue(rowNo++);
                            rr.CreateCell(1).SetCellValue(d.AssignedDate ?? "");
                            rr.CreateCell(2).SetCellValue(d.AssignedSession ?? "");
                            rr.CreateCell(3).SetCellValue(d.AssignedTime ?? "");
                            rr.CreateCell(4).SetCellValue(d.AgeGroup ?? "");
                            rr.CreateCell(5).SetCellValue(d.Gender ?? "");
                            rr.CreateCell(6).SetCellValue(d.EventName ?? "");
                            rr.CreateCell(7).SetCellValue(d.Stage ?? "");
                            rr.CreateCell(8).SetCellValue(d.Heats);
                            rr.CreateCell(9).SetCellValue(d.HeatRange ?? "");
                        }
                    } else if (ExistingSchedule != null) {
                        var schedList = ExistingSchedule.ToList();
                        foreach (var si in schedList.OrderBy(s => s.SessionNumber).ThenBy(s => s.Time ?? "")) {
                            var rr = sheet.CreateRow(r++);
                            rr.CreateCell(0).SetCellValue(rowNo++);
                            rr.CreateCell(1).SetCellValue(si.Date ?? "");
                            rr.CreateCell(2).SetCellValue(si.SessionName ?? "");
                            rr.CreateCell(3).SetCellValue(si.Time ?? "");
                            rr.CreateCell(4).SetCellValue(si.AgeGroup ?? "");
                            rr.CreateCell(5).SetCellValue(si.Gender ?? "");
                            rr.CreateCell(6).SetCellValue(si.EventName ?? "");
                            rr.CreateCell(7).SetCellValue(si.Stage ?? "");
                            rr.CreateCell(8).SetCellValue(si.HeatCount);
                            rr.CreateCell(9).SetCellValue("");
                        }
                    } else {
                        var rr = sheet.CreateRow(r++);
                        rr.CreateCell(0).SetCellValue("（暂无赛程；请在 Tab 3 编排或返回主程序生成日程后再导出）");
                    }
                } else if (sectionTitle.Contains("各项运动员竞赛分组名单")) {
                    // 2026-05-25 章节 15: 列出每个 项目+组别+性别+赛次 的每组泳道分配
                    int rowNo = 0;
                    // 按 FINA 项目顺序 + 组别 + 性别 + 赛次 分组
                    var indi = _swimmers.Where(s => s.Notes == null || !s.Notes.StartsWith("接力队员")).ToList();
                    var byGroup = indi.GroupBy(s => new {
                        Event = s.EventName ?? "",
                        Age = s.AgeCategory ?? "",
                        Gender = s.Gender ?? "",
                        Stage = s.CurrentStage ?? "预赛"
                    }).Where(g => !string.IsNullOrEmpty(g.Key.Event))
                       .OrderBy(g => g.Key.Event)
                       .ThenBy(g => g.Key.Age)
                       .ThenBy(g => g.Key.Gender)
                       .ToList();
                    if (byGroup.Count == 0) {
                        var rr0 = sheet.CreateRow(r++);
                        rr0.CreateCell(0).SetCellValue("（暂无分组数据；请先在「赛程管理 / 项目自动分组」完成分组）");
                    }
                    foreach (var g in byGroup) {
                        // 项目标题行
                        var titleRow = sheet.CreateRow(r++);
                        string title = string.Format("{0}{1} {2} {3}",
                            string.IsNullOrEmpty(g.Key.Age) ? "" : g.Key.Age + " ",
                            g.Key.Gender, g.Key.Event, g.Key.Stage);
                        titleRow.CreateCell(0).SetCellValue(title);

                        // 表头
                        var hdr = sheet.CreateRow(r++);
                        hdr.CreateCell(0).SetCellValue("组");
                        hdr.CreateCell(1).SetCellValue("道");
                        hdr.CreateCell(2).SetCellValue("号码");
                        hdr.CreateCell(3).SetCellValue("姓名");
                        hdr.CreateCell(4).SetCellValue("代表队");
                        hdr.CreateCell(5).SetCellValue("报名成绩");

                        // 各 heat 排序: heat → lane
                        var assigned = g.Where(s => s.Heat > 0).OrderBy(s => s.Heat).ThenBy(s => s.Lane).ToList();
                        if (assigned.Count == 0) {
                            var rr1 = sheet.CreateRow(r++);
                            rr1.CreateCell(0).SetCellValue("（未分组）");
                        } else {
                            foreach (var s in assigned) {
                                var rr = sheet.CreateRow(r++);
                                rr.CreateCell(0).SetCellValue(s.Heat);
                                rr.CreateCell(1).SetCellValue(s.Lane);
                                rr.CreateCell(2).SetCellValue(s.BibNumber ?? "");
                                rr.CreateCell(3).SetCellValue(s.Name ?? "");
                                rr.CreateCell(4).SetCellValue(s.Country ?? "");
                                rr.CreateCell(5).SetCellValue(s.EntryTime ?? "");
                            }
                        }
                        // 项目间空行分隔
                        r++;
                        rowNo++;
                    }
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
        // 2026-05-24 C5 长距离快慢组：可选值 "慢组" / "快组" / null（普通项目）
        public string HeatRange { get; set; }
        public string StageDisplay {
            get { return string.IsNullOrEmpty(HeatRange) ? Stage : Stage + "(" + HeatRange + ")"; }
        }
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

    // 2026-05-25 向导草稿: 中途关闭/修改未确认时的状态快照, 下次打开恢复
    public class WizardDraft
    {
        public List<SchedulingPlanEntry> PlanRows { get; set; }
        public List<DistEntry> DistPending { get; set; }
        public List<DistEntry> DistAssigned { get; set; }
        public List<string> AvailableDates { get; set; }
        public int[] SessionStartMin { get; set; }
        public bool HasData { get { return (PlanRows != null && PlanRows.Count > 0) || (DistAssigned != null && DistAssigned.Count > 0); } }
        public WizardDraft() {
            PlanRows = new List<SchedulingPlanEntry>();
            DistPending = new List<DistEntry>();
            DistAssigned = new List<DistEntry>();
            AvailableDates = new List<string>();
            SessionStartMin = new[] { 9 * 60, 14 * 60 + 30, 19 * 60 + 30 };
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
        // 2026-05-24 C5 长距离快慢组：可选值 "慢组" / "快组" / null
        public string HeatRange { get; set; }
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
        public int Participants { get { return _participants; } set { _participants = value; Notify("Participants"); Notify("ParticipantsLabel"); } }
        public string ParticipantsLabel { get { return _participants + "人"; } }
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
