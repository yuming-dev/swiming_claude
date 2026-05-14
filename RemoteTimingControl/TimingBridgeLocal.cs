using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RemoteTimingControl
{
    public enum HwConnectionMode { None, Serial, Udp }

    /// <summary>
    /// 本地计时硬件直连 — 串口 + UDP 双模式
    /// 与 SwimmingScoreboard.TimingBridge 使用相同的帧协议（游泳计时通讯协议 2023-11-13）
    /// 帧格式(12字节): D0=SOH(0xF1) | D1='S' | D2=CMD | D3=CMD1 | D4=Lane
    ///                | D5=Min | D6=Sec | D7=Cs | D8=(Hr&lt;&lt;4)|Ms1 | D9=0 | D10=0 | D11=EOT(0xF4)
    /// 命令码: 0x16=触板 0x17/18/19=盲表1/2/3 0x1A=出发台 0x1C=开始计时
    ///         0x1D=测试 0x20=计时清零 0x21=准备就绪 0x7F=滚动时间
    ///         0x40=设置泳池参数 0x41=设置比赛距离 0x42=设置命令
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

        // lane, commandType, timeInSeconds
        public event Action<int, string, double> OnTimingData;
        public event Action<string> OnStatusChanged;
        public event Action<string> OnLog;

        public TimingBridgeLocal() {
            StatusText = "未连接";
            ConnectionMode = HwConnectionMode.None;
            for (int i = 0; i < 20; i++) _moduleToLane[i] = i;
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
        // 使用单一UdpClient绑定接收端口，收发共用同一套接字（与主服务器一致）
        // 这样对端（模拟器/硬件）可以在同一端口看到请求与回复，更可靠
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

                byte cmd0 = acc[2];
                byte rawD4 = acc[4]; // D4: 0-9=终点端泳道号, 10-19=另一端泳道号(实际泳道=D4-10)
                // D5=分 D6=秒 D7=1/100秒 D8=(小时<<4)|(1/1000秒个位)
                int min = acc[5], sec = acc[6], cs = acc[7];
                int hour = (acc[8] >> 4) & 0x0F;
                int ms1  = acc[8] & 0x0F;
                byte rawD10 = acc[10];   // 2026-05-12 协议扩展：0x1A 出发台命令的符号位
                acc.RemoveRange(0, FRAME_LENGTH);

                // D4 拆分：实际泳道号 + 终点端/另一端标识
                bool isFinishEnd = rawD4 < 10;
                int actualLane = isFinishEnd ? rawD4 : rawD4 - 10;
                int lane = actualLane < 20 ? _moduleToLane[actualLane] : actualLane;
                double time = hour * 3600.0 + min * 60.0 + sec + cs / 100.0 + ms1 / 1000.0;
                // 2026-05-12 抢跳：0x1A 出发台命令的 D10≠0 表示运动员早于发令枪，时间取反为负值
                if (cmd0 == 0x1A && rawD10 != 0) time = -time;

                string cmdType;
                switch (cmd0) {
                    case 0x16: cmdType = "Touchpad"; break;        // 触板时间成绩
                    case 0x17: cmdType = "PushButton1"; break;     // 盲表1
                    case 0x18: cmdType = "PushButton2"; break;     // 盲表2
                    case 0x19: cmdType = "PushButton3"; break;     // 盲表3
                    case 0x1A: cmdType = "StartingBlock"; break;   // 出发台
                    case 0x1C: cmdType = "StartCommand"; break;    // 发令开始计时
                    case 0x1D: cmdType = "TestCommand"; break;     // 测试设备
                    case 0x20: cmdType = "TimerReset"; break;      // 计时清零
                    case 0x21: cmdType = "TimerReady"; break;      // 准备就绪
                    case 0x7F: cmdType = "RunningTime"; break;     // 滚动时间
                    case 0x40: cmdType = "PoolConfig"; break;      // 设置泳池参数
                    case 0x41: cmdType = "RaceConfig"; break;      // 设置比赛距离
                    case 0x42: cmdType = "SetCommand"; break;      // 设置命令
                    default:
                        RaiseLog(string.Format("[硬件] 未知命令: 0x{0:X2}", cmd0));
                        continue;
                }

                string endLabel = isFinishEnd ? "终点端" : "另一端";
                RaiseLog(string.Format("[硬件] D4={0} 道{1}({2}) {3} {4:F3}s", rawD4, lane, endLabel, cmdType, time));
                Action<int, string, double> h = OnTimingData;
                if (h != null) h(lane, cmdType, time);
            }
        }

        // ═══════ 发送命令到计时硬件 ═══════
        public void SendCommand(byte cmd, byte lane = 0) {
            byte[] frame = new byte[FRAME_LENGTH];
            frame[0] = SOH;
            frame[1] = (byte)'S';
            frame[2] = cmd;
            frame[3] = 0;
            frame[4] = lane;
            frame[FRAME_LENGTH - 1] = EOT;
            try {
                if (ConnectionMode == HwConnectionMode.Serial && _serialPort != null && _serialPort.IsOpen) {
                    _serialPort.Write(frame, 0, frame.Length);
                } else if (ConnectionMode == HwConnectionMode.Udp && _udp != null) {
                    IPEndPoint target = _udpSendTarget ?? _udpLastSender;
                    if (target != null) {
                        _udp.Send(frame, frame.Length, target);
                    } else {
                        RaiseLog("[硬件] UDP发送失败: 未知目标地址，请配置发送目标或等待硬件先发送数据");
                    }
                } else {
                    RaiseLog("[硬件] 未连接，无法发送命令");
                }
            } catch (Exception ex) {
                RaiseLog("[硬件] 发送失败: " + ex.Message);
            }
        }

        private void RaiseStatus(string s) { var h = OnStatusChanged; if (h != null) h(s); }
        private void RaiseLog(string s) { var h = OnLog; if (h != null) h(s); }

        public void Dispose() { Disconnect(); }
    }
}
