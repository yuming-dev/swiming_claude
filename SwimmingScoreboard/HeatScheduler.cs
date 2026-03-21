using System;
using System.Collections.Generic;
using System.Linq;

namespace SwimmingScoreboard
{
    // ═══════════════════════════════════════════════════════════════
    // 分组编排算法（遵循FINA/中国游泳协会规则）
    //
    // 核心规则：
    // 1. 按报名成绩蛇形分组，确保各组实力均衡
    // 2. 最快的运动员在最后一组（决赛组），最慢的在第一组
    // 3. 每组内按成绩分配泳道：最快→第4道，次快→第5道，交替向外
    // 4. 每组不少于3人
    // 5. 比赛顺序：慢组先游，快组后游
    // ═══════════════════════════════════════════════════════════════
    public static class HeatScheduler
    {
        // 泳道分配优先级（最快→最慢，中间道最快）
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
            int[] priority = new int[pool.LaneCount];
            List<int> lanes = new List<int>(pool.LaneNumbers);
            int mid = lanes.Count / 2;
            int left = mid - 1, right = mid;
            int idx = 0;
            if (lanes.Count % 2 == 0) {
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
        /// FINA蛇形分组算法
        /// 按报名成绩排序后，蛇形分配到各组，确保各组实力均衡
        ///
        /// 例：16人分2组（8道），按成绩排名1-16
        /// 第2组（快组，后游）：1, 4, 5, 8, 9, 12, 13, 16
        /// 第1组（慢组，先游）：2, 3, 6, 7, 10, 11, 14, 15
        ///
        /// 蛇形顺序：组2→组1→组1→组2→组2→组1→...
        /// </summary>
        public static List<HeatAssignment> GenerateHeats(List<Swimmer> swimmers, PoolConfig pool, string eventName, string stage) {
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
                int cmp = ta.CompareTo(tb);
                if (cmp != 0) return cmp;
                // 成绩相同按姓名排序
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            int laneCount = pool.LaneCount;
            int totalSwimmers = eligible.Count;
            if (totalSwimmers == 0) return new List<HeatAssignment>();

            // 计算组数
            int heatCount = (int)Math.Ceiling((double)totalSwimmers / laneCount);

            // 确保每组不少于3人（FINA规则）
            while (heatCount > 1) {
                // 计算第一组（最慢组）的人数
                int firstHeatSize = totalSwimmers - (heatCount - 1) * laneCount;
                if (firstHeatSize < 0) firstHeatSize = totalSwimmers % laneCount;
                // 用蛇形分配时各组人数会更均匀，但仍需保证最少组不少于3人
                int minPerGroup = totalSwimmers / heatCount;
                if (minPerGroup >= 3) break;
                heatCount--;
            }

            // 蛇形分组：按成绩排名依次分配
            // 排名第1→最后一组，第2→倒数第二组...到第一组后反向回来
            List<List<Swimmer>> heats = new List<List<Swimmer>>();
            for (int h = 0; h < heatCount; h++) heats.Add(new List<Swimmer>());

            bool forward = false; // false=从最后一组开始往前
            int currentHeat = heatCount - 1; // 从最后一组开始

            for (int i = 0; i < eligible.Count; i++) {
                heats[currentHeat].Add(eligible[i]);

                // 蛇形方向切换
                if (forward) {
                    currentHeat++;
                    if (currentHeat >= heatCount) {
                        currentHeat = heatCount - 1;
                        forward = false;
                    }
                } else {
                    currentHeat--;
                    if (currentHeat < 0) {
                        currentHeat = 0;
                        forward = true;
                    }
                }
            }

            // 每组内按报名成绩排序（快→慢），用于泳道分配
            foreach (var heat in heats) {
                heat.Sort((a, b) => {
                    double ta = a.EntryTimeSeconds > 0 ? a.EntryTimeSeconds : double.MaxValue;
                    double tb = b.EntryTimeSeconds > 0 ? b.EntryTimeSeconds : double.MaxValue;
                    return ta.CompareTo(tb);
                });
            }

            // 分配泳道（最快→中间道，交替向外）
            int[] lanePriority = GetLanePriority(pool);
            var assignments = new List<HeatAssignment>();

            for (int h = 0; h < heats.Count; h++) {
                var heat = heats[h];
                for (int s = 0; s < heat.Count; s++) {
                    int lane = s < lanePriority.Length ? lanePriority[s] : pool.LaneNumbers[s];
                    assignments.Add(new HeatAssignment {
                        Swimmer = heat[s],
                        Heat = h + 1,  // 第1组=最慢组先游
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
        /// 接力队蛇形分组（同个人项目规则）
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
                int minPerGroup = totalTeams / heatCount;
                if (minPerGroup >= 3) break;
                heatCount--;
            }

            // 蛇形分组
            List<List<RelayTeam>> heats = new List<List<RelayTeam>>();
            for (int h = 0; h < heatCount; h++) heats.Add(new List<RelayTeam>());

            bool forward = false;
            int currentHeat = heatCount - 1;
            for (int i = 0; i < eligible.Count; i++) {
                heats[currentHeat].Add(eligible[i]);
                if (forward) {
                    currentHeat++;
                    if (currentHeat >= heatCount) { currentHeat = heatCount - 1; forward = false; }
                } else {
                    currentHeat--;
                    if (currentHeat < 0) { currentHeat = 0; forward = true; }
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
        /// 确定赛事阶段（按参赛人数，FINA标准）
        /// >=25人：预赛→复赛→半决赛→决赛
        /// 17-24人：预赛→半决赛→决赛
        /// 9-16人：预赛→决赛
        /// <=8人：直接决赛
        /// </summary>
        public static List<string> GetStages(int participantCount) {
            if (participantCount >= 25) return new List<string> { "预赛", "复赛", "半决赛", "决赛" };
            if (participantCount >= 17) return new List<string> { "预赛", "半决赛", "决赛" };
            if (participantCount >= 9) return new List<string> { "预赛", "决赛" };
            return new List<string> { "决赛" };
        }

        /// <summary>
        /// 各阶段晋级人数
        /// 预赛→半决赛/复赛：取所有小组成绩排名前16名
        /// 半决赛→决赛：取所有半决赛成绩前8名
        /// </summary>
        public static int GetPromotionCount(string fromStage, string toStage) {
            if (toStage == "决赛") return 8;
            if (toStage == "半决赛") return 16;
            if (toStage == "复赛") return 16;
            return 8;
        }

        /// <summary>
        /// 根据比赛成绩生成晋级名单
        /// 按所有小组的成绩统一排名，取前count名
        /// 成绩相同者比较触壁反应时间（时间短者晋级）
        /// </summary>
        public static List<Swimmer> GetPromotedSwimmers(List<Swimmer> allSwimmers, string eventName, string fromStage, int count) {
            // 筛选有该阶段成绩的运动员（不检查CurrentStage，因为调用者可能已预先过滤）
            var results = allSwimmers.Where(s =>
                s.EventName == eventName &&
                s.Status != "DNS" && s.Status != "DNF" && s.Status != "DSQ" &&
                s.GetResultForStage(fromStage) != null
            ).ToList();

            // 如果按成绩没找到，回退到按CurrentStage匹配
            if (results.Count == 0) {
                results = allSwimmers.Where(s =>
                    s.EventName == eventName &&
                    s.CurrentStage == fromStage &&
                    s.Status != "DNS" && s.Status != "DNF" && s.Status != "DSQ"
                ).ToList();
            }

            // 按最终成绩排序（所有小组统一排名）
            results.Sort((a, b) => {
                var ra = a.GetResultForStage(fromStage);
                var rb = b.GetResultForStage(fromStage);
                double ta = ra != null && ra.FinalTime > 0 ? ra.FinalTime : double.MaxValue;
                double tb = rb != null && rb.FinalTime > 0 ? rb.FinalTime : double.MaxValue;
                int cmp = ta.CompareTo(tb);
                if (cmp != 0) return cmp;
                // 成绩相同，比较反应时间（短者晋级）
                double reactA = ra != null ? ra.StartingBlockTime : double.MaxValue;
                double reactB = rb != null ? rb.StartingBlockTime : double.MaxValue;
                return reactA.CompareTo(reactB);
            });

            return results.Take(count).ToList();
        }

        /// <summary>
        /// 晋级后重新蛇形分组（半决赛/决赛分组）
        /// 按上一轮成绩蛇形排列，确保各组实力均衡
        /// </summary>
        public static List<HeatAssignment> GenerateHeatsFromResults(List<Swimmer> promoted, PoolConfig pool, string eventName, string toStage, string fromStage) {
            // 按上一轮成绩排序
            promoted.Sort((a, b) => {
                var ra = a.GetResultForStage(fromStage);
                var rb = b.GetResultForStage(fromStage);
                double ta = ra != null && ra.FinalTime > 0 ? ra.FinalTime : double.MaxValue;
                double tb = rb != null && rb.FinalTime > 0 ? rb.FinalTime : double.MaxValue;
                return ta.CompareTo(tb);
            });

            // 更新阶段和报名成绩（用上一轮成绩作为本轮排序依据）
            foreach (var sw in promoted) {
                sw.CurrentStage = toStage;
                var result = sw.GetResultForStage(fromStage);
                if (result != null && result.FinalTime > 0) {
                    sw.EntryTimeSeconds = result.FinalTime;
                    sw.EntryTime = TimeFormatter.Format(result.FinalTime);
                }
                sw.Heat = 0;
                sw.Lane = 0;
            }

            // 使用蛇形分组
            return GenerateHeats(promoted, pool, eventName, toStage);
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
