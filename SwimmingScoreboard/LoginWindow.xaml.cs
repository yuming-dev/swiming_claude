using System;
using System.Windows;
using System.Windows.Input;

namespace SwimmingScoreboard
{
    public partial class LoginWindow : Window
    {
        public LoginWindow() {
            InitializeComponent();
            string savedUser, savedPass;
            if (AuthHelper.TryLoadRemembered(out savedUser, out savedPass)) {
                UsernameBox.Text = savedUser;
                PasswordBox.Password = savedPass;
                RememberMe.IsChecked = true;
                PasswordBox.Focus();
            } else {
                UsernameBox.Focus();
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e) {
            DoLogin();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.Key == Key.Enter) DoLogin();
        }

        private void DoLogin() {
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) {
                ShowError("请输入用户名和密码。"); return;
            }
            if (AuthHelper.Verify(username, password)) {
                if (RememberMe.IsChecked == true)
                    AuthHelper.SaveRemembered(username, password);
                else
                    AuthHelper.ClearRemembered();
                DialogResult = true;
                Close();
            } else {
                ShowError("用户名或密码错误，请重试。");
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }

        private void ShowError(string msg) {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
