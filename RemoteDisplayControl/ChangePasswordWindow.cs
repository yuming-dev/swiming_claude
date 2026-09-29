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
            Title = SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_Title");
            Width = 420; Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));

            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(new TextBlock {
                Text = SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_Title"), FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12)
            });

            _userBox = AddRow(sp, SwimmingScoreboard.Loc.T("Str_Win_ChangePwd_NewUsername"), CredentialStore.CurrentUser());
            _oldBox = AddPwdRow(sp, SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_OldPassword"));
            _newBox = AddPwdRow(sp, SwimmingScoreboard.Loc.T("Str_Win_ChangePwd_New"));
            _newBox2 = AddPwdRow(sp, SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_ConfirmNew"));

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
                Content = SwimmingScoreboard.Loc.T("Str_Btn_Cancel"), Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnCancel.Click += delegate { Close(); };
            var btnOK = new Button {
                Content = SwimmingScoreboard.Loc.T("Str_Btn_OK"), Padding = new Thickness(24, 6, 24, 6), IsDefault = true,
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
            if (string.IsNullOrEmpty(newUser)) { _status.Text = SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_UsernameEmpty"); return; }
            if (string.IsNullOrEmpty(newPwd)) { _status.Text = SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_NewEmpty"); return; }
            if (newPwd != newPwd2) { _status.Text = SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_Mismatch"); return; }
            if (!CredentialStore.Change(oldPwd, newUser, newPwd)) {
                _status.Text = SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_OldWrong");
                return;
            }
            // 改完密码后清掉"记住"，强制下次手动输
            CredentialStore.ClearRemembered();
            MessageBox.Show(SwimmingScoreboard.Loc.T("Str_StandaloneChangePwd_UpdatedMsg"),
                SwimmingScoreboard.Loc.T("Str_MsgTitle_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
