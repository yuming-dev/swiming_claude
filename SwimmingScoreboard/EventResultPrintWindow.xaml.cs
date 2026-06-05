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
        public string SelectedAgeGroup { get; private set; }
        public int SelectedHeat { get; private set; }

        public EventResultPrintWindow(ObservableCollection<Swimmer> swimmers,
            ObservableCollection<ScheduleItem> schedule,
            string competitionName, string location,
            string referee, string chiefJudge, string starter,
            IList<AgeGroup> ageGroups = null)
        {
            InitializeComponent();
            _swimmers = swimmers;
            _schedule = schedule;
            _competitionName = competitionName;
            _location = location;
            _referee = referee;
            _chiefJudge = chiefJudge;
            _starter = starter;
            PopulateAgeGroupCombo(ageGroups);
            PopulateEventCombo();
            _initialized = true;
            UpdateHeatCombo();
        }

        // 2026-06-01 加 AgeGroup 下拉, 解决决赛-only 比赛多年龄组共用 EventName 时按 Heat 误叠的 bug
        private void PopulateAgeGroupCombo(IList<AgeGroup> ageGroups) {
            AgeGroupCombo.Items.Add("全部");
            // 优先用配置的 AgeGroups; 否则从 _swimmers.AgeCategory 自动收集
            var names = new List<string>();
            if (ageGroups != null && ageGroups.Count > 0) {
                foreach (var ag in ageGroups) {
                    if (!string.IsNullOrEmpty(ag.Name)) names.Add(ag.Name);
                }
            } else {
                var set = new HashSet<string>();
                foreach (var s in _swimmers) {
                    if (!string.IsNullOrEmpty(s.AgeCategory)) set.Add(s.AgeCategory);
                }
                names.AddRange(set.OrderBy(x => x));
            }
            foreach (var n in names) AgeGroupCombo.Items.Add(n);
            AgeGroupCombo.SelectedIndex = 0;
        }

        // 组别匹配: 空/"全部"/"不限" = 全选; 否则严格相等
        private static bool MatchesAge(string swimmerAge, string filterAge) {
            if (string.IsNullOrEmpty(filterAge) || filterAge == "全部" || filterAge == "不限") return true;
            return (swimmerAge ?? "") == filterAge;
        }

        private void PopulateEventCombo()
        {
            // 2026-06-01 按当前组别+性别过滤可选项目
            string ageFilter = AgeGroupCombo != null && AgeGroupCombo.SelectedItem != null ? AgeGroupCombo.SelectedItem.ToString() : "全部";
            string gender = GetComboText(GenderCombo);
            string prev = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            EventCombo.Items.Clear();
            var events = new HashSet<string>();
            foreach (var s in _swimmers)
            {
                if (string.IsNullOrEmpty(s.EventName)) continue;
                if (!string.IsNullOrEmpty(gender) && s.Gender != gender && s.Gender != "混合") continue;
                if (!MatchesAge(s.AgeCategory, ageFilter)) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                events.Add(s.EventName);
            }
            foreach (string ev in events.OrderBy(x => x)) EventCombo.Items.Add(ev);
            if (!string.IsNullOrEmpty(prev) && EventCombo.Items.Contains(prev))
                EventCombo.SelectedItem = prev;
            else if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;
            // 当组别/性别变化时重建项目列表
            if (sender == AgeGroupCombo || sender == GenderCombo) {
                PopulateEventCombo();
            }
            UpdateHeatCombo();
        }

        private void Heat_Changed(object sender, SelectionChangedEventArgs e) { }

        private void UpdateHeatCombo()
        {
            if (HeatCombo == null) return;
            string ageFilter = AgeGroupCombo != null && AgeGroupCombo.SelectedItem != null ? AgeGroupCombo.SelectedItem.ToString() : "全部";
            string gender = GetComboText(GenderCombo);
            string eventName = EventCombo.SelectedItem != null ? EventCombo.SelectedItem.ToString() : "";
            string stage = GetComboText(StageCombo);

            HeatCombo.Items.Clear();
            HeatCombo.Items.Add(new ComboBoxItem { Content = "全部" });

            var heats = new HashSet<int>();
            // 2026-06-01 加 AgeGroup 过滤, 避免决赛-only 比赛多年龄组共用 EventName 时 Heat 数字重叠
            foreach (var s in _swimmers)
            {
                if (s.Gender != gender && s.Gender != "混合") continue;
                if (s.EventName != eventName) continue;
                if (!MatchesAge(s.AgeCategory, ageFilter)) continue;
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
            SetActionButtonsEnabled(false);
            StatusText.Text = "请选择条件后点击查询";
            StatusText.Foreground = System.Windows.Media.Brushes.SlateGray;
        }

        private void Query_Click(object sender, RoutedEventArgs e)
        {
            string ageFilter = AgeGroupCombo != null && AgeGroupCombo.SelectedItem != null ? AgeGroupCombo.SelectedItem.ToString() : "全部";
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

            // 2026-06-01 加 AgeGroup 过滤; 男/女 也包含混合性别接力
            var matched = _swimmers.Where(s =>
                (s.Gender == gender || s.Gender == "混合") &&
                s.EventName == eventName &&
                MatchesAge(s.AgeCategory, ageFilter) &&
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
                // 2026-06-04 TRI 不进项目成绩 (= 总排名性质) 表, spec: '总排名列表中不显示 TRI'
                if (s.Status == "TRI") return false;
                if (r != null && r.Status == "TRI") return false;
                return r != null && r.FinalTime > 0;
            }).ToList();

            if (withResults.Count == 0)
            {
                StatusText.Text = string.Format("{0} {1} {2}{3} — 暂无比赛成绩，无法打印",
                    gender, eventName, stage, filterHeat > 0 ? " 第" + filterHeat + "组" : "");
                StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                PreviewGrid.ItemsSource = null;
                SetActionButtonsEnabled(false);
                return;
            }

            // 接力赛棒次数（用于反应时分棒输出）
            bool isRelay = eventName.Contains("接力");
            int legCount = 4;
            if (isRelay) {
                var mLeg = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)\s*[x×]\s*\d+");
                if (mLeg.Success) {
                    int n; if (int.TryParse(mLeg.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n;
                }
            }

            // 录取标志 Q：判断打印的赛次后是否还有"半决赛 / 决赛"分组
            // 预赛 → 半决赛（若 schedule 含）/ 决赛；半决赛 → 决赛；决赛无下一赛次
            string nextStageQ = null;
            if (stage == "预赛") {
                bool hasSemi = _schedule != null && _schedule.Any(s => s.Gender == gender && s.EventName == eventName && s.Stage == "半决赛");
                nextStageQ = hasSemi ? "半决赛" : "决赛";
            } else if (stage == "半决赛") {
                nextStageQ = "决赛";
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
                // 反应时：接力赛展开为 N 棒（"第N棒:0.45"），未记录到的棒显示"—"；个人赛仍是单值
                // 预览（DataGrid TextWrapping=Wrap）用空格分隔以便在窄列里按词换行；打印 HTML 用 <br>
                string reactionPlain = "", reactionHtml = "";
                if (isRelay) {
                    var parts = new List<string>();
                    for (int li = 0; li < legCount; li++) {
                        double rt = (r != null && r.LegReactionTimes != null && li < r.LegReactionTimes.Count) ? r.LegReactionTimes[li] : 0;
                        parts.Add(string.Format("第{0}棒:{1}", li + 1, rt > 0 ? rt.ToString("F2") : "—"));
                    }
                    reactionPlain = string.Join("  ", parts.ToArray());
                    reactionHtml = string.Join("<br>", parts.ToArray());
                } else if (r != null && r.StartingBlockTime != 0) {
                    reactionPlain = r.StartingBlockTime.ToString("F2");
                    reactionHtml = reactionPlain;
                }
                // 是否晋级到下一赛次：检查该运动员是否已被分配到 nextStageQ 的组次
                bool qualified = !isDQ && !string.IsNullOrEmpty(nextStageQ) && s.GetAssignmentForStage(nextStageQ) != null;
                return new
                {
                    SortTime = isDQ ? double.MaxValue : r.FinalTime,
                    RawFinalTime = isDQ ? 0 : r.FinalTime,
                    IsDQ = isDQ,
                    Lane = r.Lane,
                    BibNumber = s.BibNumber ?? "",
                    Name = epName,
                    Country = s.Country ?? "",
                    FinalTime = isDQ ? "" : (r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : ""),
                    ReactionTime = reactionPlain,        // DataGrid 预览用：空格分隔，TextWrapping=Wrap 自动换行
                    ReactionTimeHtml = reactionHtml,     // 打印 HTML 用：<br> 强制每棒一行
                    Remark = remark,
                    Qualified = qualified
                };
            }).OrderBy(x => x.SortTime).ToList();

            // 计算第1名成绩 = 排序后的第一个非 DQ 项
            double leaderTime = 0;
            foreach (var d in displayData) {
                if (!d.IsDQ && d.RawFinalTime > 0) { leaderTime = d.RawFinalTime; break; }
            }

            _currentResults = new List<object>();
            int rank = 1;
            foreach (var item in displayData)
            {
                string diffText = "";
                if (!item.IsDQ && item.RawFinalTime > 0 && leaderTime > 0 && item.RawFinalTime > leaderTime) {
                    diffText = (item.RawFinalTime - leaderTime).ToString("F2");
                }
                // 备注列：判罚（DSQ/DNS/DNF）优先；否则若已晋级则显示 Q
                string remarkPlain = item.Remark;
                string remarkHtml;
                if (!string.IsNullOrEmpty(item.Remark)) {
                    remarkHtml = "<span style='color:#dc2626;'>" + item.Remark + "</span>";
                } else if (item.Qualified) {
                    remarkPlain = "Q";
                    remarkHtml = "<span style='color:#16a34a;font-weight:bold;'>Q</span>";
                } else {
                    remarkHtml = "";
                }
                _currentResults.Add(new
                {
                    Rank = item.IsDQ ? "-" : rank.ToString(),
                    item.Lane,
                    item.BibNumber,
                    item.Name,
                    item.Country,
                    item.FinalTime,
                    Diff = diffText,
                    item.ReactionTime,
                    item.ReactionTimeHtml,
                    Remark = remarkPlain,
                    RemarkHtml = remarkHtml
                });
                if (!item.IsDQ) rank++;
            }

            PreviewGrid.ItemsSource = _currentResults;
            SetActionButtonsEnabled(true);

            SelectedGender = gender;
            SelectedEvent = eventName;
            SelectedStage = stage;
            SelectedAgeGroup = ageFilter;
            SelectedHeat = filterHeat;

            string ageHead = (string.IsNullOrEmpty(ageFilter) || ageFilter == "全部") ? "" : (ageFilter + " ");
            string heatDesc = filterHeat > 0 ? " 第" + filterHeat + "组" : " 总排名";
            StatusText.Text = string.Format("{0}{1} {2} {3}{4} — 共{5}人有成绩",
                ageHead, gender, eventName, stage, heatDesc, withResults.Count);
            StatusText.Foreground = System.Windows.Media.Brushes.Green;
        }

        // 2026-06-01 集中开关 5 个动作按钮 (查询出结果后才启用)
        private void SetActionButtonsEnabled(bool enabled) {
            if (OpenBrowserButton != null) OpenBrowserButton.IsEnabled = enabled;
            if (ExportPdfButton != null) ExportPdfButton.IsEnabled = enabled;
            if (ExportDocButton != null) ExportDocButton.IsEnabled = enabled;
            if (ExportHtmlButton != null) ExportHtmlButton.IsEnabled = enabled;
            if (PrintButton != null) PrintButton.IsEnabled = enabled;
        }

        // 2026-06-01 5 个动作按钮共用的 HTML 构造 (原 Print_Click 文件输出主体抽出来)
        private string BuildPrintHtml(out string suggestedFileName) {
            suggestedFileName = "";
            if (_currentResults.Count == 0) return "";

            // 组号显示逻辑：决赛只有1组时不显示，预赛/半决赛即使1组也显示
            // 2026-06-01 totalHeats 也要带 AgeGroup 过滤, 不然跨组别会把别人的 Heat 也算进来
            int totalHeats = _swimmers.Where(s =>
                (s.Gender == SelectedGender || s.Gender == "混合") &&
                s.EventName == SelectedEvent &&
                MatchesAge(s.AgeCategory, SelectedAgeGroup) &&
                s.GetResultForStage(SelectedStage) != null
            ).Select(s => s.GetResultForStage(SelectedStage).Heat).Distinct().Count();

            bool showHeat = SelectedHeat > 0 &&
                ((totalHeats > 1) || SelectedStage.Contains("预赛") || SelectedStage.Contains("半决赛"));
            string heatDisplay = showHeat ? string.Format(" 第 {0} 组", SelectedHeat) : "";

            // 2026-06-04 顺序统一: 性别 组别 项目 赛次 (= 性别 前)
            string ageHead = (string.IsNullOrEmpty(SelectedAgeGroup) || SelectedAgeGroup == "全部") ? "" : (SelectedAgeGroup + " ");
            string eventTitle = string.Format("{0} {1}{2} {3}{4}",
                SelectedGender, ageHead, SelectedEvent, SelectedStage, heatDisplay);

            // 匹配赛程获取日期时间
            string dateTimeInfo = "（时间待定）";
            if (_schedule != null)
            {
                var sch = _schedule.FirstOrDefault(s =>
                    s.Gender == SelectedGender && s.EventName == SelectedEvent && s.Stage == SelectedStage &&
                    ((s.AgeGroup ?? "") == (SelectedAgeGroup == "全部" ? (s.AgeGroup ?? "") : (SelectedAgeGroup ?? ""))));
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
            // 接力赛反应时分 4 棒，宽度加大以容纳"第N棒:0.45"换行展示
            int reactionWidth = epRelay ? 110 : 70;
            sb.AppendFormat("<th width='90'>最终成绩</th><th width='70'>成绩差</th><th width='{0}'>反应时间</th><th width='50'>备注</th>", reactionWidth);
            sb.Append("</tr>");
            foreach (dynamic item in _currentResults)
            {
                string c1 = epRelay ? item.Country : item.Name;
                string c2 = epRelay ? item.Name : item.Country;
                sb.Append("<tr>");
                sb.AppendFormat("<td>{0}</td><td>{1}</td><td>{2}</td>", item.Rank, item.Lane, item.BibNumber);
                sb.AppendFormat("<td><b>{0}</b></td><td>{1}</td>", c1, c2);
                sb.AppendFormat("<td style='font-weight:bold; background:#eff6ff;'>{0}</td>", item.FinalTime);
                sb.AppendFormat("<td>{0}</td>", item.Diff);
                sb.AppendFormat("<td style='font-size:12px;'>{0}</td><td>{1}</td>", item.ReactionTimeHtml, item.RemarkHtml);
                sb.Append("</tr>");
            }
            sb.Append("</table>");

            // 签名栏 (2026-05-26 删除"编排长"签字行)
            sb.Append("<div class='signature-row'>");
            sb.AppendFormat("<p>裁判：{0}</p>",
                !string.IsNullOrEmpty(_referee) ? _referee + "___________" : "__________________");
            sb.Append("<p>记录长：__________________</p>");
            sb.Append("</div>");

            sb.Append("</div>");
            sb.AppendFormat("<p style='text-align:right; padding:20px; color:gray;'>打印时间：{0}</p>",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</body></html>");

            // 推荐的文件名 (5 个按钮共用)
            string ageNamePart = (string.IsNullOrEmpty(SelectedAgeGroup) || SelectedAgeGroup == "全部") ? "" : (SelectedAgeGroup + "_");
            string safeEvent2 = ageNamePart + SelectedGender + SelectedEvent;
            string heatSuffix2 = showHeat ? "_第" + SelectedHeat + "组" : "";
            suggestedFileName = string.Format("成绩单_{0}_{1}{2}", safeEvent2, SelectedStage, heatSuffix2);
            foreach (char c in Path.GetInvalidFileNameChars()) suggestedFileName = suggestedFileName.Replace(c, '_');

            return sb.ToString();
        }

        // 2026-06-01 5 个动作按钮: 在浏览器打开 / 导出 PDF / 导出 DOC / 导出 HTML / 打印
        //   同步行为与"成绩册/分段计时报告"使用的 DocumentPreviewWindow 一致.
        private string WriteTempHtml(string html, string suggested) {
            string tmp = Path.Combine(Path.GetTempPath(), suggested + ".html");
            File.WriteAllText(tmp, html, Encoding.UTF8);
            return tmp;
        }

        private void OpenBrowser_Click(object sender, RoutedEventArgs e) {
            try {
                string suggested; var html = BuildPrintHtml(out suggested);
                if (string.IsNullOrEmpty(html)) return;
                var p = WriteTempHtml(html, suggested);
                Process.Start(p);
            } catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message); }
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e) {
            try {
                string suggested; var html = BuildPrintHtml(out suggested);
                if (string.IsNullOrEmpty(html)) return;
                var p = WriteTempHtml(html, suggested);
                Process.Start(p);
                MessageBox.Show("已在浏览器中打开。\n请按 Ctrl+P 打印，选择 \"Microsoft Print to PDF\" 打印机另存为 PDF。",
                    "导出 PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) { MessageBox.Show("导出 PDF 失败：" + ex.Message); }
        }

        private void ExportDoc_Click(object sender, RoutedEventArgs e) { SaveAs(".doc", "Word 文档|*.doc|所有文件|*.*"); }
        private void ExportHtml_Click(object sender, RoutedEventArgs e) { SaveAs(".html", "HTML 文件|*.html|所有文件|*.*"); }

        private void SaveAs(string ext, string filter) {
            string suggested; var html = BuildPrintHtml(out suggested);
            if (string.IsNullOrEmpty(html)) return;
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = filter,
                FileName = suggested + ext,
                Title = "导出 " + ext.TrimStart('.').ToUpper()
            };
            if (dlg.ShowDialog() != true) return;
            try {
                File.WriteAllText(dlg.FileName, html, Encoding.UTF8);
                if (MessageBox.Show("导出完成：\n" + dlg.FileName + "\n\n是否立即打开？", "导出成功",
                                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                    Process.Start(dlg.FileName);
                }
            } catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message); }
        }

        // "打印"按钮: 复用 DocumentPreviewWindow (含 WebBrowser, execCommand 打印更可靠)
        private void Print_Click(object sender, RoutedEventArgs e) {
            try {
                string suggested; var html = BuildPrintHtml(out suggested);
                if (string.IsNullOrEmpty(html)) return;
                var prevWin = new DocumentPreviewWindow("项目成绩 - " + suggested, html) { Owner = this };
                prevWin.Show();
            } catch (Exception ex) { MessageBox.Show("打印失败：" + ex.Message); }
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
