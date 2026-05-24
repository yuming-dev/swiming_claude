using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace SwimmingScoreboard
{
    // 2026-05-24 P0-2 项目用时设置 — 编辑 EventDurationConfig 并写回主程序
    public partial class EventDurationConfigWindow : Window
    {
        private readonly EventDurationConfig _config;
        private ObservableCollection<DurRow> _indiRows = new ObservableCollection<DurRow>();
        private ObservableCollection<DurRow> _relayRows = new ObservableCollection<DurRow>();

        public EventDurationConfigWindow(EventDurationConfig config) {
            InitializeComponent();
            _config = config ?? new EventDurationConfig();
            IndividualGrid.ItemsSource = _indiRows;
            RelayGrid.ItemsSource = _relayRows;
            LoadFromConfig();
        }

        private void LoadFromConfig() {
            _indiRows.Clear();
            _relayRows.Clear();
            if (_config.IndividualMinutesPerHeat != null) {
                foreach (var kv in _config.IndividualMinutesPerHeat.OrderBy(k => k.Key))
                    _indiRows.Add(new DurRow { Distance = kv.Key, Minutes = kv.Value });
            }
            if (_config.RelayMinutesPerHeat != null) {
                foreach (var kv in _config.RelayMinutesPerHeat.OrderBy(k => k.Key))
                    _relayRows.Add(new DurRow { Distance = kv.Key, Minutes = kv.Value });
            }
            GapMinBox.Text = _config.InterEventGapMinutes.ToString();
            DefaultIndiBox.Text = _config.DefaultIndividualMinutes.ToString();
            DefaultRelayBox.Text = _config.DefaultRelayMinutes.ToString();
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e) {
            if (MessageBox.Show("将所有项目用时恢复到默认值？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _config.ResetToDefaults();
            LoadFromConfig();
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            var newIndi = new Dictionary<int, int>();
            foreach (var r in _indiRows) {
                if (r.Distance <= 0 || r.Minutes <= 0) continue;
                newIndi[r.Distance] = r.Minutes;
            }
            var newRelay = new Dictionary<int, int>();
            foreach (var r in _relayRows) {
                if (r.Distance <= 0 || r.Minutes <= 0) continue;
                newRelay[r.Distance] = r.Minutes;
            }
            int gap, defI, defR;
            if (!int.TryParse(GapMinBox.Text.Trim(), out gap) || gap < 0) gap = 2;
            if (!int.TryParse(DefaultIndiBox.Text.Trim(), out defI) || defI <= 0) defI = 5;
            if (!int.TryParse(DefaultRelayBox.Text.Trim(), out defR) || defR <= 0) defR = 8;

            _config.IndividualMinutesPerHeat = newIndi;
            _config.RelayMinutesPerHeat = newRelay;
            _config.InterEventGapMinutes = gap;
            _config.DefaultIndividualMinutes = defI;
            _config.DefaultRelayMinutes = defR;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }

        public class DurRow
        {
            public int Distance { get; set; }
            public int Minutes { get; set; }
        }
    }
}
