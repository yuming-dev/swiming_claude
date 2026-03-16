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
        Open,       // 绿色 - 打开（正常接收数据）
        Closed,     // 灰色 - 关闭（不接收数据）
        Broken,     // 红色 - 损坏
        FalseStart  // 黄色 - 抢跳（仅出发台）
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

        public Swimmer() {
            _gender = "男";
            _currentStage = "预赛";
            _isQualified = true;
            _status = "";
            _results = new ObservableCollection<LaneResult>();
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

        private void UpdateAgeCategory() {
            if (_age >= 12 && _age <= 13) _ageCategory = "青少年";
            else if (_age >= 14 && _age <= 17) _ageCategory = "少年";
            else if (_age >= 18 && _age <= 45) _ageCategory = "成人";
            else if (_age > 45) _ageCategory = "大师";
            else _ageCategory = "";
            OnPropertyChanged("AgeCategory");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name) {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 接力队伍
    // ═══════════════════════════════════════════════════════════════
    public class RelayTeam : INotifyPropertyChanged
    {
        private string _teamName;
        private string _eventName;
        private string _gender;
        private ObservableCollection<Swimmer> _members;
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
            set { _date = value; OnPropertyChanged("Date"); }
        }
        public string Location {
            get { return _location; }
            set { _location = value; OnPropertyChanged("Location"); }
        }

        public string TimeDisplay { get { return TimeFormatter.Format(_time); } }

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
        private DeviceStatus _leftBlindWatchStatus = DeviceStatus.Closed;
        private DeviceStatus _leftStartBlockStatus = DeviceStatus.Open;
        private DeviceStatus _rightTouchpadStatus = DeviceStatus.Closed;
        private DeviceStatus _rightBlindWatchStatus = DeviceStatus.Closed;
        private DeviceStatus _rightStartBlockStatus = DeviceStatus.Closed;
        private double _laneCloseCountdown;
        private double _laneCloseTime = 20.0;
        private string _direction = "→";
        private int _currentLap;
        private bool _isFinished;
        private double _reactionTime;
        private bool _isFalseStart;
        private double _leftManualTouchTime;
        private double _rightManualTouchTime;
        private bool _leftTouchpadBroken;
        private bool _leftBlindWatchBroken;
        private bool _leftStartBlockBroken;
        private bool _rightTouchpadBroken;
        private bool _rightBlindWatchBroken;
        private bool _rightStartBlockBroken;

        public int Lane {
            get { return _lane; }
            set { _lane = value; OnPropertyChanged("Lane"); }
        }
        public DeviceStatus LeftTouchpadStatus {
            get { return _leftTouchpadBroken ? DeviceStatus.Broken : _leftTouchpadStatus; }
            set { _leftTouchpadStatus = value; OnPropertyChanged("LeftTouchpadStatus"); }
        }
        public DeviceStatus LeftBlindWatchStatus {
            get { return _leftBlindWatchBroken ? DeviceStatus.Broken : _leftBlindWatchStatus; }
            set { _leftBlindWatchStatus = value; OnPropertyChanged("LeftBlindWatchStatus"); }
        }
        public DeviceStatus LeftStartBlockStatus {
            get {
                if (_leftStartBlockBroken) return DeviceStatus.Broken;
                if (_isFalseStart) return DeviceStatus.FalseStart;
                return _leftStartBlockStatus;
            }
            set { _leftStartBlockStatus = value; OnPropertyChanged("LeftStartBlockStatus"); }
        }
        public DeviceStatus RightTouchpadStatus {
            get { return _rightTouchpadBroken ? DeviceStatus.Broken : _rightTouchpadStatus; }
            set { _rightTouchpadStatus = value; OnPropertyChanged("RightTouchpadStatus"); }
        }
        public DeviceStatus RightBlindWatchStatus {
            get { return _rightBlindWatchBroken ? DeviceStatus.Broken : _rightBlindWatchStatus; }
            set { _rightBlindWatchStatus = value; OnPropertyChanged("RightBlindWatchStatus"); }
        }
        public DeviceStatus RightStartBlockStatus {
            get {
                if (_rightStartBlockBroken) return DeviceStatus.Broken;
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
            set { _reactionTime = value; OnPropertyChanged("ReactionTime"); OnPropertyChanged("ReactionTimeDisplay"); }
        }
        public bool IsFalseStart {
            get { return _isFalseStart; }
            set { _isFalseStart = value; OnPropertyChanged("IsFalseStart"); OnPropertyChanged("LeftStartBlockStatus"); }
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
        public bool LeftBlindWatchBroken {
            get { return _leftBlindWatchBroken; }
            set { _leftBlindWatchBroken = value; OnPropertyChanged("LeftBlindWatchBroken"); OnPropertyChanged("LeftBlindWatchStatus"); }
        }
        public bool LeftStartBlockBroken {
            get { return _leftStartBlockBroken; }
            set { _leftStartBlockBroken = value; OnPropertyChanged("LeftStartBlockBroken"); OnPropertyChanged("LeftStartBlockStatus"); }
        }
        public bool RightTouchpadBroken {
            get { return _rightTouchpadBroken; }
            set { _rightTouchpadBroken = value; OnPropertyChanged("RightTouchpadBroken"); OnPropertyChanged("RightTouchpadStatus"); }
        }
        public bool RightBlindWatchBroken {
            get { return _rightBlindWatchBroken; }
            set { _rightBlindWatchBroken = value; OnPropertyChanged("RightBlindWatchBroken"); OnPropertyChanged("RightBlindWatchStatus"); }
        }
        public bool RightStartBlockBroken {
            get { return _rightStartBlockBroken; }
            set { _rightStartBlockBroken = value; OnPropertyChanged("RightStartBlockBroken"); OnPropertyChanged("RightStartBlockStatus"); }
        }

        public string ReactionTimeDisplay {
            get {
                if (_reactionTime == 0) return "";
                return _reactionTime.ToString("F2");
            }
        }

        public void ResetForNewRace() {
            _leftTouchpadStatus = DeviceStatus.Closed;
            _leftBlindWatchStatus = DeviceStatus.Closed;
            _leftStartBlockStatus = DeviceStatus.Open;
            _rightTouchpadStatus = DeviceStatus.Closed;
            _rightBlindWatchStatus = DeviceStatus.Closed;
            _rightStartBlockStatus = DeviceStatus.Closed;
            _laneCloseCountdown = 0;
            _direction = "→";
            _currentLap = 0;
            _isFinished = false;
            _reactionTime = 0;
            _isFalseStart = false;
            _leftManualTouchTime = 0;
            _rightManualTouchTime = 0;
            NotifyAll();
        }

        private void NotifyAll() {
            string[] props = { "LeftTouchpadStatus", "LeftBlindWatchStatus", "LeftStartBlockStatus",
                "RightTouchpadStatus", "RightBlindWatchStatus", "RightStartBlockStatus",
                "LaneCloseCountdown", "Direction", "CurrentLap", "IsFinished",
                "ReactionTime", "ReactionTimeDisplay", "IsFalseStart",
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
        public LaneCloseSettings LaneCloseSettings { get; set; }
        public Dictionary<string, List<string>> DisputeLog { get; set; }

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
            LaneCloseSettings = new LaneCloseSettings();
            DisputeLog = new Dictionary<string, List<string>>();
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
        /// 格式化为滚动时间显示（精度0.1秒）：MM:SS.t
        /// </summary>
        public static string FormatRunning(double seconds) {
            if (seconds < 0) seconds = 0;
            int totalTenths = (int)(seconds * 10);
            int tenths = totalTenths % 10;
            int totalSecs = totalTenths / 10;
            int secs = totalSecs % 60;
            int mins = totalSecs / 60;

            if (mins > 0) {
                return string.Format("{0}:{1:D2}.{2}", mins, secs, tenths);
            }
            return string.Format("{0}.{1}", secs, tenths);
        }

        /// <summary>
        /// 解析时间字符串为秒数
        /// 支持格式：SS.ss, M:SS.ss, H:MM:SS.ss
        /// </summary>
        public static double Parse(string timeStr) {
            if (string.IsNullOrEmpty(timeStr)) return 0;
            timeStr = timeStr.Trim();
            try {
                string[] colonParts = timeStr.Split(':');
                if (colonParts.Length == 3) {
                    return double.Parse(colonParts[0]) * 3600 + double.Parse(colonParts[1]) * 60 + double.Parse(colonParts[2]);
                } else if (colonParts.Length == 2) {
                    return double.Parse(colonParts[0]) * 60 + double.Parse(colonParts[1]);
                } else {
                    return double.Parse(colonParts[0]);
                }
            } catch {
                return 0;
            }
        }
    }
}
