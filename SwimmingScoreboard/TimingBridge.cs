using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;

namespace SwimmingScoreboard
{
    // ═══════════════════════════════════════════════════════════════
    // 计时硬件桥接 — 连接C++计时系统
    // 支持串口直连和TCP/IP两种模式
    // 协议帧: SOH(0xF1) | 'S' | CMD0 | CMD1 | 端口/泳道 | 分 | 秒 | 百毫秒 | 毫秒 | ... | EOT(0xF4)
    // ═══════════════════════════════════════════════════════════════

    public class TimingData
    {
        public int Lane { get; set; }
        public TimingCommandType CommandType { get; set; }
        public double TimeInSeconds { get; set; }
        public int Minutes { get; set; }
        public int Seconds { get; set; }
        public int Centiseconds { get; set; }
        public int Milliseconds { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    public enum TimingCommandType
    {
        Touchpad = 0x06,
        PushButton1 = 0x07,
        PushButton2 = 0x08,
        PushButton3 = 0x09,
        StartingBlock = 0x0A,
        StartCommand = 0x0C,
        TestCommand = 0x0D
    }

    public enum TimingConnectionMode
    {
        None,
        SerialPort,
        TcpClient
    }

    public class TimingBridge : IDisposable
    {
        // 协议常量
        private const byte SOH = 0xF1;
        private const byte EOT = 0xF4;
        private const int FRAME_LENGTH = 12;

        // 连接
        private SerialPort _serialPort;
        private TcpClient _tcpClient;
        private NetworkStream _tcpStream;
        private Thread _receiveThread;
        private volatile bool _running;

        // 泳道映射
        private int[] _moduleToLane = new int[20];

        // 状态
        public TimingConnectionMode ConnectionMode { get; private set; }
        public bool IsConnected { get; private set; }
        public string StatusText { get; private set; }

        // 事件
        public event Action<TimingData> OnTimingData;
        public event Action<string> OnStatusChanged;
        public event Action<string> OnLog;

        public TimingBridge() {
            ConnectionMode = TimingConnectionMode.None;
            StatusText = "未连接";
            // 默认1:1映射
            for (int i = 0; i < 20; i++) _moduleToLane[i] = i;
        }

        public void SetModuleLaneMapping(int[] mapping) {
            if (mapping != null && mapping.Length <= 20) {
                Array.Copy(mapping, _moduleToLane, mapping.Length);
            }
        }

        // ═══════ 串口连接 ═══════
        public void ConnectSerial(string portName, int baudRate = 115200) {
            Disconnect();
            try {
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
                _serialPort.ReadBufferSize = 1024;
                _serialPort.WriteBufferSize = 1024;
                _serialPort.Open();

                ConnectionMode = TimingConnectionMode.SerialPort;
                IsConnected = true;
                StatusText = string.Format("串口已连接: {0} @ {1}", portName, baudRate);
                RaiseStatus(StatusText);

                _running = true;
                _receiveThread = new Thread(SerialReceiveLoop) { IsBackground = true, Name = "TimingSerial" };
                _receiveThread.Start();
            } catch (Exception ex) {
                StatusText = "串口连接失败: " + ex.Message;
                RaiseStatus(StatusText);
                RaiseLog("串口连接错误: " + ex.Message);
            }
        }

        // ═══════ TCP连接 ═══════
        public void ConnectTcp(string host, int port) {
            Disconnect();
            try {
                _tcpClient = new TcpClient();
                _tcpClient.Connect(host, port);
                _tcpStream = _tcpClient.GetStream();

                ConnectionMode = TimingConnectionMode.TcpClient;
                IsConnected = true;
                StatusText = string.Format("TCP已连接: {0}:{1}", host, port);
                RaiseStatus(StatusText);

                _running = true;
                _receiveThread = new Thread(TcpReceiveLoop) { IsBackground = true, Name = "TimingTcp" };
                _receiveThread.Start();
            } catch (Exception ex) {
                StatusText = "TCP连接失败: " + ex.Message;
                RaiseStatus(StatusText);
                RaiseLog("TCP连接错误: " + ex.Message);
            }
        }

        public void Disconnect() {
            _running = false;
            try { if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close(); } catch { }
            try { if (_tcpStream != null) _tcpStream.Close(); } catch { }
            try { if (_tcpClient != null) _tcpClient.Close(); } catch { }
            _serialPort = null;
            _tcpClient = null;
            _tcpStream = null;
            ConnectionMode = TimingConnectionMode.None;
            IsConnected = false;
            StatusText = "未连接";
        }

        // ═══════ 串口接收循环 ═══════
        private void SerialReceiveLoop() {
            byte[] buffer = new byte[1024];
            List<byte> accumulator = new List<byte>();

            while (_running && _serialPort != null && _serialPort.IsOpen) {
                try {
                    int bytesAvailable = _serialPort.BytesToRead;
                    if (bytesAvailable > 0) {
                        int count = _serialPort.Read(buffer, 0, Math.Min(bytesAvailable, buffer.Length));
                        for (int i = 0; i < count; i++) accumulator.Add(buffer[i]);
                        ProcessAccumulator(accumulator);
                    } else {
                        Thread.Sleep(5);
                    }
                } catch (Exception ex) {
                    if (_running) RaiseLog("串口读取错误: " + ex.Message);
                    break;
                }
            }

            IsConnected = false;
            StatusText = "串口已断开";
            RaiseStatus(StatusText);
        }

        // ═══════ TCP接收循环 ═══════
        private void TcpReceiveLoop() {
            byte[] buffer = new byte[1024];
            List<byte> accumulator = new List<byte>();

            while (_running && _tcpClient != null && _tcpClient.Connected) {
                try {
                    int count = _tcpStream.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    for (int i = 0; i < count; i++) accumulator.Add(buffer[i]);
                    ProcessAccumulator(accumulator);
                } catch {
                    if (_running) break;
                }
            }

            IsConnected = false;
            StatusText = "TCP已断开";
            RaiseStatus(StatusText);
        }

        // ═══════ 帧解析 ═══════
        private void ProcessAccumulator(List<byte> acc) {
            while (acc.Count >= FRAME_LENGTH) {
                // 查找SOH
                int sohIdx = acc.IndexOf(SOH);
                if (sohIdx < 0) { acc.Clear(); return; }
                if (sohIdx > 0) { acc.RemoveRange(0, sohIdx); }
                if (acc.Count < FRAME_LENGTH) return;

                // 验证EOT
                if (acc[FRAME_LENGTH - 1] != EOT) {
                    acc.RemoveAt(0);
                    continue;
                }

                // 验证第二字节为 'S'
                if (acc[1] != (byte)'S') {
                    acc.RemoveAt(0);
                    continue;
                }

                // 提取帧
                byte[] frame = new byte[FRAME_LENGTH];
                for (int i = 0; i < FRAME_LENGTH; i++) frame[i] = acc[i];
                acc.RemoveRange(0, FRAME_LENGTH);

                ParseFrame(frame);
            }
        }

        private void ParseFrame(byte[] frame) {
            // frame[0] = SOH, frame[1] = 'S'
            // frame[2] = CMD0, frame[3] = CMD1
            // frame[4] = 控制端口/泳道
            // frame[5] = 分, frame[6] = 秒, frame[7] = 百毫秒, frame[8] = 毫秒
            byte cmd0 = frame[2];
            byte controlPort = frame[4];
            int minutes = frame[5];
            int seconds = frame[6];
            int centiseconds = frame[7];
            int milliseconds = frame[8];

            int laneIndex = controlPort < 20 ? _moduleToLane[controlPort] : controlPort;

            double timeInSeconds = minutes * 60.0 + seconds + centiseconds / 100.0 + milliseconds / 1000.0;

            TimingCommandType cmdType;
            switch (cmd0) {
                case 0x06: cmdType = TimingCommandType.Touchpad; break;
                case 0x07: cmdType = TimingCommandType.PushButton1; break;
                case 0x08: cmdType = TimingCommandType.PushButton2; break;
                case 0x09: cmdType = TimingCommandType.PushButton3; break;
                case 0x0A: cmdType = TimingCommandType.StartingBlock; break;
                case 0x0C: cmdType = TimingCommandType.StartCommand; break;
                case 0x0D: cmdType = TimingCommandType.TestCommand; break;
                default:
                    RaiseLog(string.Format("未知命令类型: 0x{0:X2}", cmd0));
                    return;
            }

            var data = new TimingData {
                Lane = laneIndex,
                CommandType = cmdType,
                TimeInSeconds = timeInSeconds,
                Minutes = minutes,
                Seconds = seconds,
                Centiseconds = centiseconds,
                Milliseconds = milliseconds,
                ReceivedAt = DateTime.Now
            };

            RaiseLog(string.Format("计时数据: 泳道{0} {1} {2}", laneIndex, cmdType, TimeFormatter.Format(timeInSeconds)));

            Action<TimingData> handler = OnTimingData;
            if (handler != null) handler(data);
        }

        // ═══════ 发送命令到计时系统 ═══════
        public void SendCommand(byte command, byte param1 = 0, byte param2 = 0) {
            byte[] frame = new byte[FRAME_LENGTH];
            frame[0] = SOH;
            frame[1] = (byte)'S';
            frame[2] = command;
            frame[3] = param1;
            frame[4] = param2;
            frame[FRAME_LENGTH - 1] = EOT;

            try {
                if (ConnectionMode == TimingConnectionMode.SerialPort && _serialPort != null && _serialPort.IsOpen) {
                    _serialPort.Write(frame, 0, frame.Length);
                } else if (ConnectionMode == TimingConnectionMode.TcpClient && _tcpStream != null) {
                    _tcpStream.Write(frame, 0, frame.Length);
                }
            } catch (Exception ex) {
                RaiseLog("发送命令失败: " + ex.Message);
            }
        }

        private void RaiseStatus(string status) {
            Action<string> h = OnStatusChanged;
            if (h != null) h(status);
        }

        private void RaiseLog(string msg) {
            Action<string> h = OnLog;
            if (h != null) h(msg);
        }

        public void Dispose() {
            Disconnect();
        }

        // ═══════ 计时源裁定算法 ═══════
        public static TimingJudgement JudgeTimingSource(double touchpad, double blind1, double blind2, double blind3, double manualTouch) {
            var result = new TimingJudgement();

            // 优先级1：触板有效且合理
            if (touchpad > 0) {
                result.FinalTime = touchpad;
                result.Source = "TP";
                result.NeedsReview = false;
                return result;
            }

            // 统计有效盲表数量
            List<double> validBlinds = new List<double>();
            if (blind1 > 0) validBlinds.Add(blind1);
            if (blind2 > 0) validBlinds.Add(blind2);
            if (blind3 > 0) validBlinds.Add(blind3);

            // 优先级2-4：盲表
            if (validBlinds.Count == 3) {
                // 取中值
                validBlinds.Sort();
                result.FinalTime = validBlinds[1];
                result.Source = "MB";
                result.NeedsReview = false;
                return result;
            }
            if (validBlinds.Count == 2) {
                result.FinalTime = (validBlinds[0] + validBlinds[1]) / 2.0;
                result.Source = "MB";
                result.NeedsReview = false;
                return result;
            }
            if (validBlinds.Count == 1) {
                result.FinalTime = validBlinds[0];
                result.Source = "MB";
                result.NeedsReview = true; // 仅一个盲表需人工复核
                return result;
            }

            // 优先级5：手动触板按钮
            if (manualTouch > 0) {
                result.FinalTime = manualTouch;
                result.Source = "PB";
                result.NeedsReview = true;
                return result;
            }

            // 无计时数据
            result.FinalTime = 0;
            result.Source = "";
            result.NeedsReview = true;
            return result;
        }
    }

    public class TimingJudgement
    {
        public double FinalTime { get; set; }
        public string Source { get; set; }
        public bool NeedsReview { get; set; }
    }
}
