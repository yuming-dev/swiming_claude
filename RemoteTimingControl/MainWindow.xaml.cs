using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RemoteTimingControl
{
    public partial class MainWindow : Window
    {
        private SimpleWebSocketClient _ws;
        private JObject _data;
        private int _selectedLane = -1;

        public MainWindow() {
            InitializeComponent();
            // Populate COM ports
            foreach (string port in System.IO.Ports.SerialPort.GetPortNames()) {
                ComPortCombo.Items.Add(port);
            }
        }

        // ═══════ 连接管理 ═══════
        private void Connect_Click(object sender, RoutedEventArgs e) {
            if (_ws != null && _ws.IsConnected) {
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

            try {
                _ws = new SimpleWebSocketClient();
                _ws.OnMessage += OnServerMessage;
                _ws.OnDisconnected += delegate() {
                    Dispatcher.Invoke((Action)delegate() {
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
            } catch (Exception ex) {
                AddLog("连接失败: " + ex.Message);
            }
        }

        private void OnServerMessage(string json) {
            Dispatcher.Invoke((Action)delegate() {
                try {
                    var msg = JObject.Parse(json);
                    _data = msg["data"] as JObject;
                    if (_data != null) RenderAll();
                } catch { }
            });
        }

        private void UpdateConnStatus(bool connected) {
            WsConn.Fill = new SolidColorBrush(connected ? Colors.LimeGreen : Colors.Red);
            WsConnText.Text = "服务器: " + (connected ? "已连接" : "未连接");
        }

        // ═══════ 发送命令 ═══════
        private void SendCmd(string cmd, object data = null) {
            if (_ws == null || !_ws.IsConnected) return;
            _ws.Send(JsonConvert.SerializeObject(new { type = "TIMING_CMD", command = cmd, data = data }));
        }

        // ═══════ 渲染 ═══════
        private void RenderAll() {
            if (_data == null) return;

            // 滚动时间
            RunningTime.Text = _data["runningTime"] != null ? _data["runningTime"].ToString() : "0.0";

            // 状态
            string state = _data["raceState"] != null ? _data["raceState"].ToString().ToLower() : "waiting";
            var stateColors = new Dictionary<string, Color> {
                {"waiting", (Color)ColorConverter.ConvertFromString("#F59E0B")},
                {"ready", (Color)ColorConverter.ConvertFromString("#3B82F6")},
                {"racing", (Color)ColorConverter.ConvertFromString("#22C55E")},
                {"finished", (Color)ColorConverter.ConvertFromString("#EF4444")}
            };
            Color c;
            if (!stateColors.TryGetValue(state, out c)) c = Colors.Gray;
            StateIndicator.Fill = new SolidColorBrush(c);
            var labels = new Dictionary<string, string> {
                {"waiting","等待"}, {"ready","就位"}, {"racing","比赛中"}, {"finished","已完赛"}
            };
            string label;
            if (!labels.TryGetValue(state, out label)) label = state;
            StateLabel.Text = label;

            // 抢跳
            string fsText = "";
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers != null) {
                foreach (JObject sw in swimmers) {
                    if (sw["isFalseStart"] != null && (bool)sw["isFalseStart"]) {
                        fsText += string.Format("道{0} FS! ", sw["lane"]);
                    }
                }
            }
            FalseStartInfo.Text = fsText;

            // 当前比赛信息
            string curInfo = string.Format("{0}子 {1} {2} 第{3}/{4}组",
                _data["currentGender"] ?? "", _data["currentEvent"] ?? "",
                _data["currentStage"] ?? "", _data["currentHeat"] ?? "0", _data["totalHeats"] ?? "0");
            CurrentInfoText.Text = curInfo;

            // 赛程树
            RenderScheduleTree();

            // 泳道图
            RenderLanes(swimmers);

            // 出发表
            string startList = "";
            if (swimmers != null) {
                foreach (JObject sw in swimmers) {
                    startList += string.Format("道{0}-{1}({2}) ", sw["lane"], sw["name"], sw["entryTime"]);
                }
            }
            StartListText.Text = startList;
        }

        // ═══════ 赛程树 ═══════
        private string _lastScheduleHash = "";

        private void RenderScheduleTree() {
            if (_data == null || ScheduleTree == null) return;
            var schedule = _data["schedule"] as JArray;
            if (schedule == null) return;

            // 避免频繁重建：简单用 count+当前比赛 做 hash
            string hash = schedule.Count + "|" + (_data["currentHeat"] ?? "") + "|" + (_data["currentEvent"] ?? "");
            if (hash == _lastScheduleHash) return;
            _lastScheduleHash = hash;

            ScheduleTree.Items.Clear();

            // 按单元分组
            var sessions = new Dictionary<int, List<JObject>>();
            var sessionNames = new Dictionary<int, string>();
            foreach (JObject item in schedule) {
                int sn = item["session"] != null ? (int)item["session"] : 1;
                if (!sessions.ContainsKey(sn)) { sessions[sn] = new List<JObject>(); }
                sessions[sn].Add(item);
                if (item["sessionName"] != null) sessionNames[sn] = item["sessionName"].ToString();
            }

            string curGender = _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            string curEvent = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            string curStage = _data["currentStage"] != null ? _data["currentStage"].ToString() : "";
            int curHeat = _data["currentHeat"] != null ? (int)_data["currentHeat"] : 0;

            foreach (var kv in sessions) {
                string sName;
                if (!sessionNames.TryGetValue(kv.Key, out sName)) sName = string.Format("第{0}单元", kv.Key);
                var sessionItem = new TreeViewItem {
                    Header = sName,
                    IsExpanded = true,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    FontSize = 11
                };

                foreach (JObject ev in kv.Value) {
                    string gender = ev["gender"] != null ? ev["gender"].ToString() : "";
                    string eventName = ev["eventName"] != null ? ev["eventName"].ToString() : "";
                    string stage = ev["stage"] != null ? ev["stage"].ToString() : "";
                    int heatCount = ev["heatCount"] != null ? (int)ev["heatCount"] : 1;
                    if (heatCount < 1) heatCount = 1;

                    string evHeader = string.Format("{0}子 {1} {2}", gender, eventName, stage);
                    var eventItem = new TreeViewItem {
                        Header = evHeader,
                        Foreground = Brushes.White,
                        FontSize = 11
                    };

                    for (int h = 1; h <= heatCount; h++) {
                        string tag = string.Format("{0}|{1}|{2}|{3}", gender, eventName, stage, h);
                        var heatItem = new TreeViewItem {
                            Header = string.Format("第{0}组 (共{1}组)", h, heatCount),
                            Tag = tag,
                            FontSize = 11
                        };
                        // 高亮当前比赛
                        if (gender == curGender && eventName == curEvent && stage == curStage && h == curHeat) {
                            heatItem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                            heatItem.FontWeight = FontWeights.Bold;
                        } else {
                            heatItem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                        }
                        eventItem.Items.Add(heatItem);
                    }
                    sessionItem.Items.Add(eventItem);
                }
                ScheduleTree.Items.Add(sessionItem);
            }
        }

        private void ScheduleTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e) {
            var item = ScheduleTree.SelectedItem as TreeViewItem;
            if (item == null || item.Tag == null) return;
            string tag = item.Tag.ToString();
            string[] parts = tag.Split('|');
            if (parts.Length >= 4) {
                string gender = parts[0], eventName = parts[1], stage = parts[2];
                int heat;
                if (int.TryParse(parts[3], out heat)) {
                    SendCmd("SET_EVENT", gender + "子" + eventName);
                    SendCmd("SET_STAGE", stage);
                    SendCmd("SET_HEAT", heat);
                    AddLog(string.Format("选择: {0}子 {1} {2} 第{3}组", gender, eventName, stage, heat));
                }
            }
        }

        private void RenderLanes(JArray swimmers) {
            LanePanel.Children.Clear();
            if (swimmers == null) return;

            foreach (JObject sw in swimmers) {
                int lane = sw["lane"] != null ? (int)sw["lane"] : 0;
                var ds = sw["deviceStatus"] as JObject;

                var row = new Border {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 0, 2),
                    Padding = new Thickness(4, 2, 4, 2),
                    Height = 36
                };

                if (sw["isFalseStart"] != null && (bool)sw["isFalseStart"]) {
                    row.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    row.BorderThickness = new Thickness(1);
                }

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

                // 左端设备
                var leftDevices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var touchBtnL = new Button {
                    Content = "T", Width = 20, Height = 20, FontSize = 9,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                int capturedLane = lane;
                touchBtnL.Click += delegate { SendCmd("MANUAL_TOUCH_LEFT", new { lane = capturedLane }); };
                leftDevices.Children.Add(touchBtnL);
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftBlindWatch")));
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftStartBlock")));
                leftDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "leftTouchpad")));
                Grid.SetColumn(leftDevices, 0);
                grid.Children.Add(leftDevices);

                // 泳道信息
                var midPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
                midPanel.Children.Add(new TextBlock { Text = lane.ToString(), Width = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), VerticalAlignment = VerticalAlignment.Center });
                var infoStack = new StackPanel { Width = 90 };
                infoStack.Children.Add(new TextBlock { Text = sw["name"] != null ? sw["name"].ToString() : "", FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontSize = 11 });
                infoStack.Children.Add(new TextBlock { Text = sw["country"] != null ? sw["country"].ToString() : "", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 9 });
                midPanel.Children.Add(infoStack);
                midPanel.Children.Add(new TextBlock { Text = sw["direction"] != null ? sw["direction"].ToString() : "", Width = 20, FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), VerticalAlignment = VerticalAlignment.Center });

                double countdown = sw["laneCloseCountdown"] != null ? (double)sw["laneCloseCountdown"] : 0;
                if (countdown > 0) {
                    midPanel.Children.Add(new TextBlock { Text = string.Format("({0:F1}s)", countdown), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
                }
                Grid.SetColumn(midPanel, 1);
                grid.Children.Add(midPanel);

                // 右端设备
                var rightDevices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightTouchpad")));
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightStartBlock")));
                rightDevices.Children.Add(MakeDeviceDot(GetDeviceStatus(ds, "rightBlindWatch")));
                var touchBtnR = new Button {
                    Content = "T", Width = 20, Height = 20, FontSize = 9,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0)
                };
                touchBtnR.Click += delegate { SendCmd("MANUAL_TOUCH_RIGHT", new { lane = capturedLane }); };
                rightDevices.Children.Add(touchBtnR);
                Grid.SetColumn(rightDevices, 2);
                grid.Children.Add(rightDevices);

                // 成绩信息
                var infoArea = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                infoArea.Children.Add(new TextBlock { Text = sw["reactionTime"] != null ? sw["reactionTime"].ToString() : "", Width = 50, FontSize = 10, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") });
                infoArea.Children.Add(new TextBlock {
                    Text = sw["finalTime"] != null && sw["finalTime"].ToString() != "" ? sw["finalTime"].ToString() : (sw["status"] != null ? sw["status"].ToString() : ""),
                    Width = 70, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas")
                });

                var splits = sw["splits"] as JArray;
                string splitText = "";
                if (splits != null && splits.Count > 0) {
                    var last = splits[splits.Count - 1] as JObject;
                    if (last != null && last["time"] != null) splitText = last["time"].ToString();
                }
                infoArea.Children.Add(new TextBlock { Text = splitText, Width = 60, FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") });

                int rank = sw["rank"] != null ? (int)sw["rank"] : 0;
                Color rankColor = Colors.White;
                if (rank == 1) rankColor = (Color)ColorConverter.ConvertFromString("#F59E0B");
                else if (rank == 2) rankColor = (Color)ColorConverter.ConvertFromString("#C0C0C0");
                else if (rank == 3) rankColor = (Color)ColorConverter.ConvertFromString("#CD7F32");
                infoArea.Children.Add(new TextBlock { Text = rank > 0 ? rank.ToString() : "", Width = 40, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(rankColor), TextAlignment = TextAlignment.Center });

                string status = sw["status"] != null ? sw["status"].ToString() : "";
                infoArea.Children.Add(new TextBlock { Text = status, Width = 40, FontSize = 10, Foreground = new SolidColorBrush(Colors.Red), TextAlignment = TextAlignment.Center });

                Grid.SetColumn(infoArea, 3);
                grid.Children.Add(infoArea);

                row.Child = grid;
                row.MouseLeftButtonDown += delegate { _selectedLane = capturedLane; UpdateTimingSourceInfo(); };
                LanePanel.Children.Add(row);
            }
        }

        private Ellipse MakeDeviceDot(string status) {
            Color c;
            switch (status) {
                case "open": c = (Color)ColorConverter.ConvertFromString("#22C55E"); break;
                case "broken": c = (Color)ColorConverter.ConvertFromString("#EF4444"); break;
                case "falsestart": c = (Color)ColorConverter.ConvertFromString("#F59E0B"); break;
                default: c = (Color)ColorConverter.ConvertFromString("#475569"); break;
            }
            return new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(c), Margin = new Thickness(2, 0, 2, 0) };
        }

        private string GetDeviceStatus(JObject ds, string key) {
            if (ds == null || ds[key] == null) return "closed";
            return ds[key].ToString();
        }

        private void UpdateTimingSourceInfo() {
            if (_data == null || _selectedLane < 0) return;
            var swimmers = _data["swimmers"] as JArray;
            if (swimmers == null) return;
            foreach (JObject sw in swimmers) {
                if ((int)sw["lane"] == _selectedLane) {
                    var ts = sw["timingSources"] as JObject;
                    if (ts != null) {
                        TimingSourceInfo.Text = string.Format("道{0} {1}\n触板: {2}\n盲表1: {3}\n盲表2: {4}\n盲表3: {5}\n手动左: {6}\n手动右: {7}",
                            _selectedLane, sw["name"],
                            ts["touchpad"], ts["blindWatch1"], ts["blindWatch2"], ts["blindWatch3"],
                            ts["manualTouchLeft"], ts["manualTouchRight"]);
                    }
                    break;
                }
            }
        }

        // ═══════ 按钮事件 ═══════
        private void Ready_Click(object sender, RoutedEventArgs e) { SendCmd("READY"); AddLog("就位"); }
        private void Start_Click(object sender, RoutedEventArgs e) { SendCmd("START_RACE"); AddLog("发令"); }
        private void Restart_Click(object sender, RoutedEventArgs e) { SendCmd("RESTART"); AddLog("重新出发"); }
        private void Confirm_Click(object sender, RoutedEventArgs e) { SendCmd("CONFIRM_RESULT"); AddLog("确认成绩"); }
        private void PrevHeat_Click(object sender, RoutedEventArgs e) { SendCmd("PREV_HEAT"); }
        private void NextHeat_Click(object sender, RoutedEventArgs e) { SendCmd("NEXT_HEAT"); }
        private void OpenAll_Click(object sender, RoutedEventArgs e) { SendCmd("OPEN_ALL_LANES"); }
        private void CloseAll_Click(object sender, RoutedEventArgs e) { SendCmd("CLOSE_ALL_LANES"); }

        private void ConnectSerial_Click(object sender, RoutedEventArgs e) {
            AddLog("串口功能待实现");
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e) {
            // 构建参数设置对话框
            var dlg = new Window {
                Title = "参数设置",
                Width = 360, Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };

            // 读取当前值
            double closeTime = 20, sbDelay = 3, confDelay = 3, fsThresh = 0.10, splitDisp = 5;
            if (_data != null && _data["laneCloseSettings"] != null) {
                var lcs = _data["laneCloseSettings"];
                if (lcs["laneCloseTime"] != null) closeTime = (double)lcs["laneCloseTime"];
                if (lcs["startBlockCloseDelay"] != null) sbDelay = (double)lcs["startBlockCloseDelay"];
                if (lcs["resultConfirmCloseDelay"] != null) confDelay = (double)lcs["resultConfirmCloseDelay"];
                if (lcs["falseStartThreshold"] != null) fsThresh = (double)lcs["falseStartThreshold"];
                if (lcs["splitDisplayTime"] != null) splitDisp = (double)lcs["splitDisplayTime"];
            }

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "参数设置", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });

            var tbCloseTime = AddParamRow(sp, "泳道关闭时间", closeTime.ToString(), "秒");
            var tbSBDelay = AddParamRow(sp, "出发台关闭延迟", sbDelay.ToString(), "秒");
            var tbConfDelay = AddParamRow(sp, "成绩确认关闭延迟", confDelay.ToString(), "秒");
            var tbFSThresh = AddParamRow(sp, "抢跳判定阈值", fsThresh.ToString(), "秒");
            var tbSplitDisp = AddParamRow(sp, "分段成绩显示时长", splitDisp.ToString(), "秒");

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 4, 16, 4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button { Content = "确定", Padding = new Thickness(16, 4, 16, 4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);

            dlg.Content = sp;

            if (dlg.ShowDialog() == true) {
                var settings = new {
                    laneCloseTime = double.Parse(tbCloseTime.Text),
                    startBlockCloseDelay = double.Parse(tbSBDelay.Text),
                    resultConfirmCloseDelay = double.Parse(tbConfDelay.Text),
                    falseStartThreshold = double.Parse(tbFSThresh.Text),
                    splitDisplayTime = double.Parse(tbSplitDisp.Text)
                };
                SendCmd("SET_LANE_CLOSE_SETTINGS", settings);
                AddLog("参数已更新");
            }
        }

        private TextBox AddParamRow(StackPanel parent, string label, string value, string unit) {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            var lbl = new TextBlock { Text = label, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lbl, 0);
            var tb = new TextBox { Text = value, Padding = new Thickness(4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Foreground = Brushes.White, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), FontSize = 13, TextAlignment = TextAlignment.Center };
            Grid.SetColumn(tb, 1);
            var unitTb = new TextBlock { Text = unit, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(unitTb, 2);
            row.Children.Add(lbl);
            row.Children.Add(tb);
            row.Children.Add(unitTb);
            parent.Children.Add(row);
            return tb;
        }

        // ═══════ 键盘快捷键 ═══════
        private void Window_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.F5) { SendCmd("READY"); e.Handled = true; }
            else if (e.Key == Key.F6) { SendCmd("START_RACE"); e.Handled = true; }
            else if (e.Key == Key.F7) { SendCmd("RESTART"); e.Handled = true; }
            else if (e.Key == Key.Return) { SendCmd("CONFIRM_RESULT"); }
            else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Control) { SendCmd("PREV_HEAT"); }
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Control) { SendCmd("NEXT_HEAT"); }
            else if (e.Key == Key.D && _selectedLane >= 0) { SendCmd("MARK_DNS", new { lane = _selectedLane }); }
            else if (e.Key == Key.F && _selectedLane >= 0) { SendCmd("MARK_DNF", new { lane = _selectedLane }); }
            else if (e.Key == Key.Q && _selectedLane >= 0) { SendCmd("MARK_DSQ", new { lane = _selectedLane }); }
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None) { _selectedLane = e.Key - Key.D0; UpdateTimingSourceInfo(); }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.None) { _selectedLane = 0; UpdateTimingSourceInfo(); }
            // Shift+1~0 = 左端手动触板
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.Shift) { SendCmd("MANUAL_TOUCH_LEFT", new { lane = e.Key - Key.D0 }); }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Shift) { SendCmd("MANUAL_TOUCH_LEFT", new { lane = 10 }); }
            // Ctrl+1~0 = 右端手动触板
            else if (e.Key >= Key.D1 && e.Key <= Key.D9 && Keyboard.Modifiers == ModifierKeys.Control) { SendCmd("MANUAL_TOUCH_RIGHT", new { lane = e.Key - Key.D0 }); }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control) { SendCmd("MANUAL_TOUCH_RIGHT", new { lane = 10 }); }
        }

        private void AddLog(string msg) {
            string entry = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), msg);
            LogList.Items.Add(entry);
            if (LogList.Items.Count > 200) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(entry);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            if (_ws != null) _ws.Close();
        }
    }
}
