using System;
using System.Collections.Generic;
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
            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        private class SplitState
        {
            public int SplitCount;
            public DateTime ShowTime;
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
                _ws.Close();
                UpdateConnStatus(false);
                ConnBtn.Content = "连接";
                return;
            }

            string addr = ServerBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 3002;
            if (parts.Length > 1) int.TryParse(parts[1], out port);

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
                    });
                };
                _ws.Connect(host, port);
                _ws.Send(JsonConvert.SerializeObject(new { type = "TIMING_EXE_IDENTITY" }));
                UpdateConnStatus(true);
                ConnBtn.Content = "断开";
                AddLog("已连接: " + addr);
            }
            catch (Exception ex)
            {
                AddLog("连接失败: " + ex.Message);
            }
        }

        private void OnServerMessage(string json)
        {
            Dispatcher.Invoke((Action)delegate()
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
                    if (_data != null) RenderAll();
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
            if (_ws == null || !_ws.IsConnected) return;
            _ws.Send(JsonConvert.SerializeObject(new { type = "TIMING_CMD", command = cmd, data = data }));
        }

        private void SendCmd(string cmd)
        {
            SendCmd(cmd, null);
        }

        private void SendDisplay(string mode)
        {
            if (_ws == null || !_ws.IsConnected) return;
            _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = mode }));
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

            // Sync settings
            if (_data["laneCloseSettings"] != null)
            {
                var lcs = _data["laneCloseSettings"];
                if (lcs["startPosition"] != null) _startPosition = lcs["startPosition"].ToString();
                if (lcs["finishPosition"] != null) _finishPosition = lcs["finishPosition"].ToString();
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
        private void RenderPoolHeader()
        {
            PoolHeader.Children.Clear();
            PoolHeader.ColumnDefinitions.Clear();

            // Start indicator left
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            // Left devices
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            // Mid
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // Right devices
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            // Start indicator right
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            // Info area
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            // Left start indicator
            var leftInd = new TextBlock
            {
                Text = _startPosition == "left" ? ">" : "",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(leftInd, 0);
            PoolHeader.Children.Add(leftInd);

            // Left device labels
            var leftLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftLabels.Children.Add(MakeHeaderLabel("[T]", 78));
            leftLabels.Children.Add(MakeHeaderLabel("盲1", 18));
            leftLabels.Children.Add(MakeHeaderLabel("盲2", 18));
            leftLabels.Children.Add(MakeHeaderLabel("盲3", 18));
            leftLabels.Children.Add(MakeHeaderLabel("出发", 18));
            leftLabels.Children.Add(MakeHeaderLabel("触板", 18));
            leftLabels.Children.Add(MakeHeaderLabel("圈", 22));
            Grid.SetColumn(leftLabels, 1);
            PoolHeader.Children.Add(leftLabels);

            // Mid header
            var midPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            midPanel.Children.Add(MakeHeaderLabel("道", 24));
            midPanel.Children.Add(MakeHeaderLabel("姓名/代表队", 110));
            midPanel.Children.Add(new TextBlock { Text = "方向/进度", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(midPanel, 2);
            PoolHeader.Children.Add(midPanel);

            // Right device labels
            var rightLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rightLabels.Children.Add(MakeHeaderLabel("圈", 22));
            rightLabels.Children.Add(MakeHeaderLabel("触板", 18));
            rightLabels.Children.Add(MakeHeaderLabel("出发", 18));
            rightLabels.Children.Add(MakeHeaderLabel("盲1", 18));
            rightLabels.Children.Add(MakeHeaderLabel("盲2", 18));
            rightLabels.Children.Add(MakeHeaderLabel("盲3", 18));
            rightLabels.Children.Add(MakeHeaderLabel("[T]", 78));
            Grid.SetColumn(rightLabels, 3);
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
            Grid.SetColumn(rightInd, 4);
            PoolHeader.Children.Add(rightInd);

            // Info area header
            var infoHeader = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            infoHeader.Children.Add(MakeHeaderLabel("反应", 55));
            infoHeader.Children.Add(MakeHeaderLabel("成绩", 100));
            infoHeader.Children.Add(MakeHeaderLabel("名次", 35));
            infoHeader.Children.Add(MakeHeaderLabel("备注", 35));
            Grid.SetColumn(infoHeader, 5);
            PoolHeader.Children.Add(infoHeader);
        }

        private TextBlock MakeHeaderLabel(string text, double width)
        {
            return new TextBlock
            {
                Text = text,
                Width = width,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                FontSize = 11,
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
                    Margin = new Thickness(0, 0, 0, 3),
                    Padding = new Thickness(0, 0, 0, 0),
                    Height = 48
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
                // Columns: startIndL, leftDevices, mid, rightDevices, startIndR, infoArea
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

                int capturedLane = lane;

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
                Grid.SetColumn(leftStartInd, 0);
                grid.Children.Add(leftStartInd);

                // Left devices
                var leftDevices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                var touchBtnL = new Button
                {
                    Content = "T", Width = 78, Height = 26, FontSize = 14,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                touchBtnL.Click += delegate { SendCmd("MANUAL_TOUCH_LEFT", new { lane = capturedLane }); };
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
                    Width = 22,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(leftRemainActive ?
                        (Color)ColorConverter.ConvertFromString("#F59E0B") :
                        (Color)ColorConverter.ConvertFromString("#475569"))
                });
                Grid.SetColumn(leftDevices, 1);
                grid.Children.Add(leftDevices);

                // Mid panel
                var midPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
                midPanel.Children.Add(new TextBlock
                {
                    Text = lane.ToString(),
                    Width = 24,
                    FontSize = 19,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                });

                // Swimmer info - show differently for relay
                var infoStack = new StackPanel { Width = 110 };
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

                Grid.SetColumn(midPanel, 2);
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
                    Width = 22,
                    FontSize = 18,
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
                    Content = "T", Width = 78, Height = 26, FontSize = 14,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                touchBtnR.Click += delegate { SendCmd("MANUAL_TOUCH_RIGHT", new { lane = capturedLane }); };
                rightDevices.Children.Add(touchBtnR);
                Grid.SetColumn(rightDevices, 3);
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
                Grid.SetColumn(rightStartInd, 4);
                grid.Children.Add(rightStartInd);

                // Info area
                var infoArea = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                infoArea.Children.Add(new TextBlock
                {
                    Text = sw["reactionTime"] != null ? sw["reactionTime"].ToString() : "",
                    Width = 55,
                    FontSize = 17,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    FontFamily = new FontFamily("Consolas")
                });

                // Final time / split time with timed display
                string displayTime = GetSplitOrFinalTime(sw);
                infoArea.Children.Add(new TextBlock
                {
                    Text = displayTime,
                    Width = 100,
                    FontSize = 19,
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
                    Width = 35,
                    FontSize = 20,
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
                    Width = 35,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                });

                Grid.SetColumn(infoArea, 5);
                grid.Children.Add(infoArea);

                row.Child = grid;
                row.MouseLeftButtonDown += delegate
                {
                    _selectedLane = capturedLane;
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
                Width = 18,
                Height = 18,
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

        private void UpdateTimingSourceInfo()
        {
            if (_data == null || _selectedLane < 0) return;
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers == null) return;
            foreach (JObject sw in swimmers)
            {
                if (sw["lane"] != null && (int)sw["lane"] == _selectedLane)
                {
                    var ts = sw["timingSources"] as JObject;
                    if (ts != null)
                    {
                        TimingSourceInfo.Text = string.Format("道{0} {1}\n触板: {2}\n盲表1: {3}\n盲表2: {4}\n盲表3: {5}\n手动左: {6}\n手动右: {7}",
                            _selectedLane, sw["name"],
                            ts["touchpad"] ?? "-", ts["blindWatch1"] ?? "-", ts["blindWatch2"] ?? "-", ts["blindWatch3"] ?? "-",
                            ts["manualTouchLeft"] ?? "-", ts["manualTouchRight"] ?? "-");
                    }
                    break;
                }
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

            double closeTime = 20, sbDelay = 3, confDelay = 3, fsThresh = 0.10, splitDisp = 5;
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

            // Serial port (moved from main UI)
            var serialRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            serialRow.Children.Add(new TextBlock { Text = "本地串口(可选)", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var comPortCombo = new ComboBox { Width = 100, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Foreground = Brushes.White };
            try
            {
                foreach (string port in System.IO.Ports.SerialPort.GetPortNames())
                    comPortCombo.Items.Add(port);
            }
            catch { }
            serialRow.Children.Add(comPortCombo);
            sp.Children.Add(serialRow);

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

                var settings = new
                {
                    laneCloseTime = double.Parse(tbCloseTime.Text),
                    startBlockCloseDelay = double.Parse(tbSBDelay.Text),
                    resultConfirmCloseDelay = double.Parse(tbConfDelay.Text),
                    falseStartThreshold = double.Parse(tbFSThresh.Text),
                    splitDisplayTime = double.Parse(tbSplitDisp.Text),
                    startPosition = _finishPosition
                };
                SendCmd("SET_LANE_CLOSE_SETTINGS", settings);
                AddLog(string.Format("参数已更新，第1名停留: {0}s，终点位置: {1}", _firstPlaceHoldTime, _finishPosition == "left" ? "左端" : "右端"));
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
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None) { _selectedLane = e.Key - Key.D0; UpdateTimingSourceInfo(); RenderLanes(_data != null ? _data["swimmers"] as JArray : null); }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.None) { _selectedLane = 0; UpdateTimingSourceInfo(); RenderLanes(_data != null ? _data["swimmers"] as JArray : null); }
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
            if (_refreshTimer != null) _refreshTimer.Stop();
            if (_ws != null) _ws.Close();
        }
    }
}
