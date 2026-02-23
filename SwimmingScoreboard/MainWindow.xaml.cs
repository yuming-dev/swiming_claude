using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Fleck;
using System.IO;

namespace SwimmingScoreboard
{
    /// <summary>
    /// 泳道状态（计时Tab用）
    /// </summary>
    public class LaneStatus : INotifyPropertyChanged
    {
        private int _laneNumber;
        private string _swimmerName;
        private string _organization;
        private string _timeInput;
        private string _status;

        public int LaneNumber { get { return _laneNumber; } set { _laneNumber = value; OnPropertyChanged("LaneNumber"); } }
        public string SwimmerName { get { return _swimmerName; } set { _swimmerName = value; OnPropertyChanged("SwimmerName"); } }
        public string Organization { get { return _organization; } set { _organization = value; OnPropertyChanged("Organization"); } }
        public string TimeInput { get { return _timeInput; } set { _timeInput = value; OnPropertyChanged("TimeInput"); } }
        public string Status { get { return _status; } set { _status = value; OnPropertyChanged("Status"); } }

        public LaneStatus() { _status = "OK"; }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // WebSocket
        private WebSocketServer _server;
        private List<IWebSocketConnection> _allSockets = new List<IWebSocketConnection>();
        public int ConnectionCount { get { return _allSockets.Count; } }

        // 核心数据
        private ObservableCollection<Swimmer> _swimmers = new ObservableCollection<Swimmer>();
        private ObservableCollection<RelayTeam> _relayTeams = new ObservableCollection<RelayTeam>();
        private ObservableCollection<Organization> _organizations = new ObservableCollection<Organization>();
        private ObservableCollection<ScheduleItem> _dailySchedule = new ObservableCollection<ScheduleItem>();
        private ObservableCollection<RecordEntry> _records = new ObservableCollection<RecordEntry>();
        private ObservableCollection<string> _systemLogs = new ObservableCollection<string>();
        private ObservableCollection<BackupInfo> _savedCompetitions = new ObservableCollection<BackupInfo>();
        private ObservableCollection<HeatGroupInfo> _groupedSwimmersView = new ObservableCollection<HeatGroupInfo>();
        private ObservableCollection<LaneStatus> _laneStatuses = new ObservableCollection<LaneStatus>();

        // 公开属性供绑定
        public ObservableCollection<Swimmer> Swimmers { get { return _swimmers; } }
        public ObservableCollection<RelayTeam> RelayTeams { get { return _relayTeams; } }
        public ObservableCollection<Organization> Organizations { get { return _organizations; } }
        public ObservableCollection<ScheduleItem> DailySchedule { get { return _dailySchedule; } }
        public ObservableCollection<RecordEntry> Records { get { return _records; } }
        public ObservableCollection<string> SystemLogs { get { return _systemLogs; } }
        public ObservableCollection<BackupInfo> SavedCompetitions { get { return _savedCompetitions; } }
        public ObservableCollection<HeatGroupInfo> GroupedSwimmersView { get { return _groupedSwimmersView; } }
        public ObservableCollection<LaneStatus> LaneStatuses { get { return _laneStatuses; } }

        public BackupInfo SelectedBackup { get; set; }

        // 去重运动员列表
        public List<Swimmer> DistinctSwimmers
        {
            get
            {
                return _swimmers.GroupBy(s => s.Name).Select(g => g.First())
                    .OrderBy(s => s.Organization).ThenBy(s => s.Name).ToList();
            }
        }

        // 比赛基本信息
        private string _competitionName = "2026年游泳锦标赛";
        public string CompetitionName { get { return _competitionName; } set { _competitionName = value; OnPropertyChanged("CompetitionName"); } }

        private DateTime _startDate;
        private DateTime _endDate;
        public DateTime StartDate { get { return _startDate; } set { _startDate = value; OnPropertyChanged("StartDate"); AutoSaveData(); } }
        public DateTime EndDate { get { return _endDate; } set { _endDate = value; OnPropertyChanged("EndDate"); AutoSaveData(); } }

        // 竞赛官员
        public string OfficialReferee { get; set; }
        public string OfficialStarter { get; set; }
        public string OfficialChiefTimekeeper { get; set; }
        public string OfficialStrokeJudges { get; set; }
        public string OfficialTurnJudges { get; set; }
        public string OfficialSecretary { get; set; }

        // 统计属性
        public int TotalRegistrationCount { get { return _swimmers.Count; } }
        public int EventsCount { get { return _swimmers.Select(s => s.EventId).Distinct().Count(); } }
        public int RelayTeamsCount { get { return _relayTeams.Count; } }
        public int OrganizationsCount { get { return _organizations.Count; } }

        // 项目/阶段/组次级联
        public List<string> EventsList { get { return _swimmers.Select(s => s.EventId).Distinct().OrderBy(e => e).ToList(); } }
        public List<string> StagesList
        {
            get
            {
                return _swimmers.Where(s => s.EventId == CurrentEvent)
                    .Select(s => s.Stage).Distinct().OrderBy(s => s).ToList();
            }
        }
        public List<int> HeatsList
        {
            get
            {
                var list = _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == CurrentStage).ToList();
                int max = list.Count > 0 ? list.Max(s => s.HeatNumber) : 1;
                return Enumerable.Range(1, Math.Max(1, max)).ToList();
            }
        }

        // 当前组次选手
        public List<Swimmer> CurrentHeatSwimmers
        {
            get
            {
                return _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == CurrentStage && s.HeatNumber == _currentHeat)
                    .OrderBy(s => s.Lane).ToList();
            }
        }

        // 当前组次成绩
        public List<RaceResult> CurrentHeatResults
        {
            get
            {
                var results = new List<RaceResult>();
                var swimmers = CurrentHeatSwimmers;
                foreach (var s in swimmers)
                {
                    var r = s.Results.FirstOrDefault(x => x.EventId == CurrentEvent && x.Stage == CurrentStage && x.HeatNumber == _currentHeat);
                    if (r != null) results.Add(r);
                }
                var sorted = results.Where(r => r.IsValid).OrderBy(r => r.FinishTime)
                    .Concat(results.Where(r => !r.IsValid)).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i].IsValid) sorted[i].Rank = i + 1;
                }
                return sorted;
            }
        }

        // 项目总排名
        public List<RaceResult> EventRankings
        {
            get
            {
                var results = new List<RaceResult>();
                var swimmers = _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == CurrentStage).ToList();
                foreach (var s in swimmers)
                {
                    var r = s.Results.FirstOrDefault(x => x.EventId == CurrentEvent && x.Stage == CurrentStage);
                    if (r != null) results.Add(r);
                }
                var sorted = results.Where(r => r.IsValid).OrderBy(r => r.FinishTime)
                    .Concat(results.Where(r => !r.IsValid)).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i].IsValid) sorted[i].OverallRank = i + 1;
                }
                return sorted;
            }
        }

        // 编排统计
        public List<EventSummary> EventSummaries
        {
            get
            {
                var groups = _swimmers.GroupBy(s => s.EventId);
                var summaries = new List<EventSummary>();
                foreach (var g in groups)
                {
                    int uniqueCount = g.Select(s => s.Name).Distinct().Count();
                    int maxHeat = g.Max(s => s.HeatNumber);
                    double bestTime = g.Where(s => s.ReportedTime > 0).Any() ? g.Where(s => s.ReportedTime > 0).Min(s => s.ReportedTime) : 0;
                    summaries.Add(new EventSummary
                    {
                        EventName = g.Key,
                        AthleteCount = uniqueCount,
                        HeatCount = maxHeat,
                        BestTime = bestTime,
                        BestTimeDisplay = TimeHelper.FormatTime(bestTime)
                    });
                }
                return summaries.OrderBy(s => s.EventName).ToList();
            }
        }

        // 级联选择状态
        private string _currentEvent;
        private string _currentStage = "预赛";
        private int _currentHeat = 1;

        public string CurrentEvent
        {
            get { if (string.IsNullOrEmpty(_currentEvent) && EventsList.Count > 0) _currentEvent = EventsList[0]; return _currentEvent; }
            set
            {
                if (_currentEvent == value) return;
                _currentEvent = value;
                OnPropertyChanged("CurrentEvent");
                OnPropertyChanged("StagesList");
                var stages = StagesList;
                if (stages.Count > 0) { _currentStage = stages[0]; OnPropertyChanged("CurrentStage"); }
                OnPropertyChanged("HeatsList");
                CurrentHeat = 1;
                RefreshGroupedView();
                Broadcast();
            }
        }

        public string CurrentStage
        {
            get { return _currentStage; }
            set
            {
                if (_currentStage == value) return;
                _currentStage = value;
                OnPropertyChanged("CurrentStage");
                OnPropertyChanged("HeatsList");
                CurrentHeat = 1;
                RefreshGroupedView();
                Broadcast();
            }
        }

        public int CurrentHeat
        {
            get { return _currentHeat; }
            set
            {
                _currentHeat = value;
                OnPropertyChanged("CurrentHeat");
                OnPropertyChanged("CurrentHeatSwimmers");
                OnPropertyChanged("CurrentHeatResults");
                OnPropertyChanged("EventRankings");
                RefreshLaneStatuses();
                Broadcast();
            }
        }

        // 大屏控制
        private string _displayAnnouncement = "欢迎光临游泳竞赛现场";
        public string DisplayAnnouncement { get { return _displayAnnouncement; } set { _displayAnnouncement = value; OnPropertyChanged("DisplayAnnouncement"); } }
        private int _pageFlipInterval = 10;
        public int PageFlipInterval { get { return _pageFlipInterval; } set { _pageFlipInterval = value; OnPropertyChanged("PageFlipInterval"); } }

        public ObservableCollection<string> TimeOptions
        {
            get
            {
                return new ObservableCollection<string> {
                    "08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30",
                    "13:30", "14:00", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00",
                    "19:00", "19:30", "20:00", "20:30", "21:00"
                };
            }
        }

        // ===== 泳道分配规则 =====
        private static readonly int[] FinalsLaneOrder = { 4, 5, 3, 6, 2, 7, 1, 8 };

        // ======================== 构造函数 ========================
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            _startDate = DateTime.Now;
            _endDate = DateTime.Now.AddDays(3);
            OfficialReferee = "王裁判长"; OfficialStarter = "李发令员"; OfficialChiefTimekeeper = "张计时长";
            OfficialStrokeJudges = "途中裁判1\n途中裁判2"; OfficialTurnJudges = "转身裁判1\n转身裁判2"; OfficialSecretary = "陈记录长";

            // 初始化8泳道
            for (int i = 1; i <= 8; i++) _laneStatuses.Add(new LaneStatus { LaneNumber = i });

            RefreshBackupList();
            AutoLoadData();
            StartServer();

            if (EventsList.Count > 0)
            {
                CurrentEvent = EventsList[0];
                RefreshGroupedView();
            }
        }

        // ======================== 数据持久化 ========================
        private void RefreshBackupList()
        {
            _savedCompetitions.Clear();
            if (!Directory.Exists("Database")) Directory.CreateDirectory("Database");
            foreach (var f in Directory.GetFiles("Database", "*.json"))
            {
                var fi = new FileInfo(f);
                _savedCompetitions.Add(new BackupInfo { Name = Path.GetFileNameWithoutExtension(fi.Name), FilePath = fi.FullName, LastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
            }
        }

        private void AutoSaveData()
        {
            try
            {
                var data = new CompetitionData
                {
                    CompetitionName = CompetitionName,
                    StartDate = StartDate.ToString("yyyy-MM-dd"),
                    EndDate = EndDate.ToString("yyyy-MM-dd"),
                    Swimmers = _swimmers.ToList(),
                    RelayTeams = _relayTeams.ToList(),
                    Organizations = _organizations.ToList(),
                    Schedule = _dailySchedule.ToList(),
                    Records = _records.ToList(),
                    OfficialReferee = OfficialReferee,
                    OfficialStarter = OfficialStarter,
                    OfficialChiefTimekeeper = OfficialChiefTimekeeper,
                    OfficialStrokeJudges = OfficialStrokeJudges,
                    OfficialTurnJudges = OfficialTurnJudges,
                    OfficialSecretary = OfficialSecretary
                };
                string fileName = string.Format("Database/{0}.json", CompetitionName);
                File.WriteAllText(fileName, JsonConvert.SerializeObject(data, Formatting.Indented));
                RefreshBackupList();
            }
            catch { }
        }

        private void AutoLoadData()
        {
            try
            {
                string fileName = string.Format("Database/{0}.json", CompetitionName);
                if (File.Exists(fileName))
                {
                    var data = JsonConvert.DeserializeObject<CompetitionData>(File.ReadAllText(fileName));
                    if (data != null)
                    {
                        _swimmers.Clear(); foreach (var s in data.Swimmers) _swimmers.Add(s);
                        _relayTeams.Clear(); foreach (var r in data.RelayTeams) _relayTeams.Add(r);
                        _organizations.Clear(); foreach (var o in data.Organizations) _organizations.Add(o);
                        _dailySchedule.Clear(); foreach (var s in data.Schedule) _dailySchedule.Add(s);
                        _records.Clear(); foreach (var r in data.Records) _records.Add(r);

                        if (!string.IsNullOrEmpty(data.OfficialReferee)) OfficialReferee = data.OfficialReferee;
                        if (!string.IsNullOrEmpty(data.OfficialStarter)) OfficialStarter = data.OfficialStarter;
                        if (!string.IsNullOrEmpty(data.OfficialChiefTimekeeper)) OfficialChiefTimekeeper = data.OfficialChiefTimekeeper;
                        if (!string.IsNullOrEmpty(data.OfficialStrokeJudges)) OfficialStrokeJudges = data.OfficialStrokeJudges;
                        if (!string.IsNullOrEmpty(data.OfficialTurnJudges)) OfficialTurnJudges = data.OfficialTurnJudges;
                        if (!string.IsNullOrEmpty(data.OfficialSecretary)) OfficialSecretary = data.OfficialSecretary;

                        NotifyAllChanges();
                        if (EventsList.Count > 0) { CurrentEvent = EventsList[0]; RefreshGroupedView(); }
                    }
                }
            }
            catch { }
        }

        private void LoadBackup_Click(object sender, RoutedEventArgs e) { if (SelectedBackup != null) { CompetitionName = SelectedBackup.Name; AutoLoadData(); } }
        private void DeleteBackup_Click(object sender, RoutedEventArgs e) { if (SelectedBackup != null) { File.Delete(SelectedBackup.FilePath); RefreshBackupList(); } }
        private void SaveData_Click(object sender, RoutedEventArgs e) { AutoSaveData(); MessageBox.Show("保存完成"); }
        private void ClearDatabase_Click(object sender, RoutedEventArgs e)
        {
            string warning = string.Format("警告：您正在清除 [{0}] 的所有数据！\n这将删除所有已报名的运动员和所有已录入的成绩且无法撤销。\n\n是否继续？", CompetitionName);
            if (MessageBox.Show(warning, "高危操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _swimmers.Clear(); _relayTeams.Clear(); _organizations.Clear(); _dailySchedule.Clear(); _records.Clear();
                _groupedSwimmersView.Clear();
                NotifyAllChanges(); AutoSaveData();
                AddLog("！！！当前数据库已清空复位！！！");
                MessageBox.Show("数据已清空，您可以开始重新录入。");
            }
        }

        // ======================== WebSocket ========================
        private void StartServer()
        {
            try
            {
                _server = new WebSocketServer("ws://0.0.0.0:3002");
                _server.Start(socket =>
                {
                    socket.OnOpen = () => { _allSockets.Add(socket); this.Dispatcher.Invoke((Action)(() => OnPropertyChanged("ConnectionCount"))); };
                    socket.OnClose = () => { _allSockets.Remove(socket); this.Dispatcher.Invoke((Action)(() => OnPropertyChanged("ConnectionCount"))); };
                    socket.OnMessage = message => this.Dispatcher.Invoke((Action)(() => HandleMessage(message)));
                });
                AddLog("WebSocket服务器已启动 (ws://0.0.0.0:3002)");
            }
            catch (Exception ex) { AddLog("WebSocket启动失败: " + ex.Message); }
        }

        private void HandleMessage(string message)
        {
            try
            {
                var data = Newtonsoft.Json.Linq.JObject.Parse(message);
                string type = data["type"].ToString();

                if (type == "REGISTER_SWIMMER")
                {
                    string name = data["name"].ToString();
                    string gender = data["gender"].ToString();
                    string eventId = data["eventId"].ToString();
                    string org = data["organization"].ToString();
                    double reportedTime = data["reportedTime"] != null ? (double)data["reportedTime"] : 0;

                    if (!_swimmers.Any(s => s.Name == name && s.EventId == eventId && s.Stage == "预赛"))
                    {
                        _swimmers.Add(new Swimmer
                        {
                            Name = name, Gender = gender, EventId = eventId, Organization = org,
                            ReportedTime = reportedTime, Stage = "预赛", IsQualified = true
                        });
                        NotifyAllChanges(); AutoSaveData();
                        AddLog("远程报名: " + name + " - " + eventId);
                    }
                }
                else if (type == "TIMING_INPUT")
                {
                    int lane = (int)data["lane"];
                    double time = (double)data["time"];
                    string status = data["status"] != null ? data["status"].ToString() : "OK";
                    var ls = _laneStatuses.FirstOrDefault(l => l.LaneNumber == lane);
                    if (ls != null) { ls.TimeInput = TimeHelper.FormatTime(time); ls.Status = status; }
                    AddLog(string.Format("计时输入: 泳道{0} = {1}", lane, TimeHelper.FormatTime(time)));
                }
            }
            catch { }
        }

        private void Broadcast()
        {
            var data = GetLiveTimingData();
            var msg = new { type = "SHOW_LIVE_TIMING", data = data, interval = PageFlipInterval };
            string json = JsonConvert.SerializeObject(msg);
            foreach (var s in _allSockets) try { s.Send(json); } catch { }
        }

        private void SendDisplayCommand(string type, object data = null)
        {
            var msg = new { type = type, data = data ?? GetLiveTimingData(), interval = PageFlipInterval };
            string json = JsonConvert.SerializeObject(msg);
            foreach (var s in _allSockets) try { s.Send(json); } catch { }
            AddLog("大屏显示指令: " + type);
        }

        private object GetLiveTimingData()
        {
            return new
            {
                competitionName = CompetitionName,
                eventName = CurrentEvent,
                stage = CurrentStage,
                heatNumber = CurrentHeat,
                lanes = _laneStatuses.Select(l => new
                {
                    lane = l.LaneNumber,
                    name = l.SwimmerName,
                    org = l.Organization,
                    time = l.TimeInput,
                    status = l.Status
                }).ToList(),
                allSwimmers = _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == CurrentStage)
                    .Select(s => new
                    {
                        name = s.Name, org = s.Organization, heat = s.HeatNumber, lane = s.Lane,
                        time = s.BestTime, timeDisplay = s.BestTimeDisplay, rank = s.CurrentRank
                    }).ToList()
            };
        }

        // ======================== 报名功能 ========================
        private void ManualRegister_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(RegName.Text) || RegGender.SelectedItem == null || string.IsNullOrEmpty(RegOrg.Text))
            {
                MessageBox.Show("请至少填写姓名、性别和参赛单位"); return;
            }

            string birthStr = RegBirthPicker.SelectedDate.HasValue ? RegBirthPicker.SelectedDate.Value.ToString("yyyy-MM-dd") : "";
            string ageGroup = RegAgeGroup.SelectedItem != null ? RegAgeGroup.SelectedItem.ToString() : "";
            string phone = RegPhone.Text;

            // 添加每个填写了的项目
            int added = 0;
            added += TryAddEvent(RegDist1, RegStroke1, RegTime1, birthStr, ageGroup, phone);
            added += TryAddEvent(RegDist2, RegStroke2, RegTime2, birthStr, ageGroup, phone);
            added += TryAddEvent(RegDist3, RegStroke3, RegTime3, birthStr, ageGroup, phone);

            if (added == 0) { MessageBox.Show("请至少选择一个参赛项目（距离+泳姿）"); return; }

            NotifyAllChanges();
            AddLog(string.Format("运动员 [{0}] 已报名 {1} 个项目", RegName.Text, added));
        }

        private int TryAddEvent(ComboBox distCb, ComboBox strokeCb, TextBox timeTb, string birthStr, string ageGroup, string phone)
        {
            if (distCb.SelectedItem == null || strokeCb.SelectedItem == null) return 0;
            string dist = distCb.SelectedItem.ToString();
            string stroke = strokeCb.SelectedItem.ToString();
            string eventId = RegGender.SelectedItem.ToString() + "子" + dist + stroke;
            double reportedTime = TimeHelper.ParseTime(timeTb.Text);

            if (_swimmers.Any(s => s.Name == RegName.Text && s.EventId == eventId))
            {
                MessageBox.Show("该选手已报名 " + eventId + "，请勿重复添加。");
                return 0;
            }

            _swimmers.Add(new Swimmer
            {
                Name = RegName.Text,
                Gender = RegGender.SelectedItem.ToString(),
                Organization = RegOrg.Text,
                BirthDate = birthStr,
                AgeGroup = ageGroup,
                Phone = phone,
                EventId = eventId,
                ReportedTime = reportedTime,
                Stage = "预赛",
                IsQualified = true
            });
            return 1;
        }

        private void FinalizeRegistration_Click(object sender, RoutedEventArgs e)
        {
            AutoSaveData();
            ClearRegForm_Click(null, null);
            MessageBox.Show("该运动员的所有报名项已正式入库。");
        }

        private void ClearRegForm_Click(object sender, RoutedEventArgs e)
        {
            RegName.Clear(); RegOrg.Clear(); RegPhone.Clear();
            RegBirthPicker.SelectedDate = null;
            RegGender.SelectedIndex = -1; RegAgeGroup.SelectedIndex = -1;
            RegDist1.SelectedIndex = -1; RegStroke1.SelectedIndex = -1; RegTime1.Clear();
            RegDist2.SelectedIndex = -1; RegStroke2.SelectedIndex = -1; RegTime2.Clear();
            RegDist3.SelectedIndex = -1; RegStroke3.SelectedIndex = -1; RegTime3.Clear();
        }

        // CSV/Excel导入
        private void ImportFromExcel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "数据文件 (*.csv;*.txt)|*.csv;*.txt|所有文件 (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(dialog.FileName, System.Text.Encoding.UTF8);
                    int count = 0;
                    foreach (var line in lines.Skip(1))
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var parts = line.Split(',', '\t', ';');
                        if (parts.Length < 5) continue;

                        string name = parts[0].Trim();
                        string gender = parts[1].Trim();
                        string org = parts[2].Trim();
                        string eventId = parts[3].Trim();
                        double reportedTime = TimeHelper.ParseTime(parts[4].Trim());
                        string ageGroup = parts.Length > 5 ? parts[5].Trim() : "";
                        string birthDate = parts.Length > 6 ? parts[6].Trim() : "";

                        if (!_swimmers.Any(s => s.Name == name && s.EventId == eventId))
                        {
                            _swimmers.Add(new Swimmer
                            {
                                Name = name, Gender = gender, Organization = org,
                                EventId = eventId, ReportedTime = reportedTime,
                                AgeGroup = ageGroup, BirthDate = birthDate,
                                Stage = "预赛", IsQualified = true
                            });
                            count++;
                        }
                    }
                    NotifyAllChanges(); AutoSaveData();
                    MessageBox.Show(string.Format("成功导入 {0} 条运动员报名数据", count));
                }
                catch (Exception ex) { MessageBox.Show("导入失败: " + ex.Message); }
            }
        }

        // 接力队报名
        private void AddRelayTeam_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(RelayTeamName.Text) || RelayEvent.SelectedItem == null)
            {
                MessageBox.Show("请填写参赛单位和接力项目"); return;
            }

            var team = new RelayTeam
            {
                TeamName = RelayTeamName.Text,
                EventId = RelayEvent.SelectedItem.ToString(),
                ReportedTime = TimeHelper.ParseTime(RelayTime.Text),
                Stage = "预赛"
            };
            if (!string.IsNullOrEmpty(RelayMember1.Text)) team.MemberNames.Add(RelayMember1.Text);
            if (!string.IsNullOrEmpty(RelayMember2.Text)) team.MemberNames.Add(RelayMember2.Text);
            if (!string.IsNullOrEmpty(RelayMember3.Text)) team.MemberNames.Add(RelayMember3.Text);
            if (!string.IsNullOrEmpty(RelayMember4.Text)) team.MemberNames.Add(RelayMember4.Text);

            _relayTeams.Add(team);
            NotifyAllChanges(); AutoSaveData();
            AddLog("接力队报名: " + team.TeamName + " - " + team.EventId);
            RelayTeamName.Clear(); RelayTime.Clear(); RelayMember1.Clear(); RelayMember2.Clear(); RelayMember3.Clear(); RelayMember4.Clear();
        }

        // 组织信息
        private void AddOrganization_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OrgName.Text)) { MessageBox.Show("请填写单位名称"); return; }
            _organizations.Add(new Organization { Name = OrgName.Text, LeaderName = OrgLeader.Text, CoachName = OrgCoach.Text, Phone = OrgPhone.Text });
            NotifyAllChanges(); AutoSaveData();
            OrgName.Clear(); OrgLeader.Clear(); OrgCoach.Clear(); OrgPhone.Clear();
        }

        // 报名表弹出
        private void ShowRegistrationList_Click(object sender, RoutedEventArgs e) { RegistrationModal.Visibility = Visibility.Visible; }
        private void CloseRegistrationList_Click(object sender, RoutedEventArgs e) { RegistrationModal.Visibility = Visibility.Collapsed; }

        // 分配参赛号码
        private void AssignBibNumbers_Click(object sender, RoutedEventArgs e)
        {
            var all = _swimmers.GroupBy(s => s.Name).Select(g => g.First()).OrderBy(s => s.Organization).ThenBy(s => s.Name).ToList();
            for (int i = 0; i < all.Count; i++)
            {
                string bib = (i + 1).ToString("D3");
                foreach (var s in _swimmers.Where(x => x.Name == all[i].Name)) s.BibNumber = bib;
            }
            AutoSaveData();
            MessageBox.Show("参赛号码已分配（共 " + all.Count + " 人）");
        }

        // ======================== 编排算法 ========================

        /// <summary>执行全场编排（蛇形编排 + 泳道分配）</summary>
        private void GenerateOrder_Click(object sender, RoutedEventArgs e)
        {
            if (_swimmers.Count == 0) { MessageBox.Show("无报名数据。"); return; }
            if (MessageBox.Show("执行全场编排？这将按照报名成绩进行蛇形分组和泳道分配。", "编排确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            // 1. 清理：去重，重置编排信息
            var allList = _swimmers.ToList();
            var groups = allList.GroupBy(s => new { s.Name, s.EventId });
            _swimmers.Clear();
            foreach (var g in groups)
            {
                var master = g.First();
                master.HeatNumber = 0;
                master.Lane = 0;
                master.Results.Clear();
                _swimmers.Add(master);
            }

            _dailySchedule.Clear();

            // 2. 按项目分别编排
            var events = _swimmers.Select(s => s.EventId).Distinct().OrderBy(e2 => GetEventSortWeight(e2)).ToList();
            var scheduleItems = new List<string>();

            foreach (var evt in events)
            {
                var athletes = _swimmers.Where(s => s.EventId == evt && s.Stage == "预赛").ToList();
                int count = athletes.Count;
                if (count == 0) continue;

                // 确定赛次路径
                if (count <= 8)
                {
                    // 直接决赛
                    foreach (var a in athletes) a.Stage = "决赛";
                    ArrangeHeatsForEvent(athletes, "决赛");
                    scheduleItems.Add(evt + " 决赛");
                }
                else if (count <= 16)
                {
                    // 预赛→决赛
                    ArrangeHeatsForEvent(athletes, "预赛");
                    scheduleItems.Add(evt + " 预赛");
                    scheduleItems.Add(evt + " 决赛");
                }
                else
                {
                    // 预赛→半决赛→决赛
                    ArrangeHeatsForEvent(athletes, "预赛");
                    scheduleItems.Add(evt + " 预赛");
                    scheduleItems.Add(evt + " 半决赛");
                    scheduleItems.Add(evt + " 决赛");
                }
            }

            // 3. 生成日程
            GenerateScheduleFromItems(scheduleItems);

            RefreshGroupedView();
            NotifyAllChanges();
            AutoSaveData();
            AddLog("全场编排完成（蛇形分组+泳道分配）。");
            MessageBox.Show("全场编排已完成！");
        }

        /// <summary>蛇形分组 + 泳道分配</summary>
        private void ArrangeHeatsForEvent(List<Swimmer> athletes, string stage)
        {
            int count = athletes.Count;
            if (count == 0) return;

            // 按报名成绩排序：有成绩的按时间升序，无成绩的随机排在最前（视为最慢）
            var rng = new Random();
            var withTime = athletes.Where(a => a.ReportedTime > 0).OrderBy(a => a.ReportedTime).ToList();
            var noTime = athletes.Where(a => a.ReportedTime <= 0).OrderBy(a => rng.Next()).ToList();

            // 从慢到快排列：无成绩→有成绩(慢→快)
            var sorted = noTime.Concat(withTime).ToList();

            // 计算组数
            int numHeats = (int)Math.Ceiling(count / 8.0);
            if (numHeats > 1)
            {
                // 确保每组至少3人
                while (count - (numHeats - 1) * 8 < 3 && numHeats > 1)
                {
                    numHeats--;
                }
            }

            // 蛇形分组：第1组最慢，最后一组最快
            int perHeat = (int)Math.Ceiling(count / (double)numHeats);
            for (int i = 0; i < sorted.Count; i++)
            {
                int heatIdx = i / perHeat;
                if (heatIdx >= numHeats) heatIdx = numHeats - 1;
                sorted[i].HeatNumber = heatIdx + 1;
                sorted[i].Stage = stage;
            }

            // 组内泳道分配
            for (int h = 1; h <= numHeats; h++)
            {
                var heatAthletes = sorted.Where(a => a.HeatNumber == h).ToList();
                // 组内按成绩排序（快→慢），然后按决赛泳道规则分配
                if (stage == "决赛" || stage == "半决赛")
                {
                    heatAthletes = heatAthletes.OrderBy(a => a.ReportedTime > 0 ? a.ReportedTime : 9999).ToList();
                }
                else
                {
                    // 预赛也按成绩分配泳道（快的在中间道）
                    heatAthletes = heatAthletes.OrderBy(a => a.ReportedTime > 0 ? a.ReportedTime : 9999).ToList();
                }

                for (int i = 0; i < heatAthletes.Count; i++)
                {
                    if (i < FinalsLaneOrder.Length)
                        heatAthletes[i].Lane = FinalsLaneOrder[i];
                    else
                        heatAthletes[i].Lane = i + 1;
                }
            }
        }

        /// <summary>从预赛/半决赛成绩编排下一阶段</summary>
        private void ProcessPromotion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CurrentEvent)) return;

            var currentStageSwimmers = _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == CurrentStage).ToList();
            // 收集已有成绩
            var results = new List<KeyValuePair<Swimmer, double>>();
            foreach (var s in currentStageSwimmers)
            {
                var r = s.Results.FirstOrDefault(x => x.EventId == CurrentEvent && x.Stage == CurrentStage && x.IsValid);
                if (r != null) results.Add(new KeyValuePair<Swimmer, double>(s, r.FinishTime));
            }

            if (results.Count == 0)
            {
                MessageBox.Show("当前阶段尚无有效成绩，无法晋级。"); return;
            }

            string nextStage = "";
            int qualifyCount = 8;
            if (CurrentStage == "预赛")
            {
                if (currentStageSwimmers.Count > 16) { nextStage = "半决赛"; qualifyCount = 16; }
                else { nextStage = "决赛"; qualifyCount = 8; }
            }
            else if (CurrentStage == "半决赛") { nextStage = "决赛"; qualifyCount = 8; }
            else { MessageBox.Show("已是决赛，无需晋级。"); return; }

            var ranked = results.OrderBy(r => r.Value).ToList();
            int actual = Math.Min(ranked.Count, qualifyCount);

            string confirmMsg = string.Format("[{0}] {1} 已完成。\n取前 {2} 名晋级 {3}，是否继续？", CurrentEvent, CurrentStage, actual, nextStage);
            if (MessageBox.Show(confirmMsg, "晋级确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            // 清理已有的下一阶段数据
            var existing = _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == nextStage).ToList();
            foreach (var old in existing) _swimmers.Remove(old);

            // 创建晋级记录
            var promoted = new List<Swimmer>();
            for (int i = 0; i < actual; i++)
            {
                var source = ranked[i].Key;
                var next = new Swimmer
                {
                    Name = source.Name, BibNumber = source.BibNumber, Gender = source.Gender,
                    Organization = source.Organization, BirthDate = source.BirthDate,
                    AgeGroup = source.AgeGroup, Phone = source.Phone,
                    EventId = source.EventId, Stage = nextStage,
                    ReportedTime = ranked[i].Value, // 用实际成绩作为种子成绩
                    IsQualified = true
                };
                promoted.Add(next);
                _swimmers.Add(next);
            }

            // 按成绩编排泳道
            ArrangeHeatsForEvent(promoted, nextStage);

            CurrentStage = nextStage;
            NotifyAllChanges(); AutoSaveData();
            AddLog(string.Format("{0} -> {1} 晋级处理完成（{2}人）", CurrentEvent, nextStage, actual));
            MessageBox.Show(string.Format("晋级成功！{0} 名选手已编排至{1}。", actual, nextStage));
        }

        /// <summary>项目排序权重</summary>
        private int GetEventSortWeight(string eventId)
        {
            int weight = 0;
            if (eventId.Contains("女")) weight += 0; else weight += 1000;
            if (eventId.Contains("50")) weight += 1;
            else if (eventId.Contains("100")) weight += 2;
            else if (eventId.Contains("200")) weight += 3;
            else if (eventId.Contains("400")) weight += 4;
            else if (eventId.Contains("800")) weight += 5;
            else if (eventId.Contains("1500")) weight += 6;
            if (eventId.Contains("自由")) weight += 0;
            else if (eventId.Contains("仰")) weight += 10;
            else if (eventId.Contains("蛙")) weight += 20;
            else if (eventId.Contains("蝶")) weight += 30;
            else if (eventId.Contains("混合")) weight += 40;
            return weight;
        }

        /// <summary>生成日程</summary>
        private void GenerateScheduleFromItems(List<string> items)
        {
            _dailySchedule.Clear();
            if (items.Count == 0) return;
            int days = Math.Max(1, (EndDate - StartDate).Days + 1);
            int perDay = (int)Math.Ceiling(items.Count / (double)days);
            string[] times = { "09:00", "09:30", "10:00", "10:30", "14:00", "14:30", "15:00", "15:30", "19:00", "19:30" };
            for (int i = 0; i < items.Count; i++)
            {
                _dailySchedule.Add(new ScheduleItem
                {
                    Date = StartDate.AddDays(Math.Min(days - 1, i / perDay)).ToString("yyyy-MM-dd"),
                    Time = times[i % perDay % times.Length],
                    Event = items[i]
                });
            }
        }

        private void GenerateSchedule_Click(object sender, RoutedEventArgs e) { GenerateOrder_Click(sender, e); }

        // ======================== 视图刷新 ========================
        private void RefreshGroupedView()
        {
            _groupedSwimmersView.Clear();
            var heatGroups = _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == CurrentStage && s.Lane > 0)
                .GroupBy(s => s.HeatNumber).OrderBy(g => g.Key);

            foreach (var hg in heatGroups)
            {
                _groupedSwimmersView.Add(new HeatGroupInfo
                {
                    GroupName = string.Format("{0} {1} - 第 {2} 组", CurrentEvent, CurrentStage, hg.Key),
                    Members = new ObservableCollection<Swimmer>(hg.OrderBy(s => s.Lane).ToList())
                });
            }
            OnPropertyChanged("GroupedSwimmersView");
        }

        private void RefreshLaneStatuses()
        {
            var heatSwimmers = CurrentHeatSwimmers;
            foreach (var ls in _laneStatuses)
            {
                var s = heatSwimmers.FirstOrDefault(x => x.Lane == ls.LaneNumber);
                if (s != null)
                {
                    ls.SwimmerName = s.Name;
                    ls.Organization = s.Organization;
                    // 查看是否已有成绩
                    var r = s.Results.FirstOrDefault(x => x.EventId == CurrentEvent && x.Stage == CurrentStage && x.HeatNumber == _currentHeat);
                    ls.TimeInput = r != null ? TimeHelper.FormatTime(r.FinishTime) : "";
                    ls.Status = r != null ? r.Status : "OK";
                }
                else
                {
                    ls.SwimmerName = "--";
                    ls.Organization = "";
                    ls.TimeInput = "";
                    ls.Status = "OK";
                }
            }
        }

        // 编排微调
        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) { AutoSaveData(); }
        private void ApplyManualAdjustment_Click(object sender, RoutedEventArgs e)
        {
            RefreshGroupedView(); NotifyAllChanges(); AutoSaveData();
            AddLog("手动编排微调已应用。");
            MessageBox.Show("手动调整已应用并保存。");
        }

        // ======================== 现场计时 ========================
        private void ConfirmLaneResult_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            int lane = (int)btn.Tag;
            var ls = _laneStatuses.FirstOrDefault(l => l.LaneNumber == lane);
            if (ls == null || string.IsNullOrEmpty(ls.SwimmerName) || ls.SwimmerName == "--") return;

            double time = TimeHelper.ParseTime(ls.TimeInput);
            var swimmer = _swimmers.FirstOrDefault(s => s.Name == ls.SwimmerName && s.EventId == CurrentEvent && s.Stage == CurrentStage);
            if (swimmer == null) return;

            // 移除旧成绩
            var old = swimmer.Results.FirstOrDefault(r => r.EventId == CurrentEvent && r.Stage == CurrentStage && r.HeatNumber == CurrentHeat);
            if (old != null) swimmer.Results.Remove(old);

            var result = new RaceResult
            {
                EventId = CurrentEvent,
                Stage = CurrentStage,
                HeatNumber = CurrentHeat,
                Lane = lane,
                FinishTime = time,
                Status = ls.Status,
                SwimmerName = swimmer.Name,
                Organization = swimmer.Organization
            };
            swimmer.AddResult(result);

            NotifyAllChanges(); AutoSaveData();
            AddLog(string.Format("泳道{0} [{1}] 成绩确认: {2} ({3})", lane, swimmer.Name, TimeHelper.FormatTime(time), ls.Status));
            Broadcast();
        }

        private void ConfirmAllHeatResults_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ls in _laneStatuses)
            {
                if (!string.IsNullOrEmpty(ls.SwimmerName) && ls.SwimmerName != "--" && !string.IsNullOrEmpty(ls.TimeInput))
                {
                    double time = TimeHelper.ParseTime(ls.TimeInput);
                    var swimmer = _swimmers.FirstOrDefault(s => s.Name == ls.SwimmerName && s.EventId == CurrentEvent && s.Stage == CurrentStage);
                    if (swimmer == null) continue;

                    var old = swimmer.Results.FirstOrDefault(r => r.EventId == CurrentEvent && r.Stage == CurrentStage && r.HeatNumber == CurrentHeat);
                    if (old != null) swimmer.Results.Remove(old);

                    swimmer.AddResult(new RaceResult
                    {
                        EventId = CurrentEvent, Stage = CurrentStage, HeatNumber = CurrentHeat,
                        Lane = ls.LaneNumber, FinishTime = time, Status = ls.Status,
                        SwimmerName = swimmer.Name, Organization = swimmer.Organization
                    });
                }
            }
            NotifyAllChanges(); AutoSaveData();
            AddLog("本组全部成绩已确认");
            Broadcast();
            MessageBox.Show("本组成绩已全部确认。");
        }

        private void PrevHeat_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentHeat > 1) CurrentHeat--;
        }

        private void NextHeat_Click(object sender, RoutedEventArgs e)
        {
            var maxHeat = HeatsList.Count > 0 ? HeatsList.Max() : 1;
            if (CurrentHeat < maxHeat) CurrentHeat++;
        }

        // ======================== 大屏控制 ========================
        private void PushEventList_Click(object sender, RoutedEventArgs e)
        {
            var data = new
            {
                competitionName = CompetitionName,
                events = EventSummaries.Select(s => new { name = s.EventName, athleteCount = s.AthleteCount, heatCount = s.HeatCount }).ToList()
            };
            SendDisplayCommand("SHOW_EVENT_LIST", data);
            ScreenStatusText.Text = "[全场项目预告]";
        }

        private void PushHeatSheet_Click(object sender, RoutedEventArgs e)
        {
            var heats = _swimmers.Where(s => s.EventId == CurrentEvent && s.Stage == CurrentStage && s.Lane > 0)
                .GroupBy(s => s.HeatNumber).OrderBy(g => g.Key)
                .Select(g => new
                {
                    heatNum = g.Key,
                    lanes = g.OrderBy(s => s.Lane).Select(s => new { lane = s.Lane, name = s.Name, org = s.Organization, seedTime = TimeHelper.FormatTime(s.ReportedTime) }).ToList()
                }).ToList();
            SendDisplayCommand("SHOW_HEAT_SHEET", new { competitionName = CompetitionName, eventName = CurrentEvent, stage = CurrentStage, heats = heats });
            ScreenStatusText.Text = "[编排表]";
        }

        private void PushCurrentHeat_Click(object sender, RoutedEventArgs e)
        {
            SendDisplayCommand("SHOW_CURRENT_HEAT");
            ScreenStatusText.Text = "[当前组次出场]";
        }

        private void PushLiveTiming_Click(object sender, RoutedEventArgs e)
        {
            Broadcast();
            ScreenStatusText.Text = "[实时计时画面]";
        }

        private void PushHeatResults_Click(object sender, RoutedEventArgs e)
        {
            var results = CurrentHeatResults.Select(r => new { rank = r.Rank, lane = r.Lane, name = r.SwimmerName, org = r.Organization, time = r.FinishTimeDisplay, status = r.Status }).ToList();
            SendDisplayCommand("SHOW_HEAT_RESULTS", new { competitionName = CompetitionName, eventName = CurrentEvent, stage = CurrentStage, heatNumber = CurrentHeat, results = results });
            ScreenStatusText.Text = "[本组成绩]";
        }

        private void PushEventRanking_Click(object sender, RoutedEventArgs e)
        {
            var rankings = EventRankings.Select(r => new { rank = r.OverallRank, name = r.SwimmerName, org = r.Organization, heat = r.HeatNumber, time = r.FinishTimeDisplay, status = r.Status }).ToList();
            SendDisplayCommand("SHOW_EVENT_RANKING", new { competitionName = CompetitionName, eventName = CurrentEvent, stage = CurrentStage, rankings = rankings });
            ScreenStatusText.Text = "[项目总排名]";
        }

        private void PushFinalsListDisplay_Click(object sender, RoutedEventArgs e)
        {
            var rankings = EventRankings.Where(r => r.IsValid).Take(8)
                .Select(r => new { rank = r.OverallRank, name = r.SwimmerName, org = r.Organization, time = r.FinishTimeDisplay }).ToList();
            SendDisplayCommand("SHOW_FINALS_LIST", new { competitionName = CompetitionName, eventName = CurrentEvent, qualifiers = rankings });
            ScreenStatusText.Text = "[决赛名单]";
        }

        private void PushMedalList_Click(object sender, RoutedEventArgs e)
        {
            var top3 = EventRankings.Where(r => r.IsValid).Take(3)
                .Select(r => new { rank = r.OverallRank, name = r.SwimmerName, org = r.Organization, time = r.FinishTimeDisplay }).ToList();
            SendDisplayCommand("SHOW_MEDAL_LIST", new { competitionName = CompetitionName, eventName = CurrentEvent, medals = top3 });
            ScreenStatusText.Text = "[颁奖榜]";
        }

        private void PushAwardCeremony_Click(object sender, RoutedEventArgs e) { SendDisplayCommand("SHOW_AWARDS"); ScreenStatusText.Text = "[颁奖仪式]"; }
        private void PushSponsorAd_Click(object sender, RoutedEventArgs e) { SendDisplayCommand("SHOW_ADVERT"); ScreenStatusText.Text = "[广告播放]"; }
        private void PushWelcomeScreen_Click(object sender, RoutedEventArgs e) { SendDisplayCommand("SHOW_WELCOME"); ScreenStatusText.Text = "[欢迎画面]"; }
        private void PushPauseScreen_Click(object sender, RoutedEventArgs e) { SendDisplayCommand("SHOW_PAUSE"); ScreenStatusText.Text = "[暂停画面]"; }
        private void PushAnnouncement_Click(object sender, RoutedEventArgs e) { SendDisplayCommand("SHOW_ANNOUNCEMENT", new { content = DisplayAnnouncement }); ScreenStatusText.Text = "[紧急公告]"; }
        private void ClearAnnouncement_Click(object sender, RoutedEventArgs e) { DisplayAnnouncement = ""; SendDisplayCommand("CLEAR_ANNOUNCEMENT"); }
        private void PushTeamScores_Click(object sender, RoutedEventArgs e)
        {
            var teamScores = CalculateTeamScores();
            SendDisplayCommand("SHOW_TEAM_SCORES", new { competitionName = CompetitionName, teams = teamScores });
            ScreenStatusText.Text = "[团体总分]";
        }

        // ======================== 团体总分计算 ========================
        private List<object> CalculateTeamScores()
        {
            int[] pointsTable = { 9, 7, 6, 5, 4, 3, 2, 1 };
            var teamPoints = new Dictionary<string, double>();

            // 个人项目
            var events = _swimmers.Select(s => s.EventId).Distinct();
            foreach (var evt in events)
            {
                // 取决赛成绩，如无决赛则取最高阶段
                string bestStage = "决赛";
                var stageSwimmers = _swimmers.Where(s => s.EventId == evt && s.Stage == bestStage).ToList();
                if (stageSwimmers.Count == 0) { bestStage = "半决赛"; stageSwimmers = _swimmers.Where(s => s.EventId == evt && s.Stage == bestStage).ToList(); }
                if (stageSwimmers.Count == 0) { bestStage = "预赛"; stageSwimmers = _swimmers.Where(s => s.EventId == evt && s.Stage == bestStage).ToList(); }

                var results = new List<KeyValuePair<string, double>>();
                foreach (var s in stageSwimmers)
                {
                    var r = s.Results.FirstOrDefault(x => x.EventId == evt && x.Stage == bestStage && x.IsValid);
                    if (r != null) results.Add(new KeyValuePair<string, double>(s.Organization, r.FinishTime));
                }
                var ranked = results.OrderBy(r => r.Value).ToList();
                for (int i = 0; i < Math.Min(ranked.Count, pointsTable.Length); i++)
                {
                    string org = ranked[i].Key;
                    if (!teamPoints.ContainsKey(org)) teamPoints[org] = 0;
                    teamPoints[org] += pointsTable[i];
                }
            }

            // 接力项目（积分翻倍）
            foreach (var relay in _relayTeams.Where(r => r.FinishTime > 0 && r.Status == "OK"))
            {
                // 简化：接力积分按排名直接加分
                if (!teamPoints.ContainsKey(relay.TeamName)) teamPoints[relay.TeamName] = 0;
                teamPoints[relay.TeamName] += relay.PointsEarned * 2;
            }

            return teamPoints.OrderByDescending(kv => kv.Value)
                .Select((kv, idx) => (object)new { rank = idx + 1, name = kv.Key, totalPoints = kv.Value })
                .ToList();
        }

        // ======================== 报表生成 ========================

        private void PrintSchedule_Click(object sender, RoutedEventArgs e)
        {
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:40px;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid black;padding:10px;text-align:center;} th{background:#f2f2f2;}</style></head><body>" +
                "<h1 style='text-align:center;'>" + CompetitionName + " 竞赛日程表</h1>" +
                "<table><tr><th>日期</th><th>时间</th><th>项目内容</th></tr>";
            foreach (var item in DailySchedule) html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td></tr>", item.Date, item.Time, item.Event);
            html += "</table></body></html>";
            WriteAndOpenHtml("Schedule.html", html);
        }

        private void PrintManual_Click(object sender, RoutedEventArgs e)
        {
            string html = "<html><head><meta charset='UTF-8'><style>" +
                "body{font-family:SimSun;padding:40px;line-height:1.6;} h1,h2,h3{text-align:center;} .section{margin-top:30px;} " +
                "table{border-collapse:collapse;width:100%;margin-top:10px;} th,td{border:1px solid black;padding:8px;text-align:center;} th{background:#f2f2f2;} " +
                ".team-name{background:#f9f9f9;font-weight:bold;text-align:left;padding-left:15px;} .page-break{page-break-before:always;}" +
                "</style></head><body>";

            // 标题
            html += string.Format("<h1>{0}</h1><h2>竞 赛 秩 序 册</h2>", CompetitionName);

            // 官员名单
            html += "<div class='section'><h3>一、竞赛官员名单</h3>";
            html += string.Format("<p><b>裁判长：</b>{0}</p>", OfficialReferee ?? "");
            html += string.Format("<p><b>发令员：</b>{0}</p>", OfficialStarter ?? "");
            html += string.Format("<p><b>计时长：</b>{0}</p>", OfficialChiefTimekeeper ?? "");
            html += string.Format("<p><b>途中裁判：</b><br/>{0}</p>", (OfficialStrokeJudges ?? "").Replace("\n", "<br/>"));
            html += string.Format("<p><b>转身裁判：</b><br/>{0}</p>", (OfficialTurnJudges ?? "").Replace("\n", "<br/>"));
            html += string.Format("<p><b>记录长：</b>{0}</p>", OfficialSecretary ?? "");
            html += "</div>";

            // 运动员名单
            html += "<div class='section page-break'><h3>二、参赛运动员名单</h3>";
            var distinctSwimmers = _swimmers.GroupBy(s => s.Name).Select(g => g.First()).ToList();
            var teams = distinctSwimmers.GroupBy(s => s.Organization).OrderBy(g => g.Key).ToList();
            html += "<table><tr><th>参赛单位</th><th>号码</th><th>姓名</th><th>性别</th><th>年龄组</th></tr>";
            foreach (var team in teams)
            {
                bool first = true;
                var members = team.OrderBy(s => s.BibNumber).ToList();
                foreach (var m in members)
                {
                    html += "<tr>";
                    if (first) { html += string.Format("<td rowspan='{0}' class='team-name'>{1}</td>", members.Count, team.Key); first = false; }
                    html += string.Format("<td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td></tr>", m.BibNumber, m.Name, m.Gender, m.AgeGroup);
                }
            }
            html += "</table></div>";

            // 编排表
            html += "<div class='section page-break'><h3>三、各项目编排表</h3>";
            var allEvents = _swimmers.Where(s => s.Lane > 0).GroupBy(s => new { s.EventId, s.Stage })
                .OrderBy(g => GetEventSortWeight(g.Key.EventId)).ThenBy(g => g.Key.Stage);
            foreach (var eg in allEvents)
            {
                var heats = eg.GroupBy(s => s.HeatNumber).OrderBy(h => h.Key);
                foreach (var heat in heats)
                {
                    html += string.Format("<h4 style='text-align:left;margin-top:25px;'>{0} ({1}) - 第 {2} 组</h4>", eg.Key.EventId, eg.Key.Stage, heat.Key);
                    html += "<table><tr><th width='50'>泳道</th><th width='100'>号码</th><th>姓名</th><th>参赛单位</th><th width='100'>报名成绩</th></tr>";
                    foreach (var s in heat.OrderBy(x => x.Lane))
                    {
                        html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td></tr>",
                            s.Lane, s.BibNumber, s.Name, s.Organization, s.ReportedTimeDisplay);
                    }
                    html += "</table>";
                }
            }
            html += "</div>";

            // 日程
            html += "<div class='section page-break'><h3>四、竞赛日程表</h3>";
            if (DailySchedule.Count > 0)
            {
                html += "<table><tr><th>日期</th><th>时间</th><th>比赛内容</th></tr>";
                foreach (var s in DailySchedule) html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td></tr>", s.Date, s.Time, s.Event);
                html += "</table>";
            }
            html += "</div></body></html>";
            WriteAndOpenHtml("Manual.html", html);
            AddLog("秩序册导出成功");
        }

        private void PrintHeatSheet_Click(object sender, RoutedEventArgs e)
        {
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:30px;} table{border-collapse:collapse;width:100%;margin-bottom:20px;} th,td{border:1px solid black;padding:8px;text-align:center;} th{background:#f2f2f2;} h1,h2,h3{text-align:center;}</style></head><body>" +
                "<h1>" + CompetitionName + "</h1><h2>编排一览表</h2>";

            var allEvents = _swimmers.Where(s => s.Lane > 0).GroupBy(s => new { s.EventId, s.Stage })
                .OrderBy(g => GetEventSortWeight(g.Key.EventId));
            foreach (var eg in allEvents)
            {
                var heats = eg.GroupBy(s => s.HeatNumber).OrderBy(h => h.Key);
                foreach (var heat in heats)
                {
                    html += string.Format("<h3>{0} ({1}) - 第 {2} 组</h3>", eg.Key.EventId, eg.Key.Stage, heat.Key);
                    html += "<table><tr><th>泳道</th><th>号码</th><th>姓名</th><th>单位</th><th>报名成绩</th><th>备注</th></tr>";
                    for (int lane = 1; lane <= 8; lane++)
                    {
                        var s = heat.FirstOrDefault(x => x.Lane == lane);
                        if (s != null)
                            html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td></td></tr>", lane, s.BibNumber, s.Name, s.Organization, s.ReportedTimeDisplay);
                        else
                            html += string.Format("<tr><td>{0}</td><td></td><td></td><td></td><td></td><td></td></tr>", lane);
                    }
                    html += "</table>";
                }
            }
            html += "</body></html>";
            WriteAndOpenHtml("HeatSheet.html", html);
        }

        private void PrintCheckInSheet_Click(object sender, RoutedEventArgs e)
        {
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:30px;} table{border-collapse:collapse;width:100%;margin-bottom:20px;} th,td{border:1px solid black;padding:10px;text-align:center;} th{background:#f2f2f2;} h1,h3{text-align:center;}</style></head><body>" +
                "<h1>" + CompetitionName + " 检录表</h1>";

            var allEvents = _swimmers.Where(s => s.Lane > 0).GroupBy(s => new { s.EventId, s.Stage, s.HeatNumber })
                .OrderBy(g => GetEventSortWeight(g.Key.EventId)).ThenBy(g => g.Key.HeatNumber);
            foreach (var heat in allEvents)
            {
                html += string.Format("<h3>{0} ({1}) - 第 {2} 组</h3>", heat.Key.EventId, heat.Key.Stage, heat.Key.HeatNumber);
                html += "<table><tr><th>泳道</th><th>号码</th><th>姓名</th><th>单位</th><th>签到</th><th>备注</th></tr>";
                foreach (var s in heat.OrderBy(x => x.Lane))
                    html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td style='width:80px;'></td><td style='width:80px;'></td></tr>", s.Lane, s.BibNumber, s.Name, s.Organization);
                html += "</table>";
            }
            html += "</body></html>";
            WriteAndOpenHtml("CheckInSheet.html", html);
        }

        private void PrintHeatResults_Click(object sender, RoutedEventArgs e)
        {
            var results = CurrentHeatResults;
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:30px;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid black;padding:10px;text-align:center;} th{background:#f2f2f2;} h1,h2{text-align:center;}</style></head><body>" +
                "<h1>" + CompetitionName + "</h1><h2>" + CurrentEvent + " (" + CurrentStage + ") - 第 " + CurrentHeat + " 组成绩</h2>" +
                "<table><tr><th>名次</th><th>泳道</th><th>姓名</th><th>单位</th><th>成绩</th><th>状态</th></tr>";
            foreach (var r in results)
                html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td style='font-weight:bold;'>{4}</td><td>{5}</td></tr>",
                    r.IsValid ? r.Rank.ToString() : "-", r.Lane, r.SwimmerName, r.Organization, r.FinishTimeDisplay, r.Status);
            html += "</table><p style='text-align:right;margin-top:30px;'>裁判长签字：__________ 记录长签字：__________</p></body></html>";
            WriteAndOpenHtml("HeatResults.html", html);
        }

        private void PrintResultAnnouncement_Click(object sender, RoutedEventArgs e)
        {
            PrintHeatResults_Click(sender, e); // 与组次成绩相同格式
        }

        private void PrintEventRanking_Click(object sender, RoutedEventArgs e)
        {
            var rankings = EventRankings;
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:40px;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid black;padding:12px;text-align:center;} th{background:#f2f2f2;} h1,h2{text-align:center;}</style></head><body>" +
                "<h1>" + CompetitionName + "</h1><h2>" + CurrentEvent + " (" + CurrentStage + ") 总排名</h2>" +
                "<p style='text-align:center;'>日期：" + DateTime.Now.ToString("yyyy年MM月dd日") + "</p>" +
                "<table><tr><th>名次</th><th>姓名</th><th>单位</th><th>组次</th><th>泳道</th><th>成绩</th></tr>";
            foreach (var r in rankings)
                html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td style='font-weight:bold;'>{5}</td></tr>",
                    r.IsValid ? r.OverallRank.ToString() : "-", r.SwimmerName, r.Organization, r.HeatNumber, r.Lane, r.FinishTimeDisplay);
            html += "</table>" +
                "<div style='margin-top:40px;border-top:2px solid #000;padding-top:20px;'>" +
                "<p>裁判长：" + (OfficialReferee ?? "") + "　　记录长：" + (OfficialSecretary ?? "") + "</p></div></body></html>";
            WriteAndOpenHtml("EventRanking.html", html);
        }

        private void PrintAwards_Click(object sender, RoutedEventArgs e)
        {
            var top3 = EventRankings.Where(r => r.IsValid).Take(3).ToList();
            if (top3.Count == 0) { MessageBox.Show("暂无成绩数据"); return; }

            string html = "<html><head><meta charset='UTF-8'><style>" +
                "body{text-align:center;font-family:SimSun;padding:50px;} h1{font-size:36px;} h2{font-size:28px;color:#333;} " +
                ".medal{font-size:32px;font-weight:bold;margin:25px auto;padding:20px;border-radius:15px;width:400px;} " +
                ".gold{color:#D97706;border:2px solid #F59E0B;background:#FFFBEB;} .silver{color:#64748B;border:2px solid #94A3B8;background:#F8FAFC;} .bronze{color:#92400E;border:2px solid #B45309;background:#FEF3C7;}" +
                "</style></head><body>" +
                "<h1>" + CompetitionName + "</h1><h2>颁奖名单</h2>" +
                "<h3>" + CurrentEvent + " (" + CurrentStage + ")</h3><p>" + DateTime.Now.ToString("yyyy年MM月dd日") + "</p><hr/>";

            string[] titles = { "第一名 (金牌)", "第二名 (银牌)", "第三名 (铜牌)" };
            string[] classes = { "gold", "silver", "bronze" };
            for (int i = 0; i < top3.Count; i++)
                html += string.Format("<div class='medal {0}'><h3>{1}</h3><p>{2}</p><p>{3} - {4}</p></div>", classes[i], titles[i], top3[i].SwimmerName, top3[i].Organization, top3[i].FinishTimeDisplay);
            html += "</body></html>";
            WriteAndOpenHtml("Awards.html", html);
        }

        private void PrintTeamScores_Click(object sender, RoutedEventArgs e)
        {
            var teamScores = CalculateTeamScores();
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:40px;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid black;padding:12px;text-align:center;} th{background:#f2f2f2;} h1,h2{text-align:center;}</style></head><body>" +
                "<h1>" + CompetitionName + "</h1><h2>团体总分表</h2>" +
                "<table><tr><th>排名</th><th>参赛单位</th><th>总积分</th></tr>";
            foreach (dynamic t in teamScores)
                html += string.Format("<tr><td>{0}</td><td>{1}</td><td style='font-weight:bold;'>{2}</td></tr>", t.rank, t.name, t.totalPoints);
            html += "</table></body></html>";
            WriteAndOpenHtml("TeamScores.html", html);
        }

        private void PrintRecords_Click(object sender, RoutedEventArgs e)
        {
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:40px;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid black;padding:12px;text-align:center;} th{background:#f2f2f2;} h1,h2{text-align:center;}</style></head><body>" +
                "<h1>" + CompetitionName + "</h1><h2>破纪录一览表</h2>";
            if (_records.Count == 0) { html += "<p style='text-align:center;color:#666;'>暂无破纪录记录</p>"; }
            else
            {
                html += "<table><tr><th>项目</th><th>姓名</th><th>单位</th><th>成绩</th><th>纪录类型</th><th>原纪录</th><th>日期</th></tr>";
                foreach (var r in _records)
                    html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td></tr>",
                        r.EventId, r.SwimmerName, r.Organization, r.TimeDisplay, r.RecordType, TimeHelper.FormatTime(r.PreviousRecord), r.DateAchieved);
                html += "</table>";
            }
            html += "</body></html>";
            WriteAndOpenHtml("Records.html", html);
        }

        private void PrintFullResultBook_Click(object sender, RoutedEventArgs e)
        {
            string html = "<html><head><meta charset='UTF-8'><style>body{font-family:SimSun;padding:50px;line-height:1.6;} .page-break{page-break-after:always;} h1,h2,h3{text-align:center;} table{border-collapse:collapse;width:100%;margin-bottom:30px;} th,td{border:1px solid #000;padding:8px;text-align:center;} th{background:#eee;}</style></head><body>" +
                "<div style='height:80vh;display:flex;flex-direction:column;justify-content:center;'>" +
                "<h1 style='font-size:48px;'>" + CompetitionName + "</h1>" +
                "<h2 style='font-size:32px;'>成 绩 册</h2>" +
                "<p style='text-align:center;font-size:20px;margin-top:100px;'>比赛日期：" + StartDate.ToString("yyyy-MM-dd") + " 至 " + EndDate.ToString("yyyy-MM-dd") + "</p></div>" +
                "<div class='page-break'></div>";

            var events = _swimmers.Select(s => s.EventId).Distinct().OrderBy(x => GetEventSortWeight(x));
            foreach (var evt in events)
            {
                html += "<h1>项目：" + evt + "</h1>";
                string[] stages = { "预赛", "半决赛", "决赛" };
                foreach (var stg in stages)
                {
                    var stgSwimmers = _swimmers.Where(s => s.EventId == evt && s.Stage == stg).ToList();
                    if (stgSwimmers.Count == 0) continue;

                    var results = new List<KeyValuePair<Swimmer, RaceResult>>();
                    foreach (var s in stgSwimmers)
                    {
                        var r = s.Results.FirstOrDefault(x => x.EventId == evt && x.Stage == stg);
                        if (r != null) results.Add(new KeyValuePair<Swimmer, RaceResult>(s, r));
                    }
                    var ranked = results.Where(r => r.Value.IsValid).OrderBy(r => r.Value.FinishTime)
                        .Concat(results.Where(r => !r.Value.IsValid)).ToList();

                    if (ranked.Count == 0) continue;

                    html += "<h3>" + stg + " 最终排名</h3>";
                    html += "<table><tr><th width='60'>名次</th><th width='80'>号码</th><th>姓名</th><th>单位</th><th width='100'>成绩</th><th>状态</th></tr>";
                    for (int i = 0; i < ranked.Count; i++)
                    {
                        var s = ranked[i].Key; var r = ranked[i].Value;
                        html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                            r.IsValid ? (i + 1).ToString() : "-", s.BibNumber, s.Name, s.Organization, r.FinishTimeDisplay, r.Status);
                    }
                    html += "</table>";
                }
                html += "<div class='page-break'></div>";
            }
            html += "</body></html>";
            WriteAndOpenHtml("CompetitionResultBook.html", html);
            AddLog("全场总成绩册汇编成功");
        }

        // ======================== 工具方法 ========================
        private void AddLog(string msg)
        {
            this.Dispatcher.Invoke((Action)(() =>
            {
                _systemLogs.Insert(0, string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), msg));
            }));
        }

        private void WriteAndOpenHtml(string filename, string html)
        {
            File.WriteAllText(filename, html, System.Text.Encoding.UTF8);
            System.Diagnostics.Process.Start(filename);
        }

        private void NotifyAllChanges()
        {
            OnPropertyChanged("EventsList"); OnPropertyChanged("StagesList"); OnPropertyChanged("HeatsList");
            OnPropertyChanged("CurrentHeatSwimmers"); OnPropertyChanged("CurrentHeatResults"); OnPropertyChanged("EventRankings");
            OnPropertyChanged("EventSummaries"); OnPropertyChanged("TotalRegistrationCount"); OnPropertyChanged("EventsCount");
            OnPropertyChanged("RelayTeamsCount"); OnPropertyChanged("OrganizationsCount"); OnPropertyChanged("DistinctSwimmers");
            OnPropertyChanged("GroupedSwimmersView");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
