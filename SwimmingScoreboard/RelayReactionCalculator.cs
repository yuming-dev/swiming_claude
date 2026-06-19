using System;
using System.Collections.Generic;
using System.Linq;

namespace SwimmingScoreboard
{
    // 2026-06-03 接力 SB Reaction Time 计算器 — 14 条规则
    //
    // 规则总览 (= 用户 2026-06-03 确认):
    //   每 (lane, side) 一个独立 window. 第一个事件到达起 N 秒倒计时 (= ReactionEventWindowSec, 默认 3s).
    //   窗口内收集 TP/SB/MB(第 1 块按下) /手动 TP, 窗口结束时算 reaction.
    //
    // 基准时间优先级 (= 用哪个作 reaction 的减数):
    //   TP (= 多次取第一次) > MB final > 手动 TP > 无 → "---"
    //
    // SB 时间: 多次取最后一次. 无 SB → "---"
    //
    // reaction = LastSbSwim - basis
    //   > 0 = 正常反应时
    //   < 0 = 抢跳
    //
    // 边界:
    //   - "MB 来" 指 MB 第 1 块按下时刻 (= 起 3s 倒计时用)
    //   - reaction 用 MB final 算出时刻 (= cmd 0x16 d3=Pushbutton_Result, MBdelay 后)
    //   - 3s 窗口到了, 但 MB 第 1 块已按 final 没出 → 继续等 MB final cmd (= 兜底 10s 超时算"无 MB final")
    //   - PC 端 14 条算法独立于硬件 StartPosition 配置, 完全由 SB cmd 自带 frame[4] side 驱动
    public class RelayReactionCalculator
    {
        public enum EventKind
        {
            TP,             // 0x16 d3=Touchpad_Result 触板
            SB,             // 0x1A 出发台
            MB_FirstPress,  // 0x17/0x18/0x19 盲表 1/2/3 任一第 1 次按下 (= 起 3s 倒计时基准)
            MB_Final,       // 0x16 d3=Pushbutton_Result 触代 (= MB final 算出)
            ManualTP        // PC 端手动 T 按钮触发
        }

        public class Window
        {
            public int Lane;
            public string Side;             // "left" or "right"
            public double WindowStartSwim;  // 第一个事件到达 swim_now
            public double FirstTpSwim = -1;
            public double LastSbSwim = -1;
            public double MbFirstPressSwim = -1;
            public double MbFinalSwim = -1;
            public double HandTpSwim = -1;
            public bool Emitted;            // 已发出结果, window 不再处理
        }

        // 兜底超时 (= 窗口起 N 秒 + 10s 还没等到 MB final → 强制 emit)
        private const double EmitTimeoutSec = 10.0;

        private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>();
        // 2026-06-18 抢跳场景: SB 先于 TP 到达, 不立即创建 window (会被 windowSec 窗口期把 TP 排除掉),
        //   先暂存到 _pendingSbs 等基准事件 (TP/MB/手动 TP) 到达再创建 window 并把 SB 加入.
        private readonly Dictionary<string, double> _pendingSbs = new Dictionary<string, double>();
        private readonly Func<double> _windowSecGetter;
        private readonly Action<int, string, double, string> _onReaction;
        private readonly Action<int, string> _onNoReaction;

        public RelayReactionCalculator(
            Func<double> windowSecGetter,
            Action<int, string, double, string> onReaction,
            Action<int, string> onNoReaction)
        {
            _windowSecGetter = windowSecGetter;
            _onReaction = onReaction;
            _onNoReaction = onNoReaction;
        }

        public void OnEvent(int lane, string side, EventKind kind, double swimTime)
        {
            if (string.IsNullOrEmpty(side)) return;
            string key = lane + "_" + side;
            // 2026-06-18 抢跳修复: 窗口只由"基准事件"(TP/MB/MB_Final/ManualTP) 触发创建.
            //   SB 单独到时若无窗口 → 暂存 _pendingSbs[key] = swimTime, 等基准事件创建窗口时再取出加入.
            bool isBasisKind = kind == EventKind.TP
                            || kind == EventKind.MB_FirstPress
                            || kind == EventKind.MB_Final
                            || kind == EventKind.ManualTP;
            Window w;
            bool hasWindow = _windows.TryGetValue(key, out w) && !w.Emitted;
            if (!hasWindow)
            {
                if (!isBasisKind)
                {
                    // 抢跳 SB: 暂存等基准事件
                    if (kind == EventKind.SB) _pendingSbs[key] = swimTime;
                    return;
                }
                // 基准事件: 创建新窗口
                w = new Window { Lane = lane, Side = side, WindowStartSwim = swimTime };
                _windows[key] = w;
                // 如有挂起 SB, 加入窗口 (= 抢跳场景的 SB 时刻)
                double pendingSb;
                if (_pendingSbs.TryGetValue(key, out pendingSb))
                {
                    w.LastSbSwim = pendingSb;
                    _pendingSbs.Remove(key);
                }
            }
            switch (kind)
            {
                case EventKind.TP:
                    if (w.FirstTpSwim < 0) w.FirstTpSwim = swimTime;
                    break;
                case EventKind.SB:
                    w.LastSbSwim = swimTime;
                    break;
                case EventKind.MB_FirstPress:
                    if (w.MbFirstPressSwim < 0) w.MbFirstPressSwim = swimTime;
                    break;
                case EventKind.MB_Final:
                    w.MbFinalSwim = swimTime;
                    break;
                case EventKind.ManualTP:
                    if (w.HandTpSwim < 0) w.HandTpSwim = swimTime;
                    break;
            }
            TryEmit(w, swimTime);
        }

        // 100ms timer 调用, 检查所有 window 超时
        public void Tick(double swimNow)
        {
            foreach (var w in _windows.Values.ToList())
            {
                if (!w.Emitted) TryEmit(w, swimNow);
            }
        }

        // 比赛 reset 时清理
        public void Reset()
        {
            _windows.Clear();
            _pendingSbs.Clear();   // 2026-06-18 抢跳挂起 SB 也清
        }

        private void TryEmit(Window w, double swimNow)
        {
            if (w.Emitted) return;
            double windowSec = _windowSecGetter();
            double elapsed = swimNow - w.WindowStartSwim;

            // 兜底超时: 窗口起 (windowSec + 10s) 后强制 emit
            bool hardTimeout = elapsed >= windowSec + EmitTimeoutSec;

            if (elapsed < windowSec && !hardTimeout)
            {
                // 窗口内, 继续等
                return;
            }

            // 窗口到了 (= 至少 windowSec 已过)
            // 选基准: TP > MB final > 手动 TP
            double basis = -1;
            string basisKind = "";
            if (w.FirstTpSwim > 0) { basis = w.FirstTpSwim; basisKind = "TP"; }
            else if (w.MbFinalSwim > 0) { basis = w.MbFinalSwim; basisKind = "MB"; }
            else if (w.HandTpSwim > 0) { basis = w.HandTpSwim; basisKind = "HandTP"; }

            if (basis < 0 && w.MbFirstPressSwim > 0 && w.MbFinalSwim < 0 && !hardTimeout)
            {
                // MB 第 1 块按了, final 还没出 → 继续等 MB final cmd (= MBdelay 4s 后会发)
                return;
            }

            if (w.LastSbSwim > 0 && basis > 0)
            {
                double reaction = w.LastSbSwim - basis;
                _onReaction(w.Lane, w.Side, reaction, basisKind);
            }
            else
            {
                // 没 SB OR 没基准 → "---"
                _onNoReaction(w.Lane, w.Side);
            }
            w.Emitted = true;
        }
    }
}
