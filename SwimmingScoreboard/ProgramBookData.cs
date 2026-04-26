using System.Collections.Generic;

namespace SwimmingScoreboard
{
    // 秩序册可编辑数据：所有字段为空时使用自动生成内容
    public class ProgramBookData
    {
        public string CoverTitle { get; set; }       // 默认 "游泳比赛"
        public string CoverSubtitle { get; set; }    // 默认 "秩 序 册"
        public string Foreword { get; set; }         // 前言 / 序
        public string Regulations { get; set; }      // 竞赛规程总则
        public string SupplementaryNotice { get; set; } // 比赛补充通知
        public string ClosingNote { get; set; }      // 尾页 / 附注
        public string VenueImagePath { get; set; }   // 比赛场地示意图（图片本地路径）

        public List<ProgramBookActivityItem> KeyActivities { get; set; }
        public List<ProgramBookTrainingItem> TrainingSchedule { get; set; }
        public List<ProgramBookOfficialItem> Officials { get; set; }
        public List<ProgramBookTeamStaff> TeamStaffList { get; set; }

        public ProgramBookData() {
            CoverTitle = "游泳比赛";
            CoverSubtitle = "秩 序 册";
            Foreword = "";
            Regulations = "";
            SupplementaryNotice = "";
            ClosingNote = "";
            VenueImagePath = "";
            KeyActivities = new List<ProgramBookActivityItem>();
            TrainingSchedule = new List<ProgramBookTrainingItem>();
            Officials = new List<ProgramBookOfficialItem>();
            TeamStaffList = new List<ProgramBookTeamStaff>();
        }

        public ProgramBookData Clone() {
            var c = new ProgramBookData {
                CoverTitle = CoverTitle,
                CoverSubtitle = CoverSubtitle,
                Foreword = Foreword,
                Regulations = Regulations,
                SupplementaryNotice = SupplementaryNotice,
                ClosingNote = ClosingNote,
                VenueImagePath = VenueImagePath
            };
            foreach (var x in KeyActivities) c.KeyActivities.Add(new ProgramBookActivityItem { Date = x.Date, Time = x.Time, Activity = x.Activity, Participants = x.Participants, Venue = x.Venue });
            foreach (var x in TrainingSchedule) c.TrainingSchedule.Add(new ProgramBookTrainingItem { Date = x.Date, Time = x.Time, Venue = x.Venue });
            foreach (var x in Officials) c.Officials.Add(new ProgramBookOfficialItem { Title = x.Title, Name = x.Name });
            foreach (var x in TeamStaffList) c.TeamStaffList.Add(new ProgramBookTeamStaff { TeamName = x.TeamName, Leader = x.Leader, Coaches = x.Coaches, Doctors = x.Doctors, Staff = x.Staff });
            return c;
        }
    }

    public class ProgramBookActivityItem
    {
        public string Date { get; set; }
        public string Time { get; set; }
        public string Activity { get; set; }
        public string Participants { get; set; }
        public string Venue { get; set; }
    }

    public class ProgramBookTrainingItem
    {
        public string Date { get; set; }
        public string Time { get; set; }
        public string Venue { get; set; }
    }

    public class ProgramBookOfficialItem
    {
        public string Title { get; set; }   // 例如：仲裁委员、总裁判长、检录长
        public string Name { get; set; }
    }

    public class ProgramBookTeamStaff
    {
        public string TeamName { get; set; }
        public string Leader { get; set; }    // 领队（多人逗号分隔）
        public string Coaches { get; set; }   // 教练
        public string Doctors { get; set; }   // 队医
        public string Staff { get; set; }     // 工作人员
    }
}
