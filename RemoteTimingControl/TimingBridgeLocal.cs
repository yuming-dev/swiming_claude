using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RemoteTimingControl
{
    public enum HwConnectionMode { None, Serial, Udp }

    // 游泳计时通讯协议 2023-11-13 + 2026-05 扩展  D2 命令字节
    // 与 SwimmingScoreboard.TimingBridge 保持完全一致
    public enum TimingCommandType
    {
        Touchpad           = 0x16, // 触板时间成绩   D4=泳道号(0-9终点,10-19另一端)
        PushButton1        = 0x17, // 盲表1时间成绩
        PushButton2        = 0x18, // 盲表2时间成绩
        PushButton3        = 0x19, // 盲表3时间成绩
        StartingBlock      = 0x1A, // 出发台出发时间 (D10!=0 表示抢跳，TimeInSeconds 取反)
        StartCommand       = 0x1C, // 发令开始计时
        TestCommand        = 0x1D, // 测试设备
        TimerReset         = 0x20, // 计时清零
        TimerReady         = 0x21, // 准备就绪
        RunningTime        = 0x7F, // 滚动时间
        PoolConfig         = 0x40, // 设置泳池参数 (0x44 同语义)
        RaceConfig         = 0x41, // 设置比赛距离参数
        SetCommand         = 0x42, // 设置命令
        BatteryVoltage     = 0x4B, // 2026-05-13 硬件计时器电池电压 d3:d4 BE mV
        LaneOpenClose      = 0x47, // 2026-05-16 道次开/关 (硬件↔PC)
        PoolSingleOrDouble = 0x3A, // 2026-05-16 泳池单/两端触板配置 (硬件↔PC)
        LaneOrder          = 0x62, // 2026-05-17 道次顺序 (硬件→PC)
        FinishPosition     = 0x63, // 2026-05-17 终点位置 (硬件→PC)
        TimingsBundle      = 0x64, // 2026-05-17 5 项时间数据 (硬件→PC)
    }

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
        /// <summary>D4 0-9 = 终点端，10-19 = 另一端</summary>
        public bool IsFinishEnd { get; set; }
        public byte Param1 { get; set; }   // D3
        public byte RawD4 { get; set; }    // D4
        public byte Param5 { get; set; }   // D5
        public byte Param6 { get; set; }   // D6
        public byte Param7 { get; set; }   // D7
        public byte Param8 { get; set; }   // D8
        /// <summary>2026-05-12 D10：StartingBlock 帧符号位 (0=正, 非0=抢跳/负)</summary>
        public byte Param10 { get; set; }
        /// <summary>2026-05-12 抢跳标志。true 时 TimeInSeconds 已取反</summary>
        public bool IsFalseStart { get; set; }
        /// <summary>2026-05-13 cmd==0x4B 时硬件电池电压(伏); 其它命令 0.0</summary>
        public double BatteryVoltage { get; set; }
    }

    /// <summary>
    /// 本地计时硬件直连 — 串口 + UDP 双模式
    /// 与 SwimmingScoreboard.TimingBridge 使用相同的帧协议（游泳计时通讯协议 2023-11-13 + 2026-05 扩展）
    /// 帧格式(12字节): D0=SOH(0xF1) | D1='S' | D2=CMD | D3 | D4 | D5..D10 | D11=EOT(0xF4)
    /// </summary>
    public class TimingBridgeLocal : IDisposable
    {
        private const byte SOH = 0xF1;
        private const byte EOT = 0xF4;
        private const int FRAME_LENGTH = 12;

        private SerialPort _serialPort;
        private UdpClient _udp;                 // 单一UDP套接字，收发共用（与主服务器一致）
        private IPEndPoint _udpSendTarget;      // 配置的UDP发送目标
        private IPEndPoint _udpLastSender;      // 最近接收方，作为发送回复的后备
        private Thread _receiveThread;
        private volatile bool _running;
        private int[] _moduleToLane = new int[20];

        public HwConnectionMode ConnectionMode { get; private set; }
        public bool IsConnected { get; private set; }
        public string StatusText { get; private set; }

        /// <summary>旧签名事件（向后兼容）：lane, cmdType-字符串, timeInSeconds</summary>
        public event Action<int, string, double> OnTimingData;
        /// <summary>2026-05-19 完整 TimingData 事件，含 BatteryVoltage/IsFalseStart/Param10 等扩展字段</summary>
        public event Action<TimingData> OnTimingDataEx;
        public event Action<string> OnStatusChanged;
        public event Action<string> OnLog;

        public TimingBridgeLocal() {
            StatusText = "未连接";
            ConnectionMode = HwConnectionMode.None;
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
                _serialPort.Open();
                ConnectionMode = HwConnectionMode.Serial;
                IsConnected = true;
                StatusText = "串口: " + portName;
                RaiseStatus(StatusText);
                RaiseLog("[硬件] 已连接串口: " + portName);
                _running = true;
                _receiveThread = new Thread(SerialRecvLoop) { IsBackground = true, Name = "HwSerial" };
                _receiveThread.Start();
            } catch (Exception ex) {
                StatusText = "串口失败: " + ex.Message;
                RaiseStatus(StatusText);
                RaiseLog("[硬件] 串口连接失败: " + ex.Message);
            }
        }

        // ═══════ UDP连接 ═══════
        public void ConnectUdp(string sendHost, int sendPort, int recvPort) {
            Disconnect();
            try {
                _udp = new UdpClient(recvPort);
                if (!string.IsNullOrEmpty(sendHost) && sendPort > 0)
                    _udpSendTarget = new IPEndPoint(IPAddress.Parse(sendHost), sendPort);
                else
                    _udpSendTarget = null;
                _udpLastSender = null;
                ConnectionMode = HwConnectionMode.Udp;
                IsConnected = true;
                StatusText = string.Format("UDP: →{0}:{1} ←{2}", sendHost, sendPort, recvPort);
                RaiseStatus(StatusText);
                RaiseLog(string.Format("[硬件] UDP已连接: 发→{0}:{1}  收←{2}", sendHost, sendPort, recvPort));
                _running = true;
                _receiveThread = new Thread(UdpRecvLoop) { IsBackground = true, Name = "HwUdp" };
                _receiveThread.Start();
            } catch (Exception ex) {
                StatusText = "UDP失败: " + ex.Message;
                RaiseStatus(StatusText);
                RaiseLog("[硬件] UDP连接失败: " + ex.Message);
            }
        }

        public void Disconnect() {
            _running = false;
            try { if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close(); } catch { }
            try { if (_udp != null) _udp.Close(); } catch { }
            _serialPort = null; _udp = null;
            _udpSendTarget = null; _udpLastSender = null;
            ConnectionMode = HwConnectionMode.None;
            IsConnected = false;
            StatusText = "未连接";
        }

        // ═══════ 串口接收循环 ═══════
        private void SerialRecvLoop() {
            byte[] buf = new byte[1024];
            var acc = new List<byte>();
            while (_running && _serialPort != null && _serialPort.IsOpen) {
                try {
                    int n = _serialPort.BytesToRead;
                    if (n > 0) {
                        int c = _serialPort.Read(buf, 0, Math.Min(n, buf.Length));
                        for (int i = 0; i < c; i++) acc.Add(buf[i]);
                        ProcessFrames(acc);
                    } else Thread.Sleep(5);
                } catch { if (_running) break; }
            }
            IsConnected = false;
            StatusText = "串口已断开";
            RaiseStatus(StatusText);
        }

        // ═══════ UDP接收循环 ═══════
        private void UdpRecvLoop() {
            var acc = new List<byte>();
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (_running && _udp != null) {
                try {
                    byte[] data = _udp.Receive(ref ep);
                    _udpLastSender = ep;   // 记住发送方，作为未配置目标时的后备
                    for (int i = 0; i < data.Length; i++) acc.Add(data[i]);
                    ProcessFrames(acc);
                } catch { if (_running) break; }
            }
            IsConnected = false;
            StatusText = "UDP已断开";
            RaiseStatus(StatusText);
        }

        // ═══════ 帧解析 ═══════
        private void ProcessFrames(List<byte> acc) {
            while (acc.Count >= FRAME_LENGTH) {
                int idx = acc.IndexOf(SOH);
                if (idx < 0) { acc.Clear(); return; }
                if (idx > 0) acc.RemoveRange(0, idx);
                if (acc.Count < FRAME_LENGTH) return;
                if (acc[FRAME_LENGTH - 1] != EOT || acc[1] != (byte)'S') { acc.RemoveAt(0); continue; }

                byte[] frame = new byte[FRAME_LENGTH];
                for (int i = 0; i < FRAME_LENGTH; i++) frame[i] = acc[i];
                acc.RemoveRange(0, FRAME_LENGTH);

                // 与发帧对称的收帧日志
                try {
                    var sb = new System.Text.StringBuilder("收帧[原始]: ");
                    for (int i = 0; i < frame.Length; i++) sb.AppendFormat("{0:X2} ", frame[i]);
                    RaiseLog(sb.ToString().TrimEnd());
                } catch { }

                ParseFrame(frame);
            }
        }

        private void ParseFrame(byte[] frame) {
            byte cmd0   = frame[2];
            byte cmd1   = frame[3];
            byte rawD4  = frame[4];
            int min     = frame[5], sec = frame[6], cs = frame[7];
            int hour    = (frame[8] >> 4) & 0x0F;
            int ms1     = frame[8] & 0x0F;
            byte rawD10 = frame[10];

            // D4 拆分：实际泳道号 + 终点端/另一端标识
            bool isFinishEnd = rawD4 < 10;
            int actualLane = isFinishEnd ? rawD4 : rawD4 - 10;
            int lane = actualLane < 20 ? _moduleToLane[actualLane] : actualLane;
            double time = hour * 3600.0 + min * 60.0 + sec + cs / 100.0 + ms1 / 1000.0;

            // 2026-05-12 抢跳：0x1A 出发台 D10≠0 → time 取反为负
            bool isFalseStart = false;
            if (cmd0 == 0x1A && rawD10 != 0) {
                isFalseStart = true;
                time = -time;
            }

            // 2026-05-13(2) 电池电压：0x4B 帧 d3:d4 BIG-ENDIAN mV → V
            double batteryVolt = 0.0;
            if (cmd0 == 0x4B) {
                int v_mV = (frame[3] << 8) | frame[4];
                batteryVolt = v_mV / 1000.0;
            }

            TimingCommandType cmdType;
            string cmdName;
            switch (cmd0) {
                case 0x16: cmdType = TimingCommandType.Touchpad;           cmdName = "Touchpad";           break;
                case 0x17: cmdType = TimingCommandType.PushButton1;        cmdName = "PushButton1";        break;
                case 0x18: cmdType = TimingCommandType.PushButton2;        cmdName = "PushButton2";        break;
                case 0x19: cmdType = TimingCommandType.PushButton3;        cmdName = "PushButton3";        break;
                case 0x1A: cmdType = TimingCommandType.StartingBlock;      cmdName = "StartingBlock";      break;
                case 0x1C: cmdType = TimingCommandType.StartCommand;       cmdName = "StartCommand";       break;
                case 0x1D: cmdType = TimingCommandType.TestCommand;        cmdName = "TestCommand";        break;
                case 0x20: cmdType = TimingCommandType.TimerReset;         cmdName = "TimerReset";         break;
                case 0x21: cmdType = TimingCommandType.TimerReady;         cmdName = "TimerReady";         break;
                case 0x7F: cmdType = TimingCommandType.RunningTime;        cmdName = "RunningTime";        break;
                case 0x40: cmdType = TimingCommandType.PoolConfig;         cmdName = "PoolConfig";         break;
                case 0x41: cmdType = TimingCommandType.RaceConfig;         cmdName = "RaceConfig";         break;
                case 0x42: cmdType = TimingCommandType.SetCommand;         cmdName = "SetCommand";         break;
                case 0x4B: cmdType = TimingCommandType.BatteryVoltage;     cmdName = "BatteryVoltage";     break;
                case 0x47: cmdType = TimingCommandType.LaneOpenClose;      cmdName = "LaneOpenClose";      break;
                case 0x3A: cmdType = TimingCommandType.PoolSingleOrDouble; cmdName = "PoolSingleOrDouble"; break;
                case 0x44: cmdType = TimingCommandType.PoolConfig;         cmdName = "PoolConfig";         break; // 0x44 同 0x40
                case 0x62: cmdType = TimingCommandType.LaneOrder;          cmdName = "LaneOrder";          break;
                case 0x63: cmdType = TimingCommandType.FinishPosition;     cmdName = "FinishPosition";     break;
                case 0x64: cmdType = TimingCommandType.TimingsBundle;      cmdName = "TimingsBundle";      break;
                default:
                    RaiseLog(string.Format("[硬件] 未知命令: 0x{0:X2}", cmd0));
                    return;
            }

            var data = new TimingData {
                Lane = lane,
                CommandType = cmdType,
                TimeInSeconds = time,
                Minutes = min,
                Seconds = sec,
                Centiseconds = cs,
                Milliseconds = ms1,
                ReceivedAt = DateTime.Now,
                IsFinishEnd = isFinishEnd,
                Param1 = cmd1,
                RawD4 = rawD4,
                Param5 = frame[5],
                Param6 = frame[6],
                Param7 = frame[7],
                Param8 = frame[8],
                Param10 = rawD10,
                IsFalseStart = isFalseStart,
                BatteryVoltage = batteryVolt
            };

            string endLabel = isFinishEnd ? "终点端" : "另一端";
            RaiseLog(string.Format("[硬件] D4={0} 道{1}({2}) {3} {4:F3}s", rawD4, lane, endLabel, cmdName, time));

            // 新事件: 完整对象
            Action<TimingData> hx = OnTimingDataEx;
            if (hx != null) hx(data);
            // 旧事件: 向后兼容
            Action<int, string, double> h = OnTimingData;
            if (h != null) h(lane, cmdName, time);
        }

        // ═══════ 发送命令到计时硬件 (旧 3 字节版本) ═══════
        public void SendCommand(byte cmd, byte lane = 0) {
            SendFullFrame(cmd, 0, lane);
        }

        public void DelayBetweenFrames(int milliseconds = 50) {
            try { Thread.Sleep(milliseconds); } catch { }
        }

        /// <summary>发送完整 12 字节帧。位置 3..10 全部可携带数据 (与 SwimmingScoreboard.TimingBridge 一致)。</summary>
        public void SendFullFrame(byte command, byte d3, byte d4,
                                  byte d5 = 0, byte d6 = 0, byte d7 = 0,
                                  byte d8 = 0, byte d9 = 0, byte d10 = 0) {
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

            string hex;
            try {
                hex = string.Format("F1 53 {0:X2} {1:X2} {2:X2} {3:X2} {4:X2} {5:X2} {6:X2} {7:X2} {8:X2} F4",
                    command, d3, d4, d5, d6, d7, d8, d9, d10);
            } catch (Exception fex) { hex = "(hex format failed: " + fex.Message + ")"; }

            try {
                if (ConnectionMode == HwConnectionMode.Serial && _serialPort != null && _serialPort.IsOpen) {
                    _serialPort.Write(frame, 0, frame.Length);
                    RaiseLog("发帧[串口]: " + hex);
                } else if (ConnectionMode == HwConnectionMode.Udp && _udp != null) {
                    IPEndPoint target = _udpSendTarget ?? _udpLastSender;
                    if (target != null) {
                        _udp.Send(frame, frame.Length, target);
                        RaiseLog("发帧[UDP " + target + "]: " + hex);
                    } else {
                        RaiseLog("UDP发送失败: 未知目标地址，请配置UDP发送目标或等待硬件先发送数据");
                    }
                } else {
                    RaiseLog("未发送（无连接）: " + hex);
                }
            } catch (Exception ex) {
                RaiseLog("[硬件] 发送失败: " + ex.Message + "  帧: " + hex);
            }
        }

        // ───────────────────────────────────────────────────────────────
        // 2026-05 协议扩展：与 SwimmingScoreboard.TimingBridge 完全一致
        // ───────────────────────────────────────────────────────────────

        /// <summary>把 PC 当前日期+时间下发到硬件 (0x39 Set_DateTime)，硬件 RTC 同步。</summary>
        public void SendDateTimeSync() { SendDateTimeSync(DateTime.Now); }

        public void SendDateTimeSync(DateTime dt) {
            byte yearLo = (byte)(dt.Year & 0xFF);
            byte yearHi = (byte)((dt.Year >> 8) & 0xFF);
            SendFullFrame(0x39, yearLo, yearHi,
                (byte)dt.Month, (byte)dt.Day,
                (byte)dt.Hour, (byte)dt.Minute, (byte)dt.Second, 0);
            RaiseLog(string.Format("发送日期时间同步: {0:yyyy-MM-dd HH:mm:ss}", dt));
        }

        /// <summary>泳池单/两端触板配置 (0x3A): d3=0 两端 / 1 单边</summary>
        public void SendPoolSingleOrDoubleTP(bool isSingleSide) {
            SendFullFrame(0x3A, (byte)(isSingleSide ? 1 : 0), 0);
            RaiseLog(string.Format("发送泳池触板安装方式: {0}", isSingleSide ? "单边" : "两端"));
        }

        /// <summary>2026-05-13(2) 强制全开/恢复正常 整道或某道的所有设备 (0x4C)
        /// d3=0xFF 全部道 / 0..9 单道;  d4=1 全开 / 0 恢复正常</summary>
        public void SendLaneDeviceFullOpen(int laneIndex, bool forceOpen) {
            byte d3 = (laneIndex < 0 || laneIndex >= 10) ? (byte)0xFF : (byte)laneIndex;
            byte d4 = (byte)(forceOpen ? 1 : 0);
            SendFullFrame(0x4C, d3, d4);
            RaiseLog(string.Format("发送 设备{0} {1} (0x4C)",
                d3 == 0xFF ? "全部道" : ("第" + laneIndex + "道"),
                forceOpen ? "全开(强制打开)" : "恢复正常关闭流程"));
        }

        /// <summary>2026-05-14 道次打开/关闭 (0x47): d3=0xFF 全部 / 0..9 单道; d4=1 开 / 0 关
        /// 与 0x4C 区别: 0x4C 是"设备状态机覆盖"，0x47 是"泳道整体启用/禁用"</summary>
        public void SendLaneOpenClose(int laneIndex, bool laneOpen) {
            byte d3 = (laneIndex < 0 || laneIndex >= 10) ? (byte)0xFF : (byte)laneIndex;
            byte d4 = (byte)(laneOpen ? 1 : 0);
            SendFullFrame(0x47, d3, d4);
            RaiseLog(string.Format("发送 泳道{0} {1} (0x47)",
                d3 == 0xFF ? "(全部道)" : ("第" + laneIndex + "道"),
                laneOpen ? "打开/纳入比赛" : "关闭/移出比赛"));
        }

        /// <summary>2026-05-17 单道屏蔽/启用 (0x60): d3=0..9 单道; d4=1 启用 / 0 屏蔽
        /// 与 0x47 区别: 0x47 = 全部开关 TP/SB/MB; 0x60 = 单道整体启用/屏蔽(动道次按钮)</summary>
        public void SendLaneEnableDisable(int laneIndex, bool enable) {
            if (laneIndex < 0 || laneIndex >= 10) {
                RaiseLog(string.Format("SendLaneEnableDisable: 非法 lane {0}（仅 0..9）", laneIndex));
                return;
            }
            SendFullFrame(0x60, (byte)laneIndex, (byte)(enable ? 1 : 0));
            RaiseLog(string.Format("发送 第{0}道 {1} (0x60)", laneIndex, enable ? "启用" : "屏蔽"));
        }

        /// <summary>2026-05-17 单道某侧剩余圈数同步 (0x61): d3=道(0..9) d4=端(0左/1右) d5=圈数(0..255)</summary>
        public void SendLapRemaining(int laneIndex, bool isLeft, int remaining) {
            if (laneIndex < 0 || laneIndex >= 10) {
                RaiseLog(string.Format("SendLapRemaining: 非法 lane {0}（仅 0..9）", laneIndex));
                return;
            }
            if (remaining < 0) remaining = 0;
            if (remaining > 255) remaining = 255;
            SendFullFrame(0x61, (byte)laneIndex, (byte)(isLeft ? 0 : 1), (byte)remaining);
            RaiseLog(string.Format("发送 第{0}道 {1}侧剩余圈数={2} (0x61)", laneIndex, isLeft ? "左" : "右", remaining));
        }

        /// <summary>2026-05-18 让硬件主控完整重画一次 (0x65 Set_RefreshDisplay)
        /// 硬件接收时: 保存比赛状态 → SwimControl_init → 恢复并重画。
        /// 用途: PC 端"参数设置"对话框 OK 后下发，让硬件按新参数主动刷主控界面。</summary>
        public void SendRefreshDisplay() {
            SendFullFrame(0x65, 0, 0);
            RaiseLog("发送 刷新硬件主控显示 (0x65)");
        }

        private void RaiseStatus(string s) { var h = OnStatusChanged; if (h != null) h(s); }
        private void RaiseLog(string s)    { var h = OnLog;            if (h != null) h(s); }

        public void Dispose() { Disconnect(); }
    }
}
