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

        private void Query_Click(object sender, RoutedEventArgs e) {
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string fromStage = ((ComboBoxItem)FromStageCombo.SelectedItem).Content.ToString();
            int count;
            if (!int.TryParse(CountBox.Text.Trim(), out count)) count = 8;

            _promoted = HeatScheduler.GetPromotedSwimmers(_swimmers.ToList(), eventName, fromStage, count);

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
            string toStage = ((ComboBoxItem)ToStageCombo.SelectedItem).Content.ToString();

            foreach (var sw in _promoted) {
                sw.CurrentStage = toStage;
                sw.Heat = 0;
                sw.Lane = 0;
            }

            // 重新分组
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            HeatScheduler.GenerateHeats(_promoted, _poolConfig, eventName, toStage);

            MessageBox.Show(string.Format("已将{0}名运动员晋级到{1}", _promoted.Count, toStage));
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
