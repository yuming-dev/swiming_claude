using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SwimmingScoreboard
{
    /// <summary>
    /// 时间格式化工具类
    /// </summary>
    public static class TimeHelper
    {
        /// <summary>将秒数格式化为 mm:ss.ff 或 ss.ff</summary>
        public static string FormatTime(double seconds)
        {
            if (seconds <= 0) return "--";
            int totalHundredths = (int)Math.Round(seconds * 100);
            int mins = totalHundredths / 6000;
            int secs = (totalHundredths % 6000) / 100;
            int hundredths = totalHundredths % 100;
            if (mins > 0)
                return string.Format("{0}:{1:D2}.{2:D2}", mins, secs, hundredths);
            return string.Format("{0}.{1:D2}", secs, hundredths);
        }

        /// <summary>解析 mm:ss.ff 或 ss.ff 格式为秒数</summary>
        public static double ParseTime(string timeStr)
        {
            if (string.IsNullOrEmpty(timeStr) || timeStr == "--" || timeStr == "无") return 0;
            timeStr = timeStr.Trim();
            try
            {
                if (timeStr.Contains(":"))
                {
                    var parts = timeStr.Split(':');
                    double mins = double.Parse(parts[0]);
                    double secs = double.Parse(parts[1]);
                    return mins * 60 + secs;
                }
                return double.Parse(timeStr);
            }
            catch { return 0; }
        }
    }

    /// <summary>
    /// 比赛成绩记录
    /// </summary>
    public class RaceResult : INotifyPropertyChanged
    {
        private string _eventId;
        private string _stage;
        private int _heatNumber;
        private int _lane;
        private double _finishTime;
        private int _rank;
        private int _overallRank;
        private string _status;
        private string _dqReason;
        private int _pointsEarned;
        private string _swimmerName;
        private string _organization;

        public RaceResult()
        {
            _status = "OK";
            _stage = "预赛";
        }

        public string EventId { get { return _eventId; } set { _eventId = value; OnPropertyChanged("EventId"); } }
        public string Stage { get { return _stage; } set { _stage = value; OnPropertyChanged("Stage"); } }
        public int HeatNumber { get { return _heatNumber; } set { _heatNumber = value; OnPropertyChanged("HeatNumber"); } }
        public int Lane { get { return _lane; } set { _lane = value; OnPropertyChanged("Lane"); } }
        public double FinishTime { get { return _finishTime; } set { _finishTime = value; OnPropertyChanged("FinishTime"); OnPropertyChanged("FinishTimeDisplay"); } }
        public int Rank { get { return _rank; } set { _rank = value; OnPropertyChanged("Rank"); } }
        public int OverallRank { get { return _overallRank; } set { _overallRank = value; OnPropertyChanged("OverallRank"); } }
        public string Status { get { return _status; } set { _status = value; OnPropertyChanged("Status"); OnPropertyChanged("IsValid"); } }
        public string DQReason { get { return _dqReason; } set { _dqReason = value; OnPropertyChanged("DQReason"); } }
        public int PointsEarned { get { return _pointsEarned; } set { _pointsEarned = value; OnPropertyChanged("PointsEarned"); } }
        public string SwimmerName { get { return _swimmerName; } set { _swimmerName = value; OnPropertyChanged("SwimmerName"); } }
        public string Organization { get { return _organization; } set { _organization = value; OnPropertyChanged("Organization"); } }

        public string FinishTimeDisplay { get { return TimeHelper.FormatTime(_finishTime); } }
        public bool IsValid { get { return _status == "OK"; } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 运动员
    /// </summary>
    public class Swimmer : INotifyPropertyChanged
    {
        private string _name;
        private string _gender;
        private string _birthDate;
        private string _idNumber;
        private int _age;
        private string _phone;
        private string _organization;
        private string _bibNumber;
        private string _ageGroup;
        private bool _hasDeepWaterCert;
        private bool _hasInsurance;
        private bool _hasHealthCert;
        private bool _hasLiabilityWaiver;
        private bool _isCheckedIn;
        private string _notes;

        // 报名信息：每条记录对应一个项目（与diving的Diver相同模式）
        private string _eventId;
        private double _reportedTime;
        private string _stage;
        private int _heatNumber;
        private int _lane;
        private int _currentRank;
        private bool _isQualified;

        private ObservableCollection<RaceResult> _results;

        public Swimmer()
        {
            _gender = "男";
            _stage = "预赛";
            _isQualified = true;
            _results = new ObservableCollection<RaceResult>();
        }

        // 基本信息
        public string Name { get { return _name; } set { _name = value; OnPropertyChanged("Name"); OnPropertyChanged("DisplayName"); } }
        public string Gender { get { return _gender; } set { _gender = value; OnPropertyChanged("Gender"); } }
        public string BirthDate { get { return _birthDate; } set { _birthDate = value; OnPropertyChanged("BirthDate"); } }
        public string IDNumber { get { return _idNumber; } set { _idNumber = value; OnPropertyChanged("IDNumber"); } }
        public int Age { get { return _age; } set { _age = value; OnPropertyChanged("Age"); } }
        public string Phone { get { return _phone; } set { _phone = value; OnPropertyChanged("Phone"); } }
        public string Organization { get { return _organization; } set { _organization = value; OnPropertyChanged("Organization"); } }
        public string BibNumber { get { return _bibNumber; } set { _bibNumber = value; OnPropertyChanged("BibNumber"); } }
        public string AgeGroup { get { return _ageGroup; } set { _ageGroup = value; OnPropertyChanged("AgeGroup"); } }
        public string Notes { get { return _notes; } set { _notes = value; OnPropertyChanged("Notes"); } }

        // 资格审核
        public bool HasDeepWaterCert { get { return _hasDeepWaterCert; } set { _hasDeepWaterCert = value; OnPropertyChanged("HasDeepWaterCert"); } }
        public bool HasInsurance { get { return _hasInsurance; } set { _hasInsurance = value; OnPropertyChanged("HasInsurance"); } }
        public bool HasHealthCert { get { return _hasHealthCert; } set { _hasHealthCert = value; OnPropertyChanged("HasHealthCert"); } }
        public bool HasLiabilityWaiver { get { return _hasLiabilityWaiver; } set { _hasLiabilityWaiver = value; OnPropertyChanged("HasLiabilityWaiver"); } }
        public bool IsCheckedIn { get { return _isCheckedIn; } set { _isCheckedIn = value; OnPropertyChanged("IsCheckedIn"); } }

        // 项目相关（每条Swimmer记录绑定一个项目，多项目则有多条记录）
        public string EventId { get { return _eventId; } set { _eventId = value; OnPropertyChanged("EventId"); } }
        public double ReportedTime { get { return _reportedTime; } set { _reportedTime = value; OnPropertyChanged("ReportedTime"); OnPropertyChanged("ReportedTimeDisplay"); } }
        public string Stage { get { return _stage; } set { _stage = value; OnPropertyChanged("Stage"); OnPropertyChanged("BestTime"); OnPropertyChanged("BestTimeDisplay"); } }
        public int HeatNumber { get { return _heatNumber; } set { _heatNumber = value; OnPropertyChanged("HeatNumber"); } }
        public int Lane { get { return _lane; } set { _lane = value; OnPropertyChanged("Lane"); } }
        public int CurrentRank { get { return _currentRank; } set { _currentRank = value; OnPropertyChanged("CurrentRank"); } }
        public bool IsQualified { get { return _isQualified; } set { _isQualified = value; OnPropertyChanged("IsQualified"); } }

        public ObservableCollection<RaceResult> Results { get { return _results; } }

        // 计算属性
        public string DisplayName { get { return _name; } }
        public string ReportedTimeDisplay { get { return TimeHelper.FormatTime(_reportedTime); } }

        /// <summary>获取该选手在当前阶段的成绩</summary>
        public double BestTime
        {
            get
            {
                var stageResults = _results.Where(r => r.EventId == _eventId && r.Stage == _stage && r.IsValid).ToList();
                if (stageResults.Count == 0) return 0;
                return stageResults.Min(r => r.FinishTime);
            }
        }

        public string BestTimeDisplay { get { return TimeHelper.FormatTime(BestTime); } }

        public void AddResult(RaceResult result)
        {
            _results.Add(result);
            OnPropertyChanged("BestTime");
            OnPropertyChanged("BestTimeDisplay");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 接力队
    /// </summary>
    public class RelayTeam : INotifyPropertyChanged
    {
        private string _teamName;
        private string _eventId;
        private string _ageGroup;
        private ObservableCollection<string> _memberNames;
        private double _reportedTime;
        private string _stage;
        private int _heatNumber;
        private int _lane;
        private double _finishTime;
        private string _status;
        private int _rank;
        private int _overallRank;
        private int _pointsEarned;
        private double _leg1Time;
        private double _leg2Time;
        private double _leg3Time;
        private double _leg4Time;

        public RelayTeam()
        {
            _memberNames = new ObservableCollection<string>();
            _stage = "预赛";
            _status = "OK";
        }

        public string TeamName { get { return _teamName; } set { _teamName = value; OnPropertyChanged("TeamName"); OnPropertyChanged("DisplayName"); } }
        public string EventId { get { return _eventId; } set { _eventId = value; OnPropertyChanged("EventId"); } }
        public string AgeGroup { get { return _ageGroup; } set { _ageGroup = value; OnPropertyChanged("AgeGroup"); } }
        public ObservableCollection<string> MemberNames { get { return _memberNames; } set { _memberNames = value; OnPropertyChanged("MemberNames"); OnPropertyChanged("MembersSummary"); } }
        public double ReportedTime { get { return _reportedTime; } set { _reportedTime = value; OnPropertyChanged("ReportedTime"); OnPropertyChanged("ReportedTimeDisplay"); } }
        public string Stage { get { return _stage; } set { _stage = value; OnPropertyChanged("Stage"); } }
        public int HeatNumber { get { return _heatNumber; } set { _heatNumber = value; OnPropertyChanged("HeatNumber"); } }
        public int Lane { get { return _lane; } set { _lane = value; OnPropertyChanged("Lane"); } }
        public double FinishTime { get { return _finishTime; } set { _finishTime = value; OnPropertyChanged("FinishTime"); OnPropertyChanged("FinishTimeDisplay"); } }
        public string Status { get { return _status; } set { _status = value; OnPropertyChanged("Status"); } }
        public int Rank { get { return _rank; } set { _rank = value; OnPropertyChanged("Rank"); } }
        public int OverallRank { get { return _overallRank; } set { _overallRank = value; OnPropertyChanged("OverallRank"); } }
        public int PointsEarned { get { return _pointsEarned; } set { _pointsEarned = value; OnPropertyChanged("PointsEarned"); } }
        public double Leg1Time { get { return _leg1Time; } set { _leg1Time = value; OnPropertyChanged("Leg1Time"); } }
        public double Leg2Time { get { return _leg2Time; } set { _leg2Time = value; OnPropertyChanged("Leg2Time"); } }
        public double Leg3Time { get { return _leg3Time; } set { _leg3Time = value; OnPropertyChanged("Leg3Time"); } }
        public double Leg4Time { get { return _leg4Time; } set { _leg4Time = value; OnPropertyChanged("Leg4Time"); } }

        public string DisplayName { get { return _teamName; } }
        public string ReportedTimeDisplay { get { return TimeHelper.FormatTime(_reportedTime); } }
        public string FinishTimeDisplay { get { return TimeHelper.FormatTime(_finishTime); } }
        public string MembersSummary
        {
            get
            {
                if (_memberNames == null || _memberNames.Count == 0) return "-";
                return string.Join("/", _memberNames);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 比赛项目定义
    /// </summary>
    public class SwimmingEvent : INotifyPropertyChanged
    {
        private string _eventId;
        private string _gender;
        private string _distance;
        private string _stroke;
        private string _ageGroup;
        private bool _isRelay;
        private string _currentStage;
        private int _totalHeats;
        private int _currentHeat;
        private bool _isCompleted;

        public string EventId { get { return _eventId; } set { _eventId = value; OnPropertyChanged("EventId"); } }
        public string Gender { get { return _gender; } set { _gender = value; OnPropertyChanged("Gender"); } }
        public string Distance { get { return _distance; } set { _distance = value; OnPropertyChanged("Distance"); } }
        public string Stroke { get { return _stroke; } set { _stroke = value; OnPropertyChanged("Stroke"); } }
        public string AgeGroup { get { return _ageGroup; } set { _ageGroup = value; OnPropertyChanged("AgeGroup"); } }
        public bool IsRelay { get { return _isRelay; } set { _isRelay = value; OnPropertyChanged("IsRelay"); } }
        public string CurrentStage { get { return _currentStage; } set { _currentStage = value; OnPropertyChanged("CurrentStage"); } }
        public int TotalHeats { get { return _totalHeats; } set { _totalHeats = value; OnPropertyChanged("TotalHeats"); } }
        public int CurrentHeat { get { return _currentHeat; } set { _currentHeat = value; OnPropertyChanged("CurrentHeat"); } }
        public bool IsCompleted { get { return _isCompleted; } set { _isCompleted = value; OnPropertyChanged("IsCompleted"); } }

        public string FullName { get { return _eventId; } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 编排分配（组次/泳道分配记录）
    /// </summary>
    public class HeatAssignment : INotifyPropertyChanged
    {
        public string EventId { get; set; }
        public string Stage { get; set; }
        public int HeatNumber { get; set; }
        public int Lane { get; set; }
        public string SwimmerName { get; set; }
        public string Organization { get; set; }
        public double SeedTime { get; set; }
        public bool IsRelay { get; set; }

        public string SeedTimeDisplay { get { return TimeHelper.FormatTime(SeedTime); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 日程项
    /// </summary>
    public class ScheduleItem : INotifyPropertyChanged
    {
        private string _date;
        private string _time;
        private string _session;
        private string _event;
        private string _stage;

        public string Date { get { return _date; } set { _date = value; OnPropertyChanged("Date"); } }
        public string Time { get { return _time; } set { _time = value; OnPropertyChanged("Time"); } }
        public string Session { get { return _session; } set { _session = value; OnPropertyChanged("Session"); } }
        public string Event { get { return _event; } set { _event = value; OnPropertyChanged("Event"); } }
        public string Stage { get { return _stage; } set { _stage = value; OnPropertyChanged("Stage"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 参赛单位
    /// </summary>
    public class Organization : INotifyPropertyChanged
    {
        private string _name;
        private string _leaderName;
        private string _coachName;
        private string _phone;
        private double _totalPoints;

        public string Name { get { return _name; } set { _name = value; OnPropertyChanged("Name"); } }
        public string LeaderName { get { return _leaderName; } set { _leaderName = value; OnPropertyChanged("LeaderName"); } }
        public string CoachName { get { return _coachName; } set { _coachName = value; OnPropertyChanged("CoachName"); } }
        public string Phone { get { return _phone; } set { _phone = value; OnPropertyChanged("Phone"); } }
        public double TotalPoints { get { return _totalPoints; } set { _totalPoints = value; OnPropertyChanged("TotalPoints"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 纪录条目
    /// </summary>
    public class RecordEntry : INotifyPropertyChanged
    {
        private string _eventId;
        private string _swimmerName;
        private string _organization;
        private double _time;
        private string _recordType;
        private double _previousRecord;
        private string _dateAchieved;

        public string EventId { get { return _eventId; } set { _eventId = value; OnPropertyChanged("EventId"); } }
        public string SwimmerName { get { return _swimmerName; } set { _swimmerName = value; OnPropertyChanged("SwimmerName"); } }
        public string Organization { get { return _organization; } set { _organization = value; OnPropertyChanged("Organization"); } }
        public double Time { get { return _time; } set { _time = value; OnPropertyChanged("Time"); OnPropertyChanged("TimeDisplay"); } }
        public string RecordType { get { return _recordType; } set { _recordType = value; OnPropertyChanged("RecordType"); } }
        public double PreviousRecord { get { return _previousRecord; } set { _previousRecord = value; OnPropertyChanged("PreviousRecord"); } }
        public string DateAchieved { get { return _dateAchieved; } set { _dateAchieved = value; OnPropertyChanged("DateAchieved"); } }

        public string TimeDisplay { get { return TimeHelper.FormatTime(_time); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 编排统计视图
    /// </summary>
    public class EventSummary
    {
        public string EventName { get; set; }
        public int AthleteCount { get; set; }
        public int HeatCount { get; set; }
        public string BestTimeDisplay { get; set; }
        public double BestTime { get; set; }
    }

    /// <summary>
    /// 分组信息（用于编排微调视图）
    /// </summary>
    public class HeatGroupInfo : INotifyPropertyChanged
    {
        public string GroupName { get; set; }
        public ObservableCollection<Swimmer> Members { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// 存档信息
    /// </summary>
    public class BackupInfo
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string LastModified { get; set; }
    }

    /// <summary>
    /// 字符串到日期转换器（WPF绑定用）
    /// </summary>
    public class StringToDateConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString())) return null;
            DateTime dt;
            if (DateTime.TryParse(value.ToString(), out dt)) return dt;
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is DateTime) return ((DateTime)value).ToString("yyyy-MM-dd");
            return value == null ? null : value.ToString();
        }
    }

    /// <summary>
    /// 竞赛数据存储容器（JSON序列化/反序列化用）
    /// </summary>
    public class CompetitionData
    {
        public string CompetitionName { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public List<Swimmer> Swimmers { get; set; }
        public List<RelayTeam> RelayTeams { get; set; }
        public List<Organization> Organizations { get; set; }
        public List<ScheduleItem> Schedule { get; set; }
        public List<RecordEntry> Records { get; set; }

        // 竞赛官员
        public string OfficialReferee { get; set; }
        public string OfficialStarter { get; set; }
        public string OfficialChiefTimekeeper { get; set; }
        public string OfficialStrokeJudges { get; set; }
        public string OfficialTurnJudges { get; set; }
        public string OfficialSecretary { get; set; }

        public CompetitionData()
        {
            Swimmers = new List<Swimmer>();
            RelayTeams = new List<RelayTeam>();
            Organizations = new List<Organization>();
            Schedule = new List<ScheduleItem>();
            Records = new List<RecordEntry>();
        }
    }
}
