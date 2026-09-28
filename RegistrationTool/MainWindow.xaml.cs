using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RegistrationTool
{
    public partial class MainWindow : Window
    {
        private SimpleWebSocketClient _ws;
        private string _assignedBib = "";
        private bool _submitted = false;
        private List<EventEntry> _events = new List<EventEntry>();

        private class EventEntry {
            public string EventName { get; set; }
            public string EntryTime { get; set; }
            public override string ToString() {
                return string.IsNullOrEmpty(EntryTime) ? EventName : SwimmingScoreboard.Loc.F("Str_RegTool_EventEntryFmt", EventName, EntryTime);
            }
        }

        // 2026-09-22 多人报名队列：先"添加到报名列表"攒多个运动员，最后"全部提交"一次性发给服务器
        //   （对齐 register.html 的 pendingRegs/REGISTER_SWIMMERS_MULTI，之前 EXE 端只能一次提交一人）。
        private class QueueEntry {
            public JObject Swimmer { get; set; }
            public List<EventEntry> Events { get; set; }
            public bool IsResubmit { get; set; }
            public override string ToString() {
                string nm = Swimmer["name"] != null ? Swimmer["name"].ToString() : "";
                string gd = Swimmer["gender"] != null ? Swimmer["gender"].ToString() : "";
                string ct = Swimmer["country"] != null ? Swimmer["country"].ToString() : "";
                return SwimmingScoreboard.Loc.F("Str_RegTool_QueueEntryFmt", nm, gd, ct, Events.Count);
            }
        }
        private List<QueueEntry> _pendingRegs = new List<QueueEntry>();

        // 2026-09-22 接力：一支队可报多个接力项目（同一组棒次），多支队排队后一次性提交
        //   （对齐 register.html 的 pendingRelayRegs/REGISTER_RELAYS_MULTI）。
        private List<EventEntry> _relayEvents = new List<EventEntry>();
        private class RelayQueueEntry {
            public string TeamName { get; set; }
            public string Gender { get; set; }
            public string AgeGroup { get; set; }
            public string CountryShort { get; set; }
            public JArray Legs { get; set; }
            public List<EventEntry> Events { get; set; }
            public override string ToString() {
                return SwimmingScoreboard.Loc.F("Str_RegTool_RelayQueueEntryFmt", TeamName, Gender, AgeGroup, Events.Count, Legs.Count);
            }
        }
        private List<RelayQueueEntry> _pendingRelayRegs = new List<RelayQueueEntry>();

        public MainWindow() {
            InitializeComponent();
            // 2026-09-28 SubmitQueueBtn/SubmitRelayQueueBtn 的初始文案是带计数的动态格式化文本
            // (Loc.F)，XAML 里不留静态占位，构造完就用当前语言渲染一次"0人/0队"的初始状态。
            RefreshRegQueue();
            RefreshRelayQueue();
        }

        // 状态颜色：红=未连接，绿=已连接，黄=连接中/失败
        private static readonly System.Windows.Media.SolidColorBrush LedRed   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
        private static readonly System.Windows.Media.SolidColorBrush LedGreen = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E));
        private static readonly System.Windows.Media.SolidColorBrush LedAmber = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));

        private void SetConnState(string text, System.Windows.Media.Brush color, string btnLabel) {
            StatusText.Text = text;
            StatusText.Foreground = color;
            if (StatusLed != null) StatusLed.Background = color;
            ConnBtn.Content = btnLabel;
        }

        // 顶部"修改用户名和密码"按钮 — 弹 ChangePasswordWindow，凭据存 register_credentials.json
        private void ChangePassword_Click(object sender, RoutedEventArgs e) {
            var dlg = new ChangePasswordWindow();
            dlg.Owner = this;
            dlg.ShowDialog();
        }

        private void Connect_Click(object sender, RoutedEventArgs e) {
            if (_ws != null && _ws.IsConnected) {
                _ws.Close();
                _ws = null;
                SetConnState(SwimmingScoreboard.Loc.T("Str_RegTool_StatusDisconnected"), LedRed, SwimmingScoreboard.Loc.T("Str_RegTool_ConnectBtn"));
                return;
            }
            string addr = ServerBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 3002;
            if (parts.Length > 1) int.TryParse(parts[1], out port);

            SetConnState(SwimmingScoreboard.Loc.T("Str_RegTool_Connecting"), LedAmber, SwimmingScoreboard.Loc.T("Str_RegTool_ConnectBtn"));
            ConnBtn.IsEnabled = false;

            // 2026-05-21 改异步：原来 TcpClient.Connect 同步阻塞 UI 线程最长 ~21s；
            // 现在内部走 5s 超时，并放后台线程跑，主线程立刻返回。
            // （本项目 TargetFramework=v4.0，无 Task.Run，故用 Task.Factory.StartNew）
            System.Threading.Tasks.Task.Factory.StartNew((Action)delegate() {
                SimpleWebSocketClient ws = null;
                string err = null;
                try {
                    ws = new SimpleWebSocketClient();
                    ws.OnMessage += OnServerMessage;
                    ws.OnDisconnected += delegate() {
                        Dispatcher.Invoke((Action)delegate() {
                            SetConnState(SwimmingScoreboard.Loc.T("Str_RegTool_ConnLost"), LedRed, SwimmingScoreboard.Loc.T("Str_RegTool_ConnectBtn"));
                        });
                    };
                    ws.ConnectWithTimeout(host, port, 5000);
                    if (!ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_TERMINAL_IDENTITY" }))) {
                        throw new Exception(SwimmingScoreboard.Loc.T("Str_RegTool_ErrIdentitySendFailed"));
                    }
                } catch (Exception ex) {
                    err = ex.Message;
                    try { if (ws != null) ws.Close(); } catch { }
                    ws = null;
                }
                Dispatcher.Invoke((Action)delegate() {
                    ConnBtn.IsEnabled = true;
                    if (err != null) {
                        SetConnState(SwimmingScoreboard.Loc.F("Str_RegTool_ConnFailedFmt", err), LedAmber, SwimmingScoreboard.Loc.T("Str_RegTool_ConnectBtn"));
                    } else {
                        _ws = ws;
                        SetConnState(SwimmingScoreboard.Loc.F("Str_RegTool_ConnectedFmt", host, port), LedGreen, SwimmingScoreboard.Loc.T("Str_RegTool_DisconnectBtn"));
                    }
                });
            });
        }

        /// <summary>
        /// 2026-09-03 用主服务器下发的配置表重填四类下拉: 性别 / 组别 / 项目 / 接力项目。
        ///
        /// 这些原来全是 XAML 里写死的(男/女/混合/男女、甲乙丙丁组、50米自由泳…) ——
        /// 主程序的"比赛参数设置管理"改了, 这边一点不知道, 报名员选不到本场真正的组别和项目。
        /// 服务器没下发某一项(空表)时保持原样, 不要把下拉清空 —— 宁可用旧的, 也不能没得选。
        /// 组别/项目下拉是可编辑的, 重填时把用户已经输入的文字留住。
        /// </summary>
        private void ApplyMetaLists(JObject d) {
            if (d == null) return;
            try {
                var genders = ToList(d["genders"]);
                var ages = ToList(d["ageGroups"]);
                var events = ToList(d["events"]);
                FillCombo(GenderCombo, genders, false);
                FillCombo(RelayGenderCombo, genders, false);
                FillCombo(AgeGroupCombo, ages, true);
                FillCombo(RelayAgeGroupCombo, ages, true);
                // 项目表里接力和个人项目混在一起, 按名字分到两个下拉
                if (events.Count > 0) {
                    var indiv = new List<string>();
                    var relay = new List<string>();
                    foreach (var ev in events) {
                        if (ev.Contains("接力")) relay.Add(ev); else indiv.Add(ev);
                    }
                    FillCombo(EventCombo, indiv, false);
                    FillCombo(RelayEventCombo, relay, false);
                }
                RegStatusText.Text = SwimmingScoreboard.Loc.F("Str_RegTool_MetaSyncedFmt",
                    genders.Count, ages.Count, events.Count);
                RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            } catch { }
        }

        private static List<string> ToList(JToken t) {
            var l = new List<string>();
            var arr = t as JArray;
            if (arr == null) return l;
            foreach (var x in arr) {
                string s = x != null ? x.ToString() : "";
                if (!string.IsNullOrEmpty(s) && !l.Contains(s)) l.Add(s);
            }
            return l;
        }

        /// <summary>空表不动(保住原有选项); 可编辑下拉在最前面留一个空项, 允许不填。</summary>
        private static void FillCombo(ComboBox cb, List<string> items, bool allowBlank) {
            if (cb == null || items == null || items.Count == 0) return;
            string prev = "";
            var sel = cb.SelectedItem as ComboBoxItem;
            if (sel != null && sel.Content != null) prev = sel.Content.ToString();
            else if (cb.SelectedItem is string) prev = (string)cb.SelectedItem;
            else if (cb.IsEditable && !string.IsNullOrEmpty(cb.Text)) prev = cb.Text;
            cb.Items.Clear();
            if (allowBlank) cb.Items.Add(new ComboBoxItem { Content = "" });
            foreach (var s in items) cb.Items.Add(new ComboBoxItem { Content = s });
            for (int i = 0; i < cb.Items.Count; i++) {
                var ci = cb.Items[i] as ComboBoxItem;
                if (ci != null && ci.Content != null && ci.Content.ToString() == prev) { cb.SelectedIndex = i; return; }
            }
            if (cb.IsEditable && !string.IsNullOrEmpty(prev)) { cb.Text = prev; return; }
            cb.SelectedIndex = 0;
        }

        private void OnServerMessage(string json) {
            Dispatcher.Invoke((Action)delegate() {
                try {
                    var msg = JObject.Parse(json);
                    string mtype = msg["type"] != null ? msg["type"].ToString() : "";
                    // 2026-09-03 主服务器下发【比赛参数设置管理】里的五张表。
                    //   本程序原来性别/组别是 XAML 里写死的 男/女/混合/男女 —— 源码注释里
                    //   自己也写着"没有配置推送通道，暂时写死；治本要加 genderList 下发"。
                    //   现在通道有了: 一上线服务器推一次, 参数改了再推一次。
                    if (mtype == "META_LISTS") {
                        ApplyMetaLists(msg["data"] as JObject);
                        return;
                    }
                    if (mtype == "REGISTER_RESULT") {
                        var data = msg["data"];
                        bool ok = data != null && data["success"] != null && (bool)data["success"];
                        string srvMsg = data != null && data["message"] != null ? data["message"].ToString() : "";
                        if (ok) {
                            _assignedBib = data["bibNumber"] != null ? data["bibNumber"].ToString() : _assignedBib;
                            _submitted = true;
                            // 服务器若带 message（如 "成功新增 2 项，跳过 1 项已存在"）则显示出来
                            RegStatusText.Text = string.IsNullOrEmpty(srvMsg)
                                ? SwimmingScoreboard.Loc.F("Str_RegTool_RegSuccessFmt", _assignedBib)
                                : SwimmingScoreboard.Loc.F("Str_RegTool_RegSuccessMsgFmt", _assignedBib, srvMsg);
                            RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                        } else {
                            RegStatusText.Text = SwimmingScoreboard.Loc.F("Str_RegTool_RegFailedFmt", string.IsNullOrEmpty(srvMsg) ? SwimmingScoreboard.Loc.T("Str_RegTool_UnknownError") : srvMsg);
                            RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        }
                    } else if (mtype == "REGISTER_RELAY_RESULT") {
                        // 2026-05-21 新增：原来 EXE 直接忽略服务器对接力的回执，
                        // 用户看到的"已提交"只是发出去那一刻的乐观提示，与服务器实际处理结果无关。
                        var data = msg["data"];
                        bool ok = data != null && data["success"] != null && (bool)data["success"];
                        string srvMsg = data != null && data["message"] != null ? data["message"].ToString() : "";
                        if (ok) {
                            string team = data["teamName"] != null ? data["teamName"].ToString() : "";
                            string bib  = data["bibNumber"] != null ? data["bibNumber"].ToString() : "";
                            int legCount = data["legCount"] != null && data["legCount"].Type != JTokenType.Null ? (int)data["legCount"] : 0;
                            bool updated = data["updated"] != null && (bool)data["updated"];
                            string action = SwimmingScoreboard.Loc.T(updated ? "Str_RegTool_RelayUpdated" : "Str_RegTool_RelayCreated");
                            RelayStatusText.Text = string.IsNullOrEmpty(srvMsg)
                                ? SwimmingScoreboard.Loc.F("Str_RegTool_RelaySuccessFmt", action, team, bib, legCount)
                                : SwimmingScoreboard.Loc.F("Str_RegTool_RelaySuccessMsgFmt", action, team, bib, legCount, srvMsg);
                            RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                        } else {
                            RelayStatusText.Text = SwimmingScoreboard.Loc.F("Str_RegTool_RelayFailedFmt", string.IsNullOrEmpty(srvMsg) ? SwimmingScoreboard.Loc.T("Str_RegTool_UnknownError") : srvMsg);
                            RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        }
                    } else if (mtype == "REGISTER_MULTI_RESULT") {
                        HandleMultiResult(msg["data"] as JObject);
                    } else if (mtype == "REGISTER_RELAYS_MULTI_RESULT") {
                        HandleRelaysMultiResult(msg["data"] as JObject);
                    }
                } catch { }
            });
        }

        private void AddEvent_Click(object sender, RoutedEventArgs e) {
            string eventName = EventCombo.SelectedItem != null ? ((ComboBoxItem)EventCombo.SelectedItem).Content.ToString() : "";
            if (string.IsNullOrEmpty(eventName)) return;
            foreach (var ev in _events) {
                if (ev.EventName == eventName) {
                    MessageBox.Show(SwimmingScoreboard.Loc.T("Str_RegTool_ErrDupEvent"), SwimmingScoreboard.Loc.T("Str_MsgTitle_Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            _events.Add(new EventEntry { EventName = eventName, EntryTime = EntryTimeBox.Text.Trim() });
            EntryTimeBox.Clear();
            RefreshEventList();
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e) {
            int idx = EventListBox.SelectedIndex;
            if (idx < 0 || idx >= _events.Count) { MessageBox.Show(SwimmingScoreboard.Loc.T("Str_RegTool_ErrSelectEventFirst")); return; }
            _events.RemoveAt(idx);
            RefreshEventList();
        }

        private void RefreshEventList() {
            EventListBox.Items.Clear();
            foreach (var ev in _events) EventListBox.Items.Add(ev.ToString());
        }

        // 2026-09-22 原来点一次只提交当前表单这一人——现在改成"加入队列, 最后一次性全提交",
        //   对齐 register.html 的多人报名列表。
        private void AddToQueue_Click(object sender, RoutedEventArgs e) {
            string name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetRegStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrNameRequired"), true); return; }
            if (_events.Count == 0) { SetRegStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrNeedEvent"), true); return; }

            // 2026-05-21 支持手动输入 yyyy-MM-dd（与 HTML <input type="date"> 一致），解析失败时报错而不是静默丢弃
            string bdErr;
            string birthDate = ReadBirthDate(BirthDatePicker, out bdErr);
            if (bdErr != null) { SetRegStatus(bdErr, true); return; }
            int age = ComputeAge(birthDate);

            var swimmerData = new JObject();
            swimmerData["name"] = name;
            swimmerData["gender"] = ((ComboBoxItem)GenderCombo.SelectedItem).Content.ToString();
            swimmerData["age"] = age;
            swimmerData["country"] = CountryBox.Text.Trim();
            swimmerData["countryShort"] = CountryShortBox.Text.Trim();
            swimmerData["ageGroup"] = ReadComboText(AgeGroupCombo);
            swimmerData["idNumber"] = IDNumberBox.Text.Trim();
            swimmerData["phone"] = PhoneBox.Text.Trim();
            swimmerData["birthDate"] = birthDate;
            swimmerData["csaNumber"] = CSABox.Text.Trim();
            swimmerData["notes"] = NotesBox.Text.Trim();
            swimmerData["bibNumber"] = _assignedBib;

            _pendingRegs.Add(new QueueEntry { Swimmer = swimmerData, Events = new List<EventEntry>(_events), IsResubmit = _submitted });
            RefreshRegQueue();
            ClearFormCore();
            SetRegStatus(SwimmingScoreboard.Loc.T("Str_RegTool_AddedToQueue"), false);
        }

        private void ClearForm_Click(object sender, RoutedEventArgs e) { ClearFormCore(); RegStatusText.Text = ""; }

        private void ClearFormCore() {
            NameBox.Clear(); IDNumberBox.Clear(); PhoneBox.Clear(); CSABox.Clear(); NotesBox.Clear();
            CountryBox.Clear(); CountryShortBox.Clear();
            BirthDatePicker.SelectedDate = null; BirthDatePicker.Text = "";
            if (GenderCombo.Items.Count > 0) GenderCombo.SelectedIndex = 0;
            if (AgeGroupCombo.Items.Count > 0) AgeGroupCombo.SelectedIndex = 0;
            EntryTimeBox.Clear();
            _events.Clear();
            RefreshEventList();
            _assignedBib = "";
            _submitted = false;
        }

        private void RefreshRegQueue() {
            RegQueueListBox.Items.Clear();
            foreach (var q in _pendingRegs) RegQueueListBox.Items.Add(q.ToString());
            SubmitQueueBtn.IsEnabled = _pendingRegs.Count > 0;
            SubmitQueueBtn.Content = SwimmingScoreboard.Loc.F("Str_RegTool_SubmitQueueFmt", _pendingRegs.Count);
        }

        private void SubmitQueue_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { SetRegStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrNotConnected"), true); return; }
            if (_pendingRegs.Count == 0) { SetRegStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrQueueEmpty"), true); return; }
            var entriesArr = new JArray();
            foreach (var q in _pendingRegs) {
                var en = new JObject();
                en["swimmer"] = q.Swimmer;
                var evArr = new JArray();
                foreach (var ev in q.Events) {
                    var o = new JObject(); o["eventName"] = ev.EventName; o["entryTime"] = ev.EntryTime; evArr.Add(o);
                }
                en["events"] = evArr;
                en["isResubmit"] = q.IsResubmit;
                entriesArr.Add(en);
            }
            var data = new JObject(); data["entries"] = entriesArr;
            bool sent = _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_SWIMMERS_MULTI", data = data }));
            if (!sent) { SetRegStatus(SwimmingScoreboard.Loc.F("Str_RegTool_ErrSendFailedFmt", SwimmingScoreboard.Loc.T("Str_RegTool_ConnectBtn")), true); return; }
            SetRegStatus(SwimmingScoreboard.Loc.F("Str_RegTool_SubmittingFmt", _pendingRegs.Count), false);
        }

        // 服务器批量回执（register.html 同款协议 REGISTER_MULTI_RESULT）：整批要么全过要么全部退回，
        // 见服务端 HandleRegisterSwimmersMulti 的"任何一条失败都不入库"注释。
        private void HandleMultiResult(JObject data) {
            if (data == null) return;
            bool ok = data["success"] != null && (bool)data["success"];
            string srvMsg = data["message"] != null ? data["message"].ToString() : "";
            var entries = data["entries"] as JArray;
            if (ok) {
                _pendingRegs.Clear();
                RefreshRegQueue();
                SetRegStatus(string.IsNullOrEmpty(srvMsg) ? SwimmingScoreboard.Loc.T("Str_RegTool_AllSubmittedOk") : srvMsg, false);
            } else {
                string detail = "";
                if (entries != null) {
                    foreach (JObject en in entries) {
                        if (en["ok"] != null && !(bool)en["ok"])
                            detail += (detail.Length > 0 ? "；" : "") + (en["message"] != null ? en["message"].ToString() : "");
                    }
                }
                SetRegStatus(SwimmingScoreboard.Loc.F("Str_RegTool_QueueRejectedFmt", string.IsNullOrEmpty(detail) ? srvMsg : detail), true);
            }
        }

        private void SetRegStatus(string text, bool isError) {
            RegStatusText.Text = text;
            RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                isError ? System.Windows.Media.Colors.Red : System.Windows.Media.Colors.Green);
        }

        private static int ComputeAge(string birthDate) {
            if (string.IsNullOrEmpty(birthDate)) return 0;
            DateTime bdDt;
            if (!DateTime.TryParseExact(birthDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out bdDt)) return 0;
            var today = DateTime.Today;
            int age = today.Year - bdDt.Year;
            if (bdDt.Date > today.AddYears(-age)) age--;
            return age;
        }

        // ═══════ 接力：项目列表 + 多队报名列表 ═══════
        private void AddRelayEvent_Click(object sender, RoutedEventArgs e) {
            string eventName = RelayEventCombo.SelectedItem != null ? ((ComboBoxItem)RelayEventCombo.SelectedItem).Content.ToString() : "";
            if (string.IsNullOrEmpty(eventName)) return;
            foreach (var ev in _relayEvents) {
                if (ev.EventName == eventName) {
                    MessageBox.Show(SwimmingScoreboard.Loc.T("Str_RegTool_ErrDupEvent"), SwimmingScoreboard.Loc.T("Str_MsgTitle_Info"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            _relayEvents.Add(new EventEntry { EventName = eventName, EntryTime = RelayEntryTimeBox.Text.Trim() });
            RelayEntryTimeBox.Clear();
            RefreshRelayEventList();
        }

        private void RefreshRelayEventList() {
            RelayEventListBox.Items.Clear();
            foreach (var ev in _relayEvents) RelayEventListBox.Items.Add(ev.ToString());
        }

        private void AddRelayToQueue_Click(object sender, RoutedEventArgs e) {
            if (_relayEvents.Count == 0) { SetRelayStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrNeedRelayEvent"), true); return; }
            string team = RelayTeamBox.Text.Trim();
            if (string.IsNullOrEmpty(team)) { SetRelayStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrTeamNameRequired"), true); return; }

            var legs = new JArray();
            TextBox[] nameBoxes = { Leg1Name, Leg2Name, Leg3Name, Leg4Name };
            TextBox[] idBoxes = { Leg1ID, Leg2ID, Leg3ID, Leg4ID };
            TextBox[] bibBoxes = { Leg1Bib, Leg2Bib, Leg3Bib, Leg4Bib };
            DatePicker[] birthPickers = { Leg1Birth, Leg2Birth, Leg3Birth, Leg4Birth };
            for (int i = 0; i < 4; i++) {
                string legName = nameBoxes[i].Text.Trim();
                if (!string.IsNullOrEmpty(legName)) {
                    string legBdErr;
                    string legBd = ReadBirthDate(birthPickers[i], out legBdErr);
                    if (legBdErr != null) { SetRelayStatus(SwimmingScoreboard.Loc.F("Str_RegTool_LegErrFmt", i + 1, legBdErr), true); return; }
                    var leg = new JObject();
                    leg["legOrder"] = i + 1;
                    leg["swimmerName"] = legName;
                    leg["swimmerIDNumber"] = idBoxes[i].Text.Trim();
                    leg["swimmerBibNumber"] = bibBoxes[i].Text.Trim();
                    leg["swimmerBirthDate"] = legBd;
                    legs.Add(leg);
                }
            }
            if (legs.Count == 0) { SetRelayStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrNeedOneLeg"), true); return; }

            var entry = new RelayQueueEntry {
                TeamName = team,
                Gender = RelayGenderCombo.SelectedItem != null ? ((ComboBoxItem)RelayGenderCombo.SelectedItem).Content.ToString() : "男",
                AgeGroup = ReadComboText(RelayAgeGroupCombo),
                CountryShort = RelayCountryShortBox.Text.Trim(),
                Legs = legs,
                Events = new List<EventEntry>(_relayEvents)
            };
            _pendingRelayRegs.Add(entry);
            RefreshRelayQueue();
            ClearRelayFormCore();
            SetRelayStatus(SwimmingScoreboard.Loc.T("Str_RegTool_AddedToRelayQueue"), false);
        }

        private void ClearRelayForm_Click(object sender, RoutedEventArgs e) { ClearRelayFormCore(); RelayStatusText.Text = ""; }

        private void ClearRelayFormCore() {
            RelayTeamBox.Clear(); RelayCountryShortBox.Clear(); RelayEntryTimeBox.Clear();
            if (RelayGenderCombo.Items.Count > 0) RelayGenderCombo.SelectedIndex = 0;
            if (RelayAgeGroupCombo.Items.Count > 0) RelayAgeGroupCombo.SelectedIndex = 0;
            TextBox[] nameBoxes = { Leg1Name, Leg2Name, Leg3Name, Leg4Name };
            TextBox[] idBoxes = { Leg1ID, Leg2ID, Leg3ID, Leg4ID };
            TextBox[] bibBoxes = { Leg1Bib, Leg2Bib, Leg3Bib, Leg4Bib };
            DatePicker[] birthPickers = { Leg1Birth, Leg2Birth, Leg3Birth, Leg4Birth };
            for (int i = 0; i < 4; i++) {
                nameBoxes[i].Clear(); idBoxes[i].Clear(); bibBoxes[i].Clear();
                birthPickers[i].SelectedDate = null; birthPickers[i].Text = "";
            }
            _relayEvents.Clear();
            RefreshRelayEventList();
        }

        private void RefreshRelayQueue() {
            RelayQueueListBox.Items.Clear();
            foreach (var q in _pendingRelayRegs) RelayQueueListBox.Items.Add(q.ToString());
            SubmitRelayQueueBtn.IsEnabled = _pendingRelayRegs.Count > 0;
            SubmitRelayQueueBtn.Content = SwimmingScoreboard.Loc.F("Str_RegTool_SubmitRelayQueueFmt", _pendingRelayRegs.Count);
        }

        private void SubmitRelayQueue_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { SetRelayStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrNotConnected"), true); return; }
            if (_pendingRelayRegs.Count == 0) { SetRelayStatus(SwimmingScoreboard.Loc.T("Str_RegTool_ErrRelayQueueEmpty"), true); return; }
            var entriesArr = new JArray();
            foreach (var q in _pendingRelayRegs) {
                var en = new JObject();
                en["teamName"] = q.TeamName;
                en["gender"] = q.Gender;
                en["ageGroup"] = q.AgeGroup;
                en["countryShort"] = q.CountryShort;
                en["legs"] = q.Legs;
                var evArr = new JArray();
                foreach (var ev in q.Events) {
                    var o = new JObject(); o["eventName"] = ev.EventName; o["entryTime"] = ev.EntryTime; evArr.Add(o);
                }
                en["events"] = evArr;
                entriesArr.Add(en);
            }
            var data = new JObject(); data["entries"] = entriesArr;
            bool sent = _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_RELAYS_MULTI", data = data }));
            if (!sent) { SetRelayStatus(SwimmingScoreboard.Loc.F("Str_RegTool_ErrSendFailedFmt", SwimmingScoreboard.Loc.T("Str_RegTool_ConnectBtn")), true); return; }
            SetRelayStatus(SwimmingScoreboard.Loc.F("Str_RegTool_SubmittingRelayFmt", _pendingRelayRegs.Count), false);
        }

        // 服务器批量回执：entries[i] = {ok, message, teamName, eventName, bibNumber, legCount, updated}——
        // 每条（队×项目）独立处理，不是全有全无，见服务端 HandleRegisterRelaysMulti。
        private void HandleRelaysMultiResult(JObject data) {
            if (data == null) return;
            var entries = data["entries"] as JArray;
            int okCount = 0, failCount = 0;
            string failDetail = "";
            if (entries != null) {
                foreach (JObject en in entries) {
                    bool ok = en["ok"] != null && (bool)en["ok"];
                    if (ok) okCount++;
                    else {
                        failCount++;
                        string tn = en["teamName"] != null ? en["teamName"].ToString() : "";
                        string ev = en["eventName"] != null ? en["eventName"].ToString() : "";
                        string msg = en["message"] != null ? en["message"].ToString() : "";
                        failDetail += (failDetail.Length > 0 ? "；" : "") + tn + (string.IsNullOrEmpty(ev) ? "" : "(" + ev + ")") + ": " + msg;
                    }
                }
            }
            if (failCount == 0 && okCount > 0) {
                _pendingRelayRegs.Clear();
                RefreshRelayQueue();
                SetRelayStatus(SwimmingScoreboard.Loc.F("Str_RegTool_RelayAllOkFmt", okCount), false);
            } else if (okCount > 0) {
                SetRelayStatus(SwimmingScoreboard.Loc.F("Str_RegTool_RelayPartialFmt", okCount, failCount, failDetail), true);
            } else {
                SetRelayStatus(SwimmingScoreboard.Loc.F("Str_RegTool_RelayAllFailedFmt", failDetail), true);
            }
        }

        private void SetRelayStatus(string text, bool isError) {
            RelayStatusText.Text = text;
            RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                isError ? System.Windows.Media.Colors.Red : System.Windows.Media.Colors.Green);
        }

        // 读取可编辑 ComboBox 的当前值（兼容选项+自由输入）
        private static string ReadComboText(ComboBox cb) {
            if (cb == null) return "";
            var cbi = cb.SelectedItem as ComboBoxItem;
            if (cbi != null && cbi.Content != null) return cbi.Content.ToString().Trim();
            return (cb.Text ?? "").Trim();
        }

        // 2026-05-21 与 register.html 的 <input type="date"> 行为对齐：
        //   DatePicker 既能输入 yyyy-MM-dd，也能点日历选择；
        //   输入了但解析不出来 → 返回 null + 错误信息，避免静默清空用户输入。
        // 返回："yyyy-MM-dd" 或 ""（未填）或 null（带 error 提示）
        private static string ReadBirthDate(DatePicker dp, out string error) {
            error = null;
            if (dp == null) return "";
            if (dp.SelectedDate.HasValue)
                return dp.SelectedDate.Value.ToString("yyyy-MM-dd");
            string text = (dp.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return "";
            DateTime d;
            if (DateTime.TryParseExact(text, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out d))
                return d.ToString("yyyy-MM-dd");
            if (DateTime.TryParse(text, System.Globalization.CultureInfo.GetCultureInfo("en-CA"),
                    System.Globalization.DateTimeStyles.None, out d))
                return d.ToString("yyyy-MM-dd");
            error = SwimmingScoreboard.Loc.F("Str_RegTool_BirthDateErrFmt", text);
            return null;
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (_ws != null) _ws.Close();
        }
    }
}
