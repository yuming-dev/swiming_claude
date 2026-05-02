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
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            int laneCount = pool.LaneCount;
            if (eligible.Count == 0) return new List<HeatAssignment>();

            int[] lanePriority = GetLanePriority(pool);
            var heats = AssignFinaSeeding(eligible.Cast<object>().ToList(), laneCount, lanePriority,
                                          isShortDistance: IsShortDistanceEvent(eventName));

            var assignments = new List<HeatAssignment>();
            for (int h = 0; h < heats.Count; h++) {
                foreach (var slot in heats[h]) {
                    var sw = (Swimmer)slot.Item1;
                    int lane = slot.Item2;
                    assignments.Add(new HeatAssignment {
                        Swimmer = sw,
                        Heat = h + 1,   // 第1组=最慢组先游
                        Lane = lane,
                        EventName = eventName,
                        Stage = stage
                    });
                    sw.Heat = h + 1;
                    sw.Lane = lane;
                    sw.SetStageAssignment(stage, h + 1, lane, sw.EntryTimeSeconds, sw.EntryTime);
                }
            }
            return assignments;
        }

        /// <summary>
        /// 接力队分组（按 FINA SW 3.1.1.5：最后两组循环排位，前面组直接排位由慢到快）
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
            if (eligible.Count == 0) return new List<RelayHeatAssignment>();

            int[] lanePriority = GetLanePriority(pool);
            // 接力按"长距离"规则：最后2组循环排位
            var heats = AssignFinaSeeding(eligible.Cast<object>().ToList(), laneCount, lanePriority,
                                          isShortDistance: false);

            var assignments = new List<RelayHeatAssignment>();
            for (int h = 0; h < heats.Count; h++) {
                foreach (var slot in heats[h]) {
                    var team = (RelayTeam)slot.Item1;
                    int lane = slot.Item2;
                    assignments.Add(new RelayHeatAssignment {
                        Team = team,
                        Heat = h + 1,
                        Lane = lane,
                        EventName = eventName,
                        Stage = stage
                    });
                    team.Heat = h + 1;
                    team.Lane = lane;
                }
            }
            return assignments;
        }

        /// <summary>
        /// 按 FINA SW 3.1 规则把已排序（快→慢）的参赛者分配到组次和泳道。
        /// 返回 heats[heatIndex0]: List of (entry, lane).
        ///
        /// 规则：
        ///  - heatCount = ceil(N / laneCount)
        ///  - 短距离(50/100/200米)：seedHeats = min(heatCount, 3)；其余项目（含中长距离/接力）：min(heatCount, 2)
        ///  - 后 seedHeats 组按"循环（种子）排位"分配前 seededN = seedHeats × laneCount 名（不足按实际人数）
        ///    第1名→最后组中道(优先级[0])，第2名→倒数第2组中道，… 第seedHeats名→倒数第seedHeats组中道，
        ///    第seedHeats+1名→最后组优先级[1]道，依次填满。
        ///  - 前面 (heatCount - seedHeats) 个"直接排位"组由慢到快：
        ///    第1组放最慢若干人（不满则中央泳道用满，最外侧空着）；后续组每组 laneCount 人，
        ///    组内按报名成绩快→慢用 lanePriority 分道。
        /// </summary>
        private static List<List<Tuple<object, int>>> AssignFinaSeeding(
                List<object> sortedFastFirst, int laneCount, int[] lanePriority, bool isShortDistance) {
            int total = sortedFastFirst.Count;
            int heatCount = (int)Math.Ceiling((double)total / laneCount);
            if (heatCount < 1) heatCount = 1;

            int seedHeats = Math.Min(heatCount, isShortDistance ? 3 : 2);
            int directHeats = heatCount - seedHeats;
            int seededN = Math.Min(total, seedHeats * laneCount);

            var heats = new List<List<Tuple<object, int>>>();
            for (int h = 0; h < heatCount; h++) heats.Add(new List<Tuple<object, int>>());

            // ── 后 seedHeats 组：循环（种子）排位 ──
            // 种子顺序索引 i (0=最快): 中道优先级 lanePos = i / seedHeats，组内偏移 within = i % seedHeats
            // i % seedHeats == 0 ⇒ 最后一组；==1 ⇒ 倒数第2组；……
            for (int i = 0; i < seededN; i++) {
                int lanePos = i / seedHeats;
                int within = i % seedHeats;
                int heatIndex = heatCount - 1 - within;
                int lane = lanePos < lanePriority.Length ? lanePriority[lanePos] : lanePriority[lanePriority.Length - 1];
                heats[heatIndex].Add(Tuple.Create(sortedFastFirst[i], lane));
            }

            // ── 前 directHeats 组：直接排位 ──
            // remaining 是种子之外剩余的报名（仍按快→慢排序）。
            // 安排次序：把"快的"放进最靠后的直接组（heatIndex = directHeats-1）填满 laneCount，依次往前；
            // 第1组（最慢组）拿剩下不满的部分（最外侧泳道空着）。
            int idx = seededN;
            for (int h = directHeats; h >= 1; h--) {
                int remaining = total - idx;
                if (remaining <= 0) break;
                bool isFirstHeat = (h == 1);
                int heatSize = isFirstHeat ? remaining : Math.Min(laneCount, remaining);
                // 取出 heatSize 名（仍按快→慢顺序）
                var thisHeat = sortedFastFirst.GetRange(idx, heatSize);
                idx += heatSize;
                for (int j = 0; j < thisHeat.Count; j++) {
                    int lane = j < lanePriority.Length ? lanePriority[j] : lanePriority[lanePriority.Length - 1];
                    heats[h - 1].Add(Tuple.Create(thisHeat[j], lane));
                }
            }

            return heats;
        }

        /// <summary>
        /// 确定赛事阶段（按国际游泳竞赛规则）
        /// 短距离个人项目（50m/100m/200m，含各泳姿）：
        ///   ≥17人 → 预赛→半决赛→决赛（三步制）
        ///   9-16人 → 预赛→决赛（两步制，不设半决赛，直接取前8名）
        ///   ≤8人  → 直接决赛
        /// 中长距离/接力（400m/800m/1500m/接力）：
        ///   ≥9人  → 预赛→决赛（两步制，无半决赛）
        ///   ≤8人  → 直接决赛
        /// </summary>
        public static List<string> GetStages(int participantCount, string eventName = "") {
            bool isShortDistance = IsShortDistanceEvent(eventName);

            if (isShortDistance) {
                // 50米/100米/200米：≥17人必须设半决赛
                if (participantCount >= 17) return new List<string> { "预赛", "半决赛", "决赛" };
                if (participantCount > 8) return new List<string> { "预赛", "决赛" };
                return new List<string> { "决赛" };
            } else {
                // 400米及以上、接力：无半决赛，无论人数多少
                if (participantCount > 8) return new List<string> { "预赛", "决赛" };
                return new List<string> { "决赛" };
            }
        }

        /// <summary>
        /// 判断是否为短距离项目（50米/100米/200米，非接力）
        /// </summary>
        private static bool IsShortDistanceEvent(string eventName) {
            if (string.IsNullOrEmpty(eventName)) return false;
            if (eventName.Contains("接力")) return false;
            if (eventName.Contains("50米") || eventName.Contains("100米") || eventName.Contains("200米")) return true;
            return false;
        }

        /// <summary>
        /// 各阶段晋级人数
        /// 预赛→半决赛：取前16名
        /// 半决赛→决赛/预赛→决赛：取前8名
        /// </summary>
        public static int GetPromotionCount(string fromStage, string toStage) {
            if (toStage == "半决赛") return 16;
            if (toStage == "决赛") return 8;
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
        /// 晋级后分组（半决赛/决赛）
        /// 半决赛：16人分2组，按总排名交替分配（奇数名→第1组，偶数名→第2组，最好成绩在第2组）
        /// 决赛：8人1组，按成绩排泳道
        /// 预赛→决赛（无半决赛时）：8人1组，按成绩排泳道
        /// </summary>
        public static List<HeatAssignment> GenerateHeatsFromResults(List<Swimmer> promoted, PoolConfig pool, string eventName, string toStage, string fromStage) {
            // 按上一轮成绩排序（总排名，不分小组）
            promoted.Sort((a, b) => {
                var ra = a.GetResultForStage(fromStage);
                var rb = b.GetResultForStage(fromStage);
                double ta = ra != null && ra.FinalTime > 0 ? ra.FinalTime : double.MaxValue;
                double tb = rb != null && rb.FinalTime > 0 ? rb.FinalTime : double.MaxValue;
                return ta.CompareTo(tb);
            });

            // 更新阶段和成绩（用上一轮成绩作为本轮排序依据）
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

            int laneCount = pool.LaneCount;
            int[] lanePriority = GetLanePriority(pool);

            // 决赛或人数≤泳道数：1组，按成绩排泳道
            if (toStage == "决赛" || promoted.Count <= laneCount) {
                var assignments = new List<HeatAssignment>();
                for (int i = 0; i < promoted.Count; i++) {
                    int lane = i < lanePriority.Length ? lanePriority[i] : pool.LaneNumbers[i];
                    assignments.Add(new HeatAssignment {
                        Swimmer = promoted[i], Heat = 1, Lane = lane,
                        EventName = eventName, Stage = toStage
                    });
                    promoted[i].Heat = 1;
                    promoted[i].Lane = lane;
                    promoted[i].SetStageAssignment(toStage, 1, lane, promoted[i].EntryTimeSeconds, promoted[i].EntryTime);
                }
                return assignments;
            }

            // 半决赛：交替分组（奇数名次→第1组，偶数名次→第2组，成绩最好者在第2组）
            // 第1名→第2组，第2名→第1组，第3名→第2组，第4名→第1组...
            int heatCount = (int)Math.Ceiling((double)promoted.Count / laneCount);
            if (heatCount < 2) heatCount = 2; // 半决赛至少2组
            List<List<Swimmer>> heats = new List<List<Swimmer>>();
            for (int h = 0; h < heatCount; h++) heats.Add(new List<Swimmer>());

            for (int i = 0; i < promoted.Count; i++) {
                // 交替分配：排名1,3,5...→最后一组（快组），排名2,4,6...→第一组
                int targetHeat = (i % 2 == 0) ? (heatCount - 1) : 0;
                // 超过2组时用蛇形
                if (heatCount > 2) {
                    int round = i / heatCount;
                    int pos = i % heatCount;
                    targetHeat = (round % 2 == 0) ? (heatCount - 1 - pos) : pos;
                }
                heats[targetHeat].Add(promoted[i]);
            }

            // 每组内按成绩排序后分配泳道
            var allAssignments = new List<HeatAssignment>();
            for (int h = 0; h < heats.Count; h++) {
                heats[h].Sort((a, b) => {
                    double ta2 = a.EntryTimeSeconds > 0 ? a.EntryTimeSeconds : double.MaxValue;
                    double tb2 = b.EntryTimeSeconds > 0 ? b.EntryTimeSeconds : double.MaxValue;
                    return ta2.CompareTo(tb2);
                });
                for (int s = 0; s < heats[h].Count; s++) {
                    int lane = s < lanePriority.Length ? lanePriority[s] : pool.LaneNumbers[s];
                    allAssignments.Add(new HeatAssignment {
                        Swimmer = heats[h][s], Heat = h + 1, Lane = lane,
                        EventName = eventName, Stage = toStage
                    });
                    heats[h][s].Heat = h + 1;
                    heats[h][s].Lane = lane;
                    heats[h][s].SetStageAssignment(toStage, h + 1, lane, heats[h][s].EntryTimeSeconds, heats[h][s].EntryTime);
                }
            }
            return allAssignments;
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
