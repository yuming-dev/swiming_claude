using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScheduleEditor
{
    public partial class MainWindow : Window
    {
        private SimpleWebSocketClient _ws;
        private DispatcherTimer _reconnect;
        private JObject _data;            // 最新一次主服务器广播
        private string _serverHost = "127.0.0.1";
        private int _serverPort = 3002;
        private int _failCount = 0;

        // 三个 Tab 的数据集合
        public ObservableCollection<SwimmerRow> Swimmers { get; set; }
        public ObservableCollection<ResultRow> Results { get; set; }
        public ObservableCollection<DocRow> Documents { get; set; }

        public MainWindow() {
            InitializeComponent();
            Swimmers = new ObservableCollection<SwimmerRow>();
            Results = new ObservableCollection<ResultRow>();
            Documents = new ObservableCollection<DocRow>();
            SwimmerGrid.ItemsSource = Swimmers;
            ResultGrid.ItemsSource = Results;
            DocGrid.ItemsSource = Documents;

            UserText.Text = "用户: " + CredentialStore.CurrentUser();

            // 上次保存的服务器地址
            string lastHost = ReadSettingsString("serverHost");
            if (!string.IsNullOrEmpty(lastHost)) ServerHostBox.Text = lastHost;

            _reconnect = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _reconnect.Tick += delegate { if (_ws == null || !_ws.IsConnected) DoConnect(); };

            Loaded += delegate { DoConnect(); };
            Closed += delegate { try { if (_ws != null) _ws.Close(); } catch { } };
        }

        // ═══════════════════════════════════════════════════════════════
        // 修改用户名和密码（顶部按钮）
        // ═══════════════════════════════════════════════════════════════
        private void ChangePassword_Click(object sender, RoutedEventArgs e) {
            var dlg = new ChangePasswordWindow();
            dlg.Owner = this;
            dlg.ShowDialog();
            // 改密后顶部用户名同步刷新
            UserText.Text = "用户: " + CredentialStore.CurrentUser();
        }

        // ═══════════════════════════════════════════════════════════════
        // 服务器连接
        // ═══════════════════════════════════════════════════════════════
        private void Connect_Click(object sender, RoutedEventArgs e) {
            DoConnect();
        }

        private void DoConnect() {
            string txt = (ServerHostBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt)) return;
            string host = txt; int port = 3002;
            int colon = txt.IndexOf(':');
            if (colon > 0) {
                host = txt.Substring(0, colon);
                int.TryParse(txt.Substring(colon + 1), out port);
            }
            _serverHost = host; _serverPort = port;

            try { if (_ws != null) _ws.Close(); } catch { }
            _ws = new SimpleWebSocketClient();
            _ws.OnMessage += OnWsMessage;
            _ws.OnDisconnected += delegate {
                Dispatcher.BeginInvoke((Action)delegate { SetConnState(false, "已断开"); _reconnect.Start(); });
            };
            try {
                _ws.Connect(host, port);
                SetConnState(true, host + ":" + port);
                _failCount = 0;
                _reconnect.Stop();
                WriteSettingsString("serverHost", txt);
                // 通知服务器我是编辑端身份（沿用 TIMING_WEB_IDENTITY，避免新加协议）
                _ws.Send(JsonConvert.SerializeObject(new { type = "TIMING_WEB_IDENTITY" }));
            } catch (Exception ex) {
                SetConnState(false, "连接失败");
                _failCount++;
                if (_failCount > 3) {
                    MessageBox.Show("无法连接到主服务器：" + ex.Message, "连接错误",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    _failCount = 0;
                }
                _reconnect.Start();
            }
        }

        private void SetConnState(bool connected, string text) {
            ConnDot.Fill = new SolidColorBrush(connected
                ? Color.FromRgb(0x22, 0xC5, 0x5E)
                : Color.FromRgb(0xEF, 0x44, 0x44));
            ConnStatusText.Text = connected ? ("已连接 " + text) : text;
        }

        private void OnWsMessage(string raw) {
            Dispatcher.BeginInvoke((Action)delegate {
                try {
                    JObject msg = JObject.Parse(raw);
                    string type = msg["type"] != null ? msg["type"].ToString() : "";
                    JToken data = msg["data"];
                    // 服务器主流广播：SHOW_LIVE_RACE / SHOW_HEAT_RESULT 等 — 都带完整状态
                    if (data is JObject) {
                        _data = (JObject)data;
                        ApplyBroadcast();
                    } else if (type == "DOC_LIST_RESULT") {
                        ApplyDocList(data as JArray);
                    } else if (type == "EDITOR_OP_RESULT") {
                        ApplyOpResult(data as JObject);
                    }
                } catch { }
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // 应用广播
        // ═══════════════════════════════════════════════════════════════
        private void ApplyBroadcast() {
            if (_data == null) return;
            // 1) 运动员（要全量列表，服务器在 GetStatusData.allSwimmers 中提供 — 见后端补丁）
            JArray all = _data["allSwimmers"] as JArray;
            if (all == null) all = _data["swimmers"] as JArray;   // 兜底：先用当前组
            UpdateSwimmerComboFilters();
            RebuildSwimmerGrid(all);

            // 2) 成绩 — 服务器 eventRanking 数组（已含名次 / final time / status 等）
            UpdateResultComboFilters();
            RebuildResultGrid();
        }

        private void ApplyDocList(JArray docs) {
            Documents.Clear();
            if (docs == null) return;
            foreach (JObject d in docs) {
                Documents.Add(new DocRow {
                    Category = d["category"] != null ? d["category"].ToString() : "",
                    FileName = d["fileName"] != null ? d["fileName"].ToString() : "",
                    Path = d["path"] != null ? d["path"].ToString() : "",
                    Time = d["time"] != null ? d["time"].ToString() : "",
                    Size = d["size"] != null ? d["size"].ToString() : ""
                });
            }
        }

        private void ApplyOpResult(JObject data) {
            if (data == null) return;
            bool ok = data["success"] != null && (bool)data["success"];
            string msg = data["message"] != null ? data["message"].ToString() : (ok ? "操作成功" : "操作失败");
            if (!ok) MessageBox.Show(msg, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ═══════════════════════════════════════════════════════════════
        // 赛事管理与报名
        // ═══════════════════════════════════════════════════════════════
        private void UpdateSwimmerComboFilters() {
            // 项目下拉
            var eventList = ToStringList(_data["eventList"] as JArray);
            eventList.Insert(0, "全部");
            SetCombo(FilterEventCombo, eventList);
            // 性别下拉
            var genders = ToStringList(_data["genderList"] as JArray);
            genders.Insert(0, "全部");
            SetCombo(FilterGenderCombo, genders);
            // 代表队下拉（从全量 swimmers 提取唯一代表队）
            JArray all = _data["allSwimmers"] as JArray;
            if (all == null) all = _data["swimmers"] as JArray;
            var countries = new SortedSet<string>(StringComparer.Ordinal);
            if (all != null) foreach (JObject sw in all) {
                string c = sw["country"] != null ? sw["country"].ToString() : "";
                if (!string.IsNullOrEmpty(c)) countries.Add(c);
            }
            var cl = new List<string>(countries);
            cl.Insert(0, "全部");
            SetCombo(FilterCountryCombo, cl);
        }

        private void RebuildSwimmerGrid(JArray all) {
            Swimmers.Clear();
            if (all == null) return;
            string evFilter = SelectedOrAll(FilterEventCombo);
            string gFilter = SelectedOrAll(FilterGenderCombo);
            string cFilter = SelectedOrAll(FilterCountryCombo);
            string search = (SearchBox.Text ?? "").Trim();

            foreach (JObject sw in all) {
                string name = Get(sw, "name");
                string ev = Get(sw, "eventName");
                string g = Get(sw, "gender");
                string c = Get(sw, "country");
                if (evFilter != "全部" && ev != evFilter) continue;
                if (gFilter != "全部" && g != gFilter) continue;
                if (cFilter != "全部" && c != cFilter) continue;
                if (search.Length > 0 && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && Get(sw, "bibNumber").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                Swimmers.Add(new SwimmerRow {
                    BibNumber = Get(sw, "bibNumber"),
                    Name = name,
                    Gender = g,
                    BirthDate = Get(sw, "birthDate"),
                    AgeCategory = Get(sw, "ageCategory"),
                    Country = c,
                    EventName = ev,
                    CurrentStage = Get(sw, "currentStage"),
                    Heat = Get(sw, "heat"),
                    Lane = Get(sw, "lane"),
                    EntryTime = Get(sw, "entryTime"),
                    Status = Get(sw, "status"),
                    IDNumber = Get(sw, "idNumber"),
                    Phone = Get(sw, "phone"),
                    Notes = Get(sw, "notes"),
                    Age = Get(sw, "age")
                });
            }
            SwimmerCountText.Text = string.Format("共 {0} 人", Swimmers.Count);
        }

        private void RefreshSwimmers_Click(object sender, RoutedEventArgs e) {
            ApplyBroadcast();
        }

        private void Filter_Changed(object sender, EventArgs e) {
            if (_data == null) return;
            JArray all = _data["allSwimmers"] as JArray;
            if (all == null) all = _data["swimmers"] as JArray;
            RebuildSwimmerGrid(all);
        }

        private void AddSwimmer_Click(object sender, RoutedEventArgs e) {
            var dlg = new SwimmerEditDialog(null, _data);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "REGISTER_SWIMMER",
                    data = dlg.Result
                }));
            }
        }

        private void DeleteSwimmer_Click(object sender, RoutedEventArgs e) {
            var sel = SwimmerGrid.SelectedItems.Cast<SwimmerRow>().ToList();
            if (sel.Count == 0) {
                MessageBox.Show("请先选中要删除的运动员（可按住 Ctrl 或 Shift 多选）", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string preview = string.Join("\n", sel.Take(8)
                .Select(s => string.Format("· {0}({1}) {2}", s.Name, s.BibNumber, s.EventName)));
            if (sel.Count > 8) preview += string.Format("\n... 及其它 {0} 条", sel.Count - 8);
            var r = MessageBox.Show(string.Format("确定删除以下 {0} 条报名记录？\n\n{1}", sel.Count, preview),
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            foreach (var s in sel) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "DELETE_SWIMMER",
                    data = new {
                        bibNumber = s.BibNumber,
                        name = s.Name,
                        eventName = s.EventName
                    }
                }));
            }
        }

        private void SwimmerGrid_DoubleClick(object sender, MouseButtonEventArgs e) {
            var row = SwimmerGrid.SelectedItem as SwimmerRow;
            if (row == null) return;
            var dlg = new SwimmerEditDialog(row, _data);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "UPDATE_SWIMMER",
                    data = dlg.Result
                }));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 成绩与排名
        // ═══════════════════════════════════════════════════════════════
        private void UpdateResultComboFilters() {
            var eventList = ToStringList(_data["eventList"] as JArray);
            SetCombo(ResultEventCombo, eventList);
            var genders = ToStringList(_data["genderList"] as JArray);
            SetCombo(ResultGenderCombo, genders);
            var stages = ToStringList(_data["stageList"] as JArray);
            if (stages.Count == 0) stages = new List<string> { "预赛", "半决赛", "决赛" };
            SetCombo(ResultStageCombo, stages);
            // 默认选当前正在进行项目
            string curEvt = _data["currentEvent"] != null ? _data["currentEvent"].ToString() : "";
            if (!string.IsNullOrEmpty(curEvt)) SelectIfExists(ResultEventCombo, curEvt);
            string curG = _data["currentGender"] != null ? _data["currentGender"].ToString() : "";
            if (!string.IsNullOrEmpty(curG)) SelectIfExists(ResultGenderCombo, curG);
            string curS = _data["currentStage"] != null ? _data["currentStage"].ToString() : "";
            if (!string.IsNullOrEmpty(curS)) SelectIfExists(ResultStageCombo, curS);
        }

        private void RebuildResultGrid() {
            Results.Clear();
            if (_data == null) return;
            string ev = SelectedOrEmpty(ResultEventCombo);
            string g = SelectedOrEmpty(ResultGenderCombo);
            string stage = SelectedOrEmpty(ResultStageCombo);
            if (string.IsNullOrEmpty(ev) || string.IsNullOrEmpty(g)) return;

            JArray all = _data["allSwimmers"] as JArray;
            if (all == null) return;
            // 服务器没有专门"event ranking by stage"广播 — 在客户端按 currentStage / stage 字段筛选
            // 并按 FinalTime 升序排
            var rows = new List<ResultRow>();
            foreach (JObject sw in all) {
                if (Get(sw, "eventName") != ev) continue;
                if (Get(sw, "gender") != g) continue;
                if (!string.IsNullOrEmpty(stage) && Get(sw, "currentStage") != stage) continue;
                rows.Add(new ResultRow {
                    BibNumber = Get(sw, "bibNumber"),
                    Name = Get(sw, "name"),
                    Country = Get(sw, "country"),
                    AgeCategory = Get(sw, "ageCategory"),
                    Heat = Get(sw, "heat"),
                    Lane = Get(sw, "lane"),
                    FinalTime = Get(sw, "finalTime"),
                    TimingSource = Get(sw, "timingSource"),
                    StartingBlockTime = Get(sw, "startingBlockTime"),
                    RecordNote = Get(sw, "recordNote"),
                    Status = Get(sw, "status"),
                    RankNum = ParseIntOr(Get(sw, "currentRank"), 0)
                });
            }
            // 按服务器已计算的 currentRank（0 表示无成绩或被排除）
            rows.Sort((a, b) => {
                if (a.RankNum == 0 && b.RankNum == 0) return 0;
                if (a.RankNum == 0) return 1;
                if (b.RankNum == 0) return -1;
                return a.RankNum.CompareTo(b.RankNum);
            });
            int i = 0;
            foreach (var r in rows) {
                r.Rank = r.RankNum > 0 ? r.RankNum.ToString() : "";
                Results.Add(r);
                i++;
            }
            ResultCountText.Text = string.Format("共 {0} 条记录", Results.Count);
        }

        private void RefreshResults_Click(object sender, RoutedEventArgs e) {
            RebuildResultGrid();
        }

        private void ResultFilter_Changed(object sender, EventArgs e) {
            RebuildResultGrid();
        }

        // ═══════════════════════════════════════════════════════════════
        // 文档编辑/输出/打印
        // ═══════════════════════════════════════════════════════════════
        private void RefreshDocuments_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) {
                MessageBox.Show("未连接到主服务器，请先连接。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _ws.Send(JsonConvert.SerializeObject(new {
                type = "TIMING_CMD",
                command = "LIST_DOCUMENTS"
            }));
        }

        private void DocGrid_DoubleClick(object sender, MouseButtonEventArgs e) {
            var d = DocGrid.SelectedItem as DocRow;
            if (d == null || string.IsNullOrEmpty(d.Path)) return;
            // 服务器 Documents/RawData 一般通过 http://server:3002/ 提供静态文件
            // 这里直接拼 URL 让浏览器打开（HTML 可预览，doc/pdf 浏览器下载）
            string url = string.Format("http://{0}:{1}/Documents/{2}", _serverHost, _serverPort, d.FileName);
            try {
                System.Diagnostics.Process.Start(url);
            } catch (Exception ex) {
                MessageBox.Show("打开失败: " + ex.Message, "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════════════════════════
        private static string Get(JObject o, string key) {
            if (o == null || o[key] == null) return "";
            return o[key].ToString();
        }
        private static int ParseIntOr(string s, int def) {
            int v; return int.TryParse(s, out v) ? v : def;
        }
        private static List<string> ToStringList(JArray arr) {
            var r = new List<string>();
            if (arr == null) return r;
            foreach (var t in arr) r.Add(t.ToString());
            return r;
        }
        private static void SetCombo(ComboBox cb, List<string> items) {
            string prev = cb.SelectedItem as string;
            cb.Items.Clear();
            foreach (var s in items) cb.Items.Add(s);
            if (!string.IsNullOrEmpty(prev) && items.Contains(prev)) cb.SelectedItem = prev;
            else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }
        private static void SelectIfExists(ComboBox cb, string val) {
            foreach (var it in cb.Items) {
                if (string.Equals(it as string, val, StringComparison.Ordinal)) {
                    cb.SelectedItem = it;
                    return;
                }
            }
        }
        private static string SelectedOrAll(ComboBox cb) {
            string s = cb.SelectedItem as string;
            return string.IsNullOrEmpty(s) ? "全部" : s;
        }
        private static string SelectedOrEmpty(ComboBox cb) {
            return (cb.SelectedItem as string) ?? "";
        }

        // 设置文件 editor_settings.json
        private string SettingsPath {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "editor_settings.json"); }
        }
        private string ReadSettingsString(string key) {
            try {
                if (!File.Exists(SettingsPath)) return null;
                JObject o = JObject.Parse(File.ReadAllText(SettingsPath));
                return o[key] != null ? o[key].ToString() : null;
            } catch { return null; }
        }
        private void WriteSettingsString(string key, string val) {
            try {
                JObject o = File.Exists(SettingsPath) ? JObject.Parse(File.ReadAllText(SettingsPath)) : new JObject();
                o[key] = val;
                File.WriteAllText(SettingsPath, o.ToString(Formatting.Indented));
            } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 行模型
    // ═══════════════════════════════════════════════════════════════
    public class SwimmerRow
    {
        public string BibNumber { get; set; }
        public string Name { get; set; }
        public string Gender { get; set; }
        public string BirthDate { get; set; }
        public string AgeCategory { get; set; }
        public string Country { get; set; }
        public string EventName { get; set; }
        public string CurrentStage { get; set; }
        public string Heat { get; set; }
        public string Lane { get; set; }
        public string EntryTime { get; set; }
        public string Status { get; set; }
        public string IDNumber { get; set; }
        public string Phone { get; set; }
        public string Notes { get; set; }
        public string Age { get; set; }
    }

    public class ResultRow
    {
        public string Rank { get; set; }
        public int RankNum { get; set; }
        public string BibNumber { get; set; }
        public string Name { get; set; }
        public string Country { get; set; }
        public string AgeCategory { get; set; }
        public string Heat { get; set; }
        public string Lane { get; set; }
        public string FinalTime { get; set; }
        public string TimingSource { get; set; }
        public string StartingBlockTime { get; set; }
        public string RecordNote { get; set; }
        public string Status { get; set; }
    }

    public class DocRow
    {
        public string Category { get; set; }
        public string FileName { get; set; }
        public string Path { get; set; }
        public string Time { get; set; }
        public string Size { get; set; }
    }
}
