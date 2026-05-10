using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;

class SetupForm : Form
{
    // 安装步骤面板
    Panel panelWelcome, panelPath, panelProgress, panelDone;
    TextBox txtPath;
    ProgressBar progressBar;
    Label lblStatus, lblProgress;
    string installDir = @"C:\SwimmingTimingSystem";
    string sourceDir;

    public SetupForm()
    {
        sourceDir = Path.GetDirectoryName(Application.ExecutablePath);
        Text = "游泳赛事管理与计时系统 - 安装向导";
        Size = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.White;

        // 尝试设置图标
        try {
            string icoPath = Path.Combine(sourceDir, "setup.ico");
            if (File.Exists(icoPath)) Icon = new Icon(icoPath);
        } catch { }

        BuildWelcome();
        BuildPathSelect();
        BuildProgress();
        BuildDone();

        ShowStep(0);
    }

    // ═══════ 左侧蓝色装饰条 ═══════
    Panel CreateSideBar()
    {
        var p = new Panel { Width = 200, Dock = DockStyle.Left };
        p.Paint += (s, e) => {
            var rect = new Rectangle(0, 0, p.Width, p.Height);
            using (var brush = new LinearGradientBrush(rect, Color.FromArgb(30, 64, 175), Color.FromArgb(37, 99, 235), 90f))
                e.Graphics.FillRectangle(brush, rect);

            var sf = new StringFormat { Alignment = StringAlignment.Center };
            // 用 Wingdings 字体绘制水波图标
            using (var iconFont = new Font("Wingdings", 48, FontStyle.Bold))
                e.Graphics.DrawString("S", iconFont, new SolidBrush(Color.FromArgb(120, 255, 255, 255)), new RectangleF(0, 60, p.Width, 70), sf);
            // 系统名称（分三行显示，确保不被截断）
            using (var titleFont = new Font("Microsoft YaHei", 14, FontStyle.Bold))
                e.Graphics.DrawString("游泳赛事", titleFont, Brushes.White, new RectangleF(0, 150, p.Width, 30), sf);
            using (var titleFont = new Font("Microsoft YaHei", 14, FontStyle.Bold))
                e.Graphics.DrawString("管理与计时", titleFont, Brushes.White, new RectangleF(0, 180, p.Width, 30), sf);
            using (var titleFont = new Font("Microsoft YaHei", 14, FontStyle.Bold))
                e.Graphics.DrawString("系统", titleFont, Brushes.White, new RectangleF(0, 210, p.Width, 30), sf);
            // 版本号
            using (var verFont = new Font("Segoe UI", 10))
                e.Graphics.DrawString("v2026.06.10", verFont, new SolidBrush(Color.FromArgb(180, 255, 255, 255)), new RectangleF(0, 260, p.Width, 22), sf);
        };
        return p;
    }

    // ═══════ 按钮栏 ═══════
    Panel CreateButtonBar(string backText, EventHandler backClick, string nextText, EventHandler nextClick, string cancelText = "取消")
    {
        var bar = new Panel { Height = 60, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(248, 250, 252) };
        bar.Paint += (s, e) => { e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240)), 0, 0, bar.Width, 0); };

        if (!string.IsNullOrEmpty(cancelText))
        {
            var btnCancel = MakeButton(cancelText, Color.FromArgb(100, 116, 139), Color.White);
            btnCancel.Location = new Point(bar.Width - 110, 14);
            btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnCancel.Click += (s, e) => { if (MessageBox.Show("确定取消安装？", "取消", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Close(); };
            bar.Controls.Add(btnCancel);
        }

        if (!string.IsNullOrEmpty(nextText))
        {
            var btnNext = MakeButton(nextText, Color.FromArgb(59, 130, 246), Color.White);
            btnNext.Location = new Point(bar.Width - (string.IsNullOrEmpty(cancelText) ? 110 : 220), 14);
            btnNext.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnNext.Click += nextClick;
            bar.Controls.Add(btnNext);
        }

        if (!string.IsNullOrEmpty(backText))
        {
            var btnBack = MakeButton(backText, Color.FromArgb(241, 245, 249), Color.FromArgb(51, 65, 85));
            btnBack.Location = new Point(bar.Width - (string.IsNullOrEmpty(cancelText) ? 220 : 330), 14);
            btnBack.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnBack.Click += backClick;
            bar.Controls.Add(btnBack);
        }

        return bar;
    }

    Button MakeButton(string text, Color bg, Color fg)
    {
        var btn = new Button {
            Text = text, Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg,
            Font = new Font("Microsoft YaHei", 10), Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    // ═══════ 步骤1: 欢迎 ═══════
    void BuildWelcome()
    {
        panelWelcome = new Panel { Dock = DockStyle.Fill, Visible = false };

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 30, 30, 0) };
        var title = new Label { Text = "欢迎使用安装向导", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59), AutoSize = true, Location = new Point(30, 20) };
        content.Controls.Add(title);

        var desc = new Label {
            Text = "本向导将安装以下组件到您的计算机：\n\n" +
                   "  ●  游泳赛事管理主服务器\n" +
                   "       赛事管理核心，连接计时硬件，管理运动员、日程、成绩\n\n" +
                   "  ●  远程计时控制台（EXE客户端）\n" +
                   "       独立的比赛控制客户端，可在裁判台电脑运行\n\n" +
                   "  ●  Web控制台（HTML浏览器客户端）\n" +
                   "       在浏览器中控制比赛，无需安装客户端\n\n" +
                   "  ●  大屏显示 / 排名屏 / 在线报名\n" +
                   "       主服务器内置的Web页面\n\n" +
                   "点击【下一步】继续安装。",
            Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(71, 85, 105),
            Location = new Point(30, 60), Size = new Size(380, 320)
        };
        content.Controls.Add(desc);
        // WinForms Dock 顺序：先 Add 的后布局，后 Add 的先布局
        // 正确顺序：Fill → Left → Bottom（这样 Bottom 先占底部，Left 再占左侧，Fill 填剩余）
        panelWelcome.Controls.Add(content);
        panelWelcome.Controls.Add(CreateSideBar());
        panelWelcome.Controls.Add(CreateButtonBar("", null, "下一步 >", (s, e) => ShowStep(1)));
        Controls.Add(panelWelcome);
    }

    // ═══════ 步骤2: 选择目录 ═══════
    void BuildPathSelect()
    {
        panelPath = new Panel { Dock = DockStyle.Fill, Visible = false };

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 30, 30, 0) };
        var title = new Label { Text = "选择安装目录", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59), AutoSize = true, Location = new Point(30, 20) };
        content.Controls.Add(title);

        var desc = new Label { Text = "请选择程序的安装目录，或使用默认路径：", Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(30, 60), AutoSize = true };
        content.Controls.Add(desc);

        txtPath = new TextBox {
            Text = installDir, Font = new Font("Consolas", 11),
            Location = new Point(30, 100), Size = new Size(300, 28),
            BorderStyle = BorderStyle.FixedSingle
        };
        content.Controls.Add(txtPath);

        var btnBrowse = MakeButton("浏览...", Color.FromArgb(241, 245, 249), Color.FromArgb(51, 65, 85));
        btnBrowse.Location = new Point(340, 98);
        btnBrowse.Click += (s, e) => {
            var dlg = new FolderBrowserDialog { Description = "选择安装目录", SelectedPath = txtPath.Text };
            if (dlg.ShowDialog() == DialogResult.OK) txtPath.Text = dlg.SelectedPath;
        };
        content.Controls.Add(btnBrowse);

        var info = new Label {
            Text = "安装将创建以下子目录：\n\n" +
                   "  Server\\                主服务器程序和Web页面\n" +
                   "  Server\\Database\\       赛事数据（JSON）\n" +
                   "  Server\\Documents\\      生成的文档\n" +
                   "  Server\\Records\\        纪录模板\n" +
                   "  RemoteControl\\         远程控制台",
            Font = new Font("Microsoft YaHei", 9.5f), ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(30, 150), Size = new Size(380, 180)
        };
        content.Controls.Add(info);

        panelPath.Controls.Add(content);
        panelPath.Controls.Add(CreateSideBar());
        panelPath.Controls.Add(CreateButtonBar("< 上一步", (s, e) => ShowStep(0), "安装", (s, e) => { installDir = txtPath.Text; ShowStep(2); DoInstall(); }));
        Controls.Add(panelPath);
    }

    // ═══════ 步骤3: 安装进度 ═══════
    void BuildProgress()
    {
        panelProgress = new Panel { Dock = DockStyle.Fill, Visible = false };

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 30, 30, 0) };
        var title = new Label { Text = "正在安装...", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59), AutoSize = true, Location = new Point(30, 20) };
        content.Controls.Add(title);

        lblStatus = new Label { Text = "准备中...", Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(30, 70), AutoSize = true };
        content.Controls.Add(lblStatus);

        progressBar = new ProgressBar { Location = new Point(30, 110), Size = new Size(380, 26), Style = ProgressBarStyle.Continuous };
        content.Controls.Add(progressBar);

        lblProgress = new Label { Text = "0%", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(59, 130, 246), Location = new Point(30, 145), AutoSize = true };
        content.Controls.Add(lblProgress);

        panelProgress.Controls.Add(content);
        panelProgress.Controls.Add(CreateSideBar());
        panelProgress.Controls.Add(CreateButtonBar("", null, "", null, ""));
        Controls.Add(panelProgress);
    }

    // ═══════ 步骤4: 完成 ═══════
    void BuildDone()
    {
        panelDone = new Panel { Dock = DockStyle.Fill, Visible = false };

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 30, 30, 0) };
        var title = new Label { Text = "安装完成！", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(34, 197, 94), AutoSize = true, Location = new Point(30, 20) };
        content.Controls.Add(title);

        var info = new Label {
            Text = "游泳赛事管理与计时系统 v2026.05.10 已成功安装。\n\n" +
                   "已创建桌面快捷方式：\n" +
                   "  ★  游泳赛事管理主服务器\n" +
                   "  ★  远程计时控制台\n\n" +
                   "Web 客户端地址（主服务器启动后）：\n" +
                   "  比赛控制  http://<server>:3002/race_control.html\n" +
                   "  大屏显示  http://<server>:3002/display.html\n" +
                   "  排名屏      http://<server>:3002/leaderboard.html\n" +
                   "  在线报名  http://<server>:3002/register.html\n" +
                   "  检录台      http://<server>:3002/checkin.html\n\n" +
                   "使用说明书：" + installDir + "\\使用说明书.pdf",
            Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(71, 85, 105),
            Location = new Point(30, 60), Size = new Size(380, 280)
        };
        content.Controls.Add(info);

        panelDone.Controls.Add(content);
        panelDone.Controls.Add(CreateSideBar());
        panelDone.Controls.Add(CreateButtonBar("", null, "完成", (s, e) => Close(), ""));
        Controls.Add(panelDone);
    }

    void ShowStep(int step)
    {
        panelWelcome.Visible = (step == 0);
        panelPath.Visible = (step == 1);
        panelProgress.Visible = (step == 2);
        panelDone.Visible = (step == 3);
    }

    void SetProgress(int pct, string msg)
    {
        if (InvokeRequired) { Invoke(new Action(() => SetProgress(pct, msg))); return; }
        progressBar.Value = pct;
        lblProgress.Text = pct + "%";
        lblStatus.Text = msg;
        Application.DoEvents();
    }

    void DoInstall()
    {
        try
        {
            SetProgress(5, "创建安装目录...");
            string[] dirs = { installDir, installDir + @"\Server", installDir + @"\Server\Web",
                installDir + @"\Server\Records", installDir + @"\Server\Database",
                installDir + @"\Server\Documents", installDir + @"\RemoteControl" };
            foreach (var d in dirs) { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }

            SetProgress(15, "复制主服务器程序及依赖库...");
            // 复制 SwimmingScoreboard\ 根目录下所有文件（exe + 所有 dll）到 Server\
            string serverSrc = Path.Combine(sourceDir, "SwimmingScoreboard");
            string serverDst = Path.Combine(installDir, "Server");
            if (Directory.Exists(serverSrc))
                foreach (var f in Directory.GetFiles(serverSrc))
                    File.Copy(f, Path.Combine(serverDst, Path.GetFileName(f)), true);

            SetProgress(40, "复制Web页面...");
            string webSrc = Path.Combine(sourceDir, "SwimmingScoreboard", "Web");
            if (Directory.Exists(webSrc))
                foreach (var f in Directory.GetFiles(webSrc))
                    File.Copy(f, Path.Combine(installDir, "Server", "Web", Path.GetFileName(f)), true);

            SetProgress(55, "复制纪录模板...");
            string recSrc = Path.Combine(sourceDir, "SwimmingScoreboard", "Records");
            if (Directory.Exists(recSrc))
                foreach (var f in Directory.GetFiles(recSrc))
                    File.Copy(f, Path.Combine(installDir, "Server", "Records", Path.GetFileName(f)), true);

            SetProgress(65, "复制远程控制台及依赖库...");
            // 复制 RemoteTimingControl\ 根目录下所有文件到 RemoteControl\
            string remoteSrc = Path.Combine(sourceDir, "RemoteTimingControl");
            string remoteDst = Path.Combine(installDir, "RemoteControl");
            if (Directory.Exists(remoteSrc))
                foreach (var f in Directory.GetFiles(remoteSrc))
                    File.Copy(f, Path.Combine(remoteDst, Path.GetFileName(f)), true);

            SetProgress(70, "复制工具程序（计时模拟器/参数调试）...");
            string toolsDst = Path.Combine(installDir, "Tools");
            if (!Directory.Exists(toolsDst)) Directory.CreateDirectory(toolsDst);
            foreach (string toolName in new[] { "TimingSimulator.exe", "ParamDebugBot.exe" }) {
                string toolSrc = Path.Combine(sourceDir, toolName);
                if (File.Exists(toolSrc)) File.Copy(toolSrc, Path.Combine(toolsDst, toolName), true);
            }

            SetProgress(80, "创建桌面快捷方式...");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktop, "游泳赛事管理主服务器.lnk"),
                Path.Combine(installDir, "Server", "SwimmingScoreboard.exe"),
                Path.Combine(installDir, "Server"));
            CreateShortcut(Path.Combine(desktop, "远程计时控制台.lnk"),
                Path.Combine(installDir, "RemoteControl", "RemoteTimingControl.exe"),
                Path.Combine(installDir, "RemoteControl"));

            SetProgress(90, "创建开始菜单快捷方式...");
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "游泳赛事管理系统");
            if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, "游泳赛事管理主服务器.lnk"),
                Path.Combine(installDir, "Server", "SwimmingScoreboard.exe"),
                Path.Combine(installDir, "Server"));
            CreateShortcut(Path.Combine(startMenu, "远程计时控制台.lnk"),
                Path.Combine(installDir, "RemoteControl", "RemoteTimingControl.exe"),
                Path.Combine(installDir, "RemoteControl"));

            // 复制使用说明书（PDF）
            string manualSrc = Path.Combine(sourceDir, "使用说明书.pdf");
            if (File.Exists(manualSrc)) File.Copy(manualSrc, Path.Combine(installDir, "使用说明书.pdf"), true);

            SetProgress(95, "创建卸载程序...");
            CreateUninstaller(desktop, startMenu);

            SetProgress(100, "安装完成！");
            System.Threading.Thread.Sleep(500);
            ShowStep(3);
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装过程中出现错误:\n\n" + ex.Message, "安装错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowStep(1);
        }
    }

    void FileCopy(string relSrc, string relDst)
    {
        string src = Path.Combine(sourceDir, relSrc);
        string dst = Path.Combine(installDir, relDst);
        if (File.Exists(src)) File.Copy(src, dst, true);
    }

    void CreateUninstaller(string desktop, string startMenu)
    {
        // 复制卸载程序 EXE（与安装包一起打包的 Uninstall.exe）
        string uninstSrc = Path.Combine(sourceDir, "Uninstall.exe");
        string uninstDst = Path.Combine(installDir, "Uninstall.exe");
        if (File.Exists(uninstSrc))
            File.Copy(uninstSrc, uninstDst, true);

        // 在开始菜单添加卸载快捷方式
        CreateShortcut(Path.Combine(startMenu, "\u5378\u8F7D\u6E38\u6CF3\u8D5B\u4E8B\u7BA1\u7406\u7CFB\u7EDF.lnk"),
            uninstDst, installDir); // 卸载游泳赛事管理系统.lnk
    }

    void CreateShortcut(string lnkPath, string target, string workDir)
    {
        try
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // WScript.Shell
            dynamic shell = Activator.CreateInstance(t);
            dynamic sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath = target;
            sc.WorkingDirectory = workDir;
            sc.Save();
            Marshal.FinalReleaseComObject(sc);
            Marshal.FinalReleaseComObject(shell);
        }
        catch { }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }
}
