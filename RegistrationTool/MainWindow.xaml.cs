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
                SetConnState("未连接", LedRed, "连接");
                return;
            }
            string addr = ServerBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 3002;
            if (parts.Length > 1) int.TryParse(parts[1], out port);
            SetConnState("连接中…", LedAmber, "连接");
            try {
                _ws = new SimpleWebSocketClient();
                _ws.OnMessage += OnServerMessage;
                _ws.OnDisconnected += delegate() {
                    Dispatcher.Invoke((Action)delegate() {
                        SetConnState("连接断开", LedRed, "连接");
                    });
                };
                _ws.Connect(host, port);
                _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_TERMINAL_IDENTITY" }));
                SetConnState("已连接 " + host + ":" + port, LedGreen, "断开");
            } catch (Exception ex) {
                SetConnState("连接失败: " + ex.Message, LedAmber, "连接");
            }
        }

        private void OnServerMessage(string json) {
            Dispatcher.Invoke((Action)delegate() {
                try {
                    var msg = JObject.Parse(json);
                    if (msg["type"] != null && msg["type"].ToString() == "REGISTER_RESULT") {
                        var data = msg["data"];
                        bool ok = data != null && data["success"] != null && (bool)data["success"];
                        if (ok) {
                            _assignedBib = data["bibNumber"] != null ? data["bibNumber"].ToString() : _assignedBib;
                            _submitted = true;
                            RegStatusText.Text = string.Format("报名成功！参赛号: {0}。如需修改，可重新编辑后再次提交。", _assignedBib);
                            RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                        } else {
                            string errMsg = data != null && data["message"] != null ? data["message"].ToString() : "未知错误";
                            RegStatusText.Text = "报名失败: " + errMsg;
                            RegStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
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

            string birthDate = BirthDatePicker.SelectedDate.HasValue ? BirthDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") : "";
            int age = 0;
            if (BirthDatePicker.SelectedDate.HasValue) {
                var today = DateTime.Today;
                var bd = BirthDatePicker.SelectedDate.Value;
                age = today.Year - bd.Year;
                if (bd.Date > today.AddYears(-age)) age--;
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

            _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_SWIMMER_BATCH", data = msgData }));
            RegStatusText.Text = string.Format("正在提交 {0} 个项目...", _events.Count);
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
                    var leg = new JObject();
                    leg["legOrder"] = i + 1;
                    leg["swimmerName"] = legName;
                    leg["swimmerIDNumber"] = idBoxes[i].Text.Trim();
                    leg["swimmerBibNumber"] = bibBoxes[i].Text.Trim();
                    leg["swimmerBirthDate"] = birthPickers[i].SelectedDate.HasValue
                        ? birthPickers[i].SelectedDate.Value.ToString("yyyy-MM-dd") : "";
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

            _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_RELAY", data = data }));
            RelayStatusText.Text = string.Format("已提交: {0} ({1}人)", team, legs.Count);
        }

        // 读取可编辑 ComboBox 的当前值（兼容选项+自由输入）
        private static string ReadComboText(ComboBox cb) {
            if (cb == null) return "";
            var cbi = cb.SelectedItem as ComboBoxItem;
            if (cbi != null && cbi.Content != null) return cbi.Content.ToString().Trim();
            return (cb.Text ?? "").Trim();
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (_ws != null) _ws.Close();
        }
    }
}
