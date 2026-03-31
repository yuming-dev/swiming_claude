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
            UpdateEditHeatCombo();
            AddLog("系统启动完成");
        }

        private string GetDatePickerText(DatePicker dp) {
            if (dp == null || dp.SelectedDate == null) return "";
            return dp.SelectedDate.Value.ToString("yyyy-MM-dd");
        }

        private void SetDatePicker(DatePicker dp, string dateStr) {
            if (dp == null) return;
            DateTime dt;
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out dt))
                dp.SelectedDate = dt;
            else
                dp.SelectedDate = null;
        }

        private void InitializeData() {
            SwimmerGrid.ItemsSource = _swimmers;
            RelayGrid.ItemsSource = _relayTeams;
            // ScheduleGrid replaced by ScheduleGroupedPanel
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
            var dup = FindDuplicate(swimmer.Name, swimmer.Gender, swimmer.EventName, swimmer.BibNumber, swimmer.IDNumber, swimmer.Country);
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

                string idNum = swimmerData["idNumber"] != null ? swimmerData["idNumber"].ToString() : "";
                string country = swimmerData["country"] != null ? swimmerData["country"].ToString() : "";
                var dup = FindDuplicate(name, gender, eventName, bibNumber, idNum, country);
                if (dup != null && !isResubmit) continue;

                var swimmer = new Swimmer {
                    BibNumber = bibNumber,
                    Name = name,
                    Gender = gender,
                    Age = swimmerData["age"] != null ? (int)swimmerData["age"] : 0,
                    Country = country,
                    IDNumber = idNum,
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

            // 在_swimmers中创建代表该接力队的条目，统一走日程/分组/成绩流程
            string legNames = "";
            foreach (var leg in team.Legs) legNames += (legNames.Length > 0 ? "," : "") + leg.SwimmerName;
            string bibNumber = "R" + (_relayTeams.Count).ToString("D3");
            // 检查重复
            var dup = FindDuplicate(team.TeamName, team.Gender, team.EventName, bibNumber, "", team.TeamName);
            if (dup == null) {
                _swimmers.Add(new Swimmer {
                    BibNumber = bibNumber,
                    Name = team.TeamName,
                    Gender = team.Gender,
                    Country = team.TeamName,
                    EventName = team.EventName,
                    EntryTime = team.EntryTime,
                    EntryTimeSeconds = team.EntryTimeSeconds,
                    Notes = string.Format("接力队 棒次:{0}", legNames)
                });
            }

            AddLog(string.Format("注册接力队: {0} ({1}) {2}人 [{3}]", team.TeamName, team.EventName, team.Legs.Count, legNames));
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
                case "EXECUTE_PROMOTION":
                    if (data != null) {
                        string pGender = data["gender"] != null ? data["gender"].ToString() : "";
                        string pEvent = data["eventName"] != null ? data["eventName"].ToString() : "";
                        string pFrom = data["fromStage"] != null ? data["fromStage"].ToString() : "";
                        string pNext = data["nextStage"] != null ? data["nextStage"].ToString() : "";
                        int pCount = data["promoCount"] != null ? (int)data["promoCount"] : 8;
                        ExecutePromotion(pGender, pEvent, pFrom, pNext, pCount);
                    }
                    break;
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
                        rightStartBlock = laneState != null ? laneState.RightStartBlockStatus.ToString().ToLower() : "closed",
                        // 损坏标志
                        leftTouchpadBroken = laneState != null && laneState.LeftTouchpadBroken,
                        leftBlindWatch1Broken = laneState != null && laneState.LeftBlindWatch1Broken,
                        leftBlindWatch2Broken = laneState != null && laneState.LeftBlindWatch2Broken,
                        leftBlindWatch3Broken = laneState != null && laneState.LeftBlindWatch3Broken,
                        leftStartBlockBroken = laneState != null && laneState.LeftStartBlockBroken,
                        rightTouchpadBroken = laneState != null && laneState.RightTouchpadBroken,
                        rightBlindWatch1Broken = laneState != null && laneState.RightBlindWatch1Broken,
                        rightBlindWatch2Broken = laneState != null && laneState.RightBlindWatch2Broken,
                        rightBlindWatch3Broken = laneState != null && laneState.RightBlindWatch3Broken,
                        rightStartBlockBroken = laneState != null && laneState.RightStartBlockBroken
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
                schedule = _schedule.Select(s => {
                    int hc = s.HeatCount > 0 ? s.HeatCount : 1;
                    var heatConfirmed = new List<bool>();
                    for (int hh = 1; hh <= hc; hh++) heatConfirmed.Add(IsHeatConfirmed(s.Gender, s.EventName, s.Stage, hh));
                    return new {
                        session = s.SessionNumber, sessionName = s.SessionName,
                        date = s.Date, time = s.Time,
                        eventName = s.EventName, gender = s.Gender,
                        stage = s.Stage, heatCount = s.HeatCount, isRelay = s.IsRelay,
                        heatConfirmed = heatConfirmed,
                        allConfirmed = heatConfirmed.Count > 0 && heatConfirmed.All(x => x)
                    };
                }).ToList(),
                swimmers = swimmerData,
                laneDevices = _laneDeviceStates.Select(s => new {
                    lane = s.Lane,
                    leftTouchpadBroken = s.LeftTouchpadBroken,
                    leftStartBlockBroken = s.LeftStartBlockBroken,
                    leftBlindWatch1Broken = s.LeftBlindWatch1Broken,
                    leftBlindWatch2Broken = s.LeftBlindWatch2Broken,
                    leftBlindWatch3Broken = s.LeftBlindWatch3Broken,
                    rightTouchpadBroken = s.RightTouchpadBroken,
                    rightStartBlockBroken = s.RightStartBlockBroken,
                    rightBlindWatch1Broken = s.RightBlindWatch1Broken,
                    rightBlindWatch2Broken = s.RightBlindWatch2Broken,
                    rightBlindWatch3Broken = s.RightBlindWatch3Broken
                }).ToList(),
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
                // 保存反应时间到成绩记录
                if (laneState.ReactionTime > 0) {
                    result.StartingBlockTime = laneState.ReactionTime;
                }

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

            // 刷新成绩与排名页面
            UpdateResultHeatCombo();
            RefreshResultGrid();

            // 检查该项目该阶段是否所有组都已完赛（广播给HTML控制端）
            CheckStageComplete();

            // EXE本地也弹晋级确认（当从EXE按钮触发时）
            if (sender != null) {
                CheckStageCompleteLocal();
            }

            Broadcast();
        }

        /// <summary>
        /// EXE本地的赛次完赛晋级确认（仅EXE界面操作时弹出）
        /// </summary>
        private void CheckStageCompleteLocal() {
            if (string.IsNullOrEmpty(_currentEvent) || string.IsNullOrEmpty(_currentStage)) return;
            if (_currentStage == "决赛") return;

            var stageSwimmers = _swimmers.Where(s =>
                s.EventName == _currentEvent && s.Gender == _currentGender && s.CurrentStage == _currentStage
            ).ToList();
            if (stageSwimmers.Count == 0) return;

            bool allDone = true;
            foreach (var sw in stageSwimmers) {
                if (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ") continue;
                var result = sw.GetResultForStage(_currentStage);
                if (result == null || result.FinalTime <= 0) { allDone = false; break; }
            }
            if (!allDone) return;

            string nextStage = null;
            if (_currentStage == "预赛") {
                if (_schedule.Any(s => s.Gender == _currentGender && s.EventName == _currentEvent && s.Stage == "半决赛"))
                    nextStage = "半决赛";
                else
                    nextStage = "决赛";
            } else if (_currentStage == "半决赛") {
                nextStage = "决赛";
            }
            if (nextStage == null) return;

            int promoCount = HeatScheduler.GetPromotionCount(_currentStage, nextStage);
            var answer = MessageBox.Show(
                string.Format("{0} {1} {2} 全部{3}人已完赛！\n\n是否自动晋级前{4}名到{5}？\n（按成绩总排名）",
                    _currentGender, _currentEvent, _currentStage, stageSwimmers.Count, promoCount, nextStage),
                "自动晋级", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes) {
                ExecutePromotion(_currentGender, _currentEvent, _currentStage, nextStage, promoCount);
            }
        }

        private void CheckStageComplete() {
            if (string.IsNullOrEmpty(_currentEvent) || string.IsNullOrEmpty(_currentStage)) return;
            if (_currentStage == "决赛") return;

            var stageSwimmers = _swimmers.Where(s =>
                s.EventName == _currentEvent &&
                s.Gender == _currentGender &&
                s.CurrentStage == _currentStage
            ).ToList();

            if (stageSwimmers.Count == 0) return;

            bool allDone = true;
            foreach (var sw in stageSwimmers) {
                if (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ") continue;
                var result = sw.GetResultForStage(_currentStage);
                if (result == null || result.FinalTime <= 0) { allDone = false; break; }
            }

            if (allDone) {
                string nextStage = null;
                if (_currentStage == "预赛") {
                    if (_schedule.Any(s => s.Gender == _currentGender && s.EventName == _currentEvent && s.Stage == "半决赛"))
                        nextStage = "半决赛";
                    else
                        nextStage = "决赛";
                } else if (_currentStage == "半决赛") {
                    nextStage = "决赛";
                }

                if (nextStage != null) {
                    int promoCount = HeatScheduler.GetPromotionCount(_currentStage, nextStage);
                    AddLog(string.Format("★ {0}{1} {2}全部完赛！可晋级{3}人到{4}", _currentGender, _currentEvent, _currentStage, promoCount, nextStage));

                    // 向所有控制端广播赛次完成通知（控制端弹出晋级确认）
                    var stageCompleteData = new {
                        type = "STAGE_COMPLETE",
                        gender = _currentGender,
                        eventName = _currentEvent,
                        fromStage = _currentStage,
                        nextStage = nextStage,
                        totalSwimmers = stageSwimmers.Count,
                        promoCount = promoCount
                    };
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(stageCompleteData);
                    foreach (var conn in _allSockets) {
                        try { conn.Send(json); } catch { }
                    }
                }
            } else {
                AddLog("请选择下一组比赛");
            }
        }

        /// <summary>
        /// 执行晋级处理（由控制端确认后调用）
        /// </summary>
        private void ExecutePromotion(string gender, string eventName, string fromStage, string nextStage, int promoCount) {
            var filtered = _swimmers.Where(s => s.Gender == gender && s.EventName == eventName).ToList();
            var promoted = HeatScheduler.GetPromotedSwimmers(filtered, eventName, fromStage, promoCount);

            if (promoted.Count > 0) {
                var assignments = HeatScheduler.GenerateHeatsFromResults(promoted, _poolConfig, eventName, nextStage, fromStage);
                int heatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;

                bool scheduleExists = _schedule.Any(s => s.Gender == gender && s.EventName == eventName && s.Stage == nextStage);
                if (!scheduleExists) {
                    _schedule.Add(new ScheduleItem {
                        SessionNumber = _schedule.Count > 0 ? _schedule.Max(s => s.SessionNumber) + 1 : 1,
                        Gender = gender,
                        EventName = eventName,
                        Stage = nextStage,
                        HeatCount = heatCount
                    });
                } else {
                    var schedItem = _schedule.FirstOrDefault(s => s.Gender == gender && s.EventName == eventName && s.Stage == nextStage);
                    if (schedItem != null) schedItem.HeatCount = heatCount;
                }

                BuildScheduleTree();
                UpdateResultHeatCombo();
                RefreshResultGrid();
                UpdateEditHeatCombo();
                AutoSaveData();
                AddLog(string.Format("已自动晋级{0}人到{1}，分为{2}组", promoted.Count, nextStage, heatCount));

                // 通知所有客户端晋级完成
                var resultData = new {
                    type = "PROMOTION_DONE",
                    gender = gender,
                    eventName = eventName,
                    nextStage = nextStage,
                    promotedCount = promoted.Count,
                    heatCount = heatCount
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(resultData);
                foreach (var conn in _allSockets) {
                    try { conn.Send(json); } catch { }
                }
                Broadcast();
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
            // 已完赛的组：灰色可见但不能选择开始比赛
            if (tag.StartsWith("done:")) {
                AddLog("该组比赛已完赛，不能重新选择");
                return;
            }
            // 格式: "heat:性别|项目|阶段|组次"
            if (tag.StartsWith("heat:")) {
                string[] parts = tag.Substring(5).Split('|');
                if (parts.Length >= 4) {
                    _currentGender = parts[0];
                    _currentEvent = parts[1];
                    _currentStage = parts[2];
                    _isRelay = _currentEvent.Contains("接力");

                    int heat;
                    if (int.TryParse(parts[3], out heat)) {
                        _totalHeats = CountHeatsForEvent(_currentGender, _currentEvent, _currentStage);
                        CurrentEventText.Text = _currentGender + _currentEvent;
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
                    string header = string.Format("{0} {1} {2}", ev.Gender, ev.EventName, ev.Stage);
                    bool allHeatsConfirmed = IsStageAllConfirmed(ev.Gender, ev.EventName, ev.Stage);

                    var eventItem = new TreeViewItem {
                        Tag = string.Format("event:{0}|{1}|{2}", ev.Gender, ev.EventName, ev.Stage),
                        Header = allHeatsConfirmed ? header + " [已完赛]" : header,
                        Foreground = allHeatsConfirmed ? new SolidColorBrush(Colors.Gray) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                        IsExpanded = !allHeatsConfirmed
                    };

                    int heatCount = ev.HeatCount > 0 ? ev.HeatCount : 1;
                    for (int h = 1; h <= heatCount; h++) {
                        bool heatConfirmed = IsHeatConfirmed(ev.Gender, ev.EventName, ev.Stage, h);
                        var heatItem = new TreeViewItem {
                            Tag = string.Format("{0}:{1}|{2}|{3}|{4}", heatConfirmed ? "done" : "heat", ev.Gender, ev.EventName, ev.Stage, h),
                            Header = heatConfirmed ? string.Format("第{0}组 [已完赛]", h) : string.Format("第{0}组 (共{1}组)", h, heatCount),
                            Foreground = heatConfirmed ? new SolidColorBrush(Colors.Gray) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
                        };
                        eventItem.Items.Add(heatItem);
                    }
                    sessionItem.Items.Add(eventItem);
                }
                ScheduleTree.Items.Add(sessionItem);
            }
            RebuildScheduleGroupedView();
        }

        /// <summary>
        /// 检查某组比赛是否已有成绩（所有运动员都有成绩或标记为DNS/DNF/DSQ）
        /// </summary>
        private bool IsHeatConfirmed(string gender, string eventName, string stage, int heat) {
            var heatSwimmers = _swimmers.Where(s =>
                s.Gender == gender && s.EventName == eventName
            ).ToList();

            // 从StageAssignments或当前赛次获取该组运动员
            var inHeat = new List<Swimmer>();
            foreach (var s in heatSwimmers) {
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat == heat) { inHeat.Add(s); continue; }
                if (s.CurrentStage == stage && s.Heat == heat) inHeat.Add(s);
            }

            if (inHeat.Count == 0) return false;

            foreach (var sw in inHeat) {
                if (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ") continue;
                var r = sw.GetResultForStage(stage);
                if (r == null || r.FinalTime <= 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 检查某项目某赛次是否全部组都已完赛
        /// </summary>
        private bool IsStageAllConfirmed(string gender, string eventName, string stage) {
            var schedItem = _schedule.FirstOrDefault(s => s.Gender == gender && s.EventName == eventName && s.Stage == stage);
            int heatCount = schedItem != null && schedItem.HeatCount > 0 ? schedItem.HeatCount : 0;
            if (heatCount == 0) return false;

            for (int h = 1; h <= heatCount; h++) {
                if (!IsHeatConfirmed(gender, eventName, stage, h)) return false;
            }
            return true;
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
            if (laneState != null) {
                // 保存反应时间到成绩记录
                if (laneState.ReactionTime > 0) {
                    result.StartingBlockTime = laneState.ReactionTime;
                }
                laneState.IsFinished = true;
            }

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
        private Swimmer FindDuplicate(string name, string gender, string eventName, string bibNumber, string idNumber = "", string country = "") {
            foreach (var s in _swimmers) {
                // 1. 身份证号相同且非空 + 同项目 = 重复（最高优先级）
                if (!string.IsNullOrEmpty(idNumber) && !string.IsNullOrEmpty(s.IDNumber) && s.IDNumber == idNumber && s.EventName == eventName) return s;
                // 2. 号码牌相同且非空 + 同项目 = 重复
                if (!string.IsNullOrEmpty(bibNumber) && !string.IsNullOrEmpty(s.BibNumber) && s.BibNumber == bibNumber && s.EventName == eventName) return s;
                // 3. 同姓名 + 同性别 + 同代表队 + 同项目 = 重复
                if (!string.IsNullOrEmpty(name) && s.Name == name && s.Gender == gender
                    && (s.Country ?? "") == (country ?? "") && s.EventName == eventName) return s;
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
                        var dup = FindDuplicate(sw.Name, sw.Gender, sw.EventName, sw.BibNumber, sw.IDNumber, sw.Country);
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
                string regIdNumber = RegIDNumberBox.Text.Trim();
                string regCountry = RegCountryBox.Text.Trim();
                var dup = FindDuplicate(name, gender, ev.Item1, bibNumber, regIdNumber, regCountry);
                if (dup != null) continue;

                var sw = new Swimmer {
                    BibNumber = bibNumber,
                    Name = name,
                    Gender = gender,
                    Country = regCountry,
                    IDNumber = regIdNumber,
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

        // ═══════════════════════════════════════════════════════════════
        // 出场编排微调
        // ═══════════════════════════════════════════════════════════════
        private bool _editUpdating = false;
        private DataGrid _editSelectedGrid = null;

        private void EditFilter_Changed(object sender, SelectionChangedEventArgs e) {
            if (!_initialized || _editUpdating) return;
            UpdateEditHeatCombo();
        }

        private void EditHeat_Changed(object sender, SelectionChangedEventArgs e) {
            if (!_initialized || _editUpdating) return;
            // 使用Dispatcher延迟执行，确保SelectedItem已更新
            Dispatcher.BeginInvoke((Action)delegate() {
                RefreshEditPreview();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateEditHeatCombo() {
            if (_editUpdating) return;
            if (EditHeatCombo == null || EditGenderCombo == null || EditEventCombo == null || EditStageCombo == null) return;
            _editUpdating = true;
            try {
                // 填充项目列表（仅在为空时填充，避免破坏选择状态）
                if (EditEventCombo.Items.Count == 0) {
                    var evSet = new HashSet<string>();
                    foreach (var s in _swimmers) { if (!string.IsNullOrEmpty(s.EventName)) evSet.Add(s.EventName); }
                    foreach (string ev in evSet.OrderBy(x => x)) EditEventCombo.Items.Add(ev);
                    if (EditEventCombo.Items.Count > 0) EditEventCombo.SelectedIndex = 0;
                }

                string gender = EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "男";
                string eventName = EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
                string stage = EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "预赛";

                int prevHeatIndex = EditHeatCombo.SelectedIndex;
                EditHeatCombo.Items.Clear();
                EditHeatCombo.Items.Add("全部");
                // 从运动员数据获取有人的组号（同时检查当前赛次和历史StageAssignments）
                var heatNumbers = new HashSet<int>();
                foreach (var s in _swimmers) {
                    if (s.Gender != gender || s.EventName != eventName) continue;
                    // 当前赛次匹配
                    if (s.CurrentStage == stage && s.Heat > 0)
                        heatNumbers.Add(s.Heat);
                    // 历史赛次记录
                    var sa = s.GetAssignmentForStage(stage);
                    if (sa != null && sa.Heat > 0)
                        heatNumbers.Add(sa.Heat);
                }
                foreach (int h in heatNumbers.OrderBy(x => x)) {
                    EditHeatCombo.Items.Add(string.Format("第{0}组", h));
                }
                // 恢复之前选中的组别索引
                if (prevHeatIndex >= 0 && prevHeatIndex < EditHeatCombo.Items.Count)
                    EditHeatCombo.SelectedIndex = prevHeatIndex;
                else if (EditHeatCombo.Items.Count > 0)
                    EditHeatCombo.SelectedIndex = 0;
            } finally {
                _editUpdating = false;
            }
            RefreshEditPreview();
        }

        private void RebuildEditEventCombo() {
            // 重建项目下拉框（保留当前选择）
            int prevIndex = EditEventCombo.SelectedIndex;
            EditEventCombo.Items.Clear();
            var evSet = new HashSet<string>();
            foreach (var s in _swimmers) { if (!string.IsNullOrEmpty(s.EventName)) evSet.Add(s.EventName); }
            foreach (string ev in evSet.OrderBy(x => x)) EditEventCombo.Items.Add(ev);
            if (prevIndex >= 0 && prevIndex < EditEventCombo.Items.Count)
                EditEventCombo.SelectedIndex = prevIndex;
            else if (EditEventCombo.Items.Count > 0)
                EditEventCombo.SelectedIndex = 0;
        }

        private void RefreshEditPreview_Click(object sender, RoutedEventArgs e) {
            // 刷新按钮：重建项目列表（可能有新导入的项目），然后刷新组别和数据
            _editUpdating = true;
            try {
                RebuildEditEventCombo();
            } finally {
                _editUpdating = false;
            }
            UpdateEditHeatCombo();
        }

        private string GetPreviousStage(string stage) {
            switch (stage) {
                case "半决赛": return "预赛";
                case "决赛": return "半决赛";
                default: return "";
            }
        }

        private void RefreshEditPreview() {
            if (EditPreviewGrid == null) return;
            string gender = EditGenderCombo != null && EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "";
            string eventName = EditEventCombo != null && EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
            string stage = EditStageCombo != null && EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "";

            string heatStr = EditHeatCombo != null && EditHeatCombo.SelectedItem != null ? EditHeatCombo.SelectedItem.ToString() : "";
            bool showAll = (heatStr == "全部");
            int heat = 0;
            if (!showAll) {
                var m = System.Text.RegularExpressions.Regex.Match(heatStr, @"\d+");
                if (m.Success) heat = int.Parse(m.Value);
            }

            string prevStage = GetPreviousStage(stage);
            if (EditSeedTimeColumn != null) {
                EditSeedTimeColumn.Header = stage == "预赛" ? "报名成绩" : (prevStage + "成绩");
            }

            // 查找该赛次的运动员
            var matchedSwimmers = new List<Tuple<Swimmer, int, int>>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat > 0) {
                    if (showAll || sa.Heat == heat)
                        matchedSwimmers.Add(Tuple.Create(s, sa.Heat, sa.Lane));
                    continue;
                }
                if (s.CurrentStage == stage && s.Heat > 0) {
                    if (showAll || s.Heat == heat)
                        matchedSwimmers.Add(Tuple.Create(s, s.Heat, s.Lane));
                }
            }
            matchedSwimmers.Sort((a, b) => {
                int c = a.Item2.CompareTo(b.Item2);
                return c != 0 ? c : a.Item3.CompareTo(b.Item3);
            });

            if (showAll)
                AddLog(string.Format("编排预览: {0} {1} {2} 全部 → {3}人", gender, eventName, stage, matchedSwimmers.Count));
            else
                AddLog(string.Format("编排预览: {0} {1} {2} 第{3}组 → {4}人", gender, eventName, stage, heat, matchedSwimmers.Count));

            if (showAll) {
                // ═══ 全部模式：按组分隔显示，每组一个标题+DataGrid ═══
                EditPreviewGrid.Visibility = System.Windows.Visibility.Collapsed;
                EditAllGroupsScroll.Visibility = System.Windows.Visibility.Visible;
                EditAllGroupsPanel.Children.Clear();

                var groups = matchedSwimmers.GroupBy(t => t.Item2).OrderBy(g => g.Key);
                string seedColHeader = stage == "预赛" ? "报名成绩" : (prevStage + "成绩");

                foreach (var grp in groups) {
                    // 组标题
                    var header = new TextBlock {
                        Text = string.Format("第{0}组（{1}人）", grp.Key, grp.Count()),
                        FontWeight = FontWeights.Bold,
                        FontSize = 15,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF")),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
                        Padding = new Thickness(10, 5, 10, 5),
                        Margin = new Thickness(0, 8, 0, 4)
                    };
                    EditAllGroupsPanel.Children.Add(header);

                    // 组内DataGrid
                    var grid = new DataGrid {
                        AutoGenerateColumns = false,
                        CanUserAddRows = false,
                        IsReadOnly = true,
                        SelectionMode = DataGridSelectionMode.Single,
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                        MinHeight = 30,
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
                    };
                    grid.Columns.Add(new DataGridTextColumn { Header = "道", Binding = new System.Windows.Data.Binding("Lane"), Width = new DataGridLength(40) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "号码", Binding = new System.Windows.Data.Binding("BibNumber"), Width = new DataGridLength(60) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "姓名", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(80) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "性别", Binding = new System.Windows.Data.Binding("Gender"), Width = new DataGridLength(40) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "代表队", Binding = new System.Windows.Data.Binding("Country"), Width = new DataGridLength(80) });
                    grid.Columns.Add(new DataGridTextColumn { Header = seedColHeader, Binding = new System.Windows.Data.Binding("SeedTime"), Width = new DataGridLength(80) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "年龄组", Binding = new System.Windows.Data.Binding("AgeCategory"), Width = new DataGridLength(60) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding("Status"), Width = new DataGridLength(50) });

                    var grpData = grp.OrderBy(t => t.Item3).Select(t => {
                        var s = t.Item1;
                        string seedTime = "";
                        if (stage == "预赛") {
                            var sa2 = s.GetAssignmentForStage(stage);
                            seedTime = sa2 != null && !string.IsNullOrEmpty(sa2.EntryTime) ? sa2.EntryTime : (s.EntryTime ?? "");
                        } else {
                            var prevResult = s.GetResultForStage(prevStage);
                            seedTime = prevResult != null && prevResult.FinalTime > 0 ? TimeFormatter.Format(prevResult.FinalTime) : "";
                        }
                        return new {
                            Lane = t.Item3,
                            BibNumber = s.BibNumber ?? "",
                            Name = s.Name ?? "",
                            Gender = s.Gender ?? "",
                            Country = s.Country ?? "",
                            SeedTime = seedTime,
                            AgeCategory = s.AgeCategory ?? "",
                            Status = s.Status ?? ""
                        };
                    }).ToList();
                    grid.SelectionChanged += delegate { _editSelectedGrid = grid; };
                    grid.ItemsSource = grpData;
                    EditAllGroupsPanel.Children.Add(grid);
                }

                if (matchedSwimmers.Count == 0) {
                    EditAllGroupsPanel.Children.Add(new TextBlock {
                        Text = "暂无分组数据",
                        Foreground = new SolidColorBrush(Colors.Gray),
                        Margin = new Thickness(10),
                        FontSize = 14
                    });
                }
            } else {
                // ═══ 单组模式：同样用蓝底标题+DataGrid风格 ═══
                EditPreviewGrid.Visibility = System.Windows.Visibility.Collapsed;
                EditAllGroupsScroll.Visibility = System.Windows.Visibility.Visible;
                EditAllGroupsPanel.Children.Clear();

                string seedColHeader = stage == "预赛" ? "报名成绩" : (prevStage + "成绩");

                // 组标题
                var header = new TextBlock {
                    Text = string.Format("第{0}组（{1}人）", heat, matchedSwimmers.Count),
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                EditAllGroupsPanel.Children.Add(header);

                var grid = new DataGrid {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    IsReadOnly = true,
                    SelectionMode = DataGridSelectionMode.Single,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                    MinHeight = 30,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "道", Binding = new System.Windows.Data.Binding("Lane"), Width = new DataGridLength(40) });
                grid.Columns.Add(new DataGridTextColumn { Header = "号码", Binding = new System.Windows.Data.Binding("BibNumber"), Width = new DataGridLength(60) });
                grid.Columns.Add(new DataGridTextColumn { Header = "姓名", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(80) });
                grid.Columns.Add(new DataGridTextColumn { Header = "性别", Binding = new System.Windows.Data.Binding("Gender"), Width = new DataGridLength(40) });
                grid.Columns.Add(new DataGridTextColumn { Header = "代表队", Binding = new System.Windows.Data.Binding("Country"), Width = new DataGridLength(80) });
                grid.Columns.Add(new DataGridTextColumn { Header = seedColHeader, Binding = new System.Windows.Data.Binding("SeedTime"), Width = new DataGridLength(80) });
                grid.Columns.Add(new DataGridTextColumn { Header = "年龄组", Binding = new System.Windows.Data.Binding("AgeCategory"), Width = new DataGridLength(60) });
                grid.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding("Status"), Width = new DataGridLength(50) });

                // 选中行跟踪（用于上移/下移操作）
                grid.SelectionChanged += delegate { _editSelectedGrid = grid; };

                var displayData = matchedSwimmers.Select(t => {
                    var s = t.Item1;
                    string seedTime = "";
                    if (stage == "预赛") {
                        var sa = s.GetAssignmentForStage(stage);
                        seedTime = sa != null && !string.IsNullOrEmpty(sa.EntryTime) ? sa.EntryTime : (s.EntryTime ?? "");
                    } else {
                        var prevResult = s.GetResultForStage(prevStage);
                        seedTime = prevResult != null && prevResult.FinalTime > 0 ? TimeFormatter.Format(prevResult.FinalTime) : "";
                    }
                    return new {
                        Lane = t.Item3,
                        BibNumber = s.BibNumber ?? "",
                        Name = s.Name ?? "",
                        Gender = s.Gender ?? "",
                        Country = s.Country ?? "",
                        SeedTime = seedTime,
                        AgeCategory = s.AgeCategory ?? "",
                        Status = s.Status ?? ""
                    };
                }).ToList();
                grid.ItemsSource = displayData;
                EditAllGroupsPanel.Children.Add(grid);

                if (matchedSwimmers.Count == 0) {
                    EditAllGroupsPanel.Children.Add(new TextBlock {
                        Text = "暂无分组数据",
                        Foreground = new SolidColorBrush(Colors.Gray),
                        Margin = new Thickness(10),
                        FontSize = 14
                    });
                }
            }
        }

        private DataGrid GetActiveEditGrid() {
            // 优先使用动态生成的grid（全部模式和单组模式都用它）
            if (_editSelectedGrid != null && _editSelectedGrid.SelectedIndex >= 0)
                return _editSelectedGrid;
            return EditPreviewGrid;
        }

        private void EditMoveUp_Click(object sender, RoutedEventArgs e) {
            var grid = GetActiveEditGrid();
            int idx = grid.SelectedIndex;
            if (idx <= 0) return;
            SwapLanes(idx, idx - 1);
        }

        private void EditMoveDown_Click(object sender, RoutedEventArgs e) {
            var grid = GetActiveEditGrid();
            int idx = grid.SelectedIndex;
            if (idx < 0 || idx >= grid.Items.Count - 1) return;
            SwapLanes(idx, idx + 1);
        }

        private void EditSwapLane_Click(object sender, RoutedEventArgs e) {
            MessageBox.Show("请选中一行后使用上移/下移来调整泳道位置", "提示");
        }

        private void SwapLanes(int idx1, int idx2) {
            string gender = ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString();
            string eventName = EditEventCombo.SelectedItem.ToString();
            string stage = ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString();
            string heatStr = EditHeatCombo.SelectedItem != null ? EditHeatCombo.SelectedItem.ToString() : "";
            bool showAll = (heatStr == "全部");
            int heat = 0;
            if (!showAll) {
                var m = System.Text.RegularExpressions.Regex.Match(heatStr, @"\d+");
                if (m.Success) heat = int.Parse(m.Value);
            }

            // 查找运动员列表（同RefreshEditPreview逻辑）
            var matchedSwimmers = new List<Tuple<Swimmer, int, int>>();  // (swimmer, heat, lane)
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat > 0) {
                    if (showAll || sa.Heat == heat)
                        matchedSwimmers.Add(Tuple.Create(s, sa.Heat, sa.Lane));
                    continue;
                }
                if (s.CurrentStage == stage && s.Heat > 0) {
                    if (showAll || s.Heat == heat)
                        matchedSwimmers.Add(Tuple.Create(s, s.Heat, s.Lane));
                }
            }
            matchedSwimmers.Sort((a, b) => {
                int c = a.Item2.CompareTo(b.Item2);
                return c != 0 ? c : a.Item3.CompareTo(b.Item3);
            });

            if (idx1 >= 0 && idx1 < matchedSwimmers.Count && idx2 >= 0 && idx2 < matchedSwimmers.Count) {
                var sw1 = matchedSwimmers[idx1];
                var sw2 = matchedSwimmers[idx2];
                // 只允许同组内交换泳道
                if (sw1.Item2 != sw2.Item2) {
                    MessageBox.Show("只能在同一组内交换泳道位置。", "提示");
                    return;
                }
                int tmpLane = sw1.Item3;
                var sa1 = sw1.Item1.GetAssignmentForStage(stage);
                var sa2 = sw2.Item1.GetAssignmentForStage(stage);
                if (sa1 != null && sa2 != null) {
                    sa1.Lane = sw2.Item3;
                    sa2.Lane = tmpLane;
                }
                if (sw1.Item1.CurrentStage == stage) sw1.Item1.Lane = sw2.Item3;
                if (sw2.Item1.CurrentStage == stage) sw2.Item1.Lane = tmpLane;
                AutoSaveData();
                RefreshEditPreview();
            }
        }

        private void EditRemoveFromHeat_Click(object sender, RoutedEventArgs e) {
            var grid = GetActiveEditGrid();
            var selected = grid.SelectedItem;
            if (selected == null) { MessageBox.Show("请先选中一名运动员"); return; }
            string bib = selected.GetType().GetProperty("BibNumber").GetValue(selected, null).ToString();
            string stage = EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "";
            var sw = _swimmers.FirstOrDefault(s => s.BibNumber == bib);
            if (sw != null) {
                if (MessageBox.Show(string.Format("确定将 {0} 移出本组？", sw.Name), "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                    // 清除StageAssignments中的记录
                    if (sw.StageAssignments.ContainsKey(stage))
                        sw.StageAssignments.Remove(stage);
                    // 如果是当前赛次，也清除当前属性
                    if (sw.CurrentStage == stage) {
                        sw.Heat = 0;
                        sw.Lane = 0;
                    }
                    AutoSaveData();
                    RefreshEditPreview();
                    AddLog(string.Format("已将 {0} 移出编排", sw.Name));
                }
            }
        }

        private void EditSaveChanges_Click(object sender, RoutedEventArgs e) {
            AutoSaveData();
            Broadcast();
            BuildScheduleTree();
            MessageBox.Show("编排修改已保存！", "保存成功");
            AddLog("出场编排修改已保存");
        }
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
        private ScheduleItem _selectedScheduleItem;

        private void AddSchedule_Click(object sender, RoutedEventArgs e) {
            int nextSession = _schedule.Count > 0 ? _schedule.Max(s => s.SessionNumber) : 1;
            _schedule.Add(new ScheduleItem {
                SessionNumber = nextSession,
                SessionName = string.Format("第{0}单元", nextSession),
                Date = GetDatePickerText(StartDatePicker)
            });
            AutoSaveData();
            RebuildScheduleGroupedView();
        }

        private void DeleteSchedule_Click(object sender, RoutedEventArgs e) {
            if (_selectedScheduleItem != null) {
                _schedule.Remove(_selectedScheduleItem);
                _selectedScheduleItem = null;
                AutoSaveData();
                RebuildScheduleGroupedView();
            } else {
                MessageBox.Show("请先在日程表中选中要删除的行");
            }
        }

        private string InferTimePeriod(string time) {
            if (string.IsNullOrEmpty(time)) return "上午";
            int hour = 9;
            var parts = time.Split(':');
            if (parts.Length >= 1) int.TryParse(parts[0], out hour);
            if (hour >= 18) return "晚上";
            if (hour >= 12) return "下午";
            return "上午";
        }

        private void RebuildScheduleGroupedView() {
            if (ScheduleGroupedPanel == null) return;
            ScheduleGroupedPanel.Children.Clear();

            // 自动推断单元编号：按日期+时段分组
            var sessionMap = new Dictionary<string, int>();
            int sessionNum = 1;
            foreach (var item in _schedule.OrderBy(s => s.Date).ThenBy(s => s.Time)) {
                string period = InferTimePeriod(item.Time);
                string key = (item.Date ?? "") + "|" + period;
                if (!sessionMap.ContainsKey(key)) {
                    sessionMap[key] = sessionNum;
                    sessionNum++;
                }
                item.SessionNumber = sessionMap[key];
                item.SessionName = string.Format("第{0}单元（{1}{2}）", sessionMap[key], item.Date ?? "", period);
            }

            // 按单元分组显示
            var groups = _schedule.GroupBy(s => s.SessionNumber).OrderBy(g => g.Key);
            foreach (var group in groups) {
                string label = group.First().SessionName ?? string.Format("第{0}单元", group.Key);

                // 单元标题
                var header = new TextBlock {
                    Text = label,
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A5FB4")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F0FE")),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 5, 0, 6)
                };
                ScheduleGroupedPanel.Children.Add(header);

                // 该单元的DataGrid
                var grid = new DataGrid {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    MinHeight = 30,
                    AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                    IsReadOnly = false,
                    SelectionMode = DataGridSelectionMode.Single
                };

                grid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new System.Windows.Data.Binding("Time"), Width = new DataGridLength(70) });
                grid.Columns.Add(new DataGridTextColumn { Header = "性别", Binding = new System.Windows.Data.Binding("Gender"), Width = new DataGridLength(40) });
                grid.Columns.Add(new DataGridTextColumn { Header = "项目", Binding = new System.Windows.Data.Binding("EventName"), Width = new DataGridLength(140) });
                grid.Columns.Add(new DataGridTextColumn { Header = "阶段", Binding = new System.Windows.Data.Binding("Stage"), Width = new DataGridLength(60) });
                grid.Columns.Add(new DataGridTextColumn { Header = "组数", Binding = new System.Windows.Data.Binding("HeatCount"), Width = new DataGridLength(50) });

                var items = new ObservableCollection<ScheduleItem>(group.OrderBy(s => s.Time));
                grid.ItemsSource = items;

                // 选中行跟踪
                grid.SelectionChanged += delegate {
                    _selectedScheduleItem = grid.SelectedItem as ScheduleItem;
                };

                ScheduleGroupedPanel.Children.Add(grid);
            }

            if (_schedule.Count == 0) {
                ScheduleGroupedPanel.Children.Add(new TextBlock {
                    Text = "暂无赛程。请点击\"一键生成日程\"或\"添加赛程项\"。",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    Margin = new Thickness(10)
                });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 一键生成日程（只生成赛程安排和预估组数，不分配具体运动员）
        // ═══════════════════════════════════════════════════════════════
        private void AutoBuildSchedule_Click(object sender, RoutedEventArgs e) {
            if (_swimmers.Count == 0) {
                MessageBox.Show("没有已注册的运动员/接力队，请先注册再生成日程。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // 检查是否有有效赛程（忽略空条目）
            int validScheduleCount = _schedule.Count(s => !string.IsNullOrEmpty(s.EventName));
            if (validScheduleCount > 0) {
                if (MessageBox.Show(string.Format("已有{0}条赛程数据，是否清除并重新生成？", validScheduleCount), "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;
            }
            _schedule.Clear();
            AutoBuildSchedule();
            BuildScheduleTree();
            AutoSaveData();
            Broadcast();
            MessageBox.Show(string.Format("日程生成完成！\n共{0}条赛程项。\n\n下一步请点击\"预赛自动分组\"对预赛进行蛇形分组。", _schedule.Count),
                "日程安排完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ═══════════════════════════════════════════════════════════════
        // 预赛自动分组（只对第一赛次的运动员进行蛇形分组）
        // 后续赛次（半决赛/决赛）需等上一轮成绩出来后通过晋级处理分组
        // ═══════════════════════════════════════════════════════════════
        private void AutoGenerateHeats_Click(object sender, RoutedEventArgs e) {
            if (_swimmers.Count == 0) {
                MessageBox.Show("没有已注册的运动员/接力队，请先注册再生成分组。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_schedule.Count == 0) {
                MessageBox.Show("请先点击\"一键生成日程\"生成赛程安排，再进行分组。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 统一对所有项目（个人+接力）的第一赛次进行蛇形分组
            int generated = 0;
            foreach (var item in _schedule) {
                string fullEvent = item.EventName;
                string stage = item.Stage;
                string gender = item.Gender;

                var eventSwimmers = _swimmers.Where(s =>
                    s.EventName == fullEvent && s.Gender == gender && s.CurrentStage == stage
                ).ToList();

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
                string typeLabel = fullEvent.Contains("接力") ? "队" : "人";
                AddLog(string.Format("  {0} {1} {2}: {3}{4} → {5}组", gender, fullEvent, stage, eventSwimmers.Count, typeLabel, item.HeatCount));
            }

            BuildScheduleTree();
            if (generated > 0) {
                AddLog(string.Format("自动分组完成: {0}项已分配", generated));
                MessageBox.Show(string.Format("分组完成！\n共{0}项已按报名成绩蛇形分组。\n\n后续赛次需在成绩与排名中通过\"晋级处理\"根据比赛成绩进行分组。", generated),
                    "分组完成", MessageBoxButton.OK, MessageBoxImage.Information);
            } else {
                MessageBox.Show("未分配任何运动员/接力队。\n\n请检查：\n1. 项目名称是否与赛程一致\n2. 性别是否与赛程一致\n3. 是否已生成日程", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            AutoSaveData();
            Broadcast();
            UpdateEditHeatCombo();
        }

        /// <summary>
        /// 自动生成赛程和日程安排
        /// 根据报名人数确定赛次和预估组数，按游泳比赛惯例安排时间
        /// 上午预赛，下午半决赛，晚上决赛
        /// </summary>
        private void AutoBuildSchedule() {
            string startDateStr = GetDatePickerText(StartDatePicker);
            string endDateStr = GetDatePickerText(EndDatePicker);
            DateTime startDate, endDate;
            if (!DateTime.TryParse(startDateStr, out startDate)) startDate = DateTime.Today;
            if (!DateTime.TryParse(endDateStr, out endDate)) endDate = startDate;
            if (endDate < startDate) endDate = startDate;
            int totalDays = (int)(endDate - startDate).TotalDays + 1;

            // 收集所有项目（过滤掉无效数据：性别或项目为空的）
            var validSwimmers = _swimmers.Where(s => !string.IsNullOrEmpty(s.Gender) && !string.IsNullOrEmpty(s.EventName)).ToList();
            var eventGroups = validSwimmers.GroupBy(s => new { s.Gender, s.EventName })
                .OrderBy(g => g.Key.Gender).ThenBy(g => g.Key.EventName).ToList();

            var allItems = new List<ScheduleItem>();
            int laneCount = _poolConfig.LaneCount;

            foreach (var group in eventGroups) {
                string gender = group.Key.Gender;
                string eventName = group.Key.EventName;
                bool isRelay = eventName.Contains("接力");
                int count = group.Count();
                var stages = HeatScheduler.GetStages(count, eventName);
                string firstStage = stages[0];

                foreach (var sw in group) sw.CurrentStage = firstStage;

                foreach (string stage in stages) {
                    int estimatedCount = count;
                    if (stage == "半决赛") estimatedCount = 16;
                    else if (stage == "决赛" && stages.Count > 1) estimatedCount = 8;
                    int estimatedHeats = (int)Math.Ceiling((double)estimatedCount / laneCount);
                    if (estimatedHeats < 1) estimatedHeats = 1;

                    allItems.Add(new ScheduleItem {
                        Gender = gender, EventName = eventName, Stage = stage,
                        IsRelay = isRelay, HeatCount = estimatedHeats
                    });
                }
                string typeLabel = isRelay ? "(接力)" : "";
                AddLog(string.Format("  {0} {1}{2}: {3}{4} → {5}", gender, eventName, typeLabel, count, isRelay ? "队" : "人", string.Join("→", stages.ToArray())));
            }

            // 按赛次分类：预赛、半决赛、决赛
            var prelims = allItems.Where(s => s.Stage == "预赛").ToList();
            var semis = allItems.Where(s => s.Stage == "半决赛").ToList();
            var finals = allItems.Where(s => s.Stage == "决赛").ToList();

            // 预赛分配到各天上午，半决赛分配到下午，决赛分配到晚上
            int prelimsPerDay = prelims.Count > 0 ? (int)Math.Ceiling((double)prelims.Count / totalDays) : 0;
            int finalsPerDay = finals.Count > 0 ? (int)Math.Ceiling((double)finals.Count / totalDays) : 0;

            int prelimIdx = 0, semiIdx = 0, finalIdx = 0;
            int sessionNum = 1;

            for (int day = 0; day < totalDays; day++) {
                string dateStr = startDate.AddDays(day).ToString("yyyy-MM-dd");

                // 上午场：预赛（09:00开始，每场约15分钟间隔）
                int morningCount = 0;
                int morningMinute = 0;
                while (prelimIdx < prelims.Count && morningCount < prelimsPerDay) {
                    var item = prelims[prelimIdx];
                    item.Date = dateStr;
                    item.Time = string.Format("09:{0:D2}", morningMinute);
                    item.SessionNumber = sessionNum;
                    item.SessionName = string.Format("第{0}单元（{1}上午）", sessionNum, dateStr);
                    _schedule.Add(item);
                    morningMinute += 15;
                    if (morningMinute >= 60) morningMinute = 0;
                    prelimIdx++;
                    morningCount++;
                }
                if (morningCount > 0) sessionNum++;

                // 下午场：半决赛（14:00开始）
                int afternoonMinute = 0;
                bool hasAfternoon = false;
                while (semiIdx < semis.Count && semiIdx < (day + 1) * Math.Max(1, (int)Math.Ceiling((double)semis.Count / totalDays))) {
                    var item = semis[semiIdx];
                    item.Date = dateStr;
                    item.Time = string.Format("14:{0:D2}", afternoonMinute);
                    item.SessionNumber = sessionNum;
                    item.SessionName = string.Format("第{0}单元（{1}下午）", sessionNum, dateStr);
                    _schedule.Add(item);
                    afternoonMinute += 15;
                    if (afternoonMinute >= 60) afternoonMinute = 0;
                    semiIdx++;
                    hasAfternoon = true;
                }
                if (hasAfternoon) sessionNum++;

                // 晚上场：决赛（19:00开始）
                int eveningCount = 0;
                int eveningMinute = 0;
                while (finalIdx < finals.Count && eveningCount < finalsPerDay) {
                    var item = finals[finalIdx];
                    item.Date = dateStr;
                    item.Time = string.Format("19:{0:D2}", eveningMinute);
                    item.SessionNumber = sessionNum;
                    item.SessionName = string.Format("第{0}单元（{1}晚上）", sessionNum, dateStr);
                    _schedule.Add(item);
                    eveningMinute += 15;
                    if (eveningMinute >= 60) eveningMinute = 0;
                    finalIdx++;
                    eveningCount++;
                }
                if (eveningCount > 0) sessionNum++;
            }

            // 处理剩余未分配的（如果比赛天数不够）
            string lastDate = endDate.ToString("yyyy-MM-dd");
            while (prelimIdx < prelims.Count) {
                prelims[prelimIdx].Date = lastDate; prelims[prelimIdx].Time = "09:00";
                prelims[prelimIdx].SessionNumber = sessionNum;
                _schedule.Add(prelims[prelimIdx]); prelimIdx++;
            }
            while (semiIdx < semis.Count) {
                semis[semiIdx].Date = lastDate; semis[semiIdx].Time = "14:00";
                semis[semiIdx].SessionNumber = sessionNum;
                _schedule.Add(semis[semiIdx]); semiIdx++;
            }
            while (finalIdx < finals.Count) {
                finals[finalIdx].Date = lastDate; finals[finalIdx].Time = "19:00";
                finals[finalIdx].SessionNumber = sessionNum;
                _schedule.Add(finals[finalIdx]); finalIdx++;
            }

            AddLog(string.Format("自动日程安排完成: {0}条赛程, {1}天, 上午预赛/下午半决赛/晚上决赛",
                _schedule.Count, totalDays));
        }

        // ═══════════════════════════════════════════════════════════════
        // 成绩与排名
        // ═══════════════════════════════════════════════════════════════
        private bool _resultUpdating = false;
        private void ResultEvent_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) { UpdateResultHeatCombo(); RefreshResultGrid(); } }
        private void ResultStage_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) { UpdateResultHeatCombo(); RefreshResultGrid(); } }
        private void ResultGender_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) { UpdateResultHeatCombo(); RefreshResultGrid(); } }
        private void ResultHeat_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) RefreshResultGrid(); }

        private void UpdateResultHeatCombo() {
            if (ResultHeatCombo == null || _resultUpdating) return;
            _resultUpdating = true;
            try {
            // 刷新项目列表：只显示有运动员注册的项目
            string prevEvent = ResultEventCombo.SelectedItem != null ? ResultEventCombo.SelectedItem.ToString() : "";
            string gender = ResultGenderCombo.SelectedItem != null ? ((ComboBoxItem)ResultGenderCombo.SelectedItem).Content.ToString() : "男";
            ResultEventCombo.Items.Clear();
            var eventSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (s.Gender == gender && !string.IsNullOrEmpty(s.EventName))
                    eventSet.Add(s.EventName);
            }
            foreach (string ev in eventSet.OrderBy(x => x)) ResultEventCombo.Items.Add(ev);
            // 恢复之前选中的项目
            if (!string.IsNullOrEmpty(prevEvent) && ResultEventCombo.Items.Contains(prevEvent))
                ResultEventCombo.SelectedItem = prevEvent;
            else if (ResultEventCombo.Items.Count > 0)
                ResultEventCombo.SelectedIndex = 0;

            string eventName = ResultEventCombo.SelectedItem != null ? ResultEventCombo.SelectedItem.ToString() : "";
            string stage = ResultStageCombo.SelectedItem != null ? ((ComboBoxItem)ResultStageCombo.SelectedItem).Content.ToString() : "预赛";

            ResultHeatCombo.Items.Clear();
            ResultHeatCombo.Items.Add(new ComboBoxItem { Content = "全部" });

            var heats = new HashSet<int>();
            foreach (var s in _swimmers) {
                if (!string.IsNullOrEmpty(eventName) && s.EventName != eventName) continue;
                if (s.Gender != gender) continue;
                foreach (var r in s.Results) {
                    if (r.Stage == stage && r.Heat > 0) heats.Add(r.Heat);
                }
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat > 0) heats.Add(sa.Heat);
                if (s.CurrentStage == stage && s.Heat > 0) heats.Add(s.Heat);
            }
            foreach (int h in heats.OrderBy(x => x)) {
                ResultHeatCombo.Items.Add(new ComboBoxItem { Content = string.Format("第{0}组", h) });
            }
            ResultHeatCombo.SelectedIndex = 0;
            } finally {
                _resultUpdating = false;
            }
        }

        private void RefreshResultGrid() {
            if (ResultEventCombo == null || ResultStageCombo == null || ResultGenderCombo == null || ResultGrid == null) return;
            string gender = ResultGenderCombo.SelectedItem != null ? ((ComboBoxItem)ResultGenderCombo.SelectedItem).Content.ToString() : "男";
            string eventName = ResultEventCombo.SelectedItem != null ? ResultEventCombo.SelectedItem.ToString() : "";
            string stage = ResultStageCombo.SelectedItem != null ? ((ComboBoxItem)ResultStageCombo.SelectedItem).Content.ToString() : "预赛";
            string heatFilter = ResultHeatCombo != null && ResultHeatCombo.SelectedItem != null ? ((ComboBoxItem)ResultHeatCombo.SelectedItem).Content.ToString() : "全部";
            int filterHeat = 0;
            if (heatFilter != "全部") {
                var m = System.Text.RegularExpressions.Regex.Match(heatFilter, @"\d+");
                if (m.Success) filterHeat = int.Parse(m.Value);
            }

            if (string.IsNullOrEmpty(eventName)) {
                ResultGrid.ItemsSource = null;
                return;
            }

            // 按性别和项目筛选
            var allMatched = _swimmers.Where(s => s.EventName == eventName && s.Gender == gender).ToList();

            // 按阶段筛选：有该阶段成绩，或有该阶段分组记录
            var results = allMatched.Where(s =>
                s.GetResultForStage(stage) != null ||
                s.GetAssignmentForStage(stage) != null ||
                s.CurrentStage == stage
            ).ToList();

            // 按组筛选
            if (filterHeat > 0) {
                results = results.Where(s => {
                    var r = s.GetResultForStage(stage);
                    if (r != null) return r.Heat == filterHeat;
                    var sa = s.GetAssignmentForStage(stage);
                    if (sa != null) return sa.Heat == filterHeat;
                    return s.Heat == filterHeat;
                }).ToList();
            }

            var displayData = results.Select(s => {
                var r = s.GetResultForStage(stage);
                // 获取该赛次的泳道号（优先成绩记录 > StageAssignment > 当前值）
                int lane = 0;
                if (r != null) lane = r.Lane;
                else {
                    var sa = s.GetAssignmentForStage(stage);
                    lane = sa != null ? sa.Lane : s.Lane;
                }
                double sortTime = r != null && r.FinalTime > 0 ? r.FinalTime : double.MaxValue;
                return new {
                    SortTime = sortTime,
                    Lane = lane,
                    BibNumber = s.BibNumber ?? "",
                    Name = s.Name ?? "",
                    Country = s.Country ?? "",
                    FinalTime = r != null ? TimeFormatter.Format(r.FinalTime) : "",
                    TimingSource = r != null ? (r.TimingSource ?? "") : "",
                    ReactionTime = r != null && r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F2") : "",
                    Status = s.Status ?? "",
                    RecordNote = ""
                };
            }).OrderBy(x => x.SortTime).ToList();

            // 重新计算排名
            var rankedData = new List<object>();
            int rankNum = 1;
            foreach (var item in displayData) {
                string rankStr = item.SortTime < double.MaxValue ? rankNum.ToString() : "";
                rankedData.Add(new {
                    Rank = rankStr,
                    item.Lane, item.BibNumber, item.Name, item.Country,
                    item.FinalTime, item.TimingSource, item.ReactionTime, item.Status, item.RecordNote
                });
                if (item.SortTime < double.MaxValue) rankNum++;
            }

            ResultGrid.ItemsSource = rankedData;
        }

        private void Promotion_Click(object sender, RoutedEventArgs e) {
            var win = new PromotionQueryWindow(_swimmers, _events, _poolConfig, _schedule);
            win.Owner = this;
            win.ShowDialog();
            // 晋级处理后刷新相关界面
            BuildScheduleTree();
            UpdateResultHeatCombo();
            RefreshResultGrid();
            UpdateEditHeatCombo();
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
            string newName = CompNameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName)) {
                MessageBox.Show("请输入赛事名称", "提示");
                return;
            }

            // 检查是否切换到了新赛事（名称变了）
            bool isNewCompetition = !string.IsNullOrEmpty(_competitionName) && _competitionName != newName;

            if (isNewCompetition) {
                // 切换到新赛事，清除所有旧数据和界面
                ClearAllDataAndUI();
                AddLog(string.Format("已从 [{0}] 切换到新赛事 [{1}]", _competitionName, newName));
            }

            _competitionName = newName;
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
            RefreshBackupList();
            Broadcast();
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
            if (_swimmers.Count > 0 || _schedule.Count > 0) {
                if (MessageBox.Show("新建赛事将清除当前所有数据，是否继续？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }
            ClearAllDataAndUI();
            Broadcast();
            AddLog("已新建赛事，所有数据已清除");
        }

        /// <summary>
        /// 清除所有数据集合和所有界面显示（公共方法，供新建/清除/删除等调用）
        /// </summary>
        /// <summary>
        /// 只清除比赛数据和界面，保留赛事基本信息（名称、日期、地点、裁判等）
        /// 用于"清除当前库数据"
        /// </summary>
        private void ClearCompetitionData() {
            _swimmers.Clear();
            _relayTeams.Clear();
            _teamScores.Clear();
            _schedule.Clear();
            _records.Clear();

            if (SwimmerGrid != null) SwimmerGrid.ItemsSource = _swimmers;
            if (RelayGrid != null) RelayGrid.ItemsSource = _relayTeams;
            if (RegEventListBox != null) RegEventListBox.Items.Clear();
            if (RegStatusText != null) RegStatusText.Text = "";
            ScheduleTree.Items.Clear();
            if (ScheduleGroupedPanel != null) ScheduleGroupedPanel.Children.Clear();
            if (EditEventCombo != null) EditEventCombo.Items.Clear();
            if (EditHeatCombo != null) EditHeatCombo.Items.Clear();
            if (EditPreviewGrid != null) EditPreviewGrid.ItemsSource = null;
            if (EditAllGroupsPanel != null) EditAllGroupsPanel.Children.Clear();
            if (EditAllGroupsScroll != null) EditAllGroupsScroll.Visibility = System.Windows.Visibility.Collapsed;
            if (ResultEventCombo != null) ResultEventCombo.Items.Clear();
            if (ResultGrid != null) ResultGrid.ItemsSource = null;
            if (LaneStatusGrid != null) LaneStatusGrid.ItemsSource = null;
            if (CurrentEventText != null) CurrentEventText.Text = "-";
            if (CurrentStageText != null) CurrentStageText.Text = "-";
            if (CurrentHeatText != null) CurrentHeatText.Text = "-";
            if (RaceStateText != null) { RaceStateText.Text = "等待"; RaceStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); }
            if (RunningTimeText != null) RunningTimeText.Text = "0.0";

            // 刷新系统状态显示（保留赛事信息的模式和泳池信息）
            UpdateRaceStateDisplay();
            RefreshBackupList();
        }

        /// <summary>
        /// 清除全部数据和界面（含赛事基本信息）
        /// 用于"新建赛事"、"删除当前存档"
        /// </summary>
        private void ClearAllDataAndUI() {
            ClearCompetitionData();

            // 赛事基本信息
            _competitionName = "";
            if (CompNameBox != null) CompNameBox.Clear();
            if (CompModeCombo != null) CompModeCombo.SelectedIndex = 0;
            SetDatePicker(StartDatePicker, null);
            SetDatePicker(EndDatePicker, null);
            if (LocationBox != null) LocationBox.Text = "";
            if (OrganizerBox != null) OrganizerBox.Text = "";
            if (HostBox != null) HostBox.Text = "";
            if (TechDelegateBox != null) TechDelegateBox.Text = "";
            if (RefereeBox != null) RefereeBox.Text = "";
            if (StarterBox != null) StarterBox.Text = "";
            if (ChiefJudgeBox != null) ChiefJudgeBox.Text = "";
            if (PoolLengthCombo != null) PoolLengthCombo.SelectedIndex = 0;
            if (LaneCountCombo != null) LaneCountCombo.SelectedIndex = 0;
            if (CompModeText != null) CompModeText.Text = "";
            if (PoolInfoText != null) PoolInfoText.Text = "";
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
                    StartDate = GetDatePickerText(StartDatePicker),
                    EndDate = GetDatePickerText(EndDatePicker),
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
                SetDatePicker(StartDatePicker, package.StartDate);
                SetDatePicker(EndDatePicker, package.EndDate);
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
                if (package.Swimmers != null) {
                    foreach (var sw in package.Swimmers) {
                        // 兼容旧数据：如果没有StageAssignments，从已有数据重建
                        if (sw.StageAssignments == null || sw.StageAssignments.Count == 0) {
                            // 从当前Heat/Lane重建当前赛次的分组记录
                            if (sw.Heat > 0 && !string.IsNullOrEmpty(sw.CurrentStage)) {
                                sw.SetStageAssignment(sw.CurrentStage, sw.Heat, sw.Lane, sw.EntryTimeSeconds, sw.EntryTime);
                            }
                            // 从比赛成绩LaneResult重建历史赛次的分组记录
                            foreach (var r in sw.Results) {
                                if (r.Heat > 0 && !string.IsNullOrEmpty(r.Stage) && sw.GetAssignmentForStage(r.Stage) == null) {
                                    sw.SetStageAssignment(r.Stage, r.Heat, r.Lane, 0, "");
                                }
                            }
                        }
                        _swimmers.Add(sw);
                    }
                }
                _relayTeams.Clear();
                if (package.RelayTeams != null) {
                    foreach (var rt in package.RelayTeams) {
                        _relayTeams.Add(rt);
                        // 兼容旧数据：确保接力队在_swimmers中有对应条目
                        if (!string.IsNullOrEmpty(rt.EventName)) {
                            bool exists = false;
                            foreach (var s in _swimmers) {
                                if (s.Name == rt.TeamName && s.EventName == rt.EventName && s.Gender == rt.Gender) { exists = true; break; }
                            }
                            if (!exists) {
                                string legNames = "";
                                foreach (var leg in rt.Legs) legNames += (legNames.Length > 0 ? "," : "") + leg.SwimmerName;
                                _swimmers.Add(new Swimmer {
                                    BibNumber = "R" + _relayTeams.Count.ToString("D3"),
                                    Name = rt.TeamName,
                                    Gender = rt.Gender,
                                    Country = rt.TeamName,
                                    EventName = rt.EventName,
                                    EntryTime = rt.EntryTime,
                                    EntryTimeSeconds = rt.EntryTimeSeconds,
                                    CurrentStage = rt.Stage ?? "预赛",
                                    Heat = rt.Heat,
                                    Lane = rt.Lane,
                                    Notes = string.Format("接力队 棒次:{0}", legNames)
                                });
                            }
                        }
                    }
                }
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
        private void PrintEventResults_Click(object sender, RoutedEventArgs e) {
            var win = new EventResultPrintWindow(_swimmers, _schedule, _competitionName,
                LocationBox.Text, RefereeBox.Text, ChiefJudgeBox.Text, StarterBox.Text);
            win.Owner = this;
            win.ShowDialog();
        }
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

        // 通用文档CSS样式（参照跳水赛事系统格式）
        private static string DocCss() {
            return "body{font-family:'SimSun'; padding:0; margin:0; line-height:1.5; color:#333;} "
                + ".page{padding:50px; position:relative; box-sizing:border-box; min-height:1000px;} "
                + "h1{text-align:center; font-size:36px; font-family:'SimHei'; margin-top:10px; letter-spacing:5px;} "
                + "h2{text-align:center; font-size:28px; font-family:'SimHei'; margin-bottom:50px; letter-spacing:10px;} "
                + "h3{font-size:22px; font-family:'SimHei'; border-bottom:3px solid #1e40af; padding-bottom:8px; margin-top:40px; color:#1e40af;} "
                + "h4{font-size:18px; font-weight:bold; margin-top:20px; border-left:5px solid #1e40af; padding-left:10px;} "
                + "table{border-collapse:collapse; width:100%; margin:15px 0; background:#fff;} "
                + "th{border:1px solid #333; background:#dbeafe; padding:10px; font-weight:bold; font-size:14px;} "
                + "td{border:1px solid #333; padding:8px; text-align:center; font-size:14px;} "
                + "tr:nth-child(even){background:#f0f7ff;} "
                + ".signature-row{margin-top:60px; display:flex; justify-content:space-between; font-size:15px; font-weight:bold;} "
                + "@media print { .page-break{page-break-before:always;} body{-webkit-print-color-adjust:exact;} @page { margin: 1cm; } } ";
        }

        private string DocHeader(string subtitle) {
            return string.Format("<div class='page'><h1>{0}</h1><h2>{1}</h2>", _competitionName, subtitle);
        }

        private string DocSignatureRow() {
            return string.Format("<div class='signature-row'>"
                + "<p>裁判长：{0}</p><p>裁判：{1}</p><p>记录长：__________________</p></div>",
                !string.IsNullOrEmpty(ChiefJudgeBox.Text) ? ChiefJudgeBox.Text + "___________" : "__________________",
                !string.IsNullOrEmpty(RefereeBox.Text) ? RefereeBox.Text + "___________" : "__________________");
        }

        private string DocFooter() {
            return string.Format("</div><p style='text-align:right; padding:20px; color:gray;'>打印时间：{0}</p>",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private string BuildScheduleHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("竞 赛 日 程"));
            sb.AppendFormat("<h4>日期：{0} - {1} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{2}</h4>",
                GetDatePickerText(StartDatePicker), GetDatePickerText(EndDatePicker), LocationBox.Text);

            // 按单元分组显示
            var sessions = _schedule.GroupBy(s => s.SessionNumber).OrderBy(g => g.Key);
            foreach (var session in sessions) {
                var first = session.First();
                sb.AppendFormat("<h3>第{0}单元 {1}</h3>", session.Key, !string.IsNullOrEmpty(first.Date) ? first.Date : "");
                sb.Append("<table><tr><th width='60'>时间</th><th>项目</th><th width='70'>赛次</th><th width='50'>组数</th></tr>");
                foreach (var s in session.OrderBy(x => x.Time)) {
                    sb.AppendFormat("<tr><td>{0}</td><td>{1} {2}</td><td>{3}</td><td>{4}</td></tr>",
                        s.Time, s.Gender, s.EventName, s.Stage, s.HeatCount > 0 ? s.HeatCount.ToString() : "");
                }
                sb.Append("</table>");
            }
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildManualHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("秩 序 册"));

            // 赛事信息
            sb.Append("<h3>赛事信息</h3>");
            sb.AppendFormat("<h4>地点：{0} &nbsp;&nbsp; 日期：{1} - {2} &nbsp;&nbsp; 泳池：{3}米 {4}道</h4>",
                LocationBox.Text, GetDatePickerText(StartDatePicker), GetDatePickerText(EndDatePicker),
                _poolConfig.Length, _poolConfig.LaneCount);
            sb.Append("<table><tr><th>主办方</th><th>承办方</th><th>技术代表</th><th>裁判长</th><th>发令员</th></tr>");
            sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td></tr>",
                OrganizerBox.Text, HostBox.Text, TechDelegateBox.Text, ChiefJudgeBox.Text, StarterBox.Text);
            sb.Append("</table>");

            // 参赛队伍统计
            var teamStats = _swimmers.GroupBy(s => s.Country).OrderBy(g => g.Key);
            sb.Append("<h3>参赛队伍统计</h3>");
            sb.Append("<table><tr><th>代表队</th><th>人数</th><th>男</th><th>女</th></tr>");
            foreach (var team in teamStats) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td></tr>",
                    team.Key, team.Count(), team.Count(s => s.Gender == "男"), team.Count(s => s.Gender == "女"));
            }
            sb.Append("</table>");

            // 各项目运动员名单
            var eventGroups = _swimmers.GroupBy(s => new { s.Gender, s.EventName }).OrderBy(g => g.Key.Gender).ThenBy(g => g.Key.EventName);
            foreach (var g in eventGroups) {
                sb.AppendFormat("<h3>{0} {1}</h3>", g.Key.Gender, g.Key.EventName);
                sb.Append("<table><tr><th width='50'>号码</th><th width='80'>姓名</th><th width='80'>代表队</th><th width='70'>报名成绩</th><th width='40'>组</th><th width='40'>道</th></tr>");
                foreach (var sw in g.OrderBy(s => s.Heat).ThenBy(s => s.Lane)) {
                    sb.AppendFormat("<tr><td>{0}</td><td><b>{1}</b></td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                        sw.BibNumber, sw.Name, sw.Country, sw.EntryTime, sw.Heat > 0 ? sw.Heat.ToString() : "", sw.Lane > 0 ? sw.Lane.ToString() : "");
                }
                sb.Append("</table>");
            }
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildStartListHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());

            bool hasContent = false;

            // 按赛程顺序遍历每个赛次的每一组
            foreach (var schedItem in _schedule.OrderBy(s => s.SessionNumber).ThenBy(s => s.Time)) {
                string gender = schedItem.Gender;
                string eventName = schedItem.EventName;
                string stage = schedItem.Stage;

                // 从StageAssignments和当前赛次获取该项目该赛次所有有分组的运动员
                var assigned = new List<Tuple<Swimmer, int, int, string>>();  // (swimmer, heat, lane, seedTime)
                foreach (var s in _swimmers) {
                    if (s.Gender != gender || s.EventName != eventName) continue;
                    // 优先从StageAssignments获取
                    var sa = s.GetAssignmentForStage(stage);
                    if (sa != null && sa.Heat > 0) {
                        assigned.Add(Tuple.Create(s, sa.Heat, sa.Lane, sa.EntryTime ?? s.EntryTime ?? ""));
                        continue;
                    }
                    // 兼容旧数据
                    if (s.CurrentStage == stage && s.Heat > 0) {
                        assigned.Add(Tuple.Create(s, s.Heat, s.Lane, s.EntryTime ?? ""));
                    }
                }

                if (assigned.Count == 0) continue;

                var heatNumbers = assigned.Select(t => t.Item2).Distinct().OrderBy(h => h).ToList();
                int totalHeats = heatNumbers.Count;
                bool showHeat = (totalHeats > 1) || stage.Contains("预赛") || stage.Contains("半决赛") ;

                foreach (int heat in heatNumbers) {
                    var heatSwimmers = assigned.Where(t => t.Item2 == heat).OrderBy(t => t.Item3).ToList();
                    if (heatSwimmers.Count == 0) continue;

                    string heatDisplay = showHeat ? string.Format(" 第 {0} 组", heat) : "";
                    string eventTitle = string.Format("{0} {1} {2}{3}", gender, eventName, stage, heatDisplay);

                    if (hasContent) sb.Append("<div class='page-break'></div>");
                    sb.Append("<div class='page'>");
                    sb.AppendFormat("<h1>{0}</h1>", _competitionName);
                    sb.Append("<h2>出 发 表</h2>");
                    sb.AppendFormat("<h3>项目：{0}</h3>", eventTitle);

                    string dateTimeInfo = !string.IsNullOrEmpty(schedItem.Date) ? schedItem.Date : "";
                    if (!string.IsNullOrEmpty(schedItem.Time)) dateTimeInfo += " " + schedItem.Time;
                    if (string.IsNullOrEmpty(dateTimeInfo.Trim())) dateTimeInfo = "（时间待定）";
                    sb.AppendFormat("<h4>比赛时间：{0} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{1}</h4>", dateTimeInfo.Trim(), LocationBox.Text);

                    sb.Append("<table><tr><th width='50'>道</th><th width='60'>号码</th><th width='100'>姓名</th><th width='40'>性别</th><th width='100'>代表队</th><th width='80'>备注</th></tr>");
                    foreach (var t in heatSwimmers) {
                        var s = t.Item1;
                        string remark = "";
                        if (!string.IsNullOrEmpty(s.Status) && (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ" || s.Status == "DQ")) remark = s.Status;
                        sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td><b>{2}</b></td><td>{3}</td><td>{4}</td><td style='color:#dc2626;'>{5}</td></tr>",
                            t.Item3, s.BibNumber, s.Name, s.Gender, s.Country, remark);
                    }
                    sb.Append("</table>");
                    sb.Append(DocSignatureRow());
                    sb.Append("</div>");
                    hasContent = true;
                }
            }

            if (!hasContent) {
                sb.Append(DocHeader("出 发 表"));
                sb.Append("<p style='text-align:center; font-size:18px; margin-top:60px;'>暂无分组数据，请先进行分组编排。</p>");
                sb.Append("</div>");
            }

            sb.AppendFormat("<p style='text-align:right; padding:20px; color:gray;'>打印时间：{0}</p>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildHeatResultsHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("成 绩 单"));

            string eventTitle = string.Format("{0} {1} {2} 第 {3} 组", _currentGender, _currentEvent, _currentStage, _currentHeat);
            sb.AppendFormat("<h3>项目：{0}</h3>", eventTitle);

            var sch = _schedule.FirstOrDefault(s => s.Gender == _currentGender && s.EventName == _currentEvent && s.Stage == _currentStage);
            string dateTimeInfo = sch != null ? string.Format("{0} {1}", sch.Date, sch.Time).Trim() : "（时间待定）";
            sb.AppendFormat("<h4>比赛时间：{0} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{1}</h4>", dateTimeInfo, LocationBox.Text);

            sb.Append("<table><tr><th width='50'>名次</th><th width='40'>道</th><th width='60'>号码</th><th width='100'>姓名</th><th width='100'>代表队</th><th width='90'>成绩</th><th width='70'>反应时间</th><th width='50'>备注</th></tr>");
            var swimmers = GetCurrentHeatSwimmers().OrderBy(s => s.CurrentRank > 0 ? s.CurrentRank : int.MaxValue).ToList();
            foreach (var sw in swimmers) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (!string.IsNullOrEmpty(sw.Status) && (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ" || sw.Status == "DQ")) remark = sw.Status;
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td><b>{3}</b></td><td>{4}</td><td style='font-weight:bold; background:#eff6ff;'>{5}</td><td>{6}</td><td style='color:#dc2626;'>{7}</td></tr>",
                    r != null && r.Rank > 0 ? r.Rank.ToString() : "-",
                    sw.Lane, sw.BibNumber, sw.Name, sw.Country,
                    r != null ? r.FinalTimeDisplay : "",
                    r != null && r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F2") : "",
                    remark);
            }
            sb.Append("</table>");
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildEventResultsHtml() {
            // 已由 EventResultPrintWindow 弹窗替代，此方法保留作为备用
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("成 绩 单"));
            sb.AppendFormat("<h3>项目：{0} {1} {2}</h3>", _currentGender, _currentEvent, _currentStage);
            sb.Append("<table><tr><th>名次</th><th>号码</th><th>姓名</th><th>代表队</th><th>最终成绩</th></tr>");
            var ranking = GetEventRanking(_currentEvent, _currentGender);
            foreach (dynamic item in ranking) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td><b>{2}</b></td><td>{3}</td><td style='font-weight:bold;'>{4}</td></tr>",
                    item.rank, item.bibNumber, item.name, item.country, item.finalTime);
            }
            sb.Append("</table>");
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildFullResultBookHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}"
                + ".medal-gold td:first-child{{color:#d4af37; font-weight:bold;}} "
                + ".medal-silver td:first-child{{color:#9ca3af; font-weight:bold;}} "
                + ".medal-bronze td:first-child{{color:#b45309; font-weight:bold;}} "
                + "</style></head><body>", DocCss());

            // ═══ 封面 ═══
            sb.Append("<div class='page' style='display:flex; flex-direction:column; justify-content:center; align-items:center; min-height:900px;'>");
            sb.AppendFormat("<h1 style='font-size:48px; margin-bottom:30px;'>{0}</h1>", _competitionName);
            sb.Append("<h2 style='font-size:36px; margin-bottom:60px;'>成 绩 册</h2>");
            sb.Append("<table style='width:60%; border:none; margin-top:40px;'>");
            sb.AppendFormat("<tr><td style='border:none; font-size:20px; line-height:2; text-align:left;'><b>主办单位：</b>{0}</td></tr>", OrganizerBox.Text);
            sb.AppendFormat("<tr><td style='border:none; font-size:20px; line-height:2; text-align:left;'><b>承办单位：</b>{0}</td></tr>", HostBox.Text);
            sb.AppendFormat("<tr><td style='border:none; font-size:20px; line-height:2; text-align:left;'><b>比赛地点：</b>{0}</td></tr>", LocationBox.Text);
            sb.AppendFormat("<tr><td style='border:none; font-size:20px; line-height:2; text-align:left;'><b>比赛时间：</b>{0} - {1}</td></tr>",
                GetDatePickerText(StartDatePicker), GetDatePickerText(EndDatePicker));
            sb.Append("</table></div><div class='page-break'></div>");

            // ═══ 奖牌榜统计 ═══
            sb.Append("<div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", _competitionName);
            sb.Append("<h3>全场奖牌榜统计</h3>");
            sb.Append("<table><tr><th width='10%'>排名</th><th>代表队</th><th width='12%'>金牌</th><th width='12%'>银牌</th><th width='12%'>铜牌</th><th width='12%'>总计</th></tr>");

            var medalTable = new Dictionary<string, int[]>();
            // 统计各项目决赛前3名的奖牌
            var eventGroups = _swimmers.GroupBy(s => new { s.Gender, s.EventName });
            foreach (var ev in eventGroups) {
                var finalists = ev.Where(s => s.GetResultForStage("决赛") != null && s.GetResultForStage("决赛").FinalTime > 0)
                    .OrderBy(s => s.GetResultForStage("决赛").FinalTime).Take(3).ToList();
                for (int i = 0; i < finalists.Count; i++) {
                    string country = finalists[i].Country ?? "未知";
                    if (!medalTable.ContainsKey(country)) medalTable[country] = new int[3];
                    medalTable[country][i]++;
                }
            }
            var sortedMedals = medalTable.OrderByDescending(x => x.Value[0] * 10000 + x.Value[1] * 100 + x.Value[2]).ToList();
            for (int i = 0; i < sortedMedals.Count; i++) {
                var m = sortedMedals[i];
                sb.AppendFormat("<tr><td>{0}</td><td style='text-align:left; padding-left:20px;'><b>{1}</b></td><td>{2}</td><td>{3}</td><td>{4}</td><td style='font-weight:bold;'>{5}</td></tr>",
                    i + 1, m.Key, m.Value[0], m.Value[1], m.Value[2], m.Value[0] + m.Value[1] + m.Value[2]);
            }
            sb.Append("</table></div><div class='page-break'></div>");

            // ═══ 按赛程顺序逐场打印成绩 ═══
            foreach (var schedItem in _schedule.OrderBy(s => s.SessionNumber).ThenBy(s => s.Time)) {
                string gender = schedItem.Gender;
                string eventName = schedItem.EventName;
                string stage = schedItem.Stage;

                // 查找该场比赛中有成绩的运动员
                var matchedSwimmers = _swimmers.Where(s =>
                    s.Gender == gender && s.EventName == eventName &&
                    s.GetResultForStage(stage) != null && s.GetResultForStage(stage).FinalTime > 0
                ).ToList();

                if (matchedSwimmers.Count == 0) continue;

                // 获取该赛次各组
                var heatNumbers = matchedSwimmers.Select(s => s.GetResultForStage(stage).Heat).Distinct().OrderBy(h => h).ToList();

                foreach (int heat in heatNumbers) {
                    var heatSwimmers = matchedSwimmers.Where(s => s.GetResultForStage(stage).Heat == heat)
                        .OrderBy(s => s.GetResultForStage(stage).FinalTime).ToList();

                    if (heatSwimmers.Count == 0) continue;

                    // 组号显示逻辑：决赛只有1组时不显示，预赛/半决赛即使1组也显示
                    bool showHeat = (heatNumbers.Count > 1) || stage.Contains("预赛") || stage.Contains("半决赛") ;
                    string heatDisplay = showHeat ? string.Format(" 第 {0} 组", heat) : "";
                    string eventTitle = string.Format("{0} {1} {2}{3}", gender, eventName, stage, heatDisplay);

                    sb.Append("<div class='page'>");
                    sb.AppendFormat("<h1>{0}</h1>", _competitionName);
                    sb.AppendFormat("<h3>项目：{0}</h3>", eventTitle);

                    // 比赛时间和地点
                    string dateTimeInfo = !string.IsNullOrEmpty(schedItem.Date) ? schedItem.Date : "";
                    if (!string.IsNullOrEmpty(schedItem.Time)) dateTimeInfo += " " + schedItem.Time;
                    if (string.IsNullOrEmpty(dateTimeInfo.Trim())) dateTimeInfo = "（时间待定）";
                    sb.AppendFormat("<h4>比赛时间：{0} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{1}</h4>", dateTimeInfo.Trim(), LocationBox.Text);

                    // 成绩表
                    sb.Append("<table><tr><th width='50'>名次</th><th width='40'>道</th><th width='60'>号码</th>");
                    sb.Append("<th width='100'>姓名</th><th width='100'>代表队</th>");
                    sb.Append("<th width='90'>成绩</th><th width='70'>反应时间</th><th width='50'>备注</th></tr>");

                    int rank = 1;
                    foreach (var sw in heatSwimmers) {
                        var r = sw.GetResultForStage(stage);
                        string rowBg = "";
                        if (stage == "决赛" && !showHeat) {
                            if (rank == 1) rowBg = " style='background:#fef3c7;'";
                            else if (rank == 2) rowBg = " style='background:#f1f5f9;'";
                            else if (rank == 3) rowBg = " style='background:#fef0e7;'";
                        }
                        string remark = "";
                        if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                        else if (!string.IsNullOrEmpty(sw.Status) && (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ" || sw.Status == "DQ")) remark = sw.Status;
                        sb.AppendFormat("<tr{0}><td>{1}</td><td>{2}</td><td>{3}</td>",
                            rowBg, rank, r.Lane, sw.BibNumber);
                        sb.AppendFormat("<td><b>{0}</b></td><td>{1}</td>", sw.Name, sw.Country);
                        sb.AppendFormat("<td style='font-weight:bold; background:#eff6ff;'>{0}</td>", TimeFormatter.Format(r.FinalTime));
                        sb.AppendFormat("<td>{0}</td>", r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F2") : "");
                        sb.AppendFormat("<td style='color:#dc2626;'>{0}</td></tr>", remark);
                        rank++;
                    }
                    sb.Append("</table>");
                    sb.Append(DocSignatureRow());
                    sb.Append("</div><div class='page-break'></div>");
                }
            }

            sb.AppendFormat("<p style='text-align:right; padding:20px; color:gray;'>打印时间：{0}</p>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildTeamStandingsHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("团 体 成 绩"));
            sb.AppendFormat("<h4>日期：{0} - {1} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{2}</h4>",
                GetDatePickerText(StartDatePicker), GetDatePickerText(EndDatePicker), LocationBox.Text);

            sb.Append("<table><tr><th width='50'>名次</th><th width='100'>代表队</th><th width='70'>总分</th><th width='70'>个人分</th><th width='70'>接力分</th><th width='80'>破纪录加分</th><th width='40'>金</th><th width='40'>银</th><th width='40'>铜</th></tr>");
            foreach (var ts in _teamScores.OrderBy(t => t.Rank)) {
                string medalStyle = "";
                if (ts.Rank == 1) medalStyle = " style='background:#fef3c7;'";
                else if (ts.Rank == 2) medalStyle = " style='background:#f1f5f9;'";
                else if (ts.Rank == 3) medalStyle = " style='background:#fef0e7;'";
                sb.AppendFormat("<tr{0}><td>{1}</td><td><b>{2}</b></td><td style='font-weight:bold;'>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td><td>{7}</td><td>{8}</td><td>{9}</td></tr>",
                    medalStyle, ts.Rank, ts.TeamName, ts.TotalPoints, ts.IndividualPoints, ts.RelayPoints,
                    ts.RecordBonusPoints, ts.GoldCount, ts.SilverCount, ts.BronzeCount);
            }
            sb.Append("</table>");
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildRecordReportHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("纪 录 报 告"));
            sb.AppendFormat("<h4>日期：{0} - {1} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{2}</h4>",
                GetDatePickerText(StartDatePicker), GetDatePickerText(EndDatePicker), LocationBox.Text);

            sb.Append("<table><tr><th>项目</th><th>类型</th><th>保持者</th><th>代表队</th><th>成绩</th><th>日期</th><th>地点</th></tr>");
            foreach (var r in _records) {
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td><b>{2}</b></td><td>{3}</td><td style='font-weight:bold;'>{4}</td><td>{5}</td><td>{6}</td></tr>",
                    r.EventName, r.RecordType, r.HolderName, r.HolderCountry, TimeFormatter.Format(r.Time), r.Date, r.Location);
            }
            sb.Append("</table>");
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildAwardCertificateHtml() {
            // 查找决赛前3名
            var finalists = _swimmers.Where(s => s.CurrentStage == "决赛" && s.Results.Any(r => r.Stage == "决赛" && r.FinalTime > 0))
                .GroupBy(s => new { s.Gender, s.EventName });

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='UTF-8'><style>");
            sb.Append("body{font-family:'SimSun',serif;margin:0;padding:0;}");
            sb.Append(".cert-page{width:210mm;min-height:297mm;margin:0 auto;box-sizing:border-box;");
            sb.Append("padding:24mm 22mm 22mm;border:10px double #cc0000;position:relative;page-break-after:always;}");
            sb.Append(".cert-title{font-size:86px;font-family:'SimHei';text-align:center;letter-spacing:40px;color:#cc0000;margin-bottom:18px;}");
            sb.Append(".cert-divider{border:none;border-top:3px solid #cc0000;margin:0 0 28px;}");
            sb.Append(".cert-body{font-size:22px;line-height:2.8;color:#111;}");
            sb.Append(".cert-name{font-size:26px;font-weight:bold;color:#1a0000;text-decoration:underline;text-underline-offset:5px;}");
            sb.Append(".cert-comp-name{font-weight:bold;color:#1a3a6e;text-decoration:underline;text-underline-offset:5px;}");
            sb.Append(".cert-event-name{font-weight:bold;text-decoration:underline;text-underline-offset:5px;}");
            sb.Append(".cert-text{text-indent:2em;}");
            sb.Append(".cert-rank{font-size:26px;font-weight:bold;color:#cc0000;}");
            sb.Append(".cert-fields{margin-top:36px;font-size:20px;line-height:2.8;}");
            sb.Append(".cert-field{display:flex;align-items:baseline;margin-bottom:4px;}");
            sb.Append(".field-label{white-space:nowrap;color:#222;min-width:140px;font-weight:bold;}");
            sb.Append(".field-value{flex:1;border-bottom:1px solid #555;padding-left:10px;min-width:160px;color:#111;}");
            sb.Append(".field-blank{flex:1;border-bottom:1px solid #555;min-width:160px;}");
            sb.Append(".cert-date{text-align:right;font-size:20px;margin-top:20px;color:#333;letter-spacing:3px;}");
            sb.Append("@media print{.cert-page{-webkit-print-color-adjust:exact;}@page{size:A4;margin:0;}}");
            sb.Append("</style></head><body>");

            string[] rankNames = { "冠军", "亚军", "季军", "第四名", "第五名", "第六名", "第七名", "第八名" };
            int certCount = 0;

            foreach (var g in finalists) {
                var ranked = g.OrderBy(s => {
                    var r = s.GetResultForStage("决赛");
                    return r != null ? r.FinalTime : double.MaxValue;
                }).Take(3).ToList();

                for (int i = 0; i < ranked.Count; i++) {
                    var sw = ranked[i];
                    string rk = i < rankNames.Length ? rankNames[i] : string.Format("第{0}名", i + 1);

                    sb.Append("<div class='cert-page'>");
                    sb.Append("<div class='cert-title'>奖&nbsp;&nbsp;状</div>");
                    sb.Append("<hr class='cert-divider'/>");
                    sb.Append("<div class='cert-body'>");
                    sb.AppendFormat("<div style='font-size:24px;'><span class='cert-name'>{0}</span>：</div>", sw.Name);
                    sb.Append("<div class='cert-text'>");
                    sb.AppendFormat("在&nbsp;<span class='cert-comp-name'>{0}</span>&nbsp;", _competitionName);
                    sb.AppendFormat("<span class='cert-event-name'>{0} {1}</span>&nbsp;项目比赛中，", g.Key.Gender, g.Key.EventName);
                    sb.AppendFormat("表现优异，荣获<span class='cert-rank'>{0}</span>，特发此证，以资鼓励。", rk);
                    sb.Append("</div></div>");
                    sb.Append("<div class='cert-fields'>");
                    sb.AppendFormat("<div class='cert-field'><span class='field-label'>参赛单位：</span><span class='field-value'>{0}</span></div>", sw.Country ?? "");
                    sb.AppendFormat("<div class='cert-field'><span class='field-label'>裁判长签字：</span><span class='field-value'>{0}</span></div>", ChiefJudgeBox.Text ?? "");
                    sb.Append("<div class='cert-field'><span class='field-label'>赛事组委会盖章：</span><span class='field-blank'>&nbsp;</span></div>");
                    string dateStr = GetDatePickerText(StartDatePicker);
                    DateTime dt;
                    if (DateTime.TryParse(dateStr, out dt))
                        sb.AppendFormat("<div class='cert-date'>日期：{0}&nbsp;年&nbsp;{1}&nbsp;月&nbsp;{2}&nbsp;日</div>", dt.Year, dt.Month, dt.Day);
                    sb.Append("</div></div>");
                    certCount++;
                }
            }

            if (certCount == 0) {
                sb.Append("<div class='cert-page'><p style='text-align:center;font-size:24px;margin-top:200px;'>暂无决赛成绩，无法生成奖状</p></div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildRecordCertificateHtml() {
            // 查找本次比赛中破纪录的运动员
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset='UTF-8'><style>");
            sb.Append("body{font-family:'SimSun',serif;margin:0;padding:0;}");
            sb.Append(".cert-page{width:210mm;min-height:297mm;margin:0 auto;box-sizing:border-box;");
            sb.Append("padding:24mm 22mm;border:10px double #1a3a6e;position:relative;page-break-after:always;}");
            sb.Append(".cert-title{font-size:54px;font-family:'SimHei';text-align:center;letter-spacing:18px;color:#1a3a6e;margin-bottom:18px;}");
            sb.Append(".cert-divider{border:none;border-top:3px solid #1a3a6e;margin:0 0 28px;}");
            sb.Append(".cert-body{font-size:22px;line-height:2.6;color:#111;}");
            sb.Append(".cert-name{font-size:26px;font-weight:bold;color:#1a0000;text-decoration:underline;text-underline-offset:5px;}");
            sb.Append(".cert-text{text-indent:2em;}");
            sb.Append(".cert-comp-name{font-weight:bold;color:#1a3a6e;text-decoration:underline;text-underline-offset:4px;}");
            sb.Append(".cert-event-name{font-weight:bold;text-decoration:underline;text-underline-offset:4px;}");
            sb.Append(".cert-score{font-size:24px;font-weight:bold;color:#cc0000;text-decoration:underline;text-underline-offset:4px;}");
            sb.Append(".cert-record-type{font-weight:bold;color:#1a3a6e;text-decoration:underline;text-underline-offset:4px;}");
            sb.Append(".cert-fields{margin-top:40px;font-size:20px;line-height:2.8;}");
            sb.Append(".cert-field{display:flex;align-items:baseline;margin-bottom:4px;}");
            sb.Append(".field-label{white-space:nowrap;color:#222;font-weight:bold;}");
            sb.Append(".field-value{flex:1;border-bottom:1px solid #555;padding-left:10px;min-width:120px;color:#111;}");
            sb.Append(".field-blank{flex:1;border-bottom:1px solid #555;min-width:120px;}");
            sb.Append(".cert-date{text-align:right;font-size:20px;margin-top:20px;color:#333;letter-spacing:3px;}");
            sb.Append("@media print{.cert-page{-webkit-print-color-adjust:exact;}@page{size:A4;margin:0;}}");
            sb.Append("</style></head><body>");

            // 这里暂用占位，可通过 RecordCertificateWindow 弹窗填入具体信息
            sb.Append("<div class='cert-page'>");
            sb.Append("<div class='cert-title'>破&nbsp;纪&nbsp;录&nbsp;证&nbsp;书</div>");
            sb.Append("<hr class='cert-divider'/>");
            sb.Append("<div class='cert-body'>");
            sb.Append("<div style='font-size:24px;'><span class='cert-name'>__________________</span>：</div>");
            sb.Append("<div class='cert-text'>");
            sb.AppendFormat("在&nbsp;<span class='cert-comp-name'>{0}</span>&nbsp;", _competitionName);
            sb.Append("<span class='cert-event-name'>__________________</span>&nbsp;项目比赛中，");
            sb.Append("凭借卓越的竞技水平，以<span class='cert-score'>__________________</span>的优异成绩，");
            sb.Append("打破<span class='cert-record-type'>__________________</span>，");
            sb.Append("特发此证，以表彰其杰出成就。");
            sb.Append("</div></div>");
            sb.Append("<div class='cert-fields'>");
            sb.AppendFormat("<div class='cert-field'><span class='field-label'>裁判长签字：</span><span class='field-value'>{0}</span></div>", ChiefJudgeBox.Text ?? "");
            sb.Append("<div class='cert-field'><span class='field-label'>赛事组委会盖章：</span><span class='field-blank'>&nbsp;</span></div>");
            string dateStr = GetDatePickerText(StartDatePicker);
            DateTime dt;
            if (DateTime.TryParse(dateStr, out dt))
                sb.AppendFormat("<div class='cert-date'>日期：{0}&nbsp;年&nbsp;{1}&nbsp;月&nbsp;{2}&nbsp;日</div>", dt.Year, dt.Month, dt.Day);
            sb.Append("</div></div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private string BuildSplitTimeReportHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("分 段 计 时 报 告"));

            string eventTitle = string.Format("{0} {1} {2} 第 {3} 组", _currentGender, _currentEvent, _currentStage, _currentHeat);
            sb.AppendFormat("<h3>项目：{0}</h3>", eventTitle);

            var sch = _schedule.FirstOrDefault(s => s.Gender == _currentGender && s.EventName == _currentEvent && s.Stage == _currentStage);
            string dateTimeInfo = sch != null ? string.Format("{0} {1}", sch.Date, sch.Time).Trim() : "（时间待定）";
            sb.AppendFormat("<h4>比赛时间：{0} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{1}</h4>", dateTimeInfo, LocationBox.Text);

            foreach (var sw in GetCurrentHeatSwimmers().OrderBy(s => s.Lane)) {
                var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                if (result == null || result.Splits.Count == 0) continue;
                sb.AppendFormat("<h4 style='margin-top:30px;'>泳道 {0} &nbsp; {1} （{2}） &nbsp; 最终成绩：{3}</h4>",
                    sw.Lane, sw.Name, sw.Country, TimeFormatter.Format(result.FinalTime));
                sb.Append("<table><tr><th width='50'>段</th><th width='70'>距离</th><th width='90'>分段时间</th><th width='90'>累计时间</th></tr>");
                foreach (var split in result.Splits) {
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}m</td><td>{2}</td><td style='font-weight:bold;'>{3}</td></tr>",
                        split.Lap, split.Distance, TimeFormatter.Format(split.Time), TimeFormatter.Format(split.CumulativeTime));
                }
                sb.Append("</table>");
            }
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
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

                        // 如果删除的是当前正在使用的比赛，同步清除内存数据和界面
                        if (selected.Name == _competitionName) {
                            ClearAllDataAndUI();
                            Broadcast();
                            AddLog(string.Format("当前比赛 [{0}] 的存档已删除，界面数据已清除", _competitionName));
                        }
                    }
                } catch (Exception ex) {
                    MessageBox.Show("删除失败: " + ex.Message);
                }
            }
        }

        private void ClearDatabase_Click(object sender, RoutedEventArgs e) {
            if (MessageBox.Show(
                string.Format("严重警告：确定要清空当前比赛 [{0}] 的所有数据吗？\n\n运动员、赛程、成绩将全部清除！\n赛事基本信息（名称、日期、地点、裁判等）将保留。", _competitionName),
                "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) {
                ClearCompetitionData();
                AutoSaveData();
                Broadcast();
                AddLog(string.Format("比赛 [{0}] 的数据已清空（赛事信息保留）", _competitionName));
                MessageBox.Show(string.Format("比赛 [{0}] 的数据已成功清空。\n赛事基本信息已保留。", _competitionName));
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
