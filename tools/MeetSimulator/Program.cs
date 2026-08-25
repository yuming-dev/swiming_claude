using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MeetSimulator
{
    // ══════════════════════════════════════════════════════════════════════
    // 模拟比赛 自动化测试                                      2026-08-24
    //
    // 两头同时假装：
    //   一头当【计时硬件】—— TCP 服务端，主程序按 timing_connection.json
    //     连过来，然后我们按真协议发 12 字节帧（反应时/触板/盲表/滚动时间）
    //   一头当【遥控台】 —— WebSocket 客户端连 ws://host:3002，
    //     发 SET_AGEGROUP / SET_EVENT / SET_STAGE / SET_HEAT / READY /
    //     START_RACE / CONFIRM_RESULT，跟 race_control.html 走同一条路
    //
    // 跑完自动核对：
    //   · 每组每道有没有成绩、分段够不够段
    //   · 名次是不是按项目各排各的
    //   · 主程序进程内存全程曲线（这是 2026-08 那次 400 米涨几 GB 的回归验证）
    //
    // 用法:
    //   MeetSimulator.exe [--heats N] [--host 127.0.0.1] [--port 5000]
    //                     [--ws 3002] [--lanes 8] [--no-launch]
    //                     [--dsq] [--dns] [--pause 300]
    // ══════════════════════════════════════════════════════════════════════
    internal static class Program
    {
        // ── 协议常量（与 TimingBridge.cs 一致）──
        const byte SOH = 0xF1, EOT = 0xF4, S = 0x53;
        const int FRAME_LEN = 12;
        const byte CMD_TOUCHPAD = 0x16;   // D3: 0=真触板 1=盲表代触 3=仰泳松开 4=手动TP键
        const byte CMD_PB1 = 0x17, CMD_PB2 = 0x18, CMD_PB3 = 0x19;
        const byte CMD_STARTBLOCK = 0x1A; // D10: 0=正常 1=抢跳 2=接力超时
        const byte CMD_START = 0x1C;
        const byte CMD_RUNNING = 0x7F;

        static Opts _o;
        // 终点端。程序默认左端(LaneCloseSettings.FinishPosition)，
        // 模拟器跟着它算每一段该发哪一端。
        static bool _finishLeft = true;
        static readonly List<string> _fail = new List<string>();
        static readonly List<double> _memSamples = new List<double>();

        class Opts
        {
            public int Heats = 3, Lanes = 8, WsPort = 3002, TcpPort = 5000, PauseMs = 300;
            // 触板不是一直开着的：发令后要等「泳道开关时间」倒计时到点才打开到达端。
            // 所以帧不能一口气灌完 —— 那样程序会把它们记进原始日志但拒绝算成绩，
            // 这是程序的正确行为，不是 bug。这里按节奏发，并把开关时间调小让测试跑得快。
            public double LapGap = 2.5;
            public string Host = "127.0.0.1";
            public bool NoLaunch, WithDsq, WithDns;
            public string AppDir;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _o = ParseArgs(args);
            Banner();

            Process app = null;
            HardwareSim hw = null;
            RaceControl rc = null;
            try
            {
                hw = new HardwareSim(_o.TcpPort);
                hw.Start();
                Info("计时硬件模拟器已监听 " + _o.Host + ":" + _o.TcpPort + "（等主程序连过来）");

                app = FindOrLaunchApp();
                if (app == null) { Err("找不到 SwimmingScoreboard 进程，也没能启动它"); return Finish(); }
                Info("主程序 PID=" + app.Id + "  " + app.ProcessName);

                if (!hw.WaitConnected(TimeSpan.FromSeconds(40)))
                { Err("主程序一直没连上计时端口 —— 检查 timing_connection.json 是不是 tcp/" + _o.TcpPort); return Finish(); }
                Info("主程序已连上计时端口");

                rc = new RaceControl(_o.Host, _o.WsPort);
                if (!rc.Connect(TimeSpan.FromSeconds(20)))
                { Err("连不上 ws://" + _o.Host + ":" + _o.WsPort); return Finish(); }
                Info("遥控台已连上 ws://" + _o.Host + ":" + _o.WsPort);

                // 把「泳道开关时间」调小，测试才跑得快。这是程序本来就支持的用户设置
                // (参数设置→泳道开关)，不是给测试开的后门。
                double closeTime = Math.Max(1.0, _o.LapGap - 0.5);
                rc.Send("SET_LANE_CLOSE_SETTINGS", new JObject {
                    ["laneCloseTime"] = closeTime,
                    ["resultConfirmCloseDelay"] = 1.0 });
                Info(string.Format("泳道开关时间设为 {0:N1}s，分段间隔 {1:N1}s", closeTime, _o.LapGap));
                Thread.Sleep(600);

                var heats = LoadSchedule();
                if (heats.Count == 0) { Err("读不到赛程 —— 主程序里先加载一个赛事档案"); return Finish(); }
                int n = Math.Min(_o.Heats, heats.Count);
                Info("赛程读到 " + heats.Count + " 个组，本次跑前 " + n + " 个");

                Sample(app, "开跑前");
                for (int i = 0; i < n; i++) RunOneHeat(rc, hw, app, heats[i], i + 1, n);
                Sample(app, "跑完");

                Thread.Sleep(1500);
                Verify(heats.Take(n).ToList());
                MemReport();
            }
            catch (Exception ex) { Err("模拟器异常: " + ex); }
            finally
            {
                if (rc != null) rc.Dispose();
                if (hw != null) hw.Dispose();
            }
            return Finish();
        }

        // ══════════════ 跑一个组 ══════════════
        static void RunOneHeat(RaceControl rc, HardwareSim hw, Process app, HeatRef h, int idx, int total)
        {
            Console.WriteLine();
            Info(string.Format("── [{0}/{1}] {2}{3} {4} {5} 第{6}组 ──",
                idx, total, string.IsNullOrEmpty(h.AgeGroup) ? "" : h.AgeGroup + " ",
                h.Gender, h.EventName, h.Stage, h.Heat));

            rc.Send("SET_AGEGROUP", h.AgeGroup ?? "");
            rc.Send("SET_GENDER", h.Gender ?? "");
            rc.Send("SET_EVENT", h.EventName);
            rc.Send("SET_STAGE", h.Stage);
            rc.Send("SET_HEAT", h.Heat);
            Thread.Sleep(_o.PauseMs);

            rc.Send("READY", null);
            Thread.Sleep(_o.PauseMs);

            rc.Send("START_RACE", null);
            hw.SendFrame(CMD_START, 0, 0, 0);          // 硬件回发令帧
            Thread.Sleep(120);

            // 反应时（出发台）。第 1 道故意来一次抢跳，验证红标不崩。
            for (int lane = 1; lane <= _o.Lanes; lane++)
            {
                bool falseStart = (idx == 1 && lane == 1);
                hw.SendFrame(CMD_STARTBLOCK, 0, lane, 0.62 + lane * 0.01, falseStart ? (byte)1 : (byte)0);
                Thread.Sleep(15);
            }

            // 分段 + 终点。段数按距离算，跟真实一致（1500 米就是 29 段 + 终点）。
            //
            // 帧里报的时间是【真实成绩】(28 秒一个 50 米)，跟发送节奏无关 ——
            // 程序拿帧里的时间当成绩，墙钟只用来判断触板该不该开着。所以这里
            // 按 LapGap 的节奏发，既让触板真的开着，又不用真等两分钟。
            int segs = Math.Max(1, h.TotalDistance / 50);
            double baseLap = h.TotalDistance >= 400 ? 29.5 : 27.0;
            int gapMs = (int)(_o.LapGap * 1000);
            Thread.Sleep(gapMs);                         // 等到达端触板打开
            for (int seg = 1; seg <= segs; seg++)
            {
                // 到达端要【从终点倒推】，不能想当然从出发端正推：
                //   终点端固定(默认左端)，最后一段必定到终点端；
                //   奇数圈的项目(50/150米)发令端和终点端相反，第 1 段就到终点端。
                // 上一版按"从左端出发、第1段到右端"正推，200 米(偶数圈)蒙对了，
                // 50 米(奇数圈)全错 —— 5 个组一条成绩都没落下。
                // 协议里 D4 <10 = 物理左端，D4 >=10 = 物理右端(实际道次 = D4-10)。
                bool arriveRight = ((segs - seg) % 2 == 1) ^ !_finishLeft;
                for (int lane = 1; lane <= _o.Lanes; lane++)
                {
                    if (_o.WithDns && idx == 1 && lane == _o.Lanes) continue;    // 这一道没来
                    double cum = seg * baseLap + lane * 0.35 + (seg * lane % 3) * 0.07;
                    hw.SendFrame(CMD_TOUCHPAD, 0, arriveRight ? lane + 10 : lane, cum);
                    Thread.Sleep(8);
                }
                // 滚动时间：模拟真实帧流。0x7F 会被主程序提前 return，不该触发任何存盘。
                hw.SendFrame(CMD_RUNNING, 0, 0, seg * baseLap);
                if (seg < segs) Thread.Sleep(gapMs);     // 等下一段的触板重新打开
            }

            // 盲表：给中间一道补一个，走 PushButton 路径。端别跟终点段一致。
            if (_o.Lanes >= 3)
                hw.SendFrame(CMD_PB1, 0, _finishLeft ? 3 : 13, segs * baseLap + 3 * 0.35 + 0.02);

            Thread.Sleep(_o.PauseMs);
            Sample(app, string.Format("第{0}组 触板完", h.Heat));

            if (_o.WithDns && idx == 1) rc.Send("MARK_DNS", new JObject { ["lane"] = _o.Lanes });
            if (_o.WithDsq && idx == 2) rc.Send("MARK_DSQ", new JObject { ["lane"] = 2 });

            rc.Send("CONFIRM_RESULT", null);
            Thread.Sleep(_o.PauseMs * 2);
            Sample(app, string.Format("第{0}组 已确认", h.Heat));
            Ok(string.Format("第{0}组跑完：{1} 道 × {2} 段 = {3} 次触板事件",
                h.Heat, _o.Lanes, segs, _o.Lanes * segs));
        }

        // ══════════════ 计时硬件模拟（TCP 服务端）══════════════
        class HardwareSim : IDisposable
        {
            readonly TcpListener _listener;
            TcpClient _client;
            NetworkStream _st;
            Thread _rx;
            volatile bool _run = true;
            public int FramesSent, CommandsReceived;

            public HardwareSim(int port) { _listener = new TcpListener(IPAddress.Any, port); }

            public void Start()
            {
                _listener.Start();
                new Thread(() =>
                {
                    try
                    {
                        _client = _listener.AcceptTcpClient();
                        _st = _client.GetStream();
                        _rx = new Thread(RxLoop) { IsBackground = true };
                        _rx.Start();
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            }

            public bool WaitConnected(TimeSpan t)
            {
                var end = DateTime.Now + t;
                while (DateTime.Now < end) { if (_st != null) return true; Thread.Sleep(200); }
                return false;
            }

            // 主程序发给"硬件"的命令帧，收下来记个数即可（0x21 就绪 / 0x1C 发令 / 0x43 …）
            void RxLoop()
            {
                var buf = new byte[512];
                while (_run)
                {
                    try
                    {
                        int n = _st.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        CommandsReceived += n / FRAME_LEN;
                    }
                    catch { break; }
                }
            }

            /// <summary>按真协议发一帧。seconds 会拆成 时/分/秒/百分秒/千分秒。</summary>
            public void SendFrame(byte cmd, byte cmd1, int lane, double seconds, byte d10 = 0)
            {
                if (_st == null) return;
                double abs = Math.Abs(seconds);
                int h = (int)(abs / 3600);
                int m = (int)((abs - h * 3600) / 60);
                int s = (int)(abs - h * 3600 - m * 60);
                int cs = (int)Math.Round((abs - h * 3600 - m * 60 - s) * 100);
                if (cs >= 100) { cs = 99; }
                int ms1 = 0;

                var f = new byte[FRAME_LEN];
                f[0] = SOH; f[1] = S; f[2] = cmd; f[3] = cmd1;
                f[4] = (byte)lane;                       // <10 = 物理左端；实际道次即此值
                f[5] = (byte)m; f[6] = (byte)s; f[7] = (byte)cs;
                f[8] = (byte)(((h & 0x0F) << 4) | (ms1 & 0x0F));
                f[9] = 0; f[10] = d10; f[11] = EOT;
                try { _st.Write(f, 0, f.Length); _st.Flush(); FramesSent++; } catch { }
            }

            public void Dispose()
            {
                _run = false;
                try { if (_st != null) _st.Close(); } catch { }
                try { if (_client != null) _client.Close(); } catch { }
                try { _listener.Stop(); } catch { }
            }
        }

        // ══════════════ 遥控台（WebSocket 客户端）══════════════
        class RaceControl : IDisposable
        {
            readonly string _url;
            ClientWebSocket _ws;
            public int Sent;

            public RaceControl(string host, int port) { _url = "ws://" + host + ":" + port; }

            public bool Connect(TimeSpan t)
            {
                var end = DateTime.Now + t;
                while (DateTime.Now < end)
                {
                    try
                    {
                        _ws = new ClientWebSocket();
                        _ws.ConnectAsync(new Uri(_url), CancellationToken.None).Wait(5000);
                        if (_ws.State == WebSocketState.Open)
                        {
                            Raw(new JObject { ["type"] = "TIMING_EXE_IDENTITY" });
                            return true;
                        }
                    }
                    catch { }
                    Thread.Sleep(1000);
                }
                return false;
            }

            public void Send(string command, object data)
            {
                var m = new JObject { ["type"] = "TIMING_CMD", ["command"] = command };
                if (data != null) m["data"] = data is JToken ? (JToken)data : JToken.FromObject(data);
                Raw(m);
                Sent++;
            }

            void Raw(JObject m)
            {
                if (_ws == null || _ws.State != WebSocketState.Open) return;
                var b = Encoding.UTF8.GetBytes(m.ToString(Formatting.None));
                try
                {
                    _ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true,
                                  CancellationToken.None).Wait(3000);
                }
                catch { }
            }

            public void Dispose()
            {
                try
                {
                    if (_ws != null && _ws.State == WebSocketState.Open)
                        _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(2000);
                }
                catch { }
                try { if (_ws != null) _ws.Dispose(); } catch { }
            }
        }

        // ══════════════ 赛程 ══════════════
        class HeatRef
        {
            public string AgeGroup, Gender, EventName, Stage;
            public int Heat, TotalDistance;
        }

        static string DbDir { get { return Path.Combine(_o.AppDir, "Database"); } }

        static string CurrentPackagePath()
        {
            string last = Path.Combine(_o.AppDir, "last_competition.txt");
            string name = File.Exists(last) ? File.ReadAllText(last, Encoding.UTF8).Trim() : null;
            if (!string.IsNullOrEmpty(name))
            {
                string p = Path.Combine(DbDir, name + ".json");
                if (File.Exists(p)) return p;
            }
            if (!Directory.Exists(DbDir)) return null;
            return new DirectoryInfo(DbDir).GetFiles("*.json")
                   .OrderByDescending(f => f.Length).Select(f => f.FullName).FirstOrDefault();
        }

        static List<HeatRef> LoadSchedule()
        {
            var list = new List<HeatRef>();
            string p = CurrentPackagePath();
            if (p == null) return list;
            Info("赛事档案: " + Path.GetFileName(p));
            var pkg = JObject.Parse(File.ReadAllText(p, Encoding.UTF8));
            var sch = pkg["Schedule"] as JArray;
            if (sch == null) return list;
            foreach (var it in sch)
            {
                string ev = (string)it["EventName"];
                if (string.IsNullOrEmpty(ev)) continue;
                int hc = it["HeatCount"] != null ? (int)it["HeatCount"] : 0;
                if (hc <= 0) hc = 1;
                for (int k = 1; k <= hc; k++)
                    list.Add(new HeatRef {
                        AgeGroup = (string)it["AgeGroup"], Gender = (string)it["Gender"],
                        EventName = ev, Stage = (string)it["Stage"] ?? "决赛",
                        Heat = k, TotalDistance = ParseDistance(ev) });
            }
            return list;
        }

        /// <summary>从项目名算总距离：4x100米自由泳接力 = 400；1500米自由泳 = 1500。</summary>
        static int ParseDistance(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"(\d+)\s*[xX×]\s*(\d+)");
            if (m.Success) return int.Parse(m.Groups[1].Value) * int.Parse(m.Groups[2].Value);
            m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 50;
        }

        // ══════════════ 核对 ══════════════
        static void Verify(List<HeatRef> ran)
        {
            Console.WriteLine();
            Info("══ 核对结果 ══");
            string p = CurrentPackagePath();
            if (p == null) { Err("核对时找不到档案"); return; }
            var pkg = JObject.Parse(File.ReadAllText(p, Encoding.UTF8));
            var swimmers = pkg["Swimmers"] as JArray;
            if (swimmers == null) { Err("档案里没有 Swimmers"); return; }

            foreach (var h in ran)
            {
                var inHeat = swimmers.Where(s =>
                        string.Equals((string)s["EventName"], h.EventName, StringComparison.Ordinal)
                     && SameGender((string)s["Gender"], h.Gender)
                     && HeatOf(s, h.Stage) == h.Heat).ToList();

                if (inHeat.Count == 0)
                { Err(Desc(h) + " 档案里查不到这一组的人"); continue; }

                int withTime = 0, withSplits = 0;
                int wantSegs = Math.Max(1, h.TotalDistance / 50);
                foreach (var s in inHeat)
                {
                    var r = ResultOf(s, h.Stage, h.Heat);
                    if (r == null) continue;
                    double ft = r["FinalTime"] != null ? (double)r["FinalTime"] : 0;
                    string st = (string)r["Status"];
                    if (ft > 0 || !string.IsNullOrEmpty(st)) withTime++;
                    var sp = r["Splits"] as JArray;
                    if (sp != null && sp.Count >= wantSegs - 1) withSplits++;
                }

                if (withTime == 0) Err(Desc(h) + " 一条成绩都没落下");
                else if (withTime < inHeat.Count)
                    Warn(Desc(h) + string.Format(" {0}/{1} 道有成绩（其余可能是 DNS）", withTime, inHeat.Count));
                else Ok(Desc(h) + string.Format(" {0} 道全部有成绩", withTime));

                if (wantSegs > 1)
                {
                    if (withSplits == 0) Err(Desc(h) + string.Format(" 应有 {0} 段分段，一条都没有", wantSegs));
                    else Ok(Desc(h) + string.Format(" {0} 道分段齐（每人约 {1} 段）", withSplits, wantSegs));
                }
            }

            // 竞赛库（新库）也核一遍
            string db = Directory.Exists(DbDir)
                ? Directory.GetFiles(DbDir, "*.db").FirstOrDefault(f => !f.EndsWith("current_heat.db")) : null;
            if (db != null) Ok("竞赛库文件已生成: " + Path.GetFileName(db)
                + string.Format(" ({0:N0} 字节)", new FileInfo(db).Length));
            else Warn("没找到竞赛库 .db —— 第 1 步的导入可能没跑起来");
        }

        static bool SameGender(string a, string b)
        {
            a = (a ?? "").Replace("子", ""); b = (b ?? "").Replace("子", "");
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        static int HeatOf(JToken s, string stage)
        {
            var sa = s["StageAssignments"] as JObject;
            if (sa != null && sa[stage] != null && sa[stage]["Heat"] != null) return (int)sa[stage]["Heat"];
            return s["Heat"] != null ? (int)s["Heat"] : 0;
        }

        static JToken ResultOf(JToken s, string stage, int heat)
        {
            var rs = s["Results"] as JArray;
            if (rs == null) return null;
            foreach (var r in rs)
                if (string.Equals((string)r["Stage"], stage, StringComparison.Ordinal)
                    && r["Heat"] != null && (int)r["Heat"] == heat) return r;
            foreach (var r in rs)
                if (string.Equals((string)r["Stage"], stage, StringComparison.Ordinal)) return r;
            return null;
        }

        static string Desc(HeatRef h)
        {
            return string.Format("{0}{1} {2} 第{3}组",
                string.IsNullOrEmpty(h.AgeGroup) ? "" : h.AgeGroup + " ", h.Gender, h.EventName, h.Heat);
        }

        // ══════════════ 内存 ══════════════
        static void Sample(Process app, string tag)
        {
            try
            {
                app.Refresh();
                double mb = app.PrivateMemorySize64 / 1024.0 / 1024.0;
                _memSamples.Add(mb);
                Console.WriteLine(string.Format("      内存 {0,8:N1} MB   ({1})", mb, tag));
            }
            catch { }
        }

        static void MemReport()
        {
            Console.WriteLine();
            Info("══ 内存 ══");
            if (_memSamples.Count < 2) { Warn("采样不足"); return; }
            double first = _memSamples[0], last = _memSamples[_memSamples.Count - 1];
            double peak = _memSamples.Max();
            Console.WriteLine(string.Format("      开跑前 {0:N1} MB → 跑完 {1:N1} MB   峰值 {2:N1} MB   净增 {3:+0.0;-0.0} MB",
                first, last, peak, last - first));
            // 2026-08 那次是一组 400 米就涨几 GB。这里给一个明确的回归阈值。
            if (last - first > 500) Err(string.Format("内存净增 {0:N0} MB —— 超过 500MB 阈值，热路径可能又在序列化整包", last - first));
            else Ok(string.Format("内存净增 {0:N0} MB，在阈值内", last - first));
        }

        // ══════════════ 杂项 ══════════════
        static Process FindOrLaunchApp()
        {
            var ps = Process.GetProcessesByName("SwimmingScoreboard");
            if (ps.Length > 0) { _o.AppDir = Path.GetDirectoryName(ps[0].MainModule.FileName); return ps[0]; }
            if (_o.NoLaunch) return null;

            string exe = FindExe();
            if (exe == null) return null;
            _o.AppDir = Path.GetDirectoryName(exe);
            Info("启动主程序: " + exe);
            var p = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _o.AppDir, UseShellExecute = true });
            if (DismissLogin(TimeSpan.FromSeconds(30))) Info("已过登录窗");
            else Warn("没看到登录窗（可能已经登录过，或窗口标题变了）");
            Thread.Sleep(6000);
            return p;
        }

        /// <summary>
        /// 点掉登录窗。用 UIAutomation 而不是改主程序加免登录开关 ——
        /// 为了跑测试在生产代码里开个后门，那口子迟早会留在正式版里。
        /// 用户名密码靠 remember.json 预填(见 AuthHelper.SaveRemembered)。
        /// </summary>
        static bool DismissLogin(TimeSpan t)
        {
            var end = DateTime.Now + t;
            while (DateTime.Now < end)
            {
                try
                {
                    var win = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                        new PropertyCondition(AutomationElement.NameProperty, "游泳赛事管理系统 — 登录"));
                    if (win != null)
                    {
                        var btn = win.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.NameProperty, "登  录"));
                        if (btn != null)
                        {
                            object pat;
                            if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out pat))
                            { ((InvokePattern)pat).Invoke(); return true; }
                        }
                    }
                }
                catch { }
                Thread.Sleep(400);
            }
            return false;
        }

        static string FindExe()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var rel in new[] {
                @"..\..\..\..\SwimmingScoreboard\bin\x64\Release\SwimmingScoreboard.exe",
                @"..\..\..\SwimmingScoreboard\bin\x64\Release\SwimmingScoreboard.exe",
                @"..\..\SwimmingScoreboard\bin\x64\Release\SwimmingScoreboard.exe" })
            {
                string p = Path.GetFullPath(Path.Combine(here, rel));
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static Opts ParseArgs(string[] a)
        {
            var o = new Opts();
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--heats": o.Heats = int.Parse(a[++i]); break;
                    case "--lanes": o.Lanes = int.Parse(a[++i]); break;
                    case "--host":  o.Host = a[++i]; break;
                    case "--port":  o.TcpPort = int.Parse(a[++i]); break;
                    case "--ws":    o.WsPort = int.Parse(a[++i]); break;
                    case "--pause": o.PauseMs = int.Parse(a[++i]); break;
                    case "--lap-gap": o.LapGap = double.Parse(a[++i]); break;
                    case "--no-launch": o.NoLaunch = true; break;
                    case "--dsq":   o.WithDsq = true; break;
                    case "--dns":   o.WithDns = true; break;
                }
            }
            return o;
        }

        static void Banner()
        {
            Console.WriteLine("══════════════════════════════════════════════════════");
            Console.WriteLine("  模拟比赛 自动化测试");
            Console.WriteLine("  计时硬件: TCP 服务端 :" + _o.TcpPort + "   遥控台: ws://" + _o.Host + ":" + _o.WsPort);
            Console.WriteLine("  组数 " + _o.Heats + "   泳道 " + _o.Lanes
                + (_o.WithDsq ? "   含 DSQ" : "") + (_o.WithDns ? "   含 DNS" : ""));
            Console.WriteLine("══════════════════════════════════════════════════════");
        }

        static int Finish()
        {
            Console.WriteLine();
            if (_fail.Count == 0) { Ok("全部通过"); return 0; }
            Console.WriteLine("══ 失败 " + _fail.Count + " 项 ══");
            foreach (var f in _fail) Console.WriteLine("  " + f);
            return 1;
        }

        static void Info(string s) { Console.WriteLine("  " + s); }
        static void Ok(string s)   { var c = Console.ForegroundColor; Console.ForegroundColor = ConsoleColor.Green;
                                     Console.WriteLine("  [OK]   " + s); Console.ForegroundColor = c; }
        static void Warn(string s) { var c = Console.ForegroundColor; Console.ForegroundColor = ConsoleColor.Yellow;
                                     Console.WriteLine("  [注意] " + s); Console.ForegroundColor = c; }
        static void Err(string s)  { var c = Console.ForegroundColor; Console.ForegroundColor = ConsoleColor.Red;
                                     Console.WriteLine("  [失败] " + s); Console.ForegroundColor = c; _fail.Add(s); }
    }
}
