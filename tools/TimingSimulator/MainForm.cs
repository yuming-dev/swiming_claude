using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace TimingSimulator
{
    // ══════════════════════════════════════════════════════════════════════
    // 模拟游泳计时器                                            2026-08-26
    //
    // 真硬件的替身: 画出 10 道泳池, 每道左右两端各有 触板/出发台/盲表×3,
    // 按下 = 读当前计时时刻, 按通讯协议发一帧给比赛控制计算机。
    //
    // 【按实际位置左右对称】触板在池壁最内侧(贴水面), 往外依次出发台、
    //   盲表1/2/3。所以左端在屏幕上是【反过来】排的:
    //     盲表3 盲表2 盲表1 出发台 触板 ▏≈≈ 水面 ≈≈▕ 触板 出发台 盲表1 盲表2 盲表3
    //   成绩栏同理贴内侧, 跟自己那一端对齐。
    //
    // 它是 TCP【服务端】—— 主程序按 timing_connection.json 的 tcp/host:5000
    // 主动连过来, 跟真硬件接法一致。
    //
    // 【必须先起模拟器再起主程序】主程序启动时连一次计时端口, 连不上就不再
    //   重试, 顺序反了一辈子连不上。
    //
    // 协议(与 TimingBridge.cs 一致, 12 字节一帧):
    //   D0=0xF1(SOH) D1=0x53('S') D2=命令 D3=命令1 D4=泳道
    //   D5=分 D6=秒 D7=1/100秒 D8=(时<<4)|(1/1000秒) D9=备用
    //   D10=标志(0x1A: 0正常 1抢跳 2接力超时) D11=0xF4(EOT)
    //   D4 <10 = 物理【左端】道次;  D4 >=10 = 物理【右端】(实际道次 = D4-10)
    // ══════════════════════════════════════════════════════════════════════
    public class MainForm : Form
    {
        // ── 协议 ──
        const byte SOH = 0xF1, EOT = 0xF4, STX_S = 0x53;   // D1 固定是 ASCII 的 S
        const int FRAME = 12;
        const byte CMD_TOUCHPAD = 0x16, CMD_MB1 = 0x17, CMD_MB2 = 0x18, CMD_MB3 = 0x19;
        const byte CMD_STARTBLOCK = 0x1A, CMD_START = 0x1C, CMD_RESET = 0x20, CMD_RUNNING = 0x7F;
        const int LANES = 10, PORT = 5000;

        // ── 配色 ─────────────────────────────────────────────────────
        // 原来一片深灰蓝, 太闷。改成泳池的样子: 深蓝池台 + 亮青水面 + 黄色分道线,
        // 三类按钮各用一个高饱和色, 一眼分得清 触板/出发台/盲表。
        static readonly Color CDeck     = Color.FromArgb(16, 30, 50);
        static readonly Color CBar      = Color.FromArgb(10, 20, 36);
        static readonly Color CWaterA   = Color.FromArgb(22, 104, 158);
        static readonly Color CWaterB   = Color.FromArgb(34, 142, 200);
        static readonly Color CLaneLine = Color.FromArgb(255, 214, 92);
        static readonly Color CSideA    = Color.FromArgb(30, 48, 74);
        static readonly Color CSideB    = Color.FromArgb(38, 58, 88);
        static readonly Color CTouch    = Color.FromArgb(0, 158, 226);
        static readonly Color CBlock    = Color.FromArgb(245, 158, 30);
        static readonly Color CBlind    = Color.FromArgb(152, 112, 224);
        static readonly Color CGun      = Color.FromArgb(34, 194, 112);
        static readonly Color CReset    = Color.FromArgb(232, 78, 68);
        static readonly Color CInk      = Color.FromArgb(232, 240, 250);
        static readonly Color CInkDim   = Color.FromArgb(146, 172, 202);
        static readonly Color CClock    = Color.FromArgb(255, 208, 64);

        // ── 计时 ──
        readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();
        DateTime _lastRunningSent = DateTime.MinValue;

        // ── 网络 ──
        TcpListener _listener;
        NetworkStream _stream;
        TcpClient _tcpClient;
        System.Net.Sockets.UdpClient _udp;
        IPEndPoint _udpTarget;
        ConnMode _mode = ConnMode.TcpServer;
        string _host = "127.0.0.1";
        int _port = PORT;
        int _netGen;              // 每次重开网络 +1, 老线程发现代数变了就自己退出
        ConnDialog _connDlg;
        volatile bool _running = true;
        int _framesSent, _framesRecv;

        // ── 界面 ──
        Label _clockLabel, _connLabel, _statLabel;
        Panel _flash, _horn;
        CheckBox _chkHorn;
        readonly ComboBox[,] _cbTp = new ComboBox[LANES, 2];
        readonly ComboBox[,] _cbMb = new ComboBox[LANES, 2];
        readonly ComboBox[,] _cbSb = new ComboBox[LANES, 2];
        int _flashLeft, _hornLeft;

        // ── 自动缩放 ──────────────────────────────────────────────
        // 整个界面一屏显示完, 不要滚动。按屏幕工作区跟"设计尺寸"的比值算一个
        // 系数, 所有尺寸和字号都乘它 —— 小屏自动缩小, 大屏适当放大。
        double _s = 1.0;
        // 字号是【磅值】, 渲染时还要再乘一遍系统 DPI(150% 缩放时 x1.5), 而本程序
        // 的布局坐标是【像素】—— 不折算的话, 换台高 DPI 的机器字就撑爆版面。
        // 这里把磅值除以 DPI/96, 让字的像素高度只跟 _s 走, 跟系统缩放无关。
        float _dpiAdj = 1f;
        int S(int v) { return Math.Max(1, (int)Math.Round(v * _s)); }
        Font F(float pt, FontStyle st) { return new Font("Microsoft YaHei", Math.Max(6f, (float)(pt * _s) * _dpiAdj), st); }
        Font FM(float pt, FontStyle st) { return new Font("Consolas", Math.Max(7f, (float)(pt * _s) * _dpiAdj), st); }

        // 设计尺寸(缩放系数 = 1 时)
        // D_WATER 只是水面的【下限】。窗口比这宽多少, 就全部加给水面 ——
        // 所以最大化时右边不会空一块。按钮按实际手感放大, 不跟着窗口变。
        // D_CW 取 (5*D_BW + 4*D_GAP - 2*D_GAP)/3 = 144:
        // 3 个成绩栏加 2 道缝, 正好等于 5 个按钮加 4 道缝 —— 上下对齐, 不参差
        // 触板成绩挪到水面上后, 侧区只剩 2 个成绩栏, 各占一半:
        //   2*D_CW + 1*D_GAP = 5*D_BW + 4*D_GAP  =>  D_CW = 219
        // 加宽是为了 XX:XX.XX 这种长成绩能完整显示。
        const int D_BW = 84, D_BH = 36, D_GAP = 6, D_CW = 219, D_WATER = 920;
        // 触板成绩要完整显示 时:分:秒.百分秒 (H:MM:SS.CC 共 10 个字符), 所以要够宽
        const int D_CWT = 390;   // 水面上那个「触板成绩」的宽度(主角, 做大)
        const int D_SIDE = 5 * D_BW + 4 * D_GAP + 16;
        const int D_ROW = 104, D_BAR = 120;   // 加高一点, 免得"空格/R"提示被底边切掉

        int BW, BH, GAP, CW, CWT, SIDE_W, WATER_W, ROW_H, BAR_H;

        public MainForm()
        {
            Text = "模拟游泳计时器 —— 10 道泳池 (TCP :" + PORT + ")";
            // 缩放全由下面的 _s 一手包办。WinForms 默认 AutoScaleMode=Font 还会
            // 按字体 DPI 再缩一次, 两套缩放打架 —— 结果就是时钟只露半截、
            // 成绩栏被行底切掉。关掉它。
            AutoScaleMode = AutoScaleMode.None;
            using (var g0 = CreateGraphics()) _dpiAdj = 96f / g0.DpiX;

            // 按屏幕工作区算缩放: 设计尺寸放不下就缩, 屏幕富余就适当放大(最多 1.6 倍)
            var wa = Screen.PrimaryScreen.WorkingArea;
            // 标题栏和边框不算客户区, 得先扣掉再算 —— 直接拿 Width/Height 去比,
            // 算出来的客户区会比屏幕高一个标题栏, 底下那道就被切掉、右边冒出滚动条。
            int chromeW = Width - ClientSize.Width;
            int chromeH = Height - ClientSize.Height;
            int designW = D_SIDE * 2 + D_WATER + 20;
            int designH = D_BAR + LANES * D_ROW + 8;
            // 高度说了算: 10 道必须一次全露出来。宽度另算 —— 见下面的 ClientSize。
            _s = (double)(wa.Height - 24 - chromeH) / designH;
            double wCap = (double)(wa.Width - 40 - chromeW) / designW;   // 窄屏兜底
            if (_s > wCap) _s = wCap;
            if (_s > 1.6) _s = 1.6;
            if (_s < 0.45) _s = 0.45;

            BW = S(D_BW); BH = S(D_BH); GAP = S(D_GAP); CW = S(D_CW); CWT = S(D_CWT);
            SIDE_W = 5 * BW + 4 * GAP + S(14); WATER_W = S(D_WATER);
            ROW_H = S(D_ROW); BAR_H = S(D_BAR);

            // 设客户区, 外框由窗体自己加 —— 保证 10 道全部露出来, 不出滚动条
            // 宽度直接吃满工作区: 两端按钮区是定宽的, 中间水面把余下的宽度全占了。
            // 这样最大化(或本来就铺满)时右侧不会留白。
            int wantW = Math.Max(SIDE_W * 2 + WATER_W + S(20), wa.Width - 40 - chromeW);
            ClientSize = new Size(wantW, BAR_H + LANES * ROW_H + S(8));
            // CenterScreen 在高 DPI 下不靠谱(实测跑到了 134,128, 右边和第 9 道被推出屏幕),
            // 自己算: 在工作区里居中, 且左上角不许为负 —— 宁可贴边也不许有内容跑到屏幕外。
            StartPosition = FormStartPosition.Manual;
            Load += delegate {
                var w2 = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(w2.X + Math.Max(0, (w2.Width  - Width)  / 2),
                                     w2.Y + Math.Max(0, (w2.Height - Height) / 2));
            };
            BackColor = CDeck;
            KeyPreview = true;
            DoubleBuffered = true;

            BuildPool();     // 先 Fill 后 Top —— WinForms 停靠顺序是反的
            BuildTopBar();

            _tick.Interval = 50; _tick.Tick += OnTick; _tick.Start();
            ReadCmdLine();
            StartNet();
            FormClosing += delegate { _running = false; StopNet(); };
        }

        // ══════════════ 顶栏: 发令点 + 滚动时间 ══════════════
        void BuildTopBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = BAR_H, BackColor = CBar };
            bar.Paint += delegate(object s, PaintEventArgs e) {
                using (var p = new Pen(CTouch, 2)) e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };
            Controls.Add(bar);

            var fTitle = F(10, FontStyle.Bold);
            bar.Controls.Add(new Label {
                Text = "发 令 点", ForeColor = CInkDim, Font = fTitle,
                Left = S(18), Top = S(8), Width = S(120), Height = fTitle.Height + S(3)
            });

            var bGun = MakeButton("发 令", S(18), S(34), S(152), S(56), CGun, 15);
            bGun.Click += delegate { FireGun(); };
            bar.Controls.Add(bGun);
            bar.Controls.Add(MakeHint("空格", S(18), S(96), S(152)));

            var bReset = MakeButton("复 位", S(180), S(34), S(122), S(56), CReset, 15);
            bReset.Click += delegate { ResetClock(); };
            bar.Controls.Add(bReset);
            bar.Controls.Add(MakeHint("R", S(180), S(96), S(122)));

            bar.Controls.Add(MakeHint("闪光灯", S(316), S(34), S(64)));
            _flash = new Panel { Left = S(316), Top = S(50), Width = S(64), Height = S(40), BackColor = CBar };
            _flash.Paint += delegate(object s, PaintEventArgs e) { PaintLamp(e.Graphics, _flash, _flashLeft > 0, Color.White); };
            bar.Controls.Add(_flash);

            bar.Controls.Add(MakeHint("喇叭", S(388), S(34), S(64)));
            _horn = new Panel { Left = S(388), Top = S(50), Width = S(64), Height = S(40), BackColor = CBar };
            _horn.Paint += delegate(object s, PaintEventArgs e) { PaintLamp(e.Graphics, _horn, _hornLeft > 0, CGun); };
            bar.Controls.Add(_horn);

            _chkHorn = new CheckBox {
                Text = "响", Checked = true, ForeColor = CInkDim, Font = F(8, FontStyle.Regular),
                Left = S(458), Top = S(60), Width = S(48), Height = S(22)
            };
            bar.Controls.Add(_chkHorn);

            var bConn = MakeButton("连接设置", S(520), S(34), S(150), S(56), Color.FromArgb(56, 108, 190), 11);
            bConn.Click += delegate { OpenConnDialog(); };
            bar.Controls.Add(bConn);
            bar.Controls.Add(MakeHint("比赛控制计算机", S(500), S(96), S(190)));

            _connLabel = new Label {
                Text = "等待比赛控制计算机连接…", ForeColor = Color.FromArgb(255, 176, 64),
                Font = F(9, FontStyle.Regular), Width = S(420), Height = F(9, FontStyle.Regular).Height + S(3),
                TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = bar.Width - S(450), Top = S(5), BackColor = CBar
            };
            bar.Controls.Add(_connLabel);

            _clockLabel = new Label {
                Text = "0.00", ForeColor = CClock, Font = FM(29, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight, Width = S(430), Height = FM(29, FontStyle.Bold).Height + S(6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right, Left = bar.Width - S(450), Top = S(20), BackColor = CBar
            };
            bar.Controls.Add(_clockLabel);

            _statLabel = new Label {
                Text = "发出 0 帧 / 收到 0 帧", ForeColor = Color.FromArgb(100, 126, 158),
                Font = F(8, FontStyle.Regular), Width = S(420), Height = F(8, FontStyle.Regular).Height + S(3),
                TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = bar.Width - S(450), Top = S(96), BackColor = CBar
            };
            bar.Controls.Add(_statLabel);

            KeyDown += delegate(object s, KeyEventArgs e) {
                if (e.KeyCode == Keys.Space) { FireGun(); e.Handled = true; }
                else if (e.KeyCode == Keys.R) { ResetClock(); e.Handled = true; }
            };
        }

        // 高度一律按字体实际行高算 —— 写死的话换字号/换 DPI 就切掉下半截
        Label MakeHint(string t, int x, int y, int w) { return MakeHint(t, x, y, w, 8f); }
        Label MakeHint(string t, int x, int y, int w, float pt)
        {
            var fn = F(pt, FontStyle.Regular);
            return new Label {
                Text = t, ForeColor = CInkDim, Font = fn,
                Left = x, Top = y, Width = w, Height = fn.Height + S(3),
                TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent
            };
        }

        static void PaintLamp(Graphics g, Panel p, bool on, Color lit)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (on) using (var glow = new SolidBrush(Color.FromArgb(70, lit)))
                g.FillEllipse(glow, -3, -3, p.Width + 4, p.Height + 4);
            var r = new Rectangle(6, 4, p.Width - 13, p.Height - 9);
            using (var b = new SolidBrush(on ? lit : Color.FromArgb(38, 52, 72))) g.FillEllipse(b, r);
            using (var pen = new Pen(on ? Color.White : Color.FromArgb(72, 94, 124), 2)) g.DrawEllipse(pen, r);
        }

        static Button MakeButton(string text, int x, int y, int w, int h, Color back, int fontSize)
        {
            var b = new Button {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                BackColor = back, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", fontSize, FontStyle.Bold),
                TabStop = false, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = ControlPaint.Light(back, 0.45f);
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Light(back, 0.55f);
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.2f);
            return b;
        }

        // ══════════════ 泳池 ══════════════
        void BuildPool()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CDeck };
            // 注意: Dock=Fill 必须排在 Controls 集合最前(BringToFront),
            // 它才是最后一个参与停靠、只吃剩下的地方。否则它先占满整个客户区,
            // 顶栏就被压成一条缝 —— 发令/复位/时间框全被切掉半截。
            Controls.Add(scroll);
            scroll.BringToFront();

            int rowH = ROW_H, y = S(8), margin = S(14);
            // 行宽 = 客户区宽 - 两边留白; Anchor 左右, 窗口变宽时行跟着变宽,
            // 变出来的宽度落在水面上(PaintRow 里的 waterW 是算出来的, 不是常量)。
            int totalW = Math.Max(SIDE_W * 2 + WATER_W, ClientSize.Width - margin * 2);
            for (int lane = 0; lane < LANES; lane++)
            {
                int laneCopy = lane;
                var row = new Panel { Left = margin, Top = y, Width = totalW, Height = rowH - S(6), BackColor = CDeck,
                                      Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                row.Paint += delegate(object s, PaintEventArgs e) { PaintRow(e.Graphics, row, laneCopy); };
                scroll.Controls.Add(row);
                BuildSide(row, lane, 0);   // 左端(镜像)
                BuildSide(row, lane, 1);   // 右端
                y += rowH;
            }
        }

        void PaintRow(Graphics g, Panel row, int lane)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = row.Height;
            int waterW = Math.Max(S(80), row.Width - SIDE_W * 2);   // 窗口多宽, 水面就多宽
            using (var b = new SolidBrush(lane % 2 == 0 ? CSideA : CSideB))
            {
                g.FillRectangle(b, 0, 0, SIDE_W, h);
                g.FillRectangle(b, SIDE_W + waterW, 0, SIDE_W, h);
            }
            var water = new Rectangle(SIDE_W, 0, waterW, h);
            using (var lg = new LinearGradientBrush(water, CWaterB, CWaterA, 90f)) g.FillRectangle(lg, water);
            using (var p = new Pen(Color.FromArgb(150, CLaneLine), 3))
            {
                g.DrawLine(p, SIDE_W, 2, SIDE_W + waterW, 2);
                g.DrawLine(p, SIDE_W, h - 3, SIDE_W + waterW, h - 3);
            }
            using (var p = new Pen(Color.FromArgb(225, 238, 250), 3))
            {
                g.DrawLine(p, SIDE_W, 0, SIDE_W, h);                       // 左池壁(触板位置)
                g.DrawLine(p, SIDE_W + waterW, 0, SIDE_W + waterW, h);   // 右池壁
            }
            var badge = new Rectangle(SIDE_W + waterW / 2 - S(32), h / 2 - S(29), S(64), S(58));
            using (var b = new SolidBrush(Color.FromArgb(210, 6, 26, 48))) g.FillEllipse(b, badge);
            using (var p = new Pen(CLaneLine, 2)) g.DrawEllipse(p, badge);
            using (var b = new SolidBrush(Color.White))
            using (var f = FM(25, FontStyle.Bold))
            {
                string t = lane.ToString();
                var sz = g.MeasureString(t, f);
                g.DrawString(t, f, b, badge.X + (badge.Width - sz.Width) / 2, badge.Y + (badge.Height - sz.Height) / 2);
            }
        }

        // 一端。left=true 时整体镜像, 触板永远贴着水面。
        void BuildSide(Panel row, int lane, int side)
        {
            bool left = (side == 0);
            int rw = row.Width;
            // 右端按行的右边缘算(不是按 WATER_W), 再配 Anchor=Right,
            // 窗口拉宽时右端整体跟着走, 始终贴着右池壁。
            Func<int, int, int> pos = delegate(int k, int w) {
                return left ? SIDE_W - 8 - (k + 1) * w - k * GAP
                            : rw - SIDE_W + 8 + k * (w + GAP);
            };
            var anc = left ? (AnchorStyles.Top | AnchorStyles.Left)
                           : (AnchorStyles.Top | AnchorStyles.Right);

            var bTp = MakeButton("触板", pos(0, BW), S(6), BW, BH, CTouch, 11);
            bTp.Anchor = anc;
            bTp.Click += delegate { OnPress(lane, side, CMD_TOUCHPAD); };
            row.Controls.Add(bTp);

            var bSb = MakeButton("出发台", pos(1, BW), S(6), BW, BH, CBlock, 10);
            bSb.Anchor = anc;
            bSb.Click += delegate { OnPress(lane, side, CMD_STARTBLOCK); };
            row.Controls.Add(bSb);

            for (int m = 0; m < 3; m++)
            {
                byte cmd = m == 0 ? CMD_MB1 : (m == 1 ? CMD_MB2 : CMD_MB3);
                var b = MakeButton("盲表" + (m + 1), pos(2 + m, BW), S(6), BW, BH, CBlind, 10);
                b.Anchor = anc;
                b.Click += delegate { OnPress(lane, side, cmd); };
                row.Controls.Add(b);
            }

            // 触板成绩放在水面上、紧挨着触板那面池壁 —— 看触板成绩不用把视线移出泳池
            int twX = left ? SIDE_W + S(14) : rw - SIDE_W - S(14) - CWT;
            _cbTp[lane, side] = MakeCombo(row, twX, S(30), CWT, "触板成绩", CTouch, anc, 19);   // 主角, 竖向居中
            // 出发/盲表是参考值, 字小框矮, 不跟触板成绩抢眼
            _cbSb[lane, side] = MakeCombo(row, pos(0, CW), S(58), CW, "出发成绩", CBlock, anc, 11);
            _cbMb[lane, side] = MakeCombo(row, pos(1, CW), S(58), CW, "盲表成绩", CBlind, anc, 11);
        }

        ComboBox MakeCombo(Panel row, int x, int y, int w, string hint, Color accent, AnchorStyles anc, float pt)
        {
            var fHint = F(pt >= 16 ? 9 : 8, FontStyle.Bold);
            int hHint = fHint.Height + S(3);
            row.Controls.Add(new Label {
                Text = hint, ForeColor = ControlPaint.Light(accent, 0.5f),
                Font = fHint, Left = x, Top = y - hHint, Width = w, Height = hHint,
                TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, Anchor = anc
            });
            var fnt = FM(pt, FontStyle.Bold);
            var cb = new ComboBox {
                Left = x, Top = y, Width = w, Anchor = anc,
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                Font = fnt, TabStop = false,
                BackColor = Color.FromArgb(8, 18, 32), ForeColor = CInk
            };
            // DropDownList 不认 TextAlign, 想右对齐只能自绘。
            // 一旦改成 OwnerDrawFixed, 控件高度就由 ItemHeight 说了算(默认 13,
            // 不设会缩成一条缝), 所以必须按字体高度自己给。
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.ItemHeight = fnt.Height + S(6);
            int padR = S(6);
            cb.DrawItem += delegate(object ds, DrawItemEventArgs de) {
                bool sel = (de.State & DrawItemState.Selected) != 0;
                using (var bg = new SolidBrush(sel ? Color.FromArgb(24, 52, 88) : Color.FromArgb(8, 18, 32)))
                    de.Graphics.FillRectangle(bg, de.Bounds);
                if (de.Index < 0) return;
                var box = (ComboBox)ds;
                var r2 = new Rectangle(de.Bounds.X, de.Bounds.Y, de.Bounds.Width - padR, de.Bounds.Height);
                using (var br = new SolidBrush(CInk))
                using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                    de.Graphics.DrawString(box.Items[de.Index].ToString(), box.Font, br, r2, sf);
            };
            row.Controls.Add(cb);
            return cb;
        }

        // ══════════════ 按键 → 发帧 ══════════════
        void OnPress(int lane, int side, byte cmd)
        {
            double t = _clock.Elapsed.TotalSeconds;
            int d4 = (side == 0) ? lane : lane + 10;
            SendFrame(cmd, 0, d4, t);

            string txt = Fmt(t);
            ComboBox cb;
            if (cmd == CMD_TOUCHPAD) { cb = _cbTp[lane, side]; txt = FmtFull(t); }
            else if (cmd == CMD_STARTBLOCK) cb = _cbSb[lane, side];
            else { cb = _cbMb[lane, side]; txt = (cmd == CMD_MB1 ? "1|" : cmd == CMD_MB2 ? "2|" : "3|") + txt; }
            cb.Items.Insert(0, txt);
            cb.SelectedIndex = 0;
        }

        void FireGun()
        {
            _clock.Restart();
            SendFrame(CMD_START, 0, 0, 0);
            _flashLeft = 20; _hornLeft = 8;
            if (_chkHorn.Checked)
                ThreadPool.QueueUserWorkItem(delegate { try { Console.Beep(1000, 300); } catch { } });
        }

        void ResetClock()
        {
            _clock.Reset();
            SendFrame(CMD_RESET, 0, 0, 0);
            _clockLabel.Text = "0.00";
            for (int l = 0; l < LANES; l++)
                for (int s = 0; s < 2; s++)
                {
                    _cbTp[l, s].Items.Clear(); _cbSb[l, s].Items.Clear(); _cbMb[l, s].Items.Clear();
                    _cbTp[l, s].SelectedIndex = -1; _cbSb[l, s].SelectedIndex = -1; _cbMb[l, s].SelectedIndex = -1;
                }
        }

        void OnTick(object sender, EventArgs e)
        {
            if (_clock.IsRunning)
            {
                double t = _clock.Elapsed.TotalSeconds;
                _clockLabel.Text = Fmt(t);
                if ((DateTime.Now - _lastRunningSent).TotalMilliseconds >= 100)
                { _lastRunningSent = DateTime.Now; SendFrame(CMD_RUNNING, 0, 0, t); }
            }
            if (_flashLeft > 0) { _flashLeft--; _flash.Invalidate(); }
            if (_hornLeft > 0) { _hornLeft--; _horn.Invalidate(); }
            _statLabel.Text = string.Format("发出 {0} 帧 / 收到 {1} 帧", _framesSent, _framesRecv);
        }

        // 触板成绩是正式成绩, 时/分/秒/百分秒一个都不能少 —— 哪怕是 0 也照写,
        // 免得看的人还得自己判断"这个 3.35 到底是 3 秒还是 3 分"。
        static string FmtFull(double s)
        {
            if (s < 0) s = 0;
            long cs = (long)Math.Round(s * 100);
            long h = cs / 360000, m = (cs / 6000) % 60, sec = (cs / 100) % 60, c = cs % 100;
            // 前导 0 消隐: 没到 1 分就不写分, 没到 1 小时就不写时 ——
            // 成绩栏是右对齐的, 空出来的位置正好就是"这几位是 0"的意思,
            // 跟计时大屏的读法一致(5.36 而不是 0:00:05.36)。
            if (h > 0) return string.Format("{0}:{1:D2}:{2:D2}.{3:D2}", h, m, sec, c);
            if (m > 0) return string.Format("{0}:{1:D2}.{2:D2}", m, sec, c);
            return string.Format("{0}.{1:D2}", sec, c);
        }

        static string Fmt(double s)
        {
            if (s < 0) s = 0;
            int cs = (int)Math.Round(s * 100);
            int m = cs / 6000, sec = (cs / 100) % 60, c = cs % 100;
            return m > 0 ? string.Format("{0}:{1:D2}.{2:D2}", m, sec, c)
                         : string.Format("{0}.{1:D2}", sec, c);
        }

        // ══════════════ 网络: TCP 服务端 / TCP 客户端 / UDP ══════════════
        static readonly Color COk   = Color.FromArgb(72, 230, 140);
        static readonly Color CWait = Color.FromArgb(255, 176, 64);
        static readonly Color CBad  = Color.FromArgb(255, 96, 96);

        /// <summary>
        /// 命令行预设连接方式, 免得每次开机都要进对话框点一遍:
        ///   --mode tcpserver|tcpclient|udp   --host 192.168.1.20   --port 5000
        /// 现场部署时写进快捷方式就行。
        /// </summary>
        void ReadCmdLine()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 1; i < a.Length; i++)
            {
                string k = a[i].ToLowerInvariant();
                string v = (i + 1 < a.Length) ? a[i + 1] : null;
                if (v == null) break;
                if (k == "--mode")
                {
                    string m = v.ToLowerInvariant();
                    if (m == "tcpserver" || m == "server") _mode = ConnMode.TcpServer;
                    else if (m == "tcpclient" || m == "client") _mode = ConnMode.TcpClient;
                    else if (m == "udp") _mode = ConnMode.Udp;
                    i++;
                }
                else if (k == "--host") { _host = v; i++; }
                else if (k == "--port") { int n; if (int.TryParse(v, out n) && n > 0 && n < 65536) _port = n; i++; }
            }
        }

        void StopNet()
        {
            _netGen++;                      // 老线程看到代数变了就自己收摊
            try { if (_stream != null) _stream.Close(); } catch { }
            _stream = null;
            try { if (_tcpClient != null) _tcpClient.Close(); } catch { }
            _tcpClient = null;
            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;
            try { if (_udp != null) _udp.Close(); } catch { }
            _udp = null;
            _udpTarget = null;
        }

        void StartNet()
        {
            StopNet();
            int gen = ++_netGen;
            try
            {
                if (_mode == ConnMode.TcpServer)
                {
                    _listener = new TcpListener(IPAddress.Any, _port);
                    _listener.Start();
                    new Thread((ThreadStart)delegate { AcceptLoop(gen); }) { IsBackground = true }.Start();
                    SetConn(string.Format("TCP 服务端 :{0} —— 等待比赛控制计算机连接…", _port), CWait);
                }
                else if (_mode == ConnMode.TcpClient)
                {
                    new Thread((ThreadStart)delegate { ClientLoop(gen); }) { IsBackground = true }.Start();
                    SetConn(string.Format("TCP 客户端 → {0}:{1} 连接中…", _host, _port), CWait);
                }
                else
                {
                    _udp = new System.Net.Sockets.UdpClient(_port);
                    _udpTarget = new IPEndPoint(Resolve(_host), _port);
                    new Thread((ThreadStart)delegate { UdpLoop(gen); }) { IsBackground = true }.Start();
                    // UDP 不建连接, 发得出去就算通 —— 这里不写"已连接", 免得误导
                    SetConn(string.Format("UDP :{0} → {1}:{0}", _port, _host), COk);
                }
            }
            catch (Exception ex)
            {
                SetConn("启动失败: " + ex.Message, CBad);
                MessageBox.Show("按当前设置启动网络失败:\n" + ex.Message +
                    "\n\n端口被占用(比如已经开着一个模拟器), 或者地址写错了。", "模拟计时器");
            }
            UpdateTitle();
        }

        static IPAddress Resolve(string host)
        {
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return ip;
            var a = System.Net.Dns.GetHostAddresses(host);
            foreach (var x in a)
                if (x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return x;
            if (a.Length > 0) return a[0];
            throw new Exception("解析不了地址: " + host);
        }

        void UpdateTitle()
        {
            string m = _mode == ConnMode.TcpServer ? "TCP 服务端 :" + _port
                     : _mode == ConnMode.TcpClient ? "TCP 客户端 → " + _host + ":" + _port
                                                   : "UDP :" + _port + " → " + _host;
            string t = "模拟游泳计时器 —— 10 道泳池 (" + m + ")";
            try { BeginInvoke((Action)delegate { Text = t; }); } catch { Text = t; }
        }

        void Pump(NetworkStream st, int gen, Func<bool> alive)
        {
            var buf = new byte[512];
            while (_running && gen == _netGen && alive())
            {
                int n = st.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                _framesRecv += n / FRAME;
            }
        }

        void AcceptLoop(int gen)
        {
            while (_running && gen == _netGen)
            {
                try
                {
                    var c = _listener.AcceptTcpClient();
                    _tcpClient = c;
                    _stream = c.GetStream();
                    SetConn("比赛控制计算机已连接", COk);
                    Pump(_stream, gen, delegate { return c.Connected; });
                }
                catch { }
                _stream = null;
                if (!_running || gen != _netGen) break;
                SetConn("已断开，等待重新连接…", CWait);
            }
        }

        void ClientLoop(int gen)
        {
            while (_running && gen == _netGen)
            {
                try
                {
                    var c = new TcpClient();
                    c.Connect(Resolve(_host), _port);
                    _tcpClient = c;
                    _stream = c.GetStream();
                    SetConn(string.Format("已连上 {0}:{1}", _host, _port), COk);
                    Pump(_stream, gen, delegate { return c.Connected; });
                }
                catch { }
                _stream = null;
                if (!_running || gen != _netGen) break;
                SetConn(string.Format("连不上 {0}:{1}，2 秒后重试…", _host, _port), CWait);
                Thread.Sleep(2000);
            }
        }

        void UdpLoop(int gen)
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running && gen == _netGen)
            {
                try
                {
                    var u = _udp;
                    if (u == null) break;
                    var d = u.Receive(ref any);
                    if (d != null && d.Length > 0) _framesRecv += d.Length / FRAME;
                }
                catch { break; }
            }
        }

        void OpenConnDialog()
        {
            using (var d = new ConnDialog(_mode, _host, _port))
            {
                _connDlg = d;
                d.ShowState(_connLabel.Text, Color.FromArgb(140, 70, 0));
                d.Connect = delegate(ConnMode m, string h, int pt) {
                    _mode = m; _host = h.Length == 0 ? _host : h; _port = pt;
                    StartNet();                       // 当场生效, 不用关窗口
                };
                d.Disconnect = delegate {
                    StopNet();
                    SetConn("已断开(手动)", CWait);
                    UpdateTitle();
                };
                d.ShowDialog(this);
                _connDlg = null;
            }
        }

        void SetConn(string text, Color color)
        {
            try { BeginInvoke((Action)delegate {
                _connLabel.Text = text; _connLabel.ForeColor = color;
                if (_connDlg != null && !_connDlg.IsDisposed) _connDlg.ShowState(text, Color.FromArgb(140, 70, 0));
            }); }
            catch { }
        }

        void SendFrame(byte cmd, byte cmd1, int d4, double seconds, byte d10 = 0)
        {
            double abs = Math.Abs(seconds);
            int h = (int)(abs / 3600);
            int m = (int)((abs - h * 3600) / 60);
            int s = (int)(abs - h * 3600 - m * 60);
            int cs = (int)Math.Round((abs - h * 3600 - m * 60 - s) * 100);
            if (cs >= 100) cs = 99;

            var f = new byte[FRAME];
            f[0] = SOH; f[1] = STX_S; f[2] = cmd; f[3] = cmd1; f[4] = (byte)d4;
            f[5] = (byte)m; f[6] = (byte)s; f[7] = (byte)cs;
            f[8] = (byte)((h & 0x0F) << 4);
            f[9] = 0; f[10] = d10; f[11] = EOT;
            try
            {
                if (_mode == ConnMode.Udp)
                {
                    var u = _udp; var t = _udpTarget;
                    if (u == null || t == null) return;
                    u.Send(f, f.Length, t);
                }
                else
                {
                    var st = _stream;
                    if (st == null) return;
                    st.Write(f, 0, f.Length); st.Flush();
                }
                _framesSent++;
            }
            catch { }
        }
    }
}
