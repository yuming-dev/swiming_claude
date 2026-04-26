using System.Collections.Generic;

namespace SwimmingScoreboard
{
    // 成绩册可编辑数据。所有字段为空时由系统自动生成默认内容。
    public class ResultBookData
    {
        public string CoverTitle { get; set; }       // 默认 "游泳比赛"
        public string CoverSubtitle { get; set; }    // 默认 "成 绩 册"
        public string Foreword { get; set; }         // 前言 / 序
        public string ClosingNote { get; set; }      // 尾页备注
        public string VenueImagePath { get; set; }   // 场地示意图

        // 各章节是否包含
        public bool IncludeMedalCount { get; set; }
        public bool IncludeSportsAwards { get; set; }
        public bool IncludeRecordStats { get; set; }
        public bool IncludeFinalRanking { get; set; }
        public bool IncludeFullResults { get; set; }
        public bool ShowSplitTimes { get; set; }
        public bool ShowTimeDifference { get; set; }

        // 体育道德风尚奖（一行一个；运动员/裁判员可写"姓名(队)"）
        public List<string> SportsTeams { get; set; }
        public List<string> SportsAthletes { get; set; }
        public List<string> SportsJudges { get; set; }

        // 名次公告中"教练员 / 联合培养单位"覆盖（按号码索引）
        public List<ResultBookSwimmerInfo> SwimmerInfos { get; set; }

        public ResultBookData() {
            CoverTitle = "游泳比赛";
            CoverSubtitle = "成 绩 册";
            Foreword = "";
            ClosingNote = "";
            VenueImagePath = "";
            IncludeMedalCount = true;
            IncludeSportsAwards = true;
            IncludeRecordStats = true;
            IncludeFinalRanking = true;
            IncludeFullResults = true;
            ShowSplitTimes = true;
            ShowTimeDifference = true;
            SportsTeams = new List<string>();
            SportsAthletes = new List<string>();
            SportsJudges = new List<string>();
            SwimmerInfos = new List<ResultBookSwimmerInfo>();
        }

        public ResultBookData Clone() {
            var c = new ResultBookData {
                CoverTitle = CoverTitle, CoverSubtitle = CoverSubtitle,
                Foreword = Foreword, ClosingNote = ClosingNote, VenueImagePath = VenueImagePath,
                IncludeMedalCount = IncludeMedalCount,
                IncludeSportsAwards = IncludeSportsAwards,
                IncludeRecordStats = IncludeRecordStats,
                IncludeFinalRanking = IncludeFinalRanking,
                IncludeFullResults = IncludeFullResults,
                ShowSplitTimes = ShowSplitTimes,
                ShowTimeDifference = ShowTimeDifference
            };
            c.SportsTeams.AddRange(SportsTeams);
            c.SportsAthletes.AddRange(SportsAthletes);
            c.SportsJudges.AddRange(SportsJudges);
            foreach (var s in SwimmerInfos)
                c.SwimmerInfos.Add(new ResultBookSwimmerInfo { BibNumber = s.BibNumber, Name = s.Name, Country = s.Country, Coach = s.Coach, JointTrainingUnit = s.JointTrainingUnit });
            return c;
        }
    }

    public class ResultBookSwimmerInfo
    {
        public string BibNumber { get; set; }
        public string Name { get; set; }
        public string Country { get; set; }
        public string Coach { get; set; }                // 教练员
        public string JointTrainingUnit { get; set; }    // 联合培养单位
    }
}
