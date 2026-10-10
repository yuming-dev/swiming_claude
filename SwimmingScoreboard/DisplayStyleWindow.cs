// 2026-06-01 主控 PC 端"大屏样式"远程控制窗口 (code-only WPF, 无 XAML)
// 与 RemoteDisplayControl 的同名窗口一致, 区别: 不走 WebSocket, 直接调用 MainWindow 的回调
// 修改服务器侧 _displayStyle* 字段 + BroadcastDisplayStyle 广播给所有客户端.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard
{
    public class DisplayStyleWindow : Window
    {
        // 2026-10-10 现场反馈两轮都没根治"字体下拉文字看不清"——根因其实是没retemplate的
        //   ComboBox 在 Windows 默认主题下, 收起状态那个"选择框"的底色是主题自己画的系统控件
        //   灰色调(不受 ComboBox.Background / ComboBoxItem.Background 影响, 这是 WPF 一个常见
        //   坑), 之前两次改 Foreground/Background 都只影响得到下拉展开后的列表项, 收起状态
        //   那个灰底子怎么调都调不掉, 文字颜色换来换去都是"灰底配浅色字"低对比度。彻底解决
        //   只能整个重写 ControlTemplate, 不依赖系统默认的 ToggleButton chrome——用 XAML 字符串
        //   通过 XamlReader.Parse 定义一个纯色深底模板(深色底框 + 白字选中框 + 深底弹出列表),
        //   跟窗口里其它控件(CSS 输入框等)已经验证可读的深底白字配色保持一致。
        private static ControlTemplate _darkComboTemplate;
        private static ControlTemplate DarkComboTemplate {
            get {
                if (_darkComboTemplate == null) {
                    const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                  xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                  TargetType='ComboBox'>
  <Grid>
    <ToggleButton Name='ToggleBtn' Focusable='False' ClickMode='Press'
                  IsChecked='{Binding Path=IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}'>
      <ToggleButton.Template>
        <ControlTemplate TargetType='ToggleButton'>
          <Border Background='#0F172A' BorderBrush='#475569' BorderThickness='1' CornerRadius='3'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='18'/>
              </Grid.ColumnDefinitions>
              <Path Grid.Column='1' Data='M0,0 L4,4 L8,0 Z' Fill='#CBD5E1' HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Name='ContentSite' IsHitTestVisible='False'
                       Content='{TemplateBinding SelectionBoxItem}'
                       ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                       ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}'
                       Margin='6,0,20,0' VerticalAlignment='Center' HorizontalAlignment='Left'
                       TextElement.Foreground='#F8FAFC'/>
    <Popup Name='Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}'
           AllowsTransparency='True' Focusable='False' PopupAnimation='Slide'>
      <Border Background='#1E293B' BorderBrush='#475569' BorderThickness='1' CornerRadius='3' MaxHeight='320'
              MinWidth='{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}'>
        <ScrollViewer SnapsToDevicePixels='True'>
          <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/>
        </ScrollViewer>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>";
                    _darkComboTemplate = (ControlTemplate)XamlReader.Parse(xaml);
                }
                return _darkComboTemplate;
            }
        }

        private readonly Action<JObject> _applyStyle;        // 把局部更新 (bg/fs/textStyle JObject) 写入服务器并广播
        private readonly Func<JObject>   _getCurrentStyle;   // 读当前 {bg, fs, textStyle}
        private bool _suppress;

        private TextBox _bgHex;
        private TextBlock _fsLabel;
        private double _fs = 1.0;
        private TextBox[] _tsHex;
        private ComboBox[] _tsFont;

        // 2026-09-28 懒加载(不能在静态字段初始化器里调 Loc.T——那时 Application.Current
        //   可能还没就绪), 每次开窗都按当前语言重建一次, 够用(这窗口不常开, 不需要活绑定)。
        // 2026-10-10 现场反馈: "滚动时间"/"成绩"两行的字体下拉打开就是空白——根因是这两行
        //   默认字体写成 "'Consolas', monospace"(带引号), 但下面 FONT_OPTIONS 里 Consolas
        //   那一项 Value 是 "Consolas, monospace"(不带引号, 跟 Arial/Impact 等其它单词字体名
        //   同一个写法, 只有带空格的"Microsoft YaHei"才需要引号)——两边字符串对不上,
        //   下面"if (f.Value == def.DefaultFont) 选中它"这行永远选不中, ComboBox 没有
        //   SelectedItem, 自然什么都不显示。统一去掉 Consolas 两处的多余引号。
        private static TextKeyDef[] BuildTextKeys() {
            return new TextKeyDef[] {
                new TextKeyDef("title",  Loc.T("Str_DisplayStyle_Key_Title"), "#f8fafc", "'Microsoft YaHei', sans-serif"),
                new TextKeyDef("event",  Loc.T("Str_EM_Events"), "#f8fafc", "'Microsoft YaHei', sans-serif"),
                new TextKeyDef("time",   Loc.T("Str_DisplayStyle_Key_RollingTime"), "#f59e0b", "Consolas, monospace"),
                new TextKeyDef("lane",   Loc.T("Str_Col_LaneNo"), "#94a3b8", "'Microsoft YaHei', sans-serif"),
                new TextKeyDef("rank",   Loc.T("Str_Results_ColRank"), "#f8fafc", "'Microsoft YaHei', sans-serif"),
                new TextKeyDef("name",   Loc.T("Str_Col_Name"), "#f8fafc", "'Microsoft YaHei', sans-serif"),
                new TextKeyDef("team",   Loc.T("Str_Col_Team"), "#94a3b8", "'Microsoft YaHei', sans-serif"),
                new TextKeyDef("result", Loc.T("Str_Col_RecordTime"), "#f8fafc", "Consolas, monospace"),
                new TextKeyDef("remark", Loc.T("Str_Col_Notes"), "#ef4444", "'Microsoft YaHei', sans-serif"),
                new TextKeyDef("record", Loc.T("Str_DisplayStyle_Key_Record"), "#FBBF24", "'Microsoft YaHei', sans-serif")
            };
        }
        private static FontDef[] BuildFontOptions() {
            return new FontDef[] {
                new FontDef(Loc.T("Str_Font_MicrosoftYaHei"), "'Microsoft YaHei', sans-serif"),
                new FontDef(Loc.T("Str_Font_SimHei"),     "SimHei, sans-serif"),
                new FontDef(Loc.T("Str_Font_SimSun"),     "SimSun, serif"),
                new FontDef(Loc.T("Str_Font_KaiTi"),      "KaiTi, serif"),
                new FontDef(Loc.T("Str_Font_FangSong"),   "FangSong, serif"),
                new FontDef(Loc.T("Str_Font_LiSu"),       "LiSu, serif"),
                new FontDef(Loc.T("Str_Font_YouYuan"),    "YouYuan, sans-serif"),
                new FontDef("Arial",    "Arial, sans-serif"),
                new FontDef(Loc.T("Str_Font_ConsolasMono"), "Consolas, monospace"),
                new FontDef("Impact",   "Impact, sans-serif")
            };
        }
        private readonly TextKeyDef[] TEXT_KEYS = BuildTextKeys();
        private readonly FontDef[] FONT_OPTIONS = BuildFontOptions();

        public DisplayStyleWindow(Action<JObject> applyStyle, Func<JObject> getCurrentStyle) {
            _applyStyle = applyStyle;
            _getCurrentStyle = getCurrentStyle;
            Title = Loc.T("Str_DisplayStyle_Title");
            Width = 640;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x29, 0x3b));

            var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) };
            var stack = new StackPanel();
            root.Content = stack;
            Content = root;

            stack.Children.Add(MakeHeader(Loc.T("Str_DisplayStyle_Header")));

            // ── BG ──
            stack.Children.Add(MakeSectionTitle(Loc.T("Str_DisplayStyle_SectionBg")));
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
            var presetGrid = new UniformGrid { Columns = 8, Rows = 1 };
            string[,] presets = new string[,] {
                {Loc.T("Str_BgPreset_DeepBlue"),"#0f172a"}, {Loc.T("Str_BgPreset_PoolTeal"),"#0c4a6e"},
                {Loc.T("Str_BgPreset_DeepSeaBlue"),"#082f49"}, {Loc.T("Str_BgPreset_ForestGreen"),"#14532d"},
                {Loc.T("Str_BgPreset_Violet"),"#312e81"}, {Loc.T("Str_BgPreset_CharcoalBlack"),"#0a0a0a"},
                {Loc.T("Str_BgPreset_DarkMagenta"),"#581c87"}, {Loc.T("Str_BgPreset_SlateGray"),"#1e293b"}
            };
            for (int i = 0; i < presets.GetLength(0); i++) {
                string name = presets[i,0];
                string val  = presets[i,1];
                var b = new Button {
                    Content = name, Background = HexBrush(val), Foreground = Brushes.White,
                    BorderThickness = new Thickness(0), Margin = new Thickness(2), FontSize = 10, Padding = new Thickness(2)
                };
                b.Click += delegate { SendBg(val); };
                presetGrid.Children.Add(b);
            }
            bgPanel.Children.Add(presetGrid);

            // ── FS ──
            stack.Children.Add(MakeSectionTitle(Loc.T("Str_DisplayStyle_SectionFs")));
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
            var fsReset = new Button { Content = Loc.T("Str_DisplayStyle_FsReset"), Width = 80, Height = 36, Margin = new Thickness(12,4,4,4), Background = new SolidColorBrush(Color.FromRgb(0x64,0x74,0x8b)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
            fsReset.Click += delegate { SendFs(1.0); };
            fsRow.Children.Add(fsReset);

            // ── TextStyle ──
            stack.Children.Add(MakeSectionTitle(Loc.T("Str_DisplayStyle_SectionText")));
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
                    Padding = new Thickness(4,2,4,2), Margin = new Thickness(0,0,4,0),
                    FontFamily = new FontFamily("Consolas"), FontSize = 11,
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
                    Foreground = Brushes.White, FontSize = 12,
                    Template = DarkComboTemplate   // 见类顶部 DarkComboTemplate 的说明
                };
                var itemBg = new SolidColorBrush(Color.FromRgb(0x33,0x41,0x55));
                foreach (var f in FONT_OPTIONS) {
                    var item = new ComboBoxItem { Content = f.Label, Tag = f.Value, Foreground = Brushes.White, Background = itemBg };
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
                    Content = "↺", Background = new SolidColorBrush(Color.FromRgb(0x33,0x41,0x55)),
                    Foreground = Brushes.LightGray, BorderThickness = new Thickness(0), FontSize = 13
                };
                resetBtn.Click += delegate {
                    SendTextColor(def.Key, def.DefaultColor);
                    SendTextFont(def.Key, def.DefaultFont);
                };
                Grid.SetColumn(resetBtn, 3); row.Children.Add(resetBtn);
                tsPanel.Children.Add(row);
            }

            // 初始拉一次当前样式同步 UI
            if (_getCurrentStyle != null) {
                try { ApplyRemoteStyle(_getCurrentStyle()); } catch { }
            }
        }

        // 服务器侧任意路径修改样式后, 由 MainWindow 调用此方法同步 UI
        public void ApplyRemoteStyle(JObject data) {
            if (data == null) return;
            _suppress = true;
            try {
                if (data["bg"] != null && _bgHex != null) _bgHex.Text = data["bg"].ToString();
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
            if (_suppress || _applyStyle == null || string.IsNullOrEmpty(v)) return;
            _applyStyle(new JObject { ["bg"] = v });
        }
        private void SendFs(double v) {
            if (_suppress) return;
            v = ClampFs(v);
            _fs = v;
            if (_fsLabel != null) _fsLabel.Text = v.ToString("0.0") + "x";
            if (_applyStyle != null) _applyStyle(new JObject { ["fs"] = v });
        }
        private void SendTextColor(string key, string color) {
            if (_suppress || _applyStyle == null || string.IsNullOrEmpty(color)) return;
            var add = new JObject(); add[key] = new JObject { ["c"] = color };
            _applyStyle(new JObject { ["textStyle"] = add, ["textStyleMerge"] = true });
        }
        private void SendTextFont(string key, string font) {
            if (_suppress || _applyStyle == null || string.IsNullOrEmpty(font)) return;
            var add = new JObject(); add[key] = new JObject { ["f"] = font };
            _applyStyle(new JObject { ["textStyle"] = add, ["textStyleMerge"] = true });
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
            return new TextBlock { Text = s, Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)), FontSize = 11, Margin = new Thickness(0, 0, 0, 10) };
        }
        private static TextBlock MakeSectionTitle(string s) {
            return new TextBlock { Text = s, Foreground = new SolidColorBrush(Color.FromRgb(0x0e, 0xa5, 0xe9)), FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 8, 0, 4) };
        }
        private static StackPanel MakeSectionPanel() {
            var p = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x0f, 0x17, 0x2a)) };
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
