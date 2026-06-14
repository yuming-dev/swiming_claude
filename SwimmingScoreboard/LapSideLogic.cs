namespace SwimmingScoreboard
{
    // 2026-06-14 按左/右侧计圈的纯逻辑 (无 UI / 状态依赖), 供主程序 (ProcessTouchpadHit/GetTouchRemain) 与
    //   自动化测试 (LapCountTest) 共用同一份, 保证"测的就是真实逻辑".
    //   语义: 50m 池, totalLaps = 总段数. 出发端在 偶数段(2,4,...) 触, 到达端(远端) 在 奇数段(1,3,...) 触.
    //   start=left 时 左=出发端 / 右=到达端; start=right 反之.
    public static class LapSideLogic
    {
        // 该侧总触板次数: 出发端 = totalLaps/2 (偶数段); 到达端 = (totalLaps+1)/2 (奇数段); 50m(totalLaps=1) 出0/到1.
        public static int SideTouchTotal(int totalLaps, bool startFromLeft, bool isLeft)
        {
            int startSideTotal, farSideTotal;
            if (totalLaps == 1) { startSideTotal = 0; farSideTotal = 1; }
            else { startSideTotal = totalLaps / 2; farSideTotal = (totalLaps + 1) / 2; }
            if (startFromLeft) return isLeft ? startSideTotal : farSideTotal;
            return isLeft ? farSideTotal : startSideTotal;
        }

        // 该侧剩余 = 该侧总次数 − 该侧实际已触次数 (不小于 0). 两侧完全独立, 互不牵连.
        public static int Remain(int totalLaps, bool startFromLeft, bool isLeft, int sideDone)
        {
            int r = SideTouchTotal(totalLaps, startFromLeft, isLeft) - sideDone;
            return r < 0 ? 0 : r;
        }

        // 缺硬件实际 side 时 (手动/盲代/跳圈) 按"触后累计段号"奇偶推断该段触板端: 偶=出发端, 奇=到达端.
        public static string InferSide(int currentLapAfter, bool startFromLeft)
        {
            bool atStartSide = (currentLapAfter % 2 == 0);
            return (atStartSide == startFromLeft) ? "left" : "right";
        }

        // 触某侧后的物理游动方向 (与大屏 sw.direction 一致): 触左→"→"(朝右) / 触右→"←"(朝左).
        public static string DirectionFor(string touchSide)
        {
            return (touchSide == "left") ? "→" : "←";
        }

        // 触某侧后即将到达(开启)的下一侧 = 对侧. 触左→右; 触右→左.
        public static bool NextOpenIsRight(string touchSide)
        {
            return touchSide == "left";
        }

        // 2026-06-14 封闭时间去抖 (学习硬件 Close_Time): 同侧上次计圈时刻 lastSideTime 之后 closeWin 秒内的
        //   同侧触板 = 重复(抖动/坏TP重发), 返回 true 表示应忽略不计圈. (物理上同侧两圈相隔数十秒, 不会误杀真实圈)
        public static bool IsRepeatWithinClose(double touchTime, double lastSideTime, double closeWin)
        {
            return lastSideTime > 0 && touchTime > 0 && (touchTime - lastSideTime) < closeWin;
        }

        // 去抖窗口 = 泳道封闭时间, 钳到 [2,10]s (防误配过小漏抖动 / 过大误杀真实圈).
        public static double ClampCloseWin(double laneCloseTime)
        {
            double w = laneCloseTime;
            if (w < 2) w = 2;
            if (w > 10) w = 10;
            return w;
        }
    }
}
