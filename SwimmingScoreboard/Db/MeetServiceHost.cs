using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 竞赛数据服务 服务端                                       2026-08-25
    //
    // 主服务器独占 meet.db, 别的机器把请求发过来, 这里转给 LocalMeetService
    // 再把结果发回去。业务规则只有 LocalMeetService 那一份, 本地和远端
    // 不可能算出两个结果。
    //
    // 用法: WebSocket 分发里认到 type=="MEET_RPC" 就调 Handle(), 把返回的
    //   字符串原样发回那个 socket。除此以外主程序不用知道这层的存在。
    //
    // 【本类不抛异常】业务异常打包成错误应答发回去(errorKind 带着),
    //   客户端会还原成同一个异常。服务端因为一个客户端的坏请求崩掉,
    //   那是比赛事故。
    // ══════════════════════════════════════════════════════════════════════
    public class MeetServiceHost
    {
        private readonly Func<IMeetService> _svc;
        private readonly Action<string> _log;

        /// <param name="serviceProvider">
        /// 每次取一次当前的服务实例 —— 换赛事时库会重开, 不能把实例缓存死。
        /// </param>
        public MeetServiceHost(Func<IMeetService> serviceProvider, Action<string> log)
        {
            _svc = serviceProvider;
            _log = log ?? delegate { };
        }

        public static bool IsRpc(string type)
        { return string.Equals(type, MeetRpc.RequestType, StringComparison.Ordinal); }

        /// <summary>
        /// 回环传输：请求不出进程, 直接交给本机的 Host。
        /// 用途一是自检 —— 同一批操作分别走 LocalMeetService 和走
        /// RemoteMeetService(回环), 结果必须逐字一致, 否则协议层有 bug;
        /// 用途二是单机模式想统一走 RPC 路径时不必真开网络。
        /// </summary>
        public class LoopbackTransport : IMeetRpcTransport
        {
            private readonly MeetServiceHost _host;
            public int Calls;
            public long Chars;

            public LoopbackTransport(MeetServiceHost host)
            {
                if (host == null) throw new ArgumentNullException("host");
                _host = host;
            }

            public bool IsConnected { get { return true; } }

            public string SendAndWait(string requestJson, int timeoutMs)
            {
                Calls++;
                Chars += requestJson == null ? 0 : requestJson.Length;
                string reply = _host.Handle(JObject.Parse(requestJson));
                Chars += reply == null ? 0 : reply.Length;
                return reply;
            }
        }

        /// <summary>处理一条请求, 返回要发回去的应答 JSON。永不抛异常。</summary>
        public string Handle(JObject msg)
        {
            long id = 0;
            try
            {
                if (msg == null) return null;
                id = msg["id"] != null ? (long)msg["id"] : 0;
                string op = msg["op"] != null ? msg["op"].ToString() : "";
                var args = msg["args"];

                var s = _svc != null ? _svc() : null;
                if (s == null)
                    return MeetRpc.BuildErrReply(id, new MeetDataException("服务器还没打开赛事库"));

                object result = Dispatch(s, op, args);
                return MeetRpc.BuildOkReply(id, result);
            }
            catch (Exception ex)
            {
                _log("竞赛服务处理失败: " + ex.Message);
                return MeetRpc.BuildErrReply(id, ex);
            }
        }

        // 返回 null 表示这个操作没有返回值(void)
        private static object Dispatch(IMeetService s, string op, JToken a)
        {
            switch (op)
            {
                // ── A ──
                case "GetMeetInfo":   return s.GetMeetInfo();
                case "SaveMeetInfo":  s.SaveMeetInfo(MeetRpc.Arg<MeetInfo>(a, "info"), MeetRpc.Arg<string>(a, "op")); return null;
                case "GetSetting":    return s.GetSetting(MeetRpc.Arg<string>(a, "key"));
                case "SaveSetting":   s.SaveSetting(MeetRpc.Arg<string>(a, "key"), MeetRpc.Arg<string>(a, "json"), MeetRpc.Arg<string>(a, "op")); return null;
                case "GetUnits":      return s.GetUnits();
                case "SaveUnit":      return s.SaveUnit(MeetRpc.Arg<UnitDto>(a, "u"), MeetRpc.Arg<string>(a, "op"));
                case "DeleteUnit":    s.DeleteUnit(MeetRpc.Arg<long>(a, "id"), MeetRpc.Arg<string>(a, "op")); return null;
                case "GetStaff":      return s.GetStaff(MeetRpc.Arg<bool>(a, "officialsOnly"));
                case "SaveStaff":     return s.SaveStaff(MeetRpc.Arg<StaffDto>(a, "s"), MeetRpc.Arg<string>(a, "op"));
                case "DeleteStaff":   s.DeleteStaff(MeetRpc.Arg<long>(a, "id"), MeetRpc.Arg<string>(a, "op")); return null;

                // ── B ──
                case "GetSchedule":   return s.GetSchedule();
                case "GetRound":      return s.GetRound(MeetRpc.Arg<long>(a, "roundId"));
                case "SaveSession":   return s.SaveSession(MeetRpc.Arg<SessionDto>(a, "s"), MeetRpc.Arg<string>(a, "op"));
                case "SaveEvent":     return s.SaveEvent(MeetRpc.Arg<EventDto>(a, "e"), MeetRpc.Arg<string>(a, "op"));
                case "SaveRound":     return s.SaveRound(MeetRpc.Arg<RoundDto>(a, "r"), MeetRpc.Arg<string>(a, "op"));
                case "SetRoundEvents":
                    s.SetRoundEvents(MeetRpc.Arg<long>(a, "roundId"),
                        MeetRpc.Arg<List<RoundEventDto>>(a, "events") ?? new List<RoundEventDto>(),
                        MeetRpc.Arg<string>(a, "op")); return null;
                case "GetEvents":     return s.GetEvents();

                // ── C ──
                case "GetEntries":    return s.GetEntries(MeetRpc.Arg<long>(a, "eventId"));
                case "SaveEntry":     return s.SaveEntry(MeetRpc.Arg<EntryDto>(a, "e"), MeetRpc.Arg<string>(a, "op"));
                case "DeleteEntry":   s.DeleteEntry(MeetRpc.Arg<long>(a, "id"), MeetRpc.Arg<string>(a, "op")); return null;
                case "SearchAthletes":
                    return s.SearchAthletes(MeetRpc.Arg<string>(a, "keyword"),
                        MeetRpc.Arg<long?>(a, "unitId"), MeetRpc.Arg<int>(a, "limit", 200));
                case "SaveAthlete":   return s.SaveAthlete(MeetRpc.Arg<AthleteDto>(a, "a"), MeetRpc.Arg<string>(a, "op"));
                case "ImportEntries":
                    return s.ImportEntries(MeetRpc.Arg<List<EntryDto>>(a, "list") ?? new List<EntryDto>(),
                        MeetRpc.Arg<string>(a, "op"));

                // ── D ──
                case "GetHeatList":   return s.GetHeatList(MeetRpc.Arg<long>(a, "roundId"));
                case "GetHeat":       return s.GetHeat(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<int>(a, "heat"));
                case "SaveHeatEntry": s.SaveHeatEntry(MeetRpc.Arg<LaneRow>(a, "row"), MeetRpc.Arg<string>(a, "op")); return null;
                case "AssignLanes":
                    s.AssignLanes(MeetRpc.Arg<long>(a, "roundId"),
                        MeetRpc.Arg<List<LaneRow>>(a, "rows") ?? new List<LaneRow>(),
                        MeetRpc.Arg<string>(a, "op")); return null;
                case "MoveEntry":
                    s.MoveEntry(MeetRpc.Arg<long>(a, "heatEntryId"), MeetRpc.Arg<int>(a, "newHeat"),
                        MeetRpc.Arg<int>(a, "newLane"), MeetRpc.Arg<string>(a, "op")); return null;
                case "MergeHeats":
                    s.MergeHeats(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<int>(a, "fromHeat"),
                        MeetRpc.Arg<int>(a, "intoHeat"),
                        MeetRpc.Arg<Dictionary<long, int>>(a, "laneMap") ?? new Dictionary<long, int>(),
                        MeetRpc.Arg<string>(a, "op")); return null;
                case "CancelHeat":
                    s.CancelHeat(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<int>(a, "heat"),
                        MeetRpc.Arg<string>(a, "reason"), MeetRpc.Arg<string>(a, "op")); return null;
                case "SetReserves":
                    s.SetReserves(MeetRpc.Arg<long>(a, "roundId"),
                        MeetRpc.Arg<List<long>>(a, "entryIds") ?? new List<long>(),
                        MeetRpc.Arg<string>(a, "op")); return null;

                // ── E ──
                case "GetRelayLegs":  return s.GetRelayLegs(MeetRpc.Arg<long>(a, "heatEntryId"));
                case "SaveRelayLegs":
                    s.SaveRelayLegs(MeetRpc.Arg<long>(a, "heatEntryId"),
                        MeetRpc.Arg<List<RelayLegDto>>(a, "legs") ?? new List<RelayLegDto>(),
                        MeetRpc.Arg<string>(a, "op")); return null;

                // ── F ──
                case "GetCheckinList": return s.GetCheckinList(MeetRpc.Arg<long>(a, "roundId"));
                case "SetCheckin":
                    s.SetCheckin(MeetRpc.Arg<long>(a, "heatEntryId"), MeetRpc.Arg<string>(a, "status"),
                        MeetRpc.Arg<string>(a, "op")); return null;

                // ── G. 只有这三个走网络; 当前组库的高频写在客户端本机 ──
                case "OpenHeat":
                    return s.OpenHeat(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<int>(a, "heat"), MeetRpc.Arg<string>(a, "op"));
                case "CommitHeat":    return s.CommitHeat(MeetRpc.Arg<string>(a, "op"));
                case "CommitHeatFrom":
                    return s.CommitHeatFrom(MeetRpc.Arg<LiveHeat>(a, "live"), MeetRpc.Arg<string>(a, "op"));
                case "DiscardHeat":   s.DiscardHeat(MeetRpc.Arg<string>(a, "op")); return null;

                // ── H ──
                case "GetResults":    return s.GetResults(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<long?>(a, "eventId"));
                case "RecomputeRanks":
                    s.RecomputeRanks(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<long>(a, "eventId"),
                        MeetRpc.Arg<string>(a, "op")); return null;
                case "SaveResult":
                    s.SaveResult(MeetRpc.Arg<LaneRow>(a, "row"), MeetRpc.Arg<string>(a, "reason"),
                        MeetRpc.Arg<string>(a, "op")); return null;
                case "GetPromotionList":
                    return s.GetPromotionList(MeetRpc.Arg<long>(a, "eventId"),
                        MeetRpc.Arg<string>(a, "fromStage"), MeetRpc.Arg<string>(a, "toStage"));
                case "PromoteToNextRound":
                    s.PromoteToNextRound(MeetRpc.Arg<long>(a, "eventId"), MeetRpc.Arg<string>(a, "fromStage"),
                        MeetRpc.Arg<string>(a, "toStage"),
                        MeetRpc.Arg<List<long>>(a, "heatEntryIds") ?? new List<long>(),
                        MeetRpc.Arg<string>(a, "op")); return null;

                // ── I ──
                case "GetRecords":
                    return s.GetRecords(MeetRpc.Arg<string>(a, "ageGroup"), MeetRpc.Arg<string>(a, "gender"),
                        MeetRpc.Arg<int>(a, "distance"), MeetRpc.Arg<string>(a, "stroke"),
                        MeetRpc.Arg<int>(a, "relayLegs", 1));
                case "CheckRecordBreak": return s.CheckRecordBreak(MeetRpc.Arg<long>(a, "heatEntryId"));
                case "ApplyRecordBreak":
                    s.ApplyRecordBreak(MeetRpc.Arg<long>(a, "heatEntryId"),
                        MeetRpc.Arg<List<RecordBreak>>(a, "breaks") ?? new List<RecordBreak>(),
                        MeetRpc.Arg<string>(a, "op")); return null;
                case "SaveRecord":    return s.SaveRecord(MeetRpc.Arg<RecordDto>(a, "r"), MeetRpc.Arg<string>(a, "op"));
                case "GetTeamScores": return s.GetTeamScores();
                case "RecomputeTeamScores": s.RecomputeTeamScores(MeetRpc.Arg<string>(a, "op")); return null;

                // ── J ──
                case "GetStartList":  return s.GetStartList(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<long?>(a, "eventId"));
                case "GetResultSheet": return s.GetResultSheet(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<long?>(a, "eventId"));
                case "GetSummary":    return s.GetSummary(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<long>(a, "eventId"));
                case "GetRankingBulletin": return s.GetRankingBulletin(MeetRpc.Arg<long>(a, "eventId"));
                case "GetEventRankRows": return s.GetEventRankRows(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<long>(a, "eventId"));
                case "GetAllEventRankings": return s.GetAllEventRankings();
                case "ListHeatStamps": return s.ListHeatStamps();
                case "ListConfirmedHeats": return s.ListConfirmedHeats();
                case "GetRacingHeats": return s.GetRacingHeats();
                case "GenerateEventRankingIfComplete":
                    return s.GenerateEventRankingIfComplete(MeetRpc.Arg<long>(a, "roundId"), MeetRpc.Arg<long>(a, "eventId"),
                        MeetRpc.Arg<string>(a, "ageGroup"), MeetRpc.Arg<string>(a, "gender"),
                        MeetRpc.Arg<string>(a, "eventName"), MeetRpc.Arg<string>(a, "stage"), MeetRpc.Arg<string>(a, "op"));

                // ── K ──
                case "Backup":        s.Backup(MeetRpc.Arg<string>(a, "targetPath")); return null;
                case "GetAuditLog":
                    return s.GetAuditLog(MeetRpc.Arg<string>(a, "action"), MeetRpc.Arg<string>(a, "since"),
                        MeetRpc.Arg<int>(a, "limit", 500));
                case "LogAudit":
                    s.LogAudit(MeetRpc.Arg<string>(a, "action"), MeetRpc.Arg<string>(a, "target"),
                        MeetRpc.Arg<string>(a, "oldValue"), MeetRpc.Arg<string>(a, "newValue"),
                        MeetRpc.Arg<string>(a, "note"), MeetRpc.Arg<string>(a, "op"));
                    return null;

                default:
                    throw new MeetDataException("不认识的操作: " + op);
            }
        }
    }
}
