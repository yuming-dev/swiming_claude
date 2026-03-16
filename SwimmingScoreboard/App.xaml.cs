using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SwimmingScoreboard
{
    public partial class App : Application
    {
        public App() {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_Startup(object sender, StartupEventArgs e) {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AuthHelper.EnsureDefaultCredentials();
            var loginWin = new LoginWindow();
            bool? result = loginWin.ShowDialog();
            if (result != true) {
                Shutdown(); return;
            }
            try {
                var mainWin = new MainWindow();
                MainWindow = mainWin;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWin.Show();
            } catch (Exception ex) {
                WriteErrorLog(ex);
                MessageBox.Show("启动失败:\n" + ex.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
            WriteErrorLog(e.Exception);
            MessageBox.Show("未处理的异常:\n" + e.Exception.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            var ex = e.ExceptionObject as Exception;
            if (ex != null) WriteErrorLog(ex);
        }

        private static void WriteErrorLog(Exception ex) {
            try {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                string msg = string.Format("[{0}] {1}\r\n{2}\r\n\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ex.Message, ex.StackTrace);
                File.AppendAllText(logPath, msg, Encoding.UTF8);
            } catch { }
        }
    }
}
