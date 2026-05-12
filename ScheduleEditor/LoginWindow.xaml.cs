using System.Windows;
using System.Windows.Input;

namespace ScheduleEditor
{
    public partial class LoginWindow : Window
    {
        public LoginWindow() {
            InitializeComponent();
            string savedUser, savedPass;
            if (CredentialStore.TryLoadRemembered(out savedUser, out savedPass)) {
                UsernameBox.Text = savedUser;
                PasswordBox.Password = savedPass;
                RememberMe.IsChecked = true;
                PasswordBox.Focus();
            } else {
                UsernameBox.Text = CredentialStore.CurrentUser();
                PasswordBox.Focus();
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
            string user = (UsernameBox.Text ?? "").Trim();
            string pwd = PasswordBox.Password ?? "";
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pwd)) {
                ShowError("请输入用户名和密码。");
                return;
            }
            if (!CredentialStore.Verify(user, pwd)) {
                ShowError("用户名或密码错误，请重试。");
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }
            // 记住 / 清除
            if (RememberMe.IsChecked == true) CredentialStore.SaveRemembered(user, pwd);
            else CredentialStore.ClearRemembered();

            // 登录成功 → 主窗口
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            this.Close();
        }

        private void ShowError(string msg) {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
