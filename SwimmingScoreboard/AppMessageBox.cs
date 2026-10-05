using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwimmingScoreboard
{
    // 2026-10-05 现场反馈: CSV导入成功弹窗里, 标题/正文都是English, 唯独"确定"按钮还是中文。
    //
    // 根因: 全系统 ~470 处 MessageBox.Show() 调用，背后包的是 Win32 原生消息框——OK/取消/
    // 是/否 这几个按钮的文字，是 Windows 自己从 user32.dll 的 MUI(多语言资源)里按"当前线程
    // UI 语言"取的，跟本系统这套 Loc.Table/DynamicResource 翻译机制完全是两条不搭界的路。
    // 之前试过 Thread.CurrentUICulture 和 SetThreadUILanguage() 两种办法让 Windows 自己切，
    // 实测在没装英文语言包的机器上都不生效——Windows 在找不到对应语言的 MUI 资源时会静默
    // 回退到系统默认语言，这不是应用程序代码能单方面控制的。
    //
    // 用户确认后选择的方案: 不再依赖 Windows 系统语言，把 OK/取消/是/否 按钮自己画，文字从
    // 咱们自己的 Loc.Table 取——这样无论装不装英文语言包，这几个按钮永远跟着 Loc.CurrentLanguage
    // 走。API 签名故意跟 System.Windows.MessageBox.Show() 保持一致（这批调用点本来就只用到
    // text/caption/button/icon 这几个重载，没用到 Window-owner / defaultResult 版本），这样
    // 全系统只需要把 "MessageBox.Show(" 批量替换成 "AppMessageBox.Show(" 就能接上，不用逐个
    // 调用点改参数。
    //
    // 放在 Localization.cs 同一个共享目录下、走一样的 <Compile Include> 链接方式，5 个 exe
    // (SwimmingScoreboard/RemoteTimingControl/ScheduleEditor/RemoteDisplayControl/
    // RegistrationTool) 都能直接用。
    public static class AppMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText) {
            return Show(messageBoxText, "", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption) {
            return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) {
            return Show(messageBoxText, caption, button, MessageBoxImage.None);
        }

        // 2026-10-05 少量调用点显式传了 owner(如 MainWindow.xaml.cs 里 Show(this, msg, ...))——
        // 补一个同签名重载, 原样接回 MessageBox.Show(Window, string, string, MessageBoxButton,
        // MessageBoxImage) 那一个重载, 批量替换时不用改这几处调用参数。
        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) {
            return Show(messageBoxText, caption, button, icon, owner);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) {
            return Show(messageBoxText, caption, button, icon, null);
        }

        private static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, Window explicitOwner) {
            MessageBoxResult result = (button == MessageBoxButton.YesNo || button == MessageBoxButton.YesNoCancel)
                ? MessageBoxResult.No
                : (button == MessageBoxButton.OKCancel ? MessageBoxResult.Cancel : MessageBoxResult.OK);

            Window owner = explicitOwner ?? FindOwnerWindow();
            var win = new Window {
                Title = caption ?? "",
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 340, MaxWidth = 640, MaxHeight = 560,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = owner == null,
                Owner = owner,
                Background = Brushes.White
            };

            var root = new Grid { Margin = new Thickness(20, 18, 20, 14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var contentRow = new StackPanel { Orientation = Orientation.Horizontal };
            string glyph; Brush glyphBrush;
            GetIconGlyph(icon, out glyph, out glyphBrush);
            if (glyph != null) {
                contentRow.Children.Add(new TextBlock {
                    Text = glyph, FontSize = 30, Foreground = glyphBrush,
                    Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top
                });
            }

            var scroller = new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420, MaxWidth = glyph != null ? 520 : 560
            };
            scroller.Content = new TextBlock {
                Text = messageBoxText ?? "", TextWrapping = TextWrapping.Wrap, FontSize = 13
            };
            contentRow.Children.Add(scroller);
            Grid.SetRow(contentRow, 0);
            root.Children.Add(contentRow);

            var btnRow = new StackPanel {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            Grid.SetRow(btnRow, 1);

            Action<MessageBoxResult> close = r => { result = r; win.DialogResult = true; };
            MessageBoxResult escResult = MessageBoxResult.OK;

            switch (button) {
                case MessageBoxButton.YesNo: {
                    var bYes = MakeButton(Loc.T("Str_Btn_Yes"), true);
                    bYes.Click += delegate { close(MessageBoxResult.Yes); };
                    var bNo = MakeButton(Loc.T("Str_Btn_No"), false);
                    bNo.Click += delegate { close(MessageBoxResult.No); };
                    btnRow.Children.Add(bYes); btnRow.Children.Add(bNo);
                    escResult = MessageBoxResult.No;
                    break;
                }
                case MessageBoxButton.YesNoCancel: {
                    var bYes = MakeButton(Loc.T("Str_Btn_Yes"), true);
                    bYes.Click += delegate { close(MessageBoxResult.Yes); };
                    var bNo = MakeButton(Loc.T("Str_Btn_No"), false);
                    bNo.Click += delegate { close(MessageBoxResult.No); };
                    var bCancel = MakeButton(Loc.T("Str_Btn_Cancel"), false);
                    bCancel.Click += delegate { close(MessageBoxResult.Cancel); };
                    btnRow.Children.Add(bYes); btnRow.Children.Add(bNo); btnRow.Children.Add(bCancel);
                    escResult = MessageBoxResult.Cancel;
                    break;
                }
                case MessageBoxButton.OKCancel: {
                    var bOk = MakeButton(Loc.T("Str_Btn_OK"), true);
                    bOk.Click += delegate { close(MessageBoxResult.OK); };
                    var bCancel = MakeButton(Loc.T("Str_Btn_Cancel"), false);
                    bCancel.Click += delegate { close(MessageBoxResult.Cancel); };
                    btnRow.Children.Add(bOk); btnRow.Children.Add(bCancel);
                    escResult = MessageBoxResult.Cancel;
                    break;
                }
                default: {
                    var bOk = MakeButton(Loc.T("Str_Btn_OK"), true);
                    bOk.Click += delegate { close(MessageBoxResult.OK); };
                    btnRow.Children.Add(bOk);
                    escResult = MessageBoxResult.OK;
                    break;
                }
            }
            win.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) close(escResult); };
            root.Children.Add(btnRow);
            win.Content = root;

            try { System.Media.SystemSounds.Beep.Play(); } catch { }
            win.ShowDialog();
            return result;
        }

        private static Button MakeButton(string text, bool isDefault) {
            return new Button {
                Content = text, Padding = new Thickness(18, 6, 18, 6), MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault,
                Background = isDefault ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")) : Brushes.WhiteSmoke,
                Foreground = isDefault ? Brushes.White : Brushes.Black,
                BorderThickness = new Thickness(isDefault ? 0 : 1),
                BorderBrush = Brushes.LightGray, FontWeight = isDefault ? FontWeights.Bold : FontWeights.Normal
            };
        }

        private static void GetIconGlyph(MessageBoxImage icon, out string glyph, out Brush brush) {
            switch (icon) {
                case MessageBoxImage.Error:       glyph = "⛔"; brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")); break;   // ⛔
                case MessageBoxImage.Warning:     glyph = "⚠"; brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")); break;   // ⚠
                case MessageBoxImage.Information: glyph = "ℹ"; brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")); break;   // ℹ
                case MessageBoxImage.Question:    glyph = "❓"; brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")); break;   // ❓
                default:                          glyph = null;    brush = Brushes.Black; break;
            }
        }

        // 2026-10-05 原生 MessageBox.Show() 不传 owner 时, Windows 自己会挑"当前活动窗口"当家长;
        // 这里手动找一遍: 先找真正 IsActive 的窗口, 找不到就退回 Application.MainWindow。
        private static Window FindOwnerWindow() {
            try {
                var app = Application.Current;
                if (app == null) return null;
                foreach (Window w in app.Windows) {
                    if (w != null && w.IsActive && w.IsLoaded) return w;
                }
                if (app.MainWindow != null && app.MainWindow.IsLoaded) return app.MainWindow;
            } catch { }
            return null;
        }
    }
}
