using System;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RegistrationTool
{
    public partial class MainWindow : Window
    {
        private SimpleWebSocketClient _ws;
        private string _lastBib = "";

        public MainWindow() {
            InitializeComponent();
        }

        private void Connect_Click(object sender, RoutedEventArgs e) {
            if (_ws != null && _ws.IsConnected) {
                _ws.Close();
                StatusText.Text = "未连接";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                ConnBtn.Content = "连接";
                return;
            }
            string addr = ServerBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 3002;
            if (parts.Length > 1) int.TryParse(parts[1], out port);
            try {
                _ws = new SimpleWebSocketClient();
                _ws.OnMessage += delegate(string msg) { };
                _ws.OnDisconnected += delegate() {
                    Dispatcher.Invoke((Action)delegate() {
                        StatusText.Text = "连接断开";
                        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        ConnBtn.Content = "连接";
                    });
                };
                _ws.Connect(host, port);
                _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_TERMINAL_IDENTITY" }));
                StatusText.Text = "已连接";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                ConnBtn.Content = "断开";
            } catch (Exception ex) {
                StatusText.Text = "失败: " + ex.Message;
            }
        }

        private JObject BuildSwimmerData() {
            string name = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { RegStatusText.Text = "请输入姓名"; return null; }
            string birthDate = BirthDatePicker.SelectedDate.HasValue ? BirthDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd") : "";
            int age = 0;
            if (BirthDatePicker.SelectedDate.HasValue) {
                var today = DateTime.Today;
                var bd = BirthDatePicker.SelectedDate.Value;
                age = today.Year - bd.Year;
                if (bd.Date > today.AddYears(-age)) age--;
            }
            var obj = new JObject();
            obj["bibNumber"] = BibBox.Text.Trim();
            obj["name"] = name;
            obj["gender"] = ((ComboBoxItem)GenderCombo.SelectedItem).Content.ToString();
            obj["age"] = age;
            obj["country"] = CountryBox.Text.Trim();
            obj["idNumber"] = IDNumberBox.Text.Trim();
            obj["phone"] = PhoneBox.Text.Trim();
            obj["eventName"] = EventCombo.SelectedItem != null ? ((ComboBoxItem)EventCombo.SelectedItem).Content.ToString() : "";
            obj["entryTime"] = EntryTimeBox.Text.Trim();
            obj["birthDate"] = birthDate;
            obj["csaNumber"] = CSABox.Text.Trim();
            obj["notes"] = NotesBox.Text.Trim();
            return obj;
        }

        private void Register_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { RegStatusText.Text = "请先连接服务器"; return; }
            var data = BuildSwimmerData();
            if (data == null) return;
            _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_SWIMMER", data = data }));
            _lastBib = data["bibNumber"].ToString();
            RegStatusText.Text = string.Format("已提交: {0} - {1}", data["name"], data["eventName"]);
            EntryTimeBox.Clear();
            NotesBox.Clear();
        }

        private void AddEvent_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { RegStatusText.Text = "请先连接服务器"; return; }
            if (string.IsNullOrEmpty(_lastBib) && string.IsNullOrEmpty(BibBox.Text.Trim())) {
                RegStatusText.Text = "请先注册第一个项目";
                return;
            }
            var data = BuildSwimmerData();
            if (data == null) return;
            if (!string.IsNullOrEmpty(_lastBib)) data["bibNumber"] = _lastBib;
            _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_SWIMMER", data = data }));
            RegStatusText.Text = string.Format("已为 {0} 增报: {1}", data["name"], data["eventName"]);
            EntryTimeBox.Clear();
            NotesBox.Clear();
        }

        private void RelayRegister_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { RelayStatusText.Text = "请先连接服务器"; return; }
            string team = RelayTeamBox.Text.Trim();
            if (string.IsNullOrEmpty(team)) { RelayStatusText.Text = "请输入队名"; return; }

            var legs = new JArray();
            TextBox[] nameBoxes = { Leg1Name, Leg2Name, Leg3Name, Leg4Name };
            TextBox[] bibBoxes = { Leg1Bib, Leg2Bib, Leg3Bib, Leg4Bib };
            for (int i = 0; i < 4; i++) {
                string legName = nameBoxes[i].Text.Trim();
                if (!string.IsNullOrEmpty(legName)) {
                    var leg = new JObject();
                    leg["legOrder"] = i + 1;
                    leg["swimmerName"] = legName;
                    leg["swimmerBibNumber"] = bibBoxes[i].Text.Trim();
                    legs.Add(leg);
                }
            }

            var data = new JObject();
            data["teamName"] = team;
            data["eventName"] = ((ComboBoxItem)RelayEventCombo.SelectedItem).Content.ToString();
            data["gender"] = ((ComboBoxItem)RelayGenderCombo.SelectedItem).Content.ToString();
            data["entryTime"] = RelayEntryTimeBox.Text.Trim();
            data["legs"] = legs;

            _ws.Send(JsonConvert.SerializeObject(new { type = "REGISTER_RELAY", data = data }));
            RelayStatusText.Text = string.Format("已提交: {0} ({1}人)", team, legs.Count);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (_ws != null) _ws.Close();
        }
    }
}
