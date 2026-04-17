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
    TcpListener _tcpServer;
    TcpClient _tcpClient;
    NetworkStream _tcpStream;
    Thread _recvThread;
    volatile bool _running;

    // 控件
    ComboBox cmbConnType, cmbPort, cmbLane, cmbEnd;
    TextBox txtUdpHost, txtUdpSendPort, txtUdpRecvPort, txtTcpPort, txtTime;
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

    // 时钟
    Label lblClock, lblClockState;
    System.Windows.Forms.Timer _clockTimer;
    DateTime _clockStart;
    volatile bool _clockRunning;
    double _clockOffset; // 已累计时间（暂停/恢复用）

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
        cmbConnType = new ComboBox { Location = new Point(90, 22), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbConnType.Items.AddRange(new[] { "串口", "UDP", "TCP服务器" });
        cmbConnType.SelectedIndex = 2; // 默认TCP服务器
        cmbConnType.SelectedIndexChanged += (s, e) => ToggleConnUI();
        grpConn.Controls.Add(cmbConnType);

        // 串口控件
        grpConn.Controls.Add(MakeLabel("串口:", 220, 25));
        cmbPort = new ComboBox { Location = new Point(265, 22), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        grpConn.Controls.Add(cmbPort);
        var btnRefresh = MakeBtn("刷新", 365, 21, 50, 26, Color.FromArgb(71, 85, 105));
        btnRefresh.Click += (s, e) => PopulateComPorts();
        grpConn.Controls.Add(btnRefresh);

        // UDP控件
        txtUdpHost = new TextBox { Text = "127.0.0.1", Location = new Point(275, 22), Width = 110, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpHost);
        txtUdpSendPort = new TextBox { Text = "5001", Location = new Point(470, 22), Width = 60, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpSendPort);
        txtUdpRecvPort = new TextBox { Text = "5002", Location = new Point(615, 22), Width = 60, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpRecvPort);

        // TCP服务器控件
        txtTcpPort = new TextBox { Text = "5555", Location = new Point(320, 22), Width = 70, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtTcpPort);

        btnConnect = MakeBtn("连接", 15, 58, 100, 32, Color.FromArgb(34, 197, 94));
        btnConnect.Click += (s, e) => DoConnect();
        grpConn.Controls.Add(btnConnect);

        btnDisconnect = MakeBtn("断开", 125, 58, 100, 32, Color.FromArgb(239, 68, 68));
        btnDisconnect.Enabled = false;
        btnDisconnect.Click += (s, e) => DoDisconnect();
        grpConn.Controls.Add(btnDisconnect);

        lblStatus = new Label { Text = "● 未连接", ForeColor = Color.FromArgb(239, 68, 68), Location = new Point(240, 64), AutoSize = true, Font = new Font("Microsoft YaHei", 10, FontStyle.Bold) };
        grpConn.Controls.Add(lblStatus);

        grpConn.Controls.Add(MakeLabel("协议: SOH(F1) | 'S'(53) | CMD | CMD1 | Lane | Min | Sec | Cs | (Hr<<4)|Ms1 | 00 | 00 | EOT(F4)", 15, 98, Color.FromArgb(148, 163, 184), 9));

        ToggleConnUI();

        // ═══ 比赛控制命令 ═══
        var grpCmd = MakeGroup("比赛控制命令（发送到主服务器）", 10, 150, 360, 130);
        Controls.Add(grpCmd);

        btnReady = MakeBtn("准备就绪 (0x21)", 15, 25, 150, 38, Color.FromArgb(245, 158, 11));
        btnReady.Click += (s, e) => {
            SendCommand(0x21, 0, "准备就绪");
            if (lblClockState != null) { lblClockState.Text = "■ 就位"; lblClockState.ForeColor = Color.FromArgb(245, 158, 11); }
        };
        grpCmd.Controls.Add(btnReady);

        btnStart = MakeBtn("开始计时 (0x1C)", 175, 25, 150, 38, Color.FromArgb(34, 197, 94));
        btnStart.Click += (s, e) => { SendCommand(0x1C, 0, "开始计时"); StartClock(); };
        grpCmd.Controls.Add(btnStart);

        btnReset = MakeBtn("计时清零 (0x20)", 15, 73, 150, 38, Color.FromArgb(239, 68, 68));
        btnReset.Click += (s, e) => {
            SendCommand(0x20, 0, "计时清零");
            byte[] rtFrame = BuildFrame(0x7F, 0, 0.0);
            SendFrame(rtFrame);
            Log("[发送] 滚动时间 0 (" + FrameToHex(rtFrame) + ")");
            ResetClock();
        };
        grpCmd.Controls.Add(btnReset);

        // ═══ 计时数据发送 ═══
        var grpData = MakeGroup("计时数据发送", 10, 290, 360, 240);
        Controls.Add(grpData);

        grpData.Controls.Add(MakeLabel("泳道:", 15, 28));
        cmbLane = new ComboBox { Location = new Point(55, 25), Width = 45, DropDownStyle = ComboBoxStyle.DropDownList };
        for (int i = 0; i <= 9; i++) cmbLane.Items.Add(i.ToString());
        cmbLane.SelectedIndex = 4;
        grpData.Controls.Add(cmbLane);

        // 端选择（D4 编码：终点端=0-9, 另一端=10-19）
        cmbEnd = new ComboBox { Location = new Point(105, 25), Width = 75, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbEnd.Items.Add("终点端");
        cmbEnd.Items.Add("另一端");
        cmbEnd.SelectedIndex = 0;
        grpData.Controls.Add(cmbEnd);

        grpData.Controls.Add(MakeLabel("成绩:", 185, 28));
        txtTime = new TextBox { Text = "25.34", Location = new Point(225, 25), Width = 80, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11) };
        grpData.Controls.Add(txtTime);
        grpData.Controls.Add(MakeLabel("秒", 310, 28));

        btnTouchpad = MakeBtn("触板 (0x16)", 15, 65, 155, 34, Color.FromArgb(59, 130, 246));
        btnTouchpad.Click += (s, e) => SendTimingData(0x16, "触板");
        grpData.Controls.Add(btnTouchpad);

        btnStartBlock = MakeBtn("出发台 (0x1A)", 180, 65, 155, 34, Color.FromArgb(124, 58, 237));
        btnStartBlock.Click += (s, e) => SendTimingData(0x1A, "出发台");
        grpData.Controls.Add(btnStartBlock);

        btnBlind1 = MakeBtn("盲表1 (0x17)", 15, 108, 100, 34, Color.FromArgb(100, 116, 139));
        btnBlind1.Click += (s, e) => SendTimingData(0x17, "盲表1");
        grpData.Controls.Add(btnBlind1);

        btnBlind2 = MakeBtn("盲表2 (0x18)", 125, 108, 100, 34, Color.FromArgb(100, 116, 139));
        btnBlind2.Click += (s, e) => SendTimingData(0x18, "盲表2");
        grpData.Controls.Add(btnBlind2);

        btnBlind3 = MakeBtn("盲表3 (0x19)", 235, 108, 100, 34, Color.FromArgb(100, 116, 139));
        btnBlind3.Click += (s, e) => SendTimingData(0x19, "盲表3");
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

        // ═══ 时钟 ═══
        var grpClock = MakeGroup("计时时钟", 380, 540, 370, 110);
        Controls.Add(grpClock);

        lblClock = new Label {
            Text = "0:00.00",
            Font = new Font("Consolas", 32, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 197, 94),
            Location = new Point(10, 20), AutoSize = true
        };
        grpClock.Controls.Add(lblClock);

        lblClockState = new Label {
            Text = "● 待机",
            Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(260, 45), AutoSize = true
        };
        grpClock.Controls.Add(lblClockState);

        var btnClockReset = MakeBtn("清零", 260, 70, 80, 28, Color.FromArgb(71, 85, 105));
        btnClockReset.Font = new Font("Microsoft YaHei", 9);
        btnClockReset.Click += (s, e) => ResetClock();
        grpClock.Controls.Add(btnClockReset);

        _clockTimer = new System.Windows.Forms.Timer { Interval = 10 };
        _clockTimer.Tick += (s, e) => UpdateClockDisplay();

        // ═══ 协议说明 ═══
        var grpHelp = MakeGroup("协议说明", 10, 540, 370, 110);
        Controls.Add(grpHelp);

        var helpText = new Label {
            Text = "命令码(游泳计时通讯协议2023-11-13):\n" +
                   "  0x16=触板  0x17/18/19=盲表1/2/3  0x1A=出发台  0x1C=开始计时\n" +
                   "  0x1D=测试  0x20=计时清零  0x21=准备就绪  0x7F=滚动时间\n" +
                   "帧格式: F1 53 CMD CMD1 Lane Min Sec Cs (Hr<<4)|Ms1 00 00 F4  (12字节)\n" +
                   "示例: 1:23.456 → Min=1,Sec=23,Cs=45,D8=0x06(Hr=0,Ms1=6)",
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Consolas", 9.5f),
            Location = new Point(10, 20), Size = new Size(720, 82)
        };
        grpHelp.Controls.Add(helpText);
    }

    // ═══════ 时钟方法 ═══════
    void StartClock()
    {
        _clockStart = DateTime.Now;
        _clockOffset = 0;
        _clockRunning = true;
        _clockTimer.Start();
        if (InvokeRequired) BeginInvoke(new Action(() => {
            lblClockState.Text = "▶ 计时中";
            lblClockState.ForeColor = Color.FromArgb(34, 197, 94);
            lblClock.ForeColor = Color.FromArgb(34, 197, 94);
        }));
        else {
            lblClockState.Text = "▶ 计时中";
            lblClockState.ForeColor = Color.FromArgb(34, 197, 94);
            lblClock.ForeColor = Color.FromArgb(34, 197, 94);
        }
    }

    void ResetClock()
    {
        _clockRunning = false;
        _clockTimer.Stop();
        _clockOffset = 0;
        if (InvokeRequired) BeginInvoke(new Action(() => {
            lblClock.Text = "0:00.00";
            lblClock.ForeColor = Color.FromArgb(148, 163, 184);
            lblClockState.Text = "● 待机";
            lblClockState.ForeColor = Color.FromArgb(148, 163, 184);
        }));
        else {
            lblClock.Text = "0:00.00";
            lblClock.ForeColor = Color.FromArgb(148, 163, 184);
            lblClockState.Text = "● 待机";
            lblClockState.ForeColor = Color.FromArgb(148, 163, 184);
        }
    }

    void UpdateClockDisplay()
    {
        if (!_clockRunning) return;
        double elapsed = (DateTime.Now - _clockStart).TotalSeconds + _clockOffset;
        int totalCs = (int)(elapsed * 100);
        int min = totalCs / 6000;
        int sec = (totalCs % 6000) / 100;
        int cs = totalCs % 100;
        lblClock.Text = string.Format("{0}:{1:D2}.{2:D2}", min, sec, cs);
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
        int mode = cmbConnType.SelectedIndex; // 0=串口, 1=UDP, 2=TCP服务器
        // 串口控件
        cmbPort.Visible = (mode == 0);
        // UDP控件
        txtUdpHost.Visible = (mode == 1);
        txtUdpSendPort.Visible = (mode == 1);
        txtUdpRecvPort.Visible = (mode == 1);
        // TCP服务器控件
        txtTcpPort.Visible = (mode == 2);
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
            int mode = cmbConnType.SelectedIndex;
            if (mode == 0) {
                // 串口
                if (cmbPort.SelectedItem == null) { Log("[错误] 请选择串口"); return; }
                _serial = new SerialPort(cmbPort.SelectedItem.ToString(), 115200, Parity.None, 8, StopBits.One);
                _serial.Open();
                _running = true;
                _recvThread = new Thread(SerialRecvLoop) { IsBackground = true };
                _recvThread.Start();
                Log("[串口] 已连接: " + cmbPort.SelectedItem);
            } else if (mode == 1) {
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
                Log(string.Format("[UDP] 发送->{0}:{1}  接收<-{2}", txtUdpHost.Text, sendPort, recvPort));
            } else {
                // TCP服务器：模拟器作为服务器，主服务器用"连接TCP"连过来
                int port = 5555;
                int.TryParse(txtTcpPort.Text, out port);
                _tcpServer = new TcpListener(IPAddress.Any, port);
                _tcpServer.Start();
                _running = true;
                _recvThread = new Thread(TcpServerLoop) { IsBackground = true };
                _recvThread.Start();
                Log(string.Format("[TCP服务器] 监听端口 {0}，等待主服务器连接...", port));
                Log("提示: 主服务器 -> 设置 -> TCP地址填 127.0.0.1:" + port + " -> 点击连接TCP");
            }

            lblStatus.Text = mode == 2 ? "● 等待连接..." : "● 已连接";
            lblStatus.ForeColor = mode == 2 ? Color.FromArgb(245, 158, 11) : Color.FromArgb(34, 197, 94);
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
        try { if (_tcpStream != null) _tcpStream.Close(); } catch { }
        try { if (_tcpClient != null) _tcpClient.Close(); } catch { }
        try { if (_tcpServer != null) _tcpServer.Stop(); } catch { }
        _serial = null; _udpSend = null; _udpRecv = null;
        _tcpStream = null; _tcpClient = null; _tcpServer = null;
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

    void TcpServerLoop()
    {
        // 等待主服务器连接
        try {
            _tcpServer.Server.ReceiveTimeout = 500;
            while (_running && _tcpClient == null) {
                try {
                    if (_tcpServer.Pending()) {
                        _tcpClient = _tcpServer.AcceptTcpClient();
                        _tcpStream = _tcpClient.GetStream();
                        BeginInvoke(new Action(() => {
                            lblStatus.Text = "● 已连接";
                            lblStatus.ForeColor = Color.FromArgb(34, 197, 94);
                        }));
                        Log("[TCP服务器] 主服务器已连接: " + ((IPEndPoint)_tcpClient.Client.RemoteEndPoint).ToString());
                    } else {
                        Thread.Sleep(200);
                    }
                } catch { if (_running) Thread.Sleep(200); }
            }
        } catch (Exception ex) {
            Log("[TCP服务器] 监听错误: " + ex.Message);
            return;
        }

        // 接收帧（阻塞读，500ms 超时检查 _running）
        _tcpClient.ReceiveTimeout = 500;
        var acc = new List<byte>();
        byte[] buf = new byte[1024];
        while (_running && _tcpClient != null) {
            try {
                int c = _tcpStream.Read(buf, 0, buf.Length);
                if (c == 0) break;
                for (int i = 0; i < c; i++) acc.Add(buf[i]);
                ParseReceived(acc);
            } catch (System.IO.IOException) {
                // 超时 — 继续检查 _running
                if (_running) continue;
                break;
            } catch { if (_running) break; }
        }
        Log("[TCP服务器] 连接已断开");
        BeginInvoke(new Action(() => {
            lblStatus.Text = "● 等待连接...";
            lblStatus.ForeColor = Color.FromArgb(245, 158, 11);
        }));
    }

    void ParseReceived(List<byte> acc)
    {
        while (acc.Count >= FRAME_LEN) {
            int idx = acc.IndexOf(SOH);
            if (idx < 0) { acc.Clear(); return; }
            if (idx > 0) acc.RemoveRange(0, idx);
            if (acc.Count < FRAME_LEN) return;
            if (acc[FRAME_LEN - 1] != EOT || acc[1] != (byte)'S') { acc.RemoveAt(0); continue; }

            // 提取完整帧字节
            byte[] rawFrame = new byte[FRAME_LEN];
            for (int i = 0; i < FRAME_LEN; i++) rawFrame[i] = acc[i];

            byte cmd  = acc[2];
            byte rawD4 = acc[4]; // D4: 0-9=终点端泳道号, 10-19=另一端泳道号(实际泳道=D4-10)
            int min = acc[5], sec = acc[6], cs = acc[7];
            int hour = (acc[8] >> 4) & 0x0F;
            int ms1  = acc[8] & 0x0F;   // 1/1000秒个位
            double time = hour * 3600.0 + min * 60.0 + sec + cs / 100.0 + ms1 / 1000.0;
            // D4 拆分
            bool isFinishEnd = rawD4 < 10;
            int lane = isFinishEnd ? rawD4 : rawD4 - 10;
            string endLabel = isFinishEnd ? "终点端" : "另一端";

            string cmdName = GetCmdName(cmd);
            Log(string.Format("[接收] {0} D4={1} 道{2}({3}) {4} ({5})", cmdName, rawD4, lane, endLabel, FormatTime(time), FrameToHex(rawFrame)));

            // 驱动时钟
            switch (cmd) {
                case 0x21: // 准备就绪
                    BeginInvoke(new Action(() => {
                        lblClockState.Text = "■ 就位";
                        lblClockState.ForeColor = Color.FromArgb(245, 158, 11);
                    }));
                    break;
                case 0x1C: // 开始计时 → 启动时钟
                    BeginInvoke(new Action(() => StartClock()));
                    break;
                case 0x20: // 计时清零 → 清零
                    BeginInvoke(new Action(() => ResetClock()));
                    break;
            }

            acc.RemoveRange(0, FRAME_LEN);
        }
    }

    // ═══════ 帧 Hex 显示 ═══════
    string FrameToHex(byte[] frame)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < frame.Length; i++) {
            if (i > 0) sb.Append(' ');
            sb.Append(frame[i].ToString("X2"));
        }
        return sb.ToString();
    }

    // ═══════ 发送 ═══════
    byte[] BuildFrame(byte cmd, int lane, double timeInSeconds)
    {
        // D5=分 D6=秒 D7=1/100秒 D8=(小时<<4)|(1/1000秒个位)
        int totalMs  = (int)Math.Round(timeInSeconds * 1000);
        int hour     = totalMs / 3600000; totalMs %= 3600000;
        int min      = totalMs / 60000;   totalMs %= 60000;
        int sec      = totalMs / 1000;    totalMs %= 1000;
        int cs       = totalMs / 10;
        int ms1      = totalMs % 10;      // 1/1000秒个位

        byte[] frame = new byte[FRAME_LEN];
        frame[0] = SOH;
        frame[1] = (byte)'S';
        frame[2] = cmd;
        frame[3] = 0;
        frame[4] = (byte)lane;
        frame[5] = (byte)min;
        frame[6] = (byte)sec;
        frame[7] = (byte)cs;
        frame[8] = (byte)((hour << 4) | ms1);  // 高4位=小时，低4位=1/1000秒个位
        frame[FRAME_LEN - 1] = EOT;
        return frame;
    }

    // 发送帧并记录完整日志（供自动模拟使用）
    void SendFrameLog(byte[] frame, string prefix)
    {
        SendFrame(frame);
        Log(string.Format("{0} ({1})", prefix, FrameToHex(frame)));
    }

    void SendFrame(byte[] frame)
    {
        try {
            if (_serial != null && _serial.IsOpen) {
                _serial.Write(frame, 0, frame.Length);
            } else if (_udpSend != null) {
                _udpSend.Send(frame, frame.Length);
            } else if (_tcpStream != null) {
                _tcpStream.Write(frame, 0, frame.Length);
                _tcpStream.Flush();
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
        Log(string.Format("[发送] {0} ({1})", desc, FrameToHex(frame)));
    }

    void SendTimingData(byte cmd, string desc)
    {
        int lane = cmbLane.SelectedIndex;
        bool isOtherEnd = cmbEnd.SelectedIndex == 1; // 1=另一端
        int d4 = isOtherEnd ? lane + 10 : lane;       // D4 编码
        double time = ParseTime(txtTime.Text);
        if (time <= 0) { Log("[错误] 成绩格式无效"); return; }

        byte[] frame = BuildFrame(cmd, d4, time);
        string endLabel = isOtherEnd ? "另一端" : "终点端";
        SendFrame(frame);
        Log(string.Format("[发送] 道{0}({1}) {2} {3} ({4})", lane, endLabel, desc, FormatTime(time), FrameToHex(frame)));
    }

    // ═══════ 自动模拟比赛 ═══════
    // D4 编码规则（游泳计时通讯协议）：
    //   D4 = 泳道号 (0-9)    → 终点端设备（触板/出发台/盲表在终点端）
    //   D4 = 泳道号 + 10     → 另一端设备
    // 100米比赛：出发在终点端 → 出发台 D4=0-9(终点端), 50m转身触板 D4=10-19(另一端), 100m终点触板 D4=0-9(终点端)
    void AutoRaceThread()
    {
        var rng = new Random();
        int lanes = (int)Invoke(new Func<int>(() => (int)nudLanes.Value));
        bool withBlind = (bool)Invoke(new Func<bool>(() => chkAutoBlind.Checked));

        Log("═══ 自动模拟开始（100米比赛，出发=终点端）═══");
        // 终点端(D4=lane), 另一端(D4=lane+10)

        // 1. 就位
        if (!_autoRunning) return;
        SendFrameLog(BuildFrame(0x21, 0, 0), "[自动] 准备就绪");
        Thread.Sleep(2000);

        // 2. 发令
        if (!_autoRunning) return;
        SendFrameLog(BuildFrame(0x1C, 0, 0), "[自动] 开始计时");
        Thread.Sleep(500);

        // 3. 出发台反应时 — 出发台在终点端 → D4=lane（含第0道）
        for (int i = 0; i <= lanes && _autoRunning; i++) {
            double rt = 0.15 + rng.NextDouble() * 0.45;
            var sbFrame = BuildFrame(0x1A, i, rt);  // D4=i (终点端)
            SendFrameLog(sbFrame, string.Format("[自动] 道{0} 出发台(终点端 D4={0}) 反应时={1:F2}s", i, rt));
            Thread.Sleep(50);
        }

        // 4. 等待泳道关闭倒计时
        Log("[自动] 等待泳道关闭倒计时...");
        Thread.Sleep(8000);

        // 5. 50米分段 — 转身端=另一端 → D4=lane+10（含第0道）
        if (!_autoRunning) return;
        Log("[自动] ─── 50米分段（另一端 D4=道号+10） ───");
        double[] splitTimes = new double[lanes + 1];
        var order = new List<int>();
        for (int i = 0; i <= lanes; i++) order.Add(i);
        Shuffle(order, rng);

        double baseTime = 24.0 + rng.NextDouble() * 3.0;
        foreach (int i in order) {
            if (!_autoRunning) return;
            double t = baseTime + rng.NextDouble() * 4.0;
            splitTimes[i] = t;
            int d4Turn = i + 10;  // 另一端

            SendFrameLog(BuildFrame(0x16, d4Turn, t), string.Format("[自动] 道{0} 触板(另一端 D4={1}) 50m={2}", i, d4Turn, FormatTime(t)));

            if (withBlind) {
                Thread.Sleep(30);
                SendFrameLog(BuildFrame(0x17, d4Turn, t + (rng.NextDouble() * 0.06 - 0.03)), string.Format("[自动] 道{0} 盲表1(另一端)", i));
                Thread.Sleep(20);
                SendFrameLog(BuildFrame(0x18, d4Turn, t + (rng.NextDouble() * 0.06 - 0.03)), string.Format("[自动] 道{0} 盲表2(另一端)", i));
                Thread.Sleep(20);
                SendFrameLog(BuildFrame(0x19, d4Turn, t + (rng.NextDouble() * 0.06 - 0.03)), string.Format("[自动] 道{0} 盲表3(另一端)", i));
            }

            Thread.Sleep(200 + rng.Next(500));
        }

        // 6. 等待泳道再次关闭
        Log("[自动] 等待下一段泳道关闭...");
        Thread.Sleep(6000);

        // 7. 100米终点 — 终点端 → D4=lane
        if (!_autoRunning) return;
        Log("[自动] ─── 100米终点（终点端 D4=道号） ───");
        Shuffle(order, rng);

        foreach (int i in order) {
            if (!_autoRunning) return;
            double t = splitTimes[i] + 26.0 + rng.NextDouble() * 5.0;
            int d4Finish = i;  // 终点端

            SendFrameLog(BuildFrame(0x16, d4Finish, t), string.Format("[自动] 道{0} 触板(终点端 D4={1}) 100m={2} (终点)", i, d4Finish, FormatTime(t)));

            if (withBlind) {
                Thread.Sleep(30);
                SendFrameLog(BuildFrame(0x17, d4Finish, t + (rng.NextDouble() * 0.06 - 0.03)), string.Format("[自动] 道{0} 盲表1(终点端)", i));
                Thread.Sleep(20);
                SendFrameLog(BuildFrame(0x18, d4Finish, t + (rng.NextDouble() * 0.06 - 0.03)), string.Format("[自动] 道{0} 盲表2(终点端)", i));
                Thread.Sleep(20);
                SendFrameLog(BuildFrame(0x19, d4Finish, t + (rng.NextDouble() * 0.06 - 0.03)), string.Format("[自动] 道{0} 盲表3(终点端)", i));
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
            case 0x16: return "触板成绩";
            case 0x17: return "盲表1";
            case 0x18: return "盲表2";
            case 0x19: return "盲表3";
            case 0x1A: return "出发台";
            case 0x1C: return "开始计时";
            case 0x1D: return "测试设备";
            case 0x20: return "计时清零";
            case 0x21: return "准备就绪";
            case 0x40: return "设置泳池参数";
            case 0x41: return "设置比赛距离";
            case 0x42: return "设置命令";
            case 0x7F: return "滚动时间";
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
