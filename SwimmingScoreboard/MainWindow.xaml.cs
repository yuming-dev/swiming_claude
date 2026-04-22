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
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;
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
        private int _selectedLane = -1;
        private int _lastTsSplitCount = -1;
        private string _firstPlaceFinishTime = "";
        private DateTime _firstPlaceShowStart = DateTime.MinValue;
        private int _firstPlaceDetectedRank = 0;
        // 泳��设备状态
        private List<LaneDeviceState> _laneDeviceStates = new List<LaneDeviceState>();

        // 原始计时数据记录
        private StringBuilder _rawTimingLog = new StringBuilder();

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
            LoadTimingSettings();
            LoadLastCompetition();
            PopulateComPorts();
            UpdateConnectionStatus();
            _initialized = true;
            RefreshBackupList();
            UpdateEditHeatCombo();
            UpdateResultHeatCombo();
            RefreshResultGrid();
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
            RebuildRelayGroupedView();
            // ScheduleGrid replaced by ScheduleGroupedPanel
            RecordGrid.ItemsSource = _records;
            RefreshRecordFilterCombos();

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
                    case "CHECKIN_IDENTITY":
                        AddLog("检录台已连接");
                        break;
                    case "GET_HEAT_SWIMMERS":
                        HandleGetHeatSwimmers(socket, msg);
                        break;
                    case "SAVE_CHECKIN":
                        HandleSaveCheckin(msg);
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
                    case "CONNECT_HW":
                        HandleConnectHw(msg);
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

        // ═══════════════════════════════════════════════════════════════
        // 检录台支持
        // ═══════════════════════════════════════════════════════════════

        // 查询指定组运动员（不改变服务器当前组状态；只回复给请求者）
        private void HandleGetHeatSwimmers(IWebSocketConnection socket, JObject msg) {
            var data = msg["data"];
            if (data == null) return;
            string gender = data["gender"] != null ? data["gender"].ToString() : "";
            string eventName = data["eventName"] != null ? data["eventName"].ToString() : "";
            string stage = data["stage"] != null ? data["stage"].ToString() : "";
            int heat = data["heat"] != null ? (int)data["heat"] : 0;

            bool isRelay = eventName.Contains("接力");
            var heatSwimmers = _swimmers.Where(s =>
                s.Gender == gender && s.EventName == eventName &&
                !(isRelay && s.Notes != null && s.Notes.StartsWith("接力队员"))
            ).ToList();

            var list = new List<object>();
            foreach (var s in heatSwimmers) {
                var sa = s.GetAssignmentForStage(stage);
                int lane = 0;
                string entryTime = s.EntryTime ?? "";
                if (sa != null && sa.Heat == heat) { lane = sa.Lane; if (!string.IsNullOrEmpty(sa.EntryTime)) entryTime = sa.EntryTime; }
                else if (s.CurrentStage == stage && s.Heat == heat) { lane = s.Lane; }
                else continue;

                // 接力队员列表（按棒次顺序）
                var memberList = new List<object>();
                if (isRelay) {
                    // 优先从 RelayTeam.Legs 按棒次顺序取
                    var relayTeam = _relayTeams.FirstOrDefault(rt =>
                        rt.TeamName == s.Country && rt.EventName == s.EventName && rt.Gender == s.Gender);
                    if (relayTeam != null && relayTeam.Legs != null && relayTeam.Legs.Count > 0) {
                        var orderedLegs = relayTeam.Legs.OrderBy(l => l.LegOrder).ToList();
                        foreach (var leg in orderedLegs) {
                            // 通过 bibNumber 或 name 在 _swimmers 中找到该队员
                            var mem = _swimmers.FirstOrDefault(m =>
                                m.Notes != null && m.Notes.StartsWith("接力队员")
                                && m.EventName == s.EventName && m.Country == s.Country
                                && ((!string.IsNullOrEmpty(leg.SwimmerBibNumber) && m.BibNumber == leg.SwimmerBibNumber)
                                    || (!string.IsNullOrEmpty(leg.SwimmerName) && m.Name == leg.SwimmerName)));
                            // 身份证号：优先取 Swimmer 记录；否则取 RelayLeg 存储的值
                            string memIdNum = (mem != null && !string.IsNullOrEmpty(mem.IDNumber))
                                ? mem.IDNumber : (leg.SwimmerIDNumber ?? "");
                            memberList.Add(new {
                                legOrder = leg.LegOrder,
                                bibNumber = mem != null ? (mem.BibNumber ?? "") : (leg.SwimmerBibNumber ?? ""),
                                name = mem != null ? (mem.Name ?? "") : (leg.SwimmerName ?? ""),
                                idNumber = memIdNum,
                                status = mem != null ? (mem.Status ?? "") : ""
                            });
                        }
                    } else {
                        // 兼容：没有 RelayTeam 数据时，按 _swimmers 直接过滤
                        var members = _swimmers.Where(m => m.Notes != null && m.Notes.StartsWith("接力队员")
                            && m.Country == s.Country && m.EventName == s.EventName).ToList();
                        int idx = 1;
                        foreach (var m in members) {
                            memberList.Add(new {
                                legOrder = idx++,
                                bibNumber = m.BibNumber ?? "",
                                name = m.Name ?? "",
                                idNumber = m.IDNumber ?? "",
                                status = m.Status ?? ""
                            });
                        }
                    }
                }

                list.Add(new {
                    lane = lane,
                    bibNumber = s.BibNumber ?? "",
                    name = s.Name ?? "",
                    idNumber = s.IDNumber ?? "",
                    country = s.Country ?? "",
                    team = s.Country ?? "",
                    age = s.Age,
                    ageCategory = s.AgeCategory ?? "",
                    entryTime = entryTime,
                    status = s.Status ?? "",
                    members = memberList,
                    isRelay = isRelay
                });
            }
            list = list.OrderBy(o => ((dynamic)o).lane).ToList();

            try {
                var reply = new {
                    type = "HEAT_SWIMMERS",
                    data = new { gender, eventName, stage, heat, isRelay, swimmers = list }
                };
                socket.Send(JsonConvert.SerializeObject(reply));
            } catch { }
        }

        // 检录表确认：批量更新状态（DNS 等），保存并广播
        // statuses 支持两种键：bibNumber（首选，兼容接力队员）或 lane（个人项目兼容）
        private void HandleSaveCheckin(JObject msg) {
            var data = msg["data"];
            if (data == null) return;
            string gender = data["gender"] != null ? data["gender"].ToString() : "";
            string eventName = data["eventName"] != null ? data["eventName"].ToString() : "";
            string stage = data["stage"] != null ? data["stage"].ToString() : "";
            int heat = data["heat"] != null ? (int)data["heat"] : 0;
            var statuses = data["statuses"] as JArray;
            if (statuses == null) return;

            int updated = 0;
            foreach (JObject st in statuses.Cast<JObject>()) {
                string bib = st["bibNumber"] != null ? st["bibNumber"].ToString() : "";
                int lane = st["lane"] != null ? (int)st["lane"] : 0;
                string newStatus = st["status"] != null ? st["status"].ToString() : "";

                Swimmer target = null;
                // 首选按 bibNumber 精准匹配（个人 + 接力队员通用）
                if (!string.IsNullOrEmpty(bib)) {
                    target = _swimmers.FirstOrDefault(s =>
                        s.BibNumber == bib && s.EventName == eventName && s.Gender == gender);
                }
                // 回退按泳道匹配（仅针对当前组的非接力队员代表条目）
                if (target == null && lane > 0) {
                    target = _swimmers.FirstOrDefault(s => {
                        if (s.Gender != gender || s.EventName != eventName) return false;
                        if (s.Notes != null && s.Notes.StartsWith("接力队员")) return false;
                        var sa = s.GetAssignmentForStage(stage);
                        if (sa != null && sa.Heat == heat && sa.Lane == lane) return true;
                        if (s.CurrentStage == stage && s.Heat == heat && s.Lane == lane) return true;
                        return false;
                    });
                }
                if (target != null && (target.Status ?? "") != newStatus) {
                    target.Status = newStatus;
                    updated++;
                }
            }

            AddLog(string.Format("检录: {0} {1} {2} 第{3}组 已保存{4}条状态",
                gender, eventName, stage, heat, updated));
            AutoSaveData();
            UpdateLaneStatusDisplay();
            Broadcast();
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
            RefreshOverviewStats();
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
            RefreshOverviewStats();
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
                        SwimmerBibNumber = leg["swimmerBibNumber"] != null ? leg["swimmerBibNumber"].ToString() : "",
                        SwimmerIDNumber = leg["swimmerIDNumber"] != null ? leg["swimmerIDNumber"].ToString() : ""
                    });
                }
            }
            // 自动匹配队员号码/身份证：从已注册运动员中查找
            foreach (var leg in team.Legs) {
                if (string.IsNullOrEmpty(leg.SwimmerName)) continue;
                var match = _swimmers.FirstOrDefault(s => s.Name == leg.SwimmerName && s.Country == team.TeamName);
                if (match != null) {
                    if (string.IsNullOrEmpty(leg.SwimmerBibNumber) && !string.IsNullOrEmpty(match.BibNumber))
                        leg.SwimmerBibNumber = match.BibNumber;
                    if (string.IsNullOrEmpty(leg.SwimmerIDNumber) && !string.IsNullOrEmpty(match.IDNumber))
                        leg.SwimmerIDNumber = match.IDNumber;
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

            // 为每位队员创建/更新 Swimmer 子条目（用于存储身份证并承载检录状态）
            foreach (var leg in team.Legs) {
                if (string.IsNullOrEmpty(leg.SwimmerName)) continue;
                // 队员条目的 BibNumber 取自 RelayLeg.SwimmerBibNumber（若无则生成 teamBib-legN）
                string memBib = !string.IsNullOrEmpty(leg.SwimmerBibNumber)
                    ? leg.SwimmerBibNumber : bibNumber + "-" + leg.LegOrder;

                var memExisting = _swimmers.FirstOrDefault(s =>
                    s.Notes != null && s.Notes.StartsWith("接力队员")
                    && s.EventName == team.EventName && s.Country == team.TeamName
                    && (s.Name == leg.SwimmerName || s.BibNumber == memBib));
                if (memExisting != null) {
                    // 补充/更新身份证号（若新数据非空）
                    if (!string.IsNullOrEmpty(leg.SwimmerIDNumber))
                        memExisting.IDNumber = leg.SwimmerIDNumber;
                    if (string.IsNullOrEmpty(memExisting.BibNumber)) memExisting.BibNumber = memBib;
                } else {
                    _swimmers.Add(new Swimmer {
                        BibNumber = memBib,
                        Name = leg.SwimmerName,
                        Gender = team.Gender == "混合" ? "男" : team.Gender, // 混合默认标男，可后续编辑
                        Country = team.TeamName,
                        IDNumber = leg.SwimmerIDNumber ?? "",
                        EventName = team.EventName,
                        Notes = string.Format("接力队员 {0} 第{1}棒", team.EventName, leg.LegOrder)
                    });
                }
            }

            AddLog(string.Format("注册接力队: {0} ({1}) {2}人 [{3}]", team.TeamName, team.EventName, team.Legs.Count, legNames));
            RebuildRelayGroupedView();
            AutoSaveData();
            Broadcast();
        }

        private void HandleTimingCommand(JObject msg) {
            string cmd = msg["command"] != null ? msg["command"].ToString() : "";
            var data = msg["data"];

            switch (cmd) {
                case "READY":
                    if (_timingBridge != null && _timingBridge.IsConnected) _timingBridge.SendCommand(0x21);
                    Ready_Click(null, null);
                    break;
                case "START_RACE":
                    if (_timingBridge != null && _timingBridge.IsConnected) _timingBridge.SendCommand(0x1C);
                    StartRace_Click(null, null);
                    break;
                case "RESTART":
                case "TIMER_RESET":
                    if (_timingBridge != null && _timingBridge.IsConnected) _timingBridge.SendCommand(0x20);
                    Restart_Click(null, null);
                    break;
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
                case "PUBLISH_RESULT":
                    AddLog("收到PUBLISH_RESULT命令");
                    if (data != null) {
                        string prGender = data["gender"] != null ? data["gender"].ToString() : "";
                        string prEvent = data["eventName"] != null ? data["eventName"].ToString() : "";
                        string prStage = data["stage"] != null ? data["stage"].ToString() : "";
                        int prHeat = data["heat"] != null ? (int)data["heat"] : 1;
                        PublishResultToDisplay(prGender, prEvent, prStage, prHeat);
                    }
                    break;
                case "AUTO_GENERATE_HEATS": AutoGenerateHeats_Click(null, null); break;
                case "NEXT_HEAT":
                    if (_raceState == RaceState.Ready || _raceState == RaceState.Racing) {
                        AddLog("比赛进行中不能切换到下一组"); break;
                    }
                    NextHeat_Click(null, null); break;
                case "PREV_HEAT":
                    if (_raceState == RaceState.Ready || _raceState == RaceState.Racing) {
                        AddLog("比赛进行中不能切换到上一组"); break;
                    }
                    PrevHeat_Click(null, null); break;
                case "SET_GENDER":
                    if (_raceState == RaceState.Ready || _raceState == RaceState.Racing) {
                        AddLog("比赛进行中不能切换项目"); break;
                    }
                    if (data != null) {
                        _currentGender = data.ToString();
                        AddLog("设置性别: " + _currentGender);
                    }
                    break;
                case "SET_EVENT":
                    if (_raceState == RaceState.Ready || _raceState == RaceState.Racing) {
                        AddLog("比赛进行中不能切换项目"); break;
                    }
                    if (data != null) SetCurrentEvent(data.ToString());
                    break;
                case "SET_STAGE":
                    if (_raceState == RaceState.Ready || _raceState == RaceState.Racing) {
                        AddLog("比赛进行中不能切换赛次"); break;
                    }
                    if (data != null) SetCurrentStage(data.ToString());
                    break;
                case "SET_HEAT":
                    if (_raceState == RaceState.Ready || _raceState == RaceState.Racing) {
                        AddLog("比赛进行中不能切换组次"); break;
                    }
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
                case "CANCEL_NOTE":
                    if (data != null) CancelLaneNote((int)data["lane"]);
                    break;
                case "USE_BLIND_RESULT":
                    if (data != null) UseBlindResultForCurrentSegment((int)data["lane"]);
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
                case "SET_MANUAL_STATUS":
                    if (data != null) {
                        int msLane = (int)data["lane"];
                        bool msLeft = data["leftEnabled"] != null && (bool)data["leftEnabled"];
                        bool msRight = data["rightEnabled"] != null && (bool)data["rightEnabled"];
                        var msState = _laneDeviceStates.FirstOrDefault(s => s.Lane == msLane);
                        if (msState != null) {
                            msState.LeftManualEnabled = msLeft;
                            msState.RightManualEnabled = msRight;
                            if (!msLeft) msState.LeftManualStatus = DeviceStatus.Closed;
                            if (!msRight) msState.RightManualStatus = DeviceStatus.Closed;
                        }
                        AddLog(string.Format("泳道{0} 手动按键: 左={1} 右={2}", msLane, msLeft ? "启用" : "禁用", msRight ? "启用" : "禁用"));
                    }
                    break;
                case "SET_LANE_CLOSE_SETTINGS":
                    if (data != null) {
                        if (data["laneCloseTime"] != null) _laneCloseSettings.LaneCloseTime = (double)data["laneCloseTime"];
                        if (data["startBlockCloseDelay"] != null) _laneCloseSettings.StartBlockCloseDelay = (double)data["startBlockCloseDelay"];
                        if (data["resultConfirmCloseDelay"] != null) _laneCloseSettings.ResultConfirmCloseDelay = (double)data["resultConfirmCloseDelay"];
                        if (data["falseStartThreshold"] != null) _laneCloseSettings.FalseStartThreshold = (double)data["falseStartThreshold"];
                        if (data["splitDisplayTime"] != null) _laneCloseSettings.SplitDisplayTime = (double)data["splitDisplayTime"];
                        if (data["startPosition"] != null) {
                            string pos = data["startPosition"].ToString();
                            _laneCloseSettings.FinishPosition = pos;  // 参数设置的是终点位置（固定）
                            _laneCloseSettings.StartPosition = pos;   // 默认发令位置=终点位置（非50米时）
                            AutoAdjustStartPosition();                // 根据当前项目自动调整
                            // 重置出发台状态到正确一端
                            if (_raceState == RaceState.Waiting || _raceState == RaceState.Ready) {
                                foreach (var st in _laneDeviceStates) st.ResetForNewRace(_laneCloseSettings.StartPosition);
                            }
                        }
                        // 同步全局设置到所有泳道（清除每道独立值，使用全局值）
                        foreach (var st in _laneDeviceStates) st.LaneCloseTime = 0;
                        if (data["firstPlaceHoldTime"] != null) _laneCloseSettings.FirstPlaceHoldTime = (double)data["firstPlaceHoldTime"];
                        AddLog(string.Format("参数更新: 关闭{0}s 出发台{1}s 确认{2}s 抢跳{3}s 分段{4}s 终点:{5}",
                            _laneCloseSettings.LaneCloseTime, _laneCloseSettings.StartBlockCloseDelay,
                            _laneCloseSettings.ResultConfirmCloseDelay, _laneCloseSettings.FalseStartThreshold,
                            _laneCloseSettings.SplitDisplayTime, _laneCloseSettings.FinishPosition == "left" ? "左端" : "右端"));
                        SaveTimingSettings();
                        AutoSaveData();
                        UpdateLaneStatusDisplay();
                        Broadcast();
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
                    if (data != null && (_raceState == RaceState.Racing || _raceState == RaceState.Finished)) {
                        int laneNum = (int)data["lane"];
                        var lState = _laneDeviceStates.FirstOrDefault(s => s.Lane == laneNum);
                        // 检查：启用且打开状态才记录
                        if (lState != null && lState.LeftManualEnabled && lState.LeftManualStatus == DeviceStatus.Open) {
                            lState.LeftManualTouchTime = _runningTime;
                            SaveManualTouchToSplit(laneNum, _runningTime);
                            LogRawTimingData(laneNum, "ManualTouchLeft", _runningTime);
                            AddLog(string.Format("泳道{0} 左端手动触板: {1}", laneNum, TimeFormatter.Format(_runningTime)));
                        } else if (lState != null && !lState.LeftManualEnabled) {
                            AddLog(string.Format("泳道{0} 左端手动触板(未启用)", laneNum));
                        } else if (lState != null && lState.LeftManualStatus != DeviceStatus.Open) {
                            AddLog(string.Format("泳道{0} 左端手动触板(未打开)", laneNum));
                        }
                    }
                    break;
                case "MANUAL_TOUCH_RIGHT":
                    if (data != null && (_raceState == RaceState.Racing || _raceState == RaceState.Finished)) {
                        int laneNum = (int)data["lane"];
                        var lState = _laneDeviceStates.FirstOrDefault(s => s.Lane == laneNum);
                        if (lState != null && lState.RightManualEnabled && lState.RightManualStatus == DeviceStatus.Open) {
                            lState.RightManualTouchTime = _runningTime;
                            SaveManualTouchToSplit(laneNum, _runningTime);
                            LogRawTimingData(laneNum, "ManualTouchRight", _runningTime);
                            AddLog(string.Format("泳道{0} 右端手动触板: {1}", laneNum, TimeFormatter.Format(_runningTime)));
                        } else if (lState != null && !lState.RightManualEnabled) {
                            AddLog(string.Format("泳道{0} 右端手动触板(未启用)", laneNum));
                        } else if (lState != null && lState.RightManualStatus != DeviceStatus.Open) {
                            AddLog(string.Format("泳道{0} 右端手动触板(未打开)", laneNum));
                        }
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
            string side = data["side"] != null ? data["side"].ToString() : null;
            ProcessTimingData(lane, cmdType, time, side);
        }

        private void HandleConnectHw(JObject msg) {
            var d = msg["data"];
            if (d == null) return;
            string mode = d["mode"] != null ? d["mode"].ToString() : "none";
            if (mode == "disconnect") {
                if (_timingBridge != null) { _timingBridge.Disconnect(); UpdateConnectionStatus(); Broadcast(); }
                return;
            }
            if (mode == "serial") {
                string port = d["port"] != null ? d["port"].ToString() : "COM3";
                _timingBridge.ConnectSerial(port);
            } else if (mode == "udp") {
                int udpPort = d["port"] != null ? (int)d["port"] : 5002;
                _timingBridge.ConnectUdp(udpPort);
            } else if (mode == "tcp") {
                string host = d["host"] != null ? d["host"].ToString() : "127.0.0.1";
                int port = d["port"] != null ? (int)d["port"] : 5555;
                _timingBridge.ConnectTcp(host, port);
            }
            UpdateConnectionStatus();
            Broadcast();
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
            // 实时分段名次（广播给客户端）
            var liveRanksForBroadcast = ComputeLiveRanks(currentSwimmers);
            foreach (var sw in currentSwimmers) {
                // 获取该赛次的泳道号（优先StageAssignment，不受晋级影响）
                var stageAssign = sw.GetAssignmentForStage(_currentStage);
                int displayLane = stageAssign != null ? stageAssign.Lane : sw.Lane;

                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == displayLane);
                var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                var latestSplit = result != null && result.Splits.Count > 0 ? result.Splits.Last() : null;

                // 接力项目：name显示队员姓名
                string bcastName = sw.Name;
                if (_isRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:")) {
                    bcastName = sw.Notes.Substring("接力队 棒次:".Length);
                }
                swimmerData.Add(new {
                    lane = displayLane,
                    name = bcastName,
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
                    manualButton = new {
                        leftEnabled = laneState != null && laneState.LeftManualEnabled,
                        rightEnabled = laneState != null && laneState.RightManualEnabled,
                        leftStatus = laneState != null ? laneState.LeftManualStatus.ToString().ToLower() : "closed",
                        rightStatus = laneState != null ? laneState.RightManualStatus.ToString().ToLower() : "closed"
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
                    finalTime = (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF") ? "" : (result != null ? TimeFormatter.Format(result.FinalTime) : ""),
                    // 实时分段名次：有则用 liveRank，否则回退到 result.Rank
                    rank = (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF") ? 0
                        : (liveRanksForBroadcast.ContainsKey(sw) ? liveRanksForBroadcast[sw]
                           : (result != null ? result.Rank : 0)),
                    status = sw.Status ?? "",
                    timingSources = result != null ? new {
                        touchpad = TimeFormatter.Format(result.TouchpadTime),
                        blindWatch1 = TimeFormatter.Format(result.PushButton1Time),
                        blindWatch2 = TimeFormatter.Format(result.PushButton2Time),
                        blindWatch3 = TimeFormatter.Format(result.PushButton3Time),
                        manualTouchLeft = laneState != null && laneState.LeftManualTouchTime > 0
                            ? TimeFormatter.Format(laneState.LeftManualTouchTime)
                            : "",
                        manualTouchRight = laneState != null && laneState.RightManualTouchTime > 0
                            ? TimeFormatter.Format(laneState.RightManualTouchTime)
                            : "",
                        manual = result.Splits.Count > 0 && result.Splits.Last().ManualTouchTime > 0
                            ? TimeFormatter.Format(result.Splits.Last().ManualTouchTime)
                            : (laneState != null ? TimeFormatter.Format(Math.Max(laneState.LeftManualTouchTime, laneState.RightManualTouchTime)) : "")
                    } : (object)null,
                    isFalseStart = laneState != null && laneState.IsFalseStart,
                    isNewRecord = false,
                    currentLap = laneState != null ? laneState.CurrentLap : 0,
                    isFinished = laneState != null && laneState.IsFinished,
                    leftTouchRemain = GetTouchRemain(laneState, true),
                    rightTouchRemain = GetTouchRemain(laneState, false)
                });
            }

            // 为空泳道（无运动员或无当前组）添加占位，使客户端总能显示全部泳道
            var assignedLanes = new HashSet<int>();
            foreach (var o in swimmerData) {
                try { assignedLanes.Add((int)((dynamic)o).lane); } catch { }
            }
            if (_poolConfig.LaneNumbers != null) {
                foreach (int ln in _poolConfig.LaneNumbers) {
                    if (assignedLanes.Contains(ln)) continue;
                    var ls = _laneDeviceStates.FirstOrDefault(s => s.Lane == ln);
                    swimmerData.Add(new {
                        lane = ln,
                        name = "", country = "", bibNumber = "", entryTime = "",
                        direction = ls != null ? ls.Direction : "",
                        deviceStatus = new {
                            leftTouchpad = "closed", leftBlindWatch1 = "closed",
                            leftBlindWatch2 = "closed", leftBlindWatch3 = "closed",
                            leftStartBlock = "closed",
                            rightTouchpad = "closed", rightBlindWatch1 = "closed",
                            rightBlindWatch2 = "closed", rightBlindWatch3 = "closed",
                            rightStartBlock = "closed",
                            leftTouchpadBroken = false, leftBlindWatch1Broken = false,
                            leftBlindWatch2Broken = false, leftBlindWatch3Broken = false,
                            leftStartBlockBroken = false,
                            rightTouchpadBroken = false, rightBlindWatch1Broken = false,
                            rightBlindWatch2Broken = false, rightBlindWatch3Broken = false,
                            rightStartBlockBroken = false
                        },
                        manualButton = new {
                            leftEnabled = false, rightEnabled = false,
                            leftStatus = "closed", rightStatus = "closed"
                        },
                        laneCloseCountdown = 0.0,
                        reactionTime = "",
                        splits = new List<object>(),
                        finalTime = "",
                        rank = 0,
                        status = "EMPTY",         // 空泳道标记（客户端以此区别）
                        timingSources = (object)null,
                        isFalseStart = false,
                        isNewRecord = false,
                        currentLap = 0,
                        isFinished = false,
                        leftTouchRemain = "",
                        rightTouchRemain = ""
                    });
                }
            }
            // 按泳道号排序（空泳道正确插入对应位置）
            swimmerData = swimmerData.OrderBy(o => { try { return (int)((dynamic)o).lane; } catch { return 0; } }).ToList();

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
                // 第1名成绩（由ProcessTouchpadHit设置，客户端直接显示）
                firstPlaceFinishTime = _firstPlaceFinishTime ?? "",
                firstPlaceActive = (_firstPlaceShowStart != DateTime.MinValue &&
                    (DateTime.Now - _firstPlaceShowStart).TotalSeconds <
                    (_laneCloseSettings.FirstPlaceHoldTime > 0 ? _laneCloseSettings.FirstPlaceHoldTime : 3)),
                laneCloseSettings = new {
                    laneCloseTime = _laneCloseSettings.LaneCloseTime,
                    startBlockCloseDelay = _laneCloseSettings.StartBlockCloseDelay,
                    resultConfirmCloseDelay = _laneCloseSettings.ResultConfirmCloseDelay,
                    falseStartThreshold = _laneCloseSettings.FalseStartThreshold,
                    splitDisplayTime = _laneCloseSettings.SplitDisplayTime,
                    startPosition = _laneCloseSettings.StartPosition,
                    finishPosition = _laneCloseSettings.FinishPosition,
                    firstPlaceHoldTime = _laneCloseSettings.FirstPlaceHoldTime
                },
                timingHwConnected = _timingBridge != null && _timingBridge.IsConnected,
                timingHwStatus = _timingBridge != null ? _timingBridge.StatusText : "未连接",
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

        // 判断泳道是否参赛：有运动员 且 状态不是 DNS/DNF/DSQ
        private bool IsLaneParticipating(int lane) {
            var swimmers = GetCurrentHeatSwimmers();
            var sw = swimmers.FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (sw == null) return false;
            string st = sw.Status ?? "";
            return st != "DNS" && st != "DNF" && st != "DSQ";
        }

        private List<Swimmer> GetCurrentHeatSwimmers() {
            if (string.IsNullOrEmpty(_currentEvent) || _currentHeat <= 0) return new List<Swimmer>();
            bool isRelay = _currentEvent.Contains("接力");
            var result = new List<Swimmer>();
            foreach (var s in _swimmers) {
                if (s.EventName != _currentEvent) continue;
                // 性别匹配：兼容"男"/"男子"等格式
                if (s.Gender != _currentGender && !s.Gender.StartsWith(_currentGender) && !_currentGender.StartsWith(s.Gender)) continue;
                // 接力项目：跳过个人队员条目，只保留代表队
                if (isRelay && s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                // 优先从StageAssignments查找（不受晋级后CurrentStage变化影响）
                var sa = s.GetAssignmentForStage(_currentStage);
                if (sa != null && sa.Heat == _currentHeat) {
                    result.Add(s);
                    continue;
                }
                // 兼容：当前赛次直接匹配
                if (s.CurrentStage == _currentStage && s.Heat == _currentHeat) {
                    result.Add(s);
                }
            }
            return result.OrderBy(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return sa != null ? sa.Lane : s.Lane;
            }).ToList();
        }

        private List<object> GetEventRanking(string eventName, string gender) {
            if (string.IsNullOrEmpty(eventName)) return new List<object>();
            bool rankRelay = eventName.Contains("接力");

            // 获取当前赛次的总组数
            var schedItem = _schedule.FirstOrDefault(s => s.Gender == gender && s.EventName == eventName && s.Stage == _currentStage);
            int heatCount = schedItem != null && schedItem.HeatCount > 0 ? schedItem.HeatCount : _totalHeats;

            // 收集已确认成绩的各组中被分配到该赛次的运动员
            var stageSwimmers = new List<Swimmer>();
            for (int h = 1; h <= Math.Max(heatCount, _currentHeat); h++) {
                // 判断该组是否已确认成绩
                bool confirmed = IsHeatConfirmed(gender, eventName, _currentStage, h);
                if (!confirmed && h == _currentHeat && _resultConfirmed) confirmed = true;
                if (!confirmed) continue;

                foreach (var s in _swimmers) {
                    if (s.EventName != eventName || s.Gender != gender) continue;
                    if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                    // 必须通过StageAssignment确认在该组
                    var sa = s.GetAssignmentForStage(_currentStage);
                    if (sa != null && sa.Heat == h) { stageSwimmers.Add(s); continue; }
                    // 兼容：无StageAssignment时按CurrentStage+Heat匹配
                    if (sa == null && s.CurrentStage == _currentStage && s.Heat == h) stageSwimmers.Add(s);
                }
            }

            var ranked = new List<object>();

            // 有有效成绩且非DSQ/DNS/DNF的运动员按成绩排名
            var withTimes = stageSwimmers.Where(s => {
                if (s.Status == "DSQ" || s.Status == "DNS" || s.Status == "DNF") return false;
                var r = s.GetResultForStage(_currentStage);
                return r != null && r.FinalTime > 0;
            }).OrderBy(s => s.GetResultForStage(_currentStage).FinalTime).ToList();

            int rank = 1;
            foreach (var sw in withTimes) {
                var r = sw.GetResultForStage(_currentStage);
                string rkName = sw.Name;
                if (rankRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                    rkName = sw.Notes.Substring("接力队 棒次:".Length);
                ranked.Add(new {
                    rank = rank++,
                    lane = sw.Lane,
                    bibNumber = sw.BibNumber,
                    name = rkName,
                    country = sw.Country,
                    entryTime = sw.EntryTime ?? "",
                    finalTime = r != null ? TimeFormatter.Format(r.FinalTime) : "",
                    timingSource = r != null ? r.TimingSource : "",
                    status = sw.Status ?? "",
                    resultStatus = r != null ? (r.Status ?? "") : ""
                });
            }

            // 其余运动员（DSQ/DNS/DNF/无成绩）追加到末尾，无名次
            var others = stageSwimmers.Where(s => !withTimes.Contains(s)).ToList();
            foreach (var sw in others) {
                var r = sw.GetResultForStage(_currentStage);
                string rkName = sw.Name;
                if (rankRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                    rkName = sw.Notes.Substring("接力队 棒次:".Length);
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (!string.IsNullOrEmpty(sw.Status)) remark = sw.Status;
                ranked.Add(new {
                    rank = 0,
                    lane = sw.Lane,
                    bibNumber = sw.BibNumber,
                    name = rkName,
                    country = sw.Country,
                    entryTime = sw.EntryTime ?? "",
                    finalTime = "",
                    timingSource = "",
                    status = remark,
                    resultStatus = remark
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
            // 根据终点端/另一端标识 + 终点位置设置，映射到 left/right
            // FinishPosition="left" → 终点端=left, 另一端=right
            // FinishPosition="right" → 终点端=right, 另一端=left
            bool finishIsLeft = _laneCloseSettings.FinishPosition != "right";
            string side;
            if (data.IsFinishEnd)
                side = finishIsLeft ? "left" : "right";
            else
                side = finishIsLeft ? "right" : "left";
            ProcessTimingData(data.Lane, cmdType, data.TimeInSeconds, side);
        }

        private void ProcessTimingData(int lane, string cmdType, double timeInSeconds, string side = null) {
            // 硬件触发的比赛控制命令：任何状态下都接收
            switch (cmdType) {
                case "TimerReady":
                    AddLog("硬件触发: 就位");
                    Ready_Click(null, null);
                    return;
                case "StartCommand":
                    AddLog("硬件触发: 发令");
                    // 若仍在 Waiting 状态，先自动就位再发令，确保定时器能启动
                    if (_raceState == RaceState.Waiting) Ready_Click(null, null);
                    StartRace_Click(null, null);
                    return;
                case "TimerReset":
                    AddLog("硬件触发: 计时清零");
                    Restart_Click(null, null);
                    return;
            }

            // Racing状态或Finished状态（延迟关闭期内盲表/手动仍有效）都接收数据
            if (_raceState != RaceState.Racing && _raceState != RaceState.Finished && cmdType != "StartCommand") return;

            // 空泳道或 DNS/DNF/DSQ 泳道：整条泳道关闭，不记录也不处理任何计时数据
            if (!IsLaneParticipating(lane)) {
                AddLog(string.Format("泳道{0} 数据丢弃（空泳道或未参赛）: {1}", lane, cmdType));
                return;
            }

            // 记录原始数据
            LogRawTimingData(lane, cmdType, timeInSeconds);

            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState == null) return;

            switch (cmdType) {
                case "StartCommand":
                    // 发令信号（兼容旧协议）
                    break;

                case "StartingBlock":
                    // 出发台 — 反应时间（含接力交接棒抢跳检测）
                    // 根据 side 检查对应端出发台是否打开（side=null 时兼容两端任一打开）
                    {
                        bool sbOpen;
                        if (side == "left")
                            sbOpen = laneState.LeftStartBlockStatus == DeviceStatus.Open || laneState.LeftStartBlockStatus == DeviceStatus.FalseStart;
                        else if (side == "right")
                            sbOpen = laneState.RightStartBlockStatus == DeviceStatus.Open || laneState.RightStartBlockStatus == DeviceStatus.FalseStart;
                        else
                            sbOpen = laneState.LeftStartBlockStatus == DeviceStatus.Open || laneState.RightStartBlockStatus == DeviceStatus.Open
                                  || laneState.LeftStartBlockStatus == DeviceStatus.FalseStart || laneState.RightStartBlockStatus == DeviceStatus.FalseStart;
                        if (sbOpen) {

                        if (_isRelay && laneState.CurrentLap > 0) {
                            // 接力交接：出发台时间是绝对时间，与上次触板时间比较
                            // 获取上次触板累计时间
                            var swForLane = GetCurrentHeatSwimmers().FirstOrDefault(s2 => {
                                var sa2 = s2.GetAssignmentForStage(_currentStage);
                                return (sa2 != null ? sa2.Lane : s2.Lane) == lane;
                            });
                            double lastTouchTime = 0;
                            if (swForLane != null) {
                                var res = swForLane.Results.FirstOrDefault(r2 => r2.Stage == _currentStage && r2.Heat == _currentHeat);
                                if (res != null && res.Splits.Count > 0) lastTouchTime = res.Splits.Last().CumulativeTime;
                            }
                            double relayReaction = timeInSeconds - lastTouchTime;
                            laneState.ReactionTime = relayReaction;
                            if (relayReaction < _laneCloseSettings.FalseStartThreshold) {
                                laneState.IsFalseStart = true;
                                AddLog(string.Format("★接力抢跳! 泳道{0} 出发台:{1:F2}s 触板:{2:F2}s 差值:{3:F3}s", lane, timeInSeconds, lastTouchTime, relayReaction));
                            } else {
                                AddLog(string.Format("泳道{0} 接力交接 出发台:{1:F2}s 差值:{2:F2}s", lane, timeInSeconds, relayReaction));
                            }
                        } else {
                            // 普通出发：反应时间就是出发台时间
                            laneState.ReactionTime = timeInSeconds;
                            if (timeInSeconds <= _laneCloseSettings.FalseStartThreshold) {
                                laneState.IsFalseStart = true;
                                AddLog(string.Format("★抢跳! 泳道{0} 反应时间: {1:F3}s", lane, timeInSeconds));
                            } else {
                                AddLog(string.Format("泳道{0} 反应时间: {1:F2}s", lane, timeInSeconds));
                            }
                        }
                    }
                    }
                    break;

                case "Touchpad":
                    // 触板 — 根据 side 检查对应端是否打开（side=null 时兼容两端任一打开）
                    {
                        bool touchOpen;
                        if (side == "left") touchOpen = laneState.LeftTouchpadStatus == DeviceStatus.Open;
                        else if (side == "right") touchOpen = laneState.RightTouchpadStatus == DeviceStatus.Open;
                        else touchOpen = laneState.LeftTouchpadStatus == DeviceStatus.Open || laneState.RightTouchpadStatus == DeviceStatus.Open;
                        if (touchOpen) {
                            ProcessTouchpadHit(lane, timeInSeconds, laneState);
                        } else {
                            AddLog(string.Format("泳道{0} 触板数据丢弃（{1}泳道关闭中）", lane, side ?? ""));
                        }
                    }
                    break;

                case "PushButton1":
                case "PushButton2":
                case "PushButton3":
                    // 盲表 — 根据 side 检查对应端盲表是否打开
                    {
                        bool blindOpen;
                        if (side == "left")
                            blindOpen = laneState.LeftBlindWatch1Status == DeviceStatus.Open || laneState.LeftBlindWatch2Status == DeviceStatus.Open || laneState.LeftBlindWatch3Status == DeviceStatus.Open;
                        else if (side == "right")
                            blindOpen = laneState.RightBlindWatch1Status == DeviceStatus.Open || laneState.RightBlindWatch2Status == DeviceStatus.Open || laneState.RightBlindWatch3Status == DeviceStatus.Open;
                        else
                            blindOpen = laneState.LeftBlindWatch1Status == DeviceStatus.Open || laneState.LeftBlindWatch2Status == DeviceStatus.Open || laneState.LeftBlindWatch3Status == DeviceStatus.Open
                                     || laneState.RightBlindWatch1Status == DeviceStatus.Open || laneState.RightBlindWatch2Status == DeviceStatus.Open || laneState.RightBlindWatch3Status == DeviceStatus.Open;
                        if (blindOpen) {
                            ProcessBlindWatchData(lane, cmdType, timeInSeconds);
                        }
                    }
                    break;
            }

            UpdateLaneStatusDisplay();
            Broadcast();
        }

        /// <summary>
        /// 记录原始计时数据到当前比赛日志
        /// </summary>
        private void LogRawTimingData(int lane, string cmdType, double time) {
            string elapsed = _raceStartTime > DateTime.MinValue
                ? (DateTime.Now - _raceStartTime).TotalSeconds.ToString("F3")
                : "0.000";
            // 查找运动员
            string swimmerName = "";
            var sw = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (sw != null) swimmerName = sw.Name ?? "";

            _rawTimingLog.AppendFormat("{0}\t道{1}\t{2}\t{3}\t{4}\r\n",
                DateTime.Now.ToString("HH:mm:ss.fff"), lane, cmdType,
                TimeFormatter.Format(time), swimmerName);
        }

        /// <summary>
        /// 保存当前比赛的原始计时数据到文件
        /// </summary>
        private void SaveRawTimingLog() {
            if (_rawTimingLog.Length == 0) return;
            if (string.IsNullOrEmpty(_currentEvent) || _currentHeat <= 0) return;
            try {
                string dir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", "RawData");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 文件名：性别_项目_赛次_第N组.txt
                string safeName = string.Format("{0}_{1}_{2}_第{3}组",
                    _currentGender, _currentEvent, _currentStage, _currentHeat)
                    .Replace("×", "x").Replace("/", "_");
                string path = IOPath.Combine(dir, safeName + ".txt");

                // 写入文件头 + 数据
                var sb = new StringBuilder();
                sb.AppendFormat("═══ 原始计时数据 ═══\r\n");
                sb.AppendFormat("赛事: {0}\r\n", _competitionName);
                sb.AppendFormat("项目: {0} {1}\r\n", _currentGender, _currentEvent);
                sb.AppendFormat("赛次: {0}  第{1}组\r\n", _currentStage, _currentHeat);
                sb.AppendFormat("比赛时间: {0}\r\n", _raceStartTime > DateTime.MinValue ? _raceStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "未开始");
                sb.AppendFormat("终点位置: {0}端  发令位置: {1}端\r\n",
                    _laneCloseSettings.FinishPosition == "left" ? "左" : "右",
                    _laneCloseSettings.StartPosition == "left" ? "左" : "右");

                // 运动员名单
                sb.AppendFormat("\r\n--- 出场名单 ---\r\n");
                foreach (var s in GetCurrentHeatSwimmers()) {
                    var sa = s.GetAssignmentForStage(_currentStage);
                    int sLane = sa != null ? sa.Lane : s.Lane;
                    sb.AppendFormat("道{0}\t{1}\t{2}\t报名:{3}\r\n", sLane, s.Name, s.Country, s.EntryTime);
                }

                // 原始数据
                sb.AppendFormat("\r\n--- 原始数据 (时刻/泳道/类型/时间/运动员) ---\r\n");
                sb.Append(_rawTimingLog.ToString());

                // 最终成绩汇总
                sb.AppendFormat("\r\n--- 最终成绩 ---\r\n");
                foreach (var s in GetCurrentHeatSwimmers().OrderBy(s2 => s2.CurrentRank > 0 ? s2.CurrentRank : int.MaxValue)) {
                    var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                    var sa = s.GetAssignmentForStage(_currentStage);
                    int sLane = sa != null ? sa.Lane : s.Lane;
                    string status = !string.IsNullOrEmpty(s.Status) ? s.Status : "";
                    string finalTime = r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "";
                    string source = r != null ? (r.TimingSource ?? "") : "";
                    string reaction = r != null && r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F3") : "";
                    sb.AppendFormat("名次:{0}\t道{1}\t{2}\t{3}\t成绩:{4}\t计时源:{5}\t反应:{6}\t{7}\r\n",
                        s.CurrentRank > 0 ? s.CurrentRank.ToString() : "-", sLane, s.Name, s.Country,
                        !string.IsNullOrEmpty(status) ? status : finalTime, source, reaction, status);

                    // 各计时设备最终成绩
                    if (r != null) {
                        sb.AppendFormat("  终点成绩: 触板:{0}  盲表1:{1}  盲表2:{2}  盲表3:{3}\r\n",
                            TimeFormatter.Format(r.TouchpadTime),
                            TimeFormatter.Format(r.PushButton1Time),
                            TimeFormatter.Format(r.PushButton2Time),
                            TimeFormatter.Format(r.PushButton3Time));
                    }

                    // 分段明细
                    if (r != null && r.Splits.Count > 0) {
                        foreach (var sp in r.Splits) {
                            sb.AppendFormat("  分段{0}({1}m): 触板:{2}  盲1:{3}  盲2:{4}  盲3:{5}  手动:{6}  累计:{7}  源:{8}\r\n",
                                sp.Lap, sp.Distance,
                                TimeFormatter.Format(sp.TouchpadTime),
                                TimeFormatter.Format(sp.PushButton1Time),
                                TimeFormatter.Format(sp.PushButton2Time),
                                TimeFormatter.Format(sp.PushButton3Time),
                                TimeFormatter.Format(sp.ManualTouchTime),
                                TimeFormatter.Format(sp.CumulativeTime), sp.TimingSource);
                        }
                    }
                }

                sb.AppendFormat("\r\n保存时间: {0}\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

                // 同时生成HTML版本（可用浏览器打印为PDF，不可编辑）
                string htmlPath = IOPath.Combine(dir, safeName + ".html");
                SaveRawTimingHtml(htmlPath, safeName);

                AddLog(string.Format("原始计时数据已保存: {0}（txt + html）", safeName));
            } catch (Exception ex) {
                AddLog("保存原始计时数据失败: " + ex.Message);
            }
        }

        private void SaveRawTimingHtml(string htmlPath, string title) {
            var h = new StringBuilder();
            h.Append("<!DOCTYPE html><html><head><meta charset='UTF-8'>");
            h.AppendFormat("<title>{0} - 原始计时数据</title>", title);
            h.Append("<style>");
            h.Append("@page { size: A4; margin: 15mm; }");
            h.Append("body { font-family: 'Microsoft YaHei', sans-serif; font-size: 12px; color: #1e293b; margin: 0; padding: 20px; -webkit-user-select: none; user-select: none; }");
            h.Append("h1 { font-size: 18px; text-align: center; margin-bottom: 4px; }");
            h.Append("h2 { font-size: 14px; text-align: center; color: #475569; margin-bottom: 16px; }");
            h.Append(".info { margin-bottom: 12px; }");
            h.Append(".info td { padding: 2px 12px 2px 0; font-size: 12px; }");
            h.Append(".info .label { color: #64748b; }");
            h.Append(".section { font-size: 13px; font-weight: bold; color: #3b82f6; margin: 14px 0 6px; border-bottom: 1px solid #e2e8f0; padding-bottom: 2px; }");
            h.Append("table.data { width: 100%; border-collapse: collapse; font-size: 11px; margin-bottom: 10px; }");
            h.Append("table.data th { background: #f1f5f9; border: 1px solid #cbd5e1; padding: 3px 6px; text-align: center; font-size: 11px; }");
            h.Append("table.data td { border: 1px solid #e2e8f0; padding: 2px 6px; }");
            h.Append("table.data tr:nth-child(even) { background: #f8fafc; }");
            h.Append(".mono { font-family: Consolas, 'Courier New', monospace; }");
            h.Append(".raw-data { font-family: Consolas, 'Courier New', monospace; font-size: 11px; white-space: pre; background: #f8fafc; border: 1px solid #e2e8f0; padding: 8px; overflow-x: auto; }");
            h.Append(".footer { text-align: right; color: #94a3b8; font-size: 10px; margin-top: 12px; }");
            h.Append(".watermark { text-align: center; color: #cbd5e1; font-size: 10px; margin-top: 6px; }");
            h.Append("@media print { .no-print { display: none; } }");
            h.Append("</style></head><body>");

            // 标题
            h.AppendFormat("<h1>{0}</h1>", _competitionName);
            h.AppendFormat("<h2>{0} {1}　{2}　第{3}组　—　原始计时数据</h2>",
                _currentGender, _currentEvent, _currentStage, _currentHeat);

            // 赛事信息
            h.Append("<table class='info'>");
            h.AppendFormat("<tr><td class='label'>比赛时间:</td><td>{0}</td><td class='label'>终点位置:</td><td>{1}端</td></tr>",
                _raceStartTime > DateTime.MinValue ? _raceStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "未开始",
                _laneCloseSettings.FinishPosition == "left" ? "左" : "右");
            h.AppendFormat("<tr><td class='label'>发令位置:</td><td>{0}端</td><td class='label'>泳道数:</td><td>{1}道</td></tr>",
                _laneCloseSettings.StartPosition == "left" ? "左" : "右", _poolConfig.LaneCount);
            h.Append("</table>");

            // 出场名单
            h.Append("<div class='section'>出场名单</div>");
            h.Append("<table class='data'><tr><th>道次</th><th>姓名</th><th>代表队</th><th>报名成绩</th></tr>");
            foreach (var s in GetCurrentHeatSwimmers()) {
                var sa = s.GetAssignmentForStage(_currentStage);
                int sLane = sa != null ? sa.Lane : s.Lane;
                h.AppendFormat("<tr><td style='text-align:center;'>{0}</td><td><b>{1}</b></td><td>{2}</td><td class='mono' style='text-align:center;'>{3}</td></tr>",
                    sLane, s.Name, s.Country, s.EntryTime);
            }
            h.Append("</table>");

            // 原始数据流
            h.Append("<div class='section'>原始计时数据</div>");
            h.Append("<table class='data'><tr><th>时刻</th><th>泳道</th><th>类型</th><th>时间</th><th>运动员</th></tr>");
            string[] rawLines = _rawTimingLog.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in rawLines) {
                string[] cols = line.Split('\t');
                if (cols.Length >= 4) {
                    h.AppendFormat("<tr><td class='mono'>{0}</td><td style='text-align:center;'>{1}</td><td>{2}</td><td class='mono' style='text-align:center;'>{3}</td><td>{4}</td></tr>",
                        cols[0], cols[1], cols[2], cols[3], cols.Length > 4 ? cols[4] : "");
                }
            }
            h.Append("</table>");

            // 最终成绩
            h.Append("<div class='section'>最终成绩</div>");
            h.Append("<table class='data'><tr><th>名次</th><th>道</th><th>姓名</th><th>代表队</th><th>成绩</th><th>计时源</th><th>反应时间</th><th>触板</th><th>盲表1</th><th>盲表2</th><th>盲表3</th><th>状态</th></tr>");
            foreach (var s in GetCurrentHeatSwimmers().OrderBy(s2 => s2.CurrentRank > 0 ? s2.CurrentRank : int.MaxValue)) {
                var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                var sa = s.GetAssignmentForStage(_currentStage);
                int sLane = sa != null ? sa.Lane : s.Lane;
                string status = s.Status ?? "";
                bool isDQ = status == "DSQ" || status == "DNS" || status == "DNF";
                string finalTime = !isDQ && r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "";
                h.AppendFormat("<tr><td style='text-align:center;font-weight:bold;'>{0}</td><td style='text-align:center;'>{1}</td><td><b>{2}</b></td><td>{3}</td>" +
                    "<td class='mono' style='text-align:center;font-weight:bold;'>{4}</td><td style='text-align:center;'>{5}</td><td class='mono' style='text-align:center;'>{6}</td>" +
                    "<td class='mono' style='text-align:center;'>{7}</td><td class='mono' style='text-align:center;'>{8}</td><td class='mono' style='text-align:center;'>{9}</td><td class='mono' style='text-align:center;'>{10}</td>" +
                    "<td style='text-align:center;color:#dc2626;font-weight:bold;'>{11}</td></tr>",
                    s.CurrentRank > 0 ? s.CurrentRank.ToString() : (isDQ ? "-" : ""),
                    sLane, s.Name, s.Country, isDQ ? status : finalTime,
                    r != null ? (r.TimingSource ?? "") : "",
                    r != null && r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F3") : "",
                    r != null ? TimeFormatter.Format(r.TouchpadTime) : "",
                    r != null ? TimeFormatter.Format(r.PushButton1Time) : "",
                    r != null ? TimeFormatter.Format(r.PushButton2Time) : "",
                    r != null ? TimeFormatter.Format(r.PushButton3Time) : "",
                    isDQ ? status : "");

                // 分段明细
                if (r != null && r.Splits.Count > 1) {
                    h.AppendFormat("<tr><td colspan='12' style='padding-left:30px;font-size:10px;color:#64748b;'>");
                    foreach (var sp in r.Splits) {
                        h.AppendFormat("分段{0}({1}m): 触板:<b>{2}</b> 盲1:{3} 盲2:{4} 盲3:{5} 累计:{6}　",
                            sp.Lap, sp.Distance,
                            TimeFormatter.Format(sp.TouchpadTime),
                            TimeFormatter.Format(sp.PushButton1Time),
                            TimeFormatter.Format(sp.PushButton2Time),
                            TimeFormatter.Format(sp.PushButton3Time),
                            TimeFormatter.Format(sp.CumulativeTime));
                    }
                    h.Append("</td></tr>");
                }
            }
            h.Append("</table>");

            h.AppendFormat("<div class='footer'>保存时间：{0}</div>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            h.Append("<div class='watermark'>本文档由竞赛管理系统自动生成 - 请使用浏览器 打印-另存为PDF 导出只读PDF文件</div>");
            h.Append("</body></html>");

            File.WriteAllText(htmlPath, h.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// 触板打开时预创建空分段，确保后续的触板/盲表/手动数据都能写入
        /// </summary>
        private void PreCreateSplit(int lane) {
            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState == null || laneState.IsFinished) return;
            int nextLap = laneState.CurrentLap + 1;

            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
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

            // 检查是否已有该段的split（避免重复创建）
            bool exists = false;
            foreach (var sp in result.Splits) {
                if (sp.Lap == nextLap) { exists = true; break; }
            }
            if (!exists) {
                result.Splits.Add(new SplitTime {
                    Lap = nextLap,
                    Distance = nextLap * _poolConfig.Length
                });
            }
        }

        private void ProcessTouchpadHit(int lane, double time, LaneDeviceState laneState) {
            // 第1名成绩检测：不管哪个泳道，hold时间过期后最先收到的触板就是新的第1名
            if (time > 0) {
                double holdSec = _laneCloseSettings.FirstPlaceHoldTime > 0 ? _laneCloseSettings.FirstPlaceHoldTime : 3;
                bool holdExpired = _firstPlaceShowStart == DateTime.MinValue ||
                                   (DateTime.Now - _firstPlaceShowStart).TotalSeconds >= holdSec;
                if (holdExpired) {
                    _firstPlaceFinishTime = TimeFormatter.Format(time);
                    _firstPlaceShowStart = DateTime.Now;
                    // 立即更新滚动时间显示（不等下一个timer tick）
                    if (RunningTimeText != null) {
                        RunningTimeText.Text = _firstPlaceFinishTime;
                        RunningTimeText.Foreground = _firstPlaceBrush;
                    }
                }
            }

            // 获取当前运动员（优先StageAssignment泳道号，兼容sw.Lane）
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
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

            // 查找预创建的split（由PreCreateSplit在触板打开时创建）
            SplitTime split = null;
            foreach (var sp in result.Splits) {
                if (sp.Lap == currentLap) { split = sp; break; }
            }
            if (split == null) {
                // 兼容：如果没有预创建（比如50米第1段出发后直接触板），创建新的
                split = new SplitTime { Lap = currentLap, Distance = currentLap * _poolConfig.Length };
                result.Splits.Add(split);
            }

            // 计算上一段累计时间
            double prevCumulative = 0;
            foreach (var sp in result.Splits) {
                if (sp.Lap == currentLap - 1) { prevCumulative = sp.CumulativeTime; break; }
            }

            // 填入触板数据到预创建的split中（盲表和手动可能已经写入了）
            split.Time = time - prevCumulative;
            split.CumulativeTime = time;
            split.TouchpadTime = time;
            // 如果手动/盲表还没写入split（在触板之前存到了laneState），补充写入
            if (split.ManualTouchTime <= 0) {
                double manualTime = Math.Max(laneState.LeftManualTouchTime, laneState.RightManualTouchTime);
                if (manualTime > prevCumulative) split.ManualTouchTime = manualTime;
            }
            if (split.PushButton1Time <= 0 && laneState.PendingBlind1Time > prevCumulative)
                split.PushButton1Time = laneState.PendingBlind1Time;
            if (split.PushButton2Time <= 0 && laneState.PendingBlind2Time > prevCumulative)
                split.PushButton2Time = laneState.PendingBlind2Time;
            if (split.PushButton3Time <= 0 && laneState.PendingBlind3Time > prevCumulative)
                split.PushButton3Time = laneState.PendingBlind3Time;

            // 清除暂存
            laneState.LeftManualTouchTime = 0;
            laneState.RightManualTouchTime = 0;
            laneState.PendingBlind1Time = 0;
            laneState.PendingBlind2Time = 0;
            laneState.PendingBlind3Time = 0;
            AddLog(string.Format("泳道{0} 第{1}段: {2} (累计: {3})", lane, currentLap,
                TimeFormatter.Format(split.Time), TimeFormatter.Format(time)));

            if (currentLap >= totalLaps) {
                // 最终到达 — 同步最终段的各计时源到LaneResult终点汇总
                laneState.IsFinished = true;
                result.TouchpadTime = split.TouchpadTime;
                result.PushButton1Time = split.PushButton1Time;
                result.PushButton2Time = split.PushButton2Time;
                result.PushButton3Time = split.PushButton3Time;
                result.FinalTime = time;
                result.TimeInSeconds = time;
                // 保存反应时间到成绩记录
                if (laneState.ReactionTime > 0) {
                    result.StartingBlockTime = laneState.ReactionTime;
                }

                // 计时源裁定（使用最终段的各计时源数据）
                var judgement = TimingBridge.JudgeTimingSource(
                    split.TouchpadTime, split.PushButton1Time, split.PushButton2Time, split.PushButton3Time,
                    split.ManualTouchTime);
                result.FinalTime = judgement.FinalTime;
                result.TimingSource = judgement.Source;
                split.TimingSource = judgement.Source;

                // 关闭触板（立即），盲表延迟关闭（给盲表和手动留时间记录）
                laneState.LeftTouchpadStatus = DeviceStatus.Closed;
                laneState.RightTouchpadStatus = DeviceStatus.Closed;
                laneState.LaneCloseCountdown = 0;

                // 延迟关闭盲表（ResultConfirmCloseDelay秒后）
                int capLane3 = lane;
                var finishCloseTimer = new DispatcherTimer();
                finishCloseTimer.Interval = TimeSpan.FromSeconds(_laneCloseSettings.ResultConfirmCloseDelay);
                finishCloseTimer.Tick += delegate(object s3, EventArgs a3) {
                    finishCloseTimer.Stop();
                    var ls3 = _laneDeviceStates.FirstOrDefault(st => st.Lane == capLane3);
                    if (ls3 == null) return;
                    ls3.LeftBlindWatch1Status = DeviceStatus.Closed; ls3.LeftBlindWatch2Status = DeviceStatus.Closed; ls3.LeftBlindWatch3Status = DeviceStatus.Closed;
                    ls3.RightBlindWatch1Status = DeviceStatus.Closed; ls3.RightBlindWatch2Status = DeviceStatus.Closed; ls3.RightBlindWatch3Status = DeviceStatus.Closed;
                    ls3.LeftManualStatus = DeviceStatus.Closed; ls3.RightManualStatus = DeviceStatus.Closed;
                    Broadcast();
                };
                finishCloseTimer.Start();

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
                // 分段触碰 — 切换方向和开始新倒计时
                laneState.Direction = laneState.Direction == "→" ? "←" : "→";
                laneState.LaneCloseCountdown = laneState.LaneCloseTime > 0 ? laneState.LaneCloseTime : _laneCloseSettings.LaneCloseTime;

                // 接力项目：交接棒触板触碰后，延迟关闭出发台
                if (_isRelay) {
                    int totalLapsVal2 = GetTotalLaps();
                    int legCnt2 = 4;
                    var rlMatch2 = System.Text.RegularExpressions.Regex.Match(_currentEvent, @"(\d+)\s*[x×]");
                    if (rlMatch2.Success) legCnt2 = int.Parse(rlMatch2.Groups[1].Value);
                    int lapsPerLeg2 = totalLapsVal2 > 0 ? totalLapsVal2 / legCnt2 : 1;
                    bool isExchange = (lapsPerLeg2 > 0) && (currentLap % lapsPerLeg2 == 0) && (currentLap < totalLapsVal2);

                    if (isExchange) {
                        // 交接棒触板已触碰，延迟后关闭出发台
                        bool startLeft2 = _laneCloseSettings.StartPosition != "right";
                        int capLane2 = lane;
                        bool capStartLeft2 = startLeft2;
                        var sbCloseTimer = new DispatcherTimer();
                        sbCloseTimer.Interval = TimeSpan.FromSeconds(_laneCloseSettings.StartBlockCloseDelay);
                        sbCloseTimer.Tick += delegate(object s4, EventArgs a4) {
                            sbCloseTimer.Stop();
                            var ls2 = _laneDeviceStates.FirstOrDefault(st => st.Lane == capLane2);
                            if (ls2 == null || ls2.IsFinished) return;
                            if (capStartLeft2) ls2.LeftStartBlockStatus = DeviceStatus.Closed;
                            else ls2.RightStartBlockStatus = DeviceStatus.Closed;
                            Broadcast();
                        };
                        sbCloseTimer.Start();
                        AddLog(string.Format("泳道{0} 交接棒触板，出发台将在{1}秒后关闭", lane, _laneCloseSettings.StartBlockCloseDelay));
                    }
                }

                // 延迟后关闭到达端设备（给盲表留时间）
                string arrivedEnd = laneState.Direction == "→" ? "left" : "right";
                var closeTimer = new DispatcherTimer();
                closeTimer.Interval = TimeSpan.FromSeconds(_laneCloseSettings.ResultConfirmCloseDelay);
                closeTimer.Tick += delegate(object s2, EventArgs a2) {
                    closeTimer.Stop();
                    if (laneState.IsFinished) return;
                    if (arrivedEnd == "right") {
                        laneState.RightTouchpadStatus = DeviceStatus.Closed;
                        laneState.RightBlindWatch1Status = DeviceStatus.Closed; laneState.RightBlindWatch2Status = DeviceStatus.Closed; laneState.RightBlindWatch3Status = DeviceStatus.Closed;
                        laneState.RightManualStatus = DeviceStatus.Closed;
                    } else {
                        laneState.LeftTouchpadStatus = DeviceStatus.Closed;
                        laneState.LeftBlindWatch1Status = DeviceStatus.Closed; laneState.LeftBlindWatch2Status = DeviceStatus.Closed; laneState.LeftBlindWatch3Status = DeviceStatus.Closed;
                        laneState.LeftManualStatus = DeviceStatus.Closed;
                    }
                    Broadcast();
                };
                closeTimer.Start();
            }
        }

        /// <summary>
        /// 查找当前段的split记录（预创建的或已完成的）
        /// 触板打开后、触板触碰前：targetLap = CurrentLap + 1（预创建的空split）
        /// 触板触碰后：targetLap = CurrentLap（已填入触板数据的split）
        /// </summary>
        private SplitTime FindCurrentSplit(int lane) {
            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState == null) return null;

            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer == null) return null;

            var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (result == null || result.Splits.Count == 0) return null;

            // 返回最后一个split（预创建的或最近完成的）
            // 因为预创建的split总是在最后面，且是当前段
            return result.Splits.Last();
        }

        private void SaveManualTouchToSplit(int lane, double time) {
            // 查找当前段的预创建split（触板打开时已创建）
            var split = FindCurrentSplit(lane);
            if (split != null) {
                split.ManualTouchTime = time;
                // 如果已完赛，同步到result终点汇总（触板先到达时result已设置，手动后到需要补充）
                SyncSplitToResultIfFinished(lane, split);
                return;
            }
        }

        /// <summary>
        /// 已完赛时，将最终段split的计时数据同步到result终点汇总
        /// </summary>
        private void SyncSplitToResultIfFinished(int lane, SplitTime split) {
            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState == null || !laneState.IsFinished) return;

            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer == null) return;

            var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (result == null) return;

            // 同步最终段的各计时源到result
            if (split.TouchpadTime > 0) result.TouchpadTime = split.TouchpadTime;
            if (split.PushButton1Time > 0) result.PushButton1Time = split.PushButton1Time;
            if (split.PushButton2Time > 0) result.PushButton2Time = split.PushButton2Time;
            if (split.PushButton3Time > 0) result.PushButton3Time = split.PushButton3Time;
        }

        private void ProcessBlindWatchData(int lane, string cmdType, double time) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
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

            // 保存到LaneResult总记录（终点汇总）
            switch (cmdType) {
                case "PushButton1": result.PushButton1Time = time; break;
                case "PushButton2": result.PushButton2Time = time; break;
                case "PushButton3": result.PushButton3Time = time; break;
            }

            // 保存到当前段的split（预创建的split在触板打开时已存在）
            var targetSplit = FindCurrentSplit(lane);
            if (targetSplit != null) {
                switch (cmdType) {
                    case "PushButton1": targetSplit.PushButton1Time = time; break;
                    case "PushButton2": targetSplit.PushButton2Time = time; break;
                    case "PushButton3": targetSplit.PushButton3Time = time; break;
                }
                // 如果已完赛，同步到result终点汇总
                SyncSplitToResultIfFinished(lane, targetSplit);
            } else {
                // 备用：预创建split不存在时暂存到laneState
                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
                if (laneState != null) {
                    switch (cmdType) {
                        case "PushButton1": laneState.PendingBlind1Time = time; break;
                        case "PushButton2": laneState.PendingBlind2Time = time; break;
                        case "PushButton3": laneState.PendingBlind3Time = time; break;
                    }
                }
            }
            AddLog(string.Format("泳道{0} {1}: {2}", lane, cmdType, TimeFormatter.Format(time)));
        }

        private int GetTotalLaps() {
            string ev = _currentEvent;
            int distance = 0;

            // 接力项目：4x100米 → 总距离 = 4 * 100 = 400
            if (ev.Contains("x") || ev.Contains("×")) {
                var relayMatch = System.Text.RegularExpressions.Regex.Match(ev, @"(\d+)\s*[x×]\s*(\d+)");
                if (relayMatch.Success) {
                    int legs = int.Parse(relayMatch.Groups[1].Value);
                    int legDist = int.Parse(relayMatch.Groups[2].Value);
                    distance = legs * legDist;
                }
            }

            // 个人项目：直接取第一个数字
            if (distance == 0) {
                var distMatch = System.Text.RegularExpressions.Regex.Match(ev, @"(\d+)米");
                if (distMatch.Success) distance = int.Parse(distMatch.Groups[1].Value);
            }

            if (distance == 0) distance = 50;
            return Math.Max(1, distance / _poolConfig.Length);
        }

        /// <summary>
        /// 计算某道左/右触板剩余次数
        /// isLeft=true: 出发端(左端)触板剩余; isLeft=false: 到达端(右端)触板剩余
        /// 出发方向由设置决定，这里假设startPosition决定出发端
        /// </summary>
        private int GetTouchRemain(LaneDeviceState laneState, bool isLeft) {
            int totalLaps = GetTotalLaps();
            int currentLap = laneState != null ? laneState.CurrentLap : 0;
            bool startFromLeft = _laneCloseSettings.StartPosition != "right";

            // 总触板次数：出发端 = totalLaps/2（偶数段触），到达端 = (totalLaps+1)/2（奇数段触）
            int startSideTotal, farSideTotal;
            if (totalLaps == 1) {
                startSideTotal = 0; farSideTotal = 1;  // 50米：出发端0次，到达端1次
            } else {
                startSideTotal = totalLaps / 2;
                farSideTotal = (totalLaps + 1) / 2;
            }

            // 已完成的触板次数
            int startSideDone = currentLap / 2;           // 偶数段在出发端触板
            int farSideDone = (currentLap + 1) / 2;       // 奇数段在到达端触板

            int startRemain = Math.Max(0, startSideTotal - startSideDone);
            int farRemain = Math.Max(0, farSideTotal - farSideDone);

            if (startFromLeft) {
                return isLeft ? startRemain : farRemain;
            } else {
                return isLeft ? farRemain : startRemain;
            }
        }

        private void UpdateHeatRanking() {
            var swimmers = GetCurrentHeatSwimmers();

            // 先清零所有运动员的排名（防止DSQ等运动员保留旧排名）
            foreach (var sw in swimmers) {
                sw.CurrentRank = 0;
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                if (r != null) r.Rank = 0;
            }

            var withResults = swimmers.Where(s => {
                var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                return r != null && r.FinalTime > 0 && s.Status != "DSQ" && s.Status != "DNS" && s.Status != "DNF";
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


        private int _raceTickCount = 0;
        private static readonly SolidColorBrush _firstPlaceBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
        private static readonly SolidColorBrush _runningTimeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

        private void RaceTimer_Tick(object sender, EventArgs e) {
            // 发令后一直计时，不因比赛结束而停止，直到复位信号
            if (_raceStartTime != DateTime.MinValue) {
                _runningTime = (DateTime.Now - _raceStartTime).TotalSeconds;
                _raceTickCount++;

                // 第1名成绩显示（轻量操作，每次tick都执行）
                double holdSec = _laneCloseSettings.FirstPlaceHoldTime > 0 ? _laneCloseSettings.FirstPlaceHoldTime : 3;
                if (_firstPlaceShowStart != DateTime.MinValue &&
                    (DateTime.Now - _firstPlaceShowStart).TotalSeconds < holdSec &&
                    !string.IsNullOrEmpty(_firstPlaceFinishTime)) {
                    if (RunningTimeText != null) {
                        RunningTimeText.Text = _firstPlaceFinishTime;
                        RunningTimeText.Foreground = _firstPlaceBrush;
                    }
                } else {
                    if (RunningTimeText != null) {
                        RunningTimeText.Text = TimeFormatter.FormatRunning(_runningTime);
                        RunningTimeText.Foreground = _runningTimeBrush;
                    }
                }

                // 每100ms刷新泳道动态内容（增量更新，不重建UI）
                UpdateLaneStatusDisplay();

                // 广播降频为每500ms一次（WebSocket序列化/网络发送开销大）
                if (_raceTickCount % 5 == 0) {
                    Broadcast();
                }
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e) {
            bool changed = false;
            foreach (var state in _laneDeviceStates) {
                if (state.LaneCloseCountdown > 0 && !state.IsFinished) {
                    state.LaneCloseCountdown -= 0.1;
                    if (state.LaneCloseCountdown <= 0) {
                        state.LaneCloseCountdown = 0;
                        // 空泳道或 DNS/DNF/DSQ 运动员：不自动打开任何设备
                        if (!IsLaneParticipating(state.Lane)) { changed = true; continue; }
                        // 打开运动员即将到达端的触板和盲表
                        bool arriveRight = state.Direction == "→";
                        if (arriveRight) {
                            if (!state.RightTouchpadBroken) state.RightTouchpadStatus = DeviceStatus.Open;
                            if (!state.RightBlindWatch1Broken) state.RightBlindWatch1Status = DeviceStatus.Open;
                            if (!state.RightBlindWatch2Broken) state.RightBlindWatch2Status = DeviceStatus.Open;
                            if (!state.RightBlindWatch3Broken) state.RightBlindWatch3Status = DeviceStatus.Open;
                            if (state.RightManualEnabled) state.RightManualStatus = DeviceStatus.Open;
                        } else {
                            if (!state.LeftTouchpadBroken) state.LeftTouchpadStatus = DeviceStatus.Open;
                            if (!state.LeftBlindWatch1Broken) state.LeftBlindWatch1Status = DeviceStatus.Open;
                            if (!state.LeftBlindWatch2Broken) state.LeftBlindWatch2Status = DeviceStatus.Open;
                            if (!state.LeftBlindWatch3Broken) state.LeftBlindWatch3Status = DeviceStatus.Open;
                            if (state.LeftManualEnabled) state.LeftManualStatus = DeviceStatus.Open;
                        }

                        // 触板打开 = 新分段开始：预创建空分段，后续触板/盲表/手动都写入此分段
                        PreCreateSplit(state.Lane);

                        // 接力项目：只在交接棒段打开出发端出发台
                        // 交接棒 = 每棒最后一段（运动员游回出发端触板）
                        bool relayStartBlockOpened = false;
                        if (_isRelay && state.CurrentLap > 0) {
                            int totalLapsVal = GetTotalLaps();
                            int legCnt = 4;
                            var rlMatch = System.Text.RegularExpressions.Regex.Match(_currentEvent, @"(\d+)\s*[x×]");
                            if (rlMatch.Success) legCnt = int.Parse(rlMatch.Groups[1].Value);
                            int lapsPerLegVal = totalLapsVal > 0 ? totalLapsVal / legCnt : 1;
                            // 下一段触板是否是交接棒（当前段+1 是每棒最后一段，且不是最后一棒最后一段）
                            int nextLap = state.CurrentLap + 1;
                            bool isNextExchange = (lapsPerLegVal > 0) && (nextLap % lapsPerLegVal == 0) && (nextLap < totalLapsVal);
                            if (isNextExchange) {
                                bool startLeft = _laneCloseSettings.StartPosition != "right";
                                if (startLeft) {
                                    if (!state.LeftStartBlockBroken) state.LeftStartBlockStatus = DeviceStatus.Open;
                                } else {
                                    if (!state.RightStartBlockBroken) state.RightStartBlockStatus = DeviceStatus.Open;
                                }
                                relayStartBlockOpened = true;
                            }
                        }

                        AddLog(string.Format("泳道{0} 倒计时结束，{1}端设备已打开{2}", state.Lane, arriveRight ? "右" : "左",
                            relayStartBlockOpened ? "（含出发台-交接棒检测）" : ""));
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
            if (sender != null && _timingBridge != null && _timingBridge.IsConnected) _timingBridge.SendCommand(0x21);
            Broadcast();
        }

        private void StartRace_Click(object sender, RoutedEventArgs e) {
            if (_raceState != RaceState.Ready) return;
            _raceState = RaceState.Racing;
            _raceStartTime = DateTime.Now;
            _runningTime = 0;
            // 开始新的原始数据记录
            _rawTimingLog.Clear();
            _rawTimingLog.AppendFormat("{0}\t---\tSTART\t0.000\t发令\r\n", DateTime.Now.ToString("HH:mm:ss.fff"));
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
            if (sender != null && _timingBridge != null && _timingBridge.IsConnected) _timingBridge.SendCommand(0x1C);
            Broadcast();
        }

        private void OpenAll_Click(object sender, RoutedEventArgs e) {
            foreach (var s in _laneDeviceStates) {
                s.LeftTouchpadStatus = DeviceStatus.Open;
                s.LeftBlindWatch1Status = DeviceStatus.Open;
                s.LeftBlindWatch2Status = DeviceStatus.Open;
                s.LeftBlindWatch3Status = DeviceStatus.Open;
                s.RightTouchpadStatus = DeviceStatus.Open;
                s.RightBlindWatch1Status = DeviceStatus.Open;
                s.RightBlindWatch2Status = DeviceStatus.Open;
                s.RightBlindWatch3Status = DeviceStatus.Open;
                s.LaneCloseCountdown = 0;
            }
            UpdateLaneStatusDisplay();
            AddLog("全部泳道已打开");
            Broadcast();
        }

        private void CloseAll_Click(object sender, RoutedEventArgs e) {
            foreach (var s in _laneDeviceStates) {
                s.LeftTouchpadStatus = DeviceStatus.Closed;
                s.LeftBlindWatch1Status = DeviceStatus.Closed;
                s.LeftBlindWatch2Status = DeviceStatus.Closed;
                s.LeftBlindWatch3Status = DeviceStatus.Closed;
                s.RightTouchpadStatus = DeviceStatus.Closed;
                s.RightBlindWatch1Status = DeviceStatus.Closed;
                s.RightBlindWatch2Status = DeviceStatus.Closed;
                s.RightBlindWatch3Status = DeviceStatus.Closed;
                s.LaneCloseCountdown = 0;
            }
            UpdateLaneStatusDisplay();
            AddLog("全部泳道已关闭");
            Broadcast();
        }

        private void Restart_Click(object sender, RoutedEventArgs e) {
            // 本地按钮点击时弹确认对话框，WebSocket远程调用时(sender==null)跳过
            if (sender != null) {
                var r = MessageBox.Show(
                    "确定计时器复位？\n\n本组成绩数据将被清除，\n但 DSQ/DNS/DNF 等特殊状态会保留。",
                    "计时复位", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            // ═══ 计时复位：与EXE "计时复位"按钮功能完全一致 ═══
            // 1. 停止计时器，复位状态到Waiting
            _raceState = RaceState.Waiting;
            _raceTimer.Stop();
            _countdownTimer.Stop();
            _runningTime = 0;
            _raceStartTime = DateTime.MinValue;
            _resultConfirmed = false;

            // 2. 清除所有计时显示状态（第一名成绩、分段、滚动时间）
            _firstPlaceFinishTime = "";
            _firstPlaceShowStart = DateTime.MinValue;
            _firstPlaceDetectedRank = 0;
            _laneSplitCount.Clear();
            _laneSplitShowTime.Clear();
            _laneReactionLastValue.Clear();
            _laneReactionShowTime.Clear();
            if (RunningTimeText != null) RunningTimeText.Text = "0.00";

            // 3. 清除原始计时日志
            if (_rawTimingLog != null) _rawTimingLog.Clear();

            // 4. 根据项目重置发令位置（50米→对面端）
            AutoAdjustStartPosition();

            // 5. 重置所有泳道设备状态
            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
            }

            // 6. 清除当前组的成绩数据（确认对话框已明确"数据清除"）
            var currentSwimmers = GetCurrentHeatSwimmers();
            foreach (var sw in currentSwimmers) {
                // 规则：犯规/中退/缺席等状态（DSQ/DNS/DNF）不能清除
                // 因为这些运动员按规则不能参赛或被取消资格，保留其状态（只是没有成绩）
                bool keepStatus = sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF";
                var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                if (result != null) sw.Results.Remove(result);
                if (!keepStatus) sw.Status = "";
            }

            // 7. 更新显示和广播
            UpdateRaceStateDisplay();
            UpdateLaneStatusDisplay();
            AddLog("计时复位");
            if (_timingBridge != null && _timingBridge.IsConnected) {
                _timingBridge.SendCommand(0x20); // 计时清零命令
                _timingBridge.SendCommand(0x7F); // 滚动时间 = 0
            }
            Broadcast();
        }

        private void ConfirmResult_Click(object sender, RoutedEventArgs e) {
            // 本地按钮点击时弹确认对话框，WebSocket远程调用时(sender==null)跳过
            if (sender != null) {
                string info = string.Format("{0} {1} {2} 第{3}组", _currentGender, _currentEvent, _currentStage, _currentHeat);
                var r = MessageBox.Show(
                    "确认本组成绩？\n\n" + info + "\n\n确认后成绩将锁定保存。",
                    "确认成绩", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            _countdownTimer.Stop();
            _raceState = RaceState.Finished;
            _resultConfirmed = true;

            // 将抢跳标记写入运动员Status和成绩备注
            var heatSwimmers = GetCurrentHeatSwimmers();
            foreach (var sw in heatSwimmers) {
                var stageAssign = sw.GetAssignmentForStage(_currentStage);
                int lane = stageAssign != null ? stageAssign.Lane : sw.Lane;
                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
                if (laneState != null && laneState.IsFalseStart && string.IsNullOrEmpty(sw.Status)) {
                    sw.Status = "DSQ";
                    var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                    if (result != null) result.Status = "DSQ";
                    AddLog(string.Format("  泳道{0} {1} 抢跳→DSQ", lane, sw.Name));
                }
            }

            UpdateHeatRanking();
            AutoSaveData();
            UpdateLaneStatusDisplay();
            UpdateRaceStateDisplay();

            AddLog(string.Format("★ 已确认本组成绩: {0}子 {1} {2} 第{3}组", _currentGender, _currentEvent, _currentStage, _currentHeat));

            // 保存原始计时数据
            SaveRawTimingLog();

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
                s.EventName == _currentEvent && s.Gender == _currentGender && s.CurrentStage == _currentStage &&
                !(_isRelay && s.Notes != null && s.Notes.StartsWith("接力队员"))
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
                s.CurrentStage == _currentStage &&
                !(_isRelay && s.Notes != null && s.Notes.StartsWith("接力队员"))
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
                SyncRelayHeatInfo();
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
            // 支持两种格式：
            // 1. 带性别前缀："男子100米自由泳"（旧格式，兼容EXE端赛程树）
            // 2. 纯项目名："4x100米自由泳接力"（新格式，HTML端单独发SET_GENDER）
            if (eventName.StartsWith("男子") || eventName.StartsWith("女子")) {
                _currentGender = eventName.Substring(0, 1);
                _currentEvent = eventName.Substring(2);
            } else if (eventName.StartsWith("混合子")) {
                _currentGender = "混合";
                _currentEvent = eventName.Substring(3);
            } else if (eventName.StartsWith("男") && !eventName.StartsWith("男子") && eventName.Length > 1 && !char.IsDigit(eventName[1])) {
                _currentGender = "男";
                _currentEvent = eventName.Substring(1);
            } else if (eventName.StartsWith("女") && !eventName.StartsWith("女子") && eventName.Length > 1 && !char.IsDigit(eventName[1])) {
                _currentGender = "女";
                _currentEvent = eventName.Substring(1);
            } else {
                // 纯项目名（性别已通过SET_GENDER设置）
                _currentEvent = eventName;
            }
            _isRelay = _currentEvent.Contains("接力");
            CurrentEventText.Text = _currentGender + " " + _currentEvent;
            UpdateRecordDisplay();
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

        /// <summary>
        /// 根据比赛项目自动调整发令位置：
        /// 50米项目（单程）→ 发令在终点对面端
        /// 其他项目（多程）→ 发令在终点同侧端
        /// </summary>
        private void AutoAdjustStartPosition() {
            string finish = _laneCloseSettings.FinishPosition;
            if (string.IsNullOrEmpty(finish)) finish = _laneCloseSettings.StartPosition;
            bool is50m = !string.IsNullOrEmpty(_currentEvent) && _currentEvent.StartsWith("50米");
            string newStart;
            if (is50m) {
                // 50米：出发在终点对面
                newStart = finish == "left" ? "right" : "left";
            } else {
                // 其他项目：出发在终点同侧
                newStart = finish;
            }
            if (_laneCloseSettings.StartPosition != newStart) {
                _laneCloseSettings.StartPosition = newStart;
                AddLog(string.Format("发令位置自动切换: {0}（{1}，终点在{2}端）",
                    newStart == "left" ? "左端" : "右端", is50m ? "50米单程" : "多程", finish == "left" ? "左" : "右"));
            }
        }

        private void SetCurrentHeat(int heat) {
            _currentHeat = heat;
            CurrentHeatText.Text = string.Format("第{0}组 / 共{1}组", heat, _totalHeats);
            if (PoolCurrentEventText != null)
                PoolCurrentEventText.Text = string.Format("{0} {1} {2} 第{3}组", _currentGender, _currentEvent, _currentStage, heat);
            _raceState = RaceState.Waiting;
            _resultConfirmed = false;
            _laneSplitCount.Clear();
            _laneSplitShowTime.Clear();
            _laneReactionLastValue.Clear();
            _laneReactionShowTime.Clear();
            _firstPlaceFinishTime = "";
            _firstPlaceShowStart = DateTime.MinValue;
            _firstPlaceDetectedRank = 0;
            // 切换组次 = 复位计时器
            _runningTime = 0;
            _raceStartTime = DateTime.MinValue;
            _raceTimer.Stop();
            _countdownTimer.Stop();
            if (RunningTimeText != null) RunningTimeText.Text = "0.00";

            // 根据项目自动调整发令位置（50米项目切换到对面端）
            AutoAdjustStartPosition();
            UpdateRaceStateDisplay();

            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
            }

            UpdateLaneStatusDisplay();
            UpdateRecordDisplay();
            Broadcast();
        }

        private void ScheduleTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e) {
            var item = ScheduleTree.SelectedItem as TreeViewItem;
            if (item == null || item.Tag == null) return;

            string tag = item.Tag.ToString();
            // 比赛进行中禁止切换项目/组次（防止误操作导致参数复位）
            if ((_raceState == RaceState.Ready || _raceState == RaceState.Racing) &&
                (tag.StartsWith("heat:") || tag.StartsWith("event:"))) {
                MessageBox.Show(
                    "比赛进行中不能重新选择比赛项目。\n\n如需切换，请先点击 \"计时复位\" 结束当前比赛。",
                    "操作被阻止", MessageBoxButton.OK, MessageBoxImage.Warning);
                AddLog("比赛进行中不能切换项目");
                return;
            }
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
                bool sessionAllDone = true;
                var sessionItem = new TreeViewItem {
                    Header = session.First().SessionName ?? string.Format("第{0}单元", session.Key)
                };

                foreach (var ev in session) {
                    string header = string.Format("{0} {1} {2}", ev.Gender, ev.EventName, ev.Stage);
                    bool allHeatsConfirmed = IsStageAllConfirmed(ev.Gender, ev.EventName, ev.Stage);
                    if (!allHeatsConfirmed) sessionAllDone = false;

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

                // 单元内所有项目都完赛时收起，否则展开
                sessionItem.IsExpanded = !sessionAllDone;
                if (sessionAllDone) {
                    sessionItem.Header = (session.First().SessionName ?? string.Format("第{0}单元", session.Key)) + " [已完赛]";
                    sessionItem.Foreground = new SolidColorBrush(Colors.Gray);
                }

                ScheduleTree.Items.Add(sessionItem);
            }
            RebuildScheduleGroupedView();
        }

        /// <summary>
        /// 检查某组比赛是否已有成绩（所有运动员都有成绩或标记为DNS/DNF/DSQ）
        /// </summary>
        private bool IsHeatConfirmed(string gender, string eventName, string stage, int heat) {
            bool isRelay = eventName.Contains("接力");
            var heatSwimmers = _swimmers.Where(s =>
                s.Gender == gender && s.EventName == eventName &&
                !(isRelay && s.Notes != null && s.Notes.StartsWith("接力队员"))
            ).ToList();

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
        private Dictionary<int, int> _laneSplitCount = new Dictionary<int, int>();  // 每道已显示的分段数
        private Dictionary<int, DateTime> _laneSplitShowTime = new Dictionary<int, DateTime>();  // 每道分段显示开始时间
        private Dictionary<int, double> _laneReactionLastValue = new Dictionary<int, double>();  // 每道上次记录的反应时值
        private Dictionary<int, DateTime> _laneReactionShowTime = new Dictionary<int, DateTime>();  // 每道反应时显示开始时间

        private bool _poolHeaderBuilt = false;

        private void RenderPoolHeader() {
            if (PoolHeader == null) return;
            PoolHeader.Children.Clear();
            PoolHeader.ColumnDefinitions.Clear();

            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });   // 道次
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 左发令
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });  // 左设备
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 姓名+进度
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });  // 右设备
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 右发令
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(249) });  // 成绩信息

            Action<int, string, double> addLabel = (col, text, width) => {
                var tb = new TextBlock { Text = text, Width = width, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(tb, col);
                PoolHeader.Children.Add(tb);
            };
            addLabel(0, "道", 32);

            // 左发令标志
            var leftHdrInd = new Border { Width = 8, CornerRadius = new CornerRadius(2), Background = _laneCloseSettings.StartPosition == "left" ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")) : Brushes.Transparent, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetColumn(leftHdrInd, 1); PoolHeader.Children.Add(leftHdrInd);

            var leftLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (string s in new[] { "[T]:80", "盲1:26", "盲2:26", "盲3:26", "出发:26", "触板:26", "圈:28" }) {
                string[] p = s.Split(':');
                leftLabels.Children.Add(new TextBlock { Text = p[0], Width = double.Parse(p[1]), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            Grid.SetColumn(leftLabels, 2); PoolHeader.Children.Add(leftLabels);

            var midLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            midLabels.Children.Add(new TextBlock { Text = "姓名/代表队", Width = 120, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            midLabels.Children.Add(new TextBlock { Text = "方向/进度", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(midLabels, 3); PoolHeader.Children.Add(midLabels);

            var rightLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (string s in new[] { "圈:28", "触板:26", "出发:26", "盲1:26", "盲2:26", "盲3:26", "[T]:80" }) {
                string[] p = s.Split(':');
                rightLabels.Children.Add(new TextBlock { Text = p[0], Width = double.Parse(p[1]), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            Grid.SetColumn(rightLabels, 4); PoolHeader.Children.Add(rightLabels);

            // 右发令标志
            var rightHdrInd = new Border { Width = 8, CornerRadius = new CornerRadius(2), Background = _laneCloseSettings.StartPosition == "right" ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")) : Brushes.Transparent, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetColumn(rightHdrInd, 5); PoolHeader.Children.Add(rightHdrInd);

            var infoLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (string s in new[] { "反应:55", "成绩:110", "名次:44", "备注:40" }) {
                string[] p = s.Split(':');
                infoLabels.Children.Add(new TextBlock { Text = p[0], Width = double.Parse(p[1]), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            }
            Grid.SetColumn(infoLabels, 6); PoolHeader.Children.Add(infoLabels);
        }

        // 画刷缓存：避免每次tick重复创建
        private static readonly SolidColorBrush _brushGreen = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
        private static readonly SolidColorBrush _brushRed = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
        private static readonly SolidColorBrush _brushAmber = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
        private static readonly SolidColorBrush _brushSlate = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
        private static readonly SolidColorBrush _brushDark = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
        private static readonly SolidColorBrush _brushBlue = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
        private static readonly SolidColorBrush _brushSilver = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0"));
        private static readonly SolidColorBrush _brushBronze = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CD7F32"));
        private static readonly SolidColorBrush _brushGray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        private static readonly SolidColorBrush _brushMutedText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

        private static SolidColorBrush GetDeviceBrush(DeviceStatus status) {
            switch (status) {
                case DeviceStatus.Open: return _brushGreen;
                case DeviceStatus.Broken: return _brushRed;
                case DeviceStatus.FalseStart: return _brushAmber;
                default: return _brushSlate;
            }
        }

        private Ellipse MakeLaneDot(DeviceStatus status) {
            return new Ellipse { Width = 22, Height = 22, Fill = GetDeviceBrush(status), Margin = new Thickness(2, 0, 2, 0) };
        }

        // 泳道行UI引用（动态内容增量更新用）
        private class LaneRowUI {
            public int Lane;
            public Swimmer Swimmer;
            public Border Row;
            public Button TouchL, TouchR;
            public Ellipse[] LeftDots;  // [BlindWatch1, BlindWatch2, BlindWatch3, StartBlock, Touchpad]
            public Ellipse[] RightDots; // [Touchpad, StartBlock, BlindWatch1, BlindWatch2, BlindWatch3]
            public TextBlock LeftRemainText, RightRemainText;
            public TextBlock TrackText;
            public TextBlock ReactionText, DisplayTimeText, RankText, RemarkText;
            public Border LeftSignalInd, RightSignalInd;
        }

        private List<LaneRowUI> _laneRowUIs = new List<LaneRowUI>();
        private string _laneRowsBuiltKey = "";

        private void UpdateLaneStatusDisplay() {
            if (LanePanel == null || PoolHeader == null) return;

            var currentSwimmers = GetCurrentHeatSwimmers();

            // 为所有泳道建立 lane→swimmer 映射（空泳道 swimmer 为 null）
            var laneSwimmerMap = new Dictionary<int, Swimmer>();
            foreach (var sw in currentSwimmers) {
                var sa = sw.GetAssignmentForStage(_currentStage);
                int ln = sa != null ? sa.Lane : sw.Lane;
                if (!laneSwimmerMap.ContainsKey(ln)) laneSwimmerMap[ln] = sw;
            }
            var allPoolLanes = (_poolConfig.LaneNumbers ?? new List<int>()).ToList();
            allPoolLanes.Sort();

            // 构造本次数据的key（运动员列表或泳道集合变化时需要重建UI）
            string key = _currentGender + "|" + _currentEvent + "|" + _currentStage + "|" + _currentHeat + "|" + allPoolLanes.Count + "|" + currentSwimmers.Count;
            foreach (int ln in allPoolLanes) {
                Swimmer lsw;
                laneSwimmerMap.TryGetValue(ln, out lsw);
                key += "|" + ln + ":" + (lsw != null ? lsw.Name : "(empty)");
            }

            if (key != _laneRowsBuiltKey) {
                RenderPoolHeader();
                BuildLaneRows(allPoolLanes, laneSwimmerMap);
                _laneRowsBuiltKey = key;
            }
            RefreshLaneRows(currentSwimmers);
            UpdateTimingSourceInfo();
        }

        private void BuildLaneRows(List<int> allPoolLanes, Dictionary<int, Swimmer> laneSwimmerMap) {
            LanePanel.Children.Clear();
            _laneRowUIs.Clear();
            bool isRelay = _isRelay;

            foreach (int ln in allPoolLanes) {
                int lane = ln;
                Swimmer sw;
                laneSwimmerMap.TryGetValue(lane, out sw);
                var rowUI = new LaneRowUI { Lane = lane, Swimmer = sw };

                var row = new Border {
                    Background = _brushDark,
                    CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 4),
                    Height = 48, BorderThickness = new Thickness(0)
                };
                rowUI.Row = row;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(249) });

                // Col 0: 道次（静态）
                var laneNum = new TextBlock { Text = lane.ToString(), FontSize = 18, FontWeight = FontWeights.Bold, Foreground = _brushGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(laneNum, 0); grid.Children.Add(laneNum);

                // Col 1: 左发令指示（动态：背景色）
                var leftInd = new Border { Width = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetColumn(leftInd, 1); grid.Children.Add(leftInd);
                rowUI.LeftSignalInd = leftInd;

                // Col 2: 左设备（T按钮 + 5圆点 + 剩余秒数）
                var leftDev = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                var touchL = new Button { Content = "T", Width = 80, Height = 26, FontSize = 14, BorderThickness = new Thickness(0) };
                int capLane = lane;
                touchL.PreviewMouseLeftButtonDown += delegate(object s1, System.Windows.Input.MouseButtonEventArgs e1) { e1.Handled = true; HandleTimingCommand(Newtonsoft.Json.Linq.JObject.FromObject(new { command = "MANUAL_TOUCH_LEFT", data = new { lane = capLane } })); };
                leftDev.Children.Add(touchL);
                rowUI.TouchL = touchL;

                rowUI.LeftDots = new Ellipse[5];
                for (int i = 0; i < 5; i++) {
                    var dot = new Ellipse { Width = 22, Height = 22, Margin = new Thickness(2, 0, 2, 0) };
                    rowUI.LeftDots[i] = dot;
                    leftDev.Children.Add(dot);
                }

                var leftRemainText = new TextBlock { Width = 28, FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                leftDev.Children.Add(leftRemainText);
                rowUI.LeftRemainText = leftRemainText;
                Grid.SetColumn(leftDev, 2); grid.Children.Add(leftDev);

                // Col 3: 姓名 + 进度条
                var midPanel = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                var infoStack = new StackPanel { Width = 120 };
                string dispName;
                string dispTeam;
                if (sw == null) {
                    // 空泳道
                    dispName = "（空泳道）";
                    dispTeam = "";
                    row.Opacity = 0.35;   // 整行淡化
                } else {
                    dispName = isRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:") ? sw.Country : sw.Name;
                    dispTeam = isRelay ? "" : (sw.Country ?? "");
                }
                infoStack.Children.Add(new TextBlock { Text = dispName ?? "", FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontSize = 14 });
                if (!string.IsNullOrEmpty(dispTeam)) infoStack.Children.Add(new TextBlock { Text = dispTeam, Foreground = _brushMutedText, FontSize = 12 });
                DockPanel.SetDock(infoStack, Dock.Left); midPanel.Children.Add(infoStack);

                var trackBorder = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A2332")), CornerRadius = new CornerRadius(4), Height = 22, Padding = new Thickness(4, 0, 4, 0), MinWidth = 60 };
                var trackText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                trackBorder.Child = trackText;
                midPanel.Children.Add(trackBorder);
                rowUI.TrackText = trackText;
                Grid.SetColumn(midPanel, 3); grid.Children.Add(midPanel);

                // Col 4: 右设备
                var rightDev = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var rightRemainText = new TextBlock { Width = 28, FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                rightDev.Children.Add(rightRemainText);
                rowUI.RightRemainText = rightRemainText;

                rowUI.RightDots = new Ellipse[5];
                for (int i = 0; i < 5; i++) {
                    var dot = new Ellipse { Width = 22, Height = 22, Margin = new Thickness(2, 0, 2, 0) };
                    rowUI.RightDots[i] = dot;
                    rightDev.Children.Add(dot);
                }

                var touchR = new Button { Content = "T", Width = 80, Height = 26, FontSize = 14, BorderThickness = new Thickness(0) };
                touchR.PreviewMouseLeftButtonDown += delegate(object s2, System.Windows.Input.MouseButtonEventArgs e2) { e2.Handled = true; HandleTimingCommand(Newtonsoft.Json.Linq.JObject.FromObject(new { command = "MANUAL_TOUCH_RIGHT", data = new { lane = capLane } })); };
                rightDev.Children.Add(touchR);
                rowUI.TouchR = touchR;
                Grid.SetColumn(rightDev, 4); grid.Children.Add(rightDev);

                // Col 5: 右发令指示
                var rightInd = new Border { Width = 8, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetColumn(rightInd, 5); grid.Children.Add(rightInd);
                rowUI.RightSignalInd = rightInd;

                // Col 6: 成绩信息（反应时 + 成绩 + 名次 + 备注）
                var infoArea = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var reactionText = new TextBlock { Width = 55, FontSize = 15, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") };
                infoArea.Children.Add(reactionText);
                rowUI.ReactionText = reactionText;

                var displayTimeText = new TextBlock { Width = 110, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontFamily = new FontFamily("Consolas") };
                infoArea.Children.Add(displayTimeText);
                rowUI.DisplayTimeText = displayTimeText;

                var rankText = new TextBlock { Width = 44, FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center };
                infoArea.Children.Add(rankText);
                rowUI.RankText = rankText;

                var remarkText = new TextBlock { Width = 40, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = _brushRed, TextAlignment = TextAlignment.Center };
                infoArea.Children.Add(remarkText);
                rowUI.RemarkText = remarkText;

                Grid.SetColumn(infoArea, 6); grid.Children.Add(infoArea);

                row.Child = grid;
                int clickLane = lane;
                row.MouseLeftButtonDown += delegate {
                    _selectedLane = clickLane;
                    _lastTsSplitCount = -1;
                    if (LaneInputBox != null) LaneInputBox.Text = clickLane.ToString();
                    UpdateTimingSourceInfo();
                    RefreshLaneRows(GetCurrentHeatSwimmers());
                };
                LanePanel.Children.Add(row);
                _laneRowUIs.Add(rowUI);
            }
        }

        /// <summary>
        /// 计算当前组运动员的实时分段名次
        /// 规则：DSQ/DNS/DNF 无名次；已完赛按 FinalTime；未完赛按 (有效分段数 DESC, 最后累计时间 ASC)
        /// </summary>
        private Dictionary<Swimmer, int> ComputeLiveRanks(IEnumerable<Swimmer> swimmers) {
            var liveRanks = new Dictionary<Swimmer, int>();
            var rankables = new List<Tuple<Swimmer, int, double, bool>>(); // (swimmer, splitCount, cumTime/FinalTime, finished)
            foreach (var sw2 in swimmers) {
                string st = sw2.Status ?? "";
                if (st == "DSQ" || st == "DNS" || st == "DNF") continue;
                var r2 = sw2.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                if (r2 == null) continue;

                // 查找最后一个有效分段（CumulativeTime > 0）
                int validSplits = 0;
                double lastCum = 0;
                for (int i = r2.Splits.Count - 1; i >= 0; i--) {
                    if (r2.Splits[i].CumulativeTime > 0) {
                        validSplits = r2.Splits[i].Lap;
                        lastCum = r2.Splits[i].CumulativeTime;
                        break;
                    }
                }
                bool fin = r2.FinalTime > 0;
                double sortTime = fin ? r2.FinalTime : lastCum;
                if (validSplits == 0 && !fin) continue; // 无任何分段数据
                rankables.Add(Tuple.Create(sw2, validSplits, sortTime, fin));
            }
            rankables.Sort((a, b) => {
                if (a.Item4 != b.Item4) return a.Item4 ? -1 : 1; // 已完赛优先
                if (a.Item4) return a.Item3.CompareTo(b.Item3);  // 都完赛：FinalTime升序
                int c = b.Item2.CompareTo(a.Item2);              // 分段数降序
                if (c != 0) return c;
                return a.Item3.CompareTo(b.Item3);               // 累计时间升序
            });
            for (int i = 0; i < rankables.Count; i++) liveRanks[rankables[i].Item1] = i + 1;
            return liveRanks;
        }

        private void RefreshLaneRows(List<Swimmer> currentSwimmers) {
            double splitDisplaySec = _laneCloseSettings.SplitDisplayTime > 0 ? _laneCloseSettings.SplitDisplayTime : 5;
            bool leftStart = _laneCloseSettings.StartPosition == "left";

            // 实时分段名次
            var liveRanks = ComputeLiveRanks(_laneRowUIs.Select(r => r.Swimmer).Where(s => s != null));

            foreach (var rowUI in _laneRowUIs) {
                var sw = rowUI.Swimmer;
                int lane = rowUI.Lane;
                var ls = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
                // 空泳道：仅保留淡化效果，所有状态/成绩字段清空
                if (sw == null) {
                    rowUI.Row.BorderThickness = new Thickness(0);
                    if (rowUI.LeftDots != null) for (int i = 0; i < rowUI.LeftDots.Length; i++) rowUI.LeftDots[i].Fill = _brushSlate;
                    if (rowUI.RightDots != null) for (int i = 0; i < rowUI.RightDots.Length; i++) rowUI.RightDots[i].Fill = _brushSlate;
                    if (rowUI.LeftRemainText != null) rowUI.LeftRemainText.Text = "";
                    if (rowUI.RightRemainText != null) rowUI.RightRemainText.Text = "";
                    if (rowUI.ReactionText != null) rowUI.ReactionText.Text = "";
                    if (rowUI.DisplayTimeText != null) rowUI.DisplayTimeText.Text = "";
                    if (rowUI.RankText != null) rowUI.RankText.Text = "";
                    if (rowUI.RemarkText != null) rowUI.RemarkText.Text = "";
                    if (rowUI.TrackText != null) rowUI.TrackText.Text = "";
                    if (rowUI.TouchL != null) { rowUI.TouchL.Background = _brushDark; rowUI.TouchL.Foreground = _brushSlate; }
                    if (rowUI.TouchR != null) { rowUI.TouchR.Background = _brushDark; rowUI.TouchR.Foreground = _brushSlate; }
                    continue;
                }
                var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                bool isFinished = ls != null && ls.IsFinished;
                string status = sw.Status ?? "";
                bool isDQ = status == "DSQ" || status == "DNS" || status == "DNF";
                // DSQ/DNS/DNF 行淡化
                rowUI.Row.Opacity = isDQ ? 0.45 : 1.0;

                // 行边框（抢跳/选中高亮）
                if (ls != null && ls.IsFalseStart) {
                    rowUI.Row.BorderBrush = _brushAmber;
                    rowUI.Row.BorderThickness = new Thickness(2);
                } else if (lane == _selectedLane) {
                    rowUI.Row.BorderBrush = _brushBlue;
                    rowUI.Row.BorderThickness = new Thickness(2);
                } else {
                    rowUI.Row.BorderThickness = new Thickness(0);
                }

                // 左右发令指示
                rowUI.LeftSignalInd.Background = leftStart ? (Brush)_brushGreen : Brushes.Transparent;
                rowUI.RightSignalInd.Background = !leftStart ? (Brush)_brushGreen : Brushes.Transparent;

                // 左T按钮
                bool leftManualOn = ls == null || ls.LeftManualEnabled;
                if (leftManualOn) {
                    rowUI.TouchL.Background = (ls != null && ls.LeftManualStatus == DeviceStatus.Open) ? (Brush)_brushGreen : (Brush)_brushSlate;
                    rowUI.TouchL.Foreground = Brushes.White;
                } else {
                    rowUI.TouchL.Background = _brushDark;
                    rowUI.TouchL.Foreground = _brushSlate;
                }

                // 左设备5个圆点：BlindWatch1/2/3, StartBlock, Touchpad
                if (ls != null) {
                    rowUI.LeftDots[0].Fill = GetDeviceBrush(ls.LeftBlindWatch1Status);
                    rowUI.LeftDots[1].Fill = GetDeviceBrush(ls.LeftBlindWatch2Status);
                    rowUI.LeftDots[2].Fill = GetDeviceBrush(ls.LeftBlindWatch3Status);
                    rowUI.LeftDots[3].Fill = GetDeviceBrush(ls.LeftStartBlockStatus);
                    rowUI.LeftDots[4].Fill = GetDeviceBrush(ls.LeftTouchpadStatus);
                } else {
                    for (int i = 0; i < 5; i++) rowUI.LeftDots[i].Fill = _brushSlate;
                }

                // 左剩余秒数
                int leftRemain = GetTouchRemain(ls, true);
                rowUI.LeftRemainText.Text = leftRemain > 0 ? leftRemain.ToString() : "";
                rowUI.LeftRemainText.Foreground = leftRemain > 0 ? (Brush)_brushAmber : (Brush)_brushSlate;

                // 右T按钮
                bool rightManualOn = ls == null || ls.RightManualEnabled;
                if (rightManualOn) {
                    rowUI.TouchR.Background = (ls != null && ls.RightManualStatus == DeviceStatus.Open) ? (Brush)_brushGreen : (Brush)_brushSlate;
                    rowUI.TouchR.Foreground = Brushes.White;
                } else {
                    rowUI.TouchR.Background = _brushDark;
                    rowUI.TouchR.Foreground = _brushSlate;
                }

                // 右设备5个圆点：Touchpad, StartBlock, BlindWatch1/2/3
                if (ls != null) {
                    rowUI.RightDots[0].Fill = GetDeviceBrush(ls.RightTouchpadStatus);
                    rowUI.RightDots[1].Fill = GetDeviceBrush(ls.RightStartBlockStatus);
                    rowUI.RightDots[2].Fill = GetDeviceBrush(ls.RightBlindWatch1Status);
                    rowUI.RightDots[3].Fill = GetDeviceBrush(ls.RightBlindWatch2Status);
                    rowUI.RightDots[4].Fill = GetDeviceBrush(ls.RightBlindWatch3Status);
                } else {
                    for (int i = 0; i < 5; i++) rowUI.RightDots[i].Fill = _brushSlate;
                }

                // 右剩余秒数
                int rightRemain = GetTouchRemain(ls, false);
                rowUI.RightRemainText.Text = rightRemain > 0 ? rightRemain.ToString() : "";
                rowUI.RightRemainText.Foreground = rightRemain > 0 ? (Brush)_brushAmber : (Brush)_brushSlate;

                // 方向/进度文本
                string dir = ls != null ? ls.Direction : "→";
                rowUI.TrackText.HorizontalAlignment = dir == "←" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                string arrow = dir == "←" ? "◀" : "▶";
                int maxArrows = 8;
                if (isFinished && result != null) {
                    rowUI.TrackText.Text = "== " + TimeFormatter.Format(result.FinalTime) + " ==";
                    rowUI.TrackText.Foreground = _brushAmber;
                } else if (isDQ) {
                    rowUI.TrackText.Text = status;
                    rowUI.TrackText.Foreground = _brushAmber;
                } else if (ls != null && ls.LaneCloseCountdown > 0) {
                    double closeTime = _laneCloseSettings.LaneCloseTime > 0 ? _laneCloseSettings.LaneCloseTime : 20;
                    double elapsed = closeTime - ls.LaneCloseCountdown;
                    double progress = closeTime > 0 ? elapsed / closeTime : 1;
                    int arrowCount = Math.Max(1, (int)Math.Round(progress * maxArrows));
                    if (arrowCount > maxArrows) arrowCount = maxArrows;
                    // 格式：第1个箭头 + 倒计时时间 + 尾部箭头（尾部箭头数量随进度增长）
                    int tailCount = arrowCount > 0 ? arrowCount - 1 : 0;
                    string tailArrows = new string(arrow[0], tailCount);
                    string cdText = string.Format("({0:F1}s)", ls.LaneCloseCountdown);
                    rowUI.TrackText.Text = dir == "←"
                        ? tailArrows + " " + cdText + " " + arrow
                        : arrow + " " + cdText + " " + tailArrows;
                    rowUI.TrackText.Foreground = _brushBlue;
                } else if (ls != null && (ls.CurrentLap > 0 || _raceState == RaceState.Racing)) {
                    rowUI.TrackText.Text = new string(arrow[0], maxArrows);
                    rowUI.TrackText.Foreground = dir == "←" ? (Brush)_brushGreen : (Brush)_brushBlue;
                } else {
                    rowUI.TrackText.Text = "";
                }

                // 分段/成绩显示（带停留时长）
                // 只统计有效分段（CumulativeTime > 0），跳过 PreCreateSplit 创建的空段
                SplitTime lastValidSplit = null;
                int validSplitCount = 0;
                if (result != null) {
                    for (int i = result.Splits.Count - 1; i >= 0; i--) {
                        if (result.Splits[i].CumulativeTime > 0) {
                            lastValidSplit = result.Splits[i];
                            validSplitCount = lastValidSplit.Lap;
                            break;
                        }
                    }
                }
                if (!_laneSplitCount.ContainsKey(lane)) _laneSplitCount[lane] = 0;
                if (!_laneSplitShowTime.ContainsKey(lane)) _laneSplitShowTime[lane] = DateTime.MinValue;
                if (validSplitCount > _laneSplitCount[lane]) {
                    _laneSplitCount[lane] = validSplitCount;
                    _laneSplitShowTime[lane] = DateTime.Now;
                }
                // splitVisible：分段成绩是否在显示时间窗口内（已完赛则一直显示）
                bool splitVisible = (isFinished && result != null && !isDQ) ||
                                    (!isDQ && lastValidSplit != null &&
                                     (DateTime.Now - _laneSplitShowTime[lane]).TotalSeconds < splitDisplaySec);
                string displayTime = "";
                if (splitVisible) {
                    displayTime = (isFinished && result != null && !isDQ)
                        ? TimeFormatter.Format(result.FinalTime)
                        : TimeFormatter.Format(lastValidSplit.CumulativeTime);
                }
                rowUI.DisplayTimeText.Text = displayTime;

                // 名次：与分段成绩同步显示/消隐
                int rank = 0;
                if (splitVisible) {
                    if (liveRanks.ContainsKey(sw)) rank = liveRanks[sw];
                    else if (result != null && result.Rank > 0) rank = result.Rank;
                }
                rowUI.RankText.Text = rank > 0 ? rank.ToString() : "";
                if (rank == 1) rowUI.RankText.Foreground = _brushAmber;
                else if (rank == 2) rowUI.RankText.Foreground = _brushSilver;
                else if (rank == 3) rowUI.RankText.Foreground = _brushBronze;
                else rowUI.RankText.Foreground = Brushes.White;

                // 反应时：首次显示后 splitDisplaySec 秒后消隐，已完赛则一直显示
                double reactionVal = ls != null ? ls.ReactionTime : 0;
                if (reactionVal != 0) {
                    if (!_laneReactionLastValue.ContainsKey(lane) || _laneReactionLastValue[lane] != reactionVal) {
                        _laneReactionLastValue[lane] = reactionVal;
                        _laneReactionShowTime[lane] = DateTime.Now;
                    }
                }
                bool reactionVisible = reactionVal != 0 && (
                    isFinished ||
                    (_laneReactionShowTime.ContainsKey(lane) &&
                     (DateTime.Now - _laneReactionShowTime[lane]).TotalSeconds < splitDisplaySec));
                rowUI.ReactionText.Text = reactionVisible ? reactionVal.ToString("F2") : "";

                // 备注
                rowUI.RemarkText.Text = ls != null && ls.IsFalseStart ? "DSQ" : (isDQ ? status : "");
            }
        }

        // ═══════ 计时源对比 ═══════
        private void SplitSelect_Changed(object sender, SelectionChangedEventArgs e) {
            if (TimingSourceInfo == null) return;
            ShowTimingSourceData();
        }

        private void UpdateTimingSourceInfo() {
            if (TimingSourceInfo == null || SplitSelectCombo == null) return;
            if (_selectedLane < 0) { TimingSourceInfo.Text = "点击泳道行查看计时源"; return; }

            var currentSwimmers = GetCurrentHeatSwimmers();
            Swimmer targetSw = null;
            int targetLane = _selectedLane;
            foreach (var s in currentSwimmers) {
                var sa = s.GetAssignmentForStage(_currentStage);
                int sLane = sa != null ? sa.Lane : s.Lane;
                if (sLane == targetLane) { targetSw = s; break; }
            }
            if (targetSw == null) { TimingSourceInfo.Text = ""; return; }

            var result = targetSw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            int splitCount = result != null ? result.Splits.Count : 0;

            if (splitCount != _lastTsSplitCount) {
                _lastTsSplitCount = splitCount;
                int prevIdx = SplitSelectCombo.SelectedIndex;
                SplitSelectCombo.SelectionChanged -= SplitSelect_Changed;
                SplitSelectCombo.Items.Clear();
                SplitSelectCombo.Items.Add("终点");
                if (result != null) {
                    foreach (var sp in result.Splits) {
                        SplitSelectCombo.Items.Add(string.Format("第{0}段({1}m)", sp.Lap, sp.Distance));
                    }
                }
                if (prevIdx >= 0 && prevIdx < SplitSelectCombo.Items.Count)
                    SplitSelectCombo.SelectedIndex = prevIdx;
                else
                    SplitSelectCombo.SelectedIndex = 0;
                SplitSelectCombo.SelectionChanged += SplitSelect_Changed;
            }
            ShowTimingSourceData();
        }

        private void ShowTimingSourceData() {
            if (TimingSourceInfo == null || _selectedLane < 0) return;

            var currentSwimmers = GetCurrentHeatSwimmers();
            Swimmer targetSw = null;
            foreach (var s in currentSwimmers) {
                var sa = s.GetAssignmentForStage(_currentStage);
                int sLane = sa != null ? sa.Lane : s.Lane;
                if (sLane == _selectedLane) { targetSw = s; break; }
            }
            if (targetSw == null) { TimingSourceInfo.Text = ""; return; }

            var result = targetSw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            var ls = _laneDeviceStates.FirstOrDefault(st => st.Lane == _selectedLane);
            int selIdx = SplitSelectCombo != null ? SplitSelectCombo.SelectedIndex : 0;

            var sb = new StringBuilder();
            sb.AppendFormat("道{0}  {1}\n", _selectedLane, targetSw.Name ?? "");

            if (selIdx <= 0) {
                sb.Append("【终点】\n");
                string rt = ls != null && ls.ReactionTime != 0 ? ls.ReactionTime.ToString("F2") : "-";
                sb.AppendFormat("反应时间: {0}\n", rt);
                sb.AppendFormat("触  板:  {0}\n", result != null && result.TouchpadTime > 0 ? TimeFormatter.Format(result.TouchpadTime) : "-");
                sb.AppendFormat("盲表 1:  {0}\n", result != null && result.PushButton1Time > 0 ? TimeFormatter.Format(result.PushButton1Time) : "-");
                sb.AppendFormat("盲表 2:  {0}\n", result != null && result.PushButton2Time > 0 ? TimeFormatter.Format(result.PushButton2Time) : "-");
                sb.AppendFormat("盲表 3:  {0}\n", result != null && result.PushButton3Time > 0 ? TimeFormatter.Format(result.PushButton3Time) : "-");
                string manual = "";
                if (result != null && result.Splits.Count > 0 && result.Splits.Last().ManualTouchTime > 0)
                    manual = TimeFormatter.Format(result.Splits.Last().ManualTouchTime);
                else if (ls != null) {
                    double m = _laneCloseSettings.FinishPosition == "right" ? ls.RightManualTouchTime : ls.LeftManualTouchTime;
                    if (m > 0) manual = TimeFormatter.Format(m);
                }
                sb.AppendFormat("手  动:  {0}\n", !string.IsNullOrEmpty(manual) ? manual : "-");
            } else if (result != null && selIdx - 1 < result.Splits.Count) {
                var sp = result.Splits[selIdx - 1];
                sb.AppendFormat("【第{0}段  {1}m】\n", sp.Lap, sp.Distance);
                if (ls != null && ls.ReactionTime != 0)
                    sb.AppendFormat("反应时间: {0}\n", ls.ReactionTime.ToString("F2"));
                sb.AppendFormat("触  板:  {0}\n", sp.TouchpadTime > 0 ? TimeFormatter.Format(sp.TouchpadTime) : "-");
                sb.AppendFormat("盲表 1:  {0}\n", sp.PushButton1Time > 0 ? TimeFormatter.Format(sp.PushButton1Time) : "-");
                sb.AppendFormat("盲表 2:  {0}\n", sp.PushButton2Time > 0 ? TimeFormatter.Format(sp.PushButton2Time) : "-");
                sb.AppendFormat("盲表 3:  {0}\n", sp.PushButton3Time > 0 ? TimeFormatter.Format(sp.PushButton3Time) : "-");
                sb.AppendFormat("手  动:  {0}\n", sp.ManualTouchTime > 0 ? TimeFormatter.Format(sp.ManualTouchTime) : "-");
                sb.AppendFormat("计时源:  {0}\n", !string.IsNullOrEmpty(sp.TimingSource) ? sp.TimingSource : "-");
            }
            TimingSourceInfo.Text = sb.ToString();
        }

        private void UpdateRecordDisplay() {
            if (RecordDisplayText == null) return;
            if (string.IsNullOrEmpty(_currentEvent)) { RecordDisplayText.Text = "WR: ---    CR: ---"; return; }
            string wrTime = "", crTime = "";
            // 去除所有空格，防止"50 米自由泳"与"50米自由泳"不匹配
            string evClean = _currentEvent.Replace(" ", "").Trim();
            string genClean = (_currentGender ?? "").Trim();
            foreach (var r in _records) {
                if (string.IsNullOrEmpty(r.EventName) || string.IsNullOrEmpty(r.Gender)) continue;
                string rGender = r.Gender.Trim();
                string rEvent = r.EventName.Replace(" ", "").Trim();
                bool genderMatch = rGender.StartsWith(genClean) || genClean.StartsWith(rGender);
                if (!genderMatch || rEvent != evClean) continue;
                if (r.RecordType == null || r.Time <= 0) continue;
                string rt = r.RecordType;
                if (rt.Contains("世界")) wrTime = TimeFormatter.Format(r.Time);
                if (rt.Contains("赛会") || rt.Contains("省运会")) crTime = TimeFormatter.Format(r.Time);
            }
            RecordDisplayText.Text = string.Format("WR: {0}    CR: {1}", !string.IsNullOrEmpty(wrTime) ? wrTime : "---", !string.IsNullOrEmpty(crTime) ? crTime : "---");
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

        // 取消备注：清除 DNS/DNF/DSQ 等状态，使运动员回到正常参赛状态
        private void CancelLaneNote(int lane) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer == null) { AddLog(string.Format("泳道{0} 未找到运动员", lane)); return; }

            string oldStatus = swimmer.Status ?? "";
            swimmer.Status = "";
            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState != null) {
                // 若比赛尚未结束/未有最终成绩，重置完成标志，使其继续参赛
                var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                bool hasFinalTime = result != null && result.FinalTime > 0;
                if (!hasFinalTime) laneState.IsFinished = false;
            }
            LogRawTimingData(lane, "CANCEL_NOTE", 0);
            AddLog(string.Format("泳道{0} {1} 取消备注（原 {2}），恢复正常参赛状态",
                lane, swimmer.Name, string.IsNullOrEmpty(oldStatus) ? "无" : oldStatus));
            UpdateLaneStatusDisplay();
            AutoSaveData();
            Broadcast();
        }

        // 盲表成绩：当前分段若无触板时间，使用盲表时间作为该段正式成绩
        // 逻辑：在 laneState.PendingBlind*Time 中选择一个最早的有效盲表时间，
        //       按触板到达处理（ProcessTouchpadHit 会把盲表时间清理并完成分段）
        private void UseBlindResultForCurrentSegment(int lane) {
            var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (laneState == null) { AddLog(string.Format("泳道{0} 状态不存在", lane)); return; }

            // 选择一个可用盲表时间：优先中位数；若只有一个则取之
            var blinds = new List<double>();
            if (laneState.PendingBlind1Time > 0) blinds.Add(laneState.PendingBlind1Time);
            if (laneState.PendingBlind2Time > 0) blinds.Add(laneState.PendingBlind2Time);
            if (laneState.PendingBlind3Time > 0) blinds.Add(laneState.PendingBlind3Time);
            if (blinds.Count == 0) {
                AddLog(string.Format("泳道{0} 无盲表数据可用", lane));
                return;
            }
            blinds.Sort();
            double blindTime = blinds[blinds.Count / 2]; // 中位数

            // 检查当前段是否已有触板时间（若有则不覆盖）
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer != null) {
                var res = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                int curLap = laneState.CurrentLap + 1;
                if (res != null) {
                    var sp = res.Splits.FirstOrDefault(x => x.Lap == curLap);
                    if (sp != null && sp.TouchpadTime > 0) {
                        AddLog(string.Format("泳道{0} 当前分段已有触板成绩，盲表成绩补充操作跳过", lane));
                        return;
                    }
                }
            }

            AddLog(string.Format("泳道{0} 使用盲表成绩 {1} 作为当前分段正式成绩",
                lane, TimeFormatter.Format(blindTime)));
            LogRawTimingData(lane, "USE_BLIND_RESULT", blindTime);

            // 按触板处理（ProcessTouchpadHit 会把 PendingBlind 写入分段后清空）
            ProcessTouchpadHit(lane, blindTime, laneState);
            // 恢复泳道到正常参赛状态：不自动关闭
            laneState.LaneCloseCountdown = 0;
            UpdateLaneStatusDisplay();
            Broadcast();
        }

        // 道次打开 / 道次关闭：只对指定泳道生效（单道版本的 OpenAll/CloseAll）
        private void SetSingleLaneOpen(int lane, bool open) {
            var s = _laneDeviceStates.FirstOrDefault(x => x.Lane == lane);
            if (s == null) { AddLog(string.Format("泳道{0} 状态不存在", lane)); return; }
            DeviceStatus st = open ? DeviceStatus.Open : DeviceStatus.Closed;
            s.LeftTouchpadStatus = st;
            s.LeftBlindWatch1Status = st; s.LeftBlindWatch2Status = st; s.LeftBlindWatch3Status = st;
            s.LeftStartBlockStatus = st;
            s.RightTouchpadStatus = st;
            s.RightBlindWatch1Status = st; s.RightBlindWatch2Status = st; s.RightBlindWatch3Status = st;
            s.RightStartBlockStatus = st;
            s.LaneCloseCountdown = 0;
            AddLog(string.Format("泳道{0} 已{1}", lane, open ? "打开" : "关闭"));
            UpdateLaneStatusDisplay();
            Broadcast();
        }

        private void MarkLaneStatus(int lane, string status) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
            if (swimmer != null) {
                swimmer.Status = status;
                LogRawTimingData(lane, "MARK_" + status, 0);
                AddLog(string.Format("泳道{0} {1} 标记为 {2}", lane, swimmer.Name, status));
                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
                if (laneState != null) laneState.IsFinished = true;
                UpdateLaneStatusDisplay();
                AutoSaveData();
                Broadcast();
            }
        }

        private void OverrideLaneTime(int lane, double time) {
            LogRawTimingData(lane, "ManualOverride", time);
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => s.Lane == lane);
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

        private bool ConfirmMarkStatus(int lane, string status, string desc) {
            var r = MessageBox.Show(
                string.Format("确认将泳道 {0} 标记为 {1}（{2}）？\n\n此操作将取消该泳道的成绩。",
                    lane, status, desc),
                "确认标记", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return r == MessageBoxResult.Yes;
        }

        private void MarkDNS_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            if (!ConfirmMarkStatus(lane, "DNS", "缺席未出发")) return;
            MarkLaneStatus(lane, "DNS");
        }

        private void MarkDNF_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            if (!ConfirmMarkStatus(lane, "DNF", "中途退出")) return;
            MarkLaneStatus(lane, "DNF");
        }

        private void MarkDSQ_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            if (!ConfirmMarkStatus(lane, "DSQ", "犯规取消资格")) return;
            MarkLaneStatus(lane, "DSQ");
        }

        private void CancelNote_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            CancelLaneNote(lane);
        }

        private void BlindResult_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            UseBlindResultForCurrentSegment(lane);
        }

        private void LaneOpen_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            SetSingleLaneOpen(lane, true);
        }

        private void LaneClose_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            SetSingleLaneOpen(lane, false);
        }

        private void ManualTime_Click(object sender, RoutedEventArgs e) {
            int lane;
            if (!int.TryParse(LaneInputBox.Text, out lane)) { AddLog("请输入泳道号"); return; }
            var dlg = new Window {
                Title = string.Format("手动输入成绩 — 泳道{0}", lane), Width = 320, Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "请输入成绩（如 49.23 或 1:23.45）:" });
            var tb = new TextBox { Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(4), FontSize = 14 };
            sp.Children.Add(tb);
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOK = new Button { Content = "确定", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 6, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 4, 16, 4) };
            btnOK.Click += delegate { dlg.DialogResult = true; };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnRow.Children.Add(btnOK);
            btnRow.Children.Add(btnCancel);
            sp.Children.Add(btnRow);
            dlg.Content = sp;
            tb.Focus();
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(tb.Text)) {
                double time = TimeFormatter.Parse(tb.Text.Trim());
                if (time <= 0) {
                    MessageBox.Show("成绩格式无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // 确认对话框
                var r = MessageBox.Show(
                    string.Format("确认将泳道 {0} 的成绩手动输入为 {1}？\n\n此操作将写入数据库。",
                        lane, TimeFormatter.Format(time)),
                    "确认手动输入", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                OverrideLaneTime(lane, time);
            }
        }

        private void TimingSettings_Click(object sender, RoutedEventArgs e) {
            var dlg = new Window {
                Title = "参数设置",
                Width = 420, Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "参数设置", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14) });

            var tbCloseTime = AddSettingsRow(sp, "泳道关闭时间", _laneCloseSettings.LaneCloseTime.ToString(), "秒");
            var tbSBDelay = AddSettingsRow(sp, "出发台关闭延迟", _laneCloseSettings.StartBlockCloseDelay.ToString(), "秒");
            var tbConfDelay = AddSettingsRow(sp, "成绩确认关闭延迟", _laneCloseSettings.ResultConfirmCloseDelay.ToString(), "秒");
            var tbFSThresh = AddSettingsRow(sp, "抢跳判定阈值", _laneCloseSettings.FalseStartThreshold.ToString(), "秒");
            var tbSplitDisp = AddSettingsRow(sp, "分段成绩显示时长", _laneCloseSettings.SplitDisplayTime.ToString(), "秒");
            var tbFirstHold = AddSettingsRow(sp, "第1名成绩停留时间", _laneCloseSettings.FirstPlaceHoldTime.ToString(), "秒");

            // 终点位置
            var finishRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            finishRow.Children.Add(new TextBlock { Text = "终点位置", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbLeft = new RadioButton { Content = "左端", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneCloseSettings.FinishPosition == "left", GroupName = "FinPos", Margin = new Thickness(0, 0, 12, 0) };
            var rbRight = new RadioButton { Content = "右端", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneCloseSettings.FinishPosition == "right", GroupName = "FinPos" };
            finishRow.Children.Add(rbLeft);
            finishRow.Children.Add(rbRight);
            sp.Children.Add(finishRow);

            // 设备状态管理按钮
            var deviceSep = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 10, 0, 0) };
            var btnDeviceMgr = new Button { Content = "设备状态管理", Padding = new Thickness(0, 8, 0, 8), FontSize = 14, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnDeviceMgr.Click += delegate { dlg.DialogResult = false; DeviceStatus_Click(null, null); };
            var btnManualMgr = new Button { Content = "手动按键管理", Padding = new Thickness(0, 8, 0, 8), FontSize = 14, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")), Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 6, 0, 0) };
            btnManualMgr.Click += delegate { dlg.DialogResult = false; OpenManualButtonManager(); };
            var deviceStack = new StackPanel();
            deviceStack.Children.Add(btnDeviceMgr);
            deviceStack.Children.Add(btnManualMgr);
            deviceSep.Child = deviceStack;
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

            if (dlg.ShowDialog() == true) {
                double v;
                if (double.TryParse(tbCloseTime.Text, out v)) _laneCloseSettings.LaneCloseTime = v;
                if (double.TryParse(tbSBDelay.Text, out v)) _laneCloseSettings.StartBlockCloseDelay = v;
                if (double.TryParse(tbConfDelay.Text, out v)) _laneCloseSettings.ResultConfirmCloseDelay = v;
                if (double.TryParse(tbFSThresh.Text, out v)) _laneCloseSettings.FalseStartThreshold = v;
                if (double.TryParse(tbSplitDisp.Text, out v)) _laneCloseSettings.SplitDisplayTime = v;
                if (double.TryParse(tbFirstHold.Text, out v)) _laneCloseSettings.FirstPlaceHoldTime = v;
                string newFinish = rbRight.IsChecked == true ? "right" : "left";
                _laneCloseSettings.FinishPosition = newFinish;
                _laneCloseSettings.StartPosition = newFinish;
                AutoAdjustStartPosition();
                if (_raceState == RaceState.Waiting || _raceState == RaceState.Ready) {
                    foreach (var st in _laneDeviceStates) st.ResetForNewRace(_laneCloseSettings.StartPosition);
                }
                foreach (var st in _laneDeviceStates) st.LaneCloseTime = 0;
                SaveTimingSettings();
                AutoSaveData();
                UpdateLaneStatusDisplay();
                Broadcast();
                AddLog(string.Format("参数更新: 关闭{0}s 出发台{1}s 确认{2}s 抢跳{3}s 分段{4}s 终点:{5}",
                    _laneCloseSettings.LaneCloseTime, _laneCloseSettings.StartBlockCloseDelay,
                    _laneCloseSettings.ResultConfirmCloseDelay, _laneCloseSettings.FalseStartThreshold,
                    _laneCloseSettings.SplitDisplayTime, _laneCloseSettings.FinishPosition == "left" ? "左端" : "右端"));
            }
        }

        private TextBox AddSettingsRow(StackPanel parent, string label, string value, string unit) {
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

        private void OpenManualButtonManager() {
            var dlg = new Window {
                Title = "手动按键管理", Width = 500, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.CanResize
            };

            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            mainGrid.Children.Add(new TextBlock { Text = "手动按键 用/不用 设置", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });

            var dataGrid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false,
                IsReadOnly = false, FontSize = 14, RowHeight = 30,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                HeadersVisibility = DataGridHeadersVisibility.Column
            };

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "道次", Binding = new System.Windows.Data.Binding("Lane"), Width = new DataGridLength(60), IsReadOnly = true });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "左手动 用", Binding = new System.Windows.Data.Binding("LeftEnabled"), Width = new DataGridLength(100) });
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "右手动 用", Binding = new System.Windows.Data.Binding("RightEnabled"), Width = new DataGridLength(100) });

            var items = new List<ManualBtnItem>();
            foreach (var ls in _laneDeviceStates) {
                items.Add(new ManualBtnItem { Lane = ls.Lane, LeftEnabled = ls.LeftManualEnabled, RightEnabled = ls.RightManualEnabled });
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
                for (int i = 0; i < items.Count && i < _laneDeviceStates.Count; i++) {
                    _laneDeviceStates[i].LeftManualEnabled = items[i].LeftEnabled;
                    _laneDeviceStates[i].RightManualEnabled = items[i].RightEnabled;
                    if (!items[i].LeftEnabled) _laneDeviceStates[i].LeftManualStatus = DeviceStatus.Closed;
                    if (!items[i].RightEnabled) _laneDeviceStates[i].RightManualStatus = DeviceStatus.Closed;
                }
                UpdateLaneStatusDisplay();
                Broadcast();
                AddLog("手动按键设置已更新");
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

        private class ManualBtnItem {
            public int Lane { get; set; }
            public bool LeftEnabled { get; set; }
            public bool RightEnabled { get; set; }
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
            RefreshOverviewStats();
        }

        private void DeleteSwimmer_Click(object sender, RoutedEventArgs e) {
            var selected = SwimmerGrid.SelectedItem as Swimmer;
            if (selected != null) {
                if (MessageBox.Show(string.Format("确定删除运动员 {0}({1}) 的 {2} 报名记录？", selected.Name, selected.BibNumber, selected.EventName),
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                    _swimmers.Remove(selected);
                    AutoSaveData();
                    RefreshOverviewStats();
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
                    // 自动检测编码：有UTF-8 BOM用UTF-8，否则用系统默认编码（中文Windows为GBK）
                    Encoding csvEncoding = Encoding.Default;
                    byte[] rawBytes = File.ReadAllBytes(dlg.FileName);
                    if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                        csvEncoding = Encoding.UTF8;
                    string[] lines = File.ReadAllLines(dlg.FileName, csvEncoding);
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
                    RefreshOverviewStats();
                    Broadcast();
                } catch (Exception ex) {
                    AddLog("CSV导入失败: " + ex.Message);
                }
            }
        }

        private void AddRelay_Click(object sender, RoutedEventArgs e) {
            // 弹出对话框，让用户填写队名/项目/性别/报名成绩/各棒队员，
            // 确认后完整创建 RelayTeam + _swimmers 代表条目 + 队员子条目
            var relayEvents = _events.Where(ev => ev.Contains("接力")).ToList();
            if (relayEvents.Count == 0) {
                MessageBox.Show("没有可用的接力项目。请检查项目列表。", "提示"); return;
            }

            var dlg = new Window {
                Title = "添加接力队", Width = 480, Height = 540,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "填写接力队信息：", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

            // 项目
            var rowEvent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            rowEvent.Children.Add(new TextBlock { Text = "项目:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var cbEvent = new ComboBox { Width = 320 };
            foreach (var ev in relayEvents) cbEvent.Items.Add(ev);
            cbEvent.SelectedIndex = 0;
            rowEvent.Children.Add(cbEvent);
            sp.Children.Add(rowEvent);

            // 性别
            var rowGender = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            rowGender.Children.Add(new TextBlock { Text = "性别:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var cbGender = new ComboBox { Width = 320 };
            cbGender.Items.Add("男"); cbGender.Items.Add("女"); cbGender.Items.Add("混合");
            cbGender.SelectedIndex = 0;
            rowGender.Children.Add(cbGender);
            sp.Children.Add(rowGender);

            // 队名
            var rowTeam = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            rowTeam.Children.Add(new TextBlock { Text = "队名:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var tbTeam = new TextBox { Width = 320, Padding = new Thickness(4) };
            rowTeam.Children.Add(tbTeam);
            sp.Children.Add(rowTeam);

            // 报名成绩
            var rowEntry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            rowEntry.Children.Add(new TextBlock { Text = "报名成绩:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var tbEntryTime = new TextBox { Width = 320, Padding = new Thickness(4), ToolTip = "如 4:10.20 或 250.20，可留空" };
            rowEntry.Children.Add(tbEntryTime);
            sp.Children.Add(rowEntry);

            // 棒次行容器（根据项目名自动调整棒数）
            var legsTitle = new TextBlock { Margin = new Thickness(0, 10, 0, 4), FontSize = 13, FontWeight = FontWeights.Bold };
            sp.Children.Add(legsTitle);
            var legsPanel = new StackPanel();
            sp.Children.Add(legsPanel);

            var legNameBoxes = new List<TextBox>();
            var legBibBoxes = new List<TextBox>();

            Action rebuildLegs = delegate {
                legsPanel.Children.Clear();
                legNameBoxes.Clear();
                legBibBoxes.Clear();
                string evName = cbEvent.SelectedItem != null ? cbEvent.SelectedItem.ToString() : "";
                int legCount = 4;
                var mm = System.Text.RegularExpressions.Regex.Match(evName, @"(\d+)\s*[x×*]");
                if (mm.Success) { int n; if (int.TryParse(mm.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n; }
                legsTitle.Text = string.Format("队员（共{0}棒，可留空，后续再补）:", legCount);
                for (int i = 0; i < legCount; i++) {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                    row.Children.Add(new TextBlock { Text = string.Format("第{0}棒:", i + 1), Width = 55, VerticalAlignment = VerticalAlignment.Center });
                    var tbLegName = new TextBox { Width = 180, Padding = new Thickness(4) };
                    var tbLegBib = new TextBox { Width = 100, Padding = new Thickness(4), Margin = new Thickness(8, 0, 0, 0), ToolTip = "队员号码（可空）" };
                    row.Children.Add(new TextBlock { Text = "姓名", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(tbLegName);
                    row.Children.Add(new TextBlock { Text = "号码", Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(tbLegBib);
                    legsPanel.Children.Add(row);
                    legNameBoxes.Add(tbLegName);
                    legBibBoxes.Add(tbLegBib);
                }
            };
            cbEvent.SelectionChanged += delegate { rebuildLegs(); };
            rebuildLegs();

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var btnOk = new Button { Content = "确定添加", Padding = new Thickness(16, 6, 16, 6), FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);

            dlg.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            if (dlg.ShowDialog() != true) return;

            string eventName = cbEvent.SelectedItem != null ? cbEvent.SelectedItem.ToString() : "";
            string gender = cbGender.SelectedItem != null ? cbGender.SelectedItem.ToString() : "男";
            string teamName = (tbTeam.Text ?? "").Trim();
            string entryTimeStr = (tbEntryTime.Text ?? "").Trim();

            if (string.IsNullOrEmpty(eventName)) { MessageBox.Show("请选择接力项目"); return; }
            if (string.IsNullOrEmpty(teamName)) { MessageBox.Show("队名不能为空"); return; }
            if (_relayTeams.Any(t => t.EventName == eventName && t.TeamName == teamName && t.Gender == gender)) {
                MessageBox.Show(string.Format("代表队 \"{0}\" 已在该项目（{1} {2}）报名过接力队。", teamName, gender, eventName)); return;
            }

            double entrySec = 0;
            if (!string.IsNullOrEmpty(entryTimeStr)) entrySec = TimeFormatter.Parse(entryTimeStr);

            var team = new RelayTeam {
                TeamName = teamName, EventName = eventName, Gender = gender,
                EntryTime = entryTimeStr, EntryTimeSeconds = entrySec
            };
            for (int i = 0; i < legNameBoxes.Count; i++) {
                string ln = (legNameBoxes[i].Text ?? "").Trim();
                string lb = (legBibBoxes[i].Text ?? "").Trim();
                team.Legs.Add(new RelayLeg { LegOrder = i + 1, SwimmerName = ln, SwimmerBibNumber = lb, SwimmerIDNumber = "" });
            }
            _relayTeams.Add(team);

            // 代表队条目（用于分组/成绩流程）
            string legNames = "";
            foreach (var leg in team.Legs) legNames += (legNames.Length > 0 ? "," : "") + (leg.SwimmerName ?? "");
            string teamBib = "R" + (_relayTeams.Count).ToString("D3");
            while (_swimmers.Any(s => s.BibNumber == teamBib)) teamBib = "R" + DateTime.Now.Ticks.ToString().Substring(10);

            _swimmers.Add(new Swimmer {
                BibNumber = teamBib, Name = teamName, Gender = gender, Country = teamName,
                EventName = eventName,
                EntryTime = entryTimeStr, EntryTimeSeconds = entrySec,
                Notes = string.Format("接力队 棒次:{0}", legNames)
            });

            // 队员子条目
            foreach (var leg in team.Legs) {
                if (string.IsNullOrEmpty(leg.SwimmerName)) continue;
                string memBib = !string.IsNullOrEmpty(leg.SwimmerBibNumber) ? leg.SwimmerBibNumber : (teamBib + "-" + leg.LegOrder);
                if (_swimmers.Any(s => s.BibNumber == memBib)) continue;
                _swimmers.Add(new Swimmer {
                    BibNumber = memBib, Name = leg.SwimmerName,
                    Gender = gender == "混合" ? "男" : gender, Country = teamName,
                    IDNumber = leg.SwimmerIDNumber ?? "",
                    EventName = eventName,
                    Notes = string.Format("接力队员 {0} 第{1}棒", eventName, leg.LegOrder)
                });
            }

            AutoSaveData();
            RebuildRelayGroupedView();
            AddLog(string.Format("手动添加接力队: {0} ({1} {2}) {3}棒", teamName, gender, eventName, team.Legs.Count));
        }

        private void DeleteRelay_Click(object sender, RoutedEventArgs e) {
            var selected = _selectedRelayTeam;
            if (selected == null) { MessageBox.Show("请先选中一支接力队"); return; }
            if (MessageBox.Show(string.Format("确定删除接力队 [{0}] ({1})？", selected.TeamName, selected.EventName), "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                // 同时删除_swimmers中的代表条目
                var proxy = _swimmers.FirstOrDefault(s => s.Name == selected.TeamName && s.EventName == selected.EventName && s.Gender == selected.Gender && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队"));
                if (proxy != null) _swimmers.Remove(proxy);
                _relayTeams.Remove(selected);
                _selectedRelayTeam = null;
                if (RelayLegGrid != null) RelayLegGrid.ItemsSource = null;
                RelayLegTitle.Text = "棒次安排（请选中一支接力队）";
                RebuildRelayGroupedView();
                AutoSaveData();
            }
        }

        private void RelayGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var selected = _selectedRelayTeam;
            if (selected == null) {
                if (RelayLegGrid != null) RelayLegGrid.ItemsSource = null;
                RelayLegTitle.Text = "棒次安排（请选中一支接力队）";
                return;
            }
            // 自动补全队员号码 / 身份证号
            foreach (var leg in selected.Legs) {
                if (string.IsNullOrEmpty(leg.SwimmerName)) continue;
                var match = _swimmers.FirstOrDefault(s => s.Name == leg.SwimmerName && s.Country == selected.TeamName);
                if (match == null) continue;
                if (string.IsNullOrEmpty(leg.SwimmerBibNumber) && !string.IsNullOrEmpty(match.BibNumber))
                    leg.SwimmerBibNumber = match.BibNumber;
                if (string.IsNullOrEmpty(leg.SwimmerIDNumber) && !string.IsNullOrEmpty(match.IDNumber))
                    leg.SwimmerIDNumber = match.IDNumber;
            }
            RelayLegTitle.Text = string.Format("{0} — {1} 棒次安排（{2}人）", selected.TeamName, selected.EventName, selected.Legs.Count);
            RelayLegGrid.ItemsSource = selected.Legs;
        }

        private void RelayLegUp_Click(object sender, RoutedEventArgs e) {
            var team = _selectedRelayTeam;
            if (team == null) { MessageBox.Show("请先选中一支接力队"); return; }
            int idx = RelayLegGrid.SelectedIndex;
            if (idx <= 0) return;
            // 交换棒次
            var leg1 = team.Legs[idx];
            var leg2 = team.Legs[idx - 1];
            int tmpOrder = leg1.LegOrder;
            leg1.LegOrder = leg2.LegOrder;
            leg2.LegOrder = tmpOrder;
            team.Legs.Move(idx, idx - 1);
            RelayLegGrid.SelectedIndex = idx - 1;
        }

        private void RelayLegDown_Click(object sender, RoutedEventArgs e) {
            var team = _selectedRelayTeam;
            if (team == null) { MessageBox.Show("请先选中一支接力队"); return; }
            int idx = RelayLegGrid.SelectedIndex;
            if (idx < 0 || idx >= team.Legs.Count - 1) return;
            var leg1 = team.Legs[idx];
            var leg2 = team.Legs[idx + 1];
            int tmpOrder = leg1.LegOrder;
            leg1.LegOrder = leg2.LegOrder;
            leg2.LegOrder = tmpOrder;
            team.Legs.Move(idx, idx + 1);
            RelayLegGrid.SelectedIndex = idx + 1;
        }

        private void RelayLegReplace_Click(object sender, RoutedEventArgs e) {
            var team = _selectedRelayTeam;
            if (team == null) { MessageBox.Show("请先选中一支接力队"); return; }
            var leg = RelayLegGrid.SelectedItem as RelayLeg;
            if (leg == null) { MessageBox.Show("请在棒次表中选中要更换的队员"); return; }

            // 确定被更换队员的性别
            string legGender = "";
            if (!string.IsNullOrEmpty(leg.SwimmerName)) {
                var currentMember = _swimmers.FirstOrDefault(s => s.Name == leg.SwimmerName && s.Country == team.TeamName);
                if (currentMember != null) legGender = currentMember.Gender;
            }
            // 非混合接力：性别和队伍一致
            if (team.Gender != "混合") legGender = team.Gender;

            // 查找该代表队的可替换运动员（同队名、同性别，排除已在棒次中的）
            var existingNames = new HashSet<string>();
            foreach (var l in team.Legs) {
                if (!string.IsNullOrEmpty(l.SwimmerName)) existingNames.Add(l.SwimmerName);
            }
            var teamMembers = _swimmers.Where(s =>
                (s.Country == team.TeamName || s.Name == team.TeamName) &&
                !string.IsNullOrEmpty(s.Name) &&
                !existingNames.Contains(s.Name) &&
                (string.IsNullOrEmpty(s.Notes) || !s.Notes.StartsWith("接力队 ")) &&
                (string.IsNullOrEmpty(legGender) || s.Gender == legGender)
            ).ToList();

            string genderLabel = !string.IsNullOrEmpty(legGender) ? "（" + legGender + "）" : "";

            var dlg = new Window {
                Title = string.Format("更换第{0}棒队员{1} — {2}", leg.LegOrder, genderLabel, team.TeamName),
                Width = 400, Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock {
                Text = string.Format("当前第{0}棒: {1} {2}", leg.LegOrder, leg.SwimmerName, genderLabel),
                FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10)
            });

            // 从本队运动员中选择
            sp.Children.Add(new TextBlock { Text = "从本队运动员中选择:", FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            var memberList = new ListBox { Height = 120, FontSize = 13 };
            foreach (var m in teamMembers) {
                memberList.Items.Add(string.Format("{0}  ({1})  {2}", m.Name, m.BibNumber ?? "", m.Gender));
            }
            if (teamMembers.Count == 0) {
                memberList.Items.Add("（无可选队员，请手动输入）");
                memberList.IsEnabled = false;
            }
            sp.Children.Add(memberList);

            // 分隔线
            sp.Children.Add(new TextBlock { Text = "— 或手动输入 —", HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 8, 0, 4) });

            var inputPanel = new Grid();
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            var tbName = new TextBox { Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 0) };
            tbName.SetValue(Grid.ColumnProperty, 0);
            var tbID = new TextBox { Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 0) };
            tbID.SetValue(Grid.ColumnProperty, 1);
            var tbBib = new TextBox { Padding = new Thickness(4) };
            tbBib.SetValue(Grid.ColumnProperty, 2);
            inputPanel.Children.Add(tbName);
            inputPanel.Children.Add(tbID);
            inputPanel.Children.Add(tbBib);

            var labelPanel = new Grid();
            labelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            labelPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            var lb1 = new TextBlock { Text = "姓名:", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) };
            lb1.SetValue(Grid.ColumnProperty, 0);
            var lb2 = new TextBlock { Text = "身份证号:", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) };
            lb2.SetValue(Grid.ColumnProperty, 1);
            var lb3 = new TextBlock { Text = "参赛号:", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) };
            lb3.SetValue(Grid.ColumnProperty, 2);
            labelPanel.Children.Add(lb1);
            labelPanel.Children.Add(lb2);
            labelPanel.Children.Add(lb3);
            sp.Children.Add(labelPanel);
            sp.Children.Add(inputPanel);

            // 提示条
            var hintBorder = new Border {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 6, 0, 0)
            };
            hintBorder.Child = new TextBlock {
                Text = "提示：姓名需与身份证一致；身份证号为 18 位数字；参赛号由报名时分配",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E")),
                FontSize = 11, TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(hintBorder);

            // 选择列表项时自动填入
            memberList.SelectionChanged += delegate {
                int selIdx = memberList.SelectedIndex;
                if (selIdx >= 0 && selIdx < teamMembers.Count) {
                    tbName.Text = teamMembers[selIdx].Name;
                    tbBib.Text = teamMembers[selIdx].BibNumber ?? "";
                    tbID.Text = teamMembers[selIdx].IDNumber ?? "";
                }
            };

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var btnOk = new Button {
                Content = "确定更换", Padding = new Thickness(16, 6, 16, 6), FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0)
            };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnCancel = new Button {
                Content = "关闭", Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0)
            };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(tbName.Text.Trim())) {
                string oldName = leg.SwimmerName;
                leg.SwimmerName = tbName.Text.Trim();
                leg.SwimmerBibNumber = tbBib.Text.Trim();
                leg.SwimmerIDNumber = tbID.Text.Trim();
                RelayLegGrid.Items.Refresh();
                AddLog(string.Format("接力队 {0} 第{1}棒: {2} → {3}", team.TeamName, leg.LegOrder, oldName, leg.SwimmerName));
            }
        }

        private void RelayLegSave_Click(object sender, RoutedEventArgs e) {
            var team = _selectedRelayTeam;
            if (team == null) { MessageBox.Show("请先选中一支接力队"); return; }
            // 更新_swimmers中代表条目的Notes
            var proxy = _swimmers.FirstOrDefault(s => s.Name == team.TeamName && s.EventName == team.EventName && s.Gender == team.Gender && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队"));
            if (proxy != null) {
                string legNames = "";
                foreach (var leg in team.Legs) legNames += (legNames.Length > 0 ? "," : "") + leg.SwimmerName;
                proxy.Notes = string.Format("接力队 棒次:{0}", legNames);
            }
            // 同步每位队员的 Swimmer 子条目：身份证号/号码
            string teamBib = proxy != null ? (proxy.BibNumber ?? "") : "";
            foreach (var leg in team.Legs) {
                if (string.IsNullOrEmpty(leg.SwimmerName)) continue;
                string memBib = !string.IsNullOrEmpty(leg.SwimmerBibNumber) ? leg.SwimmerBibNumber : teamBib + "-" + leg.LegOrder;
                var memExisting = _swimmers.FirstOrDefault(s =>
                    s.Notes != null && s.Notes.StartsWith("接力队员")
                    && s.EventName == team.EventName && s.Country == team.TeamName
                    && (s.Name == leg.SwimmerName || s.BibNumber == memBib));
                if (memExisting != null) {
                    if (!string.IsNullOrEmpty(leg.SwimmerIDNumber)) memExisting.IDNumber = leg.SwimmerIDNumber;
                    if (!string.IsNullOrEmpty(leg.SwimmerBibNumber)) memExisting.BibNumber = leg.SwimmerBibNumber;
                    if (!string.IsNullOrEmpty(leg.SwimmerName)) memExisting.Name = leg.SwimmerName;
                } else {
                    _swimmers.Add(new Swimmer {
                        BibNumber = memBib,
                        Name = leg.SwimmerName,
                        Gender = team.Gender == "混合" ? "男" : team.Gender,
                        Country = team.TeamName,
                        IDNumber = leg.SwimmerIDNumber ?? "",
                        EventName = team.EventName,
                        Notes = string.Format("接力队员 {0} 第{1}棒", team.EventName, leg.LegOrder)
                    });
                }
            }
            // 刷新上方分组视图
            RebuildRelayGroupedView();
            AutoSaveData();
            Broadcast();
            MessageBox.Show("接力队棒次修改已保存！", "保存成功");
        }

        /// <summary>
        /// 同步接力队的分组信息：从_swimmers代表条目同步Heat/Lane/Stage到_relayTeams
        /// </summary>
        private void SyncRelayHeatInfo_Click(object sender, RoutedEventArgs e) {
            SyncRelayHeatInfo();
            RebuildRelayGroupedView();
            MessageBox.Show(string.Format("已同步接力队的组数和道次信息。"), "同步完成");
        }

        /// <summary>
        /// 同步接力队的分组信息：从_swimmers代表条目同步Heat/Lane/Stage到_relayTeams
        /// </summary>
        private void SyncRelayHeatInfo() {
            int synced = 0;
            foreach (var team in _relayTeams) {
                var proxy = _swimmers.FirstOrDefault(s => s.Name == team.TeamName && s.EventName == team.EventName && s.Gender == team.Gender && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队"));
                if (proxy != null) {
                    team.Heat = proxy.Heat;
                    team.Lane = proxy.Lane;
                    team.Stage = proxy.CurrentStage;
                    synced++;
                }
            }
            if (RelayGrid != null) RelayGrid.Items.Refresh();
            RebuildRelayGroupedView();
            if (synced > 0) AddLog(string.Format("已同步{0}支接力队的分组信息", synced));
        }

        private RelayTeam _selectedRelayTeam;

        /// <summary>
        /// 按接力项目分组显示接力队列表（类似赛程管理的单元分组）
        /// </summary>
        private void RebuildRelayGroupedView() {
            if (RelayGroupedPanel == null) return;
            RelayGroupedPanel.Children.Clear();

            if (_relayTeams.Count == 0) {
                RelayGroupedPanel.Children.Add(new TextBlock {
                    Text = "暂无接力队。请通过测试机器人或手动添加接力队。",
                    Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(10), FontSize = 13
                });
                return;
            }

            // 按 性别+项目 分组
            var groups = _relayTeams.GroupBy(t => new { t.Gender, t.EventName })
                .OrderBy(g => g.Key.Gender).ThenBy(g => g.Key.EventName);

            foreach (var group in groups) {
                // 项目标题（蓝底）
                string title = string.Format("{0} {1}（{2}队）", group.Key.Gender, group.Key.EventName, group.Count());
                var header = new TextBlock {
                    Text = title,
                    FontWeight = FontWeights.Bold, FontSize = 15,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 6, 0, 4)
                };
                RelayGroupedPanel.Children.Add(header);

                // 该项目下的接力队DataGrid
                var grid = new DataGrid {
                    AutoGenerateColumns = false, CanUserAddRows = false,
                    SelectionMode = DataGridSelectionMode.Single,
                    IsReadOnly = true,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                    MinHeight = 30,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "队名", Binding = new System.Windows.Data.Binding("TeamName"), Width = new DataGridLength(100) });
                grid.Columns.Add(new DataGridTextColumn { Header = "报名成绩", Binding = new System.Windows.Data.Binding("EntryTime"), Width = new DataGridLength(80) });
                grid.Columns.Add(new DataGridTextColumn { Header = "棒次", Binding = new System.Windows.Data.Binding("LegOrderDisplay"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                grid.Columns.Add(new DataGridTextColumn { Header = "阶段", Binding = new System.Windows.Data.Binding("Stage"), Width = new DataGridLength(50) });
                grid.Columns.Add(new DataGridTextColumn { Header = "组", Binding = new System.Windows.Data.Binding("Heat"), Width = new DataGridLength(35) });
                grid.Columns.Add(new DataGridTextColumn { Header = "道", Binding = new System.Windows.Data.Binding("Lane"), Width = new DataGridLength(35) });
                grid.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding("Status"), Width = new DataGridLength(50) });

                var teamList = group.OrderBy(t => t.Heat).ThenBy(t => t.Lane).ToList();
                grid.ItemsSource = teamList;

                // 选中行时更新棒次详情
                grid.SelectionChanged += delegate {
                    var sel = grid.SelectedItem as RelayTeam;
                    if (sel != null) {
                        _selectedRelayTeam = sel;
                        // 自动补全队员号码
                        foreach (var leg in sel.Legs) {
                            if (string.IsNullOrEmpty(leg.SwimmerBibNumber) && !string.IsNullOrEmpty(leg.SwimmerName)) {
                                var match = _swimmers.FirstOrDefault(s => s.Name == leg.SwimmerName && s.Country == sel.TeamName);
                                if (match != null && !string.IsNullOrEmpty(match.BibNumber))
                                    leg.SwimmerBibNumber = match.BibNumber;
                            }
                        }
                        RelayLegTitle.Text = string.Format("{0} — {1} 棒次安排（{2}人）", sel.TeamName, sel.EventName, sel.Legs.Count);
                        RelayLegGrid.ItemsSource = sel.Legs;
                    }
                };

                RelayGroupedPanel.Children.Add(grid);
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
                string gender = EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "男";

                // 按当前性别过滤项目列表（排除接力队员个人条目）
                string prevEvent = EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
                EditEventCombo.Items.Clear();
                var evSet = new HashSet<string>();
                foreach (var s in _swimmers) {
                    if (string.IsNullOrEmpty(s.EventName) || s.Gender != gender) continue;
                    if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                    evSet.Add(s.EventName);
                }
                foreach (string ev in evSet.OrderBy(x => x)) EditEventCombo.Items.Add(ev);
                if (!string.IsNullOrEmpty(prevEvent) && EditEventCombo.Items.Contains(prevEvent))
                    EditEventCombo.SelectedItem = prevEvent;
                else if (EditEventCombo.Items.Count > 0)
                    EditEventCombo.SelectedIndex = 0;

                string eventName = EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
                string stage = EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "预赛";

                int prevHeatIndex = EditHeatCombo.SelectedIndex;
                EditHeatCombo.Items.Clear();
                EditHeatCombo.Items.Add("全部");
                // 从运动员数据获取有人的组号（接力项目只看代表队条目）
                bool isRelayEv = eventName.Contains("接力");
                var heatNumbers = new HashSet<int>();
                foreach (var s in _swimmers) {
                    if (s.Gender != gender || s.EventName != eventName) continue;
                    if (isRelayEv && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队员")) continue;
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
            // 重建项目下拉框（按性别过滤，保留当前选择）
            string gender = EditGenderCombo != null && EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "男";
            int prevIndex = EditEventCombo.SelectedIndex;
            EditEventCombo.Items.Clear();
            var evSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (string.IsNullOrEmpty(s.EventName) || s.Gender != gender) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                evSet.Add(s.EventName);
            }
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

            // 查找该赛次的运动员（接力项目只显示代表队条目，不显示个人队员）
            bool isRelay = eventName.Contains("接力");
            var matchedSwimmers = new List<Tuple<Swimmer, int, int>>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                // 接力项目：跳过个人队员条目（Notes以"接力队员"开头），只保留代表队条目
                if (isRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队员")) continue;
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
            var grid = GetActiveEditGrid();
            var selected = grid.SelectedItem;
            if (selected == null) { MessageBox.Show("请先选中要交换的运动员"); return; }
            string bib = selected.GetType().GetProperty("BibNumber").GetValue(selected, null).ToString();
            string gender = EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "";
            string eventName = EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
            string stage = EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "";
            bool isRelay = eventName.Contains("接力");

            var sw1 = _swimmers.FirstOrDefault(s => s.BibNumber == bib);
            if (sw1 == null) return;
            var sa1 = sw1.GetAssignmentForStage(stage);
            int heat1 = sa1 != null ? sa1.Heat : sw1.Heat;
            int lane1 = sa1 != null ? sa1.Lane : sw1.Lane;

            // 候选目标 = 同项目/同赛次的其它运动员（可交换） + 空道（可移动）
            // 使用 Tuple<Swimmer, int, int>：Swimmer==null 表示空道；Heat, Lane 为目标组/道
            var candidates = new List<Tuple<Swimmer, int, int>>();
            var occupiedHeatLane = new HashSet<string>(); // "heat|lane" 占用记录（用于计算空道）
            var heatSet = new HashSet<int>();             // 该项目该赛次出现过的组号

            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (isRelay && s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                int h = 0, ln = 0;
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat > 0) { h = sa.Heat; ln = sa.Lane; }
                else if (s.CurrentStage == stage && s.Heat > 0) { h = s.Heat; ln = s.Lane; }
                if (h <= 0) continue;
                heatSet.Add(h);
                occupiedHeatLane.Add(h + "|" + ln);
                if (s.BibNumber == bib) continue;
                candidates.Add(Tuple.Create(s, h, ln));
            }

            // 补充空道候选：泳池所有泳道 × 所有出现过的组，剔除已占用
            var poolLanes = (_poolConfig != null && _poolConfig.LaneNumbers != null)
                ? _poolConfig.LaneNumbers.ToList() : new List<int>();
            foreach (int h in heatSet) {
                foreach (int ln in poolLanes) {
                    if (occupiedHeatLane.Contains(h + "|" + ln)) continue;
                    candidates.Add(Tuple.Create<Swimmer, int, int>(null, h, ln));
                }
            }

            candidates.Sort((a, b) => { int c = a.Item2.CompareTo(b.Item2); return c != 0 ? c : a.Item3.CompareTo(b.Item3); });

            if (candidates.Count == 0) { MessageBox.Show("没有可交换的运动员或空道"); return; }

            var dlg = new Window {
                Title = string.Format("交换泳道 / 移到空道 — {0}（第{1}组 第{2}道）", sw1.Name, heat1, lane1),
                Width = 500, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = string.Format("当前: {0} — 第{1}组 第{2}道", sw1.Name, heat1, lane1), FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            sp.Children.Add(new TextBlock { Text = "选择要交换的运动员，或选择空道直接移动（空道项标注“[空道]”）:", Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });

            var listBox = new ListBox { Height = 240, FontSize = 13 };
            foreach (var c in candidates) {
                string label;
                if (c.Item1 == null)
                    label = string.Format("第{0}组 第{1}道 — [空道]", c.Item2, c.Item3);
                else
                    label = string.Format("第{0}组 第{1}道 — {2}（{3}）", c.Item2, c.Item3, c.Item1.Name, c.Item1.Country);
                listBox.Items.Add(label);
            }
            sp.Children.Add(listBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var btnOk = new Button { Content = "确定交换", Padding = new Thickness(16, 6, 16, 6), FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnClose = new Button { Content = "关闭", Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnClose.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnClose);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && listBox.SelectedIndex >= 0 && listBox.SelectedIndex < candidates.Count) {
                var target = candidates[listBox.SelectedIndex];
                var sw2 = target.Item1;

                if (sw2 == null) {
                    // 目标是空道：直接把 sw1 移到目标组/道
                    int newHeat = target.Item2, newLane = target.Item3;
                    if (sa1 != null) { sa1.Heat = newHeat; sa1.Lane = newLane; }
                    else {
                        sw1.SetStageAssignment(stage, newHeat, newLane, sw1.EntryTimeSeconds, sw1.EntryTime);
                    }
                    if (sw1.CurrentStage == stage) { sw1.Heat = newHeat; sw1.Lane = newLane; }
                    AutoSaveData();
                    RefreshEditPreview();
                    AddLog(string.Format("移动到空道: {0}(第{1}组{2}道) → 第{3}组{4}道", sw1.Name, heat1, lane1, newHeat, newLane));
                } else {
                    var sa2 = sw2.GetAssignmentForStage(stage);
                    // 交换 Heat 和 Lane
                    if (sa1 != null && sa2 != null) {
                        int tmpH = sa1.Heat; int tmpL = sa1.Lane;
                        sa1.Heat = sa2.Heat; sa1.Lane = sa2.Lane;
                        sa2.Heat = tmpH; sa2.Lane = tmpL;
                    }
                    if (sw1.CurrentStage == stage && sw2.CurrentStage == stage) {
                        int tmpH = sw1.Heat; int tmpL = sw1.Lane;
                        sw1.Heat = sw2.Heat; sw1.Lane = sw2.Lane;
                        sw2.Heat = tmpH; sw2.Lane = tmpL;
                    }
                    AutoSaveData();
                    RefreshEditPreview();
                    AddLog(string.Format("泳道交换: {0}(第{1}组{2}道) ↔ {3}(第{4}组{5}道)", sw1.Name, target.Item2, target.Item3, sw2.Name, heat1, lane1));
                }
            }
        }

        private void EditAddToHeat_Click(object sender, RoutedEventArgs e) {
            string gender = EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "";
            string eventName = EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
            string stage = EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "";
            string heatStr = EditHeatCombo.SelectedItem != null ? EditHeatCombo.SelectedItem.ToString() : "";
            bool isRelay = eventName.Contains("接力");

            if (heatStr == "全部" || string.IsNullOrEmpty(heatStr)) {
                MessageBox.Show("请先选择具体的组（不能是\"全部\"）", "提示"); return;
            }
            int heat = 0;
            var m = System.Text.RegularExpressions.Regex.Match(heatStr, @"\d+");
            if (m.Success) heat = int.Parse(m.Value);
            if (heat <= 0) return;

            // 查找未分组的运动员（同性别同项目同赛次，Heat=0或无StageAssignment）
            var unassigned = new List<Swimmer>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (isRelay && s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                var sa = s.GetAssignmentForStage(stage);
                bool hasAssignment = (sa != null && sa.Heat > 0) || (s.CurrentStage == stage && s.Heat > 0);
                if (!hasAssignment) unassigned.Add(s);
            }

            if (unassigned.Count == 0) { MessageBox.Show("没有未分组的运动员可以添加", "提示"); return; }

            var dlg = new Window {
                Title = string.Format("增加到第{0}组 — {1} {2} {3}", heat, gender, eventName, stage),
                Width = 450, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = string.Format("选择要加入第{0}组的运动员:", heat), FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

            var listBox = new ListBox { Height = 200, FontSize = 13, SelectionMode = SelectionMode.Extended };
            foreach (var s in unassigned) {
                listBox.Items.Add(string.Format("{0}（{1}）{2}", s.Name, s.BibNumber ?? "", !string.IsNullOrEmpty(s.EntryTime) ? " 成绩:" + s.EntryTime : ""));
            }
            sp.Children.Add(listBox);

            // 泳道号输入
            sp.Children.Add(new TextBlock { Text = "起始泳道号（自动递增）:", FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 8, 0, 2) });
            var tbLane = new TextBox { Text = "0", Width = 60, Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(tbLane);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var btnOk = new Button { Content = "确定添加", Padding = new Thickness(16, 6, 16, 6), FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnClose = new Button { Content = "关闭", Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnClose.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnClose);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && listBox.SelectedItems.Count > 0) {
                int startLane = 0;
                int.TryParse(tbLane.Text.Trim(), out startLane);
                int added = 0;
                foreach (var item in listBox.SelectedItems) {
                    int idx = listBox.Items.IndexOf(item);
                    if (idx >= 0 && idx < unassigned.Count) {
                        var sw = unassigned[idx];
                        int lane = startLane + added;
                        sw.SetStageAssignment(stage, heat, lane, sw.EntryTimeSeconds, sw.EntryTime);
                        if (sw.CurrentStage == stage) { sw.Heat = heat; sw.Lane = lane; }
                        added++;
                        AddLog(string.Format("已将 {0} 加入第{1}组第{2}道", sw.Name, heat, lane));
                    }
                }
                AutoSaveData();
                RefreshEditPreview();
                UpdateEditHeatCombo();
            }
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

            // 查找运动员列表（同RefreshEditPreview逻辑，接力项目只看代表队）
            bool isRelaySwap = eventName.Contains("接力");
            var matchedSwimmers = new List<Tuple<Swimmer, int, int>>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (isRelaySwap && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队员")) continue;
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

        private void EditAddTempSwimmer_Click(object sender, RoutedEventArgs e) {
            // 临时加人：在当前项目/赛次新增个人运动员或接力队，不自动重新编排全部分组；
            // 新增对象默认未分组，需通过"增加到本组"或"交换泳道→空道"手动放入组/道
            string gender = EditGenderCombo != null && EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "";
            string eventName = EditEventCombo != null && EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
            string stage = EditStageCombo != null && EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "";
            if (string.IsNullOrEmpty(gender) || string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(stage)) {
                MessageBox.Show("请先在上方选择性别、项目、赛次后再临时加人。", "提示"); return;
            }

            bool isRelay = eventName.Contains("接力");
            if (isRelay) AddTempRelayTeam(gender, eventName, stage);
            else AddTempIndividualSwimmer(gender, eventName, stage);
        }

        private void AddTempIndividualSwimmer(string gender, string eventName, string stage) {
            var dlg = new Window {
                Title = string.Format("临时加人 — {0} {1} {2}", gender, eventName, stage),
                Width = 420, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock {
                Text = string.Format("新增到: {0} {1} {2}（确认后请使用“增加到本组”或“交换泳道”手动放入组/道，不会自动重新分组）", gender, eventName, stage),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 13
            });

            Func<string, TextBox, StackPanel> addRow = delegate(string label, TextBox tb) {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center });
                tb.Width = 280; tb.Padding = new Thickness(4);
                row.Children.Add(tb);
                sp.Children.Add(row);
                return row;
            };

            var tbName = new TextBox();
            var tbBib = new TextBox();
            var tbCountry = new TextBox();
            var tbAge = new TextBox();
            var tbEntryTime = new TextBox { ToolTip = "报名成绩，格式如 0:58.23 或 58.23" };
            addRow("姓名:", tbName);
            addRow("号码:", tbBib);
            addRow("代表队:", tbCountry);
            addRow("年龄:", tbAge);
            addRow("报名成绩:", tbEntryTime);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var btnOk = new Button { Content = "确定添加", Padding = new Thickness(16, 6, 16, 6), FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() != true) return;

            string name = (tbName.Text ?? "").Trim();
            string bib = (tbBib.Text ?? "").Trim();
            string country = (tbCountry.Text ?? "").Trim();
            string entryTimeStr = (tbEntryTime.Text ?? "").Trim();
            int ageVal = 0; int.TryParse((tbAge.Text ?? "").Trim(), out ageVal);

            if (string.IsNullOrEmpty(name)) { MessageBox.Show("姓名不能为空"); return; }
            if (!string.IsNullOrEmpty(bib) && _swimmers.Any(s => s.BibNumber == bib)) {
                MessageBox.Show(string.Format("号码 {0} 已存在，请使用唯一号码。", bib)); return;
            }

            double entrySec = 0;
            if (!string.IsNullOrEmpty(entryTimeStr)) entrySec = TimeFormatter.Parse(entryTimeStr);

            var sw = new Swimmer {
                Name = name, BibNumber = bib, Gender = gender, Country = country, Age = ageVal,
                EventName = eventName, CurrentStage = stage,
                EntryTime = entryTimeStr, EntryTimeSeconds = entrySec,
                Heat = 0, Lane = 0, Notes = "临时加人"
            };
            _swimmers.Add(sw);
            AutoSaveData();
            UpdateEditHeatCombo();
            RefreshEditPreview();
            AddLog(string.Format("临时加人: {0} 加入 {1} {2} {3}（未分组，请手动放入组/道）", name, gender, eventName, stage));
            MessageBox.Show(string.Format("已添加 {0}。\n请使用“增加到本组”或“交换泳道→空道”将其放入具体组/道。", name), "临时加人完成");
        }

        private void AddTempRelayTeam(string gender, string eventName, string stage) {
            // 从项目名解析棒数（如 "4×100米自由泳接力" → 4 棒），解析失败默认 4
            int legCount = 4;
            var mm = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)\s*[x×*]");
            if (mm.Success) { int n; if (int.TryParse(mm.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n; }

            var dlg = new Window {
                Title = string.Format("临时加接力队 — {0} {1} {2}", gender, eventName, stage),
                Width = 460, Height = 140 + legCount * 34 + 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock {
                Text = string.Format("新增接力队到: {0} {1} {2}（确认后请用“增加到本组”或“交换泳道→空道”手动放入组/道，不会自动重新分组）", gender, eventName, stage),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 13
            });

            Func<string, TextBox, double, StackPanel> addRow = delegate(string label, TextBox tb, double labelWidth) {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center });
                tb.Padding = new Thickness(4);
                row.Children.Add(tb);
                sp.Children.Add(row);
                return row;
            };

            var tbTeam = new TextBox { Width = 320 };
            var tbEntryTime = new TextBox { Width = 320, ToolTip = "报名成绩，格式如 4:10.20 或 250.20" };
            addRow("队名（代表队）:", tbTeam, 110);
            addRow("报名成绩:", tbEntryTime, 110);

            sp.Children.Add(new TextBlock { Text = string.Format("队员（共{0}棒，可留空，后续再补）:", legCount),
                Margin = new Thickness(0, 10, 0, 4), FontSize = 13, FontWeight = FontWeights.Bold });

            var legNameBoxes = new List<TextBox>();
            var legBibBoxes = new List<TextBox>();
            for (int i = 0; i < legCount; i++) {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                row.Children.Add(new TextBlock { Text = string.Format("第{0}棒:", i + 1), Width = 55, VerticalAlignment = VerticalAlignment.Center });
                var tbLegName = new TextBox { Width = 180, Padding = new Thickness(4) };
                var tbLegBib = new TextBox { Width = 100, Padding = new Thickness(4), Margin = new Thickness(8, 0, 0, 0), ToolTip = "队员号码（可空）" };
                row.Children.Add(new TextBlock { Text = "姓名", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(tbLegName);
                row.Children.Add(new TextBlock { Text = "号码", Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(tbLegBib);
                sp.Children.Add(row);
                legNameBoxes.Add(tbLegName);
                legBibBoxes.Add(tbLegBib);
            }

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var btnOk = new Button { Content = "确定添加", Padding = new Thickness(16, 6, 16, 6), FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);
            dlg.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            if (dlg.ShowDialog() != true) return;

            string teamName = (tbTeam.Text ?? "").Trim();
            string entryTimeStr = (tbEntryTime.Text ?? "").Trim();
            if (string.IsNullOrEmpty(teamName)) { MessageBox.Show("队名不能为空"); return; }
            if (_swimmers.Any(s => s.EventName == eventName && s.Country == teamName && s.Notes != null && s.Notes.StartsWith("接力队 棒次:"))) {
                MessageBox.Show(string.Format("代表队 \"{0}\" 已在本项目报名过接力队。", teamName)); return;
            }

            double entrySec = 0;
            if (!string.IsNullOrEmpty(entryTimeStr)) entrySec = TimeFormatter.Parse(entryTimeStr);

            // 构造 RelayTeam
            var team = new RelayTeam {
                TeamName = teamName, EventName = eventName, Gender = gender,
                EntryTime = entryTimeStr, EntryTimeSeconds = entrySec
            };
            for (int i = 0; i < legCount; i++) {
                string ln = (legNameBoxes[i].Text ?? "").Trim();
                string lb = (legBibBoxes[i].Text ?? "").Trim();
                team.Legs.Add(new RelayLeg { LegOrder = i + 1, SwimmerName = ln, SwimmerBibNumber = lb, SwimmerIDNumber = "" });
            }
            _relayTeams.Add(team);

            // 代表队条目（统一走分组/成绩流程）
            string legNames = "";
            foreach (var leg in team.Legs) legNames += (legNames.Length > 0 ? "," : "") + (leg.SwimmerName ?? "");
            string teamBib = "R" + (_relayTeams.Count).ToString("D3");
            while (_swimmers.Any(s => s.BibNumber == teamBib)) teamBib = "R" + DateTime.Now.Ticks.ToString().Substring(10);

            _swimmers.Add(new Swimmer {
                BibNumber = teamBib, Name = teamName, Gender = gender, Country = teamName,
                EventName = eventName, CurrentStage = stage,
                EntryTime = entryTimeStr, EntryTimeSeconds = entrySec,
                Heat = 0, Lane = 0,
                Notes = string.Format("接力队 棒次:{0}", legNames)
            });

            // 队员子条目（号码为空时自动生成 teamBib-legN）
            foreach (var leg in team.Legs) {
                if (string.IsNullOrEmpty(leg.SwimmerName)) continue;
                string memBib = !string.IsNullOrEmpty(leg.SwimmerBibNumber) ? leg.SwimmerBibNumber : (teamBib + "-" + leg.LegOrder);
                if (_swimmers.Any(s => s.BibNumber == memBib)) continue;
                _swimmers.Add(new Swimmer {
                    BibNumber = memBib, Name = leg.SwimmerName,
                    Gender = gender == "混合" ? "男" : gender, Country = teamName,
                    IDNumber = leg.SwimmerIDNumber ?? "",
                    EventName = eventName,
                    Notes = string.Format("接力队员 {0} 第{1}棒", eventName, leg.LegOrder)
                });
            }

            AutoSaveData();
            RebuildRelayGroupedView();
            UpdateEditHeatCombo();
            RefreshEditPreview();
            AddLog(string.Format("临时加接力队: {0} ({1}) → {2} {3}（未分组，请手动放入组/道）", teamName, eventName, gender, stage));
            MessageBox.Show(string.Format("已添加接力队 {0}。\n请使用“增加到本组”或“交换泳道→空道”将其放入具体组/道。", teamName), "临时加人完成");
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
            if (MessageBox.Show("确定要添加一条新的赛程项？\n（将插入到选中行后面，未选中则添加到末尾）", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            // 根据选中行确定插入位置和单元号
            int insertIndex = _schedule.Count;
            int sessionNum = _schedule.Count > 0 ? _schedule.Max(s => s.SessionNumber) : 1;
            string date = GetDatePickerText(StartDatePicker);

            // 没选中行时，时间设为当前最后一条的时间+1分钟
            string time = "";
            if (_schedule.Count > 0) {
                var last = _schedule.OrderBy(s => s.Date).ThenBy(s => s.Time).LastOrDefault();
                if (last != null) {
                    if (!string.IsNullOrEmpty(last.Date)) date = last.Date;
                    sessionNum = last.SessionNumber;
                    if (!string.IsNullOrEmpty(last.Time)) {
                        var p = last.Time.Split(':');
                        int hh = 9, mm = 0;
                        if (p.Length >= 1) int.TryParse(p[0], out hh);
                        if (p.Length >= 2) int.TryParse(p[1], out mm);
                        mm++;
                        if (mm >= 60) { mm = 0; hh++; }
                        time = string.Format("{0:D2}:{1:D2}", hh, mm);
                    }
                }
            }

            if (_selectedScheduleItem != null) {
                insertIndex = _schedule.IndexOf(_selectedScheduleItem) + 1;
                sessionNum = _selectedScheduleItem.SessionNumber;
                if (!string.IsNullOrEmpty(_selectedScheduleItem.Date)) date = _selectedScheduleItem.Date;
                // 时间设为选中行时间+1分钟，确保排在后面
                if (!string.IsNullOrEmpty(_selectedScheduleItem.Time)) {
                    var parts = _selectedScheduleItem.Time.Split(':');
                    int h = 9, mi = 0;
                    if (parts.Length >= 1) int.TryParse(parts[0], out h);
                    if (parts.Length >= 2) int.TryParse(parts[1], out mi);
                    mi++;
                    if (mi >= 60) { mi = 0; h++; }
                    time = string.Format("{0:D2}:{1:D2}", h, mi);
                }
            }

            var newItem = new ScheduleItem {
                SessionNumber = sessionNum,
                SessionName = string.Format("第{0}单元", sessionNum),
                Date = date,
                Time = time
            };
            _schedule.Insert(insertIndex, newItem);
            AutoSaveData();
            BuildScheduleTree();
            Broadcast();
        }

        private void EditSchedule_Click(object sender, RoutedEventArgs e) {
            // 复制一份赛程用于编辑（取消时不影响原数据）
            var editList = new ObservableCollection<ScheduleItem>();
            foreach (var s in _schedule) {
                editList.Add(new ScheduleItem {
                    SessionNumber = s.SessionNumber, SessionName = s.SessionName,
                    Date = s.Date, Time = s.Time,
                    Gender = s.Gender, EventName = s.EventName,
                    Stage = s.Stage, HeatCount = s.HeatCount,
                    IsRelay = s.IsRelay
                });
            }

            var dlg = new Window {
                Title = "修改赛程", Width = 800, Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };

            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 按单元分组显示（可编辑），和主界面风格一致
            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var editPanel = new StackPanel();
            ScheduleItem _editSelected = null;

            Action rebuildEditPanel = null;
            rebuildEditPanel = delegate {
                editPanel.Children.Clear();
                // 归一化日期和时间（2026-4-21 -> 2026-04-21；9:5 -> 09:05），使不同写法视为同一天
                foreach (var it in editList) {
                    it.Date = NormalizeDate(it.Date);
                    it.Time = NormalizeTime(it.Time);
                }
                // 推断单元：按 editList 自然顺序遍历，首次出现的 (日期+时段) 组合分配新单元号
                var sMap = new Dictionary<string, int>();
                int sNum = 1;
                foreach (var it in editList) {
                    string per = InferTimePeriod(it.Time);
                    string k = (it.Date ?? "") + "|" + per;
                    if (!sMap.ContainsKey(k)) { sMap[k] = sNum; sNum++; }
                    it.SessionNumber = sMap[k];
                    it.SessionName = string.Format("第{0}单元（{1}{2}）", sMap[k], it.Date ?? "", per);
                }
                var grps = editList.GroupBy(s2 => s2.SessionNumber).OrderBy(g2 => g2.Key);
                foreach (var grp in grps) {
                    var hdr = new TextBlock {
                        Text = grp.First().SessionName ?? string.Format("第{0}单元", grp.Key),
                        FontWeight = FontWeights.Bold, FontSize = 15,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A5FB4")),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F0FE")),
                        Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 5, 0, 6)
                    };
                    editPanel.Children.Add(hdr);

                    var eg = new DataGrid {
                        AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false,
                        MinHeight = 30, SelectionMode = DataGridSelectionMode.Single,
                        AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
                    };
                    eg.Columns.Add(new DataGridTextColumn { Header = "日期", Binding = new System.Windows.Data.Binding("Date"), Width = new DataGridLength(100) });
                    eg.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new System.Windows.Data.Binding("Time"), Width = new DataGridLength(70) });
                    var gc = new DataGridComboBoxColumn { Header = "性别", Width = new DataGridLength(55), SelectedItemBinding = new System.Windows.Data.Binding("Gender") };
                    gc.ItemsSource = new string[] { "男", "女", "混合" }; eg.Columns.Add(gc);
                    var ec = new DataGridComboBoxColumn { Header = "项目", Width = new DataGridLength(160), SelectedItemBinding = new System.Windows.Data.Binding("EventName") };
                    ec.ItemsSource = _events; eg.Columns.Add(ec);
                    var sc = new DataGridComboBoxColumn { Header = "阶段", Width = new DataGridLength(70), SelectedItemBinding = new System.Windows.Data.Binding("Stage") };
                    sc.ItemsSource = new string[] { "预赛", "半决赛", "决赛" }; eg.Columns.Add(sc);
                    eg.Columns.Add(new DataGridTextColumn { Header = "组数", Binding = new System.Windows.Data.Binding("HeatCount"), Width = new DataGridLength(50) });

                    // 保留 editList 自然顺序（不再按时间排序），便于用户自定义比赛顺序
                    eg.ItemsSource = new ObservableCollection<ScheduleItem>(grp);
                    eg.SelectionChanged += delegate { _editSelected = eg.SelectedItem as ScheduleItem; };
                    editPanel.Children.Add(eg);
                }
                if (editList.Count == 0) {
                    editPanel.Children.Add(new TextBlock { Text = "暂无赛程项，请点击\"添加赛程项\"。", Foreground = new SolidColorBrush(Colors.Gray), Margin = new Thickness(10) });
                }
            };
            rebuildEditPanel();
            scrollViewer.Content = editPanel;
            mainGrid.Children.Add(scrollViewer);

            // 按钮栏
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            btnPanel.SetValue(Grid.RowProperty, 1);

            // 插入赛程项的公共方法
            Action<bool> insertScheduleItem = delegate(bool before) {
                var refItem = _editSelected != null ? _editSelected : (editList.Count > 0 ? editList[editList.Count - 1] : null);
                int insertIdx = _editSelected != null ? editList.IndexOf(_editSelected) : editList.Count;
                if (!before) insertIdx++;
                string newTime = "";
                if (refItem != null && !string.IsNullOrEmpty(refItem.Time)) {
                    var tp = refItem.Time.Split(':'); int hh = 9, mm = 0;
                    if (tp.Length >= 1) int.TryParse(tp[0], out hh);
                    if (tp.Length >= 2) int.TryParse(tp[1], out mm);
                    if (before) { mm--; if (mm < 0) { mm = 59; hh--; } }
                    else { mm++; if (mm >= 60) { mm = 0; hh++; } }
                    if (hh < 0) hh = 0;
                    newTime = string.Format("{0:D2}:{1:D2}", hh, mm);
                }
                editList.Insert(insertIdx, new ScheduleItem {
                    Date = refItem != null ? refItem.Date : GetDatePickerText(StartDatePicker),
                    Time = newTime, SessionNumber = refItem != null ? refItem.SessionNumber : 1
                });
                rebuildEditPanel();
            };

            var btnAddBefore = new Button { Content = "前插入", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 4, 0), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnAddBefore.Click += delegate { insertScheduleItem(true); };

            var btnAddAfter = new Button { Content = "后插入", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnAddAfter.Click += delegate { insertScheduleItem(false); };

            var btnDel = new Button { Content = "删除选中", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnDel.Click += delegate {
                if (_editSelected != null) { editList.Remove(_editSelected); _editSelected = null; rebuildEditPanel(); }
                else MessageBox.Show("请先选中要删除的行");
            };

            // 上移/下移：在 editList 中交换选中项与相邻项的位置
            Action<int> moveSelected = delegate(int delta) {
                if (_editSelected == null) { MessageBox.Show("请先选中要移动的行"); return; }
                int idx = editList.IndexOf(_editSelected);
                int newIdx = idx + delta;
                if (idx < 0 || newIdx < 0 || newIdx >= editList.Count) return;
                var sel = _editSelected;
                editList.Move(idx, newIdx);
                rebuildEditPanel();
                _editSelected = sel;
            };

            var btnMoveUp = new Button { Content = "上移", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 4, 0), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnMoveUp.Click += delegate { moveSelected(-1); };

            var btnMoveDown = new Button { Content = "下移", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnMoveDown.Click += delegate { moveSelected(1); };

            // 将当前 DataGrid 里正在编辑的单元格提交回绑定源，防止未失焦的输入被丢弃
            Action flushPendingEdits = delegate {
                var fe = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
                if (fe is TextBox) {
                    var be = fe.GetBindingExpression(TextBox.TextProperty);
                    if (be != null) be.UpdateSource();
                }
                foreach (var child in editPanel.Children) {
                    var g = child as DataGrid;
                    if (g != null) {
                        try { g.CommitEdit(DataGridEditingUnit.Cell, true); g.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
                    }
                }
            };

            // 按 (日期, 时间) 对 editList 整体重排
            Action sortEditListByDateTime = delegate {
                var sorted = editList.OrderBy(s2 => s2.Date ?? "").ThenBy(s2 => s2.Time ?? "").ToList();
                editList.Clear();
                foreach (var it in sorted) editList.Add(it);
            };

            // 刷新按钮：flush + 归一化 + 按日期/时间重排 + 重建面板
            var btnRefresh = new Button { Content = "刷新", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnRefresh.Click += delegate {
                flushPendingEdits();
                foreach (var it in editList) { it.Date = NormalizeDate(it.Date); it.Time = NormalizeTime(it.Time); }
                sortEditListByDateTime();
                _editSelected = null;
                rebuildEditPanel();
            };

            var btnOk = new Button { Content = "确认修改", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 13, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate {
                flushPendingEdits();
                // 归一化 + 按日期、时间重排
                foreach (var it in editList) { it.Date = NormalizeDate(it.Date); it.Time = NormalizeTime(it.Time); }
                sortEditListByDateTime();
                dlg.DialogResult = true;
            };

            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };

            btnPanel.Children.Add(btnAddBefore);
            btnPanel.Children.Add(btnAddAfter);
            btnPanel.Children.Add(btnMoveUp);
            btnPanel.Children.Add(btnMoveDown);
            btnPanel.Children.Add(btnDel);
            btnPanel.Children.Add(btnRefresh);
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            mainGrid.Children.Add(btnPanel);
            dlg.Content = mainGrid;

            if (dlg.ShowDialog() == true) {
                // 根据最终的 editList 顺序，重新推断单元编号（日期+时段 首次出现分配新单元号）
                var sMap2 = new Dictionary<string, int>();
                int sNum2 = 1;
                foreach (var it in editList) {
                    string per = InferTimePeriod(it.Time);
                    string k = (it.Date ?? "") + "|" + per;
                    if (!sMap2.ContainsKey(k)) { sMap2[k] = sNum2; sNum2++; }
                    it.SessionNumber = sMap2[k];
                    it.SessionName = string.Format("第{0}单元（{1}{2}）", sMap2[k], it.Date ?? "", per);
                }
                // 用编辑后的数据替换原赛程（保留用户自定义顺序）
                _schedule.Clear();
                foreach (var item in editList) _schedule.Add(item);
                AutoSaveData();       // 保存
                BuildScheduleTree();  // 刷新日程树 & 赛程管理面板
                Broadcast();          // 同步到三个计时控制台 + 显示 + 打印
                AddLog(string.Format("赛程已修改: {0}条赛程项", _schedule.Count));
            }
        }

        private void DeleteSchedule_Click(object sender, RoutedEventArgs e) {
            if (_selectedScheduleItem == null) { MessageBox.Show("请先在日程表中选中要删除的行"); return; }
            string desc = string.Format("{0} {1} {2}", _selectedScheduleItem.Gender, _selectedScheduleItem.EventName, _selectedScheduleItem.Stage);
            if (MessageBox.Show(string.Format("确定要删除赛程项 [{0}]？", desc), "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _schedule.Remove(_selectedScheduleItem);
            _selectedScheduleItem = null;
            AutoSaveData();
            BuildScheduleTree();
            Broadcast();
        }

        // 将用户输入的日期归一化为 yyyy-MM-dd 格式（支持 2026-4-21、2026/4/21、2026.4.21 等）
        private string NormalizeDate(string input) {
            if (string.IsNullOrWhiteSpace(input)) return input;
            string s = input.Trim().Replace('/', '-').Replace('.', '-');
            DateTime dt;
            if (DateTime.TryParseExact(s, new[] { "yyyy-M-d", "yyyy-MM-dd", "yyyy-M-dd", "yyyy-MM-d" },
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) {
                return dt.ToString("yyyy-MM-dd");
            }
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt)) {
                return dt.ToString("yyyy-MM-dd");
            }
            return input;
        }

        // 将用户输入的时间归一化为 HH:mm 格式（支持 9:5、09:05、9:05 等）
        private string NormalizeTime(string input) {
            if (string.IsNullOrWhiteSpace(input)) return input;
            string s = input.Trim();
            DateTime dt;
            if (DateTime.TryParseExact(s, new[] { "H:m", "HH:mm", "H:mm", "HH:m" },
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) {
                return dt.ToString("HH:mm");
            }
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt)) {
                return dt.ToString("HH:mm");
            }
            return input;
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

            // 归一化日期/时间，避免 2026-4-21 与 2026-04-21 被视为不同日期
            foreach (var item in _schedule) {
                item.Date = NormalizeDate(item.Date);
                item.Time = NormalizeTime(item.Time);
            }

            // 自动推断单元编号：按 _schedule 自然顺序遍历，首次出现的(日期+时段)分配新单元号
            var sessionMap = new Dictionary<string, int>();
            int sessionNum = 1;
            foreach (var item in _schedule) {
                string period = InferTimePeriod(item.Time);
                string key = (item.Date ?? "") + "|" + period;
                if (!sessionMap.ContainsKey(key)) {
                    sessionMap[key] = sessionNum;
                    sessionNum++;
                }
                item.SessionNumber = sessionMap[key];
                item.SessionName = string.Format("第{0}单元（{1}{2}）", sessionMap[key], item.Date ?? "", period);
            }

            // 按单元分组只读显示
            var groups = _schedule.GroupBy(s => s.SessionNumber).OrderBy(g => g.Key);
            foreach (var group in groups) {
                string label = group.First().SessionName ?? string.Format("第{0}单元", group.Key);

                var header = new TextBlock {
                    Text = label,
                    FontWeight = FontWeights.Bold, FontSize = 15,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A5FB4")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F0FE")),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 5, 0, 6)
                };
                ScheduleGroupedPanel.Children.Add(header);

                var grid = new DataGrid {
                    AutoGenerateColumns = false, CanUserAddRows = false,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    MinHeight = 30,
                    AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                    IsReadOnly = true,
                    SelectionMode = DataGridSelectionMode.Single
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new System.Windows.Data.Binding("Time"), Width = new DataGridLength(70) });
                grid.Columns.Add(new DataGridTextColumn { Header = "性别", Binding = new System.Windows.Data.Binding("Gender"), Width = new DataGridLength(40) });
                grid.Columns.Add(new DataGridTextColumn { Header = "项目", Binding = new System.Windows.Data.Binding("EventName"), Width = new DataGridLength(160) });
                grid.Columns.Add(new DataGridTextColumn { Header = "阶段", Binding = new System.Windows.Data.Binding("Stage"), Width = new DataGridLength(60) });
                grid.Columns.Add(new DataGridTextColumn { Header = "组数", Binding = new System.Windows.Data.Binding("HeatCount"), Width = new DataGridLength(50) });

                // 保留 _schedule 自然顺序（支持用户自定义的比赛顺序）
                grid.ItemsSource = new ObservableCollection<ScheduleItem>(group);
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
            } else {
                if (MessageBox.Show("确定根据已注册运动员自动生成比赛日程？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
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
            // 检测是否已有分组
            bool hasExistingAssignment = _swimmers.Any(s =>
                !string.IsNullOrEmpty(s.EventName) && s.Heat > 0 &&
                !(s.Notes != null && s.Notes.StartsWith("接力队员")));
            string confirmMsg = "确定按报名成绩对预赛进行蛇形自动分组？";
            if (hasExistingAssignment)
                confirmMsg = "警告：已有运动员分好组！\n\n重新自动分组将清除所有已有分组，重新分配。\n如需仅对新增运动员分组，请使用\"追加分组\"。\n\n确定要重新全部分组吗？";
            if (MessageBox.Show(confirmMsg, "确认", MessageBoxButton.YesNo, hasExistingAssignment ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            // 统一对所有项目（个人+接力）的第一赛次进行蛇形分组
            int generated = 0;
            foreach (var item in _schedule) {
                string fullEvent = item.EventName;
                string stage = item.Stage;
                string gender = item.Gender;

                // 过滤掉接力队员个人条目（只保留代表队条目）
                var eventSwimmers = _swimmers.Where(s =>
                    s.EventName == fullEvent && s.Gender == gender && s.CurrentStage == stage &&
                    !(s.Notes != null && s.Notes.StartsWith("接力队员"))
                ).ToList();

                if (eventSwimmers.Count == 0) {
                    eventSwimmers = _swimmers.Where(s =>
                        s.EventName == fullEvent &&
                        (s.Gender.StartsWith(gender) || gender.StartsWith(s.Gender)) &&
                        s.CurrentStage == stage &&
                        !(s.Notes != null && s.Notes.StartsWith("接力队员"))
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
            SyncRelayHeatInfo();
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

        // ═══════════════════════════════════════════════════════════════
        // 追加分组：只对新增的未分组运动员进行分组，已分好组的不变
        // ═══════════════════════════════════════════════════════════════
        private void SupplementHeats_Click(object sender, RoutedEventArgs e) {
            if (_schedule.Count == 0) {
                MessageBox.Show("请先生成日程和预赛分组。", "提示"); return;
            }

            // 收集所有未分组的运动员（按项目分组）
            var unassigned = new Dictionary<string, List<Swimmer>>(); // key: "gender|eventName|stage"
            foreach (var s in _swimmers) {
                if (string.IsNullOrEmpty(s.EventName) || string.IsNullOrEmpty(s.Gender)) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                string stage = s.CurrentStage ?? "预赛";
                var sa = s.GetAssignmentForStage(stage);
                bool assigned = (sa != null && sa.Heat > 0) || (s.Heat > 0 && s.CurrentStage == stage);
                if (!assigned) {
                    string key = s.Gender + "|" + s.EventName + "|" + stage;
                    if (!unassigned.ContainsKey(key)) unassigned[key] = new List<Swimmer>();
                    unassigned[key].Add(s);
                }
            }

            if (unassigned.Count == 0) {
                MessageBox.Show("所有运动员已分组，没有需要追加的。", "提示"); return;
            }

            // 弹窗显示未分组运动员
            var dlg = new Window {
                Title = "追加分组", Width = 600, Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };
            var mainPanel = new Grid { Margin = new Thickness(16) };
            mainPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var infoText = new TextBlock {
                Text = string.Format("以下 {0} 个项目有未分组运动员，点击\"确认追加\"将自动分配到新增组：",
                    unassigned.Count),
                FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(infoText);

            // 列表
            var listBox = new ListBox { FontSize = 13, Margin = new Thickness(0, 0, 0, 10) };
            listBox.SetValue(Grid.RowProperty, 1);
            foreach (var kv in unassigned) {
                var parts = kv.Key.Split('|');
                string label = string.Format("{0} {1} {2}: {3}{4}未分组",
                    parts[0], parts[1], parts[2], kv.Value.Count,
                    parts[1].Contains("接力") ? "队" : "人");
                // 显示每个人的名字
                var names = new List<string>();
                foreach (var s in kv.Value) names.Add(s.Name);
                label += " (" + string.Join(", ", names.ToArray()) + ")";
                listBox.Items.Add(label);
            }
            mainPanel.Children.Add(listBox);

            // 按钮
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btnPanel.SetValue(Grid.RowProperty, 2);
            var btnOk = new Button {
                Content = "确认追加", Padding = new Thickness(16, 6, 16, 6), FontSize = 14, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0)
            };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnCancel = new Button {
                Content = "取消", Padding = new Thickness(16, 6, 16, 6), FontSize = 14,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0)
            };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            mainPanel.Children.Add(btnPanel);
            dlg.Content = mainPanel;

            if (dlg.ShowDialog() != true) return;

            // 执行追加分组
            int totalAdded = 0;
            int laneCount = _poolConfig.LaneCount;
            int[] lanePriority = HeatScheduler.GetLanePriority(_poolConfig);

            foreach (var kv in unassigned) {
                var parts = kv.Key.Split('|');
                string gender = parts[0];
                string eventName = parts[1];
                string stage = parts[2];
                var newSwimmers = kv.Value;

                // 找该项目当前最大组号
                int maxHeat = 0;
                foreach (var s in _swimmers) {
                    if (s.Gender != gender || s.EventName != eventName) continue;
                    if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                    var sa = s.GetAssignmentForStage(stage);
                    if (sa != null && sa.Heat > maxHeat) maxHeat = sa.Heat;
                    if (s.CurrentStage == stage && s.Heat > maxHeat) maxHeat = s.Heat;
                }

                // 检查最后一组是否还有空位
                int lastHeatCount = 0;
                if (maxHeat > 0) {
                    foreach (var s in _swimmers) {
                        if (s.Gender != gender || s.EventName != eventName) continue;
                        if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                        var sa = s.GetAssignmentForStage(stage);
                        if ((sa != null && sa.Heat == maxHeat) || (s.CurrentStage == stage && s.Heat == maxHeat))
                            lastHeatCount++;
                    }
                }

                int availableInLast = (maxHeat > 0) ? (laneCount - lastHeatCount) : 0;
                int idx = 0;

                // 先填满最后一组的空位
                if (availableInLast > 0 && maxHeat > 0) {
                    int startLane = lastHeatCount; // 已有几人，从下一个泳道开始
                    while (idx < newSwimmers.Count && availableInLast > 0) {
                        int lane = startLane < lanePriority.Length ? lanePriority[startLane] : startLane;
                        var sw = newSwimmers[idx];
                        sw.Heat = maxHeat;
                        sw.Lane = lane;
                        sw.CurrentStage = stage;
                        sw.SetStageAssignment(stage, maxHeat, lane, sw.EntryTimeSeconds, sw.EntryTime);
                        AddLog(string.Format("  追加: {0} → 第{1}组第{2}道", sw.Name, maxHeat, lane));
                        startLane++;
                        availableInLast--;
                        idx++;
                        totalAdded++;
                    }
                }

                // 剩余的分配到新组
                while (idx < newSwimmers.Count) {
                    maxHeat++;
                    int heatSize = Math.Min(laneCount, newSwimmers.Count - idx);
                    for (int j = 0; j < heatSize; j++) {
                        int lane = j < lanePriority.Length ? lanePriority[j] : j;
                        var sw = newSwimmers[idx];
                        sw.Heat = maxHeat;
                        sw.Lane = lane;
                        sw.CurrentStage = stage;
                        sw.SetStageAssignment(stage, maxHeat, lane, sw.EntryTimeSeconds, sw.EntryTime);
                        AddLog(string.Format("  追加: {0} → 第{1}组第{2}道", sw.Name, maxHeat, lane));
                        idx++;
                        totalAdded++;
                    }
                }

                // 更新赛程表中的组数（不存在则新建）
                var schedItem = _schedule.FirstOrDefault(s => s.Gender == gender && s.EventName == eventName && s.Stage == stage);
                if (schedItem != null) {
                    if (maxHeat > schedItem.HeatCount) schedItem.HeatCount = maxHeat;
                } else {
                    // 新项目：在赛程表中新建条目
                    _schedule.Add(new ScheduleItem {
                        SessionNumber = _schedule.Count > 0 ? _schedule.Max(s => s.SessionNumber) : 1,
                        Gender = gender,
                        EventName = eventName,
                        Stage = stage,
                        HeatCount = maxHeat
                    });
                    AddLog(string.Format("  新建赛程: {0} {1} {2}（{3}组）", gender, eventName, stage, maxHeat));
                }
            }

            BuildScheduleTree();
            SyncRelayHeatInfo();
            UpdateEditHeatCombo();
            AutoSaveData();
            Broadcast();
            AddLog(string.Format("追加分组完成: {0}人已分配", totalAdded));
            MessageBox.Show(string.Format("追加分组完成！\n共{0}人已分配到各组。\n\n已分好组的运动员不受影响。", totalAdded),
                "追加分组完成", MessageBoxButton.OK, MessageBoxImage.Information);
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

            // 收集所有项目（过滤掉无效数据和个人接力队员条目）
            var validSwimmers = _swimmers.Where(s =>
                !string.IsNullOrEmpty(s.Gender) && !string.IsNullOrEmpty(s.EventName) &&
                !(s.Notes != null && s.Notes.StartsWith("接力队员"))
            ).ToList();
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
                    if (stage == "半决赛") estimatedCount = Math.Min(count, 16);
                    else if (stage == "决赛" && stages.Count > 1) estimatedCount = Math.Min(count, 8);
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

            // ====== 按依赖关系分配日程 ======
            // 核心原则：同一项目的赛次必须严格按 预赛→半决赛→决赛 的时间顺序，且至少间隔一天
            // 每个项目的半决赛日 = 预赛日+1，决赛日 = 半决赛日+1（或预赛日+1）

            var prelims = allItems.Where(s => s.Stage == "预赛").OrderBy(s => s.IsRelay ? 1 : 0).ThenBy(s => s.EventName).ToList();
            var semis = allItems.Where(s => s.Stage == "半决赛").OrderBy(s => s.IsRelay ? 1 : 0).ThenBy(s => s.EventName).ToList();
            var directFinals = allItems.Where(s => s.Stage == "决赛" && !allItems.Any(p => p.EventName == s.EventName && p.Gender == s.Gender && p.Stage == "预赛"))
                .OrderBy(s => s.IsRelay ? 1 : 0).ThenBy(s => s.EventName).ToList();
            var linkedFinals = allItems.Where(s => s.Stage == "决赛" && allItems.Any(p => p.EventName == s.EventName && p.Gender == s.Gender && p.Stage == "预赛"))
                .OrderBy(s => s.IsRelay ? 1 : 0).ThenBy(s => s.EventName).ToList();

            // 标识有半决赛的项目（三阶段：预赛→半决赛→决赛）
            var threeStageKeys = new HashSet<string>();
            foreach (var semi in semis)
                threeStageKeys.Add(semi.Gender + "|" + semi.EventName);

            // ---- 第一步：分配预赛到各天上午 ----
            // 三阶段项目的预赛必须在 day 0 ~ totalDays-3（留2天给半决赛和决赛）
            // 两阶段项目的预赛必须在 day 0 ~ totalDays-2（留1天给决赛）
            var prelimDayMap = new Dictionary<string, int>();

            var threeStgPrelims = prelims.Where(p => threeStageKeys.Contains(p.Gender + "|" + p.EventName)).ToList();
            var twoStgPrelims = prelims.Where(p => !threeStageKeys.Contains(p.Gender + "|" + p.EventName)).ToList();

            int availDays3 = Math.Max(1, totalDays - 2);
            int availDays2 = Math.Max(1, totalDays - 1);
            if (availDays2 < 1) availDays2 = 1;

            int perDay3 = threeStgPrelims.Count > 0 ? (int)Math.Ceiling((double)threeStgPrelims.Count / availDays3) : 0;
            int pIdx = 0;
            for (int d = 0; d < availDays3 && pIdx < threeStgPrelims.Count; d++) {
                for (int i = 0; i < perDay3 && pIdx < threeStgPrelims.Count; i++, pIdx++)
                    prelimDayMap[threeStgPrelims[pIdx].Gender + "|" + threeStgPrelims[pIdx].EventName] = d;
            }

            int perDay2 = twoStgPrelims.Count > 0 ? (int)Math.Ceiling((double)twoStgPrelims.Count / availDays2) : 0;
            pIdx = 0;
            for (int d = 0; d < availDays2 && pIdx < twoStgPrelims.Count; d++) {
                for (int i = 0; i < perDay2 && pIdx < twoStgPrelims.Count; i++, pIdx++)
                    prelimDayMap[twoStgPrelims[pIdx].Gender + "|" + twoStgPrelims[pIdx].EventName] = d;
            }

            // ---- 第二步：半决赛日 = 预赛日+1（下午场） ----
            var semiDayMap = new Dictionary<string, int>();
            foreach (var semi in semis) {
                string key = semi.Gender + "|" + semi.EventName;
                int pDay = prelimDayMap.ContainsKey(key) ? prelimDayMap[key] : 0;
                semiDayMap[key] = Math.Min(pDay + 1, totalDays - 1);
            }

            // ---- 第三步：关联决赛日 ----
            // 有半决赛的：决赛日 = 半决赛日+1（晚上场）
            // 无半决赛的：决赛日 = 预赛日+1（晚上场）
            var linkedFinalDayMap = new Dictionary<string, int>();
            foreach (var lf in linkedFinals) {
                string key = lf.Gender + "|" + lf.EventName;
                int fDay;
                if (semiDayMap.ContainsKey(key))
                    fDay = semiDayMap[key] + 1;
                else if (prelimDayMap.ContainsKey(key))
                    fDay = prelimDayMap[key] + 1;
                else
                    fDay = totalDays - 1;
                linkedFinalDayMap[key] = Math.Min(fDay, totalDays - 1);
            }

            // ---- 第四步：直接决赛均匀分配到各天晚上 ----
            var directFinalDayMap = new Dictionary<string, int>();
            int dfPerDay = directFinals.Count > 0 ? (int)Math.Ceiling((double)directFinals.Count / totalDays) : 0;
            if (dfPerDay < 1 && directFinals.Count > 0) dfPerDay = directFinals.Count;
            int dfIdx = 0;
            for (int d = 0; d < totalDays && dfIdx < directFinals.Count; d++) {
                for (int i = 0; i < dfPerDay && dfIdx < directFinals.Count; i++, dfIdx++)
                    directFinalDayMap[directFinals[dfIdx].Gender + "|" + directFinals[dfIdx].EventName] = d;
            }

            // ---- 第五步：按天分组 ----
            var prelimsByDay = new Dictionary<int, List<ScheduleItem>>();
            foreach (var p in prelims) {
                string key = p.Gender + "|" + p.EventName;
                int d = prelimDayMap.ContainsKey(key) ? prelimDayMap[key] : 0;
                if (!prelimsByDay.ContainsKey(d)) prelimsByDay[d] = new List<ScheduleItem>();
                prelimsByDay[d].Add(p);
            }
            var semisByDay = new Dictionary<int, List<ScheduleItem>>();
            foreach (var s in semis) {
                string key = s.Gender + "|" + s.EventName;
                int d = semiDayMap.ContainsKey(key) ? semiDayMap[key] : 1;
                if (!semisByDay.ContainsKey(d)) semisByDay[d] = new List<ScheduleItem>();
                semisByDay[d].Add(s);
            }
            var dfByDay = new Dictionary<int, List<ScheduleItem>>();
            foreach (var df in directFinals) {
                string key = df.Gender + "|" + df.EventName;
                int d = directFinalDayMap.ContainsKey(key) ? directFinalDayMap[key] : 0;
                if (!dfByDay.ContainsKey(d)) dfByDay[d] = new List<ScheduleItem>();
                dfByDay[d].Add(df);
            }
            var lfByDay = new Dictionary<int, List<ScheduleItem>>();
            foreach (var lf in linkedFinals) {
                string key = lf.Gender + "|" + lf.EventName;
                int d = linkedFinalDayMap.ContainsKey(key) ? linkedFinalDayMap[key] : totalDays - 1;
                if (!lfByDay.ContainsKey(d)) lfByDay[d] = new List<ScheduleItem>();
                lfByDay[d].Add(lf);
            }

            // ---- 第六步：逐天分配日期和时间 ----
            // 每个项目的时间间隔 = 组数 × 每组预估用时（根据比赛距离计算）
            int sessionNum = 1;

            for (int day = 0; day < totalDays; day++) {
                string dateStr = startDate.AddDays(day).ToString("yyyy-MM-dd");
                int offsetMin; // 从场次开始时间算起的分钟偏移

                // 上午场：预赛（09:00开始）
                if (prelimsByDay.ContainsKey(day)) {
                    offsetMin = 0;
                    foreach (var item in prelimsByDay[day]) {
                        item.Date = dateStr;
                        item.Time = string.Format("{0:D2}:{1:D2}", 9 + offsetMin / 60, offsetMin % 60);
                        item.SessionNumber = sessionNum;
                        item.SessionName = string.Format("第{0}单元（{1}上午）", sessionNum, dateStr);
                        _schedule.Add(item);
                        int duration = Math.Max(1, item.HeatCount) * EstimateMinutesPerHeat(item.EventName);
                        offsetMin += duration;
                    }
                    sessionNum++;
                }

                // 下午场：半决赛（14:00开始）
                if (semisByDay.ContainsKey(day)) {
                    offsetMin = 0;
                    foreach (var item in semisByDay[day]) {
                        item.Date = dateStr;
                        item.Time = string.Format("{0:D2}:{1:D2}", 14 + offsetMin / 60, offsetMin % 60);
                        item.SessionNumber = sessionNum;
                        item.SessionName = string.Format("第{0}单元（{1}下午）", sessionNum, dateStr);
                        _schedule.Add(item);
                        int duration = Math.Max(1, item.HeatCount) * EstimateMinutesPerHeat(item.EventName);
                        offsetMin += duration;
                    }
                    sessionNum++;
                }

                // 晚上场：决赛（19:00开始）
                bool hasEvening = false;
                offsetMin = 0;

                // 直接决赛
                if (dfByDay.ContainsKey(day)) {
                    foreach (var item in dfByDay[day]) {
                        item.Date = dateStr;
                        item.Time = string.Format("{0:D2}:{1:D2}", 19 + offsetMin / 60, offsetMin % 60);
                        item.SessionNumber = sessionNum;
                        item.SessionName = string.Format("第{0}单元（{1}晚上）", sessionNum, dateStr);
                        _schedule.Add(item);
                        int duration = Math.Max(1, item.HeatCount) * EstimateMinutesPerHeat(item.EventName);
                        offsetMin += duration;
                        hasEvening = true;
                    }
                }

                // 关联决赛（有预赛/半决赛的项目的决赛）
                if (lfByDay.ContainsKey(day)) {
                    foreach (var item in lfByDay[day]) {
                        item.Date = dateStr;
                        item.Time = string.Format("{0:D2}:{1:D2}", 19 + offsetMin / 60, offsetMin % 60);
                        item.SessionNumber = sessionNum;
                        item.SessionName = string.Format("第{0}单元（{1}晚上）", sessionNum, dateStr);
                        _schedule.Add(item);
                        int duration = Math.Max(1, item.HeatCount) * EstimateMinutesPerHeat(item.EventName);
                        offsetMin += duration;
                        hasEvening = true;
                    }
                }

                if (hasEvening) sessionNum++;
            }

            AddLog(string.Format("自动日程安排完成: {0}条赛程, {1}天, 上午预赛/下午半决赛/晚上决赛",
                _schedule.Count, totalDays));
        }

        /// <summary>
        /// 根据项目名称估算每组比赛所需分钟数（含运动员检录出场、上道准备、比赛、间歇）
        /// 参考世界纪录成绩，考虑普通运动员较慢，加上赛间流程时间
        /// </summary>
        private static int EstimateMinutesPerHeat(string eventName) {
            bool isRelay = eventName.Contains("接力");
            // 解析距离：匹配 "50米"、"100米"、"1500米"、"4×100米" 等
            int distance = 0;
            var match = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)米");
            if (match.Success) distance = int.Parse(match.Groups[1].Value);

            // 接力：实际比赛距离 = 单人距离 × 4
            int effectiveDistance = isRelay ? distance * 4 : distance;

            // 每组用时（分钟）= 预估比赛时间 + 赛间流程时间(检录、出场、上道、间歇约4分钟)
            // 50米:  ~35秒比赛 + 4分钟流程 ≈ 5分钟
            // 100米: ~1分15秒 + 4分钟 ≈ 6分钟
            // 200米: ~2分30秒 + 4分钟 ≈ 7分钟
            // 400米(含4×100接力): ~5分钟 + 5分钟 ≈ 10分钟
            // 800米(含4×200接力): ~10分钟 + 5分钟 ≈ 15分钟
            // 1500米: ~18分钟 + 4分钟 ≈ 22分钟
            if (effectiveDistance <= 50) return 5;
            if (effectiveDistance <= 100) return 6;
            if (effectiveDistance <= 200) return 7;
            if (effectiveDistance <= 400) return 10;
            if (effectiveDistance <= 800) return 15;
            return 22; // 1500米及以上
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
            // 刷新项目列表：只显示有运动员/运动队注册的项目（过滤接力队员个人条目）
            string prevEvent = ResultEventCombo.SelectedItem != null ? ResultEventCombo.SelectedItem.ToString() : "";
            string gender = ResultGenderCombo.SelectedItem != null ? ((ComboBoxItem)ResultGenderCombo.SelectedItem).Content.ToString() : "男";
            ResultEventCombo.Items.Clear();
            var eventSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (s.Gender == gender && !string.IsNullOrEmpty(s.EventName) &&
                    !(s.Notes != null && s.Notes.StartsWith("接力队员")))
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
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
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

            // 按性别和项目筛选（接力项目只查代表队，不查个人队员）
            var allMatched = _swimmers.Where(s =>
                s.EventName == eventName && s.Gender == gender &&
                !(s.Notes != null && s.Notes.StartsWith("接力队员"))
            ).ToList();

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

            bool isRelayEvent = eventName.Contains("接力");
            var displayData = results.Select(s => {
                var r = s.GetResultForStage(stage);
                int lane = 0;
                if (r != null) lane = r.Lane;
                else {
                    var sa = s.GetAssignmentForStage(stage);
                    lane = sa != null ? sa.Lane : s.Lane;
                }
                bool isDQ = s.Status == "DSQ" || s.Status == "DNS" || s.Status == "DNF" || s.Status == "DQ";
                double sortTime = (!isDQ && r != null && r.FinalTime > 0) ? r.FinalTime : double.MaxValue;
                // 接力项目：姓名显示四位队员姓名
                string displayName = s.Name ?? "";
                if (isRelayEvent && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:")) {
                    displayName = s.Notes.Substring("接力队 棒次:".Length);
                }
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (isDQ) remark = s.Status;
                return new {
                    SortTime = sortTime,
                    Lane = lane,
                    BibNumber = s.BibNumber ?? "",
                    Name = displayName,
                    Country = s.Country ?? "",
                    FinalTime = isDQ ? "" : (r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : ""),
                    TimingSource = r != null ? (r.TimingSource ?? "") : "",
                    ReactionTime = r != null && r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F2") : "",
                    Status = !string.IsNullOrEmpty(remark) ? remark : (s.Status ?? ""),
                    RecordNote = ""
                };
            }).OrderBy(x => x.SortTime).ToList();

            // 重新计算排名（DSQ/DNS/DNF无名次）
            var rankedData = new List<object>();
            int rankNum = 1;
            foreach (var item in displayData) {
                string rankStr = item.SortTime < double.MaxValue ? rankNum.ToString() : "-";
                rankedData.Add(new {
                    Rank = rankStr,
                    item.Lane, item.BibNumber,
                    Name = isRelayEvent ? item.Country : item.Name,
                    Country = isRelayEvent ? item.Name : item.Country,
                    item.FinalTime, item.TimingSource, item.ReactionTime, item.Status, item.RecordNote
                });
                if (item.SortTime < double.MaxValue) rankNum++;
            }

            ResultGrid.ItemsSource = rankedData;
        }

        private void PublishResult_Click(object sender, RoutedEventArgs e) {
            // 弹出窗口选择已完赛项目的组成绩发布到大屏
            var dlg = new Window {
                Title = "成绩发布到大屏", Width = 500, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "选择已完赛的比赛项目成绩发布到大屏幕：", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

            // 收集所有已完赛的组
            var completedHeats = new List<object>();
            var completedLabels = new List<string>();
            foreach (var schedItem in _schedule) {
                int hc = schedItem.HeatCount > 0 ? schedItem.HeatCount : 1;
                for (int h = 1; h <= hc; h++) {
                    if (IsHeatConfirmed(schedItem.Gender, schedItem.EventName, schedItem.Stage, h)) {
                        completedHeats.Add(new { Gender = schedItem.Gender, EventName = schedItem.EventName, Stage = schedItem.Stage, Heat = h, HeatCount = hc });
                        bool showHeat = (hc > 1) || schedItem.Stage.Contains("预赛") || schedItem.Stage.Contains("半决赛");
                        string heatLabel = showHeat ? string.Format(" 第{0}组", h) : "";
                        completedLabels.Add(string.Format("{0} {1} {2}{3}", schedItem.Gender, schedItem.EventName, schedItem.Stage, heatLabel));
                    }
                }
            }

            var listBox = new ListBox { Height = 250, FontSize = 14 };
            if (completedHeats.Count == 0) {
                listBox.Items.Add("（暂无已完赛的比赛）");
                listBox.IsEnabled = false;
            } else {
                foreach (string label in completedLabels) listBox.Items.Add(label);
            }
            sp.Children.Add(listBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnOk = new Button { Content = "发布到大屏", Padding = new Thickness(16, 6, 16, 6), FontSize = 14, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), Foreground = new SolidColorBrush(Colors.White), FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnClose = new Button { Content = "关闭", Padding = new Thickness(16, 6, 16, 6), FontSize = 14,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnClose.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnClose);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            if (dlg.ShowDialog() == true && listBox.SelectedIndex >= 0 && listBox.SelectedIndex < completedHeats.Count) {
                dynamic sel = completedHeats[listBox.SelectedIndex];
                PublishResultToDisplay((string)sel.Gender, (string)sel.EventName, (string)sel.Stage, (int)sel.Heat);
            }
        }

        /// <summary>
        /// 将指定组的成绩发布到大屏（供EXE按钮和HTML远程调用）
        /// </summary>
        private void PublishResultToDisplay(string gender, string eventName, string stage, int heat) {
            var heatSwimmers = new List<object>();
            var schedItem = _schedule.FirstOrDefault(s => s.Gender == gender && s.EventName == eventName && s.Stage == stage);
            int heatCount = schedItem != null ? schedItem.HeatCount : 1;

            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                var sa = s.GetAssignmentForStage(stage);
                bool inHeat = (sa != null && sa.Heat == heat) || (s.CurrentStage == stage && s.Heat == heat);
                if (!inHeat) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                var r = s.GetResultForStage(stage);
                int lane = sa != null ? sa.Lane : s.Lane;
                // 接力项目：name显示队员姓名
                string dispName = s.Name ?? "";
                if (eventName.Contains("接力") && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:")) {
                    dispName = s.Notes.Substring("接力队 棒次:".Length);
                }
                bool isDQ = s.Status == "DSQ" || s.Status == "DNS" || s.Status == "DNF";
                heatSwimmers.Add(new {
                    lane = lane,
                    name = dispName,
                    country = s.Country ?? "",
                    bibNumber = s.BibNumber ?? "",
                    finalTime = !isDQ && r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "",
                    rank = isDQ ? 0 : (r != null ? r.Rank : 0),
                    status = s.Status ?? "",
                    resultStatus = r != null ? (r.Status ?? "") : "",
                    reactionTime = r != null && r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F2") : ""
                });
            }

            bool showHeatNum = (heatCount > 1) || stage.Contains("预赛") || stage.Contains("半决赛");
            string heatDisplay = showHeatNum ? string.Format(" 第{0}组", heat) : "";

            var publishData = new {
                type = "SHOW_PUBLISHED_RESULT",
                data = new {
                    competitionName = _competitionName,
                    gender = gender,
                    eventName = eventName,
                    stage = stage,
                    heat = heat,
                    heatDisplay = heatDisplay,
                    swimmers = heatSwimmers,
                    poolConfig = new { lanes = _poolConfig.LaneCount }
                }
            };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(publishData);
            AddLog(string.Format("发布成绩: {0} {1} {2}{3}, {4}人, 发送到{5}个连接", gender, eventName, stage, heatDisplay, heatSwimmers.Count, _allSockets.Count));
            foreach (var conn in _allSockets.ToList()) {
                try { conn.Send(json); } catch { }
            }
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
        private void ViewRawData_Click(object sender, RoutedEventArgs e) {
            string dir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", "RawData");
            if (!Directory.Exists(dir)) {
                MessageBox.Show("暂无原始数据文件。\n\n原始数据在比赛确认成绩后自动保存到:\n" + dir, "提示");
                return;
            }
            var files = Directory.GetFiles(dir, "*.txt").OrderByDescending(f => File.GetLastWriteTime(f)).ToArray();
            if (files.Length == 0) {
                MessageBox.Show("暂无原始数据文件。", "提示");
                return;
            }

            var dlg = new Window {
                Title = "查询比赛原始数据",
                Width = 900, Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
            };
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 6) };
            topPanel.Children.Add(new TextBlock {
                Text = "选择比赛:", VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14, Margin = new Thickness(0, 0, 8, 0)
            });
            var combo = new ComboBox { Width = 500, FontSize = 14 };
            foreach (string f in files) {
                string name = IOPath.GetFileNameWithoutExtension(f);
                string time = File.GetLastWriteTime(f).ToString("MM-dd HH:mm");
                combo.Items.Add(new ComboBoxItem { Content = name + "  (" + time + ")", Tag = f });
            }
            var textBox = new TextBox {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Microsoft YaHei"),
                FontSize = 13, Margin = new Thickness(12, 0, 12, 12),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                Padding = new Thickness(10),
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap
            };
            combo.SelectionChanged += delegate {
                var sel = combo.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Tag != null) {
                    try {
                        textBox.Text = File.ReadAllText(sel.Tag.ToString(), Encoding.UTF8);
                        textBox.ScrollToHome();
                    } catch (Exception ex) {
                        textBox.Text = "读取失败: " + ex.Message;
                    }
                }
            };
            var pdfBtn = new Button {
                Content = "导出PDF", Padding = new Thickness(14, 5, 14, 5), FontSize = 13,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
                Foreground = new SolidColorBrush(Colors.White), FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0), Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            pdfBtn.Click += delegate {
                var sel = combo.SelectedItem as ComboBoxItem;
                if (sel == null || sel.Tag == null) return;
                string txtFile = sel.Tag.ToString();
                string htmlFile = IOPath.ChangeExtension(txtFile, ".html");
                if (File.Exists(htmlFile)) {
                    try { Process.Start(htmlFile); } catch (Exception ex2) { MessageBox.Show("打开失败: " + ex2.Message); }
                } else {
                    MessageBox.Show("对应的HTML文件不存在。\n\n该文件可能是旧版本保存的，请重新确认成绩以生成HTML版本。", "提示");
                }
            };
            topPanel.Children.Add(combo);
            topPanel.Children.Add(pdfBtn);
            Grid.SetRow(topPanel, 0);
            mainGrid.Children.Add(topPanel);
            Grid.SetRow(textBox, 1);
            mainGrid.Children.Add(textBox);

            dlg.Content = mainGrid;
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            dlg.ShowDialog();
        }

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
            RefreshOverviewStats();
            Broadcast();
            AddLog("赛事信息已保存: " + _competitionName);
        }

        private void LoadCompetition_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "JSON文件|*.json",
                InitialDirectory = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database"),
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
            RefreshOverviewStats();
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

            // 运动员管理
            if (SwimmerGrid != null) SwimmerGrid.ItemsSource = _swimmers;
            if (RelayGrid != null) RelayGrid.ItemsSource = _relayTeams;
            if (RegEventListBox != null) RegEventListBox.Items.Clear();
            if (RegStatusText != null) RegStatusText.Text = "";

            // 接力队管理
            _selectedRelayTeam = null;
            if (RelayGroupedPanel != null) RelayGroupedPanel.Children.Clear();
            if (RelayLegGrid != null) RelayLegGrid.ItemsSource = null;
            if (RelayLegTitle != null) RelayLegTitle.Text = "棒次安排（请选中一支接力队）";

            // 赛程导航树
            ScheduleTree.Items.Clear();
            if (ScheduleGroupedPanel != null) ScheduleGroupedPanel.Children.Clear();

            // 出场编排微调
            _editSelectedGrid = null;
            if (EditEventCombo != null) EditEventCombo.Items.Clear();
            if (EditHeatCombo != null) EditHeatCombo.Items.Clear();
            if (EditPreviewGrid != null) EditPreviewGrid.ItemsSource = null;
            if (EditAllGroupsPanel != null) EditAllGroupsPanel.Children.Clear();
            if (EditAllGroupsScroll != null) EditAllGroupsScroll.Visibility = System.Windows.Visibility.Collapsed;

            // 成绩与排名
            if (ResultEventCombo != null) ResultEventCombo.Items.Clear();
            if (ResultGrid != null) ResultGrid.ItemsSource = null;

            // 比赛控制
            if (LanePanel != null) LanePanel.Children.Clear();
            _laneSplitCount.Clear();
            _laneSplitShowTime.Clear();
            _laneReactionLastValue.Clear();
            _laneReactionShowTime.Clear();

            // 系统工作状态
            if (CurrentEventText != null) CurrentEventText.Text = "-";
            if (CurrentStageText != null) CurrentStageText.Text = "-";
            if (CurrentHeatText != null) CurrentHeatText.Text = "-";
            if (RaceStateText != null) { RaceStateText.Text = "等待"; RaceStateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); }
            if (RunningTimeText != null) RunningTimeText.Text = "0.00";

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
        // 计时参数独立存储（不依赖比赛数据）
        // ═══════════════════════════════════════════════════════════════
        private string TimingSettingsPath {
            get { return IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "timing_settings.json"); }
        }

        private void SaveTimingSettings() {
            try {
                string json = JsonConvert.SerializeObject(_laneCloseSettings, Formatting.Indented);
                File.WriteAllText(TimingSettingsPath, json, Encoding.UTF8);
            } catch { }
        }

        private void LoadTimingSettings() {
            try {
                if (File.Exists(TimingSettingsPath)) {
                    string json = File.ReadAllText(TimingSettingsPath, Encoding.UTF8);
                    var loaded = JsonConvert.DeserializeObject<LaneCloseSettings>(json);
                    if (loaded != null) {
                        _laneCloseSettings = loaded;
                        AddLog(string.Format("已加载计时参数: 关闭{0}s 出发台{1}s 确认{2}s 抢跳{3}s 分段{4}s 第1名停留{5}s 终点:{6}",
                            _laneCloseSettings.LaneCloseTime, _laneCloseSettings.StartBlockCloseDelay,
                            _laneCloseSettings.ResultConfirmCloseDelay, _laneCloseSettings.FalseStartThreshold,
                            _laneCloseSettings.SplitDisplayTime, _laneCloseSettings.FirstPlaceHoldTime,
                            _laneCloseSettings.FinishPosition == "left" ? "左端" : "右端"));
                    }
                }
            } catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // JSON 持久化
        // ═══════════════════════════════════════════════════════════════
        private void AutoSaveData() {
            if (string.IsNullOrEmpty(_competitionName)) return;
            try {
                string dbDir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database");
                if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);

                File.WriteAllText(
                    IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_competition.txt"),
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
                File.WriteAllText(IOPath.Combine(dbDir, _competitionName + ".json"), json, Encoding.UTF8);
            } catch (Exception ex) {
                AddLog("自动保存失败: " + ex.Message);
            }
        }

        private void LoadLastCompetition() {
            try {
                string lastFile = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_competition.txt");
                if (File.Exists(lastFile)) {
                    string name = File.ReadAllText(lastFile, Encoding.UTF8).Trim();
                    string jsonFile = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", name + ".json");
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

                if (package.LaneCloseSettings != null) {
                    _laneCloseSettings = package.LaneCloseSettings;
                    // 兼容旧数据：FinishPosition为空时，用StartPosition作为默认终点位置
                    if (string.IsNullOrEmpty(_laneCloseSettings.FinishPosition))
                        _laneCloseSettings.FinishPosition = _laneCloseSettings.StartPosition;
                }
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
                RebuildRelayGroupedView();
                UpdateRaceStateDisplay();
                RefreshOverviewStats();
                RefreshRecordFilterCombos();

                AddLog("已加载赛事: " + _competitionName);
            } catch (Exception ex) {
                AddLog("加载赛事失败: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 赛事概览统计
        // ═══════════════════════════════════════════════════════════════
        private void RefreshOverviewStats() {
            if (OverviewSummaryPanel == null || OverviewStatsPanel == null) return;

            // 统计各项目、各性别的报名人数（排除接力队员子条目）
            var athletes = _swimmers.Where(s => s.Notes == null || !s.Notes.StartsWith("接力队员")).ToList();

            // 按项目+性别统计
            var eventStats = new Dictionary<string, int[]>(); // eventName -> [男, 女, 混合]
            var teamSet = new HashSet<string>();
            foreach (var s in athletes) {
                string ev = s.EventName ?? "";
                if (string.IsNullOrEmpty(ev)) continue;
                if (!eventStats.ContainsKey(ev)) eventStats[ev] = new int[3];
                if (s.Gender == "男") eventStats[ev][0]++;
                else if (s.Gender == "女") eventStats[ev][1]++;
                else eventStats[ev][2]++;
                if (!string.IsNullOrEmpty(s.Country)) teamSet.Add(s.Country);
            }

            int totalMale = athletes.Count(s => s.Gender == "男");
            int totalFemale = athletes.Count(s => s.Gender == "女");
            int totalMixed = athletes.Count(s => s.Gender != "男" && s.Gender != "女");
            int totalAthletes = athletes.Count;
            int totalEvents = eventStats.Count;
            int totalTeams = teamSet.Count;

            // 汇总条
            OverviewSummaryPanel.Children.Clear();
            var summaryItems = new[] {
                new { Label = "代表队", Value = totalTeams.ToString(), Color = "#8B5CF6" },
                new { Label = "总人次", Value = totalAthletes.ToString(), Color = "#3B82F6" },
                new { Label = "男", Value = totalMale.ToString(), Color = "#2563EB" },
                new { Label = "女", Value = totalFemale.ToString(), Color = "#EC4899" },
                new { Label = "项目数", Value = totalEvents.ToString(), Color = "#F59E0B" }
            };
            if (totalMixed > 0) summaryItems = new[] {
                new { Label = "代表队", Value = totalTeams.ToString(), Color = "#8B5CF6" },
                new { Label = "总人次", Value = totalAthletes.ToString(), Color = "#3B82F6" },
                new { Label = "男", Value = totalMale.ToString(), Color = "#2563EB" },
                new { Label = "女", Value = totalFemale.ToString(), Color = "#EC4899" },
                new { Label = "混合", Value = totalMixed.ToString(), Color = "#10B981" },
                new { Label = "项目数", Value = totalEvents.ToString(), Color = "#F59E0B" }
            };

            foreach (var item in summaryItems) {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 0) };
                sp.Children.Add(new TextBlock {
                    Text = item.Label + "  ",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    VerticalAlignment = VerticalAlignment.Center, FontSize = 13
                });
                sp.Children.Add(new TextBlock {
                    Text = item.Value,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color)),
                    FontWeight = FontWeights.Bold, FontSize = 20, VerticalAlignment = VerticalAlignment.Center
                });
                OverviewSummaryPanel.Children.Add(sp);
            }

            // 项目明细表
            OverviewStatsPanel.Children.Clear();
            if (eventStats.Count == 0) {
                OverviewStatsPanel.Children.Add(new TextBlock {
                    Text = "暂无项目报名数据", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0)
                });
                return;
            }

            // 按_events中的顺序排列，分为个人项目和接力项目
            var personalEvents = new List<string>();
            var relayEvents = new List<string>();
            foreach (string ev in _events) {
                if (!eventStats.ContainsKey(ev)) continue;
                if (ev.Contains("接力")) relayEvents.Add(ev);
                else personalEvents.Add(ev);
            }
            // 补充不在_events中的项目
            foreach (var ev in eventStats.Keys) {
                if (!personalEvents.Contains(ev) && !relayEvents.Contains(ev)) {
                    if (ev.Contains("接力")) relayEvents.Add(ev);
                    else personalEvents.Add(ev);
                }
            }

            // 创建表格
            Action<string, List<string>> buildTable = (title, evList) => {
                if (evList.Count == 0) return;
                var titleBlock = new TextBlock {
                    Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                    Margin = new Thickness(0, 8, 0, 6)
                };
                OverviewStatsPanel.Children.Add(titleBlock);

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });    // 序号
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 项目名
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });    // 男
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });    // 女
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });    // 合计

                // 表头
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                var headerBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                string[] headers = { "#", "项目名称", "男", "女", "合计" };
                for (int c = 0; c < headers.Length; c++) {
                    var border = new Border { Background = headerBg, Padding = new Thickness(6, 0, 6, 0) };
                    var tb = new TextBlock {
                        Text = headers[c], FontWeight = FontWeights.SemiBold, FontSize = 12,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = c >= 2 ? TextAlignment.Center : TextAlignment.Left
                    };
                    border.Child = tb;
                    Grid.SetRow(border, 0);
                    Grid.SetColumn(border, c);
                    grid.Children.Add(border);
                }

                int sumM = 0, sumF = 0, sumT = 0;
                for (int i = 0; i < evList.Count; i++) {
                    string ev = evList[i];
                    int[] counts = eventStats[ev];
                    int m = counts[0], f = counts[1], t = m + f + counts[2];
                    sumM += m; sumF += f; sumT += t;

                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
                    int row = i + 1;
                    var rowBg = i % 2 == 0 ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    var dimColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    var textColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    var maleColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                    var femaleColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EC4899"));
                    var totalColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));

                    Action<int, string, Brush, TextAlignment, bool> addCell = (col, text, fg, align, bold) => {
                        var border2 = new Border { Background = rowBg, Padding = new Thickness(6, 0, 6, 0),
                            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")), BorderThickness = new Thickness(0, 0, 0, 1) };
                        var tb2 = new TextBlock {
                            Text = text, FontSize = 12, Foreground = fg,
                            VerticalAlignment = VerticalAlignment.Center, TextAlignment = align
                        };
                        if (bold) tb2.FontWeight = FontWeights.SemiBold;
                        border2.Child = tb2;
                        Grid.SetRow(border2, row);
                        Grid.SetColumn(border2, col);
                        grid.Children.Add(border2);
                    };

                    addCell(0, (i + 1).ToString(), dimColor, TextAlignment.Left, false);
                    addCell(1, ev, textColor, TextAlignment.Left, false);
                    addCell(2, m > 0 ? m.ToString() : "-", m > 0 ? maleColor : dimColor, TextAlignment.Center, m > 0);
                    addCell(3, f > 0 ? f.ToString() : "-", f > 0 ? femaleColor : dimColor, TextAlignment.Center, f > 0);
                    addCell(4, t.ToString(), totalColor, TextAlignment.Center, true);
                }

                // 合计行
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                int sumRow = evList.Count + 1;
                var sumBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
                var sumFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF"));
                Action<int, string> addSumCell = (col, text) => {
                    var border3 = new Border { Background = sumBg, Padding = new Thickness(6, 0, 6, 0) };
                    var tb3 = new TextBlock {
                        Text = text, FontSize = 12, FontWeight = FontWeights.Bold,
                        Foreground = sumFg, VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = col >= 2 ? TextAlignment.Center : TextAlignment.Left
                    };
                    border3.Child = tb3;
                    Grid.SetRow(border3, sumRow);
                    Grid.SetColumn(border3, col);
                    grid.Children.Add(border3);
                };
                addSumCell(0, "");
                addSumCell(1, "小计 (" + evList.Count + " 项)");
                addSumCell(2, sumM.ToString());
                addSumCell(3, sumF.ToString());
                addSumCell(4, sumT.ToString());

                OverviewStatsPanel.Children.Add(grid);
            };

            buildTable("个人项目", personalEvents);
            buildTable("接力项目", relayEvents);
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

        private void ConnectUdp_Click(object sender, RoutedEventArgs e) {
            int listenPort = 5001;
            int.TryParse(UdpPortBox.Text.Trim(), out listenPort);
            string sendHost = null;
            int sendPort = 0;
            string sendText = UdpSendBox.Text.Trim();
            if (!string.IsNullOrEmpty(sendText)) {
                string[] parts = sendText.Split(':');
                if (parts.Length == 2) {
                    sendHost = parts[0];
                    int.TryParse(parts[1], out sendPort);
                }
            }
            _timingBridge.ConnectUdp(listenPort, sendHost, sendPort);
            UpdateConnectionStatus();
        }

        private void DisconnectTiming_Click(object sender, RoutedEventArgs e) {
            _timingBridge.Disconnect();
            UpdateConnectionStatus();
        }

        // ═══════════════════════════════════════════════════════════════
        // 纪录管理
        // ═══════════════════════════════════════════════════════════════
        private bool _recordFilterUpdating = false;

        private void RefreshRecordFilterCombos() {
            if (_recordFilterUpdating) return;
            _recordFilterUpdating = true;
            try {
                // 刷新项目下拉框
                string prevEvent = RecordFilterEvent.SelectedItem != null ? RecordFilterEvent.SelectedItem.ToString() : "";
                RecordFilterEvent.Items.Clear();
                RecordFilterEvent.Items.Add("全部");
                var eventSet = new HashSet<string>();
                foreach (var r in _records) {
                    if (!string.IsNullOrEmpty(r.EventName) && eventSet.Add(r.EventName))
                        RecordFilterEvent.Items.Add(r.EventName);
                }
                RecordFilterEvent.SelectedItem = RecordFilterEvent.Items.Contains(prevEvent) ? prevEvent : "全部";

                // 刷新类型下拉框
                string prevType = RecordFilterType.SelectedItem != null ? RecordFilterType.SelectedItem.ToString() : "";
                RecordFilterType.Items.Clear();
                RecordFilterType.Items.Add("全部");
                var typeSet = new HashSet<string>();
                foreach (var r in _records) {
                    if (!string.IsNullOrEmpty(r.RecordType) && typeSet.Add(r.RecordType))
                        RecordFilterType.Items.Add(r.RecordType);
                }
                RecordFilterType.SelectedItem = RecordFilterType.Items.Contains(prevType) ? prevType : "全部";
            } finally {
                _recordFilterUpdating = false;
            }
        }

        private void ApplyRecordFilter() {
            if (_recordFilterUpdating || RecordGrid == null) return;

            string gender = RecordFilterGender.SelectedItem != null ? ((ComboBoxItem)RecordFilterGender.SelectedItem).Content.ToString() : "全部";
            string eventName = RecordFilterEvent.SelectedItem != null ? RecordFilterEvent.SelectedItem.ToString() : "全部";
            string recordType = RecordFilterType.SelectedItem != null ? RecordFilterType.SelectedItem.ToString() : "全部";
            string keyword = RecordFilterKeyword != null ? RecordFilterKeyword.Text.Trim() : "";

            var filtered = new List<SwimmingRecord>();
            foreach (var r in _records) {
                if (gender != "全部" && r.Gender != gender) continue;
                if (eventName != "全部" && r.EventName != eventName) continue;
                if (recordType != "全部" && r.RecordType != recordType) continue;
                if (!string.IsNullOrEmpty(keyword)) {
                    bool match = false;
                    if (r.HolderName != null && r.HolderName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                    if (r.HolderCountry != null && r.HolderCountry.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                    if (r.Location != null && r.Location.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                    if (!match) continue;
                }
                filtered.Add(r);
            }
            RecordGrid.ItemsSource = filtered;
            RecordFilterCount.Text = string.Format("共 {0} 条（总 {1} 条）", filtered.Count, _records.Count);
        }

        private void RecordFilter_Changed(object sender, SelectionChangedEventArgs e) {
            if (_initialized && !_recordFilterUpdating) ApplyRecordFilter();
        }

        private void RecordFilter_Changed(object sender, TextChangedEventArgs e) {
            if (_initialized && !_recordFilterUpdating) ApplyRecordFilter();
        }

        private void RecordFilterReset_Click(object sender, RoutedEventArgs e) {
            _recordFilterUpdating = true;
            RecordFilterGender.SelectedIndex = 0;
            if (RecordFilterEvent.Items.Count > 0) RecordFilterEvent.SelectedIndex = 0;
            if (RecordFilterType.Items.Count > 0) RecordFilterType.SelectedIndex = 0;
            RecordFilterKeyword.Text = "";
            _recordFilterUpdating = false;
            RecordGrid.ItemsSource = _records;
            RecordFilterCount.Text = string.Format("共 {0} 条", _records.Count);
        }

        private void ClearAllRecords_Click(object sender, RoutedEventArgs e) {
            if (_records.Count == 0) {
                MessageBox.Show("当前没有纪录数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = MessageBox.Show(
                string.Format("确定要删除全部 {0} 条纪录数据吗？\n\n此操作不可撤销！", _records.Count),
                "删除全部纪录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) {
                int count = _records.Count;
                _records.Clear();
                AutoSaveData();
                Broadcast();
                RefreshRecordFilterCombos();
                RecordFilterReset_Click(null, null);
                AddLog(string.Format("已删除全部 {0} 条纪录数据", count));
            }
        }

        private void AddRecord_Click(object sender, RoutedEventArgs e) {
            _records.Add(new SwimmingRecord { RecordType = "赛会纪录", Gender = "男" });
            RefreshRecordFilterCombos();
        }

        private void DeleteRecord_Click(object sender, RoutedEventArgs e) {
            var selected = RecordGrid.SelectedItem as SwimmingRecord;
            if (selected != null) {
                _records.Remove(selected);
                AutoSaveData();
                RefreshRecordFilterCombos();
                ApplyRecordFilter();
            }
        }

        private void ImportDefaultRecords_Click(object sender, RoutedEventArgs e) {
            // 预置世界纪录（长池50米，截至2025年最新数据）
            var defaults = new[] {
                // ═══ 男子个人 ═══
                new { G="男", E="50米自由泳",       T=20.91,  H="César Cielo",          C="巴西",   D="2009-12-18", L="圣保罗" },
                new { G="男", E="100米自由泳",      T=46.40,  H="潘展乐",               C="中国",   D="2024-07-31", L="巴黎" },
                new { G="男", E="200米自由泳",      T=102.00, H="Paul Biedermann",      C="德国",   D="2009-07-28", L="罗马" },
                new { G="男", E="400米自由泳",      T=219.96, H="Lukas Märtens",        C="德国",   D="2025-04-12", L="布达佩斯" },
                new { G="男", E="800米自由泳",      T=451.12, H="张琳",                 C="中国",   D="2009-07-29", L="罗马" },
                new { G="男", E="1500米自由泳",     T=870.67, H="Bobby Finke",          C="美国",   D="2024-08-04", L="巴黎" },
                new { G="男", E="50米仰泳",         T=23.55,  H="Kliment Kolesnikov",   C="俄罗斯", D="2023-07-27", L="福冈" },
                new { G="男", E="100米仰泳",        T=51.60,  H="Thomas Ceccon",        C="意大利", D="2022-06-20", L="布达佩斯" },
                new { G="男", E="200米仰泳",        T=111.92, H="Aaron Peirsol",        C="美国",   D="2009-07-31", L="罗马" },
                new { G="男", E="50米蛙泳",         T=25.95,  H="Adam Peaty",           C="英国",   D="2017-07-25", L="布达佩斯" },
                new { G="男", E="100米蛙泳",        T=56.88,  H="Adam Peaty",           C="英国",   D="2019-07-21", L="光州" },
                new { G="男", E="200米蛙泳",        T=125.48, H="覃海洋",               C="中国",   D="2023-07-28", L="福冈" },
                new { G="男", E="50米蝶泳",         T=22.27,  H="Andrii Govorov",       C="乌克兰", D="2018-07-01", L="罗马" },
                new { G="男", E="100米蝶泳",        T=49.45,  H="Caeleb Dressel",       C="美国",   D="2021-07-31", L="东京" },
                new { G="男", E="200米蝶泳",        T=110.34, H="Kristóf Milák",        C="匈牙利", D="2022-06-21", L="布达佩斯" },
                new { G="男", E="200米个人混合泳",  T=112.69, H="Léon Marchand",        C="法国",   D="2025-07-30", L="新加坡" },
                new { G="男", E="400米个人混合泳",  T=242.50, H="Léon Marchand",        C="法国",   D="2023-07-23", L="福冈" },
                // ═══ 男子接力 ═══
                new { G="男", E="4×100米自由泳接力",  T=188.24, H="美国队",  C="美国", D="2008-08-11", L="北京" },
                new { G="男", E="4×200米自由泳接力",  T=418.55, H="美国队",  C="美国", D="2009-07-31", L="罗马" },
                new { G="男", E="4×100米混合泳接力",  T=206.78, H="美国队",  C="美国", D="2021-08-01", L="东京" },
                // ═══ 女子个人 ═══
                new { G="女", E="50米自由泳",       T=23.61,  H="Sarah Sjöström",       C="瑞典",     D="2023-07-29", L="福冈" },
                new { G="女", E="100米自由泳",      T=51.71,  H="Sarah Sjöström",       C="瑞典",     D="2017-07-23", L="布达佩斯" },
                new { G="女", E="200米自由泳",      T=112.23, H="Ariarne Titmus",       C="澳大利亚", D="2024-06-12", L="布里斯班" },
                new { G="女", E="400米自由泳",      T=234.18, H="Summer McIntosh",      C="加拿大",   D="2025-06-07", L="多伦多" },
                new { G="女", E="800米自由泳",      T=484.12, H="Katie Ledecky",        C="美国",     D="2025-05-03", L="印第安纳波利斯" },
                new { G="女", E="1500米自由泳",     T=920.48, H="Katie Ledecky",        C="美国",     D="2018-05-16", L="印第安纳波利斯" },
                new { G="女", E="50米仰泳",         T=26.86,  H="Kaylee McKeown",       C="澳大利亚", D="2023-10-20", L="柏林" },
                new { G="女", E="100米仰泳",        T=57.13,  H="Regan Smith",          C="美国",     D="2024-06-18", L="印第安纳波利斯" },
                new { G="女", E="200米仰泳",        T=123.14, H="Kaylee McKeown",       C="澳大利亚", D="2023-03-10", L="墨尔本" },
                new { G="女", E="50米蛙泳",         T=29.16,  H="Rūta Meilutytė",      C="立陶宛",   D="2023-07-30", L="福冈" },
                new { G="女", E="100米蛙泳",        T=64.13,  H="Lilly King",           C="美国",     D="2017-07-25", L="布达佩斯" },
                new { G="女", E="200米蛙泳",        T=137.55, H="Evgeniia Chikunova",   C="俄罗斯",   D="2023-04-21", L="喀山" },
                new { G="女", E="50米蝶泳",         T=24.43,  H="Sarah Sjöström",       C="瑞典",     D="2014-07-05", L="博洛斯" },
                new { G="女", E="100米蝶泳",        T=54.60,  H="Gretchen Walsh",       C="美国",     D="2025-05-03", L="印第安纳波利斯" },
                new { G="女", E="200米蝶泳",        T=121.81, H="刘子歌",               C="中国",     D="2009-10-21", L="济南" },
                new { G="女", E="200米个人混合泳",  T=125.70, H="Summer McIntosh",      C="加拿大",   D="2025-06-09", L="多伦多" },
                new { G="女", E="400米个人混合泳",  T=263.65, H="Summer McIntosh",      C="加拿大",   D="2025-06-11", L="多伦多" },
                // ═══ 女子接力 ═══
                new { G="女", E="4×100米自由泳接力",  T=207.96, H="澳大利亚队", C="澳大利亚", D="2023-07-23", L="福冈" },
                new { G="女", E="4×200米自由泳接力",  T=457.50, H="澳大利亚队", C="澳大利亚", D="2023-07-27", L="福冈" },
                new { G="女", E="4×100米混合泳接力",  T=229.34, H="美国队",     C="美国",     D="2025-08-03", L="新加坡" },
                // ═══ 混合接力 ═══
                new { G="混合", E="4×100米自由泳接力",  T=198.48, H="美国队", C="美国", D="2025-08-02", L="新加坡" },
                new { G="混合", E="4×100米混合泳接力",  T=217.43, H="美国队", C="美国", D="2024-08-03", L="巴黎" }
            };

            // 先清除所有旧的世界纪录（包括可能因编码问题产生的乱码记录）
            var toRemove = new List<SwimmingRecord>();
            foreach (var r in _records) {
                if (r.RecordType != null && r.RecordType.Contains("世界")) toRemove.Add(r);
            }
            foreach (var r in toRemove) _records.Remove(r);
            int removed = toRemove.Count;

            // 重新写入全部世界纪录
            int added = 0;
            foreach (var d in defaults) {
                _records.Add(new SwimmingRecord {
                    Gender = d.G, EventName = d.E, RecordType = "世界纪录",
                    HolderName = d.H, HolderCountry = d.C,
                    Time = d.T, TimeInSeconds = d.T, Date = d.D, Location = d.L
                });
                added++;
            }
            AddLog(string.Format("世界纪录: 清除旧记录{0}条, 导入{1}条", removed, added));
            AutoSaveData();
            Broadcast();
            RefreshRecordFilterCombos();
        }

        private void ImportRecordsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|文本文件|*.txt|所有文件|*.*",
                Title = "导入纪录（格式：性别,项目,类型,保持者,代表队,成绩,日期,地点）"
            };
            if (dlg.ShowDialog() == true) {
                try {
                    // 检查是否为Excel二进制格式（.xls/.xlsx），提示用户先另存为CSV
                    string ext = System.IO.Path.GetExtension(dlg.FileName).ToLower();
                    if (ext == ".xls" || ext == ".xlsx") {
                        MessageBox.Show(
                            "无法直接读取Excel文件（.xls/.xlsx）。\n\n" +
                            "请在Excel中将文件另存为：\n" +
                            "  文件类型: CSV UTF-8（逗号分隔）(*.csv)\n" +
                            "  或: CSV（逗号分隔）(*.csv)\n\n" +
                            "然后用导出的CSV文件重新导入。",
                            "格式提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 自动检测编码：有UTF-8 BOM用UTF-8，否则用系统默认编码（中文Windows为GBK）
                    Encoding csvEncoding = Encoding.Default;
                    byte[] rawBytes = File.ReadAllBytes(dlg.FileName);
                    if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                        csvEncoding = Encoding.UTF8;
                    string[] lines = File.ReadAllLines(dlg.FileName, csvEncoding);
                    int imported = 0, updated = 0, skippedEmpty = 0, skippedParse = 0, skippedDup = 0;
                    for (int i = 1; i < lines.Length; i++) {
                        string line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line)) continue;

                        // 支持逗号分隔和Tab分隔（Excel另存为文本时用Tab）
                        char delimiter = line.Contains('\t') ? '\t' : ',';
                        string[] cols = line.Split(delimiter);
                        if (cols.Length < 3) continue;

                        string gender = cols[0].Trim();
                        // 规范化项目名：去除多余空格（如"50 米自由泳"→"50米自由泳"）
                        string eventName = System.Text.RegularExpressions.Regex.Replace(cols[1].Trim(), @"\s+", "");
                        string recordType = cols[2].Trim();
                        string holderName = cols.Length > 3 ? cols[3].Trim() : "";
                        string holderCountry = cols.Length > 4 ? cols[4].Trim() : "";
                        string timeStr = cols.Length > 5 ? cols[5].Trim() : "";
                        string dateStr = cols.Length > 6 ? cols[6].Trim() : "";
                        string location = cols.Length > 7 ? cols[7].Trim() : "";

                        // 跳过成绩或保持者为空的行（模板占位行）
                        if (string.IsNullOrEmpty(timeStr) || string.IsNullOrEmpty(holderName)) { skippedEmpty++; continue; }

                        // 清理成绩字符串：去除Excel可能添加的多余字符
                        // Excel可能将 1:42.00 显示为 0:01:42.00 或 1:42:00 等
                        timeStr = timeStr.Replace("'", "").Replace("\u2019", "").Trim();

                        double t = TimeFormatter.Parse(timeStr);
                        if (t <= 0) {
                            AddLog(string.Format("  第{0}行成绩解析失败: [{1}] ({2} {3})", i + 1, timeStr, gender, eventName));
                            skippedParse++;
                            continue;
                        }

                        // 查找是否已存在相同纪录
                        SwimmingRecord existing = null;
                        foreach (var r in _records) {
                            if (r.Gender == gender && r.EventName == eventName && r.RecordType == recordType) {
                                existing = r; break;
                            }
                        }
                        if (existing != null) {
                            if (t < existing.Time || existing.Time <= 0) {
                                existing.HolderName = holderName; existing.HolderCountry = holderCountry;
                                existing.Time = t; existing.TimeInSeconds = t;
                                existing.Date = dateStr; existing.Location = location;
                                updated++;
                            } else { skippedDup++; }
                        } else {
                            _records.Add(new SwimmingRecord {
                                Gender = gender, EventName = eventName, RecordType = recordType,
                                HolderName = holderName, HolderCountry = holderCountry,
                                Time = t, TimeInSeconds = t, Date = dateStr, Location = location
                            });
                            imported++;
                        }
                    }
                    AddLog(string.Format("CSV导入纪录: 新增{0}条, 更新{1}条, 跳过空行{2}, 解析失败{3}, 重复{4}",
                        imported, updated, skippedEmpty, skippedParse, skippedDup));
                    AutoSaveData();
                    Broadcast();
                    RefreshRecordFilterCombos();
                } catch (Exception ex) {
                    AddLog("CSV导入纪录失败: " + ex.Message);
                }
            }
        }

        private void ExportRecordTemplate_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV文件|*.csv",
                Title = "保存纪录模板（用Excel打开填写后，通过【导入CSV纪录】读入）",
                FileName = "游泳纪录模板.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                // 所有比赛项目（个人 + 接力）
                var events = new[] {
                    "50米自由泳", "100米自由泳", "200米自由泳", "400米自由泳", "800米自由泳", "1500米自由泳",
                    "50米仰泳", "100米仰泳", "200米仰泳",
                    "50米蛙泳", "100米蛙泳", "200米蛙泳",
                    "50米蝶泳", "100米蝶泳", "200米蝶泳",
                    "200米个人混合泳", "400米个人混合泳",
                    "4×100米自由泳接力", "4×200米自由泳接力", "4×100米混合泳接力"
                };
                var genders = new[] { "男", "女" };
                var recordTypes = new[] { "世界纪录", "奥运会纪录", "亚洲纪录", "亚洲青年纪录", "全国纪录", "省运会纪录", "赛会纪录" };

                var sb = new System.Text.StringBuilder();
                // BOM + 表头
                sb.Append('\uFEFF');
                sb.AppendLine("性别,项目,类型,保持者,代表队,成绩,日期,地点");

                // 已有的纪录数据先填入对应位置
                var existingMap = new Dictionary<string, SwimmingRecord>();
                foreach (var r in _records) {
                    string key = r.Gender + "|" + r.EventName + "|" + r.RecordType;
                    existingMap[key] = r;
                }

                foreach (string recType in recordTypes) {
                    foreach (string gender in genders) {
                        foreach (string ev in events) {
                            string key = gender + "|" + ev + "|" + recType;
                            SwimmingRecord existing;
                            if (existingMap.TryGetValue(key, out existing) && existing.Time > 0) {
                                sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                                    gender, ev, recType,
                                    existing.HolderName ?? "", existing.HolderCountry ?? "",
                                    TimeFormatter.Format(existing.Time),
                                    existing.Date ?? "", existing.Location ?? ""));
                            } else {
                                sb.AppendLine(string.Format("{0},{1},{2},,,,,", gender, ev, recType));
                            }
                        }
                    }
                    // 混合接力（仅部分项目）
                    if (recType == "世界纪录" || recType == "奥运会纪录") {
                        foreach (string ev in new[] { "4×100米自由泳接力", "4×100米混合泳接力" }) {
                            string key = "混合|" + ev + "|" + recType;
                            SwimmingRecord existing;
                            if (existingMap.TryGetValue(key, out existing) && existing.Time > 0) {
                                sb.AppendLine(string.Format("混合,{0},{1},{2},{3},{4},{5},{6}",
                                    ev, recType,
                                    existing.HolderName ?? "", existing.HolderCountry ?? "",
                                    TimeFormatter.Format(existing.Time),
                                    existing.Date ?? "", existing.Location ?? ""));
                            } else {
                                sb.AppendLine(string.Format("混合,{0},{1},,,,,", ev, recType));
                            }
                        }
                    }
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                AddLog("已导出纪录模板: " + dlg.FileName);
                MessageBox.Show(
                    "纪录模板已保存！\n\n" +
                    "使用说明：\n" +
                    "1. 用Excel打开CSV文件\n" +
                    "2. 在空行中填入各项纪录（保持者、代表队、成绩、日期、地点）\n" +
                    "3. 成绩格式：秒.百分秒(如20.91) 或 分:秒.百分秒(如1:42.00) 或 时:分:秒.百分秒\n" +
                    "4. 已有世界纪录数据已预填，可直接修改\n" +
                    "5. 未填的行会自动跳过\n" +
                    "6. 保存后通过【导入CSV纪录】按钮一次性读入",
                    "纪录模板", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                AddLog("导出纪录模板失败: " + ex.Message);
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
                string dir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Documents");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string filePath = IOPath.Combine(dir, title + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
                File.WriteAllText(filePath, html, Encoding.UTF8);
                Process.Start(filePath);
                AddLog("已生成文档: " + title);
            } catch (Exception ex) {
                AddLog("文档生成失败: " + ex.Message);
            }
        }

        // 通用文档CSS样式（参照跳水赛事系统格式）
        // 接力项目列标题和数据交换：代表队在前，姓名在后
        private static string RelayCol1Header(bool isRelay) { return isRelay ? "代表队" : "姓名"; }
        private static string RelayCol2Header(bool isRelay) { return isRelay ? "姓名" : "代表队"; }
        private static string RelayCol1(bool isRelay, string name, string country) { return isRelay ? country : name; }
        private static string RelayCol2(bool isRelay, string name, string country) { return isRelay ? name : country; }

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
                foreach (var s in session) {
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
                bool manRelay = g.Key.EventName.Contains("接力");
                sb.AppendFormat("<h3>{0} {1}</h3>", g.Key.Gender, g.Key.EventName);
                sb.AppendFormat("<table><tr><th width='50'>号码</th><th width='80'>{0}</th><th width='80'>{1}</th><th width='70'>报名成绩</th><th width='40'>组</th><th width='40'>道</th></tr>",
                    RelayCol1Header(manRelay), RelayCol2Header(manRelay));
                foreach (var sw in g.OrderBy(s => s.Heat).ThenBy(s => s.Lane)) {
                    string mName = sw.Name; string mCountry = sw.Country ?? "";
                    if (manRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                        mName = sw.Notes.Substring("接力队 棒次:".Length);
                    sb.AppendFormat("<tr><td>{0}</td><td><b>{1}</b></td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                        sw.BibNumber, RelayCol1(manRelay, mName, mCountry), RelayCol2(manRelay, mName, mCountry), sw.EntryTime, sw.Heat > 0 ? sw.Heat.ToString() : "", sw.Lane > 0 ? sw.Lane.ToString() : "");
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

            // 按用户编辑的赛程顺序遍历每个赛次的每一组
            foreach (var schedItem in _schedule) {
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

                    bool slRelay = eventName.Contains("接力");
                    sb.AppendFormat("<table><tr><th width='50'>道</th><th width='60'>号码</th><th width='100'>{0}</th><th width='40'>性别</th><th width='100'>{1}</th><th width='80'>备注</th></tr>",
                        RelayCol1Header(slRelay), RelayCol2Header(slRelay));
                    foreach (var t in heatSwimmers) {
                        var s = t.Item1;
                        string remark = "";
                        if (!string.IsNullOrEmpty(s.Status) && (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ" || s.Status == "DQ")) remark = s.Status;
                        string slName = s.Name; string slCountry = s.Country ?? "";
                        if (slRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                            slName = s.Notes.Substring("接力队 棒次:".Length);
                        sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td><b>{2}</b></td><td>{3}</td><td>{4}</td><td style='color:#dc2626;'>{5}</td></tr>",
                            t.Item3, s.BibNumber, RelayCol1(slRelay, slName, slCountry), s.Gender, RelayCol2(slRelay, slName, slCountry), remark);
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

            bool printRelay = _currentEvent.Contains("接力");
            sb.AppendFormat("<table><tr><th width='50'>名次</th><th width='40'>道</th><th width='60'>号码</th><th width='100'>{0}</th><th width='100'>{1}</th><th width='90'>成绩</th><th width='70'>反应时间</th><th width='50'>备注</th></tr>",
                RelayCol1Header(printRelay), RelayCol2Header(printRelay));
            var swimmers = GetCurrentHeatSwimmers().OrderBy(s => s.CurrentRank > 0 ? s.CurrentRank : int.MaxValue).ToList();
            foreach (var sw in swimmers) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (!string.IsNullOrEmpty(sw.Status) && (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ" || sw.Status == "DQ")) remark = sw.Status;
                string pName = sw.Name; string pCountry = sw.Country ?? "";
                if (printRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                    pName = sw.Notes.Substring("接力队 棒次:".Length);
                string timeText = string.IsNullOrEmpty(remark) && r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "";
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td><b>{3}</b></td><td>{4}</td><td style='font-weight:bold; background:#eff6ff;'>{5}</td><td>{6}</td><td style='color:#dc2626;'>{7}</td></tr>",
                    r != null && r.Rank > 0 ? r.Rank.ToString() : "-",
                    sw.Lane, sw.BibNumber, RelayCol1(printRelay, pName, pCountry), RelayCol2(printRelay, pName, pCountry),
                    timeText,
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
                var finalists = ev.Where(s => s.GetResultForStage("决赛") != null && s.GetResultForStage("决赛").FinalTime > 0
                    && s.Status != "DSQ" && s.Status != "DNS" && s.Status != "DNF")
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

            // ═══ 按赛程顺序逐场打印成绩（遵循用户在赛程管理中的自定义顺序） ═══
            foreach (var schedItem in _schedule) {
                string gender = schedItem.Gender;
                string eventName = schedItem.EventName;
                string stage = schedItem.Stage;

                // 查找该场比赛中有成绩记录的运动员（含DSQ等）
                var matchedSwimmers = _swimmers.Where(s =>
                    s.Gender == gender && s.EventName == eventName &&
                    !(s.Notes != null && s.Notes.StartsWith("接力队员")) &&
                    s.GetResultForStage(stage) != null
                ).ToList();

                if (matchedSwimmers.Count == 0) continue;

                // 获取该赛次各组
                var heatNumbers = matchedSwimmers.Select(s => s.GetResultForStage(stage).Heat).Distinct().OrderBy(h => h).ToList();

                foreach (int heat in heatNumbers) {
                    // 正常运动员按成绩排序，DSQ/DNS/DNF排到最后
                    var heatSwimmers = matchedSwimmers.Where(s => s.GetResultForStage(stage).Heat == heat).ToList();
                    Func<Swimmer, bool> isSwDQ = sw2 => sw2.Status == "DSQ" || sw2.Status == "DNS" || sw2.Status == "DNF";
                    heatSwimmers = heatSwimmers
                        .OrderBy(s => isSwDQ(s) ? 1 : 0)
                        .ThenBy(s => isSwDQ(s) ? 0 : s.GetResultForStage(stage).FinalTime)
                        .ToList();

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

                    // 成绩表（接力：代表队在前）
                    bool bookRelay = eventName.Contains("接力");
                    sb.Append("<table><tr><th width='50'>名次</th><th width='40'>道</th><th width='60'>号码</th>");
                    sb.AppendFormat("<th width='100'>{0}</th><th width='100'>{1}</th>", RelayCol1Header(bookRelay), RelayCol2Header(bookRelay));
                    sb.Append("<th width='90'>成绩</th><th width='70'>反应时间</th><th width='50'>备注</th></tr>");

                    int rank = 1;
                    foreach (var sw in heatSwimmers) {
                        var r = sw.GetResultForStage(stage);
                        string remark = "";
                        if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                        else if (!string.IsNullOrEmpty(sw.Status) && (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ" || sw.Status == "DQ")) remark = sw.Status;
                        bool swDQ = !string.IsNullOrEmpty(remark);
                        string rowBg = "";
                        if (!swDQ && stage == "决赛" && !showHeat) {
                            if (rank == 1) rowBg = " style='background:#fef3c7;'";
                            else if (rank == 2) rowBg = " style='background:#f1f5f9;'";
                            else if (rank == 3) rowBg = " style='background:#fef0e7;'";
                        }
                        string bkName = sw.Name; string bkCountry = sw.Country ?? "";
                        if (bookRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                            bkName = sw.Notes.Substring("接力队 棒次:".Length);
                        string rankText = swDQ ? "-" : rank.ToString();
                        string bkTime = swDQ ? "" : (r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "");
                        sb.AppendFormat("<tr{0}><td>{1}</td><td>{2}</td><td>{3}</td>",
                            rowBg, rankText, r.Lane, sw.BibNumber);
                        sb.AppendFormat("<td><b>{0}</b></td><td>{1}</td>", RelayCol1(bookRelay, bkName, bkCountry), RelayCol2(bookRelay, bkName, bkCountry));
                        sb.AppendFormat("<td style='font-weight:bold; background:#eff6ff;'>{0}</td>", bkTime);
                        sb.AppendFormat("<td>{0}</td>", r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F2") : "");
                        sb.AppendFormat("<td style='color:#dc2626;'>{0}</td></tr>", remark);
                        if (!swDQ) rank++;
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
                    string certName = sw.Name;
                    bool certRelay = g.Key.EventName.Contains("接力");
                    if (certRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                        certName = sw.Country + "（" + sw.Notes.Substring("接力队 棒次:".Length) + "）";
                    sb.AppendFormat("<div style='font-size:24px;'><span class='cert-name'>{0}</span>：</div>", certName);
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
                string spName = sw.Name;
                if (_isRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                    spName = sw.Notes.Substring("接力队 棒次:".Length);
                string spLabel = _isRelay ? string.Format("泳道 {0} &nbsp; {1} （{2}）", sw.Lane, sw.Country, spName)
                    : string.Format("泳道 {0} &nbsp; {1} （{2}）", sw.Lane, spName, sw.Country);
                sb.AppendFormat("<h4 style='margin-top:30px;'>{0} &nbsp; 最终成绩：{1}</h4>", spLabel, TimeFormatter.Format(result.FinalTime));
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
            if (MessageBox.Show("确定要退出游泳赛事管理系统？\n\n数据将自动保存。", "确认退出", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
                e.Cancel = true;
                return;
            }
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
            string dbDir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            foreach (var f in Directory.GetFiles(dbDir, "*.json")) {
                var fi = new FileInfo(f);
                _savedCompetitions.Add(new BackupInfo {
                    Name = IOPath.GetFileNameWithoutExtension(fi.Name),
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
