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
                ShowError(SwimmingScoreboard.Loc.T("Str_LoginWin_ErrEmptyFields"));
                return;
            }
            if (!CredentialStore.Verify(user, pwd)) {
                ShowError(SwimmingScoreboard.Loc.T("Str_LoginWin_ErrWrongCreds"));
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }
            // 记住 / 清除
            if (RememberMe.IsChecked == true) CredentialStore.SaveRemembered(user, pwd);
            else CredentialStore.ClearRemembered();

            // 登录成功 → 主服务器 MainWindow（编排模式：构造函数自动识别入口程序集名为 ScheduleEditor，
            // 并隐藏与编排无关的标签页，跳过 WebSocket 服务/计时硬件初始化）
            SwimmingScoreboard.Loc.LoadSaved();
            SwimmingScoreboard.Loc.Apply();
            var main = new SwimmingScoreboard.MainWindow();
            Application.Current.MainWindow = main;
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
            this.Close();
        }

        private void ShowError(string msg) {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
