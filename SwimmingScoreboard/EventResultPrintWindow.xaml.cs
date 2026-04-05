using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace SwimmingScoreboard
{
    public partial class EventResultPrintWindow : Window
    {
        private ObservableCollection<Swimmer> _swimmers;
        private ObservableCollection<ScheduleItem> _schedule;
        private string _competitionName;
        private string _location;
        private string _referee;
        private string _chiefJudge;
        private string _starter;
        private bool _initialized = false;
        private List<object> _currentResults = new List<object>();

        public string SelectedGender { get; private set; }
        public string SelectedEvent { get; private set; }
        public string SelectedStage { get; private set; }
        public int SelectedHeat { get; private set; }

        public EventResultPrintWindow(ObservableCollection<Swimmer> swimmers,
            ObservableCollection<ScheduleItem> schedule,
            string competitionName, string location,
            string referee, string chiefJudge, string starter)
        {
            InitializeComponent();
            _swimmers = swimmers;
            _schedule = schedule;
            _competitionName = competitionName;
            _location = location;
            _referee = referee;
            _chiefJudge = chiefJudge;
            _starter = starter;
            PopulateEventCombo();
            _initialized = true;
            UpdateHeatCombo();
        }

        private void PopulateEventCombo()
        {
            var events = new HashSet<string>();
            foreach (var s in _swimmers)
            {
                if (!string.IsNullOrEmpty(s.EventName)) events.Add(s.EventName);
            }
            foreach (string ev in events.OrderBy(x => x))
            {
                EventCombo.Items.Add(ev);
            }
            if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;
            UpdateHeatCombo();
        }

        private void Heat_Changed(object sender, SelectionChangedEventArgs e) { }

        private void UpdateHeatCombo()
        {
            if (HeatCombo == null) return;
            string gender = GetComboText(GenderCombo);
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string stage = GetComboText(StageCombo);

            HeatCombo.Items.Clear();
            HeatCombo.Items.Add(new ComboBoxItem { Content = "全部" });

            var heats = new HashSet<int>();
            foreach (var s in _swimmers)
            {
                if (s.Gender != gender || s.EventName != eventName) continue;
                foreach (var r in s.Results)
                {
                    if (r.Stage == stage && r.Heat > 0) heats.Add(r.Heat);
                }
                var sa = s.GetAssignmentForStage(stage);
                if (sa != null && sa.Heat > 0) heats.Add(sa.Heat);
                if (s.CurrentStage == stage && s.Heat > 0) heats.Add(s.Heat);
            }
            foreach (int h in heats.OrderBy(x => x))
            {
                HeatCombo.Items.Add(new ComboBoxItem { Content = string.Format("第{0}组", h) });
            }
            HeatCombo.SelectedIndex = 0;

            PreviewGrid.ItemsSource = null;
            PrintButton.IsEnabled = false;
            StatusText.Text = "请选择条件后点击查询";
            StatusText.Foreground = System.Windows.Media.Brushes.SlateGray;
        }

        private void Query_Click(object sender, RoutedEventArgs e)
        {
            string gender = GetComboText(GenderCombo);
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string stage = GetComboText(StageCombo);
            string heatFilter = GetComboText(HeatCombo);
            int filterHeat = 0;
            if (heatFilter != "全部")
            {
                var m = System.Text.RegularExpressions.Regex.Match(heatFilter, @"\d+");
                if (m.Success) filterHeat = int.Parse(m.Value);
            }

            if (string.IsNullOrEmpty(eventName))
            {
                StatusText.Text = "请先选择比赛项目";
                StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            var matched = _swimmers.Where(s =>
                s.Gender == gender && s.EventName == eventName &&
                s.GetResultForStage(stage) != null
            ).ToList();

            if (filterHeat > 0)
            {
                matched = matched.Where(s =>
                {
                    var r = s.GetResultForStage(stage);
                    return r != null && r.Heat == filterHeat;
                }).ToList();
            }

            var withResults = matched.Where(s =>
            {
                var r = s.GetResultForStage(stage);
                return r != null && r.FinalTime > 0;
            }).ToList();

            if (withResults.Count == 0)
            {
                StatusText.Text = string.Format("{0} {1} {2}{3} — 暂无比赛成绩，无法打印",
                    gender, eventName, stage, filterHeat > 0 ? " 第" + filterHeat + "组" : "");
                StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                PreviewGrid.ItemsSource = null;
                PrintButton.IsEnabled = false;
                return;
            }

            var displayData = withResults.Select(s =>
            {
                var r = s.GetResultForStage(stage);
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (!string.IsNullOrEmpty(s.Status) && (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ" || s.Status == "DQ")) remark = s.Status;
                bool isDQ = !string.IsNullOrEmpty(remark);
                // 接力项目：Name显示队员姓名
                string epName = s.Name ?? "";
                if (stage.Length > 0 && eventName.Contains("接力") && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                    epName = s.Notes.Substring("接力队 棒次:".Length);
                return new
                {
                    SortTime = isDQ ? double.MaxValue : r.FinalTime,
                    IsDQ = isDQ,
                    Lane = r.Lane,
                    BibNumber = s.BibNumber ?? "",
                    Name = epName,
                    Country = s.Country ?? "",
                    FinalTime = isDQ ? "" : (r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : ""),
                    ReactionTime = r.StartingBlockTime > 0 ? r.StartingBlockTime.ToString("F2") : "",
                    Remark = remark
                };
            }).OrderBy(x => x.SortTime).ToList();

            _currentResults = new List<object>();
            int rank = 1;
            foreach (var item in displayData)
            {
                _currentResults.Add(new
                {
                    Rank = item.IsDQ ? "-" : rank.ToString(),
                    item.Lane,
                    item.BibNumber,
                    item.Name,
                    item.Country,
                    item.FinalTime,
                    item.ReactionTime,
                    item.Remark
                });
                if (!item.IsDQ) rank++;
            }

            PreviewGrid.ItemsSource = _currentResults;
            PrintButton.IsEnabled = true;

            SelectedGender = gender;
            SelectedEvent = eventName;
            SelectedStage = stage;
            SelectedHeat = filterHeat;

            string heatDesc = filterHeat > 0 ? " 第" + filterHeat + "组" : " 总排名";
            StatusText.Text = string.Format("{0} {1} {2}{3} — 共{4}人有成绩",
                gender, eventName, stage, heatDesc, withResults.Count);
            StatusText.Foreground = System.Windows.Media.Brushes.Green;
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResults.Count == 0) return;

            // 组号显示逻辑：决赛只有1组时不显示，预赛/半决赛即使1组也显示
            int totalHeats = _swimmers.Where(s =>
                s.Gender == SelectedGender && s.EventName == SelectedEvent &&
                s.GetResultForStage(SelectedStage) != null
            ).Select(s => s.GetResultForStage(SelectedStage).Heat).Distinct().Count();

            bool showHeat = SelectedHeat > 0 &&
                ((totalHeats > 1) || SelectedStage.Contains("预赛") || SelectedStage.Contains("半决赛"));
            string heatDisplay = showHeat ? string.Format(" 第 {0} 组", SelectedHeat) : "";

            string eventTitle = string.Format("{0} {1} {2}{3}",
                SelectedGender, SelectedEvent, SelectedStage, heatDisplay);

            // 匹配赛程获取日期时间
            string dateTimeInfo = "（时间待定）";
            if (_schedule != null)
            {
                var sch = _schedule.FirstOrDefault(s =>
                    s.Gender == SelectedGender && s.EventName == SelectedEvent && s.Stage == SelectedStage);
                if (sch != null)
                    dateTimeInfo = string.Format("{0} {1}", sch.Date, !string.IsNullOrEmpty(sch.Time) ? sch.Time : "").Trim();
            }

            var sb = new StringBuilder();
            // HTML头和样式（参照跳水格式）
            sb.Append("<html><head><meta charset='UTF-8'><style>");
            sb.Append("body{font-family:'SimSun'; padding:0; margin:0; line-height:1.5; color:#333;} ");
            sb.Append(".page{padding:50px; position:relative; box-sizing:border-box; min-height:1000px;} ");
            sb.Append("h1{text-align:center; font-size:36px; font-family:'SimHei'; margin-top:10px; letter-spacing:5px;} ");
            sb.Append("h2{text-align:center; font-size:28px; font-family:'SimHei'; margin-bottom:50px; letter-spacing:10px;} ");
            sb.Append("h3{font-size:22px; font-family:'SimHei'; border-bottom:3px solid #1e40af; padding-bottom:8px; margin-top:40px; color:#1e40af;} ");
            sb.Append("h4{font-size:18px; font-weight:bold; margin-top:20px; border-left:5px solid #1e40af; padding-left:10px;} ");
            sb.Append("table{border-collapse:collapse; width:100%; margin:15px 0; background:#fff;} ");
            sb.Append("th{border:1px solid #333; background:#dbeafe; padding:10px; font-weight:bold; font-size:14px;} ");
            sb.Append("td{border:1px solid #333; padding:8px; text-align:center; font-size:14px;} ");
            sb.Append("tr:nth-child(even){background:#f0f7ff;} ");
            sb.Append(".signature-row{margin-top:60px; display:flex; justify-content:space-between; font-size:15px; font-weight:bold;} ");
            sb.Append("@media print { .page-break{page-break-before:always;} body{-webkit-print-color-adjust:exact;} @page { margin: 1cm; } } ");
            sb.Append("</style></head><body>");

            // 正文
            sb.Append("<div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", _competitionName);
            sb.Append("<h2>成 绩 单</h2>");
            sb.AppendFormat("<h3>项目：{0}</h3>", eventTitle);
            sb.AppendFormat("<h4>比赛时间：{0} &nbsp;&nbsp;&nbsp;&nbsp; 地点：{1}</h4>",
                dateTimeInfo, !string.IsNullOrEmpty(_location) ? _location : "——");

            // 成绩表（接力：代表队在前）
            bool epRelay = SelectedEvent.Contains("接力");
            string epH1 = epRelay ? "代表队" : "姓名";
            string epH2 = epRelay ? "姓名" : "代表队";
            sb.Append("<table><tr>");
            sb.AppendFormat("<th width='50'>名次</th><th width='40'>道</th><th width='60'>号码</th>");
            sb.AppendFormat("<th width='100'>{0}</th><th width='100'>{1}</th>", epH1, epH2);
            sb.Append("<th width='90'>最终成绩</th><th width='70'>反应时间</th><th width='50'>备注</th>");
            sb.Append("</tr>");
            foreach (dynamic item in _currentResults)
            {
                string c1 = epRelay ? item.Country : item.Name;
                string c2 = epRelay ? item.Name : item.Country;
                sb.Append("<tr>");
                sb.AppendFormat("<td>{0}</td><td>{1}</td><td>{2}</td>", item.Rank, item.Lane, item.BibNumber);
                sb.AppendFormat("<td><b>{0}</b></td><td>{1}</td>", c1, c2);
                sb.AppendFormat("<td style='font-weight:bold; background:#eff6ff;'>{0}</td>", item.FinalTime);
                sb.AppendFormat("<td>{0}</td><td style='color:#dc2626;'>{1}</td>", item.ReactionTime, item.Remark);
                sb.Append("</tr>");
            }
            sb.Append("</table>");

            // 签名栏
            sb.Append("<div class='signature-row'>");
            sb.AppendFormat("<p>裁判长：{0}</p>",
                !string.IsNullOrEmpty(_chiefJudge) ? _chiefJudge + "___________" : "__________________");
            sb.AppendFormat("<p>裁判：{0}</p>",
                !string.IsNullOrEmpty(_referee) ? _referee + "___________" : "__________________");
            sb.Append("<p>记录长：__________________</p>");
            sb.Append("</div>");

            sb.Append("</div>");
            sb.AppendFormat("<p style='text-align:right; padding:20px; color:gray;'>打印时间：{0}</p>",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</body></html>");

            // 输出文件
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Documents");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string safeEvent = SelectedGender + SelectedEvent;
                string heatSuffix = showHeat ? "_第" + SelectedHeat + "组" : "";
                string fileName = string.Format("成绩单_{0}_{1}{2}.html", safeEvent, SelectedStage, heatSuffix);
                string filePath = Path.Combine(dir, fileName);
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                Process.Start(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("文档生成失败: " + ex.Message, "错误");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private string GetComboText(ComboBox combo)
        {
            if (combo == null || combo.SelectedItem == null) return "";
            if (combo.SelectedItem is ComboBoxItem)
                return ((ComboBoxItem)combo.SelectedItem).Content.ToString();
            return combo.SelectedItem.ToString();
        }
    }
}
