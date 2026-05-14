using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RemoteTimingControl
{
    public partial class MainWindow : Window
    {
        private SimpleWebSocketClient _ws;
        private JObject _data;
        private int _selectedLane = -1;
        private string _startPosition = "left";
        private string _finishPosition = "left";
        private double _firstPlaceHoldTime = 3;
        private string _serverHost = "127.0.0.1";
        private int _serverPort = 3002;
        private int _connFailCount = 0;
        private DispatcherTimer _reconnectTimer;

        // 计时硬件直连
        private TimingBridgeLocal _localBridge;
        private string _hwMode = "none";       // "none" / "serial" / "udp"
        private string _hwSerialPort = "COM3";
        private string _hwUdpHost = "127.0.0.1";
        private int _hwUdpSendPort = 5001;
        private int _hwUdpRecvPort = 5002;

        // 保存的计时参数（本地缓存，用于设置对话框默认值和持久化）
        private double _laneCloseTime = 20;
        private double _startBlockCloseDelay = 3;
        private double _resultConfirmCloseDelay = 3;
        private double _falseStartThreshold = 0.10;
        private double _splitDisplayTime = 5;
        private int _leftBlindWatchCount = 3;
        private int _rightBlindWatchCount = 3;
        private double _bigDisplayPageInterval = 5;
        private bool _reactionTimeEnabled = true; // 反应时(RT)开关：关闭时所有出发反应时相关处理跳过
        private string _laneOrder = "forward";    // 道次顺序: "forward" 0→9（顶到底）；"reverse" 9→0

        // Local timer for smooth running time
        private DateTime _localTimerStart = DateTime.MinValue;
        private bool _localTimerSynced = false;
        private DispatcherTimer _refreshTimer;

        // Split display tracking per lane
        private Dictionary<int, SplitState> _laneSplitState = new Dictionary<int, SplitState>();

        // First place hold display
        private string _firstPlaceFinishTime = "";
        private DateTime _firstPlaceShowStart = DateTime.MinValue;
        private int _firstPlaceDetectedRank = 0;

        // Schedule tree collapse state
        private Dictionary<string, bool> _treeCollapsed = new Dictionary<string, bool>();
        private string _selectedHeatKey = "";
        private string _lastScheduleHash = "";

        public MainWindow()
        {
            InitializeComponent();

            _refreshTimer = new DispatcherTimer();
            // 2026-05-13 把 500ms 改 100ms：匹配硬件 0.1s 的滚动时间节拍，
            // 否则计时窗每秒只刷 2 次，肉眼能感觉到"跳跃"
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(100);

            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(3);
            _reconnectTimer.Tick += delegate { TryReconnect(); };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // 加载保存的参数
            LoadSettings();
            ServerBox.Text = _serverHost + ":" + _serverPort;

            // 窗口加载完成后自动连接服务器 + 硬件
            Loaded += delegate {
                DoConnect();
                if (_hwMode != "none") ConnectHardware(_hwMode, _hwSerialPort, _hwUdpHost, _hwUdpSendPort, _hwUdpRecvPort);
            };

            // 退出时把当前参数设置再落盘一次，确保下次启动可正确初始化
            Closing += delegate { try { SaveSettings(); } catch { } };
        }

        // ═══════ 暗色 ComboBox 模板（WPF 4.0 Aero 主题的 ComboBox 闭合态由主题 ButtonChrome 控制，
        //   忽略 Background 属性。只有完整替换 ControlTemplate 才能实现深色背景） ═══════
        private static ControlTemplate BuildDarkComboTemplate() {
            const string xaml =
"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
"                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>" +
"  <Grid>" +
"    <Grid.ColumnDefinitions>" +
"      <ColumnDefinition Width='*'/>" +
"      <ColumnDefinition Width='20'/>" +
"    </Grid.ColumnDefinitions>" +
"    <Border Grid.ColumnSpan='2' Background='{TemplateBinding Background}'" +
"            BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'/>" +
"    <ContentPresenter Grid.Column='0' Margin='6,2,2,2'" +
"                      Content='{TemplateBinding SelectionBoxItem}'" +
"                      ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'" +
"                      IsHitTestVisible='False' VerticalAlignment='Center'" +
"                      TextBlock.Foreground='{TemplateBinding Foreground}'/>" +
"    <Path Grid.Column='1' Fill='{TemplateBinding Foreground}'" +
"          Data='M 0 0 L 4 4 L 8 0 Z'" +
"          HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'/>" +
"    <ToggleButton Grid.ColumnSpan='2' Background='Transparent' BorderThickness='0' Focusable='False'" +
"                  IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>" +
"      <ToggleButton.Template>" +
"        <ControlTemplate TargetType='ToggleButton'>" +
"          <Border Background='Transparent'/>" +
"        </ControlTemplate>" +
"      </ToggleButton.Template>" +
"    </ToggleButton>" +
"    <Popup Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}'" +
"           Focusable='False' AllowsTransparency='True' PopupAnimation='Slide'>" +
"      <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'" +
"              BorderThickness='1' MinWidth='{TemplateBinding ActualWidth}' MaxHeight='240'>" +
"        <ScrollViewer>" +
"          <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Contained'/>" +
"        </ScrollViewer>" +
"      </Border>" +
"    </Popup>" +
"  </Grid>" +
"</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        private static Style BuildDarkComboItemStyle(Brush bg, Brush hoverBg) {
            var s = new Style(typeof(ComboBoxItem));
            s.Setters.Add(new Setter(Control.BackgroundProperty, bg));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            // 覆写 ComboBoxItem 模板：否则 Aero 主题下选中/悬停色仍然是系统色
            string itemXaml =
"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
"                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBoxItem'>" +
"  <Border x:Name='Bd' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>" +
"    <ContentPresenter TextBlock.Foreground='{TemplateBinding Foreground}'/>" +
"  </Border>" +
"  <ControlTemplate.Triggers>" +
"    <Trigger Property='IsHighlighted' Value='True'>" +
"      <Setter TargetName='Bd' Property='Background' Value='#374151'/>" +
"    </Trigger>" +
"  </ControlTemplate.Triggers>" +
"</ControlTemplate>";
            s.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(itemXaml)));
            return s;
        }

        // ═══════ 网络地址持久化（参数全部从服务器获取） ═══════
        private string GetSettingsPath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteTimingServer.txt");
        }

        private string GetHwSettingsPath() {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteTimingHw.json");
        }

        private string GetLaneCloseSettingsPath() {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote_lane_close_settings.json");
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsPath();
                if (System.IO.File.Exists(path)) {
                    string addr = System.IO.File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(addr)) {
                        string[] parts = addr.Split(':');
                        _serverHost = parts[0];
                        if (parts.Length > 1) int.TryParse(parts[1], out _serverPort);
                    }
                }
            }
            catch { }
            try
            {
                string hwPath = GetHwSettingsPath();
                if (System.IO.File.Exists(hwPath)) {
                    var j = JObject.Parse(System.IO.File.ReadAllText(hwPath, Encoding.UTF8));
                    if (j["mode"] != null) _hwMode = j["mode"].ToString();
                    if (j["serialPort"] != null) _hwSerialPort = j["serialPort"].ToString();
                    if (j["udpHost"] != null) _hwUdpHost = j["udpHost"].ToString();
                    if (j["udpSendPort"] != null) _hwUdpSendPort = (int)j["udpSendPort"];
                    if (j["udpRecvPort"] != null) _hwUdpRecvPort = (int)j["udpRecvPort"];
                }
            }
            catch { }
            // 计时"参数设置"本地缓存：启动时先用上次的值初始化（连上服务器后会被服务器值覆盖）
            try
            {
                string lcsPath = GetLaneCloseSettingsPath();
                if (System.IO.File.Exists(lcsPath)) {
                    var j = JObject.Parse(System.IO.File.ReadAllText(lcsPath, Encoding.UTF8));
                    if (j["laneCloseTime"] != null) _laneCloseTime = (double)j["laneCloseTime"];
                    if (j["startBlockCloseDelay"] != null) _startBlockCloseDelay = (double)j["startBlockCloseDelay"];
                    if (j["resultConfirmCloseDelay"] != null) _resultConfirmCloseDelay = (double)j["resultConfirmCloseDelay"];
                    if (j["falseStartThreshold"] != null) _falseStartThreshold = (double)j["falseStartThreshold"];
                    if (j["splitDisplayTime"] != null) _splitDisplayTime = (double)j["splitDisplayTime"];
                    if (j["firstPlaceHoldTime"] != null) _firstPlaceHoldTime = (double)j["firstPlaceHoldTime"];
                    if (j["startPosition"] != null) _startPosition = j["startPosition"].ToString();
                    if (j["finishPosition"] != null) _finishPosition = j["finishPosition"].ToString();
                    if (j["leftBlindWatchCount"] != null) {
                        int v = (int)j["leftBlindWatchCount"]; if (v >= 1 && v <= 3) _leftBlindWatchCount = v;
                    }
                    if (j["rightBlindWatchCount"] != null) {
                        int v = (int)j["rightBlindWatchCount"]; if (v >= 1 && v <= 3) _rightBlindWatchCount = v;
                    }
                    if (j["bigDisplayPageInterval"] != null) _bigDisplayPageInterval = (double)j["bigDisplayPageInterval"];
                    if (j["reactionTimeEnabled"] != null) _reactionTimeEnabled = (bool)j["reactionTimeEnabled"];
                    if (j["laneOrder"] != null) {
                        string lv = j["laneOrder"].ToString();
                        _laneOrder = (lv == "reverse") ? "reverse" : "forward";
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                System.IO.File.WriteAllText(GetSettingsPath(), _serverHost + ":" + _serverPort, Encoding.UTF8);
            }
            catch { }
            try
            {
                var hw = new JObject();
                hw["mode"] = _hwMode;
                hw["serialPort"] = _hwSerialPort;
                hw["udpHost"] = _hwUdpHost;
                hw["udpSendPort"] = _hwUdpSendPort;
                hw["udpRecvPort"] = _hwUdpRecvPort;
                System.IO.File.WriteAllText(GetHwSettingsPath(), hw.ToString(), Encoding.UTF8);
            }
            catch { }
            try
            {
                var lcs = new JObject();
                lcs["laneCloseTime"] = _laneCloseTime;
                lcs["startBlockCloseDelay"] = _startBlockCloseDelay;
                lcs["resultConfirmCloseDelay"] = _resultConfirmCloseDelay;
                lcs["falseStartThreshold"] = _falseStartThreshold;
                lcs["splitDisplayTime"] = _splitDisplayTime;
                lcs["firstPlaceHoldTime"] = _firstPlaceHoldTime;
                lcs["startPosition"] = _startPosition;
                lcs["finishPosition"] = _finishPosition;
                lcs["leftBlindWatchCount"] = _leftBlindWatchCount;
                lcs["rightBlindWatchCount"] = _rightBlindWatchCount;
                lcs["bigDisplayPageInterval"] = _bigDisplayPageInterval;
                lcs["reactionTimeEnabled"] = _reactionTimeEnabled;
                lcs["laneOrder"] = _laneOrder;
                System.IO.File.WriteAllText(GetLaneCloseSettingsPath(), lcs.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private class SplitState
        {
            public int SplitCount;
            public DateTime ShowTime;
            public string LastReaction;
            public DateTime ReactionShowTime;
        }

        // ═══════ Local timer ═══════
        private double ParseServerTime(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;
            string[] parts = str.Split(':');
            if (parts.Length == 3)
                return double.Parse(parts[0]) * 3600 + double.Parse(parts[1]) * 60 + double.Parse(parts[2]);
            if (parts.Length == 2)
                return double.Parse(parts[0]) * 60 + double.Parse(parts[1]);
            double val;
            if (double.TryParse(parts[0], out val)) return val;
            return 0;
        }

        private string FormatLocalTime(double total)
        {
            if (total < 0) total = 0;
            int tenths = ((int)(total * 10)) % 10;
            int secs = ((int)total) % 60;
            int mins = ((int)(total / 60)) % 60;
            int hrs = (int)(total / 3600);
            if (hrs > 0) return string.Format("{0}:{1:D2}:{2:D2}.{3} ", hrs, mins, secs, tenths);
            if (mins > 0) return string.Format("{0}:{1:D2}.{2} ", mins, secs, tenths);
            return string.Format("{0}.{1} ", secs, tenths);
        }

        private string GetLocalRunningTime()
        {
            if (_localTimerStart == DateTime.MinValue) return _data != null && _data["runningTime"] != null ? _data["runningTime"].ToString() : "0.00";
            double elapsed = (DateTime.Now - _localTimerStart).TotalSeconds;
            return FormatLocalTime(elapsed);
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_data == null) return;
            string st = _data["raceState"] != null ? _data["raceState"].ToString().ToLower() : "waiting";
            // 2026-05-13 测试模式也要走 100ms 刷新：让 EXE 里的滚动时间 / 测试事件文字跟硬件节拍同步
            bool isTestMode = _data["testMode"] != null && (bool)_data["testMode"];
            if (st == "racing" || st == "finished" || isTestMode)
            {
                RenderLanes(_data["swimmers"] as JArray);
                UpdateRunningTimeDisplay();
            }
        }

        private void UpdateRunningTimeDisplay()
        {
            // 第1名成绩：直接读取服务器数据（由服务器端 ProcessTouchpadHit 设置）
            bool fpActive = _data != null && _data["firstPlaceActive"] != null && (bool)_data["firstPlaceActive"];
            string fpTime = _data != null && _data["firstPlaceFinishTime"] != null ? _data["firstPlaceFinishTime"].ToString() : "";
            if (fpActive && !string.IsNullOrEmpty(fpTime))
            {
                RunningTime.Text = fpTime;
                RunningTime.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
            }
            else
            {
                RunningTime.Text = GetLocalRunningTime();
                RunningTime.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
        }

        // ═══════ 连接管理 ═══════
        // 导航栏 系统硬件 — 连接串口 / 断开（让服务器执行）
        private void NavQuickConnSerial_Click(object sender, RoutedEventArgs e)
        {
            SendCmd("QUICK_CONNECT_SERIAL", null);
            AddLog("已请求服务器连接/断开串口");
        }

        // 导航栏 系统硬件 — 设备测试切换
        private void NavDeviceTest_Click(object sender, RoutedEventArgs e)
        {
            bool inTest = (_data != null && _data["testMode"] != null && (bool)_data["testMode"]);
            if (!inTest) {
                var r = MessageBox.Show("确认进入设备测试模式？\n\n所有触板/出发台/盲表都将打开，硬件来什么数据都直接显示但不计入比赛成绩。\n再点同一按钮退出。",
                    "设备测试", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            SendCmd("DEVICE_TEST_TOGGLE", null);
            AddLog(inTest ? "已请求退出设备测试" : "已请求进入设备测试");
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_ws != null && _ws.IsConnected)
            {
                _reconnectTimer.Stop();
                _ws.Close();
                UpdateConnStatus(false);
                ConnBtn.Content = "连接";
                _connFailCount = 0;
                return;
            }

            string addr = ServerBox.Text.Trim();
            string[] parts = addr.Split(':');
            _serverHost = parts[0];
            _serverPort = 3002;
            if (parts.Length > 1) int.TryParse(parts[1], out _serverPort);

            DoConnect();
        }

        private void DoConnect()
        {
            try
            {
                _ws = new SimpleWebSocketClient();
                _ws.OnMessage += OnServerMessage;
                _ws.OnDisconnected += delegate()
                {
                    Dispatcher.Invoke((Action)delegate()
                    {
                        UpdateConnStatus(false);
                        ConnBtn.Content = "连接";
                        AddLog("服务器断开");
                        // 自动重连
                        _connFailCount++;
                        if (_connFailCount >= 3)
                        {
                            _reconnectTimer.Stop();
                            _connFailCount = 0;
                            if (MessageBox.Show("连接失败，是否重新设置服务器地址？", "连接失败", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                            {
                                OpenSettings_Click(null, null);
                            }
                        }
                        else
                        {
                            _reconnectTimer.Start();
                        }
                    });
                };
                _ws.Connect(_serverHost, _serverPort);
                _ws.Send(JsonConvert.SerializeObject(new { type = "TIMING_EXE_IDENTITY" }));
                UpdateConnStatus(true);
                ConnBtn.Content = "断开";
                _connFailCount = 0;
                _reconnectTimer.Stop();
                SaveSettings();
                AddLog("已连接: " + _serverHost + ":" + _serverPort);
            }
            catch (Exception ex)
            {
                AddLog("连接失败: " + ex.Message);
                _connFailCount++;
                if (_connFailCount >= 3)
                {
                    _connFailCount = 0;
                    _reconnectTimer.Stop();
                }
                else
                {
                    _reconnectTimer.Start();
                }
            }
        }

        private void TryReconnect()
        {
            _reconnectTimer.Stop();
            if (_ws != null && _ws.IsConnected) return;
            AddLog("尝试重连...");
            DoConnect();
        }

        private DateTime _lastRenderTime = DateTime.MinValue;
        private bool _renderPending = false;

        private void OnServerMessage(string json)
        {
            Dispatcher.BeginInvoke((Action)delegate()
            {
                try
                {
                    var msg = JObject.Parse(json);
                    string msgType = msg["type"] != null ? msg["type"].ToString() : "";

                    if (msgType == "STAGE_COMPLETE")
                    {
                        HandleStageComplete(msg);
                        return;
                    }
                    if (msgType == "PROMOTION_DONE")
                    {
                        AddLog(string.Format("晋级完成：{0} {1} → {2}（{3}人，{4}组）",
                            msg["gender"], msg["eventName"], msg["nextStage"],
                            msg["promotedCount"], msg["heatCount"]));
                        return;
                    }
                    // 硬件 0.1s 节拍的轻量滚动时间帧 — 仅更新现有 _data 中的 runningTime，
                    // 不替换整个 _data，也不触发完整 RenderAll；专门的时间显示由现有同步逻辑刷新
                    if (msgType == "RUNNING_TIME_UPDATE" && _data != null)
                    {
                        var rtMsg = msg["data"] as JObject;
                        if (rtMsg != null && rtMsg["runningTime"] != null)
                            _data["runningTime"] = rtMsg["runningTime"].ToString();
                        return;
                    }

                    _data = msg["data"] as JObject;
                    if (_data != null)
                    {
                        // 节流渲染：至少间隔200ms，避免频繁重建UI导致按钮无响应
                        double elapsed = (DateTime.Now - _lastRenderTime).TotalMilliseconds;
                        if (elapsed >= 200)
                        {
                            _lastRenderTime = DateTime.Now;
                            _renderPending = false;
                            RenderAll();
                        }
                        else if (!_renderPending)
                        {
                            _renderPending = true;
                            var delayTimer = new DispatcherTimer();
                            delayTimer.Interval = TimeSpan.FromMilliseconds(200 - elapsed);
                            delayTimer.Tick += delegate {
                                delayTimer.Stop();
                                _renderPending = false;
                                _lastRenderTime = DateTime.Now;
                                if (_data != null) RenderAll();
                            };
                            delayTimer.Start();
                        }
                    }
                }
                catch { }
            });
        }

        private void HandleStageComplete(JObject msg)
        {
            string g = msg["gender"] != null ? msg["gender"].ToString() : "";
            string ev = msg["eventName"] != null ? msg["eventName"].ToString() : "";
            string from = msg["fromStage"] != null ? msg["fromStage"].ToString() : "";
            string next = msg["nextStage"] != null ? msg["nextStage"].ToString() : "";
            int total = msg["totalSwimmers"] != null ? (int)msg["totalSwimmers"] : 0;
            int promo = msg["promoCount"] != null ? (int)msg["promoCount"] : 0;

            string question = string.Format("{0} {1} {2} 全部{3}人已完赛！\n\n是否自动晋级前{4}名到{5}？\n（按成绩总排名）",
                g, ev, from, total, promo, next);
            MessageBoxResult answer = MessageBox.Show(question, "赛次完赛", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                SendCmd("EXECUTE_PROMOTION", new { gender = g, eventName = ev, fromStage = from, nextStage = next, promoCount = promo });
                AddLog("已确认晋级：" + g + " " + ev + " → " + next);
            }
            else
            {
                AddLog("已取消自动晋级");
            }
        }

        private void UpdateConnStatus(bool connected)
        {
            WsConn.Fill = new SolidColorBrush(connected ? Colors.LimeGreen : Colors.Red);
            WsConnText.Text = "服务器: " + (connected ? "已连接" : "未连接");
        }

        // ═══════ 发送命令 ═══════
        // ═══════ 计时硬件直连 ═══════
        private void ConnectHardware(string mode, string serialPort, string udpHost, int udpSend, int udpRecv) {
            if (_localBridge != null) { _localBridge.Dispose(); _localBridge = null; }
            if (mode == "none") { UpdateHwStatus(false, "未连接"); return; }

            _localBridge = new TimingBridgeLocal();
            _localBridge.OnStatusChanged += status => Dispatcher.Invoke((Action)(() => UpdateHwStatus(_localBridge.IsConnected, status)));
            _localBridge.OnLog += msg => Dispatcher.Invoke((Action)(() => AddLog(msg)));
            _localBridge.OnTimingData += (lane, cmdType, time) => Dispatcher.Invoke((Action)(() => HandleHwTimingData(lane, cmdType, time)));

            if (mode == "serial") _localBridge.ConnectSerial(serialPort);
            else if (mode == "udp") _localBridge.ConnectUdp(udpHost, udpSend, udpRecv);

            UpdateHwStatus(_localBridge.IsConnected, _localBridge.StatusText);
        }

        private void HandleHwTimingData(int lane, string cmdType, double time) {
            switch (cmdType) {
                case "TimerReady":   SendCmd("READY"); AddLog("[硬件触发] 就位"); return;
                case "StartCommand": SendCmd("START_RACE"); AddLog("[硬件触发] 发令"); return;
                case "TimerReset":   SendCmd("TIMER_RESET"); AddLog("[硬件触发] 计时复位"); return;
                case "TestCommand":  AddLog("[硬件] 测试命令"); return;
                case "RunningTime":  return; // 滚动时间：由本地计时器显示，硬件数据忽略
                case "PoolConfig":
                case "RaceConfig":
                case "SetCommand":   AddLog("[硬件] 配置命令 " + cmdType); return;
            }
            // 计时数据 → 转发给主服务器
            SendCmd("TIMING_DATA", new { lane = lane, commandType = cmdType, time = time });
        }

        private void UpdateHwStatus(bool connected, string text) {
            HwConn.Fill = new SolidColorBrush(connected
                ? (Color)ColorConverter.ConvertFromString("#22C55E")
                : (Color)ColorConverter.ConvertFromString("#EF4444"));
            HwConnText.Text = connected ? "硬件: " + text : "硬件";
        }

        private void SendCmd(string cmd, object data)
        {
            if (_ws == null || !_ws.IsConnected)
            {
                AddLog("命令未发送(未连接): " + cmd);
                return;
            }
            try
            {
                _ws.Send(JsonConvert.SerializeObject(new { type = "TIMING_CMD", command = cmd, data = data }));
            }
            catch (Exception ex)
            {
                AddLog("发送失败: " + cmd + " - " + ex.Message);
            }
        }

        private void SendCmd(string cmd)
        {
            SendCmd(cmd, null);
        }

        private void SendDisplay(string mode)
        {
            if (_ws == null || !_ws.IsConnected)
            {
                AddLog("命令未发送(未连接): " + mode);
                return;
            }
            try
            {
                _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = mode }));
            }
            catch (Exception ex)
            {
                AddLog("发送失败: " + mode + " - " + ex.Message);
            }
        }

        // ═══════ 渲染 ═══════
        private void RenderAll()
        {
            if (_data == null) return;

            string state = _data["raceState"] != null ? _data["raceState"].ToString().ToLower() : "waiting";

            // Start refresh timer for racing/finished states
            if (state == "racing" || state == "finished")
            {
                if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
            }

            // Reset split tracking on waiting
            if (state == "waiting")
            {
                _laneSplitState.Clear();
            }

            // 状态徽章：等待蓝 / 就位黄 / 比赛中红 / 已完赛灰；与主服务器一致
            // 设备测试模式覆盖所有比赛状态，显示红底"设备测试"
            bool inTestMode = _data["testMode"] != null && (bool)_data["testMode"];
            // 导航栏 设备测试 / 连接串口 按钮文字+底色随服务器状态走
            if (NavDeviceTestBtn != null) {
                NavDeviceTestBtn.Content = inTestMode ? "退出测试" : "设备测试";
                NavDeviceTestBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(inTestMode ? "#EF4444" : "#0EA5E9"));
            }
            bool hwConnected = _data["timingHwConnected"] != null && (bool)_data["timingHwConnected"];
            if (NavQuickConnBtn != null) {
                NavQuickConnBtn.Content = hwConnected ? "断开串口" : "连接串口";
                NavQuickConnBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hwConnected ? "#EF4444" : "#22C55E"));
            }
            string stateText;
            string bgHex, fgHex;
            if (inTestMode) {
                stateText = "设备测试"; bgHex = "#EF4444"; fgHex = "#FFFFFF";
            } else switch (state)
            {
                case "ready":    stateText = "就位";   bgHex = "#F59E0B"; fgHex = "#000000"; break;
                case "racing":   stateText = "比赛中"; bgHex = "#EF4444"; fgHex = "#FFFFFF"; break;
                case "finished": stateText = "已完赛"; bgHex = "#475569"; fgHex = "#FFFFFF"; break;
                default:         stateText = "等待";   bgHex = "#3B82F6"; fgHex = "#FFFFFF"; break;
            }
            var bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
            var fgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
            if (StateBox != null) StateBox.Background = bgBrush;
            StateLabel.Foreground = fgBrush;

            bool resultConfirmed = _data["resultConfirmed"] != null && (bool)_data["resultConfirmed"];
            if (resultConfirmed) stateText = "已确认 — 请选择下一组";
            StateLabel.Text = stateText;

            // Local timer sync：
            // 服务器（主控制）是唯一权威时钟源，由硬件START事件驱动。
            // 客户端每次接收服务器广播都重新同步 _localTimerStart，确保本地时钟不会漂移。
            if (state == "waiting" || state == "ready")
            {
                _localTimerStart = DateTime.MinValue;
                _localTimerSynced = false;
            }
            if ((state == "racing" || state == "finished") && _data["runningTime"] != null)
            {
                double serverSec = ParseServerTime(_data["runningTime"].ToString());
                // 每次服务器广播都重新同步本地起点（消除时钟漂移）
                _localTimerStart = DateTime.Now.AddSeconds(-serverSec);
                _localTimerSynced = true;
            }

            // First place detection
            if (state == "waiting")
            {
                _firstPlaceDetectedRank = 0;
                _firstPlaceShowStart = DateTime.MinValue;
                _firstPlaceFinishTime = "";
            }

            var swimmers = _data["swimmers"] as JArray;
            UpdateRunningTimeDisplay();

            // 从服务器同步所有参数（服务器是唯一的参数来源）
            if (_data["laneCloseSettings"] != null)
            {
                var lcs = _data["laneCloseSettings"];
                if (lcs["startPosition"] != null) _startPosition = lcs["startPosition"].ToString();
                if (lcs["finishPosition"] != null) _finishPosition = lcs["finishPosition"].ToString();
                if (lcs["laneCloseTime"] != null) _laneCloseTime = (double)lcs["laneCloseTime"];
                if (lcs["startBlockCloseDelay"] != null) _startBlockCloseDelay = (double)lcs["startBlockCloseDelay"];
                if (lcs["resultConfirmCloseDelay"] != null) _resultConfirmCloseDelay = (double)lcs["resultConfirmCloseDelay"];
                if (lcs["falseStartThreshold"] != null) _falseStartThreshold = (double)lcs["falseStartThreshold"];
                if (lcs["splitDisplayTime"] != null) _splitDisplayTime = (double)lcs["splitDisplayTime"];
                if (lcs["firstPlaceHoldTime"] != null) _firstPlaceHoldTime = (double)lcs["firstPlaceHoldTime"];
                if (lcs["leftBlindWatchCount"] != null) {
                    int v = (int)lcs["leftBlindWatchCount"];
                    if (v >= 1 && v <= 3) _leftBlindWatchCount = v;
                }
                if (lcs["rightBlindWatchCount"] != null) {
                    int v = (int)lcs["rightBlindWatchCount"];
                    if (v >= 1 && v <= 3) _rightBlindWatchCount = v;
                }
                if (lcs["bigDisplayPageInterval"] != null) _bigDisplayPageInterval = (double)lcs["bigDisplayPageInterval"];
                if (lcs["reactionTimeEnabled"] != null) _reactionTimeEnabled = (bool)lcs["reactionTimeEnabled"];
                if (lcs["laneOrder"] != null) {
                    string lv2 = lcs["laneOrder"].ToString();
                    _laneOrder = (lv2 == "reverse") ? "reverse" : "forward";
                }
                // 把"主服务器同步过来"的参数也写到本地持久化文件，
                // 这样下次启动（即便服务器尚未连上）也能用最新值初始化
                try { SaveSettings(); } catch { }
            }

            // Control mode
            string controlMode = _data["scoringControlMode"] != null ? _data["scoringControlMode"].ToString() : "-";
            ControlModeText.Text = "控制模式: " + controlMode;

            // False start info
            string fsText = "";
            if (swimmers != null)
            {
                foreach (JObject sw in swimmers)
                {
                    if (sw["isFalseStart"] != null && (bool)sw["isFalseStart"])
                        fsText += string.Format("道{0} FS! ", sw["lane"]);
                    else if (sw["isSuspectFalseStart"] != null && (bool)sw["isSuspectFalseStart"])
                        fsText += string.Format("道{0} 可疑 ", sw["lane"]);
                }
            }
            FalseStartInfo.Text = fsText;

            // Current race info
            // 特例：服务器在最后一组确认+计时器清零后会把 currentHeat 设为 0
            //       并把本赛次所有组的 heatConfirmed 都置为 true。此时 EXE 也展示
            //       "[组别] 性别 项目 阶段 第N组已完赛 / 共M组"，与主控顶部一致。
            string curAgInfo = _data["currentAgeGroup"] != null ? _data["currentAgeGroup"].ToString() : "";
            string curGenderS = _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            string curEventS = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            string curStageS = _data["currentStage"] != null ? _data["currentStage"].ToString() : "";
            int curHeatN = _data["currentHeat"] != null ? (int)_data["currentHeat"] : 0;
            int totalHeatN = _data["totalHeats"] != null ? (int)_data["totalHeats"] : 0;

            bool stageDone = false;
            int doneHeatNum = 0, doneHeatCount = 0;
            if (curHeatN == 0 && !string.IsNullOrEmpty(curEventS)) {
                var sched = _data["schedule"] as JArray;
                if (sched != null) {
                    foreach (JObject it in sched) {
                        string ag = it["ageGroup"] != null ? it["ageGroup"].ToString() : "";
                        string g = it["gender"] != null ? it["gender"].ToString() : "";
                        string ev = it["eventName"] != null ? it["eventName"].ToString() : "";
                        string st = it["stage"] != null ? it["stage"].ToString() : "";
                        if (ag != curAgInfo || g != curGenderS || ev != curEventS || st != curStageS) continue;
                        var hc = it["heatConfirmed"] as JArray;
                        int hCount = it["heatCount"] != null ? (int)it["heatCount"] : 0;
                        if (hc == null || hc.Count == 0 || hCount == 0) break;
                        bool allDone = true;
                        for (int i = 0; i < hc.Count; i++) {
                            if (hc[i] == null || !(bool)hc[i]) { allDone = false; break; }
                        }
                        if (allDone) {
                            stageDone = true;
                            doneHeatNum = hCount;
                            doneHeatCount = hCount;
                        }
                        break;
                    }
                }
            }

            string curInfo;
            if (stageDone) {
                curInfo = string.Format("{0}{1}子 {2} {3} 第{4}组已完赛 / 共{5}组",
                    string.IsNullOrEmpty(curAgInfo) ? "" : ("[" + curAgInfo + "] "),
                    curGenderS, curEventS, curStageS, doneHeatNum, doneHeatCount);
            } else {
                curInfo = string.Format("{0}{1}子 {2} {3} 第{4}/{5}组",
                    string.IsNullOrEmpty(curAgInfo) ? "" : ("[" + curAgInfo + "] "),
                    curGenderS, curEventS, curStageS, curHeatN, totalHeatN);
            }
            CurrentInfoText.Text = curInfo;

            // Schedule tree
            RenderScheduleTree();

            // Pool header
            RenderPoolHeader();

            // Lanes
            RenderLanes(swimmers);

            // Start list
            RenderStartList(swimmers);

            // 更新计时源对比（刷新分段下拉列表和数据）
            UpdateTimingSourceInfo();
            UpdateRecordDisplay();
        }

        // ═══════ Pool Header ═══════
        private void UpdateRecordDisplay()
        {
            if (RecordDisplay == null || _data == null) return;
            string curEvent = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            string curGender = _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            if (string.IsNullOrEmpty(curEvent))
            {
                RecordDisplay.Text = "";
                return;
            }

            // 主纪录类型（来自主控的"大屏显示记录"设置）
            string mainLabel = _data["displayRecordLabel"] != null ? _data["displayRecordLabel"].ToString() : "WR";
            string mainTypeName = _data["displayRecordTypeName"] != null ? _data["displayRecordTypeName"].ToString() : "世界纪录";

            // 从纪录数据中查找当前项目的主纪录与赛会纪录
            string mainTime = "", crTime = "", mainHolder = "", crHolder = "";
            var records = _data["records"] as JArray;
            if (records != null)
            {
                foreach (JObject r in records)
                {
                    string rEvent = r["eventName"] != null ? r["eventName"].ToString() : "";
                    string rGender = r["gender"] != null ? r["gender"].ToString() : "";
                    if (rEvent != curEvent || rGender != curGender) continue;

                    string rType = r["recordType"] != null ? r["recordType"].ToString() : "";
                    string rTime = r["time"] != null ? r["time"].ToString() : "";
                    double rTimeSec = r["timeInSeconds"] != null ? (double)r["timeInSeconds"] : 0;
                    string rHolder = r["holderName"] != null ? r["holderName"].ToString() : "";

                    if ((rType == mainTypeName || rType.Contains(mainTypeName)) && rTimeSec > 0)
                    {
                        mainTime = rTime;
                        mainHolder = rHolder;
                    }
                    else if (rType.Contains("赛会") && rTimeSec > 0)
                    {
                        crTime = rTime;
                        crHolder = rHolder;
                    }
                }
            }

            // 显示当前主纪录类型 + CR
            var sb = new System.Text.StringBuilder();
            sb.Append(mainLabel + ": ");
            sb.Append(!string.IsNullOrEmpty(mainTime) ? mainTime : "---");
            sb.Append("    CR: ");
            sb.Append(!string.IsNullOrEmpty(crTime) ? crTime : "---");

            RecordDisplay.Text = sb.ToString();
        }

        private void RenderPoolHeader()
        {
            PoolHeader.Children.Clear();
            PoolHeader.ColumnDefinitions.Clear();

            // 列布局与泳道行一致
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });   // 0: 道次
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 1: 左发令
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });  // 2: 左设备（+24，容纳圈数 spinner）
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 3: 姓名+进度
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });  // 4: 右设备（+24，容纳圈数 spinner）
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 5: 右发令
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(267) });  // 6: 成绩信息

            // 道次
            var laneHeader = MakeHeaderLabel("道", 32);
            Grid.SetColumn(laneHeader, 0);
            PoolHeader.Children.Add(laneHeader);

            // Left start indicator
            var leftInd = new TextBlock
            {
                Text = _startPosition == "left" ? ">" : "",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(leftInd, 1);
            PoolHeader.Children.Add(leftInd);

            // Left device labels（与右端对称：[T] 盲3 盲2 盲1 出发 触板 圈；盲表数量减少时
            // 用 Hidden 保留位置，使盲1/出发/触板的位置固定）
            var leftLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftLabels.Children.Add(MakeHeaderLabel("[T]", 80));
            var lh3 = MakeHeaderLabel("盲3", 26); lh3.Visibility = _leftBlindWatchCount >= 3 ? Visibility.Visible : Visibility.Hidden; leftLabels.Children.Add(lh3);
            var lh2 = MakeHeaderLabel("盲2", 26); lh2.Visibility = _leftBlindWatchCount >= 2 ? Visibility.Visible : Visibility.Hidden; leftLabels.Children.Add(lh2);
            var lh1 = MakeHeaderLabel("盲1", 26); lh1.Visibility = _leftBlindWatchCount >= 1 ? Visibility.Visible : Visibility.Hidden; leftLabels.Children.Add(lh1);
            leftLabels.Children.Add(MakeHeaderLabel("出发", 26));
            leftLabels.Children.Add(MakeHeaderLabel("触板", 26));
            leftLabels.Children.Add(MakeHeaderLabel("圈", 50));
            Grid.SetColumn(leftLabels, 2);
            PoolHeader.Children.Add(leftLabels);

            // Mid header
            var midHeaderPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            midHeaderPanel.Children.Add(MakeHeaderLabel("姓名/代表队", 120));
            midHeaderPanel.Children.Add(new TextBlock { Text = "方向/进度", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(midHeaderPanel, 3);
            PoolHeader.Children.Add(midHeaderPanel);

            // Right device labels（圈 触板 出发 盲1 盲2 盲3 [T]；盲表数量减少时盲2/盲3 用 Hidden 保留位置）
            var rightLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rightLabels.Children.Add(MakeHeaderLabel("圈", 50));
            rightLabels.Children.Add(MakeHeaderLabel("触板", 26));
            rightLabels.Children.Add(MakeHeaderLabel("出发", 26));
            var rh1 = MakeHeaderLabel("盲1", 26); rh1.Visibility = _rightBlindWatchCount >= 1 ? Visibility.Visible : Visibility.Hidden; rightLabels.Children.Add(rh1);
            var rh2 = MakeHeaderLabel("盲2", 26); rh2.Visibility = _rightBlindWatchCount >= 2 ? Visibility.Visible : Visibility.Hidden; rightLabels.Children.Add(rh2);
            var rh3 = MakeHeaderLabel("盲3", 26); rh3.Visibility = _rightBlindWatchCount >= 3 ? Visibility.Visible : Visibility.Hidden; rightLabels.Children.Add(rh3);
            rightLabels.Children.Add(MakeHeaderLabel("[T]", 80));
            Grid.SetColumn(rightLabels, 4);
            PoolHeader.Children.Add(rightLabels);

            // Right start indicator
            var rightInd = new TextBlock
            {
                Text = _startPosition == "right" ? "<" : "",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rightInd, 5);
            PoolHeader.Children.Add(rightInd);

            // Info area header
            var infoHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            infoHeader.Children.Add(MakeHeaderLabel("反应", 60));
            infoHeader.Children.Add(MakeHeaderLabel("成绩", 115));
            infoHeader.Children.Add(MakeHeaderLabel("名次", 44));
            infoHeader.Children.Add(MakeHeaderLabel("备注", 40));
            Grid.SetColumn(infoHeader, 6);
            PoolHeader.Children.Add(infoHeader);
        }

        private TextBlock MakeHeaderLabel(string text, double width)
        {
            return new TextBlock
            {
                Text = text,
                Width = width,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // ═══════ 赛程树 ═══════
        private void RenderScheduleTree()
        {
            if (_data == null || ScheduleTree == null) return;
            var schedule = _data["schedule"] as JArray;
            if (schedule == null) return;

            string curAgeGroup = _data["currentAgeGroup"] != null ? _data["currentAgeGroup"].ToString() : "";
            string curGender = _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            string curEvent = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            string curStage = _data["currentStage"] != null ? _data["currentStage"].ToString() : "";
            int curHeat = _data["currentHeat"] != null ? (int)_data["currentHeat"] : 0;

            string hash = schedule.Count + "|" + curAgeGroup + "|" + curGender + "|" + curEvent + "|" + curStage + "|" + curHeat + "|" + _selectedHeatKey;
            if (hash == _lastScheduleHash) return;
            _lastScheduleHash = hash;

            ScheduleTree.Items.Clear();

            // Group by session
            var sessions = new Dictionary<int, List<JObject>>();
            var sessionNames = new Dictionary<int, string>();
            foreach (JObject item in schedule)
            {
                int sn = item["session"] != null ? (int)item["session"] : 1;
                if (!sessions.ContainsKey(sn)) sessions[sn] = new List<JObject>();
                sessions[sn].Add(item);
                if (item["sessionName"] != null) sessionNames[sn] = item["sessionName"].ToString();
            }

            foreach (KeyValuePair<int, List<JObject>> kv in sessions)
            {
                string sName;
                if (!sessionNames.TryGetValue(kv.Key, out sName)) sName = string.Format("第{0}单元", kv.Key);

                // Check if all events in session are completed
                bool sessionAllDone = true;
                foreach (JObject ev in kv.Value)
                {
                    if (ev["allConfirmed"] == null || !(bool)ev["allConfirmed"])
                    {
                        sessionAllDone = false;
                        break;
                    }
                }

                // Auto-collapse completed sessions
                string sessionKey = kv.Key.ToString();
                if (sessionAllDone && !_treeCollapsed.ContainsKey(sessionKey))
                    _treeCollapsed[sessionKey] = true;
                bool sessionCollapsed;
                if (!_treeCollapsed.TryGetValue(sessionKey, out sessionCollapsed))
                    sessionCollapsed = false;

                string sessionHeader = sName;
                if (sessionAllDone) sessionHeader += " [已完赛]";

                var sessionItem = new TreeViewItem
                {
                    Header = sessionHeader,
                    IsExpanded = !sessionCollapsed,
                    Foreground = sessionAllDone ?
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")) :
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold
                };

                if (!sessionCollapsed)
                {
                    int evIdx = 0;
                    foreach (JObject ev in kv.Value)
                    {
                        string ageGroup = ev["ageGroup"] != null ? ev["ageGroup"].ToString() : "";
                        string gender = ev["gender"] != null ? ev["gender"].ToString() : "";
                        string eventName = ev["eventName"] != null ? ev["eventName"].ToString() : "";
                        string stage = ev["stage"] != null ? ev["stage"].ToString() : "";
                        int heatCount = ev["heatCount"] != null ? (int)ev["heatCount"] : 1;
                        if (heatCount < 1) heatCount = 1;
                        bool allDone = ev["allConfirmed"] != null && (bool)ev["allConfirmed"];
                        var heatConfirmed = ev["heatConfirmed"] as JArray;

                        string evHeader = (string.IsNullOrEmpty(ageGroup) ? "" : ("[" + ageGroup + "] "))
                                        + string.Format("{0} {1} {2}", gender, eventName, stage);
                        if (allDone) evHeader += " [已完赛]";

                        // Auto-collapse completed events
                        string evKey = "ev_" + kv.Key + "_" + evIdx;
                        if (allDone && !_treeCollapsed.ContainsKey(evKey))
                            _treeCollapsed[evKey] = true;
                        bool evCollapsed;
                        if (!_treeCollapsed.TryGetValue(evKey, out evCollapsed))
                            evCollapsed = false;

                        var eventItem = new TreeViewItem
                        {
                            Header = evHeader,
                            IsExpanded = !evCollapsed,
                            Foreground = allDone ?
                                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")) :
                                Brushes.White,
                            FontSize = 13,
                            FontWeight = FontWeights.Normal
                        };

                        if (!evCollapsed || !allDone)
                        {
                            for (int h = 1; h <= heatCount; h++)
                            {
                                bool confirmed = false;
                                if (heatConfirmed != null && h - 1 < heatConfirmed.Count)
                                    confirmed = heatConfirmed[h - 1] != null && (bool)heatConfirmed[h - 1];

                                // tag 格式: 组别|性别|项目|赛次|组次（5 段）
                                string tag = string.Format("{0}|{1}|{2}|{3}|{4}", ageGroup, gender, eventName, stage, h);
                                string heatHeader;
                                if (confirmed)
                                {
                                    heatHeader = string.Format("第{0}组 [已完赛]", h);
                                }
                                else
                                {
                                    heatHeader = string.Format("第{0}组 (共{1}组)", h, heatCount);
                                }

                                var heatItem = new TreeViewItem
                                {
                                    Header = heatHeader,
                                    Tag = confirmed ? null : tag,
                                    FontSize = 13,
                                    FontWeight = FontWeights.Normal
                                };

                                if (confirmed)
                                {
                                    heatItem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA"));
                                    heatItem.IsEnabled = false;
                                }
                                else
                                {
                                    // Highlight current or selected heat
                                    bool isActive = false;
                                    if (!string.IsNullOrEmpty(_selectedHeatKey) && tag == _selectedHeatKey)
                                        isActive = true;
                                    else if (string.IsNullOrEmpty(_selectedHeatKey) &&
                                        ageGroup == curAgeGroup && gender == curGender && eventName == curEvent && stage == curStage && h == curHeat)
                                        isActive = true;

                                    if (isActive)
                                    {
                                        heatItem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                                        heatItem.FontWeight = FontWeights.Bold;
                                    }
                                    else
                                    {
                                        heatItem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                                    }
                                }
                                eventItem.Items.Add(heatItem);
                            }
                        }
                        sessionItem.Items.Add(eventItem);
                        evIdx++;
                    }
                }
                ScheduleTree.Items.Add(sessionItem);
            }
        }

        // 比赛进行中禁止切换项目（防止误操作导致参数复位）
        private bool IsRacing()
        {
            if (_data == null) return false;
            string st = _data["raceState"] != null ? _data["raceState"].ToString().ToUpper() : "";
            return st == "READY" || st == "RACING";
        }

        // 当前组已比完但成绩还未确认：也禁止切组
        private bool IsFinishedUnconfirmed()
        {
            if (_data == null) return false;
            string st = _data["raceState"] != null ? _data["raceState"].ToString().ToUpper() : "";
            if (st != "FINISHED") return false;
            bool resCnf = _data["resultConfirmed"] != null && (bool)_data["resultConfirmed"];
            if (resCnf) return false;
            var sws = _data["swimmers"] as JArray;
            if (sws == null) return false;
            foreach (JObject sw in sws)
            {
                bool fin = sw["isFinished"] != null && (bool)sw["isFinished"];
                string ft = sw["finalTime"] != null ? sw["finalTime"].ToString() : "";
                if (fin || !string.IsNullOrEmpty(ft)) return true;
            }
            return false;
        }

        private bool BlockIfRacing(string action)
        {
            if (IsRacing())
            {
                MessageBox.Show(
                    "比赛进行中不能重新选择比赛项目。\n\n如需切换，请先点击 \"计时复位\" 结束当前比赛。",
                    "操作被阻止", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("比赛进行中不能" + action);
                return true;
            }
            if (IsFinishedUnconfirmed())
            {
                MessageBox.Show(
                    "当前组尚未确认成绩，不能" + action + "。\n请先点击 \"确认成绩\" 或 \"计时复位\"。",
                    "操作被阻止", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("当前组未确认成绩，不能" + action);
                return true;
            }
            return false;
        }

        private void ScheduleTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = ScheduleTree.SelectedItem as TreeViewItem;
            if (item == null || item.Tag == null) return;
            // tag 格式: 组别|性别|项目|赛次|组次（5 段）
            string[] parts = item.Tag.ToString().Split('|');
            if (parts.Length < 5) return;
            string ageGroup = parts[0];
            string gender = parts[1];
            string eventName = parts[2];
            string stage = parts[3];
            int heat;
            if (!int.TryParse(parts[4], out heat)) return;

            // 比赛进行中拦截
            if (BlockIfRacing("切换项目")) return;

            _selectedHeatKey = item.Tag.ToString();
            SendCmd("SET_AGEGROUP", ageGroup);
            SendCmd("SET_GENDER", gender);
            SendCmd("SET_EVENT", eventName);
            SendCmd("SET_STAGE", stage);
            SendCmd("SET_HEAT", heat);
            AddLog(string.Format("选择: {0}{1} {2} {3} 第{4}组",
                string.IsNullOrEmpty(ageGroup) ? "" : ("[" + ageGroup + "] "),
                gender, eventName, stage, heat));
            _lastScheduleHash = ""; // Force refresh
        }

        // ═══════ Lane Rendering (增量更新) ═══════
        // 画刷缓存
        private static readonly SolidColorBrush _brushGreen = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
        private static readonly SolidColorBrush _brushRed = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
        private static readonly SolidColorBrush _brushBlack = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
        private static readonly SolidColorBrush _brushAmber = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
        private static readonly SolidColorBrush _brushSlate = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
        private static readonly SolidColorBrush _brushDark = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        private static readonly SolidColorBrush _brushBlue = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
        private static readonly SolidColorBrush _brushSilver = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0"));
        private static readonly SolidColorBrush _brushBronze = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CD7F32"));
        private static readonly SolidColorBrush _brushGray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        private static readonly SolidColorBrush _brushMutedText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
        private static readonly SolidColorBrush _brushInstalledStroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));

        private static SolidColorBrush GetDeviceBrush(string status)
        {
            switch (status)
            {
                case "open": return _brushGreen;
                case "broken": return _brushBlack;  // 黑色 = 损坏（与 Touched 红色区分）
                case "touched": return _brushRed;   // 红色 = 已触板（窗口期内）
                case "falsestart": return _brushAmber;
                case "notinstalled": return _brushInstalledStroke;
                default: return _brushSlate;
            }
        }

        // 泳道行UI引用
        private class LaneRowUI
        {
            public int Lane;
            public Border Row;
            public Button TouchL, TouchR;
            public Ellipse[] LeftDots;  // 0-4: BlindWatch1/2/3, StartBlock, Touchpad
            public Ellipse[] RightDots; // 0-4: Touchpad, StartBlock, BlindWatch1/2/3
            public TextBlock LeftRemainText, RightRemainText;
            public TextBlock TrackText;
            public TextBlock ReactionText, DisplayTimeText, RankText, RemarkText;
            public Border LeftSignalInd, RightSignalInd;
            public TextBlock NameText, TeamText;
        }

        private List<LaneRowUI> _laneRowUIs = new List<LaneRowUI>();
        private string _laneRowsBuiltKey = "";

        private void RenderLanes(JArray swimmers)
        {
            if (swimmers == null) { LanePanel.Children.Clear(); _laneRowUIs.Clear(); _laneRowsBuiltKey = ""; return; }

            // 按"参数设置 → 道次顺序"排序：正序=升序 0→9，逆序=降序 9→0
            var sortedList = new List<JObject>();
            foreach (JObject sw in swimmers) sortedList.Add(sw);
            sortedList.Sort(delegate (JObject a, JObject b) {
                int la = a["lane"] != null ? (int)a["lane"] : 0;
                int lb = b["lane"] != null ? (int)b["lane"] : 0;
                int d = la - lb;
                return _laneOrder == "reverse" ? -d : d;
            });
            var sorted = new JArray();
            foreach (var s in sortedList) sorted.Add(s);
            swimmers = sorted;

            bool isRelay = false;
            string curEventStr = _data != null && _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            if (curEventStr.Contains("接力")) isRelay = true;

            // 构造 key：运动员列表/项目变化时重建结构（盲表数量、道次顺序变更也触发重建）
            var sbKey = new StringBuilder();
            sbKey.Append(curEventStr).Append('|').Append(isRelay).Append('|').Append(swimmers.Count);
            sbKey.Append("|bw:").Append(_leftBlindWatchCount).Append('/').Append(_rightBlindWatchCount);
            sbKey.Append("|lo:").Append(_laneOrder);
            foreach (JObject sw in swimmers)
            {
                sbKey.Append('|');
                sbKey.Append(sw["name"] != null ? sw["name"].ToString() : "");
                sbKey.Append(':').Append(sw["lane"] != null ? sw["lane"].ToString() : "0");
            }
            string key = sbKey.ToString();

            if (key != _laneRowsBuiltKey)
            {
                RenderPoolHeader();   // 表头同步重画（盲表数量影响标签可见性）
                BuildLaneRows(swimmers, isRelay);
                _laneRowsBuiltKey = key;
            }
            RefreshLaneRows(swimmers, isRelay);
        }

        private void BuildLaneRows(JArray swimmers, bool isRelay)
        {
            LanePanel.Children.Clear();
            _laneRowUIs.Clear();

            foreach (JObject sw in swimmers)
            {
                int lane = sw["lane"] != null ? (int)sw["lane"] : 0;
                var rowUI = new LaneRowUI { Lane = lane };

                var row = new Border
                {
                    Background = _brushDark,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(0, 0, 0, 0),
                    Height = 56
                };
                rowUI.Row = row;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(267) });

                int capturedLane = lane;

                // Col 0: 道次（静态）
                var laneNumTb = new TextBlock
                {
                    Text = lane.ToString(), FontSize = 22, FontWeight = FontWeights.Bold,
                    Foreground = _brushGray,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(laneNumTb, 0); grid.Children.Add(laneNumTb);

                // Col 1: 左发令指示（动态）
                var leftStartInd = new Border { Width = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetColumn(leftStartInd, 1); grid.Children.Add(leftStartInd);
                rowUI.LeftSignalInd = leftStartInd;

                // Col 2: 左设备
                var leftDevices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                var touchBtnL = new Button { Content = "T", Width = 80, Height = 30, FontSize = 15, BorderThickness = new Thickness(0) };
                touchBtnL.PreviewMouseLeftButtonDown += delegate { SendCmd("MANUAL_TOUCH_LEFT", new { lane = capturedLane }); };
                leftDevices.Children.Add(touchBtnL);
                rowUI.TouchL = touchBtnL;

                // 左端 5 圆点：[0]=盲3, [1]=盲2, [2]=盲1, [3]=出发, [4]=触板
                // 创建时即按当前数量设置 Visibility，避免初始全显示再被刷新隐藏
                int initLbc = _leftBlindWatchCount;
                rowUI.LeftDots = new Ellipse[5];
                for (int i = 0; i < 5; i++)
                {
                    var dot = new Ellipse { Width = 22, Height = 22, Margin = new Thickness(2, 0, 2, 0) };
                    if (i == 0) dot.Visibility = initLbc >= 3 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 1) dot.Visibility = initLbc >= 2 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 2) dot.Visibility = initLbc >= 1 ? Visibility.Visible : Visibility.Hidden;
                    rowUI.LeftDots[i] = dot;
                    leftDevices.Children.Add(dot);
                }

                // 圈数 [数字 + ▲▼ spinner]：左端
                var leftRemainTb = new TextBlock { Width = 26, FontSize = 20, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                leftDevices.Children.Add(leftRemainTb);
                rowUI.LeftRemainText = leftRemainTb;
                var leftSpinner = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
                var leftLapUp = new Button { Content = "▲", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
                leftLapUp.PreviewMouseLeftButtonDown += delegate { SendCmd("ADJUST_LAP_DISPLAY", new { lane = capturedLane, isLeft = true, delta = +1 }); };
                var leftLapDown = new Button { Content = "▼", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Margin = new Thickness(0, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
                leftLapDown.PreviewMouseLeftButtonDown += delegate { SendCmd("ADJUST_LAP_DISPLAY", new { lane = capturedLane, isLeft = true, delta = -1 }); };
                leftSpinner.Children.Add(leftLapUp);
                leftSpinner.Children.Add(leftLapDown);
                leftDevices.Children.Add(leftSpinner);
                Grid.SetColumn(leftDevices, 2); grid.Children.Add(leftDevices);

                // Col 3: 姓名+进度
                var midPanel = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                var infoStack = new StackPanel { Width = 120 };
                var nameTb = new TextBlock { FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontSize = 16 };
                var teamTb = new TextBlock { Foreground = _brushMutedText, FontSize = 14 };
                infoStack.Children.Add(nameTb);
                infoStack.Children.Add(teamTb);
                rowUI.NameText = nameTb;
                rowUI.TeamText = teamTb;
                DockPanel.SetDock(infoStack, Dock.Left); midPanel.Children.Add(infoStack);

                var trackBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A2332")),
                    CornerRadius = new CornerRadius(4), Height = 22,
                    Margin = new Thickness(6, 0, 6, 0), Padding = new Thickness(4, 0, 4, 0), MinWidth = 60
                };
                var trackText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
                trackBorder.Child = trackText;
                midPanel.Children.Add(trackBorder);
                rowUI.TrackText = trackText;
                Grid.SetColumn(midPanel, 3); grid.Children.Add(midPanel);

                // Col 4: 右设备
                var rightDevices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                // 圈数 [▲▼ spinner + 数字]：右端
                var rightSpinner = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
                var rightLapUp = new Button { Content = "▲", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
                rightLapUp.PreviewMouseLeftButtonDown += delegate { SendCmd("ADJUST_LAP_DISPLAY", new { lane = capturedLane, isLeft = false, delta = +1 }); };
                var rightLapDown = new Button { Content = "▼", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Margin = new Thickness(0, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
                rightLapDown.PreviewMouseLeftButtonDown += delegate { SendCmd("ADJUST_LAP_DISPLAY", new { lane = capturedLane, isLeft = false, delta = -1 }); };
                rightSpinner.Children.Add(rightLapUp);
                rightSpinner.Children.Add(rightLapDown);
                rightDevices.Children.Add(rightSpinner);
                var rightRemainTb = new TextBlock { Width = 26, FontSize = 20, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                rightDevices.Children.Add(rightRemainTb);
                rowUI.RightRemainText = rightRemainTb;

                // 右端 5 圆点：[0]=触板, [1]=出发, [2]=盲1, [3]=盲2, [4]=盲3
                int initRbc = _rightBlindWatchCount;
                rowUI.RightDots = new Ellipse[5];
                for (int i = 0; i < 5; i++)
                {
                    var dot = new Ellipse { Width = 22, Height = 22, Margin = new Thickness(2, 0, 2, 0) };
                    if (i == 2) dot.Visibility = initRbc >= 1 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 3) dot.Visibility = initRbc >= 2 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 4) dot.Visibility = initRbc >= 3 ? Visibility.Visible : Visibility.Hidden;
                    rowUI.RightDots[i] = dot;
                    rightDevices.Children.Add(dot);
                }

                var touchBtnR = new Button { Content = "T", Width = 80, Height = 30, FontSize = 15, BorderThickness = new Thickness(0) };
                touchBtnR.PreviewMouseLeftButtonDown += delegate { SendCmd("MANUAL_TOUCH_RIGHT", new { lane = capturedLane }); };
                rightDevices.Children.Add(touchBtnR);
                rowUI.TouchR = touchBtnR;
                Grid.SetColumn(rightDevices, 4); grid.Children.Add(rightDevices);

                // Col 5: 右发令指示
                var rightStartInd = new Border { Width = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetColumn(rightStartInd, 5); grid.Children.Add(rightStartInd);
                rowUI.RightSignalInd = rightStartInd;

                // Col 6: 成绩信息
                var infoArea = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var reactionTb = new TextBlock { Width = 60, FontSize = 18, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") };
                infoArea.Children.Add(reactionTb);
                rowUI.ReactionText = reactionTb;

                var displayTimeTb = new TextBlock { Width = 115, FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") };
                infoArea.Children.Add(displayTimeTb);
                rowUI.DisplayTimeText = displayTimeTb;

                var rankTb = new TextBlock { Width = 44, FontSize = 22, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") };
                infoArea.Children.Add(rankTb);
                rowUI.RankText = rankTb;

                var remarkTb = new TextBlock { Width = 40, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = _brushRed, TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                infoArea.Children.Add(remarkTb);
                rowUI.RemarkText = remarkTb;

                Grid.SetColumn(infoArea, 6); grid.Children.Add(infoArea);

                row.Child = grid;
                row.MouseLeftButtonDown += delegate
                {
                    _selectedLane = capturedLane;
                    _lastSplitCount = -1;
                    LaneInput.Text = capturedLane.ToString();
                    UpdateTimingSourceInfo();
                    RefreshLaneRows(_data != null ? _data["swimmers"] as JArray : null, isRelay);
                };
                LanePanel.Children.Add(row);
                _laneRowUIs.Add(rowUI);
            }
        }

        private void RefreshLaneRows(JArray swimmers, bool isRelay)
        {
            if (swimmers == null || _laneRowUIs.Count == 0) return;
            bool leftStart = _startPosition == "left";

            int idx = 0;
            foreach (JObject sw in swimmers)
            {
                if (idx >= _laneRowUIs.Count) break;
                var rowUI = _laneRowUIs[idx++];
                int lane = rowUI.Lane;
                var ds = sw["deviceStatus"] as JObject;
                var mb = sw["manualButton"] as JObject;
                bool isFalseStart = sw["isFalseStart"] != null && (bool)sw["isFalseStart"];
                bool isSuspectFalseStart = sw["isSuspectFalseStart"] != null && (bool)sw["isSuspectFalseStart"];
                bool isFinished = sw["isFinished"] != null && (bool)sw["isFinished"];
                string status = sw["status"] != null ? sw["status"].ToString() : "";

                // 行边框 + 空泳道/未参赛泳道淡化（已判罚抢跳=琥珀，可疑抢跳=琥珀，选中=蓝）
                if (isFalseStart || isSuspectFalseStart) { rowUI.Row.BorderBrush = _brushAmber; rowUI.Row.BorderThickness = new Thickness(1); }
                else if (lane == _selectedLane) { rowUI.Row.BorderBrush = _brushBlue; rowUI.Row.BorderThickness = new Thickness(1); }
                else { rowUI.Row.BorderThickness = new Thickness(0); }
                if (status == "EMPTY") rowUI.Row.Opacity = 0.35;
                else if (status == "DNS" || status == "DNF" || status == "DSQ") rowUI.Row.Opacity = 0.45;
                else rowUI.Row.Opacity = 1.0;

                // 左右发令指示
                rowUI.LeftSignalInd.Background = leftStart ? (Brush)_brushGreen : Brushes.Transparent;
                rowUI.RightSignalInd.Background = !leftStart ? (Brush)_brushGreen : Brushes.Transparent;

                // 左T按钮
                bool leftManualEnabled = mb != null && mb["leftEnabled"] != null && (bool)mb["leftEnabled"];
                bool rightManualEnabled = mb != null && mb["rightEnabled"] != null && (bool)mb["rightEnabled"];
                string leftManualStatus = mb != null && mb["leftStatus"] != null ? mb["leftStatus"].ToString() : "closed";
                string rightManualStatus = mb != null && mb["rightStatus"] != null ? mb["rightStatus"].ToString() : "closed";
                if (leftManualEnabled)
                {
                    rowUI.TouchL.Background = leftManualStatus == "open" ? (Brush)_brushGreen : (Brush)_brushSlate;
                    rowUI.TouchL.Foreground = Brushes.White;
                }
                else
                {
                    rowUI.TouchL.Background = _brushDark;
                    rowUI.TouchL.Foreground = _brushSlate;
                }

                // 左设备5个圆点（与右端对称）：盲表3 / 盲表2 / 盲表1 / 出发台 / 触板
                rowUI.LeftDots[0].Fill = GetDeviceBrush(GetDeviceStatus(ds, "leftBlindWatch3"));
                rowUI.LeftDots[1].Fill = GetDeviceBrush(GetDeviceStatus(ds, "leftBlindWatch2"));
                rowUI.LeftDots[2].Fill = GetDeviceBrush(GetDeviceStatus(ds, "leftBlindWatch1"));
                // 用 Hidden 保留位置，不让盲1 / 出发台 / 触板因数量变化而移动
                rowUI.LeftDots[0].Visibility = _leftBlindWatchCount >= 3 ? Visibility.Visible : Visibility.Hidden; // 盲3
                rowUI.LeftDots[1].Visibility = _leftBlindWatchCount >= 2 ? Visibility.Visible : Visibility.Hidden; // 盲2
                rowUI.LeftDots[2].Visibility = _leftBlindWatchCount >= 1 ? Visibility.Visible : Visibility.Hidden; // 盲1
                rowUI.LeftDots[3].Fill = GetDeviceBrush(GetDeviceStatus(ds, "leftStartBlock"));
                rowUI.LeftDots[4].Fill = GetDeviceBrush(GetDeviceStatus(ds, "leftTouchpad"));

                // 左剩余秒数
                string leftRemainStr = sw["leftTouchRemain"] != null ? sw["leftTouchRemain"].ToString() : "";
                int leftRemainVal;
                bool leftRemainActive = int.TryParse(leftRemainStr, out leftRemainVal) && leftRemainVal > 0;
                rowUI.LeftRemainText.Text = leftRemainActive ? leftRemainStr : "";
                rowUI.LeftRemainText.Foreground = leftRemainActive ? (Brush)_brushAmber : (Brush)_brushSlate;

                // 右T按钮
                if (rightManualEnabled)
                {
                    rowUI.TouchR.Background = rightManualStatus == "open" ? (Brush)_brushGreen : (Brush)_brushSlate;
                    rowUI.TouchR.Foreground = Brushes.White;
                }
                else
                {
                    rowUI.TouchR.Background = _brushDark;
                    rowUI.TouchR.Foreground = _brushSlate;
                }

                // 右设备5个圆点
                rowUI.RightDots[0].Fill = GetDeviceBrush(GetDeviceStatus(ds, "rightTouchpad"));
                rowUI.RightDots[1].Fill = GetDeviceBrush(GetDeviceStatus(ds, "rightStartBlock"));
                rowUI.RightDots[2].Fill = GetDeviceBrush(GetDeviceStatus(ds, "rightBlindWatch1"));
                rowUI.RightDots[3].Fill = GetDeviceBrush(GetDeviceStatus(ds, "rightBlindWatch2"));
                rowUI.RightDots[4].Fill = GetDeviceBrush(GetDeviceStatus(ds, "rightBlindWatch3"));
                rowUI.RightDots[2].Visibility = _rightBlindWatchCount >= 1 ? Visibility.Visible : Visibility.Hidden;
                rowUI.RightDots[3].Visibility = _rightBlindWatchCount >= 2 ? Visibility.Visible : Visibility.Hidden;
                rowUI.RightDots[4].Visibility = _rightBlindWatchCount >= 3 ? Visibility.Visible : Visibility.Hidden;

                // 右剩余秒数
                string rightRemainStr = sw["rightTouchRemain"] != null ? sw["rightTouchRemain"].ToString() : "";
                int rightRemainVal;
                bool rightRemainActive = int.TryParse(rightRemainStr, out rightRemainVal) && rightRemainVal > 0;
                rowUI.RightRemainText.Text = rightRemainActive ? rightRemainStr : "";
                rowUI.RightRemainText.Foreground = rightRemainActive ? (Brush)_brushAmber : (Brush)_brushSlate;

                // 姓名/代表队 — 测试模式下姓名栏改为显示左端最近事件文字
                bool inTestMode = (_data != null && _data["testMode"] != null && (bool)_data["testMode"]);
                if (inTestMode)
                {
                    string leftEvt = sw["testLeftEvent"] != null ? sw["testLeftEvent"].ToString() : "";
                    rowUI.NameText.Text = leftEvt;
                    rowUI.NameText.Foreground = leftEvt.Length > 0 ? (Brush)_brushAmber : Brushes.White;
                    rowUI.TeamText.Text = "";
                }
                else if (status == "EMPTY")
                {
                    rowUI.NameText.Text = "（空泳道）";
                    rowUI.NameText.Foreground = Brushes.White;
                    rowUI.TeamText.Text = "";
                }
                else if (isRelay)
                {
                    rowUI.NameText.Text = sw["country"] != null ? sw["country"].ToString() : "";
                    rowUI.NameText.Foreground = Brushes.White;
                    rowUI.TeamText.Text = GetRelayCurrentLegName(sw);
                }
                else
                {
                    rowUI.NameText.Text = sw["name"] != null ? sw["name"].ToString() : "";
                    rowUI.NameText.Foreground = Brushes.White;
                    rowUI.TeamText.Text = sw["country"] != null ? sw["country"].ToString() : "";
                }

                // 方向/进度
                string swDir = sw["direction"] != null ? sw["direction"].ToString() : (leftStart ? "→" : "←");
                string dir = swDir == "←" ? "returning" : "going";
                rowUI.TrackText.HorizontalAlignment = dir == "returning" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                rowUI.TrackText.Text = BuildArrows(sw, swDir, dir);
                if (isFinished) rowUI.TrackText.Foreground = _brushAmber;
                else if (dir == "returning") rowUI.TrackText.Foreground = _brushGreen;
                else rowUI.TrackText.Foreground = _brushBlue;

                // 反应时：首次显示后 splitDisplayTime 秒后消隐，已完赛则一直显示
                // 关闭RT时直接隐藏（服务器端已停止发送有效值，但本地也兜底）
                string rtVal = (_reactionTimeEnabled && sw["reactionTime"] != null) ? sw["reactionTime"].ToString() : "";
                string rtDisplay = "";
                if (_reactionTimeEnabled && !string.IsNullOrEmpty(rtVal))
                {
                    if (!_laneSplitState.ContainsKey(lane)) _laneSplitState[lane] = new SplitState();
                    var ss = _laneSplitState[lane];
                    if (rtVal != ss.LastReaction) { ss.LastReaction = rtVal; ss.ReactionShowTime = DateTime.Now; }
                    if (isFinished) {
                        rtDisplay = rtVal; // 完赛后一直显示
                    } else if (ss.ReactionShowTime > DateTime.MinValue) {
                        double dispSec = _splitDisplayTime > 0 ? _splitDisplayTime : 5;
                        if ((DateTime.Now - ss.ReactionShowTime).TotalSeconds < dispSec) rtDisplay = rtVal;
                    }
                }
                rowUI.ReactionText.Text = rtDisplay;
                // 起跳可疑/已判罚：反应时标红，提醒裁判判罚
                bool rtSuspect = _reactionTimeEnabled && (
                    (sw["isSuspectFalseStart"] != null && (bool)sw["isSuspectFalseStart"]) ||
                    (sw["isFalseStart"] != null && (bool)sw["isFalseStart"]));
                rowUI.ReactionText.Foreground = rtSuspect
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"))
                    : Brushes.White;

                // 成绩/分段 — 测试模式下成绩栏改为显示右端最近事件文字
                string timeDisplay;
                if (inTestMode)
                {
                    string rightEvt = sw["testRightEvent"] != null ? sw["testRightEvent"].ToString() : "";
                    timeDisplay = rightEvt;
                    rowUI.DisplayTimeText.Text = rightEvt;
                    rowUI.DisplayTimeText.Foreground = rightEvt.Length > 0 ? (Brush)_brushAmber : Brushes.White;
                }
                else
                {
                    timeDisplay = GetSplitOrFinalTime(sw);
                    rowUI.DisplayTimeText.Text = timeDisplay;
                    rowUI.DisplayTimeText.Foreground = Brushes.White;
                }

                // 名次：直接以分段成绩显示与否为同步依据
                // 分段成绩有显示 && 服务器送来的 rank > 0 → 显示名次
                // DSQ/DNS/DNF 服务器会把 rank 设为 0，所以自动不会显示
                // 测试模式下 timeDisplay 是事件文字，不是真正成绩 → 名次列不亮
                bool hasTimeDisplay = !inTestMode && !string.IsNullOrEmpty(timeDisplay) && string.IsNullOrEmpty(status);
                int rank = 0;
                if (hasTimeDisplay && sw["rank"] != null) rank = (int)sw["rank"];
                rowUI.RankText.Text = rank > 0 ? rank.ToString() : "";
                if (rank == 1) rowUI.RankText.Foreground = _brushAmber;
                else if (rank == 2) rowUI.RankText.Foreground = _brushSilver;
                else if (rank == 3) rowUI.RankText.Foreground = _brushBronze;
                else rowUI.RankText.Foreground = Brushes.White;

                // 备注：DSQ/DNS/DNF 优先显示；否则显示破/平纪录标识（如 WR / =AR / WR/NR）
                string recordNote = sw["recordNote"] != null ? sw["recordNote"].ToString() : "";
                bool isStatusDQ = (status == "DSQ" || status == "DNS" || status == "DNF");
                string remark = isStatusDQ ? status : (string.IsNullOrEmpty(recordNote) ? "" : recordNote);
                rowUI.RemarkText.Text = remark;
                // 破纪录用金色，DSQ/DNS/DNF 用红色
                rowUI.RemarkText.Foreground = (!isStatusDQ && !string.IsNullOrEmpty(recordNote))
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            }
        }

        private string GetSplitOrFinalTime(JObject sw)
        {
            string finalTime = sw["finalTime"] != null ? sw["finalTime"].ToString() : "";
            if (!string.IsNullOrEmpty(finalTime)) return finalTime;
            string status = sw["status"] != null ? sw["status"].ToString() : "";
            if (!string.IsNullOrEmpty(status)) return status;

            int lane = sw["lane"] != null ? (int)sw["lane"] : 0;
            var splits = sw["splits"] as JArray;

            // 只统计有效分段（cumulative 非空），跳过服务器预创建的空段
            JObject lastValidSplit = null;
            int validSplitCount = 0;
            if (splits != null)
            {
                for (int i = splits.Count - 1; i >= 0; i--)
                {
                    var sp = splits[i] as JObject;
                    if (sp != null && sp["cumulative"] != null && !string.IsNullOrEmpty(sp["cumulative"].ToString()))
                    {
                        lastValidSplit = sp;
                        validSplitCount = sp["lap"] != null ? (int)sp["lap"] : (i + 1);
                        break;
                    }
                }
            }

            SplitState ls;
            if (!_laneSplitState.TryGetValue(lane, out ls))
            {
                ls = new SplitState { SplitCount = 0, ShowTime = DateTime.MinValue };
                _laneSplitState[lane] = ls;
            }

            if (validSplitCount > ls.SplitCount)
            {
                ls.SplitCount = validSplitCount;
                ls.ShowTime = DateTime.Now;
            }

            if (lastValidSplit != null && ls.ShowTime != DateTime.MinValue)
            {
                double displaySec = 5;
                if (_data != null && _data["laneCloseSettings"] != null && _data["laneCloseSettings"]["splitDisplayTime"] != null)
                    displaySec = (double)_data["laneCloseSettings"]["splitDisplayTime"];
                double elapsed = (DateTime.Now - ls.ShowTime).TotalMilliseconds;
                if (elapsed < displaySec * 1000)
                {
                    return lastValidSplit["cumulative"].ToString();
                }
            }
            return "";
        }

        /// <summary>
        /// 判断该泳道的分段成绩/名次是否应当可见（用于与分段时间同步显示/消隐）
        /// 已完赛永远可见；否则看最后有效分段的显示窗口
        /// </summary>
        private bool IsSplitVisible(JObject sw)
        {
            string finalTime = sw["finalTime"] != null ? sw["finalTime"].ToString() : "";
            if (!string.IsNullOrEmpty(finalTime)) return true;
            string status = sw["status"] != null ? sw["status"].ToString() : "";
            if (!string.IsNullOrEmpty(status)) return false;

            int lane = sw["lane"] != null ? (int)sw["lane"] : 0;
            SplitState ls;
            if (!_laneSplitState.TryGetValue(lane, out ls)) return false;
            if (ls.ShowTime == DateTime.MinValue) return false;
            double displaySec = 5;
            if (_data != null && _data["laneCloseSettings"] != null && _data["laneCloseSettings"]["splitDisplayTime"] != null)
                displaySec = (double)_data["laneCloseSettings"]["splitDisplayTime"];
            return (DateTime.Now - ls.ShowTime).TotalMilliseconds < displaySec * 1000;
        }

        private string GetRelayCurrentLegName(JObject sw)
        {
            string names = sw["name"] != null ? sw["name"].ToString() : "";
            string[] parts = names.Split(',');
            if (parts.Length <= 1) return names;

            // 优先用 displayedCurrentLap（含 spinner 加/减圈偏移），旧版主控只有 currentLap 时退回用之
            int currentLap;
            if (sw["displayedCurrentLap"] != null) currentLap = (int)sw["displayedCurrentLap"];
            else currentLap = sw["currentLap"] != null ? (int)sw["currentLap"] : 0;
            string ev = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            Match m = Regex.Match(ev, @"(\d+)\s*[x×]\s*(\d+)");
            int legCount = m.Success ? int.Parse(m.Groups[1].Value) : 4;
            int legDist = m.Success ? int.Parse(m.Groups[2].Value) : 100;
            int poolLen = 50;
            if (_data["poolConfig"] != null && _data["poolConfig"]["length"] != null)
                poolLen = (int)_data["poolConfig"]["length"];
            int lapsPerLeg = Math.Max(1, legDist / poolLen);
            int currentLeg = currentLap / lapsPerLeg;
            if (currentLeg >= legCount) currentLeg = legCount - 1;
            if (currentLeg < 0) currentLeg = 0;
            bool isFinished = sw["isFinished"] != null && (bool)sw["isFinished"];
            if (isFinished) currentLeg = legCount - 1;
            string legName = currentLeg < parts.Length ? parts[currentLeg].Trim() : parts[0].Trim();
            return string.Format("第{0}棒: {1}", currentLeg + 1, legName);
        }

        private string BuildArrows(JObject sw, string swDir, string dir)
        {
            bool isFinished = sw["isFinished"] != null && (bool)sw["isFinished"];
            if (isFinished)
            {
                string ft = sw["finalTime"] != null ? sw["finalTime"].ToString() : "";
                if (string.IsNullOrEmpty(ft)) ft = sw["status"] != null ? sw["status"].ToString() : "";
                return "== " + ft + " ==";
            }

            string status = sw["status"] != null ? sw["status"].ToString() : "";
            if (status == "DNS" || status == "DNF" || status == "DSQ") return status;

            double countdown = sw["laneCloseCountdown"] != null ? (double)sw["laneCloseCountdown"] : 0;
            double closeTime = 20;
            if (_data != null && _data["laneCloseSettings"] != null && _data["laneCloseSettings"]["laneCloseTime"] != null)
                closeTime = (double)_data["laneCloseSettings"]["laneCloseTime"];

            string arrow = swDir == "←" ? "◀" : "▶";
            int maxArrows = 8;

            if (countdown > 0)
            {
                double elapsed = closeTime - countdown;
                double progress = closeTime > 0 ? elapsed / closeTime : 1;
                int arrowCount = Math.Max(1, (int)Math.Round(progress * maxArrows));
                if (arrowCount > maxArrows) arrowCount = maxArrows;
                // 格式：第1个箭头 + 倒计时时间 + 尾部箭头（尾部随进度增长）
                int tailCount = arrowCount > 0 ? arrowCount - 1 : 0;
                string tailArrows = "";
                for (int i = 0; i < tailCount; i++) tailArrows += arrow;
                string cdText = string.Format("({0:F1}s)", countdown);
                if (dir == "going") return arrow + " " + cdText + " " + tailArrows;
                return tailArrows + " " + cdText + " " + arrow;
            }

            int currentLap = sw["currentLap"] != null ? (int)sw["currentLap"] : 0;
            string raceState = _data != null && _data["raceState"] != null ? _data["raceState"].ToString().ToLower() : "";
            if (currentLap > 0 || raceState == "racing")
            {
                string fullArrows = "";
                for (int i = 0; i < maxArrows; i++) fullArrows += arrow;
                return fullArrows;
            }
            return "";
        }

        private Ellipse MakeDeviceDot(string status)
        {
            Color c;
            switch (status)
            {
                case "open": c = (Color)ColorConverter.ConvertFromString("#22C55E"); break;
                case "broken": c = (Color)ColorConverter.ConvertFromString("#000000"); break;  // 黑 = 损坏
                case "touched": c = (Color)ColorConverter.ConvertFromString("#EF4444"); break; // 红 = 已触板
                case "falsestart": c = (Color)ColorConverter.ConvertFromString("#F59E0B"); break;
                case "notinstalled": c = (Color)ColorConverter.ConvertFromString("#334155"); break;
                default: c = (Color)ColorConverter.ConvertFromString("#475569"); break;
            }
            return new Ellipse
            {
                Width = 22,
                Height = 22,
                Fill = new SolidColorBrush(c),
                Margin = new Thickness(2, 0, 2, 0),
                Stroke = status == "notinstalled" ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")) : null,
                StrokeThickness = status == "notinstalled" ? 2 : 0,
                StrokeDashArray = status == "notinstalled" ? new DoubleCollection(new double[] { 2, 2 }) : null
            };
        }

        private string GetDeviceStatus(JObject ds, string key)
        {
            if (ds == null || ds[key] == null) return "closed";
            return ds[key].ToString();
        }

        private int _lastSplitCount = -1;

        private void SplitSelect_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (TimingSourceInfo == null) return;
            ShowTimingSourceData();
        }

        private void UpdateTimingSourceInfo()
        {
            if (TimingSourceInfo == null || SplitSelectCombo == null) return;
            if (_data == null || _selectedLane < 0) { TimingSourceInfo.Text = "点击泳道行查看计时源"; return; }
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers == null) return;

            foreach (JObject sw in swimmers)
            {
                if (sw["lane"] == null || (int)sw["lane"] != _selectedLane) continue;

                var splits = sw["splits"] as JArray;
                int splitCount = splits != null ? splits.Count : 0;

                // 只在分段数量变化时才重建下拉框（避免频繁重建破坏用户选择）
                if (splitCount != _lastSplitCount)
                {
                    _lastSplitCount = splitCount;
                    int prevIdx = SplitSelectCombo.SelectedIndex;
                    SplitSelectCombo.SelectionChanged -= SplitSelect_Changed;
                    SplitSelectCombo.Items.Clear();
                    SplitSelectCombo.Items.Add("终点");
                    for (int i = 0; i < splitCount; i++)
                    {
                        JObject sp = (JObject)splits[i];
                        SplitSelectCombo.Items.Add(string.Format("第{0}段({1}m)", sp["lap"], sp["distance"]));
                    }
                    if (prevIdx >= 0 && prevIdx < SplitSelectCombo.Items.Count)
                        SplitSelectCombo.SelectedIndex = prevIdx;
                    else
                        SplitSelectCombo.SelectedIndex = 0;
                    SplitSelectCombo.SelectionChanged += SplitSelect_Changed;
                }

                ShowTimingSourceData();
                break;
            }
        }

        private void ShowTimingSourceData()
        {
            if (_data == null || _selectedLane < 0) return;
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers == null) return;

            foreach (JObject sw in swimmers)
            {
                if (sw["lane"] == null || (int)sw["lane"] != _selectedLane) continue;

                var splits = sw["splits"] as JArray;
                int splitCount = splits != null ? splits.Count : 0;
                int selIdx = SplitSelectCombo.SelectedIndex;

                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("道{0}  {1}\n", _selectedLane, sw["name"] ?? "");

                if (selIdx <= 0)
                {
                    // 终点：反应时间、触板、盲表1/2/3、手动
                    sb.Append("【终点】\n");
                    if (_reactionTimeEnabled) {
                        string rt = sw["reactionTime"] != null ? sw["reactionTime"].ToString() : "";
                        sb.AppendFormat("反应时间: {0}\n", rt != "" ? rt : "-");
                    } else {
                        sb.Append("反应时间: 已关闭\n");
                    }
                    var ts = sw["timingSources"] as JObject;
                    if (ts != null)
                    {
                        string tp = ts["touchpad"] != null ? ts["touchpad"].ToString() : "";
                        string b1 = ts["blindWatch1"] != null ? ts["blindWatch1"].ToString() : "";
                        string b2 = ts["blindWatch2"] != null ? ts["blindWatch2"].ToString() : "";
                        string b3 = ts["blindWatch3"] != null ? ts["blindWatch3"].ToString() : "";
                        // 手动成绩：优先用终点段汇总的manual字段
                        string manual = ts["manual"] != null ? ts["manual"].ToString() : "";
                        if (string.IsNullOrEmpty(manual)) {
                            manual = _finishPosition == "right"
                                ? (ts["manualTouchRight"] != null ? ts["manualTouchRight"].ToString() : "")
                                : (ts["manualTouchLeft"] != null ? ts["manualTouchLeft"].ToString() : "");
                        }
                        sb.AppendFormat("触  板:  {0}\n", tp != "" ? tp : "-");
                        sb.AppendFormat("盲表 1:  {0}\n", b1 != "" ? b1 : "-");
                        sb.AppendFormat("盲表 2:  {0}\n", b2 != "" ? b2 : "-");
                        sb.AppendFormat("盲表 3:  {0}\n", b3 != "" ? b3 : "-");
                        sb.AppendFormat("手  动:  {0}\n", manual != "" ? manual : "-");
                    }
                }
                else if (splits != null && selIdx - 1 < splitCount)
                {
                    // 分段：反应时间(若有)、触板、盲表1/2/3、手动、计时源
                    JObject sp = (JObject)splits[selIdx - 1];
                    sb.AppendFormat("【第{0}段  {1}m】\n", sp["lap"], sp["distance"]);
                    // 接力交接棒有反应时间（关闭RT时隐藏）
                    if (_reactionTimeEnabled) {
                        string spReact = sw["reactionTime"] != null ? sw["reactionTime"].ToString() : "";
                        if (spReact != "") sb.AppendFormat("反应时间: {0}\n", spReact);
                    }
                    string spTp = sp["touchpad"] != null ? sp["touchpad"].ToString() : "";
                    string spB1 = sp["blind1"] != null ? sp["blind1"].ToString() : "";
                    string spB2 = sp["blind2"] != null ? sp["blind2"].ToString() : "";
                    string spB3 = sp["blind3"] != null ? sp["blind3"].ToString() : "";
                    string spM = sp["manual"] != null ? sp["manual"].ToString() : "";
                    string spSrc = sp["source"] != null ? sp["source"].ToString() : "";
                    sb.AppendFormat("触  板:  {0}\n", spTp != "" ? spTp : "-");
                    sb.AppendFormat("盲表 1:  {0}\n", spB1 != "" ? spB1 : "-");
                    sb.AppendFormat("盲表 2:  {0}\n", spB2 != "" ? spB2 : "-");
                    sb.AppendFormat("盲表 3:  {0}\n", spB3 != "" ? spB3 : "-");
                    sb.AppendFormat("手  动:  {0}\n", spM != "" ? spM : "-");
                    sb.AppendFormat("计时源:  {0}\n", spSrc != "" ? spSrc : "-");
                }
                TimingSourceInfo.Text = sb.ToString();
                break;
            }
        }

        private void RenderStartList(JArray swimmers)
        {
            if (swimmers == null) { StartListText.Text = ""; return; }
            bool isRelay = false;
            string curEventStr = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            if (curEventStr.Contains("接力")) isRelay = true;
            // 与道次顺序设置一致排序
            var sortedList = new List<JObject>();
            foreach (JObject sw in swimmers) sortedList.Add(sw);
            sortedList.Sort(delegate (JObject a, JObject b) {
                int la = a["lane"] != null ? (int)a["lane"] : 0;
                int lb = b["lane"] != null ? (int)b["lane"] : 0;
                int d = la - lb;
                return _laneOrder == "reverse" ? -d : d;
            });
            string startList = "";
            foreach (var sw in sortedList)
            {
                string label = isRelay ? (sw["country"] != null ? sw["country"].ToString() : "") : (sw["name"] != null ? sw["name"].ToString() : "");
                startList += string.Format("道{0}-{1}({2}) ", sw["lane"], label, sw["entryTime"] ?? "");
            }
            StartListText.Text = startList;
        }

        // 当前组是否已"确认本组成绩"（从广播的 schedule.heatConfirmed 数组里反查）
        private bool IsCurrentHeatConfirmed()
        {
            if (_data == null) return false;
            string curAg = _data["currentAgeGroup"] != null ? _data["currentAgeGroup"].ToString() : "";
            string curG = _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            string curE = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            string curS = _data["currentStage"] != null ? _data["currentStage"].ToString() : "";
            int curH = _data["currentHeat"] != null ? (int)_data["currentHeat"] : 0;
            if (string.IsNullOrEmpty(curE) || curH <= 0) return false;
            var schedule = _data["schedule"] as JArray;
            if (schedule == null) return false;
            foreach (JObject item in schedule)
            {
                string ag = item["ageGroup"] != null ? item["ageGroup"].ToString() : "";
                string g = item["gender"] != null ? item["gender"].ToString() : "";
                string ev = item["eventName"] != null ? item["eventName"].ToString() : "";
                string st = item["stage"] != null ? item["stage"].ToString() : "";
                if (ag != curAg || g != curG || ev != curE || st != curS) continue;
                var hc = item["heatConfirmed"] as JArray;
                if (hc == null) return false;
                int idx = curH - 1;
                if (idx < 0 || idx >= hc.Count) return false;
                return hc[idx] != null && (bool)hc[idx];
            }
            return false;
        }

        // ═══════ 按钮事件 ═══════
        private void Ready_Click(object sender, RoutedEventArgs e)
        {
            // 已确认成绩的组：禁止再次开始比赛（与主控端规则一致）
            if (IsCurrentHeatConfirmed())
            {
                MessageBox.Show(
                    "当前组已确认成绩并锁定，不能重新开始比赛。\n请在赛程导航中切换到下一组。",
                    "操作被阻止", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("当前组已确认成绩，不能再次开始");
                return;
            }
            // 与主服务器一致：弹简短确认对话框，避免误按
            string ag = _data != null && _data["currentAgeGroup"] != null ? _data["currentAgeGroup"].ToString() : "";
            string gender = _data != null && _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            string ev = _data != null && _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            string stage = _data != null && _data["currentStage"] != null ? _data["currentStage"].ToString() : "";
            int heat = _data != null && _data["currentHeat"] != null ? (int)_data["currentHeat"] : 0;
            string info = (string.IsNullOrEmpty(ag) ? "" : ("[" + ag + "] ")) + gender + " " + ev + " " + stage + " 第" + heat + "组";
            var rR = MessageBox.Show("确定让本组进入【就位】状态？" + info + "\n",
                "就位确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (rR != MessageBoxResult.Yes) return;

            SendCmd("READY");
            if (_localBridge != null && _localBridge.IsConnected) _localBridge.SendCommand(0x21);
            AddLog("就位");
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            SendCmd("START_RACE");
            if (_localBridge != null && _localBridge.IsConnected) _localBridge.SendCommand(0x1C);
            AddLog("发令");
        }

        private void TimerReset_Click(object sender, RoutedEventArgs e)
        {
            // 与主服务器一致：简短确认，避免误按
            MessageBoxResult result = MessageBox.Show("确定计时复位？", "计时复位确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                bool currentHeatConfirmed = IsCurrentHeatConfirmed();
                SendCmd("TIMER_RESET");
                if (_localBridge != null && _localBridge.IsConnected) {
                    _localBridge.SendCommand(0x20); // 计时清零命令
                    _localBridge.SendCommand(0x7F); // 滚动时间 = 0
                }
                AddLog(currentHeatConfirmed ? "计时复位（保留已确认成绩，自动切下一组）" : "计时复位（重新发令）");
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            string ag = _data != null && _data["currentAgeGroup"] != null ? _data["currentAgeGroup"].ToString() : "";
            string info = string.Format("{0}{1}子 {2} {3} 第{4}组",
                string.IsNullOrEmpty(ag) ? "" : ("[" + ag + "] "),
                _data != null ? (_data["currentGender"] ?? "") : "",
                _data != null ? (_data["currentEvent"] ?? "") : "",
                _data != null ? (_data["currentStage"] ?? "") : "",
                _data != null ? (_data["currentHeat"] ?? "") : "");
            MessageBoxResult result = MessageBox.Show(
                "确认本组成绩？\n\n" + info + "\n\n确认后成绩将锁定保存。",
                "确认成绩", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                SendCmd("CONFIRM_RESULT");
                AddLog("已确认本组成绩");
            }
        }

        private void PrevHeat_Click(object sender, RoutedEventArgs e)
        {
            if (BlockIfRacing("切换到上一组")) return;
            SendCmd("PREV_HEAT");
        }

        private void NextHeat_Click(object sender, RoutedEventArgs e)
        {
            if (BlockIfRacing("切换到下一组")) return;
            SendCmd("NEXT_HEAT");
        }

        private void OpenAll_Click(object sender, RoutedEventArgs e)
        {
            SendCmd("OPEN_ALL_LANES");
        }

        private void CloseAll_Click(object sender, RoutedEventArgs e)
        {
            SendCmd("CLOSE_ALL_LANES");
        }

        // ═══════ Lane Operations ═══════
        private int GetLaneInput()
        {
            int v;
            if (int.TryParse(LaneInput.Text, out v)) return v;
            return -1;
        }

        private void MarkDNS_Click(object sender, RoutedEventArgs e)
        {
            MarkLane("DNS", "缺席未出发");
        }

        private void MarkDNF_Click(object sender, RoutedEventArgs e)
        {
            MarkLane("DNF", "中途退出");
        }

        private void MarkDSQ_Click(object sender, RoutedEventArgs e)
        {
            MarkLane("DSQ", "犯规取消资格");
        }

        private void CancelNote_Click(object sender, RoutedEventArgs e)
        {
            int lane = GetLaneInput();
            if (lane < 0) { MessageBox.Show("请输入泳道号"); return; }
            SendCmd("CANCEL_NOTE", new { lane = lane });
            AddLog(string.Format("泳道{0} 取消备注", lane));
        }

        private void BlindResult_Click(object sender, RoutedEventArgs e)
        {
            int lane = GetLaneInput();
            if (lane < 0) { MessageBox.Show("请输入泳道号"); return; }
            SendCmd("USE_BLIND_RESULT", new { lane = lane });
            AddLog(string.Format("泳道{0} 使用盲表成绩", lane));
        }

        private void LaneOpen_Click(object sender, RoutedEventArgs e)
        {
            int lane = GetLaneInput();
            if (lane < 0) { MessageBox.Show("请输入泳道号"); return; }
            SendCmd("MANUAL_LANE_OPEN", new { lane = lane });
            AddLog(string.Format("泳道{0} 打开", lane));
        }

        private void LaneClose_Click(object sender, RoutedEventArgs e)
        {
            int lane = GetLaneInput();
            if (lane < 0) { MessageBox.Show("请输入泳道号"); return; }
            SendCmd("MANUAL_LANE_CLOSE", new { lane = lane });
            AddLog(string.Format("泳道{0} 关闭", lane));
        }

        private void MarkLane(string status, string desc)
        {
            int lane = GetLaneInput();
            if (lane < 0) { MessageBox.Show("请输入泳道号"); return; }
            MessageBoxResult result = MessageBox.Show(
                string.Format("确认将泳道 {0} 标记为 {1}（{2}）？\n\n此操作将取消该泳道的成绩。",
                    lane, status, desc),
                "确认标记", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                SendCmd("MARK_" + status, new { lane = lane });
                AddLog(string.Format("泳道{0} 标记为 {1}", lane, status));
            }
        }

        private void ManualTime_Click(object sender, RoutedEventArgs e)
        {
            int lane = GetLaneInput();
            if (lane < 0) { MessageBox.Show("请输入泳道号"); return; }

            // Build manual time input dialog
            var dlg = new Window
            {
                Title = "手动输入成绩",
                Width = 360,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock
            {
                Text = string.Format("泳道 {0} 手动输入成绩（如 49.23 或 1:23.45）：", lane),
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            });
            var timeBox = new TextBox
            {
                Padding = new Thickness(6),
                FontSize = 16,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"))
            };
            sp.Children.Add(timeBox);
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnCancel = new Button
            {
                Content = "取消",
                Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button
            {
                Content = "确定",
                Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true)
            {
                string timeStr = timeBox.Text.Trim();
                if (string.IsNullOrEmpty(timeStr)) return;
                double seconds = ParseTimeToSeconds(timeStr);
                if (seconds <= 0) { MessageBox.Show("成绩格式不正确"); return; }
                // 二次确认
                var cr = MessageBox.Show(
                    string.Format("确认将泳道 {0} 的成绩手动输入为 {1}？\n\n此操作将写入数据库。",
                        lane, timeStr),
                    "确认手动输入", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (cr != MessageBoxResult.Yes) return;
                SendCmd("OVERRIDE_TIME", new { lane = lane, time = seconds });
                AddLog(string.Format("泳道{0} 手动输入成绩: {1}", lane, timeStr));
            }
        }

        private double ParseTimeToSeconds(string str)
        {
            str = str.Trim();
            if (str.Contains(":"))
            {
                string[] parts = str.Split(':');
                double min = 0;
                double sec = 0;
                double.TryParse(parts[0], out min);
                if (parts.Length > 1) double.TryParse(parts[1], out sec);
                return min * 60 + sec;
            }
            double val;
            if (double.TryParse(str, out val)) return val;
            return 0;
        }

        // ═══════ Display Control ═══════
        private void DisplayBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                SendDisplay(btn.Tag.ToString());
                AddLog("显示: " + btn.Tag.ToString());
            }
        }

        // ═══════ Publish Result ═══════
        private void PublishResult_Click(object sender, RoutedEventArgs e)
        {
            if (_data == null || _data["schedule"] == null)
            {
                MessageBox.Show("暂无赛程数据");
                return;
            }
            var schedule = _data["schedule"] as JArray;
            if (schedule == null || schedule.Count == 0)
            {
                MessageBox.Show("暂无赛程数据");
                return;
            }

            // Collect confirmed heats
            var items = new List<PublishItem>();
            foreach (JObject s in schedule)
            {
                var confirmed = s["heatConfirmed"] as JArray;
                if (confirmed == null) continue;
                for (int h = 0; h < confirmed.Count; h++)
                {
                    if (confirmed[h] != null && (bool)confirmed[h])
                    {
                        string stageStr = s["stage"] != null ? s["stage"].ToString() : "";
                        bool showHeat = confirmed.Count > 1 || stageStr.Contains("预赛") || stageStr.Contains("半决赛");
                        string label = string.Format("{0} {1} {2}{3}",
                            s["gender"] ?? "", s["eventName"] ?? "", stageStr,
                            showHeat ? " 第" + (h + 1) + "组" : "");
                        items.Add(new PublishItem
                        {
                            Label = label,
                            Gender = s["gender"] != null ? s["gender"].ToString() : "",
                            EventName = s["eventName"] != null ? s["eventName"].ToString() : "",
                            Stage = stageStr,
                            Heat = h + 1
                        });
                    }
                }
            }

            if (items.Count == 0)
            {
                MessageBox.Show("暂无已完赛的比赛");
                return;
            }

            // Build publish dialog
            var dlg = new Window
            {
                Title = "选择发布成绩",
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock
            {
                Text = "选择发布成绩",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            });
            var combo = new ComboBox
            {
                Padding = new Thickness(6),
                FontSize = 14,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                Foreground = Brushes.White
            };
            for (int i = 0; i < items.Count; i++)
            {
                combo.Items.Add(items[i].Label);
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            sp.Children.Add(combo);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnCancel = new Button
            {
                Content = "取消",
                Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button
            {
                Content = "发布",
                Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && combo.SelectedIndex >= 0)
            {
                PublishItem it = items[combo.SelectedIndex];
                SendCmd("PUBLISH_RESULT", new { gender = it.Gender, eventName = it.EventName, stage = it.Stage, heat = it.Heat });
                AddLog("发布成绩: " + it.Label);
            }
        }

        private class PublishItem
        {
            public string Label;
            public string Gender;
            public string EventName;
            public string Stage;
            public int Heat;
        }

        // ═══════ Settings ═══════
        // 顶部右上角"修改用户名和密码"按钮 — 弹 ChangePasswordWindow，登录凭据存 timing_credentials.json
        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ChangePasswordWindow();
            dlg.Owner = this;
            dlg.ShowDialog();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Window
            {
                Title = "参数设置",
                Width = 420,
                SizeToContent = SizeToContent.Height,    // 高度自适应内容，不再出现滚动条
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };

            // 优先用服务器广播的最新值，否则用本地保存值
            double closeTime = _laneCloseTime, sbDelay = _startBlockCloseDelay, confDelay = _resultConfirmCloseDelay, fsThresh = _falseStartThreshold, splitDisp = _splitDisplayTime, holdTime = _firstPlaceHoldTime;
            double bigPageInterval = _bigDisplayPageInterval;
            bool rtEnabled = _reactionTimeEnabled;
            if (_data != null && _data["laneCloseSettings"] != null)
            {
                var lcs = _data["laneCloseSettings"];
                if (lcs["laneCloseTime"] != null) closeTime = (double)lcs["laneCloseTime"];
                if (lcs["startBlockCloseDelay"] != null) sbDelay = (double)lcs["startBlockCloseDelay"];
                if (lcs["resultConfirmCloseDelay"] != null) confDelay = (double)lcs["resultConfirmCloseDelay"];
                if (lcs["falseStartThreshold"] != null) fsThresh = (double)lcs["falseStartThreshold"];
                if (lcs["splitDisplayTime"] != null) splitDisp = (double)lcs["splitDisplayTime"];
                if (lcs["firstPlaceHoldTime"] != null) holdTime = (double)lcs["firstPlaceHoldTime"];
                if (lcs["bigDisplayPageInterval"] != null) bigPageInterval = (double)lcs["bigDisplayPageInterval"];
                if (lcs["reactionTimeEnabled"] != null) rtEnabled = (bool)lcs["reactionTimeEnabled"];
            }

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "参数设置", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14) });

            var tbCloseTime = AddParamRow(sp, "泳道关闭时间", closeTime.ToString(), "秒");
            var tbSBDelay = AddParamRow(sp, "出发台关闭延迟", sbDelay.ToString(), "秒");
            var tbConfDelay = AddParamRow(sp, "成绩确认关闭延迟", confDelay.ToString(), "秒");
            var tbFSThresh = AddParamRow(sp, "抢跳判定阈值", fsThresh.ToString(), "秒");
            var tbSplitDisp = AddParamRow(sp, "分段成绩显示时长", splitDisp.ToString(), "秒");
            var tbFirstHold = AddParamRow(sp, "第1名成绩停留时间", holdTime.ToString(), "秒");
            var tbBigPage = AddParamRow(sp, "大屏翻屏时间", bigPageInterval.ToString(), "秒");

            // Finish position radio
            var finishRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            finishRow.Children.Add(new TextBlock { Text = "终点位置", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbLeft = new RadioButton { Content = "左端", Foreground = Brushes.White, FontSize = 14, IsChecked = _finishPosition == "left", GroupName = "FinishPos", Margin = new Thickness(0, 0, 12, 0) };
            var rbRight = new RadioButton { Content = "右端", Foreground = Brushes.White, FontSize = 14, IsChecked = _finishPosition == "right", GroupName = "FinishPos" };
            finishRow.Children.Add(rbLeft);
            finishRow.Children.Add(rbRight);
            sp.Children.Add(finishRow);

            // 反应时(RT)开关：关闭后所有出发反应时相关处理跳过
            var rtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            rtRow.Children.Add(new TextBlock { Text = "反应时(RT)", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbRtOn = new RadioButton { Content = "打开", Foreground = Brushes.White, FontSize = 14, IsChecked = rtEnabled, GroupName = "RTSwitch", Margin = new Thickness(0, 0, 12, 0) };
            var rbRtOff = new RadioButton { Content = "关闭", Foreground = Brushes.White, FontSize = 14, IsChecked = !rtEnabled, GroupName = "RTSwitch" };
            rtRow.Children.Add(rbRtOn);
            rtRow.Children.Add(rbRtOff);
            sp.Children.Add(rtRow);

            // 道次顺序：正序 0→9（顶到底）/ 逆序 9→0
            var orderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            orderRow.Children.Add(new TextBlock { Text = "道次顺序", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbOrderFwd = new RadioButton { Content = "正序 0→9", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneOrder != "reverse", GroupName = "LaneOrderSwitch", Margin = new Thickness(0, 0, 12, 0) };
            var rbOrderRev = new RadioButton { Content = "逆序 9→0", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneOrder == "reverse", GroupName = "LaneOrderSwitch" };
            orderRow.Children.Add(rbOrderFwd);
            orderRow.Children.Add(rbOrderRev);
            sp.Children.Add(orderRow);

            // 服务器地址
            var serverSep = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 10, 0, 0) };
            var serverRow = new Grid();
            serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            serverRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            var serverLabel = new TextBlock { Text = "服务器地址", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(serverLabel, 0);
            serverRow.Children.Add(serverLabel);
            var tbServerHost = new TextBox { Text = ServerBox.Text, Padding = new Thickness(4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Foreground = Brushes.White, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), FontSize = 14, Margin = new Thickness(0, 0, 4, 0) };
            Grid.SetColumn(tbServerHost, 1);
            serverRow.Children.Add(tbServerHost);
            var btnConnect = new Button { Content = "连接", Padding = new Thickness(10, 4, 10, 4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12 };
            btnConnect.Click += delegate {
                ServerBox.Text = tbServerHost.Text.Trim();
                Connect_Click(null, null);
            };
            Grid.SetColumn(btnConnect, 2);
            serverRow.Children.Add(btnConnect);
            serverSep.Child = serverRow;
            sp.Children.Add(serverSep);

            // ── 计时硬件直连 ──
            var hwSep = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 10, 0, 0) };
            var hwStack = new StackPanel();
            hwStack.Children.Add(new TextBlock { Text = "计时硬件直连", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")), FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

            // 模式选择
            var hwModeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            hwModeRow.Children.Add(new TextBlock { Text = "连接方式:", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Width = 70 });
            // 暗灰色 ComboBox：WPF 4.0 Aero 主题的 ComboBox 闭合态由模板的 ButtonChrome 渲染，
            // 忽略 Background 属性，必须完整替换 ControlTemplate 才能生效
            var darkBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            var darkBgHover = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"));
            var darkBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
            var darkCombo = BuildDarkComboTemplate();
            var cmbItemStyle = BuildDarkComboItemStyle(darkBg, darkBgHover);
            var cmbHwMode = new ComboBox {
                Width = 100, Padding = new Thickness(6, 2, 4, 2),
                Background = darkBg, Foreground = Brushes.White, BorderBrush = darkBorder,
                FontSize = 13, ItemContainerStyle = cmbItemStyle, Template = darkCombo
            };
            cmbHwMode.Items.Add("不连接");
            cmbHwMode.Items.Add("串口");
            cmbHwMode.Items.Add("UDP");
            cmbHwMode.SelectedIndex = _hwMode == "serial" ? 1 : (_hwMode == "udp" ? 2 : 0);
            hwModeRow.Children.Add(cmbHwMode);
            hwStack.Children.Add(hwModeRow);

            // 串口参数面板
            var serialPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            serialPanel.Children.Add(new TextBlock { Text = "串口:", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Width = 40 });
            var cmbSerialPort = new ComboBox {
                Width = 90, Padding = new Thickness(6, 2, 4, 2),
                Background = darkBg, Foreground = Brushes.White, BorderBrush = darkBorder,
                FontSize = 13, ItemContainerStyle = cmbItemStyle, Template = BuildDarkComboTemplate()
            };
            foreach (string p in System.IO.Ports.SerialPort.GetPortNames()) cmbSerialPort.Items.Add(p);
            if (cmbSerialPort.Items.Count == 0) cmbSerialPort.Items.Add("COM3");
            cmbSerialPort.Text = _hwSerialPort;
            serialPanel.Children.Add(cmbSerialPort);
            var btnRefreshPorts = new Button { Content = "刷新", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12 };
            btnRefreshPorts.Click += delegate { cmbSerialPort.Items.Clear(); foreach (string p in System.IO.Ports.SerialPort.GetPortNames()) cmbSerialPort.Items.Add(p); };
            serialPanel.Children.Add(btnRefreshPorts);
            hwStack.Children.Add(serialPanel);

            // UDP参数面板
            var udpPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var udpRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            udpRow1.Children.Add(new TextBlock { Text = "目标IP:", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Width = 55 });
            var tbUdpHost = new TextBox { Text = _hwUdpHost, Width = 120, Padding = new Thickness(4, 2, 4, 2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Foreground = Brushes.White, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), FontSize = 13 };
            udpRow1.Children.Add(tbUdpHost);
            var udpRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            udpRow2.Children.Add(new TextBlock { Text = "发送:", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Width = 55 });
            var tbUdpSend = new TextBox { Text = _hwUdpSendPort.ToString(), Width = 60, Padding = new Thickness(4, 2, 4, 2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Foreground = Brushes.White, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), FontSize = 13 };
            udpRow2.Children.Add(tbUdpSend);
            udpRow2.Children.Add(new TextBlock { Text = " 接收:", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            var tbUdpRecv = new TextBox { Text = _hwUdpRecvPort.ToString(), Width = 60, Padding = new Thickness(4, 2, 4, 2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Foreground = Brushes.White, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), FontSize = 13 };
            udpRow2.Children.Add(tbUdpRecv);
            udpPanel.Children.Add(udpRow1);
            udpPanel.Children.Add(udpRow2);
            hwStack.Children.Add(udpPanel);

            // 模式切换可见性
            Action updateHwPanels = () => {
                serialPanel.Visibility = cmbHwMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
                udpPanel.Visibility = cmbHwMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            };
            cmbHwMode.SelectionChanged += (s2, e2) => updateHwPanels();
            updateHwPanels();

            // 连接/断开按钮 + 状态
            var hwBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var btnHwConnect = new Button { Content = "连接硬件", Padding = new Thickness(12, 4, 12, 4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13, FontWeight = FontWeights.Bold };
            var btnHwDisconnect = new Button { Content = "断开", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13 };
            var lblHwStatus = new TextBlock { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                Text = _localBridge != null && _localBridge.IsConnected ? "● " + _localBridge.StatusText : "● 未连接" };
            btnHwConnect.Click += delegate {
                string modeStr = cmbHwMode.SelectedIndex == 1 ? "serial" : (cmbHwMode.SelectedIndex == 2 ? "udp" : "none");
                _hwMode = modeStr; _hwSerialPort = cmbSerialPort.Text.Trim();
                _hwUdpHost = tbUdpHost.Text.Trim();
                int.TryParse(tbUdpSend.Text, out _hwUdpSendPort);
                int.TryParse(tbUdpRecv.Text, out _hwUdpRecvPort);
                ConnectHardware(_hwMode, _hwSerialPort, _hwUdpHost, _hwUdpSendPort, _hwUdpRecvPort);
                SaveSettings();
                lblHwStatus.Text = _localBridge != null && _localBridge.IsConnected ? "● " + _localBridge.StatusText : "● 连接失败";
                lblHwStatus.Foreground = new SolidColorBrush(_localBridge != null && _localBridge.IsConnected
                    ? (Color)ColorConverter.ConvertFromString("#22C55E")
                    : (Color)ColorConverter.ConvertFromString("#EF4444"));
            };
            btnHwDisconnect.Click += delegate {
                _hwMode = "none";
                ConnectHardware("none", "", "", 0, 0);
                SaveSettings();
                lblHwStatus.Text = "● 未连接";
                lblHwStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            };
            hwBtnRow.Children.Add(btnHwConnect);
            hwBtnRow.Children.Add(btnHwDisconnect);
            hwBtnRow.Children.Add(lblHwStatus);
            hwStack.Children.Add(hwBtnRow);
            hwSep.Child = hwStack;
            sp.Children.Add(hwSep);

            // 设备状态管理按钮
            var deviceSep = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 10, 0, 0) };
            var btnDeviceMgr = new Button { Content = "设备状态管理", Padding = new Thickness(0, 8, 0, 8), FontSize = 14, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnDeviceMgr.Click += delegate {
                dlg.DialogResult = false;
                OpenDeviceManager();
            };
            var btnManualMgr = new Button { Content = "手动按键管理", Padding = new Thickness(0, 8, 0, 8), FontSize = 14, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 6, 0, 0) };
            btnManualMgr.Click += delegate { dlg.DialogResult = false; OpenManualButtonManager(); };
            var btnBlindMgr = new Button { Content = "左右盲表数量设置", Padding = new Thickness(0, 8, 0, 8), FontSize = 14, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), Foreground = Brushes.Black, BorderThickness = new Thickness(0), Margin = new Thickness(0, 6, 0, 0) };
            btnBlindMgr.Click += delegate { dlg.DialogResult = false; OpenBlindWatchCountDialog(); };
            var deviceStack = new StackPanel();
            deviceStack.Children.Add(btnDeviceMgr);
            deviceStack.Children.Add(btnManualMgr);
            deviceStack.Children.Add(btnBlindMgr);
            deviceSep.Child = deviceStack;
            sp.Children.Add(deviceSep);

            // 系统硬件 — 连接串口（请求服务器）+ 设备测试切换
            var hwOpsSep = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 10, 0, 0) };
            var hwOpsStack = new StackPanel();
            hwOpsStack.Children.Add(new TextBlock { Text = "系统硬件", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")), FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            var hwOpsRow = new Grid();
            hwOpsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hwOpsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            hwOpsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var btnQuickConn = new Button { Content = "连接串口", Padding = new Thickness(0, 8, 0, 8), FontSize = 13, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnQuickConn.Click += delegate {
                SendCmd("QUICK_CONNECT_SERIAL", null);
                AddLog("已请求服务器连接/断开串口");
                dlg.DialogResult = false;
            };
            Grid.SetColumn(btnQuickConn, 0);
            bool dtNow = (_data != null && _data["testMode"] != null && (bool)_data["testMode"]);
            var btnDeviceTest = new Button {
                Content = dtNow ? "退出测试" : "设备测试",
                Padding = new Thickness(0, 8, 0, 8), FontSize = 13, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dtNow ? "#EF4444" : "#0EA5E9")),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnDeviceTest.Click += delegate {
                bool inTest = (_data != null && _data["testMode"] != null && (bool)_data["testMode"]);
                if (!inTest) {
                    var r = MessageBox.Show("确认进入设备测试模式？\n\n所有触板/出发台/盲表都将打开，硬件来什么数据都直接显示但不计入比赛成绩。\n再点同一按钮退出。",
                        "设备测试", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return;
                }
                SendCmd("DEVICE_TEST_TOGGLE", null);
                AddLog(inTest ? "已请求退出设备测试" : "已请求进入设备测试");
                dlg.DialogResult = false;
            };
            Grid.SetColumn(btnDeviceTest, 2);
            hwOpsRow.Children.Add(btnQuickConn);
            hwOpsRow.Children.Add(btnDeviceTest);
            hwOpsStack.Children.Add(hwOpsRow);
            hwOpsSep.Child = hwOpsStack;
            sp.Children.Add(hwOpsSep);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button { Content = "确定", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);

            // 高度自适应内容，无滚动条
            dlg.Content = sp;

            if (dlg.ShowDialog() == true)
            {
                double holdVal;
                if (double.TryParse(tbFirstHold.Text, out holdVal)) _firstPlaceHoldTime = holdVal;
                _finishPosition = rbRight.IsChecked == true ? "right" : "left";

                // 保存计时参数到本地字段
                _laneCloseTime = double.Parse(tbCloseTime.Text);
                _startBlockCloseDelay = double.Parse(tbSBDelay.Text);
                _resultConfirmCloseDelay = double.Parse(tbConfDelay.Text);
                _falseStartThreshold = double.Parse(tbFSThresh.Text);
                _splitDisplayTime = double.Parse(tbSplitDisp.Text);
                double bigPageVal;
                if (double.TryParse(tbBigPage.Text, out bigPageVal)) {
                    if (bigPageVal < 1) bigPageVal = 1; if (bigPageVal > 60) bigPageVal = 60;
                    _bigDisplayPageInterval = bigPageVal;
                }
                _reactionTimeEnabled = rbRtOn.IsChecked == true;
                _laneOrder = rbOrderRev.IsChecked == true ? "reverse" : "forward";

                var settings = new
                {
                    laneCloseTime = _laneCloseTime,
                    startBlockCloseDelay = _startBlockCloseDelay,
                    resultConfirmCloseDelay = _resultConfirmCloseDelay,
                    falseStartThreshold = _falseStartThreshold,
                    splitDisplayTime = _splitDisplayTime,
                    startPosition = _finishPosition,
                    firstPlaceHoldTime = _firstPlaceHoldTime,
                    leftBlindWatchCount = _leftBlindWatchCount,
                    rightBlindWatchCount = _rightBlindWatchCount,
                    bigDisplayPageInterval = _bigDisplayPageInterval,
                    reactionTimeEnabled = _reactionTimeEnabled,
                    laneOrder = _laneOrder
                };
                SendCmd("SET_LANE_CLOSE_SETTINGS", settings);

                // 保存服务器地址（从设置对话框的输入）
                string sAddr = tbServerHost.Text.Trim();
                if (!string.IsNullOrEmpty(sAddr))
                {
                    string[] addrParts = sAddr.Split(':');
                    _serverHost = addrParts[0];
                    if (addrParts.Length > 1) int.TryParse(addrParts[1], out _serverPort);
                    ServerBox.Text = _serverHost + ":" + _serverPort;
                }

                SaveSettings();
                AddLog(string.Format("参数已更新并保存，第1名停留: {0}s，终点位置: {1}，翻屏: {2}s，反应时: {3}，道次: {4}",
                    _firstPlaceHoldTime, _finishPosition == "left" ? "左端" : "右端",
                    _bigDisplayPageInterval, _reactionTimeEnabled ? "打开" : "关闭",
                    _laneOrder == "reverse" ? "逆序9→0" : "正序0→9"));
            }
        }

        private TextBox AddParamRow(StackPanel parent, string label, string value, string unit)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            var lbl = new TextBlock { Text = label, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            var tb = new TextBox { Text = value, Padding = new Thickness(4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Foreground = Brushes.White, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), FontSize = 15, TextAlignment = TextAlignment.Center };
            Grid.SetColumn(tb, 1);
            var unitTb = new TextBlock { Text = unit, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(unitTb, 2);
            row.Children.Add(lbl);
            row.Children.Add(tb);
            row.Children.Add(unitTb);
            parent.Children.Add(row);
            return tb;
        }

        // ═══════ 左右盲表数量设置 ═══════
        private void OpenBlindWatchCountDialog()
        {
            var dlg = new Window
            {
                Title = "左右盲表数量设置",
                Width = 400, Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "左右盲表数量设置", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock {
                Text = "每道使用的盲表数量（1-3）。修改后将通过服务器同步到所有计时控制台与硬件计时控制器。",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
            });

            Func<string, int, ComboBox> mkRow = delegate (string label, int sel) {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.Children.Add(new TextBlock {
                    Text = label, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    FontSize = 15, VerticalAlignment = VerticalAlignment.Center
                });
                var cb = new ComboBox {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                    Foreground = Brushes.Black, FontSize = 15, FontWeight = FontWeights.Bold
                };
                cb.Items.Add("1"); cb.Items.Add("2"); cb.Items.Add("3");
                cb.SelectedIndex = (sel < 1 ? 1 : (sel > 3 ? 3 : sel)) - 1;
                Grid.SetColumn(cb, 1);
                row.Children.Add(cb);
                sp.Children.Add(row);
                return cb;
            };
            var cbLeft = mkRow("左边 盲表数量", _leftBlindWatchCount);
            var cbRight = mkRow("右边 盲表数量", _rightBlindWatchCount);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnCancel = new Button {
                Content = "取消", Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0)
            };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button {
                Content = "确定", Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold
            };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);

            dlg.Content = sp;
            if (dlg.ShowDialog() == true)
            {
                int newLeft = cbLeft.SelectedIndex + 1;
                int newRight = cbRight.SelectedIndex + 1;
                if (newLeft != _leftBlindWatchCount || newRight != _rightBlindWatchCount)
                {
                    _leftBlindWatchCount = newLeft;
                    _rightBlindWatchCount = newRight;
                    // 通过服务器同步：发送完整 SET_LANE_CLOSE_SETTINGS（仅含已知字段+新增计数）
                    var settings = new
                    {
                        laneCloseTime = _laneCloseTime,
                        startBlockCloseDelay = _startBlockCloseDelay,
                        resultConfirmCloseDelay = _resultConfirmCloseDelay,
                        falseStartThreshold = _falseStartThreshold,
                        splitDisplayTime = _splitDisplayTime,
                        startPosition = _finishPosition,
                        firstPlaceHoldTime = _firstPlaceHoldTime,
                        leftBlindWatchCount = _leftBlindWatchCount,
                        rightBlindWatchCount = _rightBlindWatchCount,
                        bigDisplayPageInterval = _bigDisplayPageInterval,
                        reactionTimeEnabled = _reactionTimeEnabled
                    };
                    SendCmd("SET_LANE_CLOSE_SETTINGS", settings);
                    SaveSettings();
                    AddLog(string.Format("盲表数量更新：左 {0}，右 {1}（已通过服务器同步到三端与硬件）", newLeft, newRight));
                }
            }
        }

        // ═══════ 设备状态管理 ═══════
        private void OpenDeviceManager()
        {
            if (_data == null || _data["swimmers"] == null) { MessageBox.Show("暂无泳道数据"); return; }
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers == null || swimmers.Count == 0) { MessageBox.Show("暂无泳道数据"); return; }

            string[] deviceKeys = { "leftTouchpad", "leftStartBlock", "leftBlindWatch1", "leftBlindWatch2", "leftBlindWatch3",
                                    "rightTouchpad", "rightStartBlock", "rightBlindWatch1", "rightBlindWatch2", "rightBlindWatch3" };
            string[] brokenKeys = { "leftTouchpadBroken", "leftStartBlockBroken", "leftBlindWatch1Broken", "leftBlindWatch2Broken", "leftBlindWatch3Broken",
                                    "rightTouchpadBroken", "rightStartBlockBroken", "rightBlindWatch1Broken", "rightBlindWatch2Broken", "rightBlindWatch3Broken" };
            string[] colHeaders = { "左触板坏", "左出发台坏", "左盲1坏", "左盲2坏", "左盲3坏",
                                    "右触板坏", "右出发台坏", "右盲1坏", "右盲2坏", "右盲3坏" };

            var dlg = new Window
            {
                Title = "设备状态管理",
                Width = 950, Height = 580,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.CanResize
            };

            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            mainGrid.Children.Add(new TextBlock { Text = "泳道设备好/坏状态设置", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });

            // DataGrid with CheckBox columns
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = false,
                FontSize = 14,
                RowHeight = 30,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                HeadersVisibility = DataGridHeadersVisibility.Column
            };

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "道次", Binding = new System.Windows.Data.Binding("Lane"), Width = new DataGridLength(55), IsReadOnly = true });

            // 构建每道数据，用CheckBox列
            var items = new List<DeviceRowItem>();
            foreach (JObject sw in swimmers)
            {
                int lane = sw["lane"] != null ? (int)sw["lane"] : 0;
                var ds = sw["deviceStatus"] as JObject;
                var item = new DeviceRowItem { Lane = lane };
                item.LeftTouchpadBroken = ds != null && ds[brokenKeys[0]] != null && (bool)ds[brokenKeys[0]];
                item.LeftStartBlockBroken = ds != null && ds[brokenKeys[1]] != null && (bool)ds[brokenKeys[1]];
                item.LeftBlindWatch1Broken = ds != null && ds[brokenKeys[2]] != null && (bool)ds[brokenKeys[2]];
                item.LeftBlindWatch2Broken = ds != null && ds[brokenKeys[3]] != null && (bool)ds[brokenKeys[3]];
                item.LeftBlindWatch3Broken = ds != null && ds[brokenKeys[4]] != null && (bool)ds[brokenKeys[4]];
                item.RightTouchpadBroken = ds != null && ds[brokenKeys[5]] != null && (bool)ds[brokenKeys[5]];
                item.RightStartBlockBroken = ds != null && ds[brokenKeys[6]] != null && (bool)ds[brokenKeys[6]];
                item.RightBlindWatch1Broken = ds != null && ds[brokenKeys[7]] != null && (bool)ds[brokenKeys[7]];
                item.RightBlindWatch2Broken = ds != null && ds[brokenKeys[8]] != null && (bool)ds[brokenKeys[8]];
                item.RightBlindWatch3Broken = ds != null && ds[brokenKeys[9]] != null && (bool)ds[brokenKeys[9]];
                items.Add(item);
            }

            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "左触板坏", Binding = new System.Windows.Data.Binding("LeftTouchpadBroken"), Width = new DataGridLength(70) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "左出发台坏", Binding = new System.Windows.Data.Binding("LeftStartBlockBroken"), Width = new DataGridLength(80) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "左盲1坏", Binding = new System.Windows.Data.Binding("LeftBlindWatch1Broken"), Width = new DataGridLength(65) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "左盲2坏", Binding = new System.Windows.Data.Binding("LeftBlindWatch2Broken"), Width = new DataGridLength(65) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "左盲3坏", Binding = new System.Windows.Data.Binding("LeftBlindWatch3Broken"), Width = new DataGridLength(65) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "右触板坏", Binding = new System.Windows.Data.Binding("RightTouchpadBroken"), Width = new DataGridLength(70) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "右出发台坏", Binding = new System.Windows.Data.Binding("RightStartBlockBroken"), Width = new DataGridLength(80) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "右盲1坏", Binding = new System.Windows.Data.Binding("RightBlindWatch1Broken"), Width = new DataGridLength(65) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "右盲2坏", Binding = new System.Windows.Data.Binding("RightBlindWatch2Broken"), Width = new DataGridLength(65) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "右盲3坏", Binding = new System.Windows.Data.Binding("RightBlindWatch3Broken"), Width = new DataGridLength(65) });
            dataGrid.ItemsSource = items;

            Grid.SetRow(dataGrid, 1);
            mainGrid.Children.Add(dataGrid);

            // 底部按钮
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnAllGood = new Button { Content = "全部设为好", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
            btnAllGood.Click += delegate {
                foreach (var item in items) {
                    item.LeftTouchpadBroken = false; item.LeftStartBlockBroken = false;
                    item.LeftBlindWatch1Broken = false; item.LeftBlindWatch2Broken = false; item.LeftBlindWatch3Broken = false;
                    item.RightTouchpadBroken = false; item.RightStartBlockBroken = false;
                    item.RightBlindWatch1Broken = false; item.RightBlindWatch2Broken = false; item.RightBlindWatch3Broken = false;
                }
                dataGrid.Items.Refresh();
            };
            var btnAllBad = new Button { Content = "全部设为坏", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 16, 0) };
            btnAllBad.Click += delegate {
                foreach (var item in items) {
                    item.LeftTouchpadBroken = true; item.LeftStartBlockBroken = true;
                    item.LeftBlindWatch1Broken = true; item.LeftBlindWatch2Broken = true; item.LeftBlindWatch3Broken = true;
                    item.RightTouchpadBroken = true; item.RightStartBlockBroken = true;
                    item.RightBlindWatch1Broken = true; item.RightBlindWatch2Broken = true; item.RightBlindWatch3Broken = true;
                }
                dataGrid.Items.Refresh();
            };
            var btnOK = new Button { Content = "确定", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
            btnOK.Click += delegate {
                // 发送所有设备状态到服务器
                foreach (var item in items) {
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "leftTouchpad", status = item.LeftTouchpadBroken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "leftStartBlock", status = item.LeftStartBlockBroken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "leftBlindWatch1", status = item.LeftBlindWatch1Broken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "leftBlindWatch2", status = item.LeftBlindWatch2Broken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "leftBlindWatch3", status = item.LeftBlindWatch3Broken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "rightTouchpad", status = item.RightTouchpadBroken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "rightStartBlock", status = item.RightStartBlockBroken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "rightBlindWatch1", status = item.RightBlindWatch1Broken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "rightBlindWatch2", status = item.RightBlindWatch2Broken ? "broken" : "normal" });
                    SendCmd("SET_DEVICE_STATUS", new { lane = item.Lane, device = "rightBlindWatch3", status = item.RightBlindWatch3Broken ? "broken" : "normal" });
                }
                AddLog("设备状态已更新");
                dlg.Close();
            };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6) };
            btnCancel.Click += delegate { dlg.Close(); };

            btnPanel.Children.Add(btnAllGood);
            btnPanel.Children.Add(btnAllBad);
            btnPanel.Children.Add(btnOK);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 2);
            mainGrid.Children.Add(btnPanel);

            dlg.Content = mainGrid;
            dlg.ShowDialog();
        }

        // 设备状态数据行
        private class DeviceRowItem
        {
            public int Lane { get; set; }
            public bool LeftTouchpadBroken { get; set; }
            public bool LeftStartBlockBroken { get; set; }
            public bool LeftBlindWatch1Broken { get; set; }
            public bool LeftBlindWatch2Broken { get; set; }
            public bool LeftBlindWatch3Broken { get; set; }
            public bool RightTouchpadBroken { get; set; }
            public bool RightStartBlockBroken { get; set; }
            public bool RightBlindWatch1Broken { get; set; }
            public bool RightBlindWatch2Broken { get; set; }
            public bool RightBlindWatch3Broken { get; set; }
        }

        // ═══════ 手动按键管理 ═══════
        private void OpenManualButtonManager()
        {
            if (_data == null || _data["swimmers"] == null) { MessageBox.Show("暂无泳道数据"); return; }
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers == null || swimmers.Count == 0) { MessageBox.Show("暂无泳道数据"); return; }

            var dlg = new Window
            {
                Title = "手动按键管理", Width = 500, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.CanResize
            };

            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            mainGrid.Children.Add(new TextBlock { Text = "手动按键 用/不用 设置", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false,
                IsReadOnly = false, FontSize = 14, RowHeight = 30,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                HeadersVisibility = DataGridHeadersVisibility.Column
            };

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "道次", Binding = new System.Windows.Data.Binding("Lane"), Width = new DataGridLength(60), IsReadOnly = true });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "左手动 用", Binding = new System.Windows.Data.Binding("LeftEnabled"), Width = new DataGridLength(100) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "右手动 用", Binding = new System.Windows.Data.Binding("RightEnabled"), Width = new DataGridLength(100) });

            var items = new List<ManualBtnItem>();
            foreach (JObject sw in swimmers)
            {
                int lane = sw["lane"] != null ? (int)sw["lane"] : 0;
                var mb = sw["manualButton"] as JObject;
                bool leftEn = mb != null && mb["leftEnabled"] != null && (bool)mb["leftEnabled"];
                bool rightEn = mb != null && mb["rightEnabled"] != null && (bool)mb["rightEnabled"];
                items.Add(new ManualBtnItem { Lane = lane, LeftEnabled = leftEn, RightEnabled = rightEn });
            }
            dataGrid.ItemsSource = items;
            Grid.SetRow(dataGrid, 1);
            mainGrid.Children.Add(dataGrid);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnAllOn = new Button { Content = "全部设为用", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
            btnAllOn.Click += delegate { foreach (var it in items) { it.LeftEnabled = true; it.RightEnabled = true; } dataGrid.Items.Refresh(); };
            var btnAllOff = new Button { Content = "全部设为不用", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 16, 0) };
            btnAllOff.Click += delegate { foreach (var it in items) { it.LeftEnabled = false; it.RightEnabled = false; } dataGrid.Items.Refresh(); };
            var btnOK = new Button { Content = "确认", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
            btnOK.Click += delegate {
                for (int i = 0; i < items.Count; i++) {
                    SendCmd("SET_MANUAL_STATUS", new { lane = items[i].Lane, leftEnabled = items[i].LeftEnabled, rightEnabled = items[i].RightEnabled });
                }
                AddLog("手动按键设置已发送");
                dlg.Close();
            };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6) };
            btnCancel.Click += delegate { dlg.Close(); };
            btnPanel.Children.Add(btnAllOn); btnPanel.Children.Add(btnAllOff);
            btnPanel.Children.Add(btnOK); btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 2);
            mainGrid.Children.Add(btnPanel);

            dlg.Content = mainGrid;
            dlg.ShowDialog();
        }

        private class ManualBtnItem
        {
            public int Lane { get; set; }
            public bool LeftEnabled { get; set; }
            public bool RightEnabled { get; set; }
        }

        // ═══════ 键盘快捷键 ═══════
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Skip if focus is on a TextBox or ComboBox
            if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox) return;

            if (e.Key == Key.F5) {
                if (IsCurrentHeatConfirmed()) {
                    MessageBox.Show("当前组已确认成绩并锁定，不能重新开始比赛。\n请在赛程导航中切换到下一组。",
                        "操作被阻止", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AddLog("当前组已确认成绩，不能再次开始");
                } else {
                    SendCmd("READY");
                }
                e.Handled = true;
            }
            else if (e.Key == Key.F6) { SendCmd("START_RACE"); e.Handled = true; }
            else if (e.Key == Key.F7)
            {
                e.Handled = true;
                // F7 计时复位走与按钮一致的逻辑
                TimerReset_Click(null, null);
            }
            else if (e.Key == Key.Return)
            {
                Confirm_Click(null, null);
            }
            else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Control) {
                if (!BlockIfRacing("切换到上一组")) SendCmd("PREV_HEAT");
                e.Handled = true;
            }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Control) {
                if (!BlockIfRacing("切换到下一组")) SendCmd("NEXT_HEAT");
                e.Handled = true;
            }
            else if (e.Key == Key.D && _selectedLane >= 0 && Keyboard.Modifiers == ModifierKeys.None) { SendCmd("MARK_DNS", new { lane = _selectedLane }); }
            else if (e.Key == Key.F && _selectedLane >= 0 && Keyboard.Modifiers == ModifierKeys.None) { SendCmd("MARK_DNF", new { lane = _selectedLane }); }
            else if (e.Key == Key.Q && _selectedLane >= 0) { SendCmd("MARK_DSQ", new { lane = _selectedLane }); }
            // Number keys: select lane
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None) { _selectedLane = e.Key - Key.D0; _lastSplitCount = -1; UpdateTimingSourceInfo(); RenderLanes(_data != null ? _data["swimmers"] as JArray : null); }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.None) { _selectedLane = 0; _lastSplitCount = -1; UpdateTimingSourceInfo(); RenderLanes(_data != null ? _data["swimmers"] as JArray : null); }
            // Shift+1~0 = left manual touch
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.Shift) { SendCmd("MANUAL_TOUCH_LEFT", new { lane = e.Key - Key.D0 }); }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Shift) { SendCmd("MANUAL_TOUCH_LEFT", new { lane = 10 }); }
            // Ctrl+1~0 = right manual touch
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SendCmd("MANUAL_TOUCH_RIGHT", new { lane = e.Key - Key.D0 }); }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SendCmd("MANUAL_TOUCH_RIGHT", new { lane = 10 }); }
        }

        private void AddLog(string msg)
        {
            string entry = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), msg);
            LogList.Items.Add(entry);
            if (LogList.Items.Count > 200) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(entry);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("确定要退出远程计时控制台？", "确认退出", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            SaveSettings();
            if (_refreshTimer != null) _refreshTimer.Stop();
            if (_reconnectTimer != null) _reconnectTimer.Stop();
            if (_ws != null) _ws.Close();
            if (_localBridge != null) { _localBridge.Dispose(); _localBridge = null; }
        }
    }
}
