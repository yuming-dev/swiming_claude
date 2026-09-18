using System;
using System.Collections.Generic;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 竞赛数据服务 —— 接口与数据契约                            2026-08-24
    //
    // 【为什么要有这一层】meet.db 是 SQLite，是文件库。多台机器同时开共享
    //   目录里的同一个文件，SMB 上的锁不可靠，迟早坏库 —— 比赛当天坏一次
    //   就完了。所以主服务器独占这个文件，别人走这个接口。
    //
    // 【两种实现，上层代码不变】
    //   LocalMeetService   直接开库。单机比赛、就一台机器的小赛事用这个。
    //   RemoteMeetService  走网络请到主服务器。联网比赛用这个。
    //
    // 【三条规矩】（用户 2026-08-24 定）
    //   一、每个操作只碰该碰的行。没有 GetAll / SaveAll。
    //   二、所有写操作带操作人，自动落 audit_log：谁、什么时候、改前是多少。
    //   三、锁只锁正在比赛的那一组。别的组、别的项目照常读写。
    //       撞上锁就直接拒绝并回一句「本组比赛中」，不排队。
    //
    // 【名次一律由服务器算】名次、Q/R、并列的 = 前缀、成绩差，都在
    //   RecomputeRanks 一处算完。各端只管排版 —— 否则大屏、打印、网页
    //   三家各算一遍，迟早算出三个结果。
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>撞上「本组比赛中」的锁。调用方直接把 Message 显示给用户即可。</summary>
    public class HeatLockedException : Exception
    {
        public long RoundId; public int Heat;
        public HeatLockedException(long roundId, int heat)
            : base("本组比赛中，不能修改（第" + heat + "组）") { RoundId = roundId; Heat = heat; }
    }

    /// <summary>数据不合规（重号、抢道、找不到对象…）。</summary>
    public class MeetDataException : Exception
    {
        public MeetDataException(string msg) : base(msg) { }
    }

    // ── 赛事 ────────────────────────────────────────────────────────────
    public class MeetInfo
    {
        public string Name, NameEn, Mode, Rule, StartDate, EndDate, Location, City;
        public bool UseAgeGroup = true;              // 青少年·U系列必须为 true
        public int PoolLength = 50, LaneCount = 8;
        public string LaneNumbers, StartPosition;    // 泳道编号逗号分隔，注意 0 是真泳道
        public string Organizer, Host, TechnicalDelegate, Referee, Starter, Arbiter, ChiefJudge;
        public string DisplayRecordLabel, DisplayRecordTypeName;
    }

    public class UnitDto
    {
        public long Id;
        public string Name, ShortName, FullName, Leader, Coach, Doctor, Phone, Address, BibRanges, Note;
        public double BasePoints;
        public double Score, IndividualPoints, RelayPoints, RecordBonus;
        public int Gold, Silver, Bronze, ScoreRank;
    }

    public class StaffDto
    {
        public long Id; public int Ord;
        public string Name, Title, Group, Gender, RefereeLevel, Country, Phone, Note;
        public bool IsOfficial;                      // 列入秩序册裁判员名单
    }

    // ── 日程 / 项目 / 赛次 ──────────────────────────────────────────────
    public class SessionDto
    {
        public long Id; public int No;
        public string Date, Name, StartTime, Note;   // Name = 上午 / 下午 / 晚间
    }

    /// <summary>项目 = 录取单元。名次、晋级、破纪录、得分、发奖全按它算。</summary>
    public class EventDto
    {
        public long Id;
        public int EvNum;                            // 项目号，跨赛次共享
        public string AgeGroup = "";                 // 组别，青少年·U系列必填
        public string Gender;                        // 男 / 女 / 男女（男女=项目本身混合，如混合泳接力）
        public int Distance;                         // 接力填每棒距离
        public string Stroke;
        public int RelayLegs = 1;                    // 个人 1，接力 4
        public string EventName;                     // 显示名，不参与任何判断
        public int AwardPlaces = 8;
        public string Note;
        public bool IsRelay { get { return RelayLegs > 1; } }
        public int TotalDistance { get { return Distance * Math.Max(RelayLegs, 1); } }
    }

    /// <summary>赛次 = 一次实际下水的比赛安排。可同时录取多个项目（混编）。</summary>
    public class RoundDto
    {
        public long Id; public long? SessionId;
        public string Stage;                         // 预赛/半决赛/决赛/计时决赛/颁奖仪式
        public int Distance; public string Stroke; public int RelayLegs = 1;
        public int Ord; public string Time;
        public int HeatCount; public int? LaneCount;
        public bool IsCeremony;
        public string Status, Title, Note;
        public List<RoundEventDto> Events = new List<RoundEventDto>();
    }

    public class RoundEventDto
    {
        public long EventId; public int Ord;
        public int PromoteCount, ReserveCount;       // 各组别各算各的晋级/替补人数
        public string AgeGroup, Gender, EventName;   // 只读，方便显示
    }

    /// <summary>日程一行（v_schedule）。混编赛次会出多行，每个项目一行。</summary>
    public class ScheduleRow
    {
        public long RoundId, EventId; public long? SessionId;
        public int SessionNo; public string Date, SessionName, Time;
        public int EvNum; public string AgeGroup, Gender, EventName;
        public int Distance; public string Stroke; public int RelayLegs;
        public string Stage; public int HeatCount;
        public bool IsCeremony; public string Status, RoundTitle;
        public int PromoteCount;
    }

    // ── 报名 ────────────────────────────────────────────────────────────
    public class AthleteDto
    {
        public long Id; public long? UnitId;
        public string BibNumber, Name, NameEn, Gender, BirthDate, IdNumber;
        public string JointUnit, Coach, Phone, CsaNumber, FinaNumber, HealthCertDate, AgeCategory, Note;
        public string UnitName;                      // 只读
    }

    /// <summary>报名。个人和接力队共用一张表：AthleteId 有值=个人，TeamName 有值=接力队。</summary>
    public class EntryDto
    {
        public long Id; public long EventId;
        public long? AthleteId, UnitId;
        public string TeamName;                      // 有值 = 接力队
        public string BibNumber, EntryTime, Coach, Status, Note;
        public double EntryTimeSeconds;
        public bool IsQualified = true;
        public string AthleteName, UnitName;         // 只读
        public bool IsRelay { get { return !string.IsNullOrEmpty(TeamName); } }
        public string DisplayName { get { return IsRelay ? TeamName : AthleteName; } }
    }

    // ── 分组 / 成绩 ─────────────────────────────────────────────────────
    public class HeatSummary
    {
        public long RoundId; public int Heat;
        public string State;                         // normal / racing / cancelled
        public int? MergedInto; public string CancelReason;
        public string GunTime, StartedAt, ConfirmedAt, ConfirmedBy, Note;
        public int LaneCount;                        // 实到几条泳道
        public bool IsConfirmed { get { return !string.IsNullOrEmpty(ConfirmedAt); } }
        public bool IsRacing { get { return State == "racing"; } }
        public bool IsCancelled { get { return State == "cancelled"; } }
    }

    /// <summary>一条泳道。检录、成绩、名次、DSQ、破纪录标注全在这一行上。</summary>
    public class LaneRow
    {
        public long Id;                              // heat_entries.id，回写就靠它
        public long RoundId, EntryId, EventId;
        public int? Heat, Lane, ReserveNo;           // 替补没有组次和道次；注意 0 是真泳道
        public string Stage;
        public int EvNum; public string AgeGroup, Gender, EventName;
        public string Name, UnitName, BirthDate, JointUnit, Coach;
        public bool IsRelay;
        public double SeedTimeSeconds; public string SeedTime;   // 半决赛名单上那列「预赛成绩」
        public string CheckinStatus, CheckinAt;
        public long? PromotedFrom; public int PromotedRank;

        public double FinalTime;
        public int Rank;                             // 本项目本赛次的跨组大排名
        public int HeatRank;                         // 组内名次，服务器现算，不入库
        public double Gap;                           // 与本项目第一名的成绩差，现算
        public bool IsTie;                           // 与人并列 → 打印时名次前加 =
        public string PromotionMark;                 // Q 晋级 / R 替补
        public double Score;
        public string Status, DsqCode; public int DsqLeg;
        public string RecordNote, TimingSource;
        public double ReactionTime, TouchpadTime, StartBlockTime;
        public double Pb1Time, Pb2Time, Pb3Time, ManualLeft, ManualRight;
        public string ResultAt, Note, DisputeNote;

        public List<SplitDto> Splits;                // 按需带上
        public List<RelayLegDto> Legs;
    }

    public class SplitDto
    {
        public int Distance; public double CumulativeTime, LapTime;
        public int RankAt;                           // 途中名次，结果表上 50m (1)28.22 的括号
        public string TimingSource; public bool IsManual;
    }

    public class RelayLegDto
    {
        public int LegOrder; public long? AthleteId;
        public string SwimmerName, SwimmerBib, SwimmerIdNo, SwimmerBirth, SwimmerGender;
        public double ReactionTime;                  // 可为负（交接边缘），校验不许用 > 0
        public double LegTime, CumulativeTime; public int RankAt;
    }

    // ── 纪录 ────────────────────────────────────────────────────────────
    public class RecordDto
    {
        public long Id; public int Ord;
        public string Abbr, RecordType;              // WR/AR/NR/WJ/NJ/MR/省R
        public string AgeGroup = "", Gender, Stroke, EventName;
        public int Distance, RelayLegs = 1;
        public double TimeSeconds;
        public string HolderName, HolderCountry, Date, Location, Note;
        public bool IsCurrent = true;
        public long? BrokenBy;
    }

    public class RecordBreak
    {
        public RecordDto Record;                     // 被打破的那条
        public double NewTime; public bool IsTie;    // 平纪录
        public string NewHolder, NewCountry;
    }

    // ── 比赛中（当前组）─────────────────────────────────────────────────
    public class LiveHeat
    {
        public long MeetRoundId, MeetEventId;
        public int EvNum; public string SessionName, AgeGroup, Gender, EventName, Stage;
        public int Distance; public string Stroke; public int RelayLegs = 1;
        public int Heat, TotalHeats, LaneCount, PoolLength, TotalDistance;
        public string RaceState, GunTime, OpenedAt;
        public bool ResultConfirmed;
        public List<LiveLane> Lanes = new List<LiveLane>();
    }

    public class LiveLane
    {
        public int Lane; public long HeatEntryId;
        public string BibNumber, Name, LegNames, Country, AgeCategory, Gender, SeedTime;
        public bool IsRelay;
        public double FinalTime; public int Rank;
        public string Status, RecordNote, TimingSource;
        public double ReactionTime, TouchpadTime, StartBlockTime;
        public double Pb1Time, Pb2Time, Pb3Time, ManualLeft, ManualRight;
        public int CurrentLap; public bool IsFinished, IsFalseStart;
        public string DsqCode; public int DsqLeg;
        public List<SplitDto> Splits;
        public List<RelayLegDto> Legs;
    }

    public class AuditRow
    {
        public long Id; public string At, Operator, Action, Target;
        public long? TargetId; public string OldValue, NewValue, Note;
    }

    // ══════════════════════════════════════════════════════════════════════
    // 服务接口
    // ══════════════════════════════════════════════════════════════════════
    public interface IMeetService : IDisposable
    {
        // ── A. 赛事 / 基础资料 ──
        MeetInfo GetMeetInfo();
        void     SaveMeetInfo(MeetInfo info, string op);
        string   GetSetting(string key);
        void     SaveSetting(string key, string json, string op);
        List<UnitDto> GetUnits();
        long     SaveUnit(UnitDto u, string op);
        void     DeleteUnit(long id, string op);
        List<StaffDto> GetStaff(bool officialsOnly);
        long     SaveStaff(StaffDto s, string op);
        void     DeleteStaff(long id, string op);

        // ── B. 日程 / 项目 / 赛次 ──
        List<ScheduleRow> GetSchedule();
        RoundDto GetRound(long roundId);
        long     SaveSession(SessionDto s, string op);
        long     SaveEvent(EventDto e, string op);
        long     SaveRound(RoundDto r, string op);
        /// <summary>这一趟下水录取哪几个项目 —— 混编就在这里定。</summary>
        void     SetRoundEvents(long roundId, List<RoundEventDto> events, string op);
        List<EventDto> GetEvents();

        // ── C. 报名 ──
        List<EntryDto> GetEntries(long eventId);
        long     SaveEntry(EntryDto e, string op);
        void     DeleteEntry(long id, string op);
        List<AthleteDto> SearchAthletes(string keyword, long? unitId, int limit);
        long     SaveAthlete(AthleteDto a, string op);
        /// <summary>批量导报名，逐条返回结果（成功/原因）。</summary>
        List<string> ImportEntries(List<EntryDto> list, string op);

        // ── D. 分组 ──
        List<HeatSummary> GetHeatList(long roundId);
        /// <summary>一组的名单。混编时每行带各自的组别和项目。最高频操作。</summary>
        List<LaneRow> GetHeat(long roundId, int heat);
        void     SaveHeatEntry(LaneRow row, string op);
        void     AssignLanes(long roundId, List<LaneRow> rows, string op);
        void     MoveEntry(long heatEntryId, int newHeat, int newLane, string op);
        /// <summary>并组：把 fromHeat 的人按 laneMap 挪进 intoHeat，原组标取消并记并入哪组。</summary>
        void     MergeHeats(long roundId, int fromHeat, int intoHeat, Dictionary<long, int> laneMap, string op);
        void     CancelHeat(long roundId, int heat, string reason, string op);
        void     SetReserves(long roundId, List<long> entryIds, string op);

        // ── E. 接力棒次 ──
        List<RelayLegDto> GetRelayLegs(long heatEntryId);
        void     SaveRelayLegs(long heatEntryId, List<RelayLegDto> legs, string op);

        // ── F. 检录 ──
        List<LaneRow> GetCheckinList(long roundId);
        void     SetCheckin(long heatEntryId, string status, string op);

        // ── G. 比赛中（当前组库）──
        /// <summary>灌当前组库并把这一组置为锁定。别处再改这一组就抛 HeatLockedException。</summary>
        LiveHeat OpenHeat(long roundId, int heat, string op);
        LiveHeat GetLiveHeat();
        void     UpdateLane(int lane, LiveLane data);
        void     UpdateSplit(int lane, int distance, double cumulative, double lap, string source);
        void     SetLaneStatus(int lane, string status, int dsqLeg, string dsqCode, string op);
        void     SetRaceState(string state, string gunTime);
        /// <summary>确认成绩：回写 meet.db、查破纪录、解锁、清当前组库。</summary>
        List<RecordBreak> CommitHeat(string op);

        /// <summary>
        /// 把【外面送来的】一组成绩回写。远端模式必须走这个 ——
        /// 计时端的成绩在计时端本机的 current_heat.db 里, 服务器自己那个是空的,
        /// 服务器要是去读自己的, 回写的就是一组空成绩。
        /// </summary>
        List<RecordBreak> CommitHeatFrom(LiveHeat live, string op);

        /// <summary>
        /// 用一份现成的 LiveHeat 灌本机当前组库(不查 meet.db)。
        /// 远端模式下计时端就是这么把服务器给的名单装进本机小库的。
        /// </summary>
        void SeedLiveHeat(LiveHeat live);
        /// <summary>放弃本组（重赛）：解锁并清当前组库，不回写。</summary>
        void     DiscardHeat(string op);

        // ── H. 成绩 / 名次 / 晋级 ──
        List<LaneRow> GetResults(long roundId, long? eventId);
        /// <summary>按项目各排各的大排名，打 Q/R，算组内名次和成绩差。名次一律在这里算。</summary>
        void     RecomputeRanks(long roundId, long eventId, string op);
        void     SaveResult(LaneRow row, string reason, string op);
        List<LaneRow> GetPromotionList(long eventId, string fromStage, string toStage);
        void     PromoteToNextRound(long eventId, string fromStage, string toStage, List<long> heatEntryIds, string op);

        // ── I. 纪录 / 团体分 ──
        List<RecordDto> GetRecords(string ageGroup, string gender, int distance, string stroke, int relayLegs);
        List<RecordBreak> CheckRecordBreak(long heatEntryId);
        void     ApplyRecordBreak(long heatEntryId, List<RecordBreak> breaks, string op);
        long     SaveRecord(RecordDto r, string op);
        /// <summary>2026-09-18 SaveRecord 一直只有增/改, 没有删——手工编辑纪录界面(ApplyRecordsPatch)
        /// 的"deletes"那半份补丁从没地方落地过, 一并补上。</summary>
        void     DeleteRecord(long id, string op);
        /// <summary>2026-09-18 破纪录证书要印"原记录成绩/原持有人/原记录时间"——ApplyRecordBreak
        /// 早就把被打破那条 records 行置成历史(is_current=0, broken_by=这条成绩的 heat_entries.id),
        /// 是唯一保真的一份, 从没被真正查询过。证书功能一直读的是 LaneResult 上一个纯内存字段
        /// (CheckRecords 在实时出成绩那一刻顺手记的快照), 这份快照从不落库、也不跨机器同步——
        /// 只要打证书那一刻不是当初计时那台机器上没重载过的同一个内存对象(比如换机器打印、
        /// 或者这条成绩是靠"竞赛库回读"重新建出来的), 快照就是空的, 即使库里其实一直老实
        /// 存着原记录。这个方法把 meet.db 里这份历史行重新暴露出来, 给 ApplyHeatFromDb
        /// 兜底补齐。abbr 为空时随便挑一条(单一记录类型的常见情形)。</summary>
        RecordDto GetOldRecordForHeatEntry(long heatEntryId, string abbr);
        List<UnitDto> GetTeamScores();
        void     RecomputeTeamScores(string op);

        // ── J. 报表（只读，服务器算好返回）──
        List<LaneRow> GetStartList(long roundId, long? eventId);
        List<LaneRow> GetResultSheet(long roundId, long? eventId);
        List<LaneRow> GetSummary(long roundId, long eventId);
        List<LaneRow> GetRankingBulletin(long eventId);
        /// <summary>2026-09-17 组排名表(event_rankings)的原始行——项目全部组确认后生成的
        /// 定稿快照, "项目成绩"打印/查询就读这个。必须走 RPC(而不是各机器各自查本机
        /// event_rankings), 因为这张表只在主服务器那份库里真正生成; 计时端/编排端本机
        /// 的 event_rankings 要么是导入时的空表、要么压根没这张表——不这样问真身,
        /// 就会出现"服务器早就定稿了, 别的机器打印却一直说尚未定稿"。</summary>
        List<EventRankRow> GetEventRankRows(long roundId, long eventId);
        /// <summary>2026-09-17 全场"组排名表"的轻量快照(见 EventRankSyncRow)——把定稿名次
        /// 灌回内存(晋级查询/前八名/总排名/成绩公报/团体分都读内存里这份)要用它。同一个
        /// 类里(GetEventRankRows/GetEventRankingProgress/GenerateEventRankingIfComplete)
        /// 都已经走 RPC 问真身, 这个原来漏了——联机时(计时端/编排端)一直读本机那份空/
        /// 陈旧的 event_rankings, 团体分/晋级/前八在那些机器上会一直是空的, 跟主服务器
        /// 早没早定稿完全无关。</summary>
        List<EventRankSyncRow> GetAllEventRankings();
        /// <summary>2026-09-17 全场每个(组别/性别/项目/赛次/组次)的"变更指纹"(轻查询, 只
        /// 返回 key+指纹, 不带成绩)——主窗口每 10 秒的增量刷新(RefreshChangedFromDb)靠它
        /// 判断"哪几组变了才重读那一组", 原来只查本机 _local, 联机时(计时端/编排端)本机
        /// 数据库是空/陈旧的, 永远查不到任何指纹, 增量刷新在那些机器上等于彻底失效。</summary>
        List<string[]> ListHeatStamps();
        /// <summary>2026-09-17 全场"已确认成绩"的组列表——赛程导航树的"[已完赛]"标记靠它,
        /// 跟 ListHeatStamps 同一个问题: 原来只查本机, 联机时那些机器上赛程树会一直显示
        /// "未完赛", 哪怕主服务器早就确认了。</summary>
        List<string[]> ListConfirmedHeats();
        /// <summary>2026-09-18 全场当前"正在计时"(heats.state='racing')的组列表, 格式跟
        /// ListConfirmedHeats 一样 [组别,性别,项目,赛次,组次]。正常情况下最多一行(同一时刻
        /// 只有一组在游)。"赛程导航"树原来判"[正在计时]"只看本机内存里的 _currentXxx(当前
        /// 选中的项目/组), 别的机器(主服务器/编排端)navigate 到的跟正在计时的那组根本不是
        /// 同一个, 于是永远判不出"正在计时", 只能等到"确认本组成绩"那一刻数据全灌进去才
        /// 跳到[已确认]——用户实拍到的"所有赛程导航从头到尾没有一处显示过[正在计时]"就是
        /// 这个。heats.state 这张定稿之外的"实时状态"其实一直有(OpenHeat 时就写了
        /// state='racing'), 只是判定逻辑一直没读它、只信本机内存。这里补上读库那条路。</summary>
        List<string[]> GetRacingHeats();
        /// <summary>2026-09-18 生成/刷新某项目的定稿组排名表(event_rankings)——"个人总分排名"/
        /// 团体分靠它算 EventRankFor。老代码只在单机(_meet==_local)时在本机写这张表, 联机时
        /// 靠 PushGenerateEventRanking 经 EditorSyncClient 通知服务器写, 而那条通知只有
        /// IsRemoteTimingControlMode(entry assembly 名字恰好叫 RemoteTimingControl)才会发——
        /// 用 "--role timing" 把普通 SwimmingScoreboard.exe 当计时端连远端服务器(同样合法的
        /// 部署方式)时没人发这条通知, 这张表永远生成不出来。用户实拍到: 决赛项目两组都
        /// 确认了, "个人总分排名"整份是空的, 没有任何运动员。现在不管 exe 是谁一律经
        /// IMeetService(_meet)直接调用——单机时进程内直接跑, 联机时真正落在服务器自己的库上。
        /// 调用前提: 该项目全部组都已确认(调用方已用 GetHeatList 判过 pending==0 并问过操作员),
        /// 这里只再防御性复核一次 pending, 不重复弹确认框。</summary>
        int GenerateEventRankingIfComplete(long roundId, long eventId, string ageGroup, string gender, string eventName, string stage, string op);
        /// <summary>2026-09-18 "空道试游"占位运动员没有 heat_entries 行, GenerateEventRankingIfComplete
        /// 天生看不到他们, 生成组排名表后要靠这一步【追加】进去(见 TriPlaceholderInfo 的说明)。
        /// 老代码这一步一直是 MeetDbBridge 直接写 _local, 联机时(计时端/编排端不是主服务器)
        /// 写的是本机那份空副本, 真身(服务器)那张表永远缺这几行——跟 GenerateEventRankingIfComplete
        /// 同一个病根, 一并经 IMeetService(_meet) 改掉, 不再单独留一条不过网的写路。</summary>
        void AppendTriPlaceholderRankingRows(long roundId, long eventId, List<TriPlaceholderInfo> placeholders);

        // ── K. 运维 ──
        void     Backup(string targetPath);
        List<AuditRow> GetAuditLog(string action, string since, int limit);
        /// <summary>2026-09-13 让上层记一条审计。原来只有库内部的写操作会落 audit_log,
        /// 而"解锁本组成绩"这种动作发生在老程序那一侧, 一样得留下谁、什么时候、为什么。</summary>
        void     LogAudit(string action, string target, string oldValue, string newValue, string note, string op);
    }
}
