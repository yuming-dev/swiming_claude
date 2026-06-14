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
            TrySetWebBrowserIE11();   // 2026-06-13 内嵌 WebBrowser(文档预览)用 IE11 标准模式渲染, 否则默认 IE7 quirks 下表头等 CSS 不生效
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

        // 2026-06-13 让本程序内嵌的 WPF WebBrowser(文档预览/输出) 用 IE11 标准模式渲染.
        //   默认 hosted WebBrowser 跑 IE7 quirks, 导致 th 等 CSS/居中 在预览与"打印"里不生效.
        //   写 HKCU FEATURE_BROWSER_EMULATION (无需管理员), 在创建任何 WebBrowser 前设好即可.
        private static void TrySetWebBrowserIE11() {
            try {
                string exeName = System.IO.Path.GetFileName(
                    System.Reflection.Assembly.GetEntryAssembly().Location);
                if (string.IsNullOrEmpty(exeName)) return;
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION")) {
                    if (key != null) {
                        // 11001 = IE11 edge 模式(忽略 !DOCTYPE, 强制标准模式渲染)
                        key.SetValue(exeName, 11001, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
            } catch { }
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
