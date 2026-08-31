using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // IMeetService 的本地实现：直接开 meet.db + current_heat.db   2026-08-24
    //
    // 单机比赛用这个；联网比赛主服务器上也跑这个，网络实现只是把请求
    // 转过来调它。所以业务规则只写这一份，两条路不可能算出两个结果。
    //
    // 每个方法只碰它该碰的行 —— 没有 LoadAll / SaveAll，一处都没有。
    // ══════════════════════════════════════════════════════════════════════
    public class LocalMeetService : IMeetService
    {
        private readonly MeetDb _db;      // 竞赛管理库
        private readonly MeetDb _live;    // 当前组库（单独文件，比赛中只写它）
        private readonly string _liveDir;

        public MeetDb Db { get { return _db; } }

        public LocalMeetService(string meetDbPath)
        {
            _db = new MeetDb(meetDbPath);
            _liveDir = Path.GetDirectoryName(meetDbPath) ?? ".";
            _live = new MeetDb(Path.Combine(_liveDir, "current_heat.db"), LiveHeatSchema.Sql);
        }

        public void Dispose() { _db.Dispose(); _live.Dispose(); }

        // ═══════════════ 小工具 ═══════════════
        private static string S(DataRow r, string c)
        { return r.Table.Columns.Contains(c) && r[c] != DBNull.Value ? Convert.ToString(r[c]) : null; }
        private static int I(DataRow r, string c)
        { return r.Table.Columns.Contains(c) && r[c] != DBNull.Value ? Convert.ToInt32(r[c]) : 0; }
        private static int? NI(DataRow r, string c)
        { return r.Table.Columns.Contains(c) && r[c] != DBNull.Value ? (int?)Convert.ToInt32(r[c]) : null; }
        private static long L(DataRow r, string c)
        { return r.Table.Columns.Contains(c) && r[c] != DBNull.Value ? Convert.ToInt64(r[c]) : 0L; }
        private static long? NL(DataRow r, string c)
        { return r.Table.Columns.Contains(c) && r[c] != DBNull.Value ? (long?)Convert.ToInt64(r[c]) : null; }
        private static double D(DataRow r, string c)
        { return r.Table.Columns.Contains(c) && r[c] != DBNull.Value ? Convert.ToDouble(r[c]) : 0d; }
        private static bool B(DataRow r, string c) { return I(r, c) != 0; }

        private static string Now() { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); }

        private void Audit(string op, string action, string target, long? targetId,
                           string oldV, string newV, string note)
        {
            _db.ExecuteNonQuery(
                "INSERT INTO audit_log(at,operator,action,target,target_id,old_value,new_value,note) " +
                "VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                Now(), op ?? Environment.MachineName, action, target, targetId, oldV, newV, note);
        }

        /// <summary>撞上正在比赛的那一组就直接拒绝，不排队。</summary>
        private void EnsureNotRacing(long roundId, int? heat)
        {
            if (heat == null) return;
            object o = _db.ExecuteScalar("SELECT state FROM heats WHERE round_id=@p1 AND heat=@p2", roundId, heat.Value);
            if (o != null && o != DBNull.Value && Convert.ToString(o) == "racing")
                throw new HeatLockedException(roundId, heat.Value);
        }

        private void EnsureHeatRow(long roundId, int heat)
        {
            _db.ExecuteNonQuery("INSERT OR IGNORE INTO heats(round_id,heat) VALUES(@p1,@p2)", roundId, heat);
        }

        // ═══════════════ A. 赛事 / 基础资料 ═══════════════
        public MeetInfo GetMeetInfo()
        {
            var t = _db.Query("SELECT * FROM competition WHERE id=1");
            if (t.Rows.Count == 0) return new MeetInfo();
            var r = t.Rows[0];
            return new MeetInfo {
                Name = S(r,"name"), NameEn = S(r,"name_en"), Mode = S(r,"mode"), Rule = S(r,"rule"),
                UseAgeGroup = B(r,"use_age_group"),
                StartDate = S(r,"start_date"), EndDate = S(r,"end_date"),
                Location = S(r,"location"), City = S(r,"city"),
                PoolLength = I(r,"pool_length"), LaneCount = I(r,"lane_count"),
                LaneNumbers = S(r,"lane_numbers"), StartPosition = S(r,"start_position"),
                Organizer = S(r,"organizer"), Host = S(r,"host"),
                TechnicalDelegate = S(r,"technical_delegate"), Referee = S(r,"referee"),
                Starter = S(r,"starter"), Arbiter = S(r,"arbiter"), ChiefJudge = S(r,"chief_judge"),
                DisplayRecordLabel = S(r,"display_record_label"),
                DisplayRecordTypeName = S(r,"display_record_type_name") };
        }

        public void SaveMeetInfo(MeetInfo m, string op)
        {
            _db.ExecuteNonQuery(
                @"INSERT INTO competition(id,name,name_en,mode,rule,use_age_group,start_date,end_date,location,city,
                      pool_length,lane_count,lane_numbers,start_position,organizer,host,technical_delegate,referee,
                      starter,arbiter,chief_judge,display_record_label,display_record_type_name,schema_version)
                  VALUES(1,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17,@p18,@p19,@p20,@p21,@p22,@p23)
                  ON CONFLICT(id) DO UPDATE SET name=@p1,name_en=@p2,mode=@p3,rule=@p4,use_age_group=@p5,
                      start_date=@p6,end_date=@p7,location=@p8,city=@p9,pool_length=@p10,lane_count=@p11,
                      lane_numbers=@p12,start_position=@p13,organizer=@p14,host=@p15,technical_delegate=@p16,
                      referee=@p17,starter=@p18,arbiter=@p19,chief_judge=@p20,display_record_label=@p21,
                      display_record_type_name=@p22",
                m.Name, m.NameEn, m.Mode, m.Rule, m.UseAgeGroup ? 1 : 0, m.StartDate, m.EndDate, m.Location, m.City,
                m.PoolLength, m.LaneCount, m.LaneNumbers, m.StartPosition, m.Organizer, m.Host,
                m.TechnicalDelegate, m.Referee, m.Starter, m.Arbiter, m.ChiefJudge,
                m.DisplayRecordLabel, m.DisplayRecordTypeName, MeetSchema.Version);
            Audit(op, "改赛事信息", "competition", 1, null, m.Name, null);
        }

        public string GetSetting(string key)
        {
            object o = _db.ExecuteScalar("SELECT value FROM settings WHERE key=@p1", key);
            return (o == null || o == DBNull.Value) ? null : Convert.ToString(o);
        }

        public void SaveSetting(string key, string json, string op)
        {
            string old = GetSetting(key);
            _db.ExecuteNonQuery("INSERT OR REPLACE INTO settings(key,value,at) VALUES(@p1,@p2,@p3)", key, json, Now());
            Audit(op, "改配置", "settings", null, Trunc(old), Trunc(json), key);
        }
        private static string Trunc(string s)
        { return s == null ? null : (s.Length > 500 ? s.Substring(0, 500) + "…" : s); }

        public List<UnitDto> GetUnits()
        {
            var list = new List<UnitDto>();
            foreach (DataRow r in _db.Query("SELECT * FROM units ORDER BY name").Rows) list.Add(ReadUnit(r));
            return list;
        }
        private static UnitDto ReadUnit(DataRow r)
        {
            return new UnitDto {
                Id = L(r,"id"), Name = S(r,"name"), ShortName = S(r,"short_name"), FullName = S(r,"full_name"),
                Leader = S(r,"leader"), Coach = S(r,"coach"), Doctor = S(r,"doctor"), Phone = S(r,"phone"),
                Address = S(r,"address"), BibRanges = S(r,"bib_ranges"), Note = S(r,"note"),
                BasePoints = D(r,"base_points"), Score = D(r,"score"),
                IndividualPoints = D(r,"individual_points"), RelayPoints = D(r,"relay_points"),
                RecordBonus = D(r,"record_bonus"), Gold = I(r,"gold"), Silver = I(r,"silver"),
                Bronze = I(r,"bronze"), ScoreRank = I(r,"score_rank") };
        }

        public long SaveUnit(UnitDto u, string op)
        {
            if (u.Id > 0)
            {
                _db.ExecuteNonQuery(
                    @"UPDATE units SET name=@p2,short_name=@p3,full_name=@p4,leader=@p5,coach=@p6,doctor=@p7,
                          phone=@p8,address=@p9,bib_ranges=@p10,base_points=@p11,note=@p12 WHERE id=@p1",
                    u.Id, u.Name, u.ShortName, u.FullName, u.Leader, u.Coach, u.Doctor,
                    u.Phone, u.Address, u.BibRanges, u.BasePoints, u.Note);
                Audit(op, "改单位", "units", u.Id, null, u.Name, null);
                return u.Id;
            }
            long id = _db.ExecuteInsert(
                @"INSERT INTO units(name,short_name,full_name,leader,coach,doctor,phone,address,bib_ranges,base_points,note)
                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11)",
                u.Name, u.ShortName, u.FullName, u.Leader, u.Coach, u.Doctor,
                u.Phone, u.Address, u.BibRanges, u.BasePoints, u.Note);
            Audit(op, "建单位", "units", id, null, u.Name, null);
            return id;
        }

        public void DeleteUnit(long id, string op)
        {
            object n = _db.ExecuteScalar("SELECT COUNT(*) FROM athletes WHERE unit_id=@p1", id);
            if (Convert.ToInt32(n) > 0) throw new MeetDataException("该单位下还有运动员，不能删除");
            _db.ExecuteNonQuery("DELETE FROM units WHERE id=@p1", id);
            Audit(op, "删单位", "units", id, null, null, null);
        }

        public List<StaffDto> GetStaff(bool officialsOnly)
        {
            string sql = "SELECT * FROM staff " + (officialsOnly ? "WHERE is_official=1 " : "") + "ORDER BY ord,id";
            var list = new List<StaffDto>();
            foreach (DataRow r in _db.Query(sql).Rows)
                list.Add(new StaffDto {
                    Id = L(r,"id"), Ord = I(r,"ord"), Name = S(r,"name"), Title = S(r,"title"),
                    Group = S(r,"group_name"), Gender = S(r,"gender"), RefereeLevel = S(r,"referee_level"),
                    Country = S(r,"country"), Phone = S(r,"phone"), IsOfficial = B(r,"is_official"), Note = S(r,"note") });
            return list;
        }

        public long SaveStaff(StaffDto s, string op)
        {
            if (s.Id > 0)
            {
                _db.ExecuteNonQuery(
                    @"UPDATE staff SET ord=@p2,name=@p3,title=@p4,group_name=@p5,gender=@p6,referee_level=@p7,
                          country=@p8,phone=@p9,is_official=@p10,note=@p11 WHERE id=@p1",
                    s.Id, s.Ord, s.Name, s.Title, s.Group, s.Gender, s.RefereeLevel,
                    s.Country, s.Phone, s.IsOfficial ? 1 : 0, s.Note);
                Audit(op, "改工作人员", "staff", s.Id, null, s.Name, null);
                return s.Id;
            }
            long id = _db.ExecuteInsert(
                @"INSERT INTO staff(ord,name,title,group_name,gender,referee_level,country,phone,is_official,note)
                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10)",
                s.Ord, s.Name, s.Title, s.Group, s.Gender, s.RefereeLevel,
                s.Country, s.Phone, s.IsOfficial ? 1 : 0, s.Note);
            Audit(op, "建工作人员", "staff", id, null, s.Name, null);
            return id;
        }

        public void DeleteStaff(long id, string op)
        {
            _db.ExecuteNonQuery("DELETE FROM staff WHERE id=@p1", id);
            Audit(op, "删工作人员", "staff", id, null, null, null);
        }

        // ═══════════════ B. 日程 / 项目 / 赛次 ═══════════════
        public List<ScheduleRow> GetSchedule()
        {
            var list = new List<ScheduleRow>();
            foreach (DataRow r in _db.Query(
                "SELECT * FROM v_schedule ORDER BY 场次序, 场内序, 项目号").Rows)
                list.Add(new ScheduleRow {
                    RoundId = L(r,"round_id"), EventId = L(r,"event_id"), SessionId = NL(r,"session_id"),
                    SessionNo = I(r,"场次序"), Date = S(r,"日期"), SessionName = S(r,"场次"), Time = S(r,"开始时间"),
                    EvNum = I(r,"项目号"), AgeGroup = S(r,"组别"), Gender = S(r,"性别"), EventName = S(r,"项目"),
                    Distance = I(r,"距离"), Stroke = S(r,"泳姿"), RelayLegs = I(r,"棒数"),
                    Stage = S(r,"赛次"), HeatCount = I(r,"组数"), IsCeremony = B(r,"是颁奖"),
                    Status = S(r,"状态"), RoundTitle = S(r,"赛次标题"), PromoteCount = I(r,"晋级人数") });
            return list;
        }

        public RoundDto GetRound(long roundId)
        {
            var t = _db.Query("SELECT * FROM rounds WHERE id=@p1", roundId);
            if (t.Rows.Count == 0) return null;
            var r = t.Rows[0];
            var d = new RoundDto {
                Id = L(r,"id"), SessionId = NL(r,"session_id"), Stage = S(r,"stage"),
                Distance = I(r,"distance"), Stroke = S(r,"stroke"), RelayLegs = I(r,"relay_legs"),
                Ord = I(r,"ord"), Time = S(r,"time"), HeatCount = I(r,"heat_count"),
                LaneCount = NI(r,"lane_count"), IsCeremony = B(r,"is_ceremony"),
                Status = S(r,"status"), Title = S(r,"title"), Note = S(r,"note") };
            foreach (DataRow x in _db.Query(
                @"SELECT re.*, e.age_group, e.gender, e.event_name FROM round_events re
                  JOIN events e ON e.id=re.event_id WHERE re.round_id=@p1 ORDER BY re.ord", roundId).Rows)
                d.Events.Add(new RoundEventDto {
                    EventId = L(x,"event_id"), Ord = I(x,"ord"),
                    PromoteCount = I(x,"promote_count"), ReserveCount = I(x,"reserve_count"),
                    AgeGroup = S(x,"age_group"), Gender = S(x,"gender"), EventName = S(x,"event_name") });
            return d;
        }

        public long SaveSession(SessionDto s, string op)
        {
            if (s.Id > 0)
            {
                _db.ExecuteNonQuery("UPDATE sessions SET no=@p2,date=@p3,name=@p4,start_time=@p5,note=@p6 WHERE id=@p1",
                    s.Id, s.No, s.Date, s.Name, s.StartTime, s.Note);
                Audit(op, "改场次", "sessions", s.Id, null, s.Date + " " + s.Name, null);
                return s.Id;
            }
            long id = _db.ExecuteInsert("INSERT INTO sessions(no,date,name,start_time,note) VALUES(@p1,@p2,@p3,@p4,@p5)",
                s.No, s.Date, s.Name, s.StartTime, s.Note);
            Audit(op, "建场次", "sessions", id, null, s.Date + " " + s.Name, null);
            return id;
        }

        public long SaveEvent(EventDto e, string op)
        {
            if (e.Id > 0)
            {
                _db.ExecuteNonQuery(
                    @"UPDATE events SET ev_num=@p2,age_group=@p3,gender=@p4,distance=@p5,stroke=@p6,
                          relay_legs=@p7,event_name=@p8,award_places=@p9,note=@p10 WHERE id=@p1",
                    e.Id, e.EvNum, e.AgeGroup ?? "", e.Gender, e.Distance, e.Stroke,
                    e.RelayLegs, e.EventName, e.AwardPlaces, e.Note);
                Audit(op, "改项目", "events", e.Id, null, e.EventName, null);
                return e.Id;
            }
            long id = _db.ExecuteInsert(
                @"INSERT INTO events(ev_num,age_group,gender,distance,stroke,relay_legs,event_name,award_places,note)
                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                e.EvNum, e.AgeGroup ?? "", e.Gender, e.Distance, e.Stroke,
                e.RelayLegs, e.EventName, e.AwardPlaces, e.Note);
            Audit(op, "建项目", "events", id, null, e.EventName, null);
            return id;
        }

        public List<EventDto> GetEvents()
        {
            var list = new List<EventDto>();
            foreach (DataRow r in _db.Query("SELECT * FROM events ORDER BY ev_num,id").Rows)
                list.Add(new EventDto {
                    Id = L(r,"id"), EvNum = I(r,"ev_num"), AgeGroup = S(r,"age_group") ?? "",
                    Gender = S(r,"gender"), Distance = I(r,"distance"), Stroke = S(r,"stroke"),
                    RelayLegs = I(r,"relay_legs"), EventName = S(r,"event_name"),
                    AwardPlaces = I(r,"award_places"), Note = S(r,"note") });
            return list;
        }

        public long SaveRound(RoundDto d, string op)
        {
            if (d.Id > 0)
            {
                _db.ExecuteNonQuery(
                    @"UPDATE rounds SET session_id=@p2,stage=@p3,distance=@p4,stroke=@p5,relay_legs=@p6,
                          ord=@p7,time=@p8,heat_count=@p9,lane_count=@p10,is_ceremony=@p11,status=@p12,
                          title=@p13,note=@p14 WHERE id=@p1",
                    d.Id, d.SessionId, d.Stage, d.Distance, d.Stroke, d.RelayLegs, d.Ord, d.Time,
                    d.HeatCount, d.LaneCount, d.IsCeremony ? 1 : 0, d.Status, d.Title, d.Note);
                Audit(op, "改赛次", "rounds", d.Id, null, d.Stage, null);
                return d.Id;
            }
            long id = _db.ExecuteInsert(
                @"INSERT INTO rounds(session_id,stage,distance,stroke,relay_legs,ord,time,heat_count,
                      lane_count,is_ceremony,status,title,note)
                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13)",
                d.SessionId, d.Stage, d.Distance, d.Stroke, d.RelayLegs, d.Ord, d.Time,
                d.HeatCount, d.LaneCount, d.IsCeremony ? 1 : 0, d.Status, d.Title, d.Note);
            Audit(op, "建赛次", "rounds", id, null, d.Stage, null);
            if (d.Events != null && d.Events.Count > 0) SetRoundEvents(id, d.Events, op);
            return id;
        }

        public void SetRoundEvents(long roundId, List<RoundEventDto> events, string op)
        {
            // 已经排过组的项目不能随手摘掉，否则那些人就成了无主的泳道
            var keep = new HashSet<long>(events.Select(x => x.EventId));
            foreach (DataRow r in _db.Query(
                @"SELECT DISTINCT en.event_id FROM heat_entries he JOIN entries en ON en.id=he.entry_id
                  WHERE he.round_id=@p1", roundId).Rows)
            {
                long eid = L(r, "event_id");
                if (!keep.Contains(eid))
                    throw new MeetDataException("项目 " + eid + " 在本赛次已排了分组名单，不能从本赛次移除；请先清掉它的分组");
            }
            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                run("DELETE FROM round_events WHERE round_id=@p1", new object[] { roundId });
                int i = 0;
                foreach (var e in events)
                {
                    run(@"INSERT INTO round_events(round_id,event_id,ord,promote_count,reserve_count)
                          VALUES(@p1,@p2,@p3,@p4,@p5)",
                        new object[] { roundId, e.EventId, e.Ord > 0 ? e.Ord : i, e.PromoteCount, e.ReserveCount });
                    i++;
                }
            });
            Audit(op, "设赛次项目", "rounds", roundId, null,
                string.Join(",", events.Select(x => x.EventId.ToString()).ToArray()),
                events.Count > 1 ? "混编 " + events.Count + " 个项目" : null);
        }

        // ═══════════════ C. 报名 ═══════════════
        private const string EntrySql =
            @"SELECT en.*, a.name AS ath_name, COALESCE(u1.name,u2.name) AS unit_name
              FROM entries en
              LEFT JOIN athletes a ON a.id = en.athlete_id
              LEFT JOIN units u1 ON u1.id = en.unit_id
              LEFT JOIN units u2 ON u2.id = a.unit_id ";

        private static EntryDto ReadEntry(DataRow r)
        {
            return new EntryDto {
                Id = L(r,"id"), EventId = L(r,"event_id"), AthleteId = NL(r,"athlete_id"),
                UnitId = NL(r,"unit_id"), TeamName = S(r,"team_name"), BibNumber = S(r,"bib_number"),
                EntryTime = S(r,"entry_time"), EntryTimeSeconds = D(r,"entry_time_seconds"),
                Coach = S(r,"coach"), IsQualified = B(r,"is_qualified"), Status = S(r,"status"),
                Note = S(r,"note"), AthleteName = S(r,"ath_name"), UnitName = S(r,"unit_name") };
        }

        public List<EntryDto> GetEntries(long eventId)
        {
            var list = new List<EntryDto>();
            foreach (DataRow r in _db.Query(EntrySql +
                "WHERE en.event_id=@p1 ORDER BY en.entry_time_seconds=0, en.entry_time_seconds", eventId).Rows)
                list.Add(ReadEntry(r));
            return list;
        }

        public long SaveEntry(EntryDto e, string op)
        {
            if (e.AthleteId == null && string.IsNullOrEmpty(e.TeamName))
                throw new MeetDataException("报名必须是个人（运动员）或接力队之一");
            if (e.Id > 0)
            {
                _db.ExecuteNonQuery(
                    @"UPDATE entries SET event_id=@p2,athlete_id=@p3,unit_id=@p4,team_name=@p5,bib_number=@p6,
                          entry_time_seconds=@p7,entry_time=@p8,coach=@p9,is_qualified=@p10,status=@p11,note=@p12
                      WHERE id=@p1",
                    e.Id, e.EventId, e.AthleteId, e.UnitId, e.TeamName, e.BibNumber,
                    e.EntryTimeSeconds, e.EntryTime, e.Coach, e.IsQualified ? 1 : 0, e.Status, e.Note);
                Audit(op, "改报名", "entries", e.Id, null, e.DisplayName, null);
                return e.Id;
            }
            long id = _db.ExecuteInsert(
                @"INSERT INTO entries(event_id,athlete_id,unit_id,team_name,bib_number,entry_time_seconds,
                      entry_time,coach,is_qualified,status,note)
                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11)",
                e.EventId, e.AthleteId, e.UnitId, e.TeamName, e.BibNumber, e.EntryTimeSeconds,
                e.EntryTime, e.Coach, e.IsQualified ? 1 : 0, e.Status, e.Note);
            Audit(op, "建报名", "entries", id, null, e.DisplayName, null);
            return id;
        }

        public void DeleteEntry(long id, string op)
        {
            var t = _db.Query(
                @"SELECT he.round_id, he.heat FROM heat_entries he WHERE he.entry_id=@p1 AND he.heat IS NOT NULL", id);
            foreach (DataRow r in t.Rows) EnsureNotRacing(L(r, "round_id"), NI(r, "heat"));
            _db.ExecuteNonQuery("DELETE FROM entries WHERE id=@p1", id);
            Audit(op, "删报名", "entries", id, null, null, null);
        }

        public List<AthleteDto> SearchAthletes(string keyword, long? unitId, int limit)
        {
            string sql = @"SELECT a.*, u.name AS unit_name FROM athletes a
                           LEFT JOIN units u ON u.id=a.unit_id WHERE 1=1 ";
            var ps = new List<object>();
            if (!string.IsNullOrWhiteSpace(keyword))
            { sql += "AND (a.name LIKE @p" + (ps.Count+1) + " OR a.bib_number LIKE @p" + (ps.Count+1) + ") "; ps.Add("%" + keyword.Trim() + "%"); }
            if (unitId != null) { sql += "AND a.unit_id=@p" + (ps.Count+1) + " "; ps.Add(unitId.Value); }
            sql += "ORDER BY a.name LIMIT " + (limit > 0 ? limit : 200);
            var list = new List<AthleteDto>();
            foreach (DataRow r in _db.Query(sql, ps.ToArray()).Rows) list.Add(ReadAthlete(r));
            return list;
        }
        private static AthleteDto ReadAthlete(DataRow r)
        {
            return new AthleteDto {
                Id = L(r,"id"), UnitId = NL(r,"unit_id"), BibNumber = S(r,"bib_number"), Name = S(r,"name"),
                NameEn = S(r,"name_en"), Gender = S(r,"gender"), BirthDate = S(r,"birth_date"),
                IdNumber = S(r,"id_number"), JointUnit = S(r,"joint_unit"), Coach = S(r,"coach"),
                Phone = S(r,"phone"), CsaNumber = S(r,"csa_number"), FinaNumber = S(r,"fina_number"),
                HealthCertDate = S(r,"health_cert_date"), AgeCategory = S(r,"age_category"),
                Note = S(r,"note"), UnitName = S(r,"unit_name") };
        }

        public long SaveAthlete(AthleteDto a, string op)
        {
            if (a.Id > 0)
            {
                _db.ExecuteNonQuery(
                    @"UPDATE athletes SET bib_number=@p2,name=@p3,name_en=@p4,gender=@p5,birth_date=@p6,
                          id_number=@p7,unit_id=@p8,joint_unit=@p9,coach=@p10,phone=@p11,csa_number=@p12,
                          fina_number=@p13,health_cert_date=@p14,age_category=@p15,note=@p16 WHERE id=@p1",
                    a.Id, a.BibNumber, a.Name, a.NameEn, a.Gender, a.BirthDate, a.IdNumber, a.UnitId,
                    a.JointUnit, a.Coach, a.Phone, a.CsaNumber, a.FinaNumber, a.HealthCertDate,
                    a.AgeCategory, a.Note);
                Audit(op, "改运动员", "athletes", a.Id, null, a.Name, null);
                return a.Id;
            }
            long id = _db.ExecuteInsert(
                @"INSERT INTO athletes(bib_number,name,name_en,gender,birth_date,id_number,unit_id,joint_unit,
                      coach,phone,csa_number,fina_number,health_cert_date,age_category,note)
                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15)",
                a.BibNumber, a.Name, a.NameEn, a.Gender, a.BirthDate, a.IdNumber, a.UnitId, a.JointUnit,
                a.Coach, a.Phone, a.CsaNumber, a.FinaNumber, a.HealthCertDate, a.AgeCategory, a.Note);
            Audit(op, "建运动员", "athletes", id, null, a.Name, null);
            return id;
        }

        public List<string> ImportEntries(List<EntryDto> list, string op)
        {
            var res = new List<string>();
            foreach (var e in list)
            {
                try { SaveEntry(e, op); res.Add("OK"); }
                catch (Exception ex) { res.Add((e.DisplayName ?? "?") + "：" + ex.Message.Split('\n')[0]); }
            }
            Audit(op, "批量导报名", "entries", null, null, list.Count.ToString(),
                res.Count(x => x != "OK") + " 条失败");
            return res;
        }

        // ═══════════════ D. 分组 ═══════════════
        public List<HeatSummary> GetHeatList(long roundId)
        {
            var list = new List<HeatSummary>();
            foreach (DataRow r in _db.Query(
                @"SELECT h.*, (SELECT COUNT(*) FROM heat_entries he
                               WHERE he.round_id=h.round_id AND he.heat=h.heat) AS n
                  FROM heats h WHERE h.round_id=@p1 ORDER BY h.heat", roundId).Rows)
                list.Add(new HeatSummary {
                    RoundId = L(r,"round_id"), Heat = I(r,"heat"), State = S(r,"state"),
                    MergedInto = NI(r,"merged_into"), CancelReason = S(r,"cancel_reason"),
                    GunTime = S(r,"gun_time"), StartedAt = S(r,"started_at"),
                    ConfirmedAt = S(r,"confirmed_at"), ConfirmedBy = S(r,"confirmed_by"),
                    Note = S(r,"note"), LaneCount = I(r,"n") });
            return list;
        }

        // v_startlist 已经把个人/接力、组别/性别/项目、单位/教练全拼好了
        private const string LaneSql =
            @"SELECT v.*, he.entry_id, he.seed_time_seconds, he.checkin_at, he.promoted_from, he.promoted_rank,
                     he.score, he.dsq_code, he.dsq_leg, he.timing_source, he.touchpad_time, he.start_block_time,
                     he.pb1_time, he.pb2_time, he.pb3_time, he.manual_left, he.manual_right,
                     he.result_at, he.note, he.dispute_note
              FROM v_startlist v JOIN heat_entries he ON he.id = v.heat_entry_id ";

        private static LaneRow ReadLane(DataRow r)
        {
            return new LaneRow {
                Id = L(r,"heat_entry_id"), RoundId = L(r,"round_id"), EntryId = L(r,"entry_id"),
                EventId = L(r,"event_id"), Heat = NI(r,"组次"), Lane = NI(r,"道次"), ReserveNo = NI(r,"替补号"),
                Stage = S(r,"赛次"), EvNum = I(r,"项目号"), AgeGroup = S(r,"组别"), Gender = S(r,"性别"),
                EventName = S(r,"项目"), Name = S(r,"姓名"), UnitName = S(r,"单位"),
                BirthDate = S(r,"出生日期"), JointUnit = S(r,"联合培养单位"), Coach = S(r,"教练员"),
                IsRelay = B(r,"是接力"),
                SeedTime = S(r,"报名成绩"), SeedTimeSeconds = D(r,"seed_time_seconds"),
                CheckinStatus = S(r,"检录"), CheckinAt = S(r,"checkin_at"),
                PromotedFrom = NL(r,"promoted_from"), PromotedRank = I(r,"promoted_rank"),
                FinalTime = D(r,"成绩"), Rank = I(r,"名次"), PromotionMark = S(r,"晋级"),
                Score = D(r,"score"), Status = S(r,"状态"), DsqCode = S(r,"dsq_code"), DsqLeg = I(r,"dsq_leg"),
                RecordNote = S(r,"破纪录"), TimingSource = S(r,"timing_source"),
                ReactionTime = D(r,"反应时"), TouchpadTime = D(r,"touchpad_time"),
                StartBlockTime = D(r,"start_block_time"), Pb1Time = D(r,"pb1_time"), Pb2Time = D(r,"pb2_time"),
                Pb3Time = D(r,"pb3_time"), ManualLeft = D(r,"manual_left"), ManualRight = D(r,"manual_right"),
                ResultAt = S(r,"result_at"), Note = S(r,"note"), DisputeNote = S(r,"dispute_note") };
        }

        public List<LaneRow> GetHeat(long roundId, int heat)
        {
            var list = new List<LaneRow>();
            foreach (DataRow r in _db.Query(LaneSql +
                "WHERE v.round_id=@p1 AND v.组次=@p2 ORDER BY v.道次", roundId, heat).Rows)
                list.Add(ReadLane(r));
            FillHeatRankAndGap(list);
            return list;
        }

        /// <summary>组内名次和成绩差不入库，读的时候现算 —— 名次只有一处算，各端不会算出两个结果。</summary>
        private static void FillHeatRankAndGap(List<LaneRow> rows)
        {
            foreach (var g in rows.Where(x => x.FinalTime > 0 && !IsNoTime(x.Status)).GroupBy(x => x.EventId))
            {
                var ordered = g.OrderBy(x => x.FinalTime).ToList();
                double best = ordered[0].FinalTime;
                // 2026-08-30 并列判定改用 ResultOrdering(全场唯一一份)。
                //   原来这里是 Math.Abs(差) > 1e-9, 而内存那套是【按 1/100 秒取整】比 ——
                //   两套规则会算出不同的并列结果, 同一组成绩在库里和在界面上名次不一样。
                //   成绩本来就是 1/100 精度的, 该以裁判口径(取整后相等即并列)为准。
                var hrRanks = ResultOrdering.ComputeRanks(ordered, x => x.FinalTime);
                for (int i = 0; i < ordered.Count; i++)
                {
                    ordered[i].HeatRank = hrRanks[i];
                    ordered[i].Gap = Math.Round(ordered[i].FinalTime - best, 2);
                }
                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].IsTie = hrRanks.Count(v => v == hrRanks[i]) > 1;
            }
        }
        private static bool IsNoTime(string status)
        {
            if (string.IsNullOrEmpty(status)) return false;
            return status == "DSQ" || status == "DNS" || status == "DNF" || status == "DQ";
        }

        public void SaveHeatEntry(LaneRow row, string op)
        {
            EnsureNotRacing(row.RoundId, row.Heat);
            if (row.Heat != null) EnsureHeatRow(row.RoundId, row.Heat.Value);
            _db.ExecuteNonQuery(
                @"UPDATE heat_entries SET heat=@p2,lane=@p3,reserve_no=@p4,seed_time_seconds=@p5,seed_time=@p6,
                      note=@p7 WHERE id=@p1",
                row.Id, row.Heat, row.Lane, row.ReserveNo, row.SeedTimeSeconds, row.SeedTime, row.Note);
            Audit(op, "改分组", "heat_entries", row.Id, null,
                "第" + row.Heat + "组" + row.Lane + "道", row.Name);
        }

        public void AssignLanes(long roundId, List<LaneRow> rows, string op)
        {
            foreach (DataRow r in _db.Query("SELECT heat FROM heats WHERE round_id=@p1 AND state='racing'", roundId).Rows)
                throw new HeatLockedException(roundId, I(r, "heat"));

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                run("DELETE FROM heat_entries WHERE round_id=@p1", new object[] { roundId });
                var heats = new HashSet<int>();
                foreach (var x in rows)
                {
                    if (x.Heat != null && !heats.Contains(x.Heat.Value))
                    {
                        run("INSERT OR IGNORE INTO heats(round_id,heat) VALUES(@p1,@p2)",
                            new object[] { roundId, x.Heat.Value });
                        heats.Add(x.Heat.Value);
                    }
                    run(@"INSERT INTO heat_entries(round_id,heat,lane,reserve_no,entry_id,seed_time_seconds,seed_time)
                          VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                        new object[] { roundId, x.Heat, x.Lane, x.ReserveNo, x.EntryId, x.SeedTimeSeconds, x.SeedTime });
                }
                run("UPDATE rounds SET heat_count=@p2 WHERE id=@p1", new object[] { roundId, heats.Count });
            });
            Audit(op, "排分组", "rounds", roundId, null, rows.Count + " 条", null);
        }

        public void MoveEntry(long heatEntryId, int newHeat, int newLane, string op)
        {
            var t = _db.Query("SELECT round_id,heat,lane FROM heat_entries WHERE id=@p1", heatEntryId);
            if (t.Rows.Count == 0) throw new MeetDataException("找不到这条分组记录");
            long rid = L(t.Rows[0], "round_id");
            EnsureNotRacing(rid, NI(t.Rows[0], "heat"));
            EnsureNotRacing(rid, newHeat);
            string old = "第" + S(t.Rows[0],"heat") + "组" + S(t.Rows[0],"lane") + "道";

            // 目标道被占就说清楚是谁占着，别把 SQLite 的 UNIQUE constraint failed 甩给用户
            var occ = _db.Query(
                @"SELECT v.姓名 FROM v_startlist v WHERE v.round_id=@p1 AND v.组次=@p2 AND v.道次=@p3
                        AND v.heat_entry_id<>@p4", rid, newHeat, newLane, heatEntryId);
            if (occ.Rows.Count > 0)
                throw new MeetDataException("第" + newHeat + "组" + newLane + "道已经有人（"
                    + S(occ.Rows[0], "姓名") + "），请先把他挪开");

            EnsureHeatRow(rid, newHeat);
            _db.ExecuteNonQuery("UPDATE heat_entries SET heat=@p2,lane=@p3,reserve_no=NULL WHERE id=@p1",
                heatEntryId, newHeat, newLane);
            Audit(op, "挪泳道", "heat_entries", heatEntryId, old, "第" + newHeat + "组" + newLane + "道", null);
        }

        public void MergeHeats(long roundId, int fromHeat, int intoHeat, Dictionary<long, int> laneMap, string op)
        {
            EnsureNotRacing(roundId, fromHeat);
            EnsureNotRacing(roundId, intoHeat);
            if (fromHeat == intoHeat) throw new MeetDataException("并组的两个组不能是同一组");
            EnsureHeatRow(roundId, intoHeat);

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                foreach (var kv in laneMap)
                    run("UPDATE heat_entries SET heat=@p2,lane=@p3 WHERE id=@p1 AND round_id=@p4",
                        new object[] { kv.Key, intoHeat, kv.Value, roundId });
                // 原组标取消并记并入哪组 —— 取消的组没有泳道行了，只能靠 heats 记着
                run(@"INSERT INTO heats(round_id,heat,state,merged_into,cancel_reason,cancelled_at,operator)
                      VALUES(@p1,@p2,'cancelled',@p3,@p4,@p5,@p6)
                      ON CONFLICT(round_id,heat) DO UPDATE SET state='cancelled',merged_into=@p3,
                          cancel_reason=@p4,cancelled_at=@p5,operator=@p6",
                    new object[] { roundId, fromHeat, intoHeat, "并入第" + intoHeat + "组", Now(), op });
            });
            Audit(op, "并组", "heats", roundId, "第" + fromHeat + "组",
                "并入第" + intoHeat + "组", laneMap.Count + " 人");
        }

        public void CancelHeat(long roundId, int heat, string reason, string op)
        {
            EnsureNotRacing(roundId, heat);
            _db.ExecuteNonQuery(
                @"INSERT INTO heats(round_id,heat,state,cancel_reason,cancelled_at,operator)
                  VALUES(@p1,@p2,'cancelled',@p3,@p4,@p5)
                  ON CONFLICT(round_id,heat) DO UPDATE SET state='cancelled',cancel_reason=@p3,
                      cancelled_at=@p4,operator=@p5",
                roundId, heat, reason, Now(), op);
            Audit(op, "取消组", "heats", roundId, null, "第" + heat + "组", reason);
        }

        public void SetReserves(long roundId, List<long> entryIds, string op)
        {
            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                run("UPDATE heat_entries SET reserve_no=NULL WHERE round_id=@p1 AND reserve_no IS NOT NULL",
                    new object[] { roundId });
                for (int i = 0; i < entryIds.Count; i++)
                    run(@"UPDATE heat_entries SET reserve_no=@p3, heat=NULL, lane=NULL
                          WHERE round_id=@p1 AND entry_id=@p2",
                        new object[] { roundId, entryIds[i], i + 1 });
            });
            Audit(op, "设替补", "rounds", roundId, null, "R1..R" + entryIds.Count, null);
        }

        // ═══════════════ E. 接力棒次 ═══════════════
        public List<RelayLegDto> GetRelayLegs(long heatEntryId)
        {
            var list = new List<RelayLegDto>();
            foreach (DataRow r in _db.Query(
                "SELECT * FROM relay_legs WHERE heat_entry_id=@p1 ORDER BY leg_order", heatEntryId).Rows)
                list.Add(new RelayLegDto {
                    LegOrder = I(r,"leg_order"), AthleteId = NL(r,"athlete_id"),
                    SwimmerName = S(r,"swimmer_name"), SwimmerBib = S(r,"swimmer_bib"),
                    SwimmerIdNo = S(r,"swimmer_id_no"), SwimmerBirth = S(r,"swimmer_birth"),
                    SwimmerGender = S(r,"swimmer_gender"), ReactionTime = D(r,"reaction_time"),
                    LegTime = D(r,"leg_time"), CumulativeTime = D(r,"cumulative_time"), RankAt = I(r,"rank_at") });
            return list;
        }

        public void SaveRelayLegs(long heatEntryId, List<RelayLegDto> legs, string op)
        {
            var t = _db.Query("SELECT round_id,heat FROM heat_entries WHERE id=@p1", heatEntryId);
            if (t.Rows.Count == 0) throw new MeetDataException("找不到这条分组记录");
            EnsureNotRacing(L(t.Rows[0], "round_id"), NI(t.Rows[0], "heat"));

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                run("DELETE FROM relay_legs WHERE heat_entry_id=@p1", new object[] { heatEntryId });
                foreach (var l in legs)
                {
                    if (l == null || string.IsNullOrWhiteSpace(l.SwimmerName)) continue;
                    run(@"INSERT INTO relay_legs(heat_entry_id,leg_order,athlete_id,swimmer_name,swimmer_bib,
                              swimmer_id_no,swimmer_birth,swimmer_gender,reaction_time,leg_time,cumulative_time,rank_at)
                          VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12)",
                        new object[] { heatEntryId, l.LegOrder, l.AthleteId, l.SwimmerName, l.SwimmerBib,
                            l.SwimmerIdNo, l.SwimmerBirth, l.SwimmerGender, l.ReactionTime,
                            l.LegTime, l.CumulativeTime, l.RankAt });
                }
            });
            Audit(op, "改接力棒次", "relay_legs", heatEntryId, null,
                string.Join("/", legs.Where(x => x != null).Select(x => x.SwimmerName).ToArray()), null);
        }

        // ═══════════════ F. 检录 ═══════════════
        public List<LaneRow> GetCheckinList(long roundId)
        {
            var list = new List<LaneRow>();
            foreach (DataRow r in _db.Query(LaneSql +
                "WHERE v.round_id=@p1 ORDER BY v.组次, v.道次, v.替补号", roundId).Rows)
                list.Add(ReadLane(r));
            return list;
        }

        public void SetCheckin(long heatEntryId, string status, string op)
        {
            var t = _db.Query("SELECT round_id,heat,checkin_status FROM heat_entries WHERE id=@p1", heatEntryId);
            if (t.Rows.Count == 0) throw new MeetDataException("找不到这条分组记录");
            EnsureNotRacing(L(t.Rows[0], "round_id"), NI(t.Rows[0], "heat"));
            string old = S(t.Rows[0], "checkin_status");
            _db.ExecuteNonQuery("UPDATE heat_entries SET checkin_status=@p2,checkin_at=@p3 WHERE id=@p1",
                heatEntryId, status, Now());
            Audit(op, "检录", "heat_entries", heatEntryId, old, status, null);
        }

        // ═══════════════ G. 比赛中（当前组库）═══════════════
        public LiveHeat OpenHeat(long roundId, int heat, string op)
        {
            // 2026-08-28 原来这里只说"已有一组在比赛中", 不说是哪一组, 也没有恢复的路 ——
            //   计时端崩一次 / 断电一次 / 测试中途失败, 那一组就永远停在 racing,
            //   整个赛次从此开不了组, 而且操作员根本不知道该去放弃哪一组。
            //   现在: ① 说清是第几组; ② 卡住的就是要开的这一组时, 当作【重开】放行
            //   (崩溃后重来是最常见的情况, 本组数据本来就要重新灌)。
            var cur = _db.Query("SELECT heat FROM heats WHERE round_id=@p1 AND state='racing'", roundId);
            if (cur.Rows.Count > 0)
            {
                int stuck = NI(cur.Rows[0], "heat") ?? 0;
                if (stuck != heat)
                    throw new MeetDataException(string.Format(
                        "第{0}组还在比赛中（上次可能没正常结束）。请先确认或放弃第{0}组，再开第{1}组。", stuck, heat));
                // 同一组重开: 把上次那条 racing 记录清掉, 下面照常重新灌名单
                _db.ExecuteNonQuery("UPDATE heats SET state='pending' WHERE round_id=@p1 AND heat=@p2 AND state='racing'",
                    roundId, heat);
            }

            var info = GetMeetInfo();
            var round = GetRound(roundId);
            if (round == null) throw new MeetDataException("找不到这个赛次");
            var lanes = GetHeat(roundId, heat);
            if (lanes.Count == 0) throw new MeetDataException("这一组没有分组名单");

            // 混编时本组可能坐着好几个项目的人，取第一个项目做本组的显示信息
            var first = lanes[0];
            int total = round.Distance * Math.Max(round.RelayLegs, 1);

            ClearLive();
            _live.InTransaction(delegate(Func<string, object[], int> run)
            {
                run(@"INSERT INTO live_heat(id,meet_round_id,meet_event_id,ev_num,age_group,gender,event_name,
                          distance,stroke,relay_legs,stage,heat,total_heats,lane_count,pool_length,total_distance,
                          race_state,opened_at,result_confirmed,schema_version)
                      VALUES(1,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,'Waiting',@p16,0,@p17)",
                    new object[] { roundId, first.EventId, first.EvNum, first.AgeGroup, first.Gender,
                        first.EventName, round.Distance, round.Stroke, round.RelayLegs, round.Stage, heat,
                        round.HeatCount, round.LaneCount ?? info.LaneCount, info.PoolLength, total,
                        Now(), LiveHeatSchema.Version });

                foreach (var x in lanes)
                {
                    if (x.Lane == null) continue;                       // 替补不下水
                    var legs = x.IsRelay ? GetRelayLegs(x.Id) : null;
                    string legNames = legs == null ? null
                        : string.Join(",", legs.Select(g => g.SwimmerName).ToArray());
                    run(@"INSERT INTO live_lanes(lane,heat_entry_id,bib_number,name,leg_names,country,
                              age_category,gender,seed_time,is_relay)
                          VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10)",
                        new object[] { x.Lane.Value, x.Id, null, x.Name, legNames, x.UnitName,
                            x.AgeGroup, x.Gender, x.SeedTime, x.IsRelay ? 1 : 0 });
                    if (legs != null)
                        foreach (var g in legs)
                            run(@"INSERT INTO live_legs(lane,leg_order,athlete_id,swimmer_name,swimmer_bib,
                                      swimmer_id_no,swimmer_birth,reaction_time)
                                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                new object[] { x.Lane.Value, g.LegOrder, g.AthleteId, g.SwimmerName,
                                    g.SwimmerBib, g.SwimmerIdNo, g.SwimmerBirth, g.ReactionTime });
                }
            });

            EnsureHeatRow(roundId, heat);
            _db.ExecuteNonQuery("UPDATE heats SET state='racing',started_at=@p3 WHERE round_id=@p1 AND heat=@p2",
                roundId, heat, Now());
            _db.ExecuteNonQuery("UPDATE rounds SET status='进行中' WHERE id=@p1", roundId);
            Audit(op, "开组", "heats", roundId, null, "第" + heat + "组", lanes.Count + " 道");
            return GetLiveHeat();
        }

        /// <summary>
        /// 用现成的 LiveHeat 灌本机当前组库。远端模式下, 计时端拿到服务器给的
        /// 名单后就靠它装进本机小库; 之后比赛全程只写这个小库。
        /// </summary>
        public void SeedLiveHeat(LiveHeat live)
        {
            if (live == null) throw new MeetDataException("没有本组数据");
            ClearLive();
            _live.InTransaction(delegate(Func<string, object[], int> run)
            {
                run(@"INSERT INTO live_heat(id,meet_round_id,meet_event_id,ev_num,age_group,gender,event_name,
                          distance,stroke,relay_legs,stage,heat,total_heats,lane_count,pool_length,total_distance,
                          race_state,opened_at,result_confirmed,schema_version)
                      VALUES(1,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17,0,@p18)",
                    new object[] { live.MeetRoundId, live.MeetEventId, live.EvNum, live.AgeGroup, live.Gender,
                        live.EventName, live.Distance, live.Stroke, live.RelayLegs, live.Stage, live.Heat,
                        live.TotalHeats, live.LaneCount, live.PoolLength, live.TotalDistance,
                        live.RaceState ?? "Waiting", live.OpenedAt ?? Now(), LiveHeatSchema.Version });

                foreach (var ln in live.Lanes)
                {
                    run(@"INSERT INTO live_lanes(lane,heat_entry_id,bib_number,name,leg_names,country,
                              age_category,gender,seed_time,is_relay)
                          VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10)",
                        new object[] { ln.Lane, ln.HeatEntryId, ln.BibNumber, ln.Name, ln.LegNames,
                            ln.Country, ln.AgeCategory, ln.Gender, ln.SeedTime, ln.IsRelay ? 1 : 0 });
                    if (ln.Legs != null)
                        foreach (var g in ln.Legs)
                            run(@"INSERT INTO live_legs(lane,leg_order,athlete_id,swimmer_name,swimmer_bib,
                                      swimmer_id_no,swimmer_birth,reaction_time)
                                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                new object[] { ln.Lane, g.LegOrder, g.AthleteId, g.SwimmerName, g.SwimmerBib,
                                    g.SwimmerIdNo, g.SwimmerBirth, g.ReactionTime });
                }
            });
        }

        private void ClearLive()
        {
            _live.InTransaction(delegate(Func<string, object[], int> run)
            {
                run("DELETE FROM live_splits", null);
                run("DELETE FROM live_legs", null);
                run("DELETE FROM live_lanes", null);
                run("DELETE FROM live_timing_log", null);
                run("DELETE FROM live_heat", null);
            });
        }

        public LiveHeat GetLiveHeat()
        {
            var t = _live.Query("SELECT * FROM live_heat WHERE id=1");
            if (t.Rows.Count == 0) return null;
            var r = t.Rows[0];
            var h = new LiveHeat {
                MeetRoundId = L(r,"meet_round_id"), MeetEventId = L(r,"meet_event_id"), EvNum = I(r,"ev_num"),
                SessionName = S(r,"session_name"), AgeGroup = S(r,"age_group"), Gender = S(r,"gender"),
                EventName = S(r,"event_name"), Distance = I(r,"distance"), Stroke = S(r,"stroke"),
                RelayLegs = I(r,"relay_legs"), Stage = S(r,"stage"), Heat = I(r,"heat"),
                TotalHeats = I(r,"total_heats"), LaneCount = I(r,"lane_count"), PoolLength = I(r,"pool_length"),
                TotalDistance = I(r,"total_distance"), RaceState = S(r,"race_state"), GunTime = S(r,"gun_time"),
                OpenedAt = S(r,"opened_at"), ResultConfirmed = B(r,"result_confirmed") };

            foreach (DataRow x in _live.Query("SELECT * FROM live_lanes ORDER BY lane").Rows)
            {
                var ln = new LiveLane {
                    Lane = I(x,"lane"), HeatEntryId = L(x,"heat_entry_id"), BibNumber = S(x,"bib_number"),
                    Name = S(x,"name"), LegNames = S(x,"leg_names"), Country = S(x,"country"),
                    AgeCategory = S(x,"age_category"), Gender = S(x,"gender"), SeedTime = S(x,"seed_time"),
                    IsRelay = B(x,"is_relay"), FinalTime = D(x,"final_time"), Rank = I(x,"rank"),
                    Status = S(x,"status"), RecordNote = S(x,"record_note"), TimingSource = S(x,"timing_source"),
                    ReactionTime = D(x,"reaction_time"), TouchpadTime = D(x,"touchpad_time"),
                    StartBlockTime = D(x,"start_block_time"), Pb1Time = D(x,"pb1_time"), Pb2Time = D(x,"pb2_time"),
                    Pb3Time = D(x,"pb3_time"), ManualLeft = D(x,"manual_left"), ManualRight = D(x,"manual_right"),
                    CurrentLap = I(x,"current_lap"), IsFinished = B(x,"is_finished"),
                    IsFalseStart = B(x,"is_false_start"), DsqCode = S(x,"dsq_code"), DsqLeg = I(x,"dsq_leg"),
                    Splits = new List<SplitDto>(), Legs = new List<RelayLegDto>() };
                foreach (DataRow sp in _live.Query(
                    "SELECT * FROM live_splits WHERE lane=@p1 ORDER BY distance", ln.Lane).Rows)
                    ln.Splits.Add(new SplitDto {
                        Distance = I(sp,"distance"), CumulativeTime = D(sp,"cumulative_time"),
                        LapTime = D(sp,"lap_time"), RankAt = I(sp,"rank_at"),
                        TimingSource = S(sp,"timing_source"), IsManual = B(sp,"is_manual") });
                foreach (DataRow lg in _live.Query(
                    "SELECT * FROM live_legs WHERE lane=@p1 ORDER BY leg_order", ln.Lane).Rows)
                    ln.Legs.Add(new RelayLegDto {
                        LegOrder = I(lg,"leg_order"), AthleteId = NL(lg,"athlete_id"),
                        SwimmerName = S(lg,"swimmer_name"), SwimmerBib = S(lg,"swimmer_bib"),
                        SwimmerIdNo = S(lg,"swimmer_id_no"), SwimmerBirth = S(lg,"swimmer_birth"),
                        ReactionTime = D(lg,"reaction_time"), LegTime = D(lg,"leg_time"),
                        CumulativeTime = D(lg,"cumulative_time"), RankAt = I(lg,"rank_at") });
                h.Lanes.Add(ln);
            }
            return h;
        }

        /// <summary>触板/按钮/手计时来一次就调一次。只写当前组库那一行，大库一个字节都不动。</summary>
        public void UpdateLane(int lane, LiveLane d)
        {
            _live.ExecuteNonQuery(
                @"UPDATE live_lanes SET final_time=@p2,rank=@p3,status=@p4,record_note=@p5,timing_source=@p6,
                      reaction_time=@p7,touchpad_time=@p8,start_block_time=@p9,pb1_time=@p10,pb2_time=@p11,
                      pb3_time=@p12,manual_left=@p13,manual_right=@p14,current_lap=@p15,is_finished=@p16,
                      is_false_start=@p17 WHERE lane=@p1",
                lane, d.FinalTime, d.Rank, d.Status, d.RecordNote, d.TimingSource, d.ReactionTime,
                d.TouchpadTime, d.StartBlockTime, d.Pb1Time, d.Pb2Time, d.Pb3Time, d.ManualLeft,
                d.ManualRight, d.CurrentLap, d.IsFinished ? 1 : 0, d.IsFalseStart ? 1 : 0);
        }

        public void UpdateSplit(int lane, int distance, double cumulative, double lap, string source)
        {
            _live.ExecuteNonQuery(
                @"INSERT INTO live_splits(lane,distance,cumulative_time,lap_time,timing_source)
                  VALUES(@p1,@p2,@p3,@p4,@p5)
                  ON CONFLICT(lane,distance) DO UPDATE SET cumulative_time=@p3,lap_time=@p4,timing_source=@p5",
                lane, distance, cumulative, lap, source);
        }

        public void SetLaneStatus(int lane, string status, int dsqLeg, string dsqCode, string op)
        {
            _live.ExecuteNonQuery(
                "UPDATE live_lanes SET status=@p2,dsq_leg=@p3,dsq_code=@p4 WHERE lane=@p1",
                lane, status, dsqLeg, dsqCode);
            // 接力 DSQ：保留 1~(N-1) 棒的分段，清掉第 N 棒起；备份原分段，撤销时能还原
            if (dsqLeg > 0)
            {
                var t = _live.Query("SELECT * FROM live_splits WHERE lane=@p1 ORDER BY distance", lane);
                var bak = new List<object>();
                foreach (DataRow r in t.Rows)
                    bak.Add(new { d = I(r,"distance"), c = D(r,"cumulative_time"), l = D(r,"lap_time") });
                _live.ExecuteNonQuery("UPDATE live_lanes SET dsq_backup_splits=@p2 WHERE lane=@p1",
                    lane, JsonConvert.SerializeObject(bak));
                object td = _live.ExecuteScalar("SELECT total_distance FROM live_heat WHERE id=1");
                object rl = _live.ExecuteScalar("SELECT relay_legs FROM live_heat WHERE id=1");
                int legs = Convert.ToInt32(rl); int total = Convert.ToInt32(td);
                if (legs > 1)
                {
                    int keep = (dsqLeg - 1) * (total / legs);
                    _live.ExecuteNonQuery("DELETE FROM live_splits WHERE lane=@p1 AND distance>@p2", lane, keep);
                    _live.ExecuteNonQuery("UPDATE live_lanes SET final_time=0 WHERE lane=@p1", lane);
                }
            }
            Audit(op, "判罚", "live_lanes", lane, null, status, dsqLeg > 0 ? "第" + dsqLeg + "棒" : null);
        }

        public void SetRaceState(string state, string gunTime)
        {
            _live.ExecuteNonQuery("UPDATE live_heat SET race_state=@p1, gun_time=COALESCE(@p2,gun_time) WHERE id=1",
                state, gunTime);
        }

        public List<RecordBreak> CommitHeat(string op) { return CommitHeatFrom(GetLiveHeat(), op); }

        /// <summary>
        /// 回写一组成绩。live 从哪来无所谓 —— 本机当前组库(单机)或计时端送来的
        /// (远端)。回写逻辑只有这一份, 两条路不可能写出两种结果。
        /// </summary>
        public List<RecordBreak> CommitHeatFrom(LiveHeat live, string op)
        {
            if (live == null) throw new MeetDataException("当前没有正在比赛的组");
            long roundId = live.MeetRoundId; int heat = live.Heat;
            var breaks = new List<RecordBreak>();

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                foreach (var ln in live.Lanes)
                {
                    if (ln.HeatEntryId <= 0) continue;
                    run(@"UPDATE heat_entries SET final_time=@p2,status=@p3,record_note=@p4,timing_source=@p5,
                              reaction_time=@p6,touchpad_time=@p7,start_block_time=@p8,pb1_time=@p9,pb2_time=@p10,
                              pb3_time=@p11,manual_left=@p12,manual_right=@p13,dsq_code=@p14,dsq_leg=@p15,
                              result_at=@p16 WHERE id=@p1",
                        new object[] { ln.HeatEntryId, ln.FinalTime, ln.Status, ln.RecordNote, ln.TimingSource,
                            ln.ReactionTime, ln.TouchpadTime, ln.StartBlockTime, ln.Pb1Time, ln.Pb2Time,
                            ln.Pb3Time, ln.ManualLeft, ln.ManualRight, ln.DsqCode, ln.DsqLeg, Now() });

                    run("DELETE FROM splits WHERE heat_entry_id=@p1", new object[] { ln.HeatEntryId });
                    if (ln.Splits != null)
                        foreach (var sp in ln.Splits)
                            run(@"INSERT INTO splits(heat_entry_id,distance,cumulative_time,lap_time,rank_at,
                                      timing_source,is_manual) VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                                new object[] { ln.HeatEntryId, sp.Distance, sp.CumulativeTime, sp.LapTime,
                                    sp.RankAt, sp.TimingSource, sp.IsManual ? 1 : 0 });

                    if (ln.Legs != null)
                        foreach (var g in ln.Legs)
                            run(@"UPDATE relay_legs SET reaction_time=@p3,leg_time=@p4,cumulative_time=@p5,rank_at=@p6
                                  WHERE heat_entry_id=@p1 AND leg_order=@p2",
                                new object[] { ln.HeatEntryId, g.LegOrder, g.ReactionTime, g.LegTime,
                                    g.CumulativeTime, g.RankAt });
                }
                run("UPDATE heats SET state='normal',confirmed_at=@p3,confirmed_by=@p4 WHERE round_id=@p1 AND heat=@p2",
                    new object[] { roundId, heat, Now(), op });
            });

            // 本组涉及的每个项目各自重排名次（混编时几个项目各排各的）
            foreach (DataRow r in _db.Query(
                @"SELECT DISTINCT en.event_id FROM heat_entries he JOIN entries en ON en.id=he.entry_id
                  WHERE he.round_id=@p1", roundId).Rows)
                RecomputeRanks(roundId, L(r, "event_id"), op);

            foreach (var ln in live.Lanes)
                if (ln.HeatEntryId > 0 && ln.FinalTime > 0 && !IsNoTime(ln.Status))
                    breaks.AddRange(CheckRecordBreak(ln.HeatEntryId));

            ClearLive();
            Audit(op, "确认成绩", "heats", roundId, null, "第" + heat + "组",
                breaks.Count > 0 ? "破纪录 " + breaks.Count + " 项" : null);
            return breaks;
        }

        public void DiscardHeat(string op)
        {
            var live = GetLiveHeat();
            if (live == null) return;
            _db.ExecuteNonQuery("UPDATE heats SET state='normal',started_at=NULL WHERE round_id=@p1 AND heat=@p2",
                live.MeetRoundId, live.Heat);
            ClearLive();
            Audit(op, "放弃本组", "heats", live.MeetRoundId, null, "第" + live.Heat + "组", "成绩未回写");
        }

        // ═══════════════ H. 成绩 / 名次 / 晋级 ═══════════════
        public List<LaneRow> GetResults(long roundId, long? eventId)
        {
            string sql = LaneSql + "WHERE v.round_id=@p1 " + (eventId != null ? "AND v.event_id=@p2 " : "")
                       + "ORDER BY v.组次, v.道次";
            var t = eventId != null ? _db.Query(sql, roundId, eventId.Value) : _db.Query(sql, roundId);
            var list = new List<LaneRow>();
            foreach (DataRow r in t.Rows) list.Add(ReadLane(r));
            FillHeatRankAndGap(list);
            return list;
        }

        /// <summary>
        /// 按项目排大排名、打 Q/R。混编的组里别的组别的人不参与本项目的排名 ——
        /// 这就是「赛完再将成绩分开」。名次只在这一处算，各端只管排版。
        /// </summary>
        public void RecomputeRanks(long roundId, long eventId, string op)
        {
            var t = _db.Query(
                @"SELECT he.id, he.final_time, he.status FROM heat_entries he
                  JOIN entries en ON en.id = he.entry_id
                  WHERE he.round_id=@p1 AND en.event_id=@p2 AND he.reserve_no IS NULL
                  ORDER BY he.final_time", roundId, eventId);

            var valid = new List<KeyValuePair<long, double>>();
            var invalid = new List<long>();
            foreach (DataRow r in t.Rows)
            {
                double ft = D(r, "final_time");
                if (ft > 0 && !IsNoTime(S(r, "status"))) valid.Add(new KeyValuePair<long, double>(L(r, "id"), ft));
                else invalid.Add(L(r, "id"));
            }
            valid.Sort((a, b) => a.Value.CompareTo(b.Value));

            int promote = 0, reserve = 0;
            var re = _db.Query("SELECT promote_count,reserve_count FROM round_events WHERE round_id=@p1 AND event_id=@p2",
                roundId, eventId);
            if (re.Rows.Count > 0) { promote = I(re.Rows[0], "promote_count"); reserve = I(re.Rows[0], "reserve_count"); }

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                foreach (var id in invalid)
                    run("UPDATE heat_entries SET rank=0,promotion_mark=NULL WHERE id=@p1", new object[] { id });

                // 2026-08-30 并列判定改用 ResultOrdering(全场唯一一份), 见上面同样的说明。
                //   valid 已按成绩升序排好。
                var evRanks = ResultOrdering.ComputeRanks(valid, kv => kv.Value);
                for (int i = 0; i < valid.Count; i++)
                {
                    var kv = valid[i];
                    int rank = evRanks[i];
                    // 卡在晋级线上并列的一律给 Q —— 规则上要加赛决定，先都放进去，
                    // 少放一个人比多放一个人麻烦得多
                    string mark = null;
                    if (promote > 0 && rank <= promote) mark = "Q";
                    else if (reserve > 0 && rank <= promote + reserve) mark = "R";
                    run("UPDATE heat_entries SET rank=@p2,promotion_mark=@p3 WHERE id=@p1",
                        new object[] { kv.Key, rank, mark });
                }
            });
            Audit(op, "重排名次", "rounds", roundId, null, "项目 " + eventId, valid.Count + " 人有成绩");
        }

        public void SaveResult(LaneRow row, string reason, string op)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new MeetDataException("赛后改成绩必须填写理由");     // 用户 2026-08-24 定的
            var t = _db.Query(
                @"SELECT he.round_id, he.heat, he.final_time, he.status, en.event_id
                  FROM heat_entries he JOIN entries en ON en.id=he.entry_id WHERE he.id=@p1", row.Id);
            if (t.Rows.Count == 0) throw new MeetDataException("找不到这条成绩");
            long roundId = L(t.Rows[0], "round_id");
            long eventId = L(t.Rows[0], "event_id");
            EnsureNotRacing(roundId, NI(t.Rows[0], "heat"));
            string old = S(t.Rows[0], "final_time") + " " + S(t.Rows[0], "status");

            _db.ExecuteNonQuery(
                @"UPDATE heat_entries SET final_time=@p2,status=@p3,record_note=@p4,dsq_code=@p5,dsq_leg=@p6,
                      note=@p7,dispute_note=@p8,result_at=@p9 WHERE id=@p1",
                row.Id, row.FinalTime, row.Status, row.RecordNote, row.DsqCode, row.DsqLeg,
                row.Note, row.DisputeNote, Now());
            Audit(op, "改成绩", "heat_entries", row.Id, old,
                row.FinalTime.ToString(CultureInfo.InvariantCulture) + " " + row.Status, reason);
            // 成绩一改名次就得跟着重排，否则名次公告、录取、得分全按旧名次打
            RecomputeRanks(roundId, eventId, op);
        }

        public List<LaneRow> GetPromotionList(long eventId, string fromStage, string toStage)
        {
            var list = new List<LaneRow>();
            foreach (DataRow r in _db.Query(LaneSql +
                @"WHERE v.event_id=@p1 AND v.赛次=@p2 AND he.reserve_no IS NULL AND he.rank>0
                  ORDER BY he.rank", eventId, fromStage).Rows)
                list.Add(ReadLane(r));
            return list;
        }

        public void PromoteToNextRound(long eventId, string fromStage, string toStage,
                                       List<long> heatEntryIds, string op)
        {
            var t = _db.Query(
                @"SELECT r.id FROM rounds r JOIN round_events re ON re.round_id=r.id
                  WHERE re.event_id=@p1 AND r.stage=@p2", eventId, toStage);
            if (t.Rows.Count == 0) throw new MeetDataException("还没有「" + toStage + "」这个赛次，请先在日程里建出来");
            long toRound = L(t.Rows[0], "id");

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                run(@"DELETE FROM heat_entries WHERE round_id=@p1 AND entry_id IN
                      (SELECT entry_id FROM entries WHERE event_id=@p2)", new object[] { toRound, eventId });
                foreach (long src in heatEntryIds)
                {
                    var s = _db.Query("SELECT entry_id,final_time,rank FROM heat_entries WHERE id=@p1", src);
                    if (s.Rows.Count == 0) continue;
                    // 上一赛次的成绩就是下一赛次的报名成绩（半决赛名单上那列「预赛成绩」）
                    run(@"INSERT INTO heat_entries(round_id,entry_id,seed_time_seconds,promoted_from,promoted_rank)
                          VALUES(@p1,@p2,@p3,@p4,@p5)",
                        new object[] { toRound, L(s.Rows[0],"entry_id"), D(s.Rows[0],"final_time"),
                            src, I(s.Rows[0],"rank") });
                }
            });
            Audit(op, "晋级", "rounds", toRound, fromStage, toStage, heatEntryIds.Count + " 人");
        }

        // ═══════════════ I. 纪录 / 团体分 ═══════════════
        public List<RecordDto> GetRecords(string ageGroup, string gender, int distance, string stroke, int relayLegs)
        {
            var list = new List<RecordDto>();
            foreach (DataRow r in _db.Query(
                @"SELECT * FROM records WHERE is_current=1 AND gender=@p2 AND distance=@p3
                        AND stroke=@p4 AND relay_legs=@p5 AND (age_group=@p1 OR age_group='')
                  ORDER BY ord,id", ageGroup ?? "", gender, distance, stroke, relayLegs).Rows)
                list.Add(ReadRecord(r));
            return list;
        }
        private static RecordDto ReadRecord(DataRow r)
        {
            return new RecordDto {
                Id = L(r,"id"), Ord = I(r,"ord"), Abbr = S(r,"abbr"), RecordType = S(r,"record_type"),
                AgeGroup = S(r,"age_group") ?? "", Gender = S(r,"gender"), Distance = I(r,"distance"),
                Stroke = S(r,"stroke"), RelayLegs = I(r,"relay_legs"), EventName = S(r,"event_name"),
                TimeSeconds = D(r,"time_seconds"), HolderName = S(r,"holder_name"),
                HolderCountry = S(r,"holder_country"), Date = S(r,"date"), Location = S(r,"location"),
                IsCurrent = B(r,"is_current"), BrokenBy = NL(r,"broken_by"), Note = S(r,"note") };
        }

        public List<RecordBreak> CheckRecordBreak(long heatEntryId)
        {
            var res = new List<RecordBreak>();
            var t = _db.Query(
                @"SELECT he.final_time, he.status, e.age_group, e.gender, e.distance, e.stroke, e.relay_legs,
                         COALESCE(en.team_name, a.name) AS nm, COALESCE(u1.name,u2.name) AS un
                  FROM heat_entries he
                  JOIN entries en ON en.id=he.entry_id
                  JOIN events  e  ON e.id=en.event_id
                  LEFT JOIN athletes a ON a.id=en.athlete_id
                  LEFT JOIN units u1 ON u1.id=en.unit_id
                  LEFT JOIN units u2 ON u2.id=a.unit_id
                  WHERE he.id=@p1", heatEntryId);
            if (t.Rows.Count == 0) return res;
            var r = t.Rows[0];
            double ft = D(r, "final_time");
            if (ft <= 0 || IsNoTime(S(r, "status"))) return res;

            foreach (var rec in GetRecords(S(r,"age_group"), S(r,"gender"), I(r,"distance"),
                                           S(r,"stroke"), I(r,"relay_legs")))
            {
                if (rec.TimeSeconds <= 0) continue;
                // 2026-08-30 平纪录的判定必须跟"并列"同口径(1/100 秒取整后相等),
                //   否则会出现: 成绩和纪录显示的都是 42.28, 却判成没平纪录。
                //   原来这里用 1e-9 直接比 double, 跟名次那套规则对不上。
                bool tieRec = ResultOrdering.IsTie(ft, rec.TimeSeconds);
                if (tieRec || ft < rec.TimeSeconds)
                    res.Add(new RecordBreak {
                        Record = rec, NewTime = ft,
                        IsTie = tieRec,
                        NewHolder = S(r,"nm"), NewCountry = S(r,"un") });
            }
            return res;
        }

        public void ApplyRecordBreak(long heatEntryId, List<RecordBreak> breaks, string op)
        {
            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                var marks = new List<string>();
                foreach (var b in breaks)
                {
                    if (b.IsTie) { marks.Add("=" + b.Record.Abbr); continue; }   // 平纪录不刷新
                    // 旧行不删，置成历史 —— 证书上要写「原纪录 X 由 Y 保持」，删了就没了
                    run("UPDATE records SET is_current=0, broken_by=@p2 WHERE id=@p1",
                        new object[] { b.Record.Id, heatEntryId });
                    run(@"INSERT INTO records(abbr,record_type,ord,age_group,gender,distance,stroke,relay_legs,
                              event_name,time_seconds,holder_name,holder_country,date,location,is_current)
                          VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,1)",
                        new object[] { b.Record.Abbr, b.Record.RecordType, b.Record.Ord, b.Record.AgeGroup,
                            b.Record.Gender, b.Record.Distance, b.Record.Stroke, b.Record.RelayLegs,
                            b.Record.EventName, b.NewTime, b.NewHolder, b.NewCountry,
                            DateTime.Now.ToString("yyyy-MM-dd"), null });
                    marks.Add(b.Record.Abbr);
                }
                if (marks.Count > 0)
                    run("UPDATE heat_entries SET record_note=@p2 WHERE id=@p1",
                        new object[] { heatEntryId, string.Join(" ", marks.ToArray()) });
            });
            Audit(op, "刷新纪录", "heat_entries", heatEntryId, null,
                string.Join(",", breaks.Select(x => x.Record.Abbr).ToArray()), null);
        }

        public long SaveRecord(RecordDto d, string op)
        {
            if (d.Id > 0)
            {
                _db.ExecuteNonQuery(
                    @"UPDATE records SET abbr=@p2,record_type=@p3,ord=@p4,age_group=@p5,gender=@p6,distance=@p7,
                          stroke=@p8,relay_legs=@p9,event_name=@p10,time_seconds=@p11,holder_name=@p12,
                          holder_country=@p13,date=@p14,location=@p15,is_current=@p16,note=@p17 WHERE id=@p1",
                    d.Id, d.Abbr, d.RecordType, d.Ord, d.AgeGroup ?? "", d.Gender, d.Distance, d.Stroke,
                    d.RelayLegs, d.EventName, d.TimeSeconds, d.HolderName, d.HolderCountry, d.Date,
                    d.Location, d.IsCurrent ? 1 : 0, d.Note);
                Audit(op, "改纪录", "records", d.Id, null, d.EventName, null);
                return d.Id;
            }
            long id = _db.ExecuteInsert(
                @"INSERT INTO records(abbr,record_type,ord,age_group,gender,distance,stroke,relay_legs,event_name,
                      time_seconds,holder_name,holder_country,date,location,is_current,note)
                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16)",
                d.Abbr, d.RecordType, d.Ord, d.AgeGroup ?? "", d.Gender, d.Distance, d.Stroke, d.RelayLegs,
                d.EventName, d.TimeSeconds, d.HolderName, d.HolderCountry, d.Date, d.Location,
                d.IsCurrent ? 1 : 0, d.Note);
            Audit(op, "建纪录", "records", id, null, d.EventName, null);
            return id;
        }

        public List<UnitDto> GetTeamScores()
        {
            var list = new List<UnitDto>();
            foreach (DataRow r in _db.Query("SELECT * FROM units ORDER BY score DESC, name").Rows)
                list.Add(ReadUnit(r));
            return list;
        }

        public void RecomputeTeamScores(string op)
        {
            string json = GetSetting("scoring");
            var table = new Dictionary<int, double>();
            double relayMul = 2.0, recordBonus = 0;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    dynamic cfg = JsonConvert.DeserializeObject(json);
                    if (cfg != null && cfg.PlacePoints != null)
                    { int i = 1; foreach (var v in cfg.PlacePoints) { table[i] = (double)v; i++; } }
                    if (cfg != null && cfg.RelayMultiplier != null) relayMul = (double)cfg.RelayMultiplier;
                    if (cfg != null && cfg.RecordBonus != null) recordBonus = (double)cfg.RecordBonus;
                }
                catch { }
            }
            if (table.Count == 0)
            { double[] def = {9,7,6,5,4,3,2,1}; for (int i = 0; i < def.Length; i++) table[i+1] = def[i]; }

            var acc = new Dictionary<long, double[]>();     // unitId → [个人, 接力, 破纪录, 金, 银, 铜]
            foreach (DataRow r in _db.Query(
                @"SELECT COALESCE(en.unit_id,a.unit_id) AS uid, he.rank, he.record_note,
                         CASE WHEN en.team_name IS NULL THEN 0 ELSE 1 END AS is_relay
                  FROM heat_entries he
                  JOIN entries en ON en.id=he.entry_id
                  JOIN rounds  r  ON r.id=he.round_id
                  LEFT JOIN athletes a ON a.id=en.athlete_id
                  WHERE he.rank>0 AND r.stage NOT IN ('预赛','半决赛')").Rows)
            {
                if (r["uid"] == DBNull.Value) continue;
                long uid = Convert.ToInt64(r["uid"]);
                int rank = I(r, "rank");
                if (!table.ContainsKey(rank)) continue;
                if (!acc.ContainsKey(uid)) acc[uid] = new double[6];
                bool isRelay = I(r, "is_relay") != 0;
                double pt = table[rank] * (isRelay ? relayMul : 1.0);
                acc[uid][isRelay ? 1 : 0] += pt;
                if (!string.IsNullOrEmpty(S(r, "record_note")) && !S(r, "record_note").StartsWith("="))
                    acc[uid][2] += recordBonus;
                if (rank == 1) acc[uid][3]++; else if (rank == 2) acc[uid][4]++; else if (rank == 3) acc[uid][5]++;
            }

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                run("UPDATE units SET score=0,individual_points=0,relay_points=0,record_bonus=0," +
                    "gold=0,silver=0,bronze=0,score_rank=0", null);
                foreach (var kv in acc)
                {
                    var v = kv.Value;
                    run(@"UPDATE units SET individual_points=@p2,relay_points=@p3,record_bonus=@p4,
                              gold=@p5,silver=@p6,bronze=@p7,score=base_points+@p2+@p3+@p4 WHERE id=@p1",
                        new object[] { kv.Key, v[0], v[1], v[2], (int)v[3], (int)v[4], (int)v[5] });
                }
            });

            int k = 1;
            foreach (DataRow r in _db.Query("SELECT id FROM units ORDER BY score DESC, gold DESC, silver DESC").Rows)
                _db.ExecuteNonQuery("UPDATE units SET score_rank=@p2 WHERE id=@p1", L(r, "id"), k++);
            Audit(op, "重算团体分", "units", null, null, acc.Count + " 个单位", null);
        }

        // ═══════════════ J. 报表（服务器算好返回）═══════════════
        public List<LaneRow> GetStartList(long roundId, long? eventId)
        {
            string sql = LaneSql + "WHERE v.round_id=@p1 " + (eventId != null ? "AND v.event_id=@p2 " : "")
                       + "ORDER BY v.组次 IS NULL, v.组次, v.道次, v.替补号";
            var t = eventId != null ? _db.Query(sql, roundId, eventId.Value) : _db.Query(sql, roundId);
            var list = new List<LaneRow>();
            foreach (DataRow r in t.Rows) list.Add(ReadLane(r));
            return list;
        }

        public List<LaneRow> GetResultSheet(long roundId, long? eventId)
        {
            var list = GetResults(roundId, eventId);
            foreach (var x in list)
            {
                x.Splits = new List<SplitDto>();
                foreach (DataRow r in _db.Query(
                    "SELECT * FROM splits WHERE heat_entry_id=@p1 ORDER BY distance", x.Id).Rows)
                    x.Splits.Add(new SplitDto {
                        Distance = I(r,"distance"), CumulativeTime = D(r,"cumulative_time"),
                        LapTime = D(r,"lap_time"), RankAt = I(r,"rank_at"),
                        TimingSource = S(r,"timing_source"), IsManual = B(r,"is_manual") });
                if (x.IsRelay) x.Legs = GetRelayLegs(x.Id);
            }
            return list;
        }

        /// <summary>比赛结果摘要：跨组大排名 + Q/R。DSQ/DNS 无名次，排在最后。</summary>
        public List<LaneRow> GetSummary(long roundId, long eventId)
        {
            var list = new List<LaneRow>();
            foreach (DataRow r in _db.Query(LaneSql +
                // 没成绩的（DSQ/DNS/还没游）一律排在最后，别让 final_time=0 冒到第一行
                @"WHERE v.round_id=@p1 AND v.event_id=@p2
                  ORDER BY (he.rank=0), he.rank, (he.final_time<=0), he.final_time", roundId, eventId).Rows)
                list.Add(ReadLane(r));
            MarkTiesAndGap(list);
            return list;
        }

        public List<LaneRow> GetRankingBulletin(long eventId)
        {
            // 名次公告用最后一个赛次（决赛优先）的成绩
            var t = _db.Query(
                @"SELECT r.id FROM rounds r JOIN round_events re ON re.round_id=r.id
                  WHERE re.event_id=@p1 AND r.is_ceremony=0
                  ORDER BY CASE r.stage WHEN '决赛' THEN 0 WHEN '计时决赛' THEN 1
                                        WHEN '半决赛' THEN 2 ELSE 3 END LIMIT 1", eventId);
            if (t.Rows.Count == 0) return new List<LaneRow>();
            long roundId = L(t.Rows[0], "id");
            var list = GetSummary(roundId, eventId);
            foreach (var x in list) if (x.IsRelay) x.Legs = GetRelayLegs(x.Id);
            return list;
        }

        private static void MarkTiesAndGap(List<LaneRow> rows)
        {
            var scored = rows.Where(x => x.Rank > 0).OrderBy(x => x.Rank).ToList();
            if (scored.Count == 0) return;
            double best = scored[0].FinalTime;
            foreach (var x in scored)
            {
                x.Gap = Math.Round(x.FinalTime - best, 2);
                x.IsTie = scored.Count(y => y.Rank == x.Rank) > 1;
            }
        }

        // ═══════════════ K. 运维 ═══════════════
        public void Backup(string targetPath)
        {
            _db.Checkpoint();                    // 先把 WAL 落盘，否则复制出去的是半截
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(_db.FilePath, targetPath, true);
        }

        public List<AuditRow> GetAuditLog(string action, string since, int limit)
        {
            string sql = "SELECT * FROM audit_log WHERE 1=1 ";
            var ps = new List<object>();
            if (!string.IsNullOrEmpty(action)) { sql += "AND action=@p" + (ps.Count+1) + " "; ps.Add(action); }
            if (!string.IsNullOrEmpty(since))  { sql += "AND at>=@p" + (ps.Count+1) + " ";     ps.Add(since); }
            sql += "ORDER BY id DESC LIMIT " + (limit > 0 ? limit : 500);
            var list = new List<AuditRow>();
            foreach (DataRow r in _db.Query(sql, ps.ToArray()).Rows)
                list.Add(new AuditRow {
                    Id = L(r,"id"), At = S(r,"at"), Operator = S(r,"operator"), Action = S(r,"action"),
                    Target = S(r,"target"), TargetId = NL(r,"target_id"), OldValue = S(r,"old_value"),
                    NewValue = S(r,"new_value"), Note = S(r,"note") });
            return list;
        }
    }
}
