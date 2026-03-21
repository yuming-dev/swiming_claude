using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Fleck;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard
{
    public partial class MainWindow : Window
    {
        // ═══════════════════════════════════════════════════════════════
        // 核心数据集合
        // ═══════════════════════════════════════════════════════════════
        private ObservableCollection<Swimmer> _swimmers = new ObservableCollection<Swimmer>();
        private ObservableCollection<RelayTeam> _relayTeams = new ObservableCollection<RelayTeam>();
        private ObservableCollection<SwimmingRecord> _records = new ObservableCollection<SwimmingRecord>();
        private ObservableCollection<TeamScore> _teamScores = new ObservableCollection<TeamScore>();
        private ObservableCollection<ScheduleItem> _schedule = new ObservableCollection<ScheduleItem>();
        private List<string> _events = new List<string>();

        // ═══════════════════════════════════════════════════════════════
        // 比赛状态
        // ═══════════════════════════════════════════════════════════════
        private string _competitionName = "";
        private string _competitionMode = "domestic";
        private PoolConfig _poolConfig = new PoolConfig();
        private LaneCloseSettings _laneCloseSettings = new LaneCloseSettings();

        private string _currentEvent = "";
        private string _currentGender = "";
        private string _currentStage = "";
        private int _currentHeat = 0;
        private int _totalHeats = 0;
        private bool _isRelay = false;
        private RaceState _raceState = RaceState.Waiting;
        private DateTime _raceStartTime;
        private double _runningTime = 0;

        // 泳道设备状态
        private List<LaneDeviceState> _laneDeviceStates = new List<LaneDeviceState>();

        // ═══════════════════════════════════════════════════════════════
        // WebSocket 服务器
        // ═══════════════════════════════════════════════════════════════
        private WebSocketServer _server;
        private List<IWebSocketConnection> _allSockets = new List<IWebSocketConnection>();
        private List<IWebSocketConnection> _displaySockets = new List<IWebSocketConnection>();
        private List<IWebSocketConnection> _leaderboardSockets = new List<IWebSocketConnection>();
        private List<IWebSocketConnection> _registerSockets = new List<IWebSocketConnection>();
        private List<IWebSocketConnection> _timingExeSockets = new List<IWebSocketConnection>();
        private List<IWebSocketConnection> _timingWebSockets = new List<IWebSocketConnection>();
        private string _scoringControlMode = "local"; // local, remote_exe, remote_web

        // ═══════════════════════════════════════════════════════════════
        // 计时硬件
        // ═══════════════════════════════════════════════════════════════
        private TimingBridge _timingBridge;
        private DispatcherTimer _raceTimer;
        private DispatcherTimer _countdownTimer;
        private bool _initialized = false;
        private bool _resultConfirmed = false;
        private ObservableCollection<BackupInfo> _savedCompetitions = new ObservableCollection<BackupInfo>();

        // ═══════════════════════════════════════════════════════════════
        // 构造函数与初始化
        // ═══════════════════════════════════════════════════════════════
        public MainWindow() {
            InitializeComponent();
            InitializeData();
            InitializeWebSocketServer();
            InitializeTimingBridge();
            InitializeTimers();
            LoadLastCompetition();
            PopulateComPorts();
            UpdateConnectionStatus();
            _initialized = true;
            RefreshBackupList();
            AddLog("系统启动完成");
        }

        private void InitializeData() {
            SwimmerGrid.ItemsSource = _swimmers;
            RelayGrid.ItemsSource = _relayTeams;
            ScheduleGrid.ItemsSource = _schedule;
            RecordGrid.ItemsSource = _records;

            // 初始化默认项目列表
            _events = new List<string> {
                "50米自由泳", "100米自由泳", "200米自由泳", "400米自由泳",
                "800米自由泳", "1500米自由泳",
                "50米仰泳", "100米仰泳", "200米仰泳",
                "50米蛙泳", "100米蛙泳", "200米蛙泳",
                "50米蝶泳", "100米蝶泳", "200米蝶泳",
                "200米个人混合泳", "400米个人混合泳",
                "4x100米自由泳接力", "4x200米自由泳接力",
                "4x100米混合泳接力"
            };
            foreach (string ev in _events) {
                if (RegEventCombo != null) RegEventCombo.Items.Add(new ComboBoxItem { Content = ev });
                if (FilterEventCombo != null) FilterEventCombo.Items.Add(new ComboBoxItem { Content = ev });
                if (ResultEventCombo != null) ResultEventCombo.Items.Add(ev);
            }
            if (RegEventCombo != null && RegEventCombo.Items.Count > 0) RegEventCombo.SelectedIndex = 0;

            // 初始化泳道设备状态
            InitLaneDeviceStates();

            // 显示服务器地址
            string ip = GetLocalIP();
            ServerAddressText.Text = string.Format("服务器地址: ws://{0}:3002  |  Web页面: http://{0}:3002", ip);
        }

        private void InitLaneDeviceStates() {
            _laneDeviceStates.Clear();
            foreach (int lane in _poolConfig.LaneNumbers) {
                _laneDeviceStates.Add(new LaneDeviceState { Lane = lane });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // WebSocket 服务器
        // ═══════════════════════════════════════════════════════════════
        private void InitializeWebSocketServer() {
            try {
                _server = new WebSocketServer("ws://0.0.0.0:3002");
                _server.Start(delegate(IWebSocketConnection socket) {
                    socket.OnOpen = delegate() {
                        _allSockets.Add(socket);
                        Dispatcher.Invoke((Action)delegate() {
                            AddLog("客户端连接: " + socket.ConnectionInfo.ClientIpAddress);
                            UpdateConnectionStatus();
                        });
                        BroadcastSingle(socket);
                    };
                    socket.OnClose = delegate() {
                        _allSockets.Remove(socket);
                        _displaySockets.Remove(socket);
                        _leaderboardSockets.Remove(socket);
                        _registerSockets.Remove(socket);
                        _timingExeSockets.Remove(socket);
                        _timingWebSockets.Remove(socket);
                        Dispatcher.Invoke((Action)delegate() {
                            UpdateScoringControlMode();
                            UpdateConnectionStatus();
                        });
                    };
                    socket.OnMessage = delegate(string message) {
                        Dispatcher.Invoke((Action)delegate() {
                            HandleMessage(socket, message);
                        });
                    };
                });
                AddLog("WebSocket服务器已启动: ws://0.0.0.0:3002");
            } catch (Exception ex) {
                AddLog("WebSocket启动失败: " + ex.Message);
            }
        }

        private void HandleMessage(IWebSocketConnection socket, string message) {
            try {
                var msg = JObject.Parse(message);
                string type = msg["type"] != null ? msg["type"].ToString() : "";

                switch (type) {
                    case "DISPLAY_IDENTITY":
                        if (!_displaySockets.Contains(socket)) _displaySockets.Add(socket);
                        AddLog("大屏显示已连接");
                        break;
                    case "LEADERBOARD_IDENTITY":
                        if (!_leaderboardSockets.Contains(socket)) _leaderboardSockets.Add(socket);
                        AddLog("排名屏已连接");
                        break;
                    case "REGISTER_TERMINAL_IDENTITY":
                        if (!_registerSockets.Contains(socket)) _registerSockets.Add(socket);
                        AddLog("注册终端已连接");
                        break;
                    case "TIMING_EXE_IDENTITY":
                        if (!_timingExeSockets.Contains(socket)) _timingExeSockets.Add(socket);
                        AddLog("计时EXE已连接");
                        UpdateScoringControlMode();
                        break;
                    case "TIMING_WEB_IDENTITY":
                        if (!_timingWebSockets.Contains(socket)) _timingWebSockets.Add(socket);
                        AddLog("计时Web已连接");
                        UpdateScoringControlMode();
                        break;
                    case "REGISTER_SWIMMER":
                        HandleRegisterSwimmer(msg);
                        break;
                    case "REGISTER_SWIMMER_BATCH":
                        HandleRegisterSwimmerBatch(socket, msg);
                        break;
                    case "REGISTER_RELAY":
                        HandleRegisterRelay(msg);
                        break;
                    case "TIMING_CMD":
                        HandleTimingCommand(msg);
                        break;
                    case "TIMING_DATA":
                        HandleTimingData(msg);
                        break;
                    case "REMOTE_CONTROL":
                        HandleRemoteControl(msg);
                        break;
                    case "FALSE_START_DETECTED":
                        HandleFalseStartDetected(msg);
                        break;
                }

                UpdateConnectionStatus();
                BroadcastSingle(socket);
            } catch (Exception ex) {
                AddLog("消息处理错误: " + ex.Message);
            }
        }

        private void HandleRegisterSwimmer(JObject msg) {
            var data = msg["data"];
            if (data == null) return;
            string bibNumber = data["bibNumber"] != null ? data["bibNumber"].ToString() : "";
            if (string.IsNullOrEmpty(bibNumber)) bibNumber = GenerateNextBibNumber();
            var swimmer = new Swimmer {
                Name = data["name"] != null ? data["name"].ToString() : "",
                BibNumber = bibNumber,
                Gender = data["gender"] != null ? data["gender"].ToString() : "男",
                Age = data["age"] != null ? (int)data["age"] : 0,
                Country = data["country"] != null ? data["country"].ToString() : "",
                IDNumber = data["idNumber"] != null ? data["idNumber"].ToString() : "",
                Phone = data["phone"] != null ? data["phone"].ToString() : "",
                EventName = data["eventName"] != null ? data["eventName"].ToString() : "",
                EntryTime = data["entryTime"] != null ? data["entryTime"].ToString() : "",
                BirthDate = data["birthDate"] != null ? data["birthDate"].ToString() : "",
                CSANumber = data["csaNumber"] != null ? data["csaNumber"].ToString() : "",
                FINANumber = data["finaNumber"] != null ? data["finaNumber"].ToString() : "",
                Notes = data["notes"] != null ? data["notes"].ToString() : ""
            };
            swimmer.EntryTimeSeconds = TimeFormatter.Parse(swimmer.EntryTime);
            var dup = FindDuplicate(swimmer.Name, swimmer.Gender, swimmer.EventName, swimmer.BibNumber);
            if (dup != null) {
                AddLog(string.Format("远程注册被拒绝（重复）: {0} {1} — 已存在号码{2}", swimmer.Name, swimmer.EventName, dup.BibNumber));
                return;
            }
            _swimmers.Add(swimmer);
            AddLog(string.Format("远程注册运动员: {0}({1}) {2}", swimmer.Name, bibNumber, swimmer.EventName));
            AutoSaveData();
            Broadcast();
        }

        private void HandleRegisterSwimmerBatch(IWebSocketConnection socket, JObject msg) {
            var data = msg["data"];
            if (data == null) return;
            var swimmerData = data["swimmer"];
            var eventsArr = data["events"] as JArray;
            bool isResubmit = data["isResubmit"] != null && (bool)data["isResubmit"];

            if (swimmerData == null || eventsArr == null || eventsArr.Count == 0) {
                SendRegisterResult(socket, false, "数据不完整", "");
                return;
            }

            string name = swimmerData["name"] != null ? swimmerData["name"].ToString() : "";
            string gender = swimmerData["gender"] != null ? swimmerData["gender"].ToString() : "男";
            string bibNumber = swimmerData["bibNumber"] != null ? swimmerData["bibNumber"].ToString() : "";

            // 如果是重新提交，先删除该运动员之前的所有报名记录
            if (isResubmit && !string.IsNullOrEmpty(bibNumber)) {
                var toRemove = _swimmers.Where(s => s.BibNumber == bibNumber).ToList();
                foreach (var s in toRemove) _swimmers.Remove(s);
                AddLog(string.Format("重新提交: 已清除 {0}({1}) 的 {2} 条旧记录", name, bibNumber, toRemove.Count));
            }

            // 生成参赛号
            if (string.IsNullOrEmpty(bibNumber)) bibNumber = GenerateNextBibNumber();

            int added = 0;
            foreach (JObject ev in eventsArr) {
                string eventName = ev["eventName"] != null ? ev["eventName"].ToString() : "";
                string entryTime = ev["entryTime"] != null ? ev["entryTime"].ToString() : "";

                var dup = FindDuplicate(name, gender, eventName, bibNumber);
                if (dup != null && !isResubmit) continue;

                var swimmer = new Swimmer {
                    BibNumber = bibNumber,
                    Name = name,
                    Gender = gender,
                    Age = swimmerData["age"] != null ? (int)swimmerData["age"] : 0,
                    Country = swimmerData["country"] != null ? swimmerData["country"].ToString() : "",
                    IDNumber = swimmerData["idNumber"] != null ? swimmerData["idNumber"].ToString() : "",
                    Phone = swimmerData["phone"] != null ? swimmerData["phone"].ToString() : "",
                    EventName = eventName,
                    EntryTime = entryTime,
                    BirthDate = swimmerData["birthDate"] != null ? swimmerData["birthDate"].ToString() : "",
                    CSANumber = swimmerData["csaNumber"] != null ? swimmerData["csaNumber"].ToString() : "",
                    Notes = swimmerData["notes"] != null ? swimmerData["notes"].ToString() : ""
                };
                swimmer.EntryTimeSeconds = TimeFormatter.Parse(swimmer.EntryTime);
                _swimmers.Add(swimmer);
                added++;
            }

            AddLog(string.Format("批量注册: {0}({1}) {2}个项目", name, bibNumber, added));
            AutoSaveData();
            Broadcast();
            SendRegisterResult(socket, true, "", bibNumber);
        }

        private void SendRegisterResult(IWebSocketConnection socket, bool success, string message, string bibNumber) {
            try {
                var result = new { type = "REGISTER_RESULT", data = new { success = success, message = message, bibNumber = bibNumber } };
                socket.Send(JsonConvert.SerializeObject(result));
            } catch { }
        }

        private void HandleRegisterRelay(JObject msg) {
            var data = msg["data"];
            if (data == null) return;
            var team = new RelayTeam {
                TeamName = data["teamName"] != null ? data["teamName"].ToString() : "",
                EventName = data["eventName"] != null ? data["eventName"].ToString() : "",
                Gender = data["gender"] != null ? data["gender"].ToString() : "男",
                EntryTime = data["entryTime"] != null ? data["entryTime"].ToString() : ""
            };
            team.EntryTimeSeconds = TimeFormatter.Parse(team.EntryTime);
            // 棒次安排
            var legs = data["legs"] as JArray;
            if (legs != null) {
                foreach (JObject leg in legs) {
                    team.Legs.Add(new RelayLeg {
                        LegOrder = leg["legOrder"] != null ? (int)leg["legOrder"] : 0,
                        SwimmerName = leg["swimmerName"] != null ? leg["swimmerName"].ToString() : "",
                        SwimmerBibNumber = leg["swimmerBibNumber"] != null ? leg["swimmerBibNumber"].ToString() : ""
                    });
                }
            }
            _relayTeams.Add(team);
            AddLog(string.Format("远程注册接力队: {0} ({1}) {2}人", team.TeamName, team.EventName, team.Legs.Count));
            AutoSaveData();
            Broadcast();
        }

        private void HandleTimingCommand(JObject msg) {
            string cmd = msg["command"] != null ? msg["command"].ToString() : "";
            var data = msg["data"];

            switch (cmd) {
                case "READY": Ready_Click(null, null); break;
                case "START_RACE": StartRace_Click(null, null); break;
                case "RESTART": Restart_Click(null, null); break;
                case "TIMER_RESET": Restart_Click(null, null); break;
                case "CONFIRM_RESULT": ConfirmResult_Click(null, null); break;
                case "AUTO_GENERATE_HEATS": AutoGenerateHeats_Click(null, null); break;
                case "NEXT_HEAT": NextHeat_Click(null, null); break;
                case "PREV_HEAT": PrevHeat_Click(null, null); break;
                case "SET_EVENT":
                    if (data != null) SetCurrentEvent(data.ToString());
                    break;
                case "SET_STAGE":
                    if (data != null) SetCurrentStage(data.ToString());
                    break;
                case "SET_HEAT":
                    if (data != null) SetCurrentHeat((int)data);
                    break;
                case "MARK_DNS":
                    if (data != null) MarkLaneStatus((int)data["lane"], "DNS");
                    break;
                case "MARK_DNF":
                    if (data != null) MarkLaneStatus((int)data["lane"], "DNF");
                    break;
                case "MARK_DSQ":
                    if (data != null) MarkLaneStatus((int)data["lane"], "DSQ");
                    break;
                case "OVERRIDE_TIME":
                    if (data != null) OverrideLaneTime((int)data["lane"], (double)data["time"]);
                    break;
                case "SET_DEVICE_STATUS":
                    if (data != null) SetDeviceStatus((int)data["lane"], data["device"].ToString(), data["status"].ToString());
                    break;
                case "SET_LANE_CLOSE_TIME":
                    if (data != null) {
                        int lane = (int)data["lane"];
                        double time = (double)data["time"];
                        var state = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
                        if (state != null) state.LaneCloseTime = time;
                    }
                    break;
                case "SET_LANE_CLOSE_SETTINGS":
                    if (data != null) {
                        if (data["laneCloseTime"] != null) _laneCloseSettings.LaneCloseTime = (double)data["laneCloseTime"];
                        if (data["startBlockCloseDelay"] != null) _laneCloseSettings.StartBlockCloseDelay = (double)data["startBlockCloseDelay"];
                        if (data["resultConfirmCloseDelay"] != null) _laneCloseSettings.ResultConfirmCloseDelay = (double)data["resultConfirmCloseDelay"];
                        if (data["falseStartThreshold"] != null) _laneCloseSettings.FalseStartThreshold = (double)data["falseStartThreshold"];
                        if (data["splitDisplayTime"] != null) _laneCloseSettings.SplitDisplayTime = (double)data["splitDisplayTime"];
                        if (data["startPosition"] != null) _laneCloseSettings.StartPosition = data["startPosition"].ToString();
                        AddLog(string.Format("参数更新: 关闭{0}s 出发台{1}s 确认{2}s 抢跳{3}s 分段{4}s 发令:{5}",
                            _laneCloseSettings.LaneCloseTime, _laneCloseSettings.StartBlockCloseDelay,
                            _laneCloseSettings.ResultConfirmCloseDelay, _laneCloseSettings.FalseStartThreshold,
                            _laneCloseSettings.SplitDisplayTime, _laneCloseSettings.StartPosition == "left" ? "左端" : "右端"));
                        AutoSaveData();
                    }
                    break;
                case "OPEN_ALL_LANES":
                    foreach (var s in _laneDeviceStates) {
                        s.LeftTouchpadStatus = DeviceStatus.Open;
                        s.LeftBlindWatch1Status = DeviceStatus.Open; s.LeftBlindWatch2Status = DeviceStatus.Open; s.LeftBlindWatch3Status = DeviceStatus.Open;
                        s.RightTouchpadStatus = DeviceStatus.Open;
                        s.RightBlindWatch1Status = DeviceStatus.Open; s.RightBlindWatch2Status = DeviceStatus.Open; s.RightBlindWatch3Status = DeviceStatus.Open;
                    }
                    break;
                case "CLOSE_ALL_LANES":
                    foreach (var s in _laneDeviceStates) {
                        s.LeftTouchpadStatus = DeviceStatus.Closed;
                        s.LeftBlindWatch1Status = DeviceStatus.Closed; s.LeftBlindWatch2Status = DeviceStatus.Closed; s.LeftBlindWatch3Status = DeviceStatus.Closed;
                        s.RightTouchpadStatus = DeviceStatus.Closed;
                        s.RightBlindWatch1Status = DeviceStatus.Closed; s.RightBlindWatch2Status = DeviceStatus.Closed; s.RightBlindWatch3Status = DeviceStatus.Closed;
                    }
                    break;
                case "MANUAL_LANE_OPEN":
                    if (data != null) {
                        var st = _laneDeviceStates.FirstOrDefault(s => s.Lane == (int)data["lane"]);
                        if (st != null) {
                            st.LeftTouchpadStatus = DeviceStatus.Open;
                            st.LeftBlindWatch1Status = DeviceStatus.Open; st.LeftBlindWatch2Status = DeviceStatus.Open; st.LeftBlindWatch3Status = DeviceStatus.Open;
                            st.RightTouchpadStatus = DeviceStatus.Open;
                            st.RightBlindWatch1Status = DeviceStatus.Open; st.RightBlindWatch2Status = DeviceStatus.Open; st.RightBlindWatch3Status = DeviceStatus.Open;
                            st.LaneCloseCountdown = 0;
                        }
                    }
                    break;
                case "MANUAL_LANE_CLOSE":
                    if (data != null) {
                        var st = _laneDeviceStates.FirstOrDefault(s => s.Lane == (int)data["lane"]);
                        if (st != null) {
                            st.LeftTouchpadStatus = DeviceStatus.Closed;
                            st.LeftBlindWatch1Status = DeviceStatus.Closed; st.LeftBlindWatch2Status = DeviceStatus.Closed; st.LeftBlindWatch3Status = DeviceStatus.Closed;
                            st.RightTouchpadStatus = DeviceStatus.Closed;
                            st.RightBlindWatch1Status = DeviceStatus.Closed; st.RightBlindWatch2Status = DeviceStatus.Closed; st.RightBlindWatch3Status = DeviceStatus.Closed;
                        }
                    }
                    break;
                case "MANUAL_TOUCH_LEFT":
                    if (data != null && _raceState == RaceState.Racing) {
                        int laneNum = (int)data["lane"];
                        var lState = _laneDeviceStates.FirstOrDefault(s => s.Lane == laneNum);
                        if (lState != null) lState.LeftManualTouchTime = _runningTime;
                        SaveManualTouchToSplit(laneNum, _runningTime);
                        AddLog(string.Format("泳道{0} 左端手动触板: {1}", laneNum, TimeFormatter.Format(_runningTime)));
                    }
                    break;
                case "MANUAL_TOUCH_RIGHT":
                    if (data != null && _raceState == RaceState.Racing) {
                        int laneNum = (int)data["lane"];
                        var lState = _laneDeviceStates.FirstOrDefault(s => s.Lane == laneNum);
                        if (lState != null) lState.RightManualTouchTime = _runningTime;
                        SaveManualTouchToSplit(laneNum, _runningTime);
                        AddLog(string.Format("泳道{0} 右端手动触板: {1}", laneNum, TimeFormatter.Format(_runningTime)));
                    }
                    break;
            }
            Broadcast();
        }

        private void HandleTimingData(JObject msg) {
            // 处理来自远程TimingBridge转发的计时数据
            var data = msg["data"];
            if (data == null) return;
            int lane = (int)data["lane"];
            string cmdType = data["commandType"].ToString();
            double time = (double)data["time"];
            ProcessTimingData(lane, cmdType, time);
        }

        private void HandleRemoteControl(JObject msg) {
            string cmd = msg["command"] != null ? msg["command"].ToString() : "";
            switch (cmd) {
                case "SHOW_LIVE_RACE": BroadcastDisplayMode("SHOW_LIVE_RACE"); break;
                case "SHOW_START_LIST": BroadcastDisplayMode("SHOW_START_LIST"); break;
                case "SHOW_HEAT_RESULT": BroadcastDisplayMode("SHOW_HEAT_RESULT"); break;
                case "SHOW_EVENT_RANKING": BroadcastDisplayMode("SHOW_EVENT_RANKING"); break;
                case "SHOW_TEAM_STANDINGS": BroadcastDisplayMode("SHOW_TEAM_STANDINGS"); break;
                case "SHOW_RECORDS": BroadcastDisplayMode("SHOW_RECORDS"); break;
                case "SHOW_AWARDS": BroadcastDisplayMode("SHOW_AWARDS"); break;
                case "SHOW_WELCOME": BroadcastDisplayMode("SHOW_WELCOME"); break;
                case "SHOW_PAUSE": BroadcastDisplayMode("SHOW_PAUSE"); break;
                case "SHOW_EVENT_LIST": BroadcastDisplayMode("SHOW_EVENT_LIST"); break;
                case "SHOW_PROMOTION_LIST": BroadcastDisplayMode("SHOW_PROMOTION_LIST"); break;
            }
        }

        private void HandleFalseStartDetected(JObject msg) {
            var data = msg["data"];
            if (data == null) return;
            int lane = (int)data["lane"];
            double reactionTime = (double)data["reactionTime"];
            var state = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (state != null) {
                state.IsFalseStart = true;
                state.ReactionTime = reactionTime;
                AddLog(string.Format("★抢跳! 泳道{0} 反应时间: {1:F3}s", lane, reactionTime));
            }
            Broadcast();
        }

        private void UpdateScoringControlMode() {
            if (_timingExeSockets.Count > 0) {
                _scoringControlMode = "remote_exe";
                ControlModeText.Text = "远程EXE";
                ControlModeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            } else if (_timingWebSockets.Count > 0) {
                _scoringControlMode = "remote_web";
                ControlModeText.Text = "远程Web";
                ControlModeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
            } else {
                _scoringControlMode = "local";
                ControlModeText.Text = "本地";
                ControlModeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
            }
        }

        private void UpdateConnectionStatus() {
            if (DisplayConnText == null) return;
            DisplayConnText.Text = _displaySockets.Count.ToString();
            DisplayConnText.Foreground = new SolidColorBrush(_displaySockets.Count > 0 ? Colors.Green : Colors.Red);
            LeaderboardConnText.Text = _leaderboardSockets.Count.ToString();
            LeaderboardConnText.Foreground = new SolidColorBrush(_leaderboardSockets.Count > 0 ? Colors.Green : Colors.Red);
            RegisterConnText.Text = _registerSockets.Count.ToString();
            RegisterConnText.Foreground = new SolidColorBrush(_registerSockets.Count > 0 ? Colors.Green : Colors.Red);
            TimingExeConnText.Text = _timingExeSockets.Count.ToString();
            TimingExeConnText.Foreground = new SolidColorBrush(_timingExeSockets.Count > 0 ? Colors.Green : Colors.Red);
            TimingWebConnText.Text = _timingWebSockets.Count.ToString();
            TimingWebConnText.Foreground = new SolidColorBrush(_timingWebSockets.Count > 0 ? Colors.Green : Colors.Red);
            TimingHwConnText.Text = _timingBridge != null && _timingBridge.IsConnected ? _timingBridge.StatusText : "未连接";
            TimingHwConnText.Foreground = new SolidColorBrush(_timingBridge != null && _timingBridge.IsConnected ? Colors.Green : Colors.Red);
        }

        // ═══════════════════════════════════════════════════════════════
        // 广播
        // ═══════════════════════════════════════════════════════════════
        private void Broadcast() {
            if (!_initialized) return;
            try {
                var msg = new { type = "SHOW_LIVE_RACE", data = GetStatusData() };
                string json = JsonConvert.SerializeObject(msg);
                foreach (var s in _allSockets.ToList()) {
                    try { s.Send(json); } catch { }
                }
            } catch { }
        }

        private void BroadcastSingle(IWebSocketConnection socket) {
            if (!_initialized) return;
            try {
                var msg = new { type = "SHOW_LIVE_RACE", data = GetStatusData() };
                socket.Send(JsonConvert.SerializeObject(msg));
            } catch { }
        }

        private void BroadcastDisplayMode(string mode) {
            try {
                var msg = new { type = mode, data = GetStatusData() };
                string json = JsonConvert.SerializeObject(msg);
                foreach (var s in _allSockets.ToList()) {
                    try { s.Send(json); } catch { }
                }
            } catch { }
        }

        private object GetStatusData() {
            // 构建当前组运动员数据
            var swimmerData = new List<object>();
            var currentSwimmers = GetCurrentHeatSwimmers();
            foreach (var sw in currentSwimmers) {
                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == sw.Lane);
                var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                var latestSplit = result != null && result.Splits.Count > 0 ? result.Splits.Last() : null;

                swimmerData.Add(new {
                    lane = sw.Lane,
                    name = sw.Name,
                    country = sw.Country,
                    bibNumber = sw.BibNumber,
                    entryTime = sw.EntryTime ?? "",
                    direction = laneState != null ? laneState.Direction : (_laneCloseSettings.StartPosition == "right" ? "←" : "→"),
                    deviceStatus = new {
                        leftTouchpad = laneState != null ? laneState.LeftTouchpadStatus.ToString().ToLower() : "closed",
                        leftBlindWatch1 = laneState != null ? laneState.LeftBlindWatch1Status.ToString().ToLower() : "closed",
                        leftBlindWatch2 = laneState != null ? laneState.LeftBlindWatch2Status.ToString().ToLower() : "closed",
                        leftBlindWatch3 = laneState != null ? laneState.LeftBlindWatch3Status.ToString().ToLower() : "closed",
                        leftStartBlock = laneState != null ? laneState.LeftStartBlockStatus.ToString().ToLower() : "open",
                        rightTouchpad = laneState != null ? laneState.RightTouchpadStatus.ToString().ToLower() : "closed",
                        rightBlindWatch1 = laneState != null ? laneState.RightBlindWatch1Status.ToString().ToLower() : "closed",
                        rightBlindWatch2 = laneState != null ? laneState.RightBlindWatch2Status.ToString().ToLower() : "closed",
                        rightBlindWatch3 = laneState != null ? laneState.RightBlindWatch3Status.ToString().ToLower() : "closed",
                        rightStartBlock = laneState != null ? laneState.RightStartBlockStatus.ToString().ToLower() : "closed"
                    },
                    laneCloseCountdown = laneState != null ? laneState.LaneCloseCountdown : 0,
                    reactionTime = laneState != null && laneState.ReactionTime != 0 ? laneState.ReactionTime.ToString("F2") : "",
                    splits = result != null ? result.Splits.Select(sp => new {
                        lap = sp.Lap, distance = sp.Distance,
                        time = TimeFormatter.Format(sp.Time), cumulative = TimeFormatter.Format(sp.CumulativeTime),
                        touchpad = TimeFormatter.Format(sp.TouchpadTime),
                        blind1 = TimeFormatter.Format(sp.PushButton1Time), blind2 = TimeFormatter.Format(sp.PushButton2Time), blind3 = TimeFormatter.Format(sp.PushButton3Time),
                        manual = TimeFormatter.Format(sp.ManualTouchTime), source = sp.TimingSource
                    }).ToList<object>() : new List<object>(),
                    finalTime = result != null ? TimeFormatter.Format(result.FinalTime) : "",
                    rank = result != null ? result.Rank : 0,
                    status = sw.Status ?? "",
                    timingSources = result != null ? new {
                        touchpad = TimeFormatter.Format(result.TouchpadTime),
                        blindWatch1 = TimeFormatter.Format(result.PushButton1Time),
                        blindWatch2 = TimeFormatter.Format(result.PushButton2Time),
                        blindWatch3 = TimeFormatter.Format(result.PushButton3Time),
                        manualTouchLeft = laneState != null ? TimeFormatter.Format(laneState.LeftManualTouchTime) : "",
                        manualTouchRight = laneState != null ? TimeFormatter.Format(laneState.RightManualTouchTime) : ""
                    } : (object)null,
                    isFalseStart = laneState != null && laneState.IsFalseStart,
                    isNewRecord = false,
                    currentLap = laneState != null ? laneState.CurrentLap : 0,
                    isFinished = laneState != null && laneState.IsFinished
                });
            }

            // 项目总排名
            var eventRanking = GetEventRanking(_currentEvent, _currentGender);
            var teamScoresData = _teamScores.OrderBy(t => t.Rank).Select(t => new {
                teamName = t.TeamName, totalPoints = t.TotalPoints,
                individualPoints = t.IndividualPoints, relayPoints = t.RelayPoints,
                recordBonusPoints = t.RecordBonusPoints,
                gold = t.GoldCount, silver = t.SilverCount, bronze = t.BronzeCount, rank = t.Rank
            }).ToList();

            return new {
                competitionName = _competitionName,
                competitionMode = _competitionMode,
                currentEvent = _currentEvent,
                currentGender = _currentGender,
                currentStage = _currentStage,
                currentHeat = _currentHeat,
                totalHeats = _totalHeats,
                isRelay = _isRelay,
                poolConfig = new {
                    length = _poolConfig.Length,
                    lanes = _poolConfig.LaneCount,
                    laneNumbers = _poolConfig.LaneNumbers
                },
                raceState = _raceState.ToString().ToUpper(),
                runningTime = TimeFormatter.FormatRunning(_runningTime),
                laneCloseSettings = new {
                    laneCloseTime = _laneCloseSettings.LaneCloseTime,
                    startBlockCloseDelay = _laneCloseSettings.StartBlockCloseDelay,
                    resultConfirmCloseDelay = _laneCloseSettings.ResultConfirmCloseDelay,
                    falseStartThreshold = _laneCloseSettings.FalseStartThreshold,
                    splitDisplayTime = _laneCloseSettings.SplitDisplayTime,
                    startPosition = _laneCloseSettings.StartPosition
                },
                scoringControlMode = _scoringControlMode,
                resultConfirmed = _resultConfirmed,
                schedule = _schedule.Select(s => new {
                    session = s.SessionNumber, sessionName = s.SessionName,
                    date = s.Date, time = s.Time,
                    eventName = s.EventName, gender = s.Gender,
                    stage = s.Stage, heatCount = s.HeatCount, isRelay = s.IsRelay
                }).ToList(),
                swimmers = swimmerData,
                eventRanking = eventRanking,
                teamScores = teamScoresData,
                records = _records.Select(r => new {
                    eventName = r.EventName, gender = r.Gender, recordType = r.RecordType,
                    holderName = r.HolderName, holderCountry = r.HolderCountry,
                    time = TimeFormatter.Format(r.Time), timeInSeconds = r.Time,
                    date = r.Date, location = r.Location
                }).ToList()
            };
        }

        private List<Swimmer> GetCurrentHeatSwimmers() {
            if (string.IsNullOrEmpty(_currentEvent) || _currentHeat <= 0) return new List<Swimmer>();
            string fullEvent = _currentGender + _currentEvent;
            return _swimmers.Where(s =>
                (_currentGender + s.EventName) == fullEvent &&
                s.CurrentStage == _currentStage &&
                s.Heat == _currentHeat
            ).OrderBy(s => s.Lane).ToList();
        }

        private List<object> GetEventRanking(string eventName, string gender) {
            if (string.IsNullOrEmpty(eventName)) return new List<object>();
            string fullEvent = gender + eventName;
            var results = _swimmers.Where(s =>
                (gender + s.EventName) == fullEvent &&
                s.Status != "DNS" && s.Status != "DSQ"
            ).ToList();

            var ranked = new List<object>();
            var withTimes = results.Where(s => {
                var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage);
                return r != null && r.FinalTime > 0;
            }).OrderBy(s => {
                var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage);
                return r != null ? r.FinalTime : double.MaxValue;
            }).ToList();

            int rank = 1;
            foreach (var sw in withTimes) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage);
                ranked.Add(new {
                    rank = rank++,
                    lane = sw.Lane,
                    bibNumber = sw.BibNumber,
                    name = sw.Name,
                    country = sw.Country,
                    entryTime = sw.EntryTime ?? "",
                    finalTime = r != null ? TimeFormatter.Format(r.FinalTime) : "",
                    timingSource = r != null ? r.TimingSource : "",
                    status = sw.Status ?? ""
                });
            }
            return ranked;
        }

        // ═══════════════════════════════════════════════════════════════
        // 计时硬件
        // ═══════════════════════════════════════════════════════════════
        private void InitializeTimingBridge() {
            _timingBridge = new TimingBridge();
            _timingBridge.OnTimingData += delegate(TimingData data) {
                Dispatcher.Invoke((Action)delegate() {
                    ProcessTimingDataFromHardware(data);
                });
            };
            _timingBridge.OnStatusChanged += delegate(string status) {
                Dispatcher.Invoke((Action)delegate() {
                    TimingStatusText.Text = status;
                    UpdateConnectionStatus();
                });
            };
            _timingBridge.OnLog += delegate(string msg) {
                Dispatcher.Invoke((Action)delegate() {
                    AddLog(msg);
                });
            };
        }

        private void ProcessTimingDataFromHardware(TimingData data) {
            string cmdType = data.CommandType.ToString();
            ProcessTimingData(data.Lane, cmdType, data.TimeInSeconds);
        }

        private void ProcessTimingData(int lane, string cmdType, double timeInSeconds) {
            if (_raceState != RaceState.Racing && cmdType != "StartCommand") return;

            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState == null) return;

            switch (cmdType) {
                case "StartCommand":
                    // 发令信号已在StartRace中处理
                    break;

                case "StartingBlock":
                    // 出发台 — 反应时间
                    if (laneState.LeftStartBlockStatus == DeviceStatus.Open ||
                        laneState.LeftStartBlockStatus == DeviceStatus.FalseStart) {
                        laneState.ReactionTime = timeInSeconds;
                        if (timeInSeconds <= _laneCloseSettings.FalseStartThreshold) {
                            laneState.IsFalseStart = true;
                            AddLog(string.Format("★抢跳! 泳道{0} 反应时间: {1:F3}s", lane, timeInSeconds));
                        } else {
                            AddLog(string.Format("泳道{0} 反应时间: {1:F2}s", lane, timeInSeconds));
                        }
                    }
                    break;

                case "Touchpad":
                    // 触板 — 检查泳道是否打开
                    if (laneState.LeftTouchpadStatus == DeviceStatus.Open ||
                        laneState.RightTouchpadStatus == DeviceStatus.Open) {
                        ProcessTouchpadHit(lane, timeInSeconds, laneState);
                    } else {
                        AddLog(string.Format("泳道{0} 触板数据丢弃（泳道关闭中）", lane));
                    }
                    break;

                case "PushButton1":
                case "PushButton2":
                case "PushButton3":
                    // 盲表
                    if (laneState.LeftBlindWatch1Status == DeviceStatus.Open ||
                        laneState.LeftBlindWatch2Status == DeviceStatus.Open ||
                        laneState.LeftBlindWatch3Status == DeviceStatus.Open ||
                        laneState.RightBlindWatch1Status == DeviceStatus.Open ||
                        laneState.RightBlindWatch2Status == DeviceStatus.Open ||
                        laneState.RightBlindWatch3Status == DeviceStatus.Open) {
                        ProcessBlindWatchData(lane, cmdType, timeInSeconds);
                    }
                    break;
            }

            UpdateLaneStatusDisplay();
            Broadcast();
        }

        private void ProcessTouchpadHit(int lane, double time, LaneDeviceState laneState) {
            // 获取当前运动员
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer == null) return;

            // 获取或创建成绩记录
            var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (result == null) {
                result = new LaneResult {
                    EventName = _currentEvent,
                    Stage = _currentStage,
                    Heat = _currentHeat,
                    Lane = lane
                };
                swimmer.Results.Add(result);
            }

            // 计算分段
            int totalLaps = GetTotalLaps();
            int currentLap = laneState.CurrentLap + 1;
            laneState.CurrentLap = currentLap;

            double prevCumulative = 0;
            if (result.Splits.Count > 0) prevCumulative = result.Splits.Last().CumulativeTime;

            var split = new SplitTime {
                Lap = currentLap,
                Distance = currentLap * _poolConfig.Length,
                Time = time - prevCumulative,
                CumulativeTime = time,
                TouchpadTime = time
            };
            result.Splits.Add(split);
            AddLog(string.Format("泳道{0} 第{1}段: {2} (累计: {3})", lane, currentLap,
                TimeFormatter.Format(split.Time), TimeFormatter.Format(time)));

            if (currentLap >= totalLaps) {
                // 最终到达 — 保存触板时间到LaneResult用于计时源裁定
                laneState.IsFinished = true;
                result.TouchpadTime = time;
                result.FinalTime = time;
                result.TimeInSeconds = time;

                // 计时源裁定（使用最终段的各计时源数据）
                var judgement = TimingBridge.JudgeTimingSource(
                    split.TouchpadTime, split.PushButton1Time, split.PushButton2Time, split.PushButton3Time,
                    split.ManualTouchTime > 0 ? split.ManualTouchTime : Math.Max(laneState.LeftManualTouchTime, laneState.RightManualTouchTime));
                result.FinalTime = judgement.FinalTime;
                result.TimingSource = judgement.Source;
                split.TimingSource = judgement.Source;

                // 关闭该泳道所有设备
                laneState.LeftTouchpadStatus = DeviceStatus.Closed;
                laneState.LeftBlindWatch1Status = DeviceStatus.Closed; laneState.LeftBlindWatch2Status = DeviceStatus.Closed; laneState.LeftBlindWatch3Status = DeviceStatus.Closed;
                laneState.RightTouchpadStatus = DeviceStatus.Closed;
                laneState.RightBlindWatch1Status = DeviceStatus.Closed; laneState.RightBlindWatch2Status = DeviceStatus.Closed; laneState.RightBlindWatch3Status = DeviceStatus.Closed;
                laneState.LaneCloseCountdown = 0;

                AddLog(string.Format("泳道{0} 完赛: {1} (来源:{2})", lane, TimeFormatter.Format(result.FinalTime), result.TimingSource));
                UpdateHeatRanking();
                CheckRecords(swimmer, result);

                // 检查是否所有泳道都完赛
                if (_laneDeviceStates.All(s => s.IsFinished || GetCurrentHeatSwimmers().All(sw => sw.Lane != s.Lane || sw.Status == "DNS"))) {
                    _raceState = RaceState.Finished;
                    UpdateRaceStateDisplay();
                    AddLog("本组比赛结束");
                }
            } else {
                // 分段触碰 — 不立即关闭到达端设备（盲表/出发台可能延后到达）
                // 只切换方向和开始新倒计时
                laneState.Direction = laneState.Direction == "→" ? "←" : "→";
                // 立即开始新的泳道封闭倒计时
                laneState.LaneCloseCountdown = laneState.LaneCloseTime > 0 ? laneState.LaneCloseTime : _laneCloseSettings.LaneCloseTime;
                // 延迟后关闭到达端设备（给盲表和接力出发台留时间）
                string arrivedEnd = laneState.Direction == "→" ? "left" : "right"; // 新方向的出发端=刚到达端
                var closeTimer = new DispatcherTimer();
                closeTimer.Interval = TimeSpan.FromSeconds(_laneCloseSettings.ResultConfirmCloseDelay);
                closeTimer.Tick += delegate(object s2, EventArgs a2) {
                    closeTimer.Stop();
                    if (laneState.IsFinished) return;
                    if (arrivedEnd == "right") {
                        laneState.RightTouchpadStatus = DeviceStatus.Closed;
                        laneState.RightBlindWatch1Status = DeviceStatus.Closed; laneState.RightBlindWatch2Status = DeviceStatus.Closed; laneState.RightBlindWatch3Status = DeviceStatus.Closed;
                    } else {
                        laneState.LeftTouchpadStatus = DeviceStatus.Closed;
                        laneState.LeftBlindWatch1Status = DeviceStatus.Closed; laneState.LeftBlindWatch2Status = DeviceStatus.Closed; laneState.LeftBlindWatch3Status = DeviceStatus.Closed;
                    }
                    Broadcast();
                };
                closeTimer.Start();
            }
        }

        private void SaveManualTouchToSplit(int lane, double time) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer == null) return;
            var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (result != null && result.Splits.Count > 0) {
                result.Splits.Last().ManualTouchTime = time;
            }
        }

        private void ProcessBlindWatchData(int lane, string cmdType, double time) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer == null) return;

            var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (result == null) {
                result = new LaneResult {
                    EventName = _currentEvent,
                    Stage = _currentStage,
                    Heat = _currentHeat,
                    Lane = lane
                };
                swimmer.Results.Add(result);
            }

            // 保存到LaneResult总记录
            switch (cmdType) {
                case "PushButton1": result.PushButton1Time = time; break;
                case "PushButton2": result.PushButton2Time = time; break;
                case "PushButton3": result.PushButton3Time = time; break;
            }
            // 同时保存到当前分段
            if (result.Splits.Count > 0) {
                var currentSplit = result.Splits.Last();
                switch (cmdType) {
                    case "PushButton1": currentSplit.PushButton1Time = time; break;
                    case "PushButton2": currentSplit.PushButton2Time = time; break;
                    case "PushButton3": currentSplit.PushButton3Time = time; break;
                }
            }
            AddLog(string.Format("泳道{0} {1}: {2}", lane, cmdType, TimeFormatter.Format(time)));
        }

        private int GetTotalLaps() {
            // 解析项目距离
            string ev = _currentEvent;
            int distance = 0;
            foreach (char c in ev) {
                if (char.IsDigit(c)) distance = distance * 10 + (c - '0');
                else if (distance > 0) break;
            }
            if (distance == 0) distance = 50;
            return Math.Max(1, distance / _poolConfig.Length);
        }

        private void UpdateHeatRanking() {
            var swimmers = GetCurrentHeatSwimmers();
            var withResults = swimmers.Where(s => {
                var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                return r != null && r.FinalTime > 0 && s.Status != "DSQ";
            }).OrderBy(s => {
                var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                return r.FinalTime;
            }).ToList();

            int rank = 1;
            foreach (var sw in withResults) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                if (r != null) r.Rank = rank;
                sw.CurrentRank = rank++;
            }
        }

        private void CheckRecords(Swimmer swimmer, LaneResult result) {
            if (result.FinalTime <= 0) return;
            foreach (var record in _records.Where(r => r.Gender == _currentGender && r.EventName == _currentEvent)) {
                if (result.FinalTime < record.Time) {
                    AddLog(string.Format("★新纪录! {0} 打破{1}: {2} < {3}",
                        swimmer.Name, record.RecordType,
                        TimeFormatter.Format(result.FinalTime), TimeFormatter.Format(record.Time)));
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 计时器
        // ═══════════════════════════════════════════════════════════════
        private void InitializeTimers() {
            _raceTimer = new DispatcherTimer();
            _raceTimer.Interval = TimeSpan.FromMilliseconds(100);
            _raceTimer.Tick += RaceTimer_Tick;

            _countdownTimer = new DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromMilliseconds(100);
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        private void RaceTimer_Tick(object sender, EventArgs e) {
            // 发令后一直计时，不因比赛结束而停止，直到复位信号
            if (_raceStartTime != DateTime.MinValue) {
                _runningTime = (DateTime.Now - _raceStartTime).TotalSeconds;
                if (RunningTimeText != null) RunningTimeText.Text = TimeFormatter.FormatRunning(_runningTime);
                Broadcast();
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e) {
            bool changed = false;
            foreach (var state in _laneDeviceStates) {
                if (state.LaneCloseCountdown > 0 && !state.IsFinished) {
                    state.LaneCloseCountdown -= 0.1;
                    if (state.LaneCloseCountdown <= 0) {
                        state.LaneCloseCountdown = 0;
                        // 只打开运动员即将到达端的触板和盲表
                        // 方向"→"表示向右游，到达端是右端；"←"表示向左游，到达端是左端
                        bool arriveRight = state.Direction == "→";
                        if (arriveRight) {
                            if (!state.RightTouchpadBroken) state.RightTouchpadStatus = DeviceStatus.Open;
                            if (!state.RightBlindWatch1Broken) state.RightBlindWatch1Status = DeviceStatus.Open;
                            if (!state.RightBlindWatch2Broken) state.RightBlindWatch2Status = DeviceStatus.Open;
                            if (!state.RightBlindWatch3Broken) state.RightBlindWatch3Status = DeviceStatus.Open;
                        } else {
                            if (!state.LeftTouchpadBroken) state.LeftTouchpadStatus = DeviceStatus.Open;
                            if (!state.LeftBlindWatch1Broken) state.LeftBlindWatch1Status = DeviceStatus.Open;
                            if (!state.LeftBlindWatch2Broken) state.LeftBlindWatch2Status = DeviceStatus.Open;
                            if (!state.LeftBlindWatch3Broken) state.LeftBlindWatch3Status = DeviceStatus.Open;
                        }
                        AddLog(string.Format("泳道{0} 倒计时结束，{1}端设备已打开", state.Lane, arriveRight ? "右" : "左"));
                    }
                    changed = true;
                }
            }
            if (changed) Broadcast();
        }

        // ═══════════════════════════════════════════════════════════════
        // 比赛控制按钮
        // ═══════════════════════════════════════════════════════════════
        private void Ready_Click(object sender, RoutedEventArgs e) {
            if (_raceState != RaceState.Waiting) return;
            _raceState = RaceState.Ready;
            UpdateRaceStateDisplay();

            // 重置泳道状态
            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
            }

            AddLog("就位");
            Broadcast();
        }

        private void StartRace_Click(object sender, RoutedEventArgs e) {
            if (_raceState != RaceState.Ready) return;
            _raceState = RaceState.Racing;
            _raceStartTime = DateTime.Now;
            _runningTime = 0;
            UpdateRaceStateDisplay();

            // 启动计时器
            _raceTimer.Start();
            _countdownTimer.Start();

            // 泳道设备状态：发令后
            // 所有触板和盲表关闭，出发端的出发台打开
            bool startLeft = _laneCloseSettings.StartPosition != "right";
            foreach (var state in _laneDeviceStates) {
                state.LeftTouchpadStatus = DeviceStatus.Closed;
                state.LeftBlindWatch1Status = DeviceStatus.Closed; state.LeftBlindWatch2Status = DeviceStatus.Closed; state.LeftBlindWatch3Status = DeviceStatus.Closed;
                state.RightTouchpadStatus = DeviceStatus.Closed;
                state.RightBlindWatch1Status = DeviceStatus.Closed; state.RightBlindWatch2Status = DeviceStatus.Closed; state.RightBlindWatch3Status = DeviceStatus.Closed;
                // 出发端的出发台打开
                state.LeftStartBlockStatus = startLeft ? DeviceStatus.Open : DeviceStatus.Closed;
                state.RightStartBlockStatus = startLeft ? DeviceStatus.Closed : DeviceStatus.Open;
                state.LaneCloseCountdown = state.LaneCloseTime > 0 ? state.LaneCloseTime : _laneCloseSettings.LaneCloseTime;
            }

            // 延迟关闭出发台
            var sbTimer = new DispatcherTimer();
            sbTimer.Interval = TimeSpan.FromSeconds(_laneCloseSettings.StartBlockCloseDelay);
            sbTimer.Tick += delegate(object s, EventArgs args) {
                sbTimer.Stop();
                foreach (var state in _laneDeviceStates) {
                    if (!state.IsFalseStart) {
                        if (startLeft) state.LeftStartBlockStatus = DeviceStatus.Closed;
                        else state.RightStartBlockStatus = DeviceStatus.Closed;
                    }
                }
                Broadcast();
            };
            sbTimer.Start();

            AddLog("发令 - 比赛开始");
            Broadcast();
        }

        private void Restart_Click(object sender, RoutedEventArgs e) {
            // 计时复位：重置计时器和泳道设备状态
            _raceState = RaceState.Waiting;
            _raceTimer.Stop();
            _countdownTimer.Stop();
            _runningTime = 0;
            _raceStartTime = DateTime.MinValue;
            if (RunningTimeText != null) RunningTimeText.Text = "0.0";
            UpdateRaceStateDisplay();

            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
            }

            if (_resultConfirmed) {
                // 成绩已确认，不清除数据，只复位计时器和设备
                AddLog("计时复位 — 成绩已确认，数据保留");
            } else {
                // 成绩未确认，清除当前组的成绩数据
                var currentSwimmers = GetCurrentHeatSwimmers();
                foreach (var sw in currentSwimmers) {
                    var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                    if (result != null) sw.Results.Remove(result);
                    sw.Status = "";
                }
                AddLog("计时复位 — 本组数据已清除");
            }
            Broadcast();
        }

        private void ConfirmResult_Click(object sender, RoutedEventArgs e) {
            _countdownTimer.Stop();
            _raceState = RaceState.Finished;
            _resultConfirmed = true;
            UpdateHeatRanking();
            AutoSaveData();
            UpdateLaneStatusDisplay();
            UpdateRaceStateDisplay();

            AddLog(string.Format("★ 已确认本组成绩: {0}子 {1} {2} 第{3}组", _currentGender, _currentEvent, _currentStage, _currentHeat));

            // 检查该项目该阶段是否所有组都已完赛
            CheckStageComplete();

            Broadcast();
        }

        private void CheckStageComplete() {
            if (string.IsNullOrEmpty(_currentEvent) || string.IsNullOrEmpty(_currentStage)) return;
            if (_currentStage == "决赛") return; // 决赛无需晋级

            // 查找同项目同阶段的所有运动员
            var stageSwimmers = _swimmers.Where(s =>
                s.EventName == _currentEvent &&
                s.Gender == _currentGender &&
                s.CurrentStage == _currentStage
            ).ToList();

            if (stageSwimmers.Count == 0) return;

            // 检查是否所有运动员都有该阶段的成绩
            bool allDone = true;
            foreach (var sw in stageSwimmers) {
                if (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ") continue;
                var result = sw.GetResultForStage(_currentStage);
                if (result == null || result.FinalTime <= 0) { allDone = false; break; }
            }

            if (allDone) {
                // 确定下一阶段
                var stages = HeatScheduler.GetStages(stageSwimmers.Count);
                int currentIdx = stages.IndexOf(_currentStage);
                if (currentIdx >= 0 && currentIdx < stages.Count - 1) {
                    string nextStage = stages[currentIdx + 1];
                    int promoCount = HeatScheduler.GetPromotionCount(_currentStage, nextStage);

                    AddLog(string.Format("★ {0}子{1} {2}全部完赛！可晋级{3}人到{4}", _currentGender, _currentEvent, _currentStage, promoCount, nextStage));

                    var answer = MessageBox.Show(
                        string.Format("{0}子 {1} {2} 全部{3}人已完赛！\n\n是否自动晋级前{4}名到{5}？\n（按成绩统一排名，蛇形分组）",
                            _currentGender, _currentEvent, _currentStage, stageSwimmers.Count, promoCount, nextStage),
                        "自动晋级", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (answer == MessageBoxResult.Yes) {
                        var filtered = _swimmers.Where(s => s.Gender == _currentGender && s.EventName == _currentEvent).ToList();
                        var promoted = HeatScheduler.GetPromotedSwimmers(filtered, _currentEvent, _currentStage, promoCount);

                        if (promoted.Count > 0) {
                            var assignments = HeatScheduler.GenerateHeatsFromResults(promoted, _poolConfig, _currentEvent, nextStage, _currentStage);
                            int heatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;

                            // 更新赛程
                            bool scheduleExists = _schedule.Any(s => s.Gender == _currentGender && s.EventName == _currentEvent && s.Stage == nextStage);
                            if (!scheduleExists) {
                                _schedule.Add(new ScheduleItem {
                                    SessionNumber = _schedule.Count > 0 ? _schedule.Max(s => s.SessionNumber) + 1 : 1,
                                    Gender = _currentGender,
                                    EventName = _currentEvent,
                                    Stage = nextStage,
                                    HeatCount = heatCount
                                });
                            } else {
                                var schedItem = _schedule.FirstOrDefault(s => s.Gender == _currentGender && s.EventName == _currentEvent && s.Stage == nextStage);
                                if (schedItem != null) schedItem.HeatCount = heatCount;
                            }

                            BuildScheduleTree();
                            AutoSaveData();
                            AddLog(string.Format("已自动晋级{0}人到{1}，分为{2}组", promoted.Count, nextStage, heatCount));
                            MessageBox.Show(string.Format("已将{0}名运动员晋级到{1}，分为{2}组。\n请在赛程树中选择{1}的组次开始比赛。", promoted.Count, nextStage, heatCount));
                        }
                    }
                }
            } else {
                AddLog("请选择下一组比赛");
            }
        }

        private void PrevHeat_Click(object sender, RoutedEventArgs e) {
            if (_currentHeat > 1) {
                SetCurrentHeat(_currentHeat - 1);
            }
        }

        private void NextHeat_Click(object sender, RoutedEventArgs e) {
            if (_currentHeat < _totalHeats) {
                SetCurrentHeat(_currentHeat + 1);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 赛事导航
        // ═══════════════════════════════════════════════════════════════
        private void SetCurrentEvent(string eventName) {
            // 解析事件名：格式 "男子100米自由泳" 或 "女子200米蝶泳"
            if (eventName.StartsWith("男") || eventName.StartsWith("女")) {
                _currentGender = eventName.Substring(0, 1);
                _currentEvent = eventName.Length > 1 ? eventName.Substring(1) : "";
                if (_currentEvent.StartsWith("子")) _currentEvent = _currentEvent.Substring(1);
            } else {
                _currentEvent = eventName;
            }
            _isRelay = _currentEvent.Contains("接力");
            CurrentEventText.Text = _currentGender + _currentEvent;
        }

        private void SetCurrentStage(string stage) {
            _currentStage = stage;
            CurrentStageText.Text = stage;

            // 计算该阶段总组数
            _totalHeats = _swimmers.Count(s =>
                s.EventName == _currentEvent &&
                s.Gender.StartsWith(_currentGender) &&
                s.CurrentStage == _currentStage
            );
            if (_totalHeats > 0 && _poolConfig.LaneCount > 0) {
                _totalHeats = (int)Math.Ceiling((double)_totalHeats / _poolConfig.LaneCount);
            }
        }

        private void SetCurrentHeat(int heat) {
            _currentHeat = heat;
            CurrentHeatText.Text = string.Format("第{0}组 / 共{1}组", heat, _totalHeats);
            _raceState = RaceState.Waiting;
            _resultConfirmed = false;
            // 切换组次 = 复位计时器
            _runningTime = 0;
            _raceStartTime = DateTime.MinValue;
            _raceTimer.Stop();
            _countdownTimer.Stop();
            if (RunningTimeText != null) RunningTimeText.Text = "0.0";
            UpdateRaceStateDisplay();

            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
            }

            UpdateLaneStatusDisplay();
            Broadcast();
        }

        private void ScheduleTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e) {
            var item = ScheduleTree.SelectedItem as TreeViewItem;
            if (item == null || item.Tag == null) return;

            string tag = item.Tag.ToString();
            // 格式: "event:性别|项目|阶段" 或 "heat:性别|项目|阶段|组次"
            if (tag.StartsWith("heat:")) {
                string[] parts = tag.Substring(5).Split('|');
                if (parts.Length >= 4) {
                    _currentGender = parts[0];
                    _currentEvent = parts[1];
                    _currentStage = parts[2];
                    _isRelay = _currentEvent.Contains("接力");

                    int heat;
                    if (int.TryParse(parts[3], out heat)) {
                        // 计算总组数
                        _totalHeats = CountHeatsForEvent(_currentGender, _currentEvent, _currentStage);
                        CurrentEventText.Text = _currentGender + "子" + _currentEvent;
                        CurrentStageText.Text = _currentStage;
                        SetCurrentHeat(heat);
                    }
                }
            }
        }

        private int CountHeatsForEvent(string gender, string eventName, string stage) {
            int count = _swimmers.Count(s =>
                s.EventName == eventName &&
                s.Gender.StartsWith(gender) &&
                s.CurrentStage == stage &&
                s.Heat > 0
            );
            if (count == 0) return 1;
            return _swimmers.Where(s =>
                s.EventName == eventName &&
                s.Gender.StartsWith(gender) &&
                s.CurrentStage == stage &&
                s.Heat > 0
            ).Max(s => s.Heat);
        }

        private void BuildScheduleTree() {
            ScheduleTree.Items.Clear();
            var sessions = _schedule.GroupBy(s => s.SessionNumber).OrderBy(g => g.Key);

            foreach (var session in sessions) {
                var sessionItem = new TreeViewItem {
                    Header = session.First().SessionName ?? string.Format("第{0}单元", session.Key),
                    IsExpanded = true
                };

                foreach (var ev in session) {
                    string header = string.Format("{0}子 {1} {2}", ev.Gender, ev.EventName, ev.Stage);
                    var eventItem = new TreeViewItem {
                        Header = header,
                        Tag = string.Format("event:{0}|{1}|{2}", ev.Gender, ev.EventName, ev.Stage)
                    };

                    int heatCount = ev.HeatCount > 0 ? ev.HeatCount : 1;
                    for (int h = 1; h <= heatCount; h++) {
                        var heatItem = new TreeViewItem {
                            Header = string.Format("第{0}组 (共{1}组)", h, heatCount),
                            Tag = string.Format("heat:{0}|{1}|{2}|{3}", ev.Gender, ev.EventName, ev.Stage, h)
                        };
                        eventItem.Items.Add(heatItem);
                    }
                    sessionItem.Items.Add(eventItem);
                }
                ScheduleTree.Items.Add(sessionItem);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 泳道状态显示更新
        // ═══════════════════════════════════════════════════════════════
        private void UpdateLaneStatusDisplay() {
            var currentSwimmers = GetCurrentHeatSwimmers();
            var displayData = new List<object>();

            foreach (var sw in currentSwimmers) {
                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == sw.Lane);
                var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);

                displayData.Add(new {
                    Lane = sw.Lane,
                    Name = sw.Name,
                    Country = sw.Country ?? "",
                    EntryTime = sw.EntryTime ?? "",
                    Direction = laneState != null ? laneState.Direction : "→",
                    ReactionTime = laneState != null && laneState.ReactionTime != 0 ? laneState.ReactionTime.ToString("F2") : "",
                    SplitTime = result != null && result.Splits.Count > 0 ? TimeFormatter.Format(result.Splits.Last().Time) : "",
                    FinalTime = result != null ? result.FinalTimeDisplay : "",
                    Rank = result != null && result.Rank > 0 ? result.Rank.ToString() : "",
                    Status = sw.Status ?? "",
                    TimingSource = result != null ? result.TimingSource : ""
                });
            }

            LaneStatusGrid.ItemsSource = displayData;
        }

        private void UpdateRaceStateDisplay() {
            switch (_raceState) {
                case RaceState.Waiting:
                    RaceStateText.Text = "等待";
                    RaceStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    RaceStateIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    RaceStateLabel.Text = "等待";
                    break;
                case RaceState.Ready:
                    RaceStateText.Text = "就位";
                    RaceStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                    RaceStateIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                    RaceStateLabel.Text = "就位";
                    break;
                case RaceState.Racing:
                    RaceStateText.Text = "比赛中";
                    RaceStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
                    RaceStateIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
                    RaceStateLabel.Text = "比赛中";
                    break;
                case RaceState.Finished:
                    RaceStateText.Text = "已完赛";
                    RaceStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    RaceStateIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    RaceStateLabel.Text = "已完赛";
                    break;
            }
            CompModeText.Text = _competitionMode == "domestic" ? "国内" : "国际";
            PoolInfoText.Text = string.Format("{0}米 {1}道", _poolConfig.Length, _poolConfig.LaneCount);
        }

        // ═══════════════════════════════════════════════════════════════
        // 泳道操作
        // ═══════════════════════════════════════════════════════════════
        private void MarkLaneStatus(int lane, string status) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer != null) {
                swimmer.Status = status;
                AddLog(string.Format("泳道{0} {1} 标记为 {2}", lane, swimmer.Name, status));
                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
                if (laneState != null) laneState.IsFinished = true;
                UpdateLaneStatusDisplay();
                AutoSaveData();
                Broadcast();
            }
        }

        private void OverrideLaneTime(int lane, double time) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer == null) return;

            var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (result == null) {
                result = new LaneResult {
                    EventName = _currentEvent,
                    Stage = _currentStage,
                    Heat = _currentHeat,
                    Lane = lane
                };
                swimmer.Results.Add(result);
            }
            result.FinalTime = time;
            result.TimeInSeconds = time;
            result.TimingSource = "MAN";

            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState != null) laneState.IsFinished = true;

            AddLog(string.Format("泳道{0} 手动输入成绩: {1}", lane, TimeFormatter.Format(time)));
            UpdateHeatRanking();
            UpdateLaneStatusDisplay();
            AutoSaveData();
            Broadcast();
        }

        private void SetDeviceStatus(int lane, string device, string status) {
            var state = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (state == null) return;
            bool broken = status == "broken";

            switch (device) {
                case "leftTouchpad": state.LeftTouchpadBroken = broken; break;
                case "leftBlindWatch1": state.LeftBlindWatch1Broken = broken; break;
                case "leftBlindWatch2": state.LeftBlindWatch2Broken = broken; break;
                case "leftBlindWatch3": state.LeftBlindWatch3Broken = broken; break;
                case "leftStartBlock": state.LeftStartBlockBroken = broken; break;
                case "rightTouchpad": state.RightTouchpadBroken = broken; break;
                case "rightBlindWatch1": state.RightBlindWatch1Broken = broken; break;
                case "rightBlindWatch2": state.RightBlindWatch2Broken = broken; break;
                case "rightBlindWatch3": state.RightBlindWatch3Broken = broken; break;
                case "rightStartBlock": state.RightStartBlockBroken = broken; break;
            }
            AddLog(string.Format("泳道{0} {1} 设为 {2}", lane, device, broken ? "损坏" : "正常"));
            Broadcast();
        }

        private void MarkDNS_Click(object sender, RoutedEventArgs e) {
            var selected = LaneStatusGrid.SelectedItem;
            if (selected == null) { AddLog("请先选中一个泳道"); return; }
            int lane = (int)selected.GetType().GetProperty("Lane").GetValue(selected, null);
            MarkLaneStatus(lane, "DNS");
        }

        private void MarkDNF_Click(object sender, RoutedEventArgs e) {
            var selected = LaneStatusGrid.SelectedItem;
            if (selected == null) { AddLog("请先选中一个泳道"); return; }
            int lane = (int)selected.GetType().GetProperty("Lane").GetValue(selected, null);
            MarkLaneStatus(lane, "DNF");
        }

        private void MarkDSQ_Click(object sender, RoutedEventArgs e) {
            var selected = LaneStatusGrid.SelectedItem;
            if (selected == null) { AddLog("请先选中一个泳道"); return; }
            int lane = (int)selected.GetType().GetProperty("Lane").GetValue(selected, null);
            MarkLaneStatus(lane, "DSQ");
        }

        private void ManualTime_Click(object sender, RoutedEventArgs e) {
            var selected = LaneStatusGrid.SelectedItem;
            if (selected == null) { AddLog("请先选中一个泳道"); return; }
            int lane = (int)selected.GetType().GetProperty("Lane").GetValue(selected, null);
            var dlg = new Window {
                Title = "手动输入成绩", Width = 300, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "请输入成绩（如 49.23 或 1:23.45）:" });
            var tb = new TextBox { Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(4) };
            sp.Children.Add(tb);
            var btn = new Button { Content = "确定", Padding = new Thickness(16, 4, 16, 4), HorizontalAlignment = HorizontalAlignment.Right };
            btn.Click += delegate { dlg.DialogResult = true; };
            sp.Children.Add(btn);
            dlg.Content = sp;
            tb.Focus();
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(tb.Text)) {
                double time = TimeFormatter.Parse(tb.Text.Trim());
                if (time > 0) OverrideLaneTime(lane, time);
            }
        }

        private void DeviceStatus_Click(object sender, RoutedEventArgs e) {
            var win = new DeviceStatusWindow(_laneDeviceStates, _poolConfig);
            win.Owner = this;
            if (win.ShowDialog() == true) {
                Broadcast();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 运动员管理
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// 检查运动员是否重复（同姓名+同性别+同项目视为重复）
        /// 返回重复的运动员，无重复返回null
        /// </summary>
        private Swimmer FindDuplicate(string name, string gender, string eventName, string bibNumber) {
            foreach (var s in _swimmers) {
                // 号码牌相同且非空 = 重复
                if (!string.IsNullOrEmpty(bibNumber) && !string.IsNullOrEmpty(s.BibNumber) && s.BibNumber == bibNumber && s.EventName == eventName) return s;
                // 同姓名 + 同性别 + 同项目 = 重复
                if (!string.IsNullOrEmpty(name) && s.Name == name && s.Gender == gender && s.EventName == eventName) return s;
            }
            return null;
        }

        private string GenerateNextBibNumber() {
            int max = 0;
            foreach (var s in _swimmers) {
                int n;
                if (!string.IsNullOrEmpty(s.BibNumber) && int.TryParse(s.BibNumber, out n) && n > max) max = n;
            }
            return (max + 1).ToString("D3");
        }

        private void AddSwimmer_Click(object sender, RoutedEventArgs e) {
            _swimmers.Add(new Swimmer {
                BibNumber = GenerateNextBibNumber(),
                Gender = "男"
            });
            AutoSaveData();
        }

        private void DeleteSwimmer_Click(object sender, RoutedEventArgs e) {
            var selected = SwimmerGrid.SelectedItem as Swimmer;
            if (selected != null) {
                if (MessageBox.Show(string.Format("确定删除运动员 {0}({1}) 的 {2} 报名记录？", selected.Name, selected.BibNumber, selected.EventName),
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                    _swimmers.Remove(selected);
                    AutoSaveData();
                    Broadcast();
                    AddLog(string.Format("已删除: {0}({1}) {2}", selected.Name, selected.BibNumber, selected.EventName));
                }
            }
        }

        private void EditSwimmer_Click(object sender, RoutedEventArgs e) {
            var selected = SwimmerGrid.SelectedItem as Swimmer;
            if (selected == null) { MessageBox.Show("请先选中要修改的运动员"); return; }

            var dlg = new Window {
                Title = string.Format("修改运动员信息 — {0}({1})", selected.Name, selected.BibNumber),
                Width = 500, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = string.Format("参赛号: {0}", selected.BibNumber), FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string[] labels = { "姓名:", "性别:", "出生日期:", "年龄:", "身份证号:", "代表队:", "联系电话:", "项目:", "报名成绩:", "协会注册号:", "备注:" };
            for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            // Row 0: 姓名 + 性别
            var tbName = AddEditField(grid, 0, 0, "姓名:", selected.Name);
            var cbGender = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
            cbGender.Items.Add("男"); cbGender.Items.Add("女"); cbGender.Items.Add("混合");
            cbGender.SelectedItem = selected.Gender ?? "男";
            AddEditLabel(grid, 0, 2, "性别:");
            Grid.SetRow(cbGender, 0); Grid.SetColumn(cbGender, 3);
            grid.Children.Add(cbGender);

            // Row 1: 出生日期 + 年龄
            var tbBirth = AddEditField(grid, 1, 0, "出生日期:", selected.BirthDate);
            var tbAge = AddEditField(grid, 1, 2, "年龄:", selected.Age.ToString());

            // Row 2: 身份证号 + 代表队
            var tbID = AddEditField(grid, 2, 0, "身份证号:", selected.IDNumber);
            var tbCountry = AddEditField(grid, 2, 2, "代表队:", selected.Country);

            // Row 3: 电话 + 协会注册号
            var tbPhone = AddEditField(grid, 3, 0, "联系电话:", selected.Phone);
            var tbCSA = AddEditField(grid, 3, 2, "协会注册号:", selected.CSANumber);

            // Row 4: 项目 + 报名成绩
            var tbEvent = AddEditField(grid, 4, 0, "项目:", selected.EventName);
            var tbEntry = AddEditField(grid, 4, 2, "报名成绩:", selected.EntryTime);

            // Row 5: 备注
            var tbNotes = AddEditField(grid, 5, 0, "备注:", selected.Notes);

            sp.Children.Add(grid);

            // 按钮
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button { Content = "确认修改", Padding = new Thickness(20, 6, 20, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = Brushes.White, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);

            dlg.Content = sp;

            if (dlg.ShowDialog() == true) {
                selected.Name = tbName.Text.Trim();
                selected.Gender = cbGender.SelectedItem.ToString();
                selected.BirthDate = tbBirth.Text.Trim();
                int age; if (int.TryParse(tbAge.Text.Trim(), out age)) selected.Age = age;
                selected.IDNumber = tbID.Text.Trim();
                selected.Country = tbCountry.Text.Trim();
                selected.Phone = tbPhone.Text.Trim();
                selected.CSANumber = tbCSA.Text.Trim();
                selected.EventName = tbEvent.Text.Trim();
                selected.EntryTime = tbEntry.Text.Trim();
                selected.EntryTimeSeconds = TimeFormatter.Parse(selected.EntryTime);
                selected.Notes = tbNotes.Text.Trim();

                // 同步修改同一参赛号的其他项目记录的个人信息
                foreach (var s in _swimmers) {
                    if (s != selected && s.BibNumber == selected.BibNumber) {
                        s.Name = selected.Name;
                        s.Gender = selected.Gender;
                        s.BirthDate = selected.BirthDate;
                        s.Age = selected.Age;
                        s.IDNumber = selected.IDNumber;
                        s.Country = selected.Country;
                        s.Phone = selected.Phone;
                        s.CSANumber = selected.CSANumber;
                    }
                }

                SwimmerGrid.Items.Refresh();
                AutoSaveData();
                Broadcast();
                AddLog(string.Format("已修改运动员: {0}({1}) {2}", selected.Name, selected.BibNumber, selected.EventName));
            }
        }

        private TextBox AddEditField(Grid grid, int row, int col, string label, string value) {
            AddEditLabel(grid, row, col, label);
            var tb = new TextBox { Text = value ?? "", Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, col + 1);
            grid.Children.Add(tb);
            return tb;
        }

        private void AddEditLabel(Grid grid, int row, int col, string text) {
            var lbl = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, col);
            grid.Children.Add(lbl);
        }

        private void ImportCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|所有文件|*.*",
                Title = "导入运动员CSV"
            };
            if (dlg.ShowDialog() == true) {
                try {
                    string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                    int imported = 0, skipped = 0;
                    for (int i = 1; i < lines.Length; i++) {
                        string[] cols = lines[i].Split(',');
                        if (cols.Length < 5) continue;
                        // CSV格式: 号码,姓名,性别,代表队,项目,报名成绩,年龄,出生日期,身份证号,电话,备注
                        string bibNum = cols[0].Trim();
                        if (string.IsNullOrEmpty(bibNum)) bibNum = GenerateNextBibNumber();
                        var sw = new Swimmer {
                            BibNumber = bibNum,
                            Name = cols[1].Trim(),
                            Gender = cols[2].Trim(),
                            Country = cols[3].Trim(),
                            EventName = cols[4].Trim()
                        };
                        if (cols.Length > 5) sw.EntryTime = cols[5].Trim();
                        if (cols.Length > 6) { int age; if (int.TryParse(cols[6].Trim(), out age)) sw.Age = age; }
                        if (cols.Length > 7) sw.BirthDate = cols[7].Trim();
                        if (cols.Length > 8) sw.IDNumber = cols[8].Trim();
                        if (cols.Length > 9) sw.Phone = cols[9].Trim();
                        if (cols.Length > 10) sw.Notes = cols[10].Trim();
                        sw.EntryTimeSeconds = TimeFormatter.Parse(sw.EntryTime);
                        var dup = FindDuplicate(sw.Name, sw.Gender, sw.EventName, sw.BibNumber);
                        if (dup != null) {
                            skipped++;
                            continue;
                        }
                        _swimmers.Add(sw);
                        imported++;
                    }
                    AddLog(string.Format("CSV导入完成: {0}名运动员, {1}名重复跳过", imported, skipped));
                    AutoSaveData();
                    Broadcast();
                } catch (Exception ex) {
                    AddLog("CSV导入失败: " + ex.Message);
                }
            }
        }

        private void AddRelay_Click(object sender, RoutedEventArgs e) {
            _relayTeams.Add(new RelayTeam());
            AutoSaveData();
        }

        private void DeleteRelay_Click(object sender, RoutedEventArgs e) {
            var selected = RelayGrid.SelectedItem as RelayTeam;
            if (selected != null) {
                _relayTeams.Remove(selected);
                AutoSaveData();
            }
        }

        // 本地注册项目列表
        private List<Tuple<string, string>> _regEventList = new List<Tuple<string, string>>(); // eventName, entryTime

        private void RegAddEvent_Click(object sender, RoutedEventArgs e) {
            string eventName = RegEventCombo.SelectedItem != null ? ((ComboBoxItem)RegEventCombo.SelectedItem).Content.ToString() : "";
            if (string.IsNullOrEmpty(eventName)) { RegStatusText.Text = "请选择项目"; return; }

            foreach (var ev in _regEventList) {
                if (ev.Item1 == eventName) {
                    MessageBox.Show("已添加此项目，不能重复！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            _regEventList.Add(new Tuple<string, string>(eventName, RegEntryTimeBox.Text.Trim()));
            RegEntryTimeBox.Clear();
            RefreshRegEventList();
        }

        private void RegRemoveEvent_Click(object sender, RoutedEventArgs e) {
            int idx = RegEventListBox.SelectedIndex;
            if (idx < 0 || idx >= _regEventList.Count) { MessageBox.Show("请先选中要删除的项目"); return; }
            _regEventList.RemoveAt(idx);
            RefreshRegEventList();
        }

        private void RefreshRegEventList() {
            RegEventListBox.Items.Clear();
            foreach (var ev in _regEventList) {
                string display = string.IsNullOrEmpty(ev.Item2) ? ev.Item1 : string.Format("{0}  (报名: {1})", ev.Item1, ev.Item2);
                RegEventListBox.Items.Add(display);
            }
        }

        private void RegSubmitAll_Click(object sender, RoutedEventArgs e) {
            string name = RegNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { RegStatusText.Text = "请输入姓名"; return; }
            if (_regEventList.Count == 0) { RegStatusText.Text = "请至少添加一个参赛项目"; return; }

            string gender = ((ComboBoxItem)RegGenderCombo.SelectedItem).Content.ToString();
            string bibNumber = GenerateNextBibNumber();

            string birthDate = RegBirthDatePicker.SelectedDate.HasValue ? RegBirthDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") : "";
            int age = 0;
            if (RegBirthDatePicker.SelectedDate.HasValue) {
                var today = DateTime.Today;
                var bd = RegBirthDatePicker.SelectedDate.Value;
                age = today.Year - bd.Year;
                if (bd.Date > today.AddYears(-age)) age--;
            }

            int added = 0;
            foreach (var ev in _regEventList) {
                var dup = FindDuplicate(name, gender, ev.Item1, bibNumber);
                if (dup != null) continue;

                var sw = new Swimmer {
                    BibNumber = bibNumber,
                    Name = name,
                    Gender = gender,
                    Country = RegCountryBox.Text.Trim(),
                    IDNumber = RegIDNumberBox.Text.Trim(),
                    Phone = RegPhoneBox.Text.Trim(),
                    EventName = ev.Item1,
                    EntryTime = ev.Item2,
                    BirthDate = birthDate,
                    Age = age,
                    Notes = RegNotesBox.Text.Trim()
                };
                sw.EntryTimeSeconds = TimeFormatter.Parse(sw.EntryTime);
                _swimmers.Add(sw);
                added++;
            }

            AddLog(string.Format("注册运动员: {0}({1}) {2}个项目", name, bibNumber, added));
            RegStatusText.Text = string.Format("注册成功！参赛号: {0}，共{1}个项目", bibNumber, added);
            RegStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));

            // 清空表单准备下一位
            RegNameBox.Clear();
            RegIDNumberBox.Clear();
            RegCountryBox.Clear();
            RegPhoneBox.Clear();
            RegNotesBox.Clear();
            RegBirthDatePicker.SelectedDate = null;
            RegEntryTimeBox.Clear();
            _regEventList.Clear();
            RefreshRegEventList();

            AutoSaveData();
            Broadcast();
        }

        private void FilterEvent_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) RefreshSwimmerFilter(); }
        private void FilterGender_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) RefreshSwimmerFilter(); }

        private void RefreshSwimmerFilter() {
            if (FilterEventCombo == null || FilterGenderCombo == null || SwimmerGrid == null) return;
            string eventFilter = FilterEventCombo.SelectedItem != null ? ((ComboBoxItem)FilterEventCombo.SelectedItem).Content.ToString() : "全部";
            string genderFilter = FilterGenderCombo.SelectedItem != null ? ((ComboBoxItem)FilterGenderCombo.SelectedItem).Content.ToString() : "全部";

            var filtered = _swimmers.Where(s => {
                if (eventFilter != "全部" && s.EventName != eventFilter) return false;
                if (genderFilter != "全部" && s.Gender != genderFilter) return false;
                return true;
            }).ToList();

            SwimmerGrid.ItemsSource = filtered;
        }

        // ═══════════════════════════════════════════════════════════════
        // 赛程管理
        // ═══════════════════════════════════════════════════════════════
        private void AddSchedule_Click(object sender, RoutedEventArgs e) {
            _schedule.Add(new ScheduleItem {
                SessionNumber = _schedule.Count > 0 ? _schedule.Max(s => s.SessionNumber) : 1
            });
            AutoSaveData();
        }

        private void DeleteSchedule_Click(object sender, RoutedEventArgs e) {
            var selected = ScheduleGrid.SelectedItem as ScheduleItem;
            if (selected != null) {
                _schedule.Remove(selected);
                AutoSaveData();
            }
        }

        private void AutoGenerateHeats_Click(object sender, RoutedEventArgs e) {
            if (_swimmers.Count == 0) {
                MessageBox.Show("没有已注册的运动员，请先注册运动员再生成分组。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 如果赛程表为空，先根据运动员数据自动生成赛程
            if (_schedule.Count == 0) {
                AddLog("赛程表为空，根据已注册运动员自动生成赛程...");
                var eventGroups = _swimmers.GroupBy(s => new { s.Gender, s.EventName })
                    .OrderBy(g => g.Key.Gender).ThenBy(g => g.Key.EventName);
                int sessionNum = 1;
                foreach (var group in eventGroups) {
                    string gender = group.Key.Gender;
                    string eventName = group.Key.EventName;
                    bool isRelay = eventName.Contains("接力");
                    var stages = HeatScheduler.GetStages(group.Count());
                    string firstStage = stages[0];
                    // 将该项目所有运动员的阶段设为第一阶段
                    foreach (var sw in group) sw.CurrentStage = firstStage;
                    foreach (string stage in stages) {
                        _schedule.Add(new ScheduleItem {
                            SessionNumber = sessionNum,
                            SessionName = string.Format("第{0}单元", sessionNum),
                            Date = StartDateBox != null ? StartDateBox.Text : "",
                            Gender = gender,
                            EventName = eventName,
                            Stage = stage,
                            IsRelay = isRelay,
                            HeatCount = 0
                        });
                    }
                }
                AddLog(string.Format("自动生成{0}条赛程", _schedule.Count));
            }

            int generated = 0;
            foreach (var item in _schedule) {
                string fullEvent = item.EventName;
                string stage = item.Stage;
                string gender = item.Gender;

                var eventSwimmers = _swimmers.Where(s =>
                    s.EventName == fullEvent &&
                    s.Gender == gender &&
                    s.CurrentStage == stage
                ).ToList();

                // 宽松匹配：如精确匹配无结果，尝试StartsWith
                if (eventSwimmers.Count == 0) {
                    eventSwimmers = _swimmers.Where(s =>
                        s.EventName == fullEvent &&
                        (s.Gender.StartsWith(gender) || gender.StartsWith(s.Gender)) &&
                        s.CurrentStage == stage
                    ).ToList();
                }

                if (eventSwimmers.Count == 0) continue;

                var assignments = HeatScheduler.GenerateHeats(eventSwimmers, _poolConfig, fullEvent, stage);
                item.HeatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;
                generated += assignments.Count;
                AddLog(string.Format("  {0} {1} {2}: {3}人 → {4}组", gender, fullEvent, stage, eventSwimmers.Count, item.HeatCount));
            }

            BuildScheduleTree();
            ScheduleGrid.Items.Refresh();
            AddLog(string.Format("自动分组完成: {0}名运动员已分配到各组", generated));
            if (generated == 0) {
                MessageBox.Show("未分配任何运动员。\n\n请检查：\n1. 运动员的项目名称是否与赛程一致\n2. 运动员的性别是否与赛程一致\n3. 运动员的阶段是否与赛程一致", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            AutoSaveData();
            Broadcast();
        }

        // ═══════════════════════════════════════════════════════════════
        // 成绩与排名
        // ═══════════════════════════════════════════════════════════════
        private void ResultEvent_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) RefreshResultGrid(); }
        private void ResultStage_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) RefreshResultGrid(); }
        private void ResultGender_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) RefreshResultGrid(); }

        private void RefreshResultGrid() {
            if (ResultEventCombo == null || ResultStageCombo == null || ResultGenderCombo == null || ResultGrid == null) return;
            string gender = ResultGenderCombo.SelectedItem != null ? ((ComboBoxItem)ResultGenderCombo.SelectedItem).Content.ToString() : "全部";
            string eventName = ResultEventCombo.SelectedItem != null ? ResultEventCombo.SelectedItem.ToString() : "";
            string stage = ResultStageCombo.SelectedItem != null ? ((ComboBoxItem)ResultStageCombo.SelectedItem).Content.ToString() : "全部";

            if (string.IsNullOrEmpty(eventName)) {
                ResultGrid.ItemsSource = null;
                return;
            }

            var results = _swimmers.Where(s => s.EventName == eventName).ToList();
            if (gender != "全部") results = results.Where(s => s.Gender == gender).ToList();
            if (stage != "全部") results = results.Where(s => s.CurrentStage == stage).ToList();

            var displayData = results.Select(s => {
                var r = s.Results.FirstOrDefault(lr => stage == "全部" || lr.Stage == stage);
                return new {
                    Rank = s.CurrentRank > 0 ? s.CurrentRank.ToString() : "",
                    Lane = s.Lane,
                    BibNumber = s.BibNumber ?? "",
                    Name = s.Name ?? "",
                    Country = s.Country ?? "",
                    EntryTime = s.EntryTime ?? "",
                    FinalTime = r != null ? TimeFormatter.Format(r.FinalTime) : "",
                    TimingSource = r != null ? r.TimingSource : "",
                    ReactionTime = "",
                    Status = s.Status ?? "",
                    RecordNote = ""
                };
            }).OrderBy(x => {
                int rank;
                return int.TryParse(x.Rank, out rank) ? rank : int.MaxValue;
            }).ToList();

            ResultGrid.ItemsSource = displayData;
        }

        private void Promotion_Click(object sender, RoutedEventArgs e) {
            var win = new PromotionQueryWindow(_swimmers, _events, _poolConfig);
            win.Owner = this;
            win.ShowDialog();
            AutoSaveData();
            Broadcast();
        }

        private void CalcTeamScore_Click(object sender, RoutedEventArgs e) {
            CalculateTeamScores();
            var win = new TeamScoreWindow(_teamScores);
            win.Owner = this;
            win.ShowDialog();
        }

        // ═══════════════════════════════════════════════════════════════
        // 团体计分
        // ═══════════════════════════════════════════════════════════════
        private static readonly double[] IndividualPoints = { 12, 10, 8, 7, 6, 5, 4, 3 };
        private static readonly double[] RelayPointsMultiplied = { 24, 20, 16, 14, 12, 10, 8, 6 };

        private void CalculateTeamScores() {
            var teamDict = new Dictionary<string, TeamScore>();

            // 统计所有决赛成绩
            var finalSwimmers = _swimmers.Where(s => s.CurrentStage == "决赛" && s.CurrentRank > 0 && s.CurrentRank <= 8).ToList();

            foreach (var sw in finalSwimmers) {
                if (string.IsNullOrEmpty(sw.Country)) continue;
                if (!teamDict.ContainsKey(sw.Country)) {
                    teamDict[sw.Country] = new TeamScore { TeamName = sw.Country };
                }
                var ts = teamDict[sw.Country];

                int idx = sw.CurrentRank - 1;
                bool isRelay = sw.EventName.Contains("接力");
                double points = 0;

                if (idx >= 0 && idx < 8) {
                    points = isRelay ? RelayPointsMultiplied[idx] : IndividualPoints[idx];
                }

                // 年龄组系数
                if (sw.AgeCategory == "青少年" || sw.AgeCategory == "少年") {
                    points = Math.Round(points * 0.8);
                } else if (sw.AgeCategory == "大师") {
                    points = Math.Round(points * 0.7);
                }

                if (isRelay) ts.RelayPoints += points;
                else ts.IndividualPoints += points;

                // 奖牌
                if (sw.CurrentRank == 1) ts.GoldCount++;
                else if (sw.CurrentRank == 2) ts.SilverCount++;
                else if (sw.CurrentRank == 3) ts.BronzeCount++;
            }

            // 破纪录加分
            foreach (var record in _records) {
                // TODO: 检查是否有新纪录打破
            }

            // 计算总分和排名
            foreach (var ts in teamDict.Values) {
                ts.TotalPoints = ts.IndividualPoints + ts.RelayPoints + ts.RecordBonusPoints;
            }

            var sorted = teamDict.Values.OrderByDescending(t => t.TotalPoints)
                .ThenByDescending(t => t.GoldCount)
                .ThenByDescending(t => t.SilverCount)
                .ThenByDescending(t => t.BronzeCount).ToList();

            int rank = 1;
            foreach (var ts in sorted) {
                ts.Rank = rank++;
            }

            _teamScores.Clear();
            foreach (var ts in sorted) _teamScores.Add(ts);

            AutoSaveData();
            AddLog("团体计分已更新");
        }

        // ═══════════════════════════════════════════════════════════════
        // 显示控制按钮
        // ═══════════════════════════════════════════════════════════════
        private void ShowStartList_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_START_LIST"); }
        private void ShowLiveRace_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_LIVE_RACE"); }
        private void ShowHeatResult_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_HEAT_RESULT"); }
        private void ShowEventRanking_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_EVENT_RANKING"); }
        private void ShowTeamStandings_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_TEAM_STANDINGS"); }
        private void ShowAwards_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_AWARDS"); }
        private void ShowRecords_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_RECORDS"); }
        private void ShowWelcome_Click(object sender, RoutedEventArgs e) { BroadcastDisplayMode("SHOW_WELCOME"); }

        // ═══════════════════════════════════════════════════════════════
        // 赛事信息管理
        // ═══════════════════════════════════════════════════════════════
        private void SaveCompetitionInfo_Click(object sender, RoutedEventArgs e) {
            _competitionName = CompNameBox.Text.Trim();
            _competitionMode = CompModeCombo.SelectedIndex == 0 ? "domestic" : "international";

            int poolLength = 50;
            if (PoolLengthCombo.SelectedIndex == 1) poolLength = 25;
            _poolConfig.Length = poolLength;

            int laneCount = 10;
            if (LaneCountCombo.SelectedIndex == 1) laneCount = 8;
            else if (LaneCountCombo.SelectedIndex == 2) laneCount = 6;
            _poolConfig.SetLaneCount(laneCount);

            InitLaneDeviceStates();
            UpdateRaceStateDisplay();
            AutoSaveData();
            AddLog("赛事信息已保存: " + _competitionName);
        }

        private void LoadCompetition_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "JSON文件|*.json",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database"),
                Title = "加载赛事"
            };
            if (dlg.ShowDialog() == true) {
                LoadCompetitionFromFile(dlg.FileName);
            }
        }

        private void NewCompetition_Click(object sender, RoutedEventArgs e) {
            _swimmers.Clear();
            _relayTeams.Clear();
            _records.Clear();
            _teamScores.Clear();
            _schedule.Clear();
            _competitionName = "";
            CompNameBox.Clear();
            ScheduleTree.Items.Clear();
            AddLog("已新建赛事");
        }

        // ═══════════════════════════════════════════════════════════════
        // JSON 持久化
        // ═══════════════════════════════════════════════════════════════
        private void AutoSaveData() {
            if (string.IsNullOrEmpty(_competitionName)) return;
            try {
                string dbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database");
                if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);

                File.WriteAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_competition.txt"),
                    _competitionName, Encoding.UTF8);

                var package = new CompetitionPackage {
                    CompetitionName = _competitionName,
                    CompetitionMode = _competitionMode,
                    StartDate = StartDateBox.Text,
                    EndDate = EndDateBox.Text,
                    Location = LocationBox.Text,
                    PoolLength = _poolConfig.Length,
                    LaneCount = _poolConfig.LaneCount,
                    Organizer = OrganizerBox.Text,
                    Host = HostBox.Text,
                    TechnicalDelegate = TechDelegateBox.Text,
                    Referee = RefereeBox.Text,
                    Starter = StarterBox.Text,
                    ChiefJudge = ChiefJudgeBox.Text,
                    Swimmers = _swimmers.ToList(),
                    RelayTeams = _relayTeams.ToList(),
                    Records = _records.ToList(),
                    TeamScores = _teamScores.ToList(),
                    Schedule = _schedule.ToList(),
                    Events = _events,
                    LaneCloseSettings = _laneCloseSettings
                };

                string json = JsonConvert.SerializeObject(package, Formatting.Indented);
                File.WriteAllText(Path.Combine(dbDir, _competitionName + ".json"), json, Encoding.UTF8);
            } catch (Exception ex) {
                AddLog("自动保存失败: " + ex.Message);
            }
        }

        private void LoadLastCompetition() {
            try {
                string lastFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_competition.txt");
                if (File.Exists(lastFile)) {
                    string name = File.ReadAllText(lastFile, Encoding.UTF8).Trim();
                    string jsonFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", name + ".json");
                    if (File.Exists(jsonFile)) {
                        LoadCompetitionFromFile(jsonFile);
                        return;
                    }
                }
            } catch { }
        }

        private void LoadCompetitionFromFile(string path) {
            try {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var package = JsonConvert.DeserializeObject<CompetitionPackage>(json);
                if (package == null) return;

                _competitionName = package.CompetitionName ?? "";
                _competitionMode = package.CompetitionMode ?? "domestic";
                CompNameBox.Text = _competitionName;
                CompModeCombo.SelectedIndex = _competitionMode == "domestic" ? 0 : 1;
                StartDateBox.Text = package.StartDate ?? "";
                EndDateBox.Text = package.EndDate ?? "";
                LocationBox.Text = package.Location ?? "";
                OrganizerBox.Text = package.Organizer ?? "";
                HostBox.Text = package.Host ?? "";
                TechDelegateBox.Text = package.TechnicalDelegate ?? "";
                RefereeBox.Text = package.Referee ?? "";
                StarterBox.Text = package.Starter ?? "";
                ChiefJudgeBox.Text = package.ChiefJudge ?? "";

                _poolConfig.Length = package.PoolLength > 0 ? package.PoolLength : 50;
                _poolConfig.SetLaneCount(package.LaneCount > 0 ? package.LaneCount : 10);
                PoolLengthCombo.SelectedIndex = _poolConfig.Length == 25 ? 1 : 0;
                LaneCountCombo.SelectedIndex = _poolConfig.LaneCount == 8 ? 1 : (_poolConfig.LaneCount == 6 ? 2 : 0);

                if (package.LaneCloseSettings != null) _laneCloseSettings = package.LaneCloseSettings;
                if (package.Events != null && package.Events.Count > 0) _events = package.Events;

                _swimmers.Clear();
                if (package.Swimmers != null) foreach (var sw in package.Swimmers) _swimmers.Add(sw);
                _relayTeams.Clear();
                if (package.RelayTeams != null) foreach (var rt in package.RelayTeams) _relayTeams.Add(rt);
                _records.Clear();
                if (package.Records != null) foreach (var r in package.Records) _records.Add(r);
                _teamScores.Clear();
                if (package.TeamScores != null) foreach (var ts in package.TeamScores) _teamScores.Add(ts);
                _schedule.Clear();
                if (package.Schedule != null) foreach (var s in package.Schedule) _schedule.Add(s);

                InitLaneDeviceStates();
                BuildScheduleTree();
                UpdateRaceStateDisplay();

                AddLog("已加载赛事: " + _competitionName);
            } catch (Exception ex) {
                AddLog("加载赛事失败: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 计时硬件连接
        // ═══════════════════════════════════════════════════════════════
        private void PopulateComPorts() {
            try {
                ComPortCombo.Items.Clear();
                foreach (string port in SerialPort.GetPortNames()) {
                    ComPortCombo.Items.Add(port);
                }
                if (ComPortCombo.Items.Count > 0) ComPortCombo.SelectedIndex = 0;
            } catch { }
        }

        private void ConnectSerial_Click(object sender, RoutedEventArgs e) {
            if (ComPortCombo.SelectedItem == null) { AddLog("请选择串口"); return; }
            _timingBridge.ConnectSerial(ComPortCombo.SelectedItem.ToString());
            UpdateConnectionStatus();
        }

        private void ConnectTcp_Click(object sender, RoutedEventArgs e) {
            string addr = TcpHostBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 5000;
            if (parts.Length > 1) int.TryParse(parts[1], out port);
            _timingBridge.ConnectTcp(host, port);
            UpdateConnectionStatus();
        }

        private void DisconnectTiming_Click(object sender, RoutedEventArgs e) {
            _timingBridge.Disconnect();
            UpdateConnectionStatus();
        }

        // ═══════════════════════════════════════════════════════════════
        // 纪录管理
        // ═══════════════════════════════════════════════════════════════
        private void AddRecord_Click(object sender, RoutedEventArgs e) {
            _records.Add(new SwimmingRecord { RecordType = "赛会纪录", Gender = "男" });
        }

        private void DeleteRecord_Click(object sender, RoutedEventArgs e) {
            var selected = RecordGrid.SelectedItem as SwimmingRecord;
            if (selected != null) {
                _records.Remove(selected);
                AutoSaveData();
            }
        }

        private void ImportDefaultRecords_Click(object sender, RoutedEventArgs e) {
            // 预置世界纪录（长池50米，截至2024年数据）
            var defaults = new[] {
                // 男子
                new { G="男", E="50米自由泳", T=20.91, H="Cielo", C="巴西", D="2009-12-18" },
                new { G="男", E="100米自由泳", T=46.86, H="Pan Zhanle", C="中国", D="2024-07-31" },
                new { G="男", E="200米自由泳", T=102.00, H="Milak", C="匈牙利", D="2024-02-11" },
                new { G="男", E="400米自由泳", T=220.07, H="Thorpe", C="澳大利亚", D="2002-07-30" },
                new { G="男", E="800米自由泳", T=452.12, H="Zhang Lin", C="中国", D="2009-07-29" },
                new { G="男", E="1500米自由泳", T=871.02, H="Sun Yang", C="中国", D="2012-08-04" },
                new { G="男", E="100米仰泳", T=51.60, H="Xu Jiayu", C="中国", D="2024-07-29" },
                new { G="男", E="200米仰泳", T=111.92, H="Peirsol", C="美国", D="2009-07-31" },
                new { G="男", E="100米蛙泳", T=56.88, H="Peaty", C="英国", D="2019-07-21" },
                new { G="男", E="200米蛙泳", T=125.95, H="Stubblety-Cook", C="澳大利亚", D="2022-06-19" },
                new { G="男", E="100米蝶泳", T=49.45, H="Caeleb Dressel", C="美国", D="2021-07-31" },
                new { G="男", E="200米蝶泳", T=110.34, H="Milak", C="匈牙利", D="2022-06-14" },
                new { G="男", E="200米个人混合泳", T=114.00, H="Lochte", C="美国", D="2011-07-28" },
                new { G="男", E="400米个人混合泳", T=243.84, H="Phelps", C="美国", D="2008-08-10" },
                // 女子
                new { G="女", E="50米自由泳", T=23.61, H="Sjoestroem", C="瑞典", D="2024-07-28" },
                new { G="女", E="100米自由泳", T=51.71, H="Sjoestroem", C="瑞典", D="2017-07-23" },
                new { G="女", E="200米自由泳", T=112.98, H="Titmus", C="澳大利亚", D="2023-02-12" },
                new { G="女", E="400米自由泳", T=235.82, H="Titmus", C="澳大利亚", D="2023-06-07" },
                new { G="女", E="800米自由泳", T=494.07, H="Ledecky", C="美国", D="2016-08-12" },
                new { G="女", E="1500米自由泳", T=920.48, H="Ledecky", C="美国", D="2018-05-20" },
                new { G="女", E="100米仰泳", T=57.33, H="Kaylee McKeown", C="澳大利亚", D="2023-06-18" },
                new { G="女", E="200米仰泳", T=123.35, H="McKeown", C="澳大利亚", D="2024-07-30" },
                new { G="女", E="100米蛙泳", T=64.13, H="Lilly King", C="美国", D="2017-07-25" },
                new { G="女", E="200米蛙泳", T=138.95, H="Tatjana Schoenmaker", C="南非", D="2021-07-30" },
                new { G="女", E="100米蝶泳", T=55.18, H="Gretchen Walsh", C="美国", D="2024-12-19" },
                new { G="女", E="200米蝶泳", T=121.81, H="Liu Zige", C="中国", D="2009-10-21" },
                new { G="女", E="200米个人混合泳", T=124.06, H="Katinka Hosszu", C="匈牙利", D="2015-08-03" },
                new { G="女", E="400米个人混合泳", T=266.36, H="Ye Shiwen", C="中国", D="2012-07-28" }
            };

            int added = 0;
            foreach (var d in defaults) {
                // 检查是否已存在
                bool exists = false;
                foreach (var r in _records) {
                    if (r.Gender == d.G && r.EventName == d.E && r.RecordType == "世界纪录") { exists = true; break; }
                }
                if (!exists) {
                    _records.Add(new SwimmingRecord {
                        Gender = d.G, EventName = d.E, RecordType = "世界纪录",
                        HolderName = d.H, HolderCountry = d.C,
                        Time = d.T, TimeInSeconds = d.T, Date = d.D
                    });
                    added++;
                }
            }
            AddLog(string.Format("已导入{0}条世界纪录", added));
            AutoSaveData();
            Broadcast();
        }

        private void ImportRecordsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|所有文件|*.*",
                Title = "导入纪录CSV（格式：性别,项目,类型,保持者,代表队,成绩秒数,日期,地点）"
            };
            if (dlg.ShowDialog() == true) {
                try {
                    string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                    int imported = 0;
                    for (int i = 1; i < lines.Length; i++) {
                        string[] cols = lines[i].Split(',');
                        if (cols.Length < 6) continue;
                        var rec = new SwimmingRecord {
                            Gender = cols[0].Trim(),
                            EventName = cols[1].Trim(),
                            RecordType = cols[2].Trim(),
                            HolderName = cols[3].Trim(),
                            HolderCountry = cols[4].Trim()
                        };
                        double t;
                        if (double.TryParse(cols[5].Trim(), out t)) { rec.Time = t; rec.TimeInSeconds = t; }
                        else { rec.Time = TimeFormatter.Parse(cols[5].Trim()); rec.TimeInSeconds = rec.Time; }
                        if (cols.Length > 6) rec.Date = cols[6].Trim();
                        if (cols.Length > 7) rec.Location = cols[7].Trim();
                        _records.Add(rec);
                        imported++;
                    }
                    AddLog(string.Format("CSV导入{0}条纪录", imported));
                    AutoSaveData();
                    Broadcast();
                } catch (Exception ex) {
                    AddLog("CSV导入纪录失败: " + ex.Message);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 文档打印
        // ═══════════════════════════════════════════════════════════════
        private void PrintSchedule_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("竞赛日程", BuildScheduleHtml()); }
        private void PrintManual_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("秩序册", BuildManualHtml()); }
        private void PrintStartList_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("出发表", BuildStartListHtml()); }
        private void PrintHeatResults_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("分组成绩", BuildHeatResultsHtml()); }
        private void PrintEventResults_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("项目成绩", BuildEventResultsHtml()); }
        private void PrintFullResultBook_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("成绩册", BuildFullResultBookHtml()); }
        private void PrintTeamStandings_Click(object sender, RoutedEventArgs e) { CalculateTeamScores(); GenerateAndOpenDocument("团体成绩", BuildTeamStandingsHtml()); }
        private void PrintRecordReport_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("纪录报告", BuildRecordReportHtml()); }
        private void PrintAwardCertificate_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("奖状", BuildAwardCertificateHtml()); }
        private void PrintRecordCertificate_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("纪录证书", BuildRecordCertificateHtml()); }
        private void PrintSplitTimeReport_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("分段计时报告", BuildSplitTimeReportHtml()); }

        private void GenerateAndOpenDocument(string title, string html) {
            try {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Documents");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string filePath = Path.Combine(dir, title + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
                File.WriteAllText(filePath, html, Encoding.UTF8);
                Process.Start(filePath);
                AddLog("已生成文档: " + title);
            } catch (Exception ex) {
                AddLog("文档生成失败: " + ex.Message);
            }
        }

        private string WrapHtml(string title, string body) {
            return string.Format(@"<!DOCTYPE html>
<html><head><meta charset='utf-8'><title>{0}</title>
<style>
body {{ font-family: 'Microsoft YaHei', sans-serif; margin: 20px; }}
h1 {{ text-align: center; }}
h2 {{ border-bottom: 2px solid #333; padding-bottom: 5px; }}
table {{ border-collapse: collapse; width: 100%; margin: 10px 0; }}
th, td {{ border: 1px solid #ccc; padding: 6px 10px; text-align: center; }}
th {{ background: #f0f0f0; }}
tr:nth-child(even) {{ background: #fafafa; }}
.header {{ text-align: center; margin-bottom: 20px; }}
.footer {{ text-align: center; margin-top: 30px; font-size: 12px; color: #888; }}
@media print {{ body {{ margin: 0; }} }}
</style></head><body>
<div class='header'><h1>{0}</h1><p>{1}</p></div>
{2}
<div class='footer'>游泳赛事管理系统 — 自动生成</div>
</body></html>", title, _competitionName, body);
        }

        private string BuildScheduleHtml() {
            var sb = new StringBuilder();
            sb.Append("<table><tr><th>单元</th><th>日期</th><th>时间</th><th>项目</th><th>阶段</th><th>组数</th></tr>");
            foreach (var s in _schedule) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}{4}</td><td>{5}</td><td>{6}</td></tr>",
                    s.SessionNumber, s.Date, s.Time, s.Gender + "子", s.EventName, s.Stage, s.HeatCount);
            }
            sb.Append("</table>");
            return WrapHtml("竞赛日程", sb.ToString());
        }

        private string BuildManualHtml() {
            var sb = new StringBuilder();
            // 赛事信息
            sb.AppendFormat("<h2>赛事信息</h2><p>地点: {0}<br/>日期: {1} - {2}<br/>泳池: {3}米 {4}道<br/>主办: {5}<br/>承办: {6}</p>",
                LocationBox.Text, StartDateBox.Text, EndDateBox.Text,
                _poolConfig.Length, _poolConfig.LaneCount, OrganizerBox.Text, HostBox.Text);

            // 运动员列表
            var grouped = _swimmers.GroupBy(s => s.EventName);
            foreach (var g in grouped) {
                sb.AppendFormat("<h2>{0}</h2>", g.Key);
                sb.Append("<table><tr><th>号码</th><th>姓名</th><th>性别</th><th>代表队</th><th>报名成绩</th><th>组</th><th>道</th></tr>");
                foreach (var sw in g.OrderBy(s => s.Heat).ThenBy(s => s.Lane)) {
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td></tr>",
                        sw.BibNumber, sw.Name, sw.Gender, sw.Country, sw.EntryTime, sw.Heat, sw.Lane);
                }
                sb.Append("</table>");
            }
            return WrapHtml("秩序册", sb.ToString());
        }

        private string BuildStartListHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<h2>{0}子 {1} {2} 第{3}组</h2>", _currentGender, _currentEvent, _currentStage, _currentHeat);
            sb.Append("<table><tr><th>道</th><th>号码</th><th>姓名</th><th>代表队</th></tr>");
            foreach (var sw in GetCurrentHeatSwimmers()) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td></tr>",
                    sw.Lane, sw.BibNumber, sw.Name, sw.Country);
            }
            sb.Append("</table>");
            return WrapHtml("出发表", sb.ToString());
        }

        private string BuildHeatResultsHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<h2>{0}子 {1} {2} 第{3}组 成绩</h2>", _currentGender, _currentEvent, _currentStage, _currentHeat);
            sb.Append("<table><tr><th>名次</th><th>道</th><th>号码</th><th>姓名</th><th>代表队</th><th>成绩</th><th>计时源</th></tr>");
            var swimmers = GetCurrentHeatSwimmers().OrderBy(s => s.CurrentRank > 0 ? s.CurrentRank : int.MaxValue).ToList();
            foreach (var sw in swimmers) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td></tr>",
                    r != null && r.Rank > 0 ? r.Rank.ToString() : "-",
                    sw.Lane, sw.BibNumber, sw.Name, sw.Country,
                    r != null ? r.FinalTimeDisplay : "", r != null ? r.TimingSource : "");
            }
            sb.Append("</table>");
            return WrapHtml("分组成绩", sb.ToString());
        }

        private string BuildEventResultsHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<h2>{0}子 {1} {2} 总排名</h2>", _currentGender, _currentEvent, _currentStage);
            sb.Append("<table><tr><th>名次</th><th>号码</th><th>姓名</th><th>代表队</th><th>报名成绩</th><th>最终成绩</th></tr>");
            var ranking = GetEventRanking(_currentEvent, _currentGender);
            foreach (var item in ranking) {
                var dict = item as dynamic;
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                    ((dynamic)item).rank, ((dynamic)item).bibNumber, ((dynamic)item).name,
                    ((dynamic)item).country, ((dynamic)item).entryTime, ((dynamic)item).finalTime);
            }
            sb.Append("</table>");
            return WrapHtml("项目成绩", sb.ToString());
        }

        private string BuildFullResultBookHtml() {
            var sb = new StringBuilder();
            var grouped = _swimmers.GroupBy(s => s.EventName);
            foreach (var g in grouped) {
                sb.AppendFormat("<h2>{0}</h2>", g.Key);
                sb.Append("<table><tr><th>名次</th><th>号码</th><th>姓名</th><th>代表队</th><th>成绩</th><th>阶段</th></tr>");
                var sorted = g.OrderBy(s => {
                    var r = s.Results.LastOrDefault();
                    return r != null && r.FinalTime > 0 ? r.FinalTime : double.MaxValue;
                }).ToList();
                int rank = 1;
                foreach (var sw in sorted) {
                    var r = sw.Results.LastOrDefault();
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                        rank++, sw.BibNumber, sw.Name, sw.Country,
                        r != null ? TimeFormatter.Format(r.FinalTime) : "-", sw.CurrentStage);
                }
                sb.Append("</table>");
            }
            return WrapHtml("成绩册", sb.ToString());
        }

        private string BuildTeamStandingsHtml() {
            var sb = new StringBuilder();
            sb.Append("<table><tr><th>名次</th><th>代表队</th><th>总分</th><th>个人分</th><th>接力分</th><th>破纪录加分</th><th>金</th><th>银</th><th>铜</th></tr>");
            foreach (var ts in _teamScores.OrderBy(t => t.Rank)) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td><td>{7}</td><td>{8}</td></tr>",
                    ts.Rank, ts.TeamName, ts.TotalPoints, ts.IndividualPoints, ts.RelayPoints,
                    ts.RecordBonusPoints, ts.GoldCount, ts.SilverCount, ts.BronzeCount);
            }
            sb.Append("</table>");
            return WrapHtml("团体成绩", sb.ToString());
        }

        private string BuildRecordReportHtml() {
            var sb = new StringBuilder();
            sb.Append("<table><tr><th>项目</th><th>类型</th><th>保持者</th><th>代表队</th><th>成绩</th><th>日期</th><th>地点</th></tr>");
            foreach (var r in _records) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td></tr>",
                    r.EventName, r.RecordType, r.HolderName, r.HolderCountry, TimeFormatter.Format(r.Time), r.Date, r.Location);
            }
            sb.Append("</table>");
            return WrapHtml("纪录报告", sb.ToString());
        }

        private string BuildAwardCertificateHtml() {
            return WrapHtml("奖状", "<p style='text-align:center;font-size:24px;margin-top:100px;'>奖状内容将根据颁奖信息生成</p>");
        }

        private string BuildRecordCertificateHtml() {
            return WrapHtml("纪录证书", "<p style='text-align:center;font-size:24px;margin-top:100px;'>纪录证书内容将根据破纪录信息生成</p>");
        }

        private string BuildSplitTimeReportHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<h2>{0}子 {1} {2} 分段计时</h2>", _currentGender, _currentEvent, _currentStage);
            foreach (var sw in GetCurrentHeatSwimmers()) {
                var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                if (result == null || result.Splits.Count == 0) continue;
                sb.AppendFormat("<h3>泳道{0} {1} ({2})</h3>", sw.Lane, sw.Name, sw.Country);
                sb.Append("<table><tr><th>段</th><th>距离</th><th>分段时间</th><th>累计时间</th></tr>");
                foreach (var split in result.Splits) {
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}m</td><td>{2}</td><td>{3}</td></tr>",
                        split.Lap, split.Distance, TimeFormatter.Format(split.Time), TimeFormatter.Format(split.CumulativeTime));
                }
                sb.Append("</table>");
            }
            return WrapHtml("分段计时报告", sb.ToString());
        }

        // ═══════════════════════════════════════════════════════════════
        // 工具方法
        // ═══════════════════════════════════════════════════════════════
        private void AddLog(string msg) {
            if (LogListBox == null) return;
            string entry = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), msg);
            LogListBox.Items.Add(entry);
            if (LogListBox.Items.Count > 500) LogListBox.Items.RemoveAt(0);
            LogListBox.ScrollIntoView(entry);
            // 同步到系统日志页
            if (SystemLogListBox != null) {
                SystemLogListBox.Items.Insert(0, entry);
                if (SystemLogListBox.Items.Count > 1000) SystemLogListBox.Items.RemoveAt(SystemLogListBox.Items.Count - 1);
            }
        }

        private string GetLocalIP() {
            try {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0)) {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint.Address.ToString();
                }
            } catch {
                return "127.0.0.1";
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e) {
            AutoSaveData();
            _raceTimer.Stop();
            _countdownTimer.Stop();
            if (_timingBridge != null) _timingBridge.Dispose();
            if (_server != null) _server.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════
        // 系统日志与数据
        // ═══════════════════════════════════════════════════════════════
        private void RefreshBackupList() {
            _savedCompetitions.Clear();
            string dbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            foreach (var f in Directory.GetFiles(dbDir, "*.json")) {
                var fi = new FileInfo(f);
                _savedCompetitions.Add(new BackupInfo {
                    Name = Path.GetFileNameWithoutExtension(fi.Name),
                    FilePath = fi.FullName,
                    LastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
            if (BackupListBox != null) BackupListBox.ItemsSource = _savedCompetitions;
        }

        private void SyncSystemLog() {
            // 将主日志同步到系统日志页
            if (SystemLogListBox != null && LogListBox != null) {
                SystemLogListBox.ItemsSource = LogListBox.Items;
            }
        }

        private void LoadBackup_Click(object sender, RoutedEventArgs e) {
            var selected = BackupListBox.SelectedItem as BackupInfo;
            if (selected == null) { MessageBox.Show("请先选中一个存档"); return; }
            if (MessageBox.Show(
                string.Format("确定要加载存档 [{0}] 吗？\n当前未保存的数据将被覆盖。", selected.Name),
                "确认加载", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) {
                _competitionName = selected.Name;
                CompNameBox.Text = selected.Name;
                LoadCompetitionFromFile(selected.FilePath);
                AddLog(string.Format("已加载存档: {0}", selected.Name));
            }
        }

        private void DeleteBackup_Click(object sender, RoutedEventArgs e) {
            var selected = BackupListBox.SelectedItem as BackupInfo;
            if (selected == null) { MessageBox.Show("请先选中一个存档"); return; }
            if (MessageBox.Show(
                string.Format("严重警告：确定要永久删除存档文件 [{0}.json] 吗？\n\n此操作不可恢复！", selected.Name),
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Stop) == MessageBoxResult.Yes) {
                try {
                    if (File.Exists(selected.FilePath)) {
                        File.Delete(selected.FilePath);
                        AddLog("已删除存档文件: " + selected.Name);
                        RefreshBackupList();
                    }
                } catch (Exception ex) {
                    MessageBox.Show("删除失败: " + ex.Message);
                }
            }
        }

        private void ClearDatabase_Click(object sender, RoutedEventArgs e) {
            if (MessageBox.Show(
                string.Format("严重警告：确定要清空当前比赛 [{0}] 的所有数据吗？\n\n运动员、赛程、成绩将全部清除！\n不影响其他存档文件。", _competitionName),
                "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) {
                _swimmers.Clear();
                _relayTeams.Clear();
                _teamScores.Clear();
                _schedule.Clear();
                ScheduleTree.Items.Clear();
                AutoSaveData();
                RefreshBackupList();
                Broadcast();
                AddLog(string.Format("比赛 [{0}] 的数据已清空", _competitionName));
                MessageBox.Show(string.Format("比赛 [{0}] 的数据已成功清空。", _competitionName));
            }
        }

        private void ForceSave_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_competitionName)) {
                MessageBox.Show("请先设置赛事名称");
                return;
            }
            if (MessageBox.Show("确定要立即执行强制保存吗？\n系统将根据当前项目名称覆盖现有存档文件。",
                "确认保存", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                AutoSaveData();
                RefreshBackupList();
                AddLog("强制保存完成: " + _competitionName);
                MessageBox.Show("数据强制保存完成！");
            }
        }

        private void ShowLocalIP_Click(object sender, RoutedEventArgs e) {
            string ip = GetLocalIP();
            string info = string.Format("本机IP地址: {0}\n\nWebSocket服务: ws://{0}:3002\nWeb页面: http://{0}:3002\n\n请将此地址告知各客户端连接。", ip);
            MessageBox.Show(info, "本机IP地址", MessageBoxButton.OK, MessageBoxImage.Information);
            AddLog("查询IP: " + ip);
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e) {
            var win = new ChangePasswordWindow();
            win.Owner = this;
            win.ShowDialog();
            AddLog("打开修改密码窗口");
        }
    }

    public class BackupInfo
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string LastModified { get; set; }
    }
}
