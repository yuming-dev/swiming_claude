using System.Windows;

namespace SwimmingScoreboard
{
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e) {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AuthHelper.EnsureDefaultCredentials();
            var loginWin = new LoginWindow();
            bool? result = loginWin.ShowDialog();
            if (result != true) {
                Shutdown(); return;
            }
            var mainWin = new MainWindow();
            MainWindow = mainWin;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWin.Show();
        }
    }
}
