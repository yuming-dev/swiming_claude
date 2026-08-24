using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 老程序 ↔ 新库 的过渡桥                                    2026-08-24
    //
    // 迁移分四步走，这里是【第 1 步】：库只写不读，读路径一处不动。
    //   1. 打开档案时把包导进 meet.db，并逐组自检比对   ← 现在这一步
    //   2. 比赛热路径改走 current_heat.db（触板不再序列化整包）
    //   3. 报表类只读路径切到库（输出可逐字比对老结果）
    //   4. GetHeatEntries 与整包读写下线
    //
    // 为什么不能一刀切：GetHeatEntries 返回的是内存里的 Swimmer 对象，下游
    // 有几十处直接改它的字段（改参赛号、CSV 导入回填组次泳道…）。直接换成
    // 从库里读，这些写会全部丢掉，而且丢得悄无声息。所以先让库跑成影子，
    // 用自检确认库和内存一致了，再一条一条把读路径搬过去。
    //
    // 【本类的任何失败都不许影响主程序】—— 全部 try/catch 吞掉只记日志。
    // 现阶段库是影子，坏了不该让比赛停下来。
    // ══════════════════════════════════════════════════════════════════════
    public class MeetDbBridge : IDisposable
    {
        // 内部拼键用的分隔符。写成 (char)1 而不是字符串转义 ——
        // 转义在跨工具传递时被吃掉过一次。
        private const char SEP = (char)1;

        private LocalMeetService _svc;
        private string _dbPath;
        private readonly Action<string> _log;

        /// <summary>当前档案对应的库；未打开时为 null。调用方必须判空。</summary>
        public LocalMeetService Service { get { return _svc; } }
        public string DbPath { get { return _dbPath; } }
        public bool IsOpen { get { return _svc != null; } }

        /// <summary>
        /// 库文件放在哪个目录下的 Database\ 里。留空则用程序所在目录。
        /// 做成可覆盖是为了别把路径钉死在 AppDomain.BaseDirectory 上 ——
        /// 换宿主(测试脚本、以后的服务进程)时那个值不是程序目录。
        /// </summary>
        public string BaseDir { get; set; }

        public MeetDbBridge(Action<string> log) { _log = log ?? delegate { }; }

        private void Log(string s) { try { _log(s); } catch { } }

        // ── 打开 / 关闭 ─────────────────────────────────────────────────
        /// <summary>
        /// 为某个赛事打开(或新建) meet.db。库文件跟 JSON 档案同目录同名，
        /// 便于一起备份、一起分发。
        /// </summary>
        public bool Open(string competitionName)
        {
            Close();
            try
            {
                string root = string.IsNullOrEmpty(BaseDir) ? AppDomain.CurrentDomain.BaseDirectory : BaseDir;
                string dir = Path.Combine(root, "Database");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _dbPath = Path.Combine(dir, SafeName(competitionName) + ".db");
                _svc = new LocalMeetService(_dbPath);
                return true;
            }
            catch (Exception ex)
            {
                _svc = null; _dbPath = null;
                Log("竞赛库打开失败(不影响比赛): " + ex.Message);
                return false;
            }
        }

        public void Close()
        {
            if (_svc == null) return;
            try { _svc.Dispose(); } catch { }
            _svc = null; _dbPath = null;
        }

        public void Dispose() { Close(); }

        private static string SafeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "meet";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // ── 导入 ────────────────────────────────────────────────────────
        /// <summary>
        /// 把刚加载的档案整体灌进库。只在【打开档案】时调一次 ——
        /// 这不违反禁止整包读写：导入本来就是一次性的批量装载，
        /// 之后的增删改必须走 IMeetService 的单行接口。
        /// </summary>
        public bool ImportPackage(CompetitionPackage pkg)
        {
            if (_svc == null || pkg == null) return false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rep = new PackageImporter(_svc.Db).Import(pkg);
                Log(string.Format("竞赛库已建: 项目{0} 赛次{1} 运动员{2} 报名{3} 分组{4}，{5}ms",
                    rep.Events, rep.Rounds, rep.Athletes, rep.Entries, rep.HeatEntries, sw.ElapsedMilliseconds));
                if (rep.Warnings.Count > 0)
                    Log("竞赛库导入提示 " + rep.Warnings.Count + " 条，首条: " + rep.Warnings[0]);
                BuildRoundIndex();
                return true;
            }
            catch (Exception ex)
            {
                Log("竞赛库导入失败(不影响比赛): " + ex.Message);
                return false;
            }
        }

        // ── 当前组 ↔ round_id 映射 ──────────────────────────────────────
        // 老程序用 (组别|性别|项目|赛次) 四个字符串定位，新库用 round_id。
        // 这张表是两边的翻译器，导入后建一次，改赛程时重建。
        private readonly Dictionary<string, long> _roundIx =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _eventIx =
            new Dictionary<string, long>(StringComparer.Ordinal);

        private static string RKey(string ageGroup, string gender, string eventName, string stage)
        {
            return (ageGroup ?? "") + SEP + (gender ?? "") + SEP
                 + (eventName ?? "") + SEP + (stage ?? "");
        }

        public void BuildRoundIndex()
        {
            _roundIx.Clear(); _eventIx.Clear();
            if (_svc == null) return;
            try
            {
                foreach (DataRow r in _svc.Db.Query(
                    @"SELECT r.id AS rid, e.id AS eid, e.age_group, e.gender, e.event_name, r.stage
                      FROM rounds r
                      JOIN round_events re ON re.round_id = r.id
                      JOIN events e        ON e.id = re.event_id").Rows)
                {
                    string k = RKey(Convert.ToString(r["age_group"]), Convert.ToString(r["gender"]),
                                    Convert.ToString(r["event_name"]), Convert.ToString(r["stage"]));
                    if (!_roundIx.ContainsKey(k)) _roundIx[k] = Convert.ToInt64(r["rid"]);
                    if (!_eventIx.ContainsKey(k)) _eventIx[k] = Convert.ToInt64(r["eid"]);
                }
            }
            catch (Exception ex) { Log("竞赛库索引重建失败: " + ex.Message); }
        }

        /// <summary>老程序的四字段 → round_id。找不到返回 0。</summary>
        public long ResolveRound(string ageGroup, string gender, string eventName, string stage)
        {
            long id;
            return _roundIx.TryGetValue(RKey(ageGroup, gender, eventName, stage), out id) ? id : 0L;
        }

        /// <summary>老程序的四字段 → event_id。找不到返回 0。</summary>
        public long ResolveEvent(string ageGroup, string gender, string eventName, string stage)
        {
            long id;
            return _eventIx.TryGetValue(RKey(ageGroup, gender, eventName, stage), out id) ? id : 0L;
        }

        // ── 自检 ────────────────────────────────────────────────────────
        public class CheckResult
        {
            public int HeatsChecked, HeatsMatched;
            public readonly List<string> Diffs = new List<string>();
            public bool AllMatched { get { return Diffs.Count == 0; } }
            public override string ToString()
            {
                if (AllMatched) return string.Format("竞赛库自检通过: {0} 个组逐人比对一致", HeatsChecked);
                return string.Format("竞赛库自检: {0}/{1} 组一致，{2} 处不一致，首条: {3}",
                    HeatsMatched, HeatsChecked, Diffs.Count, Diffs[0]);
            }
        }

        /// <summary>
        /// 逐组比对：库里每个组的 (泳道 → 姓名) 是否和档案里一模一样。
        /// 第 1 步里库不参与任何读，自检不通过也只记日志、不阻断 ——
        /// 它的作用是在真正切读路径之前把差异先暴露出来。
        /// </summary>
        public CheckResult SelfCheck(CompetitionPackage pkg)
        {
            var res = new CheckResult();
            if (_svc == null || pkg == null || pkg.Swimmers == null) return res;
            try
            {
                // 档案侧：按 (组别|性别|项目|赛次|组次) 归拢出 泳道→姓名
                var fromPkg = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
                foreach (var s in pkg.Swimmers)
                {
                    if (s == null || string.IsNullOrEmpty(s.EventName)) continue;
                    if ((s.Notes ?? "").StartsWith("接力队员")) continue;   // 队员子条目不占泳道

                    var stages = new List<StageAssignment>();
                    if (s.StageAssignments != null && s.StageAssignments.Count > 0)
                    { foreach (var kv in s.StageAssignments) if (kv.Value != null) stages.Add(kv.Value); }
                    else if (s.Heat > 0)
                    {
                        var a = new StageAssignment();
                        a.Stage = s.CurrentStage ?? "决赛"; a.Heat = s.Heat; a.Lane = s.Lane;
                        stages.Add(a);
                    }

                    foreach (var a in stages)
                    {
                        if (a.Heat <= 0) continue;
                        string k = RKey(s.AgeCategory, s.Gender, s.EventName, a.Stage) + SEP + a.Heat;
                        if (!fromPkg.ContainsKey(k)) fromPkg[k] = new SortedDictionary<int, string>();
                        if (!fromPkg[k].ContainsKey(a.Lane)) fromPkg[k][a.Lane] = s.Name ?? "";
                    }
                }

                foreach (var kv in fromPkg)
                {
                    var parts = kv.Key.Split(SEP);
                    if (parts.Length < 5) continue;
                    long rid = ResolveRound(parts[0], parts[1], parts[2], parts[3]);
                    int heat = int.Parse(parts[4]);
                    res.HeatsChecked++;

                    if (rid == 0)
                    { res.Diffs.Add(Desc(parts, heat) + " 库里找不到对应赛次"); continue; }

                    var fromDb = new SortedDictionary<int, string>();
                    foreach (var row in _svc.GetHeat(rid, heat))
                        if (row.Lane != null) fromDb[row.Lane.Value] = row.Name ?? "";

                    if (SameRoster(kv.Value, fromDb)) { res.HeatsMatched++; continue; }
                    res.Diffs.Add(string.Format("{0} 档案[{1}] 库[{2}]",
                        Desc(parts, heat), Fmt(kv.Value), Fmt(fromDb)));
                }
            }
            catch (Exception ex) { res.Diffs.Add("自检异常: " + ex.Message); }
            return res;
        }

        private static string Desc(string[] p, int heat)
        { return p[0] + p[1] + " " + p[2] + " " + p[3] + " 第" + heat + "组"; }

        private static bool SameRoster(SortedDictionary<int, string> a, SortedDictionary<int, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
            {
                string v;
                if (!b.TryGetValue(kv.Key, out v)) return false;
                if (!string.Equals(v, kv.Value, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static string Fmt(SortedDictionary<int, string> d)
        {
            return string.Join(" ", d.Select(kv => kv.Key + ":" + kv.Value).ToArray());
        }

        // ══════════════════════════════════════════════════════════════
        // 当前组库（迁移第 2 步）
        //
        // 原来每次触板都 AutoSaveData() → BuildCurrentPackage + 序列化整个
        // 1.2 MB 的包再写盘。400 米一组上百次事件，每次约 20 MB 分配，
        // 而 1.2 MB 的字符串直接进大对象堆(LOH 不压缩) —— 这就是那次内存
        // 涨到几 GB、"清内存"也降不下来的根。
        //
        // 改成：比赛中只写 current_heat.db 里那几行，几十 KB 的小文件。
        // 确认成绩时才回写大库 + 存一次 JSON。
        //
        // 【失败一律降级，不阻断比赛】LiveActive 为 false 时，主程序会退回
        // 原来的 AutoSaveData 路径，行为和改之前一模一样。
        // ══════════════════════════════════════════════════════════════
        private bool _liveActive;
        private long _liveRoundId;
        private int  _liveHeat;

        /// <summary>当前组库是否已就绪。false 时调用方必须走原来的保存路径。</summary>
        public bool LiveActive { get { return _liveActive; } }
        public long LiveRoundId { get { return _liveRoundId; } }
        public int  LiveHeat    { get { return _liveHeat; } }

        /// <summary>就位时调：把本组名单灌进当前组库并加锁。失败返回 false（调用方降级）。</summary>
        public bool LiveOpen(string ageGroup, string gender, string eventName, string stage, int heat, string op)
        {
            _liveActive = false;
            if (_svc == null || heat <= 0) return false;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                if (rid == 0)
                {
                    Log("当前组库: 库里找不到 " + (ageGroup ?? "") + gender + " " + eventName
                        + " " + stage + "，本组仍按原方式保存");
                    return false;
                }
                var live = _svc.OpenHeat(rid, heat, op);
                if (live == null || live.Lanes.Count == 0) return false;
                _liveRoundId = rid; _liveHeat = heat; _liveActive = true;
                Log(string.Format("当前组库已就绪: 第{0}组 {1}道（比赛中不再序列化整包）", heat, live.Lanes.Count));
                return true;
            }
            catch (Exception ex)
            {
                Log("当前组库打开失败，本组按原方式保存: " + ex.Message);
                return false;
            }
        }

        /// <summary>比赛中每来一个计时事件调一次。只写当前组库的几行。</summary>
        public bool LiveSaveLanes(IEnumerable<LiveLane> lanes)
        {
            if (!_liveActive || _svc == null || lanes == null) return false;
            try
            {
                foreach (var ln in lanes)
                {
                    _svc.UpdateLane(ln.Lane, ln);
                    if (ln.Splits != null)
                        foreach (var sp in ln.Splits)
                            _svc.UpdateSplit(ln.Lane, sp.Distance, sp.CumulativeTime, sp.LapTime, sp.TimingSource);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("当前组库写入失败(已降级为原方式): " + ex.Message);
                _liveActive = false;      // 一旦出问题就交回原路径，别让成绩没地方落
                return false;
            }
        }

        public void LiveSetRaceState(string state, string gunTime)
        {
            if (!_liveActive || _svc == null) return;
            try { _svc.SetRaceState(state, gunTime); } catch { }
        }

        /// <summary>确认成绩：回写竞赛库、查破纪录、解锁、清当前组库。</summary>
        public List<RecordBreak> LiveCommit(string op)
        {
            var empty = new List<RecordBreak>();
            if (!_liveActive || _svc == null) return empty;
            try
            {
                var breaks = _svc.CommitHeat(op);
                _liveActive = false;
                Log(string.Format("当前组库已回写竞赛库: 第{0}组{1}", _liveHeat,
                    breaks.Count > 0 ? "，破纪录 " + breaks.Count + " 项" : ""));
                return breaks;
            }
            catch (Exception ex)
            {
                _liveActive = false;
                Log("当前组库回写失败(成绩仍以档案为准): " + ex.Message);
                return empty;
            }
        }

        /// <summary>复位 / 重赛 / 切组：放弃本组，解锁并清空当前组库，不回写。</summary>
        public void LiveDiscard(string op)
        {
            if (_svc == null) return;
            try { if (_liveActive) _svc.DiscardHeat(op); } catch { }
            _liveActive = false;
        }
    }
}
