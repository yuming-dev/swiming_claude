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

        // 竞赛管理库的访问方: 单机时就是 _local; 联网计时端时是 RemoteMeetService。
        // 当前组库【永远是本机的】—— 比赛中的高频写不许过网, 这是整个设计的核心。
        private IMeetService _meet;
        private LocalMeetService _local;
        private WebSocketRpcTransport _rpc;
        private string _dbPath;
        private Action<string> _log;

        /// <summary>true = 连远端主服务器取竞赛数据; false = 本机开库。</summary>
        public bool IsRemote { get { return _rpc != null; } }

        /// <summary>联网计时端: 主服务器地址。留空 = 单机模式。</summary>
        public string ServerHost;
        public int ServerPort = 3002;

        /// <summary>竞赛管理库的访问方(本机或远端)；未打开时为 null。</summary>
        public IMeetService Service { get { return _meet; } }
        /// <summary>本机那份(单机模式下和 Service 是同一个)。当前组库只认它。</summary>
        public LocalMeetService Local { get { return _local; } }
        public string DbPath { get { return _dbPath; } }
        public bool IsOpen { get { return _meet != null; } }

        /// <summary>
        /// 库文件放在哪个目录下的 Database\ 里。留空则用程序所在目录。
        /// 做成可覆盖是为了别把路径钉死在 AppDomain.BaseDirectory 上 ——
        /// 换宿主(测试脚本、以后的服务进程)时那个值不是程序目录。
        /// </summary>
        public string BaseDir { get; set; }

        /// <summary>
        /// 2026-08-28 补接日志。MainWindow 里这个对象是字段初始化器 new 出来的,
        /// 那里还不能引用实例方法 AddLog, 所以当初传了 null —— 结果【竞赛库这一层
        /// 所有日志从一开始就进了黑洞】: "竞赛库已建"、"库里找不到某某项目"、
        /// "连不上主服务器, 本组按单机模式跑" 一条都没输出过, 出了问题毫无线索。
        /// 界面初始化好之后调一次这个把日志接上。
        /// </summary>
        public void SetLogger(Action<string> log) { if (log != null) _log = log; }

        public MeetDbBridge(Action<string> log)
        {
            _log = log ?? delegate { };
            LoadServiceConfig();
        }

        /// <summary>
        /// 决定这台机器是单机开库还是连主服务器。两个来源, 命令行优先:
        ///   命令行  --meet-server 192.168.1.10[:3002]
        ///   配置文件 程序目录\meet_service.json
        ///       { "Mode": "remote", "Host": "192.168.1.10", "Port": 3002 }
        /// 都没有 = 单机, 跟现在完全一样。
        ///
        /// 这么设计是为了让计时端换一台机器只改一个文件, 不用重新编译;
        /// 而单机小比赛什么都不配就能用, 不必先架服务器。
        /// </summary>
        private void LoadServiceConfig()
        {
            try
            {
                foreach (var a in Environment.GetCommandLineArgs())
                {
                    if (!a.StartsWith("--meet-server=", StringComparison.OrdinalIgnoreCase)) continue;
                    ApplyHostSpec(a.Substring("--meet-server=".Length));
                    return;
                }
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (string.Equals(args[i], "--meet-server", StringComparison.OrdinalIgnoreCase))
                    { ApplyHostSpec(args[i + 1]); return; }

                string root = string.IsNullOrEmpty(BaseDir) ? AppDomain.CurrentDomain.BaseDirectory : BaseDir;
                string cfg = Path.Combine(root, "meet_service.json");
                if (!File.Exists(cfg)) return;
                var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(cfg, Encoding.UTF8));
                string mode = o["Mode"] != null ? o["Mode"].ToString() : "local";
                if (!string.Equals(mode, "remote", StringComparison.OrdinalIgnoreCase)) return;
                ServerHost = o["Host"] != null ? o["Host"].ToString() : null;
                if (o["Port"] != null) ServerPort = (int)o["Port"];
            }
            catch (Exception ex) { Log("读竞赛服务配置失败, 按单机跑: " + ex.Message); }
        }

        private void ApplyHostSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            spec = spec.Trim();
            int i = spec.LastIndexOf(':');
            if (i > 0)
            {
                int port;
                if (int.TryParse(spec.Substring(i + 1), out port)) { ServerHost = spec.Substring(0, i); ServerPort = port; return; }
            }
            ServerHost = spec;
        }

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

                // 本机这份永远要开: 联网时它只用来装当前组库(current_heat.db),
                // 比赛中的每一次触板都写在这里, 一步都不过网。
                _local = new LocalMeetService(_dbPath);

                if (string.IsNullOrWhiteSpace(ServerHost))
                {
                    _meet = _local;                       // 单机
                    BuildRoundIndex();
                    return true;
                }

                _rpc = new WebSocketRpcTransport(ServerHost, ServerPort, _log);
                if (!_rpc.Connect(10000))
                {
                    // 连不上就退回本机库。宁可用本机的旧数据继续比赛,
                    // 也不能因为网络不通把整台计时机卡死。
                    Log("连不上主服务器 " + ServerHost + ":" + ServerPort + "，本组按单机模式跑");
                    _rpc.Dispose(); _rpc = null;
                    _meet = _local;
                    BuildRoundIndex();
                    return true;
                }
                _meet = new RemoteMeetService(_rpc);
                Log("竞赛数据走主服务器 " + ServerHost + ":" + ServerPort + "（当前组仍写本机）");
                BuildRoundIndex();
                return true;
            }
            catch (Exception ex)
            {
                Close();
                Log("竞赛库打开失败(不影响比赛): " + ex.Message);
                return false;
            }
        }

        public void Close()
        {
            if (_rpc != null) { try { _rpc.Dispose(); } catch { } _rpc = null; }
            if (_local != null) { try { _local.Dispose(); } catch { } _local = null; }
            _meet = null; _dbPath = null;
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
            if (_local == null || pkg == null) return false;
            try
            {
                // 2026-08-28 导入【不是幂等的】: 里面绝大多数是裸 INSERT(只有少数几张表
                //   用了 OR IGNORE / OR REPLACE), 第二次跑必然撞唯一约束 constraint failed。
                //   而这个库是【会长成绩的】—— 比赛跑出来的成绩就存在里面, 所以也不能
                //   先清空再导。所以: 库里已经有数据就跳过, 不再重导。
                //   历史上这个失败还连累了赛次索引(索引原来只在导入成功时才建, 于是
                //   第二次启动之后成绩就静默不回写 meet.db 了 —— 已另行修正)。
                //   要强制重导: 关程序, 删掉 Database\<赛事名>.db, 再启动。
                long already = 0;
                try { already = Convert.ToInt64(_local.Db.ExecuteScalar("SELECT COUNT(*) FROM rounds")); }
                catch { already = 0; }
                if (already > 0)
                {
                    Log(string.Format("竞赛库已有数据({0} 个赛次), 跳过导入。要重导请关程序删掉 {1} 再启动",
                        already, System.IO.Path.GetFileName(_dbPath ?? "")));
                    BuildRoundIndex();
                    return true;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rep = new PackageImporter(_local.Db).Import(pkg);
                Log(string.Format("竞赛库已建: 项目{0} 赛次{1} 运动员{2} 报名{3} 分组{4}，{5}ms",
                    rep.Events, rep.Rounds, rep.Athletes, rep.Entries, rep.HeatEntries, sw.ElapsedMilliseconds));
                if (rep.Warnings.Count > 0)
                    Log("竞赛库导入提示 " + rep.Warnings.Count + " 条，首条: " + rep.Warnings[0]);
                BuildRoundIndex();
                return true;
            }
            catch (Exception ex)
            {
                // 导入失败不影响比赛(内存/JSON 照常), 但要说清楚: 索引仍会在 Open() 里建,
                // 所以 meet.db 回写不受这个失败影响。第二次导入撞唯一约束是已知问题。
                Log("竞赛库导入失败(不影响比赛, 赛次索引另行建立): " + ex.Message);
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
            if (_meet == null) return;
            try
            {
                // 走服务接口而不是直接查本机库 —— 远端模式下本机那个 meet.db 是空的,
                // 名单和日程都在服务器那边。GetSchedule 本地远端通用。
                foreach (var row in _meet.GetSchedule())
                {
                    string k = RKey(row.AgeGroup, row.Gender, row.EventName, row.Stage);
                    if (!_roundIx.ContainsKey(k)) _roundIx[k] = row.RoundId;
                    if (!_eventIx.ContainsKey(k)) _eventIx[k] = row.EventId;
                }
                // 2026-08-28 索引空了就是"这一组在库里对不上号"的根源, 必须喊出来。
                //   原来它只在 ImportPackage 成功时才建, 而导入第二次就会撞唯一约束
                //   (constraint failed) —— 于是索引永远是空的, 成绩静默不回写 meet.db。
                if (_roundIx.Count == 0) Log("【注意】赛次索引为空 —— 当前组库建不起来, 成绩不会回写 meet.db");
                else Log(string.Format("赛次索引已建: {0} 条", _roundIx.Count));
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
            if (_local == null || pkg == null || pkg.Swimmers == null) return res;
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
                    foreach (var row in _local.GetHeat(rid, heat))
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
            if (_meet == null || heat <= 0) return false;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                if (rid == 0)
                {
                    Log("当前组库: 库里找不到 " + (ageGroup ?? "") + gender + " " + eventName
                        + " " + stage + "，本组仍按原方式保存");
                    return false;
                }
                // 名单从竞赛库取(可能在远端), 取回来灌进【本机】当前组库。
                // 之后整场比赛只写本机那个小库。
                var live = _meet.OpenHeat(rid, heat, op);
                if (live == null || live.Lanes.Count == 0) return false;
                if (!ReferenceEquals(_meet, _local)) _local.SeedLiveHeat(live);
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
            if (!_liveActive || _local == null || lanes == null) return false;
            try
            {
                // 永远写本机。远端模式下这里一次网都不过 —— 这是"计时器专心
                // 做好计时"落到代码上的那一行。
                foreach (var ln in lanes)
                {
                    _local.UpdateLane(ln.Lane, ln);
                    if (ln.Splits != null)
                        foreach (var sp in ln.Splits)
                            _local.UpdateSplit(ln.Lane, sp.Distance, sp.CumulativeTime, sp.LapTime, sp.TimingSource);
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
            if (!_liveActive || _local == null) return;
            try { _local.SetRaceState(state, gunTime); } catch { }
        }

        /// <summary>确认成绩：回写竞赛库、查破纪录、解锁、清当前组库。</summary>
        public List<RecordBreak> LiveCommit(string op)
        {
            var empty = new List<RecordBreak>();
            if (!_liveActive || _meet == null) return empty;
            try
            {
                // 成绩在本机小库里。远端模式必须把它整份带过去 ——
                // 服务器那台机器的当前组库是空的, 它读自己等于回写一组空成绩。
                var breaks = ReferenceEquals(_meet, _local)
                    ? _local.CommitHeat(op)
                    : _meet.CommitHeatFrom(_local.GetLiveHeat(), op);
                if (!ReferenceEquals(_meet, _local)) _local.DiscardHeat(op);   // 清本机小库
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
            if (_local == null) return;
            try { if (_liveActive) _local.DiscardHeat(op); } catch { }
            try { if (_liveActive && !ReferenceEquals(_meet, _local)) _meet.DiscardHeat(op); } catch { }
            _liveActive = false;
        }
    }
}
