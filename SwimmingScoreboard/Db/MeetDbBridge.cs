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

        // ══════════════════════════════════════════════════════════════════
        // 保成绩重建 (2026-08-29)
        //
        // 为什么要有这个:
        //   ① 计时端要能在【主服务器停了】的情况下独立跑完比赛, 前提是本机
        //      有一份完整的日程和分组表。它是第一次启动时导入的 —— 但导入
        //      之后就冻住了, 编排端后来改的分组永远进不来, 断线时用的就是旧表。
        //   ② heat_entries.id 是 AUTOINCREMENT。计时端和主服务器的 id 能对上,
        //      纯粹因为两边导的是同一个包、同样的顺序。只要有一边单独刷新过,
        //      id 就会错位 —— 成绩会【写到别人身上】, 而且不报错。
        //
        // 做法: 不做增量, 而是整个重导。重导后 id 是"包"的确定性函数,
        //   两台机器各导各的也必然一致, ② 顺带就没了。成绩先取出来、后贴回去。
        //
        // 贴回去用【自然键】(组别|性别|项目|赛次|组次|道次) —— 跟回推、跟
        //   _confirmedHeats 用的是同一套口径。实测这个键在有成绩的行上唯一。

        private static readonly string[] ResultCols = new string[] {
            "final_time","rank","promotion_mark","score","status","dsq_code","dsq_leg",
            "dsq_backup_splits","record_note","timing_source","reaction_time","touchpad_time",
            "start_block_time","pb1_time","pb2_time","pb3_time","manual_left","manual_right",
            "result_at","dispute_note","checkin_status","checkin_at","promoted_from","promoted_rank"
        };

        private class SavedRow
        {
            public string Ag, Gd, Ev, St;
            public int Heat, Lane;
            public readonly Dictionary<string, object> Cols = new Dictionary<string, object>();
            public readonly List<object[]> Splits = new List<object[]>();
            public readonly List<object[]> Legs = new List<object[]>();
            public string Key()
            {
                return (Ag ?? "") + "|" + (Gd ?? "") + "|" + (Ev ?? "") + "|" + (St ?? "")
                     + "|" + Heat + "|" + Lane;
            }
        }

        /// <summary>
        /// 按最新的包重建竞赛库, 已经跑出来的成绩原样保留。
        /// 只在自检发现库和包对不上时调 —— 平时一次也不会跑。
        /// </summary>
        public bool RebuildFromPackage(CompetitionPackage pkg)
        {
            if (_local == null || pkg == null) return false;
            var saved = new List<SavedRow>();
            var savedHeats = new List<object[]>();
            try
            {
                // ── 1. 把成绩取出来 ──────────────────────────────────────
                string cols = string.Join(",", Array.ConvertAll(ResultCols, delegate(string s) { return "he." + s; }));
                var t = _local.Db.Query(
                    "SELECT e.age_group,e.gender,e.event_name,r.stage,he.heat,he.lane,he.id," + cols + " " +
                    "FROM heat_entries he " +
                    "JOIN rounds r ON r.id=he.round_id " +
                    "JOIN entries en ON en.id=he.entry_id " +
                    "JOIN events e ON e.id=en.event_id " +
                    "WHERE he.final_time IS NOT NULL OR he.status IS NOT NULL OR he.result_at IS NOT NULL");
                foreach (System.Data.DataRow row in t.Rows)
                {
                    var sr = new SavedRow();
                    sr.Ag = SS(row["age_group"]); sr.Gd = SS(row["gender"]);
                    sr.Ev = SS(row["event_name"]); sr.St = SS(row["stage"]);
                    sr.Heat = Convert.ToInt32(row["heat"]); sr.Lane = Convert.ToInt32(row["lane"]);
                    foreach (string c in ResultCols)
                        sr.Cols[c] = row[c] == DBNull.Value ? null : row[c];

                    long heid = Convert.ToInt64(row["id"]);
                    var sp = _local.Db.Query(
                        "SELECT distance,cumulative_time,lap_time,rank_at,timing_source,is_manual " +
                        "FROM splits WHERE heat_entry_id=@p1", heid);
                    foreach (System.Data.DataRow s in sp.Rows)
                        sr.Splits.Add(new object[] { s["distance"], s["cumulative_time"], s["lap_time"],
                                                     s["rank_at"], s["timing_source"], s["is_manual"] });

                    var lg = _local.Db.Query(
                        "SELECT leg_order,reaction_time,leg_time,cumulative_time,rank_at " +
                        "FROM relay_legs WHERE heat_entry_id=@p1", heid);
                    foreach (System.Data.DataRow g in lg.Rows)
                        sr.Legs.Add(new object[] { g["leg_order"], g["reaction_time"], g["leg_time"],
                                                   g["cumulative_time"], g["rank_at"] });
                    saved.Add(sr);
                }

                // 组一级的确认信息(谁、什么时候确认的)也要留住 —— 赛程树的"已完赛"读它。
                var ht = _local.Db.Query(
                    "SELECT e.age_group,e.gender,e.event_name,r.stage,h.heat," +
                    "       h.gun_time,h.started_at,h.confirmed_at,h.confirmed_by,h.operator " +
                    "FROM heats h JOIN rounds r ON r.id=h.round_id " +
                    "JOIN round_events re ON re.round_id=r.id JOIN events e ON e.id=re.event_id " +
                    "WHERE h.confirmed_at IS NOT NULL OR h.gun_time IS NOT NULL");
                foreach (System.Data.DataRow row in ht.Rows)
                    savedHeats.Add(new object[] { SS(row["age_group"]), SS(row["gender"]), SS(row["event_name"]),
                        SS(row["stage"]), Convert.ToInt32(row["heat"]), row["gun_time"], row["started_at"],
                        row["confirmed_at"], row["confirmed_by"], row["operator"] });

                Log(string.Format("重建竞赛库: 先保住 {0} 条成绩 / {1} 个组的确认信息", saved.Count, savedHeats.Count));

                // ── 2. 落一份成绩快照到磁盘 ──────────────────────────────
                // 万一下面贴回去出岔子, 这个文件是唯一能人工救回来的东西。
                // 先写盘再动库, 顺序不能反。
                string snap = null;
                try
                {
                    snap = (_dbPath ?? "meet") + ".results-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                    System.IO.File.WriteAllText(snap,
                        Newtonsoft.Json.JsonConvert.SerializeObject(saved, Newtonsoft.Json.Formatting.Indented),
                        System.Text.Encoding.UTF8);
                    Log("成绩快照已存: " + System.IO.Path.GetFileName(snap));
                }
                catch (Exception ex) { Log("【注意】成绩快照写盘失败, 本次不重建: " + ex.Message); return false; }

                // ── 3. 清空 + 重导 ───────────────────────────────────────
                // sqlite_sequence 必须一起清, 否则 AUTOINCREMENT 接着旧号往下走,
                // id 就不再是"包"的确定性函数, 两台机器又对不上了。
                try { _local.Db.ExecuteNonQuery("PRAGMA foreign_keys=OFF"); } catch { }
                var tabs = _local.Db.Query(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
                foreach (System.Data.DataRow row in tabs.Rows)
                    try { _local.Db.ExecuteNonQuery("DELETE FROM \"" + SS(row["name"]) + "\""); } catch { }
                try { _local.Db.ExecuteNonQuery("DELETE FROM sqlite_sequence"); } catch { }

                var rep = new PackageImporter(_local.Db).Import(pkg);
                Log(string.Format("重建竞赛库: 已按最新编排重导 项目{0} 赛次{1} 运动员{2} 报名{3} 分组{4}",
                    rep.Events, rep.Rounds, rep.Athletes, rep.Entries, rep.HeatEntries));

                // ── 4. 成绩贴回去 ────────────────────────────────────────
                int back = 0;
                var missed = new List<string>();
                foreach (var sr in saved)
                {
                    var hit = _local.Db.Query(
                        "SELECT he.id FROM heat_entries he " +
                        "JOIN rounds r ON r.id=he.round_id " +
                        "JOIN entries en ON en.id=he.entry_id " +
                        "JOIN events e ON e.id=en.event_id " +
                        "WHERE e.age_group=@p1 AND e.gender=@p2 AND e.event_name=@p3 " +
                        "AND r.stage=@p4 AND he.heat=@p5 AND he.lane=@p6",
                        sr.Ag, sr.Gd, sr.Ev, sr.St, sr.Heat, sr.Lane);
                    if (hit.Rows.Count == 0) { missed.Add(sr.Key()); continue; }
                    long id = Convert.ToInt64(hit.Rows[0]["id"]);

                    var sets = new List<string>();
                    var ps = new List<object>();
                    int n = 1;
                    foreach (string c in ResultCols)
                    { sets.Add(c + "=@p" + n); ps.Add(sr.Cols[c]); n++; }
                    ps.Add(id);
                    _local.Db.ExecuteNonQuery(
                        "UPDATE heat_entries SET " + string.Join(",", sets.ToArray()) + " WHERE id=@p" + n,
                        ps.ToArray());

                    _local.Db.ExecuteNonQuery("DELETE FROM splits WHERE heat_entry_id=@p1", id);
                    foreach (var s in sr.Splits)
                        _local.Db.ExecuteNonQuery(
                            "INSERT INTO splits(heat_entry_id,distance,cumulative_time,lap_time,rank_at," +
                            "timing_source,is_manual) VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                            id, s[0], s[1], s[2], s[3], s[4], s[5]);
                    foreach (var g in sr.Legs)
                        _local.Db.ExecuteNonQuery(
                            "UPDATE relay_legs SET reaction_time=@p3,leg_time=@p4,cumulative_time=@p5,rank_at=@p6 " +
                            "WHERE heat_entry_id=@p1 AND leg_order=@p2",
                            id, g[0], g[1], g[2], g[3], g[4]);
                    back++;
                }

                int hback = 0;
                foreach (var h in savedHeats)
                {
                    long rid = ResolveRound(SS(h[0]), SS(h[1]), SS(h[2]), SS(h[3]));
                    if (rid == 0) continue;
                    hback += _local.Db.ExecuteNonQuery(
                        "UPDATE heats SET gun_time=@p3,started_at=@p4,confirmed_at=@p5,confirmed_by=@p6,operator=@p7 " +
                        "WHERE round_id=@p1 AND heat=@p2",
                        rid, h[4], h[5], h[6], h[7], h[8], h[9]);
                }

                // ── 5. 校验 ──────────────────────────────────────────────
                // 少一条都要喊。成绩悄悄少掉是这个项目最不能接受的事。
                if (missed.Count > 0)
                {
                    Log(string.Format("【注意】重建后有 {0} 条成绩在新编排里找不到位置(组次/道次被改过?): {1}",
                        missed.Count, string.Join(" ; ", missed.GetRange(0, Math.Min(5, missed.Count)).ToArray())));
                    Log("【注意】这些成绩没丢, 在快照文件里: " + System.IO.Path.GetFileName(snap ?? ""));
                }
                Log(string.Format("重建竞赛库完成: 成绩 {0}/{1} 条已归位, 组确认信息 {2} 个",
                    back, saved.Count, hback));
                BuildRoundIndex();
                return true;
            }
            catch (Exception ex)
            {
                Log("【注意】重建竞赛库失败: " + ex.Message);
                Log("【注意】成绩快照在 Database 目录下 .results-*.json, 不要删");
                return false;
            }
        }

        private static string SS(object o) { return o == null || o == DBNull.Value ? "" : o.ToString(); }

        /// <summary>
        /// 2026-08-29 断线补传用: 在 LiveCommit 之【前】抓一份当前组快照。
        /// LiveCommit 会把当前组库清掉, 事后再想取就没有了。
        /// </summary>
        public LiveHeat PeekLiveHeat()
        {
            if (!_liveActive || _local == null) return null;
            try { return _local.GetLiveHeat(); }
            catch (Exception ex) { Log("取当前组快照失败: " + ex.Message); return null; }
        }

        /// <summary>
        /// 2026-08-29 主服务器侧: 把计时端送来的一组成绩写进【自己的】竞赛库。
        /// 用于两种情况: ① 计时端联机确认; ② 计时端断线期间跑的组, 重连后补传上来。
        ///
        /// 回写逻辑直接复用 CommitHeatFrom —— 跟联机实时回写是同一份代码, 两条路
        /// 不可能写出两种结果。里面全是 UPDATE ... WHERE id 和 splits 先删后插,
        /// 所以【同一组重复补传是安全的】, 这正是补传敢用"没收到回执就重发"的底气。
        /// </summary>
        public List<RecordBreak> CommitHeatFromWire(LiveHeat live, string op)
        {
            var empty = new List<RecordBreak>();
            if (_local == null || live == null) return empty;
            try
            {
                var breaks = _local.CommitHeatFrom(live, op);
                Log(string.Format("计时端第{0}组成绩已写入竞赛库{1}", live.Heat,
                    breaks.Count > 0 ? "，破纪录 " + breaks.Count + " 项" : ""));
                return breaks;
            }
            catch (Exception ex)
            {
                Log("【注意】写入计时端送来的成绩失败: " + ex.Message);
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
