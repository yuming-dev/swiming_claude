using System;

namespace SwimmingScoreboard
{
    //2026-05-29 LapAdjustLogic — 抽出 AdjustLapDisplay 的纯逻辑部分, 不依赖 UI / LaneDeviceState / MessageBox
    // 用途:
    //   1. 主程序 AdjustLapDisplay 调用 Compute() 拿到 verdict + 期望状态, 然后做 UI 操作 (弹窗 / 设状态 / 发硬件)
    //   2. 测试程序 LapTestSim 调用 Compute() 枚举所有场景, 输出 CSV 报告
    // 算法:
    //   防线 1: 该侧剩余范围 [0, maxDispThis]
    //   防线 2: CurrentLap 范围 [0, totalLaps]
    //   防线 3: isLeft 一致性 (按错 spinner) — 用 StartPosition + (▼ newCur / ▲ oldCur) 奇偶推算物理触板端
    //   防线 4: 比赛进行中 (raceIsRacing=true) 让圈数从 >0 减到 0 时需要二次确认 (这里返回 NeedConfirm; 主程序弹 YesNo 后再次调用 ConfirmAfterRacingZero=true 才继续)
    //   通过后:
    //     newCur = oldCur - actualDelta
    //     方向 (5/30): Direction = OpenTpMbSide 朝向 (= 运动员物理游动方向, 跟 PC web sw.direction 一致)
    //     openTpMbSide: ▼ → otherSide; ▲ → userSide
    //     sbOpenSide (协议 d7) (5/30 v5, 游泳规则"SB 在起跑端" + 奇数 lapsPerLeg 边界):
    //       接力 + (nextLap<totalLaps && nextLap%lapsPerLeg==0 && nextLap%2==0): startFromLeft?1:2  (= 开 startPosition)
    //       其他 (含 newCur=0, 完赛, 非交接前, 远端交接): 0
    //       个人: 0
    //       4×50m 池 50m (lapsPerLeg=1 奇): 只 newCur=2 (棒 2→3 在 startPosition 交接) 开 SB; newCur=1/3 不开 (远端交接)
    //     openAction (协议 d6): ▼ → 1; ▲ → 2
    public enum LapAdjustVerdict
    {
        Ok,                       // 通过, 可以执行
        Defense1_DispOutOfRange,  // 防线 1: 该侧剩余圈数越界
        Defense2_LapOutOfRange,   // 防线 2: CurrentLap 越界
        Defense3_WrongSpinner,    // 防线 3: 按错 spinner
        Defense4_NeedConfirm,     // 防线 4: 需要二次确认 (比赛进行中圈数清零)
        NoChange,                 // newDisp == curDisp, 无操作
    }

    public class LapAdjustInput
    {
        // 比赛参数
        public int TotalLaps { get; set; }
        public string StartPosition { get; set; }   // "left" / "right"
        public bool IsRelay { get; set; }
        public int LapsPerLeg { get; set; }         // 接力时 = 每棒圈数; 个人 = 0 (不使用)
        public bool RaceIsRacing { get; set; }      // 比赛状态 == Racing (= 用于防线 4)
        // 操作参数
        public int OldCurrentLap { get; set; }
        public int OldLeftRemain { get; set; }      // 该侧当前剩余 (=curDisp if isLeft else otherSide's curDisp)
        public int OldRightRemain { get; set; }
        public int LeftMaxDisp { get; set; }
        public int RightMaxDisp { get; set; }
        public bool IsLeft { get; set; }            // 用户按的 spinner: true=左, false=右
        public int Delta { get; set; }              // +1 (▲) 或 -1 (▼)
        // 防线 4 二次确认 (主程序弹 YesNo 后用 ConfirmAfterRacingZero=true 再调用一次)
        public bool ConfirmAfterRacingZero { get; set; }
    }

    public class LapAdjustResult
    {
        public LapAdjustVerdict Verdict { get; set; }
        public string ErrorMessage { get; set; }    // 弹窗用
        public string ErrorTitle { get; set; }
        public string LogMessage { get; set; }       // AddLog 用

        // 通过后的状态变化
        public int NewCurrentLap { get; set; }
        public int NewDispThis { get; set; }
        public int ActualDelta { get; set; }
        public string OpenTpMbSide { get; set; }     // "left" / "right"
        public string CloseTpMbSide { get; set; }
        public string Direction { get; set; }        // "→" / "←" — 落地后翻转的结果
        public byte SbOpenSide { get; set; }         // 0/1/2 — 协议 d7
        public byte OpenAction { get; set; }         // 1/2 — 协议 d6
        public bool ShouldOpenSbLeft { get; set; }   // 接力时 SB 开左侧 (sbOpenSide==1)
        public bool ShouldOpenSbRight { get; set; }  // 接力时 SB 开右侧 (sbOpenSide==2)
        public bool ShouldDeleteLastSplit { get; set; }   // ▲ 误触回退 → 软删
    }

    public static class LapAdjustLogic
    {
        public static LapAdjustResult Compute(LapAdjustInput input)
        {
            var r = new LapAdjustResult();
            int curDispThis = input.IsLeft ? input.OldLeftRemain : input.OldRightRemain;
            int maxDispThis = input.IsLeft ? input.LeftMaxDisp : input.RightMaxDisp;
            int newDispThis = curDispThis + input.Delta;

            // 防线 1
            if (newDispThis < 0 || newDispThis > maxDispThis)
            {
                r.Verdict = LapAdjustVerdict.Defense1_DispOutOfRange;
                r.ErrorTitle = "⚠ 防线 1: 该侧圈数越界";
                r.ErrorMessage = string.Format("{0}侧剩余圈数已到边界\n当前: {1}, 范围 [0, {2}]\n本次操作: {3}\n操作已取消.",
                    input.IsLeft ? "左" : "右", curDispThis, maxDispThis, input.Delta > 0 ? "▲ +1" : "▼ -1");
                r.LogMessage = string.Format("❌ {0}侧圈数越界 (curDisp={1}, delta={2}, maxDisp={3})",
                    input.IsLeft ? "左" : "右", curDispThis, input.Delta, maxDispThis);
                return r;
            }
            if (newDispThis == curDispThis)
            {
                r.Verdict = LapAdjustVerdict.NoChange;
                return r;
            }
            int actualDelta = newDispThis - curDispThis;
            int oldCur = input.OldCurrentLap;
            int newCur = oldCur - actualDelta;

            // 防线 2
            if (newCur < 0 || newCur > input.TotalLaps)
            {
                r.Verdict = LapAdjustVerdict.Defense2_LapOutOfRange;
                r.ErrorTitle = "⚠ 防线 2: 圈数总范围越界";
                r.ErrorMessage = string.Format("CurrentLap: {0} → {1}\n范围 [0, {2}]\n操作已取消.",
                    oldCur, newCur, input.TotalLaps);
                r.LogMessage = string.Format("❌ CurrentLap 越界 ({0}→{1}, 范围 [0, {2}])",
                    oldCur, newCur, input.TotalLaps);
                return r;
            }

            // 防线 3
            int expectedTouchLap = (actualDelta < 0) ? newCur : oldCur;
            bool startFromLeft = (input.StartPosition != "right");
            bool expectedAtStartSide = (expectedTouchLap % 2 == 0);
            bool expectedAtLeft = expectedAtStartSide ? startFromLeft : !startFromLeft;
            if (input.IsLeft != expectedAtLeft)
            {
                r.Verdict = LapAdjustVerdict.Defense3_WrongSpinner;
                string opDesc = actualDelta < 0 ? "▼ 模拟触板 (漏触补救)" : "▲ 撤销触板 (误触回退)";
                string lapDesc = actualDelta < 0
                    ? string.Format("即将完成的第 {0} 圈", newCur)
                    : string.Format("即将撤销的第 {0} 圈", oldCur);
                r.ErrorTitle = "❌ 防线 3: 按错 spinner";
                r.ErrorMessage = string.Format("操作: {0}\n{1}\n该圈物理触板端: {2}\n\n按了: {3} spinner\n应按: {4} spinner\n操作已取消, 请按正确侧 spinner.",
                    opDesc, lapDesc, expectedAtLeft ? "左端" : "右端",
                    input.IsLeft ? "左" : "右", expectedAtLeft ? "左" : "右");
                r.LogMessage = string.Format("❌ 按错 spinner (isLeft={0}, 期望={1}, oldCur={2}, newCur={3}, start={4})",
                    input.IsLeft, expectedAtLeft, oldCur, newCur, input.StartPosition);
                return r;
            }

            // 防线 4
            if (input.RaceIsRacing && oldCur > 0 && newCur == 0 && !input.ConfirmAfterRacingZero)
            {
                r.Verdict = LapAdjustVerdict.Defense4_NeedConfirm;
                r.ErrorTitle = "⚠ 防线 4: 比赛中圈数清零";
                r.ErrorMessage = string.Format("比赛进行中, 操作将让圈数从 {0} 减到 0 (= 回到起跑前)\n确认这是有意的误触回退吗?", oldCur);
                r.LogMessage = string.Format("⚠ 比赛中圈数清零操作待确认 ({0}→0)", oldCur);
                return r;
            }

            // === 通过所有防线 ===
            r.Verdict = LapAdjustVerdict.Ok;
            r.NewCurrentLap = newCur;
            r.NewDispThis = newDispThis;
            r.ActualDelta = actualDelta;
            r.OpenAction = (actualDelta < 0) ? (byte)1 : (byte)2;
            // userSide/otherSide
            string userSide = input.IsLeft ? "left" : "right";
            string otherSide = input.IsLeft ? "right" : "left";
            r.OpenTpMbSide = (actualDelta < 0) ? otherSide : userSide;
            r.CloseTpMbSide = (actualDelta < 0) ? userSide : otherSide;
            r.ShouldDeleteLastSplit = (actualDelta > 0);

            // SB 处理 (2026-05-30 v5 final, 加奇数 lapsPerLeg 边界处理):
            //   游泳规则: SB 物理只在 startPosition (起跑端=终点=发令点), 远端无 SB
            //   状态: 接力 + 下一段是棒次交接 + 交接端是 startPosition → SB 开 startPosition
            //         其他 (含交接在远端 / newCur=0 / 完赛 / 非交接前) → SB 关
            //   交接端判定: nextLap 触板端 = nextLap 偶数次 ? startPosition : 远端
            //   适用性:
            //     4×100m 池 50m lapsPerLeg=2 (偶): 棒次交接 lap=2/4/6 都偶, 都在 startPosition ✓
            //     4×200m 池 50m lapsPerLeg=4 (偶): 棒次交接 lap=4/8/12 都偶 ✓
            //     4×50m 池 50m  lapsPerLeg=1 (奇): 棒次交接 lap=1/2/3 交替奇偶, 只有 lap=2 在 startPosition ✓
            //   反复修订记录见 [[project-sb-formula-relay]]
            if (input.IsRelay && input.LapsPerLeg > 0)
            {
                int nextLap = newCur + 1;
                bool isNextExchange = (nextLap < input.TotalLaps) && (nextLap % input.LapsPerLeg == 0);
                bool exchangeAtStartSide = (nextLap % 2 == 0);   // 偶数次触板在 startPosition
                if (isNextExchange && exchangeAtStartSide)
                {
                    bool sbStartFromLeft = (input.StartPosition != "right");
                    r.SbOpenSide = sbStartFromLeft ? (byte)1 : (byte)2;
                    r.ShouldOpenSbLeft = (r.SbOpenSide == 1);
                    r.ShouldOpenSbRight = (r.SbOpenSide == 2);
                }
                else
                {
                    r.SbOpenSide = 0;
                }
            }
            else
            {
                r.SbOpenSide = 0;
            }

            // 2026-05-30 方向公式 (revert 到 open_side 派生, 跟物理游动方向一致):
            //   r.Direction = OpenTpMbSide 朝向 (= 运动员朝下次触板端游)
            //   ▼ 漏触补救: open_side = otherSide → Direction 朝对端 (运动员折返朝远侧)
            //   ▲ 误触回退: open_side = userSide → Direction 朝用户按的端 (撤销后, 朝 newCur 触板端再游)
            //   注: 跟硬件 ">>>/<<<" 历史代码 (朝 close_side) 反向 — 这是 PC web 端 sw.direction 物理意义, 不动硬件箭头
            r.Direction = (r.OpenTpMbSide == "right") ? "→" : "←";

            r.LogMessage = string.Format("✓ {0}侧 {1}→{2}, CurrentLap {3}→{4}, TP+MB 开{5}/关{6}, SB={7}, 方向 {8} (openAction={9})",
                input.IsLeft ? "左" : "右", curDispThis, newDispThis,
                oldCur, newCur,
                r.OpenTpMbSide == "left" ? "左" : "右",
                r.CloseTpMbSide == "left" ? "左" : "右",
                r.SbOpenSide == 0 ? "都关" : r.SbOpenSide == 1 ? "开左" : "开右",
                r.Direction,
                r.OpenAction);
            return r;
        }
    }
}
