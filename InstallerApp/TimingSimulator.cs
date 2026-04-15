using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

class TimingSimulatorForm : Form
{
    // 协议常量
    const byte SOH = 0xF1;
    const byte EOT = 0xF4;
    const int FRAME_LEN = 12;

    // 连接
    SerialPort _serial;
    UdpClient _udpSend;
    UdpClient _udpRecv;
    Thread _recvThread;
    volatile bool _running;

    // 控件
    ComboBox cmbConnType, cmbPort, cmbLane;
    TextBox txtUdpHost, txtUdpSendPort, txtUdpRecvPort, txtTime;
    Button btnConnect, btnDisconnect;
    Label lblStatus;
    ListBox lstLog;
    NumericUpDown nudLanes;

    // 比赛模拟
    Button btnReady, btnStart, btnReset;
    Button btnTouchpad, btnBlind1, btnBlind2, btnBlind3, btnStartBlock;
    Button btnAutoRace;
    CheckBox chkAutoBlind;
    volatile bool _autoRunning;

    public TimingSimulatorForm()
    {
        Text = "计时硬件模拟器 (Timing Hardware Simulator)";
        Size = new Size(780, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei", 10);

        BuildUI();
        PopulateComPorts();
    }

    void PopulateComPorts()
    {
        cmbPort.Items.Clear();
        foreach (string p in SerialPort.GetPortNames()) cmbPort.Items.Add(p);
        if (cmbPort.Items.Count > 0) cmbPort.SelectedIndex = 0;
    }

    void BuildUI()
    {
        // ═══ 连接区 ═══
        var grpConn = MakeGroup("连接设置", 10, 10, 740, 130);
        Controls.Add(grpConn);

        grpConn.Controls.Add(MakeLabel("连接方式:", 15, 25));
        cmbConnType = new ComboBox { Location = new Point(90, 22), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbConnType.Items.AddRange(new[] { "串口", "UDP" });
        cmbConnType.SelectedIndex = 1;
        cmbConnType.SelectedIndexChanged += (s, e) => ToggleConnUI();
        grpConn.Controls.Add(cmbConnType);

        // 串口
        grpConn.Controls.Add(MakeLabel("串口:", 210, 25));
        cmbPort = new ComboBox { Location = new Point(255, 22), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        grpConn.Controls.Add(cmbPort);
        var btnRefresh = MakeBtn("刷新", 355, 21, 50, 26, Color.FromArgb(71, 85, 105));
        btnRefresh.Click += (s, e) => PopulateComPorts();
        grpConn.Controls.Add(btnRefresh);

        // UDP
        grpConn.Controls.Add(MakeLabel("目标IP:", 210, 25));
        txtUdpHost = new TextBox { Text = "127.0.0.1", Location = new Point(275, 22), Width = 110, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpHost);
        grpConn.Controls.Add(MakeLabel("发送端口:", 395, 25));
        txtUdpSendPort = new TextBox { Text = "5001", Location = new Point(470, 22), Width = 60, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpSendPort);
        grpConn.Controls.Add(MakeLabel("接收端口:", 540, 25));
        txtUdpRecvPort = new TextBox { Text = "5002", Location = new Point(615, 22), Width = 60, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpRecvPort);

        btnConnect = MakeBtn("连接", 15, 58, 100, 32, Color.FromArgb(34, 197, 94));
        btnConnect.Click += (s, e) => DoConnect();
        grpConn.Controls.Add(btnConnect);

        btnDisconnect = MakeBtn("断开", 125, 58, 100, 32, Color.FromArgb(239, 68, 68));
        btnDisconnect.Enabled = false;
        btnDisconnect.Click += (s, e) => DoDisconnect();
        grpConn.Controls.Add(btnDisconnect);

        lblStatus = new Label { Text = "● 未连接", ForeColor = Color.FromArgb(239, 68, 68), Location = new Point(240, 64), AutoSize = true, Font = new Font("Microsoft YaHei", 10, FontStyle.Bold) };
        grpConn.Controls.Add(lblStatus);

        grpConn.Controls.Add(MakeLabel("协议: SOH(F1) | 'S' | CMD | 00 | Lane | Min | Sec | Cs | Ms | 00 | 00 | EOT(F4)", 15, 98, Color.FromArgb(148, 163, 184), 9));

        ToggleConnUI();

        // ═══ 比赛控制命令 ═══
        var grpCmd = MakeGroup("比赛控制命令（发送到主服务器）", 10, 150, 360, 130);
        Controls.Add(grpCmd);

        btnReady = MakeBtn("就  位 (0x10)", 15, 25, 150, 38, Color.FromArgb(245, 158, 11));
        btnReady.Click += (s, e) => SendCommand(0x10, 0, "就位");
        grpCmd.Controls.Add(btnReady);

        btnStart = MakeBtn("发  令 (0x11)", 175, 25, 150, 38, Color.FromArgb(34, 197, 94));
        btnStart.Click += (s, e) => SendCommand(0x11, 0, "发令");
        grpCmd.Controls.Add(btnStart);

        btnReset = MakeBtn("计时复位 (0x12)", 15, 73, 150, 38, Color.FromArgb(239, 68, 68));
        btnReset.Click += (s, e) => SendCommand(0x12, 0, "计时复位");
        grpCmd.Controls.Add(btnReset);

        // ═══ 计时数据发送 ═══
        var grpData = MakeGroup("计时数据发送", 10, 290, 360, 240);
        Controls.Add(grpData);

        grpData.Controls.Add(MakeLabel("泳道:", 15, 28));
        cmbLane = new ComboBox { Location = new Point(60, 25), Width = 55, DropDownStyle = ComboBoxStyle.DropDownList };
        for (int i = 0; i <= 9; i++) cmbLane.Items.Add(i.ToString());
        cmbLane.SelectedIndex = 4;
        grpData.Controls.Add(cmbLane);

        grpData.Controls.Add(MakeLabel("成绩:", 125, 28));
        txtTime = new TextBox { Text = "25.34", Location = new Point(170, 25), Width = 80, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11) };
        grpData.Controls.Add(txtTime);
        grpData.Controls.Add(MakeLabel("秒", 255, 28));

        btnTouchpad = MakeBtn("触板 (0x06)", 15, 65, 155, 34, Color.FromArgb(59, 130, 246));
        btnTouchpad.Click += (s, e) => SendTimingData(0x06, "触板");
        grpData.Controls.Add(btnTouchpad);

        btnStartBlock = MakeBtn("出发台 (0x0A)", 180, 65, 155, 34, Color.FromArgb(124, 58, 237));
        btnStartBlock.Click += (s, e) => SendTimingData(0x0A, "出发台");
        grpData.Controls.Add(btnStartBlock);

        btnBlind1 = MakeBtn("盲表1 (0x07)", 15, 108, 100, 34, Color.FromArgb(100, 116, 139));
        btnBlind1.Click += (s, e) => SendTimingData(0x07, "盲表1");
        grpData.Controls.Add(btnBlind1);

        btnBlind2 = MakeBtn("盲表2 (0x08)", 125, 108, 100, 34, Color.FromArgb(100, 116, 139));
        btnBlind2.Click += (s, e) => SendTimingData(0x08, "盲表2");
        grpData.Controls.Add(btnBlind2);

        btnBlind3 = MakeBtn("盲表3 (0x09)", 235, 108, 100, 34, Color.FromArgb(100, 116, 139));
        btnBlind3.Click += (s, e) => SendTimingData(0x09, "盲表3");
        grpData.Controls.Add(btnBlind3);

        // 自动模拟
        var sep = new Label { Text = "── 自动模拟比赛 ──", ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(15, 155), AutoSize = true, Font = new Font("Microsoft YaHei", 9) };
        grpData.Controls.Add(sep);

        grpData.Controls.Add(MakeLabel("泳道数:", 15, 182));
        nudLanes = new NumericUpDown { Location = new Point(75, 179), Width = 50, Minimum = 1, Maximum = 10, Value = 8, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White };
        grpData.Controls.Add(nudLanes);

        chkAutoBlind = new CheckBox { Text = "含盲表", Location = new Point(135, 180), AutoSize = true, Checked = true, ForeColor = Color.White };
        grpData.Controls.Add(chkAutoBlind);

        btnAutoRace = MakeBtn("模拟完整比赛", 230, 175, 110, 34, Color.FromArgb(217, 119, 6));
        btnAutoRace.Click += (s, e) => {
            if (_autoRunning) { _autoRunning = false; btnAutoRace.Text = "模拟完整比赛"; return; }
            _autoRunning = true;
            btnAutoRace.Text = "停止模拟";
            new Thread(AutoRaceThread) { IsBackground = true }.Start();
        };
        grpData.Controls.Add(btnAutoRace);

        // ═══ 日志 ═══
        var grpLog = MakeGroup("通信日志", 380, 150, 370, 380);
        Controls.Add(grpLog);

        lstLog = new ListBox {
            Location = new Point(10, 22), Size = new Size(350, 320),
            BackColor = Color.FromArgb(15, 23, 42), ForeColor = Color.FromArgb(148, 163, 184),
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9),
            HorizontalScrollbar = true
        };
        grpLog.Controls.Add(lstLog);

        var btnClear = MakeBtn("清除日志", 10, 346, 80, 26, Color.FromArgb(71, 85, 105));
        btnClear.Font = new Font("Microsoft YaHei", 9);
        btnClear.Click += (s, e) => lstLog.Items.Clear();
        grpLog.Controls.Add(btnClear);

        // ═══ 协议说明 ═══
        var grpHelp = MakeGroup("协议说明", 10, 540, 740, 110);
        Controls.Add(grpHelp);

        var helpText = new Label {
            Text = "命令码:  0x06=触板  0x07/08/09=盲表1/2/3  0x0A=出发台  0x0C=发令信号(旧)\n" +
                   "         0x10=就位  0x11=发令  0x12=计时复位\n" +
                   "帧格式:  F1 53 CMD 00 Lane Min Sec CentiSec MilliSec 00 00 F4  (12字节)\n" +
                   "时间字段: Min(分) Sec(秒) CentiSec(百分秒0-99) MilliSec(毫秒0-9)\n" +
                   "示例:  1:23.456 → Min=1, Sec=23, Cs=45, Ms=6",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Consolas", 9.5f),
            Location = new Point(10, 20), Size = new Size(720, 82)
        };
        grpHelp.Controls.Add(helpText);
    }

    // ═══════ UI 辅助 ═══════
    GroupBox MakeGroup(string text, int x, int y, int w, int h)
    {
        return new GroupBox { Text = text, ForeColor = Color.FromArgb(59, 130, 246), Location = new Point(x, y), Size = new Size(w, h), Font = new Font("Microsoft YaHei", 10, FontStyle.Bold) };
    }

    Label MakeLabel(string text, int x, int y, Color? color = null, float size = 10)
    {
        return new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = color ?? Color.White, Font = new Font("Microsoft YaHei", size) };
    }

    Button MakeBtn(string text, int x, int y, int w, int h, Color bg)
    {
        var btn = new Button {
            Text = text, Location = new Point(x, y), Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Microsoft YaHei", 10, FontStyle.Bold), Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    void ToggleConnUI()
    {
        bool isUdp = cmbConnType.SelectedIndex == 1;
        cmbPort.Visible = !isUdp;
        txtUdpHost.Visible = isUdp;
        txtUdpSendPort.Visible = isUdp;
        txtUdpRecvPort.Visible = isUdp;
    }

    void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(msg))); return; }
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg;
        lstLog.Items.Add(line);
        if (lstLog.Items.Count > 500) lstLog.Items.RemoveAt(0);
        lstLog.TopIndex = lstLog.Items.Count - 1;
    }

    // ═══════ 连接/断开 ═══════
    void DoConnect()
    {
        DoDisconnect();
        try {
            if (cmbConnType.SelectedIndex == 0) {
                // 串口
                if (cmbPort.SelectedItem == null) { Log("[错误] 请选择串口"); return; }
                _serial = new SerialPort(cmbPort.SelectedItem.ToString(), 115200, Parity.None, 8, StopBits.One);
                _serial.Open();
                _running = true;
                _recvThread = new Thread(SerialRecvLoop) { IsBackground = true };
                _recvThread.Start();
                Log("[串口] 已连接: " + cmbPort.SelectedItem);
            } else {
                // UDP
                int sendPort, recvPort;
                int.TryParse(txtUdpSendPort.Text, out sendPort);
                int.TryParse(txtUdpRecvPort.Text, out recvPort);
                _udpSend = new UdpClient();
                _udpSend.Connect(txtUdpHost.Text, sendPort);
                try {
                    _udpRecv = new UdpClient(recvPort);
                    _running = true;
                    _recvThread = new Thread(UdpRecvLoop) { IsBackground = true };
                    _recvThread.Start();
                } catch { }
                Log(string.Format("[UDP] 发送→{0}:{1}  接收←{2}", txtUdpHost.Text, sendPort, recvPort));
            }

            lblStatus.Text = "● 已连接";
            lblStatus.ForeColor = Color.FromArgb(34, 197, 94);
            btnConnect.Enabled = false;
            btnDisconnect.Enabled = true;
        } catch (Exception ex) {
            Log("[错误] 连接失败: " + ex.Message);
        }
    }

    void DoDisconnect()
    {
        _running = false;
        _autoRunning = false;
        try { if (_serial != null && _serial.IsOpen) _serial.Close(); } catch { }
        try { if (_udpSend != null) _udpSend.Close(); } catch { }
        try { if (_udpRecv != null) _udpRecv.Close(); } catch { }
        _serial = null; _udpSend = null; _udpRecv = null;
        lblStatus.Text = "● 未连接";
        lblStatus.ForeColor = Color.FromArgb(239, 68, 68);
        btnConnect.Enabled = true;
        btnDisconnect.Enabled = false;
    }

    // ═══════ 接收线程 ═══════
    void SerialRecvLoop()
    {
        byte[] buf = new byte[1024];
        var acc = new List<byte>();
        while (_running && _serial != null && _serial.IsOpen) {
            try {
                int n = _serial.BytesToRead;
                if (n > 0) {
                    int c = _serial.Read(buf, 0, Math.Min(n, buf.Length));
                    for (int i = 0; i < c; i++) acc.Add(buf[i]);
                    ParseReceived(acc);
                } else Thread.Sleep(5);
            } catch { if (_running) break; }
        }
    }

    void UdpRecvLoop()
    {
        var acc = new List<byte>();
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (_running && _udpRecv != null) {
            try {
                byte[] data = _udpRecv.Receive(ref ep);
                for (int i = 0; i < data.Length; i++) acc.Add(data[i]);
                ParseReceived(acc);
            } catch { if (_running) break; }
        }
    }

    void ParseReceived(List<byte> acc)
    {
        while (acc.Count >= FRAME_LEN) {
            int idx = acc.IndexOf(SOH);
            if (idx < 0) { acc.Clear(); return; }
            if (idx > 0) acc.RemoveRange(0, idx);
            if (acc.Count < FRAME_LEN) return;
            if (acc[FRAME_LEN - 1] != EOT || acc[1] != (byte)'S') { acc.RemoveAt(0); continue; }

            byte cmd = acc[2];
            byte lane = acc[4];
            int min = acc[5], sec = acc[6], cs = acc[7], ms = acc[8];
            double time = min * 60.0 + sec + cs / 100.0 + ms / 1000.0;

            string cmdName = GetCmdName(cmd);
            Log(string.Format("[接收] 泳道{0} {1}(0x{2:X2}) {3}", lane, cmdName, cmd, FormatTime(time)));

            acc.RemoveRange(0, FRAME_LEN);
        }
    }

    // ═══════ 发送 ═══════
    byte[] BuildFrame(byte cmd, int lane, double timeInSeconds)
    {
        int totalCs = (int)Math.Round(timeInSeconds * 100);
        int min = totalCs / 6000;
        totalCs %= 6000;
        int sec = totalCs / 100;
        int cs = totalCs % 100;
        int ms = (int)((timeInSeconds * 1000) % 10);

        byte[] frame = new byte[FRAME_LEN];
        frame[0] = SOH;
        frame[1] = (byte)'S';
        frame[2] = cmd;
        frame[3] = 0;
        frame[4] = (byte)lane;
        frame[5] = (byte)min;
        frame[6] = (byte)sec;
        frame[7] = (byte)cs;
        frame[8] = (byte)ms;
        frame[FRAME_LEN - 1] = EOT;
        return frame;
    }

    void SendFrame(byte[] frame)
    {
        try {
            if (_serial != null && _serial.IsOpen) {
                _serial.Write(frame, 0, frame.Length);
            } else if (_udpSend != null) {
                _udpSend.Send(frame, frame.Length);
            } else {
                Log("[错误] 未连接");
                return;
            }
        } catch (Exception ex) {
            Log("[错误] 发送失败: " + ex.Message);
        }
    }

    void SendCommand(byte cmd, int lane, string desc)
    {
        byte[] frame = BuildFrame(cmd, lane, 0);
        SendFrame(frame);
        Log(string.Format("[发送] {0} (0x{1:X2})", desc, cmd));
    }

    void SendTimingData(byte cmd, string desc)
    {
        int lane = cmbLane.SelectedIndex;
        double time = ParseTime(txtTime.Text);
        if (time <= 0) { Log("[错误] 成绩格式无效"); return; }

        byte[] frame = BuildFrame(cmd, lane, time);
        SendFrame(frame);
        Log(string.Format("[发送] 泳道{0} {1}(0x{2:X2}) {3}", lane, desc, cmd, FormatTime(time)));
    }

    // ═══════ 自动模拟比赛 ═══════
    void AutoRaceThread()
    {
        var rng = new Random();
        int lanes = (int)Invoke(new Func<int>(() => (int)nudLanes.Value));
        bool withBlind = (bool)Invoke(new Func<bool>(() => chkAutoBlind.Checked));

        Log("═══ 自动模拟开始 ═══");

        // 1. 就位
        if (!_autoRunning) return;
        Log("[自动] 发送就位...");
        SendFrame(BuildFrame(0x10, 0, 0));
        Thread.Sleep(2000);

        // 2. 发令
        if (!_autoRunning) return;
        Log("[自动] 发送发令...");
        SendFrame(BuildFrame(0x11, 0, 0));
        Thread.Sleep(500);

        // 3. 出发台反应时
        for (int i = 1; i <= lanes && _autoRunning; i++) {
            double rt = 0.15 + rng.NextDouble() * 0.45; // 0.15~0.60s
            SendFrame(BuildFrame(0x0A, i, rt));
            Log(string.Format("[自动] 道{0} 出发台 反应时={1:F2}s", i, rt));
            Thread.Sleep(50);
        }

        // 4. 等待泳道关闭倒计时
        Log("[自动] 等待泳道关闭倒计时...");
        Thread.Sleep(8000);

        // 5. 50米分段（模拟100米比赛）
        if (!_autoRunning) return;
        Log("[自动] ─── 50米分段 ───");
        double[] splitTimes = new double[lanes + 1];
        // 按随机顺序触碰（模拟不同速度）
        var order = new List<int>();
        for (int i = 1; i <= lanes; i++) order.Add(i);
        Shuffle(order, rng);

        double baseTime = 24.0 + rng.NextDouble() * 3.0;
        foreach (int i in order) {
            if (!_autoRunning) return;
            double t = baseTime + rng.NextDouble() * 4.0;
            splitTimes[i] = t;

            // 触板
            SendFrame(BuildFrame(0x06, i, t));
            Log(string.Format("[自动] 道{0} 触板 50m={1}", i, FormatTime(t)));

            // 盲表（略有偏差）
            if (withBlind) {
                Thread.Sleep(30);
                SendFrame(BuildFrame(0x07, i, t + (rng.NextDouble() * 0.06 - 0.03)));
                Thread.Sleep(20);
                SendFrame(BuildFrame(0x08, i, t + (rng.NextDouble() * 0.06 - 0.03)));
                Thread.Sleep(20);
                SendFrame(BuildFrame(0x09, i, t + (rng.NextDouble() * 0.06 - 0.03)));
            }

            Thread.Sleep(200 + rng.Next(500));
        }

        // 6. 等待泳道再次关闭
        Log("[自动] 等待下一段泳道关闭...");
        Thread.Sleep(6000);

        // 7. 100米终点
        if (!_autoRunning) return;
        Log("[自动] ─── 100米终点 ───");
        Shuffle(order, rng);

        foreach (int i in order) {
            if (!_autoRunning) return;
            double t = splitTimes[i] + 26.0 + rng.NextDouble() * 5.0;

            SendFrame(BuildFrame(0x06, i, t));
            Log(string.Format("[自动] 道{0} 触板 100m={1} (终点)", i, FormatTime(t)));

            if (withBlind) {
                Thread.Sleep(30);
                SendFrame(BuildFrame(0x07, i, t + (rng.NextDouble() * 0.06 - 0.03)));
                Thread.Sleep(20);
                SendFrame(BuildFrame(0x08, i, t + (rng.NextDouble() * 0.06 - 0.03)));
                Thread.Sleep(20);
                SendFrame(BuildFrame(0x09, i, t + (rng.NextDouble() * 0.06 - 0.03)));
            }

            Thread.Sleep(300 + rng.Next(800));
        }

        Log("═══ 自动模拟结束 ═══");
        _autoRunning = false;
        BeginInvoke(new Action(() => btnAutoRace.Text = "模拟完整比赛"));
    }

    void Shuffle(List<int> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            int tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    // ═══════ 工具 ═══════
    double ParseTime(string s)
    {
        s = s.Trim();
        if (s.Contains(":")) {
            string[] parts = s.Split(':');
            double m = 0, sec = 0;
            double.TryParse(parts[0], out m);
            if (parts.Length > 1) double.TryParse(parts[1], out sec);
            return m * 60 + sec;
        }
        double v;
        return double.TryParse(s, out v) ? v : 0;
    }

    string FormatTime(double sec)
    {
        if (sec <= 0) return "0.00";
        int totalCs = (int)Math.Round(sec * 100);
        int s = totalCs / 100;
        int cs = totalCs % 100;
        int min = s / 60;
        s %= 60;
        if (min > 0) return string.Format("{0}:{1:D2}.{2:D2}", min, s, cs);
        return string.Format("{0}.{1:D2}", s, cs);
    }

    string GetCmdName(byte cmd)
    {
        switch (cmd) {
            case 0x06: return "触板";
            case 0x07: return "盲表1";
            case 0x08: return "盲表2";
            case 0x09: return "盲表3";
            case 0x0A: return "出发台";
            case 0x0C: return "发令信号";
            case 0x10: return "就位";
            case 0x11: return "发令";
            case 0x12: return "计时复位";
            default: return string.Format("未知(0x{0:X2})", cmd);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _autoRunning = false;
        DoDisconnect();
        base.OnFormClosing(e);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TimingSimulatorForm());
    }
}
