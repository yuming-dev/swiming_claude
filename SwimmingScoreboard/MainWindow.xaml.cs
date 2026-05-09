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
        private List<AgeGroup> _ageGroups = new List<AgeGroup>();
        private List<string> _genders = new List<string> { "男", "女", "混合" };
        private List<string> _stages = new List<string> { "预赛", "半决赛", "决赛" };
        private List<string> _heatCounts = new List<string> { "1组", "2组", "3组", "4组", "5组", "6组", "7组", "8组" };
        private List<BibRange> _bibRanges = new List<BibRange>();
        // 已"确认本组成绩"并锁定的组次，key = "<组别>|<性别>|<项目>|<赛次>|<组次>"
        // 一旦加入永不自动移除，确保赛程导航中的"已完赛"标志稳定
        private HashSet<string> _confirmedHeats = new HashSet<string>();
        private static string ConfirmedHeatKey(string ageGroup, string gender, string eventName, string stage, int heat) {
            return string.Format("{0}|{1}|{2}|{3}|{4}", ageGroup ?? "", gender ?? "", eventName ?? "", stage ?? "", heat);
        }
        private ProgramBookData _programBook = new ProgramBookData();
        private ResultBookData _resultBook = new ResultBookData();
        // 大屏显示主纪录设置（默认 WR/世界纪录）
        private string _displayRecordLabel = "WR";
        private string _displayRecordTypeName = "世界纪录";
        private List<DisplayRecordOption> _displayRecordOptions = new List<DisplayRecordOption> {
            new DisplayRecordOption { Label = "WR", TypeName = "世界纪录" },
            new DisplayRecordOption { Label = "AR", TypeName = "亚洲纪录" },
            new DisplayRecordOption { Label = "NR", TypeName = "全国纪录" },
            new DisplayRecordOption { Label = "省R", TypeName = "省运会纪录" },
            new DisplayRecordOption { Label = "市R", TypeName = "市纪录" },
            new DisplayRecordOption { Label = "CR", TypeName = "赛会纪录" }
        };

        // ═══════════════════════════════════════════════════════════════
        // 比赛状态
        // ═══════════════════════════════════════════════════════════════
        private string _competitionName = "";
        private string _competitionMode = "domestic";
        private PoolConfig _poolConfig = new PoolConfig();
        private LaneCloseSettings _laneCloseSettings = new LaneCloseSettings();
        // 团体计分配置（持久化在 CompetitionPackage.ScoringConfig）
        private ScoringConfig _scoringConfig = new ScoringConfig();
        // 计时硬件通讯参数（串口 / TCP / UDP）— 独立 JSON 持久化，下次运行自动恢复
        private TimingConnectionConfig _timingConn = new TimingConnectionConfig();
        // 硬件滚动时间锚点：收到 0x7F 帧时记录 (硬件秒数, 本地接收时刻)；
        // RaceTimer_Tick 优先用此值 + 自接收以来的本地补偿外推，避免帧间跳动也避免本地时钟偏差
        private double _hwRunningTimeSec = 0;
        private DateTime _hwRunningTimeReceivedAt = DateTime.MinValue;
        private bool _hwRunningTimeAvailable = false;
        // 上次执行复位（本地或硬件触发）的时刻；用于硬件 0x1C 的去抖，
        // 避免"复位 + 紧接着 0x1C 回弹"导致比赛被自动再次启动
        private DateTime _lastResetAt = DateTime.MinValue;
        private const int ResetDebounceMs = 1000;

        private string _currentEvent = "";
        private string _currentGender = "";
        private string _currentStage = "";
        private string _currentAgeGroup = "";   // 当前选中赛程项的组别（空=不限）
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
            LoadTimingConnectionConfig();   // 通讯参数从 timing_connection.json 还原
            LoadLastCompetition();
            PopulateComPorts();
            ApplyTimingConnectionToUi();    // 把保存的串口/TCP/UDP 还原到 UI 控件
            TryAutoReconnectTiming();       // 仅当 AutoReconnectOnStartup=true 才尝试
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
            _ageGroups = new List<AgeGroup> {
                new AgeGroup { Name = "青少年", MinAge = 12, MaxAge = 13 },
                new AgeGroup { Name = "少年",   MinAge = 14, MaxAge = 17 },
                new AgeGroup { Name = "成人",   MinAge = 18, MaxAge = 45 },
                new AgeGroup { Name = "大师",   MinAge = 46, MaxAge = 200 }
            };
            AgeGroupRegistry.Set(_ageGroups);
            RefreshEventComboBoxes();
            RefreshEventsPreview();
            RefreshAgeGroupsPreview();
            RefreshAllAgeGroupFilterCombos();
            RefreshDisplayRecordLabelText();
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
                    case "REGISTER_QUERY":
                        HandleRegisterQuery(socket, msg);
                        break;
                    case "REGISTER_SWIMMER_BATCH":
                        HandleRegisterSwimmerBatch(socket, msg);
                        break;
                    case "REGISTER_SWIMMERS_MULTI":
                        HandleRegisterSwimmersMulti(socket, msg);
                        break;
                    case "REGISTER_RELAY":
                        HandleRegisterRelay(socket, msg);
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
            string regCountry = data["country"] != null ? data["country"].ToString() : "";
            if (string.IsNullOrEmpty(bibNumber)) bibNumber = GenerateNextBibNumber(regCountry);
            var swimmer = new Swimmer {
                Name = data["name"] != null ? data["name"].ToString() : "",
                BibNumber = bibNumber,
                Gender = data["gender"] != null ? data["gender"].ToString() : "男",
                Age = data["age"] != null ? (int)data["age"] : 0,
                Country = data["country"] != null ? data["country"].ToString() : "",
                CountryShort = data["countryShort"] != null ? data["countryShort"].ToString() : "",
                IDNumber = data["idNumber"] != null ? data["idNumber"].ToString() : "",
                Phone = data["phone"] != null ? data["phone"].ToString() : "",
                EventName = data["eventName"] != null ? data["eventName"].ToString() : "",
                EntryTime = data["entryTime"] != null ? data["entryTime"].ToString() : "",
                BirthDate = data["birthDate"] != null ? data["birthDate"].ToString() : "",
                CSANumber = data["csaNumber"] != null ? data["csaNumber"].ToString() : "",
                FINANumber = data["finaNumber"] != null ? data["finaNumber"].ToString() : "",
                Notes = data["notes"] != null ? data["notes"].ToString() : ""
            };
            // 远程指定组别（如"甲组/乙组"）→ 覆盖按年龄自动推断
            string ageGroupStr = data["ageGroup"] != null ? data["ageGroup"].ToString() : "";
            if (!string.IsNullOrEmpty(ageGroupStr)) swimmer.AgeCategory = ageGroupStr;
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
            RefreshSwimmerFilter();
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

            // 生成参赛号（按代表队号码段）
            string batchCountry = swimmerData["country"] != null ? swimmerData["country"].ToString() : "";
            if (string.IsNullOrEmpty(bibNumber)) bibNumber = GenerateNextBibNumber(batchCountry);

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
                    CountryShort = swimmerData["countryShort"] != null ? swimmerData["countryShort"].ToString() : "",
                    IDNumber = idNum,
                    Phone = swimmerData["phone"] != null ? swimmerData["phone"].ToString() : "",
                    EventName = eventName,
                    EntryTime = entryTime,
                    BirthDate = swimmerData["birthDate"] != null ? swimmerData["birthDate"].ToString() : "",
                    CSANumber = swimmerData["csaNumber"] != null ? swimmerData["csaNumber"].ToString() : "",
                    Notes = swimmerData["notes"] != null ? swimmerData["notes"].ToString() : ""
                };
                string batchAgeGroup = swimmerData["ageGroup"] != null ? swimmerData["ageGroup"].ToString() : "";
                if (!string.IsNullOrEmpty(batchAgeGroup)) swimmer.AgeCategory = batchAgeGroup;
                swimmer.EntryTimeSeconds = TimeFormatter.Parse(swimmer.EntryTime);
                _swimmers.Add(swimmer);
                added++;
            }

            AddLog(string.Format("批量注册: {0}({1}) {2}个项目", name, bibNumber, added));
            AutoSaveData();
            RefreshOverviewStats();
            RefreshSwimmerFilter();
            Broadcast();
            SendRegisterResult(socket, true, "", bibNumber);
        }

        private void SendRegisterResult(IWebSocketConnection socket, bool success, string message, string bibNumber) {
            try {
                var result = new { type = "REGISTER_RESULT", data = new { success = success, message = message, bibNumber = bibNumber } };
                socket.Send(JsonConvert.SerializeObject(result));
            } catch { }
        }

        // 网页报名端"多人一次提交"：批量校验整组数据；任何一条失败都不入库，回传逐条结果。
        // 全部通过则一次性写入并广播。客户端按 perEntry[i].success 显示并允许修改未通过的条目。
        private void HandleRegisterSwimmersMulti(IWebSocketConnection socket, JObject msg) {
            var data = msg["data"];
            var entries = data != null ? data["entries"] as JArray : null;
            if (entries == null || entries.Count == 0) {
                SendMultiResult(socket, false, "无报名条目", new List<object>());
                return;
            }

            // 校验阶段：组装规范化条目；同时检查批内重名+项目重复
            var normalized = new List<Tuple<JObject, JArray, bool, string, string>>(); // swimmerData, eventsArr, isResubmit, key(name|gender|country), reason
            var perEntry = new List<object>();
            var batchKeys = new HashSet<string>();
            bool anyFail = false;

            for (int i = 0; i < entries.Count; i++) {
                var en = entries[i] as JObject;
                if (en == null) {
                    perEntry.Add(new { ok = false, message = "条目格式错误", bibNumber = "" });
                    anyFail = true; normalized.Add(null);
                    continue;
                }
                var sw = en["swimmer"] as JObject;
                var evs = en["events"] as JArray;
                bool isResub = en["isResubmit"] != null && (bool)en["isResubmit"];

                if (sw == null) {
                    perEntry.Add(new { ok = false, message = "缺少运动员信息", bibNumber = "" });
                    anyFail = true; normalized.Add(null);
                    continue;
                }
                string nm = sw["name"] != null ? sw["name"].ToString().Trim() : "";
                if (string.IsNullOrEmpty(nm)) {
                    perEntry.Add(new { ok = false, message = "姓名不能为空", bibNumber = "" });
                    anyFail = true; normalized.Add(null);
                    continue;
                }
                if (evs == null || evs.Count == 0) {
                    perEntry.Add(new { ok = false, message = nm + ": 至少添加一个参赛项目", bibNumber = "" });
                    anyFail = true; normalized.Add(null);
                    continue;
                }
                string gd = sw["gender"] != null ? sw["gender"].ToString() : "男";
                string ct = sw["country"] != null ? sw["country"].ToString().Trim() : "";

                // 批内重复（同名+性别+代表队）
                string key = nm + "|" + gd + "|" + ct;
                if (batchKeys.Contains(key)) {
                    perEntry.Add(new { ok = false, message = nm + ": 与本批内其他条目重复（同姓名+性别+代表队）", bibNumber = "" });
                    anyFail = true; normalized.Add(null);
                    continue;
                }
                batchKeys.Add(key);

                // 批内项目重复
                var seenEvents = new HashSet<string>();
                bool dupEv = false;
                string dupName = "";
                foreach (JObject ev in evs) {
                    string en2 = ev["eventName"] != null ? ev["eventName"].ToString() : "";
                    if (seenEvents.Contains(en2)) { dupEv = true; dupName = en2; break; }
                    seenEvents.Add(en2);
                }
                if (dupEv) {
                    perEntry.Add(new { ok = false, message = nm + ": 项目重复 " + dupName, bibNumber = "" });
                    anyFail = true; normalized.Add(null);
                    continue;
                }

                normalized.Add(Tuple.Create(sw, evs, isResub, key, ""));
                perEntry.Add(new { ok = true, message = "", bibNumber = sw["bibNumber"] != null ? sw["bibNumber"].ToString() : "" });
            }

            if (anyFail) {
                SendMultiResult(socket, false, "部分条目校验未通过，请修改后重新提交", perEntry);
                return;
            }

            // 写入阶段：每条独立处理（与现有 HandleRegisterSwimmerBatch 逻辑一致）
            var newPerEntry = new List<object>();
            for (int i = 0; i < normalized.Count; i++) {
                var norm = normalized[i];
                if (norm == null) { newPerEntry.Add(perEntry[i]); continue; }
                var sw = norm.Item1;
                var evs = norm.Item2;
                bool isResub = norm.Item3;

                string name = sw["name"].ToString().Trim();
                string gender = sw["gender"] != null ? sw["gender"].ToString() : "男";
                string bibNumber = sw["bibNumber"] != null ? sw["bibNumber"].ToString() : "";
                string country = sw["country"] != null ? sw["country"].ToString() : "";

                if (isResub && !string.IsNullOrEmpty(bibNumber)) {
                    var toRemove = _swimmers.Where(s => s.BibNumber == bibNumber).ToList();
                    foreach (var s in toRemove) _swimmers.Remove(s);
                    AddLog(string.Format("重新提交: 已清除 {0}({1}) 的 {2} 条旧记录", name, bibNumber, toRemove.Count));
                }
                if (string.IsNullOrEmpty(bibNumber)) bibNumber = GenerateNextBibNumber(country);

                int added = 0;
                foreach (JObject ev in evs) {
                    string eventName = ev["eventName"] != null ? ev["eventName"].ToString() : "";
                    string entryTime = ev["entryTime"] != null ? ev["entryTime"].ToString() : "";
                    string idNum = sw["idNumber"] != null ? sw["idNumber"].ToString() : "";
                    var dup = FindDuplicate(name, gender, eventName, bibNumber, idNum, country);
                    if (dup != null && !isResub) continue;

                    var swimmer = new Swimmer {
                        BibNumber = bibNumber,
                        Name = name,
                        Gender = gender,
                        Age = sw["age"] != null ? (int)sw["age"] : 0,
                        Country = country,
                        CountryShort = sw["countryShort"] != null ? sw["countryShort"].ToString() : "",
                        IDNumber = idNum,
                        Phone = sw["phone"] != null ? sw["phone"].ToString() : "",
                        EventName = eventName,
                        EntryTime = entryTime,
                        BirthDate = sw["birthDate"] != null ? sw["birthDate"].ToString() : "",
                        CSANumber = sw["csaNumber"] != null ? sw["csaNumber"].ToString() : "",
                        Notes = sw["notes"] != null ? sw["notes"].ToString() : ""
                    };
                    string ag = sw["ageGroup"] != null ? sw["ageGroup"].ToString() : "";
                    if (!string.IsNullOrEmpty(ag)) swimmer.AgeCategory = ag;
                    swimmer.EntryTimeSeconds = TimeFormatter.Parse(swimmer.EntryTime);
                    _swimmers.Add(swimmer);
                    added++;
                }
                AddLog(string.Format("多人批量注册: {0}({1}) {2}个项目", name, bibNumber, added));
                newPerEntry.Add(new { ok = true, message = "", bibNumber = bibNumber, name = name, addedCount = added });
            }

            AutoSaveData();
            RefreshOverviewStats();
            RefreshSwimmerFilter();
            Broadcast();
            SendMultiResult(socket, true, "全部报名提交成功", newPerEntry);
        }

        private void SendMultiResult(IWebSocketConnection socket, bool success, string message, List<object> perEntry) {
            try {
                var result = new {
                    type = "REGISTER_MULTI_RESULT",
                    data = new { success = success, message = message, entries = perEntry }
                };
                socket.Send(JsonConvert.SerializeObject(result));
            } catch { }
        }

        // 网页报名端"查询/修改我已经提交的报名"：根据 bibNumber 或 (姓名+代表队) 找到该运动员所有报名条目，
        // 回送基本资料 + 参赛项目列表，用户在浏览器修改后会以 isResubmit=true 重新提交，服务器先清旧记录再写入新记录
        private void HandleRegisterQuery(IWebSocketConnection socket, JObject msg) {
            try {
                var data = msg["data"];
                string bib = data != null && data["bibNumber"] != null ? data["bibNumber"].ToString().Trim() : "";
                string name = data != null && data["name"] != null ? data["name"].ToString().Trim() : "";
                string country = data != null && data["country"] != null ? data["country"].ToString().Trim() : "";

                List<Swimmer> matches = null;
                if (!string.IsNullOrEmpty(bib)) {
                    matches = _swimmers.Where(s => s.BibNumber == bib && !(s.Notes != null && s.Notes.StartsWith("接力队员"))).ToList();
                } else if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(country)) {
                    matches = _swimmers.Where(s => s.Name == name && (s.Country ?? "") == country &&
                        !(s.Notes != null && s.Notes.StartsWith("接力队员"))).ToList();
                }
                if (matches == null || matches.Count == 0) {
                    var miss = new { type = "REGISTER_QUERY_RESULT", data = new { success = false, message = "未找到对应的报名记录" } };
                    socket.Send(JsonConvert.SerializeObject(miss));
                    return;
                }

                var first = matches[0];
                var swimmerInfo = new {
                    bibNumber = first.BibNumber ?? "",
                    name = first.Name ?? "",
                    gender = first.Gender ?? "",
                    birthDate = first.BirthDate ?? "",
                    idNumber = first.IDNumber ?? "",
                    country = first.Country ?? "",
                    countryShort = first.CountryShort ?? "",
                    ageGroup = first.AgeCategory ?? "",
                    phone = first.Phone ?? "",
                    csaNumber = first.CSANumber ?? "",
                    notes = first.Notes ?? ""
                };
                var eventList = matches.Select(s => new {
                    eventName = s.EventName ?? "",
                    entryTime = s.EntryTime ?? ""
                }).ToList();

                var result = new {
                    type = "REGISTER_QUERY_RESULT",
                    data = new { success = true, swimmer = swimmerInfo, events = eventList }
                };
                socket.Send(JsonConvert.SerializeObject(result));
                AddLog(string.Format("网页查询报名: {0}({1}) → {2} 个项目", first.Name, first.BibNumber, eventList.Count));
            } catch (Exception ex) {
                AddLog("REGISTER_QUERY 处理异常: " + ex.Message);
            }
        }

        private void SendRelayResult(IWebSocketConnection socket, bool success, string message, string teamName, string bibNumber, int legCount, bool updated) {
            if (socket == null) return;
            try {
                var result = new {
                    type = "REGISTER_RELAY_RESULT",
                    data = new {
                        success = success,
                        message = message,
                        teamName = teamName,
                        bibNumber = bibNumber,
                        legCount = legCount,
                        updated = updated
                    }
                };
                socket.Send(JsonConvert.SerializeObject(result));
            } catch { }
        }

        private void HandleRegisterRelay(IWebSocketConnection socket, JObject msg) {
            var data = msg["data"];
            if (data == null) { SendRelayResult(socket, false, "数据不完整", "", "", 0, false); return; }
            var team = new RelayTeam {
                TeamName = data["teamName"] != null ? data["teamName"].ToString() : "",
                EventName = data["eventName"] != null ? data["eventName"].ToString() : "",
                Gender = data["gender"] != null ? data["gender"].ToString() : "男",
                EntryTime = data["entryTime"] != null ? data["entryTime"].ToString() : ""
            };
            if (string.IsNullOrEmpty(team.TeamName)) { SendRelayResult(socket, false, "队名不能为空", "", "", 0, false); return; }
            if (string.IsNullOrEmpty(team.EventName)) { SendRelayResult(socket, false, "请选择项目", team.TeamName, "", 0, false); return; }
            team.EntryTimeSeconds = TimeFormatter.Parse(team.EntryTime);
            var legs = data["legs"] as JArray;
            if (legs != null) {
                foreach (JObject leg in legs) {
                    team.Legs.Add(new RelayLeg {
                        LegOrder = leg["legOrder"] != null ? (int)leg["legOrder"] : 0,
                        SwimmerName = leg["swimmerName"] != null ? leg["swimmerName"].ToString() : "",
                        SwimmerBibNumber = leg["swimmerBibNumber"] != null ? leg["swimmerBibNumber"].ToString() : "",
                        SwimmerIDNumber = leg["swimmerIDNumber"] != null ? leg["swimmerIDNumber"].ToString() : "",
                        SwimmerBirthDate = leg["swimmerBirthDate"] != null ? leg["swimmerBirthDate"].ToString() : ""
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

            if (team.Legs.Count == 0) { SendRelayResult(socket, false, "请至少填写 1 棒队员姓名", team.TeamName, "", 0, false); return; }

            // 防止同一接力队重复注册（队名+性别+项目唯一）
            var existingTeam = _relayTeams.FirstOrDefault(t =>
                t.TeamName == team.TeamName && t.Gender == team.Gender && t.EventName == team.EventName);
            if (existingTeam != null) {
                // 重复注册：用最新数据覆盖现有队伍（更新报名成绩与棒次）
                existingTeam.EntryTime = team.EntryTime;
                existingTeam.EntryTimeSeconds = team.EntryTimeSeconds;
                existingTeam.Legs.Clear();
                foreach (var leg in team.Legs) existingTeam.Legs.Add(leg);
                AddLog(string.Format("更新接力队: {0} ({1}) {2}人", team.TeamName, team.EventName, team.Legs.Count));
                RebuildRelayGroupedView();
                AutoSaveData();
                Broadcast();
                // 反馈：更新已存在的接力队
                var existingProxy = _swimmers.FirstOrDefault(s =>
                    s.Country == team.TeamName && s.Gender == team.Gender && s.EventName == team.EventName
                    && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"));
                string existBib = existingProxy != null ? (existingProxy.BibNumber ?? "") : "";
                SendRelayResult(socket, true, "接力队信息已更新", team.TeamName, existBib, team.Legs.Count, true);
                return;
            }
            _relayTeams.Add(team);

            // 在_swimmers中创建代表该接力队的条目，统一走日程/分组/成绩流程
            string legNames = "";
            foreach (var leg in team.Legs) legNames += (legNames.Length > 0 ? "," : "") + leg.SwimmerName;
            string bibNumber = "R" + (_relayTeams.Count).ToString("D3");
            // 检查重复
            var dup = FindDuplicate(team.TeamName, team.Gender, team.EventName, bibNumber, "", team.TeamName);
            string relayCountryShort = data["countryShort"] != null ? data["countryShort"].ToString() : "";
            string relayAgeGroup = data["ageGroup"] != null ? data["ageGroup"].ToString() : "";
            if (dup == null) {
                var proxy = new Swimmer {
                    BibNumber = bibNumber,
                    Name = team.TeamName,
                    Gender = team.Gender,
                    Country = team.TeamName,
                    CountryShort = relayCountryShort,
                    EventName = team.EventName,
                    EntryTime = team.EntryTime,
                    EntryTimeSeconds = team.EntryTimeSeconds,
                    Notes = string.Format("接力队 棒次:{0}", legNames)
                };
                if (!string.IsNullOrEmpty(relayAgeGroup)) proxy.AgeCategory = relayAgeGroup;
                _swimmers.Add(proxy);
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
                    if (!string.IsNullOrEmpty(leg.SwimmerBirthDate))
                        memExisting.BirthDate = leg.SwimmerBirthDate;
                    if (string.IsNullOrEmpty(memExisting.BibNumber)) memExisting.BibNumber = memBib;
                } else {
                    var mem = new Swimmer {
                        BibNumber = memBib,
                        Name = leg.SwimmerName,
                        Gender = team.Gender == "混合" ? "男" : team.Gender, // 混合默认标男，可后续编辑
                        Country = team.TeamName,
                        CountryShort = relayCountryShort,
                        IDNumber = leg.SwimmerIDNumber ?? "",
                        BirthDate = leg.SwimmerBirthDate ?? "",
                        EventName = team.EventName,
                        Notes = string.Format("接力队员 {0} 第{1}棒", team.EventName, leg.LegOrder)
                    };
                    if (!string.IsNullOrEmpty(relayAgeGroup)) mem.AgeCategory = relayAgeGroup;
                    _swimmers.Add(mem);
                }
            }

            AddLog(string.Format("注册接力队: {0} ({1}) {2}人 [{3}]", team.TeamName, team.EventName, team.Legs.Count, legNames));
            RebuildRelayGroupedView();
            AutoSaveData();
            Broadcast();
            SendRelayResult(socket, true, "接力队报名成功", team.TeamName, bibNumber, team.Legs.Count, false);
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
                    // 与本地"计时复位"一致：0x20 → 0x7F → 0x43（重发缺道，因为硬件 0x20 会清掉缺道位图）
                    if (_timingBridge != null && _timingBridge.IsConnected) {
                        _timingBridge.SendCommand(0x20);
                        _timingBridge.DelayBetweenFrames(20);
                        _timingBridge.SendCommand(0x7F);
                        _timingBridge.DelayBetweenFrames(20);
                        try { SendSetMatchEventToHardware(); } catch (Exception ex) { AddLog("Set_MatchEvent 重发失败: " + ex.Message); }
                    }
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
                    if (IsHeatSwitchBlocked("切换项目")) break;
                    if (data != null) {
                        _currentGender = data.ToString();
                        AddLog("设置性别: " + _currentGender);
                    }
                    break;
                case "SET_AGEGROUP":
                    if (IsHeatSwitchBlocked("切换组别")) break;
                    if (data != null) {
                        _currentAgeGroup = data.ToString() ?? "";
                        AddLog("设置组别: " + (string.IsNullOrEmpty(_currentAgeGroup) ? "（不限）" : _currentAgeGroup));
                    }
                    break;
                case "SET_EVENT":
                    if (IsHeatSwitchBlocked("切换项目")) break;
                    if (data != null) SetCurrentEvent(data.ToString());
                    break;
                case "SET_STAGE":
                    if (IsHeatSwitchBlocked("切换赛次")) break;
                    if (data != null) SetCurrentStage(data.ToString());
                    break;
                case "SET_HEAT":
                    if (IsHeatSwitchBlocked("切换组次")) break;
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
                case "ADJUST_LAP_DISPLAY":
                    if (data != null) {
                        int aldLane = (int)data["lane"];
                        bool aldLeft = data["isLeft"] != null && (bool)data["isLeft"];
                        int aldDelta = data["delta"] != null ? (int)data["delta"] : 0;
                        if (aldDelta != 0) AdjustLapDisplay(aldLane, aldLeft, aldDelta);
                    }
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
                        // 左右盲表数量（来自远程台）
                        if (data["leftBlindWatchCount"] != null) {
                            int v = (int)data["leftBlindWatchCount"];
                            if (v >= 1 && v <= 3) _laneCloseSettings.LeftBlindWatchCount = v;
                        }
                        if (data["rightBlindWatchCount"] != null) {
                            int v = (int)data["rightBlindWatchCount"];
                            if (v >= 1 && v <= 3) _laneCloseSettings.RightBlindWatchCount = v;
                        }
                        if (data["bigDisplayPageInterval"] != null) {
                            _laneCloseSettings.BigDisplayPageInterval = (double)data["bigDisplayPageInterval"];
                        }
                        if (data["reactionTimeEnabled"] != null) {
                            _laneCloseSettings.ReactionTimeEnabled = (bool)data["reactionTimeEnabled"];
                        }
                        if (data["laneOrder"] != null) {
                            _laneCloseSettings.LaneOrder = data["laneOrder"].ToString();
                        }
                        AddLog(string.Format("参数更新: 关闭{0}s 出发台{1}s 确认{2}s 抢跳{3}s 分段{4}s 终点:{5} 盲表 左{6}/右{7} 翻屏{8}s 道次:{9}",
                            _laneCloseSettings.LaneCloseTime, _laneCloseSettings.StartBlockCloseDelay,
                            _laneCloseSettings.ResultConfirmCloseDelay, _laneCloseSettings.FalseStartThreshold,
                            _laneCloseSettings.SplitDisplayTime, _laneCloseSettings.FinishPosition == "left" ? "左端" : "右端",
                            _laneCloseSettings.LeftBlindWatchCount, _laneCloseSettings.RightBlindWatchCount,
                            _laneCloseSettings.BigDisplayPageInterval,
                            _laneCloseSettings.LaneOrder == "reverse" ? "逆序9→0" : "正序0→9"));
                        SaveTimingSettings();
                        AutoSaveData();
                        UpdateLaneStatusDisplay();
                        Broadcast();
                        SendTimingSettingsToHardware();   // 同步到硬件计时控制器
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
            // 关闭RT：完全跳过反应时检测/抢跳判定
            if (!_laneCloseSettings.ReactionTimeEnabled) return;
            var data = msg["data"];
            if (data == null) return;
            int lane = (int)data["lane"];
            double reactionTime = (double)data["reactionTime"];
            var state = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (state != null) {
                // 起跳犯规判罚改为人工：硬件检测到的"抢跳"只作为可疑标记（反应时标红），是否判罚由裁判手动 DSQ
                state.IsSuspectFalseStart = true;
                state.ReactionTime = reactionTime;
                AddLog(string.Format("⚠ 起跳可疑（待裁判确认）泳道{0} 反应时: {1:F3}s", lane, reactionTime));
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
            // 比赛控制面板上的快捷"连接串口"按钮跟随状态变化
            UpdateQuickConnectButton();
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

        // 轻量级滚动时间广播：硬件每 100ms 发一帧 0x7F，主服务器收到后立刻把当前
        // _runningTime 发给所有客户端（大屏 / 计时控制台）。只送 type+runningTime 两个字段，
        // 比 Broadcast() 全量状态轻得多，适合 10Hz 高频转发。
        private void BroadcastRunningTime() {
            if (!_initialized || _allSockets.Count == 0) return;
            try {
                var msg = new {
                    type = "RUNNING_TIME_UPDATE",
                    data = new { runningTime = TimeFormatter.FormatRunning(_runningTime) }
                };
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
                    countryShort = sw.CountryShort ?? "",
                    ageGroup = sw.AgeCategory ?? "",
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
                    reactionTime = (_laneCloseSettings.ReactionTimeEnabled && laneState != null && laneState.ReactionTime != 0) ? laneState.ReactionTime.ToString("F2") : "",
                    // 反应时序号：每次 laneState.ReactionTime 写入即 +1，用于客户端识别"新一棒反应时"
                    // 触发大屏备注栏的反应时显示窗口（接力 4 棒共 4 个 seq）
                    reactionSeq = laneState != null ? laneState.ReactionSeq : 0,
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
                    isSuspectFalseStart = laneState != null && laneState.IsSuspectFalseStart,
                    isNewRecord = result != null && !string.IsNullOrEmpty(result.RecordNote),
                    recordNote = result != null ? (result.RecordNote ?? "") : "",
                    currentLap = laneState != null ? laneState.CurrentLap : 0,
                    // 含 spinner 人工偏移的"显示用当前圈数" = 总圈数 − 左剩余显示 − 右剩余显示。
                    // 接力赛大屏 / 三端控制台据此推算"第N棒"，让加圈/减圈即时切换棒次显示。
                    displayedCurrentLap = laneState != null ? GetDisplayedCurrentLap(laneState) : 0,
                    // 已记录最终成绩的运动员（即便 LaneDeviceState.IsFinished 因切组被复位）也算作完赛
                    isFinished = (laneState != null && laneState.IsFinished) || (result != null && result.FinalTime > 0),
                    leftTouchRemain = GetDisplayedLapCount(laneState, true),
                    rightTouchRemain = GetDisplayedLapCount(laneState, false)
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
                        name = "", country = "", countryShort = "", ageGroup = "",
                        bibNumber = "", entryTime = "",
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
                        isSuspectFalseStart = false,
                        isNewRecord = false,
                        recordNote = "",
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
                currentAgeGroup = _currentAgeGroup ?? "",
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
                    firstPlaceHoldTime = _laneCloseSettings.FirstPlaceHoldTime,
                    leftBlindWatchCount = _laneCloseSettings.LeftBlindWatchCount,
                    rightBlindWatchCount = _laneCloseSettings.RightBlindWatchCount,
                    bigDisplayPageInterval = _laneCloseSettings.BigDisplayPageInterval,
                    reactionTimeEnabled = _laneCloseSettings.ReactionTimeEnabled,
                    laneOrder = _laneCloseSettings.LaneOrder
                },
                displayRecordLabel = string.IsNullOrEmpty(_displayRecordLabel) ? "WR" : _displayRecordLabel,
                displayRecordTypeName = string.IsNullOrEmpty(_displayRecordTypeName) ? "世界纪录" : _displayRecordTypeName,
                timingHwConnected = _timingBridge != null && _timingBridge.IsConnected,
                timingHwStatus = _timingBridge != null ? _timingBridge.StatusText : "未连接",
                scoringControlMode = _scoringControlMode,
                resultConfirmed = _resultConfirmed,
                // 软件设置的组别/项目/性别/赛次列表 — 用于网页报名/检录端动态填充下拉
                ageGroups = _ageGroups.Select(g => g.Name).ToList(),
                eventList = _events,
                genderList = _genders,
                stageList = _stages,
                schedule = _schedule.Select(s => {
                    int hc = s.HeatCount > 0 ? s.HeatCount : 1;
                    string ag = s.AgeGroup ?? "";
                    var heatConfirmed = new List<bool>();
                    for (int hh = 1; hh <= hc; hh++) heatConfirmed.Add(IsHeatConfirmed(ag, s.Gender, s.EventName, s.Stage, hh));
                    return new {
                        session = s.SessionNumber, sessionName = s.SessionName,
                        date = s.Date, time = s.Time,
                        ageGroup = ag,
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

        // 判断泳道是否参赛：有运动员 且 状态不是 DNS/DNF/DSQ（用于排名/积分等"算成绩"决策）
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

        // 判断泳道是否仍需接收/保存原始计时数据：
        // DSQ（含抢跳取消资格）运动员仍在水中继续完成比赛，触板/盲表/分段/反应时数据应继续接收并保存以便日后查询；
        // 仅大屏排名/最终成绩侧把它排除（由 IsLaneParticipating 控制）。
        // DNS / DNF / 空泳道则没有数据可期，直接丢弃。
        private bool IsLaneReceivingData(int lane) {
            var swimmers = GetCurrentHeatSwimmers();
            var sw = swimmers.FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (sw == null) return false;
            string st = sw.Status ?? "";
            return st != "DNS" && st != "DNF";
        }

        private List<Swimmer> GetCurrentHeatSwimmers() {
            if (string.IsNullOrEmpty(_currentEvent) || _currentHeat <= 0) return new List<Swimmer>();
            bool isRelay = _currentEvent.Contains("接力");
            var result = new List<Swimmer>();
            foreach (var s in _swimmers) {
                if (s.EventName != _currentEvent) continue;
                // 性别匹配：兼容"男"/"男子"等格式
                if (s.Gender != _currentGender && !s.Gender.StartsWith(_currentGender) && !_currentGender.StartsWith(s.Gender)) continue;
                // 组别匹配：当前赛程项有指定组别时只取该组的运动员（避免男甲+男乙并在同一个第1组里）
                if (!MatchesAgeGroup(s, _currentAgeGroup)) continue;
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

            // 并列名次（competition ranking 1-2-2-4）：同 FinalTime 并列，后续名次跳过
            // 浮点容差：百分秒级精度，5ms 内视为并列
            int idxER = 0, rank = 1;
            double prevTimeER = -1;
            foreach (var sw in withTimes) {
                var r = sw.GetResultForStage(_currentStage);
                idxER++;
                double curT = r != null ? r.FinalTime : 0;
                if (idxER == 1 || !IsTieTime(curT, prevTimeER)) rank = idxER;
                prevTimeER = curT;
                string rkName = sw.Name;
                if (rankRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                    rkName = sw.Notes.Substring("接力队 棒次:".Length);
                ranked.Add(new {
                    rank = rank,
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
                    // 硬件刚连上时，把当前参数/设备状态/比赛距离/发令点下发一次（以服务器为准）
                    if (_timingBridge != null && _timingBridge.IsConnected) {
                        SendTimingSettingsToHardware();
                        SendDeviceStatusesToHardware();
                        SendSetMatchEventToHardware();   // 0x43 — 总圈数+左右触板次数+空泳道位图
                        SendStartPositionToHardware();   // 0x10 0x42 — 发令点
                    }
                });
            };
            _timingBridge.OnLog += delegate(string msg) {
                Dispatcher.Invoke((Action)delegate() {
                    AddLog(msg);
                });
            };
        }

        private void ProcessTimingDataFromHardware(TimingData data) {
            // 硬件下发的参数设置帧：不走运动员计时路径，走双向同步
            if (data.CommandType == TimingCommandType.PoolConfig ||
                data.CommandType == TimingCommandType.RaceConfig ||
                data.CommandType == TimingCommandType.SetCommand) {
                ApplyHardwareSettingFrame(data);
                return;
            }

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

        // ═══════════════════════════════════════════════════════════════
        // 硬件计时控制器参数双向同步（按 2023-11-13 通讯协议）
        //   下发：按确认后的参数生成 0x40 / 0x42 帧 → 发送到硬件
        //   接收：0x40 / 0x41 / 0x42 帧 → 更新 _laneCloseSettings/_poolConfig 并广播到三端
        // 0x40 D3=泳道数(8-10) D4=泳池长度(25/50 米)
        // 0x42 D3=子参数码 D4=参数值（按下表）
        //   0x01 LaneCloseTime              秒（0-255）
        //   0x02 StartBlockCloseDelay       0.1 秒（D4 × 0.1）
        //   0x03 ResultConfirmCloseDelay    0.1 秒
        //   0x04 FalseStartThreshold        0.01 秒
        //   0x05 SplitDisplayTime           秒
        //   0x06 FirstPlaceHoldTime         秒
        //   0x07 FinishPosition             0=左端，1=右端
        //   0x09 LaneOrder                  0=正序0→9，1=逆序9→0（道次显示方向）
        // ═══════════════════════════════════════════════════════════════
        private bool _applyingHardwareSettings = false;

        private void SendTimingSettingsToHardware() {
            if (_applyingHardwareSettings) return;                // 来自硬件的回环，不要再发
            if (_timingBridge == null || !_timingBridge.IsConnected) return;
            try {
                // 0x40 泳池参数
                byte lanes = (byte)Math.Max(0, Math.Min(255, _poolConfig.LaneCount));
                byte length = (byte)Math.Max(0, Math.Min(255, _poolConfig.Length));
                _timingBridge.SendCommand(0x40, lanes, length);
                _timingBridge.DelayBetweenFrames(20);

                // 0x42 各项设置子码 — 每帧间留 50ms，避免硬件来不及处理
                _timingBridge.SendCommand(0x42, 0x01, ByteClamp(_laneCloseSettings.LaneCloseTime));
                _timingBridge.DelayBetweenFrames(20);
                _timingBridge.SendCommand(0x42, 0x02, ByteClamp(_laneCloseSettings.StartBlockCloseDelay * 10));
                _timingBridge.DelayBetweenFrames(20);
                _timingBridge.SendCommand(0x42, 0x03, ByteClamp(_laneCloseSettings.ResultConfirmCloseDelay * 10));
                _timingBridge.DelayBetweenFrames(20);
                _timingBridge.SendCommand(0x42, 0x04, ByteClamp(_laneCloseSettings.FalseStartThreshold * 100));
                _timingBridge.DelayBetweenFrames(20);
                _timingBridge.SendCommand(0x42, 0x05, ByteClamp(_laneCloseSettings.SplitDisplayTime));
                _timingBridge.DelayBetweenFrames(20);
                _timingBridge.SendCommand(0x42, 0x06, ByteClamp(_laneCloseSettings.FirstPlaceHoldTime));
                _timingBridge.DelayBetweenFrames(20);
                byte fp = _laneCloseSettings.FinishPosition == "right" ? (byte)1 : (byte)0;
                _timingBridge.SendCommand(0x42, 0x07, fp);
                _timingBridge.DelayBetweenFrames(20);
                // 0x08 盲表数量（D4 高 4 位=左端 1-3，低 4 位=右端 1-3）
                int lc = _laneCloseSettings.LeftBlindWatchCount, rc = _laneCloseSettings.RightBlindWatchCount;
                if (lc < 1) lc = 1; if (lc > 3) lc = 3;
                if (rc < 1) rc = 1; if (rc > 3) rc = 3;
                byte bw = (byte)(((lc & 0x0F) << 4) | (rc & 0x0F));
                _timingBridge.SendCommand(0x42, 0x08, bw);
                _timingBridge.DelayBetweenFrames(20);
                // 0x09 道次顺序：0=正序 0→9，1=逆序 9→0
                byte lo = _laneCloseSettings.LaneOrder == "reverse" ? (byte)1 : (byte)0;
                _timingBridge.SendCommand(0x42, 0x09, lo);
                _timingBridge.DelayBetweenFrames(20);

                AddLog(string.Format(
                    "参数已同步到硬件: 泳池{0}米{1}道, 关闭{2}s, 出发台{3}s, 确认{4}s, 抢跳{5}s, 分段{6}s, 第1名{7}s, 终点:{8}, 道次:{9}",
                    _poolConfig.Length, _poolConfig.LaneCount,
                    _laneCloseSettings.LaneCloseTime, _laneCloseSettings.StartBlockCloseDelay,
                    _laneCloseSettings.ResultConfirmCloseDelay, _laneCloseSettings.FalseStartThreshold,
                    _laneCloseSettings.SplitDisplayTime, _laneCloseSettings.FirstPlaceHoldTime,
                    _laneCloseSettings.FinishPosition == "left" ? "左端" : "右端",
                    _laneCloseSettings.LaneOrder == "reverse" ? "逆序9→0" : "正序0→9"));
            } catch (Exception ex) {
                AddLog("参数下发硬件失败: " + ex.Message);
            }
        }

        // 仅同步盲表数量到硬件（避免重发整组参数）
        private void SendBlindWatchCountToHardware() {
            if (_applyingHardwareSettings) return;
            if (_timingBridge == null || !_timingBridge.IsConnected) return;
            try {
                int lc = _laneCloseSettings.LeftBlindWatchCount, rc = _laneCloseSettings.RightBlindWatchCount;
                if (lc < 1) lc = 1; if (lc > 3) lc = 3;
                if (rc < 1) rc = 1; if (rc > 3) rc = 3;
                byte bw = (byte)(((lc & 0x0F) << 4) | (rc & 0x0F));
                _timingBridge.SendCommand(0x42, 0x08, bw);
                AddLog(string.Format("盲表数量已同步到硬件：左 {0}，右 {1}（0x42/0x08 = 0x{2:X2}）", lc, rc, bw));
            } catch (Exception ex) {
                AddLog("盲表数量下发硬件失败: " + ex.Message);
            }
        }

        private static byte ByteClamp(double v) {
            int n = (int)Math.Round(v);
            if (n < 0) n = 0;
            if (n > 255) n = 255;
            return (byte)n;
        }

        // 设备损坏位图编码（2023-11-13 协议扩展：0x42 D3=0x10 子码）
        //   D4 = 泳道号（0-9）
        //   D5 = 左端损坏位图   D6 = 右端损坏位图
        //   bit0=触板  bit1=盲表1  bit2=盲表2  bit3=盲表3  bit4=出发台   （1=损坏）
        private const byte DEVSTAT_SUBCODE = 0x10;

        // 比赛距离子码（2023-11-13 协议扩展：0x42 D3=0x20）
        //   D4 = 趟数 (totalLaps, 1-255)
        //   D5 = 左端触板总次数       D6 = 右端触板总次数
        //   D7 = 距离高字节 (米/256)  D8 = 距离低字节 (米%256)
        //   发送时机：当前项目变更（SetCurrentEvent / SetCurrentHeat）、硬件刚连上
        private const byte RACEDIST_SUBCODE = 0x20;

        // 计算某项目当前出发方向下的左/右触板总次数
        private void GetRaceTouchTotals(int totalLaps, out int leftTotal, out int rightTotal) {
            int startSideTotal, farSideTotal;
            if (totalLaps <= 1) { startSideTotal = 0; farSideTotal = 1; }
            else { startSideTotal = totalLaps / 2; farSideTotal = (totalLaps + 1) / 2; }
            bool startFromLeft = _laneCloseSettings.StartPosition != "right";
            if (startFromLeft) { leftTotal = startSideTotal; rightTotal = farSideTotal; }
            else               { leftTotal = farSideTotal;   rightTotal = startSideTotal; }
        }

        private int GetRaceDistanceMeters() {
            string ev = _currentEvent ?? "";
            if (ev.Contains("x") || ev.Contains("×")) {
                var m = System.Text.RegularExpressions.Regex.Match(ev, @"(\d+)\s*[x×]\s*(\d+)");
                if (m.Success) return int.Parse(m.Groups[1].Value) * int.Parse(m.Groups[2].Value);
            }
            var d = System.Text.RegularExpressions.Regex.Match(ev, @"(\d+)米");
            if (d.Success) return int.Parse(d.Groups[1].Value);
            return 0;
        }

        // 0x10 0x42 [position]：Set_SwStartingPosition — 告诉硬件发令点（左/右）。
        // 参考 C:\2024年11月2日PM SWIM串口通讯程序Server\OnBnClickedSetSwStartingPositionButton：
        //   TCPIP_SendCommand(TCPIP_Con_command=0x10, Set_SwStartingPosition=0x42, StartingPosition, 0, ...)
        //   → 帧 F1 53 [0x10] [0x42] [pos] 00 00 00 00 00 00 F4
        // 这与我们 SendTimingSettingsToHardware 里的 0x42 0x07 (FinishPosition) 是不同的命令体系。
        private void SendStartPositionToHardware() {
            if (_applyingHardwareSettings) return;
            if (_timingBridge == null || !_timingBridge.IsConnected) return;
            try {
                byte pos = _laneCloseSettings.StartPosition == "right" ? (byte)1 : (byte)0;
                _timingBridge.SendCommand(0x10, 0x42, pos);
                _timingBridge.DelayBetweenFrames(20);
                AddLog(string.Format("发令点已下发: {0}", pos == 1 ? "右端" : "左端"));
            } catch (Exception ex) {
                AddLog("发令点下发失败: " + ex.Message);
            }
        }

        // 0x43 Set_MatchEvent: 告诉硬件本场比赛的总圈数 + 左右两侧期望触板次数 + 泳道开关位图。
        // 参考程序（C:\2024年11月2日PM SWIM串口通讯程序Server）每次按"准备就绪"前必发此帧；
        // 否则硬件不知道比赛结构，后续 0x21 / 0x1C 都不会进入 Ready / 开始计时。
        //
        // 帧布局：F1 53 43 [D3=All_Lap] [D4=RightExpected] [D5=LeftExpected] [D6=LaneOpen0_4] [D7=LaneOpen5_9] 0 0 0 F4
        //
        // 泳道开关位图（修正后协议含义，2026-05-08）：
        //   bit i = 1 → 第 i 道【有运动员 / 打开】
        //   bit i = 0 → 第 i 道【空道 / 关闭】
        //   位 0..4 装在 D6，位 5..9 装在 D7（D7 的 bit 0 对应第 5 道，依此类推）
        // 早期实现按"空道=1"反向编码，导致硬件实际开关与软件意图相反，已修正。
        private void SendSetMatchEventToHardware() {
            if (_applyingHardwareSettings) return;
            if (_timingBridge == null || !_timingBridge.IsConnected) return;
            if (string.IsNullOrEmpty(_currentEvent)) return;
            try {
                int totalLaps = GetTotalLaps();
                int leftTotal, rightTotal;
                GetRaceTouchTotals(totalLaps, out leftTotal, out rightTotal);
                // 泳道开关位图：本组有运动员的泳道置 1，空道置 0
                var swimmers = GetCurrentHeatSwimmers();
                var occupiedLanes = new HashSet<int>();
                foreach (var sw in swimmers) {
                    var sa = sw.GetAssignmentForStage(_currentStage);
                    int ln = sa != null ? sa.Lane : sw.Lane;
                    if (ln >= 0 && ln <= 9) occupiedLanes.Add(ln);
                }
                byte laneOpen0_4 = 0, laneOpen5_9 = 0;
                for (int i = 0; i <= 4; i++) {
                    if (occupiedLanes.Contains(i)) laneOpen0_4 |= (byte)(1 << i);
                }
                for (int i = 5; i <= 9; i++) {
                    if (occupiedLanes.Contains(i)) laneOpen5_9 |= (byte)(1 << (i - 5));
                }
                _timingBridge.SendFullFrame(0x43,
                    (byte)Math.Min(255, totalLaps),
                    (byte)Math.Min(255, rightTotal),
                    (byte)Math.Min(255, leftTotal),
                    laneOpen0_4, laneOpen5_9, 0);
                _timingBridge.DelayBetweenFrames(20);     // 给硬件处理本帧的时间，防止下一条命令被吞
                AddLog(string.Format("Set_MatchEvent 已下发: 总圈{0} 右{1} 左{2} 开0-4=0x{3:X2} 开5-9=0x{4:X2}（bit=1 有运动员）",
                    totalLaps, rightTotal, leftTotal, laneOpen0_4, laneOpen5_9));
            } catch (Exception ex) {
                AddLog("Set_MatchEvent 下发失败: " + ex.Message);
            }
        }

        private void SendRaceDistanceToHardware() {
            if (_applyingHardwareSettings) return;
            if (_timingBridge == null || !_timingBridge.IsConnected) return;
            if (string.IsNullOrEmpty(_currentEvent)) return;
            try {
                int totalLaps = GetTotalLaps();
                int distance = GetRaceDistanceMeters();
                int leftTotal, rightTotal;
                GetRaceTouchTotals(totalLaps, out leftTotal, out rightTotal);
                byte distHi = (byte)((distance >> 8) & 0xFF);
                byte distLo = (byte)(distance & 0xFF);
                _timingBridge.SendFullFrame(0x42, RACEDIST_SUBCODE,
                    (byte)Math.Min(255, totalLaps),
                    (byte)Math.Min(255, leftTotal),
                    (byte)Math.Min(255, rightTotal),
                    distHi, distLo);
                AddLog(string.Format("比赛距离已同步到硬件: {0} ({1} 米, {2} 趟, 左{3}次 右{4}次)",
                    _currentEvent, distance, totalLaps, leftTotal, rightTotal));
            } catch (Exception ex) {
                AddLog("比赛距离下发硬件失败: " + ex.Message);
            }
        }

        private static byte EncodeBrokenMask(bool touchpad, bool bw1, bool bw2, bool bw3, bool startBlock) {
            byte v = 0;
            if (touchpad) v |= 0x01;
            if (bw1) v |= 0x02;
            if (bw2) v |= 0x04;
            if (bw3) v |= 0x08;
            if (startBlock) v |= 0x10;
            return v;
        }

        private void SendDeviceStatusesToHardware() {
            if (_applyingHardwareSettings) return;
            if (_timingBridge == null || !_timingBridge.IsConnected) return;
            try {
                foreach (var st in _laneDeviceStates) {
                    byte left = EncodeBrokenMask(st.LeftTouchpadBroken, st.LeftBlindWatch1Broken, st.LeftBlindWatch2Broken, st.LeftBlindWatch3Broken, st.LeftStartBlockBroken);
                    byte right = EncodeBrokenMask(st.RightTouchpadBroken, st.RightBlindWatch1Broken, st.RightBlindWatch2Broken, st.RightBlindWatch3Broken, st.RightStartBlockBroken);
                    _timingBridge.SendFullFrame(0x42, DEVSTAT_SUBCODE, (byte)st.Lane, left, right);
                    _timingBridge.DelayBetweenFrames(15);   // 每泳道帧间 15ms
                }
                AddLog(string.Format("设备状态已同步到硬件: {0}条泳道记录", _laneDeviceStates.Count));
            } catch (Exception ex) {
                AddLog("设备状态下发硬件失败: " + ex.Message);
            }
        }

        private void ApplyDeviceStatusFromHardware(TimingData data) {
            int lane = data.RawD4;
            byte left = data.Param5;
            byte right = data.Param6;
            var st = _laneDeviceStates.FirstOrDefault(x => x.Lane == lane);
            if (st == null) return;
            bool changed = false;
            Action<Func<bool>, Action<bool>, bool> maybeSet = (getter, setter, target) => {
                if (getter() != target) { setter(target); changed = true; }
            };
            maybeSet(() => st.LeftTouchpadBroken,   b => st.LeftTouchpadBroken = b,   (left & 0x01) != 0);
            maybeSet(() => st.LeftBlindWatch1Broken, b => st.LeftBlindWatch1Broken = b, (left & 0x02) != 0);
            maybeSet(() => st.LeftBlindWatch2Broken, b => st.LeftBlindWatch2Broken = b, (left & 0x04) != 0);
            maybeSet(() => st.LeftBlindWatch3Broken, b => st.LeftBlindWatch3Broken = b, (left & 0x08) != 0);
            maybeSet(() => st.LeftStartBlockBroken,  b => st.LeftStartBlockBroken = b, (left & 0x10) != 0);
            maybeSet(() => st.RightTouchpadBroken,   b => st.RightTouchpadBroken = b,   (right & 0x01) != 0);
            maybeSet(() => st.RightBlindWatch1Broken, b => st.RightBlindWatch1Broken = b, (right & 0x02) != 0);
            maybeSet(() => st.RightBlindWatch2Broken, b => st.RightBlindWatch2Broken = b, (right & 0x04) != 0);
            maybeSet(() => st.RightBlindWatch3Broken, b => st.RightBlindWatch3Broken = b, (right & 0x08) != 0);
            maybeSet(() => st.RightStartBlockBroken,  b => st.RightStartBlockBroken = b, (right & 0x10) != 0);
            if (changed) {
                AddLog(string.Format("硬件设备状态回报: 泳道{0} 左=0x{1:X2} 右=0x{2:X2}", lane, left, right));
                AutoSaveData();
                UpdateLaneStatusDisplay();
                Broadcast();
            }
        }

        private void ApplyHardwareSettingFrame(TimingData data) {
            _applyingHardwareSettings = true;
            try {
                bool changed = false;
                switch (data.CommandType) {
                    case TimingCommandType.PoolConfig: {
                        int lanes = data.Param1;
                        int length = data.RawD4;
                        if (lanes >= 6 && lanes <= 10 && _poolConfig.LaneCount != lanes) {
                            _poolConfig.SetLaneCount(lanes);
                            if (LaneCountCombo != null) LaneCountCombo.SelectedIndex = lanes == 8 ? 1 : (lanes == 6 ? 2 : 0);
                            InitLaneDeviceStates();
                            changed = true;
                        }
                        if ((length == 25 || length == 50) && _poolConfig.Length != length) {
                            _poolConfig.Length = length;
                            if (PoolLengthCombo != null) PoolLengthCombo.SelectedIndex = length == 25 ? 1 : 0;
                            changed = true;
                        }
                        if (changed) AddLog(string.Format("硬件参数回报: 泳池 {0}米 {1}道", _poolConfig.Length, _poolConfig.LaneCount));
                        break;
                    }
                    case TimingCommandType.SetCommand: {
                        // 子码 0x10：设备状态帧（D4=泳道 D5=左位图 D6=右位图）
                        if (data.Param1 == DEVSTAT_SUBCODE) {
                            ApplyDeviceStatusFromHardware(data);
                            break;
                        }
                        // 子码 0x20：比赛距离回报（仅日志；比赛项目由主服务器决定，硬件下发不更改项目）
                        if (data.Param1 == RACEDIST_SUBCODE) {
                            int laps = data.RawD4;          // D4
                            int leftN = data.Param5;        // D5
                            int rightN = data.Param6;       // D6
                            int dist = (data.Param7 << 8) | data.Param8;   // D7:D8
                            AddLog(string.Format("硬件比赛距离回报: {0}米 / {1}趟 / 左{2}次 右{3}次",
                                dist, laps, leftN, rightN));
                            break;
                        }
                        double vOld, vNew;
                        switch (data.Param1) {
                            case 0x01: vOld = _laneCloseSettings.LaneCloseTime;           vNew = data.RawD4;           if (Math.Abs(vOld - vNew) > 0.001) { _laneCloseSettings.LaneCloseTime = vNew; changed = true; } break;
                            case 0x02: vOld = _laneCloseSettings.StartBlockCloseDelay;    vNew = data.RawD4 / 10.0;    if (Math.Abs(vOld - vNew) > 0.001) { _laneCloseSettings.StartBlockCloseDelay = vNew; changed = true; } break;
                            case 0x03: vOld = _laneCloseSettings.ResultConfirmCloseDelay; vNew = data.RawD4 / 10.0;    if (Math.Abs(vOld - vNew) > 0.001) { _laneCloseSettings.ResultConfirmCloseDelay = vNew; changed = true; } break;
                            case 0x04: vOld = _laneCloseSettings.FalseStartThreshold;     vNew = data.RawD4 / 100.0;   if (Math.Abs(vOld - vNew) > 0.0001) { _laneCloseSettings.FalseStartThreshold = vNew; changed = true; } break;
                            case 0x05: vOld = _laneCloseSettings.SplitDisplayTime;        vNew = data.RawD4;           if (Math.Abs(vOld - vNew) > 0.001) { _laneCloseSettings.SplitDisplayTime = vNew; changed = true; } break;
                            case 0x06: vOld = _laneCloseSettings.FirstPlaceHoldTime;      vNew = data.RawD4;           if (Math.Abs(vOld - vNew) > 0.001) { _laneCloseSettings.FirstPlaceHoldTime = vNew; changed = true; } break;
                            case 0x07: {
                                string newFin = data.RawD4 == 0 ? "left" : "right";
                                if (_laneCloseSettings.FinishPosition != newFin) {
                                    _laneCloseSettings.FinishPosition = newFin;
                                    _laneCloseSettings.StartPosition = newFin;
                                    AutoAdjustStartPosition();
                                    if (_raceState == RaceState.Waiting || _raceState == RaceState.Ready) {
                                        foreach (var st in _laneDeviceStates) st.ResetForNewRace(_laneCloseSettings.StartPosition);
                                    }
                                    changed = true;
                                }
                                break;
                            }
                            case 0x08: {
                                // 盲表数量（D4 高 4 位=左 1-3，低 4 位=右 1-3）
                                int newLeft = (data.RawD4 >> 4) & 0x0F;
                                int newRight = data.RawD4 & 0x0F;
                                if (newLeft >= 1 && newLeft <= 3 && newRight >= 1 && newRight <= 3) {
                                    if (_laneCloseSettings.LeftBlindWatchCount != newLeft) {
                                        _laneCloseSettings.LeftBlindWatchCount = newLeft; changed = true;
                                    }
                                    if (_laneCloseSettings.RightBlindWatchCount != newRight) {
                                        _laneCloseSettings.RightBlindWatchCount = newRight; changed = true;
                                    }
                                    if (changed) AddLog(string.Format("硬件盲表数量回报：左 {0}，右 {1}", newLeft, newRight));
                                } else {
                                    AddLog(string.Format("硬件盲表数量值非法: 0x{0:X2}", data.RawD4));
                                }
                                break;
                            }
                            default:
                                AddLog(string.Format("硬件参数: 未识别子码 D3=0x{0:X2} D4=0x{1:X2}", data.Param1, data.RawD4));
                                break;
                        }
                        if (changed) AddLog(string.Format("硬件参数回报: D3=0x{0:X2} D4={1}", data.Param1, data.RawD4));
                        break;
                    }
                    case TimingCommandType.RaceConfig:
                        // 0x41 比赛距离+空道位图，当前仅记录日志，由操作员自行确认
                        AddLog(string.Format("硬件比赛参数: 趟数={0} 空道位图 D6=0x{1:X2} D7=0x{2:X2}",
                            data.RawD4, data.Param6, data.Param7));
                        break;
                }

                if (changed) {
                    SaveTimingSettings();
                    AutoSaveData();
                    UpdateLaneStatusDisplay();
                    UpdateRaceStateDisplay();
                    Broadcast();   // 同步到 EXE/Web 三端
                }
            } finally {
                _applyingHardwareSettings = false;
            }
        }

        private void ProcessTimingData(int lane, string cmdType, double timeInSeconds, string side = null) {
            // 硬件 0x7F 滚动时间：硬件计时器是【唯一】权威时间源。
            // 软件不再用本地 DateTime 自算时间，也不按 race state 过滤：收到什么显示什么。
            // 这样硬件清零后只要它发 0x7F=0，软件立刻同步；硬件继续走时则软件也跟着；
            // 唯一保留 0 的场景是从未连接到硬件 / 硬件未发任何 0x7F。
            if (cmdType == "RunningTime") {
                _hwRunningTimeSec = timeInSeconds;
                _hwRunningTimeReceivedAt = DateTime.Now;
                _hwRunningTimeAvailable = true;
                _runningTime = timeInSeconds;
                if (RunningTimeText != null) RunningTimeText.Text = TimeFormatter.FormatRunning(_runningTime);
                // 立刻把硬件时间转发给大屏 / EXE / HTML 计时控制台 — 硬件 100ms 节拍
                BroadcastRunningTime();
                return;
            }
            // 硬件触发的比赛控制命令：直接联动到本地状态机；不做去抖（信任硬件按键发出的是干净的单条命令）
            switch (cmdType) {
                case "TimerReady":
                    AddLog("硬件触发: 就位");
                    Ready_Click(null, null);
                    return;
                case "StartCommand":
                    AddLog("硬件触发: 发令");
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

            // 空泳道或 DNS/DNF：整条泳道关闭，不记录也不处理任何计时数据。
            // 抢跳 DSQ 运动员仍在水中继续比赛，触板/盲表/分段数据应继续接收并保存（仅大屏不显示成绩）。
            if (!IsLaneReceivingData(lane)) {
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
                    // 出发台状态机（与触板对称）：
                    //   Open / FalseStart → 作为正式反应时处理，再切到 Touched（红）保持 StartBlockCloseDelay 秒
                    //   Touched           → 已记录正式反应时；本次记日志并写入"备用反应时"
                    //   其它              → 仅记日志
                    {
                        DeviceStatus sbStatus;
                        string sbSideForClose = side;
                        if (side == "left") sbStatus = laneState.LeftStartBlockStatus;
                        else if (side == "right") sbStatus = laneState.RightStartBlockStatus;
                        else {
                            // side 缺失：先选 Open/FalseStart 一端，否则 Touched 一端，否则左端
                            DeviceStatus lst = laneState.LeftStartBlockStatus;
                            DeviceStatus rst = laneState.RightStartBlockStatus;
                            if (lst == DeviceStatus.Open || lst == DeviceStatus.FalseStart) { sbStatus = lst; sbSideForClose = "left"; }
                            else if (rst == DeviceStatus.Open || rst == DeviceStatus.FalseStart) { sbStatus = rst; sbSideForClose = "right"; }
                            else if (lst == DeviceStatus.Touched) { sbStatus = lst; sbSideForClose = "left"; }
                            else if (rst == DeviceStatus.Touched) { sbStatus = rst; sbSideForClose = "right"; }
                            else { sbStatus = lst; sbSideForClose = "left"; }
                        }

                        bool sbOpen = (sbStatus == DeviceStatus.Open || sbStatus == DeviceStatus.FalseStart);
                        if (!sbOpen && sbStatus == DeviceStatus.Touched) {
                            RecordBackupReaction(lane, timeInSeconds, sbSideForClose);
                            break;
                        }
                        if (sbOpen) {

                        if (!_laneCloseSettings.ReactionTimeEnabled) {
                            // 关闭RT：不进行反应时/抢跳判定，仅记录出发台动作日志
                            AddLog(string.Format("泳道{0} 出发台触发（已关闭反应时检测）", lane));
                        } else if (_isRelay && laneState.CurrentLap > 0) {
                            // 接力交接：出发台时间是绝对时间，与上次触板时间比较
                            // 获取上次触板累计时间
                            var swForLane = GetCurrentHeatSwimmers().FirstOrDefault(s2 => {
                                var sa2 = s2.GetAssignmentForStage(_currentStage);
                                return (sa2 != null ? sa2.Lane : s2.Lane) == lane;
                            });
                            double lastTouchTime = 0;
                            LaneResult relayRes = null;
                            if (swForLane != null) {
                                relayRes = EnsureRelayLaneResult(swForLane, lane);
                                if (relayRes != null && relayRes.Splits.Count > 0) lastTouchTime = relayRes.Splits.Last().CumulativeTime;
                            }
                            double relayReaction = timeInSeconds - lastTouchTime;
                            laneState.ReactionTime = relayReaction;
                            // 按棒次索引覆盖写入 LegReactionTimes[legIdx]，
                            // 这样即使硬件事件重复/乱序，也只保留每棒最新值，且槽位与棒次对齐
                            if (relayRes != null) {
                                int legIdx = ComputeRelayLegIndex(laneState.CurrentLap);
                                EnsureLegReactionSlots(relayRes);
                                if (legIdx >= 0 && legIdx < relayRes.LegReactionTimes.Count)
                                    relayRes.LegReactionTimes[legIdx] = relayReaction;
                            }
                            if (relayReaction < _laneCloseSettings.FalseStartThreshold) {
                                // 仅作可疑提示（反应时标红），是否判罚由裁判手动决定
                                laneState.IsSuspectFalseStart = true;
                                AddLog(string.Format("⚠ 接力起跳可疑（待裁判确认）泳道{0} 出发台:{1:F2}s 触板:{2:F2}s 差值:{3:F3}s", lane, timeInSeconds, lastTouchTime, relayReaction));
                            } else {
                                AddLog(string.Format("泳道{0} 接力交接 出发台:{1:F2}s 差值:{2:F2}s", lane, timeInSeconds, relayReaction));
                            }
                        } else {
                            // 普通出发：反应时间就是出发台时间
                            laneState.ReactionTime = timeInSeconds;
                            // 接力第 1 棒：把出发反应时写入 LegReactionTimes[0]（覆盖式，重复触发只保留最近一次）
                            if (_isRelay) {
                                var swForLane0 = GetCurrentHeatSwimmers().FirstOrDefault(s2 => {
                                    var sa2 = s2.GetAssignmentForStage(_currentStage);
                                    return (sa2 != null ? sa2.Lane : s2.Lane) == lane;
                                });
                                if (swForLane0 != null) {
                                    var res0 = EnsureRelayLaneResult(swForLane0, lane);
                                    if (res0 != null) {
                                        EnsureLegReactionSlots(res0);
                                        if (res0.LegReactionTimes.Count > 0) res0.LegReactionTimes[0] = timeInSeconds;
                                    }
                                }
                            }
                            if (timeInSeconds <= _laneCloseSettings.FalseStartThreshold) {
                                // 仅作可疑提示（反应时标红），是否判罚由裁判手动决定
                                laneState.IsSuspectFalseStart = true;
                                AddLog(string.Format("⚠ 起跳可疑（待裁判确认）泳道{0} 反应时: {1:F3}s", lane, timeInSeconds));
                            } else {
                                AddLog(string.Format("泳道{0} 反应时间: {1:F2}s", lane, timeInSeconds));
                            }
                        }
                        // 正式反应时已记录，把该端出发台切到"已触板（红）"，StartBlockCloseDelay 秒后转 Closed
                        EnterStartBlockTouchedThenClose(laneState, sbSideForClose, lane);
                    }
                    }
                    break;

                case "Touchpad":
                    // 触板状态机：
                    //   Open       → 作为正式成绩送进 ProcessTouchpadHit，再把该端切到 Touched（红）保持 ResultConfirmCloseDelay 秒
                    //   Touched    → 已有正式成绩；本次记日志并写入"备用成绩"（争议时使用）
                    //   其它（Closed/Broken/NotInstalled）→ 仅记日志，不作为成绩
                    {
                        DeviceStatus tpStatus;
                        string sideForClose = side;
                        if (side == "left") tpStatus = laneState.LeftTouchpadStatus;
                        else if (side == "right") tpStatus = laneState.RightTouchpadStatus;
                        else {
                            // 兼容旧路径（side 缺失）：优先选 Open 的一端，否则左端
                            if (laneState.LeftTouchpadStatus == DeviceStatus.Open) { tpStatus = DeviceStatus.Open; sideForClose = "left"; }
                            else if (laneState.RightTouchpadStatus == DeviceStatus.Open) { tpStatus = DeviceStatus.Open; sideForClose = "right"; }
                            else if (laneState.LeftTouchpadStatus == DeviceStatus.Touched) { tpStatus = DeviceStatus.Touched; sideForClose = "left"; }
                            else if (laneState.RightTouchpadStatus == DeviceStatus.Touched) { tpStatus = DeviceStatus.Touched; sideForClose = "right"; }
                            else { tpStatus = laneState.LeftTouchpadStatus; sideForClose = "left"; }
                        }

                        if (tpStatus == DeviceStatus.Open) {
                            ProcessTouchpadHit(lane, timeInSeconds, laneState);
                            // 已记录正式成绩，把该端切到"已触板（红）"，到点再 Closed
                            EnterTouchedThenClose(laneState, sideForClose, lane);
                        } else if (tpStatus == DeviceStatus.Touched) {
                            // 备用成绩：写入运动员当前组的成绩记录 + 日志
                            RecordBackupTouch(lane, timeInSeconds);
                        } else {
                            AddLog(string.Format("泳道{0} 触板数据已记录但不作为成绩（{1}状态:{2}）",
                                lane, side ?? "", tpStatus));
                        }
                    }
                    break;

                case "PushButton1":
                case "PushButton2":
                case "PushButton3":
                    // 盲表状态机（按【单个盲表】粒度，每个盲表独立计时）：
                    //   Open    → 作为正式分段计时数据送 ProcessBlindWatchData，再切到 Touched（红）保持 ResultConfirmCloseDelay 秒
                    //   Touched → 已记录正式成绩；本次记日志并写入"备用盲表成绩"
                    //   其它    → 仅记日志
                    {
                        int blindNum = (cmdType == "PushButton1") ? 1 : (cmdType == "PushButton2" ? 2 : 3);
                        DeviceStatus bwStatus;
                        string bwSideForClose = side;
                        if (side == "left") bwStatus = GetBlindStatusByNum(laneState, true, blindNum);
                        else if (side == "right") bwStatus = GetBlindStatusByNum(laneState, false, blindNum);
                        else {
                            DeviceStatus lst = GetBlindStatusByNum(laneState, true, blindNum);
                            DeviceStatus rst = GetBlindStatusByNum(laneState, false, blindNum);
                            if (lst == DeviceStatus.Open) { bwStatus = lst; bwSideForClose = "left"; }
                            else if (rst == DeviceStatus.Open) { bwStatus = rst; bwSideForClose = "right"; }
                            else if (lst == DeviceStatus.Touched) { bwStatus = lst; bwSideForClose = "left"; }
                            else if (rst == DeviceStatus.Touched) { bwStatus = rst; bwSideForClose = "right"; }
                            else { bwStatus = lst; bwSideForClose = "left"; }
                        }

                        if (bwStatus == DeviceStatus.Open) {
                            ProcessBlindWatchData(lane, cmdType, timeInSeconds);
                            EnterBlindTouchedThenClose(laneState, bwSideForClose, blindNum, lane);
                        } else if (bwStatus == DeviceStatus.Touched) {
                            RecordBackupBlind(lane, blindNum, timeInSeconds);
                        } else {
                            AddLog(string.Format("泳道{0} 盲表{1} 数据已记录但不作为成绩（{2}状态:{3}）",
                                lane, blindNum, side ?? "", bwStatus));
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

                // 文件名：[组别_]性别_项目_赛次_第N组.txt
                string agName = string.IsNullOrEmpty(_currentAgeGroup) ? "" : (_currentAgeGroup + "_");
                string safeName = string.Format("{0}{1}_{2}_{3}_第{4}组",
                    agName, _currentGender, _currentEvent, _currentStage, _currentHeat)
                    .Replace("×", "x").Replace("/", "_");
                string path = IOPath.Combine(dir, safeName + ".txt");

                // 写入文件头 + 数据
                var sb = new StringBuilder();
                sb.AppendFormat("═══ 原始计时数据 ═══\r\n");
                sb.AppendFormat("赛事: {0}\r\n", _competitionName);
                sb.AppendFormat("项目: {0}{1} {2}\r\n",
                    string.IsNullOrEmpty(_currentAgeGroup) ? "" : ("[" + _currentAgeGroup + "] "),
                    _currentGender, _currentEvent);
                sb.AppendFormat("赛次: {0}  第{1}组\r\n", _currentStage, _currentHeat);
                sb.AppendFormat("比赛时间: {0}\r\n", _raceStartTime > DateTime.MinValue ? _raceStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "未开始");
                sb.AppendFormat("终点位置: {0}端  发令位置: {1}端\r\n",
                    _laneCloseSettings.FinishPosition == "left" ? "左" : "右",
                    _laneCloseSettings.StartPosition == "left" ? "左" : "右");

                // 运动员名单 — 接力赛 s.Name 实为队名，4 位队员姓名存于 s.Notes（"接力队 棒次:张三,李四,..."）
                sb.AppendFormat("\r\n--- 出场名单 ---\r\n");
                foreach (var s in GetCurrentHeatSwimmers()) {
                    var sa = s.GetAssignmentForStage(_currentStage);
                    int sLane = sa != null ? sa.Lane : s.Lane;
                    string athletes = s.Name ?? "";
                    if (_isRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                        athletes = s.Notes.Substring("接力队 棒次:".Length);
                    sb.AppendFormat("道{0}\t代表队:{1}\t运动员:{2}\t报名:{3}\r\n", sLane, s.Country, athletes, s.EntryTime);
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
                    // 接力赛反应时按棒展开"第1棒:0.45 / 第2棒:0.30 ..."；个人赛仍是 StartingBlockTime 单值
                    string reaction = "";
                    if (_isRelay && r != null && r.LegReactionTimes != null && r.LegReactionTimes.Count > 0) {
                        var rtParts = new List<string>();
                        for (int li = 0; li < r.LegReactionTimes.Count; li++) {
                            double rt = r.LegReactionTimes[li];
                            rtParts.Add(string.Format("第{0}棒:{1}", li + 1, rt > 0 ? rt.ToString("F3") : "—"));
                        }
                        reaction = string.Join(" / ", rtParts.ToArray());
                    } else if (r != null && r.StartingBlockTime > 0) {
                        reaction = r.StartingBlockTime.ToString("F3");
                    }
                    string athletesFr = s.Name ?? "";
                    if (_isRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                        athletesFr = s.Notes.Substring("接力队 棒次:".Length);
                    sb.AppendFormat("名次:{0}\t道{1}\t代表队:{2}\t运动员:{3}\t成绩:{4}\t计时源:{5}\t反应:{6}\t{7}\r\n",
                        s.CurrentRank > 0 ? s.CurrentRank.ToString() : "-", sLane, s.Country, athletesFr,
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

                    // 备用成绩 — Touched（已触板红色）窗口期内捕获的额外计时事件，争议裁定时使用
                    if (r != null && (
                        (r.BackupTouchTimes != null && r.BackupTouchTimes.Count > 0) ||
                        (r.BackupBlindTimes != null && r.BackupBlindTimes.Count > 0) ||
                        (r.BackupReactionTimes != null && r.BackupReactionTimes.Count > 0))) {
                        sb.Append("  ★备用成绩(争议时使用):");
                        if (r.BackupTouchTimes != null && r.BackupTouchTimes.Count > 0) {
                            var touchStrs = new List<string>();
                            foreach (double t in r.BackupTouchTimes) touchStrs.Add(TimeFormatter.Format(t));
                            sb.AppendFormat("  触板[{0}]", string.Join(", ", touchStrs.ToArray()));
                        }
                        if (r.BackupBlindTimes != null && r.BackupBlindTimes.Count > 0) {
                            sb.AppendFormat("  盲表[{0}]", string.Join(", ", r.BackupBlindTimes.ToArray()));
                        }
                        if (r.BackupReactionTimes != null && r.BackupReactionTimes.Count > 0) {
                            sb.AppendFormat("  出发台[{0}]", string.Join(", ", r.BackupReactionTimes.ToArray()));
                        }
                        sb.Append("\r\n");
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
            h.AppendFormat("<h2>{0}{1} {2}　{3}　第{4}组　—　原始计时数据</h2>",
                string.IsNullOrEmpty(_currentAgeGroup) ? "" : ("[" + _currentAgeGroup + "] "),
                _currentGender, _currentEvent, _currentStage, _currentHeat);

            // 赛事信息
            h.Append("<table class='info'>");
            h.AppendFormat("<tr><td class='label'>比赛时间:</td><td>{0}</td><td class='label'>终点位置:</td><td>{1}端</td></tr>",
                _raceStartTime > DateTime.MinValue ? _raceStartTime.ToString("yyyy-MM-dd HH:mm:ss") : "未开始",
                _laneCloseSettings.FinishPosition == "left" ? "左" : "右");
            h.AppendFormat("<tr><td class='label'>发令位置:</td><td>{0}端</td><td class='label'>泳道数:</td><td>{1}道</td></tr>",
                _laneCloseSettings.StartPosition == "left" ? "左" : "右", _poolConfig.LaneCount);
            h.Append("</table>");

            // 出场名单 — 接力赛 s.Name 是队名，队员姓名在 s.Notes("接力队 棒次:...") 里
            h.Append("<div class='section'>出场名单</div>");
            h.Append("<table class='data'><tr><th>道次</th><th>代表队</th><th>运动员姓名</th><th>报名成绩</th></tr>");
            foreach (var s in GetCurrentHeatSwimmers()) {
                var sa = s.GetAssignmentForStage(_currentStage);
                int sLane = sa != null ? sa.Lane : s.Lane;
                string athletesH = s.Name ?? "";
                if (_isRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                    athletesH = s.Notes.Substring("接力队 棒次:".Length);
                h.AppendFormat("<tr><td style='text-align:center;'>{0}</td><td>{1}</td><td><b>{2}</b></td><td class='mono' style='text-align:center;'>{3}</td></tr>",
                    sLane, s.Country, athletesH, s.EntryTime);
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

            // 最终成绩 — 接力赛分别给"代表队"和"运动员姓名"列；反应时按棒展开
            h.Append("<div class='section'>最终成绩</div>");
            h.Append("<table class='data'><tr><th>名次</th><th>道</th><th>代表队</th><th>运动员姓名</th><th>成绩</th><th>计时源</th><th>反应时间</th><th>触板</th><th>盲表1</th><th>盲表2</th><th>盲表3</th><th>状态</th></tr>");
            foreach (var s in GetCurrentHeatSwimmers().OrderBy(s2 => s2.CurrentRank > 0 ? s2.CurrentRank : int.MaxValue)) {
                var r = s.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                var sa = s.GetAssignmentForStage(_currentStage);
                int sLane = sa != null ? sa.Lane : s.Lane;
                string status = s.Status ?? "";
                bool isDQ = status == "DSQ" || status == "DNS" || status == "DNF";
                string finalTime = !isDQ && r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "";
                string athletesFh = s.Name ?? "";
                if (_isRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                    athletesFh = s.Notes.Substring("接力队 棒次:".Length);
                // 反应时单元：接力按棒展开（<br> 分行），个人为单值
                string reactionHtml = "";
                if (_isRelay && r != null && r.LegReactionTimes != null && r.LegReactionTimes.Count > 0) {
                    var rtParts2 = new List<string>();
                    for (int li = 0; li < r.LegReactionTimes.Count; li++) {
                        double rt2 = r.LegReactionTimes[li];
                        rtParts2.Add(string.Format("第{0}棒:{1}", li + 1, rt2 > 0 ? rt2.ToString("F3") : "—"));
                    }
                    reactionHtml = string.Join("<br>", rtParts2.ToArray());
                } else if (r != null && r.StartingBlockTime > 0) {
                    reactionHtml = r.StartingBlockTime.ToString("F3");
                }
                h.AppendFormat("<tr><td style='text-align:center;font-weight:bold;'>{0}</td><td style='text-align:center;'>{1}</td><td>{2}</td><td><b>{3}</b></td>" +
                    "<td class='mono' style='text-align:center;font-weight:bold;'>{4}</td><td style='text-align:center;'>{5}</td><td class='mono' style='text-align:center;font-size:10px;'>{6}</td>" +
                    "<td class='mono' style='text-align:center;'>{7}</td><td class='mono' style='text-align:center;'>{8}</td><td class='mono' style='text-align:center;'>{9}</td><td class='mono' style='text-align:center;'>{10}</td>" +
                    "<td style='text-align:center;color:#dc2626;font-weight:bold;'>{11}</td></tr>",
                    s.CurrentRank > 0 ? s.CurrentRank.ToString() : (isDQ ? "-" : ""),
                    sLane, s.Country, athletesFh, isDQ ? status : finalTime,
                    r != null ? (r.TimingSource ?? "") : "",
                    reactionHtml,
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

                // 备用成绩（争议时使用）— Touched 窗口期内捕获的额外触板/盲表/出发台事件
                if (r != null && (
                    (r.BackupTouchTimes != null && r.BackupTouchTimes.Count > 0) ||
                    (r.BackupBlindTimes != null && r.BackupBlindTimes.Count > 0) ||
                    (r.BackupReactionTimes != null && r.BackupReactionTimes.Count > 0))) {
                    h.Append("<tr><td colspan='12' style='padding-left:30px;font-size:10px;color:#dc2626;background:#fef2f2;'>");
                    h.Append("<b>★备用成绩(争议时使用):</b>");
                    if (r.BackupTouchTimes != null && r.BackupTouchTimes.Count > 0) {
                        var touchStrs = new List<string>();
                        foreach (double t in r.BackupTouchTimes) touchStrs.Add(TimeFormatter.Format(t));
                        h.AppendFormat(" 触板[<span class='mono'>{0}</span>]", string.Join(", ", touchStrs.ToArray()));
                    }
                    if (r.BackupBlindTimes != null && r.BackupBlindTimes.Count > 0) {
                        h.AppendFormat(" 盲表[<span class='mono'>{0}</span>]", string.Join(", ", r.BackupBlindTimes.ToArray()));
                    }
                    if (r.BackupReactionTimes != null && r.BackupReactionTimes.Count > 0) {
                        h.AppendFormat(" 出发台[<span class='mono'>{0}</span>]", string.Join(", ", r.BackupReactionTimes.ToArray()));
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

        // 把触板从 Open → Touched（红色），ResultConfirmCloseDelay 秒后再 → Closed。
        // 期间硬件上来的额外触板事件会被 RecordBackupTouch 写为"备用成绩"。
        private void EnterTouchedThenClose(LaneDeviceState laneState, string side, int lane) {
            if (laneState == null) return;
            if (side == "right") {
                if (laneState.RightTouchpadBroken) return;
                laneState.RightTouchpadStatus = DeviceStatus.Touched;
            } else {
                if (laneState.LeftTouchpadBroken) return;
                laneState.LeftTouchpadStatus = DeviceStatus.Touched;
            }

            double holdSec = _laneCloseSettings.ResultConfirmCloseDelay > 0 ? _laneCloseSettings.ResultConfirmCloseDelay : 3;
            int capLane = lane;
            string capSide = side;
            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(holdSec) };
            closeTimer.Tick += delegate {
                closeTimer.Stop();
                var ls = _laneDeviceStates.FirstOrDefault(s => s.Lane == capLane);
                if (ls == null) return;
                if (capSide == "right") {
                    if (ls.RightTouchpadStatus == DeviceStatus.Touched) ls.RightTouchpadStatus = DeviceStatus.Closed;
                } else {
                    if (ls.LeftTouchpadStatus == DeviceStatus.Touched) ls.LeftTouchpadStatus = DeviceStatus.Closed;
                }
                Broadcast();
            };
            closeTimer.Start();
        }

        // 出发台 Open/FalseStart → Touched（红色）→ StartBlockCloseDelay 秒后 → Closed。
        // 期间额外的出发台事件作为"备用反应时"记录（争议时使用）。
        private void EnterStartBlockTouchedThenClose(LaneDeviceState laneState, string side, int lane) {
            if (laneState == null) return;
            if (side == "right") {
                if (laneState.RightStartBlockBroken) return;
                laneState.RightStartBlockStatus = DeviceStatus.Touched;
            } else {
                if (laneState.LeftStartBlockBroken) return;
                laneState.LeftStartBlockStatus = DeviceStatus.Touched;
            }

            double holdSec = _laneCloseSettings.StartBlockCloseDelay > 0 ? _laneCloseSettings.StartBlockCloseDelay : 3;
            int capLane = lane;
            string capSide = side;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(holdSec) };
            t.Tick += delegate {
                t.Stop();
                var ls = _laneDeviceStates.FirstOrDefault(s => s.Lane == capLane);
                if (ls == null) return;
                if (capSide == "right") {
                    if (ls.RightStartBlockStatus == DeviceStatus.Touched) ls.RightStartBlockStatus = DeviceStatus.Closed;
                } else {
                    if (ls.LeftStartBlockStatus == DeviceStatus.Touched) ls.LeftStartBlockStatus = DeviceStatus.Closed;
                }
                Broadcast();
            };
            t.Start();
        }

        // 盲表 Open → Touched（红色）→ ResultConfirmCloseDelay 秒后 → Closed。
        // 每个盲表（左/右 × 1/2/3）独立计时，不影响其它盲表。
        private void EnterBlindTouchedThenClose(LaneDeviceState laneState, string side, int blindNum, int lane) {
            if (laneState == null) return;
            bool isLeft = side != "right";
            if (isLeft) {
                if (blindNum == 1) { if (laneState.LeftBlindWatch1Broken || laneState.LeftBlindWatch1NotInstalled) return; laneState.LeftBlindWatch1Status = DeviceStatus.Touched; }
                else if (blindNum == 2) { if (laneState.LeftBlindWatch2Broken || laneState.LeftBlindWatch2NotInstalled) return; laneState.LeftBlindWatch2Status = DeviceStatus.Touched; }
                else if (blindNum == 3) { if (laneState.LeftBlindWatch3Broken || laneState.LeftBlindWatch3NotInstalled) return; laneState.LeftBlindWatch3Status = DeviceStatus.Touched; }
                else return;
            } else {
                if (blindNum == 1) { if (laneState.RightBlindWatch1Broken || laneState.RightBlindWatch1NotInstalled) return; laneState.RightBlindWatch1Status = DeviceStatus.Touched; }
                else if (blindNum == 2) { if (laneState.RightBlindWatch2Broken || laneState.RightBlindWatch2NotInstalled) return; laneState.RightBlindWatch2Status = DeviceStatus.Touched; }
                else if (blindNum == 3) { if (laneState.RightBlindWatch3Broken || laneState.RightBlindWatch3NotInstalled) return; laneState.RightBlindWatch3Status = DeviceStatus.Touched; }
                else return;
            }

            double holdSec = _laneCloseSettings.ResultConfirmCloseDelay > 0 ? _laneCloseSettings.ResultConfirmCloseDelay : 3;
            int capLane = lane;
            bool capLeft = isLeft;
            int capNum = blindNum;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(holdSec) };
            t.Tick += delegate {
                t.Stop();
                var ls = _laneDeviceStates.FirstOrDefault(s => s.Lane == capLane);
                if (ls == null) return;
                if (capLeft) {
                    if (capNum == 1 && ls.LeftBlindWatch1Status == DeviceStatus.Touched) ls.LeftBlindWatch1Status = DeviceStatus.Closed;
                    else if (capNum == 2 && ls.LeftBlindWatch2Status == DeviceStatus.Touched) ls.LeftBlindWatch2Status = DeviceStatus.Closed;
                    else if (capNum == 3 && ls.LeftBlindWatch3Status == DeviceStatus.Touched) ls.LeftBlindWatch3Status = DeviceStatus.Closed;
                } else {
                    if (capNum == 1 && ls.RightBlindWatch1Status == DeviceStatus.Touched) ls.RightBlindWatch1Status = DeviceStatus.Closed;
                    else if (capNum == 2 && ls.RightBlindWatch2Status == DeviceStatus.Touched) ls.RightBlindWatch2Status = DeviceStatus.Closed;
                    else if (capNum == 3 && ls.RightBlindWatch3Status == DeviceStatus.Touched) ls.RightBlindWatch3Status = DeviceStatus.Closed;
                }
                Broadcast();
            };
            t.Start();
        }

        // 读取指定盲表当前状态（getter 本身屏蔽 Broken / NotInstalled）。
        private static DeviceStatus GetBlindStatusByNum(LaneDeviceState ls, bool isLeft, int blindNum) {
            if (isLeft) {
                if (blindNum == 1) return ls.LeftBlindWatch1Status;
                if (blindNum == 2) return ls.LeftBlindWatch2Status;
                return ls.LeftBlindWatch3Status;
            } else {
                if (blindNum == 1) return ls.RightBlindWatch1Status;
                if (blindNum == 2) return ls.RightBlindWatch2Status;
                return ls.RightBlindWatch3Status;
            }
        }

        // 出发台已 Touched 期间又来事件 → 备用反应时（写日志 + 持久到 LaneResult.BackupReactionTimes）
        private void RecordBackupReaction(int lane, double time, string side) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer != null) {
                var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                if (result != null) result.BackupReactionTimes.Add((side ?? "?") + ":" + time.ToString("F3"));
            }
            AddLog(string.Format("泳道{0} 出发台触发【备用反应时】{1}={2:F3}s（争议时使用，已记录但不作为正式反应时）",
                lane, side ?? "?", time));
        }

        // 盲表已 Touched 期间又来事件 → 备用盲表（写日志 + 持久到 LaneResult.BackupBlindTimes）
        private void RecordBackupBlind(int lane, int blindNum, double time) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer != null) {
                var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                if (result != null) result.BackupBlindTimes.Add(blindNum + ":" + time.ToString("F3"));
            }
            AddLog(string.Format("泳道{0} 盲表{1}【备用成绩】{2:F3}s（争议时使用，已记录但不作为正式分段）",
                lane, blindNum, time));
        }

        // 触板"已触板（红）"窗口期内的额外触板：写日志 + 保存到 LaneResult.BackupTouchTimes（争议成绩）。
        private void RecordBackupTouch(int lane, double time) {
            var swimmer = GetCurrentHeatSwimmers().FirstOrDefault(s => {
                var sa = s.GetAssignmentForStage(_currentStage);
                return (sa != null ? sa.Lane : s.Lane) == lane;
            });
            if (swimmer == null) {
                AddLog(string.Format("泳道{0} 备用成绩 {1}（找不到运动员，仅日志）", lane, TimeFormatter.Format(time)));
                return;
            }
            var result = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (result == null) {
                AddLog(string.Format("泳道{0} 备用成绩 {1}（无成绩记录，仅日志）", lane, TimeFormatter.Format(time)));
                return;
            }
            result.BackupTouchTimes.Add(time);
            AddLog(string.Format("泳道{0} {1} 触板【备用成绩】{2}（争议时使用，正式成绩 {3}）",
                lane, swimmer.Name ?? "", TimeFormatter.Format(time),
                result.FinalTime > 0 ? TimeFormatter.Format(result.FinalTime) : "—"));
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
                // 保存反应时间到成绩记录（关闭RT时不写入）
                if (_laneCloseSettings.ReactionTimeEnabled && laneState.ReactionTime > 0) {
                    result.StartingBlockTime = laneState.ReactionTime;
                }

                // 计时源裁定（使用最终段的各计时源数据）
                var judgement = TimingBridge.JudgeTimingSource(
                    split.TouchpadTime, split.PushButton1Time, split.PushButton2Time, split.PushButton3Time,
                    split.ManualTouchTime);
                result.FinalTime = judgement.FinalTime;
                result.TimingSource = judgement.Source;
                split.TimingSource = judgement.Source;

                // 终点圈：先把两端都置 Closed（清场），后面 EnterTouchedThenClose 会把
                // 真正"触板那一端"覆盖为 Touched（红）→ ResultConfirmCloseDelay 秒后再回到 Closed。
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
                // 注意：仅在 ConfirmResult_Click 才落盘，让裁判有机会修改/取消成绩

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

                // 触板：由调用方 EnterTouchedThenClose 把到达端切到"已触板（红）"，
                // 到点 ResultConfirmCloseDelay 后再转 Closed。这里只处理"该端的盲表/手动按钮"延迟关闭。
                string arrivedEnd = laneState.Direction == "→" ? "left" : "right";
                var closeTimer = new DispatcherTimer();
                closeTimer.Interval = TimeSpan.FromSeconds(_laneCloseSettings.ResultConfirmCloseDelay);
                closeTimer.Tick += delegate(object s2, EventArgs a2) {
                    closeTimer.Stop();
                    if (laneState.IsFinished) return;
                    if (arrivedEnd == "right") {
                        laneState.RightBlindWatch1Status = DeviceStatus.Closed; laneState.RightBlindWatch2Status = DeviceStatus.Closed; laneState.RightBlindWatch3Status = DeviceStatus.Closed;
                        laneState.RightManualStatus = DeviceStatus.Closed;
                    } else {
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
        // 圈数显示上限：按比赛项目实际设定的"该侧触板次数"。
        // 50m(1 段)：出发端 0 次，到达端 1 次；100m(2 段)：双侧各 1 次；
        // 150m(3 段)：出发端 1、到达端 2；200m(4 段)：双侧各 2 次；以此类推。
        private int GetLapDisplayMaxForSide(bool isLeft) {
            int totalLaps = GetTotalLaps();
            if (totalLaps <= 0) return int.MaxValue;
            int startSideTotal, farSideTotal;
            if (totalLaps == 1) { startSideTotal = 0; farSideTotal = 1; }
            else { startSideTotal = totalLaps / 2; farSideTotal = (totalLaps + 1) / 2; }
            bool startFromLeft = _laneCloseSettings.StartPosition != "right";
            if (startFromLeft) return isLeft ? startSideTotal : farSideTotal;
            return isLeft ? farSideTotal : startSideTotal;
        }

        // 手动调整某道圈数显示：左/右独立，钳到 [0, 该侧触板总次数]。
        // 仅修改对应侧的人工偏移量；不直接动 CurrentLap，所以左侧按键只改左侧显示，右侧按键只改右侧显示。
        private void AdjustLapDisplay(int lane, bool isLeft, int delta) {
            var ls = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
            if (ls == null) return;
            int maxDisp = GetLapDisplayMaxForSide(isLeft);
            int curDisp = GetDisplayedLapCount(ls, isLeft);
            int newDisp = curDisp + delta;
            if (newDisp < 0) newDisp = 0;
            if (newDisp > maxDisp) newDisp = maxDisp;
            if (newDisp == curDisp) return; // 已经在边界
            int baseVal = GetTouchRemain(ls, isLeft);
            int newAdj = newDisp - baseVal;
            if (isLeft) ls.LeftLapManualAdjust = newAdj;
            else        ls.RightLapManualAdjust = newAdj;
            AddLog(string.Format("泳道{0} 手动调整{1}侧圈数显示: {2}→{3}", lane, isLeft ? "左" : "右", curDisp, newDisp));
            UpdateLaneStatusDisplay();
            Broadcast();
        }

        // 含 spinner 偏移的"显示用当前圈数"：
        // = 总圈数 − 左剩余显示 − 右剩余显示
        // 用于接力赛"第N棒"的实时切换：用户 ▲ 加圈 → 剩余降低 → 当前圈数升高 → 棒次自动前进。
        private int GetDisplayedCurrentLap(LaneDeviceState ls) {
            if (ls == null) return 0;
            int totalLaps = GetTotalLaps();
            int leftRemain = GetDisplayedLapCount(ls, true);
            int rightRemain = GetDisplayedLapCount(ls, false);
            int v = totalLaps - leftRemain - rightRemain;
            if (v < 0) v = 0;
            if (v > totalLaps) v = totalLaps;
            return v;
        }

        // 显示用的"该侧剩余圈数" = GetTouchRemain(自动推算) + 人工 spinner 偏移；钳到 [0, 该侧触板总次数]。
        private int GetDisplayedLapCount(LaneDeviceState ls, bool isLeft) {
            if (ls == null) return 0;
            int baseVal = GetTouchRemain(ls, isLeft);
            int adj = isLeft ? ls.LeftLapManualAdjust : ls.RightLapManualAdjust;
            int v = baseVal + adj;
            int maxDisp = GetLapDisplayMaxForSide(isLeft);
            if (v < 0) v = 0;
            if (v > maxDisp) v = maxDisp;
            return v;
        }

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

            // 并列名次（competition ranking 1-2-2-4）：同成绩并列，后续名次跳过
            int idx = 0, rank = 1;
            double prevTime = -1;
            foreach (var sw in withResults) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                idx++;
                // 浮点容差：百分秒级精度，5ms 内视为并列；直接 == 会因不同计时源裁定的微小差异导致漏并列
                if (idx == 1 || (r != null && !IsTieTime(r.FinalTime, prevTime))) rank = idx;
                if (r != null) { r.Rank = rank; prevTime = r.FinalTime; }
                sw.CurrentRank = rank;
            }
            // 名次重算后同步处理"只有本组第 1 名保留破/平纪录标识"，
            // 覆盖 DSQ 取消/晋级把更慢成绩拽到第 1 等场景
            EnforceOnlyLeaderRecordNote();
        }

        // 纪录类型 → 简写标识（用于备注栏/打印/大屏 BREAK/TIE 提示）
        // 破纪录用 "WR"，平纪录用 "=WR"
        private static string RecordTypeToTag(string type) {
            if (string.IsNullOrEmpty(type)) return "REC";
            string t = type;
            if (t.Contains("世界")) return "WR";
            if (t.Contains("奥运")) return "OR";
            if (t.Contains("亚洲青年")) return "AJR";
            if (t.Contains("亚洲")) return "AR";
            if (t.Contains("全国")) return "NR";
            if (t.Contains("省")) return "省R";
            if (t.Contains("市")) return "市R";
            if (t.Contains("赛会") || t.Contains("大会")) return "CR";
            // 兜底：取首字母
            return t.Length <= 4 ? t : t.Substring(0, 4);
        }

        // 比对成绩与所有匹配该项目+性别(+组别)的记录条目，写 result.RecordNote 并记日志。
        // 返回值：是否写入了任何破/平纪录标识（用于触发广播/确认时的通知）。
        private bool CheckRecords(Swimmer swimmer, LaneResult result) {
            if (result == null || swimmer == null || result.FinalTime <= 0) {
                if (result != null) result.RecordNote = "";
                return false;
            }
            string ageGroup = swimmer.AgeCategory ?? "";
            var tags = new List<string>();
            foreach (var record in _records) {
                if (record == null || record.Time <= 0) continue;                      // 记录库无数据则不比较
                if (record.Gender != _currentGender) continue;
                if (record.EventName != _currentEvent) continue;
                // 组别：记录条目 AgeGroup 为空表示不限组别；否则需匹配
                string rg = record.AgeGroup ?? "";
                if (!string.IsNullOrEmpty(rg) && rg != ageGroup) continue;

                string tag = RecordTypeToTag(record.RecordType);
                if (result.FinalTime < record.Time) {
                    tags.Add(tag);
                    AddLog(string.Format("★破{0}! {1} {2} < {3}（原 {4}/{5}）",
                        record.RecordType, swimmer.Name,
                        TimeFormatter.Format(result.FinalTime), TimeFormatter.Format(record.Time),
                        record.HolderName ?? "", record.HolderCountry ?? ""));
                } else if (Math.Abs(result.FinalTime - record.Time) < 0.005) {
                    tags.Add("=" + tag);
                    AddLog(string.Format("★平{0}! {1} {2} = {3}",
                        record.RecordType, swimmer.Name,
                        TimeFormatter.Format(result.FinalTime), TimeFormatter.Format(record.Time)));
                }
            }
            // 去重并按 WR > OR > AR > AJR > NR > 省R > 市R > CR 的顺序排序展示
            var order = new Dictionary<string, int> { {"WR",1},{"OR",2},{"AR",3},{"AJR",4},{"NR",5},{"省R",6},{"市R",7},{"CR",8} };
            tags = tags.Distinct().OrderBy(s => {
                string key = s.StartsWith("=") ? s.Substring(1) : s;
                int v;
                return order.TryGetValue(key, out v) ? v : 99;
            }).ToList();
            string note = string.Join("/", tags);
            result.RecordNote = note;
            // 一组多人同时打破纪录时，仅保留本组成绩最佳者（含并列）的标识；其余清除。
            // FINA 惯例：纪录归本组最快者所有；慢于他的同组选手即便也好于历史纪录，也不计破纪录。
            EnforceOnlyLeaderRecordNote();
            return tags.Count > 0;
        }

        // 判断该项目从 fromStage 出发的下一赛次（"预赛" → "半决赛"或"决赛"，"半决赛" → "决赛"）
        // 用于打印预赛/半决赛成绩单时给晋级者标 Q
        private string GetNextStageFor(string gender, string eventName, string fromStage) {
            if (fromStage == "预赛") {
                if (_schedule.Any(s => s.Gender == gender && s.EventName == eventName && s.Stage == "半决赛"))
                    return "半决赛";
                return "决赛";
            }
            if (fromStage == "半决赛") return "决赛";
            return null;     // 决赛及其它无下一赛次
        }

        // 该运动员是否已晋级到下一赛次（StageAssignments 含下一赛次 → 已被分组）
        private bool IsQualifiedToNext(Swimmer sw, string fromStage) {
            if (sw == null) return false;
            string next = GetNextStageFor(sw.Gender, sw.EventName, fromStage);
            if (string.IsNullOrEmpty(next)) return false;
            return sw.GetAssignmentForStage(next) != null;
        }

        // 备注栏渲染：判罚（DSQ/DNS/DNF/DQ）红色，晋级 Q 绿色，其它空白。
        // 集中在一处，让所有打印路径（本组成绩单 / 赛事级公告 等）共用样式。
        private string RenderRemarkCellHtml(Swimmer sw, LaneResult r, string fromStage) {
            string status = "";
            if (r != null && !string.IsNullOrEmpty(r.Status)) status = r.Status;
            else if (sw != null && !string.IsNullOrEmpty(sw.Status) &&
                     (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ" || sw.Status == "DQ"))
                status = sw.Status;
            if (!string.IsNullOrEmpty(status))
                return string.Format("<span style='color:#dc2626;'>{0}</span>", status);
            if (IsQualifiedToNext(sw, fromStage))
                return "<span style='color:#16a34a;font-weight:bold;'>Q</span>";
            return "";
        }

        // 并列名次比较：硬件计时控制器以 1/100 秒精度（单字节）发送成绩，
        // 软件侧 FinalTime 是 double，存储 0.5468 这样的值时由于 IEEE 754 表达
        // 会有微小尾差（54.68 实际为 54.68000000000000682…），不同计时源裁定后
        // "本应并列"的两条成绩用 == 比较常常失败。
        // 因此先按硬件精度四舍五入到 1/100 秒整数（百分秒），再做 int 相等比较：
        // 两条都 round 到 5468 → 视为并列；硬件上一致即并列，与精度直接对齐。
        // 5 处排名计算（UpdateHeatRanking / GetEventRanking / ComputeLiveRanks / 两处打印）统一调用此方法。
        private static bool IsTieTime(double a, double b) {
            // MidpointRounding.AwayFromZero 与裁判惯例一致：54.685 → 54.69 而非 54.68
            long ah = (long)Math.Round(a * 100.0, MidpointRounding.AwayFromZero);
            long bh = (long)Math.Round(b * 100.0, MidpointRounding.AwayFromZero);
            return ah == bh;
        }

        // 仅本组最快成绩（含并列）的 LaneResult.RecordNote 保留；其它人 RecordNote 清空。
        // 用于"一组多人破纪录只显示第 1 名记录"的展示+持久化逻辑。
        // 对 DSQ/DNS/DNF：成绩无效，本就不参与"最快"判定（也不会有 RecordNote 留存）。
        private void EnforceOnlyLeaderRecordNote() {
            var swimmers = GetCurrentHeatSwimmers();
            double leaderTime = double.MaxValue;
            foreach (var sw in swimmers) {
                if (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF") continue;
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                if (r == null || r.FinalTime <= 0) continue;
                if (r.FinalTime < leaderTime) leaderTime = r.FinalTime;
            }
            if (leaderTime == double.MaxValue) return;
            // 与 IsTieTime 同精度：按硬件 1/100 秒整数比较，避免浮点尾差
            long leaderHundredths = (long)Math.Round(leaderTime * 100.0, MidpointRounding.AwayFromZero);
            foreach (var sw in swimmers) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                if (r == null || string.IsNullOrEmpty(r.RecordNote)) continue;
                long curH = (long)Math.Round(r.FinalTime * 100.0, MidpointRounding.AwayFromZero);
                if (curH > leaderHundredths) {
                    AddLog(string.Format("  泳道{0} {1} 非本组第1名（{2} > {3}），清除破/平纪录标识 [{4}]",
                        sw.Lane, sw.Name, TimeFormatter.Format(r.FinalTime), TimeFormatter.Format(leaderTime), r.RecordNote));
                    r.RecordNote = "";
                }
            }
        }

        // 裁判确认成绩后调用：把本组中所有 RecordNote 含"破纪录"标签（不带=前缀）的成绩
        // 用于刷新对应 SwimmingRecord 条目（保持者/时间/日期/地点同步更新）
        private void UpdateRecordsAfterConfirm() {
            var heatSwimmers = GetCurrentHeatSwimmers();
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            int updated = 0;
            foreach (var sw in heatSwimmers) {
                var res = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                if (res == null || res.FinalTime <= 0) continue;
                if (string.IsNullOrEmpty(res.RecordNote)) continue;
                if (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF") continue;
                string ageGroup = sw.AgeCategory ?? "";
                // 找出所有应"破"的记录（仅破，不刷新平纪录）
                foreach (var record in _records.Where(r => r.Time > 0 && r.Gender == _currentGender && r.EventName == _currentEvent).ToList()) {
                    string rg = record.AgeGroup ?? "";
                    if (!string.IsNullOrEmpty(rg) && rg != ageGroup) continue;
                    if (res.FinalTime < record.Time) {
                        record.HolderName = sw.Name ?? "";
                        record.HolderCountry = sw.Country ?? "";
                        record.Time = res.FinalTime;
                        record.TimeInSeconds = res.FinalTime;
                        record.Date = today;
                        record.Location = _competitionName ?? "";
                        AddLog(string.Format("✓ 已更新{0}: {1}({2}) {3} @ {4}",
                            record.RecordType, record.HolderName, record.HolderCountry,
                            TimeFormatter.Format(record.Time), today));
                        updated++;
                    }
                }
            }
            if (updated > 0) AutoSaveData(); // 纪录在 CompetitionPackage 中持久化
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
                // _runningTime 完全由硬件 0x7F 帧驱动（见 ProcessTimingData 早期分支），
                // 此处不再用本地 DateTime 自算时间：硬件是唯一权威时间源。
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
                        // 空泳道或 DNS/DNF 运动员：不自动打开任何设备；
                        // 抢跳 DSQ 仍继续比赛，下一段触板/盲表照常打开以接收原始数据
                        if (!IsLaneReceivingData(state.Lane)) { changed = true; continue; }
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
            // 状态守卫 → 改本地状态 → 送 0x21；硬件参数在每次 Ready 时由 SendSetMatchEventToHardware 一同下发
            if (_raceState != RaceState.Waiting) return;
            // 已确认成绩的组：禁止再次开始比赛
            if (!string.IsNullOrEmpty(_currentEvent) && _currentHeat > 0
                && IsHeatConfirmed(_currentAgeGroup, _currentGender, _currentEvent, _currentStage, _currentHeat)) {
                AddLog("当前组已确认成绩，不能再次开始");
                return;
            }
            // 本地点击弹确认；硬件触发或 WebSocket 远程调用（sender==null）跳过
            if (sender != null) {
                string info = string.Format("{0} {1} {2} 第{3}组",
                    _currentGender ?? "", _currentEvent ?? "", _currentStage ?? "", _currentHeat);
                var r = MessageBox.Show(
                    "确定让本组进入【就位】状态？" + info + "\n",
                    "就位确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            // 实际进 Ready 状态 + 推送 0x43 + 0x21 给硬件（仅本地点击时；硬件回流路径 sender==null 不回送）
            EnterReadyStateInternal(pushToHardware: sender != null);
        }

        // Ready 内部入口：状态机更新 + 可选硬件命令推送。
        // - Ready_Click 弹完确认对话框后调用（pushToHardware=本地点击）。
        // - StartRace_Click 自动就位时调用（pushToHardware=true，让硬件先收到 0x43+0x21 进入 Ready，
        //   随后的 0x1C 才会被硬件接受），不弹对话框（用户按发令已明确表示要开始）。
        private void EnterReadyStateInternal(bool pushToHardware) {
            _raceState = RaceState.Ready;
            UpdateRaceStateDisplay();
            // ResetForNewRace 把发令端出发台打开（"准备就绪"语义），其它端关闭
            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
            }
            // 立刻刷新泳道状态 UI — 此时 _raceTimer 还没启动（要发令后才启动），
            // 100ms tick 不会自动重绘，必须显式调一次让发令端出发台立即变成打开
            UpdateLaneStatusDisplay();
            AddLog("就位");
            // Set_MatchEvent (0x43) 和 发令点已经在 SetCurrentEvent / SetCurrentHeat 同步过了，
            // 这里只送 0x21 准备就绪，硬件据此打开出发台。
            if (pushToHardware && _timingBridge != null && _timingBridge.IsConnected) {
                _timingBridge.SendCommand(0x21);
                AddLog("已向硬件发送 0x21 准备就绪");
            }
            Broadcast();
        }

        private void StartRace_Click(object sender, RoutedEventArgs e) {
            // 计时复位后状态会回到 Waiting；点"发令"前用户可能没点"就位"，此时自动先就位再发令。
            // 走 EnterReadyStateInternal 跳过 Ready_Click 的确认对话框（用户按"发令"已明确开始意图），
            // 同时仍把 0x43+0x21 推给硬件，让硬件先进 Ready 再接受 0x1C。
            if (_raceState == RaceState.Waiting) {
                // 已确认组守卫
                if (!string.IsNullOrEmpty(_currentEvent) && _currentHeat > 0
                    && IsHeatConfirmed(_currentAgeGroup, _currentGender, _currentEvent, _currentStage, _currentHeat)) {
                    AddLog("当前组已确认成绩，不能再次开始");
                    return;
                }
                EnterReadyStateInternal(pushToHardware: sender != null);
            }
            if (_raceState != RaceState.Ready) return;
            _raceState = RaceState.Racing;
            _raceStartTime = DateTime.Now;
            _runningTime = 0;
            // 清掉旧硬件锚点：等下一个 0x7F 帧重新对齐到硬件
            _hwRunningTimeAvailable = false;
            _hwRunningTimeReceivedAt = DateTime.MinValue;
            _hwRunningTimeSec = 0;
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
            // 发令必须瞬时 — 参数已经在"就位"时同步给硬件了，这里只送 0x1C，硬件立刻启动计时
            if (sender != null && _timingBridge != null && _timingBridge.IsConnected) {
                _timingBridge.SendCommand(0x1C);
                AddLog("已向硬件发送 0x1C 发令");
            }
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
            // 本地点击"计时复位"先弹确认，避免误按导致丢失计时数据；
            // 硬件触发或 WebSocket 远程调用（sender==null）跳过对话框
            if (sender != null) {
                var r = MessageBox.Show("确定计时复位？", "计时复位确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }
            // 没当前组（_currentHeat <= 0）也要把状态机硬复位，避免按键静默失效。
            if (_currentHeat <= 0) {
                _raceState = RaceState.Waiting;
                if (_raceTimer != null) _raceTimer.Stop();
                if (_countdownTimer != null) _countdownTimer.Stop();
                _runningTime = 0;
                _raceStartTime = DateTime.MinValue;
                _hwRunningTimeAvailable = false;
                _hwRunningTimeReceivedAt = DateTime.MinValue;
                _hwRunningTimeSec = 0;
                if (RunningTimeText != null) RunningTimeText.Text = "0.00";
                try { UpdateRaceStateDisplay(); } catch { }
                if (sender == null) {
                    try { BroadcastDisplayMode("SHOW_WELCOME"); } catch { Broadcast(); }
                } else if (_timingBridge != null && _timingBridge.IsConnected) {
                    _timingBridge.SendCommand(0x20);
                    _timingBridge.DelayBetweenFrames(20);
                    _timingBridge.SendCommand(0x7F);
                    // 复位也要重发 0x43 Set_MatchEvent，否则硬件清空了"缺道"位图，
                    // 与主服务器实际的本组运动员名单不一致
                    _timingBridge.DelayBetweenFrames(20);
                    try { SendSetMatchEventToHardware(); } catch (Exception ex) { AddLog("Set_MatchEvent 重发失败: " + ex.Message); }
                }
                return;
            }

            // 区分两种"计时器清零"语义：
            //  A) 本组已确认成绩（赛程导航已 [已完赛]）：彻底复位 UI，自动跳到下一组准备开赛。
            //     已确认成绩永久锁定（_confirmedHeats + Swimmer.Results 已落盘），不再受任何影响。
            //  B) 本组未确认成绩（如抢跳召回重赛）：只清当前组的时间数据；备注栏（DSQ/DNS/DNF）保留。
            bool currentHeatConfirmed = !string.IsNullOrEmpty(_currentEvent) && _currentHeat > 0 &&
                _confirmedHeats.Contains(ConfirmedHeatKey(_currentAgeGroup, _currentGender, _currentEvent, _currentStage, _currentHeat));

            // 与硬件"复位"按键语义对齐：按下即生效，不再弹确认对话框（已在函数入口把 0x20 推给硬件）。
            // 已确认成绩的组：成绩在 _confirmedHeats / Swimmer.Results 中已落盘，本次复位不会丢；
            // 未确认的组：当前组的时间/分段数据会被清掉，DSQ/DNS/DNF 状态保留（与之前一致）。

            // ═══ 公共复位：计时器 / 状态 / 显示 ═══
            _raceState = RaceState.Waiting;
            _raceTimer.Stop();
            _countdownTimer.Stop();
            _runningTime = 0;
            _raceStartTime = DateTime.MinValue;
            // 清掉硬件时间锚点；如果硬件没复位继续发 0x7F，UI 仍会被新帧立刻对齐
            _hwRunningTimeAvailable = false;
            _hwRunningTimeReceivedAt = DateTime.MinValue;
            _hwRunningTimeSec = 0;
            // 标记本次复位时刻 — 接下来 1 秒内到达的硬件 0x1C 当作回弹忽略
            _lastResetAt = DateTime.Now;
            _resultConfirmed = false;

            _firstPlaceFinishTime = "";
            _firstPlaceShowStart = DateTime.MinValue;
            _laneSplitCount.Clear();
            _laneSplitShowTime.Clear();
            _laneReactionLastValue.Clear();
            _laneReactionShowTime.Clear();
            if (RunningTimeText != null) RunningTimeText.Text = "0.00";

            if (_rawTimingLog != null) _rawTimingLog.Clear();

            AutoAdjustStartPosition();
            // 计时复位：所有泳道全部回到"未开赛/全关闭"状态。
            // ResetForNewRace 会自动把发令端出发台打开（为下一场预备），但复位语义是"全部关闭"，
            // 等用户按"就绪"才打开。这里立即覆盖回 Closed。
            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
                state.LeftStartBlockStatus = DeviceStatus.Closed;
                state.RightStartBlockStatus = DeviceStatus.Closed;
            }

            bool stageDoneSwitchToWelcome = false;
            if (currentHeatConfirmed) {
                // A) 已确认：成绩锁定，UI彻底归零；优先自动切到下一组开赛
                int totalHeats = CountHeatsForEvent(_currentAgeGroup, _currentGender, _currentEvent, _currentStage);
                if (_currentHeat < totalHeats) {
                    AddLog(string.Format("计时复位：本组({0})已确认，自动切到第{1}组", _currentHeat, _currentHeat + 1));
                    SetCurrentHeat(_currentHeat + 1);
                } else {
                    // 已是本赛次最后一组：完全清空泳道行（不显示已完赛数据），等待操作员从赛程导航选下一项
                    AddLog(string.Format("计时复位：本组({0})已确认，本赛次已完赛，泳道已清空，大屏切到欢迎画面", _currentHeat));
                    int oldHeat = _currentHeat;
                    _currentHeat = 0;        // 让 GetCurrentHeatSwimmers 返回空，泳道行整体置为"（空泳道）"
                    if (CurrentHeatText != null) CurrentHeatText.Text = string.Format("第{0}组已完赛 / 共{1}组", oldHeat, totalHeats);
                    if (PoolCurrentEventText != null) PoolCurrentEventText.Text = string.Format("{0}{1} {2} {3} 已完赛",
                        string.IsNullOrEmpty(_currentAgeGroup) ? "" : ("[" + _currentAgeGroup + "] "),
                        _currentGender, _currentEvent, _currentStage);
                    UpdateRaceStateDisplay();
                    UpdateLaneStatusDisplay();
                    stageDoneSwitchToWelcome = true;   // 末尾用 SHOW_WELCOME 取代 SHOW_LIVE_RACE，避免被覆盖
                }
            } else {
                // B) 未确认（抢跳重赛）：清当前组的时间/分段数据，保留备注（DSQ/DNS/DNF）；停留在当前组
                var currentSwimmers = GetCurrentHeatSwimmers();
                foreach (var sw in currentSwimmers) {
                    bool keepStatus = sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF";
                    var result = sw.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                    if (result != null) sw.Results.Remove(result);
                    if (!keepStatus) sw.Status = "";
                }
                AddLog("计时复位（重新发令）");
                UpdateRaceStateDisplay();
                UpdateLaneStatusDisplay();
            }

            // 仅在用户本地点击复位（sender != null）时把 0x20 + 0x7F 推给硬件，
            // 之后再补发一次 0x43 Set_MatchEvent —— 硬件 0x20 复位会顺带清掉"缺道"位图，
            // 不重发的话主服务器的本组缺道与硬件就不一致了
            if (sender != null && _timingBridge != null && _timingBridge.IsConnected) {
                _timingBridge.SendCommand(0x20);
                _timingBridge.DelayBetweenFrames(20);
                _timingBridge.SendCommand(0x7F);
                _timingBridge.DelayBetweenFrames(20);
                try { SendSetMatchEventToHardware(); } catch (Exception ex) { AddLog("Set_MatchEvent 重发失败: " + ex.Message); }
            }
            try { BuildScheduleTree(); } catch { }
            // 最后一次广播：若赛次已结束就直接发 SHOW_WELCOME（包含本次 GetStatusData），
            // 否则发 SHOW_LIVE_RACE。两者只发一次，避免后发的覆盖前发的。
            if (stageDoneSwitchToWelcome) {
                try { BroadcastDisplayMode("SHOW_WELCOME"); } catch { Broadcast(); }
            } else {
                Broadcast();
            }
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

            // 起跳犯规判罚改为人工：硬件检测到的抢跳/可疑只标红反应时（IsSuspectFalseStart），
            // 不再在确认时自动 DSQ。裁判要判罚需在控制台手动按 DSQ 按钮 (MARK_DSQ)。

            // 把当前组锁定到 _confirmedHeats（先于其它 UI 步骤，避免任何异常导致锁定状态没生效）
            _confirmedHeats.Add(ConfirmedHeatKey(_currentAgeGroup, _currentGender, _currentEvent, _currentStage, _currentHeat));

            // 立即重建赛程树：哪怕下面 UpdateHeatRanking/AutoSaveData/SaveRawTimingLog 偶发异常，
            // "已完赛"标记也不会丢；之前用 try{}catch{} 把异常吞掉，"偶尔不打标记"就源于此。
            try { BuildScheduleTree(); }
            catch (Exception ex) { AddLog("赛程导航刷新失败(确认成绩-早): " + ex.Message); }

            try { UpdateHeatRanking(); } catch (Exception ex) { AddLog("成绩排名计算失败: " + ex.Message); }
            try { UpdateRecordsAfterConfirm(); } catch (Exception ex) { AddLog("更新记录库失败: " + ex.Message); }
            try { AutoSaveData(); } catch (Exception ex) { AddLog("自动保存失败: " + ex.Message); }
            try { UpdateLaneStatusDisplay(); } catch (Exception ex) { AddLog("泳道状态显示刷新失败: " + ex.Message); }
            try { UpdateRaceStateDisplay(); } catch (Exception ex) { AddLog("比赛状态显示刷新失败: " + ex.Message); }

            AddLog(string.Format("★ 已确认本组成绩: {0}{1}子 {2} {3} 第{4}组",
                string.IsNullOrEmpty(_currentAgeGroup) ? "" : ("[" + _currentAgeGroup + "] "),
                _currentGender, _currentEvent, _currentStage, _currentHeat));

            try { SaveRawTimingLog(); } catch (Exception ex) { AddLog("保存原始计时数据失败: " + ex.Message); }

            // 刷新成绩与排名页面
            try { UpdateResultHeatCombo(); } catch (Exception ex) { AddLog("成绩组次下拉刷新失败: " + ex.Message); }
            try { RefreshResultGrid(); } catch (Exception ex) { AddLog("成绩与排名刷新失败: " + ex.Message); }

            // 末尾再刷一次赛程导航，吸收期间任何异步 UI 状态变化
            try { BuildScheduleTree(); }
            catch (Exception ex) { AddLog("赛程导航刷新失败(确认成绩-后): " + ex.Message); }

            // 检查该项目该阶段是否所有组都已完赛（广播给HTML控制端）
            try { CheckStageComplete(); } catch (Exception ex) { AddLog("赛次完赛检查失败: " + ex.Message); }

            // EXE本地也弹晋级确认（当从EXE按钮触发时）
            if (sender != null) {
                try { CheckStageCompleteLocal(); } catch (Exception ex) { AddLog("本地赛次完赛检查失败: " + ex.Message); }
            }

            Broadcast();

            // 确认成绩后大屏自动从"比赛视图"切换到"本组成绩"
            try { BroadcastDisplayMode("SHOW_HEAT_RESULT"); } catch (Exception ex) { AddLog("切换大屏到本组成绩失败: " + ex.Message); }
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

        // 切换主标签页：不影响"比赛控制"后台运行，也不强行落盘（避免覆盖未确认的成绩）
        // 进入"成绩与排名"页时按当前内存重建下拉/网格，让操作员能即时看到已确认的组次
        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_initialized) return;
            if (e.OriginalSource != MainTabControl) return;   // 仅顶层 Tab 切换才处理（跳过子标签事件冒泡）
            var selected = MainTabControl.SelectedItem as TabItem;
            if (selected != null && selected.Header != null && selected.Header.ToString() == "成绩与排名") {
                try { RefreshAllAgeGroupFilterCombos(); } catch { }
                try { UpdateResultHeatCombo(); } catch { }
                try { RefreshResultGrid(); } catch { }
            }
        }

        private void PrevHeat_Click(object sender, RoutedEventArgs e) {
            if (_currentHeat > 1) {
                if (!CanLeaveCurrentHeat(_currentHeat - 1)) return;
                SetCurrentHeat(_currentHeat - 1);
            }
        }

        private void NextHeat_Click(object sender, RoutedEventArgs e) {
            if (_currentHeat < _totalHeats) {
                if (!CanLeaveCurrentHeat(_currentHeat + 1)) return;
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
            CurrentEventText.Text = (string.IsNullOrEmpty(_currentAgeGroup) ? "" : ("[" + _currentAgeGroup + "] ")) + _currentGender + " " + _currentEvent;
            UpdateRecordDisplay();
            UpdateLaneStatusDisplay();   // 主服务器本机泳道占用显示
            // 选项目时把比赛配置 / 发令点同步给硬件（出发台仍由 0x21 Ready 控制是否打开）：
            //   1) Set_MatchEvent (0x43) — 总圈数、左右预期触板次数、泳道开关位图
            //   2) Set_SwStartingPosition (0x10 0x42 [pos]) — 发令点
            if (_timingBridge != null && _timingBridge.IsConnected) {
                try { SendSetMatchEventToHardware(); } catch (Exception ex) { AddLog("Set_MatchEvent 同步失败: " + ex.Message); }
                try { SendStartPositionToHardware(); } catch (Exception ex) { AddLog("发令点同步失败: " + ex.Message); }
            }
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

        // 服务端门禁（处理来自 HTML/EXE 远程台的 SET_* 指令）：
        // - 比赛准备/进行中：拒
        // - 比赛已结束但成绩还未确认：拒
        // 拒绝时记日志（HTML/EXE 也会同步看到现有 currentXxx 不变）
        private bool IsHeatSwitchBlocked(string actionLabel) {
            if (_raceState == RaceState.Ready || _raceState == RaceState.Racing) {
                AddLog("比赛进行中不能" + actionLabel);
                return true;
            }
            // 已开赛/有成绩但未确认：阻止切组
            if (string.IsNullOrEmpty(_currentEvent) || _currentHeat <= 0) return false;
            if (_resultConfirmed) return false;
            bool anyResult = false;
            foreach (var sw in GetCurrentHeatSwimmers()) {
                if (sw.Results.Any(rx => rx.Stage == _currentStage && rx.Heat == _currentHeat)) { anyResult = true; break; }
            }
            if (anyResult) {
                AddLog("当前组未确认成绩，不能" + actionLabel);
                return true;
            }
            return false;
        }

        // 当前组未确认成绩时禁止切组：返回 true 表示放行，false 表示拦截
        // （已确认的成绩 / 还未开赛的组次 都允许切换）
        private bool CanLeaveCurrentHeat(int targetHeat) {
            if (string.IsNullOrEmpty(_currentEvent) || _currentHeat <= 0) return true;
            if (targetHeat == _currentHeat) return true;
            if (_resultConfirmed) return true;
            // 当前组没有任何成绩记录（还没开赛）也允许切走
            bool anyResult = false;
            var heatSwimmers = GetCurrentHeatSwimmers();
            foreach (var sw in heatSwimmers) {
                var r = sw.Results.FirstOrDefault(rx => rx.Stage == _currentStage && rx.Heat == _currentHeat);
                if (r != null) { anyResult = true; break; }
            }
            if (!anyResult) return true;
            MessageBox.Show(
                string.Format("当前组（{0} {1} 第{2}组）尚未确认成绩，不能切换到其它组。\n请先点击\"确认成绩\"或\"计时复位\"清除当前组数据。",
                    _currentGender, _currentEvent, _currentHeat),
                "操作被阻止", MessageBoxButton.OK, MessageBoxImage.Warning);
            AddLog("当前组未确认成绩，已拦截切组");
            return false;
        }

        private void SetCurrentHeat(int heat) {
            _currentHeat = heat;
            CurrentHeatText.Text = string.Format("第{0}组 / 共{1}组", heat, _totalHeats);
            if (PoolCurrentEventText != null)
                PoolCurrentEventText.Text = string.Format("{0}{1} {2} {3} 第{4}组",
                    string.IsNullOrEmpty(_currentAgeGroup) ? "" : ("[" + _currentAgeGroup + "] "),
                    _currentGender, _currentEvent, _currentStage, heat);

            // 判断该组是否已有确认成绩；若是则保留 Finished 状态，避免覆盖已确认的成绩显示
            bool heatAlreadyConfirmed = !string.IsNullOrEmpty(_currentEvent) && _currentHeat > 0
                && IsHeatConfirmed(_currentAgeGroup, _currentGender, _currentEvent, _currentStage, _currentHeat);

            _raceState = heatAlreadyConfirmed ? RaceState.Finished : RaceState.Waiting;
            _resultConfirmed = heatAlreadyConfirmed;
            _laneSplitCount.Clear();
            _laneSplitShowTime.Clear();
            _laneReactionLastValue.Clear();
            _laneReactionShowTime.Clear();
            _firstPlaceFinishTime = "";
            _firstPlaceShowStart = DateTime.MinValue;
            // 切换组次 = 复位计时器
            _runningTime = 0;
            _raceStartTime = DateTime.MinValue;
            _raceTimer.Stop();
            _countdownTimer.Stop();
            if (RunningTimeText != null) RunningTimeText.Text = "0.00";

            // 根据项目自动调整发令位置（50米项目切换到对面端）
            AutoAdjustStartPosition();
            UpdateRaceStateDisplay();

            // 复位所有泳道设备状态。注意：选项目/切组次时出发台必须保持【关闭】，
            // 等操作员按"就绪"按键确认后才打开 — 因此 ResetForNewRace 之后立即把
            // 两端出发台都置为 Closed（覆盖 ResetForNewRace 自动把发令端置 Open 的行为）。
            foreach (var state in _laneDeviceStates) {
                state.ResetForNewRace(_laneCloseSettings.StartPosition);
                state.LeftStartBlockStatus = DeviceStatus.Closed;
                state.RightStartBlockStatus = DeviceStatus.Closed;
            }
            // 若该组已确认成绩：把每位完赛运动员对应泳道的 IsFinished 置回 true，
            // 让广播/UI 把对应运动员显示为已完赛（保留 finalTime 显示）
            if (heatAlreadyConfirmed) {
                foreach (var sw in GetCurrentHeatSwimmers()) {
                    var sa = sw.GetAssignmentForStage(_currentStage);
                    int swLane = sa != null ? sa.Lane : sw.Lane;
                    var ls = _laneDeviceStates.FirstOrDefault(s => s.Lane == swLane);
                    if (ls == null) continue;
                    var rr = sw.Results.FirstOrDefault(rx => rx.Stage == _currentStage && rx.Heat == _currentHeat);
                    if (rr != null && rr.FinalTime > 0) {
                        ls.IsFinished = true;
                    }
                }
            }

            UpdateLaneStatusDisplay();
            UpdateRecordDisplay();
            Broadcast();
            // 组次变更 → 重新同步 0x43（不同组的空泳道位图不同）+ 发令点
            if (_timingBridge != null && _timingBridge.IsConnected) {
                try { SendSetMatchEventToHardware(); } catch (Exception ex) { AddLog("Set_MatchEvent 同步失败: " + ex.Message); }
                try { SendStartPositionToHardware(); } catch (Exception ex) { AddLog("发令点同步失败: " + ex.Message); }
            }
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
            // 格式: "heat:组别|性别|项目|阶段|组次"  （组别可空）
            if (tag.StartsWith("heat:")) {
                string[] parts = tag.Substring(5).Split('|');
                if (parts.Length >= 5) {
                    int heat;
                    if (!int.TryParse(parts[4], out heat)) return;
                    // 当前组未确认成绩时禁止切换
                    if (!CanLeaveCurrentHeat(heat)) return;
                    _currentAgeGroup = parts[0];
                    _currentGender = parts[1];
                    _currentEvent = parts[2];
                    _currentStage = parts[3];
                    _isRelay = _currentEvent.Contains("接力");

                    _totalHeats = CountHeatsForEvent(_currentAgeGroup, _currentGender, _currentEvent, _currentStage);
                    CurrentEventText.Text = (string.IsNullOrEmpty(_currentAgeGroup) ? "" : ("[" + _currentAgeGroup + "] ")) + _currentGender + _currentEvent;
                    CurrentStageText.Text = _currentStage;
                    SetCurrentHeat(heat);
                }
            }
        }

        private int CountHeatsForEvent(string gender, string eventName, string stage) {
            return CountHeatsForEvent("", gender, eventName, stage);
        }

        private int CountHeatsForEvent(string ageGroup, string gender, string eventName, string stage) {
            // 优先用赛程项的 HeatCount（手动分组后更准确）
            var sched = _schedule.FirstOrDefault(s =>
                (s.AgeGroup ?? "") == (ageGroup ?? "") &&
                s.Gender == gender && s.EventName == eventName && s.Stage == stage);
            if (sched != null && sched.HeatCount > 0) return sched.HeatCount;

            var q = _swimmers.Where(s =>
                s.EventName == eventName &&
                s.Gender.StartsWith(gender) &&
                MatchesAgeGroup(s, ageGroup) &&
                s.CurrentStage == stage &&
                s.Heat > 0);
            return q.Any() ? q.Max(s => s.Heat) : 1;
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
                    string ag = ev.AgeGroup ?? "";
                    string header = (string.IsNullOrEmpty(ag) ? "" : ("[" + ag + "] "))
                                  + string.Format("{0} {1} {2}", ev.Gender, ev.EventName, ev.Stage);
                    bool allHeatsConfirmed = IsStageAllConfirmed(ag, ev.Gender, ev.EventName, ev.Stage);
                    if (!allHeatsConfirmed) sessionAllDone = false;

                    // Tag 扩展：event:AgeGroup|Gender|Event|Stage  或  heat/done:AgeGroup|Gender|Event|Stage|Heat
                    var eventItem = new TreeViewItem {
                        Tag = string.Format("event:{0}|{1}|{2}|{3}", ag, ev.Gender, ev.EventName, ev.Stage),
                        Header = allHeatsConfirmed ? header + " [已完赛]" : header,
                        Foreground = allHeatsConfirmed ? new SolidColorBrush(Colors.Gray) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                        IsExpanded = !allHeatsConfirmed
                    };

                    int heatCount = ev.HeatCount > 0 ? ev.HeatCount : 1;
                    for (int h = 1; h <= heatCount; h++) {
                        bool heatConfirmed = IsHeatConfirmed(ag, ev.Gender, ev.EventName, ev.Stage, h);
                        var heatItem = new TreeViewItem {
                            Tag = string.Format("{0}:{1}|{2}|{3}|{4}|{5}", heatConfirmed ? "done" : "heat", ag, ev.Gender, ev.EventName, ev.Stage, h),
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
            return IsHeatConfirmed("", gender, eventName, stage, heat);
        }

        private bool IsHeatConfirmed(string ageGroup, string gender, string eventName, string stage, int heat) {
            // 权威依据：操作员按下"确认本组成绩"时已写入 _confirmedHeats 的组永远视为已完赛，
            // 不会因后续数据变化（晋级、编排调整等）而失效
            if (_confirmedHeats.Contains(ConfirmedHeatKey(ageGroup, gender, eventName, stage, heat))) return true;

            bool isRelay = eventName.Contains("接力");
            var heatSwimmers = _swimmers.Where(s =>
                s.Gender == gender && s.EventName == eventName &&
                MatchesAgeGroup(s, ageGroup) &&
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
                // 必须按 (Stage, Heat) 精确查找该组的成绩，否则当运动员在多个组（如晋级前后）有成绩时
                // GetResultForStage 仅返回首条会误判（如检查第2组时拿到第1组成绩）
                var r = sw.Results.FirstOrDefault(rx => rx.Stage == stage && rx.Heat == heat);
                if (r == null || r.FinalTime <= 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 检查某项目某赛次是否全部组都已完赛
        /// </summary>
        private bool IsStageAllConfirmed(string gender, string eventName, string stage) {
            return IsStageAllConfirmed("", gender, eventName, stage);
        }

        private bool IsStageAllConfirmed(string ageGroup, string gender, string eventName, string stage) {
            var schedItem = _schedule.FirstOrDefault(s =>
                (s.AgeGroup ?? "") == (ageGroup ?? "") &&
                s.Gender == gender && s.EventName == eventName && s.Stage == stage);
            int heatCount = schedItem != null && schedItem.HeatCount > 0 ? schedItem.HeatCount : 0;
            if (heatCount == 0) return false;

            for (int h = 1; h <= heatCount; h++) {
                if (!IsHeatConfirmed(ageGroup, gender, eventName, stage, h)) return false;
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

        private void RenderPoolHeader() {
            if (PoolHeader == null) return;
            PoolHeader.Children.Clear();
            PoolHeader.ColumnDefinitions.Clear();

            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });   // 道次
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });    // 左发令
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });  // 左设备（+24，容纳圈数 spinner）
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 姓名+进度
            PoolHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });  // 右设备（+24，容纳圈数 spinner）
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

            // 左端表头与右端对称：[T] 盲3 盲2 盲1 出发 触板 圈
            // 当 LeftBlindWatchCount<3 时，最外侧的 盲3/盲2 标签使用 Hidden 保留位置，
            // 这样 盲1 / 出发 / 触板 的位置固定不动
            var leftLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string[] leftLabelDefs = new[] { "[T]:80", "盲3:26", "盲2:26", "盲1:26", "出发:26", "触板:26", "圈:50" };
            int leftBwc = _laneCloseSettings.LeftBlindWatchCount;
            for (int li = 0; li < leftLabelDefs.Length; li++) {
                string[] p = leftLabelDefs[li].Split(':');
                var tb = new TextBlock { Text = p[0], Width = double.Parse(p[1]), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                // li=1 是 盲3（数量>=3 才显示），li=2 是 盲2（数量>=2），li=3 是 盲1（数量>=1）
                if (li == 1) tb.Visibility = leftBwc >= 3 ? Visibility.Visible : Visibility.Hidden;
                else if (li == 2) tb.Visibility = leftBwc >= 2 ? Visibility.Visible : Visibility.Hidden;
                else if (li == 3) tb.Visibility = leftBwc >= 1 ? Visibility.Visible : Visibility.Hidden;
                leftLabels.Children.Add(tb);
            }
            Grid.SetColumn(leftLabels, 2); PoolHeader.Children.Add(leftLabels);

            var midLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            midLabels.Children.Add(new TextBlock { Text = "姓名/代表队", Width = 120, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            midLabels.Children.Add(new TextBlock { Text = "方向/进度", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(midLabels, 3); PoolHeader.Children.Add(midLabels);

            // 右端表头：圈 触板 出发 盲1 盲2 盲3 [T]（盲表数量减少时盲2/盲3 用 Hidden 保留位置）
            var rightLabels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string[] rightLabelDefs = new[] { "圈:50", "触板:26", "出发:26", "盲1:26", "盲2:26", "盲3:26", "[T]:80" };
            int rightBwc = _laneCloseSettings.RightBlindWatchCount;
            for (int ri = 0; ri < rightLabelDefs.Length; ri++) {
                string[] p = rightLabelDefs[ri].Split(':');
                var tb = new TextBlock { Text = p[0], Width = double.Parse(p[1]), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                // ri=3 是 盲1（数量>=1），ri=4 是 盲2（数量>=2），ri=5 是 盲3（数量>=3）
                if (ri == 3) tb.Visibility = rightBwc >= 1 ? Visibility.Visible : Visibility.Hidden;
                else if (ri == 4) tb.Visibility = rightBwc >= 2 ? Visibility.Visible : Visibility.Hidden;
                else if (ri == 5) tb.Visibility = rightBwc >= 3 ? Visibility.Visible : Visibility.Hidden;
                rightLabels.Children.Add(tb);
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
                case DeviceStatus.Touched: return _brushRed;     // 已触板（红）— 与损坏同色，按用户指定
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
            public TextBlock NameText, TeamText;
            public TextBlock ReactionText, DisplayTimeText, RankText, RemarkText;
            public Border LeftSignalInd, RightSignalInd;
        }

        private List<LaneRowUI> _laneRowUIs = new List<LaneRowUI>();
        private string _laneRowsBuiltKey = "";

        // 接力项目：根据当前段数计算正在游的棒次，返回 "第N棒: 队员姓名"。
        // 与 race_control.html / RemoteTimingControl 的算法一致：lapsPerLeg = 单棒距离 / 泳池长度。
        private string ComputeRelayCurrentLegLabel(Swimmer sw, int currentLap, bool isFinished) {
            if (sw == null) return "";
            string legNames = "";
            if (!string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                legNames = sw.Notes.Substring("接力队 棒次:".Length);
            if (string.IsNullOrEmpty(legNames)) return sw.Country ?? "";
            string[] parts = legNames.Split(',');
            if (parts.Length <= 1) return parts.Length > 0 ? parts[0].Trim() : "";

            string ev = _currentEvent ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(ev, @"(\d+)\s*[x×]\s*(\d+)");
            int legCount = m.Success ? int.Parse(m.Groups[1].Value) : 4;
            int legDist = m.Success ? int.Parse(m.Groups[2].Value) : 100;
            int poolLen = _poolConfig != null && _poolConfig.Length > 0 ? _poolConfig.Length : 50;
            int lapsPerLeg = Math.Max(1, legDist / poolLen);
            int currentLeg = currentLap / lapsPerLeg;
            if (currentLeg >= legCount) currentLeg = legCount - 1;
            if (currentLeg < 0) currentLeg = 0;
            if (isFinished) currentLeg = legCount - 1;
            string legName = currentLeg < parts.Length ? parts[currentLeg].Trim() : parts[0].Trim();
            return string.Format("第{0}棒: {1}", currentLeg + 1, legName);
        }

        // 解析 _currentEvent (如 "4×100米自由泳接力") → (legCount, lapsPerLeg)；解析失败回退 (4, 2)
        private void ParseRelayLayout(out int legCount, out int lapsPerLeg) {
            legCount = 4; lapsPerLeg = 2;
            var ev = _currentEvent ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(ev, @"(\d+)\s*[x×]\s*(\d+)");
            if (m.Success) {
                int n;
                if (int.TryParse(m.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n;
                int d;
                if (int.TryParse(m.Groups[2].Value, out d) && d > 0) {
                    int poolLen = (_poolConfig != null && _poolConfig.Length > 0) ? _poolConfig.Length : 50;
                    lapsPerLeg = Math.Max(1, d / poolLen);
                }
            }
        }

        // 由 currentLap 计算"出发台事件属于第几棒"（0-based）。
        //   leg-1 出发台触发时 currentLap=0 → 0
        //   leg-2 交接时 currentLap=lapsPerLeg → 1
        //   依次类推；越界时夹到 [0, legCount-1]
        private int ComputeRelayLegIndex(int currentLap) {
            int legCount, lapsPerLeg;
            ParseRelayLayout(out legCount, out lapsPerLeg);
            int idx = currentLap / lapsPerLeg;
            if (idx < 0) idx = 0;
            if (idx > legCount - 1) idx = legCount - 1;
            return idx;
        }

        // 找/建当前 stage+heat 对应的 LaneResult（用于接力反应时写入；避免事件早于触板创建 LaneResult 导致丢写）
        private LaneResult EnsureRelayLaneResult(Swimmer sw, int lane) {
            if (sw == null) return null;
            var res = sw.Results.FirstOrDefault(r2 => r2.Stage == _currentStage && r2.Heat == _currentHeat);
            if (res == null) {
                res = new LaneResult {
                    EventName = _currentEvent, Stage = _currentStage,
                    Heat = _currentHeat, Lane = lane
                };
                sw.Results.Add(res);
            }
            return res;
        }

        // 把 LegReactionTimes 预填到 legCount 个 0 占位槽位。
        // 0 表示该棒尚未记录到反应时；打印端看到 0 显示 "—"。
        private void EnsureLegReactionSlots(LaneResult res) {
            if (res == null) return;
            int legCount, lapsPerLeg;
            ParseRelayLayout(out legCount, out lapsPerLeg);
            if (res.LegReactionTimes == null) res.LegReactionTimes = new List<double>();
            while (res.LegReactionTimes.Count < legCount) res.LegReactionTimes.Add(0);
        }

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
            // 道次顺序：正序=升序 0→9（顶到底）；逆序=降序 9→0
            allPoolLanes.Sort();
            if (_laneCloseSettings.LaneOrder == "reverse") allPoolLanes.Reverse();

            // 构造本次数据的key（运动员列表或泳道集合变化时需要重建UI；盲表数量变更也要重建）
            string key = _currentGender + "|" + _currentEvent + "|" + _currentStage + "|" + _currentHeat
                + "|" + allPoolLanes.Count + "|" + currentSwimmers.Count
                + "|bw:" + _laneCloseSettings.LeftBlindWatchCount + "/" + _laneCloseSettings.RightBlindWatchCount;
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
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
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

                // 左端 5 圆点：[0]=盲3, [1]=盲2, [2]=盲1, [3]=出发, [4]=触板（与右端对称）
                // 创建时即按当前 LeftBlindWatchCount 设置 Visibility，避免初始一闪而过
                int initLbc = _laneCloseSettings.LeftBlindWatchCount;
                rowUI.LeftDots = new Ellipse[5];
                for (int i = 0; i < 5; i++) {
                    var dot = new Ellipse { Width = 22, Height = 22, Margin = new Thickness(2, 0, 2, 0) };
                    if (i == 0) dot.Visibility = initLbc >= 3 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 1) dot.Visibility = initLbc >= 2 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 2) dot.Visibility = initLbc >= 1 ? Visibility.Visible : Visibility.Hidden;
                    rowUI.LeftDots[i] = dot;
                    leftDev.Children.Add(dot);
                }

                // 圈数 [数字 + ▲▼ 微调]：左端（spinner 风格，更简洁美观）
                var leftRemainText = new TextBlock { Width = 26, FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                leftDev.Children.Add(leftRemainText);
                rowUI.LeftRemainText = leftRemainText;
                var leftSpinner = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
                var leftLapUp = new Button { Content = "▲", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
                leftLapUp.PreviewMouseLeftButtonDown += delegate(object s1, System.Windows.Input.MouseButtonEventArgs e1) { e1.Handled = true; AdjustLapDisplay(capLane, true, +1); };
                var leftLapDown = new Button { Content = "▼", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Margin = new Thickness(0, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
                leftLapDown.PreviewMouseLeftButtonDown += delegate(object s1, System.Windows.Input.MouseButtonEventArgs e1) { e1.Handled = true; AdjustLapDisplay(capLane, true, -1); };
                leftSpinner.Children.Add(leftLapUp);
                leftSpinner.Children.Add(leftLapDown);
                leftDev.Children.Add(leftSpinner);
                Grid.SetColumn(leftDev, 2); grid.Children.Add(leftDev);

                // Col 3: 姓名 + 进度条
                var midPanel = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                var infoStack = new StackPanel { Width = 120 };
                if (sw == null) row.Opacity = 0.35;
                // 姓名 + 队伍/棒次 两行 — 真实文字在 RefreshLaneRows 中按当前棒次实时更新
                var nameTb = new TextBlock { Text = "", FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontSize = 14 };
                var teamTb = new TextBlock { Text = "", Foreground = _brushMutedText, FontSize = 12 };
                infoStack.Children.Add(nameTb);
                infoStack.Children.Add(teamTb);
                rowUI.NameText = nameTb;
                rowUI.TeamText = teamTb;
                DockPanel.SetDock(infoStack, Dock.Left); midPanel.Children.Add(infoStack);

                var trackBorder = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A2332")), CornerRadius = new CornerRadius(4), Height = 22, Padding = new Thickness(4, 0, 4, 0), MinWidth = 60 };
                var trackText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                trackBorder.Child = trackText;
                midPanel.Children.Add(trackBorder);
                rowUI.TrackText = trackText;
                Grid.SetColumn(midPanel, 3); grid.Children.Add(midPanel);

                // Col 4: 右设备
                var rightDev = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                // 圈数 [▲▼ 微调 + 数字]：右端（spinner 风格）
                var rightSpinner = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
                var rightLapUp = new Button { Content = "▲", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
                rightLapUp.PreviewMouseLeftButtonDown += delegate(object s1, System.Windows.Input.MouseButtonEventArgs e1) { e1.Handled = true; AdjustLapDisplay(capLane, false, +1); };
                var rightLapDown = new Button { Content = "▼", Width = 18, Height = 13, FontSize = 9, BorderThickness = new Thickness(0), Background = _brushSlate, Foreground = Brushes.White, Padding = new Thickness(0), Margin = new Thickness(0, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
                rightLapDown.PreviewMouseLeftButtonDown += delegate(object s2, System.Windows.Input.MouseButtonEventArgs e2) { e2.Handled = true; AdjustLapDisplay(capLane, false, -1); };
                rightSpinner.Children.Add(rightLapUp);
                rightSpinner.Children.Add(rightLapDown);
                rightDev.Children.Add(rightSpinner);
                var rightRemainText = new TextBlock { Width = 26, FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                rightDev.Children.Add(rightRemainText);
                rowUI.RightRemainText = rightRemainText;

                // 右端 5 圆点：[0]=触板, [1]=出发, [2]=盲1, [3]=盲2, [4]=盲3
                int initRbc = _laneCloseSettings.RightBlindWatchCount;
                rowUI.RightDots = new Ellipse[5];
                for (int i = 0; i < 5; i++) {
                    var dot = new Ellipse { Width = 22, Height = 22, Margin = new Thickness(2, 0, 2, 0) };
                    if (i == 2) dot.Visibility = initRbc >= 1 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 3) dot.Visibility = initRbc >= 2 ? Visibility.Visible : Visibility.Hidden;
                    else if (i == 4) dot.Visibility = initRbc >= 3 ? Visibility.Visible : Visibility.Hidden;
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
            // 并列名次（competition ranking 1-2-2-4）：仅对已完赛运动员按相同 FinalTime 并列
            int rank = 1;
            for (int i = 0; i < rankables.Count; i++) {
                if (i > 0) {
                    var prev = rankables[i - 1];
                    var cur = rankables[i];
                    bool tieEligible = prev.Item4 && cur.Item4 && IsTieTime(prev.Item3, cur.Item3);
                    if (!tieEligible) rank = i + 1;
                }
                liveRanks[rankables[i].Item1] = rank;
            }
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
                    if (rowUI.NameText != null) rowUI.NameText.Text = "（空泳道）";
                    if (rowUI.TeamText != null) rowUI.TeamText.Text = "";
                    if (rowUI.LeftDots != null) for (int i = 0; i < rowUI.LeftDots.Length; i++) rowUI.LeftDots[i].Fill = _brushSlate;
                    if (rowUI.RightDots != null) for (int i = 0; i < rowUI.RightDots.Length; i++) rowUI.RightDots[i].Fill = _brushSlate;
                    // 空泳道也要按当前盲表数量隐藏多余圆点
                    if (rowUI.LeftDots != null) {
                        int lbc0 = _laneCloseSettings.LeftBlindWatchCount;
                        rowUI.LeftDots[0].Visibility = lbc0 >= 3 ? Visibility.Visible : Visibility.Hidden; // 盲3
                        rowUI.LeftDots[1].Visibility = lbc0 >= 2 ? Visibility.Visible : Visibility.Hidden; // 盲2
                        rowUI.LeftDots[2].Visibility = lbc0 >= 1 ? Visibility.Visible : Visibility.Hidden; // 盲1
                    }
                    if (rowUI.RightDots != null) {
                        int rbc0 = _laneCloseSettings.RightBlindWatchCount;
                        rowUI.RightDots[2].Visibility = rbc0 >= 1 ? Visibility.Visible : Visibility.Hidden; // 盲1
                        rowUI.RightDots[3].Visibility = rbc0 >= 2 ? Visibility.Visible : Visibility.Hidden; // 盲2
                        rowUI.RightDots[4].Visibility = rbc0 >= 3 ? Visibility.Visible : Visibility.Hidden; // 盲3
                    }
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
                // 已记录最终成绩的运动员（即便 LaneDeviceState.IsFinished 因切组被复位）也算作完赛
                bool isFinished = (ls != null && ls.IsFinished) || (result != null && result.FinalTime > 0);
                string status = sw.Status ?? "";
                bool isDQ = status == "DSQ" || status == "DNS" || status == "DNF";

                // 姓名/队伍 — 个人项目: 姓名+代表队；接力项目: 代表队 + 第N棒:运动员
                if (rowUI.NameText != null && rowUI.TeamText != null) {
                    if (_isRelay) {
                        rowUI.NameText.Text = sw.Country ?? "";
                        // 用 spinner 偏移后的圈数，让"加圈/减圈"即时切换棒次显示
                        rowUI.TeamText.Text = ComputeRelayCurrentLegLabel(sw, GetDisplayedCurrentLap(ls), isFinished);
                    } else {
                        rowUI.NameText.Text = sw.Name ?? "";
                        rowUI.TeamText.Text = sw.Country ?? "";
                    }
                }
                // DSQ/DNS/DNF 行淡化
                rowUI.Row.Opacity = isDQ ? 0.45 : 1.0;

                // 行边框（已判罚抢跳=DSQ高亮，疑似抢跳=琥珀色提示，选中=蓝色）
                if (ls != null && ls.IsFalseStart) {
                    rowUI.Row.BorderBrush = _brushAmber;
                    rowUI.Row.BorderThickness = new Thickness(2);
                } else if (ls != null && ls.IsSuspectFalseStart) {
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

                // 左设备5个圆点（与右端对称）：盲表3 / 盲表2 / 盲表1 / 出发台 / 触板
                if (ls != null) {
                    rowUI.LeftDots[0].Fill = GetDeviceBrush(ls.LeftBlindWatch3Status);
                    rowUI.LeftDots[1].Fill = GetDeviceBrush(ls.LeftBlindWatch2Status);
                    rowUI.LeftDots[2].Fill = GetDeviceBrush(ls.LeftBlindWatch1Status);
                    rowUI.LeftDots[3].Fill = GetDeviceBrush(ls.LeftStartBlockStatus);
                    rowUI.LeftDots[4].Fill = GetDeviceBrush(ls.LeftTouchpadStatus);
                } else {
                    for (int i = 0; i < 5; i++) rowUI.LeftDots[i].Fill = _brushSlate;
                }
                // 按当前设置的左盲表数量隐藏多余圆点（保留位置：用 Hidden 而非 Collapsed
                // 这样盲1 / 出发台 / 触板的位置固定不变）
                int leftBwCount = _laneCloseSettings.LeftBlindWatchCount;
                rowUI.LeftDots[0].Visibility = leftBwCount >= 3 ? Visibility.Visible : Visibility.Hidden; // 盲3
                rowUI.LeftDots[1].Visibility = leftBwCount >= 2 ? Visibility.Visible : Visibility.Hidden; // 盲2
                rowUI.LeftDots[2].Visibility = leftBwCount >= 1 ? Visibility.Visible : Visibility.Hidden; // 盲1

                // 左剩余圈数（含 spinner 人工偏移）
                int leftRemain = GetDisplayedLapCount(ls, true);
                rowUI.LeftRemainText.Text = leftRemain > 0 ? leftRemain.ToString() : "0";
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
                // 按当前设置的右盲表数量隐藏多余圆点（保留位置）
                int rightBwCount = _laneCloseSettings.RightBlindWatchCount;
                rowUI.RightDots[2].Visibility = rightBwCount >= 1 ? Visibility.Visible : Visibility.Hidden;
                rowUI.RightDots[3].Visibility = rightBwCount >= 2 ? Visibility.Visible : Visibility.Hidden;
                rowUI.RightDots[4].Visibility = rightBwCount >= 3 ? Visibility.Visible : Visibility.Hidden;

                // 右剩余圈数（含 spinner 人工偏移）
                int rightRemain = GetDisplayedLapCount(ls, false);
                rowUI.RightRemainText.Text = rightRemain > 0 ? rightRemain.ToString() : "0";
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
                // 关闭RT：始终隐藏反应时
                double reactionVal = (_laneCloseSettings.ReactionTimeEnabled && ls != null) ? ls.ReactionTime : 0;
                if (_laneCloseSettings.ReactionTimeEnabled && reactionVal != 0) {
                    if (!_laneReactionLastValue.ContainsKey(lane) || _laneReactionLastValue[lane] != reactionVal) {
                        _laneReactionLastValue[lane] = reactionVal;
                        _laneReactionShowTime[lane] = DateTime.Now;
                    }
                }
                bool reactionVisible = _laneCloseSettings.ReactionTimeEnabled && reactionVal != 0 && (
                    isFinished ||
                    (_laneReactionShowTime.ContainsKey(lane) &&
                     (DateTime.Now - _laneReactionShowTime[lane]).TotalSeconds < splitDisplaySec));
                rowUI.ReactionText.Text = reactionVisible ? reactionVal.ToString("F2") : "";
                // 起跳可疑/已判罚：反应时标红
                bool rtRed = _laneCloseSettings.ReactionTimeEnabled && ls != null && (ls.IsSuspectFalseStart || ls.IsFalseStart);
                rowUI.ReactionText.Foreground = rtRed ? _brushRed : Brushes.White;

                // 备注
                // 备注栏：DSQ/DNS/DNF 优先显示；否则显示破/平纪录标识（如 WR / =AR / WR/NR）
                string remark;
                if (isDQ) remark = status;
                else if (result != null && !string.IsNullOrEmpty(result.RecordNote)) remark = result.RecordNote;
                else remark = "";
                rowUI.RemarkText.Text = remark;
                // 破纪录标记用金色高亮提示
                rowUI.RemarkText.Foreground = (!isDQ && result != null && !string.IsNullOrEmpty(result.RecordNote)) ? _brushAmber : _brushRed;
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
                if (_laneCloseSettings.ReactionTimeEnabled) {
                    string rt = ls != null && ls.ReactionTime != 0 ? ls.ReactionTime.ToString("F2") : "-";
                    sb.AppendFormat("反应时间: {0}\n", rt);
                } else {
                    sb.Append("反应时间: 已关闭\n");
                }
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
                if (_laneCloseSettings.ReactionTimeEnabled && ls != null && ls.ReactionTime != 0)
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
            string label = string.IsNullOrEmpty(_displayRecordLabel) ? "WR" : _displayRecordLabel;
            string typeName = string.IsNullOrEmpty(_displayRecordTypeName) ? "世界纪录" : _displayRecordTypeName;
            if (string.IsNullOrEmpty(_currentEvent)) { RecordDisplayText.Text = string.Format("{0}: ---    CR: ---", label); return; }
            string mainTime = "", crTime = "";
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
                if (rt == typeName || rt.Contains(typeName)) mainTime = TimeFormatter.Format(r.Time);
                if (rt.Contains("赛会") || rt.Contains("省运会")) crTime = TimeFormatter.Format(r.Time);
            }
            RecordDisplayText.Text = string.Format("{0}: {1}    CR: {2}",
                label,
                !string.IsNullOrEmpty(mainTime) ? mainTime : "---",
                !string.IsNullOrEmpty(crTime) ? crTime : "---");
        }

        private void RefreshDisplayRecordLabelText() {
            if (DisplayRecordLabelText == null) return;
            DisplayRecordLabelText.Text = string.Format("{0} ({1})",
                string.IsNullOrEmpty(_displayRecordLabel) ? "WR" : _displayRecordLabel,
                string.IsNullOrEmpty(_displayRecordTypeName) ? "世界纪录" : _displayRecordTypeName);
        }

        private void DisplayRecordTypeSetting_Click(object sender, RoutedEventArgs e) {
            var dlg = new Window {
                Title = "大屏显示记录设置",
                Width = 520, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock {
                Text = "大屏显示记录设置",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                Margin = new Thickness(0, 0, 0, 6)
            });
            sp.Children.Add(new TextBlock {
                Text = "选择大屏顶部及泳道实时状态界面中显示的主纪录类型。可在下方列表中编辑、增加新项（如 市记录 / 行业纪录 等）。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 12)
            });

            var working = new System.Collections.ObjectModel.ObservableCollection<DisplayRecordOption>();
            foreach (var o in _displayRecordOptions) working.Add(new DisplayRecordOption { Label = o.Label, TypeName = o.TypeName });

            var grid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single, Height = 250,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
                HeadersVisibility = DataGridHeadersVisibility.All, RowHeaderWidth = 50
            };
            grid.LoadingRow += delegate(object _s, DataGridRowEventArgs _ev) { _ev.Row.Header = (_ev.Row.GetIndex() + 1).ToString(); };
            grid.Columns.Add(new DataGridTextColumn { Header = "简称", Width = new DataGridLength(100),
                Binding = new System.Windows.Data.Binding("Label") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged } });
            grid.Columns.Add(new DataGridTextColumn { Header = "完整名称", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new System.Windows.Data.Binding("TypeName") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged } });
            grid.ItemsSource = working;
            // 选中当前生效项
            for (int i = 0; i < working.Count; i++) {
                if (working[i].Label == _displayRecordLabel && working[i].TypeName == _displayRecordTypeName) {
                    grid.SelectedIndex = i; break;
                }
            }
            sp.Children.Add(grid);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var btnAdd = new Button { Content = "新增", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnAdd.Click += delegate { working.Add(new DisplayRecordOption { Label = "新", TypeName = "新记录类型" }); };
            var btnDel = new Button { Content = "删除选中", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnDel.Click += delegate {
                var sel = grid.SelectedItem as DisplayRecordOption;
                if (sel != null) working.Remove(sel); else MessageBox.Show("请先选中一行");
            };
            btnRow.Children.Add(btnAdd);
            btnRow.Children.Add(btnDel);
            sp.Children.Add(btnRow);

            var okRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button { Content = "确定（应用选中项）", Padding = new Thickness(16, 6, 16, 6), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnOk.Click += delegate {
                try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
                var sel = grid.SelectedItem as DisplayRecordOption;
                if (sel == null) { MessageBox.Show("请先选中要应用的记录类型行"); return; }
                if (string.IsNullOrWhiteSpace(sel.Label) || string.IsNullOrWhiteSpace(sel.TypeName)) {
                    MessageBox.Show("简称和完整名称都不能为空"); return;
                }
                var finalList = new List<DisplayRecordOption>();
                var seen = new HashSet<string>();
                foreach (var r in working) {
                    string lab = (r.Label ?? "").Trim();
                    string tn = (r.TypeName ?? "").Trim();
                    if (string.IsNullOrEmpty(lab) || string.IsNullOrEmpty(tn)) continue;
                    string key = lab + "|" + tn;
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    finalList.Add(new DisplayRecordOption { Label = lab, TypeName = tn });
                }
                _displayRecordOptions = finalList;
                _displayRecordLabel = (sel.Label ?? "").Trim();
                _displayRecordTypeName = (sel.TypeName ?? "").Trim();
                dlg.DialogResult = true;
            };
            okRow.Children.Add(btnCancel);
            okRow.Children.Add(btnOk);
            sp.Children.Add(okRow);

            dlg.Content = sp;
            if (dlg.ShowDialog() == true) {
                RefreshDisplayRecordLabelText();
                UpdateRecordDisplay();
                AutoSaveData();
                Broadcast();
                AddLog(string.Format("大屏显示记录已切换为：{0} ({1})", _displayRecordLabel, _displayRecordTypeName));
            }
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
            // 取消 DSQ/DNS/DNF 后：若该运动员有有效成绩，按需重新比对纪录 + 更新本组排名
            var resCN = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
            if (resCN != null) {
                bool wasDQ = (oldStatus == "DSQ" || oldStatus == "DNS" || oldStatus == "DNF");
                if (wasDQ) {
                    if (resCN.Status == oldStatus) resCN.Status = "";
                    if (resCN.FinalTime > 0) {
                        // 复算破/平纪录标识
                        try { CheckRecords(swimmer, resCN); } catch { }
                    }
                }
            }
            if (laneState != null) {
                // 若比赛尚未结束/未有最终成绩，重置完成标志，使其继续参赛
                bool hasFinalTime = resCN != null && resCN.FinalTime > 0;
                if (!hasFinalTime) laneState.IsFinished = false;
            }
            LogRawTimingData(lane, "CANCEL_NOTE", 0);
            AddLog(string.Format("泳道{0} {1} 取消备注（原 {2}），恢复正常参赛状态",
                lane, swimmer.Name, string.IsNullOrEmpty(oldStatus) ? "无" : oldStatus));
            try { UpdateHeatRanking(); } catch { }
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

            // 当前 split 上可能已经记录了盲表时间（预创建 split 时盲表会写入 split.PushButton*Time），
            // 这是正常的工作路径；laneState.PendingBlind*Time 只是无 split 时的备用暂存。
            // 这里把两处都纳入候选，避免按下"盲表成绩"时找不到数据。
            var curSplit = FindCurrentSplit(lane);

            var blinds = new List<double>();
            if (laneState.PendingBlind1Time > 0) blinds.Add(laneState.PendingBlind1Time);
            if (laneState.PendingBlind2Time > 0) blinds.Add(laneState.PendingBlind2Time);
            if (laneState.PendingBlind3Time > 0) blinds.Add(laneState.PendingBlind3Time);
            if (curSplit != null) {
                if (curSplit.PushButton1Time > 0 && !blinds.Contains(curSplit.PushButton1Time)) blinds.Add(curSplit.PushButton1Time);
                if (curSplit.PushButton2Time > 0 && !blinds.Contains(curSplit.PushButton2Time)) blinds.Add(curSplit.PushButton2Time);
                if (curSplit.PushButton3Time > 0 && !blinds.Contains(curSplit.PushButton3Time)) blinds.Add(curSplit.PushButton3Time);
            }
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
                // DSQ/DNS/DNF：成绩无效，必须清除 RecordNote（否则破/平纪录标识仍残留），
                // 同时重新计算本组排名 + 复算其它运动员的破/平纪录（被取消的人不再占名次/不再当纪录候选）
                if (status == "DSQ" || status == "DNS" || status == "DNF") {
                    var res = swimmer.Results.FirstOrDefault(r => r.Stage == _currentStage && r.Heat == _currentHeat);
                    if (res != null) {
                        if (!string.IsNullOrEmpty(res.RecordNote)) {
                            AddLog(string.Format("  泳道{0} {1} 成绩无效，取消破/平纪录标识: {2}", lane, swimmer.Name, res.RecordNote));
                            res.RecordNote = "";
                        }
                        res.Status = status;
                        res.Rank = 0;
                    }
                    swimmer.CurrentRank = 0;
                }
                LogRawTimingData(lane, "MARK_" + status, 0);
                AddLog(string.Format("泳道{0} {1} 标记为 {2}", lane, swimmer.Name, status));
                var laneState = _laneDeviceStates.FirstOrDefault(s => s.Lane == lane);
                if (laneState != null) laneState.IsFinished = true;
                // 重新计算本组排名（被取消的运动员让出名次）
                try { UpdateHeatRanking(); } catch { }
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
                // 保存反应时间到成绩记录（关闭RT时不写入）
                if (_laneCloseSettings.ReactionTimeEnabled && laneState.ReactionTime > 0) {
                    result.StartingBlockTime = laneState.ReactionTime;
                }
                laneState.IsFinished = true;
            }

            AddLog(string.Format("泳道{0} 手动输入成绩: {1}", lane, TimeFormatter.Format(time)));
            UpdateHeatRanking();
            CheckRecords(swimmer, result);
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
            SendDeviceStatusesToHardware();   // 同步该泳道的设备状态到硬件
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
                Width = 420,
                SizeToContent = SizeToContent.Height, // 高度自适应内容，无滚动条
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
            var tbBigPage = AddSettingsRow(sp, "大屏翻屏时间", _laneCloseSettings.BigDisplayPageInterval.ToString(), "秒");

            // 终点位置
            var finishRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            finishRow.Children.Add(new TextBlock { Text = "终点位置", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbLeft = new RadioButton { Content = "左端", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneCloseSettings.FinishPosition == "left", GroupName = "FinPos", Margin = new Thickness(0, 0, 12, 0) };
            var rbRight = new RadioButton { Content = "右端", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneCloseSettings.FinishPosition == "right", GroupName = "FinPos" };
            finishRow.Children.Add(rbLeft);
            finishRow.Children.Add(rbRight);
            sp.Children.Add(finishRow);

            // 反应时检测（RT）开关：关闭后所有出发反应时相关处理跳过
            var rtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            rtRow.Children.Add(new TextBlock { Text = "反应时(RT)", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbRtOn = new RadioButton { Content = "打开", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneCloseSettings.ReactionTimeEnabled, GroupName = "RTSwitch", Margin = new Thickness(0, 0, 12, 0) };
            var rbRtOff = new RadioButton { Content = "关闭", Foreground = Brushes.White, FontSize = 14, IsChecked = !_laneCloseSettings.ReactionTimeEnabled, GroupName = "RTSwitch" };
            rtRow.Children.Add(rbRtOn);
            rtRow.Children.Add(rbRtOff);
            sp.Children.Add(rtRow);

            // 道次显示顺序：正序=顶到底为 0→9；逆序=顶到底为 9→0（同步给硬件计时器及所有 UI）
            var orderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            orderRow.Children.Add(new TextBlock { Text = "道次顺序", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            var rbOrderFwd = new RadioButton { Content = "正序 0→9", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneCloseSettings.LaneOrder != "reverse", GroupName = "LaneOrder", Margin = new Thickness(0, 0, 12, 0) };
            var rbOrderRev = new RadioButton { Content = "逆序 9→0", Foreground = Brushes.White, FontSize = 14, IsChecked = _laneCloseSettings.LaneOrder == "reverse", GroupName = "LaneOrder" };
            orderRow.Children.Add(rbOrderFwd);
            orderRow.Children.Add(rbOrderRev);
            sp.Children.Add(orderRow);

            // 设备状态管理按钮
            var deviceSep = new Border { BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0, 10, 0, 0) };
            var btnDeviceMgr = new Button { Content = "设备状态管理", Padding = new Thickness(0, 8, 0, 8), FontSize = 14, FontWeight = FontWeights.Bold, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnDeviceMgr.Click += delegate { dlg.DialogResult = false; DeviceStatus_Click(null, null); };
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

            if (dlg.ShowDialog() == true) {
                double v;
                if (double.TryParse(tbCloseTime.Text, out v)) _laneCloseSettings.LaneCloseTime = v;
                if (double.TryParse(tbSBDelay.Text, out v)) _laneCloseSettings.StartBlockCloseDelay = v;
                if (double.TryParse(tbConfDelay.Text, out v)) _laneCloseSettings.ResultConfirmCloseDelay = v;
                if (double.TryParse(tbFSThresh.Text, out v)) _laneCloseSettings.FalseStartThreshold = v;
                if (double.TryParse(tbSplitDisp.Text, out v)) _laneCloseSettings.SplitDisplayTime = v;
                if (double.TryParse(tbFirstHold.Text, out v)) _laneCloseSettings.FirstPlaceHoldTime = v;
                if (double.TryParse(tbBigPage.Text, out v)) _laneCloseSettings.BigDisplayPageInterval = v;
                _laneCloseSettings.ReactionTimeEnabled = rbRtOn.IsChecked == true;
                _laneCloseSettings.LaneOrder = rbOrderRev.IsChecked == true ? "reverse" : "forward";
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
                SendTimingSettingsToHardware();   // 同步到硬件计时控制器
                AddLog(string.Format("参数更新: 关闭{0}s 出发台{1}s 确认{2}s 抢跳{3}s 分段{4}s 终点:{5} 翻屏{6}s 反应时:{7} 道次:{8}",
                    _laneCloseSettings.LaneCloseTime, _laneCloseSettings.StartBlockCloseDelay,
                    _laneCloseSettings.ResultConfirmCloseDelay, _laneCloseSettings.FalseStartThreshold,
                    _laneCloseSettings.SplitDisplayTime, _laneCloseSettings.FinishPosition == "left" ? "左端" : "右端",
                    _laneCloseSettings.BigDisplayPageInterval, _laneCloseSettings.ReactionTimeEnabled ? "打开" : "关闭",
                    _laneCloseSettings.LaneOrder == "reverse" ? "逆序9→0" : "正序0→9"));
            }
        }

        private void OpenBlindWatchCountDialog() {
            var dlg = new Window {
                Title = "左右盲表数量设置",
                Width = 400, Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock { Text = "左右盲表数量设置", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(new TextBlock {
                Text = "每道使用的盲表数量（1-3）。修改后将同步到三个计时控制台和硬件计时控制器。",
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
            var cbLeft = mkRow("左边 盲表数量", _laneCloseSettings.LeftBlindWatchCount);
            var cbRight = mkRow("右边 盲表数量", _laneCloseSettings.RightBlindWatchCount);

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
            if (dlg.ShowDialog() == true) {
                int newLeft = cbLeft.SelectedIndex + 1;
                int newRight = cbRight.SelectedIndex + 1;
                bool changed = (_laneCloseSettings.LeftBlindWatchCount != newLeft) || (_laneCloseSettings.RightBlindWatchCount != newRight);
                _laneCloseSettings.LeftBlindWatchCount = newLeft;
                _laneCloseSettings.RightBlindWatchCount = newRight;
                if (changed) {
                    SaveTimingSettings();
                    AutoSaveData();
                    UpdateLaneStatusDisplay();
                    Broadcast();                       // 同步到 Web/Remote 控制台
                    SendBlindWatchCountToHardware();   // 0x42 / 0x08 子码
                    AddLog(string.Format("盲表数量更新：左 {0}，右 {1}（已同步到三端与硬件）", newLeft, newRight));
                }
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
                AutoSaveData();
                Broadcast();                         // 同步到 EXE/Web 三端
                SendDeviceStatusesToHardware();      // 同步到硬件计时控制器
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

        // 生成下一个参赛号：若该代表队配置了号码段，则在段内取最小未用号码；
        // 否则回退到全局 (max+1) 逻辑。
        private string GenerateNextBibNumber(string country = null) {
            BibRange range = null;
            if (!string.IsNullOrEmpty(country)) {
                range = _bibRanges.FirstOrDefault(r => r.Country == country && r.Start > 0 && r.End >= r.Start);
            }
            var usedNums = new HashSet<int>();
            foreach (var s in _swimmers) {
                int n;
                if (!string.IsNullOrEmpty(s.BibNumber) && int.TryParse(s.BibNumber, out n)) usedNums.Add(n);
            }
            if (range != null) {
                int width = range.Width > 0 ? range.Width : 3;
                for (int i = range.Start; i <= range.End; i++) {
                    if (!usedNums.Contains(i)) return i.ToString("D" + width);
                }
                AddLog(string.Format("代表队 [{0}] 号码段 {1}-{2} 已用完，使用全局号码。", country, range.Start, range.End));
            }
            int max = 0;
            foreach (int n in usedNums) if (n > max) max = n;
            int defaultWidth = (range != null && range.Width > 0) ? range.Width : 3;
            return (max + 1).ToString("D" + defaultWidth);
        }

        private void AddSwimmer_Click(object sender, RoutedEventArgs e) {
            // 弹出与"修改选中"一致的对话框，要求一次填齐运动员信息
            var sw = new Swimmer {
                BibNumber = GenerateNextBibNumber(),
                Gender = "男",
                CurrentStage = "决赛"
            };
            if (OpenSwimmerEditor(sw, isNew: true)) {
                _swimmers.Add(sw);
                AutoSaveData();
                RefreshOverviewStats();
                RefreshSwimmerFilter();
                Broadcast();
                AddLog(string.Format("新增运动员: {0}({1}) {2}", sw.Name, sw.BibNumber, sw.EventName));
            }
        }

        private void DeleteSwimmer_Click(object sender, RoutedEventArgs e) {
            // 先从 DataGrid 拿多选；如果为空再退回 SelectedItem（兼容单行场景）
            var toDelete = new List<Swimmer>();
            if (SwimmerGrid.SelectedItems != null) {
                foreach (var item in SwimmerGrid.SelectedItems) {
                    var sw = item as Swimmer;
                    if (sw != null && !toDelete.Contains(sw)) toDelete.Add(sw);
                }
            }
            if (toDelete.Count == 0) {
                var single = SwimmerGrid.SelectedItem as Swimmer;
                if (single != null) toDelete.Add(single);
            }
            if (toDelete.Count == 0) {
                MessageBox.Show("请先在列表里选中要删除的行（可按住 Ctrl/Shift 多选）。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string confirmMsg;
            if (toDelete.Count == 1) {
                var s = toDelete[0];
                confirmMsg = string.Format("确定删除运动员 {0}({1}) 的 {2} 报名记录？", s.Name, s.BibNumber, s.EventName);
            } else {
                var preview = string.Join("\n", toDelete.Take(8).Select(s => string.Format("· {0}({1}) {2}", s.Name, s.BibNumber, s.EventName)));
                if (toDelete.Count > 8) preview += string.Format("\n... 及其它 {0} 条", toDelete.Count - 8);
                confirmMsg = string.Format("确定删除以下 {0} 条报名记录？\n\n{1}", toDelete.Count, preview);
            }
            if (MessageBox.Show(confirmMsg, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            int removed = 0, notFound = 0;
            foreach (var sw in toDelete) {
                if (_swimmers.Remove(sw)) { removed++; continue; }
                // 兜底：按 (BibNumber + Name + EventName) 查找同一条记录再删（避免引用不对等的极端情况）
                var alt = _swimmers.FirstOrDefault(s => s.BibNumber == sw.BibNumber && s.Name == sw.Name && s.EventName == sw.EventName);
                if (alt != null && _swimmers.Remove(alt)) { removed++; AddLog(string.Format("按号码兜底删除: {0}({1}) {2}", alt.Name, alt.BibNumber, alt.EventName)); }
                else { notFound++; }
            }

            AutoSaveData();
            RefreshOverviewStats();
            RefreshSwimmerFilter();
            Broadcast();
            AddLog(string.Format("已删除运动员 {0} 条{1}", removed, notFound > 0 ? string.Format("（{0} 条未在列表中找到）", notFound) : ""));
            if (notFound > 0) {
                MessageBox.Show(string.Format("已删除 {0} 条。另有 {1} 条未在列表中找到，请检查是否被其他筛选/编辑中的操作修改。",
                    removed, notFound), "删除结果", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SwimmerGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            if (e.Key == System.Windows.Input.Key.Delete) {
                e.Handled = true;
                DeleteSwimmer_Click(sender, null);
            }
        }

        private void EditSwimmer_Click(object sender, RoutedEventArgs e) {
            var selected = SwimmerGrid.SelectedItem as Swimmer;
            if (selected == null) { MessageBox.Show("请先选中要修改的运动员"); return; }
            if (OpenSwimmerEditor(selected, isNew: false)) {
                RefreshSwimmerFilter();
                AutoSaveData();
                Broadcast();
                AddLog(string.Format("已修改运动员: {0}({1}) {2}", selected.Name, selected.BibNumber, selected.EventName));
            }
        }

        // 新增 / 修改运动员共用对话框。参数 target 是将被写入的 Swimmer 实例。
        // 返回 true 表示用户确认了修改。
        private bool OpenSwimmerEditor(Swimmer target, bool isNew) {
            var dlg = new Window {
                Title = isNew ? "新增运动员" : string.Format("修改运动员信息 — {0}({1})", target.Name, target.BibNumber),
                Width = 520, Height = 540,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this, ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(20) };

            // 参赛号
            var bibRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            bibRow.Children.Add(new TextBlock { Text = "参赛号:", Width = 60, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
            var tbBib = new TextBox { Text = target.BibNumber ?? "", Width = 150, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
            bibRow.Children.Add(tbBib);
            bibRow.Children.Add(new TextBlock {
                Text = isNew ? "（可手动修改；默认已按代表队号码段取号）" : "（可手动修改；更改后将同步到该运动员的所有项目记录）",
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12 });
            sp.Children.Add(bibRow);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 8; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            // Row 0: 姓名 + 性别
            var tbName = AddEditField(grid, 0, 0, "姓名:", target.Name);
            var cbGender = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
            cbGender.Items.Add("男"); cbGender.Items.Add("女"); cbGender.Items.Add("混合");
            cbGender.SelectedItem = target.Gender ?? "男";
            AddEditLabel(grid, 0, 2, "性别:");
            Grid.SetRow(cbGender, 0); Grid.SetColumn(cbGender, 3);
            grid.Children.Add(cbGender);

            // Row 1: 出生日期 + 年龄
            var tbBirth = AddEditField(grid, 1, 0, "出生日期:", target.BirthDate);
            var tbAge = AddEditField(grid, 1, 2, "年龄:", isNew && target.Age == 0 ? "" : target.Age.ToString());

            // Row 2: 身份证号 + 代表队
            var tbID = AddEditField(grid, 2, 0, "身份证号:", target.IDNumber);
            var tbCountry = AddEditField(grid, 2, 2, "代表队:", target.Country);

            // Row 3: 电话 + 协会注册号
            var tbPhone = AddEditField(grid, 3, 0, "联系电话:", target.Phone);
            var tbCSA = AddEditField(grid, 3, 2, "协会注册号:", target.CSANumber);


            // Row 4: 项目（下拉） + 报名成绩
            AddEditLabel(grid, 4, 0, "项目:");
            var cbEvent = new ComboBox { VerticalAlignment = VerticalAlignment.Center, IsEditable = true };
            foreach (var ev in _events) cbEvent.Items.Add(ev);
            if (!string.IsNullOrEmpty(target.EventName)) {
                if (!cbEvent.Items.Contains(target.EventName)) cbEvent.Items.Add(target.EventName);
                cbEvent.SelectedItem = target.EventName;
            } else if (cbEvent.Items.Count > 0) {
                cbEvent.SelectedIndex = 0;
            }
            Grid.SetRow(cbEvent, 4); Grid.SetColumn(cbEvent, 1);
            grid.Children.Add(cbEvent);
            var tbEntry = AddEditField(grid, 4, 2, "报名成绩:", target.EntryTime);

            // Row 5: 赛次（下拉，手动）+ 组别（下拉，选自组别）
            AddEditLabel(grid, 5, 0, "赛次:");
            var cbStage = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
            cbStage.Items.Add("决赛"); cbStage.Items.Add("预赛"); cbStage.Items.Add("半决赛");
            cbStage.SelectedItem = string.IsNullOrEmpty(target.CurrentStage) ? "决赛" : target.CurrentStage;
            Grid.SetRow(cbStage, 5); Grid.SetColumn(cbStage, 1);
            grid.Children.Add(cbStage);
            AddEditLabel(grid, 5, 2, "组别:");
            var cbGroup = new ComboBox { VerticalAlignment = VerticalAlignment.Center, IsEditable = true };
            cbGroup.Items.Add("");   // 空 = 不分组
            foreach (var g in _ageGroups) cbGroup.Items.Add(g.Name);
            // 若运动员当前 AgeCategory 不在列表里（手动输入），加入
            if (!string.IsNullOrEmpty(target.AgeCategory) && !cbGroup.Items.Contains(target.AgeCategory))
                cbGroup.Items.Add(target.AgeCategory);
            cbGroup.SelectedItem = target.AgeCategory ?? "";
            Grid.SetRow(cbGroup, 5); Grid.SetColumn(cbGroup, 3);
            grid.Children.Add(cbGroup);

            // Row 6: 单位简称（左列整行跨到右）
            var tbCountryShort = AddEditField(grid, 6, 0, "单位简称:", target.CountryShort);

            // Row 7: 备注
            var tbNotes = AddEditField(grid, 7, 0, "备注:", target.Notes);

            sp.Children.Add(grid);

            // 按钮
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOk = new Button {
                Content = isNew ? "确认添加" : "确认修改",
                Padding = new Thickness(20, 6, 20, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
                Foreground = Brushes.White, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0)
            };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);
            sp.Children.Add(btnPanel);

            dlg.Content = sp;
            if (dlg.ShowDialog() != true) return false;

            // 参赛号校验
            string oldBib = target.BibNumber ?? "";
            string newBib = (tbBib.Text ?? "").Trim();
            if (string.IsNullOrEmpty(newBib)) { MessageBox.Show("参赛号不能为空"); return false; }

            if (isNew) {
                // 新增时直接查重
                if (_swimmers.Any(s => s.BibNumber == newBib)) {
                    MessageBox.Show(string.Format("参赛号 {0} 已存在，请换一个号码。", newBib));
                    return false;
                }
            } else if (newBib != oldBib) {
                var others = _swimmers.Where(s => s != target && s.BibNumber == newBib).ToList();
                if (others.Any(s => s.Name != target.Name ||
                    (!string.IsNullOrEmpty(s.IDNumber) && !string.IsNullOrEmpty(target.IDNumber) && s.IDNumber != target.IDNumber))) {
                    MessageBox.Show(string.Format("参赛号 {0} 已被其他运动员使用，请换一个号码。", newBib));
                    return false;
                }
            }

            target.Name = (tbName.Text ?? "").Trim();
            target.Gender = cbGender.SelectedItem != null ? cbGender.SelectedItem.ToString() : "男";
            target.BirthDate = (tbBirth.Text ?? "").Trim();
            int ageVal; if (int.TryParse((tbAge.Text ?? "").Trim(), out ageVal)) target.Age = ageVal;
            target.IDNumber = (tbID.Text ?? "").Trim();
            target.Country = (tbCountry.Text ?? "").Trim();
            target.CountryShort = (tbCountryShort.Text ?? "").Trim();
            target.Phone = (tbPhone.Text ?? "").Trim();
            target.CSANumber = (tbCSA.Text ?? "").Trim();
            target.EventName = cbEvent.SelectedItem != null ? cbEvent.SelectedItem.ToString().Trim() : ((cbEvent.Text ?? "").Trim());
            target.EntryTime = (tbEntry.Text ?? "").Trim();
            target.EntryTimeSeconds = TimeFormatter.Parse(target.EntryTime);
            target.CurrentStage = cbStage.SelectedItem != null ? cbStage.SelectedItem.ToString() : "决赛";
            // "组别" 手动覆盖 AgeCategory；空串表示不指定
            string manualGroup = cbGroup.SelectedItem != null ? cbGroup.SelectedItem.ToString() : ((cbGroup.Text ?? "").Trim());
            if (!string.IsNullOrEmpty(manualGroup)) target.AgeCategory = manualGroup;
            target.Notes = (tbNotes.Text ?? "").Trim();

            // 号码变更：级联更新同一人的其它项目记录 + 接力棒次
            if (!isNew && newBib != oldBib) {
                foreach (var s in _swimmers) { if (s.BibNumber == oldBib) s.BibNumber = newBib; }
                if (_relayTeams != null) {
                    foreach (var team in _relayTeams)
                        foreach (var leg in team.Legs)
                            if (!string.IsNullOrEmpty(leg.SwimmerBibNumber) && leg.SwimmerBibNumber == oldBib)
                                leg.SwimmerBibNumber = newBib;
                }
                AddLog(string.Format("参赛号变更: {0} → {1}（{2}）", oldBib, newBib, target.Name));
            } else {
                target.BibNumber = newBib;
            }

            // 同步同一参赛号其它项目记录（仅修改流程；新增时此刻 target 还未在 _swimmers 里，不做同步）
            if (!isNew) {
                foreach (var s in _swimmers) {
                    if (s != target && s.BibNumber == target.BibNumber) {
                        s.Name = target.Name;
                        s.Gender = target.Gender;
                        s.BirthDate = target.BirthDate;
                        s.Age = target.Age;
                        s.IDNumber = target.IDNumber;
                        s.Country = target.Country;
                        s.CountryShort = target.CountryShort;
                        s.Phone = target.Phone;
                        s.CSANumber = target.CSANumber;
                    }
                }
            }
            return true;
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

        // 按代表队配置号码段（如 中国 001-050）
        private void BibRangeConfig_Click(object sender, RoutedEventArgs e) {
            // 汇总候选代表队列表：已有 BibRanges 中的 + 已注册运动员中出现的
            var countries = new List<string>();
            foreach (var r in _bibRanges) if (!countries.Contains(r.Country) && !string.IsNullOrEmpty(r.Country)) countries.Add(r.Country);
            foreach (var s in _swimmers) if (!string.IsNullOrEmpty(s.Country) && !countries.Contains(s.Country)) countries.Add(s.Country);

            // 编辑副本，取消时不保存
            var working = new System.Collections.ObjectModel.ObservableCollection<BibRange>();
            foreach (var c in countries) {
                var existing = _bibRanges.FirstOrDefault(r => r.Country == c);
                if (existing != null) working.Add(new BibRange { Country = existing.Country, Start = existing.Start, End = existing.End, Width = existing.Width > 0 ? existing.Width : 3 });
                else working.Add(new BibRange { Country = c, Start = 0, End = 0, Width = 3 });
            }

            var dlg = new Window {
                Title = "号码区间设置（按代表队）",
                Width = 600, Height = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };
            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            mainGrid.Children.Add(new TextBlock {
                Text = "为每个代表队设置参赛号的数字区间。Width 为补零宽度（3=001/4=0001）。起/止为 0 视为未配置，使用全局 +1 逻辑。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"))
            });

            var grid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "代表队", Binding = new System.Windows.Data.Binding("Country") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(180) });
            grid.Columns.Add(new DataGridTextColumn { Header = "起始号", Binding = new System.Windows.Data.Binding("Start") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = new DataGridLength(80) });
            grid.Columns.Add(new DataGridTextColumn { Header = "结束号", Binding = new System.Windows.Data.Binding("End") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = new DataGridLength(80) });
            grid.Columns.Add(new DataGridTextColumn { Header = "补零位数", Binding = new System.Windows.Data.Binding("Width") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = new DataGridLength(80) });
            // 动态计算使用状态
            var usageCol = new DataGridTextColumn { Header = "已用/区间", Width = new DataGridLength(120), IsReadOnly = true };
            usageCol.Binding = new System.Windows.Data.Binding(".") { Converter = new BibRangeUsageConverter(_swimmers) };
            grid.Columns.Add(usageCol);
            grid.ItemsSource = working;
            Grid.SetRow(grid, 1);
            mainGrid.Children.Add(grid);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(btnPanel, 2);
            var btnAdd = new Button { Content = "新增行", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnAdd.Click += delegate { working.Add(new BibRange { Country = "", Start = 0, End = 0, Width = 3 }); };
            var btnDel = new Button { Content = "删除选中", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnDel.Click += delegate {
                var sel = grid.SelectedItem as BibRange;
                if (sel != null) working.Remove(sel);
            };
            var btnOk = new Button { Content = "保存", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate {
                // 提交正在编辑的单元格
                var fe = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
                if (fe is TextBox) {
                    var be = fe.GetBindingExpression(TextBox.TextProperty);
                    if (be != null) be.UpdateSource();
                }
                try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

                // 校验 & 检测区间冲突
                var finalList = new List<BibRange>();
                var seenCountries = new HashSet<string>();
                foreach (var r in working) {
                    if (string.IsNullOrWhiteSpace(r.Country)) continue;
                    r.Country = r.Country.Trim();
                    if (r.Width <= 0) r.Width = 3;
                    if (seenCountries.Contains(r.Country)) {
                        MessageBox.Show(string.Format("代表队 \"{0}\" 重复，请合并或删除重复行。", r.Country), "提示"); return;
                    }
                    seenCountries.Add(r.Country);
                    if (r.Start != 0 || r.End != 0) {
                        if (r.Start <= 0 || r.End < r.Start) {
                            MessageBox.Show(string.Format("代表队 [{0}] 的起始/结束号不合法（要求 起始 > 0 且 结束 ≥ 起始）。", r.Country), "提示"); return;
                        }
                    }
                    finalList.Add(r);
                }
                // 检查区间重叠
                for (int i = 0; i < finalList.Count; i++) {
                    for (int j = i + 1; j < finalList.Count; j++) {
                        var a = finalList[i]; var b = finalList[j];
                        if (a.Start <= 0 || b.Start <= 0) continue;
                        if (a.End >= b.Start && b.End >= a.Start) {
                            MessageBox.Show(string.Format("[{0}] 的号码段 {1}-{2} 与 [{3}] 的 {4}-{5} 重叠，请调整。", a.Country, a.Start, a.End, b.Country, b.Start, b.End), "提示"); return;
                        }
                    }
                }
                _bibRanges = finalList;
                AutoSaveData();
                Broadcast();
                AddLog(string.Format("已更新代表队号码段配置（{0} 条）", _bibRanges.Count));
                dlg.DialogResult = true;
            };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnAdd);
            btnPanel.Children.Add(btnDel);
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            mainGrid.Children.Add(btnPanel);

            dlg.Content = mainGrid;
            dlg.ShowDialog();
        }

        // 把完整的 BibRange 行渲染成 "x/区间大小" 已用统计
        private class BibRangeUsageConverter : System.Windows.Data.IValueConverter
        {
            private readonly System.Collections.ObjectModel.ObservableCollection<Swimmer> _list;
            public BibRangeUsageConverter(System.Collections.ObjectModel.ObservableCollection<Swimmer> list) { _list = list; }
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
                var r = value as BibRange; if (r == null) return "";
                if (r.Start <= 0 || r.End < r.Start) return "未配置";
                int total = r.End - r.Start + 1;
                int used = 0;
                foreach (var s in _list) {
                    int n;
                    if (s != null && s.Country == r.Country && !string.IsNullOrEmpty(s.BibNumber) && int.TryParse(s.BibNumber, out n)) {
                        if (n >= r.Start && n <= r.End) used++;
                    }
                }
                return string.Format("{0}/{1}", used, total);
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) { throw new NotImplementedException(); }
        }

        // ─── 运动员报名 CSV 表头（导出/导入/模板共用） ───────────────────
        // 与 ImportCSV_Click 兼容：前 11 列保持原顺序，组别/单位简称作为附加列追加在后
        // 顺序: 号码,姓名,性别,代表队,项目,报名成绩,年龄,出生日期,身份证号,电话,备注,组别,单位简称
        private static readonly string[] SwimmerCsvHeaders = new[] {
            "号码","姓名","性别","代表队","项目","报名成绩","年龄","出生日期","身份证号","电话","备注","组别","单位简称"
        };

        private void ExportSwimmersCSV_Click(object sender, RoutedEventArgs e) {
            // 导出当前所有已报名运动员（含接力代表条目和接力队员个人条目）
            var rows = _swimmers.ToList();
            if (rows.Count == 0) {
                MessageBox.Show("当前没有可导出的报名数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string defaultName = string.IsNullOrEmpty(_competitionName)
                ? "运动员报名表.csv"
                : (_competitionName + "_运动员报名表.csv");
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV文件|*.csv",
                Title = "导出运动员报名数据",
                FileName = defaultName
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');   // UTF-8 BOM，兼容 Excel/WPS 直接打开
                sb.AppendLine(string.Join(",", SwimmerCsvHeaders));
                foreach (var s in rows) {
                    sb.AppendLine(string.Join(",", new[] {
                        CsvEscape(s.BibNumber ?? ""),
                        CsvEscape(s.Name ?? ""),
                        CsvEscape(s.Gender ?? ""),
                        CsvEscape(s.Country ?? ""),
                        CsvEscape(s.EventName ?? ""),
                        CsvEscape(s.EntryTime ?? ""),
                        CsvEscape(s.Age > 0 ? s.Age.ToString() : ""),
                        CsvEscape(s.BirthDate ?? ""),
                        CsvEscape(s.IDNumber ?? ""),
                        CsvEscape(s.Phone ?? ""),
                        CsvEscape(s.Notes ?? ""),
                        CsvEscape(s.AgeCategory ?? ""),
                        CsvEscape(s.CountryShort ?? "")
                    }));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                AddLog(string.Format("已导出运动员报名数据 {0} 条", rows.Count));
                MessageBox.Show(string.Format("已导出 {0} 条运动员报名数据：\n{1}", rows.Count, dlg.FileName), "完成");
            } catch (Exception ex) {
                MessageBox.Show("导出失败: " + ex.Message, "错误");
            }
        }

        private void DownloadSwimmersTemplateCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV文件|*.csv",
                Title = "保存运动员报名模板",
                FileName = "运动员报名模板.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');   // UTF-8 BOM
                sb.AppendLine(string.Join(",", SwimmerCsvHeaders));
                // 两条示例行（Excel/WPS 打开即看到格式说明）
                sb.AppendLine(string.Join(",", new[] {
                    "001","张三","男","北京队","100米自由泳","52.30","18","2007-05-12","110101200705120011","13800000001","","青年",""
                }));
                sb.AppendLine(string.Join(",", new[] {
                    "002","李四","女","上海队","50米蛙泳","34.85","22","2003-08-21","310101200308210022","13800000002","","成年","沪"
                }));
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("运动员报名模板已保存。\n\n用 Excel/WPS 打开编辑后，可用『导入CSV』按钮回灌。\n（号码列留空时，导入时由系统自动按号码段分配）", "完成");
            } catch (Exception ex) {
                MessageBox.Show("保存失败: " + ex.Message, "错误");
            }
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
                        if (string.IsNullOrEmpty(bibNum)) bibNum = GenerateNextBibNumber(cols[3].Trim());
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
                        if (cols.Length > 11) sw.AgeCategory = cols[11].Trim();
                        if (cols.Length > 12) sw.CountryShort = cols[12].Trim();
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
                    RefreshSwimmerFilter();
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
            var legBirthPickers = new List<DatePicker>();

            Action rebuildLegs = delegate {
                legsPanel.Children.Clear();
                legNameBoxes.Clear();
                legBibBoxes.Clear();
                legBirthPickers.Clear();
                string evName = cbEvent.SelectedItem != null ? cbEvent.SelectedItem.ToString() : "";
                int legCount = 4;
                var mm = System.Text.RegularExpressions.Regex.Match(evName, @"(\d+)\s*[x×*]");
                if (mm.Success) { int n; if (int.TryParse(mm.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n; }
                legsTitle.Text = string.Format("队员（共{0}棒，可留空，后续再补）:", legCount);
                for (int i = 0; i < legCount; i++) {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                    row.Children.Add(new TextBlock { Text = string.Format("第{0}棒:", i + 1), Width = 55, VerticalAlignment = VerticalAlignment.Center });
                    var tbLegName = new TextBox { Width = 130, Padding = new Thickness(4) };
                    var dpLegBirth = new DatePicker { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
                    dpLegBirth.SetValue(System.Windows.FrameworkElement.LanguageProperty, System.Windows.Markup.XmlLanguage.GetLanguage("en-CA"));
                    dpLegBirth.SelectedDateFormat = DatePickerFormat.Short;
                    var tbLegBib = new TextBox { Width = 90, Padding = new Thickness(4), Margin = new Thickness(8, 0, 0, 0), ToolTip = "队员号码（可空）" };
                    row.Children.Add(new TextBlock { Text = "姓名", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(tbLegName);
                    row.Children.Add(new TextBlock { Text = "出生日期", Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(dpLegBirth);
                    row.Children.Add(new TextBlock { Text = "号码", Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(tbLegBib);
                    legsPanel.Children.Add(row);
                    legNameBoxes.Add(tbLegName);
                    legBibBoxes.Add(tbLegBib);
                    legBirthPickers.Add(dpLegBirth);
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
                string lbd = legBirthPickers[i].SelectedDate.HasValue
                    ? legBirthPickers[i].SelectedDate.Value.ToString("yyyy-MM-dd") : "";
                team.Legs.Add(new RelayLeg {
                    LegOrder = i + 1,
                    SwimmerName = ln,
                    SwimmerBibNumber = lb,
                    SwimmerIDNumber = "",
                    SwimmerBirthDate = lbd
                });
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
                    BirthDate = leg.SwimmerBirthDate ?? "",
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
            string bibNumber = GenerateNextBibNumber(RegCountryBox != null ? RegCountryBox.Text.Trim() : "");

            string birthDate = RegBirthDatePicker.SelectedDate.HasValue ? RegBirthDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") : "";
            int age = 0;
            if (RegBirthDatePicker.SelectedDate.HasValue) {
                var today = DateTime.Today;
                var bd = RegBirthDatePicker.SelectedDate.Value;
                age = today.Year - bd.Year;
                if (bd.Date > today.AddYears(-age)) age--;
            }

            // 报名时直接选择组别；留空则沿用按年龄自动推断
            string regGroup = RegGroupCombo != null
                ? (RegGroupCombo.SelectedItem != null
                    ? RegGroupCombo.SelectedItem.ToString()
                    : (RegGroupCombo.Text ?? "").Trim())
                : "";
            string regCountryShort = RegCountryShortBox != null ? RegCountryShortBox.Text.Trim() : "";

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
                    CountryShort = regCountryShort,
                    IDNumber = regIdNumber,
                    Phone = RegPhoneBox.Text.Trim(),
                    CSANumber = RegCSABox != null ? RegCSABox.Text.Trim() : "",
                    EventName = ev.Item1,
                    EntryTime = ev.Item2,
                    BirthDate = birthDate,
                    Age = age,
                    Notes = RegNotesBox.Text.Trim()
                };
                sw.EntryTimeSeconds = TimeFormatter.Parse(sw.EntryTime);
                // 手动组别优先于 AgeGroupRegistry 自动推断
                if (!string.IsNullOrEmpty(regGroup)) sw.AgeCategory = regGroup;
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
            if (RegCountryShortBox != null) RegCountryShortBox.Clear();
            if (RegGroupCombo != null) RegGroupCombo.SelectedItem = null;
            RegPhoneBox.Clear();
            if (RegCSABox != null) RegCSABox.Clear();
            RegNotesBox.Clear();
            RegBirthDatePicker.SelectedDate = null;
            RegEntryTimeBox.Clear();
            _regEventList.Clear();
            RefreshRegEventList();

            AutoSaveData();
            RefreshOverviewStats();
            RefreshSwimmerFilter();
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
                string ageGroup = EditAgeGroupCombo != null && EditAgeGroupCombo.SelectedItem != null ? EditAgeGroupCombo.SelectedItem.ToString() : "";
                string gender = EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "男";

                // 按当前性别+组别过滤项目列表（排除接力队员个人条目）
                string prevEvent = EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
                EditEventCombo.Items.Clear();
                var evSet = new HashSet<string>();
                foreach (var s in _swimmers) {
                    if (string.IsNullOrEmpty(s.EventName) || s.Gender != gender) continue;
                    if (!MatchesAgeGroup(s, ageGroup)) continue;
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
                    if (!MatchesAgeGroup(s, ageGroup)) continue;
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
            // 重建项目下拉框（按组别+性别过滤，保留当前选择）
            string ageGroup = EditAgeGroupCombo != null && EditAgeGroupCombo.SelectedItem != null ? EditAgeGroupCombo.SelectedItem.ToString() : "";
            string gender = EditGenderCombo != null && EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "男";
            int prevIndex = EditEventCombo.SelectedIndex;
            EditEventCombo.Items.Clear();
            var evSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (string.IsNullOrEmpty(s.EventName) || s.Gender != gender) continue;
                if (!MatchesAgeGroup(s, ageGroup)) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                evSet.Add(s.EventName);
            }
            foreach (string ev in evSet.OrderBy(x => x)) EditEventCombo.Items.Add(ev);
            if (prevIndex >= 0 && prevIndex < EditEventCombo.Items.Count)
                EditEventCombo.SelectedIndex = prevIndex;
            else if (EditEventCombo.Items.Count > 0)
                EditEventCombo.SelectedIndex = 0;
        }

        // 填充"出场编排微调"里的组别下拉（首项=全部），当组别列表变化时调用
        private void RefreshEditAgeGroupCombo() {
            if (EditAgeGroupCombo == null) return;
            string prev = EditAgeGroupCombo.SelectedItem as string;
            EditAgeGroupCombo.Items.Clear();
            EditAgeGroupCombo.Items.Add("全部");
            foreach (var g in _ageGroups) EditAgeGroupCombo.Items.Add(g.Name);
            if (!string.IsNullOrEmpty(prev) && EditAgeGroupCombo.Items.Contains(prev))
                EditAgeGroupCombo.SelectedItem = prev;
            else EditAgeGroupCombo.SelectedIndex = 0;
        }

        // 其它组别过滤下拉（运动员管理/记录管理/成绩与排名）
        private void FillAgeGroupFilterCombo(ComboBox cb) {
            if (cb == null) return;
            string prev = cb.SelectedItem as string;
            cb.Items.Clear();
            cb.Items.Add("全部");
            // 主列表 _ageGroups + 已报运动员里出现的组别（兼容历史导入数据中存在但未维护到主列表的组别）
            var seen = new HashSet<string>();
            foreach (var g in _ageGroups) {
                if (!string.IsNullOrEmpty(g.Name) && seen.Add(g.Name)) cb.Items.Add(g.Name);
            }
            foreach (var s in _swimmers) {
                string ag = s.AgeCategory;
                if (!string.IsNullOrEmpty(ag) && seen.Add(ag)) cb.Items.Add(ag);
            }
            if (!string.IsNullOrEmpty(prev) && cb.Items.Contains(prev)) cb.SelectedItem = prev;
            else cb.SelectedIndex = 0;
        }

        private void RefreshAllAgeGroupFilterCombos() {
            RefreshEditAgeGroupCombo();
            FillAgeGroupFilterCombo(FilterAgeGroupCombo);
            FillAgeGroupFilterCombo(RecordFilterAgeGroup);
            FillAgeGroupFilterCombo(ResultAgeGroupCombo);
            // 右侧注册面板的组别下拉（只列具体组别，不带"全部"）
            if (RegGroupCombo != null) {
                string prev = RegGroupCombo.SelectedItem as string ?? RegGroupCombo.Text;
                RegGroupCombo.Items.Clear();
                foreach (var g in _ageGroups) RegGroupCombo.Items.Add(g.Name);
                if (!string.IsNullOrEmpty(prev) && RegGroupCombo.Items.Contains(prev))
                    RegGroupCombo.SelectedItem = prev;
            }
        }

        // 比赛参数设置管理（组别/项目/性别/赛次/组数 等）保存后统一调用：
        // 同步刷新本机所有依赖下拉，并通过 Broadcast 把新配置推送给所有网页/远程端
        private void NotifyMetadataChanged() {
            // 本机各 WPF 下拉
            try { RefreshEventComboBoxes(); } catch { }
            try { RefreshAllAgeGroupFilterCombos(); } catch { }
            try { RefillGenderCombos(); } catch { }
            try { RefillStageCombos(); } catch { }
            // 推送到所有网页/远程端
            try { Broadcast(); } catch { }
        }

        private void RefillGenderCombos() {
            ComboBox[] genderCombos = new ComboBox[] { FilterGenderCombo, EditGenderCombo, ResultGenderCombo, RegGenderCombo, RecordFilterGender };
            foreach (var cb in genderCombos) {
                if (cb == null) continue;
                string prev = "";
                var sel = cb.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Content != null) prev = sel.Content.ToString();
                else if (cb.SelectedItem is string) prev = (string)cb.SelectedItem;
                bool hasAll = false;
                foreach (var it in cb.Items) {
                    var ci = it as ComboBoxItem;
                    if (ci != null && ci.Content != null && ci.Content.ToString() == "全部") { hasAll = true; break; }
                }
                cb.Items.Clear();
                if (hasAll) cb.Items.Add(new ComboBoxItem { Content = "全部" });
                foreach (var g in _genders) cb.Items.Add(new ComboBoxItem { Content = g });
                int restored = -1;
                for (int i = 0; i < cb.Items.Count; i++) {
                    var ci = cb.Items[i] as ComboBoxItem;
                    if (ci != null && ci.Content != null && ci.Content.ToString() == prev) { restored = i; break; }
                }
                cb.SelectedIndex = restored >= 0 ? restored : 0;
            }
        }

        private void RefillStageCombos() {
            ComboBox[] stageCombos = new ComboBox[] { EditStageCombo, ResultStageCombo };
            foreach (var cb in stageCombos) {
                if (cb == null) continue;
                string prev = "";
                var sel = cb.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Content != null) prev = sel.Content.ToString();
                cb.Items.Clear();
                foreach (var s in _stages) cb.Items.Add(new ComboBoxItem { Content = s });
                int restored = -1;
                for (int i = 0; i < cb.Items.Count; i++) {
                    var ci = cb.Items[i] as ComboBoxItem;
                    if (ci != null && ci.Content != null && ci.Content.ToString() == prev) { restored = i; break; }
                }
                cb.SelectedIndex = restored >= 0 ? restored : 0;
            }
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
            string ageGroup = EditAgeGroupCombo != null && EditAgeGroupCombo.SelectedItem != null ? EditAgeGroupCombo.SelectedItem.ToString() : "";
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
                if (!MatchesAgeGroup(s, ageGroup)) continue;
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
                    grid.Columns.Add(new DataGridTextColumn { Header = "组别", Binding = new System.Windows.Data.Binding("AgeCategory"), Width = new DataGridLength(60) });
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
                grid.Columns.Add(new DataGridTextColumn { Header = "组别", Binding = new System.Windows.Data.Binding("AgeCategory"), Width = new DataGridLength(60) });
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
            string ageGroup = EditAgeGroupCombo != null && EditAgeGroupCombo.SelectedItem != null ? EditAgeGroupCombo.SelectedItem.ToString() : "";
            string gender = EditGenderCombo.SelectedItem != null ? ((ComboBoxItem)EditGenderCombo.SelectedItem).Content.ToString() : "";
            string eventName = EditEventCombo.SelectedItem != null ? EditEventCombo.SelectedItem.ToString() : "";
            string stage = EditStageCombo.SelectedItem != null ? ((ComboBoxItem)EditStageCombo.SelectedItem).Content.ToString() : "";
            bool isRelay = eventName.Contains("接力");

            var sw1 = _swimmers.FirstOrDefault(s => s.BibNumber == bib);
            if (sw1 == null) return;
            var sa1 = sw1.GetAssignmentForStage(stage);
            int heat1 = sa1 != null ? sa1.Heat : sw1.Heat;
            int lane1 = sa1 != null ? sa1.Lane : sw1.Lane;

            // 候选目标 = 同项目/同赛次的其它运动员（可交换） + 空道（可移动），按组别过滤
            // 使用 Tuple<Swimmer, int, int>：Swimmer==null 表示空道；Heat, Lane 为目标组/道
            var candidates = new List<Tuple<Swimmer, int, int>>();
            var occupiedHeatLane = new HashSet<string>(); // "heat|lane" 占用记录（用于计算空道）
            var heatSet = new HashSet<int>();             // 该项目该赛次出现过的组号

            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (!MatchesAgeGroup(s, ageGroup)) continue;
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
            string ageGroup = EditAgeGroupCombo != null && EditAgeGroupCombo.SelectedItem != null ? EditAgeGroupCombo.SelectedItem.ToString() : "";
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

            // 查找未分组的运动员（同组别同性别同项目同赛次，Heat=0或无StageAssignment）
            var unassigned = new List<Swimmer>();
            foreach (var s in _swimmers) {
                if (s.Gender != gender || s.EventName != eventName) continue;
                if (!MatchesAgeGroup(s, ageGroup)) continue;
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
            string ageGroup = EditAgeGroupCombo != null && EditAgeGroupCombo.SelectedItem != null ? EditAgeGroupCombo.SelectedItem.ToString() : "";
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

            // 查找运动员列表（同RefreshEditPreview逻辑，接力项目只看代表队；按组别过滤）
            bool isRelaySwap = eventName.Contains("接力");
            var matchedSwimmers = new List<Tuple<Swimmer, int, int>>();
            foreach (var s in _swimmers) {
                if (!MatchesAgeGroup(s, ageGroup)) continue;
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
        private void FilterAgeGroup_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) RefreshSwimmerFilter(); }
        private void FilterText_Changed(object sender, TextChangedEventArgs e) { if (_initialized) RefreshSwimmerFilter(); }

        private void FilterReset_Click(object sender, RoutedEventArgs e) {
            if (FilterEventCombo != null && FilterEventCombo.Items.Count > 0) FilterEventCombo.SelectedIndex = 0;
            if (FilterAgeGroupCombo != null && FilterAgeGroupCombo.Items.Count > 0) FilterAgeGroupCombo.SelectedIndex = 0;
            if (FilterGenderCombo != null && FilterGenderCombo.Items.Count > 0) FilterGenderCombo.SelectedIndex = 0;
            if (FilterNameBox != null) FilterNameBox.Text = "";
            if (FilterBibBox != null) FilterBibBox.Text = "";
            RefreshSwimmerFilter();
        }

        private void RefreshSwimmerFilter() {
            if (FilterEventCombo == null || FilterGenderCombo == null || SwimmerGrid == null) return;
            string eventFilter = FilterEventCombo.SelectedItem != null ? ((ComboBoxItem)FilterEventCombo.SelectedItem).Content.ToString() : "全部";
            string genderFilter = FilterGenderCombo.SelectedItem != null ? ((ComboBoxItem)FilterGenderCombo.SelectedItem).Content.ToString() : "全部";
            string ageFilter = FilterAgeGroupCombo != null && FilterAgeGroupCombo.SelectedItem != null ? FilterAgeGroupCombo.SelectedItem.ToString() : "全部";
            string nameFilter = FilterNameBox != null ? (FilterNameBox.Text ?? "").Trim() : "";
            string bibFilter = FilterBibBox != null ? (FilterBibBox.Text ?? "").Trim() : "";

            // 接力队员个人条目（Notes 以"接力队员"开头）在 接力队管理 里展示，运动员管理里隐藏
            var visibleSwimmers = _swimmers.Where(s => !(s.Notes != null && s.Notes.StartsWith("接力队员"))).ToList();

            // 全部筛选条件都为"默认值"时直接展示（仅去掉接力队员条目）
            bool allDefault = eventFilter == "全部" && genderFilter == "全部"
                           && (ageFilter == "全部" || string.IsNullOrEmpty(ageFilter))
                           && string.IsNullOrEmpty(nameFilter) && string.IsNullOrEmpty(bibFilter);
            if (allDefault) {
                SwimmerGrid.ItemsSource = visibleSwimmers;
                return;
            }

            var filtered = visibleSwimmers.Where(s => {
                if (eventFilter != "全部" && s.EventName != eventFilter) return false;
                if (genderFilter != "全部" && s.Gender != genderFilter) return false;
                if (!MatchesAgeGroup(s, ageFilter)) return false;
                if (!string.IsNullOrEmpty(nameFilter) && (s.Name == null || s.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)) return false;
                if (!string.IsNullOrEmpty(bibFilter) && (s.BibNumber == null || s.BibNumber.IndexOf(bibFilter, StringComparison.OrdinalIgnoreCase) < 0)) return false;
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
                    // 组别（空串=不限）
                    var agCol = new DataGridComboBoxColumn { Header = "组别", Width = new DataGridLength(80), SelectedItemBinding = new System.Windows.Data.Binding("AgeGroup") };
                    var agItems = new List<string> { "" };
                    foreach (var g in _ageGroups) agItems.Add(g.Name);
                    agCol.ItemsSource = agItems; eg.Columns.Add(agCol);
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
                grid.Columns.Add(new DataGridTextColumn { Header = "组别", Binding = new System.Windows.Data.Binding("AgeGroup"), Width = new DataGridLength(70) });
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
            }

            // 手动选择赛次方案（默认 "只决赛"），生效于本次生成的全部项目
            List<string> stagePlan;
            if (!PromptStagePlan(out stagePlan)) return;   // 用户取消

            _schedule.Clear();
            AutoBuildSchedule(stagePlan);
            BuildScheduleTree();
            AutoSaveData();
            Broadcast();
            MessageBox.Show(string.Format("日程生成完成！\n共{0}条赛程项（赛次：{1}）。\n\n可在\"修改赛程\"里进一步调整，或用\"预赛自动分组\"/\"手动分组\"分配道次。",
                _schedule.Count, string.Join(" → ", stagePlan.ToArray())),
                "日程安排完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 弹窗让操作员手动选本次日程要包含哪些赛次（决赛 / 预赛+决赛 / 预赛+半决赛+决赛）
        private bool PromptStagePlan(out List<string> plan) {
            plan = new List<string> { "决赛" };
            var dlg = new Window {
                Title = "选择赛次方案", Width = 420, Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(new TextBlock {
                Text = "本次日程中所有项目使用的赛次方案（手动选择，不再按人数自动判定）：",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 13
            });
            var rb1 = new RadioButton { Content = "只决赛（推荐，适合中小型赛事）", IsChecked = true, Margin = new Thickness(0, 6, 0, 6), FontSize = 13 };
            var rb2 = new RadioButton { Content = "预赛 → 决赛", Margin = new Thickness(0, 6, 0, 6), FontSize = 13 };
            var rb3 = new RadioButton { Content = "预赛 → 半决赛 → 决赛", Margin = new Thickness(0, 6, 0, 6), FontSize = 13 };
            sp.Children.Add(rb1); sp.Children.Add(rb2); sp.Children.Add(rb3);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnOk = new Button { Content = "确定", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
                Foreground = Brushes.White, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            btnOk.Click += delegate { dlg.DialogResult = true; };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(20, 6, 20, 6) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnRow.Children.Add(btnOk); btnRow.Children.Add(btnCancel);
            sp.Children.Add(btnRow);
            dlg.Content = sp;
            if (dlg.ShowDialog() != true) { plan = null; return false; }

            if (rb3.IsChecked == true) plan = new List<string> { "预赛", "半决赛", "决赛" };
            else if (rb2.IsChecked == true) plan = new List<string> { "预赛", "决赛" };
            else plan = new List<string> { "决赛" };
            return true;
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

            // 统一对所有项目（个人+接力）的第一赛次进行蛇形分组，按组别隔离
            int generated = 0;
            foreach (var item in _schedule) {
                string fullEvent = item.EventName;
                string stage = item.Stage;
                string gender = item.Gender;
                string ageGroup = item.AgeGroup ?? "";

                // 过滤：仅本 ScheduleItem 对应的组别
                var eventSwimmers = _swimmers.Where(s =>
                    s.EventName == fullEvent && s.Gender == gender && s.CurrentStage == stage &&
                    MatchesAgeGroup(s, ageGroup) &&
                    !(s.Notes != null && s.Notes.StartsWith("接力队员"))
                ).ToList();

                if (eventSwimmers.Count == 0) continue;

                var assignments = HeatScheduler.GenerateHeats(eventSwimmers, _poolConfig, fullEvent, stage);
                item.HeatCount = assignments.Count > 0 ? assignments.Max(a => a.Heat) : 0;
                generated += assignments.Count;
                string typeLabel = fullEvent.Contains("接力") ? "队" : "人";
                AddLog(string.Format("  {0}{1} {2} {3}: {4}{5} → {6}组",
                    string.IsNullOrEmpty(ageGroup) ? "" : ("[" + ageGroup + "] "),
                    gender, fullEvent, stage, eventSwimmers.Count, typeLabel, item.HeatCount));
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
        // 手动分组：按项目选人员 → 可一键均分到 N 组，或直接编辑 组/道次
        // 解决自动分组"第一组满道，后面组缺很多"的不均衡问题
        // ═══════════════════════════════════════════════════════════════
        private void ManualHeatAssign_Click(object sender, RoutedEventArgs e) {
            if (_swimmers.Count == 0) { MessageBox.Show("暂无已注册运动员。"); return; }

            // 收集有效 (组别, gender, event) 组合 — 以组别为第一维，男甲/男乙单独成条
            var combos = _swimmers
                .Where(s => !string.IsNullOrEmpty(s.Gender) && !string.IsNullOrEmpty(s.EventName) &&
                            !(s.Notes != null && s.Notes.StartsWith("接力队员")))
                .GroupBy(s => new { AgeGroup = s.AgeCategory ?? "", s.Gender, s.EventName })
                .OrderBy(g => g.Key.AgeGroup).ThenBy(g => g.Key.Gender).ThenBy(g => g.Key.EventName)
                .Select(g => g.Key)
                .ToList();
            if (combos.Count == 0) { MessageBox.Show("没有可分组的运动员。"); return; }

            var dlg = new Window {
                Title = "手动分组", Width = 900, Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };
            var mainGrid = new Grid { Margin = new Thickness(14) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ─ 选择条 ─
            var pickRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            pickRow.Children.Add(new TextBlock { Text = "项目:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var cbEvent = new ComboBox { Width = 280 };
            foreach (var c in combos) {
                string label = (string.IsNullOrEmpty(c.AgeGroup) ? "" : c.AgeGroup + " ") + c.Gender + " " + c.EventName;
                cbEvent.Items.Add(label);
            }
            cbEvent.SelectedIndex = 0;
            pickRow.Children.Add(cbEvent);

            pickRow.Children.Add(new TextBlock { Text = "  阶段:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            var cbStage = new ComboBox { Width = 90 };
            cbStage.Items.Add("预赛"); cbStage.Items.Add("半决赛"); cbStage.Items.Add("决赛");
            cbStage.SelectedIndex = 0;
            pickRow.Children.Add(cbStage);

            pickRow.Children.Add(new TextBlock { Text = "  每组人数:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            var tbPerHeat = new TextBox { Width = 50, Padding = new Thickness(4), Text = _poolConfig.LaneCount.ToString() };
            pickRow.Children.Add(tbPerHeat);

            pickRow.Children.Add(new TextBlock { Text = "  分组方式:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            var cbMode = new ComboBox { Width = 130 };
            cbMode.Items.Add("蛇形(强↔弱交替)");
            cbMode.Items.Add("顺序(按成绩依次)");
            cbMode.Items.Add("按注册顺序");
            cbMode.SelectedIndex = 0;
            pickRow.Children.Add(cbMode);
            Grid.SetRow(pickRow, 0);
            mainGrid.Children.Add(pickRow);

            // ─ 操作按钮行 ─
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var btnEvenSplit = new Button { Content = "均匀分组 (按每组人数)", Padding = new Thickness(12, 5, 12, 5),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "根据每组人数把所选项目的运动员平均分成若干组，例如 19 人按每组 8 → 2 组各 10 和 9"};
            btnRow.Children.Add(btnEvenSplit);
            var btnClear = new Button { Content = "清空本项目分组", Padding = new Thickness(12, 5, 12, 5),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
            btnRow.Children.Add(btnClear);
            btnRow.Children.Add(new TextBlock { Text = "  表格里可直接修改 [组] [道]，再按\"确认保存\"；空白表示未分配。",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")) });
            Grid.SetRow(btnRow, 1);
            mainGrid.Children.Add(btnRow);

            // ─ 表格 ─
            var rowSource = new System.Collections.ObjectModel.ObservableCollection<ManualHeatRow>();
            var grid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Extended,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "号码", Binding = new System.Windows.Data.Binding("BibNumber") { Mode = System.Windows.Data.BindingMode.OneWay }, Width = new DataGridLength(70), IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "姓名", Binding = new System.Windows.Data.Binding("Name") { Mode = System.Windows.Data.BindingMode.OneWay }, Width = new DataGridLength(100), IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "代表队", Binding = new System.Windows.Data.Binding("Country") { Mode = System.Windows.Data.BindingMode.OneWay }, Width = new DataGridLength(120), IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "组别", Binding = new System.Windows.Data.Binding("AgeCategory") { Mode = System.Windows.Data.BindingMode.OneWay }, Width = new DataGridLength(70), IsReadOnly = true });
            var seedCol = new DataGridTextColumn { Header = "报名成绩", Binding = new System.Windows.Data.Binding("SeedTime") { Mode = System.Windows.Data.BindingMode.OneWay }, Width = new DataGridLength(90), IsReadOnly = true };
            grid.Columns.Add(seedCol);
            grid.Columns.Add(new DataGridTextColumn { Header = "组", Binding = new System.Windows.Data.Binding("Heat") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(60) });
            grid.Columns.Add(new DataGridTextColumn { Header = "道", Binding = new System.Windows.Data.Binding("Lane") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(60) });
            grid.ItemsSource = rowSource;
            Grid.SetRow(grid, 2);
            mainGrid.Children.Add(grid);

            // ─ 确认 / 取消 ─
            var okRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var btnOk = new Button { Content = "确认保存", Padding = new Thickness(18, 6, 18, 6), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(18, 6, 18, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            okRow.Children.Add(btnOk);
            okRow.Children.Add(btnCancel);
            Grid.SetRow(okRow, 3);
            mainGrid.Children.Add(okRow);
            dlg.Content = mainGrid;

            // ─ 事件绑定 ─
            Action reloadRows = delegate {
                rowSource.Clear();
                if (cbEvent.SelectedIndex < 0) return;
                var combo = combos[cbEvent.SelectedIndex];
                string stage = cbStage.SelectedItem as string ?? "预赛";
                string prevStage = GetPreviousStage(stage);
                // 列标题随阶段切换：预赛=报名成绩，半决赛/决赛=上一赛次成绩
                seedCol.Header = stage == "预赛" ? "报名成绩" : (prevStage + "成绩");
                bool isRelay = combo.EventName.Contains("接力");
                var matched = _swimmers.Where(s =>
                    s.Gender == combo.Gender && s.EventName == combo.EventName &&
                    MatchesAgeGroup(s, combo.AgeGroup) &&
                    !(isRelay && s.Notes != null && s.Notes.StartsWith("接力队员"))).ToList();
                foreach (var s in matched) {
                    int h = 0, ln = 0;
                    var sa = s.GetAssignmentForStage(stage);
                    if (sa != null) { h = sa.Heat; ln = sa.Lane; }
                    else if (s.CurrentStage == stage) { h = s.Heat; ln = s.Lane; }
                    rowSource.Add(new ManualHeatRow(s, h, ln, stage, prevStage));
                }
            };
            cbEvent.SelectionChanged += delegate { reloadRows(); };
            cbStage.SelectionChanged += delegate { reloadRows(); };
            reloadRows();

            btnEvenSplit.Click += delegate {
                // 提交正在编辑的单元
                try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }

                int perHeat = 0;
                int.TryParse((tbPerHeat.Text ?? "").Trim(), out perHeat);
                if (perHeat <= 0) perHeat = _poolConfig.LaneCount;

                var targets = grid.SelectedItems.Cast<ManualHeatRow>().ToList();
                if (targets.Count == 0) targets = rowSource.ToList();
                if (targets.Count == 0) return;

                // 排序：预赛按报名成绩；半决赛/决赛按上一赛次成绩；无成绩放最后
                string sortStage = cbStage.SelectedItem as string ?? "预赛";
                string sortPrev = GetPreviousStage(sortStage);
                Func<ManualHeatRow, double> seedSeconds = delegate(ManualHeatRow r) {
                    if (sortStage != "预赛") {
                        var pr = r.Swimmer.GetResultForStage(sortPrev);
                        if (pr != null && pr.FinalTime > 0) return pr.FinalTime;
                        return double.MaxValue;
                    }
                    double t = TimeFormatter.Parse(r.Swimmer.EntryTime ?? "");
                    return t > 0 ? t : double.MaxValue;
                };
                if (cbMode.SelectedIndex == 0 || cbMode.SelectedIndex == 1) {
                    targets = targets.OrderBy(r => seedSeconds(r)).ToList();
                }
                // cbMode == 2 按注册顺序（也就是当前 rowSource 顺序），保持不变即可
                if (cbMode.SelectedIndex == 2) {
                    targets = rowSource.Where(targets.Contains).ToList();
                }

                int total = targets.Count;
                int heatCount = (int)Math.Ceiling((double)total / perHeat);
                if (heatCount < 1) heatCount = 1;

                // 均分：各组尽量相等（多出的人数依序加到前面组）
                int baseSize = total / heatCount;
                int extra = total % heatCount;
                // lane 优先顺序：中间道次优先（快者在中）
                int[] lanePrio = HeatScheduler.GetLanePriority(_poolConfig);

                if (cbMode.SelectedIndex == 0) {
                    // 蛇形：把 sorted[i] 放到 heat index = SnakeHeat(i) —— 每组各 k 人，按蛇形交替
                    int idx = 0;
                    // 先按 "每组大小" 把 heats 准备好
                    var heatBuckets = new List<ManualHeatRow>[heatCount];
                    for (int h = 0; h < heatCount; h++) heatBuckets[h] = new List<ManualHeatRow>();
                    int[] heatSizes = new int[heatCount];
                    for (int h = 0; h < heatCount; h++) heatSizes[h] = baseSize + (h < extra ? 1 : 0);
                    // 蛇形放：第 1 轮 h=0,1,2,...,heatCount-1；第 2 轮逆序；如此循环
                    int round = 0;
                    int[] remain = (int[])heatSizes.Clone();
                    while (idx < total) {
                        bool leftToRight = (round % 2) == 0;
                        for (int j = 0; j < heatCount && idx < total; j++) {
                            int h = leftToRight ? j : heatCount - 1 - j;
                            if (remain[h] <= 0) continue;
                            heatBuckets[h].Add(targets[idx++]);
                            remain[h]--;
                        }
                        round++;
                    }
                    // 分道次：每组内按成绩由快到慢映射到 lanePrio（预赛=报名成绩，否则=上一赛次成绩）
                    for (int h = 0; h < heatCount; h++) {
                        var bucket = heatBuckets[h].OrderBy(r => seedSeconds(r)).ToList();
                        for (int k = 0; k < bucket.Count; k++) {
                            bucket[k].Heat = (heatCount - h).ToString();   // 速度最快的人放"最后一组"(正式比赛惯例)
                            bucket[k].Lane = (k < lanePrio.Length ? lanePrio[k] : k + 1).ToString();
                        }
                    }
                } else {
                    // 顺序/按注册顺序：按 targets 顺序填满每组
                    int idx = 0;
                    for (int h = 0; h < heatCount && idx < total; h++) {
                        int size = baseSize + (h < extra ? 1 : 0);
                        for (int k = 0; k < size && idx < total; k++) {
                            targets[idx].Heat = (h + 1).ToString();
                            targets[idx].Lane = (k < lanePrio.Length ? lanePrio[k] : k + 1).ToString();
                            idx++;
                        }
                    }
                }
                grid.Items.Refresh();
            };

            btnClear.Click += delegate {
                foreach (var r in rowSource) { r.Heat = ""; r.Lane = ""; }
                grid.Items.Refresh();
            };

            btnOk.Click += delegate {
                try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
                dlg.DialogResult = true;
            };
            btnCancel.Click += delegate { dlg.DialogResult = false; };

            if (dlg.ShowDialog() == true) {
                if (cbEvent.SelectedIndex < 0) return;
                var combo = combos[cbEvent.SelectedIndex];
                string stage = cbStage.SelectedItem as string ?? "预赛";

                int maxHeat = 0, assigned = 0;
                foreach (var r in rowSource) {
                    int h = 0, ln = 0;
                    int.TryParse(r.Heat ?? "", out h);
                    int.TryParse(r.Lane ?? "", out ln);
                    if (h > 0) {
                        r.Swimmer.SetStageAssignment(stage, h, ln, r.Swimmer.EntryTimeSeconds, r.Swimmer.EntryTime);
                        if (r.Swimmer.CurrentStage == stage) { r.Swimmer.Heat = h; r.Swimmer.Lane = ln; }
                        if (h > maxHeat) maxHeat = h;
                        assigned++;
                    } else {
                        if (r.Swimmer.StageAssignments.ContainsKey(stage)) r.Swimmer.StageAssignments.Remove(stage);
                        if (r.Swimmer.CurrentStage == stage) { r.Swimmer.Heat = 0; r.Swimmer.Lane = 0; }
                    }
                }

                // 更新/新建 Schedule 项（按 AgeGroup+Gender+Event+Stage 唯一）
                var sched = _schedule.FirstOrDefault(s =>
                    (s.AgeGroup ?? "") == combo.AgeGroup &&
                    s.Gender == combo.Gender && s.EventName == combo.EventName && s.Stage == stage);
                if (sched == null && maxHeat > 0) {
                    _schedule.Add(new ScheduleItem {
                        SessionNumber = _schedule.Count > 0 ? _schedule.Max(s => s.SessionNumber) : 1,
                        AgeGroup = combo.AgeGroup,
                        Gender = combo.Gender, EventName = combo.EventName, Stage = stage, HeatCount = maxHeat
                    });
                } else if (sched != null) {
                    sched.HeatCount = maxHeat;
                }

                AutoSaveData();
                BuildScheduleTree();
                SyncRelayHeatInfo();
                UpdateEditHeatCombo();
                Broadcast();
                AddLog(string.Format("手动分组: {0}{1} {2} {3} — {4}人已分配到{5}组",
                    string.IsNullOrEmpty(combo.AgeGroup) ? "" : ("[" + combo.AgeGroup + "] "),
                    combo.Gender, combo.EventName, stage, assigned, maxHeat));
            }
        }

        // 手动分组对话框的行绑定对象
        private class ManualHeatRow : INotifyPropertyChanged
        {
            private string _heat, _lane;
            private string _stage, _prevStage;
            public Swimmer Swimmer { get; private set; }
            public string BibNumber { get { return Swimmer.BibNumber ?? ""; } }
            public string Name { get { return Swimmer.Name ?? ""; } }
            public string Country { get { return Swimmer.Country ?? ""; } }
            public string AgeCategory { get { return Swimmer.AgeCategory ?? ""; } }
            public string EntryTime { get { return Swimmer.EntryTime ?? ""; } }
            // 种子成绩：预赛取报名成绩；半决赛/决赛取上一赛次最终成绩
            public string SeedTime {
                get {
                    if (_stage != "预赛" && !string.IsNullOrEmpty(_prevStage)) {
                        var pr = Swimmer.GetResultForStage(_prevStage);
                        if (pr != null && pr.FinalTime > 0) return TimeFormatter.Format(pr.FinalTime);
                        return "";
                    }
                    return Swimmer.EntryTime ?? "";
                }
            }
            public string Heat { get { return _heat; } set { _heat = value; Raise("Heat"); } }
            public string Lane { get { return _lane; } set { _lane = value; Raise("Lane"); } }
            public ManualHeatRow(Swimmer s, int h, int l) : this(s, h, l, "预赛", "") { }
            public ManualHeatRow(Swimmer s, int h, int l, string stage, string prevStage) {
                Swimmer = s; _heat = h > 0 ? h.ToString() : ""; _lane = l > 0 ? l.ToString() : "";
                _stage = stage ?? "预赛"; _prevStage = prevStage ?? "";
            }
            public event PropertyChangedEventHandler PropertyChanged;
            void Raise(string n) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(n)); }
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
        private void AutoBuildSchedule(List<string> stagePlan = null) {
            // stagePlan 为 null 时回退到"只决赛"——手动默认
            if (stagePlan == null || stagePlan.Count == 0) stagePlan = new List<string> { "决赛" };
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
            // 按 (组别, 性别, 项目) 分组；组别为空时视作 "" 字符串，仍然单独成组
            var eventGroups = validSwimmers
                .GroupBy(s => new { AgeGroup = s.AgeCategory ?? "", s.Gender, s.EventName })
                .OrderBy(g => g.Key.AgeGroup).ThenBy(g => g.Key.Gender).ThenBy(g => g.Key.EventName).ToList();

            var allItems = new List<ScheduleItem>();
            int laneCount = _poolConfig.LaneCount;

            foreach (var group in eventGroups) {
                string ageGroup = group.Key.AgeGroup;
                string gender = group.Key.Gender;
                string eventName = group.Key.EventName;
                bool isRelay = eventName.Contains("接力");
                int count = group.Count();
                // 不再按人数自动判定赛次，完全按用户选择的方案生成
                var stages = new List<string>(stagePlan);
                // 特例：接力项目没有半决赛，自动剔除
                if (isRelay) stages.RemoveAll(s => s == "半决赛");
                if (stages.Count == 0) stages.Add("决赛");
                string firstStage = stages[0];

                foreach (var sw in group) sw.CurrentStage = firstStage;

                foreach (string stage in stages) {
                    int estimatedCount = count;
                    if (stage == "半决赛") estimatedCount = Math.Min(count, 16);
                    else if (stage == "决赛" && stages.Count > 1) estimatedCount = Math.Min(count, 8);
                    int estimatedHeats = (int)Math.Ceiling((double)estimatedCount / laneCount);
                    if (estimatedHeats < 1) estimatedHeats = 1;

                    allItems.Add(new ScheduleItem {
                        AgeGroup = ageGroup,
                        Gender = gender, EventName = eventName, Stage = stage,
                        IsRelay = isRelay, HeatCount = estimatedHeats
                    });
                }
                string typeLabel = isRelay ? "(接力)" : "";
                AddLog(string.Format("  {0}{1} {2}{3}: {4}{5} → {6}",
                    string.IsNullOrEmpty(ageGroup) ? "" : ("[" + ageGroup + "] "),
                    gender, eventName, typeLabel, count, isRelay ? "队" : "人", string.Join("→", stages.ToArray())));
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

                // 晚上场：决赛（17:30开始）
                bool hasEvening = false;
                int eveningStartMin = 17 * 60 + 30;   // 17:30
                offsetMin = 0;

                // 直接决赛
                if (dfByDay.ContainsKey(day)) {
                    foreach (var item in dfByDay[day]) {
                        int t = eveningStartMin + offsetMin;
                        item.Date = dateStr;
                        item.Time = string.Format("{0:D2}:{1:D2}", t / 60, t % 60);
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
                        int t = eveningStartMin + offsetMin;
                        item.Date = dateStr;
                        item.Time = string.Format("{0:D2}:{1:D2}", t / 60, t % 60);
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
        private void ResultAgeGroup_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) { UpdateResultHeatCombo(); RefreshResultGrid(); } }
        private void ResultEvent_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) { UpdateResultHeatCombo(); RefreshResultGrid(); } }
        private void ResultStage_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) { UpdateResultHeatCombo(); RefreshResultGrid(); } }
        private void ResultGender_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) { UpdateResultHeatCombo(); RefreshResultGrid(); } }
        private void ResultHeat_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized && !_resultUpdating) RefreshResultGrid(); }

        private void UpdateResultHeatCombo() {
            if (ResultHeatCombo == null || _resultUpdating) return;
            _resultUpdating = true;
            try {
            // 记住当前选中的组号文本（如 "第2组" 或 "全部"），在重建下拉后尽量恢复
            string prevHeat = null;
            var prevHeatItem = ResultHeatCombo.SelectedItem as ComboBoxItem;
            if (prevHeatItem != null && prevHeatItem.Content != null) prevHeat = prevHeatItem.Content.ToString();

            // 刷新项目列表：只显示有运动员/运动队注册的项目（过滤接力队员个人条目）
            string prevEvent = ResultEventCombo.SelectedItem != null ? ResultEventCombo.SelectedItem.ToString() : "";
            string ageFilter = ResultAgeGroupCombo != null && ResultAgeGroupCombo.SelectedItem != null ? ResultAgeGroupCombo.SelectedItem.ToString() : "全部";
            string gender = ResultGenderCombo.SelectedItem != null ? ((ComboBoxItem)ResultGenderCombo.SelectedItem).Content.ToString() : "男";
            ResultEventCombo.Items.Clear();
            var eventSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (s.Gender == gender && !string.IsNullOrEmpty(s.EventName) &&
                    MatchesAgeGroup(s, ageFilter) &&
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

            // 只列出"已确认成绩"的组次：未确认（含正在比赛）的组在此不出现
            var heats = new HashSet<int>();
            string agForCheck = ageFilter == "全部" ? "" : ageFilter;
            foreach (var s in _swimmers) {
                if (!string.IsNullOrEmpty(eventName) && s.EventName != eventName) continue;
                if (s.Gender != gender) continue;
                if (!MatchesAgeGroup(s, ageFilter)) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                int candHeat = 0;
                var saH = s.GetAssignmentForStage(stage);
                if (saH != null && saH.Heat > 0) candHeat = saH.Heat;
                else if (s.CurrentStage == stage && s.Heat > 0) candHeat = s.Heat;
                if (candHeat <= 0) continue;
                if (heats.Contains(candHeat)) continue;
                if (IsHeatConfirmed(agForCheck, gender, eventName, stage, candHeat))
                    heats.Add(candHeat);
            }
            foreach (int h in heats.OrderBy(x => x)) {
                ResultHeatCombo.Items.Add(new ComboBoxItem { Content = string.Format("第{0}组", h) });
            }

            // 恢复之前选中的组号；找不到则回退到"全部"
            int restored = -1;
            if (!string.IsNullOrEmpty(prevHeat)) {
                for (int i = 0; i < ResultHeatCombo.Items.Count; i++) {
                    var ci = ResultHeatCombo.Items[i] as ComboBoxItem;
                    if (ci != null && ci.Content != null && ci.Content.ToString() == prevHeat) { restored = i; break; }
                }
            }
            ResultHeatCombo.SelectedIndex = restored >= 0 ? restored : 0;
            } finally {
                _resultUpdating = false;
            }
        }

        private void RefreshResultGrid() {
            if (ResultEventCombo == null || ResultStageCombo == null || ResultGenderCombo == null || ResultGrid == null) return;
            string ageFilter = ResultAgeGroupCombo != null && ResultAgeGroupCombo.SelectedItem != null ? ResultAgeGroupCombo.SelectedItem.ToString() : "全部";
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

            // 按组别/性别和项目筛选（接力项目只查代表队，不查个人队员）
            var allMatched = _swimmers.Where(s =>
                s.EventName == eventName && s.Gender == gender &&
                MatchesAgeGroup(s, ageFilter) &&
                !(s.Notes != null && s.Notes.StartsWith("接力队员"))
            ).ToList();

            // 按阶段筛选：有该阶段成绩，或有该阶段分组记录
            var results = allMatched.Where(s =>
                s.GetResultForStage(stage) != null ||
                s.GetAssignmentForStage(stage) != null ||
                s.CurrentStage == stage
            ).ToList();

            // 按组筛选：优先按"分组记录"判断该运动员属于哪组（同 stage 多个 result 时 GetResultForStage 不可靠）
            if (filterHeat > 0) {
                results = results.Where(s => {
                    var sa = s.GetAssignmentForStage(stage);
                    if (sa != null && sa.Heat > 0) return sa.Heat == filterHeat;
                    if (s.CurrentStage == stage && s.Heat > 0) return s.Heat == filterHeat;
                    // 兜底：按成绩里的 Heat 字段
                    var r = s.Results.FirstOrDefault(rx => rx.Stage == stage && rx.Heat == filterHeat);
                    return r != null;
                }).ToList();
            }

            // 只展示已确认成绩的组次：未确认的组（如正在比赛中）不在此处显示，
            // 等"确认成绩"按下后才会出现。这样裁判判罚/取消成绩时不会被提前看到。
            results = results.Where(s => {
                int hh = 0;
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat > 0) hh = sa.Heat;
                else if (s.CurrentStage == stage && s.Heat > 0) hh = s.Heat;
                if (hh <= 0) return false;
                return IsHeatConfirmed(ageFilter == "全部" ? "" : ageFilter, gender, eventName, stage, hh);
            }).ToList();

            bool isRelayEvent = eventName.Contains("接力");
            // 接力赛棒次数：用于反应时按棒展开
            int rgLegCount = 4;
            if (isRelayEvent) {
                var mLeg = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)\s*[x×]\s*\d+");
                if (mLeg.Success) {
                    int n; if (int.TryParse(mLeg.Groups[1].Value, out n) && n > 0 && n <= 10) rgLegCount = n;
                }
            }
            var displayData = results.Select(s => {
                // 按运动员所属的组精确查找该组成绩（避免在 stage 内多组场景下取错）
                int swHeat = 0;
                var swSa = s.GetAssignmentForStage(stage);
                if (swSa != null && swSa.Heat > 0) swHeat = swSa.Heat;
                else if (s.CurrentStage == stage && s.Heat > 0) swHeat = s.Heat;
                LaneResult r = swHeat > 0
                    ? s.Results.FirstOrDefault(rx => rx.Stage == stage && rx.Heat == swHeat)
                    : s.GetResultForStage(stage);
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
                // 反应时：接力按棒展开 "第N棒:0.45  第N棒:..."（双空格分隔，DataGrid TextWrapping=Wrap 自动换行）
                string reactionStr = "";
                if (isRelayEvent) {
                    var rtParts = new List<string>();
                    for (int li = 0; li < rgLegCount; li++) {
                        double rt = (r != null && r.LegReactionTimes != null && li < r.LegReactionTimes.Count) ? r.LegReactionTimes[li] : 0;
                        rtParts.Add(string.Format("第{0}棒:{1}", li + 1, rt > 0 ? rt.ToString("F2") : "—"));
                    }
                    reactionStr = string.Join("  ", rtParts.ToArray());
                } else if (r != null && r.StartingBlockTime > 0) {
                    reactionStr = r.StartingBlockTime.ToString("F2");
                }
                return new {
                    SortTime = sortTime,
                    Lane = lane,
                    BibNumber = s.BibNumber ?? "",
                    Name = displayName,                  // 接力时为 4 名队员姓名（逗号分隔）
                    Country = s.Country ?? "",            // 代表队/队名
                    FinalTime = isDQ ? "" : (r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : ""),
                    TimingSource = r != null ? (r.TimingSource ?? "") : "",
                    ReactionTime = reactionStr,
                    Status = !string.IsNullOrEmpty(remark) ? remark : (s.Status ?? ""),
                    RecordNote = r != null ? (r.RecordNote ?? "") : ""
                };
            }).OrderBy(x => x.SortTime).ToList();

            // 重新计算排名（DSQ/DNS/DNF无名次）；列与表头一一对应：
            //   "姓名"列 -> Name（接力时即 4 棒队员姓名）
            //   "代表队"列 -> Country（队名）
            var rankedData = new List<object>();
            int rankNum = 1;
            foreach (var item in displayData) {
                string rankStr = item.SortTime < double.MaxValue ? rankNum.ToString() : "-";
                rankedData.Add(new {
                    Rank = rankStr,
                    item.Lane, item.BibNumber,
                    item.Name, item.Country,
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

        // "名次分设置"对话框：编辑各名次得分（个人/接力）、组别系数、取分人数、破纪录加分。
        // 点击保存后写入 _scoringConfig + 持久化到 CompetitionPackage，并重算团体积分。
        private void ScoringConfig_Click(object sender, RoutedEventArgs e) {
            var dlg = new Window {
                Title = "名次分设置 — 团体计分",
                Width = 720, Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.CanResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"))
            };
            var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14) };
            var sp = new StackPanel { Orientation = Orientation.Vertical };

            sp.Children.Add(new TextBlock {
                Text = "团体积分按\"决赛\"成绩取分；下面所有数值均可修改，保存后立即重算并入档持久化。",
                FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")),
                Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap
            });

            // 取分人数 + 破纪录加分（顶部一行）
            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            topRow.Children.Add(new TextBlock { Text = "取分人数（前 N 名得分）：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var cutoffBox = new TextBox { Text = _scoringConfig.RankCutoff.ToString(), Width = 60, VerticalAlignment = VerticalAlignment.Center };
            topRow.Children.Add(cutoffBox);
            topRow.Children.Add(new TextBlock { Text = "    破纪录加分（每项）：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 6, 0) });
            var bonusBox = new TextBox { Text = _scoringConfig.RecordBreakBonus.ToString("0.##"), Width = 60, VerticalAlignment = VerticalAlignment.Center };
            topRow.Children.Add(bonusBox);
            sp.Children.Add(topRow);

            // 个人项目名次分（每名次一个 TextBox，按取分人数限定个数；多于 cutoff 时仍允许编辑保留历史值）
            sp.Children.Add(new TextBlock { Text = "个人项目 — 名次得分", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
            var indPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var indBoxes = new List<TextBox>();
            int maxRanks = Math.Max(8, Math.Max(_scoringConfig.IndividualPoints.Count, _scoringConfig.RelayPoints.Count));
            for (int i = 0; i < maxRanks; i++) {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6) };
                item.Children.Add(new TextBlock { Text = "第" + (i + 1) + "名：", VerticalAlignment = VerticalAlignment.Center, Width = 56, TextAlign​ment = TextAlignment.Right });
                double v = i < _scoringConfig.IndividualPoints.Count ? _scoringConfig.IndividualPoints[i] : 0;
                var tb = new TextBox { Text = v.ToString("0.##"), Width = 60, VerticalAlignment = VerticalAlignment.Center };
                indBoxes.Add(tb);
                item.Children.Add(tb);
                indPanel.Children.Add(item);
            }
            sp.Children.Add(indPanel);

            // 接力项目名次分
            sp.Children.Add(new TextBlock { Text = "接力项目 — 名次得分", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
            var relayPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var relayBoxes = new List<TextBox>();
            for (int i = 0; i < maxRanks; i++) {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6) };
                item.Children.Add(new TextBlock { Text = "第" + (i + 1) + "名：", VerticalAlignment = VerticalAlignment.Center, Width = 56, TextAlignment = TextAlignment.Right });
                double v = i < _scoringConfig.RelayPoints.Count ? _scoringConfig.RelayPoints[i] : 0;
                var tb = new TextBox { Text = v.ToString("0.##"), Width = 60, VerticalAlignment = VerticalAlignment.Center };
                relayBoxes.Add(tb);
                item.Children.Add(tb);
                relayPanel.Children.Add(item);
            }
            sp.Children.Add(relayPanel);

            // 组别系数（含已有的 + 当前比赛实际出现的所有 AgeCategory）
            sp.Children.Add(new TextBlock { Text = "组别系数（最终得分 = 名次分 × 系数；找不到的组按 1.0 计）", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
            var ageGroupKeys = new SortedSet<string>(_scoringConfig.AgeGroupCoefficients.Keys);
            foreach (var s in _swimmers) if (!string.IsNullOrEmpty(s.AgeCategory)) ageGroupKeys.Add(s.AgeCategory);
            var coeffPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var coeffBoxes = new Dictionary<string, TextBox>();
            foreach (var key in ageGroupKeys) {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6) };
                item.Children.Add(new TextBlock { Text = key + "：", VerticalAlignment = VerticalAlignment.Center, MinWidth = 60, TextAlignment = TextAlignment.Right });
                double v = _scoringConfig.GetAgeCoefficient(key);
                var tb = new TextBox { Text = v.ToString("0.##"), Width = 60, VerticalAlignment = VerticalAlignment.Center };
                coeffBoxes[key] = tb;
                item.Children.Add(tb);
                coeffPanel.Children.Add(item);
            }
            sp.Children.Add(coeffPanel);

            // 按钮行
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnReset = new Button { Content = "恢复默认", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnReset.Click += delegate {
                if (MessageBox.Show("将所有数值恢复为系统默认（个人 12/10/8/7/6/5/4/3，接力 24/20/16/14/12/10/8/6，青少年/少年=0.8、大师=0.7，取分前 8 名）。继续？",
                    "恢复默认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                    _scoringConfig.ResetToDefaults();
                    dlg.DialogResult = false;
                    dlg.Close();
                    ScoringConfig_Click(null, null);   // 重新打开载入默认值
                }
            };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOK = new Button { Content = "保存并重算", Padding = new Thickness(20, 6, 20, 6), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnOK.Click += delegate {
                int cutoff;
                if (!int.TryParse(cutoffBox.Text.Trim(), out cutoff) || cutoff < 1 || cutoff > 50) {
                    MessageBox.Show("取分人数必须是 1–50 的整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                double bonus;
                if (!double.TryParse(bonusBox.Text.Trim(), out bonus) || bonus < 0) bonus = 0;

                var indPoints = new List<double>();
                foreach (var tb in indBoxes) { double p; double.TryParse(tb.Text.Trim(), out p); indPoints.Add(p); }
                var relayPoints = new List<double>();
                foreach (var tb in relayBoxes) { double p; double.TryParse(tb.Text.Trim(), out p); relayPoints.Add(p); }
                var coeffs = new Dictionary<string, double>();
                foreach (var kv in coeffBoxes) { double c; if (double.TryParse(kv.Value.Text.Trim(), out c) && c > 0) coeffs[kv.Key] = c; }

                _scoringConfig.RankCutoff = cutoff;
                _scoringConfig.RecordBreakBonus = bonus;
                _scoringConfig.IndividualPoints = indPoints;
                _scoringConfig.RelayPoints = relayPoints;
                _scoringConfig.AgeGroupCoefficients = coeffs;

                CalculateTeamScores();
                AutoSaveData();
                AddLog(string.Format("名次分设置已更新：取分前 {0} 名，个人 [{1}]，接力 [{2}]",
                    cutoff, string.Join(",", indPoints.Take(cutoff).ToArray()), string.Join(",", relayPoints.Take(cutoff).ToArray())));
                dlg.DialogResult = true;
            };
            btnRow.Children.Add(btnReset); btnRow.Children.Add(btnCancel); btnRow.Children.Add(btnOK);
            sp.Children.Add(btnRow);

            root.Content = sp;
            dlg.Content = root;
            dlg.ShowDialog();
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

        private void CalculateTeamScores() {
            var teamDict = new Dictionary<string, TeamScore>();

            // 取分范围由配置控制（默认前 8 名）
            int cutoff = _scoringConfig != null && _scoringConfig.RankCutoff > 0 ? _scoringConfig.RankCutoff : 8;
            var finalSwimmers = _swimmers.Where(s => s.CurrentStage == "决赛" && s.CurrentRank > 0 && s.CurrentRank <= cutoff).ToList();

            foreach (var sw in finalSwimmers) {
                if (string.IsNullOrEmpty(sw.Country)) continue;
                if (!teamDict.ContainsKey(sw.Country)) {
                    teamDict[sw.Country] = new TeamScore { TeamName = sw.Country };
                }
                var ts = teamDict[sw.Country];

                bool isRelay = sw.EventName.Contains("接力");
                double points = isRelay
                    ? _scoringConfig.GetRelayPoint(sw.CurrentRank)
                    : _scoringConfig.GetIndividualPoint(sw.CurrentRank);

                // 组别系数：从配置读，找不到按 1.0
                double coeff = _scoringConfig.GetAgeCoefficient(sw.AgeCategory);
                if (coeff != 1.0) points = Math.Round(points * coeff);

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

        // —— 图片 / 视频 显示控制 ——
        private string _lastMediaPath = "";
        private void ShowMedia_Click(object sender, RoutedEventArgs e) {
            var dlg = new Window {
                Title = "图片 / 视频 - 大屏显示",
                Width = 560, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock {
                Text = "图片 / 视频 大屏显示",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                Margin = new Thickness(0, 0, 0, 6)
            });
            sp.Children.Add(new TextBlock {
                Text = "选择图片（PNG/JPG/BMP/GIF）或视频（MP4/WEBM/OGG），点击\"发送到大屏\"全屏播放。点击\"停止显示\"返回比赛视图。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 12)
            });

            var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(pathRow, Dock.Top);
            var btnPick = new Button {
                Content = "选择文件...", Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            DockPanel.SetDock(btnPick, Dock.Right);
            var pathBox = new TextBox {
                IsReadOnly = true, Padding = new Thickness(6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")),
                Text = _lastMediaPath, Margin = new Thickness(0, 0, 8, 0)
            };
            pathRow.Children.Add(btnPick);
            pathRow.Children.Add(pathBox);
            sp.Children.Add(pathRow);

            // 显示选项
            var optsGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            optsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            optsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            optsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            optsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lblFit = new TextBlock { Text = "缩放方式：", VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")) };
            Grid.SetRow(lblFit, 0); Grid.SetColumn(lblFit, 0);
            var fitPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var rbContain = new RadioButton { Content = "保持比例（contain）", IsChecked = true, GroupName = "MediaFit", Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var rbCover = new RadioButton { Content = "填满屏幕（cover）", GroupName = "MediaFit", Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var rbStretch = new RadioButton { Content = "拉伸（fill）", GroupName = "MediaFit", VerticalAlignment = VerticalAlignment.Center };
            fitPanel.Children.Add(rbContain); fitPanel.Children.Add(rbCover); fitPanel.Children.Add(rbStretch);
            Grid.SetRow(fitPanel, 0); Grid.SetColumn(fitPanel, 1);
            var lblOpt = new TextBlock { Text = "视频选项：", VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")), Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetRow(lblOpt, 1); Grid.SetColumn(lblOpt, 0);
            var optPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var cbLoop = new CheckBox { Content = "循环播放", IsChecked = true, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var cbMute = new CheckBox { Content = "静音", IsChecked = false, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var cbAuto = new CheckBox { Content = "自动播放", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            optPanel.Children.Add(cbLoop); optPanel.Children.Add(cbMute); optPanel.Children.Add(cbAuto);
            Grid.SetRow(optPanel, 1); Grid.SetColumn(optPanel, 1);
            optsGrid.Children.Add(lblFit); optsGrid.Children.Add(fitPanel); optsGrid.Children.Add(lblOpt); optsGrid.Children.Add(optPanel);
            sp.Children.Add(optsGrid);

            var sizeText = new TextBlock { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(sizeText);

            string detectedKind = "";
            string detectedMime = "";
            Action updateInfo = delegate {
                string p = pathBox.Text;
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) {
                    sizeText.Text = "";
                    detectedKind = ""; detectedMime = "";
                    return;
                }
                string ext = (IOPath.GetExtension(p) ?? "").ToLower();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".webp") {
                    detectedKind = "image";
                    detectedMime = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg"
                                 : ext == ".bmp" ? "image/bmp"
                                 : ext == ".gif" ? "image/gif"
                                 : ext == ".webp" ? "image/webp"
                                 : "image/png";
                } else if (ext == ".mp4" || ext == ".webm" || ext == ".ogg" || ext == ".m4v") {
                    detectedKind = "video";
                    detectedMime = ext == ".webm" ? "video/webm"
                                 : ext == ".ogg" ? "video/ogg"
                                 : "video/mp4";
                } else {
                    detectedKind = ""; detectedMime = "";
                }
                long len = new FileInfo(p).Length;
                sizeText.Text = string.Format("文件：{0}    大小：{1:F2} MB    类型：{2}",
                    IOPath.GetFileName(p), len / 1048576.0, string.IsNullOrEmpty(detectedKind) ? "未识别（请选择图片或视频文件）" : detectedKind);
            };
            updateInfo();

            btnPick.Click += delegate {
                var ofd = new Microsoft.Win32.OpenFileDialog {
                    Title = "选择图片或视频",
                    Filter = "图片 / 视频|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.mp4;*.webm;*.ogg;*.m4v|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|视频|*.mp4;*.webm;*.ogg;*.m4v|所有文件|*.*"
                };
                if (ofd.ShowDialog() == true) {
                    pathBox.Text = ofd.FileName;
                    updateInfo();
                }
            };

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnStop = new Button {
                Content = "停止显示（返回比赛视图）", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnStop.Click += delegate { BroadcastDisplayMode("SHOW_LIVE_RACE"); AddLog("停止媒体显示，已返回比赛视图"); };
            var btnSend = new Button {
                Content = "发送到大屏", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnSend.Click += delegate {
                string p = pathBox.Text;
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) { MessageBox.Show("请选择有效的图片或视频文件"); return; }
                if (string.IsNullOrEmpty(detectedKind)) { MessageBox.Show("无法识别文件类型，请选择 PNG/JPG/BMP/GIF/WEBP 图片或 MP4/WEBM/OGG 视频"); return; }
                long len = new FileInfo(p).Length;
                if (len > 60L * 1024 * 1024) {
                    if (MessageBox.Show(string.Format("文件较大（{0:F1} MB），通过 WebSocket 嵌入可能耗时。继续发送？", len / 1048576.0),
                        "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                }
                string fit = rbCover.IsChecked == true ? "cover" : (rbStretch.IsChecked == true ? "fill" : "contain");
                BroadcastMediaToDisplay(p, detectedKind, detectedMime, fit, cbLoop.IsChecked == true, cbMute.IsChecked == true, cbAuto.IsChecked == true);
                _lastMediaPath = p;
            };
            var btnClose = new Button {
                Content = "关闭", Padding = new Thickness(14, 6, 14, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnClose.Click += delegate { dlg.Close(); };
            btnRow.Children.Add(btnStop); btnRow.Children.Add(btnSend); btnRow.Children.Add(btnClose);
            sp.Children.Add(btnRow);

            dlg.Content = sp;
            dlg.ShowDialog();
        }

        private void BroadcastMediaToDisplay(string path, string kind, string mime, string fit, bool loop, bool muted, bool autoplay) {
            try {
                byte[] bytes = File.ReadAllBytes(path);
                string b64 = Convert.ToBase64String(bytes);
                string dataUrl = "data:" + mime + ";base64," + b64;
                var payload = new {
                    type = "SHOW_MEDIA",
                    data = new {
                        kind = kind,
                        mime = mime,
                        fileName = IOPath.GetFileName(path),
                        dataUrl = dataUrl,
                        fit = fit,
                        loop = loop,
                        muted = muted,
                        autoplay = autoplay,
                        sizeBytes = bytes.LongLength
                    }
                };
                string json = JsonConvert.SerializeObject(payload);
                int sent = 0;
                foreach (var s in _allSockets.ToList()) {
                    try { s.Send(json); sent++; } catch { }
                }
                AddLog(string.Format("已发送{0}到大屏：{1}（{2:F2} MB），客户端 {3} 个",
                    kind == "image" ? "图片" : "视频", IOPath.GetFileName(path), bytes.Length / 1048576.0, sent));
            } catch (Exception ex) {
                MessageBox.Show("发送失败：" + ex.Message);
                AddLog("媒体发送失败: " + ex.Message);
            }
        }

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
            SendTimingSettingsToHardware();   // 泳池参数可能变更，同步给硬件
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
            _confirmedHeats.Clear();

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

            // 同步刷新所有依赖运动员/赛程数据的下拉、统计、面板，避免删库后旧数据滞留
            try { RefreshOverviewStats(); } catch { }
            try { RefreshSwimmerFilter(); } catch { }
            try { RebuildRelayGroupedView(); } catch { }
            try { RefreshEventComboBoxes(); } catch { }
            try { RefreshAllAgeGroupFilterCombos(); } catch { }
            try { UpdateResultHeatCombo(); } catch { }
            try { RefreshResultGrid(); } catch { }
            try { BuildScheduleTree(); } catch { }
            try { RebuildScheduleGroupedView(); } catch { }
            try { UpdateEditHeatCombo(); } catch { }
            try { RefreshEditPreview(); } catch { }
            try { UpdateLaneStatusDisplay(); } catch { }
            // 推送清空状态到所有 HTML/EXE 客户端
            try { Broadcast(); } catch { }
        }

        /// <summary>
        /// 清除全部数据和界面（含赛事基本信息）
        /// 用于"新建赛事"、"删除当前存档"
        /// </summary>
        private void ClearAllDataAndUI() {
            ClearCompetitionData();

            // 秩序册/成绩册自定义内容
            _programBook = new ProgramBookData();
            _resultBook = new ResultBookData();

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

        // 计时硬件通讯参数（串口 / TCP / UDP）持久化路径
        private string TimingConnectionPath {
            get { return IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "timing_connection.json"); }
        }

        private void SaveTimingConnectionConfig() {
            try {
                string json = JsonConvert.SerializeObject(_timingConn, Formatting.Indented);
                File.WriteAllText(TimingConnectionPath, json, Encoding.UTF8);
            } catch (Exception ex) {
                AddLog("保存通讯参数失败: " + ex.Message);
            }
        }

        private void LoadTimingConnectionConfig() {
            try {
                if (!File.Exists(TimingConnectionPath)) return;
                string json = File.ReadAllText(TimingConnectionPath, Encoding.UTF8);
                var loaded = JsonConvert.DeserializeObject<TimingConnectionConfig>(json);
                if (loaded != null) {
                    _timingConn = loaded;
                    AddLog(string.Format("已加载通讯参数: 上次={0} 串口={1} TCP={2}:{3} UDP收={4} UDP发={5}:{6}",
                        string.IsNullOrEmpty(_timingConn.LastType) ? "未连接过" : _timingConn.LastType,
                        string.IsNullOrEmpty(_timingConn.SerialPort) ? "—" : _timingConn.SerialPort,
                        _timingConn.TcpHost ?? "—", _timingConn.TcpPort,
                        _timingConn.UdpListenPort,
                        _timingConn.UdpSendHost ?? "—", _timingConn.UdpSendPort));
                }
            } catch (Exception ex) {
                AddLog("加载通讯参数失败: " + ex.Message);
            }
        }

        // 把 _timingConn 还原到 UI 控件（在 PopulateComPorts 之后调用，确保 ComboBox 已有可选项）
        private void ApplyTimingConnectionToUi() {
            if (_timingConn == null) return;
            // 串口：若保存的端口仍存在于当前可选列表中，自动选中
            if (!string.IsNullOrEmpty(_timingConn.SerialPort) && ComPortCombo != null) {
                foreach (var item in ComPortCombo.Items) {
                    if (item != null && item.ToString() == _timingConn.SerialPort) {
                        ComPortCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            // 波特率：在下拉里找匹配项；找不到就保持默认（9600）
            if (BaudRateCombo != null && _timingConn.SerialBaudRate > 0) {
                string want = _timingConn.SerialBaudRate.ToString();
                foreach (var item in BaudRateCombo.Items) {
                    var ci = item as ComboBoxItem;
                    string txt = ci != null ? ci.Content.ToString() : (item != null ? item.ToString() : "");
                    if (txt == want) {
                        BaudRateCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            if (TcpHostBox != null && _timingConn.TcpPort > 0)
                TcpHostBox.Text = string.Format("{0}:{1}",
                    string.IsNullOrEmpty(_timingConn.TcpHost) ? "127.0.0.1" : _timingConn.TcpHost,
                    _timingConn.TcpPort);
            if (UdpPortBox != null && _timingConn.UdpListenPort > 0)
                UdpPortBox.Text = _timingConn.UdpListenPort.ToString();
            if (UdpSendBox != null && _timingConn.UdpSendPort > 0)
                UdpSendBox.Text = string.Format("{0}:{1}",
                    string.IsNullOrEmpty(_timingConn.UdpSendHost) ? "127.0.0.1" : _timingConn.UdpSendHost,
                    _timingConn.UdpSendPort);
        }

        // 启动时自动重连（仅当 AutoReconnectOnStartup=true）
        private void TryAutoReconnectTiming() {
            if (_timingConn == null || !_timingConn.AutoReconnectOnStartup) return;
            try {
                if (_timingConn.LastType == "serial" && !string.IsNullOrEmpty(_timingConn.SerialPort)) {
                    int baud = _timingConn.SerialBaudRate > 0 ? _timingConn.SerialBaudRate : 9600;
                    _timingBridge.ConnectSerial(_timingConn.SerialPort, baud);
                    UpdateConnectionStatus();
                    AddLog(string.Format("启动自动重连串口: {0} @ {1} baud", _timingConn.SerialPort, baud));
                } else if (_timingConn.LastType == "tcp" && !string.IsNullOrEmpty(_timingConn.TcpHost) && _timingConn.TcpPort > 0) {
                    _timingBridge.ConnectTcp(_timingConn.TcpHost, _timingConn.TcpPort);
                    UpdateConnectionStatus();
                    AddLog(string.Format("启动自动重连TCP: {0}:{1}", _timingConn.TcpHost, _timingConn.TcpPort));
                } else if (_timingConn.LastType == "udp" && _timingConn.UdpListenPort > 0) {
                    _timingBridge.ConnectUdp(_timingConn.UdpListenPort, _timingConn.UdpSendHost, _timingConn.UdpSendPort);
                    UpdateConnectionStatus();
                    AddLog(string.Format("启动自动重连UDP: 收{0} 发{1}:{2}",
                        _timingConn.UdpListenPort, _timingConn.UdpSendHost ?? "—", _timingConn.UdpSendPort));
                }
            } catch (Exception ex) { AddLog("自动重连失败: " + ex.Message); }
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
                    AgeGroups = _ageGroups,
                    Genders = _genders,
                    Stages = _stages,
                    HeatCounts = _heatCounts,
                    BibRanges = _bibRanges,
                    LaneCloseSettings = _laneCloseSettings,
                    ScoringConfig = _scoringConfig,
                    ProgramBook = _programBook,
                    ResultBook = _resultBook,
                    DisplayRecordLabel = _displayRecordLabel,
                    DisplayRecordTypeName = _displayRecordTypeName,
                    DisplayRecordOptions = _displayRecordOptions,
                    ConfirmedHeats = _confirmedHeats.ToList()
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
                // 团体计分配置：旧档案没有就保留默认；空字段补默认值
                if (package.ScoringConfig != null) {
                    _scoringConfig = package.ScoringConfig;
                    if (_scoringConfig.IndividualPoints == null || _scoringConfig.IndividualPoints.Count == 0
                        || _scoringConfig.RelayPoints == null || _scoringConfig.RelayPoints.Count == 0) {
                        _scoringConfig.ResetToDefaults();
                    }
                }
                if (package.Events != null && package.Events.Count > 0) _events = package.Events;
                if (package.AgeGroups != null && package.AgeGroups.Count > 0) _ageGroups = package.AgeGroups;
                if (package.Genders != null && package.Genders.Count > 0) _genders = package.Genders;
                if (package.Stages != null && package.Stages.Count > 0) _stages = package.Stages;
                if (package.HeatCounts != null && package.HeatCounts.Count > 0) _heatCounts = package.HeatCounts;
                AgeGroupRegistry.Set(_ageGroups);
                RefreshEventComboBoxes();
                RefreshEventsPreview();
                RefreshAgeGroupsPreview();
                RefreshDisplayRecordLabelText();
                RefreshGendersPreview();
                RefreshStagesPreview();
                RefreshHeatCountsPreview();
                RefreshAllAgeGroupFilterCombos();
                _bibRanges = package.BibRanges ?? new List<BibRange>();
                _programBook = package.ProgramBook ?? new ProgramBookData();
                _resultBook = package.ResultBook ?? new ResultBookData();
                if (!string.IsNullOrEmpty(package.DisplayRecordLabel)) _displayRecordLabel = package.DisplayRecordLabel;
                if (!string.IsNullOrEmpty(package.DisplayRecordTypeName)) _displayRecordTypeName = package.DisplayRecordTypeName;
                if (package.DisplayRecordOptions != null && package.DisplayRecordOptions.Count > 0) _displayRecordOptions = package.DisplayRecordOptions;
                _confirmedHeats = new HashSet<string>(package.ConfirmedHeats ?? new List<string>());

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
            string portName = ComPortCombo.SelectedItem.ToString();
            int baud = ReadBaudRateFromUi();
            _timingBridge.ConnectSerial(portName, baud);
            AddLog(string.Format("串口连接: {0} @ {1} baud", portName, baud));
            UpdateConnectionStatus();
            // 通讯参数立即落盘 — 下次启动自动还原 UI 选择
            _timingConn.SerialPort = portName;
            _timingConn.SerialBaudRate = baud;
            _timingConn.LastType = "serial";
            SaveTimingConnectionConfig();
        }

        // "比赛控制"面板上的"连接串口"快捷按钮：
        // - 已连接 → 断开
        // - 未连接 → 用 timing_connection.json 里保存的串口/波特率连接；没保存就提示去 系统工作状态 配
        private void QuickConnectSerial_Click(object sender, RoutedEventArgs e) {
            if (_timingBridge != null && _timingBridge.IsConnected) {
                _timingBridge.Disconnect();
                UpdateConnectionStatus();
                UpdateQuickConnectButton();
                AddLog("硬件已断开");
                return;
            }
            string portName = _timingConn != null ? _timingConn.SerialPort : null;
            int baud = _timingConn != null && _timingConn.SerialBaudRate > 0 ? _timingConn.SerialBaudRate : 9600;
            if (string.IsNullOrEmpty(portName)) {
                MessageBox.Show(
                    "未保存默认串口。请先到 \"系统工作状态 → 硬件计时器连接\" 选择 COM 端口和波特率，按一次\"连接串口\"使其落盘，下次再用这个快捷键。",
                    "无默认串口", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try {
                _timingBridge.ConnectSerial(portName, baud);
                AddLog(string.Format("串口快速连接: {0} @ {1} baud", portName, baud));
                _timingConn.LastType = "serial";
                SaveTimingConnectionConfig();
            } catch (Exception ex) {
                AddLog("串口快速连接失败: " + ex.Message);
            }
            UpdateConnectionStatus();
            UpdateQuickConnectButton();
        }

        // 根据当前连接状态刷新比赛控制面板上的"连接串口"按钮文字与颜色
        private void UpdateQuickConnectButton() {
            if (QuickConnectSerialButton == null) return;
            bool connected = _timingBridge != null && _timingBridge.IsConnected;
            QuickConnectSerialButton.Content = connected ? "断开" : "连接串口";
            QuickConnectSerialButton.Background = connected
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
        }

        private int ReadBaudRateFromUi() {
            if (BaudRateCombo == null || BaudRateCombo.SelectedItem == null) {
                return _timingConn != null && _timingConn.SerialBaudRate > 0 ? _timingConn.SerialBaudRate : 9600;
            }
            string txt = (BaudRateCombo.SelectedItem is ComboBoxItem)
                ? ((ComboBoxItem)BaudRateCombo.SelectedItem).Content.ToString()
                : BaudRateCombo.SelectedItem.ToString();
            int v;
            return int.TryParse(txt.Trim(), out v) && v > 0 ? v : 9600;
        }

        private void BaudRate_Changed(object sender, SelectionChangedEventArgs e) {
            // 用户改波特率即落盘；连接已建立时不会立刻断开重连，等下次 ConnectSerial_Click 才生效
            if (!_initialized || _timingConn == null) return;
            int v = ReadBaudRateFromUi();
            if (v > 0 && v != _timingConn.SerialBaudRate) {
                _timingConn.SerialBaudRate = v;
                SaveTimingConnectionConfig();
                AddLog(string.Format("波特率设置已保存: {0}（下次\"连接串口\"生效）", v));
            }
        }

        private void ConnectTcp_Click(object sender, RoutedEventArgs e) {
            string addr = TcpHostBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 5000;
            if (parts.Length > 1) int.TryParse(parts[1], out port);
            _timingBridge.ConnectTcp(host, port);
            UpdateConnectionStatus();
            _timingConn.TcpHost = host;
            _timingConn.TcpPort = port;
            _timingConn.LastType = "tcp";
            SaveTimingConnectionConfig();
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
            _timingConn.UdpListenPort = listenPort;
            _timingConn.UdpSendHost = sendHost ?? "";
            _timingConn.UdpSendPort = sendPort;
            _timingConn.LastType = "udp";
            SaveTimingConnectionConfig();
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

            string ageFilter = RecordFilterAgeGroup != null && RecordFilterAgeGroup.SelectedItem != null ? RecordFilterAgeGroup.SelectedItem.ToString() : "全部";
            string gender = RecordFilterGender.SelectedItem != null ? ((ComboBoxItem)RecordFilterGender.SelectedItem).Content.ToString() : "全部";
            string eventName = RecordFilterEvent.SelectedItem != null ? RecordFilterEvent.SelectedItem.ToString() : "全部";
            string recordType = RecordFilterType.SelectedItem != null ? RecordFilterType.SelectedItem.ToString() : "全部";
            string keyword = RecordFilterKeyword != null ? RecordFilterKeyword.Text.Trim() : "";

            var filtered = new List<SwimmingRecord>();
            foreach (var r in _records) {
                if (ageFilter != "全部" && (r.AgeGroup ?? "") != ageFilter) continue;
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
            if (RecordFilterAgeGroup != null && RecordFilterAgeGroup.Items.Count > 0) RecordFilterAgeGroup.SelectedIndex = 0;
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
                Title = "导入纪录（格式：组别,性别,项目,类型,保持者,代表队,成绩,日期,地点；组别列可省略）"
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

                        // 兼容 9 列（含组别）与 8 列（旧）两种格式：首列值是性别关键字则视为无组别
                        string first = cols[0].Trim();
                        bool hasAge = !(first == "男" || first == "女" || first == "混合");
                        int colIdx = 0;
                        string ageGroup = hasAge ? cols[colIdx++].Trim() : "";
                        if (cols.Length < (hasAge ? 4 : 3)) continue;
                        string gender = cols[colIdx++].Trim();
                        // 规范化项目名：去除多余空格（如"50 米自由泳"→"50米自由泳"）
                        string eventName = System.Text.RegularExpressions.Regex.Replace(cols[colIdx++].Trim(), @"\s+", "");
                        string recordType = cols[colIdx++].Trim();
                        string holderName = cols.Length > colIdx ? cols[colIdx++].Trim() : "";
                        string holderCountry = cols.Length > colIdx ? cols[colIdx++].Trim() : "";
                        string timeStr = cols.Length > colIdx ? cols[colIdx++].Trim() : "";
                        string dateStr = cols.Length > colIdx ? cols[colIdx++].Trim() : "";
                        string location = cols.Length > colIdx ? cols[colIdx++].Trim() : "";

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

                        // 查找是否已存在相同纪录（组别+性别+项目+类型共同决定唯一性）
                        SwimmingRecord existing = null;
                        foreach (var r in _records) {
                            if ((r.AgeGroup ?? "") == (ageGroup ?? "") && r.Gender == gender
                                && r.EventName == eventName && r.RecordType == recordType) {
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
                                AgeGroup = ageGroup, Gender = gender, EventName = eventName, RecordType = recordType,
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
                sb.AppendLine("组别,性别,项目,类型,保持者,代表队,成绩,日期,地点");

                // 已有的纪录数据先填入对应位置
                var existingMap = new Dictionary<string, SwimmingRecord>();
                foreach (var r in _records) {
                    string key = (r.AgeGroup ?? "") + "|" + r.Gender + "|" + r.EventName + "|" + r.RecordType;
                    existingMap[key] = r;
                }

                foreach (string recType in recordTypes) {
                    foreach (string gender in genders) {
                        foreach (string ev in events) {
                            string key = "" + "|" + gender + "|" + ev + "|" + recType;
                            SwimmingRecord existing;
                            if (existingMap.TryGetValue(key, out existing) && existing.Time > 0) {
                                sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                                    existing.AgeGroup ?? "",
                                    gender, ev, recType,
                                    existing.HolderName ?? "", existing.HolderCountry ?? "",
                                    TimeFormatter.Format(existing.Time),
                                    existing.Date ?? "", existing.Location ?? ""));
                            } else {
                                sb.AppendLine(string.Format(",{0},{1},{2},,,,,", gender, ev, recType));
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
        // 比赛参数设置管理：比赛项目 / 组别（数据库持久化 + Excel CSV 导入导出）
        // ═══════════════════════════════════════════════════════════════

        // 若组别为空/"全部"/"不限"，则不过滤（兼容旧逻辑）；否则运动员的 AgeCategory 必须与之一致
        private bool MatchesAgeGroup(Swimmer s, string ageGroup) {
            if (string.IsNullOrEmpty(ageGroup) || ageGroup == "全部" || ageGroup == "不限") return true;
            return (s.AgeCategory ?? "") == ageGroup;
        }

        private void RefreshEventComboBoxes() {
            // 重建依赖 _events 的下拉：RegEventCombo / FilterEventCombo / ResultEventCombo / RecordFilterEvent
            if (RegEventCombo != null) {
                object prev = RegEventCombo.SelectedItem;
                RegEventCombo.Items.Clear();
                foreach (string ev in _events) RegEventCombo.Items.Add(new ComboBoxItem { Content = ev });
                if (RegEventCombo.Items.Count > 0) RegEventCombo.SelectedIndex = 0;
            }
            if (FilterEventCombo != null) {
                FilterEventCombo.Items.Clear();
                FilterEventCombo.Items.Add(new ComboBoxItem { Content = "全部", IsSelected = true });
                foreach (string ev in _events) FilterEventCombo.Items.Add(new ComboBoxItem { Content = ev });
            }
            if (ResultEventCombo != null) {
                string prev = ResultEventCombo.SelectedItem as string;
                ResultEventCombo.Items.Clear();
                foreach (string ev in _events) ResultEventCombo.Items.Add(ev);
                if (!string.IsNullOrEmpty(prev) && ResultEventCombo.Items.Contains(prev))
                    ResultEventCombo.SelectedItem = prev;
                else if (ResultEventCombo.Items.Count > 0) ResultEventCombo.SelectedIndex = 0;
            }
            if (RecordFilterEvent != null) {
                string prev = RecordFilterEvent.SelectedItem as string;
                RecordFilterEvent.Items.Clear();
                RecordFilterEvent.Items.Add("全部");
                foreach (string ev in _events) RecordFilterEvent.Items.Add(ev);
                if (!string.IsNullOrEmpty(prev) && RecordFilterEvent.Items.Contains(prev))
                    RecordFilterEvent.SelectedItem = prev;
                else RecordFilterEvent.SelectedIndex = 0;
            }
        }

        private void RefreshEventsPreview() {
            if (EventsPreviewGrid == null) return;
            var list = new List<object>();
            for (int i = 0; i < _events.Count; i++) list.Add(new { Index = i + 1, Name = _events[i] });
            EventsPreviewGrid.ItemsSource = list;
        }

        private void RefreshAgeGroupsPreview() {
            if (AgeGroupsPreviewGrid == null) return;
            AgeGroupsPreviewGrid.ItemsSource = _ageGroups.Select((g, i) => new { Index = i + 1, g.Name }).ToList();
        }

        // 组别现为人工分类，不再依赖年龄。此方法仅用于兼容性触发，不会覆盖已设置的组别。
        private void RecomputeAllAgeCategories() {
            // 留空：保留方法签名以兼容现有调用点
        }

        // ═══════════════════════════════════════════════════════════════
        // 性别 / 赛次 / 组数 三个简单字符串列表的 编辑/导入/导出/模板（与组别完全一致的交互）
        // ═══════════════════════════════════════════════════════════════
        private void RefreshGendersPreview() {
            if (GendersPreviewGrid == null) return;
            GendersPreviewGrid.ItemsSource = _genders.Select((n, i) => new { Index = i + 1, Name = n }).ToList();
        }
        private void RefreshStagesPreview() {
            if (StagesPreviewGrid == null) return;
            StagesPreviewGrid.ItemsSource = _stages.Select((n, i) => new { Index = i + 1, Name = n }).ToList();
        }
        private void RefreshHeatCountsPreview() {
            if (HeatCountsPreviewGrid == null) return;
            HeatCountsPreviewGrid.ItemsSource = _heatCounts.Select((n, i) => new { Index = i + 1, Name = n }).ToList();
        }

        // 通用：弹出"序号 + 名称"的可编辑列表对话框，确认时回调
        private void EditStringListDialog(string title, string nameHeader, List<string> source, string[] defaults, Action<List<string>> onSave) {
            var working = new System.Collections.ObjectModel.ObservableCollection<EditableNameRow>();
            foreach (var n in source) working.Add(new EditableNameRow { Value = n });

            var dlg = new Window {
                Title = title, Width = 460, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };
            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.Children.Add(new TextBlock {
                Text = string.Format("编辑{0}列表。可增删；确认保存，取消不生效。", nameHeader),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"))
            });

            var grid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                HeadersVisibility = DataGridHeadersVisibility.All,
                RowHeaderWidth = 56
            };
            grid.LoadingRow += delegate(object _s, DataGridRowEventArgs _ev) {
                _ev.Row.Header = (_ev.Row.GetIndex() + 1).ToString();
            };
            grid.Columns.Add(new DataGridTextColumn { Header = nameHeader, Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new System.Windows.Data.Binding("Value") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged } });
            grid.ItemsSource = working;
            working.CollectionChanged += delegate { try { grid.Items.Refresh(); } catch { } };
            Grid.SetRow(grid, 1);
            mainGrid.Children.Add(grid);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(btnPanel, 2);
            var btnAdd = new Button { Content = "新增", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnAdd.Click += delegate { working.Add(new EditableNameRow { Value = "" }); };
            var btnDel = new Button { Content = "删除选中", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnDel.Click += delegate {
                var sel = grid.SelectedItem as EditableNameRow;
                if (sel != null) working.Remove(sel); else MessageBox.Show("请先选中要删除的行");
            };
            var btnOk = new Button { Content = "确认保存", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate {
                var fe = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
                if (fe is TextBox) { var be = fe.GetBindingExpression(TextBox.TextProperty); if (be != null) be.UpdateSource(); }
                try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
                var seen = new HashSet<string>();
                var finalList = new List<string>();
                foreach (var r in working) {
                    string v = (r.Value ?? "").Trim();
                    if (string.IsNullOrEmpty(v)) continue;
                    if (seen.Contains(v)) { MessageBox.Show(string.Format("[{0}] 重复。", v)); return; }
                    seen.Add(v);
                    finalList.Add(v);
                }
                if (finalList.Count == 0) { MessageBox.Show(string.Format("至少保留一项{0}", nameHeader)); return; }
                onSave(finalList);
                dlg.DialogResult = true;
            };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnAdd); btnPanel.Children.Add(btnDel); btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnCancel);
            mainGrid.Children.Add(btnPanel);
            dlg.Content = mainGrid;
            dlg.ShowDialog();
        }

        private class EditableNameRow : INotifyPropertyChanged {
            private string _v;
            public string Value { get { return _v; } set { _v = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value")); } }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        // 通用 CSV 导入/导出
        private void ExportStringListCsv(string title, string fileName, string headerName, List<string> source) {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV文件|*.csv", Title = title, FileName = fileName };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine(headerName);
                foreach (var v in source) sb.AppendLine(CsvEscape(v));
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show(headerName + "表已导出。", "完成");
            } catch (Exception ex) { MessageBox.Show("导出失败: " + ex.Message, "错误"); }
        }
        private void ImportStringListCsv(string title, string headerName, Action<List<string>> onLoaded) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|文本文件|*.txt|所有文件|*.*",
                Title = string.Format("导入{0}表（表头: {0}名称）", headerName)
            };
            if (dlg.ShowDialog() != true) return;
            string ext = IOPath.GetExtension(dlg.FileName).ToLower();
            if (ext == ".xls" || ext == ".xlsx") {
                MessageBox.Show("无法直接读取 Excel 文件。请另存为 CSV UTF-8 后再导入。", "格式提示"); return;
            }
            if (MessageBox.Show("导入将替换当前列表。继续？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try {
                var rows = ReadCsvLines(dlg.FileName);
                var seen = new HashSet<string>();
                var finalList = new List<string>();
                for (int i = 0; i < rows.Count; i++) {
                    var c = rows[i];
                    if (c.Length == 0) continue;
                    string v = (c[0] ?? "").Trim();
                    if (string.IsNullOrEmpty(v)) continue;
                    if (i == 0 && (v == headerName + "名称" || v == "名称" || v == headerName || v == "Name")) continue;
                    if (seen.Contains(v)) continue;
                    seen.Add(v);
                    finalList.Add(v);
                }
                if (finalList.Count == 0) { MessageBox.Show("未读取到有效条目"); return; }
                onLoaded(finalList);
                MessageBox.Show(string.Format("已导入 {0} 个{1}。", finalList.Count, headerName), "完成");
            } catch (Exception ex) { MessageBox.Show("导入失败: " + ex.Message, "错误"); }
        }
        private void DownloadStringListTemplate(string title, string fileName, string headerName, string[] sampleRows) {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV文件|*.csv", Title = title, FileName = fileName };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine(headerName + "名称");
                foreach (var s in sampleRows) sb.AppendLine(s);
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show(headerName + "模板已保存。", "完成");
            } catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message, "错误"); }
        }

        // —— 性别 ——
        private void EditGendersList_Click(object sender, RoutedEventArgs e) {
            EditStringListDialog("性别编辑", "性别名称", _genders, new[] { "男", "女", "混合" }, list => {
                _genders = list; RefreshGendersPreview(); AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("已更新性别列表（{0} 条）", _genders.Count));
            });
        }
        private void ExportGendersCSV_Click(object sender, RoutedEventArgs e) { ExportStringListCsv("导出性别表", "性别表.csv", "性别", _genders); }
        private void ImportGendersCSV_Click(object sender, RoutedEventArgs e) {
            ImportStringListCsv("导入性别表", "性别", list => { _genders = list; RefreshGendersPreview(); AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("导入性别: 共{0}条", _genders.Count)); });
        }
        private void DownloadGendersTemplate_Click(object sender, RoutedEventArgs e) {
            DownloadStringListTemplate("保存性别模板", "性别模板.csv", "性别", new[] { "男", "女", "混合" });
        }

        // —— 赛次 ——
        private void EditStagesList_Click(object sender, RoutedEventArgs e) {
            EditStringListDialog("赛次编辑", "赛次名称", _stages, new[] { "预赛", "半决赛", "决赛" }, list => {
                _stages = list; RefreshStagesPreview(); AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("已更新赛次列表（{0} 条）", _stages.Count));
            });
        }
        private void ExportStagesCSV_Click(object sender, RoutedEventArgs e) { ExportStringListCsv("导出赛次表", "赛次表.csv", "赛次", _stages); }
        private void ImportStagesCSV_Click(object sender, RoutedEventArgs e) {
            ImportStringListCsv("导入赛次表", "赛次", list => { _stages = list; RefreshStagesPreview(); AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("导入赛次: 共{0}条", _stages.Count)); });
        }
        private void DownloadStagesTemplate_Click(object sender, RoutedEventArgs e) {
            DownloadStringListTemplate("保存赛次模板", "赛次模板.csv", "赛次", new[] { "预赛", "半决赛", "决赛", "A决赛", "B决赛" });
        }

        // —— 组数 ——
        private void EditHeatCountsList_Click(object sender, RoutedEventArgs e) {
            EditStringListDialog("组数编辑", "组数", _heatCounts, new[] { "1组", "2组", "3组", "4组", "5组", "6组", "7组", "8组" }, list => {
                _heatCounts = list; RefreshHeatCountsPreview(); AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("已更新组数列表（{0} 条）", _heatCounts.Count));
            });
        }
        private void ExportHeatCountsCSV_Click(object sender, RoutedEventArgs e) { ExportStringListCsv("导出组数表", "组数表.csv", "组数", _heatCounts); }
        private void ImportHeatCountsCSV_Click(object sender, RoutedEventArgs e) {
            ImportStringListCsv("导入组数表", "组数", list => { _heatCounts = list; RefreshHeatCountsPreview(); AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("导入组数: 共{0}条", _heatCounts.Count)); });
        }
        private void DownloadHeatCountsTemplate_Click(object sender, RoutedEventArgs e) {
            DownloadStringListTemplate("保存组数模板", "组数模板.csv", "组数", new[] { "1组", "2组", "3组", "4组", "5组", "6组", "7组", "8组" });
        }

        private void EditEventsList_Click(object sender, RoutedEventArgs e) {
            var working = new System.Collections.ObjectModel.ObservableCollection<EventRow>();
            foreach (var ev in _events) working.Add(new EventRow { Name = ev });

            var dlg = new Window {
                Title = "比赛项目编辑", Width = 480, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };
            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.Children.Add(new TextBlock {
                Text = "直接双击单元格编辑项目名；可新增或删除行。确认后保存到数据库，取消则不生效。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"))
            });

            var grid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
            };
            grid.Columns.Add(new DataGridTextColumn {
                Header = "比赛项目", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new System.Windows.Data.Binding("Name") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }
            });
            grid.ItemsSource = working;
            Grid.SetRow(grid, 1);
            mainGrid.Children.Add(grid);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(btnPanel, 2);

            var btnAdd = new Button { Content = "新增项目", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnAdd.Click += delegate { working.Add(new EventRow { Name = "新项目" }); };
            var btnDel = new Button { Content = "删除选中", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnDel.Click += delegate {
                var sel = grid.SelectedItem as EventRow;
                if (sel != null) working.Remove(sel);
                else MessageBox.Show("请先选中要删除的行");
            };
            var btnOk = new Button { Content = "确认保存", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate {
                var fe = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
                if (fe is TextBox) { var be = fe.GetBindingExpression(TextBox.TextProperty); if (be != null) be.UpdateSource(); }
                try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
                var finalList = new List<string>();
                var seen = new HashSet<string>();
                foreach (var row in working) {
                    string name = (row.Name ?? "").Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (seen.Contains(name)) { MessageBox.Show(string.Format("项目 [{0}] 重复，请合并或删除。", name)); return; }
                    seen.Add(name);
                    finalList.Add(name);
                }
                if (finalList.Count == 0) { MessageBox.Show("至少保留一个比赛项目"); return; }
                _events = finalList;
                RefreshEventsPreview();
                AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("已更新比赛项目列表（{0} 条）", _events.Count));
                dlg.DialogResult = true;
            };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnAdd); btnPanel.Children.Add(btnDel); btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnCancel);
            mainGrid.Children.Add(btnPanel);
            dlg.Content = mainGrid;
            dlg.ShowDialog();
        }

        private class EventRow : INotifyPropertyChanged
        {
            private string _name;
            public string Name { get { return _name; } set { _name = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Name")); } }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        private void EditAgeGroupsList_Click(object sender, RoutedEventArgs e) {
            var working = new System.Collections.ObjectModel.ObservableCollection<AgeGroup>();
            foreach (var g in _ageGroups) working.Add(new AgeGroup { Name = g.Name, MinAge = g.MinAge, MaxAge = g.MaxAge });

            var dlg = new Window {
                Title = "组别编辑", Width = 460, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.CanResize
            };
            var mainGrid = new Grid { Margin = new Thickness(16) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.Children.Add(new TextBlock {
                Text = "编辑组别。组别用于报名/分组等的人工分类（如甲组/乙组/少年/成人），与年龄无关。可增删；确认保存，取消不生效。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"))
            });

            var grid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                HeadersVisibility = DataGridHeadersVisibility.All,
                RowHeaderWidth = 56
            };
            // 序号列：在行头显示 1, 2, 3 ...，添加/删除后自动刷新
            grid.LoadingRow += delegate(object _s, DataGridRowEventArgs _ev) {
                _ev.Row.Header = (_ev.Row.GetIndex() + 1).ToString();
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "组别名称", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new System.Windows.Data.Binding("Name") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged } });
            grid.ItemsSource = working;
            // 添加/删除行后强制刷新行头序号
            working.CollectionChanged += delegate { try { grid.Items.Refresh(); } catch { } };
            Grid.SetRow(grid, 1);
            mainGrid.Children.Add(grid);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(btnPanel, 2);
            var btnAdd = new Button { Content = "新增", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnAdd.Click += delegate { working.Add(new AgeGroup { Name = "新组别", MinAge = 0, MaxAge = 0 }); };
            var btnDel = new Button { Content = "删除选中", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnDel.Click += delegate {
                var sel = grid.SelectedItem as AgeGroup;
                if (sel != null) working.Remove(sel); else MessageBox.Show("请先选中要删除的行");
            };
            var btnOk = new Button { Content = "确认保存", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnOk.Click += delegate {
                var fe = System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;
                if (fe is TextBox) { var be = fe.GetBindingExpression(TextBox.TextProperty); if (be != null) be.UpdateSource(); }
                try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
                var finalList = new List<AgeGroup>();
                var seen = new HashSet<string>();
                foreach (var r in working) {
                    string name = (r.Name ?? "").Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (seen.Contains(name)) { MessageBox.Show(string.Format("组别 [{0}] 重复。", name)); return; }
                    seen.Add(name);
                    finalList.Add(new AgeGroup { Name = name, MinAge = r.MinAge, MaxAge = r.MaxAge });
                }
                if (finalList.Count == 0) { MessageBox.Show("至少保留一个组别"); return; }
                _ageGroups = finalList;
                AgeGroupRegistry.Set(_ageGroups);
                RefreshAgeGroupsPreview();
                RefreshGendersPreview();
                RefreshStagesPreview();
                RefreshHeatCountsPreview();
                RecomputeAllAgeCategories();
                AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("已更新组别列表（{0} 条）", _ageGroups.Count));
                dlg.DialogResult = true;
            };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            btnPanel.Children.Add(btnAdd); btnPanel.Children.Add(btnDel); btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnCancel);
            mainGrid.Children.Add(btnPanel);
            dlg.Content = mainGrid;
            dlg.ShowDialog();
        }

        private void ExportEventsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV文件|*.csv", Title = "导出比赛项目表", FileName = "比赛项目表.csv" };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine("比赛项目");
                foreach (var ev in _events) sb.AppendLine(CsvEscape(ev));
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("比赛项目表已导出。", "完成");
            } catch (Exception ex) { MessageBox.Show("导出失败: " + ex.Message, "错误"); }
        }

        private void ImportEventsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|文本文件|*.txt|所有文件|*.*",
                Title = "导入比赛项目表（首列为项目名，可带表头）"
            };
            if (dlg.ShowDialog() != true) return;
            string ext = IOPath.GetExtension(dlg.FileName).ToLower();
            if (ext == ".xls" || ext == ".xlsx") {
                MessageBox.Show("无法直接读取 Excel 文件。请另存为 CSV UTF-8 后再导入。", "格式提示"); return;
            }
            if (MessageBox.Show("导入将替换当前比赛项目列表。继续？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try {
                var rows = ReadCsvLines(dlg.FileName);
                var finalList = new List<string>();
                var seen = new HashSet<string>();
                for (int i = 0; i < rows.Count; i++) {
                    var c = rows[i];
                    if (c.Length == 0) continue;
                    string name = (c[0] ?? "").Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    // 跳过表头行（如 "比赛项目" / "项目"）
                    if (i == 0 && (name == "比赛项目" || name == "项目" || name == "Event")) continue;
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", "");
                    if (seen.Contains(name)) continue;
                    seen.Add(name);
                    finalList.Add(name);
                }
                if (finalList.Count == 0) { MessageBox.Show("未读取到有效项目行"); return; }
                _events = finalList;
                RefreshEventsPreview();
                AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("导入比赛项目: 共{0}条", _events.Count));
                MessageBox.Show(string.Format("已导入 {0} 条比赛项目。", _events.Count), "完成");
            } catch (Exception ex) { MessageBox.Show("导入失败: " + ex.Message, "错误"); }
        }

        private void DownloadEventsTemplate_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV文件|*.csv", Title = "保存比赛项目模板", FileName = "比赛项目模板.csv" };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine("比赛项目");
                sb.AppendLine("50米自由泳");
                sb.AppendLine("100米自由泳");
                sb.AppendLine("200米自由泳");
                sb.AppendLine("4×50米自由泳接力");
                sb.AppendLine("4×100米混合泳接力");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("比赛项目模板已保存。", "完成");
            } catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message, "错误"); }
        }

        private void ExportAgeGroupsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV文件|*.csv", Title = "导出组别表", FileName = "组别表.csv" };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine("组别名称");
                foreach (var g in _ageGroups) sb.AppendLine(CsvEscape(g.Name));
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("组别表已导出。", "完成");
            } catch (Exception ex) { MessageBox.Show("导出失败: " + ex.Message, "错误"); }
        }

        private void ImportAgeGroupsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|文本文件|*.txt|所有文件|*.*",
                Title = "导入组别表（表头: 组别名称）"
            };
            if (dlg.ShowDialog() != true) return;
            string ext = IOPath.GetExtension(dlg.FileName).ToLower();
            if (ext == ".xls" || ext == ".xlsx") {
                MessageBox.Show("无法直接读取 Excel 文件。请另存为 CSV UTF-8 后再导入。", "格式提示"); return;
            }
            if (MessageBox.Show("导入将替换当前组别列表。继续？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try {
                var rows = ReadCsvLines(dlg.FileName);
                var finalList = new List<AgeGroup>();
                var seen = new HashSet<string>();
                for (int i = 0; i < rows.Count; i++) {
                    var c = rows[i];
                    if (c.Length == 0) continue;
                    string name = (c[0] ?? "").Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    // 跳表头
                    if (i == 0 && (name == "组别名称" || name == "组别")) continue;
                    if (seen.Contains(name)) continue;
                    seen.Add(name);
                    // 兼容旧 CSV：若仍带最小/最大年龄列，读入但忽略（用于运动员自动归类已停用）
                    int minA = 0, maxA = 0;
                    if (c.Length > 1) int.TryParse((c[1] ?? "").Trim(), out minA);
                    if (c.Length > 2) int.TryParse((c[2] ?? "").Trim(), out maxA);
                    finalList.Add(new AgeGroup { Name = name, MinAge = minA, MaxAge = maxA });
                }
                if (finalList.Count == 0) { MessageBox.Show("未读取到有效组别"); return; }
                _ageGroups = finalList;
                AgeGroupRegistry.Set(_ageGroups);
                RefreshAgeGroupsPreview();
                RefreshGendersPreview();
                RefreshStagesPreview();
                RefreshHeatCountsPreview();
                RecomputeAllAgeCategories();
                AutoSaveData();
                NotifyMetadataChanged();
                AddLog(string.Format("导入组别: 共{0}条", _ageGroups.Count));
                MessageBox.Show(string.Format("已导入 {0} 个组别。", _ageGroups.Count), "完成");
            } catch (Exception ex) { MessageBox.Show("导入失败: " + ex.Message, "错误"); }
        }

        private void DownloadAgeGroupsTemplate_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV文件|*.csv", Title = "保存组别模板", FileName = "组别模板.csv" };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine("组别名称");
                sb.AppendLine("甲组");
                sb.AppendLine("乙组");
                sb.AppendLine("丙组");
                sb.AppendLine("丁组");
                sb.AppendLine("戊组");
                sb.AppendLine("己组");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("组别模板已保存。", "完成");
            } catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message, "错误"); }
        }

        // ═══════════════════════════════════════════════════════════════
        // 赛程/分组表 CSV 导入导出（Excel/WPS 可直接打开或另存为 CSV 导入）
        // ═══════════════════════════════════════════════════════════════

        // —— 日程表 CSV ——
        // 列: 单元,日期,时间,性别,项目,阶段,组数
        private static string CsvEscape(string s) {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static List<string[]> ReadCsvLines(string path) {
            Encoding enc = Encoding.Default;
            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) enc = Encoding.UTF8;
            var text = File.ReadAllText(path, enc);
            var rows = new List<string[]>();
            var row = new List<string>();
            var cur = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++) {
                char ch = text[i];
                if (inQuotes) {
                    if (ch == '"') {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = false;
                    } else cur.Append(ch);
                } else {
                    if (ch == '"') inQuotes = true;
                    else if (ch == ',' || ch == '\t') { row.Add(cur.ToString()); cur.Length = 0; }
                    else if (ch == '\r') { }
                    else if (ch == '\n') { row.Add(cur.ToString()); cur.Length = 0; rows.Add(row.ToArray()); row = new List<string>(); }
                    else cur.Append(ch);
                }
            }
            if (cur.Length > 0 || row.Count > 0) { row.Add(cur.ToString()); rows.Add(row.ToArray()); }
            return rows;
        }

        private void ExportScheduleCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV文件|*.csv",
                Title = "导出日程表（Excel/WPS 可直接打开）",
                FileName = (string.IsNullOrEmpty(_competitionName) ? "日程表" : _competitionName) + ".csv"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿'); // UTF-8 BOM so Excel recognizes encoding
                sb.AppendLine("单元,日期,时间,组别,性别,项目,阶段,组数");
                foreach (var s in _schedule) {
                    sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                        s.SessionNumber,
                        CsvEscape(s.Date),
                        CsvEscape(s.Time),
                        CsvEscape(s.AgeGroup),
                        CsvEscape(s.Gender),
                        CsvEscape(s.EventName),
                        CsvEscape(s.Stage),
                        s.HeatCount));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                AddLog(string.Format("已导出日程表 CSV: {0} 条 → {1}", _schedule.Count, dlg.FileName));
                MessageBox.Show("日程表已导出。", "完成");
            } catch (Exception ex) {
                MessageBox.Show("导出失败: " + ex.Message, "错误");
            }
        }

        private void ImportScheduleCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|文本文件|*.txt|所有文件|*.*",
                Title = "导入日程表（表头: 单元,日期,时间,组别,性别,项目,阶段,组数）"
            };
            if (dlg.ShowDialog() != true) return;
            string ext = IOPath.GetExtension(dlg.FileName).ToLower();
            if (ext == ".xls" || ext == ".xlsx") {
                MessageBox.Show("无法直接读取 Excel 文件。请在 Excel/WPS 中另存为 “CSV UTF-8（逗号分隔）(*.csv)” 后再导入。",
                    "格式提示", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            if (_schedule.Count > 0) {
                if (MessageBox.Show(string.Format("当前已有{0}条日程，导入将会清空并替换。继续？", _schedule.Count),
                    "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            }
            try {
                var rows = ReadCsvLines(dlg.FileName);
                if (rows.Count < 2) { MessageBox.Show("CSV 无有效数据"); return; }
                _schedule.Clear();
                int imported = 0, skipped = 0;
                for (int i = 1; i < rows.Count; i++) {
                    var c = rows[i];
                    if (c.Length == 0 || (c.Length == 1 && string.IsNullOrWhiteSpace(c[0]))) { skipped++; continue; }
                    int sessionNum = 0;
                    if (c.Length > 0) int.TryParse((c[0] ?? "").Trim(), out sessionNum);
                    string date = c.Length > 1 ? NormalizeDate((c[1] ?? "").Trim()) : "";
                    string time = c.Length > 2 ? NormalizeTime((c[2] ?? "").Trim()) : "";
                    // 兼容 8 列（含组别）/ 7 列（旧：无组别）两种格式
                    string ageGroup = "";
                    string gender, eventName, stage;
                    int heatCount = 0;
                    if (c.Length >= 8) {
                        ageGroup = (c[3] ?? "").Trim();
                        gender = (c[4] ?? "").Trim();
                        eventName = System.Text.RegularExpressions.Regex.Replace((c[5] ?? "").Trim(), @"\s+", "");
                        stage = (c[6] ?? "").Trim();
                        int.TryParse((c[7] ?? "").Trim(), out heatCount);
                    } else {
                        gender = c.Length > 3 ? (c[3] ?? "").Trim() : "";
                        eventName = c.Length > 4 ? System.Text.RegularExpressions.Regex.Replace((c[4] ?? "").Trim(), @"\s+", "") : "";
                        stage = c.Length > 5 ? (c[5] ?? "").Trim() : "";
                        if (c.Length > 6) int.TryParse((c[6] ?? "").Trim(), out heatCount);
                    }
                    if (string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(gender) || string.IsNullOrEmpty(stage)) { skipped++; continue; }
                    _schedule.Add(new ScheduleItem {
                        SessionNumber = sessionNum, Date = date, Time = time, AgeGroup = ageGroup,
                        Gender = gender, EventName = eventName, Stage = stage,
                        HeatCount = heatCount, IsRelay = eventName.Contains("接力")
                    });
                    imported++;
                }
                AutoSaveData();
                BuildScheduleTree();
                Broadcast();
                AddLog(string.Format("导入日程表: 新增{0}条, 跳过{1}行", imported, skipped));
                MessageBox.Show(string.Format("已导入日程 {0} 条（跳过{1}行）。", imported, skipped), "完成");
            } catch (Exception ex) {
                MessageBox.Show("导入失败: " + ex.Message, "错误");
            }
        }

        private void DownloadScheduleTemplate_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV文件|*.csv",
                Title = "保存日程表模板",
                FileName = "日程表模板.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine("单元,日期,时间,组别,性别,项目,阶段,组数");
                sb.AppendLine("1,2026-04-20,09:00,少年,男,50米自由泳,预赛,4");
                sb.AppendLine("1,2026-04-20,09:15,少年,女,50米自由泳,预赛,4");
                sb.AppendLine("1,2026-04-20,09:30,成人,男,50米自由泳,预赛,4");
                sb.AppendLine("2,2026-04-20,15:00,少年,男,50米自由泳,决赛,1");
                sb.AppendLine("2,2026-04-20,15:10,,男,100米自由泳,决赛,1");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("模板已保存，用 Excel/WPS 打开填写后通过【导入日程表】读入。", "完成");
            } catch (Exception ex) {
                MessageBox.Show("保存失败: " + ex.Message, "错误");
            }
        }

        // —— 分组表 Excel (.xlsx) ——
        // 三个工作表：分组明细 / 分组表(网格) / 填写说明
        private void ExportHeatAssignmentsExcel_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "Excel 工作簿|*.xlsx",
                Title = "导出分组表 (Excel)",
                FileName = (string.IsNullOrEmpty(_competitionName) ? "分组表" : _competitionName) + "_分组表.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var rows = CollectHeatRowsForExport();
                var events = CollectEventInfoForExport();
                var teams = _swimmers.Where(s => !IsRelayMemberNote(s.Notes))
                    .Select(s => s.Country ?? "").Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
                var laneNumbers = (_poolConfig != null && _poolConfig.LaneNumbers != null && _poolConfig.LaneNumbers.Count > 0)
                    ? _poolConfig.LaneNumbers
                    : new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
                HeatExcelService.Export(dlg.FileName, _competitionName,
                    GetDatePickerText(StartDatePicker), GetDatePickerText(EndDatePicker),
                    LocationBox != null ? LocationBox.Text : "", laneNumbers, rows, events, teams);
                AddLog("已导出分组表 Excel → " + dlg.FileName);
                if (MessageBox.Show("分组表已导出。\n\n是否立即打开？", "完成", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(dlg.FileName);
            } catch (Exception ex) {
                MessageBox.Show("导出失败: " + ex.Message, "错误");
            }
        }

        private void ImportHeatAssignmentsExcel_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "Excel 文件|*.xlsx;*.xls",
                Title = "导入分组表 (Excel)"
            };
            if (dlg.ShowDialog() != true) return;

            var prompt = new HeatImportPromptWindow { Owner = this };
            if (prompt.ShowDialog() != true) return;
            bool autoRegister = prompt.AutoRegister;

            try {
                string warning;
                var rows = HeatExcelService.Import(dlg.FileName, out warning);
                if (!string.IsNullOrEmpty(warning)) {
                    if (MessageBox.Show(warning + "\n\n是否仍然继续？", "导入提示", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                }
                if (rows.Count == 0) { MessageBox.Show("Excel 中没有可导入的分组数据。", "提示"); return; }

                int imported = 0, skipped = 0, autoAdded = 0;
                var unmatched = new List<string>();
                var seenKeys = new HashSet<string>();
                foreach (var hr in rows) {
                    if (string.IsNullOrEmpty(hr.Gender) || string.IsNullOrEmpty(hr.EventName) || string.IsNullOrEmpty(hr.Stage) || hr.Heat <= 0) {
                        skipped++; continue;
                    }
                    string key = (hr.AgeGroup ?? "") + "|" + hr.Gender + "|" + hr.EventName + "|" + hr.Stage;
                    if (!seenKeys.Contains(key)) {
                        seenKeys.Add(key);
                        foreach (var sw in _swimmers) {
                            if (sw.Gender != hr.Gender || sw.EventName != hr.EventName) continue;
                            if (!MatchesAgeGroup(sw, hr.AgeGroup)) continue;
                            if (sw.Notes != null && sw.Notes.StartsWith("接力队员")) continue;
                            if (sw.StageAssignments.ContainsKey(hr.Stage)) sw.StageAssignments.Remove(hr.Stage);
                            if (sw.CurrentStage == hr.Stage) { sw.Heat = 0; sw.Lane = 0; }
                        }
                    }

                    bool isRelayEv = hr.EventName.Contains("接力");
                    Swimmer target = null;
                    if (!string.IsNullOrEmpty(hr.BibNumber)) {
                        target = _swimmers.FirstOrDefault(s => s.BibNumber == hr.BibNumber && s.EventName == hr.EventName && s.Gender == hr.Gender
                            && MatchesAgeGroup(s, hr.AgeGroup)
                            && !(isRelayEv && s.Notes != null && s.Notes.StartsWith("接力队员")));
                    }
                    if (target == null && !string.IsNullOrEmpty(hr.Name)) {
                        target = _swimmers.FirstOrDefault(s => s.Name == hr.Name && s.Country == hr.Country
                            && s.EventName == hr.EventName && s.Gender == hr.Gender
                            && MatchesAgeGroup(s, hr.AgeGroup)
                            && !(isRelayEv && s.Notes != null && s.Notes.StartsWith("接力队员")));
                    }
                    if (target == null && autoRegister && !string.IsNullOrEmpty(hr.Name)) {
                        target = new Swimmer {
                            BibNumber = string.IsNullOrEmpty(hr.BibNumber) ? "" : hr.BibNumber,
                            Name = hr.Name, Gender = hr.Gender, Country = hr.Country ?? "",
                            CountryShort = hr.CountryShort ?? "",
                            EventName = hr.EventName,
                            EntryTime = hr.EntryTime ?? "", BirthDate = hr.BirthDate ?? "",
                            Age = hr.Age, Notes = hr.Notes ?? ""
                        };
                        if (!string.IsNullOrEmpty(hr.AgeGroup)) target.AgeCategory = hr.AgeGroup;
                        target.EntryTimeSeconds = TimeFormatter.Parse(target.EntryTime);
                        _swimmers.Add(target);
                        autoAdded++;
                    }
                    if (target == null) {
                        unmatched.Add(string.Format("{0} {1} {2} 第{3}组道{4}: {5} ({6})",
                            hr.Gender, hr.EventName, hr.Stage, hr.Heat, hr.Lane, hr.Name ?? "—", hr.Country ?? "—"));
                        skipped++; continue;
                    }
                    double sec = !string.IsNullOrEmpty(hr.EntryTime) ? TimeFormatter.Parse(hr.EntryTime) : target.EntryTimeSeconds;
                    target.SetStageAssignment(hr.Stage, hr.Heat, hr.Lane, sec, hr.EntryTime);
                    if (target.CurrentStage == hr.Stage) { target.Heat = hr.Heat; target.Lane = hr.Lane; }
                    imported++;
                }

                foreach (var sched in _schedule) {
                    int maxHeat = 0;
                    foreach (var s in _swimmers) {
                        if (s.Gender != sched.Gender || s.EventName != sched.EventName) continue;
                        var sa = s.GetAssignmentForStage(sched.Stage);
                        if (sa != null && sa.Heat > maxHeat) maxHeat = sa.Heat;
                    }
                    if (maxHeat > 0) sched.HeatCount = maxHeat;
                }

                AutoSaveData();
                BuildScheduleTree();
                Broadcast();
                AddLog(string.Format("导入分组表 Excel: 分配{0}条, 跳过{1}行, 自动新建{2}人", imported, skipped, autoAdded));
                string detail = string.Format("已导入分组 {0} 条。", imported);
                if (autoAdded > 0) detail += string.Format("\n已自动注册 {0} 名运动员。", autoAdded);
                if (unmatched.Count > 0) {
                    detail += string.Format("\n有 {0} 行未匹配到运动员（已跳过）：\n  · ", unmatched.Count);
                    detail += string.Join("\n  · ", unmatched.Take(20).ToArray());
                    if (unmatched.Count > 20) detail += "\n  · ... 等共 " + unmatched.Count + " 条";
                }
                MessageBox.Show(detail, "完成");
            } catch (Exception ex) {
                MessageBox.Show("导入失败: " + ex.Message, "错误");
            }
        }

        private void DownloadHeatAssignmentsExcelTemplate_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "Excel 工作簿|*.xlsx",
                Title = "保存分组表 Excel 模板",
                FileName = "分组表_Excel模板.xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var laneNumbers = (_poolConfig != null && _poolConfig.LaneNumbers != null && _poolConfig.LaneNumbers.Count > 0)
                    ? _poolConfig.LaneNumbers
                    : new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
                HeatExcelService.WriteTemplate(dlg.FileName, laneNumbers,
                    CollectEventInfoForExport(),
                    _swimmers.Where(s => !IsRelayMemberNote(s.Notes)).Select(s => s.Country ?? "")
                        .Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList());
                if (MessageBox.Show("模板已保存：\n" + dlg.FileName + "\n\n是否立即打开？", "完成",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                    System.Diagnostics.Process.Start(dlg.FileName);
                }
            } catch (Exception ex) {
                MessageBox.Show("保存失败: " + ex.Message, "错误");
            }
        }

        // —— 收集导出数据 ——
        private List<HeatExcelService.HeatRow> CollectHeatRowsForExport() {
            var rows = new List<HeatExcelService.HeatRow>();
            // 项目编号映射（与秩序册/竞赛日程一致）
            var evtMap = BuildEventNumberMap();
            // 场次序号查表：由 schedule.SessionNumber 决定
            for (int i = 0; i < _schedule.Count; i++) {
                var schedItem = _schedule[i];
                string ageGroup = schedItem.AgeGroup ?? "";
                string gender = schedItem.Gender, ev = schedItem.EventName, stage = schedItem.Stage;
                bool isRelay = ev.Contains("接力");
                int evNo;
                evtMap.TryGetValue((gender ?? "") + "|" + (ev ?? ""), out evNo);
                string period = ParseSessionPeriod(schedItem.Time);
                foreach (var s in _swimmers) {
                    if (s.Gender != gender || s.EventName != ev) continue;
                    if (!MatchesAgeGroup(s, ageGroup)) continue;
                    if (isRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队员")) continue;
                    var sa = s.GetAssignmentForStage(stage);
                    int h = 0, ln = 0; string seed = "";
                    if (sa != null && sa.Heat > 0) { h = sa.Heat; ln = sa.Lane; seed = sa.EntryTime ?? s.EntryTime ?? ""; }
                    else if (s.CurrentStage == stage && s.Heat > 0) { h = s.Heat; ln = s.Lane; seed = s.EntryTime ?? ""; }
                    if (h <= 0) continue;
                    rows.Add(new HeatExcelService.HeatRow {
                        SessionNumber = schedItem.SessionNumber,
                        Date = schedItem.Date ?? "",
                        SessionPeriod = period,
                        EventNumber = evNo,
                        AgeGroup = ageGroup,
                        Gender = gender,
                        EventName = ev,
                        Stage = stage,
                        SortMethod = "按成绩排名",
                        Heat = h, Lane = ln,
                        BibNumber = s.BibNumber ?? "",
                        Name = s.Name ?? "",
                        Country = s.Country ?? "",
                        CountryShort = s.CountryShort ?? "",
                        EntryTime = seed ?? "",
                        BirthDate = s.BirthDate ?? "",
                        Age = s.Age,
                        Notes = s.Notes ?? ""
                    });
                }
            }
            rows.Sort((a, b) => {
                int c = a.SessionNumber.CompareTo(b.SessionNumber);
                if (c != 0) return c;
                c = a.EventNumber.CompareTo(b.EventNumber);
                if (c != 0) return c;
                c = a.Heat.CompareTo(b.Heat);
                if (c != 0) return c;
                return a.Lane.CompareTo(b.Lane);
            });
            return rows;
        }

        private List<HeatExcelService.EventInfo> CollectEventInfoForExport() {
            var evtMap = BuildEventNumberMap();
            var list = new List<HeatExcelService.EventInfo>();
            foreach (var s in _schedule) {
                int n;
                evtMap.TryGetValue((s.Gender ?? "") + "|" + (s.EventName ?? ""), out n);
                list.Add(new HeatExcelService.EventInfo {
                    Number = n, AgeGroup = s.AgeGroup ?? "", Gender = s.Gender ?? "",
                    EventName = s.EventName ?? "", Stage = s.Stage ?? "",
                    SortMethod = "按成绩排名", HeatCount = s.HeatCount
                });
            }
            return list;
        }

        private static string ParseSessionPeriod(string time) {
            if (string.IsNullOrEmpty(time)) return "";
            var m = System.Text.RegularExpressions.Regex.Match(time, @"^(\d{1,2})");
            if (!m.Success) return "";
            int hh; if (!int.TryParse(m.Groups[1].Value, out hh)) return "";
            if (hh < 12) return "上午";
            if (hh < 18) return "下午";
            return "晚上";
        }

        // —— 旧 CSV 导出（已弃用，保留方法以避免事件处理器引用问题）——
        private void ExportHeatAssignmentsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV文件|*.csv",
                Title = "导出分组表",
                FileName = (string.IsNullOrEmpty(_competitionName) ? "分组表" : _competitionName) + "_分组表.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine("组别,性别,项目,阶段,组号,道次,参赛号,姓名,代表队,报名成绩");
                // 按赛程顺序输出，保证与日程表对应
                foreach (var schedItem in _schedule) {
                    string ageGroup = schedItem.AgeGroup ?? "";
                    string gender = schedItem.Gender, eventName = schedItem.EventName, stage = schedItem.Stage;
                    bool isRelayEv = eventName.Contains("接力");
                    // 收集该项目该赛次的分组信息（按组别过滤）
                    var rows = new List<Tuple<int, int, Swimmer, string>>(); // heat, lane, sw, seedTime
                    foreach (var s in _swimmers) {
                        if (s.Gender != gender || s.EventName != eventName) continue;
                        if (!MatchesAgeGroup(s, ageGroup)) continue;
                        if (isRelayEv && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队员")) continue;
                        var sa = s.GetAssignmentForStage(stage);
                        int h = 0, ln = 0; string seed = "";
                        if (sa != null && sa.Heat > 0) { h = sa.Heat; ln = sa.Lane; seed = sa.EntryTime ?? s.EntryTime ?? ""; }
                        else if (s.CurrentStage == stage && s.Heat > 0) { h = s.Heat; ln = s.Lane; seed = s.EntryTime ?? ""; }
                        if (h <= 0) continue;
                        rows.Add(Tuple.Create(h, ln, s, seed));
                    }
                    rows.Sort((a, b) => { int c = a.Item1.CompareTo(b.Item1); return c != 0 ? c : a.Item2.CompareTo(b.Item2); });
                    foreach (var r in rows) {
                        var s = r.Item3;
                        string displayName = s.Name ?? "";
                        if (isRelayEv && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                            displayName = s.Name ?? "";
                        sb.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                            CsvEscape(ageGroup), CsvEscape(gender), CsvEscape(eventName), CsvEscape(stage),
                            r.Item1, r.Item2,
                            CsvEscape(s.BibNumber ?? ""), CsvEscape(displayName),
                            CsvEscape(s.Country ?? ""), CsvEscape(r.Item4 ?? "")));
                    }
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                AddLog("已导出分组表 CSV → " + dlg.FileName);
                MessageBox.Show("分组表已导出。", "完成");
            } catch (Exception ex) {
                MessageBox.Show("导出失败: " + ex.Message, "错误");
            }
        }

        private void ImportHeatAssignmentsCSV_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "CSV文件|*.csv|文本文件|*.txt|所有文件|*.*",
                Title = "导入分组表（表头: 组别,性别,项目,阶段,组号,道次,参赛号,姓名,代表队,报名成绩）"
            };
            if (dlg.ShowDialog() != true) return;
            string ext = IOPath.GetExtension(dlg.FileName).ToLower();
            if (ext == ".xls" || ext == ".xlsx") {
                MessageBox.Show("无法直接读取 Excel 文件。请另存为 “CSV UTF-8（逗号分隔）(*.csv)” 后再导入。",
                    "格式提示", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            if (MessageBox.Show("导入分组表将覆盖相应项目/赛次的现有分组。继续？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            try {
                var rows = ReadCsvLines(dlg.FileName);
                if (rows.Count < 2) { MessageBox.Show("CSV 无有效数据"); return; }

                // 按 (gender, event, stage) 分组：清空后再填充
                var seen = new HashSet<string>();
                int imported = 0, skipped = 0, notFound = 0;
                for (int i = 1; i < rows.Count; i++) {
                    var c = rows[i];
                    if (c.Length < 6) { skipped++; continue; }
                    // 兼容 10 列（含组别）/ 9 列（旧：无组别）两种格式：通过识别首列值判断
                    string first = (c[0] ?? "").Trim();
                    bool hasAge = !(first == "男" || first == "女" || first == "混合");
                    int col = 0;
                    string ageGroup = hasAge ? (c[col++] ?? "").Trim() : "";
                    if (c.Length < (hasAge ? 7 : 6)) { skipped++; continue; }
                    string gender = (c[col++] ?? "").Trim();
                    string eventName = System.Text.RegularExpressions.Regex.Replace((c[col++] ?? "").Trim(), @"\s+", "");
                    string stage = (c[col++] ?? "").Trim();
                    int heat = 0, lane = 0;
                    int.TryParse((c[col++] ?? "").Trim(), out heat);
                    int.TryParse((c[col++] ?? "").Trim(), out lane);
                    string bib = (c[col++] ?? "").Trim();
                    string name = c.Length > col ? (c[col++] ?? "").Trim() : "";
                    string country = c.Length > col ? (c[col++] ?? "").Trim() : "";
                    string seedTime = c.Length > col ? (c[col++] ?? "").Trim() : "";

                    if (string.IsNullOrEmpty(gender) || string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(stage) || heat <= 0) {
                        skipped++; continue;
                    }

                    // 首次遇到 (组别,gender,event,stage) → 清空其现有分组
                    string key = (ageGroup ?? "") + "|" + gender + "|" + eventName + "|" + stage;
                    if (!seen.Contains(key)) {
                        seen.Add(key);
                        foreach (var s in _swimmers) {
                            if (s.Gender != gender || s.EventName != eventName) continue;
                            if (!MatchesAgeGroup(s, ageGroup)) continue;
                            if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                            if (s.StageAssignments.ContainsKey(stage)) s.StageAssignments.Remove(stage);
                            if (s.CurrentStage == stage) { s.Heat = 0; s.Lane = 0; }
                        }
                    }

                    // 定位运动员：优先按参赛号，其次按姓名+代表队（都要满足组别）
                    Swimmer sw = null;
                    bool isRelayEv = eventName.Contains("接力");
                    if (!string.IsNullOrEmpty(bib)) {
                        sw = _swimmers.FirstOrDefault(s => s.BibNumber == bib && s.EventName == eventName && s.Gender == gender
                            && MatchesAgeGroup(s, ageGroup)
                            && !(isRelayEv && s.Notes != null && s.Notes.StartsWith("接力队员")));
                    }
                    if (sw == null && !string.IsNullOrEmpty(name)) {
                        sw = _swimmers.FirstOrDefault(s => s.Name == name && s.Country == country
                            && s.EventName == eventName && s.Gender == gender
                            && MatchesAgeGroup(s, ageGroup)
                            && !(isRelayEv && s.Notes != null && s.Notes.StartsWith("接力队员")));
                    }
                    if (sw == null) { notFound++; continue; }

                    double sec = !string.IsNullOrEmpty(seedTime) ? TimeFormatter.Parse(seedTime) : sw.EntryTimeSeconds;
                    sw.SetStageAssignment(stage, heat, lane, sec, seedTime);
                    if (sw.CurrentStage == stage) { sw.Heat = heat; sw.Lane = lane; }
                    imported++;
                }

                // 更新各项目的 HeatCount（按导入后最大组号）
                foreach (var sched in _schedule) {
                    int maxHeat = 0;
                    foreach (var s in _swimmers) {
                        if (s.Gender != sched.Gender || s.EventName != sched.EventName) continue;
                        var sa = s.GetAssignmentForStage(sched.Stage);
                        if (sa != null && sa.Heat > maxHeat) maxHeat = sa.Heat;
                    }
                    if (maxHeat > 0) sched.HeatCount = maxHeat;
                }

                AutoSaveData();
                BuildScheduleTree();
                Broadcast();
                AddLog(string.Format("导入分组表: 分配{0}条, 跳过{1}行, 未匹配{2}人", imported, skipped, notFound));
                string note = notFound > 0 ? string.Format("\n有 {0} 行未匹配到运动员（参赛号或姓名+代表队不符，已跳过）。", notFound) : "";
                MessageBox.Show(string.Format("已导入分组 {0} 条。{1}", imported, note), "完成");
            } catch (Exception ex) {
                MessageBox.Show("导入失败: " + ex.Message, "错误");
            }
        }

        private void DownloadHeatAssignmentsTemplate_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = "CSV文件|*.csv",
                Title = "保存分组表模板",
                FileName = "分组表模板.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try {
                var sb = new StringBuilder();
                sb.Append('﻿');
                sb.AppendLine("组别,性别,项目,阶段,组号,道次,参赛号,姓名,代表队,报名成绩");
                sb.AppendLine("少年,男,50米自由泳,预赛,1,4,001,张三,北京,0:23.45");
                sb.AppendLine("少年,男,50米自由泳,预赛,1,5,002,李四,上海,0:23.60");
                sb.AppendLine("成人,男,50米自由泳,预赛,2,4,003,王五,广东,0:24.10");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("模板已保存，用 Excel/WPS 打开填写后通过【导入分组表】读入。\n\n" +
                    "说明：\n• 参赛号或 “姓名+代表队” 任一能匹配到已注册运动员即可\n• 导入会覆盖相应项目/赛次的所有分组",
                    "完成");
            } catch (Exception ex) {
                MessageBox.Show("保存失败: " + ex.Message, "错误");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 文档打印
        // ═══════════════════════════════════════════════════════════════
        private void PrintSchedule_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("竞赛日程", BuildScheduleHtml()); }
        private void PrintManual_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("秩序册", BuildManualHtml()); }

        private void EditProgramBook_Click(object sender, RoutedEventArgs e) {
            // 预览回调：用窗口当前快照临时替换 _programBook 渲染，再恢复
            ProgramBookData backup = _programBook;
            ProgramBookEditWindow win = null;
            Func<List<string>> teamProvider = () =>
                _swimmers.Where(s => !IsRelayMemberNote(s.Notes))
                    .Select(s => s.Country ?? "")
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct().OrderBy(c => c).ToList();
            Func<string> previewProvider = () => {
                if (win != null && win.Result != null) _programBook = win.Result;
                try { return BuildManualHtml(); }
                finally { _programBook = backup; }
            };
            win = new ProgramBookEditWindow(_programBook, teamProvider, previewProvider) { Owner = this };
            if (win.ShowDialog() == true && win.Saved && win.Result != null) {
                _programBook = win.Result;
                AutoSaveData();
                AddLog("秩序册自定义内容已保存。");
            }
        }
        private void PrintStartList_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("出发表", BuildStartListHtml()); }
        private void PrintHeatResults_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("分组成绩", BuildHeatResultsHtml()); }

        // 比赛控制 Tab "确认本组成绩"下方的"打印成绩"按钮：
        // - 当前组成绩已确认 → 直接打印当前组成绩单
        // - 仍在比赛 / 未确认 → 弹窗提示"正在比赛中，不能打印，请稍后"
        private void PrintCurrentHeatResult_Click(object sender, RoutedEventArgs e) {
            // 当前组是否已确认：以 _resultConfirmed（本次刚确认）或 _confirmedHeats（历史已确认）为准
            bool confirmed = _resultConfirmed
                || _confirmedHeats.Contains(ConfirmedHeatKey(_currentAgeGroup, _currentGender, _currentEvent, _currentStage, _currentHeat));
            if (!confirmed) {
                MessageBox.Show("正在比赛中，不能打印，请稍后。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            GenerateAndOpenDocument("分组成绩", BuildHeatResultsHtml());
        }
        private void PrintEventResults_Click(object sender, RoutedEventArgs e) {
            var win = new EventResultPrintWindow(_swimmers, _schedule, _competitionName,
                LocationBox.Text, RefereeBox.Text, ChiefJudgeBox.Text, StarterBox.Text);
            win.Owner = this;
            win.ShowDialog();
        }
        private void PrintFullResultBook_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("成绩册", BuildFullResultBookHtml()); }

        private void EditResultBook_Click(object sender, RoutedEventArgs e) {
            ResultBookData backup = _resultBook;
            ResultBookEditWindow win = null;
            Func<List<ResultBookSwimmerInfo>> swimmerProvider = () =>
                _swimmers.Where(s => !IsRelayMemberNote(s.Notes))
                    .OrderBy(s => s.Country ?? "").ThenBy(s => s.Name ?? "")
                    .Select(s => new ResultBookSwimmerInfo {
                        BibNumber = s.BibNumber ?? "",
                        Name = s.Name ?? "",
                        Country = s.Country ?? "",
                        Coach = "",
                        JointTrainingUnit = s.CountryShort ?? ""
                    }).ToList();
            Func<string> previewProvider = () => {
                if (win != null && win.Result != null) _resultBook = win.Result;
                try { return BuildFullResultBookHtml(); }
                finally { _resultBook = backup; }
            };
            win = new ResultBookEditWindow(_resultBook, swimmerProvider, previewProvider) { Owner = this };
            if (win.ShowDialog() == true && win.Saved && win.Result != null) {
                _resultBook = win.Result;
                AutoSaveData();
                AddLog("成绩册自定义内容已保存。");
                if (win.RequestPrint) {
                    GenerateAndOpenDocument("成绩册", BuildFullResultBookHtml());
                } else if (win.RequestExportDoc) {
                    ExportResultBookAsDoc();
                }
            }
        }

        private void ExportResultBookAsDoc() {
            try {
                var dlg = new Microsoft.Win32.SaveFileDialog {
                    Filter = "Word 文档|*.doc|HTML 文件|*.html",
                    FileName = (_competitionName ?? "成绩册") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".doc",
                    Title = "导出成绩册为 DOC / HTML"
                };
                if (dlg.ShowDialog() != true) return;
                string html = BuildFullResultBookHtml();
                File.WriteAllText(dlg.FileName, html, System.Text.Encoding.UTF8);
                AddLog("成绩册已导出: " + dlg.FileName);
                if (MessageBox.Show("导出完成，是否立即打开？", "成绩册导出", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                    System.Diagnostics.Process.Start(dlg.FileName);
                }
            } catch (Exception ex) {
                MessageBox.Show("导出失败：" + ex.Message);
            }
        }
        private void PrintTeamStandings_Click(object sender, RoutedEventArgs e) { CalculateTeamScores(); GenerateAndOpenDocument("团体成绩", BuildTeamStandingsHtml()); }
        private void PrintRecordReport_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("纪录报告", BuildRecordReportHtml()); }
        private void PrintAwardCertificate_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("奖状", BuildAwardCertificateHtml()); }
        private void PrintRecordCertificate_Click(object sender, RoutedEventArgs e) { GenerateAndOpenDocument("纪录证书", BuildRecordCertificateHtml()); }
        // "分段计时报告"按钮：弹出已完赛组次树（与赛程导航同结构），选择后打印对应组的分段计时；取消则不打印
        private void PrintSplitTimeReport_Click(object sender, RoutedEventArgs e) {
            var picked = ShowConfirmedHeatPicker("选择已完赛组次 — 分段计时报告");
            if (picked == null) return;     // 取消
            GenerateAndOpenDocument("分段计时报告",
                BuildSplitTimeReportHtmlFor(picked.AgeGroup, picked.Gender, picked.EventName, picked.Stage, picked.Heat));
        }

        private class ConfirmedHeatPick {
            public string AgeGroup; public string Gender; public string EventName; public string Stage; public int Heat;
        }

        private ConfirmedHeatPick ShowConfirmedHeatPicker(string title) {
            // 构造仅含已完赛组的 TreeView（结构与"赛程导航"一致：单元 → 项目 → 第N组）
            ConfirmedHeatPick selected = null;
            var dlg = new Window {
                Title = title, Width = 500, Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                ResizeMode = ResizeMode.CanResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
            };
            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var hint = new TextBlock {
                Text = "请选择已完赛的组次（仅显示按下\"确认本组成绩\"的组）：",
                Margin = new Thickness(0, 0, 0, 8), FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"))
            };
            Grid.SetRow(hint, 0); grid.Children.Add(hint);

            var tv = new TreeView { Background = Brushes.White };
            int totalConfirmed = 0;
            var sessions = _schedule.GroupBy(s => s.SessionNumber).OrderBy(g => g.Key);
            foreach (var session in sessions) {
                var sessionEvents = new List<TreeViewItem>();
                foreach (var ev in session) {
                    string ag = ev.AgeGroup ?? "";
                    int heatCount = ev.HeatCount > 0 ? ev.HeatCount : 1;
                    var heatNodes = new List<TreeViewItem>();
                    for (int h = 1; h <= heatCount; h++) {
                        if (!IsHeatConfirmed(ag, ev.Gender, ev.EventName, ev.Stage, h)) continue;
                        var heatNode = new TreeViewItem {
                            Header = string.Format("第{0}组 [已完赛]", h),
                            Tag = new ConfirmedHeatPick { AgeGroup = ag, Gender = ev.Gender, EventName = ev.EventName, Stage = ev.Stage, Heat = h },
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
                        };
                        heatNodes.Add(heatNode);
                        totalConfirmed++;
                    }
                    if (heatNodes.Count == 0) continue;
                    string evHeader = (string.IsNullOrEmpty(ag) ? "" : ("[" + ag + "] "))
                        + string.Format("{0} {1} {2}", ev.Gender, ev.EventName, ev.Stage);
                    var evItem = new TreeViewItem { Header = evHeader, IsExpanded = true,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF")) };
                    foreach (var n in heatNodes) evItem.Items.Add(n);
                    sessionEvents.Add(evItem);
                }
                if (sessionEvents.Count == 0) continue;
                var sessionItem = new TreeViewItem {
                    Header = session.First().SessionName ?? string.Format("第{0}单元", session.Key),
                    IsExpanded = true,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
                    FontWeight = FontWeights.Bold
                };
                foreach (var e2 in sessionEvents) sessionItem.Items.Add(e2);
                tv.Items.Add(sessionItem);
            }
            if (totalConfirmed == 0) {
                var empty = new TextBlock {
                    Text = "暂无已完赛组次。请在\"比赛控制\"中按下\"确认本组成绩\"后再来打印。",
                    Margin = new Thickness(20, 30, 20, 0), FontSize = 14,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(empty, 1); grid.Children.Add(empty);
            } else {
                Grid.SetRow(tv, 1); grid.Children.Add(tv);
            }

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnCancel = new Button { Content = "取消", Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = Brushes.White,
                BorderThickness = new Thickness(0) };
            btnCancel.Click += delegate { dlg.DialogResult = false; };
            var btnOK = new Button { Content = "确定", Padding = new Thickness(20, 6, 20, 6), FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")), Foreground = Brushes.White,
                BorderThickness = new Thickness(0) };
            btnOK.Click += delegate {
                var sel = tv.SelectedItem as TreeViewItem;
                if (sel == null || !(sel.Tag is ConfirmedHeatPick)) {
                    MessageBox.Show("请先在树中选择一个【第N组 [已完赛]】节点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                selected = (ConfirmedHeatPick)sel.Tag;
                dlg.DialogResult = true;
            };
            btnPanel.Children.Add(btnCancel); btnPanel.Children.Add(btnOK);
            Grid.SetRow(btnPanel, 2); grid.Children.Add(btnPanel);

            dlg.Content = grid;
            return dlg.ShowDialog() == true ? selected : null;
        }

        // 指定组次的"分段计时报告"HTML（v2026.05 新增；不依赖 _current* 全局状态）
        private string BuildSplitTimeReportHtmlFor(string ageGroup, string gender, string eventName, string stage, int heat) {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());
            sb.Append(DocHeader("分 段 计 时 报 告"));

            string evTitleAg = string.IsNullOrEmpty(ageGroup) ? "" : ("[" + ageGroup + "] ");
            string eventTitle = string.Format("{0}{1} {2} {3} 第 {4} 组", evTitleAg, gender, eventName, stage, heat);
            sb.AppendFormat("<h3>项目：{0}</h3>", System.Net.WebUtility.HtmlEncode(eventTitle));

            var sch = _schedule.FirstOrDefault(s =>
                (s.AgeGroup ?? "") == (ageGroup ?? "") && s.Gender == gender && s.EventName == eventName && s.Stage == stage);
            string dateTimeInfo = sch != null ? string.Format("{0} {1}", sch.Date, sch.Time).Trim() : "（时间待定）";
            sb.AppendFormat("<h4>比赛时间：{0} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{1}</h4>",
                System.Net.WebUtility.HtmlEncode(dateTimeInfo),
                System.Net.WebUtility.HtmlEncode(LocationBox != null ? LocationBox.Text : ""));

            bool isRelay = (eventName ?? "").Contains("接力");
            var swimmers = _swimmers.Where(s => {
                if (s.EventName != eventName) return false;
                if (s.Gender != gender && !s.Gender.StartsWith(gender) && !gender.StartsWith(s.Gender)) return false;
                if (!MatchesAgeGroup(s, ageGroup)) return false;
                if (isRelay && s.Notes != null && s.Notes.StartsWith("接力队员")) return false;
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat == heat) return true;
                return s.CurrentStage == stage && s.Heat == heat;
            }).OrderBy(s => {
                var sa = s.GetAssignmentForStage(stage);
                return sa != null ? sa.Lane : s.Lane;
            }).ToList();

            // 把同组所有运动员的分段成绩合并到一张表：每位一行，每段距离一列。
            // 单元格双行：上行=累计时间（粗体），下行=本段时间（小字灰色，括号包裹）。
            sb.Append(BuildMergedSplitsTableHtml(swimmers, stage, heat, isRelay));
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        // 合并表渲染：把指定组的所有运动员分段成绩拼成一张表
        private string BuildMergedSplitsTableHtml(List<Swimmer> swimmers, string stage, int heat, bool isRelay) {
            var sb = new StringBuilder();
            // 找参考运动员（分段最齐全者）来确定列：所有出现过的距离的并集，按升序
            var distSet = new SortedSet<int>();
            var rowData = new List<Tuple<Swimmer, LaneResult>>();
            foreach (var sw in swimmers) {
                var result = sw.Results.FirstOrDefault(r => r.Stage == stage && r.Heat == heat);
                if (result == null || result.Splits.Count == 0) continue;
                rowData.Add(Tuple.Create(sw, result));
                foreach (var sp in result.Splits) if (sp.Distance > 0) distSet.Add(sp.Distance);
            }
            if (rowData.Count == 0) {
                sb.Append("<p style='text-align:center;color:#94a3b8;margin-top:40px;'>本组暂无分段计时数据。</p>");
                return sb.ToString();
            }
            var dists = distSet.ToList();

            sb.Append("<table style='margin-top:20px;'>");
            sb.Append("<tr>");
            sb.Append("<th width='40'>道</th>");
            sb.AppendFormat("<th width='110'>{0}</th>", isRelay ? "代表队" : "姓名");
            sb.AppendFormat("<th width='110'>{0}</th>", isRelay ? "队员" : "代表队");
            foreach (var d in dists) sb.AppendFormat("<th width='80'>{0}m</th>", d);
            sb.Append("<th width='90'>最终成绩</th></tr>");

            foreach (var pair in rowData) {
                var sw = pair.Item1;
                var result = pair.Item2;
                int dispLane = result.Lane > 0 ? result.Lane : sw.Lane;
                string spName = sw.Name;
                if (isRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                    spName = sw.Notes.Substring("接力队 棒次:".Length);
                string col1 = isRelay ? (sw.Country ?? "") : (spName ?? "");
                string col2 = isRelay ? (spName ?? "") : (sw.Country ?? "");
                sb.AppendFormat("<tr><td style='text-align:center;font-weight:bold;'>{0}</td><td><b>{1}</b></td><td>{2}</td>",
                    dispLane,
                    System.Net.WebUtility.HtmlEncode(col1),
                    System.Net.WebUtility.HtmlEncode(col2));
                foreach (var d in dists) {
                    var split = result.Splits.FirstOrDefault(sp => sp.Distance == d);
                    if (split == null) {
                        sb.Append("<td style='text-align:center;color:#cbd5e1;'>—</td>");
                    } else {
                        // 累计时间（粗体上行）+ 本段时间（小字下行带括号）
                        sb.AppendFormat("<td style='text-align:center;font-family:Consolas,monospace;'><b>{0}</b><br><span style='font-size:10px;color:#64748b;'>({1})</span></td>",
                            TimeFormatter.Format(split.CumulativeTime), TimeFormatter.Format(split.Time));
                    }
                }
                sb.AppendFormat("<td style='text-align:center;font-weight:bold;background:#eff6ff;font-family:Consolas,monospace;'>{0}</td>",
                    TimeFormatter.Format(result.FinalTime));
                sb.Append("</tr>");
            }
            sb.Append("</table>");
            sb.Append("<p style='font-size:11px;color:#64748b;margin-top:6px;'>说明：单元格上行为<b>累计时间</b>，下行为<i>本段时间</i>。</p>");
            return sb.ToString();
        }

        private void GenerateAndOpenDocument(string title, string html) {
            // 同时存档到 Documents 目录，并在文档预览/输出对话框中打开
            try {
                string dir = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "Documents");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string filePath = IOPath.Combine(dir, title + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
                File.WriteAllText(filePath, html, Encoding.UTF8);
                AddLog("已生成文档: " + title + "（" + filePath + "）");
            } catch (Exception ex) {
                AddLog("文档存档失败: " + ex.Message);
            }
            try {
                var win = new DocumentPreviewWindow(title, html) { Owner = this };
                win.Show();
            } catch (Exception ex) {
                AddLog("文档预览失败: " + ex.Message);
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
                + ".cover{display:flex; flex-direction:column; justify-content:space-between; min-height:1100px; padding:80px 60px;} "
                + ".cover-top{text-align:center;} "
                + ".cover-top .ttl1{font-size:44px; font-family:'SimHei'; letter-spacing:10px; color:#1e3a8a; margin-bottom:8px;} "
                + ".cover-top .ttl2{font-size:30px; font-family:'SimHei'; letter-spacing:6px; color:#0f172a;} "
                + ".cover-mid{flex:1; display:flex; align-items:center; justify-content:center;} "
                + ".cover-mid .name{font-size:60px; font-family:'SimHei'; letter-spacing:14px; color:#1e40af; text-align:center; line-height:1.4;} "
                + ".cover-bot{font-size:18px; line-height:2.2; border-top:3px solid #1e40af; padding-top:18px;} "
                + ".cover-bot .row{display:flex;} "
                + ".cover-bot .lbl{flex:0 0 110px; color:#475569; font-weight:bold;} "
                + ".cover-bot .val{flex:1; color:#0f172a;} "
                + ".toc{font-size:18px; line-height:2.4; max-width:560px; margin:30px auto;} "
                + ".toc .row{display:flex; border-bottom:1px dotted #94a3b8;} "
                + ".toc .row span:first-child{flex:1;} "
                + ".section-tag{display:inline-block; background:#1e40af; color:#fff; padding:2px 10px; font-size:14px; margin-right:10px; font-family:'SimHei'; letter-spacing:2px;} "
                + ".team-block{margin-bottom:18px; padding:14px 16px; border:1px solid #cbd5e1; border-radius:4px; background:#f8fafc; page-break-inside:avoid;} "
                + ".team-block .team-name{font-size:18px; font-family:'SimHei'; color:#1e40af; margin-bottom:8px; padding-bottom:6px; border-bottom:1px dashed #94a3b8;} "
                + ".team-block .role{display:flex; font-size:14px; line-height:1.9;} "
                + ".team-block .role .lbl{flex:0 0 84px; color:#475569; font-weight:bold;} "
                + ".team-block .role .vals{flex:1; color:#0f172a;} "
                + ".kv{display:inline-block; min-width:130px; margin:2px 18px 2px 0;} "
                + ".kv .k{color:#475569; font-weight:bold;} "
                + ".kv .v{color:#0f172a;} "
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

            var evtMap = BuildEventNumberMap();

            // 按场次分组显示
            var sessions = _schedule.GroupBy(s => s.SessionNumber).OrderBy(g => g.Key);
            foreach (var session in sessions) {
                var first = session.First();
                sb.AppendFormat("<h3>第{0}场 {1} {2}</h3>", session.Key,
                    !string.IsNullOrEmpty(first.Date) ? first.Date : "",
                    ComputeSessionTimeRange(session));
                sb.Append("<table><tr><th width='60'>时间</th><th width='70'>编号</th><th width='70'>组别</th><th>项目</th><th width='70'>赛次</th><th width='50'>组数</th></tr>");
                foreach (var s in session) {
                    sb.AppendFormat("<tr><td>{0}</td><td><b>{1}</b></td><td>{2}</td><td style='text-align:left;'>{3} {4}</td><td>{5}</td><td>{6}</td></tr>",
                        s.Time,
                        EventNumberLabel(evtMap, s.Gender, s.EventName),
                        s.AgeGroup ?? "",
                        s.Gender, s.EventName, s.Stage,
                        s.HeatCount > 0 ? s.HeatCount.ToString() : "");
                }
                sb.Append("</table>");
            }
            sb.Append(DocSignatureRow());
            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        // 项目编号：基于赛程顺序为每个 (性别, 项目) 分配唯一编号；不在赛程内的项目按字典序排在末尾
        private Dictionary<string, int> BuildEventNumberMap() {
            var map = new Dictionary<string, int>();
            int n = 0;
            foreach (var s in _schedule) {
                string key = (s.Gender ?? "") + "|" + (s.EventName ?? "");
                if (!map.ContainsKey(key)) map[key] = ++n;
            }
            // 报名表中存在但赛程未列出的项目
            var extras = _swimmers
                .Select(sw => new { G = sw.Gender ?? "", E = sw.EventName ?? "" })
                .Where(x => !string.IsNullOrEmpty(x.E))
                .Distinct()
                .Select(x => x.G + "|" + x.E)
                .Where(k => !map.ContainsKey(k))
                .OrderBy(k => k);
            foreach (var k in extras) map[k] = ++n;
            return map;
        }

        private static string EventNumberLabel(Dictionary<string, int> map, string gender, string eventName) {
            string k = (gender ?? "") + "|" + (eventName ?? "");
            int no;
            return map.TryGetValue(k, out no) ? no.ToString() : "-";
        }

        private string BuildManualHtml() {
            var sb = new StringBuilder();
            sb.AppendFormat("<html><head><meta charset='UTF-8'><style>{0}</style></head><body>", DocCss());

            string startDate = GetDatePickerText(StartDatePicker);
            string endDate = GetDatePickerText(EndDatePicker);
            string location = LocationBox.Text ?? "";
            string organizer = OrganizerBox.Text ?? "";
            string host = HostBox.Text ?? "";
            string techDel = TechDelegateBox.Text ?? "";
            string referee = RefereeBox.Text ?? "";
            string starter = StarterBox.Text ?? "";
            string chiefJ = ChiefJudgeBox.Text ?? "";
            string compName = string.IsNullOrEmpty(_competitionName) ? "游泳比赛" : _competitionName;
            var pb = _programBook ?? new ProgramBookData();

            var evtMap = BuildEventNumberMap();

            // ═══ 1. 封面 ═══
            sb.Append("<div class='page cover'>");
            sb.Append("<div class='cover-top'>");
            sb.AppendFormat("<div class='ttl1'>{0}</div>", string.IsNullOrEmpty(pb.CoverTitle) ? "游 泳 比 赛" : pb.CoverTitle);
            sb.AppendFormat("<div class='ttl2'>{0}</div>", string.IsNullOrEmpty(pb.CoverSubtitle) ? "秩 　 序 　 册" : pb.CoverSubtitle);
            sb.Append("</div>");
            sb.Append("<div class='cover-mid'>");
            sb.AppendFormat("<div class='name'>{0}</div>", compName);
            sb.Append("</div>");
            sb.Append("<div class='cover-bot'>");
            if (!string.IsNullOrEmpty(organizer)) sb.AppendFormat("<div class='row'><div class='lbl'>主办单位：</div><div class='val'>{0}</div></div>", organizer);
            if (!string.IsNullOrEmpty(host)) sb.AppendFormat("<div class='row'><div class='lbl'>承办单位：</div><div class='val'>{0}</div></div>", host);
            sb.AppendFormat("<div class='row'><div class='lbl'>比赛时间：</div><div class='val'>{0}{1}</div></div>",
                startDate, string.IsNullOrEmpty(endDate) || endDate == startDate ? "" : " 至 " + endDate);
            sb.AppendFormat("<div class='row'><div class='lbl'>比赛地点：</div><div class='val'>{0}</div></div>", string.IsNullOrEmpty(location) ? "&nbsp;" : location);
            sb.Append("</div></div>");

            // ═══ 2. 目录（动态：根据 _programBook 是否填充内容增加可选条目）═══
            bool hasForeword = !string.IsNullOrWhiteSpace(pb.Foreword);
            bool hasReg = !string.IsNullOrWhiteSpace(pb.Regulations);
            bool hasNotice = !string.IsNullOrWhiteSpace(pb.SupplementaryNotice);
            bool hasActivities = pb.KeyActivities != null && pb.KeyActivities.Count > 0;
            bool hasTraining = pb.TrainingSchedule != null && pb.TrainingSchedule.Count > 0;
            bool hasClosing = !string.IsNullOrWhiteSpace(pb.ClosingNote);
            bool hasVenueImg = !string.IsNullOrEmpty(pb.VenueImagePath) && File.Exists(pb.VenueImagePath);

            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1><h2>目 　 录</h2>", compName);
            sb.Append("<div class='toc'>");
            int tocN = 0;
            if (hasForeword) sb.AppendFormat("<div class='row'><span>{0}、前言</span><span></span></div>", CnNum(++tocN));
            sb.AppendFormat("<div class='row'><span>{0}、赛事概况</span><span></span></div>", CnNum(++tocN));
            if (hasReg) sb.AppendFormat("<div class='row'><span>{0}、竞赛规程</span><span></span></div>", CnNum(++tocN));
            sb.AppendFormat("<div class='row'><span>{0}、竞赛人员（仲裁、技术官员）</span><span></span></div>", CnNum(++tocN));
            if (hasActivities) sb.AppendFormat("<div class='row'><span>{0}、重要活动日程</span><span></span></div>", CnNum(++tocN));
            sb.AppendFormat("<div class='row'><span>{0}、小项设置</span><span></span></div>", CnNum(++tocN));
            sb.AppendFormat("<div class='row'><span>{0}、竞赛日程</span><span></span></div>", CnNum(++tocN));
            if (hasTraining) sb.AppendFormat("<div class='row'><span>{0}、训练日程</span><span></span></div>", CnNum(++tocN));
            sb.AppendFormat("<div class='row'><span>{0}、运动队人数统计</span><span></span></div>", CnNum(++tocN));
            sb.AppendFormat("<div class='row'><span>{0}、运动队名单</span><span></span></div>", CnNum(++tocN));
            sb.AppendFormat("<div class='row'><span>{0}、各项目报名表</span><span></span></div>", CnNum(++tocN));
            if (hasNotice) sb.AppendFormat("<div class='row'><span>{0}、比赛补充通知</span><span></span></div>", CnNum(++tocN));
            if (hasVenueImg) sb.AppendFormat("<div class='row'><span>{0}、比赛场地和功能区示意图</span><span></span></div>", CnNum(++tocN));
            if (hasClosing) sb.AppendFormat("<div class='row'><span>{0}、附注</span><span></span></div>", CnNum(++tocN));
            sb.Append("</div></div>");

            // ═══ 前言 ═══
            if (hasForeword) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.Append("<h3><span class='section-tag'>前</span>前言</h3>");
                sb.AppendFormat("<div style='font-size:16px; line-height:1.9; text-indent:2em; white-space:pre-wrap;'>{0}</div>",
                    System.Net.WebUtility.HtmlEncode(pb.Foreword));
                sb.Append("</div>");
            }

            // ═══ 3. 赛事概况 ═══
            int sectN = 0;
            if (hasForeword) sectN = 1; // 前言已占一节
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", compName);
            sb.AppendFormat("<h3><span class='section-tag'>{0}</span>赛事概况</h3>", CnNum(++sectN));
            sb.Append("<table>");
            sb.AppendFormat("<tr><th width='130'>赛事名称</th><td colspan='3' style='text-align:left;'>{0}</td></tr>", compName);
            sb.AppendFormat("<tr><th>主办单位</th><td colspan='3' style='text-align:left;'>{0}</td></tr>", string.IsNullOrEmpty(organizer) ? "&nbsp;" : organizer);
            sb.AppendFormat("<tr><th>承办单位</th><td colspan='3' style='text-align:left;'>{0}</td></tr>", string.IsNullOrEmpty(host) ? "&nbsp;" : host);
            sb.AppendFormat("<tr><th>比赛时间</th><td>{0} 至 {1}</td><th width='80'>比赛地点</th><td>{2}</td></tr>",
                startDate, string.IsNullOrEmpty(endDate) ? startDate : endDate, location);
            sb.AppendFormat("<tr><th>泳池规格</th><td>{0} 米 / {1} 道</td><th>赛事天数</th><td>{2} 天</td></tr>",
                _poolConfig.Length, _poolConfig.LaneCount, ComputeDays(startDate, endDate));
            int totalSwimmers = _swimmers.Count(s => !IsRelayMemberNote(s.Notes));
            int totalTeams = _swimmers.Select(s => s.Country ?? "").Where(c => !string.IsNullOrEmpty(c)).Distinct().Count();
            int totalRelays = _relayTeams.Count;
            int totalEvents = evtMap.Count;
            sb.AppendFormat("<tr><th>参赛队伍</th><td>{0} 支</td><th>报名人次</th><td>{1}</td></tr>", totalTeams, totalSwimmers);
            sb.AppendFormat("<tr><th>设置项目</th><td>{0} 项</td><th>接力队伍</th><td>{1} 支</td></tr>", totalEvents, totalRelays);
            sb.Append("</table>");

            sb.Append("</div>");

            // ═══ 竞赛规程 ═══
            if (hasReg) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>竞赛规程</h3>", CnNum(++sectN));
                sb.AppendFormat("<div style='font-size:15px; line-height:1.9; white-space:pre-wrap;'>{0}</div>",
                    System.Net.WebUtility.HtmlEncode(pb.Regulations));
                sb.Append("</div>");
            }

            // ═══ 竞赛人员 ═══
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", compName);
            sb.AppendFormat("<h3><span class='section-tag'>{0}</span>竞赛人员</h3>", CnNum(++sectN));
            sb.Append("<table>");
            sb.AppendFormat("<tr><th width='130'>技术代表</th><td>{0}</td><th width='130'>总裁判长</th><td>{1}</td></tr>",
                Hyphen(techDel), Hyphen(referee));
            sb.AppendFormat("<tr><th>发令员</th><td>{0}</td><th>编排长</th><td>{1}</td></tr>",
                Hyphen(starter), Hyphen(chiefJ));
            sb.Append("</table>");
            if (pb.Officials != null && pb.Officials.Count > 0) {
                sb.Append("<h4>技术官员名单</h4>");
                sb.Append("<table><tr><th width='200'>职务</th><th>姓名</th></tr>");
                foreach (var o in pb.Officials) {
                    sb.AppendFormat("<tr><td>{0}</td><td style='text-align:left;'>{1}</td></tr>",
                        System.Net.WebUtility.HtmlEncode(o.Title ?? ""),
                        System.Net.WebUtility.HtmlEncode(o.Name ?? ""));
                }
                sb.Append("</table>");
            }
            sb.Append("</div>");

            // ═══ 重要活动日程 ═══
            if (hasActivities) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>重要活动日程</h3>", CnNum(++sectN));
                sb.Append("<table><tr><th width='110'>日期</th><th width='110'>时间</th><th>活动内容</th><th width='140'>参与人员</th><th width='160'>地点</th></tr>");
                foreach (var a in pb.KeyActivities) {
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td style='text-align:left;'>{2}</td><td>{3}</td><td>{4}</td></tr>",
                        System.Net.WebUtility.HtmlEncode(a.Date ?? ""),
                        System.Net.WebUtility.HtmlEncode(a.Time ?? ""),
                        System.Net.WebUtility.HtmlEncode(a.Activity ?? ""),
                        System.Net.WebUtility.HtmlEncode(a.Participants ?? ""),
                        System.Net.WebUtility.HtmlEncode(a.Venue ?? ""));
                }
                sb.Append("</table>");
                sb.Append("</div>");
            }

            // ═══ 5. 小项设置 ═══
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", compName);
            sb.AppendFormat("<h3><span class='section-tag'>{0}</span>小项设置</h3>", CnNum(++sectN));
            sb.Append("<table><tr><th width='80'>性别</th><th>小项</th><th width='80'>项数</th></tr>");
            foreach (var gp in evtMap.Keys.GroupBy(k => k.Split('|')[0])) {
                var events = gp.Select(k => k.Split('|')[1]).OrderBy(e => evtMap[gp.Key + "|" + e]).ToList();
                sb.AppendFormat("<tr><td><b>{0}子</b></td><td style='text-align:left;'>{1}</td><td>{2} 项</td></tr>",
                    string.IsNullOrEmpty(gp.Key) ? "全部" : gp.Key,
                    string.Join("、", events.Select(e => string.Format("{0}({1})", e, evtMap[gp.Key + "|" + e])).ToArray()),
                    events.Count);
            }
            sb.Append("</table>");
            sb.Append("<p style='font-size:13px; color:#64748b; margin-top:8px;'>说明：括号内数字为项目编号，作为竞赛日程及成绩册的项目索引。</p>");
            sb.Append("</div>");

            // ═══ 6. 竞赛日程 ═══
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", compName);
            sb.AppendFormat("<h3><span class='section-tag'>{0}</span>竞赛日程</h3>", CnNum(++sectN));
            if (_schedule.Count == 0) {
                sb.Append("<p style='text-align:center; color:#94a3b8;'>暂未编排日程，请在【赛事管理与报名】中维护。</p>");
            } else {
                sb.Append("<table><tr><th width='95'>日期</th><th width='70'>场次</th><th width='110'>时间</th><th width='70'>项目编号</th><th>内容</th></tr>");
                var sessions = _schedule.GroupBy(s => s.SessionNumber).OrderBy(g => g.Key);
                foreach (var session in sessions) {
                    var first = session.First();
                    int rowCount = session.Count();
                    int idx = 0;
                    string timeRange = ComputeSessionTimeRange(session);
                    foreach (var s in session) {
                        sb.Append("<tr>");
                        if (idx == 0) {
                            sb.AppendFormat("<td rowspan='{0}'>{1}</td>", rowCount, string.IsNullOrEmpty(first.Date) ? "—" : first.Date);
                            sb.AppendFormat("<td rowspan='{0}'><b>第{1}场</b></td>", rowCount, session.Key);
                            sb.AppendFormat("<td rowspan='{0}'>{1}</td>", rowCount, timeRange);
                        }
                        sb.AppendFormat("<td><b>{0}</b></td><td style='text-align:left;'>{1}{2} {3} {4}{5}</td>",
                            EventNumberLabel(evtMap, s.Gender, s.EventName),
                            string.IsNullOrEmpty(s.AgeGroup) ? "" : ("[" + s.AgeGroup + "] "),
                            s.Gender, s.EventName, s.Stage,
                            s.HeatCount > 0 ? string.Format(" （{0}组）", s.HeatCount) : "");
                        sb.Append("</tr>");
                        idx++;
                    }
                }
                sb.Append("</table>");
            }
            sb.Append("</div>");

            // ═══ 训练日程（可选）═══
            if (hasTraining) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>训练日程</h3>", CnNum(++sectN));
                sb.Append("<table><tr><th width='160'>日期</th><th width='200'>时间</th><th>地点</th></tr>");
                foreach (var t in pb.TrainingSchedule) {
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td></tr>",
                        System.Net.WebUtility.HtmlEncode(t.Date ?? ""),
                        System.Net.WebUtility.HtmlEncode(t.Time ?? ""),
                        System.Net.WebUtility.HtmlEncode(t.Venue ?? ""));
                }
                sb.Append("</table>");
                sb.Append("</div>");
            }

            // ═══ 7. 运动队人数统计 ═══
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", compName);
            sb.AppendFormat("<h3><span class='section-tag'>{0}</span>运动队人数统计</h3>", CnNum(++sectN));
            sb.Append("<table><tr><th width='50'>序号</th><th>代表队</th><th width='70'>男</th><th width='70'>女</th><th width='70'>合计</th><th width='70'>接力队</th></tr>");
            var teamRows = _swimmers
                .Where(s => !IsRelayMemberNote(s.Notes))
                .GroupBy(s => s.Country ?? "")
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderBy(g => g.Key)
                .Select(g => new {
                    Team = g.Key,
                    Men = g.Count(s => s.Gender == "男"),
                    Women = g.Count(s => s.Gender == "女"),
                    Total = g.Count()
                })
                .ToList();
            int sumM = 0, sumW = 0, sumT = 0, sumR = 0, idxRow = 0;
            foreach (var t in teamRows) {
                idxRow++;
                int relayN = _relayTeams.Count(r => (r.TeamName ?? "") == t.Team);
                sumM += t.Men; sumW += t.Women; sumT += t.Total; sumR += relayN;
                sb.AppendFormat("<tr><td>{0}</td><td style='text-align:left;'>{1}</td><td>{2}</td><td>{3}</td><td><b>{4}</b></td><td>{5}</td></tr>",
                    idxRow, t.Team, t.Men, t.Women, t.Total, relayN);
            }
            sb.AppendFormat("<tr style='background:#e0e7ff; font-weight:bold;'><td colspan='2'>合计（{0} 支队伍）</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td></tr>",
                teamRows.Count, sumM, sumW, sumT, sumR);
            sb.Append("</table>");
            sb.Append("</div>");

            // ═══ 8. 运动队名单 ═══
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", compName);
            sb.AppendFormat("<h3><span class='section-tag'>{0}</span>运动队名单</h3>", CnNum(++sectN));
            var staffMap = (pb.TeamStaffList ?? new List<ProgramBookTeamStaff>())
                .Where(s => !string.IsNullOrEmpty(s.TeamName))
                .GroupBy(s => s.TeamName).ToDictionary(g => g.Key, g => g.First());
            foreach (var team in teamRows) {
                var teamSwimmers = _swimmers.Where(s => (s.Country ?? "") == team.Team && !IsRelayMemberNote(s.Notes)).ToList();
                var men = teamSwimmers.Where(s => s.Gender == "男").Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n).ToList();
                var women = teamSwimmers.Where(s => s.Gender == "女").Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n).ToList();
                sb.Append("<div class='team-block'>");
                sb.AppendFormat("<div class='team-name'>{0}　<span style='font-size:13px; color:#64748b; font-family:SimSun; letter-spacing:0;'>（男 {1} 人，女 {2} 人）</span></div>",
                    team.Team, men.Count, women.Count);
                ProgramBookTeamStaff staff;
                if (staffMap.TryGetValue(team.Team, out staff)) {
                    if (!string.IsNullOrWhiteSpace(staff.Leader))
                        sb.AppendFormat("<div class='role'><div class='lbl'>领　　队：</div><div class='vals'>{0}</div></div>", System.Net.WebUtility.HtmlEncode(staff.Leader));
                    if (!string.IsNullOrWhiteSpace(staff.Coaches))
                        sb.AppendFormat("<div class='role'><div class='lbl'>教　　练：</div><div class='vals'>{0}</div></div>", System.Net.WebUtility.HtmlEncode(staff.Coaches));
                    if (!string.IsNullOrWhiteSpace(staff.Doctors))
                        sb.AppendFormat("<div class='role'><div class='lbl'>队　　医：</div><div class='vals'>{0}</div></div>", System.Net.WebUtility.HtmlEncode(staff.Doctors));
                    if (!string.IsNullOrWhiteSpace(staff.Staff))
                        sb.AppendFormat("<div class='role'><div class='lbl'>工作人员：</div><div class='vals'>{0}</div></div>", System.Net.WebUtility.HtmlEncode(staff.Staff));
                }
                if (men.Count > 0)
                    sb.AppendFormat("<div class='role'><div class='lbl'>男运动员：</div><div class='vals'>{0}</div></div>", string.Join("　", men.ToArray()));
                if (women.Count > 0)
                    sb.AppendFormat("<div class='role'><div class='lbl'>女运动员：</div><div class='vals'>{0}</div></div>", string.Join("　", women.ToArray()));
                var relays = _relayTeams.Where(r => (r.TeamName ?? "") == team.Team).Select(r => r.EventName + "(" + r.Gender + ")").Distinct().OrderBy(s => s).ToList();
                if (relays.Count > 0)
                    sb.AppendFormat("<div class='role'><div class='lbl'>接力项目：</div><div class='vals'>{0}</div></div>", string.Join("　", relays.ToArray()));
                sb.Append("</div>");
            }
            sb.Append("</div>");

            // ═══ 9. 各项目报名表 ═══
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", compName);
            sb.AppendFormat("<h3><span class='section-tag'>{0}</span>各项目报名表</h3>", CnNum(++sectN));
            // 按项目编号排序展示
            var sortedKeys = evtMap.OrderBy(kv => kv.Value).ToList();
            int blockIdx = 0;
            foreach (var kv in sortedKeys) {
                var parts = kv.Key.Split('|');
                string evGender = parts[0];
                string evName = parts[1];
                bool manRelay = evName.IndexOf("接力", StringComparison.Ordinal) >= 0;
                var entries = _swimmers
                    .Where(s => s.Gender == evGender && s.EventName == evName)
                    .Where(s => !manRelay || !IsRelayMemberNote(s.Notes)) // 接力只显示队伍代表
                    .ToList();
                if (entries.Count == 0) continue;

                blockIdx++;
                if (blockIdx > 1 && blockIdx % 4 == 1) sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h4>项目 {0}：{1} {2}　<span style='font-weight:normal; font-size:14px; color:#64748b;'>报名 {3} 人{4}</span></h4>",
                    kv.Value, evGender, evName, entries.Count, manRelay ? "/队" : "");
                sb.AppendFormat("<table><tr><th width='50'>序号</th><th width='70'>号码</th><th width='110'>{0}</th><th width='110'>{1}</th><th width='80'>报名成绩</th><th width='80'>组别</th></tr>",
                    RelayCol1Header(manRelay), RelayCol2Header(manRelay));
                int rowI = 0;
                foreach (var sw in entries.OrderBy(s => s.EntryTimeSeconds <= 0 ? double.MaxValue : s.EntryTimeSeconds).ThenBy(s => s.BibNumber ?? "")) {
                    rowI++;
                    string nm = sw.Name ?? ""; string ctry = sw.Country ?? "";
                    if (manRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                        nm = sw.Notes.Substring("接力队 棒次:".Length);
                    sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td><b>{2}</b></td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                        rowI, sw.BibNumber, RelayCol1(manRelay, nm, ctry), RelayCol2(manRelay, nm, ctry),
                        string.IsNullOrEmpty(sw.EntryTime) ? "—" : sw.EntryTime,
                        sw.AgeCategory ?? "");
                }
                sb.Append("</table>");
            }

            sb.Append(DocSignatureRow());
            sb.Append("</div>");

            // ═══ 比赛补充通知 ═══
            if (hasNotice) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>比赛补充通知</h3>", CnNum(++sectN));
                sb.AppendFormat("<div style='font-size:15px; line-height:1.9; white-space:pre-wrap;'>{0}</div>",
                    System.Net.WebUtility.HtmlEncode(pb.SupplementaryNotice));
                sb.Append("</div>");
            }

            // ═══ 比赛场地和功能区示意图 ═══
            if (hasVenueImg) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>比赛场地和功能区示意图</h3>", CnNum(++sectN));
                try {
                    byte[] bytes = File.ReadAllBytes(pb.VenueImagePath);
                    string ext = (System.IO.Path.GetExtension(pb.VenueImagePath) ?? ".png").TrimStart('.').ToLower();
                    string mime = ext == "jpg" || ext == "jpeg" ? "image/jpeg" : (ext == "bmp" ? "image/bmp" : "image/png");
                    sb.AppendFormat("<div style='text-align:center; margin-top:20px;'><img src='data:{0};base64,{1}' style='max-width:100%; max-height:900px;'/></div>",
                        mime, Convert.ToBase64String(bytes));
                } catch (Exception ex) {
                    sb.AppendFormat("<p style='color:#dc2626;'>无法载入图片：{0}</p>", System.Net.WebUtility.HtmlEncode(ex.Message));
                }
                sb.Append("</div>");
            }

            // ═══ 附注 / 尾页 ═══
            if (hasClosing) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>附注</h3>", CnNum(++sectN));
                sb.AppendFormat("<div style='font-size:15px; line-height:1.9; white-space:pre-wrap;'>{0}</div>",
                    System.Net.WebUtility.HtmlEncode(pb.ClosingNote));
                sb.Append("</div>");
            }

            sb.Append(DocFooter());
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string Hyphen(string s) { return string.IsNullOrEmpty(s) ? "—" : s; }

        // 判断 Swimmer.Notes 是否表示"接力队员"子条目（用于人数统计/名单中排除）
        private static bool IsRelayMemberNote(string notes) {
            return !string.IsNullOrEmpty(notes) && notes.StartsWith("接力队员");
        }

        // 中文数字（一~二十），用于秩序册章节编号
        private static string CnNum(int n) {
            string[] tbl = { "零","一","二","三","四","五","六","七","八","九","十",
                             "十一","十二","十三","十四","十五","十六","十七","十八","十九","二十" };
            if (n >= 0 && n < tbl.Length) return tbl[n];
            return n.ToString();
        }

        private static int ComputeDays(string startDate, string endDate) {
            DateTime ds, de;
            if (!DateTime.TryParse(startDate, out ds)) return 0;
            if (!DateTime.TryParse(endDate, out de)) de = ds;
            return Math.Max(1, (int)(de.Date - ds.Date).TotalDays + 1);
        }

        private static string ComputeSessionTimeRange(IEnumerable<ScheduleItem> session) {
            var times = session.Select(s => s.Time ?? "").Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            if (times.Count == 0) return "—";
            if (times.Count == 1) return times[0];
            return times.OrderBy(t => t).First() + " ~ " + times.OrderByDescending(t => t).First();
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
            sb.AppendFormat("<table><tr><th width='50'>名次</th><th width='40'>道</th><th width='60'>号码</th><th width='100'>{0}</th><th width='100'>{1}</th><th width='90'>成绩</th><th width='70'>成绩差</th><th width='70'>反应时间</th><th width='50'>备注</th></tr>",
                RelayCol1Header(printRelay), RelayCol2Header(printRelay));
            var swimmers = GetCurrentHeatSwimmers().OrderBy(s => s.CurrentRank > 0 ? s.CurrentRank : int.MaxValue).ToList();
            // 计算第1名成绩（用于"成绩差"列）：取本组中有有效成绩且非 DSQ/DNS/DNF 的最快者
            double leaderTime = 0;
            foreach (var sw in swimmers) {
                if (sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DQ") continue;
                var rr = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                if (rr == null || rr.FinalTime <= 0) continue;
                if (!string.IsNullOrEmpty(rr.Status)) continue;
                if (leaderTime <= 0 || rr.FinalTime < leaderTime) leaderTime = rr.FinalTime;
            }
            foreach (var sw in swimmers) {
                var r = sw.Results.FirstOrDefault(lr => lr.Stage == _currentStage && lr.Heat == _currentHeat);
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (!string.IsNullOrEmpty(sw.Status) && (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ" || sw.Status == "DQ")) remark = sw.Status;
                // 备注列 HTML：判罚红色 / 晋级 Q 绿色 / 其它空白；timeText/diffText 下面仍按 remark（纯文本）判断
                string remarkHtml = RenderRemarkCellHtml(sw, r, _currentStage);
                string pName = sw.Name; string pCountry = sw.Country ?? "";
                if (printRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                    pName = sw.Notes.Substring("接力队 棒次:".Length);
                string timeText = string.IsNullOrEmpty(remark) && r != null && r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "";
                // 成绩差：当前道成绩 - 第1名成绩；第1名留空
                string diffText = "";
                if (string.IsNullOrEmpty(remark) && r != null && r.FinalTime > 0 && leaderTime > 0 && r.FinalTime > leaderTime) {
                    diffText = (r.FinalTime - leaderTime).ToString("F2");
                }
                // 反应时单元：接力赛固定展开为 N 棒（N 由项目名"4×100"解析），未记录到的棒次显示"—"。
                // 这样即便硬件事件丢失或顺序错乱，打印行数始终与棒次数对齐。
                string reactionCell = "";
                if (printRelay) {
                    int legCount = 4;
                    var mLeg = System.Text.RegularExpressions.Regex.Match(_currentEvent ?? "", @"(\d+)\s*[x×]\s*\d+");
                    if (mLeg.Success) {
                        int n; if (int.TryParse(mLeg.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n;
                    }
                    var parts = new List<string>();
                    for (int li = 0; li < legCount; li++) {
                        double rt = (r != null && r.LegReactionTimes != null && li < r.LegReactionTimes.Count) ? r.LegReactionTimes[li] : 0;
                        parts.Add(string.Format("第{0}棒:{1}", li + 1, rt > 0 ? rt.ToString("F2") : "—"));
                    }
                    reactionCell = string.Join("<br>", parts.ToArray());
                } else if (r != null && r.StartingBlockTime > 0) {
                    reactionCell = r.StartingBlockTime.ToString("F2");
                }
                sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td><b>{3}</b></td><td>{4}</td><td style='font-weight:bold; background:#eff6ff;'>{5}</td><td>{6}</td><td style='font-size:12px;'>{7}</td><td>{8}</td></tr>",
                    r != null && r.Rank > 0 ? r.Rank.ToString() : "-",
                    sw.Lane, sw.BibNumber, RelayCol1(printRelay, pName, pCountry), RelayCol2(printRelay, pName, pCountry),
                    timeText,
                    diffText,
                    reactionCell,
                    remarkHtml);
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
                + ".rb-cover .ttl1{{color:#b45309;}} "
                + ".rb-cover .name{{color:#b45309;}} "
                + ".records-bar{{background:#fef3c7; border:1px solid #fbbf24; border-radius:4px; padding:6px 12px; margin:8px 0; font-size:13px;}} "
                + ".records-bar .rec-item{{display:inline-block; margin-right:18px;}} "
                + ".records-bar b{{color:#b45309;}} "
                + ".heat-title{{margin-top:14px; padding:6px 10px; background:#fef3c7; font-weight:bold; border-left:4px solid #f59e0b;}} "
                + ".event-meta{{display:flex; justify-content:space-between; margin:4px 0; font-size:14px; color:#475569;}} "
                + "</style></head><body>", DocCss());

            string compName = string.IsNullOrEmpty(_competitionName) ? "游泳比赛" : _competitionName;
            var rb = _resultBook ?? new ResultBookData();
            string startDate = GetDatePickerText(StartDatePicker);
            string endDate = GetDatePickerText(EndDatePicker);
            string location = LocationBox.Text ?? "";
            string organizer = OrganizerBox.Text ?? "";
            string host = HostBox.Text ?? "";

            var infoMap = (rb.SwimmerInfos ?? new List<ResultBookSwimmerInfo>())
                .Where(s => !string.IsNullOrEmpty(s.BibNumber))
                .GroupBy(s => s.BibNumber)
                .ToDictionary(g => g.Key, g => g.First());

            // ═══ 1. 封面 ═══
            sb.Append("<div class='page cover rb-cover'>");
            sb.Append("<div class='cover-top'>");
            sb.AppendFormat("<div class='ttl1'>{0}</div>", string.IsNullOrEmpty(rb.CoverTitle) ? "游 泳 比 赛" : rb.CoverTitle);
            sb.AppendFormat("<div class='ttl2'>{0}</div>", string.IsNullOrEmpty(rb.CoverSubtitle) ? "成 　 绩 　 册" : rb.CoverSubtitle);
            sb.Append("</div>");
            sb.Append("<div class='cover-mid'>");
            sb.AppendFormat("<div class='name'>{0}</div>", compName);
            sb.Append("</div>");
            sb.Append("<div class='cover-bot'>");
            if (!string.IsNullOrEmpty(organizer)) sb.AppendFormat("<div class='row'><div class='lbl'>主办单位：</div><div class='val'>{0}</div></div>", organizer);
            if (!string.IsNullOrEmpty(host)) sb.AppendFormat("<div class='row'><div class='lbl'>承办单位：</div><div class='val'>{0}</div></div>", host);
            sb.AppendFormat("<div class='row'><div class='lbl'>比赛时间：</div><div class='val'>{0}{1}</div></div>",
                startDate, string.IsNullOrEmpty(endDate) || endDate == startDate ? "" : " 至 " + endDate);
            sb.AppendFormat("<div class='row'><div class='lbl'>比赛地点：</div><div class='val'>{0}</div></div>", string.IsNullOrEmpty(location) ? "&nbsp;" : location);
            sb.Append("</div></div>");

            // ═══ 2. 前言（可选）═══
            if (!string.IsNullOrWhiteSpace(rb.Foreword)) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1><h2>前 　 言</h2>", compName);
                sb.AppendFormat("<div style='font-size:16px; line-height:1.9; text-indent:2em; white-space:pre-wrap;'>{0}</div>",
                    System.Net.WebUtility.HtmlEncode(rb.Foreword));
                sb.Append("</div>");
            }

            // ═══ 3. 目录 ═══
            sb.Append("<div class='page-break'></div><div class='page'>");
            sb.AppendFormat("<h1>{0}</h1><h2>目 　 录</h2>", compName);
            sb.Append("<div class='toc'>");
            int tocN = 0;
            if (rb.IncludeMedalCount) sb.AppendFormat("<div class='row'><span>{0}、奖牌榜统计</span><span></span></div>", CnNum(++tocN));
            if (rb.IncludeSportsAwards) sb.AppendFormat("<div class='row'><span>{0}、体育道德风尚奖</span><span></span></div>", CnNum(++tocN));
            if (rb.IncludeRecordStats) sb.AppendFormat("<div class='row'><span>{0}、破纪录统计表</span><span></span></div>", CnNum(++tocN));
            if (rb.IncludeFinalRanking) sb.AppendFormat("<div class='row'><span>{0}、名次公告</span><span></span></div>", CnNum(++tocN));
            if (rb.IncludeFullResults) sb.AppendFormat("<div class='row'><span>{0}、成绩公告</span><span></span></div>", CnNum(++tocN));
            sb.Append("</div></div>");

            int sectN = 0;

            // ═══ 4. 奖牌榜统计 ═══
            if (rb.IncludeMedalCount) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>奖牌榜统计</h3>", CnNum(++sectN));
                sb.Append("<table><tr><th width='60'>排名</th><th>代表队</th><th width='80'>金牌</th><th width='80'>银牌</th><th width='80'>铜牌</th><th width='80'>总计</th></tr>");
                var medalTable = new Dictionary<string, int[]>();
                var medalEventGroups = _swimmers.Where(s => !IsRelayMemberNote(s.Notes)).GroupBy(s => new { s.Gender, s.EventName });
                foreach (var ev in medalEventGroups) {
                    var finalists = ev.Where(s => s.GetResultForStage("决赛") != null && s.GetResultForStage("决赛").FinalTime > 0
                            && s.Status != "DSQ" && s.Status != "DNS" && s.Status != "DNF")
                        .OrderBy(s => s.GetResultForStage("决赛").FinalTime).Take(3).ToList();
                    for (int i = 0; i < finalists.Count; i++) {
                        string country = string.IsNullOrEmpty(finalists[i].Country) ? "—" : finalists[i].Country;
                        if (!medalTable.ContainsKey(country)) medalTable[country] = new int[3];
                        medalTable[country][i]++;
                    }
                }
                var sortedMedals = medalTable.OrderByDescending(x => x.Value[0] * 10000 + x.Value[1] * 100 + x.Value[2]).ToList();
                for (int i = 0; i < sortedMedals.Count; i++) {
                    var m = sortedMedals[i];
                    string row = i == 0 ? " style='background:#fef3c7;'" : (i == 1 ? " style='background:#f1f5f9;'" : (i == 2 ? " style='background:#fef0e7;'" : ""));
                    sb.AppendFormat("<tr{0}><td>{1}</td><td style='text-align:left; padding-left:20px;'><b>{2}</b></td><td>{3}</td><td>{4}</td><td>{5}</td><td style='font-weight:bold;'>{6}</td></tr>",
                        row, i + 1, m.Key, m.Value[0], m.Value[1], m.Value[2], m.Value[0] + m.Value[1] + m.Value[2]);
                }
                if (sortedMedals.Count == 0) sb.Append("<tr><td colspan='6' style='color:#94a3b8;'>暂无决赛成绩</td></tr>");
                sb.Append("</table>");
                sb.Append("</div>");
            }

            // ═══ 5. 体育道德风尚奖 ═══
            if (rb.IncludeSportsAwards && (rb.SportsTeams.Count + rb.SportsAthletes.Count + rb.SportsJudges.Count > 0)) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>体育道德风尚奖</h3>", CnNum(++sectN));
                if (rb.SportsTeams.Count > 0) {
                    sb.Append("<h4>运动队</h4><div style='font-size:16px; line-height:2.4; text-align:center; padding:10px 40px;'>");
                    sb.Append(string.Join("　　　", rb.SportsTeams.Select(t => System.Net.WebUtility.HtmlEncode(t)).ToArray()));
                    sb.Append("</div>");
                }
                if (rb.SportsAthletes.Count > 0) {
                    sb.Append("<h4>运动员</h4><div style='font-size:15px; line-height:2.2; padding:10px 30px; column-count:3; column-gap:30px;'>");
                    foreach (var n in rb.SportsAthletes) sb.AppendFormat("<div>{0}</div>", System.Net.WebUtility.HtmlEncode(n));
                    sb.Append("</div>");
                }
                if (rb.SportsJudges.Count > 0) {
                    sb.Append("<h4>裁判员</h4><div style='font-size:16px; line-height:2.4; text-align:center; padding:10px 40px;'>");
                    sb.Append(string.Join("　　　", rb.SportsJudges.Select(t => System.Net.WebUtility.HtmlEncode(t)).ToArray()));
                    sb.Append("</div>");
                }
                sb.Append("</div>");
            }

            // ═══ 6. 破纪录统计表 ═══
            if (rb.IncludeRecordStats) {
                var brokenRows = CollectBrokenRecords();
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>破纪录统计表</h3>", CnNum(++sectN));
                if (brokenRows.Count == 0) {
                    sb.Append("<p style='text-align:center; color:#94a3b8; margin-top:40px;'>本次比赛暂无破纪录记录。</p>");
                } else {
                    sb.AppendFormat("<p style='text-align:right; color:#475569;'>截至 {0}</p>", DateTime.Now.ToString("yyyy-MM-dd"));
                    sb.Append("<table><tr><th width='100'>日期</th><th>项目</th><th width='80'>赛次</th><th width='110'>运动员</th><th width='100'>单位</th><th width='100'>成绩</th><th width='110'>纪录类型</th></tr>");
                    foreach (var r in brokenRows) {
                        sb.AppendFormat("<tr><td>{0}</td><td style='text-align:left;'>{1} {2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td style='font-weight:bold;'>{6}</td><td>{7}</td></tr>",
                            r.Date, r.Gender, r.EventName, r.Stage, r.Athlete, r.Country, r.Time, r.RecordType);
                    }
                    sb.Append("</table>");
                }
                sb.Append("</div>");
            }

            // ═══ 7. 名次公告（各项目决赛排名）═══
            if (rb.IncludeFinalRanking) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>名次公告</h3>", CnNum(++sectN));
                bool anyFinals = false;
                var teamCoachMap = BuildTeamCoachMap();
                foreach (var schedItem in _schedule.Where(s => s.Stage == "决赛")) {
                    var topList = GetEventFinalRanking(schedItem.Gender, schedItem.EventName);
                    if (topList.Count == 0) continue;
                    anyFinals = true;
                    bool nrRelay = (schedItem.EventName ?? "").IndexOf("接力", StringComparison.Ordinal) >= 0;
                    sb.Append("<div style='margin-top:18px; page-break-inside:avoid;'>");
                    sb.Append("<div class='event-meta'>");
                    sb.AppendFormat("<div><b>游泳</b>　　{0} {1}</div>", schedItem.Gender, schedItem.EventName);
                    sb.AppendFormat("<div>{0} {1}　　{2}</div>", schedItem.Date ?? "", schedItem.Time ?? "", location);
                    sb.Append("</div>");
                    sb.Append("<table><tr><th width='60'>名次</th><th width='110'>单位</th><th width='180'>姓名</th><th width='150'>联合培养单位</th><th width='90'>成绩</th><th>教练员</th></tr>");
                    foreach (var row in topList) {
                        var sw = row.Swimmer;
                        ResultBookSwimmerInfo info = null;
                        infoMap.TryGetValue(sw.BibNumber ?? "", out info);
                        string coach = info != null ? info.Coach : "";
                        if (string.IsNullOrEmpty(coach)) {
                            string tc;
                            if (teamCoachMap.TryGetValue(sw.Country ?? "", out tc)) coach = tc;
                        }
                        string joint = info != null ? info.JointTrainingUnit : "";
                        if (string.IsNullOrEmpty(joint)) joint = sw.CountryShort ?? "";
                        // 接力：堆叠显示队员姓名
                        string nameCell;
                        if (nrRelay) {
                            var team = _relayTeams.FirstOrDefault(rt => rt.TeamName == sw.Country && rt.EventName == schedItem.EventName && rt.Gender == schedItem.Gender);
                            if (team != null && team.Legs != null && team.Legs.Count > 0) {
                                nameCell = string.Join("<br>", team.Legs.OrderBy(l => l.LegOrder).Select(l => System.Net.WebUtility.HtmlEncode(l.SwimmerName ?? "")).ToArray());
                            } else {
                                nameCell = System.Net.WebUtility.HtmlEncode(sw.Country ?? "");
                            }
                        } else {
                            nameCell = System.Net.WebUtility.HtmlEncode(sw.Name ?? "");
                        }
                        string rankPrefix = row.IsTie ? "=" : "";
                        sb.AppendFormat("<tr><td>{0}{1}</td><td>{2}</td><td style='text-align:left; padding-left:14px;'>{3}</td><td>{4}</td><td style='font-weight:bold;'>{5}</td><td>{6}</td></tr>",
                            rankPrefix, row.Rank,
                            System.Net.WebUtility.HtmlEncode(sw.Country ?? ""),
                            nameCell,
                            System.Net.WebUtility.HtmlEncode(joint),
                            row.TimeText,
                            System.Net.WebUtility.HtmlEncode(coach));
                    }
                    sb.Append("</table></div>");
                }
                if (!anyFinals) sb.Append("<p style='text-align:center; color:#94a3b8; margin-top:30px;'>暂无决赛排名数据。</p>");
                sb.Append("</div>");
            }

            // ═══ 8. 成绩公告（每个赛次每组完整成绩）═══
            if (rb.IncludeFullResults) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.AppendFormat("<h3><span class='section-tag'>{0}</span>成绩公告</h3>", CnNum(++sectN));

                int eventBlock = 0;
                foreach (var schedItem in _schedule) {
                    string gender = schedItem.Gender;
                    string eventName = schedItem.EventName;
                    string stage = schedItem.Stage;
                    bool ffRelay = (eventName ?? "").IndexOf("接力", StringComparison.Ordinal) >= 0;

                    var matched = _swimmers.Where(s =>
                        s.Gender == gender && s.EventName == eventName &&
                        !IsRelayMemberNote(s.Notes) &&
                        s.GetResultForStage(stage) != null
                    ).ToList();
                    if (matched.Count == 0) continue;

                    var heatNumbers = matched.Select(s => s.GetResultForStage(stage).Heat).Distinct().OrderBy(h => h).ToList();
                    if (heatNumbers.Count == 0) continue;

                    eventBlock++;
                    if (eventBlock > 1) sb.Append("<div class='page-break'></div><div class='page'>");
                    sb.Append("<div class='event-meta'>");
                    sb.AppendFormat("<div><b>游泳</b>　　{0} {1}　　{2}</div>", gender, eventName, stage);
                    sb.AppendFormat("<div>{0} {1}　　{2}</div>", schedItem.Date ?? "", schedItem.Time ?? "", location);
                    sb.Append("</div>");

                    // 项目纪录参考条
                    var refs = LookupRecordReferences(gender, eventName, schedItem.AgeGroup ?? "");
                    if (refs.Count > 0) {
                        sb.Append("<div class='records-bar'>");
                        foreach (var rr in refs)
                            sb.AppendFormat("<span class='rec-item'><b>{0}</b> {1} <span style='color:#475569;'>{2}</span></span>", rr.Type, rr.TimeText, rr.HolderInfo);
                        sb.Append("</div>");
                    }

                    int distMeters = ParseDistanceMeters(eventName);
                    int splitGap = _poolConfig != null && _poolConfig.Length > 0 ? _poolConfig.Length * 2 : 100;
                    var splitMarks = new List<int>();
                    if (rb.ShowSplitTimes && distMeters > splitGap) {
                        for (int d = splitGap; d < distMeters; d += splitGap) splitMarks.Add(d);
                    }

                    foreach (int heat in heatNumbers) {
                        var heatSwimmers = matched.Where(s => s.GetResultForStage(stage).Heat == heat).ToList();
                        Func<Swimmer, bool> isDQ = sw => sw.Status == "DSQ" || sw.Status == "DNS" || sw.Status == "DNF";
                        var ordered = heatSwimmers
                            .OrderBy(s => isDQ(s) ? 1 : 0)
                            .ThenBy(s => isDQ(s) ? 0 : s.GetResultForStage(stage).FinalTime)
                            .ToList();
                        if (ordered.Count == 0) continue;
                        bool showHeat = (heatNumbers.Count > 1) || (stage ?? "").Contains("预赛") || (stage ?? "").Contains("半决赛");
                        if (showHeat) {
                            sb.AppendFormat("<div class='heat-title'>{0}第{1}组，共{2}组</div>", stage, heat, heatNumbers.Count);
                        } else {
                            sb.AppendFormat("<div class='heat-title'>{0}</div>", stage);
                        }

                        // 反应时列：接力赛展开 N 棒，宽度加大
                        int rtColW = ffRelay ? 110 : 60;
                        sb.AppendFormat("<table><tr><th width='50'>名次</th><th width='40'>道次</th><th width='110'>运动员</th><th width='80'>单位</th><th width='100'>出生日期</th><th width='{0}'>反应时</th>", rtColW);
                        foreach (var sm in splitMarks) sb.AppendFormat("<th width='60'>{0}m</th>", sm);
                        sb.Append("<th width='80'>成绩</th>");
                        if (rb.ShowTimeDifference) sb.Append("<th width='70'>成绩差</th>");
                        sb.Append("</tr>");

                        double leaderTime = ordered.Where(s => !isDQ(s)).Select(s => s.GetResultForStage(stage)).Where(r => r != null && r.FinalTime > 0).Select(r => r.FinalTime).DefaultIfEmpty(0).First();
                        int rank = 1;
                        double prevTime = -1;
                        int prevRank = 1;
                        foreach (var sw in ordered) {
                            var r = sw.GetResultForStage(stage);
                            string remark = "";
                            if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                            else if (!string.IsNullOrEmpty(sw.Status) && (sw.Status == "DNS" || sw.Status == "DNF" || sw.Status == "DSQ" || sw.Status == "DQ")) remark = sw.Status;
                            bool dq = !string.IsNullOrEmpty(remark);
                            string nm = ffRelay ? (sw.Country ?? "") : (sw.Name ?? "");
                            string ctry = sw.Country ?? "";
                            string rankText;
                            if (dq) { rankText = "—"; }
                            else {
                                if (IsTieTime(r.FinalTime, prevTime) && prevTime > 0) {
                                    rankText = "=" + prevRank.ToString();
                                } else {
                                    rankText = rank.ToString();
                                    prevRank = rank;
                                    prevTime = r.FinalTime;
                                }
                            }
                            // 反应时单元：接力赛展开 N 棒（"第N棒:0.45<br>..."），未记录到的棒显示"—"
                            string reactionCell = "";
                            if (ffRelay) {
                                int legCnt = 4;
                                var mLeg2 = System.Text.RegularExpressions.Regex.Match(eventName ?? "", @"(\d+)\s*[x×]\s*\d+");
                                if (mLeg2.Success) {
                                    int n2; if (int.TryParse(mLeg2.Groups[1].Value, out n2) && n2 > 0 && n2 <= 10) legCnt = n2;
                                }
                                var rtParts = new List<string>();
                                for (int li = 0; li < legCnt; li++) {
                                    double rt = (r != null && r.LegReactionTimes != null && li < r.LegReactionTimes.Count) ? r.LegReactionTimes[li] : 0;
                                    rtParts.Add(string.Format("第{0}棒:{1}", li + 1, rt > 0 ? rt.ToString("F2") : "—"));
                                }
                                reactionCell = string.Join("<br>", rtParts.ToArray());
                            } else if (r != null && r.StartingBlockTime > 0) {
                                reactionCell = r.StartingBlockTime.ToString("F2");
                            }
                            sb.AppendFormat("<tr><td>{0}</td><td>{1}</td><td><b>{2}</b></td><td>{3}</td><td>{4}</td><td style='font-size:12px;'>{5}</td>",
                                rankText, dq ? "—" : r.Lane.ToString(),
                                System.Net.WebUtility.HtmlEncode(nm),
                                System.Net.WebUtility.HtmlEncode(ctry),
                                System.Net.WebUtility.HtmlEncode(sw.BirthDate ?? ""),
                                reactionCell);
                            // 分段成绩
                            foreach (var sm in splitMarks) {
                                string st = "";
                                if (r.Splits != null) {
                                    var match = r.Splits.FirstOrDefault(sp => sp.Distance == sm || sp.Lap * (_poolConfig != null ? _poolConfig.Length : 50) == sm);
                                    if (match != null && match.CumulativeTime > 0) st = TimeFormatter.Format(match.CumulativeTime);
                                    else if (match != null && match.Time > 0) st = TimeFormatter.Format(match.Time);
                                }
                                sb.AppendFormat("<td>{0}</td>", st);
                            }
                            string finalText = dq ? remark : (r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "");
                            // 破/平纪录用金色标签紧跟成绩后（FINA 惯例：成绩后接 WR/=AR 等）
                            string recTag = (!dq && r != null && !string.IsNullOrEmpty(r.RecordNote))
                                ? string.Format(" <span style='color:#b45309;font-weight:bold;'>{0}</span>", System.Net.WebUtility.HtmlEncode(r.RecordNote))
                                : "";
                            // 录取标志 Q：预赛/半决赛后有下一赛次分组的运动员，时间后追加绿色 Q
                            string qTag = (!dq && IsQualifiedToNext(sw, stage))
                                ? " <span style='color:#16a34a;font-weight:bold;'>Q</span>"
                                : "";
                            sb.AppendFormat("<td style='font-weight:bold;'>{0}{1}{2}</td>",
                                dq ? string.Format("<span style='color:#dc2626;'>{0}</span>", finalText) : finalText,
                                recTag, qTag);
                            if (rb.ShowTimeDifference) {
                                string diff = "";
                                if (!dq && leaderTime > 0 && r.FinalTime > 0 && r.FinalTime > leaderTime)
                                    diff = (r.FinalTime - leaderTime).ToString("F2");
                                sb.AppendFormat("<td>{0}</td>", diff);
                            }
                            sb.Append("</tr>");
                            if (!dq) rank++;
                        }
                        sb.Append("</table>");
                    }
                }
                sb.Append("</div>");
            }

            // ═══ 9. 尾页 / 附注 ═══
            if (!string.IsNullOrWhiteSpace(rb.ClosingNote)) {
                sb.Append("<div class='page-break'></div><div class='page'>");
                sb.AppendFormat("<h1>{0}</h1>", compName);
                sb.Append("<h3>附 注</h3>");
                sb.AppendFormat("<div style='font-size:15px; line-height:1.9; white-space:pre-wrap;'>{0}</div>",
                    System.Net.WebUtility.HtmlEncode(rb.ClosingNote));
                sb.Append("</div>");
            }

            sb.AppendFormat("<p style='text-align:right; padding:20px; color:gray;'>打印时间：{0}</p>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</body></html>");
            return sb.ToString();
        }

        // ─── 成绩册辅助 ───
        private class RecordReference {
            public string Type, TimeText, HolderInfo;
        }
        private List<RecordReference> LookupRecordReferences(string gender, string eventName, string ageGroup) {
            var list = new List<RecordReference>();
            if (_records == null) return list;
            foreach (var rec in _records.Where(r => r.Gender == gender && r.EventName == eventName)) {
                if (!string.IsNullOrEmpty(ageGroup) && !string.IsNullOrEmpty(rec.AgeGroup) && rec.AgeGroup != ageGroup) continue;
                string holder = rec.HolderName ?? "";
                if (!string.IsNullOrEmpty(rec.HolderCountry)) holder += " (" + rec.HolderCountry + ")";
                if (!string.IsNullOrEmpty(rec.Date)) holder += " " + rec.Date;
                list.Add(new RecordReference {
                    Type = rec.RecordType ?? "",
                    TimeText = rec.TimeInSeconds > 0 ? TimeFormatter.Format(rec.TimeInSeconds) : (rec.Time > 0 ? TimeFormatter.Format(rec.Time) : ""),
                    HolderInfo = holder
                });
            }
            return list.OrderBy(x => x.Type).ToList();
        }

        private class BrokenRecordRow {
            public string Date, Gender, EventName, Stage, Athlete, Country, Time, RecordType;
        }
        private List<BrokenRecordRow> CollectBrokenRecords() {
            var rows = new List<BrokenRecordRow>();
            // 启发式：仅当成绩册中可识别的纪录条目里 _records 已被现场更新（即 HolderName 在本届运动员名单内）时收录
            if (_records == null) return rows;
            var swimmerKeys = new HashSet<string>(_swimmers.Where(s => !IsRelayMemberNote(s.Notes)).Select(s => (s.Name ?? "") + "|" + (s.Country ?? "")));
            foreach (var rec in _records) {
                string key = (rec.HolderName ?? "") + "|" + (rec.HolderCountry ?? "");
                if (!swimmerKeys.Contains(key)) continue;
                // 在 _swimmers 里查找该运动员的同项目最佳决赛/半决赛/预赛成绩，且应等于 rec.TimeInSeconds
                var sw = _swimmers.FirstOrDefault(s => (s.Name ?? "") == rec.HolderName && (s.Country ?? "") == rec.HolderCountry
                    && (s.EventName ?? "") == (rec.EventName ?? "") && (s.Gender ?? "") == (rec.Gender ?? ""));
                if (sw == null || sw.Results == null) continue;
                var hit = sw.Results.OrderBy(x => x.FinalTime).FirstOrDefault(x => x.FinalTime > 0 && Math.Abs(x.FinalTime - (rec.TimeInSeconds > 0 ? rec.TimeInSeconds : rec.Time)) < 0.01);
                if (hit == null) continue;
                rows.Add(new BrokenRecordRow {
                    Date = rec.Date ?? "",
                    Gender = rec.Gender ?? "",
                    EventName = rec.EventName ?? "",
                    Stage = hit.Stage ?? "",
                    Athlete = rec.HolderName ?? "",
                    Country = rec.HolderCountry ?? "",
                    Time = TimeFormatter.Format(hit.FinalTime),
                    RecordType = rec.RecordType ?? ""
                });
            }
            return rows.OrderBy(r => r.Date).ToList();
        }

        private class RankRow {
            public Swimmer Swimmer; public int Rank; public bool IsTie; public string TimeText;
        }
        private List<RankRow> GetEventFinalRanking(string gender, string eventName) {
            var list = _swimmers
                .Where(s => s.Gender == gender && s.EventName == eventName && !IsRelayMemberNote(s.Notes))
                .Select(s => new { Swimmer = s, R = s.GetResultForStage("决赛") })
                .Where(x => x.R != null && x.R.FinalTime > 0
                    && string.IsNullOrEmpty(x.R.Status)
                    && x.Swimmer.Status != "DSQ" && x.Swimmer.Status != "DNS" && x.Swimmer.Status != "DNF")
                .OrderBy(x => x.R.FinalTime)
                .ToList();
            var result = new List<RankRow>();
            int rank = 1;
            double prevTime = -1; int prevRank = 1;
            foreach (var x in list) {
                int useRank;
                bool tie = false;
                if (IsTieTime(x.R.FinalTime, prevTime)) { useRank = prevRank; tie = true; }
                else { useRank = rank; prevRank = rank; prevTime = x.R.FinalTime; }
                result.Add(new RankRow { Swimmer = x.Swimmer, Rank = useRank, IsTie = tie, TimeText = TimeFormatter.Format(x.R.FinalTime) });
                rank++;
            }
            return result;
        }

        private Dictionary<string, string> BuildTeamCoachMap() {
            var map = new Dictionary<string, string>();
            if (_programBook != null && _programBook.TeamStaffList != null) {
                foreach (var t in _programBook.TeamStaffList) {
                    if (string.IsNullOrEmpty(t.TeamName)) continue;
                    if (!map.ContainsKey(t.TeamName)) map[t.TeamName] = t.Coaches ?? "";
                }
            }
            return map;
        }

        private static int ParseDistanceMeters(string eventName) {
            if (string.IsNullOrEmpty(eventName)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)\s*[x×]\s*(\d+)");
            if (m.Success) return int.Parse(m.Groups[1].Value) * int.Parse(m.Groups[2].Value);
            var m2 = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)");
            return m2.Success ? int.Parse(m2.Groups[1].Value) : 0;
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

            // 合并表：每位运动员一行，每段距离一列（与 BuildSplitTimeReportHtmlFor 共用）
            var heatSwimmers = GetCurrentHeatSwimmers().OrderBy(s => s.Lane).ToList();
            sb.Append(BuildMergedSplitsTableHtml(heatSwimmers, _currentStage, _currentHeat, _isRelay));
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
