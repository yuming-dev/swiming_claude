using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
