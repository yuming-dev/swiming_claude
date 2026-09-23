using System.Windows;

namespace RegistrationTool
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e) {
            // 2026-09-22 语言设置要在登录窗口出现之前就生效，原来靠 StartupUri 直接弹
            // LoginWindow, 没机会先跑 Loc.LoadSaved()，登录界面永远是中文。
            SwimmingScoreboard.Loc.LoadSaved();
            SwimmingScoreboard.Loc.Apply();
            var loginWin = new LoginWindow();
            loginWin.Show();
        }
    }
}
