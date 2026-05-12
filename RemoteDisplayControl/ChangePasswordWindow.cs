using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RemoteDisplayControl
{
    // 修改用户名 / 密码弹窗 — 由主窗口右上角"修改用户名和密码"按钮打开
    public class ChangePasswordWindow : Window
    {
        private TextBox _userBox;
        private PasswordBox _oldBox, _newBox, _newBox2;
        private TextBlock _status;

        public ChangePasswordWindow() {
            Title = "修改用户名 / 密码";
            Width = 420; Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));

            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(new TextBlock {
                Text = "修改用户名 / 密码", FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12)
            });

            _userBox = AddRow(sp, "新用户名", CredentialStore.CurrentUser());
            _oldBox = AddPwdRow(sp, "旧密码");
            _newBox = AddPwdRow(sp, "新密码");
            _newBox2 = AddPwdRow(sp, "确认新密码");

            _status = new TextBlock {
                Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
                FontSize = 13, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(_status);

            var btnPanel = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0)
            };
            var btnCancel = new Button {
                Content = "取消", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnCancel.Click += delegate { Close(); };
            var btnOK = new Button {
                Content = "确定", Padding = new Thickness(24, 6, 24, 6), IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            btnOK.Click += OnOk;
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOK);
            sp.Children.Add(btnPanel);

            Content = sp;
        }

        private TextBox AddRow(StackPanel parent, string label, string value) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var tb = new TextBox {
                Text = value ?? "", Padding = new Thickness(6), FontSize = 14,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                CaretBrush = Brushes.White
            };
            Grid.SetColumn(tb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(tb);
            parent.Children.Add(grid);
            return tb;
        }

        private PasswordBox AddPwdRow(StackPanel parent, string label) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var pb = new PasswordBox {
                Padding = new Thickness(6), FontSize = 14,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))
            };
            Grid.SetColumn(pb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(pb);
            parent.Children.Add(grid);
            return pb;
        }

        private void OnOk(object sender, RoutedEventArgs e) {
            string newUser = (_userBox.Text ?? "").Trim();
            string oldPwd = _oldBox.Password ?? "";
            string newPwd = _newBox.Password ?? "";
            string newPwd2 = _newBox2.Password ?? "";
            if (string.IsNullOrEmpty(newUser)) { _status.Text = "用户名不能为空"; return; }
            if (string.IsNullOrEmpty(newPwd)) { _status.Text = "新密码不能为空"; return; }
            if (newPwd != newPwd2) { _status.Text = "两次输入的新密码不一致"; return; }
            if (!CredentialStore.Change(oldPwd, newUser, newPwd)) {
                _status.Text = "旧密码错误";
                return;
            }
            // 改完密码后清掉"记住"，强制下次手动输
            CredentialStore.ClearRemembered();
            MessageBox.Show("用户名 / 密码已更新。\n下次启动需用新凭据登录。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
