using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

class SetupForm : Form
{
    // 安装步骤面板
    Panel panelWelcome, panelPath, panelProgress, panelDone;
    TextBox txtPath;
    CheckBox chkDesktopShortcut;
    ProgressBar progressBar;
    Label lblStatus, lblProgress, lblDoneInfo;
    // 2026-10-08 现场反馈这个版本号从 6 月 13 日起就没更新过——装机向导里显示的版本号
    //   跟实际安装包的新旧完全对不上, 容易让人误以为装错了旧包。打包一次改一次这个常量
    //   (跟 SwimmingScoreboard_Setup_v<日期>_<序号>.zip 的日期对齐即可, 不需要每个小改动
    //   都单独改这里)。
    const string InstallerVersion = "v2026.10.08";
    string installDir = @"C:\SwimmingTimingSystem";
    // 2026-09-28 现场反馈"以前可选的生成桌面快捷方式选项没有了"——加一个可勾选项,
    //   默认勾选(=保留原来的行为), 不想要桌面图标的可以取消勾。
    bool createDesktopShortcut = true;
    // 同时把 CreateShortcut 原来的静默 catch{} 换成有记录——之前失败了界面上完全看不出来,
    //   用户装完发现没图标也不知道是"选了不装"还是"装失败了"。
    System.Collections.Generic.List<string> shortcutFailures = new System.Collections.Generic.List<string>();
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
                e.Graphics.DrawString(InstallerVersion, verFont, new SolidBrush(Color.FromArgb(180, 255, 255, 255)), new RectangleF(0, 260, p.Width, 22), sf);
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

        chkDesktopShortcut = new CheckBox {
            Text = "创建桌面快捷方式", Checked = true,
            Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(30, 140), AutoSize = true
        };
        content.Controls.Add(chkDesktopShortcut);

        var info = new Label {
            Text = "安装将创建以下子目录：\n\n" +
                   "  Server\\                主服务器程序和Web页面\n" +
                   "  Server\\Database\\       赛事数据（JSON）\n" +
                   "  Server\\Documents\\      生成的文档\n" +
                   "  Server\\Records\\        纪录模板\n" +
                   "  RemoteControl\\         远程计时控制台\n" +
                   "  RemoteDisplay\\         远程显示控制台\n" +
                   "  Registration\\          运动员报名工具\n" +
                   "  ScheduleEditor\\        编排记录及成绩处理",
            Font = new Font("Microsoft YaHei", 9.5f), ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(30, 175), Size = new Size(380, 200)
        };
        content.Controls.Add(info);

        panelPath.Controls.Add(content);
        panelPath.Controls.Add(CreateSideBar());
        panelPath.Controls.Add(CreateButtonBar("< 上一步", (s, e) => ShowStep(0), "安装", (s, e) => { installDir = txtPath.Text; createDesktopShortcut = chkDesktopShortcut.Checked; ShowStep(2); DoInstall(); }));
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

        lblDoneInfo = new Label {
            Text = "",   // 2026-09-28 内容改成装完后在 UpdateDoneInfo() 里动态填(快捷方式那几行
                         //   要按 createDesktopShortcut/shortcutFailures 实际结果来写, 不能再是
                         //   固定死"已创建"——之前不管装没装成功、用户是否勾选, 都写死"已创建",
                         //   跟现场反馈的"选了创建但图标没出现"这类情况对不上, 用户没法判断到底
                         //   是没装还是装错了。
            Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(71, 85, 105),
            Location = new Point(30, 60), Size = new Size(380, 340)
        };
        content.Controls.Add(lblDoneInfo);

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
                installDir + @"\Server\Documents", installDir + @"\RemoteControl",
                installDir + @"\RemoteDisplay", installDir + @"\Registration",
                installDir + @"\ScheduleEditor", installDir + @"\ScheduleEditor\Records",
                installDir + @"\ScheduleEditor\Database" };
            foreach (var d in dirs) { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }

            // 2026-09-01 见 CopyDirDeep 的说明: 这里必须【连子目录一起复制】
            SetProgress(15, "复制主服务器程序及依赖库...");
            // 复制 SwimmingScoreboard\ 根目录下所有文件（exe + 所有 dll）到 Server\
            string serverSrc = Path.Combine(sourceDir, "SwimmingScoreboard");
            string serverDst = Path.Combine(installDir, "Server");
            if (Directory.Exists(serverSrc))
                CopyDirDeep(serverSrc, serverDst);   // 2026-09-01 连子目录一起复制(x64\SQLite.Interop.dll 就在子目录里)

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

            SetProgress(60, "复制远程计时控制台及依赖库...");
            string remoteSrc = Path.Combine(sourceDir, "RemoteTimingControl");
            string remoteDst = Path.Combine(installDir, "RemoteControl");
            if (Directory.Exists(remoteSrc))
                CopyDirDeep(remoteSrc, remoteDst);   // 2026-09-01 连子目录一起复制(x64\SQLite.Interop.dll 就在子目录里)

            SetProgress(64, "复制远程显示控制台及依赖库...");
            string displaySrc = Path.Combine(sourceDir, "RemoteDisplayControl");
            string displayDst = Path.Combine(installDir, "RemoteDisplay");
            if (Directory.Exists(displaySrc))
                CopyDirDeep(displaySrc, displayDst);   // 2026-09-01 连子目录一起复制(x64\SQLite.Interop.dll 就在子目录里)

            SetProgress(67, "复制运动员报名工具及依赖库...");
            string regSrc = Path.Combine(sourceDir, "RegistrationTool");
            string regDst = Path.Combine(installDir, "Registration");
            if (Directory.Exists(regSrc))
                CopyDirDeep(regSrc, regDst);   // 2026-09-01 连子目录一起复制(x64\SQLite.Interop.dll 就在子目录里)

            SetProgress(70, "复制编排记录及成绩处理（ScheduleEditor）...");
            string editorSrc = Path.Combine(sourceDir, "ScheduleEditor");
            string editorDst = Path.Combine(installDir, "ScheduleEditor");
            if (Directory.Exists(editorSrc))
                CopyDirDeep(editorSrc, editorDst);   // 2026-09-01 连子目录一起复制(x64\SQLite.Interop.dll 就在子目录里)
            // ScheduleEditor 也带 Records 子目录（与主服务器共享纪录模板）
            string editorRecSrc = Path.Combine(sourceDir, "ScheduleEditor", "Records");
            if (Directory.Exists(editorRecSrc))
                foreach (var f in Directory.GetFiles(editorRecSrc))
                    File.Copy(f, Path.Combine(editorDst, "Records", Path.GetFileName(f)), true);

            SetProgress(75, "复制工具程序（计时模拟器/参数调试）...");
            string toolsDst = Path.Combine(installDir, "Tools");
            if (!Directory.Exists(toolsDst)) Directory.CreateDirectory(toolsDst);
            foreach (string toolName in new[] { "TimingSimulator.exe", "ParamDebugBot.exe" }) {
                string toolSrc = Path.Combine(sourceDir, toolName);
                if (File.Exists(toolSrc)) File.Copy(toolSrc, Path.Combine(toolsDst, toolName), true);
            }
            // 2026-08-26 安装包里的 Tools\ 整个搬过去(新版模拟计时器 TimingSimulatorGUI.exe
            // 和它的说明就在里面)。以后再加工具, 打包时丢进 Tools\ 即可, 这里不用再改。
            string toolsSrc = Path.Combine(sourceDir, "Tools");
            if (Directory.Exists(toolsSrc)) {
                foreach (string f in Directory.GetFiles(toolsSrc))
                    File.Copy(f, Path.Combine(toolsDst, Path.GetFileName(f)), true);
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (createDesktopShortcut) {
                SetProgress(80, "创建桌面快捷方式...");
                CreateShortcut(Path.Combine(desktop, "游泳赛事管理主服务器.lnk"),
                    Path.Combine(installDir, "Server", "SwimmingScoreboard.exe"),
                    Path.Combine(installDir, "Server"));
                CreateShortcut(Path.Combine(desktop, "远程计时控制台.lnk"),
                    Path.Combine(installDir, "RemoteControl", "RemoteTimingControl.exe"),
                    Path.Combine(installDir, "RemoteControl"));
                CreateShortcut(Path.Combine(desktop, "远程显示控制台.lnk"),
                    Path.Combine(installDir, "RemoteDisplay", "RemoteDisplayControl.exe"),
                    Path.Combine(installDir, "RemoteDisplay"));
                CreateShortcut(Path.Combine(desktop, "运动员报名工具.lnk"),
                    Path.Combine(installDir, "Registration", "RegistrationTool.exe"),
                    Path.Combine(installDir, "Registration"));
                CreateShortcut(Path.Combine(desktop, "编排记录及成绩处理.lnk"),
                    Path.Combine(installDir, "ScheduleEditor", "ScheduleEditor.exe"),
                    Path.Combine(installDir, "ScheduleEditor"));
            } else {
                SetProgress(80, "跳过桌面快捷方式（未勾选）...");
            }

            SetProgress(90, "创建开始菜单快捷方式...");
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "游泳赛事管理系统");
            if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, "游泳赛事管理主服务器.lnk"),
                Path.Combine(installDir, "Server", "SwimmingScoreboard.exe"),
                Path.Combine(installDir, "Server"));
            CreateShortcut(Path.Combine(startMenu, "远程计时控制台.lnk"),
                Path.Combine(installDir, "RemoteControl", "RemoteTimingControl.exe"),
                Path.Combine(installDir, "RemoteControl"));
            CreateShortcut(Path.Combine(startMenu, "远程显示控制台.lnk"),
                Path.Combine(installDir, "RemoteDisplay", "RemoteDisplayControl.exe"),
                Path.Combine(installDir, "RemoteDisplay"));
            CreateShortcut(Path.Combine(startMenu, "运动员报名工具.lnk"),
                Path.Combine(installDir, "Registration", "RegistrationTool.exe"),
                Path.Combine(installDir, "Registration"));
            CreateShortcut(Path.Combine(startMenu, "编排记录及成绩处理.lnk"),
                Path.Combine(installDir, "ScheduleEditor", "ScheduleEditor.exe"),
                Path.Combine(installDir, "ScheduleEditor"));

            // 复制使用说明书（PDF）
            string manualSrc = Path.Combine(sourceDir, "使用说明书.pdf");
            if (File.Exists(manualSrc)) File.Copy(manualSrc, Path.Combine(installDir, "使用说明书.pdf"), true);

            // 2026-09-14 现场速查卡（两页 A4）—— 说明书 40 页, 比赛当天没人翻得动,
            //   卡片只放"手上正在做的事", 建议打印贴在计时机旁边。
            string cardSrc = Path.Combine(sourceDir, "现场速查卡.pdf");
            if (File.Exists(cardSrc)) File.Copy(cardSrc, Path.Combine(installDir, "现场速查卡.pdf"), true);

            // 2026-09-01 装 VC++ 运行库。SQLite.Interop.dll 是原生 DLL, 依赖它;
            //   目标机没装就会报"无法加载 SQLite.Interop.dll: 找不到指定的模块"
            //   —— 那句话说的是【依赖】找不到, 不是 DLL 本身找不到, 很容易看错。
            //   后果是竞赛库从头到尾打不开: 成绩不入库、没有名次、组排名表不生成,
            //   而界面上看不出任何异常(现场就这么跑了一整天)。
            //   已装过的话它自己会跳过, 不会重复装。装不上也不拦着安装继续。
            SetProgress(92, "安装运行库 (VC++ x64)...");
            try {
                string vcSrc = Path.Combine(sourceDir, "prereq", "vc_redist.x64.exe");
                if (File.Exists(vcSrc)) {
                    var psi = new System.Diagnostics.ProcessStartInfo(vcSrc, "/install /quiet /norestart");
                    psi.UseShellExecute = true;
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p != null) p.WaitForExit(180000);   // 最多等 3 分钟
                }
            } catch { }

            // 2026-09-29 装 Microsoft Edge (若目标机没有)。程序里"文档预览/输出"的 PDF 导出
            //   (TryHtmlToPdf) 靠 headless Edge/Chrome 把 HTML 打成 PDF——现场有机器两样
            //   都没装(比如精简版 Windows 或企业策略卸载过)，PDF 导出直接失败, 弹"未找到
            //   Edge/Chrome, 请按 Ctrl+P 另存"这条不友好的兜底提示。跟 VC++ 运行库同一个
            //   道理: 静默装一次, 已经有 Edge 的机器会自己跳过, 装不上也不拦着安装继续。
            //   探测路径跟 MainWindow.xaml.cs 的 TryHtmlToPdf() 保持一致, 两边都要改的话
            //   记得一起改。
            SetProgress(93, "检查 Microsoft Edge...");
            try {
                string[] edgeCandidates = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    @"Microsoft\Edge\Application\msedge.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe")
                };
                bool edgeFound = false;
                foreach (var p in edgeCandidates) { if (File.Exists(p)) { edgeFound = true; break; } }
                // 2026-09-30 固定路径都没找到, 再查一次"App Paths"注册表——装在非标准路径的
                //   Edge(比如自定义盘符/单用户装)靠这个还能找到, 少装一次没必要的200MB。
                //   跟 MainWindow.xaml.cs 的 TryHtmlToPdf()/FindBrowserExeFromRegistry() 是
                //   同一件事的两处实现, 那边改了这边也要跟着改。
                if (!edgeFound && FindBrowserExeFromRegistry("msedge.exe") != null) edgeFound = true;
                if (!edgeFound) {
                    string edgeMsi = Path.Combine(sourceDir, "prereq", "MicrosoftEdgeEnterpriseX64.msi");
                    if (File.Exists(edgeMsi)) {
                        SetProgress(93, "安装 Microsoft Edge (约200MB, 请稍候)...");
                        var psi = new System.Diagnostics.ProcessStartInfo(edgeMsi, "/quiet /norestart");
                        psi.UseShellExecute = true;
                        var p = System.Diagnostics.Process.Start(psi);
                        if (p != null) p.WaitForExit(300000);   // 最多等 5 分钟(比 vc_redist 大得多)
                    }
                }
            } catch { }

            SetProgress(95, "创建卸载程序...");
            CreateUninstaller(desktop, startMenu);

            SetProgress(100, "安装完成！");
            UpdateDoneInfo();
            System.Threading.Thread.Sleep(500);
            ShowStep(3);
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装过程中出现错误:\n\n" + ex.Message, "安装错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowStep(1);
        }
    }

    /// <summary>
    /// 2026-09-01 连子目录一起复制。
    ///
    /// 原来用的是 Directory.GetFiles(dir) —— 【不递归】, 只拿顶层文件。
    /// 于是 x64\SQLite.Interop.dll 这种放在子目录里的原生 DLL 根本没被装到机器上:
    /// 安装包里明明有, 装完就没了, 运行时报"无法加载 SQLite.Interop.dll: 找不到指定的模块"。
    /// 后果是竞赛库全程打不开 —— 成绩不入库、没有名次、组排名不生成, 界面上还看不出来。
    /// (查了很久才发现: 我一直在验安装包里有没有, 从来没验过【装完之后】有没有。)
    /// </summary>
    static void CopyDirDeep(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDirDeep(d, Path.Combine(dst, Path.GetFileName(d)));
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

    // 2026-09-28 "完成"页内容改到装完之后再拼，好把快捷方式的真实结果（用户勾了没、
    // 有没有失败）写进去，不再是固定文案。
    void UpdateDoneInfo()
    {
        string shortcutSection;
        if (!createDesktopShortcut) {
            shortcutSection = "未创建桌面快捷方式（安装时未勾选）。程序位于：\n  " + installDir;
        } else if (shortcutFailures.Count == 0) {
            shortcutSection = "已创建桌面快捷方式：\n" +
                "  ★  游泳赛事管理主服务器\n" +
                "  ★  远程计时控制台\n" +
                "  ★  远程显示控制台\n" +
                "  ★  运动员报名工具\n" +
                "  ★  编排记录及成绩处理";
        } else {
            shortcutSection = "⚠ 部分桌面快捷方式创建失败（不影响程序本身，可到以下目录手动运行 .exe 或手动创建快捷方式）：\n" +
                "  " + string.Join("\n  ", shortcutFailures.ToArray()) + "\n" +
                "程序位于：" + installDir;
        }
        lblDoneInfo.Text =
            "游泳赛事管理与计时系统 " + InstallerVersion + " 已成功安装。\n\n" +
            shortcutSection + "\n\n" +
            // 2026-09-14 这里的端口写错过: 网页在 8080, 3002 是 WebSocket 端口。
            //   照着 3002 开网页是打不开的 —— 新装机的人第一步就会卡在这儿。
            "Web 客户端地址（主服务器启动后）：\n" +
            "  比赛控制  http://<server>:8080/race_control.html\n" +
            "  大屏显示  http://<server>:8080/display.html\n" +
            "  成绩查询  http://<server>:8080/query.html\n" +
            "  排名屏      http://<server>:8080/leaderboard.html\n" +
            "  在线报名  http://<server>:8080/register.html\n" +
            "  检录台      http://<server>:8080/checkin.html\n\n" +
            "使用说明书：" + installDir + "\\使用说明书.pdf\n" +
            "现场速查卡：" + installDir + "\\现场速查卡.pdf（两页，建议打印贴在计时机旁）";
    }

    // 2026-09-30 跟 SwimmingScoreboard/MainWindow.xaml.cs 的同名方法是同一件事的两处实现,
    // 那边改了这边也要跟着改(见那边的注释)。
    static string FindBrowserExeFromRegistry(string exeName)
    {
        try
        {
            string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName;
            using (var k = Registry.LocalMachine.OpenSubKey(subKey))
            {
                string v = k != null ? k.GetValue(null) as string : null;
                if (!string.IsNullOrEmpty(v) && File.Exists(v)) return v;
            }
            using (var k = Registry.CurrentUser.OpenSubKey(subKey))
            {
                string v = k != null ? k.GetValue(null) as string : null;
                if (!string.IsNullOrEmpty(v) && File.Exists(v)) return v;
            }
        }
        catch { }
        return null;
    }

    void CreateShortcut(string lnkPath, string target, string workDir)
    {
        try
        {
            // 2026-10-08 现场反馈: 装到一台"新电脑"(系统区域设置→管理→非Unicode程序的语言
            // 不是中文, 很多国行以外/精简版预装系统默认是英文)上, 全部 10 个快捷方式都报
            // "Unable to save shortcut "C:\Users\xxx\Desktop\??????????.lnk""——文件名里的
            // 中文字符在抛错信息里已经是问号, 说明在传进 WScript.Shell(wshom.ocx) 这个老牌
            // COM 自动化对象那一刻就已经按"非Unicode程序的语言"这个 ANSI 代码页损毁了, 跟
            // .NET 自己的编码无关——这是 WSH 这个组件内部沿用的历史遗留限制, 不是传了
            // BSTR(COM 自动化字符串本该是全 Unicode)就能绕开的。
            //
            // 换成直接用 Shell 原生的 IShellLinkW + IPersistFile 这两个 COM 接口自己创建
            // .lnk 文件——这是 shell32.dll 真正落地写 .lnk 的那两个接口, 全程 Unicode (LPWStr
            // marshal), 不经过 wshom.ocx 那层, 不受"非Unicode程序的语言"设置影响, 任何系统
            // 区域设置下中文文件名都能正常创建。
            Util.CreateShortcutNative(lnkPath, target, workDir);
        }
        catch (Exception ex)
        {
            // 2026-09-28 原来这里是空 catch{}——失败了界面上完全看不出来，用户装完发现
            // 桌面没图标，分不清是"没勾选"还是"装的时候出错了"。记下来，装完在"完成"页提示。
            shortcutFailures.Add(Path.GetFileNameWithoutExtension(lnkPath) + "：" + ex.Message);
        }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }
}

// 2026-10-08 原生 IShellLinkW + IPersistFile 创建 .lnk —— 见 SetupForm.CreateShortcut 的说明:
// WScript.Shell(wshom.ocx) 在"非Unicode程序的语言"不是中文的系统上会把中文文件名损毁成问号,
// 这两个接口是 shell32.dll 自己落地写 .lnk 文件用的, 全程走 LPWStr(Unicode), 不受那个设置影响。
[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass { }

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

internal static class Util
{
    public static void CreateShortcutNative(string lnkPath, string target, string workDir)
    {
        IShellLinkW link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(workDir ?? "");
            IPersistFile pf = (IPersistFile)link;
            pf.Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}
