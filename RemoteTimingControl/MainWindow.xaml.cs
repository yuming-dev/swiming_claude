using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // 保存的计时参数（本地缓存，用于设置对话框默认值和持久化）
        private double _laneCloseTime = 20;
        private double _startBlockCloseDelay = 3;
        private double _resultConfirmCloseDelay = 3;
        private double _falseStartThreshold = 0.10;
        private double _splitDisplayTime = 5;

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
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(500);

            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(3);
            _reconnectTimer.Tick += delegate { TryReconnect(); };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // 加载保存的参数
            LoadSettings();
            ServerBox.Text = _serverHost + ":" + _serverPort;

            // 窗口加载完成后自动连接服务器
            Loaded += delegate { DoConnect(); };
        }

        // ═══════ 网络地址持久化（参数全部从服务器获取） ═══════
        private string GetSettingsPath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteTimingServer.txt");
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsPath();
                if (!System.IO.File.Exists(path)) return;
                string addr = System.IO.File.ReadAllText(path, Encoding.UTF8).Trim();
                if (string.IsNullOrEmpty(addr)) return;
                string[] parts = addr.Split(':');
                _serverHost = parts[0];
                if (parts.Length > 1) int.TryParse(parts[1], out _serverPort);
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                string addr = _serverHost + ":" + _serverPort;
                System.IO.File.WriteAllText(GetSettingsPath(), addr, Encoding.UTF8);
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
            if (hrs > 0) return string.Format("{0}:{1:D2}:{2:D2}.{3}", hrs, mins, secs, tenths);
            if (mins > 0) return string.Format("{0}:{1:D2}.{2}", mins, secs, tenths);
            return string.Format("{0}.{1}", secs, tenths);
        }

        private string GetLocalRunningTime()
        {
            if (_localTimerStart == DateTime.MinValue) return _data != null && _data["runningTime"] != null ? _data["runningTime"].ToString() : "0.0";
            double elapsed = (DateTime.Now - _localTimerStart).TotalSeconds;
            return FormatLocalTime(elapsed);
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_data == null) return;
            string st = _data["raceState"] != null ? _data["raceState"].ToString().ToLower() : "waiting";
            if (st == "racing" || st == "finished")
            {
                RenderLanes(_data["swimmers"] as JArray);
                UpdateRunningTimeDisplay();
            }
        }

        private void UpdateRunningTimeDisplay()
        {
            double holdMs = _firstPlaceHoldTime * 1000;
            if (_firstPlaceShowStart != DateTime.MinValue &&
                (DateTime.Now - _firstPlaceShowStart).TotalMilliseconds < holdMs &&
                !string.IsNullOrEmpty(_firstPlaceFinishTime))
            {
                RunningTime.Text = _firstPlaceFinishTime;
                RunningTime.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
            }
            else
            {
                RunningTime.Text = GetLocalRunningTime();
                RunningTime.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
        }

        // ═══════ 连接管理 ═══════
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

            // State indicator
            Color stateColor;
            switch (state)
            {
                case "ready": stateColor = (Color)ColorConverter.ConvertFromString("#3B82F6"); break;
                case "racing": stateColor = (Color)ColorConverter.ConvertFromString("#22C55E"); break;
                case "finished": stateColor = (Color)ColorConverter.ConvertFromString("#EF4444"); break;
                default: stateColor = (Color)ColorConverter.ConvertFromString("#F59E0B"); break;
            }
            StateIndicator.Fill = new SolidColorBrush(stateColor);

            string stateText;
            switch (state)
            {
                case "waiting": stateText = "等待"; break;
                case "ready": stateText = "就位"; break;
                case "racing": stateText = "比赛中"; break;
                case "finished": stateText = "已完赛"; break;
                default: stateText = state; break;
            }
            bool resultConfirmed = _data["resultConfirmed"] != null && (bool)_data["resultConfirmed"];
            if (resultConfirmed) stateText = "已确认 — 请选择下一组";
            StateLabel.Text = stateText;

            // Local timer sync
            if (state == "waiting" || state == "ready")
            {
                _localTimerStart = DateTime.MinValue;
                _localTimerSynced = false;
            }
            if ((state == "racing" || state == "finished") && !_localTimerSynced && _data["runningTime"] != null)
            {
                double serverSec = ParseServerTime(_data["runningTime"].ToString());
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
            DetectFirstPlace(swimmers);
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
                    {
                        fsText += string.Format("道{0} FS! ", sw["lane"]);
                    }
                }
            }
            FalseStartInfo.Text = fsText;

            // Current race info
            string curInfo = string.Format("{0}子 {1} {2} 第{3}/{4}组",
                _data["currentGender"] ?? "", _data["currentEvent"] ?? "",
                _data["currentStage"] ?? "", _data["currentHeat"] ?? "0", _data["totalHeats"] ?? "0");
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

        private void DetectFirstPlace(JArray swimmers)
        {
            if (swimmers == null) return;
            JObject leader = null;
            int leaderSplitCount = 0;
            foreach (JObject sw in swimmers)
            {
                if (sw["status"] != null && sw["status"].ToString() != "") continue;
                int sc = 0;
                var splits = sw["splits"] as JArray;
                if (splits != null) sc = splits.Count;
                bool isFinished = sw["isFinished"] != null && (bool)sw["isFinished"];
                if (isFinished) sc = 9999;
                if (sc > leaderSplitCount || (sc == leaderSplitCount && leader != null &&
                    sw["rank"] != null && (int)sw["rank"] > 0 &&
                    (leader["rank"] == null || (int)sw["rank"] < (int)leader["rank"])))
                {
                    leaderSplitCount = sc;
                    leader = sw;
                }
            }
            if (leader != null)
            {
                int leaderSc = 0;
                var lSplits = leader["splits"] as JArray;
                if (lSplits != null) leaderSc = lSplits.Count;
                bool lFinished = leader["isFinished"] != null && (bool)leader["isFinished"];
                if (lFinished && leader["finalTime"] != null) leaderSc = -1;
                if (leaderSc != _firstPlaceDetectedRank)
                {
                    _firstPlaceDetectedRank = leaderSc;
                    if (lFinished && leader["finalTime"] != null)
                    {
                        _firstPlaceFinishTime = leader["finalTime"].ToString();
                    }
                    else if (lSplits != null && lSplits.Count > 0)
                    {
                        var last = lSplits[lSplits.Count - 1] as JObject;
                        if (last != null && last["cumulative"] != null)
                            _firstPlaceFinishTime = last["cumulative"].ToString();
                    }
                    if (!string.IsNullOrEmpty(_firstPlaceFinishTime))
                        _firstPlaceShowStart = DateTime.Now;
                }
            }
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

            // 从纪录数据中查找当前项目的WR和CR
            string wrTime = "", crTime = "", wrHolder = "", crHolder = "";
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

                    if (rType.Contains("世界") && rTimeSec > 0)
                    {
                        wrTime = rTime;
                        wrHolder = rHolder;
                    }
                    else if (rType.Contains("赛会") && rTimeSec > 0)
                    {
                        crTime = rTime;
                        crHolder = rHolder;
                    }
                }
            }

            // 始终显示WR和CR标签，有数据则显示成绩，无数据显示空
            var sb = new System.Text.StringBuilder();
            sb.Append("WR: ");
            sb.Append(!string.IsNullOrEmpty(wrTime) ? wrTime : "---");
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
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });  // 2: 左设备
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 3: 姓名+进度
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });  // 4: 右设备
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 5: 右发令
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });  // 6: 成绩信息

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

            // Left device labels
            var leftLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftLabels.Children.Add(MakeHeaderLabel("[T]", 80));
            leftLabels.Children.Add(MakeHeaderLabel("盲1", 26));
            leftLabels.Children.Add(MakeHeaderLabel("盲2", 26));
            leftLabels.Children.Add(MakeHeaderLabel("盲3", 26));
            leftLabels.Children.Add(MakeHeaderLabel("出发", 26));
            leftLabels.Children.Add(MakeHeaderLabel("触板", 26));
            leftLabels.Children.Add(MakeHeaderLabel("圈", 28));
            Grid.SetColumn(leftLabels, 2);
            PoolHeader.Children.Add(leftLabels);

            // Mid header
            var midHeaderPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            midHeaderPanel.Children.Add(MakeHeaderLabel("姓名/代表队", 120));
            midHeaderPanel.Children.Add(new TextBlock { Text = "方向/进度", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(midHeaderPanel, 3);
            PoolHeader.Children.Add(midHeaderPanel);

            // Right device labels
            var rightLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rightLabels.Children.Add(MakeHeaderLabel("圈", 28));
            rightLabels.Children.Add(MakeHeaderLabel("触板", 26));
            rightLabels.Children.Add(MakeHeaderLabel("出发", 26));
            rightLabels.Children.Add(MakeHeaderLabel("盲1", 26));
            rightLabels.Children.Add(MakeHeaderLabel("盲2", 26));
            rightLabels.Children.Add(MakeHeaderLabel("盲3", 26));
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

            string curGender = _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            string curEvent = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            string curStage = _data["currentStage"] != null ? _data["currentStage"].ToString() : "";
            int curHeat = _data["currentHeat"] != null ? (int)_data["currentHeat"] : 0;

            string hash = schedule.Count + "|" + curGender + "|" + curEvent + "|" + curStage + "|" + curHeat + "|" + _selectedHeatKey;
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
                        string gender = ev["gender"] != null ? ev["gender"].ToString() : "";
                        string eventName = ev["eventName"] != null ? ev["eventName"].ToString() : "";
                        string stage = ev["stage"] != null ? ev["stage"].ToString() : "";
                        int heatCount = ev["heatCount"] != null ? (int)ev["heatCount"] : 1;
                        if (heatCount < 1) heatCount = 1;
                        bool allDone = ev["allConfirmed"] != null && (bool)ev["allConfirmed"];
                        var heatConfirmed = ev["heatConfirmed"] as JArray;

                        string evHeader = string.Format("{0} {1} {2}", gender, eventName, stage);
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

                                string tag = string.Format("{0}|{1}|{2}|{3}", gender, eventName, stage, h);
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
                                        gender == curGender && eventName == curEvent && stage == curStage && h == curHeat)
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

        private void ScheduleTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = ScheduleTree.SelectedItem as TreeViewItem;
            if (item == null || item.Tag == null) return;
            string tag = item.Tag.ToString();
            string[] parts = tag.Split('|');
            if (parts.Length >= 4)
            {
                string gender = parts[0];
                string eventName = parts[1];
                string stage = parts[2];
                int heat;
                if (int.TryParse(parts[3], out heat))
                {
                    _selectedHeatKey = tag;
                    SendCmd("SET_GENDER", gender);
                    SendCmd("SET_EVENT", eventName);
                    SendCmd("SET_STAGE", stage);
                    SendCmd("SET_HEAT", heat);
                    AddLog(string.Format("选择: {0} {1} {2} 第{3}组", gender, eventName, stage, heat));
                    _lastScheduleHash = ""; // Force refresh
                }
            }
        }

        // ═══════ Lane Rendering ═══════
        private void RenderLanes(JArray swimmers)
        {
            LanePanel.Children.Clear();
            if (swimmers == null) return;

            bool isRelay = false;
            string curEventStr = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            if (curEventStr.Contains("接力")) isRelay = true;

            foreach (JObject sw in swimmers)
            {
                int lane = sw["lane"] != null ? (int)sw["lane"] : 0;
                var ds = sw["deviceStatus"] as JObject;
                bool isFalseStart = sw["isFalseStart"] != null && (bool)sw["isFalseStart"];
                bool isFinished = sw["isFinished"] != null && (bool)sw["isFinished"];

                var row = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(0, 0, 0, 0),
                    Height = 56
                };

                if (isFalseStart)
                {
                    row.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    row.BorderThickness = new Thickness(1);
                }
                if (lane == _selectedLane)
                {
                    row.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                    row.BorderThickness = new Thickness(1);
                }

                var grid = new Grid();
                // Columns: laneNum, startIndL, leftDevices, mid, rightDevices, startIndR, infoArea
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });   // 0: 道次
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 1: 左发令指示
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });  // 2: 左设备
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 3: 姓名+进度
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });  // 4: 右设备
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 5: 右发令指示
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });  // 6: 成绩信息

                int capturedLane = lane;

                // Lane number (column 0, leftmost)
                var laneNumTb = new TextBlock
                {
                    Text = lane.ToString(),
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(laneNumTb, 0);
                grid.Children.Add(laneNumTb);

                // Left start indicator (green bar)
                var leftStartInd = new Border
                {
                    Width = 8,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 2, 0, 2),
                    Background = _startPosition == "left" ?
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")) :
                        Brushes.Transparent
                };
                Grid.SetColumn(leftStartInd, 1);
                grid.Children.Add(leftStartInd);

                // Left devices
                var leftDevices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                var touchBtnL = new Button
                {
                    Content = "T", Width = 80, Height = 30, FontSize = 15,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                touchBtnL.PreviewMouseLeftButtonDown += delegate { SendCmd("MANUAL_TOUCH_LEFT", new { lane = capturedLane }); };
                leftDevices.Children.Add(touchBtnL);
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftBlindWatch1")));
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftBlindWatch2")));
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftBlindWatch3")));
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftStartBlock")));
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftTouchpad")));
                // Left touch remaining counter
                string leftRemainStr = "";
                if (sw["leftTouchRemain"] != null) leftRemainStr = sw["leftTouchRemain"].ToString();
                bool leftRemainActive = false;
                int leftRemainVal;
                if (int.TryParse(leftRemainStr, out leftRemainVal) && leftRemainVal > 0) leftRemainActive = true;
                leftDevices.Children.Add(new TextBlock
                {
                    Text = leftRemainStr,
                    Width = 28,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(leftRemainActive ?
                        (Color)ColorConverter.ConvertFromString("#F59E0B") :
                        (Color)ColorConverter.ConvertFromString("#475569"))
                });
                Grid.SetColumn(leftDevices, 2);
                grid.Children.Add(leftDevices);

                // Mid panel — DockPanel让进度条填满剩余空间（道次已在最左列）
                var midPanel = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };

                // Swimmer info - show differently for relay
                var infoStack = new StackPanel { Width = 120 };
                if (isRelay)
                {
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = sw["country"] != null ? sw["country"].ToString() : "",
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        FontSize = 16
                    });
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = GetRelayCurrentLegName(sw),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                        FontSize = 14
                    });
                }
                else
                {
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = sw["name"] != null ? sw["name"].ToString() : "",
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        FontSize = 16
                    });
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = sw["country"] != null ? sw["country"].ToString() : "",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                        FontSize = 14
                    });
                }
                DockPanel.SetDock(infoStack, Dock.Left);
                midPanel.Children.Add(infoStack);

                // Direction and progress track
                string swDir = sw["direction"] != null ? sw["direction"].ToString() : (_startPosition == "left" ? "→" : "←");
                string dir = swDir == "←" ? "returning" : "going";
                string status = sw["status"] != null ? sw["status"].ToString() : "";

                var trackBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A2332")),
                    CornerRadius = new CornerRadius(4),
                    Height = 22,
                    Margin = new Thickness(6, 0, 6, 0),
                    Padding = new Thickness(4, 0, 4, 0),
                    MinWidth = 60
                };
                var trackText = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 15,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = dir == "returning" ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                trackText.Text = BuildArrows(sw, swDir, dir);
                if (isFinished)
                    trackText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                else if (dir == "returning")
                    trackText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
                else
                    trackText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                trackBorder.Child = trackText;
                midPanel.Children.Add(trackBorder);

                Grid.SetColumn(midPanel, 3);
                grid.Children.Add(midPanel);

                // Right devices
                var rightDevices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                // Right touch remaining counter
                string rightRemainStr = "";
                if (sw["rightTouchRemain"] != null) rightRemainStr = sw["rightTouchRemain"].ToString();
                bool rightRemainActive = false;
                int rightRemainVal;
                if (int.TryParse(rightRemainStr, out rightRemainVal) && rightRemainVal > 0) rightRemainActive = true;
                rightDevices.Children.Add(new TextBlock
                {
                    Text = rightRemainStr,
                    Width = 28,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(rightRemainActive ?
                        (Color)ColorConverter.ConvertFromString("#F59E0B") :
                        (Color)ColorConverter.ConvertFromString("#475569"))
                });
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightTouchpad")));
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightStartBlock")));
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightBlindWatch1")));
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightBlindWatch2")));
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightBlindWatch3")));
                var touchBtnR = new Button
                {
                    Content = "T", Width = 80, Height = 30, FontSize = 15,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                touchBtnR.PreviewMouseLeftButtonDown += delegate { SendCmd("MANUAL_TOUCH_RIGHT", new { lane = capturedLane }); };
                rightDevices.Children.Add(touchBtnR);
                Grid.SetColumn(rightDevices, 4);
                grid.Children.Add(rightDevices);

                // Right start indicator (green bar)
                var rightStartInd = new Border
                {
                    Width = 8,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 2, 0, 2),
                    Background = _startPosition == "right" ?
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")) :
                        Brushes.Transparent
                };
                Grid.SetColumn(rightStartInd, 5);
                grid.Children.Add(rightStartInd);

                // Info area
                var infoArea = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                // 反应时间：与分段成绩一样显示后消隐
                string rtVal = sw["reactionTime"] != null ? sw["reactionTime"].ToString() : "";
                string rtDisplay = "";
                if (!string.IsNullOrEmpty(rtVal)) {
                    if (!_laneSplitState.ContainsKey(lane)) _laneSplitState[lane] = new SplitState();
                    var ss = _laneSplitState[lane];
                    if (rtVal != ss.LastReaction) {
                        ss.LastReaction = rtVal;
                        ss.ReactionShowTime = DateTime.Now;
                    }
                    if (ss.ReactionShowTime > DateTime.MinValue) {
                        double dispSec = _splitDisplayTime > 0 ? _splitDisplayTime : 5;
                        if ((DateTime.Now - ss.ReactionShowTime).TotalSeconds < dispSec) rtDisplay = rtVal;
                    }
                }
                infoArea.Children.Add(new TextBlock
                {
                    Text = rtDisplay,
                    Width = 60,
                    FontSize = 18,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    FontFamily = new FontFamily("Consolas")
                });

                // Final time / split time with timed display
                string displayTime = GetSplitOrFinalTime(sw);
                infoArea.Children.Add(new TextBlock
                {
                    Text = displayTime,
                    Width = 115,
                    FontSize = 21,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    FontFamily = new FontFamily("Consolas")
                });

                int rank = sw["rank"] != null ? (int)sw["rank"] : 0;
                Color rankColor = Colors.White;
                if (rank == 1) rankColor = (Color)ColorConverter.ConvertFromString("#F59E0B");
                else if (rank == 2) rankColor = (Color)ColorConverter.ConvertFromString("#C0C0C0");
                else if (rank == 3) rankColor = (Color)ColorConverter.ConvertFromString("#CD7F32");
                infoArea.Children.Add(new TextBlock
                {
                    Text = rank > 0 ? rank.ToString() : "",
                    Width = 44,
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(rankColor),
                    TextAlignment = TextAlignment.Center,
                    FontFamily = new FontFamily("Consolas")
                });

                // Remark column (isFalseStart -> DSQ, or status)
                string remarkText = "";
                if (isFalseStart) remarkText = "DSQ";
                else if (!string.IsNullOrEmpty(status)) remarkText = status;
                infoArea.Children.Add(new TextBlock
                {
                    Text = remarkText,
                    Width = 40,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                });

                Grid.SetColumn(infoArea, 6);
                grid.Children.Add(infoArea);

                row.Child = grid;
                row.MouseLeftButtonDown += delegate
                {
                    _selectedLane = capturedLane;
                    _lastSplitCount = -1; // 切换泳道时强制重建分段列表
                    LaneInput.Text = capturedLane.ToString();
                    UpdateTimingSourceInfo();
                    RenderLanes(_data != null ? _data["swimmers"] as JArray : null);
                };
                LanePanel.Children.Add(row);
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
            int curSplitCount = splits != null ? splits.Count : 0;

            SplitState ls;
            if (!_laneSplitState.TryGetValue(lane, out ls))
            {
                ls = new SplitState { SplitCount = 0, ShowTime = DateTime.MinValue };
                _laneSplitState[lane] = ls;
            }

            if (curSplitCount > ls.SplitCount)
            {
                ls.SplitCount = curSplitCount;
                ls.ShowTime = DateTime.Now;
            }

            if (curSplitCount > 0 && ls.ShowTime != DateTime.MinValue)
            {
                double displaySec = 5;
                if (_data != null && _data["laneCloseSettings"] != null && _data["laneCloseSettings"]["splitDisplayTime"] != null)
                    displaySec = (double)_data["laneCloseSettings"]["splitDisplayTime"];
                double elapsed = (DateTime.Now - ls.ShowTime).TotalMilliseconds;
                if (elapsed < displaySec * 1000)
                {
                    var lastSplit = splits[curSplitCount - 1] as JObject;
                    if (lastSplit != null && lastSplit["cumulative"] != null)
                        return lastSplit["cumulative"].ToString();
                }
            }
            return "";
        }

        private string GetRelayCurrentLegName(JObject sw)
        {
            string names = sw["name"] != null ? sw["name"].ToString() : "";
            string[] parts = names.Split(',');
            if (parts.Length <= 1) return names;

            int currentLap = sw["currentLap"] != null ? (int)sw["currentLap"] : 0;
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
            int maxArrows = 12;

            if (countdown > 0)
            {
                double elapsed = closeTime - countdown;
                double progress = closeTime > 0 ? elapsed / closeTime : 1;
                int arrowCount = Math.Max(1, (int)Math.Round(progress * maxArrows));
                if (arrowCount > maxArrows) arrowCount = maxArrows;
                string arrows = "";
                for (int i = 0; i < arrowCount; i++) arrows += arrow;
                string cdText = string.Format("({0:F1}s)", countdown);
                if (dir == "going") return arrows + " " + cdText;
                return cdText + " " + arrows;
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
                case "broken": c = (Color)ColorConverter.ConvertFromString("#EF4444"); break;
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
                    string rt = sw["reactionTime"] != null ? sw["reactionTime"].ToString() : "";
                    sb.AppendFormat("反应时间: {0}\n", rt != "" ? rt : "-");
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
                    // 接力交接棒有反应时间
                    string spReact = sw["reactionTime"] != null ? sw["reactionTime"].ToString() : "";
                    if (spReact != "") sb.AppendFormat("反应时间: {0}\n", spReact);
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
            string startList = "";
            foreach (JObject sw in swimmers)
            {
                string label = isRelay ? (sw["country"] != null ? sw["country"].ToString() : "") : (sw["name"] != null ? sw["name"].ToString() : "");
                startList += string.Format("道{0}-{1}({2}) ", sw["lane"], label, sw["entryTime"] ?? "");
            }
            StartListText.Text = startList;
        }

        // ═══════ 按钮事件 ═══════
        private void Ready_Click(object sender, RoutedEventArgs e)
        {
            SendCmd("READY");
            AddLog("就位");
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            SendCmd("START_RACE");
            AddLog("发令");
        }

        private void TimerReset_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("确定计时器复位，数据清除？", "计时复位", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                SendCmd("TIMER_RESET");
                AddLog("计时复位");
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            string info = string.Format("{0}子 {1} {2} 第{3}组",
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
            SendCmd("PREV_HEAT");
        }

        private void NextHeat_Click(object sender, RoutedEventArgs e)
        {
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
            MarkLane("DNS");
        }

        private void MarkDNF_Click(object sender, RoutedEventArgs e)
        {
            MarkLane("DNF");
        }

        private void MarkDSQ_Click(object sender, RoutedEventArgs e)
        {
            MarkLane("DSQ");
        }

        private void MarkLane(string status)
        {
            int lane = GetLaneInput();
            if (lane < 0) { MessageBox.Show("请输入泳道号"); return; }
            MessageBoxResult result = MessageBox.Show(
                string.Format("确定将泳道 {0} 标记为 {1}？", lane, status),
                "泳道操作", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Window
            {
                Title = "参数设置",
                Width = 420,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };

            // 优先用服务器广播的最新值，否则用本地保存值
            double closeTime = _laneCloseTime, sbDelay = _startBlockCloseDelay, confDelay = _resultConfirmCloseDelay, fsThresh = _falseStartThreshold, splitDisp = _splitDisplayTime;
            if (_data != null && _data["laneCloseSettings"] != null)
            {
                var lcs = _data["laneCloseSettings"];
                if (lcs["laneCloseTime"] != null) closeTime = (double)lcs["laneCloseTime"];
                if (lcs["startBlockCloseDelay"] != null) sbDelay = (double)lcs["startBlockCloseDelay"];
                if (lcs["resultConfirmCloseDelay"] != null) confDelay = (double)lcs["resultConfirmCloseDelay"];
                if (lcs["falseStartThreshold"] != null) fsThresh = (double)lcs["falseStartThreshold"];
                if (lcs["splitDisplayTime"] != null) splitDisp = (double)lcs["splitDisplayTime"];
            }

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "参数设置", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14) });

            var tbCloseTime = AddParamRow(sp, "泳道关闭时间", closeTime.ToString(), "秒");
            var tbSBDelay = AddParamRow(sp, "出发台关闭延迟", sbDelay.ToString(), "秒");
            var tbConfDelay = AddParamRow(sp, "成绩确认关闭延迟", confDelay.ToString(), "秒");
            var tbFSThresh = AddParamRow(sp, "抢跳判定阈值", fsThresh.ToString(), "秒");
            var tbSplitDisp = AddParamRow(sp, "分段成绩显示时长", splitDisp.ToString(), "秒");
            var tbFirstHold = AddParamRow(sp, "第1名成绩停留时间", _firstPlaceHoldTime.ToString(), "秒");

            // Finish position radio
            var finishRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            finishRow.Children.Add(new TextBlock { Text = "终点位置", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbLeft = new RadioButton { Content = "左端", Foreground = Brushes.White, FontSize = 14, IsChecked = _finishPosition == "left", GroupName = "FinishPos", Margin = new Thickness(0, 0, 12, 0) };
            var rbRight = new RadioButton { Content = "右端", Foreground = Brushes.White, FontSize = 14, IsChecked = _finishPosition == "right", GroupName = "FinishPos" };
            finishRow.Children.Add(rbLeft);
            finishRow.Children.Add(rbRight);
            sp.Children.Add(finishRow);

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

            // 设备状态管理按钮
            var deviceSep = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 10, 0, 0) };
            var btnDeviceMgr = new Button { Content = "设备状态管理", Padding = new Thickness(0, 8, 0, 8), FontSize = 14, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnDeviceMgr.Click += delegate {
                dlg.DialogResult = false;
                OpenDeviceManager();
            };
            deviceSep.Child = btnDeviceMgr;
            sp.Children.Add(deviceSep);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button { Content = "确定", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            dlg.Content = scroll;

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

                var settings = new
                {
                    laneCloseTime = _laneCloseTime,
                    startBlockCloseDelay = _startBlockCloseDelay,
                    resultConfirmCloseDelay = _resultConfirmCloseDelay,
                    falseStartThreshold = _falseStartThreshold,
                    splitDisplayTime = _splitDisplayTime,
                    startPosition = _finishPosition
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
                AddLog(string.Format("参数已更新并保存，第1名停留: {0}s，终点位置: {1}", _firstPlaceHoldTime, _finishPosition == "left" ? "左端" : "右端"));
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

        // ═══════ 设备状态管理 ═══════
        private void OpenDeviceManager()
        {
            if (_data == null || _data["swimmers"] == null) { MessageBox.Show("暂无泳道数据"); return; }
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers == null || swimmers.Count == 0) { MessageBox.Show("暂无泳道数据"); return; }

            var dlg = new Window
            {
                Title = "设备状态管理",
                Width = 750, Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };

            var mainSp = new StackPanel { Margin = new Thickness(16) };
            mainSp.Children.Add(new TextBlock { Text = "设备状态管理", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });
            mainSp.Children.Add(new TextBlock { Text = "点击设备名切换损坏/正常状态（红色=损坏）", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });

            string[] deviceNames = { "左触板", "左盲1", "左盲2", "左盲3", "左出发台", "右触板", "右盲1", "右盲2", "右盲3", "右出发台" };
            string[] deviceKeys = { "leftTouchpad", "leftBlindWatch1", "leftBlindWatch2", "leftBlindWatch3", "leftStartBlock", "rightTouchpad", "rightBlindWatch1", "rightBlindWatch2", "rightBlindWatch3", "rightStartBlock" };
            string[] brokenKeys = { "leftTouchpadBroken", "leftBlindWatch1Broken", "leftBlindWatch2Broken", "leftBlindWatch3Broken", "leftStartBlockBroken", "rightTouchpadBroken", "rightBlindWatch1Broken", "rightBlindWatch2Broken", "rightBlindWatch3Broken", "rightStartBlockBroken" };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 350 };
            var grid = new Grid();

            // Header row
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // 道次
            for (int d = 0; d < deviceNames.Length; d++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

            var headerLane = new TextBlock { Text = "道次", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(headerLane, 0); Grid.SetColumn(headerLane, 0);
            grid.Children.Add(headerLane);
            for (int d = 0; d < deviceNames.Length; d++)
            {
                var hdr = new TextBlock { Text = deviceNames[d], Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetRow(hdr, 0); Grid.SetColumn(hdr, d + 1);
                grid.Children.Add(hdr);
            }

            // Data rows
            for (int si = 0; si < swimmers.Count; si++)
            {
                JObject sw = (JObject)swimmers[si];
                int lane = sw["lane"] != null ? (int)sw["lane"] : si;
                var ds = sw["deviceStatus"] as JObject;
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                int rowIdx = si + 1;

                var laneTb = new TextBlock { Text = "道" + lane, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetRow(laneTb, rowIdx); Grid.SetColumn(laneTb, 0);
                grid.Children.Add(laneTb);

                for (int d = 0; d < deviceKeys.Length; d++)
                {
                    bool isBroken = ds != null && ds[brokenKeys[d]] != null && (bool)ds[brokenKeys[d]];
                    int capturedLane = lane;
                    string capturedDevice = deviceKeys[d];
                    var btn = new Button
                    {
                        Content = isBroken ? "X" : "OK",
                        FontSize = 10, Padding = new Thickness(2),
                        Background = new SolidColorBrush(isBroken ? (Color)ColorConverter.ConvertFromString("#EF4444") : (Color)ColorConverter.ConvertFromString("#22C55E")),
                        Foreground = Brushes.White, BorderThickness = new Thickness(0),
                        Margin = new Thickness(2)
                    };
                    btn.Click += delegate {
                        bool newBroken = btn.Content.ToString() == "OK";
                        SendCmd("SET_DEVICE_STATUS", new { lane = capturedLane, device = capturedDevice, status = newBroken ? "broken" : "normal" });
                        btn.Content = newBroken ? "X" : "OK";
                        btn.Background = new SolidColorBrush(newBroken ? (Color)ColorConverter.ConvertFromString("#EF4444") : (Color)ColorConverter.ConvertFromString("#22C55E"));
                    };
                    Grid.SetRow(btn, rowIdx); Grid.SetColumn(btn, d + 1);
                    grid.Children.Add(btn);
                }
            }
            scroll.Content = grid;
            mainSp.Children.Add(scroll);

            // 全部正常/全部损坏 按钮
            var allBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            var btnAllNormal = new Button { Content = "全部正常", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Bold };
            btnAllNormal.Click += delegate {
                foreach (JObject sw in swimmers)
                {
                    int l = sw["lane"] != null ? (int)sw["lane"] : 0;
                    for (int d = 0; d < deviceKeys.Length; d++)
                        SendCmd("SET_DEVICE_STATUS", new { lane = l, device = deviceKeys[d], status = "normal" });
                }
                // 刷新按钮状态
                foreach (object child in grid.Children)
                {
                    var b = child as Button;
                    if (b != null && (b.Content.ToString() == "OK" || b.Content.ToString() == "X"))
                    {
                        b.Content = "OK";
                        b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
                    }
                }
                AddLog("已设置全部设备为正常");
            };
            var btnAllBroken = new Button { Content = "全部损坏", Padding = new Thickness(16, 6, 16, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold };
            btnAllBroken.Click += delegate {
                foreach (JObject sw in swimmers)
                {
                    int l = sw["lane"] != null ? (int)sw["lane"] : 0;
                    for (int d = 0; d < deviceKeys.Length; d++)
                        SendCmd("SET_DEVICE_STATUS", new { lane = l, device = deviceKeys[d], status = "broken" });
                }
                foreach (object child in grid.Children)
                {
                    var b = child as Button;
                    if (b != null && (b.Content.ToString() == "OK" || b.Content.ToString() == "X"))
                    {
                        b.Content = "X";
                        b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    }
                }
                AddLog("已设置全部设备为损坏");
            };
            allBtnPanel.Children.Add(btnAllNormal);
            allBtnPanel.Children.Add(btnAllBroken);
            mainSp.Children.Add(allBtnPanel);

            // 确认 / 关闭 按钮
            var bottomPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var btnConfirm = new Button { Content = "确认", Padding = new Thickness(20, 6, 20, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) };
            btnConfirm.Click += delegate { dlg.Close(); AddLog("设备状态已确认"); };
            var btnClose = new Button { Content = "关闭", Padding = new Thickness(20, 6, 20, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 14 };
            btnClose.Click += delegate { dlg.Close(); };
            bottomPanel.Children.Add(btnConfirm);
            bottomPanel.Children.Add(btnClose);
            mainSp.Children.Add(bottomPanel);

            var dlgScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = mainSp };
            dlg.Content = dlgScroll;
            dlg.ShowDialog();
        }

        // ═══════ 键盘快捷键 ═══════
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Skip if focus is on a TextBox or ComboBox
            if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox) return;

            if (e.Key == Key.F5) { SendCmd("READY"); e.Handled = true; }
            else if (e.Key == Key.F6) { SendCmd("START_RACE"); e.Handled = true; }
            else if (e.Key == Key.F7)
            {
                e.Handled = true;
                MessageBoxResult r = MessageBox.Show("确定计时器复位，数据清除？", "计时复位", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Yes) { SendCmd("TIMER_RESET"); AddLog("计时复位"); }
            }
            else if (e.Key == Key.Return)
            {
                Confirm_Click(null, null);
            }
            else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Control) { SendCmd("PREV_HEAT"); }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Control) { SendCmd("NEXT_HEAT"); }
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
        }
    }
}
