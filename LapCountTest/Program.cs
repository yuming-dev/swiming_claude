using System;
using System.Collections.Generic;
using SwimmingScoreboard;   // 共享 LapSideLogic (= 主程序真实计圈数学)

namespace LapCountTest
{
    // 2026-06-14 自动化测试: 模拟一条泳道的"按侧计圈 + TP 开关"状态机, 逻辑镜像 MainWindow.ProcessTouchpadHit
    //   + CountdownTimer_Tick, 核心数学复用共享 LapSideLogic (与主程序同源). 用来验证特殊比赛情况下计圈是否正确.
    class LaneSim
    {
        public int TotalLaps; public bool StartFromLeft;
        public int LeftDone, RightDone, CurrentLap; public string Direction; public bool Finished;
        public bool LeftOpen, RightOpen;        // 该侧 TP 是否打开 (= 可接收硬件触板; 关时丢弃, 镜像状态机)

        public LaneSim(int totalLaps, bool startFromLeft) {
            TotalLaps = totalLaps; StartFromLeft = startFromLeft;
            Direction = startFromLeft ? "→" : "←";
            OpenNext(StartFromLeft ? "right" : "left");   // 第1段在远端 (start=left → 远端=右)
        }
        void OpenNext(string sideToOpen) {
            LeftOpen = false; RightOpen = false;
            if (sideToOpen == "left" && Remain(true) > 0) LeftOpen = true;
            else if (sideToOpen == "right" && Remain(false) > 0) RightOpen = true;
        }
        public int Remain(bool isLeft) {
            return LapSideLogic.Remain(TotalLaps, StartFromLeft, isLeft, isLeft ? LeftDone : RightDone);
        }
        // 硬件发来一次某侧触板. 返回是否被接收 (该侧 Open 才接收, 否则丢弃 = 镜像主程序状态机).
        public bool Touch(string side) {
            if (Finished) return false;
            bool open = (side == "left") ? LeftOpen : RightOpen;
            if (!open) return false;   // 设备关 → 丢弃 (硬件多发/杂散帧)
            CurrentLap++;
            string touchSide = (side == "left" || side == "right") ? side : LapSideLogic.InferSide(CurrentLap, StartFromLeft);
            if (touchSide == "left") LeftDone++; else RightDone++;
            Direction = LapSideLogic.DirectionFor(touchSide);
            if (touchSide == "left") LeftOpen = false; else RightOpen = false;
            if (CurrentLap >= TotalLaps) { Finished = true; LeftOpen = false; RightOpen = false; }
            else OpenNext(LapSideLogic.NextOpenIsRight(touchSide) ? "right" : "left");
            return true;
        }
    }

    static class Program
    {
        static int failures = 0, passes = 0;
        static void Check(bool cond, string msg) {
            Console.WriteLine((cond ? "  [PASS] " : "  [FAIL] ") + msg);
            if (cond) passes++; else failures++;
        }

        static void Main() {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== 按侧计圈 自动化测试 (LapSideLogic 与主程序同源) ===\n");

            ScenarioNormal(1, true, "50m (1段)");
            ScenarioNormal(2, true, "100m (2段)");
            ScenarioNormal(4, true, "200m (4段)");
            ScenarioNormal(30, true, "1500m (30段)");
            ScenarioNormal(30, false, "1500m start=right");
            ScenarioNormal(4, true, "4x50 接力 (4段)");
            ScenarioNormal(8, true, "4x100 接力 (8段)");

            ScenarioExtraSameSide(30, true, "1500m 坏TP多发(已关侧)");
            ScenarioExtraOnOpenSide(30, true, "1500m 同侧连发(开侧重发)");
            ScenarioCrossSideIndependence();
            ScenarioCloseDebounce();
            ScenarioOldModelWouldFail();

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? string.Format("==== 全部通过 ({0} 项) ====", passes)
                : string.Format("==== {0} 失败 / {1} 通过 ====", failures, passes));
            Environment.Exit(failures == 0 ? 0 : 1);
        }

        // 正常交替: 每段在开着的那侧触板, 验证完赛圈数 + 两侧剩余归0
        static void ScenarioNormal(int totalLaps, bool startLeft, string name) {
            Console.WriteLine("场景: " + name);
            var sim = new LaneSim(totalLaps, startLeft);
            int guard = 0;
            while (!sim.Finished && guard++ < totalLaps + 5) {
                string side = sim.LeftOpen ? "left" : (sim.RightOpen ? "right" : null);
                if (side == null) { Check(false, "中途无侧开启 (lap " + sim.CurrentLap + ")"); break; }
                sim.Touch(side);
            }
            Check(sim.Finished, "完赛");
            Check(sim.CurrentLap == totalLaps, "完赛圈数==" + totalLaps + " (实际 " + sim.CurrentLap + ")");
            Check(sim.Remain(true) == 0 && sim.Remain(false) == 0, "两侧剩余均为0");
            Console.WriteLine();
        }

        // 坏TP在"已关侧"狂发: 应全部丢弃, 任何计数/剩余不变, 之后仍能正常完赛 (= 不丢数据)
        static void ScenarioExtraSameSide(int totalLaps, bool startLeft, string name) {
            Console.WriteLine("场景: " + name);
            var sim = new LaneSim(totalLaps, startLeft);
            for (int i = 0; i < 10 && !sim.Finished; i++)
                sim.Touch(sim.LeftOpen ? "left" : "right");
            int lD = sim.LeftDone, rD = sim.RightDone, lap = sim.CurrentLap;
            string closed = sim.LeftOpen ? "right" : "left";   // 当前关着的那侧
            for (int k = 0; k < 5; k++) sim.Touch(closed);
            Check(sim.LeftDone == lD && sim.RightDone == rD, "已关侧多发被丢弃, 两侧 done 不变");
            Check(sim.CurrentLap == lap, "CurrentLap 不变");
            int guard = 0;
            while (!sim.Finished && guard++ < totalLaps + 5)
                sim.Touch(sim.LeftOpen ? "left" : (sim.RightOpen ? "right" : "left"));
            Check(sim.Finished && sim.CurrentLap == totalLaps, "之后仍正常完赛 (未丢后续数据)");
            Console.WriteLine();
        }

        // 开侧被坏TP重发(同一开侧连发多帧): 第1帧接收, 该侧立即关 → 其余被丢弃, 只 +1, 绝不牵连另一侧
        static void ScenarioExtraOnOpenSide(int totalLaps, bool startLeft, string name) {
            Console.WriteLine("场景: " + name);
            var sim = new LaneSim(totalLaps, startLeft);
            for (int i = 0; i < 6 && !sim.Finished; i++)
                sim.Touch(sim.LeftOpen ? "left" : "right");
            string openSide = sim.LeftOpen ? "left" : "right";
            bool openIsLeft = (openSide == "left");
            int thisBefore  = sim.Remain(openIsLeft);    // 开侧剩余
            int otherBefore = sim.Remain(!openIsLeft);   // 另一侧剩余
            for (int k = 0; k < 4; k++) sim.Touch(openSide);   // 同一开侧连发 4 帧
            int thisAfter  = sim.Remain(openIsLeft);
            int otherAfter = sim.Remain(!openIsLeft);
            Check(otherAfter == otherBefore, "开侧连发: 另一侧剩余完全不变");
            Check(thisBefore - thisAfter == 1, "开侧连发: 该侧只 −1 (其余帧丢弃)");
            Console.WriteLine();
        }

        // 核心不变量: 触任一侧, 另一侧剩余必不变 (= 旧"奇偶推算"模型会失败的关键点)
        static void ScenarioCrossSideIndependence() {
            Console.WriteLine("场景: 跨侧独立性 (触一侧绝不改另一侧剩余)");
            var sim = new LaneSim(30, true);
            var rng = new Random(12345);
            bool ok = true; int n = 0;
            for (int i = 0; i < 500 && !sim.Finished; i++) {
                bool tryLeft = rng.Next(3) != 0;   // 偏向制造非交替序列
                int beforeOther = sim.Remain(!tryLeft);
                bool acc = sim.Touch(tryLeft ? "left" : "right");
                int afterOther = sim.Remain(!tryLeft);
                if (acc) n++;
                if (beforeOther != afterOther) { ok = false; break; }
            }
            Check(ok, "随机/非交替序列下, 触一侧时另一侧剩余始终不变 (接收 " + n + " 次)");
            Console.WriteLine();
        }

        // 同侧去抖: 用现有两参数(泳道关闭时间/成绩确认关闭延迟)取窗口, 验证抖动/坏TP重发只计真实圈
        static void ScenarioCloseDebounce() {
            Console.WriteLine("场景: 同侧去抖 (泳道关闭时间/成绩确认关闭延迟)");
            // 窗口直接来自现有参数 (无任意钳制): 硬件同步后 Close_Time=6 → max(6,3)=6; PC 默认 20 → max(20,3)=20
            Check(LapSideLogic.DebounceWindow(6, 3) == 6, "硬件Close_Time=6,确认延迟=3 → 窗口6s");
            Check(LapSideLogic.DebounceWindow(20, 3) == 20, "PC默认 泳道关闭=20,确认延迟=3 → 窗口20s");
            Check(LapSideLogic.DebounceWindow(0, 0) == 3, "两参数皆0 → 兜底3s");
            double win = LapSideLogic.DebounceWindow(6, 3);   // 硬件 Close_Time 同步后
            // 实测右侧序列(秒): 1 次真实触板 + 多次抖动重发(秒内), 之后下一来回真实触板(~70s后)
            double[] rightTouches = { 161.08, 161.16, 161.38, 161.77, 162.45, 162.72, 230.00, 230.06 };
            int counted = 0; double last = 0;
            foreach (var t in rightTouches) {
                if (!LapSideLogic.IsRepeatWithinClose(t, last, win)) { counted++; last = t; }
            }
            Check(counted == 2, "8 次同侧帧(含抖动,窗口6s) → 只计 2 圈 (161.08 与 230.0), 实计 " + counted + " (旧无去抖会全计 8 → 多减圈)");
            // 边界: 恰好等于窗口的不算重复(>=win 才计)
            Check(!LapSideLogic.IsRepeatWithinClose(10.0, 4.0, 6.0), "间隔==窗口(6s) 视为新圈, 不去抖");
            Check(LapSideLogic.IsRepeatWithinClose(9.99, 4.0, 6.0), "间隔<窗口(5.99s) 视为抖动, 去抖");
            Console.WriteLine();
        }

        // 对照: 旧"单CurrentLap奇偶推算"模型在某侧多触板时会牵连另一侧 (证明本测试能抓到该 bug)
        static void ScenarioOldModelWouldFail() {
            Console.WriteLine("场景: 对照旧模型 (奇偶推算, 应失败=证明测试有效)");
            int totalLaps = 30; bool startLeft = true;
            int startSideTotal = totalLaps / 2, farSideTotal = (totalLaps + 1) / 2;
            Func<int, int> oldLeftRem = cur => {
                int startDone = cur / 2, farDone = (cur + 1) / 2;
                return startLeft ? Math.Max(0, startSideTotal - startDone) : Math.Max(0, farSideTotal - farDone);
            };
            Func<int, int> oldRightRem = cur => {
                int startDone = cur / 2, farDone = (cur + 1) / 2;
                return startLeft ? Math.Max(0, farSideTotal - farDone) : Math.Max(0, startSideTotal - startDone);
            };
            // 游到 cur=10, 再"多一次触板"(cur=11). 旧模型: 右侧剩余从 cur=10→11 变了 (即使这次是左侧触板) → 牵连
            int curBefore = 10, curAfter = 11;
            int rRemBefore = oldRightRem(curBefore), rRemAfter = oldRightRem(curAfter);
            Check(rRemBefore != rRemAfter, "旧模型: CurrentLap+1 牵连右侧剩余 " + rRemBefore + "→" + rRemAfter + " (= 旧 bug, 新模型已修复)");
            Console.WriteLine();
        }
    }
}
