using System;
using System.Windows;
using System.Windows.Media;

namespace SwimmingScoreboard
{
    public partial class ChangePasswordWindow : Window
    {
        public ChangePasswordWindow() {
            InitializeComponent();
            var creds = AuthHelper.LoadCredentials();
            if (creds != null) NewUsernameBox.Text = creds.Username;
            CurrentPasswordBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            string currentPassword = CurrentPasswordBox.Password;
            string newUsername = NewUsernameBox.Text.Trim();
            string newPassword = NewPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(currentPassword)) {
                ShowMessage("请输入当前密码。", false); return;
            }
            var creds = AuthHelper.LoadCredentials();
            if (!string.Equals(creds.PasswordHash, AuthHelper.HashPassword(currentPassword), StringComparison.Ordinal)) {
                ShowMessage("当前密码错误。", false); return;
            }
            if (string.IsNullOrEmpty(newUsername)) {
                ShowMessage("新用户名不能为空。", false); return;
            }
            if (string.IsNullOrEmpty(newPassword)) {
                ShowMessage("新密码不能为空。", false); return;
            }
            if (newPassword.Length < 6) {
                ShowMessage("新密码长度不能少于6位。", false); return;
            }
            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal)) {
                ShowMessage("两次输入的新密码不一致。", false); return;
            }

            creds.Username = newUsername;
            creds.PasswordHash = AuthHelper.HashPassword(newPassword);
            AuthHelper.SaveCredentials(creds);
            ShowMessage("账号密码修改成功！", true);
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private void ShowMessage(string msg, bool success) {
            MessageText.Text = msg;
            MessageText.Foreground = success
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
    }
}
