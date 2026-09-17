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

        // ── 并组落库 ────────────────────────────────────────────── 2026-09-12
        // 老程序那边改的是内存里的 Swimmer, 这里把同一件事记进库:
        // 谁挪到了哪一道、原组标成 cancelled 并记并入了哪组, 外加一条审计。
        //
        // laneMoves 是"原道次 → 新道次"。库里认的是 heat_entries.id, 所以先把
        // 源组的泳道表拉下来, 按道次翻成 id —— 老程序那边没有这个 id, 只能这么对。
        //
        // 两条规矩照旧:
        //   · 主服务器正在计时时一个字都不往库里写 (skipDuringRace, 见 InRaceNoDbWrite)。
        //   · 【本类的任何失败都不许影响主程序】—— 全部吞掉只记日志。
        public void MergeHeatsInDb(string ageGroup, string gender, string eventName, string stage,
                                   int srcHeat, int dstHeat, Dictionary<int, int> laneMoves,
                                   string reason, string op, bool skipDuringRace)
        {
            if (_meet == null) return;
            if (skipDuringRace)
            {
                Log("比赛中不写竞赛库: 第" + srcHeat + "组的并组只落在本机 json");
                return;
            }
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                if (rid <= 0)
                {
                    Log("并组未落库: 赛次索引里找不到 " + gender + ageGroup + eventName + stage);
                    return;
                }
                if (dstHeat > 0)
                {
                    var map = new Dictionary<long, int>();
                    foreach (var row in _meet.GetHeat(rid, srcHeat))
                    {
                        if (row.Lane == null) continue;
                        int newLane;
                        if (laneMoves == null || !laneMoves.TryGetValue(row.Lane.Value, out newLane)) continue;
                        map[row.Id] = newLane;
                    }
                    if (map.Count == 0)
                    {
                        Log("并组未落库: 库里第" + srcHeat + "组对不上道次(可能这一组还没导进库)");
                        return;
                    }
                    _meet.MergeHeats(rid, srcHeat, dstHeat, map, op);
                    Log(string.Format("并组已落库: 第{0}组 → 第{1}组, {2} 人", srcHeat, dstHeat, map.Count));
                }
                else
                {
                    _meet.CancelHeat(rid, srcHeat, string.IsNullOrEmpty(reason) ? "取消" : reason, op);
                    Log("取消组已落库: 第" + srcHeat + "组");
                }
            }
            catch (Exception ex)
            {
                Log("并组落库失败(本机数据不受影响): " + ex.Message);
            }
        }

        /// <summary>
        /// 2026-09-13 记一条审计。给"解锁本组成绩"这种发生在老程序那一侧、
        /// 但必须留痕的动作用。【失败不许影响主程序】, 照例吞掉只记日志。
        /// </summary>
        public void LogAudit(string action, string target, string oldValue, string newValue, string note, string op)
        {
            if (_meet == null) return;
            try { _meet.LogAudit(action, target, oldValue, newValue, note, op); }
            catch (Exception ex) { Log("写审计失败(不影响操作): " + ex.Message); }
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
        ///
        /// 【在旁边建新库, 建成了再换】—— 全程不动原库。
        /// 第一版是"先清空原库再重导", 重导失败时库已经空了, 真丢过一整组成绩。
        /// 现在任何一步失败, 原库一个字节都没被碰过, 删掉临时文件就完事。
        /// </summary>
        public bool RebuildFromPackage(CompetitionPackage pkg)
        {
            if (_local == null || pkg == null || string.IsNullOrEmpty(_dbPath)) return false;

            string newPath = _dbPath + ".new";
            string bakPath = _dbPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            bool wasLocal = ReferenceEquals(_meet, _local);

            List<SavedRow> saved;
            List<object[]> savedHeats;
            try { saved = SnapshotResults(_local.Db, out savedHeats); }
            catch (Exception ex) { Log("【注意】取成绩快照失败, 本次不重建: " + ex.Message); return false; }
            Log(string.Format("重建竞赛库: 先保住 {0} 条成绩 / {1} 个组的确认信息", saved.Count, savedHeats.Count));

            // 快照先写盘。万一后面全盘出错, 这个文件是唯一能人工救回来的东西 ——
            // 上一次事故就是它救的命。写盘失败就不往下走。
            string snap;
            try
            {
                snap = _dbPath + ".results-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                System.IO.File.WriteAllText(snap,
                    Newtonsoft.Json.JsonConvert.SerializeObject(saved, Newtonsoft.Json.Formatting.Indented),
                    System.Text.Encoding.UTF8);
                Log("成绩快照已存: " + System.IO.Path.GetFileName(snap));
            }
            catch (Exception ex) { Log("【注意】成绩快照写盘失败, 本次不重建: " + ex.Message); return false; }

            int back = 0, hback = 0;
            var missed = new List<string>();
            try
            {
                if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);
                using (var tmp = new MeetDb(newPath))
                {
                    var rep = new PackageImporter(tmp).Import(pkg);
                    long nr = Convert.ToInt64(tmp.ExecuteScalar("SELECT COUNT(*) FROM rounds"));
                    long nh = Convert.ToInt64(tmp.ExecuteScalar("SELECT COUNT(*) FROM heat_entries"));
                    if (nr == 0 || nh == 0) throw new Exception("新库导完是空的(赛次" + nr + " 分组" + nh + ")");
                    Log(string.Format("重建竞赛库: 新库已按最新编排导好 项目{0} 赛次{1} 运动员{2} 报名{3} 分组{4}",
                        rep.Events, rep.Rounds, rep.Athletes, rep.Entries, rep.HeatEntries));

                    back = RestoreResults(tmp, saved, missed);
                    hback = RestoreHeatMarks(tmp, savedHeats);

                    // 成绩必须全部归位才换库。差一条都不换 —— 宁可继续用旧库。
                    if (missed.Count > 0)
                    {
                        Log(string.Format("【注意】有 {0} 条成绩在新编排里找不到位置(组次/道次被改过?), 本次【不换库】: {1}",
                            missed.Count, string.Join(" ; ", missed.GetRange(0, Math.Min(5, missed.Count)).ToArray())));
                        Log("【注意】原库原样保留, 比赛不受影响。请核对这几个人的编排后再试。");
                        throw new Exception("成绩归位不全, 已放弃换库");
                    }
                    tmp.Checkpoint();
                }

                // ── 到这里新库已经建好、成绩已全部归位, 才换 ──
                _local.Dispose(); _local = null;
                try { System.IO.File.Replace(newPath, _dbPath, bakPath); }
                catch
                {
                    // 有的文件系统不支持 Replace, 退回复制
                    System.IO.File.Copy(_dbPath, bakPath, true);
                    System.IO.File.Copy(newPath, _dbPath, true);
                    try { System.IO.File.Delete(newPath); } catch { }
                }
                _local = new LocalMeetService(_dbPath);
                if (wasLocal) _meet = _local;
                BuildRoundIndex();
                Log(string.Format("重建竞赛库完成: 成绩 {0} 条已归位, 组确认信息 {1} 个; 旧库留作 {2}",
                    back, hback, System.IO.Path.GetFileName(bakPath)));
                return true;
            }
            catch (Exception ex)
            {
                Log("【注意】重建竞赛库失败, 原库未改动, 继续用原库: " + ex.Message);
                try { if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath); } catch { }
                if (_local == null)
                {
                    // 换库中途出的岔子, 得把原库重新打开, 否则整台机器没库可用
                    try
                    {
                        _local = new LocalMeetService(_dbPath);
                        if (wasLocal) _meet = _local;
                        BuildRoundIndex();
                        Log("原库已重新打开");
                    }
                    catch (Exception e2) { Log("【严重】原库重开失败: " + e2.Message); }
                }
                return false;
            }
        }

        /// <summary>把已有成绩连同分段、接力棒次一起取出来, 按自然键存。</summary>
        private List<SavedRow> SnapshotResults(MeetDb db, out List<object[]> heatMarks)
        {
            var saved = new List<SavedRow>();
            string cols = string.Join(",", Array.ConvertAll(ResultCols, delegate(string s) { return "he." + s; }));
            var t = db.Query(
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
                foreach (string c in ResultCols) sr.Cols[c] = row[c] == DBNull.Value ? null : row[c];

                long heid = Convert.ToInt64(row["id"]);
                var sp = db.Query("SELECT distance,cumulative_time,lap_time,rank_at,timing_source,is_manual " +
                                  "FROM splits WHERE heat_entry_id=@p1", heid);
                foreach (System.Data.DataRow s in sp.Rows)
                    sr.Splits.Add(new object[] { s["distance"], s["cumulative_time"], s["lap_time"],
                                                 s["rank_at"], s["timing_source"], s["is_manual"] });
                var lg = db.Query("SELECT leg_order,reaction_time,leg_time,cumulative_time,rank_at " +
                                  "FROM relay_legs WHERE heat_entry_id=@p1", heid);
                foreach (System.Data.DataRow g in lg.Rows)
                    sr.Legs.Add(new object[] { g["leg_order"], g["reaction_time"], g["leg_time"],
                                               g["cumulative_time"], g["rank_at"] });
                saved.Add(sr);
            }

            heatMarks = new List<object[]>();
            var ht = db.Query(
                "SELECT e.age_group,e.gender,e.event_name,r.stage,h.heat," +
                "       h.gun_time,h.started_at,h.confirmed_at,h.confirmed_by,h.operator " +
                "FROM heats h JOIN rounds r ON r.id=h.round_id " +
                "JOIN round_events re ON re.round_id=r.id JOIN events e ON e.id=re.event_id " +
                "WHERE h.confirmed_at IS NOT NULL OR h.gun_time IS NOT NULL");
            foreach (System.Data.DataRow row in ht.Rows)
                heatMarks.Add(new object[] { SS(row["age_group"]), SS(row["gender"]), SS(row["event_name"]),
                    SS(row["stage"]), Convert.ToInt32(row["heat"]), row["gun_time"], row["started_at"],
                    row["confirmed_at"], row["confirmed_by"], row["operator"] });
            return saved;
        }

        /// <summary>把成绩贴回新库。自然键 = 组别|性别|项目|赛次|组次|道次。</summary>
        private int RestoreResults(MeetDb db, List<SavedRow> saved, List<string> missed)
        {
            int back = 0;
            foreach (var sr in saved)
            {
                var hit = db.Query(
                    "SELECT he.id FROM heat_entries he " +
                    "JOIN rounds r ON r.id=he.round_id " +
                    "JOIN entries en ON en.id=he.entry_id " +
                    "JOIN events e ON e.id=en.event_id " +
                    "WHERE e.age_group=@p1 AND e.gender=@p2 AND e.event_name=@p3 " +
                    "AND r.stage=@p4 AND he.heat=@p5 AND he.lane=@p6",
                    sr.Ag, sr.Gd, sr.Ev, sr.St, sr.Heat, sr.Lane);
                if (hit.Rows.Count == 0)
                {
                    // 没成绩的行找不到位置无所谓(编排本来就能改), 有成绩的必须喊
                    object ft; sr.Cols.TryGetValue("final_time", out ft);
                    bool hasResult = ft != null && Convert.ToDouble(ft) > 0;
                    if (hasResult) missed.Add(sr.Key());
                    continue;
                }
                long id = Convert.ToInt64(hit.Rows[0]["id"]);

                var sets = new List<string>(); var ps = new List<object>(); int n = 1;
                foreach (string c in ResultCols) { sets.Add(c + "=@p" + n); ps.Add(sr.Cols[c]); n++; }
                ps.Add(id);
                db.ExecuteNonQuery("UPDATE heat_entries SET " + string.Join(",", sets.ToArray()) +
                                   " WHERE id=@p" + n, ps.ToArray());

                db.ExecuteNonQuery("DELETE FROM splits WHERE heat_entry_id=@p1", id);
                foreach (var s in sr.Splits)
                    db.ExecuteNonQuery(
                        "INSERT INTO splits(heat_entry_id,distance,cumulative_time,lap_time,rank_at," +
                        "timing_source,is_manual) VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7)",
                        id, s[0], s[1], s[2], s[3], s[4], s[5]);
                foreach (var g in sr.Legs)
                    db.ExecuteNonQuery(
                        "UPDATE relay_legs SET reaction_time=@p3,leg_time=@p4,cumulative_time=@p5,rank_at=@p6 " +
                        "WHERE heat_entry_id=@p1 AND leg_order=@p2",
                        id, g[0], g[1], g[2], g[3], g[4]);
                back++;
            }
            return back;
        }

        /// <summary>组一级的发令/确认信息 —— 赛程树的"已完赛"读它。</summary>
        private int RestoreHeatMarks(MeetDb db, List<object[]> marks)
        {
            int n = 0;
            foreach (var h in marks)
            {
                var rr = db.Query(
                    "SELECT r.id FROM rounds r JOIN round_events re ON re.round_id=r.id " +
                    "JOIN events e ON e.id=re.event_id " +
                    "WHERE e.age_group=@p1 AND e.gender=@p2 AND e.event_name=@p3 AND r.stage=@p4",
                    SS(h[0]), SS(h[1]), SS(h[2]), SS(h[3]));
                if (rr.Rows.Count == 0) continue;
                n += db.ExecuteNonQuery(
                    "UPDATE heats SET gun_time=@p3,started_at=@p4,confirmed_at=@p5,confirmed_by=@p6,operator=@p7 " +
                    "WHERE round_id=@p1 AND heat=@p2",
                    Convert.ToInt64(rr.Rows[0]["id"]), h[4], h[5], h[6], h[7], h[8], h[9]);
            }
            return n;
        }

        private static string SS(object o) { return o == null || o == DBNull.Value ? "" : o.ToString(); }

        /// <summary>
        /// 2026-08-30 用【当前】算法把全库的项目名次重算一遍。
        ///
        /// 为什么必须有: 名次只在确认成绩时算并入库, 所以库里存的是【当时那版算法】
        /// 算出来的。并列的口径改过(原来按 1e-9 直接比 double, 现在按 1/100 秒取整,
        /// 与裁判一致) —— 不重算, 老比赛的名次就一直是旧口径的, 而且不会有人发现。
        /// 自动化测试实测: 重算前有并列被拆成不同名次, 重算后全部正确。
        ///
        /// 幂等: 重算只是按同样的成绩再排一次, 跑多少遍结果都一样。
        /// 只在本机库上做; 远端模式下主服务器自己会做自己那份。
        /// </summary>
        /// <summary>
        /// 2026-08-31 参数存进竞赛库的 settings 表(key -> JSON)。
        ///
        /// 为什么要搬: 参数原来只存在各自程序目录的 timing_settings.json /
        /// device_states.json —— 【每台机器一份, 各存各的】。计时端改了, 主服务器
        /// 那份不知道; 靠消息同步就一定有分叉的时候(断线改的、启动顺序不同、
        /// 某个字段忘了推), 而且不报错。盲表数量那个 bug 就是这么来的。
        ///
        /// 走 _meet 而不是 _local: 联机时 _meet 就是主服务器 —— 计时端写的就是
        /// 【主服务器那份库】, 两端读同一处, 这才叫单一来源。断线时 _meet 退回
        /// 本机库, 参数照样存得下读得出, 联网后由推送对齐。
        ///
        /// settings 表本来就是 key->JSON 的设计, 不用新建表。
        /// </summary>
        public bool SaveConfig(string key, string json, string op)
        {
            if (_meet == null || string.IsNullOrEmpty(key)) return false;
            try { _meet.SaveSetting(key, json, op); return true; }
            catch (Exception ex) { Log("参数存入竞赛库失败(" + key + "): " + ex.Message); return false; }
        }

        public string LoadConfig(string key)
        {
            if (_meet == null || string.IsNullOrEmpty(key)) return null;
            try { return _meet.GetSetting(key); }
            catch (Exception ex) { Log("从竞赛库读参数失败(" + key + "): " + ex.Message); return null; }
        }

        /// <summary>
        /// 2026-08-31 【一次性】把历史名次订正到当前并列口径, 之后再也不跑。
        ///
        /// 背景: 并列判定原来是 Math.Abs(差) > 1e-9 直接比 double, 现在是按 1/100 秒
        /// 取整比(与裁判一致)。库里【已确认】的名次是旧口径算出来的, 极个别并列会不一样。
        ///
        /// 为什么可以改: 这不是"重排名次", 是把当初就该并列却没并列的订正过来 ——
        /// 成绩一个字没动, 只是并列判定的口径统一。用户明确要求不留这个尾巴。
        ///
        /// 为什么只跑一次: 名次确认后就固定, 不能每次加载都动。用 settings 里的
        /// rank_rule_version 记账, 订正过就跳过。
        ///
        /// 留痕: 每一条改动都写进日志(哪个项目、谁、原名次 -> 新名次), 一条不漏;
        /// 没有任何改动时只记一句"无需订正"。
        /// </summary>
        /// <summary>
        /// 2026-08-31 组排名表: 该项目【所有组都比完并确认】之后, 把全部运动员的成绩
        /// 合到一张表上排出来的总排名。
        ///
        /// 它跟本组排名是两回事:
        ///   本组排名 —— 第 X 组之内的名次(读时现算, 不入库)
        ///   组排名   —— 全项目跨组的总名次。有几个赛次时是【晋级的依据】;
        ///               直接决赛的项目, 它就是【最终名次】。
        ///
        /// 什么时候生成: 该项目所有组(取消的组不算)都确认之后, 由最后那一组的确认动作触发。
        ///   判定用"是不是全部确认", 不是"组次号是不是最大" —— 中间可能有取消的组。
        ///
        /// 生成之后就定稿, 跟本组名次一样不许自动改。要改只能人工决定。
        /// </summary>
        public int GenerateEventRankingIfComplete(string ageGroup, string gender, string eventName, string stage, string op)
        {
            return GenerateEventRankingIfComplete(ageGroup, gender, eventName, stage, op, null);
        }

        /// <param name="confirm">
        /// 2026-08-31 判定"全部组已确认"之后、真正写表【之前】问一句。返回 false 就不生成。
        /// 生成组成绩是定稿动作(晋级依据/最终名次), 不该在操作员不知情的情况下发生 ——
        /// 万一是误确认了最后一组, 定了稿再回头改就麻烦了。
        /// 传 null = 不问(用于没有人在跟前的场合, 比如主服务器按计时端的指令生成)。
        /// </param>
        public int GenerateEventRankingIfComplete(string ageGroup, string gender, string eventName, string stage, string op, Func<bool> confirm)
        {
            if (_local == null) return 0;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                long eid = ResolveEvent(ageGroup, gender, eventName, stage);
                if (rid == 0 || eid == 0) return 0;

                // ══════════════════════════════════════════════════════════
                // 2026-09-01 判定"全部组已确认"必须问【库的真身】。
                //
                // 联机计时端上 _local 不是真身: LiveCommit 在联机时走的是
                //   _meet.CommitHeatFrom(...)  —— 成绩和 confirmed_at 只写主服务器,
                //   本机那份 meet.db 是导入档案时建的、【一组都没确认过】的副本。
                // 拿它去数, 结果永远是"还有 N 组没确认"(N = 该项目总组数, 一次都不减),
                // 于是永远 return 0 —— 计时端从来没生成过组排名表, 也就从来没发
                // GENERATE_EVENT_RANKING 给主服务器(那句话挂在 made>0 上)。
                //
                // 现场实测就是这样: 2 组的项目, 两组都确认完了, 日志里两次都是
                // "还有 2 组没确认", 主服务器的 event_rankings 一行没有。
                // 【这是"项目成绩查不到"的真正源头】—— 断线补传/导入补生成/竞态重试
                // 那三处补丁全在下游, 通知压根没发出来过。
                // ══════════════════════════════════════════════════════════
                int pending;
                bool standalone = ReferenceEquals(_meet, _local);
                if (standalone)
                {
                    var pend = _local.Db.Query(
                        "SELECT COUNT(*) AS n FROM heats WHERE round_id=@p1 " +
                        "AND COALESCE(state,'') <> 'cancelled' AND confirmed_at IS NULL", rid);
                    pending = pend.Rows.Count > 0 ? Convert.ToInt32(pend.Rows[0]["n"]) : 0;
                }
                else
                {
                    // 联机: 走 RPC 问主服务器那份 —— 那才是成绩真正落进去的库
                    var hl = _meet.GetHeatList(rid);
                    pending = 0;
                    if (hl != null)
                        foreach (var h in hl)
                            if (!h.IsCancelled && !h.IsConfirmed) pending++;
                }
                if (pending > 0)
                {
                    Log(string.Format("{0}{1} {2} {3}: 还有 {4} 组没确认, 组排名表暂不生成",
                        ageGroup, gender, eventName, stage, pending));
                    return 0;
                }

                // 联机计时端【不在本机写这张表】: _local 是空副本, 写出来的是一张错表,
                // 而且没人会读它。判定通过 + 操作员点头之后, 由调用方发
                // GENERATE_EVENT_RANKING 让主服务器用它自己的库生成 —— 那份才作数。
                if (!standalone)
                {
                    if (confirm != null && !confirm())
                    {
                        Log(string.Format("{0}{1} {2} {3}: 操作员取消, 本次不生成组成绩(下次确认成绩时会再问)",
                            ageGroup, gender, eventName, stage));
                        return 0;
                    }
                    Log(string.Format("★ {0}{1} {2} {3} 全部组已确认 —— 通知主服务器生成组成绩（本机不写, 以服务器那份为准）",
                        ageGroup, gender, eventName, stage));
                    return 1;   // >0 = 告诉调用方"去通知主服务器"
                }

                // 2026-08-31 老库里这张表可能是早先的列序(名次不在第一列)。
                //   这表是每次全部确认后重新生成的, 丢了也能再生成 —— 列序不对就重建。
                try {
                    var ti = _local.Db.Query("PRAGMA table_info(event_rankings)");
                    if (ti.Rows.Count > 0 && SS(ti.Rows[0]["name"]) != "rank") {
                        _local.Db.ExecuteNonQuery("DROP TABLE event_rankings");
                        _local.Db.EnsureSchemaPublic();
                        Log("组排名表列序已更新(名次放到第一列), 表已重建");
                    }
                } catch { }

                // 全部确认了。写表【之前】先问一句 —— 定稿动作不能悄悄发生。
                if (confirm != null && !confirm())
                {
                    Log(string.Format("{0}{1} {2} {3}: 操作员取消, 本次不生成组成绩(下次确认成绩时会再问)",
                        ageGroup, gender, eventName, stage));
                    return 0;
                }

                // 生成/刷新这个项目的组排名表
                var rows = _local.Db.Query(
                    "SELECT he.id, he.heat, he.lane, he.final_time, he.rank, he.status, " +
                    "       he.promotion_mark, he.record_note, en.bib_number AS bib, " +
                    "       en.athlete_id AS aid, a.name AS nm, u.name AS un " +
                    "FROM heat_entries he " +
                    "JOIN entries en ON en.id=he.entry_id " +
                    "LEFT JOIN athletes a ON a.id=en.athlete_id " +
                    "LEFT JOIN units u ON u.id=en.unit_id " +
                    "WHERE he.round_id=@p1 AND en.event_id=@p2 AND he.reserve_no IS NULL " +
                    // 2026-09-01 TRI(试游)【进表, 但 rank=0】—— 跟 DSQ/DNS/DNF 一个待遇。
                    //   为什么不像先前那样直接不入表: 这张表是打印的数据源, 而
                    //   "项目成绩"按【第X组】看时是本组成绩单, TRI 要显成绩+备注 TRI;
                    //   选【全部】看时才是项目总排名, 那时才不显示 TRI。
                    //   入不入表是"记录全不全"的问题, 显不显示是视图的问题, 两件事。
                    // 2026-09-14 【本条 ORDER BY 原来拿 he.rank 当主排序键 —— 那是错的】
                    //   he.rank 是 UpdateHeatRanking() 算出来的"组内名次"(只在本组内比较,
                    //   见 MainWindow.RankHeatGroup: 只喂 GetCurrentHeatSwimmers() 这一组的人)。
                    //   一个决赛项目分 2+ 组时, 每组都各有一个"组内第1", 数值都是 1 ——
                    //   照 he.rank 排、再照 he.rank 原样写进 event_rankings.rank, 后果是:
                    //   甲组游得慢的"组内第1"和乙组游得快的"组内第1"【都显示总排名第1】,
                    //   总排名的名次跟真实用时完全对不上号(用户实拍到: 5:40 排在 3:01 前面)。
                    //   现在只按【真实成绩】+状态分档排序, 下面在 C# 里按这个顺序重新算
                    //   一份跨组的名次(并列规则与 ResultOrdering.ComputeRanks 同一份),
                    //   不再相信 he.rank 这个组内值。
                    "ORDER BY CASE COALESCE(he.status,'') WHEN 'TRI' THEN 1 WHEN 'DSQ' THEN 2 WHEN 'DQ' THEN 2 " +
                    "              WHEN 'DNF' THEN 3 WHEN 'DNS' THEN 4 ELSE 0 END, " +
                    "         CASE WHEN he.final_time>0 THEN he.final_time ELSE 999999 END, " +
                    "         he.lane", rid, eid);

                // 本项目共几组(取消的不算) —— 打印时显示"第几组/总组数"
                var th = _local.Db.Query("SELECT COUNT(*) AS n FROM heats WHERE round_id=@p1 AND COALESCE(state,'') <> 'cancelled'", rid);
                int totalHeats = th.Rows.Count > 0 ? Convert.ToInt32(th.Rows[0]["n"]) : 0;

                // 2026-09-14 跨组名次在这里重算一遍, 不用 he.rank(那是组内名次) ——
                //   算法跟 ResultOrdering.ComputeRanks 同一份(1/100 秒取整判并列,
                //   形如 1,1,3,4), 只是这里的输入是【全项目、跨所有组】按成绩排好的行,
                //   而 he.rank 当初只在各自那一组内部算过一次。
                //   rows 已经按上面的 ORDER BY 排好(有效成绩最前、按成绩升序), 这里
                //   只挑"正常状态 + 有成绩"的行参与编号, TRI/DSQ/DNF/DNS/无成绩一律 0。
                var crossRanks = new int[rows.Rows.Count];
                {
                    int rk = 0; double prevT = -1; int seen = 0;
                    for (int i = 0; i < rows.Rows.Count; i++) {
                        var rr = rows.Rows[i];
                        string st0 = SS(rr["status"]);
                        double ft0 = rr["final_time"] == DBNull.Value ? 0 : Convert.ToDouble(rr["final_time"]);
                        if (st0.Length > 0 || ft0 <= 0) { crossRanks[i] = 0; continue; }
                        seen++;
                        if (seen == 1 || !SwimmingScoreboard.ResultOrdering.IsTie(ft0, prevT)) rk = seen;
                        crossRanks[i] = rk;
                        prevT = ft0;
                    }
                }

                int n = 0;
                var skipped = new List<string>();
                _local.Db.InTransaction(delegate(Func<string, object[], int> run)
                {
                    run("DELETE FROM event_rankings WHERE round_id=@p1 AND event_id=@p2", new object[] { rid, eid });
                    for (int ri = 0; ri < rows.Rows.Count; ri++)
                    {
                        System.Data.DataRow r = rows.Rows[ri];
                            // 备注: 判罚优先 -> 晋级标记 -> 纪录标识。跟成绩单上那一列同口径。
                        string rmk = SS(r["status"]);
                        if (rmk.Length == 0) rmk = SS(r["promotion_mark"]);
                        if (rmk.Length == 0) rmk = SS(r["record_note"]);
                        // 2026-09-17 【单独一行数据有问题(比如同一 heat_entry 被 JOIN 出重复行,
                        //   撞了 event_rankings 的 (round_id,event_id,heat_entry_id) 主键)原来会让
                        //   整个 InTransaction 抛出去、连同前面已经 DELETE 的旧表一起回滚——
                        //   一个项目里一支队伍/一个人的数据有毛病, 其余人全部跟着"生成失败",
                        //   而且 DbPoll_Tick 每 10 秒重试一次, 同一条错误反复写进日志(用户实拍到:
                        //   同一个报名号连续几十次"未能启用约束"), 这个项目永远定不了稿。
                        //   现在单行插入失败只跳过这一行、记下是谁, 其余人正常定稿——
                        //   不能因为一个人的报名数据有问题, 把全项目的名次都卡死。
                        try {
                            run("INSERT INTO event_rankings(round_id,event_id,heat_entry_id,athlete_id,bib_number," +
                                "rank,heat,total_heats,lane,final_time,status,promotion_mark,record_note,remark," +
                                "athlete_name,unit_name,generated_at,generated_by) " +
                                "VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17,@p18)",
                                new object[] { rid, eid, r["id"], r["aid"], SS(r["bib"]),
                                    crossRanks[ri],
                                    r["heat"], totalHeats, r["lane"], r["final_time"], SS(r["status"]),
                                    SS(r["promotion_mark"]), SS(r["record_note"]), rmk, SS(r["nm"]), SS(r["un"]),
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), op ?? "" });
                            n++;
                        } catch (Exception exRow) {
                            skipped.Add(string.Format("号码{0}/姓名{1}/道{2}: {3}",
                                SS(r["bib"]), SS(r["nm"]), r["lane"], exRow.Message));
                        }
                    }
                });
                Log(string.Format("★ 组排名表已生成: {0}{1} {2} {3} —— 全部组已确认, 共 {4} 人（晋级/最终名次以此为准）",
                    ageGroup, gender, eventName, stage, n));
                if (skipped.Count > 0)
                    Log(string.Format("【注意】{0}{1} {2} {3}: 组排名表里有 {4} 行数据异常被跳过(其余 {5} 人正常定稿, 不受影响) —— {6}",
                        ageGroup, gender, eventName, stage, skipped.Count, n, string.Join("; ", skipped)));
                return n;
            }
            catch (Exception ex)
            {
                // 2026-09-17 原来这里只打 ex.Message —— 用户实拍到的日志是一句光秃秃的
                //   "未能启用约束。一行或多行中包含违反非空、唯一或外键约束的值"，
                //   连是哪句 SQL、哪个值都看不出来, 而且 DbPoll_Tick 每 10 秒重试一次,
                //   同一句空话在日志里刷了几十遍, 排查不动。这里落地那个 Exception 只有
                //   Message, 说明真正抛出来的不是 InTransactionCore.run() 那个已经带
                //   SQL+参数的包装异常(那个会长得多), 而是这段代码里没走 run() 的另一句
                //   查询(比如取总组数/PRAGMA table_info 那几句直接 _local.Db.Query)。
                //   现在把完整异常链(ToString, 带 InnerException 和调用栈)打出来,
                //   下次同样的问题能一眼看出究竟是哪一句、哪个值。
                Log("【注意】生成组排名表失败(不影响已确认的成绩): " + ex);
                return 0;
            }
        }

        /// <summary>
        /// 2026-09-16 "空道试游"(MainWindow.CreateEmptyLaneTriSwimmer)占位运动员是纯内存对象——
        /// 该道原本没有报名信息, 无 entry/heat_entries 可挂, 于是从来不会进 heat_entries 表。
        /// 上面 GenerateEventRankingIfComplete 那条 SQL 是从 heat_entries 出发查的, 天生看不到
        /// 这几行, 于是"项目成绩打印"(只读 event_rankings)/大屏总排名等【只认库】的地方,
        /// 会把这几个空道试游的人整条漏掉(用户实拍到: 少年组男200米自由泳决赛, 3 个空道
        /// 试游 TRI 在"项目成绩打印"的分组表和总排名里完全不出现, 而"成绩与排名"/query.html
        /// 因为读的是内存 _swimmers, 不受影响)。
        /// 调用方(MainWindow, 持有 _swimmers)在 GenerateEventRankingIfComplete 成功后, 把这个
        /// round/event 下所有"空道试游"占位行收集好传进来, 这里【追加】进 event_rankings ——
        /// 不参与排名(rank=0), 跟真实 TRI 待遇一致; heat_entry_id 用负数合成, 不会跟真实
        /// (正数自增) id 冲突。
        /// </summary>
        public void AppendTriPlaceholderRankingRows(string ageGroup, string gender, string eventName, string stage,
            List<TriPlaceholderInfo> placeholders)
        {
            if (_local == null || placeholders == null || placeholders.Count == 0) return;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                long eid = ResolveEvent(ageGroup, gender, eventName, stage);
                if (rid == 0 || eid == 0) return;
                // event_rankings 里可能还没有这个 round/event(GenerateEventRankingIfComplete
                //   没跑过, 比如全项目就这么几个人全是空道试游、真实成绩一条没有) —— 那种场景
                //   不追加, 避免凑出一张"全是占位行"的假总排名表。
                var chk = _local.Db.Query("SELECT COUNT(*) AS n FROM event_rankings WHERE round_id=@p1 AND event_id=@p2", rid, eid);
                if (chk.Rows.Count == 0 || Convert.ToInt32(chk.Rows[0]["n"]) == 0) return;
                var th = _local.Db.Query("SELECT COUNT(*) AS n FROM heats WHERE round_id=@p1 AND COALESCE(state,'') <> 'cancelled'", rid);
                int totalHeats = th.Rows.Count > 0 ? Convert.ToInt32(th.Rows[0]["n"]) : 0;
                int n2 = 0;
                _local.Db.InTransaction(delegate(Func<string, object[], int> run)
                {
                    foreach (var p in placeholders)
                    {
                        long syntheticId = -((long)p.Heat * 1000 + p.Lane);   // 负数, 不会跟真实 heat_entries.id 撞
                        run("DELETE FROM event_rankings WHERE round_id=@p1 AND event_id=@p2 AND heat_entry_id=@p3",
                            new object[] { rid, eid, syntheticId });
                        run("INSERT INTO event_rankings(round_id,event_id,heat_entry_id,athlete_id,bib_number," +
                            "rank,heat,total_heats,lane,final_time,status,promotion_mark,record_note,remark," +
                            "athlete_name,unit_name,generated_at,generated_by) " +
                            "VALUES(@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9,@p10,@p11,@p12,@p13,@p14,@p15,@p16,@p17,@p18)",
                            new object[] { rid, eid, syntheticId, DBNull.Value, "",
                                0, p.Heat, totalHeats, p.Lane, p.FinalTime, "TRI",
                                "", "", "TRI", p.Name ?? "", "",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "空道试游(占位追加)" });
                        n2++;
                    }
                });
                if (n2 > 0)
                    Log(string.Format("空道试游占位行已补进组排名表: {0}{1} {2} {3}, 共 {4} 条", ageGroup, gender, eventName, stage, n2));
            }
            catch (Exception ex) { Log("补写空道试游占位行失败(不影响已确认的成绩): " + ex.Message); }
        }

        public int MigrateRanksOnce(string op)
        {
            if (_local == null) return 0;
            const string VER_KEY = "rank_rule_version";
            const string VER_NOW = "tie-1/100";
            try
            {
                if (_local.GetSetting(VER_KEY) == VER_NOW) return 0;   // 订正过了, 直接跳

                int fixedRows = 0, checkedRounds = 0;
                foreach (var row in _local.GetSchedule())
                {
                    checkedRounds++;
                    // 取这个 (赛次,项目) 下所有有效成绩, 按成绩升序
                    var t = _local.Db.Query(
                        "SELECT he.id, he.final_time, he.rank FROM heat_entries he " +
                        "JOIN entries en ON en.id=he.entry_id " +
                        "WHERE he.round_id=@p1 AND en.event_id=@p2 AND he.reserve_no IS NULL " +
                        "AND he.final_time>0 AND (he.status IS NULL OR he.status='') " +
                        "ORDER BY he.final_time", row.RoundId, row.EventId);
                    if (t.Rows.Count == 0) continue;

                    var ids = new List<long>();
                    var times = new List<double>();
                    var olds = new List<int>();
                    foreach (System.Data.DataRow r in t.Rows)
                    {
                        ids.Add(Convert.ToInt64(r["id"]));
                        times.Add(Convert.ToDouble(r["final_time"]));
                        olds.Add(r["rank"] == DBNull.Value ? 0 : Convert.ToInt32(r["rank"]));
                    }
                    var want = ResultOrdering.ComputeRanks(times, x => x);
                    for (int i = 0; i < ids.Count; i++)
                    {
                        if (olds[i] == want[i]) continue;
                        _local.Db.ExecuteNonQuery("UPDATE heat_entries SET rank=@p2 WHERE id=@p1", ids[i], want[i]);
                        Log(string.Format("名次订正(并列口径): {0}{1} {2} {3} 成绩{4} 名次 {5} -> {6}",
                            row.AgeGroup, row.Gender, row.EventName, row.Stage,
                            times[i].ToString("F2"), olds[i], want[i]));
                        fixedRows++;
                    }
                }
                _local.SaveSetting(VER_KEY, VER_NOW, op);
                if (fixedRows > 0)
                    Log(string.Format("★ 历史名次一次性订正完成: 改了 {0} 条(共查 {1} 个赛次)。"
                        + "成绩未动, 只是把当初该并列却没并列的统一到 1/100 秒口径。", fixedRows, checkedRounds));
                else
                    Log(string.Format("历史名次检查完毕: {0} 个赛次全部符合当前并列口径, 无需订正", checkedRounds));
                return fixedRows;
            }
            catch (Exception ex)
            {
                Log("【注意】历史名次订正失败(名次维持原样): " + ex.Message);
                return 0;
            }
        }

        // ★★ 2026-08-31 警告: 不许在任何自动流程里调这个方法 ★★
        //   名次在"确认本组成绩"那一刻就固定了, 是正式成绩的一部分。
        //   全场重算 = 事后改动已确认的成绩, 比赛里不能接受。
        //   曾经加载档案时自动调过它(修正旧算法留下的名次), 已撤销 —— 那是不懂规则。
        //   保留它只为两个用途: ① 自动化测试在【库的副本】上验算法; ② 裁判长明确
        //   决定要重排时的人工操作。除此之外谁都不要调。
        public int RecomputeAllRanks(string op)
        {
            if (_local == null) return 0;
            int n = 0;
            try
            {
                foreach (var row in _local.GetSchedule())
                {
                    try { _local.RecomputeRanks(row.RoundId, row.EventId, op); n++; }
                    catch { }
                }
            }
            catch (Exception ex) { Log("重算名次失败(不影响比赛): " + ex.Message); }
            return n;
        }

        /// <summary>
        /// 2026-08-30 确认成绩之后, 把这一组从竞赛库【读回来】。
        ///
        /// 这是"成绩确定后只调数据库、不再重算"的入口: 名次(组内/项目内)、成绩差、
        /// 并列、晋级标记, 全部以库里这一份为准, 界面拿回读的结果去显示, 不自己算。
        /// 走的是 IMeetService.GetHeat —— 联机时读的就是【主服务器】那份库,
        /// 所以两端看到的名次必然一致。
        /// </summary>
        public List<LaneRow> ReadBackHeat(string ageGroup, string gender, string eventName, string stage, int heat)
        {
            if (_meet == null || heat <= 0) return null;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                if (rid == 0)
                {
                    Log(string.Format("【注意】回读第{0}组失败: 库里找不到 {1}{2} {3} {4}",
                        heat, ageGroup, gender, eventName, stage));
                    return null;
                }
                var rows = _meet.GetHeat(rid, heat);
                if (rows == null || rows.Count == 0)
                {
                    Log(string.Format("【注意】回读第{0}组: 库里这一组没有行", heat));
                    return null;
                }
                return rows;
            }
            catch (Exception ex) { Log("【注意】回读第" + heat + "组失败: " + ex.Message); return null; }
        }

        /// <summary>
        /// 2026-08-29 离线摆渡用: 从本机竞赛库里把【已经比完的某一组】重新拼成 LiveHeat。
        /// 跟联机回推走的是同一个结构, 所以主服务器那边不需要第二套接收代码。
        /// </summary>
        public LiveHeat BuildLiveHeatFromDb(string ageGroup, string gender, string eventName, string stage, int heat)
        {
            if (_local == null || heat <= 0) return null;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                if (rid == 0) return null;
                var live = new LiveHeat();
                live.MeetRoundId = rid; live.Heat = heat;
                live.AgeGroup = ageGroup; live.Gender = gender;
                live.EventName = eventName; live.Stage = stage;
                live.ResultConfirmed = true;

                var t = _local.Db.Query(
                    "SELECT id,lane,final_time,rank,status,record_note,timing_source,reaction_time," +
                    "touchpad_time,start_block_time,pb1_time,pb2_time,pb3_time,manual_left,manual_right," +
                    "dsq_code,dsq_leg FROM heat_entries WHERE round_id=@p1 AND heat=@p2 ORDER BY lane", rid, heat);
                foreach (System.Data.DataRow r in t.Rows)
                {
                    var ln = new LiveLane();
                    ln.HeatEntryId = Convert.ToInt64(r["id"]);
                    ln.Lane = Convert.ToInt32(r["lane"]);
                    ln.FinalTime = ND(r["final_time"]); ln.Rank = (int)ND(r["rank"]);
                    ln.Status = SS(r["status"]); ln.RecordNote = SS(r["record_note"]);
                    ln.TimingSource = SS(r["timing_source"]);
                    ln.ReactionTime = ND(r["reaction_time"]); ln.TouchpadTime = ND(r["touchpad_time"]);
                    ln.StartBlockTime = ND(r["start_block_time"]);
                    ln.Pb1Time = ND(r["pb1_time"]); ln.Pb2Time = ND(r["pb2_time"]); ln.Pb3Time = ND(r["pb3_time"]);
                    ln.ManualLeft = ND(r["manual_left"]); ln.ManualRight = ND(r["manual_right"]);
                    ln.DsqCode = SS(r["dsq_code"]); ln.DsqLeg = (int)ND(r["dsq_leg"]);

                    ln.Splits = new List<SplitDto>();
                    var sp = _local.Db.Query(
                        "SELECT distance,cumulative_time,lap_time,rank_at,timing_source,is_manual " +
                        "FROM splits WHERE heat_entry_id=@p1 ORDER BY distance", ln.HeatEntryId);
                    foreach (System.Data.DataRow s in sp.Rows)
                    {
                        var d = new SplitDto();
                        d.Distance = (int)ND(s["distance"]); d.CumulativeTime = ND(s["cumulative_time"]);
                        d.LapTime = ND(s["lap_time"]); d.RankAt = (int)ND(s["rank_at"]);
                        d.TimingSource = SS(s["timing_source"]); d.IsManual = ND(s["is_manual"]) > 0;
                        ln.Splits.Add(d);
                    }
                    live.Lanes.Add(ln);
                }
                return live.Lanes.Count > 0 ? live : null;
            }
            catch (Exception ex) { Log("拼装第" + heat + "组失败: " + ex.Message); return null; }
        }

        /// <summary>本机库里所有【已确认】的组, 按 组别/性别/项目/赛次/组次 列出来。</summary>
        /// <summary>
        /// 2026-08-31 一条轻查询列出【每组的变更指纹】(最后成绩时间 + 确认时间)。
        ///
        /// 用途: 调用方拿它跟上次记的比一比, 只有指纹变了的组才去读那一组 ——
        /// 没变的一行都不读。全场没人改过时, 整个刷新就只有这一条查询。
        ///
        /// 不返回成绩本身, 只返回 key + 指纹, 所以结果集很小(一组一行)。
        /// 这是"用哪部分读写哪部分": 不能因为要看一张榜就把全场成绩拖一遍 ——
        /// 数据量一大就是占内存、拖慢、最后卡死。
        /// </summary>
        /// <summary>
        /// 2026-08-31 读【组排名表】—— 已定稿的项目总排名。
        ///
        /// 返回 (组别, 性别, 项目, 赛次, 组次, 道次, 名次, 晋级标记)。
        /// 只返回定位用的 key 和这两个值, 不带成绩 —— 结果集小, 属于轻查询。
        ///
        /// 谁该用它: 晋级查询、前八名、总排名、成绩公报、团体/个人总分。
        /// 这些都是"项目全部比完之后"的事, 就该认定稿那一份, 不许各自再汇总一遍 ——
        /// 各自汇总就会各自算错, 而且错得不一样。
        /// </summary>
        public List<object[]> GetEventRankings()
        {
            var list = new List<object[]>();
            if (_local == null || _meet == null) return list;
            try
            {
                // 2026-09-17 【原来这里一直是 _local.Db.Query, 跟 GetEventRankingRows 当初
                //   那个病根一模一样】——联机时(计时端/编排端) _local 不是真身, 这条查询
                //   在那些机器上永远查到 0 行。ApplyEventRankingsFromDb 拿这个结果去刷内存
                //   的 EventRank/PromotionMark, 团体分/晋级查询/前八名/成绩公报读的都是
                //   这份内存——查不到就意味着那些机器上这几张报表永远显示"-"/空,
                //   跟主服务器早没早定稿完全无关。改走 _meet.GetAllEventRankings(), 单机时
                //   _meet==_local 跟原来结果一样, 联机时 RPC 问主服务器那份真身。
                //   2026-09-16 判罚/试游(rank=0)的行也带出来, 见 GetAllEventRankings 的说明。
                foreach (var row in _meet.GetAllEventRankings() ?? new List<EventRankSyncRow>())
                    list.Add(new object[] {
                        row.AgeGroup ?? "", row.Gender ?? "", row.EventName ?? "", row.Stage ?? "",
                        row.Heat, row.Lane, row.Rank, row.PromotionMark ?? "" });
            }
            catch (Exception ex) { Log("读组排名表失败: " + ex.Message); }
            return list;
        }

        /// <summary>
        /// 2026-09-01 读【某一个项目/赛次】的组排名表整行。
        ///
        /// 跟 GetEventRankings() 的区别: 那个是"把定稿名次灌回内存"用的轻查询(只带 key),
        /// 这个是【打印/输出直接拿来排版】用的 —— 一行就是成绩单上的一行, 不需要再去
        /// 内存里凑姓名、代表队、成绩。
        ///
        /// 为什么要有它: "文档编辑/输出/打印 → 项目成绩"原来是从【内存】里捞人的,
        /// 而内存里的成绩行只有主服务器亲自收到过回推才会有。回推丢一次、道次对不上一次,
        /// 库里明明是全的, 那张表就是空的, 还查不出原因。名次已经定稿在库里了,
        /// 就该直接读库 —— 这也是"确认之后只调数据库、不再算"的本意。
        ///
        /// 返回顺序就是表里的定稿顺序(名次 → 判罚/弃权), 调用方不要再排。
        /// 项目没定稿(表里没行)时返回空表 —— 让"还没定稿"跟"定稿了但没人"分得开,
        /// 由调用方去问 GetEventRankingProgress。
        /// </summary>
        public List<EventRankRow> GetEventRankingRows(string ageGroup, string gender, string eventName, string stage)
        {
            var list = new List<EventRankRow>();
            if (_local == null || _meet == null) return list;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                long eid = ResolveEvent(ageGroup, gender, eventName, stage);
                if (rid == 0 || eid == 0) return list;
                // 2026-09-17 【这里原来一直是 _local.Db.Query, 是"项目成绩打印在别的机器上
                //   一直说尚未定稿"的真正病根】——跟 GenerateEventRankingIfComplete 开头
                //   那段大注释同一个道理: 联机计时端/编排端上 _local 不是真身, 组排名表
                //   只会在主服务器那份库里生成; 别的机器的 _local.event_rankings 要么是
                //   导入时的空表、要么压根没这张表, 于是不管主服务器早没早定稿, 在那些
                //   机器上打印"项目成绩"都只会查到 0 行、显示【尚未定稿】。
                //   用户实测到的"这组才比完赛、成绩已确认, 打印却说尚未定稿"就是这个——
                //   不是真没定稿, 是打印所在的这台机器问错了库。现在跟同一个类里另外三处
                //   (pending 判定/进度查询/生成判定)一个待遇: 统一走 _meet, 单机时
                //   _meet==_local 直接查本机, 联机时 RPC 问主服务器那份真身。
                return _meet.GetEventRankRows(rid, eid) ?? list;
            }
            catch (Exception ex) { Log("读组排名表(单项目)失败: " + ex.Message); }
            return list;
        }

        /// <summary>
        /// 2026-09-01 这个项目【为什么还没定稿】—— 总共几组、还差几组没确认。
        /// 打印窗口拿它给出一句人话的解释, 而不是干巴巴一句"暂无成绩"。
        /// total = -1 表示库里根本找不到这个项目/赛次(多半是组别或赛次选错了)。
        /// </summary>
        public void GetEventRankingProgress(string ageGroup, string gender, string eventName, string stage,
                                            out int total, out int pending, out int ranked)
        {
            total = -1; pending = 0; ranked = 0;
            if (_local == null) return;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                long eid = ResolveEvent(ageGroup, gender, eventName, stage);
                if (rid == 0 || eid == 0) return;
                // 2026-09-01 跟 GenerateEventRankingIfComplete 同一个道理: 联机计时端上
                //   _local 一组都没确认过, 拿它数会报出"还差 N 组"这种误导人的话。
                if (ReferenceEquals(_meet, _local))
                {
                    var a = _local.Db.Query(
                        "SELECT COUNT(*) AS n FROM heats WHERE round_id=@p1 AND COALESCE(state,'') <> 'cancelled'", rid);
                    total = a.Rows.Count > 0 ? Convert.ToInt32(a.Rows[0]["n"]) : 0;
                    var b = _local.Db.Query(
                        "SELECT COUNT(*) AS n FROM heats WHERE round_id=@p1 " +
                        "AND COALESCE(state,'') <> 'cancelled' AND confirmed_at IS NULL", rid);
                    pending = b.Rows.Count > 0 ? Convert.ToInt32(b.Rows[0]["n"]) : 0;
                }
                else
                {
                    var hl = _meet.GetHeatList(rid);
                    total = 0; pending = 0;
                    if (hl != null)
                        foreach (var h in hl)
                        {
                            if (h.IsCancelled) continue;
                            total++;
                            if (!h.IsConfirmed) pending++;
                        }
                }
                // 2026-09-17 这一句原来一直是 _local.Db.Query, 跟上面 total/pending 判定
                //   犯的是同一个错——联机时 _local 不是真身, 数出来的"已有名次"在计时端/
                //   编排端上永远是 0 或一份陈旧值, DbProgressLine 那句"已有名次 N 人"
                //   在别的机器上就是一句瞎话。改用 _meet.GetSummary(RPC 安全, 单机时
                //   _meet==_local 直接查本机, 跟原来结果一样), 数 Rank>0 的行数。
                var summaryRows = _meet.GetSummary(rid, eid);
                ranked = summaryRows != null ? summaryRows.Count(x => x.Rank > 0 && x.ReserveNo == null) : 0;
            }
            catch (Exception ex) { Log("查项目定稿进度失败: " + ex.Message); }
        }

        /// <summary>
        /// 2026-09-17 单独一组是不是已经"确认本组成绩"——跟"这个项目全部组是否都已确认
        /// (从而能不能生成【组成绩/总排名】)"是两件不相关的事, 不能混着问。
        ///
        /// 用户明确指出: "项目成绩"里选【第X组】打印, 只要按过"确认本组成绩"就该能打印,
        /// 跟别的组比没比完毫无关系; 只有选【全部】(总排名)才用得上"是否全部组都确认"。
        /// 原来打印窗口没有单独问"这一组"的入口, 只能问 GetEventRankingProgress 那种
        /// "全项目还差几组"的整体进度, 或者直接查 event_rankings(只有全部组确认后才有
        /// 数据)——于是单独一组哪怕早就确认了, 只要项目里还有别的组没比，"第X组"照样
        /// 打印不出来、或者带着"尚未定稿"的警示, 这是不对的。
        /// </summary>
        public bool IsHeatConfirmed(string ageGroup, string gender, string eventName, string stage, int heat)
        {
            if (_local == null || _meet == null || heat <= 0) return false;
            try
            {
                long rid = ResolveRound(ageGroup, gender, eventName, stage);
                if (rid == 0) return false;
                var hl = _meet.GetHeatList(rid);
                if (hl == null) return false;
                foreach (var h in hl)
                    if (h.Heat == heat) return !h.IsCancelled && h.IsConfirmed;
                return false;
            }
            catch (Exception ex) { Log("查单组确认状态失败: " + ex.Message); return false; }
        }

        public List<string[]> ListHeatStamps()
        {
            var list = new List<string[]>();
            if (_local == null || _meet == null) return list;
            try
            {
                // 2026-09-01 指纹里【必须带上名次】。
                //   原来只看 result_at + confirmed_at。而确认第 2 组时 RecomputeRanks 会把
                //   【整个项目】的名次重排 —— 第 1 组那些人的 rank 从 1 变成 9, 可是他们的
                //   result_at / confirmed_at 一个字都没动。指纹没变 => 增量刷新跳过这一组
                //   => 内存里第 1 组永远停在"他们还是第 1"的旧值。
                //   现场实测: 两组成绩不同, "成绩与排名"选"全部"时两组人全显示第 1。
                //   加一个名次和(rank_sum)进指纹, 名次一变就会重读。
                // 2026-09-17 【这里原来一直是 _local.Db.Query, 联机时(计时端/编排端)主窗口
                //   每 10 秒的增量刷新(RefreshChangedFromDb)在那些机器上永远查到 0 行 ——
                //   跟前面几处(读组排名表/团体分)同一个病根, 只是这条更基础: 增量刷新本身
                //   就靠这张"指纹表"判断"要不要读", 查不到指纹, 后面那句"该重读哪一组"
                //   根本走不到。改走 _meet 统一处理。
                foreach (var row in _meet.ListHeatStamps() ?? new List<string[]>()) list.Add(row);
            }
            catch (Exception ex) { Log("列组次指纹失败: " + ex.Message); }
            return list;
        }

        public List<string[]> ListConfirmedHeats()
        {
            var list = new List<string[]>();
            if (_local == null || _meet == null) return list;
            try
            {
                // 2026-09-17 同上——赛程导航树的"[已完赛]"标记靠它, 联机时原来一直查本机,
                //   在计时端/编排端上永远显示"未完赛"。
                foreach (var row in _meet.ListConfirmedHeats() ?? new List<string[]>()) list.Add(row);
            }
            catch (Exception ex) { Log("列已确认组失败: " + ex.Message); }
            return list;
        }

        private static double ND(object o)
        {
            if (o == null || o == DBNull.Value) return 0;
            try { return Convert.ToDouble(o); } catch { return 0; }
        }

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
        /// <summary>
        /// 2026-09-14 不经当前组库, 直接把内存里这一组的成绩写进竞赛库。
        ///
        /// 用在【解锁本组成绩 → 改完 → 再确认】这条路上: 第一次确认时 LiveCommit 已经
        /// 把当前组库提交并关掉了(_liveActive=false), 第二次确认再走 CommitLiveHeat
        /// 就直接 return —— 改过的成绩根本没进库。而 10 秒一次的库回读又会拿库里那份
        /// 旧值把内存盖回去, 现场看到的就是"改好了, 过一会儿又变回去了"。
        ///
        /// 行号(heat_entries.id)一律按【道次】重新认, 不信调用方传的 —— 跟
        /// CommitHeatFromWire 一个规矩: 两边库各自重建过 id 就会错位, 认错人比不写更糟。
        /// 联机时写的是主服务器那份库(_meet), 单机时就是本机那份。
        /// </summary>
        public List<RecordBreak> CommitHeatDirect(LiveHeat live, string op)
        {
            var empty = new List<RecordBreak>();
            if (_meet == null || live == null || live.Heat <= 0) return empty;
            try
            {
                long rid = ResolveRound(live.AgeGroup, live.Gender, live.EventName, live.Stage);
                if (rid == 0)
                {
                    Log(string.Format("【注意】库里找不到 {0}{1} {2} {3}, 这一组改动没能入库",
                        live.AgeGroup, live.Gender, live.EventName, live.Stage));
                    return empty;
                }
                live.MeetRoundId = rid;
                var byLane = new Dictionary<int, long>();
                foreach (var row in _meet.GetHeat(rid, live.Heat))
                    if (row.Lane != null && !byLane.ContainsKey(row.Lane.Value)) byLane[row.Lane.Value] = row.Id;
                int lost = 0;
                foreach (var ln in live.Lanes)
                {
                    long id;
                    if (byLane.TryGetValue(ln.Lane, out id)) ln.HeatEntryId = id;
                    else { ln.HeatEntryId = 0; lost++; }
                }
                if (lost > 0)
                    Log(string.Format("【注意】第{0}组有 {1} 个道次在库里的编排中找不到, 这几道没入库", live.Heat, lost));
                var breaks = _meet.CommitHeatFrom(live, op);
                Log(string.Format("第{0}组改动已直接写入竞赛库{1}", live.Heat,
                    breaks.Count > 0 ? "，破纪录 " + breaks.Count + " 项" : ""));
                return breaks;
            }
            catch (Exception ex)
            {
                Log("【注意】直接写入竞赛库失败: " + ex.Message);
                return empty;
            }
        }

        public List<RecordBreak> CommitHeatFromWire(LiveHeat live, string op)
        {
            var empty = new List<RecordBreak>();
            if (_local == null || live == null) return empty;
            try
            {
                // 2026-08-29 送来的 MeetRoundId / HeatEntryId 是【对方库】的自增 id。
                //   两边能对上, 只是因为导的是同一个包、同样的顺序 —— 任何一边单独
                //   重建过, id 就会错位, 成绩会静默地写到别人身上。
                //   所以一律不信对方的 id, 按 (组别|性别|项目|赛次) + 组次 + 道次
                //   在本机重新认一遍。认不出来的道次跳过并报警, 绝不猜。
                long rid = ResolveRound(live.AgeGroup, live.Gender, live.EventName, live.Stage);
                if (rid == 0)
                {
                    Log(string.Format("【注意】本机库里找不到 {0}{1} {2} {3}, 这一组成绩没法入库",
                        live.AgeGroup, live.Gender, live.EventName, live.Stage));
                    return empty;
                }
                live.MeetRoundId = rid;
                int relocated = 0, lost = 0;
                foreach (var ln in live.Lanes)
                {
                    var q = _local.Db.Query(
                        "SELECT id FROM heat_entries WHERE round_id=@p1 AND heat=@p2 AND lane=@p3",
                        rid, live.Heat, ln.Lane);
                    if (q.Rows.Count == 0) { ln.HeatEntryId = 0; lost++; continue; }
                    long myId = Convert.ToInt64(q.Rows[0]["id"]);
                    if (myId != ln.HeatEntryId) relocated++;
                    ln.HeatEntryId = myId;
                }
                if (relocated > 0)
                    Log(string.Format("收到的第{0}组有 {1} 个道次的行号与本机不同, 已按道次重新对上",
                        live.Heat, relocated));
                if (lost > 0)
                    Log(string.Format("【注意】收到的第{0}组有 {1} 个道次在本机编排里没有, 这几道成绩没入库",
                        live.Heat, lost));

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

    /// <summary>
    /// 2026-09-01 组排名表的一行 —— 就是"项目成绩"单上的一行。
    /// 姓名/代表队/成绩都在库里存着(定稿时抄下来的), 打印时不用再回内存里凑,
    /// 也就不会出现"库里有、内存里没有 → 表是空的"。
    /// </summary>
    public class EventRankRow
    {
        public int Rank;
        public string BibNumber;
        public string AthleteName;
        public string UnitName;
        public double FinalTime;
        public int Heat;
        public int TotalHeats;
        public int Lane;
        public string Remark;
        public string Status;
        public string PromotionMark;
        public string RecordNote;
        public long HeatEntryId;
    }

    /// <summary>
    /// 2026-09-17 "组排名表灌回内存"用的轻量一行(见 GetEventRankings 的说明) ——
    /// 只带定位用的四个字符串(组别/性别/项目/赛次) + 组次/道次/名次/晋级标记, 不带
    /// 姓名/成绩/号码这些排版才需要的字段, 结果集小, 适合走 RPC 频繁同步。
    /// </summary>
    public class EventRankSyncRow
    {
        public string AgeGroup, Gender, EventName, Stage;
        public int Heat, Lane, Rank;
        public string PromotionMark;
    }

    /// <summary>
    /// 2026-09-16 一条"空道试游"占位记录(见 MainWindow.CreateEmptyLaneTriSwimmer) ——
    /// 只有 组次/道次/成绩, 没有报名信息(姓名/代表队/号码本来就是空的)。
    /// 用于 AppendTriPlaceholderRankingRows 把内存里这几条追加进 event_rankings。
    /// </summary>
    public class TriPlaceholderInfo
    {
        public int Heat;
        public int Lane;
        public double FinalTime;
        public string Name;
    }
}
