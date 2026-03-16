using System.Collections.Generic;
using System.Windows;

namespace SwimmingScoreboard
{
    public partial class DeviceStatusWindow : Window
    {
        private List<LaneDeviceState> _states;

        public DeviceStatusWindow(List<LaneDeviceState> states, PoolConfig pool) {
            InitializeComponent();
            _states = states;
            DeviceGrid.ItemsSource = _states;
        }

        private void AllGood_Click(object sender, RoutedEventArgs e) {
            foreach (var s in _states) {
                s.LeftTouchpadBroken = false;
                s.LeftStartBlockBroken = false;
                s.LeftBlindWatchBroken = false;
                s.RightTouchpadBroken = false;
                s.RightStartBlockBroken = false;
                s.RightBlindWatchBroken = false;
            }
            DeviceGrid.Items.Refresh();
        }

        private void AllBad_Click(object sender, RoutedEventArgs e) {
            foreach (var s in _states) {
                s.LeftTouchpadBroken = true;
                s.LeftStartBlockBroken = true;
                s.LeftBlindWatchBroken = true;
                s.RightTouchpadBroken = true;
                s.RightStartBlockBroken = true;
                s.RightBlindWatchBroken = true;
            }
            DeviceGrid.Items.Refresh();
        }

        private void OK_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }
    }
}
