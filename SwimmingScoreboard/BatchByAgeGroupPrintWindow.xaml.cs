// 2026-06-02 "按组别批量公布"窗口
// 选 性别 / 项目 / 赛次 → 一键生成该项目下"全部组别"的成绩单 (1 份整合文档, 每组分页)
// 底部 5 操作按钮 + 关闭 与 EventResultPrintWindow / DocumentPreviewWindow 完全一致
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwimmingScoreboard
{
    public partial class BatchByAgeGroupPrintWindow : Window
    {
        private readonly ObservableCollection<Swimmer> _swimmers;
        private readonly ObservableCollection<ScheduleItem> _schedule;
        private readonly string _competitionName;
        private readonly string _location;
        private readonly string _referee;
        private readonly IList<AgeGroup> _ageGroups;
        private bool _initialized;
        private string _selectedGender = "男", _selectedEvent = "", _selectedStage = "决赛";
        // 缓存最近一次"查询"产出, 5 个按钮共用
        private string _cachedHtml = "";
        private string _cachedFileBase = "";

        public BatchByAgeGroupPrintWindow(
            ObservableCollection<Swimmer> swimmers,
            ObservableCollection<ScheduleItem> schedule,
            string competitionName, string location, string referee,
            IList<AgeGroup> ageGroups)
        {
            InitializeComponent();
            _swimmers = swimmers;
            _schedule = schedule;
            _competitionName = competitionName ?? "";
            _location = location ?? "";
            _referee = referee ?? "";
            _ageGroups = ageGroups;
            PopulateEventCombo();
            _initialized = true;
        }

        private void PopulateEventCombo() {
            string gender = GetText(GenderCombo);
            string prev = EventCombo.SelectedItem as string ?? "";
            EventCombo.Items.Clear();
            var evSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (string.IsNullOrEmpty(s.EventName)) continue;
                // 2026-06-02 "全部" 性别 = 不过滤性别, 列出所有项目; 否则原行为 (含混合)
                if (gender != "全部" && s.Gender != gender && s.Gender != "混合") continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                evSet.Add(s.EventName);
            }
            foreach (var ev in evSet.OrderBy(x => x)) EventCombo.Items.Add(ev);
            if (!string.IsNullOrEmpty(prev) && EventCombo.Items.Contains(prev)) EventCombo.SelectedItem = prev;
            else if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) {
            if (!_initialized) return;
            if (sender == GenderCombo) PopulateEventCombo();
            // 切条件后清缓存 + 灰按钮
            _cachedHtml = "";
            SetActionButtonsEnabled(false);
            StatusText.Text = "请点击 查询 生成成绩单预览";
            StatusText.Foreground = Brushes.SlateGray;
            PreviewPanel.Children.Clear();
        }

        private void Query_Click(object sender, RoutedEventArgs e) {
            _selectedGender = GetText(GenderCombo);
            _selectedEvent = EventCombo.SelectedItem as string ?? "";
            _selectedStage = GetText(StageCombo);
            if (string.IsNullOrEmpty(_selectedEvent)) {
                StatusText.Text = "请先选择项目";
                StatusText.Foreground = Brushes.OrangeRed;
                return;
            }

            // 收集该 (性别, 项目, 赛次) 下所有有数据的组别 — 按 _ageGroups 列表顺序
            var ageNames = new List<string>();
            if (_ageGroups != null && _ageGroups.Count > 0) {
                foreach (var ag in _ageGroups) if (!string.IsNullOrEmpty(ag.Name)) ageNames.Add(ag.Name);
            } else {
                var set = new HashSet<string>();
                foreach (var s in _swimmers) if (!string.IsNullOrEmpty(s.AgeCategory)) set.Add(s.AgeCategory);
                ageNames.AddRange(set.OrderBy(x => x));
            }

            // 2026-06-02 并项拆分: 性别"全部" → 男+女 各跑一遍; 单一性别保持只跑那一种.
            //   每个 (性别, 注册组别) 内部再按 swimmer.Age 切子块 (例 "9-11岁" → 9岁/10岁/11岁).
            var gendersToRun = new List<string>();
            if (_selectedGender == "全部") { gendersToRun.Add("男"); gendersToRun.Add("女"); }
            else gendersToRun.Add(_selectedGender);

            bool isRelayEv = _selectedEvent.Contains("接力");
            var blocks = new List<AgeBlock>();
            foreach (var g in gendersToRun) {
                foreach (var ag in ageNames) {
                    var subBlocks = BuildAgeBlocksSplit(g, _selectedEvent, _selectedStage, ag, isRelayEv);
                    foreach (var sb2 in subBlocks) if (sb2.Rows.Count > 0) blocks.Add(sb2);
                }
            }
            if (blocks.Count == 0) {
                StatusText.Text = string.Format("{0} {1} {2} — 各组别均暂无成绩, 无法批量公布", _selectedGender, _selectedEvent, _selectedStage);
                StatusText.Foreground = Brushes.OrangeRed;
                PreviewPanel.Children.Clear();
                _cachedHtml = "";
                SetActionButtonsEnabled(false);
                return;
            }

            // 构建 HTML (整合 5 + 1 按钮共用) + 推荐文件名
            _cachedHtml = BuildHtml(blocks);
            _cachedFileBase = SanitizeFile(string.Format("批量公布_{0}_{1}_{2}", _selectedGender, _selectedEvent, _selectedStage));

            // 预览面板: 每子块一段, 标题含 性别 + 组别 + 子年龄
            PreviewPanel.Children.Clear();
            foreach (var b in blocks) {
                var header = new TextBlock {
                    Text = string.Format("{0}  {1} {2}  ({3} 人)", b.Title, _selectedEvent, _selectedStage, b.Rows.Count),
                    FontWeight = FontWeights.Bold, FontSize = 15,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
                    Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 6, 0, 4)
                };
                PreviewPanel.Children.Add(header);
                var dg = new DataGrid {
                    AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                    MinHeight = 30,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
                };
                dg.Columns.Add(new DataGridTextColumn { Header = "名次", Binding = new System.Windows.Data.Binding("Rank"), Width = new DataGridLength(50) });
                dg.Columns.Add(new DataGridTextColumn { Header = "道", Binding = new System.Windows.Data.Binding("Lane"), Width = new DataGridLength(40) });
                dg.Columns.Add(new DataGridTextColumn { Header = "姓名", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(200) });
                dg.Columns.Add(new DataGridTextColumn { Header = "代表队", Binding = new System.Windows.Data.Binding("Country"), Width = new DataGridLength(120) });
                dg.Columns.Add(new DataGridTextColumn { Header = "成绩", Binding = new System.Windows.Data.Binding("FinalTime"), Width = new DataGridLength(90) });
                dg.Columns.Add(new DataGridTextColumn { Header = "备注", Binding = new System.Windows.Data.Binding("Remark"), Width = new DataGridLength(70) });
                dg.ItemsSource = b.Rows;
                PreviewPanel.Children.Add(dg);
            }

            StatusText.Text = string.Format("{0} {1} {2} → 共 {3} 个 子表 (性别×组×子年龄) 有数据, 已生成批量成绩单",
                _selectedGender, _selectedEvent, _selectedStage, blocks.Count);
            StatusText.Foreground = Brushes.Green;
            SetActionButtonsEnabled(true);
        }

        // 2026-06-02 把单一 (性别, 注册组别) 内的运动员再按 swimmer.Age 切多个子块.
        //   接力 (无个人 Age) 退化为只 1 个子块, 标题不带 "X岁".
        private List<AgeBlock> BuildAgeBlocksSplit(string gender, string eventName, string stage, string ageGroup, bool isRelay) {
            var allRows = BuildOneAgeBlock(gender, eventName, stage, ageGroup);
            var output = new List<AgeBlock>();
            if (allRows.Count == 0) return output;
            string genderLabel = (gender == "男" || gender == "女") ? (gender + "子") : gender;
            if (isRelay) {
                // 接力不按年龄切
                // 2026-06-04 顺序统一: 性别 在前, 组别 在后
                output.Add(new AgeBlock {
                    AgeGroup = ageGroup, SubAge = 0,
                    Title = string.Format("{0} {1}", genderLabel, ageGroup),
                    Rows = allRows
                });
                return output;
            }
            // 按 swimmer.Age 分组. allRows 是 RowVm — 没带 Age, 这里换回 Swimmer 重新拿.
            // 简单做法: BuildOneAgeBlock 已经过滤好运动员, 这里再走一次按 Age 分桶.
            //   重新枚举 _swimmers 同款条件, 拿到 swimmer 对象 + 对应 RowVm 配对.
            var bucket = new SortedDictionary<int, List<RowVm>>();
            foreach (var s in _swimmers) {
                if (!(s.Gender == gender || s.Gender == "混合")) continue;
                if (s.EventName != eventName) continue;
                if ((s.AgeCategory ?? "") != ageGroup) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                var r = s.GetResultForStage(stage);
                if (r == null) continue;
                // 在 allRows 里找到对应这条 swimmer 的 RowVm (按 BibNumber + Name 双匹配, 兜底用 Name)
                RowVm vm = allRows.FirstOrDefault(rw =>
                    (!string.IsNullOrEmpty(rw.BibNumber) && rw.BibNumber == (s.BibNumber ?? ""))
                    || (rw.Name == (s.Name ?? "") && rw.Country == (s.Country ?? "")));
                if (vm == null) continue;
                int a = s.Age;
                if (!bucket.ContainsKey(a)) bucket[a] = new List<RowVm>();
                bucket[a].Add(vm);
            }
            // 单一年龄 → 不拆, 标题用 ageGroup 原名 (例 "9岁组" 已经语义清楚)
            // 2026-06-04 顺序统一: 性别 在前, 组别 在后
            if (bucket.Count <= 1) {
                output.Add(new AgeBlock {
                    AgeGroup = ageGroup, SubAge = bucket.Count == 1 ? bucket.First().Key : 0,
                    Title = string.Format("{0} {1}", genderLabel, ageGroup),
                    Rows = allRows
                });
                return output;
            }
            // 多个实际年龄 → 各出一个子块, 子块内按 SortTime 排序后重新算名次
            foreach (var kv in bucket) {
                int a = kv.Key;
                var rows = kv.Value.OrderBy(x => x.SortTime).ToList();
                int rk = 1;
                var rebuilt = new List<RowVm>();
                foreach (var rv in rows) {
                    rebuilt.Add(new RowVm {
                        Rank = rv.IsDQ ? "-" : rk.ToString(),
                        Lane = rv.Lane, BibNumber = rv.BibNumber, Name = rv.Name, Country = rv.Country,
                        FinalTime = rv.FinalTime, ReactionTime = rv.ReactionTime, ReactionHtml = rv.ReactionHtml,
                        Remark = rv.Remark, RemarkHtml = rv.RemarkHtml, IsDQ = rv.IsDQ, SortTime = rv.SortTime
                    });
                    if (!rv.IsDQ) rk++;
                }
                output.Add(new AgeBlock {
                    AgeGroup = ageGroup, SubAge = a,
                    // 2026-06-04 顺序统一: 性别 在前, 组别 + 子年龄 在后
                    Title = string.Format("{0} {1} ({2}岁)", genderLabel, ageGroup, a),
                    Rows = rebuilt
                });
            }
            return output;
        }

        private class AgeBlock { public string AgeGroup; public int SubAge; public string Title; public List<RowVm> Rows; }
        private class RowVm {
            public string Rank { get; set; }
            public int Lane { get; set; }
            public string BibNumber { get; set; }
            public string Name { get; set; }
            public string Country { get; set; }
            public string FinalTime { get; set; }
            public string ReactionTime { get; set; }
            public string ReactionHtml { get; set; }
            public string Remark { get; set; }
            public string RemarkHtml { get; set; }
            public bool IsDQ { get; set; }
            public double SortTime { get; set; }
        }

        private List<RowVm> BuildOneAgeBlock(string gender, string eventName, string stage, string ageGroup) {
            bool isRelay = eventName.Contains("接力");
            int legCount = 4;
            if (isRelay) {
                var mLeg = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)\s*[x×]\s*\d+");
                if (mLeg.Success) {
                    int n; if (int.TryParse(mLeg.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n;
                }
            }
            // 该年龄组下游泳员 (含混合性别接力)
            var matched = _swimmers.Where(s =>
                (s.Gender == gender || s.Gender == "混合") &&
                s.EventName == eventName &&
                (s.AgeCategory ?? "") == ageGroup &&
                !(s.Notes != null && s.Notes.StartsWith("接力队员")) &&
                s.GetResultForStage(stage) != null
            ).ToList();
            var withResults = matched.Where(s => {
                var r = s.GetResultForStage(stage);
                // 2026-06-04 TRI 不进 总排名性质 表 (按组别批量公布 = 总排名表)
                if (s.Status == "TRI") return false;
                if (r != null && r.Status == "TRI") return false;
                return r != null && (r.FinalTime > 0 || !string.IsNullOrEmpty(s.Status));
            }).ToList();
            if (withResults.Count == 0) return new List<RowVm>();
            var raw = withResults.Select(s => {
                var r = s.GetResultForStage(stage);
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (!string.IsNullOrEmpty(s.Status) && (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ" || s.Status == "DQ")) remark = s.Status;
                bool isDQ = !string.IsNullOrEmpty(remark);
                string displayName = s.Name ?? "";
                if (isRelay && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                    displayName = s.Notes.Substring("接力队 棒次:".Length);
                string reactionPlain = "", reactionHtml = "";
                if (isRelay) {
                    var parts = new List<string>();
                    for (int li = 0; li < legCount; li++) {
                        double rt = (r != null && r.LegReactionTimes != null && li < r.LegReactionTimes.Count) ? r.LegReactionTimes[li] : 0;
                        parts.Add(string.Format("第{0}棒:{1}", li + 1, (rt != 0 && !double.IsNaN(rt)) ? rt.ToString("F2") : "—"));
                    }
                    reactionPlain = string.Join("  ", parts.ToArray());
                    reactionHtml = string.Join("<br>", parts.ToArray());
                } else if (r != null && r.StartingBlockTime != 0) {
                    reactionPlain = r.StartingBlockTime.ToString("F2");
                    reactionHtml = reactionPlain;
                }
                return new {
                    Sw = s, R = r, IsDQ = isDQ, Remark = remark, DisplayName = displayName,
                    ReactionPlain = reactionPlain, ReactionHtml = reactionHtml,
                    SortTime = (isDQ || r == null) ? double.MaxValue : (r.FinalTime > 0 ? r.FinalTime : double.MaxValue)
                };
            }).OrderBy(x => x.SortTime).ToList();

            var rows = new List<RowVm>();
            int rank = 1;
            foreach (var x in raw) {
                int lane = x.R != null ? x.R.Lane : x.Sw.Lane;
                string remarkPlain = x.Remark;
                string remarkHtml;
                if (!string.IsNullOrEmpty(x.Remark)) remarkHtml = "<span style='color:#dc2626;'>" + x.Remark + "</span>";
                else remarkHtml = "";
                rows.Add(new RowVm {
                    Rank = x.IsDQ ? "-" : rank.ToString(),
                    Lane = lane,
                    BibNumber = x.Sw.BibNumber ?? "",
                    Name = x.DisplayName,
                    Country = x.Sw.Country ?? "",
                    FinalTime = x.IsDQ ? "" : (x.R != null && x.R.FinalTime > 0 ? TimeFormatter.Format(x.R.FinalTime) : ""),
                    ReactionTime = x.ReactionPlain,
                    ReactionHtml = x.ReactionHtml,
                    Remark = remarkPlain,
                    RemarkHtml = remarkHtml,
                    IsDQ = x.IsDQ,
                    SortTime = x.SortTime
                });
                if (!x.IsDQ) rank++;
            }
            return rows;
        }

        private string BuildHtml(List<AgeBlock> blocks) {
            bool isRelayEv = _selectedEvent.Contains("接力");
            string c1H = isRelayEv ? "代表队" : "姓名";
            string c2H = isRelayEv ? "姓名" : "代表队";
            int rxW = isRelayEv ? 110 : 70;

            var sb = new StringBuilder();
            sb.Append("<html><head><meta charset='UTF-8'><style>");
            sb.Append("body{font-family:'SimSun'; padding:0; margin:0; line-height:1.5; color:#333;} ");
            sb.Append(".page{padding:40px 50px; box-sizing:border-box;} ");
            sb.Append("h1{text-align:center; font-size:32px; font-family:'SimHei'; margin:0 0 6px; letter-spacing:5px;} ");
            sb.Append("h2{text-align:center; font-size:22px; font-family:'SimHei'; margin:0 0 22px; letter-spacing:8px; color:#1e40af;} ");
            sb.Append("h3{font-size:20px; font-family:'SimHei'; border-bottom:3px solid #1e40af; padding-bottom:6px; margin-top:24px; color:#1e40af;} ");
            sb.Append("h4{font-size:14px; font-weight:normal; color:#475569; margin-top:6px;} ");
            sb.Append("table{border-collapse:collapse; width:100%; margin:10px 0 20px; background:#fff;} ");
            sb.Append("th{border:1px solid #333; background:#dbeafe; padding:8px; font-weight:bold; font-size:13px; text-align:center; vertical-align:middle;} ");
            sb.Append("td{border:1px solid #333; padding:6px; text-align:center; font-size:13px;} ");
            sb.Append("tr:nth-child(even){background:#f0f7ff;} ");
            sb.Append(".signature-row{margin-top:30px; display:flex; justify-content:space-between; font-size:14px; font-weight:bold;} ");
            sb.Append("@media print { .page-break{page-break-before:always;} body{-webkit-print-color-adjust:exact;} @page { margin: 1cm; } } ");
            sb.Append("</style></head><body>");

            // 封面
            sb.Append("<div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", HtmlEnc(_competitionName));
            sb.Append("<h2>成 绩 单 （ 按 组 别 ）</h2>");
            sb.AppendFormat("<h4 style='text-align:center; font-size:18px;'>项目：{0} {1} {2}</h4>",
                HtmlEnc(_selectedGender), HtmlEnc(_selectedEvent), HtmlEnc(_selectedStage));
            sb.AppendFormat("<h4 style='text-align:center;'>地点：{0} &nbsp;&nbsp;&nbsp; 共 {1} 个组别</h4>",
                HtmlEnc(_location), blocks.Count);

            bool firstBlock = true;
            foreach (var b in blocks) {
                if (firstBlock) firstBlock = false;
                else sb.Append("<div class='page-break'></div><div class='page'>");

                // 2026-06-02 标题用 AgeBlock.Title (含性别 + 注册组别 + 可选实际年龄), 不再单独拼 _selectedGender
                sb.AppendFormat("<h3>{0}  {1} {2}　（{3}人）</h3>",
                    HtmlEnc(b.Title ?? b.AgeGroup), HtmlEnc(_selectedEvent), HtmlEnc(_selectedStage), b.Rows.Count);
                sb.Append("<table><tr align='center'>");
                sb.Append("<th width='50'>名次</th><th width='40'>道</th><th width='60'>号码</th>");
                sb.AppendFormat("<th width='180'>{0}</th><th width='110'>{1}</th>", c1H, c2H);
                sb.AppendFormat("<th width='80'>成绩</th><th width='{0}'>反应时间</th><th width='50'>备注</th></tr>", rxW);
                foreach (var r in b.Rows) {
                    string c1 = isRelayEv ? (r.Country ?? "") : (r.Name ?? "");
                    string c2 = isRelayEv ? (r.Name ?? "") : (r.Country ?? "");
                    sb.Append("<tr>");
                    sb.AppendFormat("<td>{0}</td><td>{1}</td><td>{2}</td>", r.Rank, r.Lane, HtmlEnc(r.BibNumber));
                    sb.AppendFormat("<td style='text-align:left; padding-left:10px;'><b>{0}</b></td><td>{1}</td>", HtmlEnc(c1), HtmlEnc(c2));
                    sb.AppendFormat("<td style='font-weight:bold; background:#eff6ff;'>{0}</td>", HtmlEnc(r.FinalTime));
                    sb.AppendFormat("<td style='font-size:11px;'>{0}</td><td>{1}</td>", r.ReactionHtml, r.RemarkHtml);
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
                sb.Append("<div class='signature-row'>");
                sb.AppendFormat("<p>裁判：{0}</p>", !string.IsNullOrEmpty(_referee) ? HtmlEnc(_referee) + "___________" : "__________________");
                sb.Append("<p>记录长：__________________</p>");
                sb.Append("</div>");
            }
            sb.AppendFormat("<p style='text-align:right; padding:14px 50px; color:gray; font-size:11px;'>打印时间：{0}</p>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        // ─── 5 个动作按钮 + 关闭 (与 EventResultPrintWindow / DocumentPreviewWindow 一致) ───
        private void SetActionButtonsEnabled(bool enabled) {
            if (OpenBrowserButton != null) OpenBrowserButton.IsEnabled = enabled;
            if (ExportPdfButton != null) ExportPdfButton.IsEnabled = enabled;
            if (ExportDocButton != null) ExportDocButton.IsEnabled = enabled;
            if (ExportHtmlButton != null) ExportHtmlButton.IsEnabled = enabled;
            if (PrintButton != null) PrintButton.IsEnabled = enabled;
        }

        private string WriteTempHtml() {
            string tmp = Path.Combine(Path.GetTempPath(), _cachedFileBase + ".html");
            File.WriteAllText(tmp, _cachedHtml, Encoding.UTF8);
            return tmp;
        }

        private void OpenBrowser_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_cachedHtml)) return;
            try { Process.Start(WriteTempHtml()); }
            catch (Exception ex) { MessageBox.Show("打开失败: " + ex.Message); }
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_cachedHtml)) return;
            try {
                Process.Start(WriteTempHtml());
                MessageBox.Show("已在浏览器中打开。\n请按 Ctrl+P 打印, 选择 \"Microsoft Print to PDF\" 打印机另存为 PDF。",
                    "导出 PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) { MessageBox.Show("导出 PDF 失败: " + ex.Message); }
        }

        private void ExportDoc_Click(object sender, RoutedEventArgs e) { SaveAs(".doc", "Word 文档|*.doc|所有文件|*.*"); }
        private void ExportHtml_Click(object sender, RoutedEventArgs e) { SaveAs(".html", "HTML 文件|*.html|所有文件|*.*"); }

        private void SaveAs(string ext, string filter) {
            if (string.IsNullOrEmpty(_cachedHtml)) return;
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = filter, FileName = _cachedFileBase + ext, Title = "导出 " + ext.TrimStart('.').ToUpper()
            };
            if (dlg.ShowDialog() != true) return;
            try {
                File.WriteAllText(dlg.FileName, _cachedHtml, Encoding.UTF8);
                if (MessageBox.Show("导出完成: \n" + dlg.FileName + "\n\n是否立即打开?", "导出成功",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                    Process.Start(dlg.FileName);
                }
            } catch (Exception ex) { MessageBox.Show("导出失败: " + ex.Message); }
        }

        private void Print_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_cachedHtml)) return;
            try {
                var prev = new DocumentPreviewWindow("按组别批量公布 - " + _cachedFileBase, _cachedHtml) { Owner = this };
                prev.Show();
            } catch (Exception ex) { MessageBox.Show("打印失败: " + ex.Message); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private static string GetText(ComboBox cb) {
            if (cb == null || cb.SelectedItem == null) return "";
            if (cb.SelectedItem is ComboBoxItem) return ((ComboBoxItem)cb.SelectedItem).Content.ToString();
            return cb.SelectedItem.ToString();
        }
        private static string HtmlEnc(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return System.Net.WebUtility.HtmlEncode(s);
        }
        private static string SanitizeFile(string s) {
            if (string.IsNullOrEmpty(s)) return "文档";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
