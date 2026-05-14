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
        /// <summary>2026-05-12 D10 原始字节（StartingBlock 帧中作为符号位：0=正，非0=负/抢跳）</summary>
        public byte Param10 { get; set; }
        /// <summary>2026-05-12 StartingBlock 帧解出来的时间是否为负值（运动员抢跳/犯规）。
        /// 当为 true 时，TimeInSeconds 已被取反为负数，调用方可直接显示 / 判断 &lt; 0。</summary>
        public bool IsFalseStart { get; set; }
        /// <summary>2026-05-13 当 CommandType==BatteryVoltage 时，把硬件上报的电池电压解析为伏 (V)。
        /// 帧 d3=mV 低字节, d4=mV 高字节, 已合并 / 1000.0 得到 V 值（例如 12.34）。</summary>
        public double BatteryVoltage { get; set; }
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
        BatteryVoltage = 0x4B,  // 2026-05-13 硬件计时器电池电压 D3:D4 = 高低字节 mV
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

                // 与"发帧"日志对称的"收帧"逐字节十六进制日志，方便对照硬件实际行为
                try {
                    var sb = new System.Text.StringBuilder("收帧[原始]: ");
                    for (int i = 0; i < frame.Length; i++) sb.AppendFormat("{0:X2} ", frame[i]);
                    RaiseLog(sb.ToString().TrimEnd());
                } catch { }

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

            // 2026-05-12 协议扩展：byte10 (D10/SW_Back1_Pos) 用作 StartingBlock 命令的符号位：
            //   = 0 : 正常出发（运动员晚于发令枪），TimeInSeconds 保持正值
            //   ≠ 0 : 抢跳/犯规（运动员早于发令枪），TimeInSeconds 取反为负值
            byte rawD10 = frame[10];
            bool isFalseStart = false;
            if (cmd == 0x1A && rawD10 != 0) {
                isFalseStart = true;
                timeInSeconds = -timeInSeconds;
            }

            // 2026-05-13(2) 协议扩展：0x4B BatteryVoltage 帧，d3:d4 BIG-ENDIAN mV，转 V
            //   按 通讯协议变更说明_v2026.05.13.pdf：d3 = mV 高字节，d4 = mV 低字节
            //   (上一版误用 LE；已与硬件 swimplay.c 同步修正)
            double batteryVolt = 0.0;
            if (cmd == 0x4B) {
                int v_mV = (frame[3] << 8) | frame[4];
                batteryVolt = v_mV / 1000.0;
            }

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
                case 0x4B: cmdType = TimingCommandType.BatteryVoltage; break; // 2026-05-13 电池电压
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
                Param8 = frame[8],
                Param10 = rawD10,        //2026-05-12 协议扩展：StartingBlock 符号位
                IsFalseStart = isFalseStart,
                BatteryVoltage = batteryVolt //2026-05-13 当 cmd==0x4B 时已转 V，其它命令保持 0.0
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

        // 帧间隔：硬件需要时间处理上一帧；连续发多个控制帧时调用本方法等一会再发下一帧。
        // 典型场景：发完 0x43 Set_MatchEvent 后 sleep 50ms，再发 0x21 准备就绪 / 0x1C 发令。
        public void DelayBetweenFrames(int milliseconds = 50) {
            try { System.Threading.Thread.Sleep(milliseconds); } catch { }
        }

        /// <summary>发送 12 字节帧。位置 3..10 全部可携带数据（与参考程序 OnSendSWCommand_Data 对齐）。
        /// 帧布局：[0]F1 [1]'S' [2]command [3]d3 [4]d4 [5]d5 [6]d6 [7]d7 [8]d8 [9]d9 [10]d10 [11]F4</summary>
        public void SendFullFrame(byte command, byte d3, byte d4, byte d5 = 0, byte d6 = 0, byte d7 = 0, byte d8 = 0, byte d9 = 0, byte d10 = 0) {
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
            frame[9] = d9;
            frame[10] = d10;
            frame[FRAME_LENGTH - 1] = EOT;

            // 逐字节十六进制日志（与参考程序 0x41 Set_ArmDelay_Time 帧对齐使用全部 8 个数据字节）
            string hex;
            try {
                hex = string.Format("F1 53 {0:X2} {1:X2} {2:X2} {3:X2} {4:X2} {5:X2} {6:X2} {7:X2} {8:X2} F4",
                    command, d3, d4, d5, d6, d7, d8, d9, d10);
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

        // ───────────────────────────────────────────────────────────────
        // 2026-05-12 协议扩展：日期/时间自动校时 + 泳池单边/两端触板配置
        // ───────────────────────────────────────────────────────────────

        /// <summary>把当前PC的日期+时间下发到硬件计时器，使其RTC同步。
        /// 协议: command=0x39 (Set_DateTime)
        ///   d3=年低字节  d4=年高字节  d5=月(1-12)  d6=日(1-31)
        ///   d7=时(0-23)  d8=分(0-59)  d9=秒(0-59)
        /// 建议在硬件 TCP 连接建立后立即调用一次。</summary>
        public void SendDateTimeSync() {
            SendDateTimeSync(DateTime.Now);
        }

        /// <summary>指定日期/时间下发到硬件计时器（一般用于测试）。</summary>
        public void SendDateTimeSync(DateTime dt) {
            byte yearLo = (byte)(dt.Year & 0xFF);
            byte yearHi = (byte)((dt.Year >> 8) & 0xFF);
            SendFullFrame(
                0x39,                       // command = Set_DateTime
                yearLo, yearHi,
                (byte)dt.Month,
                (byte)dt.Day,
                (byte)dt.Hour,
                (byte)dt.Minute,
                (byte)dt.Second,
                0
            );
            RaiseLog(string.Format("发送日期时间同步: {0:yyyy-MM-dd HH:mm:ss}", dt));
        }

        /// <summary>设置硬件计时器的"泳池单边/两端安装触板"配置。
        /// 协议: command=0x3A (Set_PoolSingleOrDoubleTP), d3=0(两端) 或 d3=1(单边)。
        /// 修改后硬件会持久化到 SD 卡。</summary>
        public void SendPoolSingleOrDoubleTP(bool isSingleSide) {
            SendFullFrame(0x3A, (byte)(isSingleSide ? 1 : 0), 0);
            RaiseLog(string.Format("发送泳池触板安装方式: {0}", isSingleSide ? "单边" : "两端"));
        }

        /// <summary>2026-05-13(2) 强制 全开 / 恢复正常 整道或某道的所有设备(TP/SB/MB)。
        /// 协议: command=0x4C (Set_ForceAllOpen) — 按 v2026.05.13 通讯协议变更说明。
        ///   d3 = 0xFF 全部道；0..9 指定单道
        ///   d4 = 0 恢复正常关闭流程；1 全开（强制打开，接受所有信号不过滤）
        /// 硬件状态 3(坏)/4(未安装)不会被覆盖。
        /// 注：上一版用 0x3B (私有)，已修正为官方 0x4C。</summary>
        /// <param name="laneIndex">0..9 单道；传入小于 0 表示"全部道"</param>
        /// <param name="forceOpen">true=全开 强制打开; false=恢复正常关闭流程</param>
        public void SendLaneDeviceFullOpen(int laneIndex, bool forceOpen) {
            byte d3 = (laneIndex < 0 || laneIndex >= 10) ? (byte)0xFF : (byte)laneIndex;
            byte d4 = (byte)(forceOpen ? 1 : 0);
            SendFullFrame(0x4C, d3, d4);
            RaiseLog(string.Format("发送 设备{0} {1} (0x4C)",
                d3 == 0xFF ? "全部道" : ("第" + laneIndex.ToString() + "道"),
                forceOpen ? "全开(强制打开)" : "恢复正常关闭流程"));
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
