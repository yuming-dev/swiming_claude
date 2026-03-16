using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace RemoteTimingControl
{
    /// <summary>
    /// 本地计时硬件直连 — 用于RemoteTimingControl独立部署时直连串口
    /// 与SwimmingScoreboard.TimingBridge使用相同的帧协议
    /// </summary>
    public class TimingBridgeLocal : IDisposable
    {
        private const byte SOH = 0xF1;
        private const byte EOT = 0xF4;
        private const int FRAME_LENGTH = 12;

        private SerialPort _serialPort;
        private Thread _receiveThread;
        private volatile bool _running;
        private int[] _moduleToLane = new int[20];

        public bool IsConnected { get; private set; }
        public string StatusText { get; private set; }

        public event Action<int, string, double> OnTimingData; // lane, commandType, timeInSeconds
        public event Action<string> OnLog;

        public TimingBridgeLocal() {
            StatusText = "未连接";
            for (int i = 0; i < 20; i++) _moduleToLane[i] = i;
        }

        public void Connect(string portName, int baudRate = 115200) {
            Disconnect();
            try {
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _serialPort.ReadBufferSize = 1024;
                _serialPort.Open();
                IsConnected = true;
                StatusText = string.Format("已连接: {0}", portName);
                RaiseLog(StatusText);

                _running = true;
                _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                _receiveThread.Start();
            } catch (Exception ex) {
                StatusText = "连接失败: " + ex.Message;
                RaiseLog(StatusText);
            }
        }

        public void Disconnect() {
            _running = false;
            try { if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close(); } catch { }
            _serialPort = null;
            IsConnected = false;
            StatusText = "未连接";
        }

        private void ReceiveLoop() {
            byte[] buffer = new byte[1024];
            List<byte> acc = new List<byte>();

            while (_running && _serialPort != null && _serialPort.IsOpen) {
                try {
                    int available = _serialPort.BytesToRead;
                    if (available > 0) {
                        int count = _serialPort.Read(buffer, 0, Math.Min(available, buffer.Length));
                        for (int i = 0; i < count; i++) acc.Add(buffer[i]);
                        ProcessFrames(acc);
                    } else {
                        Thread.Sleep(5);
                    }
                } catch {
                    if (_running) break;
                }
            }
            IsConnected = false;
        }

        private void ProcessFrames(List<byte> acc) {
            while (acc.Count >= FRAME_LENGTH) {
                int sohIdx = acc.IndexOf(SOH);
                if (sohIdx < 0) { acc.Clear(); return; }
                if (sohIdx > 0) acc.RemoveRange(0, sohIdx);
                if (acc.Count < FRAME_LENGTH) return;
                if (acc[FRAME_LENGTH - 1] != EOT || acc[1] != (byte)'S') { acc.RemoveAt(0); continue; }

                byte cmd0 = acc[2];
                byte controlPort = acc[4];
                int minutes = acc[5];
                int seconds = acc[6];
                int centiseconds = acc[7];
                int milliseconds = acc[8];

                acc.RemoveRange(0, FRAME_LENGTH);

                int lane = controlPort < 20 ? _moduleToLane[controlPort] : controlPort;
                double time = minutes * 60.0 + seconds + centiseconds / 100.0 + milliseconds / 1000.0;

                string cmdType;
                switch (cmd0) {
                    case 0x06: cmdType = "Touchpad"; break;
                    case 0x07: cmdType = "PushButton1"; break;
                    case 0x08: cmdType = "PushButton2"; break;
                    case 0x09: cmdType = "PushButton3"; break;
                    case 0x0A: cmdType = "StartingBlock"; break;
                    case 0x0C: cmdType = "StartCommand"; break;
                    default: cmdType = "Unknown"; break;
                }

                RaiseLog(string.Format("本地计时: 道{0} {1} {2:F2}s", lane, cmdType, time));
                Action<int, string, double> h = OnTimingData;
                if (h != null) h(lane, cmdType, time);
            }
        }

        private void RaiseLog(string msg) {
            Action<string> h = OnLog;
            if (h != null) h(msg);
        }

        public void Dispose() {
            Disconnect();
        }
    }
}
