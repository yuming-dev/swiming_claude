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

        public PromotionQueryWindow(ObservableCollection<Swimmer> swimmers, List<string> events, PoolConfig poolConfig) {
            InitializeComponent();
            _swimmers = swimmers;
            _events = events;
            _poolConfig = poolConfig;

            // 从实际运动员数据提取项目列表（而不是用固定列表）
            var actualEvents = new HashSet<string>();
            foreach (var s in swimmers) {
                if (!string.IsNullOrEmpty(s.EventName)) actualEvents.Add(s.EventName);
            }
            foreach (string ev in actualEvents) EventCombo.Items.Add(ev);
            // 也添加固定列表中有但运动员没报的项目
            foreach (string ev in events) {
                if (!actualEvents.Contains(ev)) EventCombo.Items.Add(ev);
            }
            if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;

            // 自动填充性别（从运动员数据）
            AutoFillFromData();
        }

        private void AutoFillFromData() {
            // 统计各阶段有成绩的运动员数量
            var stageInfo = new Dictionary<string, int>();
            foreach (var s in _swimmers) {
                foreach (var r in s.Results) {
                    if (!stageInfo.ContainsKey(r.Stage)) stageInfo[r.Stage] = 0;
                    stageInfo[r.Stage]++;
                }
            }
            // 在窗口标题显示信息
            if (stageInfo.Count > 0) {
                var parts = new List<string>();
                foreach (var kv in stageInfo) parts.Add(string.Format("{0}:{1}人", kv.Key, kv.Value));
                Title = "晋级处理 — " + string.Join(", ", parts.ToArray());
            }
        }

        private void Event_Changed(object sender, SelectionChangedEventArgs e) {
            // 切换项目时自动更新可用信息
        }

        private string GetSelectedGender() {
            return GenderCombo.SelectedItem != null ? ((ComboBoxItem)GenderCombo.SelectedItem).Content.ToString() : "男";
        }

        private void Query_Click(object sender, RoutedEventArgs e) {
            string gender = GetSelectedGender();
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string fromStage = ((ComboBoxItem)FromStageCombo.SelectedItem).Content.ToString();
            int count;
            if (!int.TryParse(CountBox.Text.Trim(), out count)) count = 8;

            // 查找有该阶段成绩的运动员（不管CurrentStage是什么）
            var withResults = new List<Swimmer>();
            var withoutResults = new List<Swimmer>();

            foreach (var s in _swimmers) {
                if (s.EventName != eventName) continue;
                if (s.Gender != gender) continue;

                var result = s.GetResultForStage(fromStage);
                if (result != null && result.FinalTime > 0 &&
                    s.Status != "DNS" && s.Status != "DNF" && s.Status != "DSQ") {
                    withResults.Add(s);
                } else if (s.CurrentStage == fromStage) {
                    withoutResults.Add(s);
                }
            }

            // 按成绩排序
            withResults.Sort((a, b) => {
                var ra = a.GetResultForStage(fromStage);
                var rb = b.GetResultForStage(fromStage);
                double ta = ra != null ? ra.FinalTime : double.MaxValue;
                double tb = rb != null ? rb.FinalTime : double.MaxValue;
                return ta.CompareTo(tb);
            });

            _promoted = withResults.Take(count).ToList();

            if (_promoted.Count == 0) {
                string msg = string.Format("未找到 {0} {1} {2} 的成绩。\n\n", gender, eventName, fromStage);
                msg += string.Format("该项目共 {0} 名运动员", _swimmers.Count(s => s.Gender == gender && s.EventName == eventName));
                msg += string.Format("\n有{0}成绩的: {1}人", fromStage, withResults.Count);
                msg += string.Format("\n当前在{0}阶段的: {1}人", fromStage, withoutResults.Count);
                MessageBox.Show(msg, "查询结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            var displayData = new List<object>();
            int rank = 1;
            foreach (var sw in _promoted) {
                var result = sw.GetResultForStage(fromStage);
                displayData.Add(new {
                    Rank = rank++,
                    BibNumber = sw.BibNumber,
                    Name = sw.Name,
                    Country = sw.Country,
                    Time = result != null ? TimeFormatter.Format(result.FinalTime) : "-",
                    Stage = sw.CurrentStage
                });
            }
            PromotionGrid.ItemsSource = displayData;
        }

        private void Execute_Click(object sender, RoutedEventArgs e) {
            if (_promoted.Count == 0) { MessageBox.Show("请先查询晋级名单"); return; }
            string fromStage = ((ComboBoxItem)FromStageCombo.SelectedItem).Content.ToString();
            string toStage = ((ComboBoxItem)ToStageCombo.SelectedItem).Content.ToString();
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";

            // 使用晋级专用分组（按上一轮成绩蛇形分组）
            var assignments = HeatScheduler.GenerateHeatsFromResults(_promoted, _poolConfig, eventName, toStage, fromStage);
            int heatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;

            MessageBox.Show(string.Format("已将{0}名运动员晋级到{1}，分为{2}组\n（按{3}成绩蛇形分组）", _promoted.Count, toStage, heatCount, fromStage));
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
