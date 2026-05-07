using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
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
        /// <summary>
        /// D4 泳道端标识：true = 终点端 (D4 0-9), false = 另一端 (D4 10-19)
        /// 用于区分左右设备（结合 FinishPosition 设置映射到 left/right）
        /// </summary>
        public bool IsFinishEnd { get; set; }
        /// <summary>D3 原始字节（设置类命令子码）</summary>
        public byte Param1 { get; set; }
        /// <summary>D4 原始字节（0x40=泳池长度；0x41=比赛距离；0x42=子参数值；0x16/0x17/…=rawD4 含终点端标识）</summary>
        public byte RawD4 { get; set; }
        /// <summary>D5 原始字节（设置类命令扩展字段）</summary>
        public byte Param5 { get; set; }
        /// <summary>D6 原始字节（0x41 下 0-4 泳道空道位图）</summary>
        public byte Param6 { get; set; }
        /// <summary>D7 原始字节（0x41 下 5-9 泳道空道位图；0x42/0x20 下为距离高字节）</summary>
        public byte Param7 { get; set; }
        /// <summary>D8 原始字节（计时帧为 (hour&lt;&lt;4)|ms1；设置帧作扩展值）</summary>
        public byte Param8 { get; set; }
    }

    // 游泳计时通讯协议 2023-11-13  D2命令字节定义
    public enum TimingCommandType
    {
        Touchpad      = 0x16,   // 触板时间成绩   D4=泳道号(0-9终点,10-19另一端)
        PushButton1   = 0x17,   // 盲表1时间成绩  D4=泳道号
        PushButton2   = 0x18,   // 盲表2时间成绩  D4=泳道号
        PushButton3   = 0x19,   // 盲表3时间成绩  D4=泳道号
        StartingBlock = 0x1A,   // 出发台出发时间 D4=泳道号
        StartCommand  = 0x1C,   // 发令开始计时
        TestCommand   = 0x1D,   // 测试设备
        TimerReset    = 0x20,   // 计时清零
        TimerReady    = 0x21,   // 准备就绪
        RunningTime   = 0x7F,   // 滚动时间
        PoolConfig    = 0x40,   // 设置泳池参数
        RaceConfig    = 0x41,   // 设置比赛距离参数
        SetCommand    = 0x42,   // 设置命令
    }

    public enum TimingConnectionMode
    {
        None,
        SerialPort,
        TcpClient,
        UdpListener
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
        private UdpClient _udpClient;
        private IPEndPoint _udpSendTarget;   // 配置的UDP发送目标
        private IPEndPoint _udpLastSender;   // 最后一次收到数据的来源（自动回复）
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
            try { if (_udpClient != null) _udpClient.Close(); } catch { }
            _serialPort = null;
            _tcpClient = null;
            _tcpStream = null;
            _udpClient = null;
            ConnectionMode = TimingConnectionMode.None;
            IsConnected = false;
            StatusText = "未连接";
        }

        // ═══════ UDP监听 ═══════
        public void ConnectUdp(int listenPort, string sendHost = null, int sendPort = 0) {
            Disconnect();
            try {
                _udpClient = new UdpClient(listenPort);
                if (!string.IsNullOrEmpty(sendHost) && sendPort > 0)
                    _udpSendTarget = new IPEndPoint(IPAddress.Parse(sendHost), sendPort);
                else
                    _udpSendTarget = null;
                ConnectionMode = TimingConnectionMode.UdpListener;
                IsConnected = true;
                if (_udpSendTarget != null)
                    StatusText = string.Format("UDP: 收←{0} 发→{1}:{2}", listenPort, sendHost, sendPort);
                else
                    StatusText = string.Format("UDP监听中: 端口 {0}", listenPort);
                RaiseStatus(StatusText);

                _running = true;
                _receiveThread = new Thread(UdpReceiveLoop) { IsBackground = true, Name = "TimingUdp" };
                _receiveThread.Start();
            } catch (Exception ex) {
                StatusText = "UDP监听失败: " + ex.Message;
                RaiseStatus(StatusText);
                RaiseLog("UDP监听错误: " + ex.Message);
            }
        }

        private void UdpReceiveLoop() {
            List<byte> accumulator = new List<byte>();
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            while (_running && _udpClient != null) {
                try {
                    byte[] received = _udpClient.Receive(ref remoteEP);
                    _udpLastSender = remoteEP;  // 记住发送方，用于回复
                    for (int i = 0; i < received.Length; i++) accumulator.Add(received[i]);
                    ProcessAccumulator(accumulator);
                } catch {
                    if (_running) break;
                }
            }

            IsConnected = false;
            StatusText = "UDP已停止";
            RaiseStatus(StatusText);
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
            // 游泳计时通讯协议 2023-11-13  12字节帧
            // D0=0xF1(SOH)  D1=0x53('S')  D2=命令  D3=命令1  D4=命令2/泳道号
            // D5=分  D6=秒  D7=1/100秒  D8=(小时<<4)|(1/1000秒)  D9=备用  D10=备用  D11=0xF4(EOT)
            byte cmd  = frame[2];
            byte cmd1 = frame[3];
            byte rawD4 = frame[4];   // D4: 0-9=终点端泳道号, 10-19=另一端泳道号(实际泳道=D4-10)
            int minutes      = frame[5];
            int seconds      = frame[6];
            int centiseconds = frame[7];
            int hour         = (frame[8] >> 4) & 0x0F;
            int ms1          = frame[8] & 0x0F;   // 1/1000秒个位

            // D4 拆分：实际泳道号 + 终点端/另一端标识
            bool isFinishEnd = rawD4 < 10;
            int actualLane = isFinishEnd ? rawD4 : rawD4 - 10;
            int laneIndex = actualLane < 20 ? _moduleToLane[actualLane] : actualLane;

            // 时间：小时*3600 + 分*60 + 秒 + 1/100秒 + 1/1000秒
            double timeInSeconds = hour * 3600.0 + minutes * 60.0 + seconds
                                   + centiseconds / 100.0 + ms1 / 1000.0;

            TimingCommandType cmdType;
            switch (cmd) {
                case 0x16: cmdType = TimingCommandType.Touchpad;      break;  // 触板时间成绩
                case 0x17: cmdType = TimingCommandType.PushButton1;   break;  // 盲表1
                case 0x18: cmdType = TimingCommandType.PushButton2;   break;  // 盲表2
                case 0x19: cmdType = TimingCommandType.PushButton3;   break;  // 盲表3
                case 0x1A: cmdType = TimingCommandType.StartingBlock; break;  // 出发台出发时间
                case 0x1C: cmdType = TimingCommandType.StartCommand;  break;  // 发令开始计时
                case 0x1D: cmdType = TimingCommandType.TestCommand;   break;  // 测试设备
                case 0x20: cmdType = TimingCommandType.TimerReset;    break;  // 计时清零
                case 0x21: cmdType = TimingCommandType.TimerReady;    break;  // 准备就绪
                case 0x7F: cmdType = TimingCommandType.RunningTime;   break;  // 滚动时间
                case 0x40: cmdType = TimingCommandType.PoolConfig;    break;  // 泳池参数
                case 0x41: cmdType = TimingCommandType.RaceConfig;    break;  // 比赛距离参数
                case 0x42: cmdType = TimingCommandType.SetCommand;    break;  // 设置命令
                default:
                    RaiseLog(string.Format("未知命令: 0x{0:X2}", cmd));
                    return;
            }

            var data = new TimingData {
                Lane = laneIndex,
                CommandType = cmdType,
                TimeInSeconds = timeInSeconds,
                Minutes = minutes,
                Seconds = seconds,
                Centiseconds = centiseconds,
                Milliseconds = ms1,
                ReceivedAt = DateTime.Now,
                IsFinishEnd = isFinishEnd,
                Param1 = cmd1,
                RawD4 = rawD4,
                Param5 = frame[5],
                Param6 = frame[6],
                Param7 = frame[7],
                Param8 = frame[8]
            };

            string endLabel = isFinishEnd ? "终点端" : "另一端";
            RaiseLog(string.Format("收帧: CMD=0x{0:X2} D4={1} 泳道{2}({3}) {4} {5}",
                cmd, rawD4, laneIndex, endLabel, cmdType, TimeFormatter.Format(timeInSeconds)));

            Action<TimingData> handler = OnTimingData;
            if (handler != null) handler(data);
        }

        // ═══════ 发送命令到计时系统 ═══════
        public void SendCommand(byte command, byte param1 = 0, byte param2 = 0) {
            SendFullFrame(command, param1, param2, 0, 0, 0, 0);
        }

        /// <summary>发送 12 字节帧，支持在 D5/D6/D7/D8 携带扩展参数（设备状态等）</summary>
        public void SendFullFrame(byte command, byte d3, byte d4, byte d5 = 0, byte d6 = 0, byte d7 = 0, byte d8 = 0) {
            byte[] frame = new byte[FRAME_LENGTH];
            frame[0] = SOH;
            frame[1] = (byte)'S';
            frame[2] = command;
            frame[3] = d3;
            frame[4] = d4;
            frame[5] = d5;
            frame[6] = d6;
            frame[7] = d7;
            frame[8] = d8;
            frame[FRAME_LENGTH - 1] = EOT;

            // 逐字节十六进制日志，便于对照硬件协议判定收到的帧是否符合预期
            // 12 字节帧：F1 53 [cmd d3 d4 d5 d6 d7 d8] 00 00 F4
            // = 静态首尾 4 字节 + 中间 7 个动态字节（占位符 {0}-{6}）
            string hex;
            try {
                hex = string.Format("F1 53 {0:X2} {1:X2} {2:X2} {3:X2} {4:X2} {5:X2} {6:X2} 00 00 F4",
                    command, d3, d4, d5, d6, d7, d8);
            } catch (Exception fex) {
                hex = string.Format("(hex format failed: {0})", fex.Message);
            }
            try {
                if (ConnectionMode == TimingConnectionMode.SerialPort && _serialPort != null && _serialPort.IsOpen) {
                    _serialPort.Write(frame, 0, frame.Length);
                    RaiseLog("发帧[串口]: " + hex);
                } else if (ConnectionMode == TimingConnectionMode.TcpClient && _tcpStream != null) {
                    _tcpStream.Write(frame, 0, frame.Length);
                    RaiseLog("发帧[TCP]: " + hex);
                } else if (ConnectionMode == TimingConnectionMode.UdpListener && _udpClient != null) {
                    IPEndPoint target = _udpSendTarget ?? _udpLastSender;
                    if (target != null) {
                        _udpClient.Send(frame, frame.Length, target);
                        RaiseLog("发帧[UDP " + target + "]: " + hex);
                    } else {
                        RaiseLog("UDP发送失败: 未知目标地址，请配置UDP发送目标或等待硬件先发送数据");
                    }
                } else {
                    RaiseLog("未发送（无连接）: " + hex);
                }
            } catch (Exception ex) {
                RaiseLog("发送命令失败: " + ex.Message + "  帧: " + hex);
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
