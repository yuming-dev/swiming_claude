using System;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;

namespace RemoteDisplayControl
{
    public partial class MainWindow : Window
    {
        private SimpleWebSocketClient _ws;

        public MainWindow() {
            InitializeComponent();
        }

        private void Connect_Click(object sender, RoutedEventArgs e) {
            if (_ws != null && _ws.IsConnected) {
                _ws.Close();
                StatusText.Text = "未连接";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                ConnectBtn.Content = "连接";
                return;
            }

            string addr = AddressBox.Text.Trim();
            string[] parts = addr.Split(':');
            string host = parts[0];
            int port = 3002;
            if (parts.Length > 1) int.TryParse(parts[1], out port);

            try {
                _ws = new SimpleWebSocketClient();
                _ws.OnMessage += delegate(string msg) {
                    // 接收但不处理
                };
                _ws.OnDisconnected += delegate() {
                    Dispatcher.Invoke((Action)delegate() {
                        StatusText.Text = "连接断开";
                        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                        ConnectBtn.Content = "连接";
                    });
                };
                _ws.Connect(host, port);
                StatusText.Text = "已连接: " + addr;
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
                ConnectBtn.Content = "断开";
            } catch (Exception ex) {
                StatusText.Text = "连接失败: " + ex.Message;
            }
        }

        private void SendMode_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) {
                StatusText.Text = "请先连接服务器";
                return;
            }
            string mode = ((Button)sender).Tag.ToString();
            _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = mode }));
            StatusText.Text = "已发送: " + mode;
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (_ws != null) _ws.Close();
        }
    }
}
