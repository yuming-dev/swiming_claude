using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;

class UninstallForm : Form
{
    string installDir;
    ProgressBar progressBar;
    Label lblStatus;
    Panel panelConfirm, panelProgress, panelDone;

    public UninstallForm()
    {
        installDir = Path.GetDirectoryName(Application.ExecutablePath);
        Text = "游泳赛事管理与计时系统 - 卸载";
        Size = new Size(500, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        BuildConfirm();
        BuildProgress();
        BuildDone();
        ShowStep(0);
    }

    // ═══ 确认页 ═══
    void BuildConfirm()
    {
        panelConfirm = new Panel { Dock = DockStyle.Fill, Visible = false };

        // 顶部红色条
        var topBar = new Panel { Dock = DockStyle.Top, Height = 60 };
        topBar.Paint += (s, e) => {
            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, topBar.Width, topBar.Height),
                Color.FromArgb(239, 68, 68), Color.FromArgb(185, 28, 28), 90f))
                e.Graphics.FillRectangle(brush, 0, 0, topBar.Width, topBar.Height);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            using (var font = new Font("Segoe UI", 11, FontStyle.Bold))
                e.Graphics.DrawString("  \u26A0  ", font, Brushes.White, 10, 30, sf);
            using (var font = new Font("Microsoft YaHei", 15, FontStyle.Bold))
                e.Graphics.DrawString("卸载游泳赛事管理与计时系统", font, Brushes.White, 50, 30, sf);
        };
        panelConfirm.Controls.Add(topBar);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 10, 30, 0) };
        var desc = new Label {
            Text = "将从您的计算机中删除以下内容：\n\n" +
                   "  \u2022  游泳赛事管理主服务器\n" +
                   "  \u2022  远程计时控制台\n" +
                   "  \u2022  Web页面和纪录模板\n" +
                   "  \u2022  桌面和开始菜单快捷方式\n\n" +
                   "安装目录：" + installDir + "\n\n" +
                   "\u26A0 Database 目录中的赛事数据将被保留，不会删除。",
            Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(0, 10), Size = new Size(420, 180)
        };
        content.Controls.Add(desc);
        panelConfirm.Controls.Add(content);

        // 底部按钮栏
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(248, 250, 252) };
        bar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240)), 0, 0, bar.Width, 0);

        var btnCancel = MakeButton("取消", Color.FromArgb(241, 245, 249), Color.FromArgb(51, 65, 85));
        btnCancel.Location = new Point(bar.Width - 118, 12);
        btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        btnCancel.Click += (s, e) => Close();
        bar.Controls.Add(btnCancel);

        var btnOK = MakeButton("确认卸载", Color.FromArgb(239, 68, 68), Color.White);
        btnOK.Size = new Size(110, 32);
        btnOK.Location = new Point(bar.Width - 236, 12);
        btnOK.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        btnOK.Click += (s, e) => { ShowStep(1); DoUninstall(); };
        bar.Controls.Add(btnOK);

        panelConfirm.Controls.Add(bar);
        Controls.Add(panelConfirm);
    }

    // ═══ 进度页 ═══
    void BuildProgress()
    {
        panelProgress = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(40, 60, 40, 0) };
        var title = new Label { Text = "正在卸载...", Font = new Font("Microsoft YaHei", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59), AutoSize = true, Location = new Point(0, 0) };
        panelProgress.Controls.Add(title);

        lblStatus = new Label { Text = "准备中...", Font = new Font("Microsoft YaHei", 10),
            ForeColor = Color.FromArgb(100, 116, 139), Location = new Point(0, 50), AutoSize = true };
        panelProgress.Controls.Add(lblStatus);

        progressBar = new ProgressBar { Location = new Point(0, 85), Size = new Size(400, 24), Style = ProgressBarStyle.Continuous };
        panelProgress.Controls.Add(progressBar);
        Controls.Add(panelProgress);
    }

    // ═══ 完成页 ═══
    void BuildDone()
    {
        panelDone = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(40, 50, 40, 0) };
        var title = new Label { Text = "\u2714 卸载完成", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 197, 94), AutoSize = true, Location = new Point(0, 0) };
        panelDone.Controls.Add(title);

        var info = new Label {
            Text = "游泳赛事管理与计时系统已成功卸载。\n\n赛事数据保留在：\n" + Path.Combine(installDir, "Server", "Database"),
            Font = new Font("Microsoft YaHei", 10), ForeColor = Color.FromArgb(71, 85, 105),
            Location = new Point(0, 50), Size = new Size(400, 120)
        };
        panelDone.Controls.Add(info);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(248, 250, 252) };
        bar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240)), 0, 0, bar.Width, 0);
        var btnClose = MakeButton("完成", Color.FromArgb(59, 130, 246), Color.White);
        btnClose.Location = new Point(bar.Width - 118, 12);
        btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        btnClose.Click += (s, e) => {
            // 延迟自删除
            try {
                string bat = "@echo off\r\nping 127.0.0.1 -n 3 >nul\r\n";
                bat += "del /q \"" + Path.Combine(installDir, "Uninstall.exe") + "\"\r\n";
                bat += "del /q \"" + Path.Combine(installDir, "卸载.bat") + "\"\r\n";
                bat += "del /q \"%~f0\"\r\n";
                string tmpBat = Path.Combine(Path.GetTempPath(), "swim_uninstall_cleanup.bat");
                File.WriteAllText(tmpBat, bat, System.Text.Encoding.Default);
                Process.Start(new ProcessStartInfo { FileName = tmpBat, WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
            } catch { }
            Close();
        };
        bar.Controls.Add(btnClose);
        panelDone.Controls.Add(bar);
        Controls.Add(panelDone);
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

    void ShowStep(int step)
    {
        panelConfirm.Visible = (step == 0);
        panelProgress.Visible = (step == 1);
        panelDone.Visible = (step == 2);
    }

    void SetProgress(int pct, string msg)
    {
        if (InvokeRequired) { Invoke(new Action(() => SetProgress(pct, msg))); return; }
        progressBar.Value = pct;
        lblStatus.Text = msg;
        Application.DoEvents();
    }

    void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    void SafeDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    void DoUninstall()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "游泳赛事管理系统");

            SetProgress(10, "删除桌面快捷方式...");
            SafeDelete(Path.Combine(desktop, "游泳赛事管理主服务器.lnk"));
            SafeDelete(Path.Combine(desktop, "远程计时控制台.lnk"));

            SetProgress(20, "删除开始菜单快捷方式...");
            SafeDeleteDir(startMenu);

            SetProgress(35, "删除主服务器程序...");
            SafeDelete(Path.Combine(installDir, "Server", "SwimmingScoreboard.exe"));
            SafeDelete(Path.Combine(installDir, "Server", "Fleck.dll"));
            SafeDelete(Path.Combine(installDir, "Server", "Newtonsoft.Json.dll"));

            SetProgress(50, "删除Web页面...");
            SafeDeleteDir(Path.Combine(installDir, "Server", "Web"));

            SetProgress(60, "删除纪录模板...");
            SafeDeleteDir(Path.Combine(installDir, "Server", "Records"));

            SetProgress(70, "删除文档目录...");
            SafeDeleteDir(Path.Combine(installDir, "Server", "Documents"));

            SetProgress(80, "删除远程控制台...");
            SafeDeleteDir(Path.Combine(installDir, "RemoteControl"));

            SetProgress(90, "清理其他文件...");
            SafeDelete(Path.Combine(installDir, "使用说明书.pdf"));
            SafeDelete(Path.Combine(installDir, "install_info.txt"));
            SafeDelete(Path.Combine(installDir, "Uninstall.ps1"));

            SetProgress(100, "卸载完成！");
            System.Threading.Thread.Sleep(500);
            ShowStep(2);
        }
        catch (Exception ex)
        {
            MessageBox.Show("卸载过程中出现错误:\n\n" + ex.Message, "卸载错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new UninstallForm());
    }
}
