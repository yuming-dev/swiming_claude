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

        /// <summary>
        /// 2026-08-31 由主窗口注入: 查询前把这个项目从竞赛库整个刷新一遍。
        /// 参数 = (组别, 性别, 项目, 赛次); 组别传空表示全部。
        /// 没注入(null)时退化成"用内存里的", 不阻断。
        /// </summary>
        public Action<string, string, string, string> RefreshFromDb { get; set; }

        /// <summary>
        /// 2026-09-01 由主窗口注入: 直接读【组排名表】(项目定稿后的总排名)。
        /// 参数 = (组别, 性别, 项目, 赛次)。返回空表 = 这个项目还没定稿。
        ///
        /// 这是本窗口的【第一数据源】。原来只从内存 _swimmers 里捞成绩行, 而内存里
        /// 的成绩行只有主服务器亲自收到过回推才会有 —— 回推丢一次、道次对不上一次,
        /// 库里明明是全的、名次也定了稿, 这张表照样是空的, 还查不出原因。
        /// 名单(姓名/代表队/号码)内存里一直是全的, 缺的只是成绩; 所以成绩和名次读库,
        /// 反应时/性别/组别这些库里没存的列再回内存补。
        /// </summary>
        public Func<string, string, string, string, List<Db.EventRankRow>> ReadEventRankings { get; set; }

        /// <summary>2026-09-01 注入: 返回 {总组数, 未确认组数, 已有名次人数}; 总组数 -1 = 库里找不到这个项目。</summary>
        public Func<string, string, string, string, int[]> ReadEventProgress { get; set; }

        /// <summary>2026-09-01 注入: 补生成组排名表, 返回生成的人数。给"全部组已确认但表没生成"兜底。</summary>
        public Func<string, string, string, string, int> GenerateEventRanking { get; set; }

        /// <summary>
        /// 2026-09-17 注入: 单独问"这一组是否已经确认本组成绩"。参数 = (组别, 性别, 项目,
        /// 赛次, 组次)。跟 ReadEventProgress/GenerateEventRanking 问的"全项目是否都确认"
        /// 是两件不相关的事——选【第X组】打印只该看这一组自己确认没确认, 跟别的组
        /// 比没比完毫无关系; 只有选【全部】(总排名)才用得上"全部组是否都确认"那条判定。
        /// </summary>
        public Func<string, string, string, string, int, bool> IsHeatConfirmed { get; set; }

        /// <summary>
        /// 2026-09-02 注入: 填左侧赛程导航树。参数 = (树, 搜索词, 状态筛选)。
        ///
        /// 直接借用主窗口的 RebuildNavTree —— 跟"成绩与排名"那棵是【同一份代码】。
        /// 不在这里另写一棵: 赛程状态(未开始/进行中/已结束/已取消)的判定散在主窗口里,
        /// 复制一份出来就等着两边慢慢长歪。
        /// </summary>
        public Action<TreeView, string, string> BuildNavTree { get; set; }

        private string _navFilter = "all";

        /// <summary>窗口打开时填一次导航树。没注入就当没有这一栏(不影响手动选)。</summary>
        private void RefreshNavTree() {
            if (BuildNavTree == null || NavTree == null) return;
            try { BuildNavTree(NavTree, NavSearchBox != null ? NavSearchBox.Text : "", _navFilter); }
            catch { }
        }

        private void NavSearch_TextChanged(object sender, TextChangedEventArgs e) {
            if (!_initialized) return;
            RefreshNavTree();
        }

        private void NavFilter_Click(object sender, RoutedEventArgs e) {
            var b = sender as Button;
            if (b == null || b.Tag == null) return;
            _navFilter = b.Tag.ToString();
            RefreshNavTree();
        }

        /// <summary>
        /// 2026-09-02 点导航 → 上面那排下拉跟着走, 并直接查出来。
        ///   项目节点 (tag "event:组别|性别|项目|赛次")        → 组次 = 全部
        ///   组次节点 (tag "nav:组别|性别|项目|赛次|组次")      → 组次 = 第X组
        /// </summary>
        private void NavTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e) {
            var item = e.NewValue as TreeViewItem;
            if (item == null || !(item.Tag is string)) return;
            string tag = (string)item.Tag;
            string ag, gd, ev, st; int heat = 0;
            if (tag.StartsWith("event:")) {
                var p = tag.Substring(6).Split('|');
                if (p.Length < 4) return;
                ag = p[0]; gd = p[1]; ev = p[2]; st = p[3];
            } else if (tag.StartsWith("nav:")) {
                var p = tag.Substring(4).Split('|');
                if (p.Length < 5) return;
                ag = p[0]; gd = p[1]; ev = p[2]; st = p[3];
                int.TryParse(p[4], out heat);
            } else return;   // 场次节点等: 不动

            // 顺序有讲究: 改组别/性别会连带重建"项目"下拉, 改项目/赛次会重建"组次"下拉。
            // 所以先定组别/性别/赛次, 再定项目, 最后才是组次 —— 反过来会被后面的重建冲掉。
            SetCombo(AgeGroupCombo, string.IsNullOrEmpty(ag) ? "全部" : ag);
            SetCombo(GenderCombo, gd);
            SetCombo(StageCombo, st);
            SetCombo(EventCombo, ev);
            UpdateHeatCombo();
            SetCombo(HeatCombo, heat > 0 ? ("第" + heat + "组") : "全部");

            Query_Click(null, null);
        }

        /// <summary>按显示文字选中下拉项。ComboBoxItem 和纯字符串两种都认。</summary>
        private static void SetCombo(ComboBox cb, string val) {
            if (cb == null || val == null) return;
            for (int i = 0; i < cb.Items.Count; i++) {
                var it = cb.Items[i];
                string content = it is ComboBoxItem
                    ? (((ComboBoxItem)it).Content == null ? "" : ((ComboBoxItem)it).Content.ToString())
                    : (it == null ? "" : it.ToString());
                if (content == val) { cb.SelectedIndex = i; return; }
            }
        }

        // DB 路径下的总组数(库里定稿时记的), 打印表头用。0 = 没走 DB 路径。
        private int _dbTotalHeats = 0;
        // 2026-09-15 名次是不是【已定稿】(= 走的是竞赛库组排名表那条路)。
        //   屏幕上一直有"尚未定稿"的橙字提示(StatusText), 但那行字只在预览窗口里,
        //   不会印到纸上 —— 现场把这种"全部 -"或"过程值"表当场打出来发给裁判/记录长,
        //   拿着纸的人根本看不到那句提示。这个字段就是让 BuildPrintHtml 也能知道
        //   "这次要不要在纸面上印一条同样的警示", 见该函数里对它的使用。
        private bool _resultsFinalized = false;
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
            RefreshHasExplicitMixed();                 // 2026-08-22 必须在 _swimmers 赋值之后
            MainWindow.FillGenderCombo(GenderCombo);   // 2026-08-21 按比赛档案的性别表填，不再写死
            MainWindow.FillStageCombo(StageCombo);     // 2026-09-03 赛次同理，按参数设置里的赛次表填
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
            // 2026-09-02 导航树要等主窗口把 BuildNavTree 注进来之后才能填 ——
            //   那些属性是 new 完再赋的, 构造函数里还是 null。所以放到 Loaded。
            Loaded += delegate { RefreshNavTree(); };
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
            string sg = swimmerAge ?? "";
            if (sg == filterAge) return true;
            // 2026-06-04 并项组别 (e.g. "12-15岁组" ⊇ "12-13岁组" + "14-15岁组")
            int sLo, sHi, gLo, gHi;
            if (TryParseAgeRangeS(filterAge, out gLo, out gHi) && TryParseAgeRangeS(sg, out sLo, out sHi)) {
                return gLo <= sLo && sHi <= gHi;
            }
            return false;
        }

        private static bool TryParseAgeRangeS(string s, out int lo, out int hi) {
            lo = hi = 0;
            if (string.IsNullOrEmpty(s)) return false;
            // 2026-06-05 加 全角 ~ (跨年龄并项 '12~15岁组') 和 半角 ~
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*[-~~]\s*(\d+)\s*岁组");
            if (!m.Success) {
                var m2 = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*岁组");
                if (m2.Success) { int a; if (int.TryParse(m2.Groups[1].Value, out a)) { lo = hi = a; return true; } }
                return false;
            }
            return int.TryParse(m.Groups[1].Value, out lo) && int.TryParse(m.Groups[2].Value, out hi) && lo <= hi;
        }

        // 2026-06-04 男女并项: 选 男女 时 男 + 女 + 混合 都过; 其他单性别保持原 (含混合) 兼容逻辑
        // 2026-08-21 原来 fg=="男女" 时匹配 男/女/混合，唯独漏了 "男女" 本身 ——
        //   混合接力队的条目 Gender 就是 "男女"，于是筛"男女"反而一条都匹配不到，
        //   男女接力的成绩单打出来是空的。补上，并把 混合/男女 视为同义。
        // 2026-08-22 名单里有没有 Gender 就是"男女"的条目 = 本场有混合接力
        private bool _hasExplicitMixed = false;
        private void RefreshHasExplicitMixed() {
            _hasExplicitMixed = false;
            if (_swimmers == null) return;
            foreach (var s in _swimmers) {
                string g = s.Gender ?? "";
                if (g == "男女" || g == "混合") { _hasExplicitMixed = true; break; }
            }
        }

        private bool SgMatchPrint(string sg, string fg) {
            if (string.IsNullOrEmpty(fg)) return true;
            // 2026-08-22 上一轮为了让混合接力能打出来, 把这里放宽成"男女也匹配男/女",
            //   方向反了 —— 同名的男子/女子接力队会一起被打进来。
            //   改为: 名单里存在 Gender 就是"男女"的条目(= 混合接力) 时按精确比对。
            string sgN = (sg == "混合") ? "男女" : sg;
            string fgN = (fg == "混合") ? "男女" : fg;
            if (fgN == "男女") {
                if (_hasExplicitMixed) return sgN == "男女";
                return sgN == "男" || sgN == "女" || sgN == "男女";
            }
            return sgN == fgN;
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
                if (!string.IsNullOrEmpty(gender) && !SgMatchPrint(s.Gender, gender)) continue;
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
                if (!SgMatchPrint(s.Gender, gender)) continue;
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

        // ══════════════════════════════════════════════════════════════════
        // 2026-09-01 从【组排名表】直接排版
        //
        // 为什么单独一条路, 而不是继续拿库里的值去补内存:
        //   内存里根本没有那一行的时候, 补也补不出来 —— 补的前提是行存在。
        //   主服务器上"确认过的成绩查不到", 十次有九次就是这个: 库是全的、
        //   名次也定了稿, 而内存里那几行从来没建起来(回推丢了/道次对不上/
        //   离线摆渡只灌了库)。所以定稿之后就该以库为准, 内存只补库里没存的列。
        // ══════════════════════════════════════════════════════════════════
        private class DbFinalRow
        {
            public Db.EventRankRow R;
            public string AgeGroup;
            public string Gender;
            public string ReactionPlain;
            public string ReactionHtml;
        }

        /// <summary>ageFilter="全部" 时要查哪几个组别 —— 从名单里收(名单内存里一直是全的)。</summary>
        private List<string> AgeGroupsToQuery(string ageFilter, string gender, string eventName)
        {
            var ages = new List<string>();
            if (!string.IsNullOrEmpty(ageFilter) && ageFilter != "全部" && ageFilter != "不限") {
                ages.Add(ageFilter);
                return ages;
            }
            foreach (var s in _swimmers) {
                if (s.EventName != eventName) continue;
                if (!SgMatchPrint(s.Gender, gender)) continue;
                string a = s.AgeCategory ?? "";
                if (!ages.Contains(a)) ages.Add(a);
            }
            if (ages.Count == 0) ages.Add("");
            return ages;
        }

        /// <summary>成绩了了的那一行在内存里对应的是谁 —— 先认号码, 号码空了再认 组次+道次。</summary>
        private Swimmer FindSwimmer(Db.EventRankRow r, string stage, string eventName)
        {
            if (!string.IsNullOrEmpty(r.BibNumber)) {
                var byBib = _swimmers.FirstOrDefault(s => s.EventName == eventName
                                                       && (s.BibNumber ?? "") == r.BibNumber);
                if (byBib != null) return byBib;
            }
            if (r.Heat > 0 && r.Lane >= 0) {
                foreach (var s in _swimmers) {
                    if (s.EventName != eventName) continue;
                    var sa = s.GetAssignmentForStage(stage);
                    int ln = sa != null ? sa.Lane : s.Lane;
                    int ht = sa != null ? sa.Heat : s.Heat;
                    if (ln == r.Lane && ht == r.Heat) return s;
                }
            }
            return null;
        }

        /// <summary>
        /// 查到定稿数据并已经填好表 → true。库里没定稿(或没注入读库口) → false, 调用方走内存那条路。
        /// </summary>
        private bool TryQueryFromDb(string ageFilter, string gender, string eventName, string stage, int filterHeat)
        {
            if (ReadEventRankings == null) return false;

            var all = new List<DbFinalRow>();
            int maxTotalHeats = 0;
            foreach (string ag in AgeGroupsToQuery(ageFilter, gender, eventName)) {
                List<Db.EventRankRow> rows = null;
                try { rows = ReadEventRankings(ag, gender, eventName, stage); } catch { }
                if (rows == null) continue;
                foreach (var r in rows) {
                    if (r.TotalHeats > maxTotalHeats) maxTotalHeats = r.TotalHeats;
                    all.Add(new DbFinalRow { R = r, AgeGroup = ag, Gender = gender });
                }
            }
            if (all.Count == 0) return false;

            if (filterHeat > 0) {
                all = all.Where(x => x.R.Heat == filterHeat).ToList();
                // 2026-09-15 "第X组"是【本组成绩单】, 名次要跟"成绩与排名"同口径 —— 组内名次,
                //   不是项目总排名。R.Rank 存的是 event_rankings 里【跨组】的项目定稿名次
                //   (全项目统一编号), 之前这里直接拿它印, 会出现"这组全印第9名"这种跟
                //   "本组一共 6 人"完全对不上的名次(用户实拍到: 少年组女100米蛙泳第1组,
                //   5人全印"第9名")。这里改成只按【本组】的真实成绩重新算一份组内名次
                //   (按年龄组分别算, 混编组避免不同年龄组互相排到一起), 覆盖 R.Rank ——
                //   只影响这张"第X组"表, "全部"(总排名)那条分支不动, 仍然用库里的跨组名次。
                foreach (var grp in all.GroupBy(x => x.AgeGroup ?? "")) {
                    var heatValid = grp.Where(x => !ResultOrdering.IsJudged(x.R.Status) && (x.R.Status ?? "") != "TRI" && x.R.FinalTime > 0)
                                        .OrderBy(x => x.R.FinalTime).ToList();
                    var heatRanks = ResultOrdering.ComputeRanks(heatValid, x => x.R.FinalTime);
                    foreach (var x in grp) x.R.Rank = 0;
                    for (int i = 0; i < heatValid.Count; i++) heatValid[i].R.Rank = heatRanks[i];
                }
            }
            // 2026-09-01 TRI(试游)按视图决定显不显:
            //   选【第X组】= 本组成绩单 -> 显成绩 + 备注 TRI + 无名次
            //   选【全部】 = 项目总排名 -> 不显示(规则: 总排名列表中不显示 TRI)
            else all = all.Where(x => (x.R.Status ?? "") != "TRI").ToList();
            if (all.Count == 0) {
                StatusText.Text = string.Format("{0} {1} {2} 第{3}组 — 组排名表里没有这一组（组次选错了？）",
                    gender, eventName, stage, filterHeat);
                StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                PreviewGrid.ItemsSource = null;
                SetActionButtonsEnabled(false);
                return true;
            }

            // 库里没存的列(性别/反应时)回内存补。名单一直是全的, 所以基本都补得上;
            // 补不上就留空 —— 留空是"没有这一项", 比拿别的值顶上去强。
            bool isRelay = eventName.Contains("接力");
            int legCount = 4;
            if (isRelay) {
                var mLeg = System.Text.RegularExpressions.Regex.Match(eventName, @"(\d+)\s*[x×]\s*\d+");
                if (mLeg.Success) { int n; if (int.TryParse(mLeg.Groups[1].Value, out n) && n > 0 && n <= 10) legCount = n; }
            }
            foreach (var x in all) {
                var sw = FindSwimmer(x.R, stage, eventName);
                if (sw != null) {
                    if (!string.IsNullOrEmpty(sw.Gender)) x.Gender = sw.Gender;
                    if (string.IsNullOrEmpty(x.AgeGroup)) x.AgeGroup = sw.AgeCategory ?? "";
                    if (string.IsNullOrEmpty(x.R.AthleteName)) x.R.AthleteName = sw.Name ?? "";
                    // 接力项目：姓名列显示队员姓名
                    if (isRelay && !string.IsNullOrEmpty(sw.Notes) && sw.Notes.StartsWith("接力队 棒次:"))
                        x.R.AthleteName = sw.Notes.Substring("接力队 棒次:".Length);
                }
                bool judged = ResultOrdering.IsJudged(x.R.Status);
                var res = sw != null ? sw.Results.FirstOrDefault(y => y.Stage == stage && y.Heat == x.R.Heat) : null;
                if (judged) { x.ReactionPlain = ""; x.ReactionHtml = ""; }
                else if (isRelay) {
                    var parts = new List<string>();
                    for (int li = 0; li < legCount; li++) {
                        double rt = (res != null && res.LegReactionTimes != null && li < res.LegReactionTimes.Count)
                                    ? res.LegReactionTimes[li] : 0;
                        parts.Add(string.Format("第{0}棒:{1}", li + 1, (rt != 0 && !double.IsNaN(rt)) ? rt.ToString("F2") : "—"));
                    }
                    x.ReactionPlain = string.Join("  ", parts.ToArray());
                    x.ReactionHtml = BatchByAgeGroupPrintWindow.PairLines(parts)   /* 2026-09-03 两棒一行, 别把接力表撑高四倍 */;
                } else if (res != null && res.StartingBlockTime != 0 && !double.IsNaN(res.StartingBlockTime)) {
                    x.ReactionPlain = res.StartingBlockTime.ToString("F2");
                    x.ReactionHtml = x.ReactionPlain;
                } else { x.ReactionPlain = ""; x.ReactionHtml = ""; }
            }

            // 2026-09-16 排序改走 ResultOrdering.RankForTotalView(全场唯一一份"项目总排名"
            //   组装规则, 名次公告/成绩与排名选【全部】都调这一份) —— 不再在这里自己写。
            //   多个组别一起看时仍然【按组别分块】: 各组别是各自的一份总排名, 串起来按
            //   成绩排会出现两个第 1 挨在一起、看着像并列其实不是, 所以外层按组别分组,
            //   组内再调共用函数。顺带把重新算出的名次覆盖回 x.R.Rank —— 判罚的人不管
            //   库里那份定稿名次是不是干净的, 这里显示的名次都以状态说了算(同一套防御性
            //   道理, 见 RankForTotalView 自己的注释)。
            var blocked = new List<DbFinalRow>();
            foreach (var grp in all.GroupBy(x => x.AgeGroup ?? "").OrderBy(g => g.Key)) {
                List<int> ranksOut;
                var orderedGrp = ResultOrdering.RankForTotalView(grp,
                    x => x.R.Status, x => x.R.Rank, x => x.R.FinalTime, out ranksOut);
                for (int i = 0; i < orderedGrp.Count; i++) orderedGrp[i].R.Rank = ranksOut[i];
                // 2026-09-16 【别在这里再 OrderBy(Lane)】—— OrderBy 不是"追加一个次要排序键",
                //   是重新按这一个键整个排一遍, 会把上面 RankForTotalView 排好的名次顺序
                //   整个打散。真正并列(名次/状态/成绩都一样)时按道次排前后只是锦上添花,
                //   不值得为了这点锦上添花去踩"整个表按道次重排"这个大坑, 保持
                //   RankForTotalView 给出的顺序(并列内部按原有顺序, OrderBy 本身是稳定排序)。
                blocked.AddRange(orderedGrp);
            }
            all = blocked;

            // 成绩差: 跟【本组别第一名】比。库里第一名就是 rank==1 那个。
            var leader = new Dictionary<string, double>();
            foreach (var x in all) {
                string k = x.AgeGroup ?? "";
                if (x.R.Rank <= 0 || x.R.FinalTime <= 0) continue;
                double cur;
                if (!leader.TryGetValue(k, out cur) || x.R.FinalTime < cur) leader[k] = x.R.FinalTime;
            }

            // 总组数优先用定稿时记在表里的那个(total_heats); 老库里没有就问一次库的进度。
            // 【不能】退回"当前看得见几组" —— 选了第2组时那是 1, 会印成 "2/1"。
            _dbTotalHeats = maxTotalHeats > 0
                ? maxTotalHeats
                : TotalHeatsOfEvent(ageFilter, gender, eventName, stage, null);

            _currentResults = new List<object>();
            foreach (var x in all) {
                bool judged = ResultOrdering.IsJudged(x.R.Status);
                string diff = "";
                double lead;
                if (!judged && x.R.FinalTime > 0 && x.R.Rank > 0
                    && leader.TryGetValue(x.AgeGroup ?? "", out lead) && x.R.FinalTime > lead)
                    diff = (x.R.FinalTime - lead).ToString("F2");

                // 备注: 库里那一列已经是同口径了(判罚 → 晋级 Q/R → 纪录)。这里只上色。
                string remarkPlain = x.R.Remark ?? "";
                string remarkHtml = "";
                if (remarkPlain.Length > 0) {
                    if (ResultOrdering.IsJudged(remarkPlain) || remarkPlain == "TRI")
                        remarkHtml = "<span style='color:#dc2626;'>" + remarkPlain + "</span>";
                    else if (remarkPlain == "Q" || remarkPlain == "R")
                        remarkHtml = "<span style='color:#16a34a;font-weight:bold;'>" + remarkPlain + "</span>";
                    else
                        remarkHtml = remarkPlain;
                }

                _currentResults.Add(new {
                    Rank = x.R.Rank > 0 ? x.R.Rank.ToString() : "-",
                    AgeGroup = x.AgeGroup ?? "",
                    HeatText = x.R.Heat > 0
                        ? (_dbTotalHeats > 0 ? x.R.Heat + "/" + _dbTotalHeats : x.R.Heat.ToString())
                        : "",
                    Lane = x.R.Lane,
                    BibNumber = x.R.BibNumber ?? "",
                    Name = x.R.AthleteName ?? "",
                    Country = x.R.UnitName ?? "",
                    Gender = x.Gender ?? "",
                    FinalTime = (judged || x.R.FinalTime <= 0) ? "" : TimeFormatter.Format(x.R.FinalTime),
                    Diff = diff,
                    ReactionTime = x.ReactionPlain ?? "",
                    ReactionTimeHtml = x.ReactionHtml ?? "",
                    Remark = remarkPlain,
                    RemarkHtml = remarkHtml
                });
            }

            ApplyPreviewColumnOrder(eventName.Contains("接力"));   // 接力: 代表队在姓名前
            PreviewGrid.ItemsSource = _currentResults;
            SetActionButtonsEnabled(true);
            SelectedGender = gender;
            SelectedEvent = eventName;
            SelectedStage = stage;
            SelectedAgeGroup = ageFilter;
            SelectedHeat = filterHeat;
            _resultsFinalized = true;

            string ageHead2 = (string.IsNullOrEmpty(ageFilter) || ageFilter == "全部") ? "" : (ageFilter + " ");
            StatusText.Text = string.Format("{0}{1} {2} {3}{4} — 共{5}人（★ 取自竞赛库【组排名表】, 已定稿）",
                ageHead2, gender, eventName, stage,
                filterHeat > 0 ? " 第" + filterHeat + "组" : " 总排名", all.Count);
            StatusText.Foreground = System.Windows.Media.Brushes.Green;
            return true;
        }

        /// <summary>
        /// 2026-09-03 本项目这个赛次【一共几组】—— 组数列 "第X组/总组数" 的分母。
        ///
        /// 必须是"本项目一共几组", 不是"当前筛选看得见几组": 选了"第2组"时看得见的
        /// 只有 1 组, 分母就成了 1, 印出来是 "2/1"。
        ///
        /// 先问竞赛库(最准 —— 取消的组不算), 库里问不到再从名单里数不重复的组次,
        /// 数的是【按组次筛掉之前】那一份。
        /// </summary>
        private int TotalHeatsOfEvent(string ageFilter, string gender, string eventName, string stage,
                                      List<Swimmer> beforeHeatFilter) {
            if (ReadEventProgress != null) {
                int best = 0;
                foreach (string ag in AgeGroupsToQuery(ageFilter, gender, eventName)) {
                    int[] p = null;
                    try { p = ReadEventProgress(ag, gender, eventName, stage); } catch { }
                    if (p != null && p.Length > 0 && p[0] > best) best = p[0];
                }
                if (best > 0) return best;
            }
            var hs = new HashSet<int>();
            if (beforeHeatFilter != null) {
                foreach (var s in beforeHeatFilter) {
                    var r = s.GetResultForStage(stage);
                    if (r != null && r.Heat > 0) { hs.Add(r.Heat); continue; }
                    var sa = s.GetAssignmentForStage(stage);
                    if (sa != null && sa.Heat > 0) hs.Add(sa.Heat);
                }
            }
            return hs.Count;
        }

        /// <summary>2026-09-01 一句话说清【库】那边到什么程度了: 总共几组、还差几组没确认、有几个人有名次。</summary>
        private string DbProgressLine(string ageFilter, string gender, string eventName, string stage)
        {
            if (ReadEventProgress == null) return "";
            int total = 0, pending = 0, ranked = 0, found = 0;
            foreach (string ag in AgeGroupsToQuery(ageFilter, gender, eventName)) {
                int[] p = null;
                try { p = ReadEventProgress(ag, gender, eventName, stage); } catch { }
                if (p == null || p.Length < 3 || p[0] < 0) continue;
                found++; total += p[0]; pending += p[1]; ranked += p[2];
            }
            if (found == 0)
                return "竞赛库里找不到这个项目/赛次（组别或赛次选错了？还是这台机器的竞赛库根本没打开——看系统日志）";
            if (pending > 0)
                return string.Format("竞赛库: 共 {0} 组, 还有 {1} 组没确认到本机 → 组排名表(定稿)还不能生成; 已有名次 {2} 人",
                    total, pending, ranked);
            return string.Format("竞赛库: 共 {0} 组【全部已确认】, 已有名次 {1} 人 —— 但组排名表还没生成",
                total, ranked);
        }

        /// <summary>
        /// 2026-09-01 全部组已确认、库里也有名次, 只差那张组排名表 —— 当场补生成。
        ///
        /// 什么时候会出现这种局面: 计时端确认最后一组那一刻正好断线(通知没送到),
        /// 或者成绩是 U 盘导进来的。库里数据是齐的, 只是没人喊那一声"定稿"。
        /// 生成本身是定稿动作, 所以仍然要人点头 —— 但至少现在点得着。
        /// 真生成出来了返回 true。
        /// </summary>
        private bool OfferGenerateIfComplete(string ageFilter, string gender, string eventName, string stage)
        {
            if (GenerateEventRanking == null || ReadEventProgress == null) return false;
            var ready = new List<string>();
            foreach (string ag in AgeGroupsToQuery(ageFilter, gender, eventName)) {
                int[] p = null;
                try { p = ReadEventProgress(ag, gender, eventName, stage); } catch { }
                if (p == null || p.Length < 3) continue;
                if (p[0] > 0 && p[1] == 0 && p[2] > 0) ready.Add(ag);
            }
            if (ready.Count == 0) return false;

            var r = MessageBox.Show(
                string.Format("【{0} {1} {2}】在竞赛库里所有组都已确认，名次也算好了，\n"
                            + "只是还没有生成【组成绩（本项目所有组的总排名）】。\n\n"
                            + "它是晋级的依据；直接决赛的项目，它就是最终名次。\n"
                            + "多半是计时端确认最后一组时正好断线，通知没送到。\n\n"
                            + "现在生成吗？", gender, eventName, stage),
                "补生成组成绩", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return false;

            int made = 0;
            foreach (string ag in ready) {
                try { made += GenerateEventRanking(ag, gender, eventName, stage); } catch { }
            }
            if (made <= 0) {
                MessageBox.Show("没有生成出来。请到【系统日志与数据】页看一眼原因。",
                    "补生成组成绩", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
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

            // 2026-08-31 【先从竞赛库刷新, 再查】。
            //   成绩可能是别的计算机(计时端/另一台控制台)写进库的, 本机内存不会自动知道;
            //   不刷就会出现"库里是新的、界面上是旧的", 而且不报错。
            //   刷新失败不阻断查询(下面照样用内存里的), 但主窗口日志里会有一条【注意】。
            if (RefreshFromDb != null) {
                try { RefreshFromDb(ageFilter == "全部" ? "" : ageFilter, gender, eventName, stage); }
                catch { }
            }

            // 2026-09-01 【先查库里的组排名表】。定稿了就直接按它排版, 一行都不用内存去凑。
            //   查不到再走下面的内存路径 —— 那条路只对"还没定稿"有意义。
            _dbTotalHeats = 0;
            _resultsFinalized = false;
            if (TryQueryFromDb(ageFilter, gender, eventName, stage, filterHeat)) return;

            // 2026-06-01 加 AgeGroup 过滤; 男/女 也包含混合性别接力
            var matched = _swimmers.Where(s =>
                SgMatchPrint(s.Gender, gender) &&
                s.EventName == eventName &&
                MatchesAge(s.AgeCategory, ageFilter) &&
                s.GetResultForStage(stage) != null
            ).ToList();

            // 2026-09-03 总组数要在【按组次筛掉之前】数 —— 见 TotalHeatsOfEvent 的说明
            var matchedAllHeats = matched;
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
                string st2 = (r != null && !string.IsNullOrEmpty(r.Status)) ? r.Status
                           : (!string.IsNullOrEmpty(s.Status) ? s.Status : "");
                // 2026-09-01 TRI(试游)按视图决定显不显:
                //   选【第X组】= 本组成绩单 -> 要显(成绩 + 备注 TRI + 无名次)
                //   选【全部】 = 项目总排名 -> 不显(spec: '总排名列表中不显示 TRI')
                if (filterHeat <= 0 && st2 == "TRI") return false;
                // 2026-09-16 判罚(DSQ/DNF/DNS)本来就没有成绩(FinalTime=0 是正常状态), 不能
                //   要求"有成绩"才显示 —— 这条筛选原来对所有状态一视同仁地要求 FinalTime>0,
                //   判罚的人天生过不了这一关, 整行从这条【内存回退路径】的名单里消失
                //   (用户实拍到: "裁判长改成绩"把某道标成 DNS 后, "成绩与排名"正常显示 DNS,
                //   但"项目成绩打印"这一道整行不见了 —— DB 那条路径(TryQueryFromDb, 走
                //   event_rankings)本来就没有这条限制, 只有这条内存回退路径独漏了判罚状态)。
                //   跟"备注"栏有没有另外填字没关系, 这里认的是"状态"栏。
                if (st2 == "DSQ" || st2 == "DQ" || st2 == "DNF" || st2 == "DNS") return true;
                return r != null && r.FinalTime > 0;
            }).ToList();

            if (withResults.Count == 0)
            {
                // 2026-08-30 原来只说"暂无比赛成绩", 到底是项目选错了、性别/组别筛掉了,
                //   还是成绩确实没进来, 一点线索都没有 —— 计时端明明确认过, 这里却说没有,
                //   谁也查不动。现在逐级报数, 断在哪一层一眼就看出来。
                int cEvent = _swimmers.Count(s => s.EventName == eventName);
                int cGender = _swimmers.Count(s => s.EventName == eventName && SgMatchPrint(s.Gender, gender));
                int cAge = _swimmers.Count(s => s.EventName == eventName && SgMatchPrint(s.Gender, gender)
                                                && MatchesAge(s.AgeCategory, ageFilter));
                int cStage = _swimmers.Count(s => s.EventName == eventName && SgMatchPrint(s.Gender, gender)
                                                && MatchesAge(s.AgeCategory, ageFilter)
                                                && s.GetResultForStage(stage) != null);
                int cHeat = matched.Count;
                string chain = string.Format("本项目{0}人 → 性别{1} → 组别{2} → 有[{3}]成绩行{4}{5} → 有效成绩0",
                    cEvent, cGender, cAge, stage, cStage,
                    filterHeat > 0 ? " → 第" + filterHeat + "组" + cHeat : "");

                // 断在最后一层最值得说: 成绩行有, 但成绩是 0 / 全是判罚弃权
                string hint = "";
                if (cStage > 0) {
                    int cTri = matched.Count(s => { var r = s.GetResultForStage(stage);
                                                   return s.Status == "TRI" || (r != null && r.Status == "TRI"); });
                    int cZero = matched.Count(s => { var r = s.GetResultForStage(stage);
                                                    return r != null && r.FinalTime <= 0; });
                    if (cTri > 0) hint = "（其中 " + cTri + " 人是试游 TRI，不进项目成绩表）";
                    else if (cZero > 0) hint = "（有成绩行但成绩为 0：多半是判罚/弃权，或成绩没真正回写）";
                } else if (cAge > 0) {
                    hint = "（这些人没有[" + stage + "]的成绩行：赛次选错了？还是这一组的成绩没同步过来？）";
                } else if (cEvent > 0) {
                    hint = "（被性别/组别筛掉了：确认上面的性别和组别选对了）";
                }

                // 2026-09-01 再报一层【库】的情况 —— 上面那条链子说的全是内存。
                //   真正的场面是: 库里成绩齐全、名次也在, 就是没生成组排名表(定稿),
                //   而内存这边一行都没有。只看内存的链子会把人往"成绩没同步"上带。
                string dbLine = DbProgressLine(ageFilter, gender, eventName, stage);

                StatusText.Text = string.Format("{0} {1} {2}{3} — 暂无比赛成绩，无法打印\n{4} {5}{6}",
                    gender, eventName, stage, filterHeat > 0 ? " 第" + filterHeat + "组" : "", chain, hint,
                    dbLine.Length > 0 ? "\n" + dbLine : "");
                StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                PreviewGrid.ItemsSource = null;
                SetActionButtonsEnabled(false);
                // 全部组都确认了、库里也有名次, 却没有组排名表 —— 当场补生成一次就好了。
                if (OfferGenerateIfComplete(ageFilter, gender, eventName, stage)) {
                    Query_Click(sender, e);   // 生成完再查一遍, 这次就该走库那条路了
                }
                return;
            }

            // 2026-09-17 【内存里有数据不代表已经无法定稿】——上面只有"内存也一行没有"
            //   (暂无比赛成绩)这一条路会主动提出"当场补生成"; 内存里凑得出过程值表时,
            //   看着"总归能打印"，这条路原来什么都不做，那条"尚未定稿"的橙字警示就
            //   可能永远挂在纸上。用户实拍到的正是这个场景: 一个刚比完、名次/DNF/DSQ
            //   全部正常显示的接力项目，打印永远带着"尚未定稿"——多半是计时端确认
            //   最后一组那一刻生成组成绩的对话框被跳过/答了否，或者压根没人点过。
            //   现在跟"暂无成绩"那条路一个待遇: 只要满足"全部组已确认、库里已有
            //   名次"，主动问一句要不要当场补生成；生成成功就重新查一遍，这次会走
            //   event_rankings 那条真正的定稿路径，警示自然消失。
            if (!_resultsFinalized && OfferGenerateIfComplete(ageFilter, gender, eventName, stage)) {
                Query_Click(sender, e);
                return;
            }

            // 2026-09-17 【选"第X组"打印, 只该看这一组自己确认没确认】——跟"生成组成绩/
            //   总排名"要求的"全项目是否都确认"是两件不相关的事, 不能拿后者卡前者。
            //   用户明确指出: 按过"确认本组成绩"就该能打印这一组, 跟别的组比没比完
            //   毫无关系; "尚未定稿"那句警示只该在选【全部】(总排名)、且项目没全部
            //   确认时才出现。这里单独问一次这一组的确认状态, 问到了就不再当"过程值"。
            bool singleHeatFinalized = filterHeat > 0 && IsHeatConfirmed != null
                && IsHeatConfirmed(ageFilter, gender, eventName, stage, filterHeat);

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
                // 2026-06-18 按 ageGroup 过滤, 修跨年龄段污染 (甲组无半决但乙组有时, 甲组误判)
                bool hasSemi = _schedule != null && _schedule.Any(s =>
                    s.Gender == gender && s.EventName == eventName && s.Stage == "半决赛"
                    && (s.AgeGroup ?? "") == (ageFilter == "全部" ? (s.AgeGroup ?? "") : ageFilter));
                nextStageQ = hasSemi ? "半决赛" : "决赛";
            } else if (stage == "半决赛") {
                nextStageQ = "决赛";
            }

            var displayData = withResults.Select(s =>
            {
                var r = s.GetResultForStage(stage);
                string remark = "";
                if (r != null && !string.IsNullOrEmpty(r.Status)) remark = r.Status;
                else if (!string.IsNullOrEmpty(s.Status) && (s.Status == "DNS" || s.Status == "DNF" || s.Status == "DSQ" || s.Status == "DQ" || s.Status == "TRI")) remark = s.Status;
                // 2026-09-01 TRI 跟判罚不是一回事: 试游【照显成绩】, 只是不排名、不算成绩差。
                //   原来 remark 一非空就当判罚, TRI 的成绩会被抹掉。
                bool isTri = remark == "TRI";
                bool isDQ = !isTri && !string.IsNullOrEmpty(remark);
                // 接力项目：Name显示队员姓名
                string epName = s.Name ?? "";
                if (stage.Length > 0 && eventName.Contains("接力") && !string.IsNullOrEmpty(s.Notes) && s.Notes.StartsWith("接力队 棒次:"))
                    epName = s.Notes.Substring("接力队 棒次:".Length);
                // 反应时：接力赛展开为 N 棒（"第N棒:0.45"），未记录到的棒显示"—"；个人赛仍是单值
                // 预览（DataGrid TextWrapping=Wrap）用空格分隔以便在窄列里按词换行；打印 HTML 用 <br>
                string reactionPlain = "", reactionHtml = "";
                // 2026-09-03 接力这一支也补上判罚守卫。下面输出时虽然还有一道 isDQ 兜着,
                //   但这一处本身就不该算出来 —— 五处接力分支里有四处只有一道防线, 都漏过。
                if (isRelay && !isDQ) {
                    var parts = new List<string>();
                    for (int li = 0; li < legCount; li++) {
                        double rt = (r != null && r.LegReactionTimes != null && li < r.LegReactionTimes.Count) ? r.LegReactionTimes[li] : 0;
                        parts.Add(string.Format("第{0}棒:{1}", li + 1, (rt != 0 && !double.IsNaN(rt)) ? rt.ToString("F2") : "—"));
                    }
                    reactionPlain = string.Join("  ", parts.ToArray());
                    reactionHtml = BatchByAgeGroupPrintWindow.PairLines(parts)   /* 2026-09-03 两棒一行, 别把接力表撑高四倍 */;
                } else if (r != null && r.StartingBlockTime != 0 && !double.IsNaN(r.StartingBlockTime)) {
                    reactionPlain = r.StartingBlockTime.ToString("F2");
                    reactionHtml = reactionPlain;
                }
                // 是否晋级到下一赛次：检查该运动员是否已被分配到 nextStageQ 的组次
                bool qualified = !isDQ && !string.IsNullOrEmpty(nextStageQ) && s.GetAssignmentForStage(nextStageQ) != null;
                return new
                {
                    // TRI 跟判罚一样排到有名次的人后面, 也不参与"成绩差"的基准和计算
                    SortTime = (isDQ || isTri) ? double.MaxValue : r.FinalTime,
                    RawFinalTime = (isDQ || isTri) ? 0 : r.FinalTime,
                    IsDQ = isDQ,
                    IsTri = isTri,
                    Lane = r.Lane,
                    BibNumber = s.BibNumber ?? "",
                    Name = epName,
                    Country = s.Country ?? "",
                    Gender = s.Gender ?? "",   // 2026-06-04 男女并项: 排名/输出 用
                    FinalTime = isDQ ? "" : (r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : ""),
                    // 2026-08-31 判罚/弃权(DSQ/DNS/DNF)不显示反应时间。
                    //   他没成绩、不排名, 却单单留一个反应时在那里, 表格上很怪, 也容易被
                    //   误当成有效数据。跟"成绩留空"同一个道理。
                    ReactionTime = isDQ ? "" : reactionPlain,        // DataGrid 预览用
                    ReactionTimeHtml = isDQ ? "" : reactionHtml,     // 打印 HTML 用
                    // 2026-08-31 表格补上组别和组次 —— 一张项目成绩单跨多个组时,
                    //   原来看不出某一行是第几组的, 也看不出这个人属于哪个组别。
                    // 2026-09-01 名次【从库里读】: EventRankFor 取的是确认成绩时竞赛库算好、
                    //   回读进内存的名次(项目定稿后是 event_rankings 的值)。这里不算。
                    // 2026-09-17 【单独确认的这一组是个例外】——EventRankFor 故意"只认
                    //   event_rankings"(全项目定稿后才有), 项目没全部确认时一律返回 0,
                    //   显示"-"。可这一组自己已经确认过了, r.Rank(组内名次, 确认那一刻
                    //   就由竞赛库回读进内存, 见 LaneResult.Rank 的注释)是真实有效的——
                    //   只看这一组、没有别的组混进来对比, 不会重蹈"多组各自的第1名全部
                    //   显示第1"那个坑(那是【总排名】视图混了多组才会出的问题)。
                    DbRank = singleHeatFinalized ? r.Rank : s.EventRankFor(stage),
                    AgeGroup = s.AgeCategory ?? "",
                    HeatNo = r.Heat,
                    Remark = remark,
                    Qualified = qualified
                };
            // 2026-06-04 男女并项: 先按 性别 (男前女后) 再按 时间
            }).OrderBy(x => gender == "男女" ? (x.Gender == "女" ? 1 : 0) : 0).ThenBy(x => x.SortTime).ToList();

            // 2026-06-04 男女并项: 按性别分两个 leader + 各自 rank
            // 2026-08-21 只有"男女并项"(条目本身是 男 或 女、同场比分开排名) 才拆；
            //   混合接力的条目 Gender 本身就是"男女"，拆出来两边都是 0 条 → 打印空白。
            bool isMixed = (gender == "男女")
                        && displayData.Any(d => (string)d.Gender == "男" || (string)d.Gender == "女");
            var leaderMap = new Dictionary<string, double>();
            if (isMixed) {
                foreach (var gKey in new[] { "男", "女" }) {
                    foreach (var d in displayData) {
                        if (d.Gender == gKey && !d.IsDQ && d.RawFinalTime > 0) { leaderMap[gKey] = d.RawFinalTime; break; }
                    }
                }
            } else {
                foreach (var d in displayData) {
                    if (!d.IsDQ && d.RawFinalTime > 0) { leaderMap["_all"] = d.RawFinalTime; break; }
                }
            }

            _currentResults = new List<object>();
            // 2026-08-30 原来名次是一个自增计数器 —— 那是【行号】不是名次:
            //   成绩相同的两个人会被印成 1 和 2, 而不是并列第 1。
            //   现在名次一律从竞赛库读(EventRankFor), 与库里、
            //   与成绩单、与大屏同一口径。displayData 已按成绩排好序。
            // 2026-09-03 组数列是"第几组/【本项目总组数】"。
            //   原来拿的是"本次查询范围内出现过的组次数" —— 选了"第2组"时范围里就 1 组,
            //   于是印成 "2/1"。用户实拍到过, 应该是 "2/6"。
            int totalHeatsForView = TotalHeatsOfEvent(ageFilter, gender, eventName, stage, matchedAllHeats);
            // 2026-09-17 【标题里的"第 X 组"没印出来, 病根就在这一行没写】——BuildPrintHtml
            //   算 showHeat 时用的是 _dbTotalHeats(只有 TryQueryFromDb 那条路才会赋值),
            //   这条"process value / 单组已确认"路径原来一直没写它, 打印时 _dbTotalHeats
            //   还停在 Query_Click 开头 reset 的 0, BuildPrintHtml 只好退回内存里现数一遍
            //   ——数出来的跟这里 totalHeatsForView(走 ReadEventProgress, 已经是联机安全
            //   的那份)不是同一个来源, 数据表格上"组数"列(1/3)是对的, 标题却当成"只有
            //   1 组"而不显示"第 1 组", 用户看着表格有"1/3"、标题却没有组号, 显然对不上。
            //   两处改成同一个数, 标题和表格才能说一句话。
            _dbTotalHeats = totalHeatsForView;

            // 2026-09-01 名次一律读库里的, 这里不再算 —— 现算就会和成绩与排名、大屏
            //   各算各的, 同一份成绩三个答案(用户实拍到过)。库里没有就显示 "-"。
            var rankArr = new int[displayData.Count];
            for (int i = 0; i < displayData.Count; i++)
                rankArr[i] = displayData[i].IsDQ ? 0 : displayData[i].DbRank;
            for (int di = 0; di < displayData.Count; di++)
            {
                var item = displayData[di];
                string rkKey = isMixed ? item.Gender : "_all";
                double leaderTime; leaderMap.TryGetValue(rkKey, out leaderTime);
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
                int curRank = rankArr[di];
                _currentResults.Add(new
                {
                    Rank = (item.IsDQ || curRank <= 0) ? "-" : curRank.ToString(),
                    item.AgeGroup,
                    HeatText = item.HeatNo > 0
                        ? (totalHeatsForView > 0 ? item.HeatNo + "/" + totalHeatsForView : item.HeatNo.ToString())
                        : "",
                    item.Lane,
                    item.BibNumber,
                    item.Name,
                    item.Country,
                    item.Gender,
                    item.FinalTime,
                    Diff = diffText,
                    item.ReactionTime,
                    item.ReactionTimeHtml,
                    Remark = remarkPlain,
                    RemarkHtml = remarkHtml
                });
            }

            ApplyPreviewColumnOrder(eventName.Contains("接力"));   // 接力: 代表队在姓名前
            PreviewGrid.ItemsSource = _currentResults;
            SetActionButtonsEnabled(true);

            SelectedGender = gender;
            SelectedEvent = eventName;
            SelectedStage = stage;
            SelectedAgeGroup = ageFilter;
            SelectedHeat = filterHeat;

            string ageHead = (string.IsNullOrEmpty(ageFilter) || ageFilter == "全部") ? "" : (ageFilter + " ");
            string heatDesc = filterHeat > 0 ? " 第" + filterHeat + "组" : " 总排名";
            if (singleHeatFinalized) {
                // 2026-09-17 这一组已经确认过成绩(不管别的组比没比完), 不再当"过程值"。
                _resultsFinalized = true;
                StatusText.Text = string.Format("{0}{1} {2} {3}{4} — 共{5}人有成绩（✓ 本组已确认, 名次以本组为准）",
                    ageHead, gender, eventName, stage, heatDesc, withResults.Count);
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                return;
            }
            // 2026-09-01 走到这里就说明【库里还没有组排名表】—— 下面这些名次都是过程值,
            //   项目全部比完定稿之后还会变。必须说出来, 别让人拿过程名次当最终名次去发奖。
            string dbNote = DbProgressLine(ageFilter, gender, eventName, stage);
            StatusText.Text = string.Format("{0}{1} {2} {3}{4} — 共{5}人有成绩\n【尚未定稿】名次是过程值, 本项目全部比完并生成组成绩后才算最终名次{6}",
                ageHead, gender, eventName, stage, heatDesc, withResults.Count,
                dbNote.Length > 0 ? "\n" + dbNote : "");
            StatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
        }

        /// <summary>
        /// 2026-09-02 预览表格的列序: 单项是【姓名 → 代表队】, 接力是【代表队 → 姓名】。
        /// 打印出来的 HTML 一直是按 epH1/epH2 这么排的, 预览这边原来写死在 XAML 里,
        /// 于是接力时预览和打印两个样子。DisplayIndex 换一下就够, 不用重建列。
        /// </summary>
        private void ApplyPreviewColumnOrder(bool relay) {
            try {
                if (NameCol == null || TeamCol == null) return;
                // 赋值会自动把另一列挤开, 所以只需要指定谁排在前面那一格
                if (relay) TeamCol.DisplayIndex = 1;
                else NameCol.DisplayIndex = 1;
            } catch { }
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
        /// <summary>
        /// 2026-09-03 11 列的百分比列宽(配 table-layout:fixed)。跟"按组别批量公布"用同一套数字。
        /// 原来是 px 宽度: 表格挤在纸的左半边, 人少的时候更窄 —— 打出来很难看。
        /// </summary>
        private static string ColGroupHtml(bool relay) {
            int[] w = relay ? new[] { 5, 15, 21, 6, 8, 6, 5, 10, 7, 12, 5 }
                            : new[] { 5, 18, 14, 7, 9, 7, 6, 11, 8, 9, 6 };
            var sb = new StringBuilder("<colgroup>");
            foreach (int x in w) sb.AppendFormat("<col style='width:{0}%'/>", x);
            sb.Append("</colgroup>");
            return sb.ToString();
        }

        private string BuildPrintHtml(out string suggestedFileName) {
            suggestedFileName = "";
            if (_currentResults.Count == 0) return "";

            // 组号显示逻辑：决赛只有1组时不显示，预赛/半决赛即使1组也显示
            // 2026-06-01 totalHeats 也要带 AgeGroup 过滤, 不然跨组别会把别人的 Heat 也算进来
            // 2026-09-01 走库那条路时用库里定稿记下的总组数 —— 内存里可能一行成绩都没有,
            //   拿它数出来是 0, 表头上的"第 X 组"就没了。
            int totalHeats = _dbTotalHeats > 0 ? _dbTotalHeats : _swimmers.Where(s =>
                SgMatchPrint(s.Gender, SelectedGender) &&
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
            // 2026-09-03 排版与"按组别批量公布"统一 —— 两份文档会摆在一起看,
            //   字号/配色/边距各是一套很难看。原来这份 h1 36px、h2 下面留 50px 空,
            //   一张 8 人的成绩单要占掉大半页纸。
            sb.Append("<html><head><meta charset='UTF-8'><style>");
            // 2026-09-16 页码。跟秩序册/成绩册那份 DocCss() 是同一个 CSS Paged Media 写法,
            //   但这份文档是独立类(不是 MainWindow 的私有方法能直接调), 只能各写一份。
            sb.Append("@page{ size:A4; margin:14mm 12mm; @bottom-center { content: '第 ' counter(page) ' 页  共 ' counter(pages) ' 页'; font-size:9px; color:#64748b; font-family:SimSun; } } ");
            sb.Append("body{font-family:'Microsoft YaHei','微软雅黑',SimHei,SimSun,sans-serif; padding:0; margin:0; line-height:1.45; color:#1f2937;} ");
            sb.Append(".page{padding:0 4px; box-sizing:border-box;} ");
            sb.Append("h1{text-align:center; font-size:26px; margin:0 0 4px; letter-spacing:3px; color:#0f172a;} ");
            sb.Append("h2{text-align:center; font-size:17px; margin:0 0 10px; letter-spacing:6px; color:#1e40af; font-weight:normal;} ");
            sb.Append("h3{font-size:15px; margin:12px 0 5px; padding:5px 10px; color:#1e3a8a;"
                    + " background:#e8f0fe; border-left:4px solid #1e40af;} ");
            sb.Append(".meta{text-align:center; font-size:12px; color:#475569; margin:0 0 4px;} ");
            sb.Append(".rule{height:2px; background:#1e40af; margin:8px 0 4px;} ");
            sb.Append("table{border-collapse:collapse; width:100%; table-layout:fixed; margin:0; background:#fff;} ");
            sb.Append("th{border:1px solid #94a3b8; background:#1e40af; color:#fff; padding:5px 3px;"
                    + " font-weight:bold; font-size:12px; text-align:center; vertical-align:middle;} ");
            sb.Append("td{border:1px solid #cbd5e1; padding:4px 3px; text-align:center; font-size:12px;"
                    + " vertical-align:middle; word-break:break-all;} ");
            sb.Append("tbody tr:nth-child(even){background:#f4f7fd;} ");
            sb.Append("h4{font-size:13px; margin:12px 0 5px; padding:4px 8px; color:#1e3a8a;"
                    + " background:#e8f0fe; border-left:4px solid #1e40af;} ");
            sb.Append(".nm{font-weight:bold;} ");
            sb.Append(".tm{font-weight:bold; font-family:Consolas,'Courier New',monospace; background:#eff6ff;} ");
            sb.Append(".r1{background:#fde68a; font-weight:bold;} .r2{background:#e5e7eb; font-weight:bold;} .r3{background:#fed7aa; font-weight:bold;} ");
            sb.Append(".rt{font-size:11px; color:#475569;} ");
            sb.Append(".signature-row{margin-top:26px; display:flex; justify-content:space-between; font-size:13px;} ");
            sb.Append("@media print { .page-break{page-break-before:always;} body{-webkit-print-color-adjust:exact; print-color-adjust:exact;} } ");
            sb.Append("</style></head><body>");

            // 正文
            sb.Append("<div class='page'>");
            sb.AppendFormat("<h1>{0}</h1>", _competitionName);
            sb.Append("<h2>成 绩 单</h2>");
            sb.AppendFormat("<div class='meta'>{0}</div>", eventTitle);
            // 2026-09-03 地点没填就不印"地点：——"
            sb.AppendFormat("<div class='meta'>比赛时间：{0}{1}</div>",
                dateTimeInfo,
                string.IsNullOrWhiteSpace(_location) ? "" : ("　|　地点：" + _location));
            // 2026-09-15 尚未定稿(竞赛库还没有组排名表)时, 预览窗口的 StatusText 一直有
            //   橙字提示, 但那句话只在屏幕上 —— 现场直接把这张表打出来发给裁判/记录长,
            //   拿着纸的人看不到那句话, 容易把过程名次当成最终名次。这里把同一句话
            //   印到纸上, 跟屏幕上说的保持一致。
            if (!_resultsFinalized) {
                sb.Append("<div style='margin:2px 0 8px;padding:6px 10px;border:1.5px solid #ea580c;"
                        + "background:#fff7ed;color:#c2410c;font-size:12px;font-weight:bold;text-align:center;'>"
                        + "【尚未定稿】本项目还没有全部组确认成绩、或未生成组排名表 —— 以下名次仅为过程值，"
                        + "不是最终名次，不能据此发奖/公告</div>");
            }
            sb.Append("<div class='rule'></div>");

            // 成绩表（接力：代表队在前）
            bool epRelay = SelectedEvent.Contains("接力");
            string epH1 = epRelay ? "代表队" : "姓名";
            string epH2 = epRelay ? "姓名" : "代表队";
            int reactionWidth = epRelay ? 110 : 70;

            // 2026-06-04 男女并项: 拆 男 / 女 两张子表
            // 2026-08-21 同上：混合接力(条目 Gender 就是"男女")不拆，否则两张子表都是空的
            bool printMixed = (SelectedGender == "男女")
                           && _currentResults.Cast<dynamic>().Any(it => (string)it.Gender == "男" || (string)it.Gender == "女");
            if (printMixed) {
                foreach (var gKey in new[] { "男", "女" }) {
                    var subResults = new List<dynamic>();
                    foreach (dynamic it in _currentResults) {
                        if ((string)it.Gender == gKey) subResults.Add(it);
                    }
                    if (subResults.Count == 0) continue;
                    string bg = (gKey == "男") ? "#dbeafe" : "#fce7f3";
                    string brd = (gKey == "男") ? "#2563eb" : "#ec4899";
                    sb.AppendFormat("<h4 style='background:{0};border-left:5px solid {1};padding:8px 12px;'>{2} 子</h4>", bg, brd, gKey);
                    sb.Append("<table><tr>");
                    // 2026-09-02 列序: 名次 → 姓名/代表队 → 号码 → 组别 → 组数 → 道 → 最终成绩 …
                    //   (接力时 epH1/epH2 已经是"代表队/姓名", 顺序自动对调)
                    // 2026-09-03 列宽改百分比 + table-layout:fixed, 让表格稳定占满纸宽
                    sb.Append(ColGroupHtml(epRelay));
                    sb.Append("<thead><tr>");
                    sb.AppendFormat("<th>名次</th>");
                    sb.AppendFormat("<th>{0}</th><th>{1}</th>", epH1, epH2);
                    sb.Append("<th>号码</th><th>组别</th><th>组数</th><th>道次</th>");
                    sb.Append("<th>最终成绩</th><th>成绩差</th><th>反应时间</th><th>备注</th>");
                    sb.Append("</tr></thead><tbody>");
                    foreach (dynamic item in subResults) {
                        string c1 = epRelay ? item.Country : item.Name;
                        string c2 = epRelay ? item.Name : item.Country;
                        string rk = (string)item.Rank;
                        string rkCls = rk == "1" ? " class='r1'" : rk == "2" ? " class='r2'" : rk == "3" ? " class='r3'" : "";
                        sb.Append("<tr>");
                        sb.AppendFormat("<td{0}>{1}</td>", rkCls, rk);
                        sb.AppendFormat("<td class='nm'>{0}</td><td>{1}</td>", c1, c2);
                        sb.AppendFormat("<td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td>", item.BibNumber, item.AgeGroup, item.HeatText, item.Lane);
                        sb.AppendFormat("<td class='tm'>{0}</td>", item.FinalTime);
                        sb.AppendFormat("<td>{0}</td>", item.Diff);
                        sb.AppendFormat("<td class='rt'>{0}</td><td>{1}</td>", item.ReactionTimeHtml, item.RemarkHtml);
                        sb.Append("</tr>");
                    }
                    sb.Append("</tbody>");
                    sb.Append("</table>");
                }
            } else {
                sb.Append("<table><tr>");
                // 2026-09-02 列序同上: 名次 → 姓名/代表队 → 号码 → 组别 → 组数 → 道次 → 最终成绩 …
                sb.Append(ColGroupHtml(epRelay));
                sb.Append("<thead><tr>");
                sb.AppendFormat("<th>名次</th>");
                sb.AppendFormat("<th>{0}</th><th>{1}</th>", epH1, epH2);
                sb.Append("<th>号码</th><th>组别</th><th>组数</th><th>道次</th>");
                sb.Append("<th>最终成绩</th><th>成绩差</th><th>反应时间</th><th>备注</th>");
                sb.Append("</tr></thead><tbody>");
                foreach (dynamic item in _currentResults)
                {
                    string c1 = epRelay ? item.Country : item.Name;
                    string c2 = epRelay ? item.Name : item.Country;
                    string rk = (string)item.Rank;
                    string rkCls = rk == "1" ? " class='r1'" : rk == "2" ? " class='r2'" : rk == "3" ? " class='r3'" : "";
                    sb.Append("<tr>");
                    sb.AppendFormat("<td{0}>{1}</td>", rkCls, rk);
                    sb.AppendFormat("<td class='nm'>{0}</td><td>{1}</td>", c1, c2);
                    sb.AppendFormat("<td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td>", item.BibNumber, item.AgeGroup, item.HeatText, item.Lane);
                    sb.AppendFormat("<td class='tm'>{0}</td>", item.FinalTime);
                    sb.AppendFormat("<td>{0}</td>", item.Diff);
                    sb.AppendFormat("<td class='rt'>{0}</td><td>{1}</td>", item.ReactionTimeHtml, item.RemarkHtml);
                    sb.Append("</tr>");
                }
                sb.Append("</tbody>");
                sb.Append("</table>");
            }

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

        /// <summary>
        /// 2026-09-03 "导出 PDF" 直接出 PDF 文件。
        ///
        /// 原来它只是把 HTML 在浏览器里打开, 再弹一句"请按 Ctrl+P 选 Print to PDF" ——
        /// 按钮叫"导出 PDF", 按下去却没有 PDF, 名不副实。
        /// 用的是跟"比赛日志 PDF"同一条路: Edge/Chrome 无头模式 --print-to-pdf。
        /// 这台机器上确实没有 Edge/Chrome 时才退回原来那套, 并明说为什么。
        /// </summary>
        private void ExportPdf_Click(object sender, RoutedEventArgs e) {
            try {
                string suggested; var html = BuildPrintHtml(out suggested);
                if (string.IsNullOrEmpty(html)) return;
                var dlg = new Microsoft.Win32.SaveFileDialog {
                    Filter = "PDF 文件|*.pdf|所有文件|*.*",
                    FileName = suggested + ".pdf",
                    Title = "导出 PDF"
                };
                if (dlg.ShowDialog() != true) return;
                string tmpHtml = WriteTempHtml(html, suggested);
                if (MainWindow.TryHtmlToPdf(tmpHtml, dlg.FileName)) {
                    if (MessageBox.Show("已导出：\n" + dlg.FileName + "\n\n是否立即打开？", "导出 PDF",
                            MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        Process.Start(dlg.FileName);
                    return;
                }
                Process.Start(tmpHtml);
                MessageBox.Show("这台机器上没找到 Edge 或 Chrome，无法直接生成 PDF。\n\n"
                    + "已在浏览器中打开，请按 Ctrl+P，打印机选 \"Microsoft Print to PDF\" 另存为 PDF。",
                    "导出 PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
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
