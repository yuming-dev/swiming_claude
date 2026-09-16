using System;
using System.Collections.Generic;
using System.Linq;

namespace SwimmingScoreboard
{
    /// <summary>
    /// 2026-08-30 全场【唯一一份】名次与排序规则。
    ///
    /// 为什么要有这个文件:
    ///   这套规则原来在程序里散着好几份 —— 并列名次的"idx/rank/prevTime 三件套"
    ///   有 5 处各写各的, 本组结果排序在 C# 里有 3 处、display.html 里还用 JS 复刻了一份。
    ///   结果就是【改了这处, 别处没改】: 打印成绩单时 DNF 被排到并列第一之前, 就是因为
    ///   排序读 sw.CurrentRank、显示读 r.Rank, 两处口径不一样。
    ///
    ///   所以: 名次怎么算、谁排前面, 只允许在这个文件里写一次。
    ///   其它地方一律调这里, 不许再自己写一遍。
    ///
    /// 规则本身:
    ///   ① 并列: 成绩按硬件精度(1/100 秒)比, 相等即并列, 名次形如 1,1,3,4
    ///   ② 本组顺序: 有名次的按名次 → 有成绩但还没名次的 → TRI → DSQ/DQ → DNF → DNS → 无成绩
    ///   ③ 名次来源: 一律以【成绩行 r.Rank】为准, sw.CurrentRank 只是兜底
    /// </summary>
    public static class ResultOrdering
    {
        // ── ① 并列判定 ────────────────────────────────────────────────
        /// <summary>
        /// 两个成绩算不算并列。按硬件精度 1/100 秒取整后比较, 避免浮点尾差。
        /// AwayFromZero 与裁判惯例一致: 54.685 → 54.69 而不是 54.68。
        /// </summary>
        public static bool IsTie(double a, double b)
        {
            long ah = (long)Math.Round(a * 100.0, MidpointRounding.AwayFromZero);
            long bh = (long)Math.Round(b * 100.0, MidpointRounding.AwayFromZero);
            return ah == bh;
        }

        // ── ② 状态先后 ────────────────────────────────────────────────
        /// <summary>
        /// 无名次者之间的先后。数字小的排前面。
        /// 注意"正常"是 5(最靠后) —— 指的是【无成绩也无状态】的人(没游/没记上)。
        /// 有成绩的正常人在 OrderForHeat 里会被提到最前, 别拿这个函数单独判。
        /// </summary>
        public static int StatusOrder(string status)
        {
            if (status == "TRI") return 1;
            if (status == "DSQ" || status == "DQ") return 2;
            if (status == "DNF") return 3;
            if (status == "DNS") return 4;
            return 5;
        }

        /// <summary>判罚 / 弃权 —— 这些人不显示成绩、不参与排名和成绩差。</summary>
        public static bool IsJudged(string status)
        {
            return status == "DSQ" || status == "DQ" || status == "DNF" || status == "DNS";
        }

        // ── ③ 名次取值 ────────────────────────────────────────────────
        /// <summary>
        /// 名次一律以【成绩行】的 Rank 为准。
        /// currentRank(运动员对象上那个) 只是兜底: 它每人只有一个值, 是"最近一次算名次"
        /// 留下的, 而且计时端回推 / 文件导入进来的成绩根本不会设它 —— 谁拿它当准,
        /// 谁就把有名次的人当成没名次的。
        /// </summary>
        public static int RankOf(int resultRank, int currentRank)
        {
            if (resultRank > 0) return resultRank;
            return currentRank > 0 ? currentRank : 0;
        }

        // ── ④ 并列名次计算 (全场唯一一份) ─────────────────────────────
        /// <summary>
        /// 给【已按成绩升序排好】的列表算并列名次, 返回与输入等长的名次数组。
        /// 形如 1,1,3,4 —— 并列占位, 下一名跳号。
        /// </summary>
        public static List<int> ComputeRanks<T>(IList<T> orderedByTime, Func<T, double> getTime)
        {
            var ranks = new List<int>();
            if (orderedByTime == null) return ranks;
            int rank = 1;
            double prev = -1;
            for (int i = 0; i < orderedByTime.Count; i++)
            {
                double cur = getTime(orderedByTime[i]);
                if (i == 0 || !IsTie(cur, prev)) rank = i + 1;
                ranks.Add(rank);
                prev = cur;
            }
            return ranks;
        }

        // ── ⑤ 本组显示顺序 (全场唯一一份) ─────────────────────────────
        /// <summary>
        /// 单组结果的显示顺序。取值器由调用方提供, 所以 Swimmer / 大屏数据行 /
        /// 打印行都能用同一份规则, 不用各排各的。
        ///
        /// 顺序: 有名次的按名次 → 有成绩没名次的 → TRI → DSQ → DNF → DNS → 无成绩
        /// 同档内: 有成绩的按成绩快慢, 再按道次。
        /// </summary>
        public static List<T> OrderForHeat<T>(
            IEnumerable<T> items,
            Func<T, string> getStatus,
            Func<T, int> getRank,
            Func<T, double> getTime,
            Func<T, int> getLane)
        {
            if (items == null) return new List<T>();
            return items
                .OrderBy(x => {
                    int so = StatusOrder(getStatus(x));
                    int rk = getRank(x);
                    return (so == 5 && rk > 0) ? rk : int.MaxValue;
                })
                .ThenBy(x => {
                    int so = StatusOrder(getStatus(x));
                    // 有成绩但还没算出名次的正常人, 提到判罚/弃权之前。
                    // (原来"正常"是 5, 这种人会被排到 DNS 后面 —— 有成绩的不该排在弃权的后面)
                    if (so == 5 && getTime(x) > 0) return 0;
                    return so;
                })
                .ThenBy(x => { double t = getTime(x); return t > 0 ? t : double.MaxValue; })
                .ThenBy(x => getLane(x))
                .ToList();
        }

        // ── ⑥ 项目总排名组装 (全场唯一一份) ─────────────────────────────
        /// <summary>
        /// 2026-09-16 "项目总排名"(名次公告/项目成绩打印选【全部】/成绩与排名选【全部】)
        /// 的组装规则, 跟 ④ OrderForHeat 是同一层次的搭档 —— 那个管"单组结果", 这个管
        /// "跨组的项目总排名"。
        ///
        /// 为什么要单独抽出这一份: 这几处各写了一份几乎一样又不完全一样的排序/判空逻辑,
        /// 这一季出的好几个 bug(判罚排到有名次的人前面/中间、判罚整行从名单消失、判罚
        /// 显示一个假名次)根子都是"同一条边界情况在其中一处漏掉了, 另一处没漏"——改一处
        /// 不代表另一处也改了。现在只写这一份, 调用方以后只用把数据接进来, 不用再自己
        /// 挑排序规则。
        ///
        /// 规则: TRI 不参与(调用方按自己的口径先筛掉, 这里不重复判——TRI 的具体取值
        /// 每处未必一致); DSQ/DNF/DNS 排在最后, 名次一律给 0 —— 不管 getDbRank 传进来的
        /// 值是不是干净的(库/内存缓存哪一层没刷新干净都不怕, 状态本身说了算); 正常人
        /// 按 getDbRank 给的库定稿名次排, 同一个数字算并列。
        /// </summary>
        public static List<T> RankForTotalView<T>(
            IEnumerable<T> items,
            Func<T, string> getStatus,
            Func<T, int> getDbRank,
            Func<T, double> getTime,
            out List<int> ranks)
        {
            var ordered = (items ?? new List<T>())
                .OrderBy(x => IsJudged(getStatus(x)) ? int.MaxValue : (getDbRank(x) > 0 ? getDbRank(x) : int.MaxValue))
                .ThenBy(x => StatusOrder(getStatus(x)))
                .ThenBy(x => { double t = getTime(x); return t > 0 ? t : double.MaxValue; })
                .ToList();
            ranks = new List<int>();
            foreach (var x in ordered)
                ranks.Add(IsJudged(getStatus(x)) ? 0 : getDbRank(x));
            return ordered;
        }
    }
}
