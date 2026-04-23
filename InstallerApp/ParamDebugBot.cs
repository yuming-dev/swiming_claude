using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// 硬件参数调试机器人（Timing Parameter Debug Bot）
//
// 按游泳计时通讯协议 2023-11-13 与主服务器进行"参数"类帧的双向收发，用于
// 调试主服务器 ↔ 硬件计时控制器之间的"参数设置 / 设备状态" 同步。
//
// 参数帧约定（与主服务器代码实现一致）：
//   CMD=0x40 (PoolConfig)        D3=泳道数(8-10)  D4=泳池长度(25/50 米)
//   CMD=0x41 (RaceConfig)        D4=比赛距离(趟)  D6=0-4 泳道空道位图  D7=5-9 泳道空道位图
//   CMD=0x42 (SetCommand)        D3=子码, D4 及可选 D5/D6 为值：
//     0x01 LaneCloseTime         D4=秒
//     0x02 StartBlockCloseDelay  D4=0.1 秒单位（1~255 → 0.1~25.5）
//     0x03 ResultConfirmCloseDelay D4=0.1 秒单位
//     0x04 FalseStartThreshold   D4=0.01 秒单位（0~100 → 0~1.00）
//     0x05 SplitDisplayTime      D4=秒
//     0x06 FirstPlaceHoldTime    D4=秒
//     0x07 FinishPosition        D4=0 左 / 1 右
//     0x10 DeviceStatus          D4=泳道(0-9)
//                                D5=左端损坏位图  D6=右端损坏位图
//                                bit0 触板  bit1 盲表1  bit2 盲表2  bit3 盲表3  bit4 出发台  (1=损坏)

class ParamDebugBotForm : Form
{
    const byte SOH = 0xF1;
    const byte EOT = 0xF4;
    const int FRAME_LEN = 12;
    const int LANE_COUNT_UI = 10;

    // 连接
    SerialPort _serial;
    UdpClient _udpSend, _udpRecv;
    TcpListener _tcpServer;
    TcpClient _tcpClient;
    NetworkStream _tcpStream;
    Thread _recvThread;
    volatile bool _running;

    // 连接控件
    ComboBox cmbConnType, cmbPort;
    TextBox txtUdpHost, txtUdpSendPort, txtUdpRecvPort, txtTcpPort;
    Button btnConnect, btnDisconnect;
    Label lblStatus;
    ListBox lstLog;

    // 参数控件
    NumericUpDown nudLanes, nudPoolLen;
    NumericUpDown nudLaneClose, nudSbDelay, nudRcDelay, nudFsThr, nudSplitDisp, nudFirstHold;
    ComboBox cmbFinishPos;
    Button btnSendPool, btnSendAll, btnSendOne;
    ComboBox cmbOneSub;
    NumericUpDown nudOneVal;

    // 设备状态复选框 [lane][side][device]   side=0 left, 1 right   device: 0 Touch 1 B1 2 B2 3 B3 4 Start
    CheckBox[,,] chkDev;
    Button btnSendDevAll, btnSendDevOne, btnDevClearAll, btnDevSetAll;
    NumericUpDown nudDevLane;

    // 显示接收到的参数帧
    Label lblLastFrame;

    bool _applyingFromHw = false;

    public ParamDebugBotForm()
    {
        Text = "硬件参数调试机器人 (Param Debug Bot)";
        Size = new Size(1050, 720);
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
        // ───── 连接区 ─────
        var grpConn = MakeGroup("连接设置（作为硬件端连接主服务器）", 10, 10, 1020, 100);
        Controls.Add(grpConn);

        grpConn.Controls.Add(MakeLabel("方式:", 15, 28));
        cmbConnType = new ComboBox { Location = new Point(60, 25), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbConnType.Items.AddRange(new object[] { "串口", "UDP", "TCP 服务器" });
        cmbConnType.SelectedIndex = 2;
        cmbConnType.SelectedIndexChanged += delegate { ToggleConnUI(); };
        grpConn.Controls.Add(cmbConnType);

        grpConn.Controls.Add(MakeLabel("串口:", 185, 28));
        cmbPort = new ComboBox { Location = new Point(230, 25), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        grpConn.Controls.Add(cmbPort);
        var btnRefresh = MakeBtn("刷新", 325, 24, 50, 26, Color.FromArgb(71, 85, 105));
        btnRefresh.Click += delegate { PopulateComPorts(); };
        grpConn.Controls.Add(btnRefresh);

        txtUdpHost = new TextBox { Text = "127.0.0.1", Location = new Point(230, 25), Width = 110, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpHost);
        txtUdpSendPort = new TextBox { Text = "5001", Location = new Point(345, 25), Width = 60, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpSendPort);
        txtUdpRecvPort = new TextBox { Text = "5002", Location = new Point(425, 25), Width = 60, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtUdpRecvPort);

        txtTcpPort = new TextBox { Text = "5555", Location = new Point(230, 25), Width = 70, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        grpConn.Controls.Add(txtTcpPort);

        btnConnect = MakeBtn("连接 / 启动监听", 510, 22, 150, 32, Color.FromArgb(34, 197, 94));
        btnConnect.Click += delegate { DoConnect(); };
        grpConn.Controls.Add(btnConnect);
        btnDisconnect = MakeBtn("断开", 670, 22, 90, 32, Color.FromArgb(239, 68, 68));
        btnDisconnect.Enabled = false;
        btnDisconnect.Click += delegate { DoDisconnect(); };
        grpConn.Controls.Add(btnDisconnect);
        lblStatus = new Label { Text = "● 未连接", ForeColor = Color.FromArgb(239, 68, 68), Location = new Point(770, 28), AutoSize = true, Font = new Font("Microsoft YaHei", 10, FontStyle.Bold) };
        grpConn.Controls.Add(lblStatus);

        grpConn.Controls.Add(MakeLabel(
            "帧: F1 53 CMD D3 D4 D5 D6 D7 D8 00 00 F4  (12 字节) — 本工具收发 0x40/0x41/0x42 参数帧",
            15, 65, Color.FromArgb(148, 163, 184), 9));

        ToggleConnUI();

        // ───── 参数区 ─────
        var grpParam = MakeGroup("参数设置（编辑后 → 发送到主服务器；接收到时会自动填回）", 10, 118, 620, 340);
        Controls.Add(grpParam);

        int y = 25;
        grpParam.Controls.Add(MakeLabel("泳道数 (8-10):", 15, y + 2));
        nudLanes = MakeNum(130, y, 8, 10, 10);
        grpParam.Controls.Add(nudLanes);
        grpParam.Controls.Add(MakeLabel("泳池长度 (25/50 米):", 195, y + 2));
        nudPoolLen = MakeNum(335, y, 25, 50, 50);
        grpParam.Controls.Add(nudPoolLen);
        btnSendPool = MakeBtn("发送 0x40", 410, y - 1, 100, 26, Color.FromArgb(124, 58, 237));
        btnSendPool.Click += delegate { SendPoolConfig(); };
        grpParam.Controls.Add(btnSendPool);

        y += 40;
        grpParam.Controls.Add(new Label { Text = "—— 0x42 子码参数 ——", ForeColor = Color.FromArgb(148, 163, 184), Location = new Point(15, y), AutoSize = true, Font = new Font("Microsoft YaHei", 9) });
        y += 26;
        grpParam.Controls.Add(MakeLabel("泳道关闭 (秒):", 15, y + 2));
        nudLaneClose = MakeNum(130, y, 0, 255, 20);
        grpParam.Controls.Add(nudLaneClose);
        grpParam.Controls.Add(MakeLabel("出发台延迟 (0.1 秒单位):", 195, y + 2));
        nudSbDelay = MakeNum(355, y, 0, 255, 30);
        grpParam.Controls.Add(nudSbDelay);

        y += 30;
        grpParam.Controls.Add(MakeLabel("成绩确认延迟:", 15, y + 2));
        nudRcDelay = MakeNum(130, y, 0, 255, 30);
        grpParam.Controls.Add(nudRcDelay);
        grpParam.Controls.Add(MakeLabel("抢跳阈值 (0.01 秒单位):", 195, y + 2));
        nudFsThr = MakeNum(355, y, 0, 100, 10);
        grpParam.Controls.Add(nudFsThr);

        y += 30;
        grpParam.Controls.Add(MakeLabel("分段显示 (秒):", 15, y + 2));
        nudSplitDisp = MakeNum(130, y, 0, 255, 5);
        grpParam.Controls.Add(nudSplitDisp);
        grpParam.Controls.Add(MakeLabel("第1名停留 (秒):", 195, y + 2));
        nudFirstHold = MakeNum(355, y, 0, 255, 3);
        grpParam.Controls.Add(nudFirstHold);

        y += 30;
        grpParam.Controls.Add(MakeLabel("终点位置:", 15, y + 2));
        cmbFinishPos = new ComboBox { Location = new Point(130, y), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbFinishPos.Items.AddRange(new object[] { "左端 (0)", "右端 (1)" });
        cmbFinishPos.SelectedIndex = 0;
        grpParam.Controls.Add(cmbFinishPos);

        y += 36;
        btnSendAll = MakeBtn("一键发送所有参数 (0x40 + 0x42×7)", 15, y, 280, 30, Color.FromArgb(34, 197, 94));
        btnSendAll.Click += delegate { SendAllParams(); };
        grpParam.Controls.Add(btnSendAll);

        // 发送单个 0x42 子码
        y += 44;
        grpParam.Controls.Add(MakeLabel("单项发送 0x42:", 15, y + 4, Color.FromArgb(148, 163, 184), 9));
        cmbOneSub = new ComboBox { Location = new Point(130, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        cmbOneSub.Items.AddRange(new object[] {
            "0x01 LaneCloseTime",
            "0x02 StartBlockCloseDelay",
            "0x03 ResultConfirmCloseDelay",
            "0x04 FalseStartThreshold",
            "0x05 SplitDisplayTime",
            "0x06 FirstPlaceHoldTime",
            "0x07 FinishPosition"
        });
        cmbOneSub.SelectedIndex = 0;
        grpParam.Controls.Add(cmbOneSub);
        grpParam.Controls.Add(MakeLabel("值(D4):", 340, y + 2));
        nudOneVal = MakeNum(395, y, 0, 255, 20);
        grpParam.Controls.Add(nudOneVal);
        btnSendOne = MakeBtn("发送", 470, y - 1, 60, 26, Color.FromArgb(59, 130, 246));
        btnSendOne.Click += delegate { SendOneSet(); };
        grpParam.Controls.Add(btnSendOne);

        lblLastFrame = new Label {
            Text = "最近接收: (无)", Location = new Point(15, y + 40), Size = new Size(590, 22),
            Font = new Font("Consolas", 9.5f), ForeColor = Color.FromArgb(148, 163, 184)
        };
        grpParam.Controls.Add(lblLastFrame);

        // ───── 设备状态区 ─────
        var grpDev = MakeGroup("设备状态管理（0x42 D3=0x10）", 640, 118, 390, 340);
        Controls.Add(grpDev);

        chkDev = new CheckBox[LANE_COUNT_UI, 2, 5];
        string[] devNames = new[] { "触", "盲1", "盲2", "盲3", "出" };
        int rowY = 22;
        grpDev.Controls.Add(new Label { Text = "道", Location = new Point(10, rowY), AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Microsoft YaHei", 9) });
        int leftStart = 32;
        for (int d = 0; d < 5; d++) {
            grpDev.Controls.Add(new Label { Text = "L" + devNames[d], Location = new Point(leftStart + d * 32, rowY), AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Microsoft YaHei", 9) });
        }
        int rightStart = leftStart + 5 * 32 + 10;
        for (int d = 0; d < 5; d++) {
            grpDev.Controls.Add(new Label { Text = "R" + devNames[d], Location = new Point(rightStart + d * 32, rowY), AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Microsoft YaHei", 9) });
        }

        for (int lane = 0; lane < LANE_COUNT_UI; lane++) {
            int ry = 44 + lane * 22;
            grpDev.Controls.Add(new Label { Text = lane.ToString(), Location = new Point(12, ry + 2), AutoSize = true, ForeColor = Color.White, Font = new Font("Microsoft YaHei", 9) });
            for (int d = 0; d < 5; d++) {
                chkDev[lane, 0, d] = new CheckBox { Location = new Point(leftStart + d * 32 - 2, ry), Width = 28 };
                grpDev.Controls.Add(chkDev[lane, 0, d]);
                chkDev[lane, 1, d] = new CheckBox { Location = new Point(rightStart + d * 32 - 2, ry), Width = 28 };
                grpDev.Controls.Add(chkDev[lane, 1, d]);
            }
        }

        int devY = 44 + LANE_COUNT_UI * 22 + 6;
        btnDevClearAll = MakeBtn("全部正常", 15, devY, 90, 26, Color.FromArgb(71, 85, 105));
        btnDevClearAll.Click += delegate { SetAllBroken(false); };
        grpDev.Controls.Add(btnDevClearAll);
        btnDevSetAll = MakeBtn("全部损坏", 115, devY, 90, 26, Color.FromArgb(239, 68, 68));
        btnDevSetAll.Click += delegate { SetAllBroken(true); };
        grpDev.Controls.Add(btnDevSetAll);

        btnSendDevAll = MakeBtn("发送全部 10 道", 210, devY, 165, 26, Color.FromArgb(34, 197, 94));
        btnSendDevAll.Click += delegate { SendAllDeviceStatuses(); };
        grpDev.Controls.Add(btnSendDevAll);

        devY += 32;
        grpDev.Controls.Add(MakeLabel("单发泳道:", 15, devY + 4, Color.FromArgb(148, 163, 184), 9));
        nudDevLane = MakeNum(100, devY, 0, LANE_COUNT_UI - 1, 0);
        grpDev.Controls.Add(nudDevLane);
        btnSendDevOne = MakeBtn("发送此道", 170, devY, 110, 26, Color.FromArgb(59, 130, 246));
        btnSendDevOne.Click += delegate { SendOneDeviceStatus((int)nudDevLane.Value); };
        grpDev.Controls.Add(btnSendDevOne);

        // ───── 日志区 ─────
        var grpLog = MakeGroup("通信日志（收 / 发 帧）", 10, 466, 1020, 210);
        Controls.Add(grpLog);

        lstLog = new ListBox {
            Location = new Point(10, 22), Size = new Size(1000, 155),
            BackColor = Color.FromArgb(15, 23, 42), ForeColor = Color.FromArgb(148, 163, 184),
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9),
            HorizontalScrollbar = true
        };
        grpLog.Controls.Add(lstLog);

        var btnClear = MakeBtn("清除日志", 10, 178, 80, 24, Color.FromArgb(71, 85, 105));
        btnClear.Font = new Font("Microsoft YaHei", 9);
        btnClear.Click += delegate { lstLog.Items.Clear(); };
        grpLog.Controls.Add(btnClear);
    }

    NumericUpDown MakeNum(int x, int y, int min, int max, int val)
    {
        return new NumericUpDown {
            Location = new Point(x, y), Width = 60, Minimum = min, Maximum = max, Value = val,
            BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White
        };
    }

    // ═══════ 连接 ═══════
    void DoConnect()
    {
        DoDisconnect();
        try {
            int mode = cmbConnType.SelectedIndex;
            if (mode == 0) {
                if (cmbPort.SelectedItem == null) { Log("[错误] 未选择串口"); return; }
                _serial = new SerialPort(cmbPort.SelectedItem.ToString(), 115200, Parity.None, 8, StopBits.One);
                _serial.Open();
                _running = true;
                _recvThread = new Thread(SerialRecv) { IsBackground = true };
                _recvThread.Start();
                Log("[串口] 已打开 " + cmbPort.SelectedItem);
            } else if (mode == 1) {
                int sp, rp;
                int.TryParse(txtUdpSendPort.Text, out sp);
                int.TryParse(txtUdpRecvPort.Text, out rp);
                _udpSend = new UdpClient();
                _udpSend.Connect(txtUdpHost.Text, sp);
                _udpRecv = new UdpClient(rp);
                _running = true;
                _recvThread = new Thread(UdpRecv) { IsBackground = true };
                _recvThread.Start();
                Log(string.Format("[UDP] 发 → {0}:{1}  收 ← {2}", txtUdpHost.Text, sp, rp));
            } else {
                int port = 5555;
                int.TryParse(txtTcpPort.Text, out port);
                _tcpServer = new TcpListener(IPAddress.Any, port);
                _tcpServer.Start();
                _running = true;
                _recvThread = new Thread(TcpServerLoop) { IsBackground = true };
                _recvThread.Start();
                Log(string.Format("[TCP 服务器] 监听 0.0.0.0:{0}，等待主服务器连入...", port));
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

    void ToggleConnUI()
    {
        int m = cmbConnType.SelectedIndex;
        cmbPort.Visible = (m == 0);
        txtUdpHost.Visible = txtUdpSendPort.Visible = txtUdpRecvPort.Visible = (m == 1);
        txtTcpPort.Visible = (m == 2);
    }

    // ═══════ 接收 ═══════
    void SerialRecv()
    {
        byte[] buf = new byte[1024];
        var acc = new List<byte>();
        while (_running && _serial != null && _serial.IsOpen) {
            try {
                int n = _serial.BytesToRead;
                if (n > 0) {
                    int c = _serial.Read(buf, 0, Math.Min(n, buf.Length));
                    for (int i = 0; i < c; i++) acc.Add(buf[i]);
                    Parse(acc);
                } else Thread.Sleep(5);
            } catch { if (_running) break; }
        }
    }

    void UdpRecv()
    {
        var acc = new List<byte>();
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (_running && _udpRecv != null) {
            try {
                byte[] d = _udpRecv.Receive(ref ep);
                for (int i = 0; i < d.Length; i++) acc.Add(d[i]);
                Parse(acc);
            } catch { if (_running) break; }
        }
    }

    void TcpServerLoop()
    {
        try {
            while (_running && _tcpClient == null) {
                if (_tcpServer.Pending()) {
                    _tcpClient = _tcpServer.AcceptTcpClient();
                    _tcpStream = _tcpClient.GetStream();
                    BeginInvoke(new Action(delegate {
                        lblStatus.Text = "● 已连接";
                        lblStatus.ForeColor = Color.FromArgb(34, 197, 94);
                    }));
                    Log("[TCP] 主服务器已接入: " + ((IPEndPoint)_tcpClient.Client.RemoteEndPoint).ToString());
                } else Thread.Sleep(200);
            }
        } catch (Exception ex) { Log("[TCP] 监听错误: " + ex.Message); return; }

        _tcpClient.ReceiveTimeout = 500;
        byte[] buf = new byte[1024];
        var acc = new List<byte>();
        while (_running && _tcpClient != null) {
            try {
                int c = _tcpStream.Read(buf, 0, buf.Length);
                if (c == 0) break;
                for (int i = 0; i < c; i++) acc.Add(buf[i]);
                Parse(acc);
            } catch (System.IO.IOException) { if (_running) continue; break; }
            catch { if (_running) break; }
        }
        Log("[TCP] 连接已断开");
        BeginInvoke(new Action(delegate {
            lblStatus.Text = "● 等待连接...";
            lblStatus.ForeColor = Color.FromArgb(245, 158, 11);
        }));
    }

    void Parse(List<byte> acc)
    {
        while (acc.Count >= FRAME_LEN) {
            int i = acc.IndexOf(SOH);
            if (i < 0) { acc.Clear(); return; }
            if (i > 0) acc.RemoveRange(0, i);
            if (acc.Count < FRAME_LEN) return;
            if (acc[FRAME_LEN - 1] != EOT || acc[1] != (byte)'S') { acc.RemoveAt(0); continue; }

            byte[] f = new byte[FRAME_LEN];
            for (int k = 0; k < FRAME_LEN; k++) f[k] = acc[k];
            acc.RemoveRange(0, FRAME_LEN);

            byte cmd = f[2], d3 = f[3], d4 = f[4], d5 = f[5], d6 = f[6], d7 = f[7], d8 = f[8];
            string hex = ToHex(f);

            if (cmd == 0x40 || cmd == 0x41 || cmd == 0x42) {
                BeginInvoke(new Action(delegate {
                    HandleIncomingParam(cmd, d3, d4, d5, d6, d7, d8, hex);
                }));
            } else {
                // 其它帧（运动员计时等）仅日志显示
                BeginInvoke(new Action(delegate {
                    Log(string.Format("[收 非参数帧] CMD=0x{0:X2} {1}", cmd, hex));
                }));
            }
        }
    }

    // ═══════ 接收到的参数帧：填回 UI + 日志 ═══════
    void HandleIncomingParam(byte cmd, byte d3, byte d4, byte d5, byte d6, byte d7, byte d8, string hex)
    {
        _applyingFromHw = true;
        try {
            if (cmd == 0x40) {
                // D3=泳道数 D4=泳池长度
                if (d3 >= (byte)nudLanes.Minimum && d3 <= (byte)nudLanes.Maximum) nudLanes.Value = d3;
                if (d4 == 25 || d4 == 50) nudPoolLen.Value = d4;
                string msg = string.Format("[收 0x40 泳池参数] {0} 道 {1} 米", d3, d4);
                Log(msg + "  " + hex);
                lblLastFrame.Text = "最近接收: " + msg;
                return;
            }
            if (cmd == 0x41) {
                string msg = string.Format("[收 0x41 比赛参数] 趟数={0} 空道位图 D6=0x{1:X2} D7=0x{2:X2}", d4, d6, d7);
                Log(msg + "  " + hex);
                lblLastFrame.Text = "最近接收: " + msg;
                return;
            }
            if (cmd == 0x42) {
                // 设备状态
                if (d3 == 0x10) {
                    if (d4 < LANE_COUNT_UI) {
                        WriteMaskToUI(d4, 0, d5);
                        WriteMaskToUI(d4, 1, d6);
                    }
                    string msg = string.Format("[收 0x42/0x10 设备状态] 道{0} 左=0x{1:X2} 右=0x{2:X2}", d4, d5, d6);
                    Log(msg + "  " + hex);
                    lblLastFrame.Text = "最近接收: " + msg;
                    return;
                }
                // 单参数
                switch (d3) {
                    case 0x01: ClampAndSet(nudLaneClose, d4); break;
                    case 0x02: ClampAndSet(nudSbDelay, d4); break;
                    case 0x03: ClampAndSet(nudRcDelay, d4); break;
                    case 0x04: ClampAndSet(nudFsThr, d4); break;
                    case 0x05: ClampAndSet(nudSplitDisp, d4); break;
                    case 0x06: ClampAndSet(nudFirstHold, d4); break;
                    case 0x07:
                        cmbFinishPos.SelectedIndex = d4 == 0 ? 0 : 1;
                        break;
                    default:
                        Log(string.Format("[收 0x42 未知子码] D3=0x{0:X2} D4=0x{1:X2}  {2}", d3, d4, hex));
                        lblLastFrame.Text = string.Format("最近接收: 0x42 D3=0x{0:X2} D4={1}", d3, d4);
                        return;
                }
                string msg2 = string.Format("[收 0x42 D3=0x{0:X2}] D4={1}", d3, d4);
                Log(msg2 + "  " + hex);
                lblLastFrame.Text = "最近接收: " + msg2;
            }
        } finally {
            _applyingFromHw = false;
        }
    }

    static void ClampAndSet(NumericUpDown n, byte v)
    {
        int val = v;
        if (val < (int)n.Minimum) val = (int)n.Minimum;
        if (val > (int)n.Maximum) val = (int)n.Maximum;
        n.Value = val;
    }

    void WriteMaskToUI(int lane, int side, byte mask)
    {
        for (int d = 0; d < 5; d++) {
            bool broken = ((mask >> d) & 0x01) != 0;
            if (chkDev[lane, side, d].Checked != broken) chkDev[lane, side, d].Checked = broken;
        }
    }

    byte ReadMaskFromUI(int lane, int side)
    {
        byte m = 0;
        for (int d = 0; d < 5; d++) if (chkDev[lane, side, d].Checked) m |= (byte)(1 << d);
        return m;
    }

    void SetAllBroken(bool broken)
    {
        for (int lane = 0; lane < LANE_COUNT_UI; lane++)
            for (int side = 0; side < 2; side++)
                for (int d = 0; d < 5; d++)
                    chkDev[lane, side, d].Checked = broken;
    }

    // ═══════ 发送 ═══════
    void SendPoolConfig()
    {
        byte d3 = (byte)nudLanes.Value;
        byte d4 = (byte)nudPoolLen.Value;
        Send(0x40, d3, d4, 0, 0, 0, 0, string.Format("[发 0x40 泳池参数] {0} 道 {1} 米", d3, d4));
    }

    void SendOneSet()
    {
        byte[] subMap = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        byte sub = subMap[cmbOneSub.SelectedIndex];
        byte v = (byte)nudOneVal.Value;
        Send(0x42, sub, v, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x{0:X2}] D4={1}", sub, v));
    }

    void SendAllParams()
    {
        SendPoolConfig();
        Send(0x42, 0x01, (byte)nudLaneClose.Value, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x01] LaneCloseTime={0}", (int)nudLaneClose.Value));
        Send(0x42, 0x02, (byte)nudSbDelay.Value, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x02] StartBlockCloseDelay={0}", (int)nudSbDelay.Value));
        Send(0x42, 0x03, (byte)nudRcDelay.Value, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x03] ResultConfirmCloseDelay={0}", (int)nudRcDelay.Value));
        Send(0x42, 0x04, (byte)nudFsThr.Value, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x04] FalseStartThreshold={0}", (int)nudFsThr.Value));
        Send(0x42, 0x05, (byte)nudSplitDisp.Value, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x05] SplitDisplayTime={0}", (int)nudSplitDisp.Value));
        Send(0x42, 0x06, (byte)nudFirstHold.Value, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x06] FirstPlaceHoldTime={0}", (int)nudFirstHold.Value));
        byte fp = (byte)cmbFinishPos.SelectedIndex;
        Send(0x42, 0x07, fp, 0, 0, 0, 0, string.Format("[发 0x42 D3=0x07] FinishPosition={0}", fp == 0 ? "左端" : "右端"));
    }

    void SendOneDeviceStatus(int lane)
    {
        if (lane < 0 || lane >= LANE_COUNT_UI) return;
        byte left = ReadMaskFromUI(lane, 0);
        byte right = ReadMaskFromUI(lane, 1);
        Send(0x42, 0x10, (byte)lane, left, right, 0, 0,
            string.Format("[发 0x42 D3=0x10 设备状态] 道{0} 左=0x{1:X2} 右=0x{2:X2}", lane, left, right));
    }

    void SendAllDeviceStatuses()
    {
        for (int lane = 0; lane < LANE_COUNT_UI; lane++) SendOneDeviceStatus(lane);
    }

    void Send(byte cmd, byte d3, byte d4, byte d5, byte d6, byte d7, byte d8, string desc)
    {
        if (_applyingFromHw) return;
        byte[] f = new byte[FRAME_LEN];
        f[0] = SOH; f[1] = (byte)'S'; f[2] = cmd; f[3] = d3; f[4] = d4;
        f[5] = d5; f[6] = d6; f[7] = d7; f[8] = d8;
        f[FRAME_LEN - 1] = EOT;
        try {
            if (_serial != null && _serial.IsOpen) _serial.Write(f, 0, f.Length);
            else if (_udpSend != null) _udpSend.Send(f, f.Length);
            else if (_tcpStream != null) { _tcpStream.Write(f, 0, f.Length); _tcpStream.Flush(); }
            else { Log("[错误] 未连接"); return; }
            Log(desc + "  " + ToHex(f));
        } catch (Exception ex) {
            Log("[错误] 发送失败: " + ex.Message);
        }
    }

    // ═══════ 小工具 ═══════
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
        var b = new Button {
            Text = text, Location = new Point(x, y), Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Microsoft YaHei", 10, FontStyle.Bold), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    string ToHex(byte[] f)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < f.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(f[i].ToString("X2")); }
        return sb.ToString();
    }

    void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(new Action(delegate { Log(msg); })); return; }
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg;
        lstLog.Items.Add(line);
        if (lstLog.Items.Count > 800) lstLog.Items.RemoveAt(0);
        lstLog.TopIndex = lstLog.Items.Count - 1;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        DoDisconnect();
        base.OnFormClosing(e);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ParamDebugBotForm());
    }
}
