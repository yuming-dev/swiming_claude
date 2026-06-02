// 2026-06-01 大屏样式远程控制窗口 (code-only WPF, 无 XAML)
// 通过 SimpleWebSocketClient 发 SET_DISPLAY_STYLE / GET_DISPLAY_STYLE; 服务器广播 DISPLAY_STYLE_PUSH
// 回来后由 MainWindow 转发到本窗口的 ApplyRemoteStyle.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RemoteDisplayControl
{
    internal class DisplayStyleWindow : Window
    {
        private readonly SimpleWebSocketClient _ws;
        private bool _suppress;

        // BG controls
        private TextBox _bgHex;

        // FS controls
        private TextBlock _fsLabel;
        private double _fs = 1.0;

        // Text style controls (9 keys)
        private static readonly TextKeyDef[] TEXT_KEYS = new TextKeyDef[] {
            new TextKeyDef("title",  "比赛名称", "#f8fafc", "'Microsoft YaHei', sans-serif"),
            new TextKeyDef("event",  "比赛项目", "#f8fafc", "'Microsoft YaHei', sans-serif"),
            new TextKeyDef("time",   "滚动时间", "#f59e0b", "'Consolas', monospace"),
            new TextKeyDef("lane",   "道次",     "#94a3b8", "'Microsoft YaHei', sans-serif"),
            new TextKeyDef("rank",   "名次",     "#f8fafc", "'Microsoft YaHei', sans-serif"),
            new TextKeyDef("name",   "姓名",     "#f8fafc", "'Microsoft YaHei', sans-serif"),
            new TextKeyDef("team",   "代表队",   "#94a3b8", "'Microsoft YaHei', sans-serif"),
            new TextKeyDef("result", "成绩",     "#f8fafc", "'Consolas', monospace"),
            new TextKeyDef("remark", "备注",     "#ef4444", "'Microsoft YaHei', sans-serif")
        };
        private static readonly FontDef[] FONT_OPTIONS = new FontDef[] {
            new FontDef("微软雅黑", "'Microsoft YaHei', sans-serif"),
            new FontDef("黑体",     "SimHei, sans-serif"),
            new FontDef("宋体",     "SimSun, serif"),
            new FontDef("楷体",     "KaiTi, serif"),
            new FontDef("仿宋",     "FangSong, serif"),
            new FontDef("隶书",     "LiSu, serif"),
            new FontDef("幼圆",     "YouYuan, sans-serif"),
            new FontDef("Arial",    "Arial, sans-serif"),
            new FontDef("Consolas (等宽)", "Consolas, monospace"),
            new FontDef("Impact",   "Impact, sans-serif")
        };
        private TextBox[] _tsHex;
        private ComboBox[] _tsFont;

        public DisplayStyleWindow(SimpleWebSocketClient ws) {
            _ws = ws;
            Title = "🎨 大屏样式远程控制";
            Width = 640;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x29, 0x3b));

            var root = new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16)
            };
            var stack = new StackPanel();
            root.Content = stack;
            Content = root;

            stack.Children.Add(MakeHeader("改动立即推送到所有大屏 display.html (服务器持久化保存)"));

            // ── BG section ──
            stack.Children.Add(MakeSectionTitle("底色 (Background)"));
            var bgPanel = MakeSectionPanel();
            stack.Children.Add(bgPanel);
            var bgRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0,0,0,6) };
            bgPanel.Children.Add(bgRow);
            var bgLabel = new TextBlock { Text = "CSS:", Foreground = Brushes.LightGray, Margin = new Thickness(0,0,8,0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(bgLabel, Dock.Left);
            bgRow.Children.Add(bgLabel);
            _bgHex = new TextBox {
                Background = new SolidColorBrush(Color.FromRgb(0x0f,0x17,0x2a)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33,0x41,0x55)),
                Padding = new Thickness(6,4,6,4),
                FontFamily = new FontFamily("Consolas")
            };
            _bgHex.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e) {
                if (e.Key == System.Windows.Input.Key.Enter) SendBg(_bgHex.Text);
            };
            _bgHex.LostFocus += delegate { SendBg(_bgHex.Text); };
            bgRow.Children.Add(_bgHex);
            // Preset buttons
            var presetGrid = new UniformGrid { Columns = 8, Rows = 1, Margin = new Thickness(0,0,0,0) };
            string[,] presets = new string[,] {
                {"深邃蓝","#0f172a"}, {"泳池青","#0c4a6e"}, {"深海蓝","#082f49"}, {"森林绿","#14532d"},
                {"紫罗兰","#312e81"}, {"炭墨黑","#0a0a0a"}, {"暗紫红","#581c87"}, {"青灰","#1e293b"}
            };
            for (int i = 0; i < presets.GetLength(0); i++) {
                string name = presets[i,0];
                string val  = presets[i,1];
                var b = new Button {
                    Content = name,
                    Background = HexBrush(val),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(2),
                    FontSize = 10,
                    Padding = new Thickness(2)
                };
                b.Click += delegate { SendBg(val); };
                presetGrid.Children.Add(b);
            }
            bgPanel.Children.Add(presetGrid);

            // ── FS section ──
            stack.Children.Add(MakeSectionTitle("字号 (0.8x ~ 3.0x)"));
            var fsPanel = MakeSectionPanel();
            stack.Children.Add(fsPanel);
            var fsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            fsPanel.Children.Add(fsRow);
            var fsMinus = new Button { Content = "−", Width = 50, Height = 36, Margin = new Thickness(4), FontSize = 18, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0x47,0x55,0x69)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            fsMinus.Click += delegate { SendFs(_fs - 0.1); };
            fsRow.Children.Add(fsMinus);
            _fsLabel = new TextBlock {
                Text = "1.0x", Width = 100, TextAlignment = TextAlignment.Center,
                FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xf5,0x9e,0x0b)),
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            };
            fsRow.Children.Add(_fsLabel);
            var fsPlus = new Button { Content = "+", Width = 50, Height = 36, Margin = new Thickness(4), FontSize = 18, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0x47,0x55,0x69)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            fsPlus.Click += delegate { SendFs(_fs + 0.1); };
            fsRow.Children.Add(fsPlus);
            var fsReset = new Button { Content = "复位 1.0x", Width = 80, Height = 36, Margin = new Thickness(12,4,4,4), Background = new SolidColorBrush(Color.FromRgb(0x64,0x74,0x8b)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            fsReset.Click += delegate { SendFs(1.0); };
            fsRow.Children.Add(fsReset);

            // ── TextStyle section ──
            stack.Children.Add(MakeSectionTitle("文字颜色 / 字体 (9 处)"));
            var tsPanel = MakeSectionPanel();
            stack.Children.Add(tsPanel);
            _tsHex = new TextBox[TEXT_KEYS.Length];
            _tsFont = new ComboBox[TEXT_KEYS.Length];
            for (int i = 0; i < TEXT_KEYS.Length; i++) {
                var def = TEXT_KEYS[i];
                int idx = i;
                var row = new Grid { Margin = new Thickness(0,2,0,2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                var lab = new TextBlock { Text = def.Label, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lab, 0); row.Children.Add(lab);
                var hex = new TextBox {
                    Text = def.DefaultColor,
                    Background = new SolidColorBrush(Color.FromRgb(0x0f,0x17,0x2a)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x33,0x41,0x55)),
                    Padding = new Thickness(4,2,4,2),
                    Margin = new Thickness(0,0,4,0),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center
                };
                hex.LostFocus += delegate { SendTextColor(def.Key, hex.Text); };
                hex.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e) {
                    if (e.Key == System.Windows.Input.Key.Enter) SendTextColor(def.Key, hex.Text);
                };
                Grid.SetColumn(hex, 1); row.Children.Add(hex);
                _tsHex[idx] = hex;
                var combo = new ComboBox {
                    Margin = new Thickness(0,0,4,0),
                    Background = new SolidColorBrush(Color.FromRgb(0x0f,0x17,0x2a)),
                    Foreground = Brushes.White,
                    FontSize = 12
                };
                foreach (var f in FONT_OPTIONS) {
                    var item = new ComboBoxItem { Content = f.Label, Tag = f.Value, Foreground = Brushes.Black };
                    combo.Items.Add(item);
                    if (f.Value == def.DefaultFont) combo.SelectedItem = item;
                }
                combo.SelectionChanged += delegate {
                    var it = combo.SelectedItem as ComboBoxItem;
                    if (it != null && it.Tag != null) SendTextFont(def.Key, it.Tag.ToString());
                };
                Grid.SetColumn(combo, 2); row.Children.Add(combo);
                _tsFont[idx] = combo;
                var resetBtn = new Button {
                    Content = "↺",
                    Background = new SolidColorBrush(Color.FromRgb(0x33,0x41,0x55)),
                    Foreground = Brushes.LightGray,
                    BorderThickness = new Thickness(0),
                    FontSize = 13
                };
                resetBtn.Click += delegate {
                    SendTextColor(def.Key, def.DefaultColor);
                    SendTextFont(def.Key, def.DefaultFont);
                };
                Grid.SetColumn(resetBtn, 3); row.Children.Add(resetBtn);
                tsPanel.Children.Add(row);
            }

            // 拉一次当前样式
            if (_ws != null && _ws.IsConnected) {
                try { _ws.Send(JsonConvert.SerializeObject(new { type = "GET_DISPLAY_STYLE" })); } catch { }
            }
        }

        // 服务器推 DISPLAY_STYLE_PUSH 后 MainWindow 转发到这里同步 UI
        public void ApplyRemoteStyle(JObject data) {
            if (data == null) return;
            _suppress = true;
            try {
                if (data["bg"] != null) {
                    string bg = data["bg"].ToString();
                    if (_bgHex != null) _bgHex.Text = bg;
                }
                if (data["fs"] != null) {
                    double v;
                    if (double.TryParse(data["fs"].ToString(), out v) && v > 0) {
                        _fs = ClampFs(v);
                        if (_fsLabel != null) _fsLabel.Text = _fs.ToString("0.0") + "x";
                    }
                }
                var ts = data["textStyle"] as JObject;
                if (ts != null) {
                    for (int i = 0; i < TEXT_KEYS.Length; i++) {
                        var def = TEXT_KEYS[i];
                        var item = ts[def.Key] as JObject;
                        if (item == null) continue;
                        if (item["c"] != null && _tsHex[i] != null) _tsHex[i].Text = item["c"].ToString();
                        if (item["f"] != null && _tsFont[i] != null) {
                            string fv = item["f"].ToString();
                            foreach (ComboBoxItem ci in _tsFont[i].Items) {
                                if (ci.Tag != null && ci.Tag.ToString() == fv) { _tsFont[i].SelectedItem = ci; break; }
                            }
                        }
                    }
                }
            } finally { _suppress = false; }
        }

        private void SendBg(string v) {
            if (_suppress || _ws == null || !_ws.IsConnected || string.IsNullOrEmpty(v)) return;
            try { _ws.Send(JsonConvert.SerializeObject(new { type = "SET_DISPLAY_STYLE", data = new { bg = v } })); } catch { }
        }
        private void SendFs(double v) {
            if (_suppress) return;
            v = ClampFs(v);
            _fs = v;
            if (_fsLabel != null) _fsLabel.Text = v.ToString("0.0") + "x";
            if (_ws == null || !_ws.IsConnected) return;
            try { _ws.Send(JsonConvert.SerializeObject(new { type = "SET_DISPLAY_STYLE", data = new { fs = v } })); } catch { }
        }
        private void SendTextColor(string key, string color) {
            if (_suppress || string.IsNullOrEmpty(color)) return;
            if (_ws == null || !_ws.IsConnected) return;
            var add = new JObject(); add[key] = new JObject { ["c"] = color };
            var msg = new JObject {
                ["type"] = "SET_DISPLAY_STYLE",
                ["data"] = new JObject { ["textStyle"] = add, ["textStyleMerge"] = true }
            };
            try { _ws.Send(msg.ToString(Formatting.None)); } catch { }
        }
        private void SendTextFont(string key, string font) {
            if (_suppress || string.IsNullOrEmpty(font)) return;
            if (_ws == null || !_ws.IsConnected) return;
            var add = new JObject(); add[key] = new JObject { ["f"] = font };
            var msg = new JObject {
                ["type"] = "SET_DISPLAY_STYLE",
                ["data"] = new JObject { ["textStyle"] = add, ["textStyleMerge"] = true }
            };
            try { _ws.Send(msg.ToString(Formatting.None)); } catch { }
        }

        private static double ClampFs(double v) {
            v = Math.Round(v * 10) / 10.0;
            if (v < 0.8) v = 0.8;
            if (v > 3.0) v = 3.0;
            return v;
        }
        private static SolidColorBrush HexBrush(string hex) {
            try {
                if (hex.StartsWith("#")) hex = hex.Substring(1);
                if (hex.Length == 6) {
                    byte r = Convert.ToByte(hex.Substring(0,2), 16);
                    byte g = Convert.ToByte(hex.Substring(2,2), 16);
                    byte b = Convert.ToByte(hex.Substring(4,2), 16);
                    return new SolidColorBrush(Color.FromRgb(r, g, b));
                }
            } catch { }
            return new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        }
        private static TextBlock MakeHeader(string s) {
            return new TextBlock {
                Text = s,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10)
            };
        }
        private static TextBlock MakeSectionTitle(string s) {
            return new TextBlock {
                Text = s,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0e, 0xa5, 0xe9)),
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 4)
            };
        }
        private static StackPanel MakeSectionPanel() {
            var p = new StackPanel {
                Background = new SolidColorBrush(Color.FromRgb(0x0f, 0x17, 0x2a))
            };
            p.Margin = new Thickness(0, 0, 0, 6);
            return p;
        }

        private class TextKeyDef {
            public string Key, Label, DefaultColor, DefaultFont;
            public TextKeyDef(string k, string l, string c, string f) { Key = k; Label = l; DefaultColor = c; DefaultFont = f; }
        }
        private class FontDef {
            public string Label, Value;
            public FontDef(string l, string v) { Label = l; Value = v; }
        }
    }
}
