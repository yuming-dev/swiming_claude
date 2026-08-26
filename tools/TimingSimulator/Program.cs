using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TimingSimulator
{
    internal static class Program
    {
        // 不声明 DPI 感知的话, 在 125%/150% 缩放的屏幕上 Windows 会把整个窗口
        // 当位图拉大: 字全是虚的, 而且程序拿到的是"虚拟分辨率", 按它算出来的
        // 窗口尺寸乘上缩放后会超出真实屏幕。必须在建任何窗口之前调用。
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
