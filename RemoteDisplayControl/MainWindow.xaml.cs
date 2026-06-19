using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RemoteDisplayControl
{
    public partial class MainWindow : Window
    {
        private SimpleWebSocketClient _ws;
        private DisplayStyleWindow _displayStyleWin;   // 2026-06-01 大屏样式远程控制窗口
        // 2026-06-17 远程化: 缓存主控广播的 schedule, 给"成绩发布"用
        private JArray _scheduleData;
        // 4 个本地弹 Window (列表填充 / 重用)
        private Window _mediaWin, _pptWin, _scheduleWin, _publishWin;
        private ListBox _mediaList, _pptList, _scheduleList, _publishList;
        // 2026-06-17 大屏预览 WebView2 是否已就绪 (异步 Init 完成后置 true)
        private bool _previewReady = false;

        // 2026-06-18 服务器地址持久化文件 (exe 同目录 rdc_server.json)
        private string ServerAddrFile {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rdc_server.json"); }
        }

        public MainWindow() {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            // 读取上次保存的服务器地址 (在 InitializeComponent 之后, AddressBox 已可访问)
            try {
                if (System.IO.File.Exists(ServerAddrFile)) {
                    string addr = System.IO.File.ReadAllText(ServerAddrFile, System.Text.Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(addr)) AddressBox.Text = addr;
                }
            } catch { }
        }

        // 2026-06-17 异步初始化 WebView2 (要求 WebView2 Runtime 已装, Win10/11 一般预装).
        //   失败时只警告, 不影响主功能 (按钮仍可用).
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            try {
                await BigPreviewWebView.EnsureCoreWebView2Async(null);
                _previewReady = true;
                // 已连接 → 立即把 display.html 加载进预览
                if (_ws != null && _ws.IsConnected) TryNavigatePreview();
            } catch (Exception ex) {
                StatusText.Text = "大屏预览初始化失败 (装 WebView2 Runtime 后重启): " + ex.Message;
                StatusText.Foreground = new SolidColorBrush(Colors.Orange);
            }
            UpdatePreviewSize(PreviewSizeSlider.Value);
        }

        // 2026-06-18 预览大小调节: vwPct 是预览宽度占窗口宽度的百分比 (12.5 ~ 50 = 1/8 ~ 1/2)
        //   右栏控件区 460 宽 + margin, cap 预览宽度不超过左列可用宽度避免溢出
        private void UpdatePreviewSize(double vwPct) {
            if (BigPreviewBorder == null) return;
            double winW = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
            double w = winW * vwPct / 100.0;
            double leftColMax = winW - 460 - 36;   // 右列 460 + Margin(12+12) + 内边距
            if (leftColMax > 100 && w > leftColMax) w = leftColMax;
            double h = w * 9.0 / 16.0;
            BigPreviewBorder.Width = w;
            BigPreviewBorder.Height = h;
            if (PreviewPctText != null) PreviewPctText.Text = (Math.Round(vwPct * 10) / 10).ToString("0.0") + "%";
        }

        private void PreviewSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            UpdatePreviewSize(e.NewValue);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (PreviewSizeSlider != null) UpdatePreviewSize(PreviewSizeSlider.Value);
        }

        // 2026-06-18 优先用本地 file:///Web/display.html?host=主服务器IP 加载 (主服务器 HTTP 8080 需 admin/netsh, 不可靠).
        //   display.html 自身已支持 ?host= URL 参数 (getServerHost 902 行优先取它), WebSocket 仍连主服务器 3002.
        //   本地 Web/ 目录由 build_installer.ps1 从主端拷贝.
        private void TryNavigatePreview() {
            if (!_previewReady) return;
            try {
                string addr = AddressBox.Text.Trim();
                string host = addr.Split(':')[0];
                if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string htmlPath = System.IO.Path.Combine(baseDir, "Web", "display.html");
                string url;
                if (System.IO.File.Exists(htmlPath)) {
                    url = "file:///" + htmlPath.Replace('\\', '/') + "?host=" + host;
                } else {
                    // 兜底: 走主服务器 HTTP (= 旧逻辑, 可能因 8080 端口绑定问题失败)
                    url = "http://" + host + ":8080/display.html";
                }
                if (BigPreviewWebView != null && BigPreviewWebView.CoreWebView2 != null) {
                    BigPreviewWebView.CoreWebView2.Navigate(url);
                }
            } catch { }
        }

        private void Connect_Click(object sender, RoutedEventArgs e) {
            if (_ws != null && _ws.IsConnected) {
                _ws.Close();
                StatusText.Text = "未连接";
                StatusText.Foreground = new SolidColorBrush(Colors.Red);
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
                _ws.OnMessage += delegate(string raw) {
                    try {
                        var msg = JObject.Parse(raw);
                        string type = msg["type"] != null ? msg["type"].ToString() : "";
                        // 2026-06-01 大屏样式
                        if (type == "DISPLAY_STYLE_PUSH") {
                            var data = msg["data"] as JObject;
                            Dispatcher.Invoke((Action)delegate() {
                                if (_displayStyleWin != null && _displayStyleWin.IsLoaded) _displayStyleWin.ApplyRemoteStyle(data);
                            });
                            return;
                        }
                        // 2026-06-17 远程化 list 响应
                        if (type == "MEDIA_FILE_LIST") {
                            var data = msg["data"] as JObject;
                            Dispatcher.Invoke((Action)delegate() { RenderMediaList(data); });
                            return;
                        }
                        if (type == "PPT_FILE_LIST") {
                            var data = msg["data"] as JObject;
                            Dispatcher.Invoke((Action)delegate() { RenderPptList(data); });
                            return;
                        }
                        if (type == "SCHEDULE_SESSION_LIST") {
                            var data = msg["data"] as JObject;
                            Dispatcher.Invoke((Action)delegate() { RenderScheduleList(data); });
                            return;
                        }
                        // 缓存 schedule 给"成绩发布"用 (跟着主控的常规广播过来)
                        var dat = msg["data"] as JObject;
                        if (dat != null && dat["schedule"] != null) {
                            _scheduleData = dat["schedule"] as JArray;
                        }
                    } catch { }
                };
                _ws.OnDisconnected += delegate() {
                    Dispatcher.Invoke((Action)delegate() {
                        StatusText.Text = "连接断开";
                        StatusText.Foreground = new SolidColorBrush(Colors.Red);
                        ConnectBtn.Content = "连接";
                    });
                };
                _ws.Connect(host, port);
                StatusText.Text = "已连接: " + addr;
                StatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
                ConnectBtn.Content = "断开";
                // 2026-06-18 连接成功后保存地址, 下次启动自动填入
                try { System.IO.File.WriteAllText(ServerAddrFile, addr, System.Text.Encoding.UTF8); } catch { }
                TryNavigatePreview();   // 2026-06-17 连上后立即加载大屏预览
            } catch (Exception ex) {
                StatusText.Text = "连接失败: " + ex.Message;
            }
        }

        // 顶部右上角"修改用户名和密码"按钮 — 弹 ChangePasswordWindow，凭据存 display_credentials.json
        private void ChangePassword_Click(object sender, RoutedEventArgs e) {
            var dlg = new ChangePasswordWindow();
            dlg.Owner = this;
            dlg.ShowDialog();
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

        // 2026-06-01 打开大屏样式远程控制窗口
        private void OpenDisplayStyle_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) {
                MessageBox.Show("请先连接服务器", "未连接", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_displayStyleWin != null && _displayStyleWin.IsLoaded) {
                _displayStyleWin.Activate();
                return;
            }
            _displayStyleWin = new DisplayStyleWindow(_ws);
            _displayStyleWin.Owner = this;
            _displayStyleWin.Show();
        }

        // ════════════════════════════════════════════════════════════
        // 2026-06-17 远程化 4 个本地弹框 Window
        // ════════════════════════════════════════════════════════════

        // 通用: 建简单 ListBox Window
        private Window BuildSimpleListWindow(string title, Brush titleBrush, out ListBox listBox, params Tuple<string, Brush, RoutedEventHandler>[] extraButtons) {
            var win = new Window {
                Title = title, Width = 480, Height = 540,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"))
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = titleBrush ?? Brushes.White, Margin = new Thickness(0, 0, 0, 8) });
            var lb = new ListBox { Height = 380, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")), Foreground = Brushes.White, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), FontSize = 13 };
            sp.Children.Add(lb);
            listBox = lb;
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            if (extraButtons != null) {
                foreach (var b in extraButtons) {
                    var btn = new Button { Content = b.Item1, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), Background = b.Item2, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold };
                    btn.Click += b.Item3;
                    btnRow.Children.Add(btn);
                }
            }
            var btnClose = new Button { Content = "关闭", Padding = new Thickness(14, 6, 14, 6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            btnClose.Click += delegate { win.Close(); };
            btnRow.Children.Add(btnClose);
            sp.Children.Add(btnRow);
            win.Content = sp;
            return win;
        }

        // 图片 / 视频
        private void OpenMediaDialog_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { MessageBox.Show("请先连接服务器", "未连接"); return; }
            _mediaWin = BuildSimpleListWindow("🖼️ 图片 / 视频 (主控 PC 文件; 双击播放 / 或本机选文件)", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")), out _mediaList,
                new Tuple<string, Brush, RoutedEventHandler>("从本机选文件...", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")), delegate { UploadLocalMedia(); }),
                new Tuple<string, Brush, RoutedEventHandler>("停止显示", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")), delegate {
                    _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = "STOP_MEDIA" }));
                    StatusText.Text = "已发送: 停止媒体显示";
                    if (_mediaWin != null) _mediaWin.Close();
                }));
            _mediaList.MouseDoubleClick += delegate {
                var item = _mediaList.SelectedItem as ListBoxItem;
                if (item == null) return;
                string fn = item.Tag as string;
                if (string.IsNullOrEmpty(fn)) return;
                _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = "PLAY_MEDIA_FILE", fileName = fn, fit = "contain", loop = true, muted = false, autoplay = true }));
                StatusText.Text = "已发送播放: " + fn;
                _mediaWin.Close();
            };
            _mediaList.Items.Add(new ListBoxItem { Content = "加载中...", IsEnabled = false });
            _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = "LIST_MEDIA_FILES" }));
            _mediaWin.Show();
        }
        private void RenderMediaList(JObject data) {
            if (_mediaList == null) return;
            _mediaList.Items.Clear();
            var files = data != null ? data["files"] as JArray : null;
            if (files == null || files.Count == 0) {
                _mediaList.Items.Add(new ListBoxItem { Content = "主控 PC Media/ 目录暂无文件", IsEnabled = false });
                return;
            }
            foreach (JObject f in files) {
                string nm = f["name"] != null ? f["name"].ToString() : "";
                double sz = f["sizeMB"] != null ? (double)f["sizeMB"] : 0;
                var item = new ListBoxItem { Content = string.Format("{0}    ({1:F2} MB)", nm, sz), Tag = nm, Foreground = Brushes.White };
                _mediaList.Items.Add(item);
            }
        }

        // PPT 播放
        private void OpenPptDialog_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { MessageBox.Show("请先连接服务器", "未连接"); return; }
            _pptWin = BuildSimpleListWindow("📊 PPT 播放 (主控 PC 文件; 双击 / 或本机选文件; 翻页在主控)", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB923C")), out _pptList,
                new Tuple<string, Brush, RoutedEventHandler>("从本机选文件...", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")), delegate { UploadLocalPpt(); }));
            _pptList.MouseDoubleClick += delegate {
                var item = _pptList.SelectedItem as ListBoxItem;
                if (item == null) return;
                string fn = item.Tag as string;
                if (string.IsNullOrEmpty(fn)) return;
                _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = "PLAY_PPT_FILE", fileName = fn }));
                StatusText.Text = "已请求播放 PPT (翻页在主控): " + fn;
                _pptWin.Close();
            };
            _pptList.Items.Add(new ListBoxItem { Content = "加载中...", IsEnabled = false });
            _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = "LIST_PPT_FILES" }));
            _pptWin.Show();
        }
        private void RenderPptList(JObject data) {
            if (_pptList == null) return;
            _pptList.Items.Clear();
            var files = data != null ? data["files"] as JArray : null;
            if (files == null || files.Count == 0) {
                _pptList.Items.Add(new ListBoxItem { Content = "主控 PC Documents/PPT/ 目录暂无 PPT", IsEnabled = false });
                return;
            }
            foreach (JObject f in files) {
                string nm = f["name"] != null ? f["name"].ToString() : "";
                _pptList.Items.Add(new ListBoxItem { Content = nm, Tag = nm, Foreground = Brushes.White });
            }
        }

        // 显示比赛日程
        private void OpenScheduleDialog_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { MessageBox.Show("请先连接服务器", "未连接"); return; }
            _scheduleWin = BuildSimpleListWindow("📅 显示比赛日程", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D9488")), out _scheduleList);
            _scheduleList.MouseDoubleClick += delegate {
                var item = _scheduleList.SelectedItem as ListBoxItem;
                if (item == null) return;
                int sn = item.Tag is int ? (int)item.Tag : -1;
                _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = "SHOW_SCHEDULE_SESSION", session = sn }));
                StatusText.Text = sn < 0 ? "已发送: 显示全部场次" : ("已发送: 显示第" + sn + "场");
                _scheduleWin.Close();
            };
            _scheduleList.Items.Add(new ListBoxItem { Content = "加载中...", IsEnabled = false });
            _ws.Send(JsonConvert.SerializeObject(new { type = "REMOTE_CONTROL", command = "LIST_SCHEDULE_SESSIONS" }));
            _scheduleWin.Show();
        }
        private void RenderScheduleList(JObject data) {
            if (_scheduleList == null) return;
            _scheduleList.Items.Clear();
            // 第一项: 全部场次
            _scheduleList.Items.Add(new ListBoxItem { Content = "📅 全部场次", Tag = -1, Foreground = Brushes.White, FontWeight = FontWeights.Bold });
            var sessions = data != null ? data["sessions"] as JArray : null;
            if (sessions == null || sessions.Count == 0) return;
            foreach (JObject s in sessions) {
                int sn = s["session"] != null ? (int)s["session"] : 0;
                string label = s["label"] != null ? s["label"].ToString() : ("第" + sn + "场");
                _scheduleList.Items.Add(new ListBoxItem { Content = label, Tag = sn, Foreground = Brushes.White });
            }
        }

        // 成绩发布 — 用本地缓存的 _scheduleData (主控广播过来的) 构建已完赛列表
        private void OpenPublishDialog_Click(object sender, RoutedEventArgs e) {
            if (_ws == null || !_ws.IsConnected) { MessageBox.Show("请先连接服务器", "未连接"); return; }
            if (_scheduleData == null || _scheduleData.Count == 0) {
                MessageBox.Show("暂未收到主控的赛程数据, 请稍后再试 (或先点其他按钮触发主控广播)", "无数据");
                return;
            }
            _publishWin = BuildSimpleListWindow("成绩发布 (双击发布到大屏)", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")), out _publishList);
            _publishList.MouseDoubleClick += delegate {
                var item = _publishList.SelectedItem as ListBoxItem;
                if (item == null) return;
                string tagStr = item.Tag as string;
                if (string.IsNullOrEmpty(tagStr)) return;
                // Tag 是 "ageGroup|gender|event|stage|heat" 拼接字符串, Split 解析
                var p = tagStr.Split('|');
                if (p.Length < 5) return;
                int heatNo;
                int.TryParse(p[4], out heatNo);
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "TIMING_CMD", command = "PUBLISH_RESULT",
                    data = new { ageGroup = p[0], gender = p[1], eventName = p[2], stage = p[3], heat = heatNo }
                }));
                StatusText.Text = "已请求发布: " + p[1] + " " + p[2] + " " + p[3];
                _publishWin.Close();
            };
            // 直接填充本地 schedule 缓存的已完赛组
            int found = 0;
            foreach (JObject s in _scheduleData) {
                int hc = s["heatCount"] != null ? (int)s["heatCount"] : 1;
                var heatConfirmed = s["heatConfirmed"] as JArray;
                if (heatConfirmed == null) continue;
                for (int h = 0; h < hc && h < heatConfirmed.Count; h++) {
                    if (!(bool)heatConfirmed[h]) continue;
                    string ageG = s["ageGroup"] != null ? s["ageGroup"].ToString() : "";
                    string gender = s["gender"] != null ? s["gender"].ToString() : "";
                    string evName = s["eventName"] != null ? s["eventName"].ToString() : "";
                    string stage = s["stage"] != null ? s["stage"].ToString() : "";
                    bool showHeat = (hc > 1) || stage.Contains("预赛") || stage.Contains("半决赛");
                    string heatLabel = showHeat ? string.Format(" 第{0}组", h + 1) : "";
                    string ageHead = string.IsNullOrEmpty(ageG) ? "" : (ageG + " ");
                    string label = string.Format("{0}{1} {2} {3}{4}", ageHead, gender, evName, stage, heatLabel);
                    // 用 "|" 拼接为字符串 Tag (避免 dynamic 依赖)
                    string tagStr = ageG + "|" + gender + "|" + evName + "|" + stage + "|" + (h + 1);
                    _publishList.Items.Add(new ListBoxItem { Content = label, Tag = tagStr, Foreground = Brushes.White });
                    found++;
                }
            }
            if (found == 0) _publishList.Items.Add(new ListBoxItem { Content = "暂无已完赛的比赛项目", IsEnabled = false });
            _publishWin.Show();
        }

        // 2026-06-17 双模式 — 本机选文件上传到主控播放
        private void UploadLocalMedia() {
            var ofd = new Microsoft.Win32.OpenFileDialog {
                Title = "选择图片/视频 (本机文件)",
                Filter = "图片/视频 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.mp4;*.webm;*.ogg;*.m4v)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.mp4;*.webm;*.ogg;*.m4v|所有文件|*.*"
            };
            if (ofd.ShowDialog() != true) return;
            string path = ofd.FileName;
            if (!System.IO.File.Exists(path)) { MessageBox.Show("文件不存在"); return; }
            string ext = (System.IO.Path.GetExtension(path) ?? "").ToLower();
            string kind, mime;
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".webp") {
                kind = "image";
                mime = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : ext == ".bmp" ? "image/bmp" : ext == ".gif" ? "image/gif" : ext == ".webp" ? "image/webp" : "image/png";
            } else if (ext == ".mp4" || ext == ".webm" || ext == ".ogg" || ext == ".m4v") {
                kind = "video";
                mime = ext == ".webm" ? "video/webm" : ext == ".ogg" ? "video/ogg" : "video/mp4";
            } else { MessageBox.Show("不支持的文件类型"); return; }
            try {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                if (bytes.Length > 64 * 1024 * 1024) {
                    if (MessageBox.Show(string.Format("文件较大 ({0:F1} MB), 上传可能耗时. 继续?", bytes.Length / 1048576.0), "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                }
                string b64 = Convert.ToBase64String(bytes);
                string dataUrl = "data:" + mime + ";base64," + b64;
                StatusText.Text = "上传中: " + System.IO.Path.GetFileName(path);
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "REMOTE_CONTROL", command = "UPLOAD_AND_PLAY_MEDIA",
                    fileName = System.IO.Path.GetFileName(path), kind = kind, mime = mime, dataUrl = dataUrl,
                    fit = "contain", loop = true, muted = false, autoplay = true
                }));
                StatusText.Text = "已上传: " + System.IO.Path.GetFileName(path);
                if (_mediaWin != null) _mediaWin.Close();
            } catch (Exception ex) { MessageBox.Show("上传失败: " + ex.Message); }
        }

        private void UploadLocalPpt() {
            var ofd = new Microsoft.Win32.OpenFileDialog {
                Title = "选择 PPT (本机文件)",
                Filter = "PowerPoint (*.ppt;*.pptx;*.pps;*.ppsx)|*.ppt;*.pptx;*.pps;*.ppsx|所有文件|*.*"
            };
            if (ofd.ShowDialog() != true) return;
            string path = ofd.FileName;
            if (!System.IO.File.Exists(path)) { MessageBox.Show("文件不存在"); return; }
            try {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                if (bytes.Length > 64 * 1024 * 1024) {
                    if (MessageBox.Show(string.Format("文件较大 ({0:F1} MB), 上传可能耗时. 继续?", bytes.Length / 1048576.0), "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                }
                string b64 = Convert.ToBase64String(bytes);
                StatusText.Text = "上传中: " + System.IO.Path.GetFileName(path);
                _ws.Send(JsonConvert.SerializeObject(new {
                    type = "REMOTE_CONTROL", command = "UPLOAD_AND_PLAY_PPT",
                    fileName = System.IO.Path.GetFileName(path), base64 = b64
                }));
                StatusText.Text = "已上传 PPT (主控转换 + 翻页): " + System.IO.Path.GetFileName(path);
                if (_pptWin != null) _pptWin.Close();
            } catch (Exception ex) { MessageBox.Show("上传失败: " + ex.Message); }
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (_displayStyleWin != null) { try { _displayStyleWin.Close(); } catch { } }
            if (_mediaWin != null) { try { _mediaWin.Close(); } catch { } }
            if (_pptWin != null) { try { _pptWin.Close(); } catch { } }
            if (_scheduleWin != null) { try { _scheduleWin.Close(); } catch { } }
            if (_publishWin != null) { try { _publishWin.Close(); } catch { } }
            if (_ws != null) _ws.Close();
        }
    }
}
