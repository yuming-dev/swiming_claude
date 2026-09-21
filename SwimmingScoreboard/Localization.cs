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

            // 2026-09-21 "比赛控制"页（第二阶段）——静态文字全部覆盖；PauseClockBtnText/
            // RaceStateLabel/RecordsHiddenBtnText/RemarkReactionBtnText/
            // QuickConnectSerialButtonText/DeviceTestButtonText 这几个会被 C# 代码在运行时
            // 按状态改写(比如"停表"↔"继续走表")，这里只翻译了它们的【初始值】，状态一变
            // 又会被改回中文——跟"设置"页 Str_Settings_NotConnected 是同一类已知限制。
            { "Str_RC_ScheduleNav",     new[] { "赛程导航", "Schedule" } },
            { "Str_RC_PrevHeat",        new[] { "◀ 上一组", "◀ Prev" } },
            { "Str_RC_NextHeat",        new[] { "下一组 ▶", "Next ▶" } },
            { "Str_RC_TabTimingCompare",new[] { "计时源对比", "Timing Sources" } },
            { "Str_RC_SplitLabel",      new[] { "分段:", "Split:" } },
            { "Str_RC_SplitFinish",     new[] { "终点", "Finish" } },
            { "Str_RC_TimingSourceHint",new[] { "点击泳道行查看计时源", "Click a lane row to see timing sources" } },
            { "Str_RC_TabRaceLog",      new[] { "比赛日志", "Race Log" } },
            { "Str_RC_LaneStatus",      new[] { "泳道实时状态", "Live Lane Status" } },
            { "Str_RC_SectionTitle",    new[] { "比赛控制", "Control" } },
            { "Str_RC_TimingSettings",  new[] { "⚙ 参数设置", "⚙ Settings" } },
            { "Str_RC_ClockReset",      new[] { "计时复位", "Clock Reset" } },
            { "Str_RC_Ready",           new[] { "准备就绪", "Ready" } },
            { "Str_RC_ManualStart",     new[] { "M.发令", "M. Start" } },
            { "Str_RC_PauseClock",      new[] { "停表", "Pause Clock" } },
            { "Str_RC_ConfirmResult",   new[] { "确认本组成绩", "Confirm Heat Result" } },
            { "Str_RC_UnlockResultTip", new[] {
                "把本组从「已完赛」解开, 好改成绩。要二次确认, 并记入审计日志。改完记得再点一次「确认本组成绩」",
                "Unlock this heat from \"Done\" so you can edit its result. Requires a second confirmation and is written to the audit log. Remember to press \"Confirm Heat Result\" again after editing." } },
            { "Str_RC_UnlockResult",    new[] { "解锁本组成绩", "Unlock Heat Result" } },
            { "Str_RC_PrintResult",     new[] { "打印成绩", "Print Result" } },
            { "Str_RC_PublishRanking",  new[] { "公布项目总排名 (存盘)", "Publish Ranking (Save)" } },
            { "Str_RC_LaneControls",    new[] { "泳道操作", "Lanes" } },
            { "Str_RC_LaneUnit",        new[] { "道", "Lane" } },
            { "Str_RC_TriTip",          new[] { "标记本道为 TRI (试游 / 不计成绩 / 不参与排名), 备注栏显示 'TRI'", "Mark this lane as TRI (trial swim / no result / not ranked); the remark column shows 'TRI'." } },
            { "Str_RC_CancelNote",      new[] { "取消备注", "Clear Note" } },
            { "Str_RC_BlindResult",     new[] { "盲表成绩", "Backup" } },
            { "Str_RC_LaneOpen",        new[] { "道次打开", "Open Lane" } },
            { "Str_RC_LaneClose",       new[] { "道次关闭", "Close Lane" } },
            { "Str_RC_OpenAll",         new[] { "全部打开", "Open All" } },
            { "Str_RC_CloseAll",        new[] { "全部关闭", "Close All" } },
            { "Str_RC_ManualTime",      new[] { "手动输入", "Manual" } },
            { "Str_RC_ManualSplit",     new[] { "手工补段", "Add Split" } },
            { "Str_RC_DisplayControl",  new[] { "显示控制", "Display Control" } },
            { "Str_RC_ShowStartList",   new[] { "出发表", "Starters" } },
            { "Str_RC_ShowLiveRace",    new[] { "比赛视图", "Live" } },
            { "Str_RC_ShowHeatResult",  new[] { "组成绩", "Result" } },
            { "Str_RC_ShowEventRanking",new[] { "总排名", "Ranking" } },
            { "Str_RC_ShowTeamStandings",new[] { "团体", "Team" } },
            { "Str_RC_ShowAwards",      new[] { "颁奖", "Awards" } },
            { "Str_RC_ShowReferees",    new[] { "裁判介绍", "Referees" } },
            { "Str_RC_ShowWelcome",     new[] { "欢迎", "Welcome" } },
            { "Str_RC_PublishResult",   new[] { "成绩发布", "Publish" } },
            { "Str_RC_ShowScheduleTip", new[] { "选择比赛场次(或全部), 大屏翻页显示该场次/全部比赛日程", "Choose a session (or all), the big screen paginates through that session's (or all) schedule." } },
            { "Str_RC_ShowSchedule",    new[] { "比赛日程", "Schedule" } },
            { "Str_RC_ShowMediaTip",    new[] { "选择图片或视频文件，发送到大屏播放", "Choose an image or video file to play on the big screen." } },
            { "Str_RC_ShowMedia",       new[] { "图片/视频", "Media" } },
            { "Str_RC_PlayPptTip",      new[] { "选择 PPT/PPTX 文件, 自动以幻灯片放映模式打开 (需安装 PowerPoint; 未装时用默认关联程序兜底)", "Choose a PPT/PPTX file, opens in slideshow mode automatically (requires PowerPoint; falls back to the default associated app if not installed)." } },
            { "Str_RC_PlayPpt",         new[] { "PPT 播放", "Play PPT" } },
            { "Str_RC_DisplayStyleTip", new[] { "底色 / 字号 / 9 处文字颜色和字体, 实时推送到所有大屏 display.html", "Background / font size / 9 text colors and fonts, pushed live to every display.html." } },
            { "Str_RC_DisplayStyle",    new[] { "大屏样式 (底色/字号/文字)", "Display Style" } },
            { "Str_RC_RecordsHiddenTip",new[] { "切换大屏右上角 WR/CR 纪录指示条的显示/隐藏", "Toggle the WR/CR records bar at the top-right of the big screen." } },
            { "Str_RC_RecordsShow",     new[] { "记录显示", "Records" } },
            { "Str_RC_RecordsHidden",   new[] { "记录已隐藏", "Hidden" } },
            { "Str_RC_RemarkReactionTip",new[] { "切换大屏 备注 栏是否显示 出发反应时 (短暂窗口期内). 关 = 不显, 开 = 显", "Toggle whether the big screen's remark column shows the reaction time briefly after the start. Off = hidden, On = shown." } },
            { "Str_RC_RemarkReaction",  new[] { "显反应时", "RT On" } },
            { "Str_RC_RemarkReactionOff",new[] { "不显反应时", "RT Off" } },
            { "Str_RC_SystemHardware",  new[] { "系统硬件", "System Hardware" } },
            { "Str_RC_NetworkConnectTip",new[] { "用上次保存的 TCP 主机/端口快速连接硬件计时器；详细配置请到 系统工作状态 → 硬件计时器连接", "Quick-connect the timing hardware using the last saved TCP host/port; for detailed setup go to System Status → Timing Hardware Connection." } },
            { "Str_RC_NetworkConnect",  new[] { "网络连接", "Connect" } },
            { "Str_RC_DeviceTestTip",   new[] { "所有触板/出发台/盲表都打开，硬件发来的任何数据都接收显示（用于联调）。再点一次退出测试。", "Opens all touchpads/starting blocks/backup watches; any data the hardware sends is received and shown (for integration testing). Click again to exit test mode." } },
            { "Str_RC_DeviceTest",      new[] { "设备测试", "Dev Test" } },
            { "Str_RC_DeviceTestExit",  new[] { "退出测试", "Exit Test" } },
            { "Str_RC_StateWaiting",    new[] { "等待", "Waiting" } },
            { "Str_RC_RaceLogHint",     new[] { "本组所选道次的实时事件 — 出发反应时 / 触板 / 盲表 (切道次可调出该道历史)", "Live events for the selected lane in this heat — start reaction time / touchpad / backup watch (switch lanes to pull up its history)" } },

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
