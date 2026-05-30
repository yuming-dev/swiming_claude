using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SwimmingScoreboard;

namespace LapTestSim
{
    // 2026-05-29 LapTestSim — 枚举 AdjustLapDisplay 的所有场景, 输出 CSV + 控制台表格
    // 共享主项目 LapAdjustLogic.cs (link), 保证测试逻辑跟生产代码同源
    class Program
    {
        class RaceConfig
        {
            public string Name;
            public int TotalLaps;
            public int LapsPerLeg;  // 0 = 个人
            public bool IsRelay;
            public int LeftMaxDisp;
            public int RightMaxDisp;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var configs = new List<RaceConfig>
            {
                // 接力
                new RaceConfig { Name="4×50m  池50m",  TotalLaps=4,  LapsPerLeg=1, IsRelay=true,  LeftMaxDisp=2, RightMaxDisp=2 },
                new RaceConfig { Name="4×100m 池50m",  TotalLaps=8,  LapsPerLeg=2, IsRelay=true,  LeftMaxDisp=4, RightMaxDisp=4 },
                new RaceConfig { Name="4×200m 池50m",  TotalLaps=16, LapsPerLeg=4, IsRelay=true,  LeftMaxDisp=8, RightMaxDisp=8 },
                // 个人
                new RaceConfig { Name="50m   个人  池50m",  TotalLaps=1, LapsPerLeg=0, IsRelay=false, LeftMaxDisp=0, RightMaxDisp=1 },
                new RaceConfig { Name="100m  个人  池50m",  TotalLaps=2, LapsPerLeg=0, IsRelay=false, LeftMaxDisp=1, RightMaxDisp=1 },
                new RaceConfig { Name="200m  个人  池50m",  TotalLaps=4, LapsPerLeg=0, IsRelay=false, LeftMaxDisp=2, RightMaxDisp=2 },
                new RaceConfig { Name="400m  个人  池50m",  TotalLaps=8, LapsPerLeg=0, IsRelay=false, LeftMaxDisp=4, RightMaxDisp=4 },
            };

            string outDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string csvPath = Path.Combine(outDir, "LapAdjust_TestReport.csv");

            using (var fout = new StreamWriter(csvPath, false, new UTF8Encoding(true)))
            {
                fout.WriteLine("Race,StartPosition,RaceState,OldCurrentLap,LeftRemain,RightRemain,IsLeftSpinner,Delta,Verdict,NewCur,OpenTpMb,SbOpenSide,Direction,LogMessage,SecondPassVerdict,SecondPassNewCur");

                int total = 0, ok = 0, def1 = 0, def2 = 0, def3 = 0, def4 = 0, nochange = 0;
                int def4Pass = 0, def4Fail = 0;
                var def4Details = new List<string>();
                // SB 分布统计: 给每个接力配置, 记录 Ok 用例里 SB 开/关的 newCur 集合
                var sbOpenNewCurs = new Dictionary<string, SortedSet<int>>();
                var sbCloseNewCurs = new Dictionary<string, SortedSet<int>>();

                foreach (var cfg in configs)
                {
                    Console.WriteLine();
                    Console.WriteLine("════════════════════════════════════════════════════════════════════");
                    Console.WriteLine("  {0}  (totalLaps={1}, lapsPerLeg={2}, IsRelay={3}, leftMax={4}, rightMax={5})",
                        cfg.Name, cfg.TotalLaps, cfg.LapsPerLeg, cfg.IsRelay, cfg.LeftMaxDisp, cfg.RightMaxDisp);
                    Console.WriteLine("════════════════════════════════════════════════════════════════════");

                    foreach (var startPos in new[] { "left", "right" })
                    {
                        bool startFromLeft = (startPos != "right");
                        Console.WriteLine();
                        Console.WriteLine("─── 起跑端 = {0} ───", startPos);
                        Console.WriteLine("oldCur | L剩 | R剩 |  L▼  |  L▲  |  R▼  |  R▲");
                        Console.WriteLine("------ | --- | --- | ---- | ---- | ---- | ----");

                        // 枚举 oldCur 0..totalLaps, 计算左右 remain
                        for (int oldCur = 0; oldCur <= cfg.TotalLaps; oldCur++)
                        {
                            int leftDone, rightDone;
                            if (startFromLeft)
                            {
                                leftDone = oldCur / 2;
                                rightDone = (oldCur + 1) / 2;
                            }
                            else
                            {
                                rightDone = oldCur / 2;
                                leftDone = (oldCur + 1) / 2;
                            }
                            int leftRemain = cfg.LeftMaxDisp - leftDone;
                            int rightRemain = cfg.RightMaxDisp - rightDone;
                            if (leftRemain < 0) leftRemain = 0;
                            if (rightRemain < 0) rightRemain = 0;

                            string[] cells = new string[4];   // L▼, L▲, R▼, R▲
                            int idx = 0;
                            // Race state 默认 Racing (= 防线 4 可能触发)
                            foreach (var op in new[] {
                                new { IsLeft=true, Delta=-1 },   // L▼
                                new { IsLeft=true, Delta=+1 },   // L▲
                                new { IsLeft=false, Delta=-1 },  // R▼
                                new { IsLeft=false, Delta=+1 },  // R▲
                            })
                            {
                                var inp = new LapAdjustInput
                                {
                                    TotalLaps = cfg.TotalLaps,
                                    StartPosition = startPos,
                                    IsRelay = cfg.IsRelay,
                                    LapsPerLeg = cfg.LapsPerLeg,
                                    RaceIsRacing = true,
                                    OldCurrentLap = oldCur,
                                    OldLeftRemain = leftRemain,
                                    OldRightRemain = rightRemain,
                                    LeftMaxDisp = cfg.LeftMaxDisp,
                                    RightMaxDisp = cfg.RightMaxDisp,
                                    IsLeft = op.IsLeft,
                                    Delta = op.Delta,
                                };
                                var r = LapAdjustLogic.Compute(inp);
                                total++;
                                switch (r.Verdict)
                                {
                                    case LapAdjustVerdict.Ok: ok++; break;
                                    case LapAdjustVerdict.Defense1_DispOutOfRange: def1++; break;
                                    case LapAdjustVerdict.Defense2_LapOutOfRange:  def2++; break;
                                    case LapAdjustVerdict.Defense3_WrongSpinner:   def3++; break;
                                    case LapAdjustVerdict.Defense4_NeedConfirm:    def4++; break;
                                    case LapAdjustVerdict.NoChange: nochange++; break;
                                }

                                // 防 4 二次确认: ConfirmAfterRacingZero=true 应该返回 Ok + newCur=0
                                string secondVerdict = "-";
                                string secondNewCur = "-";
                                bool secondPassed = false;
                                if (r.Verdict == LapAdjustVerdict.Defense4_NeedConfirm)
                                {
                                    var inp2 = new LapAdjustInput
                                    {
                                        TotalLaps = inp.TotalLaps,
                                        StartPosition = inp.StartPosition,
                                        IsRelay = inp.IsRelay,
                                        LapsPerLeg = inp.LapsPerLeg,
                                        RaceIsRacing = inp.RaceIsRacing,
                                        OldCurrentLap = inp.OldCurrentLap,
                                        OldLeftRemain = inp.OldLeftRemain,
                                        OldRightRemain = inp.OldRightRemain,
                                        LeftMaxDisp = inp.LeftMaxDisp,
                                        RightMaxDisp = inp.RightMaxDisp,
                                        IsLeft = inp.IsLeft,
                                        Delta = inp.Delta,
                                        ConfirmAfterRacingZero = true,
                                    };
                                    var r2 = LapAdjustLogic.Compute(inp2);
                                    secondVerdict = r2.Verdict.ToString();
                                    secondNewCur = r2.Verdict == LapAdjustVerdict.Ok ? r2.NewCurrentLap.ToString() : "-";
                                    secondPassed = (r2.Verdict == LapAdjustVerdict.Ok && r2.NewCurrentLap == 0);
                                    if (secondPassed) def4Pass++; else def4Fail++;
                                    string sbDesc = r2.SbOpenSide == 0 ? "关" : r2.SbOpenSide == 1 ? "开左" : "开右";
                                    def4Details.Add(string.Format(
                                        "  {0} | 起{1} | oldCur={2,2} | {3}{4} | 2nd→{5} newCur={6} SB={7} {8}",
                                        cfg.Name, startPos, oldCur,
                                        op.IsLeft ? "L" : "R", op.Delta > 0 ? "▲" : "▼",
                                        r2.Verdict, secondNewCur, sbDesc, secondPassed ? "✓" : "✗"));
                                }

                                // SB 分布统计 (接力 Ok 用例): 记录 newCur 的 SB 开/关情况
                                if (r.Verdict == LapAdjustVerdict.Ok && cfg.IsRelay)
                                {
                                    if (!sbOpenNewCurs.ContainsKey(cfg.Name))
                                    {
                                        sbOpenNewCurs[cfg.Name] = new SortedSet<int>();
                                        sbCloseNewCurs[cfg.Name] = new SortedSet<int>();
                                    }
                                    if (r.SbOpenSide != 0) sbOpenNewCurs[cfg.Name].Add(r.NewCurrentLap);
                                    else sbCloseNewCurs[cfg.Name].Add(r.NewCurrentLap);
                                }

                                cells[idx++] = VerdictShortWithSecond(r.Verdict, secondPassed);
                                // CSV 行
                                fout.WriteLine("{0},{1},Racing,{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}",
                                    cfg.Name, startPos, oldCur, leftRemain, rightRemain,
                                    op.IsLeft, op.Delta,
                                    r.Verdict,
                                    r.Verdict == LapAdjustVerdict.Ok ? r.NewCurrentLap.ToString() : "-",
                                    r.Verdict == LapAdjustVerdict.Ok ? r.OpenTpMbSide : "-",
                                    r.SbOpenSide,
                                    r.Verdict == LapAdjustVerdict.Ok ? r.Direction : "-",
                                    EscapeCsv(r.LogMessage ?? r.ErrorMessage ?? ""),
                                    secondVerdict,
                                    secondNewCur);
                            }
                            Console.WriteLine("  {0,2}   |  {1}  |  {2}  | {3} | {4} | {5} | {6}",
                                oldCur, leftRemain, rightRemain, cells[0], cells[1], cells[2], cells[3]);
                        }
                    }
                }

                Console.WriteLine();
                Console.WriteLine("════════════════════════════════════════════════════════════════════");
                Console.WriteLine("  防 4 二次确认明细:");
                Console.WriteLine("════════════════════════════════════════════════════════════════════");
                foreach (var d in def4Details) Console.WriteLine(d);

                Console.WriteLine();
                Console.WriteLine("════════════════════════════════════════════════════════════════════");
                Console.WriteLine("  接力 SB 分布 (Ok 用例): newCur 哪些时 SB 开 / 关");
                Console.WriteLine("════════════════════════════════════════════════════════════════════");
                foreach (var kv in sbOpenNewCurs)
                {
                    var openSet = sbOpenNewCurs[kv.Key];
                    var closeSet = sbCloseNewCurs[kv.Key];
                    Console.WriteLine("  {0}", kv.Key);
                    Console.WriteLine("    SB 开 newCur: {{{0}}}", string.Join(",", openSet));
                    Console.WriteLine("    SB 关 newCur: {{{0}}}", string.Join(",", closeSet));
                }

                Console.WriteLine();
                Console.WriteLine("════════════════════════════════════════════════════════════════════");
                Console.WriteLine("  汇总: total={0}, Ok={1}, 防1={2}, 防2={3}, 防3={4}, 防4={5}, 无变={6}",
                    total, ok, def1, def2, def3, def4, nochange);
                Console.WriteLine("  防 4 二次确认通过率: {0}/{1} {2}", def4Pass, def4, def4Fail == 0 ? "✓ 全通过" : "✗ 有失败");
                Console.WriteLine("  CSV 报告: {0}", csvPath);
                Console.WriteLine("════════════════════════════════════════════════════════════════════");
                Console.WriteLine();
                Console.WriteLine("图例: Ok=通过 | 防1=该侧越界 | 防2=CurrentLap 越界 | 防3=按错 spinner");
                Console.WriteLine("      防4✓=清零确认后通过 | 防4✗=确认失败 | 无变=newDisp==curDisp");
            }
            return 0;
        }

        static string VerdictShort(LapAdjustVerdict v, LapAdjustResult r)
        {
            switch (v)
            {
                case LapAdjustVerdict.Ok:                        return " Ok ";
                case LapAdjustVerdict.Defense1_DispOutOfRange:   return "防1边";
                case LapAdjustVerdict.Defense2_LapOutOfRange:    return "防2圈";
                case LapAdjustVerdict.Defense3_WrongSpinner:     return "防3错";
                case LapAdjustVerdict.Defense4_NeedConfirm:      return "防4确";
                case LapAdjustVerdict.NoChange:                  return "无变";
                default: return "  ?  ";
            }
        }

        static string VerdictShortWithSecond(LapAdjustVerdict v, bool secondPassed)
        {
            if (v == LapAdjustVerdict.Defense4_NeedConfirm)
                return secondPassed ? "防4✓" : "防4✗";
            return VerdictShort(v, null);
        }

        static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
