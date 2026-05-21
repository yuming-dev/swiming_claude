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
                return string.IsNullOrEmpty(EntryTime) ? EventName : string.Format("{0}  (报名: {1})", EventName, EntryTime);
            }
        }

        public MainWindow() {
            InitializeComponent();
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
                SetConnState("未连接", LedRed, "连接");
                return;
            }
            string addr = ServerBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 3002;
            if (parts.Length > 1) int.TryParse(parts[1], out port);

            SetConnState("连接中…", LedAmber, "连接");
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
                            SetConnState("连接断开", LedRed, "连接");
                        });
                    };
                    ws.ConnectWithTimeout(host, port, 5000);
                    if (!ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_TERMINAL_IDENTITY" }))) {
                        throw new Exception("身份注册帧发送失败");
                    }
                } catch (Exception ex) {
                    err = ex.Message;
                    try { if (ws != null) ws.Close(); } catch { }
                    ws = null;
                }
                Dispatcher.Invoke((Action)delegate() {
                    ConnBtn.IsEnabled = true;
                    if (err != null) {
                        SetConnState("连接失败: " + err, LedAmber, "连接");
                    } else {
                        _ws = ws;
                        SetConnState("已连接 " + host + ":" + port, LedGreen, "断开");
                    }
                });
            });
        }

        private void OnServerMessage(string json) {
            Dispatcher.Invoke((Action)delegate() {
                try {
                    var msg = JObject.Parse(json);
                    string mtype = msg["type"] != null ? msg["type"].ToString() : "";
                    if (mtype == "REGISTER_RESULT") {
                        var data = msg["data"];
                        bool ok = data != null && data["success"] != null && (bool)data["success"];
                        string srvMsg = data != null && data["message"] != null ? data["message"].ToString() : "";
                        if (ok) {
                            _assignedBib = data["bibNumber"] != null ? data["bibNumber"].ToString() : _assignedBib;
                            _submitted = true;
                            // 服务器若带 message（如 "成功新增 2 项，跳过 1 项已存在"）则显示出来
                            RegStatusText.Text = string.IsNullOrEmpty(srvMsg)
                                ? string.Format("报名成功！参赛号: {0}。如需修改，可重新编辑后再次提交。", _assignedBib)
                                : string.Format("报名成功！参赛号: {0}。{1}", _assignedBib, srvMsg);
                            RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                        } else {
                            RegStatusText.Text = "报名失败: " + (string.IsNullOrEmpty(srvMsg) ? "未知错误" : srvMsg);
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
                            string action = updated ? "已更新" : "已新建";
                            RelayStatusText.Text = string.IsNullOrEmpty(srvMsg)
                                ? string.Format("接力{0}！队名: {1}  代号: {2}  ({3} 棒)", action, team, bib, legCount)
                                : string.Format("接力{0}！队名: {1}  代号: {2}  ({3} 棒) — {4}", action, team, bib, legCount, srvMsg);
                            RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                        } else {
                            RelayStatusText.Text = "接力报名失败: " + (string.IsNullOrEmpty(srvMsg) ? "未知错误" : srvMsg);
                            RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        }
                    }
                } catch { }
            });
        }

        private void AddEvent_Click(object sender, RoutedEventArgs e) {
            string eventName = EventCombo.SelectedItem != null ? ((ComboBoxItem)EventCombo.SelectedItem).Content.ToString() : "";
            if (string.IsNullOrEmpty(eventName)) return;
            foreach (var ev in _events) {
                if (ev.EventName == eventName) {
                    MessageBox.Show("已添加此项目，不能重复！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            _events.Add(new EventEntry { EventName = eventName, EntryTime = EntryTimeBox.Text.Trim() });
            EntryTimeBox.Clear();
            RefreshEventList();
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e) {
            int idx = EventListBox.SelectedIndex;
            if (idx < 0 || idx >= _events.Count) { MessageBox.Show("请先选中要删除的项目"); return; }
            _events.RemoveAt(idx);
            RefreshEventList();
        }

        private void RefreshEventList() {
            EventListBox.Items.Clear();
            foreach (var ev in _events) EventListBox.Items.Add(ev.ToString());
        }

        private void SubmitAll_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { RegStatusText.Text = "请先连接服务器"; return; }
            string name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { RegStatusText.Text = "请输入姓名"; return; }
            if (_events.Count == 0) { RegStatusText.Text = "请至少添加一个参赛项目"; return; }

            // 2026-05-21 支持手动输入 yyyy-MM-dd（与 HTML <input type="date"> 一致），解析失败时报错而不是静默丢弃
            string bdErr;
            string birthDate = ReadBirthDate(BirthDatePicker, out bdErr);
            if (bdErr != null) {
                RegStatusText.Text = bdErr;
                RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                return;
            }
            int age = 0;
            if (!string.IsNullOrEmpty(birthDate)) {
                DateTime bdDt;
                if (DateTime.TryParseExact(birthDate, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out bdDt)) {
                    var today = DateTime.Today;
                    age = today.Year - bdDt.Year;
                    if (bdDt.Date > today.AddYears(-age)) age--;
                }
            }

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

            var eventsArr = new JArray();
            foreach (var ev in _events) {
                var obj = new JObject();
                obj["eventName"] = ev.EventName;
                obj["entryTime"] = ev.EntryTime;
                eventsArr.Add(obj);
            }

            var msgData = new JObject();
            msgData["swimmer"] = swimmerData;
            msgData["events"] = eventsArr;
            msgData["isResubmit"] = _submitted;

            // 2026-05-21：检查 Send 返回值；连接已半死时立刻提示，不再让用户以为提交成功
            bool sent = _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_SWIMMER_BATCH", data = msgData }));
            if (!sent) {
                RegStatusText.Text = "发送失败：与主服务器的连接已断开，请重新点击\"连接\"后再提交";
                RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                return;
            }
            RegStatusText.Text = string.Format("正在提交 {0} 个项目...（等待主服务器确认）", _events.Count);
            RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
        }

        private void RelayRegister_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { RelayStatusText.Text = "请先连接服务器"; return; }
            string team = RelayTeamBox.Text.Trim();
            if (string.IsNullOrEmpty(team)) { RelayStatusText.Text = "请输入队名"; return; }

            var legs = new JArray();
            TextBox[] nameBoxes = { Leg1Name, Leg2Name, Leg3Name, Leg4Name };
            TextBox[] idBoxes = { Leg1ID, Leg2ID, Leg3ID, Leg4ID };
            TextBox[] bibBoxes = { Leg1Bib, Leg2Bib, Leg3Bib, Leg4Bib };
            DatePicker[] birthPickers = { Leg1Birth, Leg2Birth, Leg3Birth, Leg4Birth };
            for (int i = 0; i < 4; i++) {
                string legName = nameBoxes[i].Text.Trim();
                if (!string.IsNullOrEmpty(legName)) {
                    // 2026-05-21 接力每棒出生日期同样支持手动输入 yyyy-MM-dd 或点日历选择
                    string legBdErr;
                    string legBd = ReadBirthDate(birthPickers[i], out legBdErr);
                    if (legBdErr != null) {
                        RelayStatusText.Text = string.Format("第{0}棒 {1}", i + 1, legBdErr);
                        RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        return;
                    }
                    var leg = new JObject();
                    leg["legOrder"] = i + 1;
                    leg["swimmerName"] = legName;
                    leg["swimmerIDNumber"] = idBoxes[i].Text.Trim();
                    leg["swimmerBibNumber"] = bibBoxes[i].Text.Trim();
                    leg["swimmerBirthDate"] = legBd;
                    legs.Add(leg);
                }
            }

            var data = new JObject();
            data["teamName"] = team;
            data["eventName"] = ((ComboBoxItem)RelayEventCombo.SelectedItem).Content.ToString();
            data["gender"] = ((ComboBoxItem)RelayGenderCombo.SelectedItem).Content.ToString();
            data["ageGroup"] = ReadComboText(RelayAgeGroupCombo);
            data["countryShort"] = RelayCountryShortBox.Text.Trim();
            data["entryTime"] = RelayEntryTimeBox.Text.Trim();
            data["legs"] = legs;

            // 2026-05-21：原来这里只显示"已提交"乐观提示，不等服务器回执 — 即使服务器
            // 因队名/项目/棒次为空拒掉，用户也以为成功。现在改为等服务器回的 REGISTER_RELAY_RESULT
            // 才在 OnServerMessage 里显示绿色"已新建/已更新"或红色"接力报名失败: ..."。
            bool sent = _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_RELAY", data = data }));
            if (!sent) {
                RelayStatusText.Text = "发送失败：与主服务器的连接已断开，请重新点击\"连接\"后再提交";
                RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                return;
            }
            RelayStatusText.Text = string.Format("正在提交 {0}（{1} 棒）...（等待主服务器确认）", team, legs.Count);
            RelayStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
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
            error = "出生日期格式不正确，请填 yyyy-MM-dd（4 位年份），或点日历选择：" + text;
            return null;
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (_ws != null) _ws.Close();
        }
    }
}
