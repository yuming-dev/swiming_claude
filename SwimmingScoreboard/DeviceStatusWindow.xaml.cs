using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace SwimmingScoreboard
{
    public partial class DeviceStatusWindow : Window
    {
        private List<LaneDeviceState> _states;
        private ObservableCollection<DeviceRow> _rows;

        // 2026-06-20 升级为 4 态 (安装/未安装/好/坏). 单端模式下非终点端整列锁定,
        //   防止用户改了又被 ApplyTouchpadInstallModeToLanes 覆盖.
        public DeviceStatusWindow(List<LaneDeviceState> states, PoolConfig pool, LaneCloseSettings lcs) {
            InitializeComponent();
            _states = states;
            bool dualEnd = pool != null && pool.HasRightStartBlock;
            bool finishIsLeft = lcs == null || lcs.FinishPosition != "right";
            bool lockLeft  = !dualEnd && !finishIsLeft;   // 单端 + 终点在右 → 左整列锁
            bool lockRight = !dualEnd && finishIsLeft;    // 单端 + 终点在左 → 右整列锁

            _rows = new ObservableCollection<DeviceRow>();
            foreach (var s in states) _rows.Add(new DeviceRow(s, lockLeft, lockRight));
            DeviceGrid.ItemsSource = _rows;
        }

        private void AllInstalled_Click(object sender, RoutedEventArgs e) {
            foreach (var r in _rows) r.SetAllInstalled();
            DeviceGrid.Items.Refresh();
        }
        private void AllNotInstalled_Click(object sender, RoutedEventArgs e) {
            foreach (var r in _rows) r.SetAllNotInstalled();
            DeviceGrid.Items.Refresh();
        }
        private void AllGood_Click(object sender, RoutedEventArgs e) {
            foreach (var r in _rows) r.SetAllGood();
            DeviceGrid.Items.Refresh();
        }
        private void AllBad_Click(object sender, RoutedEventArgs e) {
            foreach (var r in _rows) r.SetAllBad();
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

    public class DeviceRow : INotifyPropertyChanged
    {
        private LaneDeviceState _s;
        public DeviceRow(LaneDeviceState s, bool lockLeft, bool lockRight) {
            _s = s;
            LeftEnabled = !lockLeft;
            RightEnabled = !lockRight;
        }
        public int Lane { get { return _s.Lane; } }
        public bool LeftEnabled { get; private set; }
        public bool RightEnabled { get; private set; }

        private static string GetText(bool ni, bool br) {
            if (ni) return "未安装";
            return br ? "坏" : "好";
        }
        private static void ApplyText(string txt, Action<bool> setNI, Action<bool> setBr) {
            if (txt == "未安装") { setNI(true); setBr(false); }
            else if (txt == "坏") { setNI(false); setBr(true); }
            else { setNI(false); setBr(false); }  // "好" 或 "安装" 都视为已安装好
        }

        public string LeftTouchpad {
            get { return GetText(_s.LeftTouchpadNotInstalled, _s.LeftTouchpadBroken); }
            set { if (!LeftEnabled) { OnP("LeftTouchpad"); return; }
                  ApplyText(value, b => _s.LeftTouchpadNotInstalled = b, b => _s.LeftTouchpadBroken = b);
                  OnP("LeftTouchpad"); }
        }
        public string LeftStartBlock {
            get { return GetText(_s.LeftStartBlockNotInstalled, _s.LeftStartBlockBroken); }
            set { if (!LeftEnabled) { OnP("LeftStartBlock"); return; }
                  ApplyText(value, b => _s.LeftStartBlockNotInstalled = b, b => _s.LeftStartBlockBroken = b);
                  OnP("LeftStartBlock"); }
        }
        public string LeftBlindWatch1 {
            get { return GetText(_s.LeftBlindWatch1NotInstalled, _s.LeftBlindWatch1Broken); }
            set { if (!LeftEnabled) { OnP("LeftBlindWatch1"); return; }
                  ApplyText(value, b => _s.LeftBlindWatch1NotInstalled = b, b => _s.LeftBlindWatch1Broken = b);
                  OnP("LeftBlindWatch1"); }
        }
        public string LeftBlindWatch2 {
            get { return GetText(_s.LeftBlindWatch2NotInstalled, _s.LeftBlindWatch2Broken); }
            set { if (!LeftEnabled) { OnP("LeftBlindWatch2"); return; }
                  ApplyText(value, b => _s.LeftBlindWatch2NotInstalled = b, b => _s.LeftBlindWatch2Broken = b);
                  OnP("LeftBlindWatch2"); }
        }
        public string LeftBlindWatch3 {
            get { return GetText(_s.LeftBlindWatch3NotInstalled, _s.LeftBlindWatch3Broken); }
            set { if (!LeftEnabled) { OnP("LeftBlindWatch3"); return; }
                  ApplyText(value, b => _s.LeftBlindWatch3NotInstalled = b, b => _s.LeftBlindWatch3Broken = b);
                  OnP("LeftBlindWatch3"); }
        }
        public string RightTouchpad {
            get { return GetText(_s.RightTouchpadNotInstalled, _s.RightTouchpadBroken); }
            set { if (!RightEnabled) { OnP("RightTouchpad"); return; }
                  ApplyText(value, b => _s.RightTouchpadNotInstalled = b, b => _s.RightTouchpadBroken = b);
                  OnP("RightTouchpad"); }
        }
        public string RightStartBlock {
            get { return GetText(_s.RightStartBlockNotInstalled, _s.RightStartBlockBroken); }
            set { if (!RightEnabled) { OnP("RightStartBlock"); return; }
                  ApplyText(value, b => _s.RightStartBlockNotInstalled = b, b => _s.RightStartBlockBroken = b);
                  OnP("RightStartBlock"); }
        }
        public string RightBlindWatch1 {
            get { return GetText(_s.RightBlindWatch1NotInstalled, _s.RightBlindWatch1Broken); }
            set { if (!RightEnabled) { OnP("RightBlindWatch1"); return; }
                  ApplyText(value, b => _s.RightBlindWatch1NotInstalled = b, b => _s.RightBlindWatch1Broken = b);
                  OnP("RightBlindWatch1"); }
        }
        public string RightBlindWatch2 {
            get { return GetText(_s.RightBlindWatch2NotInstalled, _s.RightBlindWatch2Broken); }
            set { if (!RightEnabled) { OnP("RightBlindWatch2"); return; }
                  ApplyText(value, b => _s.RightBlindWatch2NotInstalled = b, b => _s.RightBlindWatch2Broken = b);
                  OnP("RightBlindWatch2"); }
        }
        public string RightBlindWatch3 {
            get { return GetText(_s.RightBlindWatch3NotInstalled, _s.RightBlindWatch3Broken); }
            set { if (!RightEnabled) { OnP("RightBlindWatch3"); return; }
                  ApplyText(value, b => _s.RightBlindWatch3NotInstalled = b, b => _s.RightBlindWatch3Broken = b);
                  OnP("RightBlindWatch3"); }
        }

        public void SetAllInstalled() {
            if (LeftEnabled) {
                _s.LeftTouchpadNotInstalled = false;
                _s.LeftStartBlockNotInstalled = false;
                _s.LeftBlindWatch1NotInstalled = false;
                _s.LeftBlindWatch2NotInstalled = false;
                _s.LeftBlindWatch3NotInstalled = false;
            }
            if (RightEnabled) {
                _s.RightTouchpadNotInstalled = false;
                _s.RightStartBlockNotInstalled = false;
                _s.RightBlindWatch1NotInstalled = false;
                _s.RightBlindWatch2NotInstalled = false;
                _s.RightBlindWatch3NotInstalled = false;
            }
            NotifyAll();
        }
        public void SetAllNotInstalled() {
            if (LeftEnabled) {
                _s.LeftTouchpadNotInstalled = true; _s.LeftTouchpadBroken = false;
                _s.LeftStartBlockNotInstalled = true; _s.LeftStartBlockBroken = false;
                _s.LeftBlindWatch1NotInstalled = true; _s.LeftBlindWatch1Broken = false;
                _s.LeftBlindWatch2NotInstalled = true; _s.LeftBlindWatch2Broken = false;
                _s.LeftBlindWatch3NotInstalled = true; _s.LeftBlindWatch3Broken = false;
            }
            if (RightEnabled) {
                _s.RightTouchpadNotInstalled = true; _s.RightTouchpadBroken = false;
                _s.RightStartBlockNotInstalled = true; _s.RightStartBlockBroken = false;
                _s.RightBlindWatch1NotInstalled = true; _s.RightBlindWatch1Broken = false;
                _s.RightBlindWatch2NotInstalled = true; _s.RightBlindWatch2Broken = false;
                _s.RightBlindWatch3NotInstalled = true; _s.RightBlindWatch3Broken = false;
            }
            NotifyAll();
        }
        public void SetAllGood() {
            // 只对"已安装"设备清坏标; 未安装不动 (= 未安装时不能选好/坏)
            if (LeftEnabled) {
                if (!_s.LeftTouchpadNotInstalled) _s.LeftTouchpadBroken = false;
                if (!_s.LeftStartBlockNotInstalled) _s.LeftStartBlockBroken = false;
                if (!_s.LeftBlindWatch1NotInstalled) _s.LeftBlindWatch1Broken = false;
                if (!_s.LeftBlindWatch2NotInstalled) _s.LeftBlindWatch2Broken = false;
                if (!_s.LeftBlindWatch3NotInstalled) _s.LeftBlindWatch3Broken = false;
            }
            if (RightEnabled) {
                if (!_s.RightTouchpadNotInstalled) _s.RightTouchpadBroken = false;
                if (!_s.RightStartBlockNotInstalled) _s.RightStartBlockBroken = false;
                if (!_s.RightBlindWatch1NotInstalled) _s.RightBlindWatch1Broken = false;
                if (!_s.RightBlindWatch2NotInstalled) _s.RightBlindWatch2Broken = false;
                if (!_s.RightBlindWatch3NotInstalled) _s.RightBlindWatch3Broken = false;
            }
            NotifyAll();
        }
        public void SetAllBad() {
            if (LeftEnabled) {
                if (!_s.LeftTouchpadNotInstalled) _s.LeftTouchpadBroken = true;
                if (!_s.LeftStartBlockNotInstalled) _s.LeftStartBlockBroken = true;
                if (!_s.LeftBlindWatch1NotInstalled) _s.LeftBlindWatch1Broken = true;
                if (!_s.LeftBlindWatch2NotInstalled) _s.LeftBlindWatch2Broken = true;
                if (!_s.LeftBlindWatch3NotInstalled) _s.LeftBlindWatch3Broken = true;
            }
            if (RightEnabled) {
                if (!_s.RightTouchpadNotInstalled) _s.RightTouchpadBroken = true;
                if (!_s.RightStartBlockNotInstalled) _s.RightStartBlockBroken = true;
                if (!_s.RightBlindWatch1NotInstalled) _s.RightBlindWatch1Broken = true;
                if (!_s.RightBlindWatch2NotInstalled) _s.RightBlindWatch2Broken = true;
                if (!_s.RightBlindWatch3NotInstalled) _s.RightBlindWatch3Broken = true;
            }
            NotifyAll();
        }

        private void NotifyAll() {
            OnP("LeftTouchpad"); OnP("LeftStartBlock");
            OnP("LeftBlindWatch1"); OnP("LeftBlindWatch2"); OnP("LeftBlindWatch3");
            OnP("RightTouchpad"); OnP("RightStartBlock");
            OnP("RightBlindWatch1"); OnP("RightBlindWatch2"); OnP("RightBlindWatch3");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnP(string n) {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(n));
        }
    }
}
