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
        private string _selectedGender = "男", _selectedEvent = "", _selectedStage = "决赛", _selectedAgeGroup = "全部";
        // 缓存最近一次"查询"产出, 5 个按钮共用
        private string _cachedHtml = "";
        private string _cachedFileBase = "";

        /// <summary>
        /// 2026-09-03 由主窗口注入: 本项目这个赛次一共几组(组数列的分母)。
        /// 用的是主窗口那一份 TotalHeatsOfEvent —— 先问竞赛库, 库里问不到退回赛程。
        /// 没注入就退回"这张子表里出现过几个组次"。
        /// </summary>
        public Func<string, string, string, string, int> TotalHeatsOf { get; set; }

        public BatchByAgeGroupPrintWindow(
            ObservableCollection<Swimmer> swimmers,
            ObservableCollection<ScheduleItem> schedule,
            string competitionName, string location, string referee,
            IList<AgeGroup> ageGroups)
        {
            InitializeComponent();
            MainWindow.FillGenderCombo(GenderCombo);   // 2026-08-21 按比赛档案的性别表填，不再写死
            MainWindow.FillStageCombo(StageCombo);     // 2026-09-03 赛次同理，按参数设置里的赛次表填(保留"全部")
            _swimmers = swimmers;
            _schedule = schedule;
            _competitionName = competitionName ?? "";
            _location = location ?? "";
            _referee = referee ?? "";
            _ageGroups = ageGroups;
            PopulateAgeGroupCombo();
            PopulateEventCombo();
            _initialized = true;
        }

        /// <summary>2026-09-03 组别下拉: "全部" + 档案里配置的组别(没配就从名单里收)。</summary>
        private void PopulateAgeGroupCombo() {
            AgeGroupCombo.Items.Clear();
            AgeGroupCombo.Items.Add("全部");
            foreach (string n in AllAgeGroupNames()) AgeGroupCombo.Items.Add(n);
            AgeGroupCombo.SelectedIndex = 0;
        }

        private List<string> AllAgeGroupNames() {
            var names = new List<string>();
            if (_ageGroups != null && _ageGroups.Count > 0) {
                foreach (var ag in _ageGroups)
                    if (!string.IsNullOrEmpty(ag.Name) && !names.Contains(ag.Name)) names.Add(ag.Name);
            } else {
                var set = new HashSet<string>();
                foreach (var s in _swimmers) if (!string.IsNullOrEmpty(s.AgeCategory)) set.Add(s.AgeCategory);
                names.AddRange(set.OrderBy(x => x));
            }
            return names;
        }

        private void PopulateEventCombo() {
            string gender = GetText(GenderCombo);
            string ageG = GetText(AgeGroupCombo);
            string prev = EventCombo.SelectedItem as string ?? "";
            EventCombo.Items.Clear();
            EventCombo.Items.Add("全部");   // 2026-09-03 项目也能选"全部": 一次生成整本
            var evSet = new HashSet<string>();
            foreach (var s in _swimmers) {
                if (string.IsNullOrEmpty(s.EventName)) continue;
                // 2026-06-02 "全部" 性别 = 不过滤性别, 列出所有项目; 否则原行为 (含混合)
                if (gender != "全部" && s.Gender != gender && s.Gender != "混合") continue;
                if (ageG != "全部" && (s.AgeCategory ?? "") != ageG) continue;
                if (s.Notes != null && s.Notes.StartsWith("接力队员")) continue;
                evSet.Add(s.EventName);
            }
            foreach (var ev in evSet.OrderBy(x => x)) EventCombo.Items.Add(ev);
            if (!string.IsNullOrEmpty(prev) && EventCombo.Items.Contains(prev)) EventCombo.SelectedItem = prev;
            else if (EventCombo.Items.Count > 0) EventCombo.SelectedIndex = 0;
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) {
            if (!_initialized) return;
            if (sender == GenderCombo || sender == AgeGroupCombo) PopulateEventCombo();
            // 切条件后清缓存 + 灰按钮
            _cachedHtml = "";
            SetActionButtonsEnabled(false);
            StatusText.Text = Loc.T("Str_Win_BatchAge_StatusClickQuery");
            StatusText.Foreground = Brushes.SlateGray;
            PreviewPanel.Children.Clear();
        }

        private void Query_Click(object sender, RoutedEventArgs e) {
            _selectedGender = GetText(GenderCombo);
            _selectedEvent = EventCombo.SelectedItem as string ?? "";
            _selectedStage = GetText(StageCombo);
            _selectedAgeGroup = GetText(AgeGroupCombo);
            if (string.IsNullOrEmpty(_selectedEvent)) {
                StatusText.Text = Loc.T("Str_Win_BatchAge_StatusSelectEventFirst");
                StatusText.Foreground = Brushes.OrangeRed;
                return;
            }

            // 2026-09-03 四个维度都支持"全部", 各自展开成要跑的清单。
            //   组别全部 = 每个组别各出一张子表(原行为); 项目/赛次全部 = 挨个跑一遍。
            var ageNames = new List<string>();
            if (_selectedAgeGroup == "全部") ageNames.AddRange(AllAgeGroupNames());
            else ageNames.Add(_selectedAgeGroup);
            // 档案里一个组别都没配时, 用空串跑一遍, 免得整个循环空转
            if (ageNames.Count == 0) ageNames.Add("");

            // 2026-06-02 并项拆分: 性别"全部" → 男+女 各跑一遍; 单一性别保持只跑那一种.
            //   每个 (性别, 注册组别) 内部再按 swimmer.Age 切子块 (例 "9-11岁" → 9岁/10岁/11岁).
            var gendersToRun = new List<string>();
            if (_selectedGender == "全部") { gendersToRun.Add("男"); gendersToRun.Add("女"); }
            else gendersToRun.Add(_selectedGender);

            var eventsToRun = new List<string>();
            if (_selectedEvent == "全部") {
                foreach (var it in EventCombo.Items) {
                    string ev = it as string;
                    if (!string.IsNullOrEmpty(ev) && ev != "全部") eventsToRun.Add(ev);
                }
            } else eventsToRun.Add(_selectedEvent);

            // 2026-09-03 "全部"赛次要跑哪几个, 取【比赛参数设置管理 → 赛次】那张表, 不写死
            var stagesToRun = new List<string>();
            if (_selectedStage == "全部") stagesToRun.AddRange(StageRegistry.List);
            else stagesToRun.Add(_selectedStage);

            var blocks = new List<AgeBlock>();
            foreach (var g in gendersToRun) {
                foreach (var ev in eventsToRun) {
                    bool isRelayEv = ev.Contains("接力");
                    foreach (var st in stagesToRun) {
                        foreach (var ag in ageNames) {
                            var subBlocks = BuildAgeBlocksSplit(g, ev, st, ag, isRelayEv);
                            foreach (var sb2 in subBlocks) if (sb2.Rows.Count > 0) blocks.Add(sb2);
                        }
                    }
                }
            }
            if (blocks.Count == 0) {
                StatusText.Text = Loc.F("Str_Win_BatchAge_StatusNoDataFmt",
                    _selectedAgeGroup, _selectedGender, _selectedEvent, _selectedStage);
                StatusText.Foreground = Brushes.OrangeRed;
                PreviewPanel.Children.Clear();
                _cachedHtml = "";
                SetActionButtonsEnabled(false);
                return;
            }

            // 构建 HTML (整合 5 + 1 按钮共用) + 推荐文件名
            _cachedHtml = BuildHtml(blocks);
            _cachedFileBase = SanitizeFile(Loc.F("Str_DocC_BatchPublishFileFmt",
                _selectedAgeGroup, Loc.GenderDisplay(_selectedGender), _selectedEvent, Loc.StageDisplay(_selectedStage)));

            // 预览面板: 每子块一段, 标题含 性别 + 组别 + 子年龄 + 项目 + 赛次
            PreviewPanel.Children.Clear();
            foreach (var b in blocks) {
                var header = new TextBlock {
                    Text = Loc.F("Str_Win_BatchAge_PreviewHeaderFmt", b.Title, b.EventName, b.Stage, b.Rows.Count),
                    FontWeight = FontWeights.Bold, FontSize = 15,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
                    Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 6, 0, 4)
                };
                PreviewPanel.Children.Add(header);
                PreviewPanel.Children.Add(BuildPreviewGrid(b));
            }

            StatusText.Text = Loc.F("Str_Win_BatchAge_StatusDoneFmt",
                _selectedAgeGroup, _selectedGender, _selectedEvent, _selectedStage, blocks.Count,
                (_selectedEvent == "全部" || _selectedStage == "全部") ? Loc.T("Str_Win_BatchAge_ExtraDims") : "");
            StatusText.Foreground = Brushes.Green;
            SetActionButtonsEnabled(true);
        }

        /// <summary>
        /// 2026-09-03 预览表格改成跟"项目成绩"一模一样的列:
        ///   单项 名次/姓名/代表队/号码/组别/组数/道次/最终成绩/成绩差/反应时间/备注
        ///   接力 名次/代表队/姓名/…(姓名与代表队对调, 其余相同)
        /// 内容一律居中(文本列走 ElementStyle, 见 XAML 里的 CellCenter)。
        /// </summary>
        private DataGrid BuildPreviewGrid(AgeBlock b) {
            var dg = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                AlternatingRowBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                MinHeight = 30,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
            };
            var center = TryFindResource("CellCenter") as Style;
            Action<string, string, double> add = delegate(string header, string path, double w) {
                var col = new DataGridTextColumn {
                    Header = header,
                    Binding = new System.Windows.Data.Binding(path),
                    Width = new DataGridLength(w)
                };
                if (center != null) col.ElementStyle = center;
                dg.Columns.Add(col);
            };
            bool relay = (b.EventName ?? "").Contains("接力");
            add(Loc.T("Str_Results_ColRank"), "Rank", 50);
            if (relay) { add(Loc.T("Str_Col_Team"), "Country", 120); add(Loc.T("Str_Col_Name"), "Name", 200); }
            else       { add(Loc.T("Str_Col_Name"), "Name", 200);      add(Loc.T("Str_Col_Team"), "Country", 120); }
            add(Loc.T("Str_Col_BibNo"), "BibNumber", 60);
            add(Loc.T("Str_Col_Group"), "AgeGroupName", 70);
            add(Loc.T("Str_Win_BatchAge_ColHeatCount"), "HeatText", 60);
            add(Loc.T("Str_Col_Lane"), "Lane", 45);
            add(Loc.T("Str_Win_BatchAge_ColFinalTime"), "FinalTime", 90);
            add(Loc.T("Str_Win_BatchAge_ColDiff"), "Diff", 70);
            add(Loc.T("Str_Win_BatchAge_ColReactionTime"), "ReactionTime", relay ? 170 : 80);
            add(Loc.T("Str_Col_Notes"), "Remark", 50);
            dg.ItemsSource = b.Rows;
            return dg;
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
                    Rows = allRows,
                    EventName = eventName, Stage = stage, Gender = gender
                });
                foreach (var b0 in output) FinalizeBlock(b0);
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
                    Rows = allRows,
                    EventName = eventName, Stage = stage, Gender = gender
                });
                foreach (var b1 in output) FinalizeBlock(b1);
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
                        AgeGroupName = rv.AgeGroupName, HeatNo = rv.HeatNo,
                        FinalTime = rv.FinalTime, ReactionTime = rv.ReactionTime, ReactionHtml = rv.ReactionHtml,
                        Remark = rv.Remark, RemarkHtml = rv.RemarkHtml, IsDQ = rv.IsDQ, SortTime = rv.SortTime
                    });
                    if (!rv.IsDQ) rk++;
                }
                output.Add(new AgeBlock {
                    AgeGroup = ageGroup, SubAge = a,
                    // 2026-06-04 顺序统一: 性别 在前, 组别 + 子年龄 在后
                    Title = string.Format("{0} {1} ({2}岁)", genderLabel, ageGroup, a),
                    Rows = rebuilt,
                    EventName = eventName, Stage = stage, Gender = gender
                });
            }
            foreach (var b2 in output) FinalizeBlock(b2);
            return output;
        }

        // 2026-09-03 子块要记住自己是哪个项目/赛次 —— 项目和赛次都能选"全部"了,
        //   一次查询里会同时出现好几个项目, 标题和表头不能再拿窗口上那个选择去拼。
        private class AgeBlock {
            public string AgeGroup; public int SubAge; public string Title; public List<RowVm> Rows;
            public string EventName; public string Stage; public string Gender;
        }
        private class RowVm {
            public string Rank { get; set; }
            public int Lane { get; set; }
            public string BibNumber { get; set; }
            public string Name { get; set; }
            public string Country { get; set; }
            // 2026-09-03 补 组别/组数/成绩差 三列(与"项目成绩"同款)
            public string AgeGroupName { get; set; }
            public int HeatNo { get; set; }
            public string HeatText { get; set; }
            public string Diff { get; set; }
            public string FinalTime { get; set; }
            public string ReactionTime { get; set; }
            public string ReactionHtml { get; set; }
            public string Remark { get; set; }
            public string RemarkHtml { get; set; }
            public bool IsDQ { get; set; }
            public double SortTime { get; set; }
        }

        /// <summary>
        /// 2026-09-03 一张子表建好之后再补两列:
        ///   组数  = "第X组/本项目总组数"(分母问主窗口/竞赛库, 不是"这张表里出现过几组")
        ///   成绩差 = 本人成绩 - 本子表第一名; 判罚/弃权不参与, 也不当基准。
        /// 放在最后统一算, 是因为按年龄再切子块时行会重排, 基准也跟着变。
        /// </summary>
        private void FinalizeBlock(AgeBlock b) {
            if (b == null || b.Rows == null || b.Rows.Count == 0) return;
            int total = 0;
            if (TotalHeatsOf != null) {
                try { total = TotalHeatsOf(b.AgeGroup ?? "", b.Gender ?? "", b.EventName ?? "", b.Stage ?? ""); }
                catch { }
            }
            if (total <= 0) {
                var hs = new HashSet<int>();
                foreach (var r in b.Rows) if (r.HeatNo > 0) hs.Add(r.HeatNo);
                total = hs.Count;
            }
            double leader = 0;
            foreach (var r in b.Rows) {
                if (r.IsDQ || r.SortTime <= 0 || r.SortTime == double.MaxValue) continue;
                if (leader <= 0 || r.SortTime < leader) leader = r.SortTime;
            }
            foreach (var r in b.Rows) {
                r.HeatText = r.HeatNo > 0
                    ? (total > 0 ? r.HeatNo + "/" + total : r.HeatNo.ToString())
                    : "";
                r.Diff = (!r.IsDQ && r.SortTime > 0 && r.SortTime != double.MaxValue
                          && leader > 0 && r.SortTime > leader)
                         ? (r.SortTime - leader).ToString("F2") : "";
            }
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
                // 2026-09-03 接力这一支原来【没有判罚守卫】—— 个人项目那半边有(见下面的 !isDQ),
                //   接力就漏了。结果是 DNF 的接力队成绩留空了、名次是 "-", 反应时那一列却还
                //   老老实实印着四棒的数字(用户在样张里看出来的)。成绩都不算数了, 反应时更不该留。
                if (isRelay && !isDQ) {
                    var parts = new List<string>();
                    for (int li = 0; li < legCount; li++) {
                        double rt = (r != null && r.LegReactionTimes != null && li < r.LegReactionTimes.Count) ? r.LegReactionTimes[li] : 0;
                        parts.Add(string.Format("第{0}棒:{1}", li + 1, (rt != 0 && !double.IsNaN(rt)) ? rt.ToString("F2") : "—"));
                    }
                    reactionPlain = string.Join("  ", parts.ToArray());
                    // 2026-09-03 打印时两棒一行 —— 四棒各占一行会把接力那张表撑到普通表的四倍高,
                    //   一页只放得下一张。预览里仍是空格分隔的一行。
                    reactionHtml = PairLines(parts);
                } else if (r != null && r.StartingBlockTime != 0 && !double.IsNaN(r.StartingBlockTime)) {
                    // 2026-08-31 判罚/弃权不显示反应时间(与项目成绩、本组成绩单同口径)
                    if (!isDQ) reactionPlain = r.StartingBlockTime.ToString("F2");
                    reactionHtml = reactionPlain;
                }
                return new {
                    Sw = s, R = r, IsDQ = isDQ, Remark = remark, DisplayName = displayName,
                    ReactionPlain = reactionPlain, ReactionHtml = reactionHtml,
                    SortTime = (isDQ || r == null) ? double.MaxValue : (r.FinalTime > 0 ? r.FinalTime : double.MaxValue)
                };
            }).ToList();

            // 2026-09-16 改调统一入口 ResultOrdering.RankForTotalView —— 跟成绩册名次公告/
            //   项目成绩打印/"成绩与排名"总排名视图是同一份规则、同一处维护, 不再在这里
            //   单独写一遍"先按 SortTime 排序、再各算各的 bgRanks[]"。
            // 2026-09-01 名次【从库里读】, 这里不算 —— 确认成绩时竞赛库已经算好并回读
            //   进内存(项目定稿后是 event_rankings 的值)。库里没有就显示 "-"。
            List<int> bgRankList;
            raw = ResultOrdering.RankForTotalView(raw,
                x => x.Remark, x => x.Sw.EventRankFor(stage), x => x.SortTime, out bgRankList);

            var rows = new List<RowVm>();
            for (int bi = 0; bi < raw.Count; bi++) {
                var x = raw[bi];
                int lane = x.R != null ? x.R.Lane : x.Sw.Lane;
                string remarkPlain = x.Remark;
                string remarkHtml;
                if (!string.IsNullOrEmpty(x.Remark)) remarkHtml = "<span style='color:#dc2626;'>" + x.Remark + "</span>";
                else remarkHtml = "";
                rows.Add(new RowVm {
                    Rank = bgRankList[bi] > 0 ? bgRankList[bi].ToString() : "-",
                    Lane = lane,
                    BibNumber = x.Sw.BibNumber ?? "",
                    Name = x.DisplayName,
                    Country = x.Sw.Country ?? "",
                    // 2026-09-03 组别取运动员自己的注册组别(子表可能按实际年龄再切, 组别名不变)
                    AgeGroupName = x.Sw.AgeCategory ?? "",
                    HeatNo = x.R != null ? x.R.Heat : 0,
                    FinalTime = x.IsDQ ? "" : (x.R != null && x.R.FinalTime > 0 ? TimeFormatter.Format(x.R.FinalTime) : ""),
                    ReactionTime = x.ReactionPlain,
                    ReactionHtml = x.ReactionHtml,
                    Remark = remarkPlain,
                    RemarkHtml = remarkHtml,
                    IsDQ = x.IsDQ,
                    SortTime = x.SortTime
                });
            }
            return rows;
        }

        private string BuildHtml(List<AgeBlock> blocks) {
            // ══════════════════════════════════════════════════════════════
            // 2026-09-03 排版重做。原来的三个毛病:
            //   ① 每张子表 page-break-before:always —— 3 个人的表也独占一整页,
            //      20 张子表打出来 24 页, 页页大半是空白。
            //   ② 列宽写的是 px, 表格挤在页面左半边, 人少的表更窄。
            //   ③ 裁判/记录长签名行每张子表都来一遍。
            // 现在: 子表连排(整张表不允许被拆到两页), 列宽用百分比 + table-layout:fixed
            //      占满纸宽, 签名行只在末尾出现一次。
            // ══════════════════════════════════════════════════════════════
            var sb = new StringBuilder();
            sb.Append("<html><head><meta charset='UTF-8'><style>");
            // 2026-09-16 页码, 跟项目成绩打印(EventResultPrintWindow)同一写法。
            sb.Append("@page{ size:A4; margin:14mm 12mm; @bottom-center { content: '" + Loc.T("Str_DocC_PageOfFmt") + "'; font-size:9px; color:#64748b; font-family:SimSun; } } ");
            sb.Append("body{font-family:'Microsoft YaHei','微软雅黑',SimHei,SimSun,sans-serif; padding:0; margin:0; line-height:1.45; color:#1f2937;} ");
            sb.Append(".page{padding:0 4px; box-sizing:border-box;} ");
            sb.Append("h1{text-align:center; font-size:26px; margin:0 0 4px; letter-spacing:3px; color:#0f172a;} ");
            sb.Append("h2{text-align:center; font-size:17px; margin:0 0 10px; letter-spacing:6px; color:#1e40af; font-weight:normal;} ");
            sb.Append(".meta{text-align:center; font-size:12px; color:#475569; margin:0 0 4px;} ");
            sb.Append(".rule{height:2px; background:#1e40af; margin:8px 0 4px;} ");
            // 2026-09-03 【一张子表 = 一张纸】。用户: 不剪, 直接一张张揭下来贴公告栏。
            //   所以每张子表另起一页(第一张跟在抬头后面, 不单独浪费一页),
            //   整块也不许被切开。
            sb.Append(".blk{page-break-inside:avoid; break-inside:avoid; margin:14px 0 0;} ");
            sb.Append(".blk.np{margin-top:0;} ");
            // 2026-09-16 换页规则从 .blk.np 挪到独立的 .page-break 标记 div 上(跟秩序册/
            // 成绩册那套是同一份写法) —— 实测揪出来的: page-break-before 挂在【第一个子
            // 元素是 table 的 div】上, Word 会整条无视, 不管这个 div 前面是不是先有个 h3
            // 标题(子表结构固定是 h3+table, 每次都踩); 换成挂在不含表格的独立标记 div 上
            // 才稳定生效。Chrome/PDF 这条路本来就认 .blk.np 那种写法, 不受这个坑影响,
            // 所以顺手统一成同一套, 不用两边各写一份。
            sb.Append(".page-break{page-break-before:always; break-before:page;} ");
            sb.Append(".blk h3{font-size:14px; margin:0 0 5px; padding:4px 8px; color:#1e3a8a;"
                    + " background:#e8f0fe; border-left:4px solid #1e40af;} ");
            sb.Append(".blk h3 .n{float:right; font-weight:normal; font-size:11px; color:#475569;} ");
            // 每张子表自带签名行 —— 每张都是要单独张贴的正式成绩单
            sb.Append(".sig{margin:14px 2px 0; display:flex; justify-content:space-between; font-size:13px; color:#334155;} ");
            sb.Append("table{border-collapse:collapse; width:100%; table-layout:fixed; margin:0; background:#fff;} ");
            sb.Append("th{border:1px solid #94a3b8; background:#1e40af; color:#fff; padding:5px 3px;"
                    + " font-weight:bold; font-size:12px; text-align:center; vertical-align:middle;} ");
            sb.Append("td{border:1px solid #cbd5e1; padding:4px 3px; text-align:center; font-size:12px;"
                    + " vertical-align:middle; word-break:break-all;} ");
            sb.Append("tbody tr:nth-child(even){background:#f4f7fd;} ");
            sb.Append(".nm{font-weight:bold;} ");
            sb.Append(".tm{font-weight:bold; font-family:Consolas,'Courier New',monospace; background:#eff6ff;} ");
            sb.Append(".r1{background:#fde68a; font-weight:bold;} .r2{background:#e5e7eb; font-weight:bold;} .r3{background:#fed7aa; font-weight:bold;} ");
            sb.Append(".rt{font-size:11px; color:#475569;} ");
            sb.Append(".signature-row{margin-top:26px; display:flex; justify-content:space-between; font-size:13px;} ");
            sb.Append(".foot{text-align:right; color:#94a3b8; font-size:10px; margin-top:10px;} ");
            sb.Append("@media print { body{-webkit-print-color-adjust:exact; print-color-adjust:exact;} } ");
            sb.Append("</style></head><body><div class='page'>");

            // 抬头(只在最前面出现一次, 不再单独占一页)
            sb.AppendFormat("<h1>{0}</h1>", HtmlEnc(_competitionName));
            sb.Append("<h2>" + Loc.T("Str_DocC_BatchHeaderTitle") + "</h2>");
            sb.AppendFormat("<div class='meta'>{0} {1} {2} {3}</div>",
                HtmlEnc(_selectedAgeGroup), HtmlEnc(Loc.GenderDisplay(_selectedGender)), HtmlEnc(_selectedEvent), HtmlEnc(Loc.StageDisplay(_selectedStage)));
            // 2026-09-03 地点没填就整段不显示 —— 原来会印出光秃秃一个"地点："
            sb.Append("<div class='meta'>" + Loc.F("Str_DocC_BatchSubTableCountFmt",
                string.IsNullOrWhiteSpace(_location) ? "" : (Loc.T("Str_DocC_LabelVenue") + HtmlEnc(_location) + "　|　"), blocks.Count) + "</div>");
            sb.Append("<div class='rule'></div>");

            bool firstBlk = true;
            foreach (var b in blocks) {
                // 第一张接在抬头下面; 之后每张另起一页(换页标记见上面 .page-break 的注释)
                if (!firstBlk) sb.Append("<div class='page-break'>&nbsp;</div>");
                sb.AppendFormat("<div class='blk{0}'>", firstBlk ? "" : " np");
                firstBlk = false;

                // 2026-06-02 标题用 AgeBlock.Title (含性别 + 注册组别 + 可选实际年龄), 不再单独拼 _selectedGender
                // 2026-09-03 项目/赛次取【本子块自己的】—— 它们都能选"全部", 一次查询里会有好几个项目
                bool isRelayEv = (b.EventName ?? "").Contains("接力");
                string c1H = isRelayEv ? Loc.T("Str_DocC_ColTeam") : Loc.T("Str_DocC_ColName");
                string c2H = isRelayEv ? Loc.T("Str_DocC_ColName") : Loc.T("Str_DocC_ColTeam");
                // 2026-09-03 标题栏右侧带上赛事名 —— 这几张是要剪下来分别贴公告栏的,
                //   剪开之后光有"男子 青少年(12岁) 100米蛙泳 决赛"看不出是哪场比赛。
                // 2026-09-30 现场反馈(说了很多次): 英文模式下 {2}(赛次, 如"Final") 和
                //   <span>{3}(赛事名) 之间原来一个分隔符都没有, 直接拼成"Final甘肃省..."
                //   挤在一起——中文模式("决赛甘肃省...")其实有同样的缺口, 只是两段都是
                //   中文时不太容易看出来, 英文单词直接顶上中文汉字就非常刺眼。补一个跟
                //   这行其它地方一致的全角空格分隔符。
                sb.AppendFormat("<h3>{0}　{1} {2}　<span class='n'>{3}　|　{4} " + Loc.T("Str_Common_Entries") + "</span></h3>",
                    HtmlEnc(b.Title ?? b.AgeGroup), HtmlEnc(b.EventName), HtmlEnc(Loc.StageDisplay(b.Stage)),
                    HtmlEnc(_competitionName), b.Rows.Count);
                // 2026-09-03 列宽改百分比 + table-layout:fixed —— 原来是 px, 表格挤在左半边,
                //   人少的表更窄(实测 1 人的表只有半幅纸宽)。百分比才能稳定占满。
                //   列序跟"项目成绩"一致: 名次 → 姓名/代表队 → 号码 → 组别 → 组数 → 道次
                //                        → 最终成绩 → 成绩差 → 反应时间 → 备注
                int[] w = isRelayEv ? new[] { 5, 15, 21, 6, 8, 6, 5, 10, 7, 12, 5 }
                                    : new[] { 5, 18, 14, 7, 9, 7, 6, 11, 8, 9, 6 };
                sb.Append("<table><colgroup>");
                foreach (int x in w) sb.AppendFormat("<col style='width:{0}%'/>", x);
                sb.Append("</colgroup><thead><tr>");
                sb.Append("<th>" + Loc.T("Str_DocC_ColRank") + "</th>");
                sb.AppendFormat("<th>{0}</th><th>{1}</th>", c1H, c2H);
                sb.Append("<th>" + Loc.T("Str_DocC_ColBib") + "</th><th>" + Loc.T("Str_DocC_ColAgeGroup") + "</th><th>" + Loc.T("Str_DocC_ColHeatCount") + "</th><th>" + Loc.T("Str_DocC_ColLaneNo") + "</th>");
                sb.Append("<th>" + Loc.T("Str_DocC_ColFinalResult") + "</th><th>" + Loc.T("Str_DocC_ColGap") + "</th><th>" + Loc.T("Str_DocC_ColReactionTime") + "</th><th>" + Loc.T("Str_DocC_ColNote") + "</th></tr></thead><tbody>");
                foreach (var r in b.Rows) {
                    string c1 = isRelayEv ? (r.Country ?? "") : (r.Name ?? "");
                    string c2 = isRelayEv ? (r.Name ?? "") : (r.Country ?? "");
                    // 前三名给金/银/铜底色 —— 一眼看得出领奖台
                    string rkCls = r.Rank == "1" ? " class='r1'" : r.Rank == "2" ? " class='r2'" : r.Rank == "3" ? " class='r3'" : "";
                    sb.Append("<tr>");
                    sb.AppendFormat("<td{0}>{1}</td>", rkCls, r.Rank);
                    sb.AppendFormat("<td class='nm'>{0}</td><td>{1}</td>", HtmlEnc(c1), HtmlEnc(c2));
                    sb.AppendFormat("<td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td>",
                        HtmlEnc(r.BibNumber), HtmlEnc(r.AgeGroupName), HtmlEnc(r.HeatText), r.Lane);
                    sb.AppendFormat("<td class='tm'>{0}</td>", HtmlEnc(r.FinalTime));
                    sb.AppendFormat("<td>{0}</td>", HtmlEnc(r.Diff));
                    sb.AppendFormat("<td class='rt'>{0}</td><td>{1}</td>", r.ReactionHtml, r.RemarkHtml);
                    sb.Append("</tr>");
                }
                sb.Append("</tbody></table>");
                // 2026-09-03 【每张子表自带签名】—— 用户: 这几张是要剪下来单独贴公告栏的,
                //   每张都得有裁判和记录长的签名位。签名行放在 .blk 里面, 跟着表一起
                //   不允许被分页切开 —— 不然会出现"表在这一页、签名在下一页"。
                sb.Append("<div class='sig'>");
                sb.Append("<span>" + Loc.F("Str_DocC_RefereeSigFmt",
                    !string.IsNullOrEmpty(_referee) ? HtmlEnc(_referee) + "　___________" : "__________________") + "</span>");
                sb.Append("<span>" + Loc.T("Str_DocC_RecorderSig") + "</span>");
                sb.AppendFormat("<span>{0}</span>", DateTime.Now.ToString("yyyy-MM-dd"));
                sb.Append("</div>");
                sb.Append("</div>");   // .blk
            }
            sb.Append("<div class='foot'>" + Loc.F("Str_DocC_BatchFooterFmt",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), blocks.Count) + "</div>");
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
            catch (Exception ex) { MessageBox.Show(Loc.T("Str_Win_DocPreview_MsgOpenFailPrefix") + ex.Message); }
        }

        /// <summary>
        /// 2026-09-03 "导出 PDF" 直接出 PDF 文件(跟"项目成绩"那边同一处理)。
        /// 原来只是把 HTML 在浏览器里打开再让人自己 Ctrl+P —— 按钮叫导出 PDF, 按下去却没有 PDF。
        /// 走的是"比赛日志 PDF"那条路: Edge/Chrome 无头 --print-to-pdf。
        /// </summary>
        private void ExportPdf_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_cachedHtml)) return;
            try {
                var dlg = new Microsoft.Win32.SaveFileDialog {
                    Filter = Loc.T("Str_Win_DocPreview_PdfFilter"),
                    FileName = _cachedFileBase + ".pdf",
                    Title = Loc.T("Str_Win_DocPreview_ExportPdfBtn")
                };
                if (dlg.ShowDialog() != true) return;
                string tmpHtml = WriteTempHtml();
                if (MainWindow.TryHtmlToPdf(tmpHtml, dlg.FileName)) {
                    if (MessageBox.Show(Loc.F("Str_Win_DocPreview_MsgExportedOpenFmt", dlg.FileName), Loc.T("Str_Win_DocPreview_ExportPdfBtn"),
                            MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        Process.Start(dlg.FileName);
                    return;
                }
                Process.Start(tmpHtml);
                MessageBox.Show(Loc.T("Str_Win_DocPreview_MsgNoPdfEngine"),
                    Loc.T("Str_Win_DocPreview_ExportPdfBtn"), MessageBoxButton.OK, MessageBoxImage.Warning);
            } catch (Exception ex) { MessageBox.Show(Loc.T("Str_Win_DocPreview_MsgExportPdfFailPrefix") + ex.Message); }
        }

        private void ExportDoc_Click(object sender, RoutedEventArgs e) { SaveAs(".doc", Loc.T("Str_Win_DocPreview_DocFilter")); }
        private void ExportHtml_Click(object sender, RoutedEventArgs e) { SaveAs(".html", Loc.T("Str_Win_DocPreview_HtmlFilter")); }

        private void SaveAs(string ext, string filter) {
            if (string.IsNullOrEmpty(_cachedHtml)) return;
            var dlg = new Microsoft.Win32.SaveFileDialog {
                Filter = filter, FileName = _cachedFileBase + ext, Title = Loc.T("Str_Win_DocPreview_ExportHtmlBtn")
            };
            if (dlg.ShowDialog() != true) return;
            try {
                File.WriteAllText(dlg.FileName, _cachedHtml, Encoding.UTF8);
                if (MessageBox.Show(Loc.F("Str_Win_DocPreview_MsgExportedOpenFmt", dlg.FileName), Loc.T("Str_Win_DocPreview_MsgTitleExportSuccess"),
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) {
                    Process.Start(dlg.FileName);
                }
            } catch (Exception ex) { MessageBox.Show(Loc.T("Str_Win_DocPreview_MsgExportFailPrefix") + ex.Message); }
        }

        private void Print_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_cachedHtml)) return;
            try {
                var prev = new DocumentPreviewWindow(Loc.T("Str_Win_BatchAge_Title") + " - " + _cachedFileBase, _cachedHtml) { Owner = this };
                prev.Show();
            } catch (Exception ex) { MessageBox.Show(Loc.T("Str_Win_BatchAge_MsgPrintFailPrefix") + ex.Message); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private static string GetText(ComboBox cb) {
            if (cb == null || cb.SelectedItem == null) return "";
            if (cb.SelectedItem is ComboBoxItem) return ((ComboBoxItem)cb.SelectedItem).Content.ToString();
            return cb.SelectedItem.ToString();
        }
        /// <summary>2026-09-03 接力反应时打印用: 两棒一行, 少占一半高度。</summary>
        internal static string PairLines(List<string> parts) {
            if (parts == null || parts.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i += 2) {
                if (i > 0) sb.Append("<br>");
                sb.Append(parts[i]);
                if (i + 1 < parts.Count) sb.Append("&nbsp;&nbsp;").Append(parts[i + 1]);
            }
            return sb.ToString();
        }

        private static string HtmlEnc(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return System.Net.WebUtility.HtmlEncode(s);
        }
        private static string SanitizeFile(string s) {
            if (string.IsNullOrEmpty(s)) return Loc.T("Str_Win_DocPreview_DefaultDocName");
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
