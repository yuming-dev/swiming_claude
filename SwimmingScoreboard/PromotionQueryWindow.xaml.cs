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
            // 从实际运动员数据提取 性别+项目 组合
            var eventSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (!string.IsNullOrEmpty(s.EventName) && !string.IsNullOrEmpty(s.Gender)) {
                    string key = s.Gender + " " + s.EventName;
                    eventSet.Add(key);
                }
            }
            foreach (string ev in eventSet.OrderBy(e => e)) {
                EventCombo.Items.Add(ev);
            }
            if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void Event_Changed(object sender, SelectionChangedEventArgs e) {
            UpdateStages();
        }

        private void UpdateStages() {
            FromStageCombo.Items.Clear();
            string selectedEvent = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            if (string.IsNullOrEmpty(selectedEvent)) return;

            // 解析性别和项目
            string gender = "", eventName = "";
            ParseGenderEvent(selectedEvent, out gender, out eventName);

            // 查找该项目运动员有哪些阶段的成绩
            var stagesWithResults = new HashSet<string>();
            var stagesInSchedule = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (s.Gender == gender && s.EventName == eventName) {
                    foreach (var r in s.Results) {
                        if (r.FinalTime > 0) stagesWithResults.Add(r.Stage);
                    }
                    if (!string.IsNullOrEmpty(s.CurrentStage)) stagesInSchedule.Add(s.CurrentStage);
                }
            }

            // 按阶段顺序添加有成绩的阶段
            string[] stageOrder = { "预赛", "复赛", "半决赛" }; // 决赛不需要晋级
            foreach (string st in stageOrder) {
                if (stagesWithResults.Contains(st)) {
                    FromStageCombo.Items.Add(st);
                }
            }

            if (FromStageCombo.Items.Count > 0) {
                FromStageCombo.SelectedIndex = FromStageCombo.Items.Count - 1; // 选最新的阶段
            }

            UpdateInfo();
        }

        private void Stage_Changed(object sender, SelectionChangedEventArgs e) {
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

            string selectedEvent = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string gender = "", eventName = "";
            ParseGenderEvent(selectedEvent, out gender, out eventName);

            // 确定下一阶段
            if (_fromStage == "预赛") _toStage = "半决赛";
            else if (_fromStage == "复赛") _toStage = "半决赛";
            else if (_fromStage == "半决赛") _toStage = "决赛";
            else { _toStage = ""; return; }

            // 统计有成绩的运动员
            int withResults = 0;
            int total = 0;
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                total++;
                var r = s.GetResultForStage(_fromStage);
                if (r != null && r.FinalTime > 0) withResults++;
            }

            int promoCount = 8;
            int.TryParse(CountBox.Text.Trim(), out promoCount);

            InfoText.Text = string.Format("{0} {1}\n{2} 共 {3} 人参赛，{4} 人有成绩\n将选取前 {5} 名晋级到 {6}",
                gender, eventName, _fromStage, total, withResults, promoCount, _toStage);

            if (withResults == 0) {
                WarningText.Text = "警告：该阶段没有任何成绩数据！请先完成比赛并确认成绩。";
            } else if (withResults < promoCount) {
                WarningText.Text = string.Format("注意：有成绩的人数（{0}）少于晋级人数（{1}），将全部晋级。", withResults, promoCount);
            }
        }

        private void Query_Click(object sender, RoutedEventArgs e) {
            string selectedEvent = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string gender = "", eventName = "";
            ParseGenderEvent(selectedEvent, out gender, out eventName);

            if (string.IsNullOrEmpty(_fromStage)) {
                MessageBox.Show("请选择数据来源阶段"); return;
            }

            int count = 8;
            int.TryParse(CountBox.Text.Trim(), out count);

            // 直接查找有该阶段成绩的运动员
            var candidates = new List<Swimmer>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ") continue;
                var r = s.GetResultForStage(_fromStage);
                if (r != null && r.FinalTime > 0) candidates.Add(s);
            }

            // 按成绩排序
            candidates.Sort((a, b) => {
                double ta = a.GetResultForStage(_fromStage).FinalTime;
                double tb = b.GetResultForStage(_fromStage).FinalTime;
                return ta.CompareTo(tb);
            });

            _promoted = candidates.Take(count).ToList();

            // 显示
            var displayData = new List<object>();
            int rank = 1;
            foreach (var sw in _promoted) {
                var result = sw.GetResultForStage(_fromStage);
                displayData.Add(new {
                    Rank = rank++,
                    BibNumber = sw.BibNumber ?? "",
                    Name = sw.Name ?? "",
                    Gender = sw.Gender ?? "",
                    Country = sw.Country ?? "",
                    Time = result != null ? TimeFormatter.Format(result.FinalTime) : "-",
                    FromStage = _fromStage,
                    ToStage = _toStage
                });
            }
            PromotionGrid.ItemsSource = displayData;

            if (_promoted.Count == 0) {
                ResultText.Text = "未找到成绩数据";
                ResultText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            } else {
                ResultText.Text = string.Format("找到 {0} 人", _promoted.Count);
                ResultText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
        }

        private void Execute_Click(object sender, RoutedEventArgs e) {
            if (_promoted.Count == 0) {
                MessageBox.Show("请先点击\"查询\"查看晋级名单"); return;
            }
            if (string.IsNullOrEmpty(_toStage)) {
                MessageBox.Show("无法确定晋级目标阶段"); return;
            }

            string selectedEvent = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string gender = "", eventName = "";
            ParseGenderEvent(selectedEvent, out gender, out eventName);

            if (MessageBox.Show(
                string.Format("确认将 {0} 名运动员从 {1} 晋级到 {2}？\n\n将按{1}成绩蛇形分组。",
                    _promoted.Count, _fromStage, _toStage),
                "确认晋级", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
                return;
            }

            var assignments = HeatScheduler.GenerateHeatsFromResults(_promoted, _poolConfig, eventName, _toStage, _fromStage);
            int heatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;

            ResultText.Text = string.Format("已晋级 {0} 人到{1}，{2}组", _promoted.Count, _toStage, heatCount);
            MessageBox.Show(string.Format("已将 {0} 名运动员晋级到 {1}，分为 {2} 组。\n\n请在赛程导航树中选择{1}的组次开始比赛。",
                _promoted.Count, _toStage, heatCount), "晋级完成");
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private void ParseGenderEvent(string combined, out string gender, out string eventName) {
            gender = "";
            eventName = "";
            if (string.IsNullOrEmpty(combined)) return;
            int spaceIdx = combined.IndexOf(' ');
            if (spaceIdx > 0) {
                gender = combined.Substring(0, spaceIdx);
                eventName = combined.Substring(spaceIdx + 1);
            } else {
                eventName = combined;
            }
        }
    }
}
