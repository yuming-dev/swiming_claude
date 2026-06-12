using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SwimmingScoreboard
{
    // ═══════════════════════════════════════════════════════════════
    // 2026-06-12 USB 热敏打印机 (ESC/POS) 实时打印服务
    //   接入方式: Windows 已安装的打印机驱动 (POS-58 / XP-80C / Gprinter 等).
    //   原理: 通过 winspool 把 ESC/POS 原始字节 (RAW 数据类型) 直发打印后台, 不经 GDI, 避免渲染开销.
    //   用途: PC 收到硬件 TP/SB/MB 数据并写"比赛日志"的同时, 打印同一行 (现场纸质流水留底).
    //   线程模型: PrintLine 入队即返回 (不阻塞 UI 线程); 后台线程批量取出, 用 GBK(936) 编码后单作业发送.
    //   编码: 中文热敏机普遍内置 GBK 字库, 故用 codepage 936; 取不到时退回 UTF-8.
    // ═══════════════════════════════════════════════════════════════
    public class ThermalPrinterService : IDisposable
    {
        public bool Enabled { get; set; }
        public string PrinterName { get; set; }

        public event Action<string> OnLog;

        private readonly BlockingCollection<string> _queue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private Thread _worker;
        private volatile bool _running;
        private readonly Encoding _enc;

        public ThermalPrinterService()
        {
            Encoding e;
            try { e = Encoding.GetEncoding(936); } catch { e = Encoding.UTF8; }
            _enc = e;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ThermalPrinter" };
            _worker.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _queue.CompleteAdding(); } catch { }
        }

        public void Dispose() { Stop(); }

        // 入队一行文本 (非阻塞). 仅在 启用 + 已选打印机 时入队; 首次入队自动起后台线程.
        public void PrintLine(string text)
        {
            if (!Enabled || string.IsNullOrEmpty(PrinterName)) return;
            if (!_running) Start();
            try { _queue.Add(text ?? ""); } catch { }
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                string first;
                try { if (!_queue.TryTake(out first, 300)) continue; }
                catch { break; }
                if (first == null) continue;

                // 200ms 短窗口内积攒多行 → 合并成一个打印作业, 减少 winspool 作业开销 (近实时)
                var sb = new StringBuilder();
                sb.Append(first).Append("\n");
                string more;
                while (_queue.TryTake(out more, 0)) { if (more != null) sb.Append(more).Append("\n"); }

                try { RawPrint(PrinterName, _enc.GetBytes(sb.ToString())); }
                catch (Exception ex) { RaiseLog("热敏打印失败: " + ex.Message); }
            }
        }

        // 测试打印: 初始化 + 示例行 + 走纸切纸
        public void TestPrint()
        {
            if (string.IsNullOrEmpty(PrinterName)) { RaiseLog("未选择打印机"); return; }
            var sb = new StringBuilder();
            sb.Append("==== 热敏打印测试 ====\n");
            sb.Append("游泳计时 TP/SB/MB 实时打印\n");
            sb.Append("道3右 触[2] = 28.45 (示例)\n");
            sb.Append("\n\n\n");
            try
            {
                var bytes = new List<byte>();
                bytes.AddRange(new byte[] { 0x1B, 0x40 });            // ESC @  初始化
                bytes.AddRange(_enc.GetBytes(sb.ToString()));
                bytes.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 }); // GS V B 0  走纸+切纸
                RawPrint(PrinterName, bytes.ToArray());
                RaiseLog("已发送 热敏打印测试");
            }
            catch (Exception ex) { RaiseLog("测试打印失败: " + ex.Message); }
        }

        public static List<string> GetInstalledPrinters()
        {
            var list = new List<string>();
            try { foreach (string p in PrinterSettings.InstalledPrinters) list.Add(p); } catch { }
            return list;
        }

        private void RaiseLog(string m) { var h = OnLog; if (h != null) h(m); }

        // ───────── winspool RAW 打印 (改编自 MS KB322090 RawPrinterHelper) ─────────
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        private static void RawPrint(string printerName, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            IntPtr hPrinter;
            var di = new DOCINFOA { pDocName = "SwimTiming", pDataType = "RAW" };
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                throw new Exception("打开打印机失败: " + printerName);
            try
            {
                if (!StartDocPrinter(hPrinter, 1, di)) throw new Exception("StartDocPrinter 失败");
                try
                {
                    if (!StartPagePrinter(hPrinter)) throw new Exception("StartPagePrinter 失败");
                    IntPtr p = Marshal.AllocCoTaskMem(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, p, bytes.Length);
                        int written;
                        WritePrinter(hPrinter, p, bytes.Length, out written);
                    }
                    finally { Marshal.FreeCoTaskMem(p); }
                    EndPagePrinter(hPrinter);
                }
                finally { EndDocPrinter(hPrinter); }
            }
            finally { ClosePrinter(hPrinter); }
        }
    }
}
