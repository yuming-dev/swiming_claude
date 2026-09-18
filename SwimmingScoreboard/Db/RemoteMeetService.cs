using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard.Db
{
    /// <summary>
    /// 一问一答的传输通道。故意做成接口而不是直接用 WebSocket ——
    /// 这样这个类不认识 Fleck / ClientWebSocket, 换传输(WS / TCP / 命名管道)
    /// 不用动它, 单元测试里也能塞个假的进来。
    /// </summary>
    public interface IMeetRpcTransport
    {
        /// <summary>发出去并等应答。超时或断线抛异常。</summary>
        string SendAndWait(string requestJson, int timeoutMs);
        bool IsConnected { get; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // IMeetService 的网络实现                                   2026-08-25
    //
    // 上层拿到的是 IMeetService, 不知道自己连的是本地库还是远端服务器 ——
    // MeetDbBridge 按配置换实现即可, OpenHeat / CommitHeat 那几处调用一行不动。
    //
    // 【计时端为什么能独立】比赛中的高频写(UpdateLane / UpdateSplit)走的是
    //   本地那个 current_heat.db, 根本不经过这个类。真正走网络的只有:
    //     选组时   OpenHeat   取一次名单
    //     确认时   CommitHeat 回写一次成绩
    //   所以主服务器卡了、在 GC、甚至挂了, 这一组照样游完。
    //
    // 【超时】默认 8 秒。宁可报错让操作员知道, 也不要无限等 —— 比赛现场
    //   卡住不动比报错更糟, 操作员至少知道该切本地模式还是重连。
    // ══════════════════════════════════════════════════════════════════════
    public class RemoteMeetService : IMeetService
    {
        private readonly IMeetRpcTransport _t;
        private long _seq;
        public int TimeoutMs = 8000;

        public RemoteMeetService(IMeetRpcTransport transport)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            _t = transport;
        }

        public bool IsConnected { get { return _t.IsConnected; } }
        public void Dispose() { }

        // ── 核心：一次调用 ──────────────────────────────────────────────
        private JToken Call(string op, object args)
        {
            long id = System.Threading.Interlocked.Increment(ref _seq);
            string reply = _t.SendAndWait(MeetRpc.BuildRequest(id, op, args), TimeoutMs);
            var o = JObject.Parse(reply);
            bool ok = o["ok"] != null && (bool)o["ok"];
            if (ok) return o["result"];
            // 服务端的异常要在这边原样重现, 否则上层的 catch 全落空
            throw MeetRpc.ToException(
                (string)o["errorKind"], (string)o["error"],
                o["roundId"] != null ? (long)o["roundId"] : 0,
                o["heat"] != null ? (int)o["heat"] : 0);
        }

        private T Call<T>(string op, object args)
        {
            var r = Call(op, args);
            if (r == null || r.Type == JTokenType.Null) return default(T);
            return r.ToObject<T>(JsonSerializer.Create(MeetRpc.Json));
        }

        private void Send(string op, object args) { Call(op, args); }

        // ── A. 赛事 / 基础资料 ──
        public MeetInfo GetMeetInfo() { return Call<MeetInfo>("GetMeetInfo", null); }
        public void SaveMeetInfo(MeetInfo i, string op) { Send("SaveMeetInfo", new { info = i, op }); }
        public string GetSetting(string key) { return Call<string>("GetSetting", new { key }); }
        public void SaveSetting(string key, string json, string op) { Send("SaveSetting", new { key, json, op }); }
        public List<UnitDto> GetUnits() { return Call<List<UnitDto>>("GetUnits", null); }
        public long SaveUnit(UnitDto u, string op) { return Call<long>("SaveUnit", new { u, op }); }
        public void DeleteUnit(long id, string op) { Send("DeleteUnit", new { id, op }); }
        public List<StaffDto> GetStaff(bool officialsOnly) { return Call<List<StaffDto>>("GetStaff", new { officialsOnly }); }
        public long SaveStaff(StaffDto s, string op) { return Call<long>("SaveStaff", new { s, op }); }
        public void DeleteStaff(long id, string op) { Send("DeleteStaff", new { id, op }); }

        // ── B. 日程 / 项目 / 赛次 ──
        public List<ScheduleRow> GetSchedule() { return Call<List<ScheduleRow>>("GetSchedule", null); }
        public RoundDto GetRound(long roundId) { return Call<RoundDto>("GetRound", new { roundId }); }
        public long SaveSession(SessionDto s, string op) { return Call<long>("SaveSession", new { s, op }); }
        public long SaveEvent(EventDto e, string op) { return Call<long>("SaveEvent", new { e, op }); }
        public long SaveRound(RoundDto r, string op) { return Call<long>("SaveRound", new { r, op }); }
        public void SetRoundEvents(long roundId, List<RoundEventDto> events, string op)
        { Send("SetRoundEvents", new { roundId, events, op }); }
        public List<EventDto> GetEvents() { return Call<List<EventDto>>("GetEvents", null); }

        // ── C. 报名 ──
        public List<EntryDto> GetEntries(long eventId) { return Call<List<EntryDto>>("GetEntries", new { eventId }); }
        public long SaveEntry(EntryDto e, string op) { return Call<long>("SaveEntry", new { e, op }); }
        public void DeleteEntry(long id, string op) { Send("DeleteEntry", new { id, op }); }
        public List<AthleteDto> SearchAthletes(string keyword, long? unitId, int limit)
        { return Call<List<AthleteDto>>("SearchAthletes", new { keyword, unitId, limit }); }
        public long SaveAthlete(AthleteDto a, string op) { return Call<long>("SaveAthlete", new { a, op }); }
        public List<string> ImportEntries(List<EntryDto> list, string op)
        { return Call<List<string>>("ImportEntries", new { list, op }); }

        // ── D. 分组 ──
        public List<HeatSummary> GetHeatList(long roundId) { return Call<List<HeatSummary>>("GetHeatList", new { roundId }); }
        public List<LaneRow> GetHeat(long roundId, int heat) { return Call<List<LaneRow>>("GetHeat", new { roundId, heat }); }
        public void SaveHeatEntry(LaneRow row, string op) { Send("SaveHeatEntry", new { row, op }); }
        public void AssignLanes(long roundId, List<LaneRow> rows, string op) { Send("AssignLanes", new { roundId, rows, op }); }
        public void MoveEntry(long heatEntryId, int newHeat, int newLane, string op)
        { Send("MoveEntry", new { heatEntryId, newHeat, newLane, op }); }
        public void MergeHeats(long roundId, int fromHeat, int intoHeat, Dictionary<long, int> laneMap, string op)
        { Send("MergeHeats", new { roundId, fromHeat, intoHeat, laneMap, op }); }
        public void CancelHeat(long roundId, int heat, string reason, string op)
        { Send("CancelHeat", new { roundId, heat, reason, op }); }
        public void SetReserves(long roundId, List<long> entryIds, string op)
        { Send("SetReserves", new { roundId, entryIds, op }); }

        // ── E. 接力棒次 ──
        public List<RelayLegDto> GetRelayLegs(long heatEntryId) { return Call<List<RelayLegDto>>("GetRelayLegs", new { heatEntryId }); }
        public void SaveRelayLegs(long heatEntryId, List<RelayLegDto> legs, string op)
        { Send("SaveRelayLegs", new { heatEntryId, legs, op }); }

        // ── F. 检录 ──
        public List<LaneRow> GetCheckinList(long roundId) { return Call<List<LaneRow>>("GetCheckinList", new { roundId }); }
        public void SetCheckin(long heatEntryId, string status, string op)
        { Send("SetCheckin", new { heatEntryId, status, op }); }

        // ── G. 比赛中 ────────────────────────────────────────────────────
        // 注意: 只有 OpenHeat / CommitHeat / DiscardHeat 走网络。
        // UpdateLane / UpdateSplit / SetLaneStatus / SetRaceState / GetLiveHeat
        // 操作的是【本机那个 current_heat.db】, 走网络毫无意义, 而且比赛中
        // 每 100ms 往服务器打一次正是要根除的东西。调用方应当持有一个本地
        // LocalMeetService 专管当前组库 —— 见 MeetDbBridge。
        public LiveHeat OpenHeat(long roundId, int heat, string op)
        { return Call<LiveHeat>("OpenHeat", new { roundId, heat, op }); }
        // 远端模式下 CommitHeat 没有意义 —— 服务器那台机器的当前组库是空的。
        // 必须把本机的成绩随请求带过去。
        public List<RecordBreak> CommitHeat(string op)
        { throw new MeetDataException("远端模式请用 CommitHeatFrom，把本机的成绩带过去"); }

        public List<RecordBreak> CommitHeatFrom(LiveHeat live, string op)
        { return Call<List<RecordBreak>>("CommitHeatFrom", new { live, op }); }

        public void SeedLiveHeat(LiveHeat live) { throw NotOverWire("SeedLiveHeat"); }
        public void DiscardHeat(string op) { Send("DiscardHeat", new { op }); }

        public LiveHeat GetLiveHeat() { throw NotOverWire("GetLiveHeat"); }
        public void UpdateLane(int lane, LiveLane d) { throw NotOverWire("UpdateLane"); }
        public void UpdateSplit(int lane, int distance, double cumulative, double lap, string source)
        { throw NotOverWire("UpdateSplit"); }
        public void SetLaneStatus(int lane, string status, int dsqLeg, string dsqCode, string op)
        { throw NotOverWire("SetLaneStatus"); }
        public void SetRaceState(string state, string gunTime) { throw NotOverWire("SetRaceState"); }

        private static MeetDataException NotOverWire(string op)
        {
            return new MeetDataException(op + " 是当前组库的操作，必须走本机 LocalMeetService，" +
                "不能走网络 —— 比赛中每 100ms 往服务器打一次正是要根除的做法");
        }

        // ── H. 成绩 / 名次 / 晋级 ──
        public List<LaneRow> GetResults(long roundId, long? eventId) { return Call<List<LaneRow>>("GetResults", new { roundId, eventId }); }
        public void RecomputeRanks(long roundId, long eventId, string op) { Send("RecomputeRanks", new { roundId, eventId, op }); }
        public void SaveResult(LaneRow row, string reason, string op) { Send("SaveResult", new { row, reason, op }); }
        public List<LaneRow> GetPromotionList(long eventId, string fromStage, string toStage)
        { return Call<List<LaneRow>>("GetPromotionList", new { eventId, fromStage, toStage }); }
        public void PromoteToNextRound(long eventId, string fromStage, string toStage, List<long> heatEntryIds, string op)
        { Send("PromoteToNextRound", new { eventId, fromStage, toStage, heatEntryIds, op }); }

        // ── I. 纪录 / 团体分 ──
        public List<RecordDto> GetRecords(string ageGroup, string gender, int distance, string stroke, int relayLegs)
        { return Call<List<RecordDto>>("GetRecords", new { ageGroup, gender, distance, stroke, relayLegs }); }
        public List<RecordBreak> CheckRecordBreak(long heatEntryId) { return Call<List<RecordBreak>>("CheckRecordBreak", new { heatEntryId }); }
        public void ApplyRecordBreak(long heatEntryId, List<RecordBreak> breaks, string op)
        { Send("ApplyRecordBreak", new { heatEntryId, breaks, op }); }
        public long SaveRecord(RecordDto r, string op) { return Call<long>("SaveRecord", new { r, op }); }
        public List<UnitDto> GetTeamScores() { return Call<List<UnitDto>>("GetTeamScores", null); }
        public void RecomputeTeamScores(string op) { Send("RecomputeTeamScores", new { op }); }

        // ── J. 报表 ──
        public List<LaneRow> GetStartList(long roundId, long? eventId) { return Call<List<LaneRow>>("GetStartList", new { roundId, eventId }); }
        public List<LaneRow> GetResultSheet(long roundId, long? eventId) { return Call<List<LaneRow>>("GetResultSheet", new { roundId, eventId }); }
        public List<LaneRow> GetSummary(long roundId, long eventId) { return Call<List<LaneRow>>("GetSummary", new { roundId, eventId }); }
        public List<LaneRow> GetRankingBulletin(long eventId) { return Call<List<LaneRow>>("GetRankingBulletin", new { eventId }); }
        public List<EventRankRow> GetEventRankRows(long roundId, long eventId) { return Call<List<EventRankRow>>("GetEventRankRows", new { roundId, eventId }); }
        public List<EventRankSyncRow> GetAllEventRankings() { return Call<List<EventRankSyncRow>>("GetAllEventRankings", new { }); }
        public List<string[]> ListHeatStamps() { return Call<List<string[]>>("ListHeatStamps", new { }); }
        public List<string[]> ListConfirmedHeats() { return Call<List<string[]>>("ListConfirmedHeats", new { }); }
        public List<string[]> GetRacingHeats() { return Call<List<string[]>>("GetRacingHeats", new { }); }
        public int GenerateEventRankingIfComplete(long roundId, long eventId, string ageGroup, string gender, string eventName, string stage, string op)
        { return Call<int>("GenerateEventRankingIfComplete", new { roundId, eventId, ageGroup, gender, eventName, stage, op }); }
        public void AppendTriPlaceholderRankingRows(long roundId, long eventId, List<TriPlaceholderInfo> placeholders)
        { Send("AppendTriPlaceholderRankingRows", new { roundId, eventId, placeholders }); }

        // ── K. 运维 ──
        // 备份是在【服务器那台机器上】复制文件, 路径也是服务器的路径。
        public void Backup(string targetPath) { Send("Backup", new { targetPath }); }
        public List<AuditRow> GetAuditLog(string action, string since, int limit)
        { return Call<List<AuditRow>>("GetAuditLog", new { action, since, limit }); }
        public void LogAudit(string action, string target, string oldValue, string newValue, string note, string op)
        { Send("LogAudit", new { action, target, oldValue, newValue, note, op }); }
    }
}
