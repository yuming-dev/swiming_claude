using System;
using System.Collections.Generic;
using System.Linq;

namespace SwimmingScoreboard
{
    // ═══════════════════════════════════════════════════════════════
    // 分组编排算法
    // ═══════════════════════════════════════════════════════════════
    public static class HeatScheduler
    {
        // 泳道分配优先级（最快→最慢）
        // 8泳道（道次1-8）：4, 5, 3, 6, 2, 7, 1, 8
        private static readonly int[] LanePriority8 = { 4, 5, 3, 6, 2, 7, 1, 8 };
        // 10泳道（道次0-9）：4, 5, 3, 6, 2, 7, 1, 8, 0, 9
        private static readonly int[] LanePriority10 = { 4, 5, 3, 6, 2, 7, 1, 8, 0, 9 };

        /// <summary>
        /// 获取泳道分配优先级数组
        /// </summary>
        public static int[] GetLanePriority(PoolConfig pool) {
            if (pool.LaneCount == 10) return LanePriority10;
            if (pool.LaneCount == 8) return LanePriority8;
            // 自定义泳道数：从中间向外分配
            int[] priority = new int[pool.LaneCount];
            List<int> lanes = new List<int>(pool.LaneNumbers);
            int mid = lanes.Count / 2;
            int left = mid - 1, right = mid;
            int idx = 0;
            // 先取中间偏右，再中间偏左，交替向外
            if (lanes.Count % 2 == 0) {
                // 偶数道
                priority[idx++] = lanes[mid - 1];
                priority[idx++] = lanes[mid];
                left = mid - 2; right = mid + 1;
            } else {
                priority[idx++] = lanes[mid];
                left = mid - 1; right = mid + 1;
            }
            while (idx < lanes.Count) {
                if (right < lanes.Count) priority[idx++] = lanes[right++];
                if (idx < lanes.Count && left >= 0) priority[idx++] = lanes[left--];
            }
            return priority;
        }

        /// <summary>
        /// 根据报名成绩生成分组和泳道分配
        /// 返回：每组中运动员列表，按比赛顺序排列（慢组先游）
        /// </summary>
        public static List<HeatAssignment> GenerateHeats(List<Swimmer> swimmers, PoolConfig pool, string eventName, string stage) {
            // 过滤当前项目、阶段的运动员
            var eligible = swimmers.Where(s =>
                s.EventName == eventName &&
                s.CurrentStage == stage &&
                s.IsQualified &&
                s.Status != "DNS" && s.Status != "DSQ"
            ).ToList();

            // 按报名成绩排序（快→慢，无成绩排最后）
            eligible.Sort((a, b) => {
                double ta = a.EntryTimeSeconds > 0 ? a.EntryTimeSeconds : double.MaxValue;
                double tb = b.EntryTimeSeconds > 0 ? b.EntryTimeSeconds : double.MaxValue;
                return ta.CompareTo(tb);
            });

            int laneCount = pool.LaneCount;
            int totalSwimmers = eligible.Count;

            if (totalSwimmers == 0) return new List<HeatAssignment>();

            // 计算组数
            int heatCount = (int)Math.Ceiling((double)totalSwimmers / laneCount);

            // 确保最后一组不少于3人
            while (heatCount > 1) {
                int lastHeatSize = totalSwimmers - (heatCount - 1) * laneCount;
                if (lastHeatSize >= 3) break;
                heatCount--;
            }

            // 按蛇形分配到各组（最后一组放最快的运动员）
            // 先分配慢组，最后分配快组
            List<List<Swimmer>> heats = new List<List<Swimmer>>();
            for (int h = 0; h < heatCount; h++) heats.Add(new List<Swimmer>());

            // 从最慢开始分配到第1组，最快分配到最后一组
            // 反转运动员列表（慢→快）
            var reversed = new List<Swimmer>(eligible);
            reversed.Reverse();

            int swimmerIdx = 0;
            for (int h = 0; h < heatCount && swimmerIdx < reversed.Count; h++) {
                int slotsInHeat = Math.Min(laneCount, reversed.Count - swimmerIdx);
                for (int s = 0; s < slotsInHeat && swimmerIdx < reversed.Count; s++) {
                    heats[h].Add(reversed[swimmerIdx++]);
                }
            }

            // 每组内按报名成绩排序（快→慢）
            foreach (var heat in heats) {
                heat.Sort((a, b) => {
                    double ta = a.EntryTimeSeconds > 0 ? a.EntryTimeSeconds : double.MaxValue;
                    double tb = b.EntryTimeSeconds > 0 ? b.EntryTimeSeconds : double.MaxValue;
                    return ta.CompareTo(tb);
                });
            }

            // 分配泳道
            int[] lanePriority = GetLanePriority(pool);
            var assignments = new List<HeatAssignment>();

            for (int h = 0; h < heats.Count; h++) {
                var heat = heats[h];
                for (int s = 0; s < heat.Count; s++) {
                    int lane = s < lanePriority.Length ? lanePriority[s] : pool.LaneNumbers[s];
                    var assignment = new HeatAssignment {
                        Swimmer = heat[s],
                        Heat = h + 1,
                        Lane = lane,
                        EventName = eventName,
                        Stage = stage
                    };
                    assignments.Add(assignment);

                    // 更新运动员数据
                    heat[s].Heat = h + 1;
                    heat[s].Lane = lane;
                }
            }

            return assignments;
        }

        /// <summary>
        /// 接力队分组编排
        /// </summary>
        public static List<RelayHeatAssignment> GenerateRelayHeats(List<RelayTeam> teams, PoolConfig pool, string eventName, string stage) {
            var eligible = teams.Where(t =>
                t.EventName == eventName &&
                t.Stage == stage &&
                t.Status != "DNS" && t.Status != "DSQ"
            ).ToList();

            eligible.Sort((a, b) => {
                double ta = a.EntryTimeSeconds > 0 ? a.EntryTimeSeconds : double.MaxValue;
                double tb = b.EntryTimeSeconds > 0 ? b.EntryTimeSeconds : double.MaxValue;
                return ta.CompareTo(tb);
            });

            int laneCount = pool.LaneCount;
            int totalTeams = eligible.Count;
            if (totalTeams == 0) return new List<RelayHeatAssignment>();

            int heatCount = (int)Math.Ceiling((double)totalTeams / laneCount);
            while (heatCount > 1) {
                int lastSize = totalTeams - (heatCount - 1) * laneCount;
                if (lastSize >= 3) break;
                heatCount--;
            }

            List<List<RelayTeam>> heats = new List<List<RelayTeam>>();
            for (int h = 0; h < heatCount; h++) heats.Add(new List<RelayTeam>());

            var reversed = new List<RelayTeam>(eligible);
            reversed.Reverse();

            int idx = 0;
            for (int h = 0; h < heatCount && idx < reversed.Count; h++) {
                int slots = Math.Min(laneCount, reversed.Count - idx);
                for (int s = 0; s < slots && idx < reversed.Count; s++) {
                    heats[h].Add(reversed[idx++]);
                }
            }

            foreach (var heat in heats) {
                heat.Sort((a, b) => {
                    double ta = a.EntryTimeSeconds > 0 ? a.EntryTimeSeconds : double.MaxValue;
                    double tb = b.EntryTimeSeconds > 0 ? b.EntryTimeSeconds : double.MaxValue;
                    return ta.CompareTo(tb);
                });
            }

            int[] lanePriority = GetLanePriority(pool);
            var assignments = new List<RelayHeatAssignment>();

            for (int h = 0; h < heats.Count; h++) {
                var heat = heats[h];
                for (int s = 0; s < heat.Count; s++) {
                    int lane = s < lanePriority.Length ? lanePriority[s] : pool.LaneNumbers[s];
                    assignments.Add(new RelayHeatAssignment {
                        Team = heat[s],
                        Heat = h + 1,
                        Lane = lane,
                        EventName = eventName,
                        Stage = stage
                    });
                    heat[s].Heat = h + 1;
                    heat[s].Lane = lane;
                }
            }

            return assignments;
        }

        /// <summary>
        /// 确定晋级规则（按参赛人数）
        /// </summary>
        public static List<string> GetStages(int participantCount) {
            if (participantCount >= 25) return new List<string> { "预赛", "复赛", "半决赛", "决赛" };
            if (participantCount >= 17) return new List<string> { "预赛", "半决赛", "决赛" };
            if (participantCount >= 9) return new List<string> { "预赛", "决赛" };
            return new List<string> { "决赛" };
        }

        /// <summary>
        /// 获取各阶段晋级人数
        /// </summary>
        public static int GetPromotionCount(string fromStage, string toStage) {
            // 预赛→复赛：16人，预赛→半决赛：16人，半决赛→决赛：8人，复赛→半决赛：16人
            if (toStage == "决赛") return 8;
            if (toStage == "半决赛") return 16;
            if (toStage == "复赛") return 16;
            return 8;
        }

        /// <summary>
        /// 根据成绩生成晋级名单
        /// </summary>
        public static List<Swimmer> GetPromotedSwimmers(List<Swimmer> allSwimmers, string eventName, string fromStage, int count) {
            var results = allSwimmers.Where(s =>
                s.EventName == eventName &&
                s.CurrentStage == fromStage &&
                s.Status != "DNS" && s.Status != "DNF" && s.Status != "DSQ"
            ).ToList();

            // 按最终成绩排序
            results.Sort((a, b) => {
                var ra = a.GetResultForStage(fromStage);
                var rb = b.GetResultForStage(fromStage);
                double ta = ra != null && ra.FinalTime > 0 ? ra.FinalTime : double.MaxValue;
                double tb = rb != null && rb.FinalTime > 0 ? rb.FinalTime : double.MaxValue;
                return ta.CompareTo(tb);
            });

            return results.Take(count).ToList();
        }
    }

    public class HeatAssignment
    {
        public Swimmer Swimmer { get; set; }
        public int Heat { get; set; }
        public int Lane { get; set; }
        public string EventName { get; set; }
        public string Stage { get; set; }
    }

    public class RelayHeatAssignment
    {
        public RelayTeam Team { get; set; }
        public int Heat { get; set; }
        public int Lane { get; set; }
        public string EventName { get; set; }
        public string Stage { get; set; }
    }
}
