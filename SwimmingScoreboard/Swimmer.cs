using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SwimmingScoreboard
{
    // ═══════════════════════════════════════════════════════════════
    // 泳道设备状态枚举
    // ═══════════════════════════════════════════════════════════════
    public enum DeviceStatus
    {
        Open,         // 绿色 - 打开（正常接收数据）
        Closed,       // 灰色 - 关闭（不接收数据）
        Broken,       // 红色 - 损坏
        FalseStart,   // 黄色 - 抢跳（仅出发台）
        NotInstalled  // 虚线框 - 未安装
    }

    // ═══════════════════════════════════════════════════════════════
    // 比赛状态枚举
    // ═══════════════════════════════════════════════════════════════
    public enum RaceState
    {
        Waiting,    // 等待
        Ready,      // 就位
        Racing,     // 比赛中
        Finished    // 已完赛
    }

    // ═══════════════════════════════════════════════════════════════
    // 泳道关闭参数（全局可设置）
    // ═══════════════════════════════════════════════════════════════
    public class LaneCloseSettings : INotifyPropertyChanged
    {
        private double _laneCloseTime = 20.0;
        private double _startBlockCloseDelay = 3.0;
        private double _resultConfirmCloseDelay = 3.0;
        private double _falseStartThreshold = 0.10;
        private double _splitDisplayTime = 5.0;
        private string _startPosition = "left";
        private string _finishPosition = "left";  // ���点（触板端）位置，整场比赛固定不变
        private double _firstPlaceHoldTime = 3.0;
        private int _leftBlindWatchCount = 3;   // 左端每道盲表数量（1-3）
        private int _rightBlindWatchCount = 3;  // 右端每道盲表数量（1-3）
        private double _bigDisplayPageInterval = 5.0; // 大屏翻屏时间（秒）— 总排名等多页内容自动翻页周期
        private bool _reactionTimeEnabled = true;     // 是否启用出发反应时检测：关闭时，所有出发反应时相关处理跳过

        public double LaneCloseTime {
            get { return _laneCloseTime; }
            set { _laneCloseTime = value; OnPropertyChanged("LaneCloseTime"); }
        }
        public double StartBlockCloseDelay {
            get { return _startBlockCloseDelay; }
            set { _startBlockCloseDelay = value; OnPropertyChanged("StartBlockCloseDelay"); }
        }
        public double ResultConfirmCloseDelay {
            get { return _resultConfirmCloseDelay; }
            set { _resultConfirmCloseDelay = value; OnPropertyChanged("ResultConfirmCloseDelay"); }
        }
        public double FalseStartThreshold {
            get { return _falseStartThreshold; }
            set { _falseStartThreshold = value; OnPropertyChanged("FalseStartThreshold"); }
        }
        public double SplitDisplayTime {
            get { return _splitDisplayTime; }
            set { _splitDisplayTime = value; OnPropertyChanged("SplitDisplayTime"); }
        }
        public string StartPosition {
            get { return _startPosition; }
            set { _startPosition = value; OnPropertyChanged("StartPosition"); }
        }
        public string FinishPosition {
            get { return _finishPosition; }
            set { _finishPosition = value; OnPropertyChanged("FinishPosition"); }
        }
        public double FirstPlaceHoldTime {
            get { return _firstPlaceHoldTime; }
            set { _firstPlaceHoldTime = value; OnPropertyChanged("FirstPlaceHoldTime"); }
        }
        public int LeftBlindWatchCount {
            get { return _leftBlindWatchCount; }
            set {
                int v = value < 1 ? 1 : (value > 3 ? 3 : value);
                if (_leftBlindWatchCount != v) { _leftBlindWatchCount = v; OnPropertyChanged("LeftBlindWatchCount"); }
            }
        }
        public int RightBlindWatchCount {
            get { return _rightBlindWatchCount; }
            set {
                int v = value < 1 ? 1 : (value > 3 ? 3 : value);
                if (_rightBlindWatchCount != v) { _rightBlindWatchCount = v; OnPropertyChanged("RightBlindWatchCount"); }
            }
        }
        public double BigDisplayPageInterval {
            get { return _bigDisplayPageInterval; }
            set {
                double v = value < 1 ? 1 : (value > 60 ? 60 : value);
                if (_bigDisplayPageInterval != v) { _bigDisplayPageInterval = v; OnPropertyChanged("BigDisplayPageInterval"); }
            }
        }
        public bool ReactionTimeEnabled {
            get { return _reactionTimeEnabled; }
            set { if (_reactionTimeEnabled != value) { _reactionTimeEnabled = value; OnPropertyChanged("ReactionTimeEnabled"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 分段成绩
    // ═══════════════════════════════════════════════════════════════
    public class SplitTime : INotifyPropertyChanged
    {
        private int _lap;
        private int _distance;
        private double _time;
        private double _cumulativeTime;
        private double _touchpadTime;
        private double _pushButton1Time;
        private double _pushButton2Time;
        private double _pushButton3Time;
        private double _manualTouchTime;
        private string _timingSource = "";

        public int Lap {
            get { return _lap; }
            set { _lap = value; OnPropertyChanged("Lap"); }
        }
        public int Distance {
            get { return _distance; }
            set { _distance = value; OnPropertyChanged("Distance"); }
        }
        public double Time {
            get { return _time; }
            set { _time = value; OnPropertyChanged("Time"); OnPropertyChanged("TimeDisplay"); }
        }
        public double CumulativeTime {
            get { return _cumulativeTime; }
            set { _cumulativeTime = value; OnPropertyChanged("CumulativeTime"); OnPropertyChanged("CumulativeTimeDisplay"); }
        }
        public double TouchpadTime {
            get { return _touchpadTime; }
            set { _touchpadTime = value; OnPropertyChanged("TouchpadTime"); }
        }
        public double PushButton1Time {
            get { return _pushButton1Time; }
            set { _pushButton1Time = value; OnPropertyChanged("PushButton1Time"); }
        }
        public double PushButton2Time {
            get { return _pushButton2Time; }
            set { _pushButton2Time = value; OnPropertyChanged("PushButton2Time"); }
        }
        public double PushButton3Time {
            get { return _pushButton3Time; }
            set { _pushButton3Time = value; OnPropertyChanged("PushButton3Time"); }
        }
        public double ManualTouchTime {
            get { return _manualTouchTime; }
            set { _manualTouchTime = value; OnPropertyChanged("ManualTouchTime"); }
        }
        public string TimingSource {
            get { return _timingSource; }
            set { _timingSource = value; OnPropertyChanged("TimingSource"); }
        }

        public string TimeDisplay { get { return TimeFormatter.Format(_time); } }
        public string CumulativeTimeDisplay { get { return TimeFormatter.Format(_cumulativeTime); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 泳道成绩（对应跳水的 DiveRecord）
    // ═══════════════════════════════════════════════════════════════
    public class LaneResult : INotifyPropertyChanged
    {
        private string _eventName;
        private string _stage;
        private int _heat;
        private int _lane;
        private double _touchpadTime;
        private double _startingBlockTime;
        private double _pushButton1Time;
        private double _pushButton2Time;
        private double _pushButton3Time;
        private double _manualTouchTimeLeft;
        private double _manualTouchTimeRight;
        private double _finalTime;
        private double _timeInSeconds;
        private string _timingSource = "";
        private int _rank;
        private string _status = "";
        private string _recordNote = ""; // 破/平纪录标识，如 "WR" / "=WR" / "WR/AR/NR"
        private ObservableCollection<SplitTime> _splits;

        public LaneResult() {
            _splits = new ObservableCollection<SplitTime>();
        }

        public string EventName {
            get { return _eventName; }
            set { _eventName = value; OnPropertyChanged("EventName"); }
        }
        public string Stage {
            get { return _stage; }
            set { _stage = value; OnPropertyChanged("Stage"); }
        }
        public int Heat {
            get { return _heat; }
            set { _heat = value; OnPropertyChanged("Heat"); }
        }
        public int Lane {
            get { return _lane; }
            set { _lane = value; OnPropertyChanged("Lane"); }
        }
        public double TouchpadTime {
            get { return _touchpadTime; }
            set { _touchpadTime = value; OnPropertyChanged("TouchpadTime"); }
        }
        public double StartingBlockTime {
            get { return _startingBlockTime; }
            set { _startingBlockTime = value; OnPropertyChanged("StartingBlockTime"); }
        }
        public double PushButton1Time {
            get { return _pushButton1Time; }
            set { _pushButton1Time = value; OnPropertyChanged("PushButton1Time"); }
        }
        public double PushButton2Time {
            get { return _pushButton2Time; }
            set { _pushButton2Time = value; OnPropertyChanged("PushButton2Time"); }
        }
        public double PushButton3Time {
            get { return _pushButton3Time; }
            set { _pushButton3Time = value; OnPropertyChanged("PushButton3Time"); }
        }
        public double ManualTouchTimeLeft {
            get { return _manualTouchTimeLeft; }
            set { _manualTouchTimeLeft = value; OnPropertyChanged("ManualTouchTimeLeft"); }
        }
        public double ManualTouchTimeRight {
            get { return _manualTouchTimeRight; }
            set { _manualTouchTimeRight = value; OnPropertyChanged("ManualTouchTimeRight"); }
        }
        public double FinalTime {
            get { return _finalTime; }
            set { _finalTime = value; OnPropertyChanged("FinalTime"); OnPropertyChanged("FinalTimeDisplay"); }
        }
        public double TimeInSeconds {
            get { return _timeInSeconds; }
            set { _timeInSeconds = value; OnPropertyChanged("TimeInSeconds"); }
        }
        public string TimingSource {
            get { return _timingSource; }
            set { _timingSource = value; OnPropertyChanged("TimingSource"); }
        }
        public int Rank {
            get { return _rank; }
            set { _rank = value; OnPropertyChanged("Rank"); }
        }
        public string Status {
            get { return _status; }
            set { _status = value; OnPropertyChanged("Status"); }
        }
        // 破/平纪录标识：例如 "WR" 表示破世界纪录，"=AR" 表示平亚洲纪录，多条用 "/" 拼接
        public string RecordNote {
            get { return _recordNote; }
            set { _recordNote = value ?? ""; OnPropertyChanged("RecordNote"); }
        }
        // 接力赛各棒反应时（单位：秒）；按棒次顺序追加：
        //   第1棒 = 出发反应时（与 StartingBlockTime 相同）
        //   第2-4棒 = 交接反应时（出发台动作时刻 - 上一棒触板时刻）
        // 个人赛此列表保持空（StartingBlockTime 即可表达）
        private List<double> _legReactionTimes = new List<double>();
        public List<double> LegReactionTimes {
            get { return _legReactionTimes; }
            set { _legReactionTimes = value ?? new List<double>(); OnPropertyChanged("LegReactionTimes"); }
        }
        public ObservableCollection<SplitTime> Splits {
            get { return _splits; }
        }

        public string FinalTimeDisplay {
            get {
                if (!string.IsNullOrEmpty(_status)) return _status;
                if (_finalTime <= 0) return "";
                return TimeFormatter.Format(_finalTime);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 赛次分组记录（保存每个赛次的分组/泳道分配，不会被后续赛次覆盖）
    // ═══════════════════════════════════════════════════════════════
    public class StageAssignment
    {
        public string Stage { get; set; }
        public int Heat { get; set; }
        public int Lane { get; set; }
        public double EntryTimeSeconds { get; set; }
        public string EntryTime { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    // 运动员（对应跳水的 Diver）
    // ═══════════════════════════════════════════════════════════════
    public class Swimmer : INotifyPropertyChanged
    {
        private string _name;
        private string _bibNumber;
        private string _birthDate;
        private int _age;
        private string _gender;
        private string _country;
        private string _countryShort;
        private string _idNumber;
        private string _phone;
        private string _notes;
        private string _csaNumber;
        private string _finaNumber;
        private string _healthCertDate;
        private string _eventName;
        private string _currentStage;
        private int _heat;
        private int _lane;
        private string _entryTime;
        private double _entryTimeSeconds;
        private bool _isQualified;
        private string _status;
        private int _currentRank;
        private string _ageCategory;
        private ObservableCollection<LaneResult> _results;
        private Dictionary<string, StageAssignment> _stageAssignments;

        public Swimmer() {
            _gender = "男";
            _currentStage = "预赛";
            _isQualified = true;
            _status = "";
            _results = new ObservableCollection<LaneResult>();
            _stageAssignments = new Dictionary<string, StageAssignment>();
        }

        public string Name {
            get { return _name; }
            set { _name = value; OnPropertyChanged("Name"); }
        }
        public string BibNumber {
            get { return _bibNumber; }
            set { _bibNumber = value; OnPropertyChanged("BibNumber"); }
        }
        public string BirthDate {
            get { return _birthDate; }
            set {
                _birthDate = value;
                OnPropertyChanged("BirthDate");
                UpdateAgeCategory();
            }
        }
        public int Age {
            get { return _age; }
            set {
                _age = value;
                OnPropertyChanged("Age");
                UpdateAgeCategory();
            }
        }
        public string Gender {
            get { return _gender; }
            set { _gender = value; OnPropertyChanged("Gender"); }
        }
        public string Country {
            get { return _country; }
            set { _country = value; OnPropertyChanged("Country"); }
        }
        // 参赛单位简称（大屏/成绩表简显用）
        public string CountryShort {
            get { return _countryShort; }
            set { _countryShort = value; OnPropertyChanged("CountryShort"); }
        }
        public string IDNumber {
            get { return _idNumber; }
            set { _idNumber = value; OnPropertyChanged("IDNumber"); }
        }
        public string Phone {
            get { return _phone; }
            set { _phone = value; OnPropertyChanged("Phone"); }
        }
        public string Notes {
            get { return _notes; }
            set { _notes = value; OnPropertyChanged("Notes"); }
        }
        public string CSANumber {
            get { return _csaNumber; }
            set { _csaNumber = value; OnPropertyChanged("CSANumber"); }
        }
        public string FINANumber {
            get { return _finaNumber; }
            set { _finaNumber = value; OnPropertyChanged("FINANumber"); }
        }
        public string HealthCertDate {
            get { return _healthCertDate; }
            set { _healthCertDate = value; OnPropertyChanged("HealthCertDate"); }
        }
        public string EventName {
            get { return _eventName; }
            set { _eventName = value; OnPropertyChanged("EventName"); }
        }
        public string CurrentStage {
            get { return _currentStage; }
            set { _currentStage = value; OnPropertyChanged("CurrentStage"); }
        }
        public int Heat {
            get { return _heat; }
            set { _heat = value; OnPropertyChanged("Heat"); }
        }
        public int Lane {
            get { return _lane; }
            set { _lane = value; OnPropertyChanged("Lane"); }
        }
        public string EntryTime {
            get { return _entryTime; }
            set { _entryTime = value; OnPropertyChanged("EntryTime"); }
        }
        public double EntryTimeSeconds {
            get { return _entryTimeSeconds; }
            set { _entryTimeSeconds = value; OnPropertyChanged("EntryTimeSeconds"); }
        }
        public bool IsQualified {
            get { return _isQualified; }
            set { _isQualified = value; OnPropertyChanged("IsQualified"); }
        }
        public string Status {
            get { return _status; }
            set { _status = value; OnPropertyChanged("Status"); }
        }
        public int CurrentRank {
            get { return _currentRank; }
            set { _currentRank = value; OnPropertyChanged("CurrentRank"); }
        }
        public string AgeCategory {
            get { return _ageCategory; }
            set { _ageCategory = value; OnPropertyChanged("AgeCategory"); }
        }
        public ObservableCollection<LaneResult> Results {
            get { return _results; }
        }

        public LaneResult GetResultForStage(string stage) {
            return _results.FirstOrDefault(r => r.Stage == stage);
        }

        // 每个赛次的分组记录（历史数据，不会被覆盖）
        public Dictionary<string, StageAssignment> StageAssignments {
            get { return _stageAssignments; }
            set { _stageAssignments = value ?? new Dictionary<string, StageAssignment>(); }
        }

        public StageAssignment GetAssignmentForStage(string stage) {
            StageAssignment a;
            return _stageAssignments.TryGetValue(stage, out a) ? a : null;
        }

        public void SetStageAssignment(string stage, int heat, int lane, double entryTimeSeconds, string entryTime) {
            _stageAssignments[stage] = new StageAssignment {
                Stage = stage,
                Heat = heat,
                Lane = lane,
                EntryTimeSeconds = entryTimeSeconds,
                EntryTime = entryTime
            };
        }

        private void UpdateAgeCategory() {
            // 组别现为人工分类（甲组/乙组/少年/成人 等），不再从年龄自动推断。
            // 仅当 AgeCategory 为空时调用 Registry.CategoryFor 作为兜底（兼容旧存档）。
            if (string.IsNullOrEmpty(_ageCategory)) {
                _ageCategory = AgeGroupRegistry.CategoryFor(_age);
                OnPropertyChanged("AgeCategory");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 接力队伍
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // 接力棒次
    // ═══════════════════════════════════════════════════════════════
    public class RelayLeg : INotifyPropertyChanged
    {
        private int _legOrder;
        private string _swimmerName;
        private string _swimmerBibNumber;
        private string _swimmerIDNumber;
        private string _swimmerBirthDate;

        public int LegOrder {
            get { return _legOrder; }
            set { _legOrder = value; OnPropertyChanged("LegOrder"); }
        }
        public string SwimmerName {
            get { return _swimmerName; }
            set { _swimmerName = value; OnPropertyChanged("SwimmerName"); }
        }
        public string SwimmerBibNumber {
            get { return _swimmerBibNumber; }
            set { _swimmerBibNumber = value; OnPropertyChanged("SwimmerBibNumber"); }
        }
        public string SwimmerIDNumber {
            get { return _swimmerIDNumber; }
            set { _swimmerIDNumber = value; OnPropertyChanged("SwimmerIDNumber"); }
        }
        // 队员出生日期（yyyy-MM-dd）；用于检录核对、组别判断等
        public string SwimmerBirthDate {
            get { return _swimmerBirthDate; }
            set { _swimmerBirthDate = value; OnPropertyChanged("SwimmerBirthDate"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayTeam : INotifyPropertyChanged
    {
        private string _teamName;
        private string _eventName;
        private string _gender;
        private ObservableCollection<Swimmer> _members;
        private ObservableCollection<RelayLeg> _legs;
        private string _entryTime;
        private double _entryTimeSeconds;
        private int _heat;
        private int _lane;
        private double _finalTime;
        private int _rank;
        private string _stage;
        private string _status;
        private ObservableCollection<SplitTime> _legSplits;

        public RelayTeam() {
            _members = new ObservableCollection<Swimmer>();
            _legs = new ObservableCollection<RelayLeg>();
            _legSplits = new ObservableCollection<SplitTime>();
            _stage = "预赛";
            _status = "";
        }

        public string TeamName {
            get { return _teamName; }
            set { _teamName = value; OnPropertyChanged("TeamName"); }
        }
        public string EventName {
            get { return _eventName; }
            set { _eventName = value; OnPropertyChanged("EventName"); }
        }
        public string Gender {
            get { return _gender; }
            set { _gender = value; OnPropertyChanged("Gender"); }
        }
        public ObservableCollection<Swimmer> Members {
            get { return _members; }
        }
        public ObservableCollection<RelayLeg> Legs {
            get { return _legs; }
        }
        public string LegOrderDisplay {
            get {
                if (_legs == null || _legs.Count == 0) return "";
                var parts = new List<string>();
                foreach (var leg in _legs) parts.Add(string.Format("{0}棒:{1}", leg.LegOrder, leg.SwimmerName));
                return string.Join(", ", parts.ToArray());
            }
        }
        public string EntryTime {
            get { return _entryTime; }
            set { _entryTime = value; OnPropertyChanged("EntryTime"); }
        }
        public double EntryTimeSeconds {
            get { return _entryTimeSeconds; }
            set { _entryTimeSeconds = value; OnPropertyChanged("EntryTimeSeconds"); }
        }
        public int Heat {
            get { return _heat; }
            set { _heat = value; OnPropertyChanged("Heat"); }
        }
        public int Lane {
            get { return _lane; }
            set { _lane = value; OnPropertyChanged("Lane"); }
        }
        public double FinalTime {
            get { return _finalTime; }
            set { _finalTime = value; OnPropertyChanged("FinalTime"); OnPropertyChanged("FinalTimeDisplay"); }
        }
        public int Rank {
            get { return _rank; }
            set { _rank = value; OnPropertyChanged("Rank"); }
        }
        public string Stage {
            get { return _stage; }
            set { _stage = value; OnPropertyChanged("Stage"); }
        }
        public string Status {
            get { return _status; }
            set { _status = value; OnPropertyChanged("Status"); }
        }
        public ObservableCollection<SplitTime> LegSplits {
            get { return _legSplits; }
        }

        public string FinalTimeDisplay {
            get {
                if (!string.IsNullOrEmpty(_status)) return _status;
                if (_finalTime <= 0) return "";
                return TimeFormatter.Format(_finalTime);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 游泳纪录
    // ═══════════════════════════════════════════════════════════════
    public class SwimmingRecord : INotifyPropertyChanged
    {
        private string _eventName;
        private string _gender;
        private string _ageGroup;
        private string _recordType;
        private string _holderName;
        private string _holderCountry;
        private double _time;
        private double _timeInSeconds;
        private string _date;
        private string _location;

        public string EventName {
            get { return _eventName; }
            set { _eventName = value; OnPropertyChanged("EventName"); }
        }
        public string Gender {
            get { return _gender; }
            set { _gender = value; OnPropertyChanged("Gender"); }
        }
        // 组别：空串表示不限（兼容旧存档的纪录数据）
        public string AgeGroup {
            get { return _ageGroup; }
            set { _ageGroup = value; OnPropertyChanged("AgeGroup"); }
        }
        public string RecordType {
            get { return _recordType; }
            set { _recordType = value; OnPropertyChanged("RecordType"); }
        }
        public string HolderName {
            get { return _holderName; }
            set { _holderName = value; OnPropertyChanged("HolderName"); }
        }
        public string HolderCountry {
            get { return _holderCountry; }
            set { _holderCountry = value; OnPropertyChanged("HolderCountry"); }
        }
        public double Time {
            get { return _time; }
            set { _time = value; OnPropertyChanged("Time"); OnPropertyChanged("TimeDisplay"); }
        }
        public double TimeInSeconds {
            get { return _timeInSeconds; }
            set { _timeInSeconds = value; OnPropertyChanged("TimeInSeconds"); }
        }
        public string Date {
            get { return _date; }
            set { _date = value; OnPropertyChanged("Date"); OnPropertyChanged("DateAsDateTime"); }
        }
        // DatePicker绑定用
        [Newtonsoft.Json.JsonIgnore]
        public DateTime? DateAsDateTime {
            get {
                DateTime dt;
                if (DateTime.TryParse(_date, out dt)) return dt;
                return null;
            }
            set {
                Date = value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "";
            }
        }
        public string Location {
            get { return _location; }
            set { _location = value; OnPropertyChanged("Location"); }
        }

        public string TimeDisplay { get { return TimeFormatter.Format(_time); } }

        /// <summary>
        /// 可编辑的成绩字段，支持输入 SS.ss / M:SS.ss / H:MM:SS.ss 格式
        /// 设置时自动解析为秒数并同步 Time 和 TimeInSeconds
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string TimeText {
            get { return TimeFormatter.Format(_time); }
            set {
                double parsed = TimeFormatter.Parse(value);
                if (parsed > 0) {
                    Time = parsed;
                    TimeInSeconds = parsed;
                }
                OnPropertyChanged("TimeText");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 团体成绩
    // ═══════════════════════════════════════════════════════════════
    public class TeamScore : INotifyPropertyChanged
    {
        private string _teamName;
        private double _totalPoints;
        private double _individualPoints;
        private double _relayPoints;
        private double _recordBonusPoints;
        private int _goldCount;
        private int _silverCount;
        private int _bronzeCount;
        private int _rank;

        public string TeamName {
            get { return _teamName; }
            set { _teamName = value; OnPropertyChanged("TeamName"); }
        }
        public double TotalPoints {
            get { return _totalPoints; }
            set { _totalPoints = value; OnPropertyChanged("TotalPoints"); }
        }
        public double IndividualPoints {
            get { return _individualPoints; }
            set { _individualPoints = value; OnPropertyChanged("IndividualPoints"); }
        }
        public double RelayPoints {
            get { return _relayPoints; }
            set { _relayPoints = value; OnPropertyChanged("RelayPoints"); }
        }
        public double RecordBonusPoints {
            get { return _recordBonusPoints; }
            set { _recordBonusPoints = value; OnPropertyChanged("RecordBonusPoints"); }
        }
        public int GoldCount {
            get { return _goldCount; }
            set { _goldCount = value; OnPropertyChanged("GoldCount"); }
        }
        public int SilverCount {
            get { return _silverCount; }
            set { _silverCount = value; OnPropertyChanged("SilverCount"); }
        }
        public int BronzeCount {
            get { return _bronzeCount; }
            set { _bronzeCount = value; OnPropertyChanged("BronzeCount"); }
        }
        public int Rank {
            get { return _rank; }
            set { _rank = value; OnPropertyChanged("Rank"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 泳道设备状态
    // ═══════════════════════════════════════════════════════════════
    public class LaneDeviceState : INotifyPropertyChanged
    {
        private int _lane;
        private DeviceStatus _leftTouchpadStatus = DeviceStatus.Closed;
        private DeviceStatus _leftBlindWatch1Status = DeviceStatus.Closed;
        private DeviceStatus _leftBlindWatch2Status = DeviceStatus.Closed;
        private DeviceStatus _leftBlindWatch3Status = DeviceStatus.Closed;
        private DeviceStatus _leftStartBlockStatus = DeviceStatus.Open;
        private DeviceStatus _rightTouchpadStatus = DeviceStatus.Closed;
        private DeviceStatus _rightBlindWatch1Status = DeviceStatus.Closed;
        private DeviceStatus _rightBlindWatch2Status = DeviceStatus.Closed;
        private DeviceStatus _rightBlindWatch3Status = DeviceStatus.Closed;
        private DeviceStatus _rightStartBlockStatus = DeviceStatus.Closed;
        private double _laneCloseCountdown;
        private double _laneCloseTime = 20.0;
        private string _direction = "→";
        private int _currentLap;
        private bool _isFinished;
        private double _reactionTime;
        private bool _isFalseStart;
        private bool _isSuspectFalseStart; // 反应时低于阈值（疑似抢跳）：仅作为视觉提示（反应时标红），是否判罚由裁判手动决定
        private int _leftLapManualAdjust;   // 左端"圈数"显示的人工调整值（spinner ▲▼）
        private int _rightLapManualAdjust;  // 右端"圈数"显示的人工调整值（spinner ▲▼）
        private string _startSide = "left";  // 出发台所在端（用于抢跳显示）
        private double _leftManualTouchTime;
        private double _rightManualTouchTime;
        // 盲表暂存（触板未触碰时，盲表数据先存这里，触板创建split时带入）
        public double PendingBlind1Time { get; set; }
        public double PendingBlind2Time { get; set; }
        public double PendingBlind3Time { get; set; }
        // 手动按钮状态：Enabled=用, Status=Open/Closed
        public bool LeftManualEnabled { get; set; }
        public bool RightManualEnabled { get; set; }
        public DeviceStatus LeftManualStatus { get; set; }
        public DeviceStatus RightManualStatus { get; set; }
        private bool _leftTouchpadBroken;
        private bool _leftBlindWatch1Broken;
        private bool _leftBlindWatch2Broken;
        private bool _leftBlindWatch3Broken;
        private bool _leftStartBlockBroken;
        private bool _rightTouchpadBroken;
        private bool _rightBlindWatch1Broken;
        private bool _rightBlindWatch2Broken;
        private bool _rightBlindWatch3Broken;
        private bool _rightStartBlockBroken;
        private bool _leftBlindWatch1NotInstalled;
        private bool _leftBlindWatch2NotInstalled;
        private bool _leftBlindWatch3NotInstalled;
        private bool _rightBlindWatch1NotInstalled;
        private bool _rightBlindWatch2NotInstalled;
        private bool _rightBlindWatch3NotInstalled;

        public int Lane {
            get { return _lane; }
            set { _lane = value; OnPropertyChanged("Lane"); }
        }
        public DeviceStatus LeftTouchpadStatus {
            get { return _leftTouchpadBroken ? DeviceStatus.Broken : _leftTouchpadStatus; }
            set { _leftTouchpadStatus = value; OnPropertyChanged("LeftTouchpadStatus"); }
        }
        public DeviceStatus LeftBlindWatch1Status {
            get { if (_leftBlindWatch1NotInstalled) return DeviceStatus.NotInstalled; return _leftBlindWatch1Broken ? DeviceStatus.Broken : _leftBlindWatch1Status; }
            set { _leftBlindWatch1Status = value; OnPropertyChanged("LeftBlindWatch1Status"); }
        }
        public DeviceStatus LeftBlindWatch2Status {
            get { if (_leftBlindWatch2NotInstalled) return DeviceStatus.NotInstalled; return _leftBlindWatch2Broken ? DeviceStatus.Broken : _leftBlindWatch2Status; }
            set { _leftBlindWatch2Status = value; OnPropertyChanged("LeftBlindWatch2Status"); }
        }
        public DeviceStatus LeftBlindWatch3Status {
            get { if (_leftBlindWatch3NotInstalled) return DeviceStatus.NotInstalled; return _leftBlindWatch3Broken ? DeviceStatus.Broken : _leftBlindWatch3Status; }
            set { _leftBlindWatch3Status = value; OnPropertyChanged("LeftBlindWatch3Status"); }
        }
        public string StartSide {
            get { return _startSide; }
            set { _startSide = value; }
        }
        public DeviceStatus LeftStartBlockStatus {
            get {
                if (_leftStartBlockBroken) return DeviceStatus.Broken;
                if (_isFalseStart && _startSide == "left") return DeviceStatus.FalseStart;
                return _leftStartBlockStatus;
            }
            set { _leftStartBlockStatus = value; OnPropertyChanged("LeftStartBlockStatus"); }
        }
        public DeviceStatus RightTouchpadStatus {
            get { return _rightTouchpadBroken ? DeviceStatus.Broken : _rightTouchpadStatus; }
            set { _rightTouchpadStatus = value; OnPropertyChanged("RightTouchpadStatus"); }
        }
        public DeviceStatus RightBlindWatch1Status {
            get { if (_rightBlindWatch1NotInstalled) return DeviceStatus.NotInstalled; return _rightBlindWatch1Broken ? DeviceStatus.Broken : _rightBlindWatch1Status; }
            set { _rightBlindWatch1Status = value; OnPropertyChanged("RightBlindWatch1Status"); }
        }
        public DeviceStatus RightBlindWatch2Status {
            get { if (_rightBlindWatch2NotInstalled) return DeviceStatus.NotInstalled; return _rightBlindWatch2Broken ? DeviceStatus.Broken : _rightBlindWatch2Status; }
            set { _rightBlindWatch2Status = value; OnPropertyChanged("RightBlindWatch2Status"); }
        }
        public DeviceStatus RightBlindWatch3Status {
            get { if (_rightBlindWatch3NotInstalled) return DeviceStatus.NotInstalled; return _rightBlindWatch3Broken ? DeviceStatus.Broken : _rightBlindWatch3Status; }
            set { _rightBlindWatch3Status = value; OnPropertyChanged("RightBlindWatch3Status"); }
        }
        public DeviceStatus RightStartBlockStatus {
            get {
                if (_rightStartBlockBroken) return DeviceStatus.Broken;
                if (_isFalseStart && _startSide == "right") return DeviceStatus.FalseStart;
                return _rightStartBlockStatus;
            }
            set { _rightStartBlockStatus = value; OnPropertyChanged("RightStartBlockStatus"); }
        }
        public double LaneCloseCountdown {
            get { return _laneCloseCountdown; }
            set { _laneCloseCountdown = value; OnPropertyChanged("LaneCloseCountdown"); }
        }
        public double LaneCloseTime {
            get { return _laneCloseTime; }
            set { _laneCloseTime = value; OnPropertyChanged("LaneCloseTime"); }
        }
        public string Direction {
            get { return _direction; }
            set { _direction = value; OnPropertyChanged("Direction"); }
        }
        public int CurrentLap {
            get { return _currentLap; }
            set { _currentLap = value; OnPropertyChanged("CurrentLap"); }
        }
        public bool IsFinished {
            get { return _isFinished; }
            set { _isFinished = value; OnPropertyChanged("IsFinished"); }
        }
        public double ReactionTime {
            get { return _reactionTime; }
            set { _reactionTime = value; ReactionSeq++; OnPropertyChanged("ReactionTime"); OnPropertyChanged("ReactionTimeDisplay"); }
        }
        // 每次写入 ReactionTime 自动 +1；客户端比较 sw.reactionSeq 即可判断"出现了新一棒反应时"，
        // 用来在大屏触发"反应时显示窗口"，跟随 splitDisplayTime 倒计时；接力赛 4 棒会有 4 个不同的 seq
        public int ReactionSeq { get; set; }
        public bool IsFalseStart {
            get { return _isFalseStart; }
            set { _isFalseStart = value; OnPropertyChanged("IsFalseStart"); OnPropertyChanged("LeftStartBlockStatus"); OnPropertyChanged("RightStartBlockStatus"); }
        }
        public bool IsSuspectFalseStart {
            get { return _isSuspectFalseStart; }
            set { if (_isSuspectFalseStart != value) { _isSuspectFalseStart = value; OnPropertyChanged("IsSuspectFalseStart"); } }
        }
        public int LeftLapManualAdjust {
            get { return _leftLapManualAdjust; }
            set { if (_leftLapManualAdjust != value) { _leftLapManualAdjust = value; OnPropertyChanged("LeftLapManualAdjust"); } }
        }
        public int RightLapManualAdjust {
            get { return _rightLapManualAdjust; }
            set { if (_rightLapManualAdjust != value) { _rightLapManualAdjust = value; OnPropertyChanged("RightLapManualAdjust"); } }
        }
        public double LeftManualTouchTime {
            get { return _leftManualTouchTime; }
            set { _leftManualTouchTime = value; OnPropertyChanged("LeftManualTouchTime"); }
        }
        public double RightManualTouchTime {
            get { return _rightManualTouchTime; }
            set { _rightManualTouchTime = value; OnPropertyChanged("RightManualTouchTime"); }
        }

        // 设备损坏标记（由设备状态管理窗口设置）
        public bool LeftTouchpadBroken {
            get { return _leftTouchpadBroken; }
            set { _leftTouchpadBroken = value; OnPropertyChanged("LeftTouchpadBroken"); OnPropertyChanged("LeftTouchpadStatus"); }
        }
        public bool LeftBlindWatch1Broken {
            get { return _leftBlindWatch1Broken; }
            set { _leftBlindWatch1Broken = value; OnPropertyChanged("LeftBlindWatch1Broken"); OnPropertyChanged("LeftBlindWatch1Status"); }
        }
        public bool LeftBlindWatch2Broken {
            get { return _leftBlindWatch2Broken; }
            set { _leftBlindWatch2Broken = value; OnPropertyChanged("LeftBlindWatch2Broken"); OnPropertyChanged("LeftBlindWatch2Status"); }
        }
        public bool LeftBlindWatch3Broken {
            get { return _leftBlindWatch3Broken; }
            set { _leftBlindWatch3Broken = value; OnPropertyChanged("LeftBlindWatch3Broken"); OnPropertyChanged("LeftBlindWatch3Status"); }
        }
        public bool LeftStartBlockBroken {
            get { return _leftStartBlockBroken; }
            set { _leftStartBlockBroken = value; OnPropertyChanged("LeftStartBlockBroken"); OnPropertyChanged("LeftStartBlockStatus"); }
        }
        public bool RightTouchpadBroken {
            get { return _rightTouchpadBroken; }
            set { _rightTouchpadBroken = value; OnPropertyChanged("RightTouchpadBroken"); OnPropertyChanged("RightTouchpadStatus"); }
        }
        public bool RightBlindWatch1Broken {
            get { return _rightBlindWatch1Broken; }
            set { _rightBlindWatch1Broken = value; OnPropertyChanged("RightBlindWatch1Broken"); OnPropertyChanged("RightBlindWatch1Status"); }
        }
        public bool RightBlindWatch2Broken {
            get { return _rightBlindWatch2Broken; }
            set { _rightBlindWatch2Broken = value; OnPropertyChanged("RightBlindWatch2Broken"); OnPropertyChanged("RightBlindWatch2Status"); }
        }
        public bool RightBlindWatch3Broken {
            get { return _rightBlindWatch3Broken; }
            set { _rightBlindWatch3Broken = value; OnPropertyChanged("RightBlindWatch3Broken"); OnPropertyChanged("RightBlindWatch3Status"); }
        }
        public bool RightStartBlockBroken {
            get { return _rightStartBlockBroken; }
            set { _rightStartBlockBroken = value; OnPropertyChanged("RightStartBlockBroken"); OnPropertyChanged("RightStartBlockStatus"); }
        }

        // 未安装标记（盲表）
        public bool LeftBlindWatch1NotInstalled {
            get { return _leftBlindWatch1NotInstalled; }
            set { _leftBlindWatch1NotInstalled = value; OnPropertyChanged("LeftBlindWatch1NotInstalled"); OnPropertyChanged("LeftBlindWatch1Status"); }
        }
        public bool LeftBlindWatch2NotInstalled {
            get { return _leftBlindWatch2NotInstalled; }
            set { _leftBlindWatch2NotInstalled = value; OnPropertyChanged("LeftBlindWatch2NotInstalled"); OnPropertyChanged("LeftBlindWatch2Status"); }
        }
        public bool LeftBlindWatch3NotInstalled {
            get { return _leftBlindWatch3NotInstalled; }
            set { _leftBlindWatch3NotInstalled = value; OnPropertyChanged("LeftBlindWatch3NotInstalled"); OnPropertyChanged("LeftBlindWatch3Status"); }
        }
        public bool RightBlindWatch1NotInstalled {
            get { return _rightBlindWatch1NotInstalled; }
            set { _rightBlindWatch1NotInstalled = value; OnPropertyChanged("RightBlindWatch1NotInstalled"); OnPropertyChanged("RightBlindWatch1Status"); }
        }
        public bool RightBlindWatch2NotInstalled {
            get { return _rightBlindWatch2NotInstalled; }
            set { _rightBlindWatch2NotInstalled = value; OnPropertyChanged("RightBlindWatch2NotInstalled"); OnPropertyChanged("RightBlindWatch2Status"); }
        }
        public bool RightBlindWatch3NotInstalled {
            get { return _rightBlindWatch3NotInstalled; }
            set { _rightBlindWatch3NotInstalled = value; OnPropertyChanged("RightBlindWatch3NotInstalled"); OnPropertyChanged("RightBlindWatch3Status"); }
        }

        public string ReactionTimeDisplay {
            get {
                if (_reactionTime == 0) return "";
                return _reactionTime.ToString("F2");
            }
        }

        public void ResetForNewRace() { ResetForNewRace("left"); }

        public void ResetForNewRace(string startPosition) {
            bool startLeft = startPosition != "right";
            _leftTouchpadStatus = DeviceStatus.Closed;
            _leftBlindWatch1Status = DeviceStatus.Closed;
            _leftBlindWatch2Status = DeviceStatus.Closed;
            _leftBlindWatch3Status = DeviceStatus.Closed;
            _leftStartBlockStatus = startLeft ? DeviceStatus.Open : DeviceStatus.Closed;
            _rightTouchpadStatus = DeviceStatus.Closed;
            _rightBlindWatch1Status = DeviceStatus.Closed;
            _rightBlindWatch2Status = DeviceStatus.Closed;
            _rightBlindWatch3Status = DeviceStatus.Closed;
            _rightStartBlockStatus = startLeft ? DeviceStatus.Closed : DeviceStatus.Open;
            _laneCloseCountdown = 0;
            _direction = startPosition == "right" ? "←" : "→";
            _currentLap = 0;
            _isFinished = false;
            _reactionTime = 0;
            _isFalseStart = false;
            _isSuspectFalseStart = false;
            _leftLapManualAdjust = 0;
            _rightLapManualAdjust = 0;
            _startSide = startPosition;
            _leftManualTouchTime = 0;
            PendingBlind1Time = 0; PendingBlind2Time = 0; PendingBlind3Time = 0;
            // 手动按钮：保持Enabled状态不变（由设置控制），只重置Open/Closed
            LeftManualStatus = LeftManualEnabled ? DeviceStatus.Closed : DeviceStatus.Closed;
            RightManualStatus = RightManualEnabled ? DeviceStatus.Closed : DeviceStatus.Closed;
            _rightManualTouchTime = 0;
            NotifyAll();
        }

        private void NotifyAll() {
            string[] props = { "LeftTouchpadStatus",
                "LeftBlindWatch1Status", "LeftBlindWatch2Status", "LeftBlindWatch3Status",
                "LeftStartBlockStatus",
                "RightTouchpadStatus",
                "RightBlindWatch1Status", "RightBlindWatch2Status", "RightBlindWatch3Status",
                "RightStartBlockStatus",
                "LaneCloseCountdown", "Direction", "CurrentLap", "IsFinished",
                "ReactionTime", "ReactionTimeDisplay", "IsFalseStart", "IsSuspectFalseStart",
                "LeftManualTouchTime", "RightManualTouchTime" };
            foreach (string p in props) OnPropertyChanged(p);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 赛程项目
    // ═══════════════════════════════════════════════════════════════
    public class ScheduleItem : INotifyPropertyChanged
    {
        private int _sessionNumber;
        private string _sessionName;
        private string _date;
        private string _time;
        private string _ageGroup;
        private string _eventName;
        private string _gender;
        private string _stage;
        private int _heatCount;
        private bool _isRelay;

        public int SessionNumber {
            get { return _sessionNumber; }
            set { _sessionNumber = value; OnPropertyChanged("SessionNumber"); }
        }
        public string SessionName {
            get { return _sessionName; }
            set { _sessionName = value; OnPropertyChanged("SessionName"); }
        }
        public string Date {
            get { return _date; }
            set { _date = value; OnPropertyChanged("Date"); }
        }
        public string Time {
            get { return _time; }
            set { _time = value; OnPropertyChanged("Time"); }
        }
        // 组别：空串表示不限组别（兼容旧存档）
        public string AgeGroup {
            get { return _ageGroup; }
            set { _ageGroup = value; OnPropertyChanged("AgeGroup"); }
        }
        public string EventName {
            get { return _eventName; }
            set { _eventName = value; OnPropertyChanged("EventName"); }
        }
        public string Gender {
            get { return _gender; }
            set { _gender = value; OnPropertyChanged("Gender"); }
        }
        public string Stage {
            get { return _stage; }
            set { _stage = value; OnPropertyChanged("Stage"); }
        }
        public int HeatCount {
            get { return _heatCount; }
            set { _heatCount = value; OnPropertyChanged("HeatCount"); }
        }
        public bool IsRelay {
            get { return _isRelay; }
            set { _isRelay = value; OnPropertyChanged("IsRelay"); }
        }

        public string DisplayText {
            get {
                return string.Format("{0} {1} {2}", _gender, _eventName, _stage);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 泳池配置
    // ═══════════════════════════════════════════════════════════════
    public class PoolConfig
    {
        public int Length { get; set; }
        public int LaneCount { get; set; }
        public List<int> LaneNumbers { get; set; }
        public bool HasRightStartBlock { get; set; }

        public PoolConfig() {
            Length = 50;
            LaneCount = 10;
            LaneNumbers = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            HasRightStartBlock = false;
        }

        public void SetLaneCount(int count) {
            LaneCount = count;
            LaneNumbers = new List<int>();
            if (count == 10) {
                for (int i = 0; i <= 9; i++) LaneNumbers.Add(i);
            } else if (count == 8) {
                for (int i = 1; i <= 8; i++) LaneNumbers.Add(i);
            } else {
                for (int i = 1; i <= count; i++) LaneNumbers.Add(i);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 比赛数据包（JSON 序列化）
    // ═══════════════════════════════════════════════════════════════
    // 组别：{名称, 最小年龄, 最大年龄}。一个运动员按其年龄落入第一个匹配的组
    public class AgeGroup
    {
        public string Name { get; set; }
        public int MinAge { get; set; }
        public int MaxAge { get; set; }
    }

    // 静态注册表：Swimmer.UpdateAgeCategory 运行时通过它查询当前有效组别列表；
    // MainWindow 负责在加载/保存组别时同步到此表
    public static class AgeGroupRegistry
    {
        private static List<AgeGroup> _groups = new List<AgeGroup>();

        public static List<AgeGroup> Groups { get { return _groups; } }

        public static void Set(IEnumerable<AgeGroup> groups) {
            _groups = groups != null ? new List<AgeGroup>(groups) : new List<AgeGroup>();
        }

        public static string CategoryFor(int age) {
            if (_groups.Count == 0) {
                // 缺省规则（兼容旧数据）
                if (age >= 12 && age <= 13) return "青少年";
                if (age >= 14 && age <= 17) return "少年";
                if (age >= 18 && age <= 45) return "成人";
                if (age > 45) return "大师";
                return "";
            }
            foreach (var g in _groups) {
                if (age >= g.MinAge && age <= g.MaxAge) return g.Name ?? "";
            }
            return "";
        }
    }

    // 代表队号码段：每个代表队设置一个数字区间（如 中国 001-050）
    public class BibRange
    {
        public string Country { get; set; }   // 代表队 / 单位 / 国家
        public int Start { get; set; }        // 起始号（纯数字）
        public int End { get; set; }          // 结束号（纯数字，含）
        public int Width { get; set; }        // 补零宽度（通常 3 或 4）

        public BibRange() { Width = 3; }
    }

    // 大屏顶部主纪录的可选项（由用户在"大屏显示记录"对话框管理）
    public class DisplayRecordOption
    {
        public string Label { get; set; }      // 简称：WR / AR / NR / 省R / 市R …
        public string TypeName { get; set; }    // 完整名称（与 SwimmingRecord.RecordType 一致）
    }

    // 团体计分配置：每个名次的得分、接力倍率、组别系数、取分人数
    // 写到 CompetitionPackage 持久化，赛事重新加载时仍生效
    public class ScoringConfig
    {
        // 个人项目第 1 / 2 / 3 / ... 名得分（按索引 0..N-1）
        public List<double> IndividualPoints { get; set; }
        // 接力项目第 1 / 2 / 3 / ... 名得分（按索引 0..N-1）
        public List<double> RelayPoints { get; set; }
        // 组别 → 得分系数（如 青少年=0.8、大师=0.7）；找不到则按 1.0 计
        public Dictionary<string, double> AgeGroupCoefficients { get; set; }
        // 取分人数（前 N 名得分），默认 8
        public int RankCutoff { get; set; }
        // 破纪录加分（每破 1 项纪录额外加分）；0 = 不加分
        public double RecordBreakBonus { get; set; }

        public ScoringConfig() { ResetToDefaults(); }

        public void ResetToDefaults() {
            IndividualPoints = new List<double> { 12, 10, 8, 7, 6, 5, 4, 3 };
            RelayPoints      = new List<double> { 24, 20, 16, 14, 12, 10, 8, 6 };
            AgeGroupCoefficients = new Dictionary<string, double> {
                { "青少年", 0.8 }, { "少年", 0.8 }, { "大师", 0.7 }
            };
            RankCutoff = 8;
            RecordBreakBonus = 0;
        }

        public double GetIndividualPoint(int rank) {
            int idx = rank - 1;
            if (idx < 0 || idx >= (IndividualPoints != null ? IndividualPoints.Count : 0)) return 0;
            return IndividualPoints[idx];
        }
        public double GetRelayPoint(int rank) {
            int idx = rank - 1;
            if (idx < 0 || idx >= (RelayPoints != null ? RelayPoints.Count : 0)) return 0;
            return RelayPoints[idx];
        }
        public double GetAgeCoefficient(string ageGroup) {
            if (string.IsNullOrEmpty(ageGroup) || AgeGroupCoefficients == null) return 1.0;
            double c;
            return AgeGroupCoefficients.TryGetValue(ageGroup, out c) ? c : 1.0;
        }
    }

    public class CompetitionPackage
    {
        public string CompetitionName { get; set; }
        public string CompetitionMode { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string Location { get; set; }
        public int PoolLength { get; set; }
        public int LaneCount { get; set; }
        public string Organizer { get; set; }
        public string Host { get; set; }
        public string TechnicalDelegate { get; set; }
        public string Referee { get; set; }
        public string Starter { get; set; }
        public string ChiefJudge { get; set; }
        public List<string> Officials { get; set; }
        public List<Swimmer> Swimmers { get; set; }
        public List<RelayTeam> RelayTeams { get; set; }
        public List<SwimmingRecord> Records { get; set; }
        public List<TeamScore> TeamScores { get; set; }
        public List<ScheduleItem> Schedule { get; set; }
        public List<string> Events { get; set; }
        public List<AgeGroup> AgeGroups { get; set; }
        public List<string> Genders { get; set; }
        public List<string> Stages { get; set; }
        public List<string> HeatCounts { get; set; }
        public List<BibRange> BibRanges { get; set; }
        public LaneCloseSettings LaneCloseSettings { get; set; }
        public Dictionary<string, List<string>> DisputeLog { get; set; }
        public ProgramBookData ProgramBook { get; set; }
        public ResultBookData ResultBook { get; set; }
        // 大屏顶部显示的"主纪录"类型（如 世界纪录/亚洲纪录/全国纪录/省记录/市记录…）
        public string DisplayRecordLabel { get; set; }      // 显示用简称，如 WR / AR / NR / 省R / 市R
        public string DisplayRecordTypeName { get; set; }    // 与 SwimmingRecord.RecordType 比对，如 "世界纪录"
        public List<DisplayRecordOption> DisplayRecordOptions { get; set; } // 可选项（预设+用户自定义）
        // 已"确认本组成绩"并锁定的组次列表，元素 = "<组别>|<性别>|<项目>|<赛次>|<组次>"
        public List<string> ConfirmedHeats { get; set; }
        // 团体计分配置（名次分 / 接力倍率 / 组别系数 / 取分人数 / 破纪录加分）
        public ScoringConfig ScoringConfig { get; set; }

        public CompetitionPackage() {
            CompetitionMode = "domestic";
            PoolLength = 50;
            LaneCount = 10;
            Officials = new List<string>();
            Swimmers = new List<Swimmer>();
            RelayTeams = new List<RelayTeam>();
            Records = new List<SwimmingRecord>();
            TeamScores = new List<TeamScore>();
            Schedule = new List<ScheduleItem>();
            Events = new List<string>();
            AgeGroups = new List<AgeGroup>();
            BibRanges = new List<BibRange>();
            LaneCloseSettings = new LaneCloseSettings();
            DisputeLog = new Dictionary<string, List<string>>();
            ScoringConfig = new ScoringConfig();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 时间格式化工具
    // ═══════════════════════════════════════════════════════════════
    public static class TimeFormatter
    {
        /// <summary>
        /// 将秒数格式化为显示时间字符串
        /// 规则：前导零消隐，小数部分始终保留两位
        /// 如：52.34, 1:05.30, 2:21.24, 0.24
        /// </summary>
        /// <summary>
        /// 格式化成绩时间（精度0.01秒），前导零消隐：
        /// 3.45（秒<60）, 1:23.45（分>0）, 1:02:03.45（时>0）
        /// </summary>
        public static string Format(double seconds) {
            if (seconds <= 0) return "";
            bool negative = seconds < 0;
            double abs = Math.Abs(seconds);

            int totalCentis = (int)Math.Round(abs * 100);
            int centis = totalCentis % 100;
            int totalSecs = totalCentis / 100;
            int secs = totalSecs % 60;
            int mins = (totalSecs / 60) % 60;
            int hours = totalSecs / 3600;

            string result;
            if (hours > 0) {
                result = string.Format("{0}:{1:D2}:{2:D2}.{3:D2}", hours, mins, secs, centis);
            } else if (mins > 0) {
                result = string.Format("{0}:{1:D2}.{2:D2}", mins, secs, centis);
            } else {
                result = string.Format("{0}.{1:D2}", secs, centis);
            }
            return negative ? "-" + result : result;
        }

        /// <summary>
        /// 格式化滚动时间（精度0.1秒），前导零消隐：
        /// 3.4（秒<60）, 1:23.4（分>0）, 1:02:03.4（时>0）
        /// </summary>
        public static string FormatRunning(double seconds) {
            if (seconds < 0) seconds = 0;
            int totalTenths = (int)(seconds * 10);
            int tenths = totalTenths % 10;
            int totalSecs = totalTenths / 10;
            int secs = totalSecs % 60;
            int mins = (totalSecs / 60) % 60;
            int hours = totalSecs / 3600;

            // 百分位用空格占位，保持与Format()对齐
            if (hours > 0) {
                return string.Format("{0}:{1:D2}:{2:D2}.{3} ", hours, mins, secs, tenths);
            } else if (mins > 0) {
                return string.Format("{0}:{1:D2}.{2} ", mins, secs, tenths);
            }
            return string.Format("{0}.{1} ", secs, tenths);
        }

        /// <summary>
        /// 解析时间字符串为秒数
        /// 支持格式：SS.ss, M:SS.ss, H:MM:SS.ss
        /// 兼容Excel可能产生的格式：0:54.60, 0:01:42.00, 1:42:00, 14:30.67等
        /// </summary>
        public static double Parse(string timeStr) {
            if (string.IsNullOrEmpty(timeStr)) return 0;
            timeStr = timeStr.Trim();
            // 去除Excel可能加的引号、空格等
            timeStr = timeStr.Trim('"', '\'', '\u2018', '\u2019', '\u201C', '\u201D', ' ');
            if (string.IsNullOrEmpty(timeStr)) return 0;
            try {
                string[] colonParts = timeStr.Split(':');
                if (colonParts.Length == 3) {
                    double h = double.Parse(colonParts[0]);
                    double m = double.Parse(colonParts[1]);
                    double s = double.Parse(colonParts[2]);
                    // Excel可能将 1:42.00 变为 0:01:42 (H:MM:SS格式，秒无小数)
                    // 判断：如果小时=0且秒数是整数（无小数点），可能是 0:MM:SS.ss 格式
                    return h * 3600 + m * 60 + s;
                } else if (colonParts.Length == 2) {
                    double part1 = double.Parse(colonParts[0]);
                    double part2 = double.Parse(colonParts[1]);
                    // 判断格式：M:SS.ss 还是 MM:SS（Excel有时把1:42.00变成1:42）
                    return part1 * 60 + part2;
                } else {
                    return double.Parse(colonParts[0]);
                }
            } catch {
                return 0;
            }
        }
    }
}
