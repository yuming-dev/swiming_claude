using System.Windows;
using System.Windows.Input;

namespace ScheduleEditor
{
    public partial class LoginWindow : Window
    {
        public LoginWindow() {
            InitializeComponent();
            UserBox.Text = CredentialStore.CurrentUser();
            PasswordBox.Focus();
        }

        private void Login_Click(object sender, RoutedEventArgs e) {
            string user = UserBox.Text ?? "";
            string pwd = PasswordBox.Password ?? "";
            if (string.IsNullOrEmpty(user.Trim())) { StatusText.Text = "请输入用户名"; return; }
            if (!CredentialStore.Verify(user, pwd)) {
                StatusText.Text = "用户名或密码错误";
                PasswordBox.SelectAll();
                PasswordBox.Focus();
                return;
            }
            // 登录成功 → 打开主窗口
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            this.Close();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) Login_Click(sender, e);
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e) {
            var dlg = new ChangePasswordWindow();
            dlg.Owner = this;
            dlg.ShowDialog();
            // 改完后回到登录窗口，用户重新输入新密码
            UserBox.Text = CredentialStore.CurrentUser();
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 修改密码弹窗（嵌在同一文件减少零碎页面）
    // ═══════════════════════════════════════════════════════════════
    public class ChangePasswordWindow : Window
    {
        private System.Windows.Controls.TextBox _userBox;
        private System.Windows.Controls.PasswordBox _oldBox, _newBox, _newBox2;
        private System.Windows.Controls.TextBlock _status;

        public ChangePasswordWindow() {
            Title = "修改用户名 / 密码";
            Width = 420; Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A));

            var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(new System.Windows.Controls.TextBlock {
                Text = "修改用户名 / 密码", FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 12)
            });

            _userBox = AddRow(sp, "新用户名", CredentialStore.CurrentUser());
            _oldBox = AddPwdRow(sp, "旧密码");
            _newBox = AddPwdRow(sp, "新密码");
            _newBox2 = AddPwdRow(sp, "确认新密码");

            _status = new System.Windows.Controls.TextBlock {
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)),
                FontSize = 13, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(_status);

            var btnPanel = new System.Windows.Controls.StackPanel {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0)
            };
            var btnCancel = new System.Windows.Controls.Button {
                Content = "取消", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x47, 0x55, 0x69)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0)
            };
            btnCancel.Click += delegate { Close(); };
            var btnOK = new System.Windows.Controls.Button {
                Content = "确定", Padding = new Thickness(24, 6, 24, 6), IsDefault = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            btnOK.Click += OnOk;
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOK);
            sp.Children.Add(btnPanel);

            Content = sp;
        }

        private System.Windows.Controls.TextBox AddRow(System.Windows.Controls.StackPanel parent, string label, string value) {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new System.Windows.Controls.TextBlock {
                Text = label, FontSize = 14,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(lbl, 0);
            var tb = new System.Windows.Controls.TextBox {
                Text = value ?? "", Padding = new Thickness(6), FontSize = 14,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x47, 0x55, 0x69))
            };
            System.Windows.Controls.Grid.SetColumn(tb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(tb);
            parent.Children.Add(grid);
            return tb;
        }

        private System.Windows.Controls.PasswordBox AddPwdRow(System.Windows.Controls.StackPanel parent, string label) {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new System.Windows.Controls.TextBlock {
                Text = label, FontSize = 14,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(lbl, 0);
            var pb = new System.Windows.Controls.PasswordBox {
                Padding = new Thickness(6), FontSize = 14,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x47, 0x55, 0x69))
            };
            System.Windows.Controls.Grid.SetColumn(pb, 1);
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
            MessageBox.Show("用户名/密码已更新。\n请用新凭据重新登录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
