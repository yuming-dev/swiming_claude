using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 把旧的 CompetitionPackage(JSON 大包) 导入竞赛管理库           2026-08-24
    //
    // 用途有二：
    //   1. 已有比赛档案(定西站 / 甘肃十六运…)迁进新库，不用重录
    //   2. 别人给的 .json 档案包照旧能用 —— 分发流程不变
    //
    // 【运动员去重规则】用户 2026-08-24 定的：只认 身份证号 / 注册号，
    //   两者都为空就各算各的、绝不合并。
    //   竞赛数据宁可冗余也不能错并 —— 同一单位真有两个同名运动员时，
    //   按姓名合并会把两个人的成绩混成一个人，那是一块奖牌的事。
    // ══════════════════════════════════════════════════════════════════════
    public class PackageImporter
    {
        private const char SEP = '\u0001';   // 内部拼键用的分隔符，数据里不可能出现

        private readonly MeetDb _db;
        public PackageImporter(MeetDb db) { _db = db; }

        public class Report
        {
            public int Sessions, Units, Athletes, Events, Rounds, Entries, RelayEntries, RelayLegs;
            public int HeatEntries, Heats, Results, Splits, Records, Staff, BibRanges, TeamScores;
            public int MergedByIdNumber, KeptSeparateNoId;
            public readonly List<string> Warnings = new List<string>();

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine("── 导入结果 ──");
                sb.AppendLine(string.Format("  场次 {0}   项目 {1}   赛次 {2}   单位 {3}", Sessions, Events, Rounds, Units));
                sb.AppendLine(string.Format("  运动员 {0}   报名 {1}（其中接力队 {2}，棒次 {3}）", Athletes, Entries, RelayEntries, RelayLegs));
                sb.AppendLine(string.Format("  分组名单 {0}   组 {1}   成绩 {2}（分段 {3}）", HeatEntries, Heats, Results, Splits));
                sb.AppendLine(string.Format("  纪录 {0}   工作人员 {1}   号段 {2}   团体分 {3}", Records, Staff, BibRanges, TeamScores));
                sb.AppendLine("── 运动员去重 ──");
                sb.AppendLine(string.Format("  按身份证/注册号合并 {0} 条；无证件号、各算各的 {1} 条", MergedByIdNumber, KeptSeparateNoId));
                if (Warnings.Count > 0)
                {
                    sb.AppendLine(string.Format("── 提示 {0} 条 ──", Warnings.Count));
                    int show = Math.Min(20, Warnings.Count);
                    for (int i = 0; i < show; i++) sb.AppendLine("  " + Warnings[i]);
                    if (Warnings.Count > show) sb.AppendLine(string.Format("  …另有 {0} 条", Warnings.Count - show));
                }
                return sb.ToString();
            }
        }

        // ── 项目名 → 距离 / 泳姿 / 棒数 ──────────────────────────────────
        // 旧档案只有一个字符串「4x100米自由泳接力」，五要素里的距离和泳姿糊在一起。
        // 这里只在导入时解析一次，落成列；之后任何判断都不许再解析字符串。
        private static readonly Regex ReRelay = new Regex(@"^\s*(\d+)\s*[xX×]\s*(\d+)\s*米?\s*(.+?)\s*$");
        private static readonly Regex ReSolo  = new Regex(@"^\s*(\d+)\s*米?\s*(.+?)\s*$");

        public static void ParseEventName(string name, out int distance, out string stroke, out int legs)
        {
            distance = 0; stroke = (name ?? "").Trim(); legs = 1;
            if (string.IsNullOrWhiteSpace(name)) return;

            var m = ReRelay.Match(name);
            if (m.Success)
            {
                legs = int.Parse(m.Groups[1].Value);
                distance = int.Parse(m.Groups[2].Value);
                stroke = m.Groups[3].Value.Trim();
            }
            else
            {
                m = ReSolo.Match(name);
                if (!m.Success) return;
                distance = int.Parse(m.Groups[1].Value);
                stroke = m.Groups[2].Value.Trim();
                // 没写棒数的接力（如「800米自由泳接力」= 4x200），按 4 棒算，
                // 距离随之改成每棒距离
                if (stroke.EndsWith("接力")) { legs = 4; if (distance % 4 == 0) distance /= 4; }
            }
            // 接力的泳姿去掉尾巴：自由泳接力→自由泳、混合泳接力→混合泳。
            // 是不是接力靠 legs 判断，不靠名字里有没有「接力」两个字。
            if (legs > 1 && stroke.EndsWith("接力")) stroke = stroke.Substring(0, stroke.Length - 2);
        }

        private static bool IsRelayProxy(Swimmer s)
        {
            string n = s.Notes ?? "";
            return n.StartsWith("接力队") && !n.StartsWith("接力队员");
        }
        private static bool IsRelayMember(Swimmer s) { return (s.Notes ?? "").StartsWith("接力队员"); }

        // 证件号身份键；null = 没有证件号 → 按规则不参与合并
        private static string IdentityKey(Swimmer s)
        {
            if (!string.IsNullOrWhiteSpace(s.IDNumber))   return "ID:"   + s.IDNumber.Trim();
            if (!string.IsNullOrWhiteSpace(s.CSANumber))  return "CSA:"  + s.CSANumber.Trim();
            if (!string.IsNullOrWhiteSpace(s.FINANumber)) return "FINA:" + s.FINANumber.Trim();
            return null;
        }

        private static string Key(params object[] parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts) { sb.Append(p == null ? "" : p.ToString()); sb.Append(SEP); }
            return sb.ToString();
        }

        public Report Import(CompetitionPackage p)
        {
            if (p == null) throw new ArgumentNullException("p");
            var rep = new Report();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var sessIds  = new Dictionary<string, long>();   // 日期|场次名 → id
            var unitIds  = new Dictionary<string, long>();   // 单位名 → id
            var athIds   = new Dictionary<string, long>();   // 证件号 → id
            var evIds    = new Dictionary<string, long>();   // 组别|性别|距离|泳姿|棒数 → id
            var rndIds   = new Dictionary<string, long>();   // eventId|赛次 → roundId
            var entIds   = new Dictionary<string, long>();   // eventId|A:athId 或 eventId|T:队名 → id
            var heSeen   = new HashSet<string>();            // roundId|组|道
            var heatSeen = new HashSet<string>();            // roundId|组

            _db.InTransaction(delegate(Func<string, object[], int> run)
            {
                // ══ 赛事 ══
                run("DELETE FROM competition", null);
                run(@"INSERT INTO competition(id,name,mode,rule,use_age_group,start_date,end_date,location,
                          pool_length,lane_count,lane_numbers,organizer,host,technical_delegate,referee,
                          starter,arbiter,chief_judge,display_record_label,display_record_type_name,schema_version)
                      VALUES(1,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17,@p18,@p19,@p20)",
                    new object[] { p.CompetitionName, p.CompetitionMode, p.CompetitionRule,
                        (p.AgeGroups != null && p.AgeGroups.Count > 0) ? 1 : 0,
                        p.StartDate, p.EndDate, p.Location, p.PoolLength, p.LaneCount,
                        string.Join(",", Enumerable.Range(1, Math.Max(p.LaneCount, 1)).Select(i => i.ToString()).ToArray()),
                        p.Organizer, p.Host, p.TechnicalDelegate, p.Referee, p.Starter, p.Arbiter, p.ChiefJudge,
                        p.DisplayRecordLabel, p.DisplayRecordTypeName, MeetSchema.Version });

                // ══ 单位 ══
                if (p.Units != null)
                    foreach (var u in p.Units)
                    {
                        if (u == null || string.IsNullOrEmpty(u.Name) || unitIds.ContainsKey(u.Name)) continue;
                        run(@"INSERT INTO units(name,short_name,leader,coach,doctor,phone,address,base_points,note)
                              VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                            new object[] { u.Name, u.ShortName, u.Leader, u.Coach, u.Doctor, u.Phone, u.Address, u.BasePoints, u.Note });
                        unitIds[u.Name] = _db.LastInsertId();
                        rep.Units++;
                    }

                Func<string, long?> unitOf = delegate(string name)
                {
                    if (string.IsNullOrEmpty(name)) return null;
                    if (!unitIds.ContainsKey(name))
                    {
                        run("INSERT INTO units(name,short_name) VALUES(@p1,@p1)", new object[] { name });
                        unitIds[name] = _db.LastInsertId();
                        rep.Units++;
                        rep.Warnings.Add("单位表里没有「" + name + "」，已按报名记录补建");
                    }
                    return unitIds[name];
                };

                // ══ 场次 ══
                Func<string, string, long?> sessionOf = delegate(string date, string name)
                {
                    if (string.IsNullOrEmpty(date) && string.IsNullOrEmpty(name)) return null;
                    string k = Key(date, name);
                    if (!sessIds.ContainsKey(k))
                    {
                        run("INSERT INTO sessions(no,date,name) VALUES(@p1,@p2,@p3)",
                            new object[] { sessIds.Count + 1, date, name });
                        sessIds[k] = _db.LastInsertId();
                        rep.Sessions++;
                    }
                    return sessIds[k];
                };

                // ══ 项目（组别·性别·距离·泳姿·棒数）══
                Func<string, string, string, int, long> eventOf =
                    delegate(string ageGroup, string gender, string eventName, int evNum)
                {
                    int dist; string stroke; int legs;
                    ParseEventName(eventName, out dist, out stroke, out legs);
                    string ag = ageGroup ?? "";
                    string k = Key(ag, gender, dist, stroke, legs);
                    if (!evIds.ContainsKey(k))
                    {
                        run(@"INSERT INTO events(ev_num,age_group,gender,distance,stroke,relay_legs,event_name)
                              VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                            new object[] { evNum > 0 ? (object)evNum : null, ag, gender, dist, stroke, legs, eventName });
                        evIds[k] = _db.LastInsertId();
                        rep.Events++;
                    }
                    return evIds[k];
                };

                // ══ 赛次 ══
                // 旧档案里没有混编，一个赛次就挂一个项目；将来编排端排混编时，
                // 往同一个 round 上再 INSERT 一条 round_events 即可。
                Func<long, string, long?, string, int, long> roundOf =
                    delegate(long evId, string stage, long? sessionId, string time, int heatCount)
                {
                    string st = string.IsNullOrEmpty(stage) ? "决赛" : stage;
                    string k = Key(evId, st);
                    if (!rndIds.ContainsKey(k))
                    {
                        var er = _db.Query("SELECT distance,stroke,relay_legs,event_name FROM events WHERE id=@p1", evId);
                        object dist = null, stroke = null, legs = 1; object title = null;
                        if (er.Rows.Count > 0)
                        {
                            dist = er.Rows[0]["distance"]; stroke = er.Rows[0]["stroke"];
                            legs = er.Rows[0]["relay_legs"]; title = er.Rows[0]["event_name"];
                        }
                        run(@"INSERT INTO rounds(session_id,stage,distance,stroke,relay_legs,ord,time,heat_count,is_ceremony,title)
                              VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,0,@p9)",
                            new object[] { sessionId, st, dist, stroke, legs, rndIds.Count, time, heatCount, title });
                        long rid = _db.LastInsertId();
                        rndIds[k] = rid;
                        rep.Rounds++;
                        run("INSERT OR IGNORE INTO round_events(round_id,event_id,ord) VALUES(@p1,@p2,0)",
                            new object[] { rid, evId });
                    }
                    return rndIds[k];
                };

                // ══ 赛程 → 场次 + 项目 + 赛次 ══
                if (p.Schedule != null)
                    foreach (var s in p.Schedule)
                    {
                        if (s == null || string.IsNullOrEmpty(s.EventName)) continue;
                        string sname = string.IsNullOrEmpty(s.SessionName)
                            ? (s.SessionNumber > 0 ? "第" + s.SessionNumber + "单元" : null)
                            : s.SessionName;
                        long? sid = sessionOf(s.Date, sname);
                        long eid = eventOf(s.AgeGroup, s.Gender, s.EventName, s.EvNum);
                        long rid = roundOf(eid, s.Stage, sid, s.Time, s.HeatCount);
                        run("UPDATE rounds SET session_id=COALESCE(@p2,session_id),time=COALESCE(@p3,time),heat_count=@p4 WHERE id=@p1",
                            new object[] { rid, sid, s.Time, s.HeatCount });

                        if (s.CancelledHeats != null)
                            foreach (var ch in s.CancelledHeats)
                            {
                                if (ch == null) continue;
                                run(@"INSERT OR REPLACE INTO heats(round_id,heat,state,merged_into,cancel_reason,cancelled_at,operator)
                                      VALUES(@p1,@p2,'cancelled',@p3,@p4,@p5,@p6)",
                                    new object[] { rid, ch.Heat, ch.MergedInto, ch.Reason, ch.Time, ch.Operator });
                                heatSeen.Add(Key(rid, ch.Heat));
                                rep.Heats++;
                            }
                    }

                // ══ 接力棒次名单先收着，等分组行建出来再挂上去 ══
                var legsByTeam = new Dictionary<string, List<RelayLeg>>();
                if (p.RelayTeams != null)
                    foreach (var t in p.RelayTeams)
                    {
                        if (t == null || t.Legs == null || t.Legs.Count == 0) continue;
                        long eid = eventOf(t.AgeGroup, t.Gender, t.EventName, 0);
                        legsByTeam[Key(eid, t.TeamName)] = t.Legs.ToList();
                    }

                // ══ 运动员 / 报名 / 分组名单 / 成绩 ══
                if (p.Swimmers != null)
                    foreach (var s in p.Swimmers)
                    {
                        if (s == null || string.IsNullOrEmpty(s.EventName)) continue;
                        if (IsRelayMember(s)) continue;            // 接力队员子条目 → 走 relay_legs

                        long eid = eventOf(s.AgeCategory, s.Gender, s.EventName, 0);
                        bool isRelay = IsRelayProxy(s);
                        long entryId;

                        if (isRelay)
                        {
                            string ek = Key(eid, "T:" + s.Name);
                            if (!entIds.ContainsKey(ek))
                            {
                                run(@"INSERT INTO entries(event_id,unit_id,team_name,bib_number,entry_time_seconds,entry_time,note)
                                      VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                                    new object[] { eid, unitOf(s.Country ?? s.Name), s.Name, s.BibNumber,
                                        s.EntryTimeSeconds, s.EntryTime, s.Notes });
                                entIds[ek] = _db.LastInsertId();
                                rep.Entries++; rep.RelayEntries++;
                            }
                            entryId = entIds[ek];
                        }
                        else
                        {
                            string idk = IdentityKey(s);
                            long athId;
                            if (idk != null && athIds.ContainsKey(idk))
                            {
                                athId = athIds[idk];
                                rep.MergedByIdNumber++;
                            }
                            else
                            {
                                run(@"INSERT INTO athletes(bib_number,name,gender,birth_date,id_number,unit_id,phone,
                                          csa_number,fina_number,health_cert_date,age_category,note)
                                      VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12)",
                                    new object[] { s.BibNumber, s.Name, s.Gender, s.BirthDate, s.IDNumber, unitOf(s.Country),
                                        s.Phone, s.CSANumber, s.FINANumber, s.HealthCertDate, s.AgeCategory, s.Notes });
                                athId = _db.LastInsertId();
                                rep.Athletes++;
                                if (idk != null) athIds[idk] = athId; else rep.KeptSeparateNoId++;
                            }

                            string ek = Key(eid, "A:" + athId);
                            if (!entIds.ContainsKey(ek))
                            {
                                run(@"INSERT INTO entries(event_id,athlete_id,unit_id,bib_number,entry_time_seconds,
                                          entry_time,is_qualified,note)
                                      VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8)",
                                    new object[] { eid, athId, unitOf(s.Country), s.BibNumber,
                                        s.EntryTimeSeconds, s.EntryTime, s.IsQualified ? 1 : 0, s.Notes });
                                entIds[ek] = _db.LastInsertId();
                                rep.Entries++;
                            }
                            else rep.Warnings.Add("同一人重复报同一项目：" + (s.Name ?? "") + " " + (s.EventName ?? ""));
                            entryId = entIds[ek];
                        }

                        // ── 各赛次的分组名单 ──
                        var byStage = new Dictionary<string, StageAssignment>();
                        if (s.StageAssignments != null && s.StageAssignments.Count > 0)
                        {
                            foreach (var kv in s.StageAssignments) if (kv.Value != null) byStage[kv.Key] = kv.Value;
                        }
                        else if (s.Heat > 0)
                        {
                            var a0 = new StageAssignment();
                            a0.Stage = s.CurrentStage ?? "决赛"; a0.Heat = s.Heat; a0.Lane = s.Lane;
                            a0.EntryTimeSeconds = s.EntryTimeSeconds; a0.EntryTime = s.EntryTime;
                            byStage[a0.Stage] = a0;
                        }

                        foreach (var kv in byStage)
                        {
                            var a = kv.Value;
                            if (a.Heat <= 0) continue;
                            long rid = roundOf(eid, kv.Key, null, null, 0);

                            string lk = Key(rid, a.Heat, a.Lane);
                            if (heSeen.Contains(lk))
                            {
                                rep.Warnings.Add(string.Format("泳道冲突，已跳过：{0} {1} 第{2}组{3}道",
                                    s.Name ?? "", kv.Key, a.Heat, a.Lane));
                                continue;
                            }
                            heSeen.Add(lk);

                            string hk = Key(rid, a.Heat);
                            if (!heatSeen.Contains(hk))
                            {
                                run("INSERT OR IGNORE INTO heats(round_id,heat) VALUES(@p1,@p2)", new object[] { rid, a.Heat });
                                heatSeen.Add(hk);
                                rep.Heats++;
                            }

                            run(@"INSERT INTO heat_entries(round_id,heat,lane,entry_id,seed_time_seconds,seed_time)
                                  VALUES(@p1,@p2,@p3,@p4,@p5,@p6)",
                                new object[] { rid, a.Heat, a.Lane, entryId, a.EntryTimeSeconds, a.EntryTime });
                            long heId = _db.LastInsertId();
                            rep.HeatEntries++;

                            // 接力棒次挂在分组行上（按赛次记 —— 预赛决赛可以换人）
                            if (isRelay)
                            {
                                List<RelayLeg> lg;
                                if (legsByTeam.TryGetValue(Key(eid, s.Name), out lg))
                                    foreach (var l in lg)
                                    {
                                        if (l == null) continue;
                                        run(@"INSERT OR REPLACE INTO relay_legs(heat_entry_id,leg_order,swimmer_name,
                                                  swimmer_bib,swimmer_id_no,swimmer_birth)
                                              VALUES(@p1,@p2,@p3,@p4,@p5,@p6)",
                                            new object[] { heId, l.LegOrder, l.SwimmerName, l.SwimmerBibNumber,
                                                l.SwimmerIDNumber, l.SwimmerBirthDate });
                                        rep.RelayLegs++;
                                    }
                            }

                            // ── 本赛次成绩 ──
                            var r = FindResult(s, kv.Key, a.Heat);
                            if (r != null && (r.FinalTime > 0 || !string.IsNullOrEmpty(r.Status)))
                            {
                                string bak = null;
                                if (r.DsqBackupSplits != null && r.DsqBackupSplits.Count > 0)
                                    try { bak = JsonConvert.SerializeObject(r.DsqBackupSplits); } catch { }
                                run(@"UPDATE heat_entries SET final_time=@p2,rank=@p3,status=@p4,record_note=@p5,
                                          timing_source=@p6,touchpad_time=@p7,start_block_time=@p8,
                                          pb1_time=@p9,pb2_time=@p10,pb3_time=@p11,manual_left=@p12,manual_right=@p13,
                                          dsq_backup_splits=@p14,result_at=@p15
                                      WHERE id=@p1",
                                    new object[] { heId, r.FinalTime, r.Rank, r.Status, r.RecordNote, r.TimingSource,
                                        r.TouchpadTime, r.StartingBlockTime, r.PushButton1Time, r.PushButton2Time,
                                        r.PushButton3Time, r.ManualTouchTimeLeft, r.ManualTouchTimeRight, bak, now });
                                rep.Results++;

                                if (r.Splits != null)
                                    foreach (var sp in r.Splits)
                                    {
                                        if (sp == null || sp.IsDeleted || sp.Distance <= 0) continue;
                                        run(@"INSERT OR REPLACE INTO splits(heat_entry_id,distance,cumulative_time,
                                                  lap_time,timing_source,is_manual)
                                              VALUES(@p1,@p2,@p3,@p4,@p5,@p6)",
                                            new object[] { heId, sp.Distance, sp.CumulativeTime, sp.Time,
                                                sp.TimingSource, sp.IsManual ? 1 : 0 });
                                        rep.Splits++;
                                    }

                                // 接力每棒反应时散在 LaneResult.LegReactionTimes 里，按棒补回去
                                if (isRelay && r.LegReactionTimes != null)
                                    for (int i = 0; i < r.LegReactionTimes.Count; i++)
                                        run("UPDATE relay_legs SET reaction_time=@p3 WHERE heat_entry_id=@p1 AND leg_order=@p2",
                                            new object[] { heId, i + 1, r.LegReactionTimes[i] });
                            }
                        }
                    }

                // ══ 已确认的组：旧格式是「组别|性别|项目|赛次|组次」拼的字符串 ══
                if (p.ConfirmedHeats != null)
                    foreach (var key in p.ConfirmedHeats)
                    {
                        if (string.IsNullOrEmpty(key)) continue;
                        var a = key.Split('|');
                        if (a.Length < 5) continue;
                        int h; if (!int.TryParse(a[4], out h)) continue;
                        int dist; string stroke; int legs;
                        ParseEventName(a[2], out dist, out stroke, out legs);
                        string ek = Key(a[0] ?? "", a[1], dist, stroke, legs);
                        if (!evIds.ContainsKey(ek)) { rep.Warnings.Add("已确认组对不上项目：" + key); continue; }
                        string rk = Key(evIds[ek], a[3]);
                        if (!rndIds.ContainsKey(rk)) { rep.Warnings.Add("已确认组对不上赛次：" + key); continue; }
                        run("INSERT OR IGNORE INTO heats(round_id,heat) VALUES(@p1,@p2)", new object[] { rndIds[rk], h });
                        run("UPDATE heats SET confirmed_at=@p3 WHERE round_id=@p1 AND heat=@p2",
                            new object[] { rndIds[rk], h, now });
                    }

                // ══ 纪录 ══
                if (p.Records != null)
                    foreach (var r in p.Records)
                    {
                        if (r == null) continue;
                        int dist; string stroke; int legs;
                        ParseEventName(r.EventName, out dist, out stroke, out legs);
                        run(@"INSERT INTO records(abbr,record_type,ord,age_group,gender,distance,stroke,relay_legs,
                                  event_name,time_seconds,holder_name,holder_country,date,location,is_current)
                              VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,1)",
                            new object[] { AbbrOf(r.RecordType), r.RecordType, rep.Records, r.AgeGroup ?? "", r.Gender,
                                dist, stroke, legs, r.EventName,
                                r.TimeInSeconds > 0 ? r.TimeInSeconds : r.Time,
                                r.HolderName, r.HolderCountry, r.Date, r.Location });
                        rep.Records++;
                    }

                // ══ 工作人员 + 裁判名单 ══
                if (p.StaffList != null)
                    foreach (var st in p.StaffList)
                    {
                        if (st == null) continue;
                        run(@"INSERT INTO staff(ord,name,title,group_name,gender,referee_level,country,phone,note)
                              VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                            new object[] { rep.Staff, st.Name, st.Title, st.Group, st.Gender, st.RefereeLevel,
                                st.Country, st.Phone, st.Note });
                        rep.Staff++;
                    }
                if (p.Officials != null)
                    for (int i = 0; i < p.Officials.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(p.Officials[i])) continue;
                        run("INSERT INTO staff(ord,name,is_official) VALUES(@p1,@p2,1)",
                            new object[] { 1000 + i, p.Officials[i] });
                        rep.Staff++;
                    }

                // ══ 号段 → units.bib_ranges ══
                if (p.BibRanges != null)
                {
                    var byUnit = new Dictionary<string, List<string>>();
                    foreach (var b in p.BibRanges)
                    {
                        if (b == null || string.IsNullOrEmpty(b.Country)) continue;
                        if (!byUnit.ContainsKey(b.Country)) byUnit[b.Country] = new List<string>();
                        byUnit[b.Country].Add(b.Start + "-" + b.End);
                        rep.BibRanges++;
                    }
                    foreach (var kv in byUnit)
                    {
                        long? uid = unitOf(kv.Key);
                        if (uid == null) continue;
                        run("UPDATE units SET bib_ranges=@p2 WHERE id=@p1",
                            new object[] { uid.Value, string.Join(",", kv.Value.ToArray()) });
                    }
                }

                // ══ 团体总分 → units ══
                if (p.TeamScores != null)
                    foreach (var ts in p.TeamScores)
                    {
                        if (ts == null || string.IsNullOrEmpty(ts.TeamName)) continue;
                        long? uid = unitOf(ts.TeamName);
                        if (uid == null) continue;
                        run(@"UPDATE units SET score=@p2,individual_points=@p3,relay_points=@p4,record_bonus=@p5,
                                  gold=@p6,silver=@p7,bronze=@p8,score_rank=@p9 WHERE id=@p1",
                            new object[] { uid.Value, ts.TotalPoints, ts.IndividualPoints, ts.RelayPoints,
                                ts.RecordBonusPoints, ts.GoldCount, ts.SilverCount, ts.BronzeCount, ts.Rank });
                        rep.TeamScores++;
                    }

                // ══ 配置块 ══
                WriteSetting(run, "scoring",       p.ScoringConfig,  now);
                WriteSetting(run, "duration",      p.DurationConfig, now);
                WriteSetting(run, "program_book",  p.ProgramBook,    now);
                WriteSetting(run, "result_book",   p.ResultBook,     now);
                WriteSetting(run, "display_record_options", p.DisplayRecordOptions, now);
                WriteSetting(run, "lane_close",    p.LaneCloseSettings, now);
                WriteSetting(run, "wizard_draft",  p.WizardDraft,    now);
                WriteSetting(run, "dispute_log",   p.DisputeLog,     now);
                WriteSetting(run, "list_gender",   p.Genders,        now);
                WriteSetting(run, "list_stage",    p.Stages,         now);
                WriteSetting(run, "list_event",    p.Events,         now);
                WriteSetting(run, "list_heat_count", p.HeatCounts,   now);
                WriteSetting(run, "list_age_group",  p.AgeGroups,    now);

                run("INSERT INTO audit_log(at,operator,action,target,note) VALUES(@p1,@p2,'导入档案','competition',@p3)",
                    new object[] { now, Environment.MachineName, "从 CompetitionPackage 导入：" + (p.CompetitionName ?? "") });
            });

            return rep;
        }

        // 找这个赛次这一组的成绩：先按 赛次+组次 精确配，配不上退回按赛次配
        private static LaneResult FindResult(Swimmer s, string stage, int heat)
        {
            if (s.Results == null) return null;
            foreach (var r in s.Results) if (r != null && r.Stage == stage && r.Heat == heat) return r;
            foreach (var r in s.Results) if (r != null && r.Stage == stage) return r;
            if (s.Results.Count == 1 && string.IsNullOrEmpty(s.Results[0].Stage)) return s.Results[0];
            return null;
        }

        private static string AbbrOf(string recordType)
        {
            string t = recordType ?? "";
            if (t.Contains("世界青年")) return "WJ";
            if (t.Contains("全国青年")) return "NJ";
            if (t.Contains("世界"))     return "WR";
            if (t.Contains("亚洲"))     return "AR";
            if (t.Contains("全国"))     return "NR";
            if (t.Contains("赛会"))     return "MR";
            if (t.Contains("省"))       return "省R";
            return null;
        }

        private static void WriteSetting(Func<string, object[], int> run, string key, object value, string now)
        {
            if (value == null) return;
            string json;
            try { json = JsonConvert.SerializeObject(value); }
            catch { return; }
            run("INSERT OR REPLACE INTO settings(key,value,at) VALUES(@p1,@p2,@p3)", new object[] { key, json, now });
        }
    }
}
