using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard
{
    /// <summary>
    /// 2026-09-21 用户要求"设置"页加一个中文/English切换按钮，按下后整个系统转换语言。
    ///
    /// 全系统 3~5 万处中文文本硬编码在 XAML 属性和 C# 字符串字面量里（AddLog/MessageBox
    /// 尤其密集），一次性全部改造工作量是以"周"计的大工程，不是加个按钮的事。这里先落地
    /// 第一阶段：翻译基础设施（本类）+ 顶层 7 个标签页标题 + "设置"页本身的文字，走
    /// {DynamicResource Str_xxx} —— WPF 的 DynamicResource 是活绑定，运行时改
    /// Application.Resources 里的值，界面立刻跟着变，不需要重启。
    ///
    /// 系统日志(AddLog)/弹窗(MessageBox)/绝大多数业务界面文字仍是硬编码中文——这些不走
    /// DynamicResource（C# 里当场拼好传给 AddLog 那一刻语言就定死了），要做到位需要把每一处
    /// 调用都改成 Loc.T("key") 查表，是后续阶段的事，本次先不动。
    ///
    /// 三个 exe（SwimmingScoreboard/RemoteTimingControl/ScheduleEditor）共用同一份
    /// MainWindow.xaml(.cs)，这个类也跟着链接进三个项目（见各 .csproj 里 AuthHelper.cs
    /// 那种链接方式），每个 exe 各自的登录成功入口调一次 LoadSaved()+Apply()。
    /// 语言选择存在本机 language.json（跟 credentials.json 同目录，不进版本库/不随包分发
    /// 覆盖客户已有设置——见 build_installer.ps1 的 excludePats，这个文件名也要加进去）。
    /// </summary>
    public static class Loc
    {
        public const string Zh = "zh";
        public const string En = "en";

        public static string CurrentLanguage { get; private set; } = Zh;

        private static string LanguagePath {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language.json"); }
        }

        // key -> (中文, English)
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]> {
            // 顶层标签页
            { "Str_Tab_SystemStatus", new[] { "系统工作状态", "System Status" } },
            { "Str_Tab_EventMgmt",    new[] { "赛事管理与报名", "Events & Registration" } },
            { "Str_Tab_RaceControl",  new[] { "比赛控制", "Race Control" } },
            { "Str_Tab_Results",      new[] { "成绩与排名", "Results & Rankings" } },
            { "Str_Tab_Docs",         new[] { "文档编辑/输出/打印", "Documents / Print" } },
            { "Str_Tab_SystemLog",    new[] { "系统日志与数据", "System Log & Data" } },
            { "Str_Tab_Settings",     new[] { "设置", "Settings" } },

            // "设置" 页：计时硬件连接
            { "Str_Settings_TimingHw",     new[] { "计时硬件连接", "Timing Hardware Connection" } },
            { "Str_Settings_SerialPort",   new[] { "串口:", "Serial Port:" } },
            { "Str_Settings_BaudRate",     new[] { "波特率:", "Baud Rate:" } },
            { "Str_Settings_ConnectSerial",new[] { "连接串口", "Connect Serial" } },
            { "Str_Settings_TcpAddr",      new[] { "TCP地址:", "TCP Address:" } },
            { "Str_Settings_ConnectTcp",   new[] { "连接TCP", "Connect TCP" } },
            { "Str_Settings_UdpRecvPort",  new[] { "UDP收端口:", "UDP Recv Port:" } },
            { "Str_Settings_ListenUdp",    new[] { "监听UDP", "Listen UDP" } },
            { "Str_Settings_UdpSend",      new[] { "UDP发送:", "UDP Send:" } },
            { "Str_Settings_Status",       new[] { "状态:", "Status:" } },
            { "Str_Settings_NotConnected", new[] { "未连接", "Not Connected" } },
            { "Str_Settings_Disconnect",   new[] { "断开", "Disconnect" } },

            // "设置" 页：裁判长权限
            { "Str_Settings_ChiefJudge",     new[] { "裁判长权限", "Chief Judge Permission" } },
            { "Str_Settings_ChiefJudgeDesc", new[] {
                "「成绩与排名」界面里「裁判长改成绩」按钮用的密码，跟系统账号密码是分开的两套——不改这里的话，任何知道系统账号密码的人都能直接改库，达不到「只给裁判长用」的目的。",
                "The password used by the \"Chief Judge Edit Result\" button on the Results screen is separate from the system login password — if you don't set it, anyone who knows the login password can edit results directly, defeating the point of restricting it to the chief judge." } },
            { "Str_Settings_ChiefJudgeBtn", new[] { "设置/修改 裁判长改成绩密码", "Set / Change Chief Judge Password" } },

            // "设置" 页：界面语言（新增）
            { "Str_Settings_Language",     new[] { "界面语言", "Interface Language" } },
            { "Str_Settings_LanguageDesc", new[] {
                "顶部标签页和本设置页会立即切换；系统日志、弹窗提示等大部分文字目前仍是中文，会在后续逐步补上英文。",
                "Tab labels and this Settings page switch immediately. Most system-log and dialog text is still Chinese for now and will get English coverage in later updates." } },
            { "Str_Settings_LanguageCurrent", new[] { "当前界面语言：中文", "Current interface language: English" } },
        };

        /// <summary>查当前语言下的文字；查不到就退回中文；中文也没有就返回 key 本身兜底。</summary>
        public static string T(string key) {
            string[] pair;
            if (!Table.TryGetValue(key, out pair)) return key;
            int idx = CurrentLanguage == En ? 1 : 0;
            return (idx < pair.Length && !string.IsNullOrEmpty(pair[idx])) ? pair[idx] : pair[0];
        }

        public static void LoadSaved() {
            try {
                if (File.Exists(LanguagePath)) {
                    var j = JObject.Parse(File.ReadAllText(LanguagePath, Encoding.UTF8));
                    string lang = j["Language"] != null ? j["Language"].ToString() : "";
                    if (lang == En) CurrentLanguage = En;
                }
            } catch { }
        }

        private static void Save() {
            try {
                var j = new JObject();
                j["Language"] = CurrentLanguage;
                File.WriteAllText(LanguagePath, j.ToString(Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));
            } catch { }
        }

        /// <summary>把当前语言下的每一条文字写进 Application.Resources —— DynamicResource 绑定会跟着活刷新。</summary>
        public static void Apply() {
            var app = Application.Current;
            if (app == null) return;
            foreach (var kv in Table) {
                app.Resources[kv.Key] = T(kv.Key);
            }
        }

        public static void Toggle() {
            CurrentLanguage = CurrentLanguage == En ? Zh : En;
            Save();
            Apply();
        }
    }
}
