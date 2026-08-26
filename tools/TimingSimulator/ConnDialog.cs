using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;

namespace TimingSimulator
{
    /// <summary>跟比赛控制计算机怎么连。</summary>
    public enum ConnMode
    {
        /// <summary>
        /// 本机监听, 等比赛控制计算机连过来 —— 【默认, 也是真实现场的接法】:
        /// 计时器(本模拟器)是服务端, 比赛控制计算机作为客户端来连它。
        /// </summary>
        TcpServer,
        /// <summary>本机主动去连比赛控制计算机(少数设备是这个方向)。</summary>
        TcpClient,
        /// <summary>UDP: 本机在端口上收, 同时往对方地址发。</summary>
        Udp
    }

    /// <summary>
    /// 连接设置对话框: 方式 + 地址 + 端口, 外加 连接/断开/关闭 三个按钮。
    /// 连接和断开都是【当场生效】的 —— 按下去就动网络, 不用关窗口。
    /// </summary>
    public class ConnDialog : Form
    {
        readonly ComboBox _mode = new ComboBox();
        readonly Label _hostLabel = new Label();
        readonly TextBox _host = new TextBox();
        readonly TextBox _port = new TextBox();
        readonly Label _tip = new Label();
        readonly Label _state = new Label();
        readonly string _realHost;      // 用户填的"对方地址", 切到服务端模式时先存着

        // 主程序是 DPI 感知的, 所以这里拿到的是【真实像素】: 150% 缩放时字会大 1.5 倍,
        // 而下面写的坐标是按 96dpi 设计的 —— 不折算就会把"连接方式"截成"连接方"。
        float _k = 1f;
        int Z(int v) { return (int)Math.Round(v * _k); }

        public ConnMode Mode { get { return (ConnMode)_mode.SelectedIndex; } }
        public string Host { get { return Mode == ConnMode.TcpServer ? _realHost : _host.Text.Trim(); } }
        public int Port { get { int v; return int.TryParse(_port.Text.Trim(), out v) ? v : 0; } }

        /// <summary>按下【连接】: 主窗口照这个设置重开网络。</summary>
        public Action<ConnMode, string, int> Connect;
        /// <summary>按下【断开】: 主窗口把网络全部停掉。</summary>
        public Action Disconnect;

        /// <summary>
        /// 本机的 IPv4 地址(可能不止一个: 有线/无线/虚拟网卡)。
        /// 服务端模式下操作员要照着这个填到比赛控制计算机上, 所以必须亮出来。
        /// </summary>
        public static string LocalIPv4Text()
        {
            try
            {
                var list = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                             && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork
                             && !IPAddress.IsLoopback(u.Address))
                    .Select(u => u.Address.ToString())
                    .Distinct()
                    .ToList();
                if (list.Count > 0) return string.Join(" / ", list.ToArray());
            }
            catch { }
            return "127.0.0.1";
        }

        public ConnDialog(ConnMode mode, string host, int port)
        {
            _realHost = host;
            Text = "连接比赛控制计算机";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) _k = g.DpiX / 96f;

            ClientSize = new Size(Z(470), Z(262));

            Controls.Add(new Label { Text = "连接方式", Left = Z(18), Top = Z(23), Width = Z(76), Height = Z(22) });
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Items.AddRange(new object[] {
                "TCP 服务端 —— 本机监听, 等对方连过来（默认）",
                "TCP 客户端 —— 本机主动去连对方",
                "UDP —— 本机在端口上收, 同时往对方发"
            });
            _mode.SetBounds(Z(100), Z(18), Z(350), Z(26));
            _mode.SelectedIndex = (int)mode;
            _mode.SelectedIndexChanged += delegate { RefreshTip(); };
            Controls.Add(_mode);

            _hostLabel.SetBounds(Z(18), Z(63), Z(76), Z(22));
            Controls.Add(_hostLabel);
            _host.SetBounds(Z(100), Z(58), Z(210), Z(26));
            _host.Text = host;
            Controls.Add(_host);

            Controls.Add(new Label { Text = "端口", Left = Z(322), Top = Z(63), Width = Z(42), Height = Z(22) });
            _port.SetBounds(Z(368), Z(58), Z(82), Z(26));
            _port.Text = port.ToString();
            Controls.Add(_port);

            _tip.SetBounds(Z(100), Z(94), Z(350), Z(58));
            _tip.ForeColor = Color.FromArgb(90, 90, 90);
            Controls.Add(_tip);

            Controls.Add(new Label { Text = "当前状态", Left = Z(18), Top = Z(164), Width = Z(76), Height = Z(22) });
            _state.SetBounds(Z(100), Z(164), Z(350), Z(22));
            _state.ForeColor = Color.FromArgb(180, 110, 0);
            _state.Text = "(未知)";
            Controls.Add(_state);

            var bConn = new Button { Text = "连 接" };
            bConn.SetBounds(Z(136), Z(208), Z(100), Z(34));
            bConn.Click += delegate {
                if (!Check()) return;
                if (Connect != null) Connect(Mode, Host, Port);
            };
            Controls.Add(bConn);

            var bDisc = new Button { Text = "断 开" };
            bDisc.SetBounds(Z(244), Z(208), Z(100), Z(34));
            bDisc.Click += delegate { if (Disconnect != null) Disconnect(); };
            Controls.Add(bDisc);

            var bClose = new Button { Text = "关 闭", DialogResult = DialogResult.Cancel };
            bClose.SetBounds(Z(352), Z(208), Z(100), Z(34));
            Controls.Add(bClose);

            AcceptButton = bConn; CancelButton = bClose;
            RefreshTip();
        }

        bool Check()
        {
            if (Port < 1 || Port > 65535)
            { MessageBox.Show(this, "端口要在 1~65535 之间。", Text); return false; }
            if (Mode != ConnMode.TcpServer && Host.Length == 0)
            { MessageBox.Show(this, "这种方式要填对方地址。", Text); return false; }
            return true;
        }

        /// <summary>主窗口把当前连接状态回填到这里。</summary>
        public void ShowState(string text, Color color)
        {
            if (IsDisposed) return;
            _state.Text = text; _state.ForeColor = color;
        }

        void RefreshTip()
        {
            if (Mode == ConnMode.TcpServer)
            {
                // 服务端模式下这一栏不是"填对方地址", 而是【告诉你本机地址是多少】——
                // 操作员要拿着它去比赛控制计算机上填。所以改标签、填本机 IP、置灰。
                _hostLabel.Text = "本机地址";
                _host.Text = LocalIPv4Text();
                _host.Enabled = false;
                _tip.Text =
                    "现场就是这么接的: 本模拟器(计时器)当服务端, 比赛控制计算机当客户端连过来。\n"
                  + "把上面的【本机地址 + 端口】填到比赛控制计算机的计时硬件设置里。\n"
                  + "注意要【先开模拟器再开比赛控制程序】—— 对方启动时只连一次, 不重试。";
            }
            else
            {
                _hostLabel.Text = "对方地址";
                if (!_host.Enabled) { _host.Text = _realHost; _host.Enabled = true; }
                _tip.Text = Mode == ConnMode.TcpClient
                    ? "本机主动去连对方。连不上会每 2 秒重试一次,\n对方晚开也没关系。"
                    : "本机在这个端口上收, 同时往【对方地址 + 同一端口】发。\nUDP 不建连接, 对方没开也照发不误。";
            }
        }
    }
}
