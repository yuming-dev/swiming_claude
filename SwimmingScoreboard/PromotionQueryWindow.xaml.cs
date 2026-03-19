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
            foreach (string ev in events) EventCombo.Items.Add(ev);
            if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void Event_Changed(object sender, SelectionChangedEventArgs e) { }

        private string GetSelectedGender() {
            return GenderCombo.SelectedItem != null ? ((ComboBoxItem)GenderCombo.SelectedItem).Content.ToString() : "男";
        }

        private void Query_Click(object sender, RoutedEventArgs e) {
            string gender = GetSelectedGender();
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string fromStage = ((ComboBoxItem)FromStageCombo.SelectedItem).Content.ToString();
            int count;
            if (!int.TryParse(CountBox.Text.Trim(), out count)) count = 8;

            // 按性别和项目筛选
            var filtered = _swimmers.Where(s => s.Gender == gender && s.EventName == eventName).ToList();
            _promoted = HeatScheduler.GetPromotedSwimmers(filtered, eventName, fromStage, count);

            var displayData = new List<object>();
            int rank = 1;
            foreach (var sw in _promoted) {
                var result = sw.GetResultForStage(fromStage);
                displayData.Add(new {
                    Rank = rank++,
                    BibNumber = sw.BibNumber,
                    Name = sw.Name,
                    Country = sw.Country,
                    Time = result != null ? TimeFormatter.Format(result.FinalTime) : "-"
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
