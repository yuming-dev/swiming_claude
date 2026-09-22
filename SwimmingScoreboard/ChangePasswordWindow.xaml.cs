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
                ShowMessage(Loc.T("Str_Win_ChangePwd_MsgEnterCurrent"), false); return;
            }
            var creds = AuthHelper.LoadCredentials();
            if (!string.Equals(creds.PasswordHash, AuthHelper.HashPassword(currentPassword), StringComparison.Ordinal)) {
                ShowMessage(Loc.T("Str_Win_ChangePwd_MsgWrongCurrent"), false); return;
            }
            if (string.IsNullOrEmpty(newUsername)) {
                ShowMessage(Loc.T("Str_Win_ChangePwd_MsgUsernameEmpty"), false); return;
            }
            if (string.IsNullOrEmpty(newPassword)) {
                ShowMessage(Loc.T("Str_Win_ChangePwd_MsgNewEmpty"), false); return;
            }
            if (newPassword.Length < 6) {
                ShowMessage(Loc.T("Str_Win_ChangePwd_MsgTooShort"), false); return;
            }
            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal)) {
                ShowMessage(Loc.T("Str_Win_ChangePwd_MsgMismatch"), false); return;
            }

            creds.Username = newUsername;
            creds.PasswordHash = AuthHelper.HashPassword(newPassword);
            AuthHelper.SaveCredentials(creds);
            ShowMessage(Loc.T("Str_Win_ChangePwd_MsgSuccess"), true);
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
