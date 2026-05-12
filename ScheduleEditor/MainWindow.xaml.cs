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
        public ObservableCollection<RelayRow> Relays { get; set; }
        public ObservableCollection<ResultRow> Results { get; set; }
        public ObservableCollection<DocRow> Documents { get; set; }
        // 比赛参数 Tab 用 — 项目 / 性别 / 赛次 是字符串列表，组别带 minAge/maxAge
        public ObservableCollection<StringRow> EventItems { get; set; }
        public ObservableCollection<StringRow> GenderItems { get; set; }
        public ObservableCollection<StringRow> StageItems { get; set; }
        public ObservableCollection<AgeGroupItem> AgeGroupItems { get; set; }
        public ObservableCollection<ScheduleRow> Schedules { get; set; }

        public MainWindow() {
            InitializeComponent();
            Swimmers = new ObservableCollection<SwimmerRow>();
            Relays = new ObservableCollection<RelayRow>();
            Results = new ObservableCollection<ResultRow>();
            Documents = new ObservableCollection<DocRow>();
            EventItems = new ObservableCollection<StringRow>();
            GenderItems = new ObservableCollection<StringRow>();
            StageItems = new ObservableCollection<StringRow>();
            AgeGroupItems = new ObservableCollection<AgeGroupItem>();
            Schedules = new ObservableCollection<ScheduleRow>();
            SwimmerGrid.ItemsSource = Swimmers;
            RelayGrid.ItemsSource = Relays;
            ResultGrid.ItemsSource = Results;
            DocGrid.ItemsSource = Documents;
            EventGrid.ItemsSource = EventItems;
            GenderGrid.ItemsSource = GenderItems;
            StageGrid.ItemsSource = StageItems;
            AgeGroupGrid.ItemsSource = AgeGroupItems;
            ScheduleGrid.ItemsSource = Schedules;

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

            // 2) 接力队
            UpdateRelayComboFilters();
            RebuildRelayGrid();

            // 3) 比赛参数 — 只在没有未保存编辑时刷新，避免覆盖用户正在编辑的内容
            //    简单策略：每次都重新拉取（用户应"保存"再切换 Tab）
            RebuildParamGrids();

            // 4) 赛程
            RebuildScheduleGrid();

            // 5) 成绩 — 服务器 eventRanking 数组（已含名次 / final time / status 等）
            UpdateResultComboFilters();
            RebuildResultGrid();
        }

        // ═══════════════════════════════════════════════════════════════
        // 比赛参数（项目 / 组别 / 性别 / 赛次）
        // ═══════════════════════════════════════════════════════════════
        private void RebuildParamGrids() {
            if (_data == null) return;
            // 项目
            EventItems.Clear();
            JArray evs = _data["eventList"] as JArray;
            if (evs != null) foreach (var t in evs) EventItems.Add(new StringRow { Value = t.ToString() });
            // 性别
            GenderItems.Clear();
            JArray gs = _data["genderList"] as JArray;
            if (gs != null) foreach (var t in gs) GenderItems.Add(new StringRow { Value = t.ToString() });
            // 赛次
            StageItems.Clear();
            JArray ss = _data["stageList"] as JArray;
            if (ss != null) foreach (var t in ss) StageItems.Add(new StringRow { Value = t.ToString() });
            // 组别 — 优先用 ageGroupsDetail
            AgeGroupItems.Clear();
            JArray detail = _data["ageGroupsDetail"] as JArray;
            if (detail != null) {
                foreach (JObject o in detail) {
                    AgeGroupItems.Add(new AgeGroupItem {
                        Name = o["name"] != null ? o["name"].ToString() : "",
                        MinAge = o["minAge"] != null ? (int)o["minAge"] : 0,
                        MaxAge = o["maxAge"] != null ? (int)o["maxAge"] : 200
                    });
                }
            } else {
                // 兜底：旧 ageGroups 只有名字
                JArray nameOnly = _data["ageGroups"] as JArray;
                if (nameOnly != null) foreach (var t in nameOnly)
                    AgeGroupItems.Add(new AgeGroupItem { Name = t.ToString(), MinAge = 0, MaxAge = 200 });
            }
        }

        // 通用 Add / Delete 操作
        private void EventAdd_Click(object sender, RoutedEventArgs e)  { EventItems.Add(new StringRow { Value = "" }); EventGrid.SelectedItem = EventItems.Last(); EventGrid.ScrollIntoView(EventGrid.SelectedItem); }
        private void EventDelete_Click(object sender, RoutedEventArgs e) {
            var sel = EventGrid.SelectedItems.Cast<StringRow>().ToList();
            foreach (var it in sel) EventItems.Remove(it);
        }
        private void EventUp_Click(object sender, RoutedEventArgs e) {
            int i = EventGrid.SelectedIndex;
            if (i > 0) { var it = EventItems[i]; EventItems.RemoveAt(i); EventItems.Insert(i - 1, it); EventGrid.SelectedIndex = i - 1; }
        }
        private void EventDown_Click(object sender, RoutedEventArgs e) {
            int i = EventGrid.SelectedIndex;
            if (i >= 0 && i < EventItems.Count - 1) { var it = EventItems[i]; EventItems.RemoveAt(i); EventItems.Insert(i + 1, it); EventGrid.SelectedIndex = i + 1; }
        }
        private void EventSave_Click(object sender, RoutedEventArgs e) {
            SendListUpdate("UPDATE_EVENT_LIST", EventItems.Select(r => (object)(r.Value ?? "")).Where(s => !string.IsNullOrEmpty(s as string)));
        }

        private void GenderAdd_Click(object sender, RoutedEventArgs e)  { GenderItems.Add(new StringRow { Value = "" }); GenderGrid.SelectedItem = GenderItems.Last(); }
        private void GenderDelete_Click(object sender, RoutedEventArgs e) {
            foreach (var it in GenderGrid.SelectedItems.Cast<StringRow>().ToList()) GenderItems.Remove(it);
        }
        private void GenderSave_Click(object sender, RoutedEventArgs e) {
            SendListUpdate("UPDATE_GENDER_LIST", GenderItems.Select(r => (object)(r.Value ?? "")).Where(s => !string.IsNullOrEmpty(s as string)));
        }

        private void StageAdd_Click(object sender, RoutedEventArgs e)  { StageItems.Add(new StringRow { Value = "" }); StageGrid.SelectedItem = StageItems.Last(); }
        private void StageDelete_Click(object sender, RoutedEventArgs e) {
            foreach (var it in StageGrid.SelectedItems.Cast<StringRow>().ToList()) StageItems.Remove(it);
        }
        private void StageSave_Click(object sender, RoutedEventArgs e) {
            SendListUpdate("UPDATE_STAGE_LIST", StageItems.Select(r => (object)(r.Value ?? "")).Where(s => !string.IsNullOrEmpty(s as string)));
        }

        private void AgeGroupAdd_Click(object sender, RoutedEventArgs e) { AgeGroupItems.Add(new AgeGroupItem { Name = "", MinAge = 0, MaxAge = 200 }); AgeGroupGrid.SelectedItem = AgeGroupItems.Last(); }
        private void AgeGroupDelete_Click(object sender, RoutedEventArgs e) {
            foreach (var it in AgeGroupGrid.SelectedItems.Cast<AgeGroupItem>().ToList()) AgeGroupItems.Remove(it);
        }
        private void AgeGroupSave_Click(object sender, RoutedEventArgs e) {
            var items = AgeGroupItems.Where(g => !string.IsNullOrEmpty(g.Name))
                .Select(g => (object)new { name = g.Name, minAge = g.MinAge, maxAge = g.MaxAge });
            SendListUpdate("UPDATE_AGE_GROUP_LIST", items);
        }

        // ═══════════════════════════════════════════════════════════════
        // 赛程编排
        // ═══════════════════════════════════════════════════════════════
        private void RebuildScheduleGrid() {
            Schedules.Clear();
            if (_data == null) return;
            JArray sch = _data["schedule"] as JArray;
            if (sch == null) return;
            foreach (JObject o in sch) {
                Schedules.Add(new ScheduleRow {
                    SessionNumber = ParseIntOr(Get(o, "sessionNumber"), 0),
                    SessionName = Get(o, "sessionName"),
                    Date = Get(o, "date"),
                    Time = Get(o, "time"),
                    AgeGroup = Get(o, "ageGroup"),
                    Gender = Get(o, "gender"),
                    EventName = Get(o, "eventName"),
                    Stage = Get(o, "stage"),
                    HeatCount = ParseIntOr(Get(o, "heatCount"), 0),
                    IsRelay = o["isRelay"] != null && (bool)o["isRelay"]
                });
            }
            ScheduleCountText.Text = string.Format("共 {0} 项赛程", Schedules.Count);
        }

        private void RefreshSchedule_Click(object sender, RoutedEventArgs e) { RebuildScheduleGrid(); }

        private void AddSchedule_Click(object sender, RoutedEventArgs e) {
            var dlg = new ScheduleItemEditDialog(null, _data);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "ADD_SCHEDULE_ITEM",
                    data = dlg.Result
                }));
            }
        }

        private void DeleteSchedule_Click(object sender, RoutedEventArgs e) {
            var sel = ScheduleGrid.SelectedItems.Cast<ScheduleRow>().ToList();
            if (sel.Count == 0) {
                MessageBox.Show("请先选中要删除的赛程项", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string preview = string.Join("\n", sel.Take(8)
                .Select(s => string.Format("· [{0}] {1} {2} {3}", s.AgeGroup, s.Gender, s.EventName, s.Stage)));
            if (sel.Count > 8) preview += string.Format("\n... 及其它 {0} 条", sel.Count - 8);
            var r = MessageBox.Show(string.Format("确定删除以下 {0} 项赛程？\n\n{1}", sel.Count, preview),
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            foreach (var s in sel) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "DELETE_SCHEDULE_ITEM",
                    data = new {
                        gender = s.Gender,
                        eventName = s.EventName,
                        stage = s.Stage,
                        ageGroup = s.AgeGroup
                    }
                }));
            }
        }

        private void ScheduleGrid_DoubleClick(object sender, MouseButtonEventArgs e) {
            var row = ScheduleGrid.SelectedItem as ScheduleRow;
            if (row == null) return;
            var dlg = new ScheduleItemEditDialog(row, _data);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "UPDATE_SCHEDULE_ITEM",
                    data = dlg.Result
                }));
            }
        }

        private void AutoGenerateHeats_Click(object sender, RoutedEventArgs e) {
            var r = MessageBox.Show("确定按报名成绩重新生成各项目的预赛分组？\n\n（已有半决赛/决赛分组不受影响）",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _ws.Send(JsonConvert.SerializeObject(new {
                type = "TIMING_CMD",
                command = "AUTO_GENERATE_HEATS"
            }));
        }

        private void SendListUpdate(string command, System.Collections.Generic.IEnumerable<object> items) {
            if (_ws == null || !_ws.IsConnected) {
                MessageBox.Show("未连接到主服务器，请先连接。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _ws.Send(JsonConvert.SerializeObject(new {
                type = "TIMING_CMD",
                command = command,
                data = new { items = items.ToList() }
            }));
        }

        // ═══════════════════════════════════════════════════════════════
        // 接力队
        // ═══════════════════════════════════════════════════════════════
        private void UpdateRelayComboFilters() {
            var eventList = new List<string> { "全部" };
            var rawEvents = ToStringList(_data["eventList"] as JArray);
            foreach (var ev in rawEvents) {
                if (ev.IndexOf('×') >= 0 || ev.ToLower().IndexOf('x') >= 0) eventList.Add(ev);
            }
            // 兜底：如果服务器没标记接力，给一份默认列表
            if (eventList.Count == 1) {
                eventList.AddRange(new[] { "4×50米自由泳接力", "4×100米自由泳接力", "4×200米自由泳接力", "4×100米混合泳接力" });
            }
            SetCombo(RelayEventCombo, eventList);
            var genders = new List<string> { "全部" };
            genders.AddRange(ToStringList(_data["genderList"] as JArray));
            SetCombo(RelayGenderCombo, genders);
        }

        private void RebuildRelayGrid() {
            Relays.Clear();
            JArray all = _data["allRelayTeams"] as JArray;
            if (all == null) { RelayCountText.Text = ""; return; }
            string evFilter = SelectedOrAll(RelayEventCombo);
            string gFilter = SelectedOrAll(RelayGenderCombo);
            string search = (RelaySearchBox.Text ?? "").Trim();

            foreach (JObject t in all) {
                string ev = Get(t, "eventName");
                string g = Get(t, "gender");
                string name = Get(t, "teamName");
                if (evFilter != "全部" && ev != evFilter) continue;
                if (gFilter != "全部" && g != gFilter) continue;
                if (search.Length > 0 && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var row = new RelayRow {
                    TeamName = name,
                    Gender = g,
                    EventName = ev,
                    Stage = Get(t, "stage"),
                    Heat = Get(t, "heat"),
                    Lane = Get(t, "lane"),
                    EntryTime = Get(t, "entryTime"),
                    Status = Get(t, "status")
                };
                var legs = t["legs"] as JArray;
                if (legs != null) foreach (JObject leg in legs) {
                    int order = leg["legOrder"] != null ? (int)leg["legOrder"] : 0;
                    string sname = leg["swimmerName"] != null ? leg["swimmerName"].ToString() : "";
                    string sbib = leg["swimmerBibNumber"] != null ? leg["swimmerBibNumber"].ToString() : "";
                    string sid  = leg["swimmerIDNumber"] != null ? leg["swimmerIDNumber"].ToString() : "";
                    string sdob = leg["swimmerBirthDate"] != null ? leg["swimmerBirthDate"].ToString() : "";
                    if (order == 1) { row.Leg1 = sname; row.Leg1Bib = sbib; row.Leg1Id = sid; row.Leg1Dob = sdob; }
                    else if (order == 2) { row.Leg2 = sname; row.Leg2Bib = sbib; row.Leg2Id = sid; row.Leg2Dob = sdob; }
                    else if (order == 3) { row.Leg3 = sname; row.Leg3Bib = sbib; row.Leg3Id = sid; row.Leg3Dob = sdob; }
                    else if (order == 4) { row.Leg4 = sname; row.Leg4Bib = sbib; row.Leg4Id = sid; row.Leg4Dob = sdob; }
                }
                Relays.Add(row);
            }
            RelayCountText.Text = string.Format("共 {0} 队", Relays.Count);
        }

        private void RefreshRelays_Click(object sender, RoutedEventArgs e) { RebuildRelayGrid(); }
        private void RelayFilter_Changed(object sender, EventArgs e) { RebuildRelayGrid(); }

        private void AddRelay_Click(object sender, RoutedEventArgs e) {
            var dlg = new RelayTeamEditDialog(null, _data);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "UPDATE_RELAY",
                    data = dlg.Result
                }));
            }
        }

        private void DeleteRelay_Click(object sender, RoutedEventArgs e) {
            var sel = RelayGrid.SelectedItems.Cast<RelayRow>().ToList();
            if (sel.Count == 0) {
                MessageBox.Show("请先选中要删除的接力队（可按住 Ctrl/Shift 多选）", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string preview = string.Join("\n", sel.Take(8)
                .Select(t => string.Format("· {0} {1} {2}", t.Gender, t.EventName, t.TeamName)));
            if (sel.Count > 8) preview += string.Format("\n... 及其它 {0} 条", sel.Count - 8);
            var r = MessageBox.Show(string.Format("确定删除以下 {0} 个接力队？\n\n{1}", sel.Count, preview),
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            foreach (var t in sel) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "DELETE_RELAY",
                    data = new { teamName = t.TeamName, gender = t.Gender, eventName = t.EventName }
                }));
            }
        }

        private void RelayGrid_DoubleClick(object sender, MouseButtonEventArgs e) {
            var row = RelayGrid.SelectedItem as RelayRow;
            if (row == null) return;
            var dlg = new RelayTeamEditDialog(row, _data);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "UPDATE_RELAY",
                    data = dlg.Result
                }));
            }
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

        // 编辑成绩（按钮 + 双击行）
        private void EditResult_Click(object sender, RoutedEventArgs e) {
            var row = ResultGrid.SelectedItem as ResultRow;
            if (row == null) {
                MessageBox.Show("请先选中要编辑的行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string ev = SelectedOrEmpty(ResultEventCombo);
            string stage = SelectedOrEmpty(ResultStageCombo);
            var dlg = new ResultEditDialog(row, ev, stage);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "UPDATE_RESULT",
                    data = dlg.Result
                }));
            }
        }

        // 标 DNS / DNF / DSQ / 清除状态
        private void MarkDNS_Click(object sender, RoutedEventArgs e) { MarkStatus("DNS"); }
        private void MarkDNF_Click(object sender, RoutedEventArgs e) { MarkStatus("DNF"); }
        private void MarkDSQ_Click(object sender, RoutedEventArgs e) { MarkStatus("DSQ"); }
        private void ClearStatus_Click(object sender, RoutedEventArgs e) { MarkStatus(""); }

        private void MarkStatus(string status) {
            var sel = ResultGrid.SelectedItems.Cast<ResultRow>().ToList();
            if (sel.Count == 0) {
                MessageBox.Show("请先选中要标记的行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!string.IsNullOrEmpty(status)) {
                var r = MessageBox.Show(string.Format("确定将 {0} 个运动员标为 {1} ？", sel.Count, status),
                    "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            string ev = SelectedOrEmpty(ResultEventCombo);
            string stage = SelectedOrEmpty(ResultStageCombo);
            foreach (var row in sel) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "MARK_STATUS",
                    data = new {
                        bibNumber = row.BibNumber,
                        eventName = ev,
                        stage = stage,
                        status = status
                    }
                }));
            }
        }

        // 晋级到下一赛次
        private void ExecutePromotion_Click(object sender, RoutedEventArgs e) {
            string ev = SelectedOrEmpty(ResultEventCombo);
            string g = SelectedOrEmpty(ResultGenderCombo);
            string fromStage = SelectedOrEmpty(ResultStageCombo);
            if (string.IsNullOrEmpty(ev) || string.IsNullOrEmpty(g) || string.IsNullOrEmpty(fromStage)) {
                MessageBox.Show("请先选好项目 / 性别 / 当前赛次", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new PromotionDialog(g, ev, fromStage);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true) {
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD",
                    command = "EXECUTE_PROMOTION",
                    data = new {
                        gender = g,
                        eventName = ev,
                        fromStage = fromStage,
                        nextStage = dlg.NextStage,
                        promoCount = dlg.PromoCount
                    }
                }));
            }
        }

        // 取消晋级（删该 stage 的 StageAssignment + schedule item）
        private void CancelPromotion_Click(object sender, RoutedEventArgs e) {
            string ev = SelectedOrEmpty(ResultEventCombo);
            string g = SelectedOrEmpty(ResultGenderCombo);
            string stage = SelectedOrEmpty(ResultStageCombo);
            if (string.IsNullOrEmpty(ev) || string.IsNullOrEmpty(g) || string.IsNullOrEmpty(stage)) {
                MessageBox.Show("请先选好项目 / 性别 / 要取消的赛次", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (stage == "预赛") {
                MessageBox.Show("预赛是首轮赛次，无法取消晋级。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var r = MessageBox.Show(string.Format("确定取消 {0} {1} {2} 的所有晋级（移除该赛次的所有 StageAssignment 与赛程项）？",
                g, ev, stage), "确认取消晋级", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            _ws.Send(JsonConvert.SerializeObject(new {
                type = "TIMING_CMD",
                command = "CANCEL_PROMOTION",
                data = new { gender = g, eventName = ev, stage = stage }
            }));
        }

        // 计算团体计分
        private void CalculateTeamScores_Click(object sender, RoutedEventArgs e) {
            var r = MessageBox.Show("确定按当前已确认的决赛成绩重新计算团体积分？",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _ws.Send(JsonConvert.SerializeObject(new {
                type = "TIMING_CMD",
                command = "CALCULATE_TEAM_SCORES"
            }));
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

    // 比赛参数 Tab 的简单行 — 单字符串值
    public class StringRow : System.ComponentModel.INotifyPropertyChanged
    {
        private string _value;
        public string Value {
            get { return _value; }
            set { _value = value; if (PropertyChanged != null) PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs("Value")); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public class AgeGroupItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _name;
        private int _min, _max;
        public string Name {
            get { return _name; }
            set { _name = value; Fire("Name"); }
        }
        public int MinAge {
            get { return _min; }
            set { _min = value; Fire("MinAge"); }
        }
        public int MaxAge {
            get { return _max; }
            set { _max = value; Fire("MaxAge"); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void Fire(string p) { if (PropertyChanged != null) PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(p)); }
    }

    public class ScheduleRow
    {
        public int SessionNumber { get; set; }
        public string SessionName { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public string AgeGroup { get; set; }
        public string Gender { get; set; }
        public string EventName { get; set; }
        public string Stage { get; set; }
        public int HeatCount { get; set; }
        public bool IsRelay { get; set; }
    }

    public class RelayRow
    {
        public string TeamName { get; set; }
        public string Gender { get; set; }
        public string EventName { get; set; }
        public string Stage { get; set; }
        public string Heat { get; set; }
        public string Lane { get; set; }
        public string EntryTime { get; set; }
        public string Status { get; set; }
        public string Leg1 { get; set; }
        public string Leg2 { get; set; }
        public string Leg3 { get; set; }
        public string Leg4 { get; set; }
        public string Leg1Bib { get; set; }
        public string Leg2Bib { get; set; }
        public string Leg3Bib { get; set; }
        public string Leg4Bib { get; set; }
        public string Leg1Id { get; set; }
        public string Leg2Id { get; set; }
        public string Leg3Id { get; set; }
        public string Leg4Id { get; set; }
        public string Leg1Dob { get; set; }
        public string Leg2Dob { get; set; }
        public string Leg3Dob { get; set; }
        public string Leg4Dob { get; set; }
    }
}
