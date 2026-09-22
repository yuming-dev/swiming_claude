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

            // 2026-09-21 "赛事管理与报名"页（第三阶段）——7 个子页全部静态文字 + 右侧"运动员注册"面板。
            // 复用词（组别/性别/男女混合/姓名等在这页里反复出现几十次）统一走 Str_Common_*，
            // 避免每个子页各建一份、翻译口径还可能不一致；DataGrid 表头同理走 Str_Col_*。
            { "Str_Common_All",     new[] { "全部", "All" } },
            { "Str_Common_AgeGroup",new[] { "组别:", "Group:" } },
            { "Str_Common_Gender",  new[] { "性别:", "Sex:" } },
            { "Str_Common_Male",    new[] { "男", "M" } },
            { "Str_Common_Female",  new[] { "女", "F" } },
            { "Str_Common_Mixed",   new[] { "混合", "Mixed" } },
            { "Str_Common_Name",    new[] { "姓名:", "Name:" } },
            { "Str_Common_Reset",   new[] { "重置", "Reset" } },

            // DataGrid 表头（多个表格复用同一批词）
            { "Str_Col_Index",   new[] { "序号", "No." } },
            { "Str_Col_Bib",     new[] { "号码", "Bib" } },
            { "Str_Col_Name",    new[] { "姓名", "Name" } },
            { "Str_Col_Sex",     new[] { "性别", "Sex" } },
            { "Str_Col_DOB",     new[] { "出生日期", "DOB" } },
            { "Str_Col_Birthday",new[] { "生日", "DOB" } },
            { "Str_Col_Age",     new[] { "年龄", "Age" } },
            { "Str_Col_Group",   new[] { "组别", "Group" } },
            { "Str_Col_Team",    new[] { "代表队", "Team" } },
            { "Str_Col_Abbr",    new[] { "单位简称", "Abbr" } },
            { "Str_Col_IDNum",   new[] { "身份证号", "ID No." } },
            { "Str_Col_Phone",   new[] { "电话", "Phone" } },
            { "Str_Col_Event",   new[] { "项目", "Event" } },
            { "Str_Col_Leg",     new[] { "棒次", "Leg" } },
            { "Str_Col_BibNo",   new[] { "参赛号", "Bib No." } },
            { "Str_Col_Entry",   new[] { "报名成绩", "Entry" } },
            { "Str_Col_Stage",   new[] { "阶段", "Stage" } },
            { "Str_Col_Heat",    new[] { "组", "Ht" } },
            { "Str_Col_Lane",    new[] { "道", "Ln" } },
            { "Str_Col_Status",  new[] { "状态", "Status" } },
            { "Str_Col_Notes",   new[] { "备注", "Notes" } },
            { "Str_Col_Date",    new[] { "日期", "Date" } },
            { "Str_Col_Location",new[] { "地点", "Venue" } },
            { "Str_Col_RecordType",new[] { "类型", "Type" } },
            { "Str_Col_Holder",  new[] { "保持者", "Holder" } },
            { "Str_Col_RecordTime",new[] { "成绩", "Time" } },

            // 子页①：赛事概览
            { "Str_EM_Tab_Overview",   new[] { "赛事概览", "Overview" } },
            { "Str_EM_BasicInfo",      new[] { "赛事基本信息", "Event Info" } },
            { "Str_EM_EventName",      new[] { "赛事名称:", "Event Name:" } },
            { "Str_EM_CompMode",       new[] { "比赛模式:", "Format:" } },
            { "Str_EM_ModeDomestic",   new[] { "国内（中国游泳协会）", "Domestic (CSA)" } },
            { "Str_EM_ModeIntl",       new[] { "国际（FINA）", "International (FINA)" } },
            { "Str_EM_PoolLength",     new[] { "泳池长度:", "Pool Length:" } },
            { "Str_EM_Pool50",         new[] { "50米", "50m" } },
            { "Str_EM_Pool25",         new[] { "25米", "25m" } },
            { "Str_EM_LaneCount",      new[] { "泳道数量:", "Lanes:" } },
            { "Str_EM_Lanes10",        new[] { "10道", "10" } },
            { "Str_EM_Lanes8",         new[] { "8道", "8" } },
            { "Str_EM_Lanes6",         new[] { "6道", "6" } },
            { "Str_EM_StartDate",      new[] { "开始日期:", "Start Date:" } },
            { "Str_EM_EndDate",        new[] { "结束日期:", "End Date:" } },
            { "Str_EM_Location",       new[] { "比赛地点:", "Location:" } },
            { "Str_EM_Organizer",      new[] { "主办方:", "Organizer:" } },
            { "Str_EM_Host",           new[] { "承办方:", "Host:" } },
            { "Str_EM_TechDelegate",   new[] { "技术代表:", "Tech Delegate:" } },
            { "Str_EM_Arbiter",        new[] { "仲裁委员:", "Arbiter:" } },
            { "Str_EM_CommaSepTip",    new[] { "多人逗号分隔", "Comma-separated for multiple names" } },
            { "Str_EM_ChiefReferee",   new[] { "总裁判长:", "Chief Referee:" } },
            { "Str_EM_SaveEventInfo",  new[] { " 保存赛事信息", " Save Event Info" } },
            { "Str_EM_LoadEvent",      new[] { " 加载赛事", " Load Event" } },
            { "Str_EM_ExportResultsFile",   new[] { " 导出成绩到文件", " Export Results to File" } },
            { "Str_EM_ExportResultsFileTip",new[] { "没有网络时用: 把本机已确认的成绩导成一个文件, U盘拿到主服务器导入", "For use without a network: export this machine's confirmed results to a file, carry it to the main server on a USB drive to import." } },
            { "Str_EM_ImportResultsFile",   new[] { " 从文件导入成绩", " Import Results from File" } },
            { "Str_EM_ImportResultsFileTip",new[] { "导入计时端导出的成绩文件。重复导入是安全的", "Import a results file exported from a timing station. Safe to import the same file more than once." } },
            { "Str_EM_NewEvent",       new[] { " 新建赛事", " New Event" } },
            { "Str_EM_UnitManage",     new[] { "参赛单位管理", "Manage Teams" } },
            { "Str_EM_UnitManageTip",  new[] { "管理参赛单位元信息（领队 / 教练 / 队医 / 基础分 / 联系电话）", "Manage team info (delegation head / coach / doctor / base score / phone)." } },
            { "Str_EM_StaffManage",    new[] { "工作人员管理", "Manage Staff" } },
            { "Str_EM_StaffManageTip", new[] { "管理 5 组工作人员（主席团 / 组织委员会 / 工作机构 / 技术及仲裁 / 裁判员）", "Manage the 5 staff groups (presidium / organizing committee / working bodies / technical & arbitration / officials)." } },
            { "Str_EM_RegStats",       new[] { "报名统计", "Registration Stats" } },
            { "Str_EM_NoRegData",      new[] { "暂无报名数据", "No registration data yet" } },
            { "Str_EM_NoEventData",    new[] { "暂无项目数据", "No event data yet" } },

            // 子页②：运动员管理
            { "Str_EM_Tab_Swimmers",   new[] { "运动员管理", "Swimmers" } },
            { "Str_EM_AddSwimmer",     new[] { "添加运动员", "Add" } },
            { "Str_EM_EditSelected",   new[] { "修改选中", "Edit" } },
            { "Str_EM_DeleteSelected", new[] { "删除选中", "Delete" } },
            { "Str_EM_ImportCSV",      new[] { "导入CSV", "Import CSV" } },
            { "Str_EM_ImportCSVTip",   new[] { "从 CSV 导入运动员报名信息（支持 Excel/WPS 另存为 CSV）", "Import swimmer registrations from CSV (Excel/WPS \"Save As CSV\" works)." } },
            { "Str_EM_ExportCSV",      new[] { "导出CSV", "Export CSV" } },
            { "Str_EM_ExportCSVTip",   new[] { "将当前所有报名运动员的数据导出为 CSV", "Export all currently registered swimmers to CSV." } },
            { "Str_EM_ExportSwimmerTemplate",   new[] { "导出个人报名模板", "Export Template" } },
            { "Str_EM_ExportSwimmerTemplateTip",new[] { "导出空白个人报名 CSV 模板（带表头与示例行），用 Excel/WPS 编辑后可用'导入CSV'回灌（导入会严格校验表头与本模板一致）", "Export a blank individual-registration CSV template (with header row and sample row); edit it in Excel/WPS then re-import with \"Import CSV\" (import strictly checks the header matches this template)." } },
            { "Str_EM_ImportOtherSwimmer",   new[] { "导入(其他)运动员信息", "Import (Other Format)" } },
            { "Str_EM_ImportOtherSwimmerTip",new[] { "从'单项明细' Excel (.xlsx/.xls) 导入运动员; 按 姓名+证件号 (优先) 或 姓名+单位+项目+性别 反查现有运动员: 找到→更新元数据/分道, 找不到→新增. 没有'距离+姿式'的行跳过.", "Import swimmers from another vendor's \"event detail\" Excel (.xlsx/.xls); matches existing swimmers by name+ID (preferred) or name+team+event+sex — found → updates metadata/lane, not found → adds new. Rows without distance+stroke are skipped." } },
            { "Str_EM_BibRangeConfig",   new[] { "号码区间设置", "Bib Ranges" } },
            { "Str_EM_BibRangeConfigTip",new[] { "按代表队设置参赛号号码段（如 中国 001-050）。新注册的运动员将在所属代表队的号码段内自动取号。", "Set bib-number ranges per team (e.g. China 001-050). Newly registered swimmers auto-assign a bib within their team's range." } },
            { "Str_EM_FilterEvent",    new[] { "筛选项目:", "Event:" } },
            { "Str_EM_FilterBib",      new[] { "编号:", "Bib:" } },
            { "Str_EM_ResetFilterTip", new[] { "清空所有筛选条件", "Clear all filters" } },
            { "Str_EM_FilterNameTip",  new[] { "按姓名模糊匹配（支持任意子串）", "Fuzzy-matches name (any substring)." } },
            { "Str_EM_FilterBibTip",   new[] { "按参赛号模糊匹配", "Fuzzy-matches bib number." } },

            // 子页③：接力队管理
            { "Str_EM_Tab_Relay",      new[] { "接力队管理", "Relay Teams" } },
            { "Str_EM_AddRelay",       new[] { "添加接力队", "Add Team" } },
            { "Str_EM_EditRelay",      new[] { "✏️ 编辑队伍信息", "✏️ Edit Team" } },
            { "Str_EM_EditRelayTip",   new[] { "编辑当前选中接力队的 队名/单位/项目/性别/4棒姓名/报名成绩，含必填校验和二次确认", "Edit the selected team's name/club/event/sex/4 leg names/entry time; includes required-field checks and a confirm step." } },
            { "Str_EM_DeleteRelay",    new[] { "删除选中队伍", "Delete Team" } },
            { "Str_EM_SyncRelayHeat",  new[] { "同步分组信息", "Sync Heat Info" } },
            { "Str_EM_ImportRelayCSV", new[] { "导入CSV", "Import CSV" } },
            { "Str_EM_ImportRelayCSVTip", new[] { "导入接力队 CSV (表头必须与「导出接力报名模板」一致；格式错误的行跳过并汇总)", "Import relay teams from CSV (header must match \"Export Template\"; malformed rows are skipped and summarized)." } },
            { "Str_EM_ExportRelayCSV", new[] { "导出CSV", "Export CSV" } },
            { "Str_EM_ExportRelayCSVTip", new[] { "把当前所有接力队导出为 CSV", "Export all current relay teams to CSV." } },
            { "Str_EM_ExportRelayTemplate", new[] { "导出接力报名模板", "Export Template" } },
            { "Str_EM_ExportRelayTemplateTip", new[] { "导出空白模板（带表头与示例行），编辑后用「导入CSV」回灌", "Export a blank template (header + sample row); edit it then re-import with \"Import CSV\"." } },
            { "Str_EM_ExportRelayLegSheet",new[] { "导出棒次填报表", "Export Leg Sheet" } },
            { "Str_EM_ExportRelayLegSheetTip",new[] { "按当前接力队导出填报表: 场次/项次/组次/泳道/项目/性别/组别/代表队 都已填好, 只留 4 棒姓名空着", "Export a fill-in sheet for current relay teams: session/event/heat/lane/event/sex/group/team are pre-filled, only the 4 leg names are left blank." } },
            { "Str_EM_ImportRelayLegSheet",new[] { "读入棒次名单", "Import Leg Sheet" } },
            { "Str_EM_ImportRelayLegSheetTip",new[] { "读入填好的棒次填报表, 一键把 4 棒姓名灌进已有的接力队 (只填姓名, 不新建/删除队伍)", "Read a filled-in leg sheet and fill the 4 leg names into existing relay teams in one click (names only — doesn't create/delete teams)." } },
            { "Str_EM_RelayLegTitleEmpty", new[] { "棒次安排（请选中一支接力队）", "Leg Order (select a relay team)" } },
            { "Str_EM_RelayLegUp",     new[] { "上移棒次", "Move Up" } },
            { "Str_EM_RelayLegDown",   new[] { "下移棒次", "Move Down" } },
            { "Str_EM_RelayLegReplace",new[] { "更换队员", "Replace" } },
            { "Str_EM_RelayLegSave",   new[] { "保存修改", "Save" } },
            { "Str_EM_RelayHintTitle", new[] { "填写提示：", "Tips: " } },
            { "Str_EM_RelayHintLeg",   new[] { "棒次", "Leg" } },
            { "Str_EM_RelayHintLegNote",new[] { "（第1-4棒，固定顺序不可改）", " (leg 1-4, fixed order)" } },
            { "Str_EM_RelayHintName",  new[] { "｜ 姓名", " | Name" } },
            { "Str_EM_RelayHintNameNote",new[] { "（队员全名，与身份证一致）", " (full legal name, matches ID)" } },
            { "Str_EM_RelayHintID",    new[] { "｜ 身份证号", " | ID No." } },
            { "Str_EM_RelayHintIDNote",new[] { "（18位数字，检录时核对证件）", " (18 digits, checked at check-in)" } },
            { "Str_EM_RelayHintBib",   new[] { "｜ 参赛号", " | Bib No." } },
            { "Str_EM_RelayHintBibNote",new[] { "（报名时分配的参赛号码）", " (bib number assigned at registration)" } },

            // 子页④：赛程管理 + ⑤出场编排微调（两页共用的赛程导航/筛选按钮词）
            { "Str_EM_Tab_Schedule",   new[] { "赛程管理", "Schedule" } },
            { "Str_EM_Tab_LineupTune", new[] { "出场编排微调", "Lineup Tuning" } },
            { "Str_EM_ScheduleNav",    new[] { "🧭 赛程导航", "🧭 Schedule" } },
            { "Str_EM_NavSearchTip",   new[] { "输入项目/组别/赛次关键字,自动展开匹配分支并高亮", "Type an event/group/stage keyword to auto-expand and highlight matches." } },
            { "Str_EM_NavPending",     new[] { "未开始", "Pending" } },
            { "Str_EM_NavRunning",     new[] { "进行中", "Running" } },
            { "Str_EM_NavDone",        new[] { "已结束", "Done" } },
            { "Str_EM_NavCancelled",   new[] { "已取消", "Cancelled" } },
            { "Str_EM_WizardSchedule", new[] { "🪄 秩序册生成向导", "🪄 Program Wizard" } },
            { "Str_EM_WizardScheduleTip",new[] { "打开秩序册生成向导：按 World Aquatics + 中国泳协规则自动统计赛次 → 编排日程 → 输出秩序册（5 步导航，含一键全自动）", "Open the program-book wizard: auto-tally heats per World Aquatics + CSA rules → build schedule → output the program book (5-step wizard, one-click auto mode included)." } },
            { "Str_EM_EditSchedule",   new[] { "修改赛程", "Edit Schedule" } },
            { "Str_EM_AutoGenHeats",   new[] { "项目自动分组", "Auto Seed" } },
            { "Str_EM_AutoGenHeatsTip",new[] { "按报名成绩为所有项目自动分组（蛇形分道）；含预赛/半决赛/决赛各阶段", "Auto-seed every event by entry time (serpentine lane assignment); covers prelims/semis/finals." } },
            { "Str_EM_ManualHeatAssign",new[] { "手动分组", "Manual Seed" } },
            { "Str_EM_ManualHeatAssignTip",new[] { "按项目选人员 → 可一键均分到 N 组，或直接在表格里修改 组/道次", "Pick swimmers by event → split evenly into N heats in one click, or edit heat/lane directly in the table." } },
            { "Str_EM_SupplementHeats",new[] { "追加分组", "Add Heats" } },
            { "Str_EM_PrintSchedule",  new[] { "打印日程表", "Print Schedule" } },
            { "Str_EM_PrintScheduleTip",new[] { "生成 HTML 文档可直接打印", "Generates an HTML document ready to print." } },
            { "Str_EM_ImportSchedule", new[] { "导入日程表", "Import Schedule" } },
            { "Str_EM_ImportScheduleTip",new[] { "从 CSV 读取日程（Excel/WPS 可另存为 CSV UTF-8）", "Read schedule from CSV (Excel/WPS \"Save As CSV UTF-8\")." } },
            { "Str_EM_ExportSchedule", new[] { "导出日程表", "Export Schedule" } },
            { "Str_EM_ExportScheduleTip",new[] { "导出为 CSV（Excel/WPS 可直接打开）", "Export to CSV (opens directly in Excel/WPS)." } },
            { "Str_EM_DownloadScheduleTemplate",new[] { "下载日程表模板", "Download Template" } },
            { "Str_EM_ImportHeatXlsx", new[] { "导入分组表 (Excel)", "Import Heats (Excel)" } },
            { "Str_EM_ImportHeatXlsxTip",new[] { "从 Excel (.xlsx) 读取各项目各组的运动员分道，可在 WPS / Excel / Word 中编辑", "Read per-event, per-heat lane assignments from Excel (.xlsx); editable in WPS/Excel/Word." } },
            { "Str_EM_ExportHeatXlsx", new[] { "导出分组表 (Excel)", "Export Heats (Excel)" } },
            { "Str_EM_ExportHeatXlsxTip",new[] { "导出 Excel：含「分组明细」「分组表(网格)」「填写说明」3 个工作表", "Exports an Excel workbook with 3 sheets: \"Heat Detail\", \"Heat Grid\", \"Instructions\"." } },
            { "Str_EM_DownloadHeatXlsxTemplate",new[] { "下载分组表 Excel 模板", "Download Heats Template" } },
            { "Str_EM_ImportOtherSchedule",new[] { "导入(其他)日程表", "Import Schedule (Other)" } },
            { "Str_EM_ImportOtherScheduleTip",new[] { "读 9 列 .xlsx (场次/时间/编号/性别/组别/项目/赛次/人(队)数/组数), 追加到现有赛程", "Reads a 9-column .xlsx (session/time/no./sex/group/event/stage/entries/heats) and appends to the existing schedule." } },
            { "Str_EM_ImportOtherHeat",new[] { "导入(其他)分组表", "Import Heats (Other)" } },
            { "Str_EM_ImportOtherHeatTip",new[] { "读网格式 .xlsx (项目 block + 组号×道次 cell 含姓名/单位), 追加运动员到现有数据, 可多次导入累加", "Reads a grid-style .xlsx (event blocks, heat×lane cells with name/team) and appends swimmers to existing data; safe to import repeatedly." } },

            // 子页⑤：出场编排微调 专属
            { "Str_EM_LTGenderAll",    new[] { " 性别:", " Sex:" } },
            { "Str_EM_LTEventAll",     new[] { " 项目:", " Event:" } },
            { "Str_EM_LTStageAll",     new[] { " 赛次:", " Stage:" } },
            { "Str_EM_LTHeatAll",      new[] { " 组:", " Heat:" } },
            { "Str_EM_StagePrelim",    new[] { "预赛", "Prelim" } },
            { "Str_EM_StageSemi",      new[] { "半决赛", "Semi" } },
            { "Str_EM_StageFinal",     new[] { "决赛", "Final" } },
            { "Str_EM_RefreshPreview", new[] { "刷新显示", "Refresh" } },
            { "Str_EM_MoveUp",         new[] { "上移", "Up" } },
            { "Str_EM_MoveDown",       new[] { "下移", "Down" } },
            { "Str_EM_SwapLane",       new[] { "交换泳道", "Swap Lane" } },
            { "Str_EM_AddToHeat",      new[] { "增加到本组", "Add to Heat" } },
            { "Str_EM_RemoveFromHeat", new[] { "移出本组", "Remove" } },
            { "Str_EM_AddTempSwimmer", new[] { "临时加人", "Add Temp Swimmer" } },
            { "Str_EM_AddTempSwimmerTip",new[] { "在当前项目/赛次临时新增一名运动员，不自动重新编排，需用“增加到本组”手动放入组道", "Temporarily add a swimmer to the current event/stage; doesn't auto-reseed — use \"Add to Heat\" to place them manually." } },
            { "Str_EM_SaveHeatChanges",new[] { "保存修改", "Save" } },
            { "Str_EM_MergeHeats",     new[] { "并组 / 取消组", "Merge / Cancel Heat" } },
            { "Str_EM_MergeHeatsTip",  new[] { "把某一组整组并入另一组(或直接取消)。源组保留组次号并标记已取消; 目标组原有的人道次不动, 并过来的人填空道, 之后可用上移/下移/交换泳道人工调整。", "Merge an entire heat into another (or just cancel it). The source heat keeps its number, marked cancelled; the target heat's existing lanes are untouched, merged-in swimmers fill empty lanes, then adjust manually with Up/Down/Swap Lane." } },
            { "Str_EM_EditGroupTip",   new[] { "选择“全部”表示不按组别过滤", "Choose \"All\" to not filter by group." } },

            // 子页⑥：比赛参数设置管理
            { "Str_EM_Tab_Params",     new[] { "比赛参数设置管理", "Competition Settings" } },
            { "Str_EM_CompRule",       new[] { "比赛规则", "Competition Rules" } },
            { "Str_EM_RuleIntl",       new[] { "国际比赛 (FINA)", "International (FINA)" } },
            { "Str_EM_RuleDomestic",   new[] { "国内大赛 (中国游协)", "Domestic (CSA)" } },
            { "Str_EM_RuleU",          new[] { "U系列青少年游泳比赛", "U-Series Youth Meet" } },
            { "Str_EM_RuleUDesc",      new[] { "U系列: 允许 男女并项 / 跨年龄并项 / TRI 参赛 / 直接决赛多组 / 组内单一名次 / 总排名按 性别×组别 拆", "U-Series: allows mixed-sex events / cross-age events / TRI entries / multi-heat finals / single in-heat ranking / overall ranking split by sex × age group." } },
            { "Str_EM_EventGroupConfig",new[] { "比赛项目 / 组别配置（数据库持久化，支持手动编辑、导入、导出）", "Events / Age Groups (stored in the database; edit, import or export)" } },
            { "Str_EM_Events",         new[] { "比赛项目", "Events" } },
            { "Str_EM_EditEvents",     new[] { "比赛项目编辑", "Edit Events" } },
            { "Str_EM_ImportEvents",   new[] { "导入比赛项目表", "Import Events" } },
            { "Str_EM_ExportEvents",   new[] { "导出比赛项目表", "Export Events" } },
            { "Str_EM_DownloadEventsTemplate",new[] { "下载比赛项目模板", "Download Template" } },
            { "Str_EM_AgeGroups",      new[] { "组别", "Age Groups" } },
            { "Str_EM_EditAgeGroups",  new[] { "组别编辑", "Edit Groups" } },
            { "Str_EM_ImportAgeGroups",new[] { "导入组别表", "Import Groups" } },
            { "Str_EM_ExportAgeGroups",new[] { "导出组别表", "Export Groups" } },
            { "Str_EM_DownloadAgeGroupsTemplate",new[] { "下载组别模板", "Download Template" } },
            { "Str_EM_Genders",        new[] { "性别", "Genders" } },
            { "Str_EM_EditGenders",    new[] { "性别编辑", "Edit Genders" } },
            { "Str_EM_ImportGenders",  new[] { "导入性别表", "Import Genders" } },
            { "Str_EM_ExportGenders",  new[] { "导出性别表", "Export Genders" } },
            { "Str_EM_DownloadGendersTemplate",new[] { "下载性别模板", "Download Template" } },
            { "Str_EM_Stages",         new[] { "赛次", "Stages" } },
            { "Str_EM_EditStages",     new[] { "赛次编辑", "Edit Stages" } },
            { "Str_EM_ImportStages",   new[] { "导入赛次表", "Import Stages" } },
            { "Str_EM_ExportStages",   new[] { "导出赛次表", "Export Stages" } },
            { "Str_EM_DownloadStagesTemplate",new[] { "下载赛次模板", "Download Template" } },
            { "Str_EM_HeatCounts",     new[] { "组数", "Heat Counts" } },
            { "Str_EM_EditHeatCounts", new[] { "组数编辑", "Edit Heat Counts" } },
            { "Str_EM_ImportHeatCounts",new[] { "导入组数表", "Import Heat Counts" } },
            { "Str_EM_ExportHeatCounts",new[] { "导出组数表", "Export Heat Counts" } },
            { "Str_EM_DownloadHeatCountsTemplate",new[] { "下载组数模板", "Download Template" } },
            { "Str_EM_TeamScoring",    new[] { "团体计分 — 名次分", "Team Scoring — Points" } },
            { "Str_EM_TeamScoringDesc",new[] { "编辑各名次得分（个人 / 接力）、组别系数、取分人数与破纪录加分；保存后立即重算团体积分。", "Edit points per place (individual/relay), age-group multipliers, how many places count, and record-break bonus; team totals recalc immediately on save." } },
            { "Str_EM_ScoringConfig",  new[] { "名次分设置", "Scoring Setup" } },
            { "Str_EM_ScoringConfigTip",new[] { "弹出对话框：编辑各名次得分、接力倍率、组别系数、取分人数、破纪录加分", "Opens a dialog to edit points per place, relay multiplier, age-group multipliers, places counted, and record-break bonus." } },
            { "Str_EM_EventDuration",  new[] { "项目用时（分钟/组）", "Event Duration (min/heat)" } },
            { "Str_EM_EventDurationDesc",new[] { "一键生成秩序册排日程时用的每组用时；按距离设置（50→2、100→3、200→5、400→8、800→15、1500→25），单条可在 Tab 3 「分/组」列覆盖。", "Per-heat duration used by the one-click program scheduler, set by distance (50→2, 100→3, 200→5, 400→8, 800→15, 1500→25 min); a single event can override it in the \"min/heat\" column on step 3." } },
            { "Str_EM_DurationConfig", new[] { "项目用时设置", "Duration Setup" } },
            { "Str_EM_DurationConfigTip",new[] { "编辑每个距离的分钟/组、项目间空隙、兜底用时", "Edit minutes-per-heat by distance, gap between events, and the fallback duration." } },

            // 子页⑦：记录管理
            { "Str_EM_Tab_Records",    new[] { "记录管理", "Records" } },
            { "Str_EM_AddRecord",      new[] { "添加纪录", "Add Record" } },
            { "Str_EM_DeleteRecord",   new[] { "删除选中", "Delete" } },
            { "Str_EM_SaveRecords",    new[] { "保存纪录", "Save Records" } },
            { "Str_EM_SaveRecordsTip", new[] { "把表格里改过的纪录落盘, 并按条同步给主服务器/编排端(不推整包)", "Persist edited records to disk and sync them to the main server/editor stations one record at a time (no full-package push)." } },
            { "Str_EM_ImportDefaultRecords",new[] { "导入标准纪录", "Import Standard Records" } },
            { "Str_EM_ImportRecordsCSV",new[] { "导入CSV纪录", "Import CSV" } },
            { "Str_EM_DownloadRecordTemplate",new[] { "下载纪录模板", "Download Template" } },
            { "Str_EM_DisplayRecordLabel",new[] { "大屏显示记录:", "Big-Screen Record:" } },
            { "Str_EM_DisplayRecordSetting",new[] { "设置...", "Configure..." } },
            { "Str_EM_RecordQuery",    new[] { "查询：", "Search:" } },
            { "Str_EM_ResetShort",     new[] { "重置", "Reset" } },
            { "Str_Col_RecordType_World", new[] { "世界纪录", "World Record" } },
            { "Str_EM_RecordKeywordTip",new[] { "输入保持者姓名或代表队搜索", "Search by holder name or team." } },

            // 右侧：运动员注册 面板
            { "Str_EM_RegPanelTitle",  new[] { "运动员注册", "Swimmer Registration" } },
            { "Str_EM_RegRequiredNote",new[] { "(带 ", "(fields with " } },
            { "Str_EM_RegRequiredNote2",new[] { " 为必填)", " are required)" } },
            { "Str_EM_RegName",        new[] { "姓名:", "Name:" } },
            { "Str_EM_RegGender",      new[] { "性别:", "Sex:" } },
            { "Str_EM_RegGroup",       new[] { "组别:", "Group:" } },
            { "Str_EM_RegGroupTip",    new[] { "报名时直接选择甲组/乙组等", "Pick the age group (e.g. Group A/B) at registration." } },
            { "Str_EM_RegBirthDate",   new[] { "出生日期:", "Birth Date:" } },
            { "Str_EM_RegBirthDateTip",new[] { "格式：yyyy-MM-dd（4 位年份）", "Format: yyyy-MM-dd (4-digit year)." } },
            { "Str_EM_RegIDNumber",    new[] { "身份证号:", "ID Number:" } },
            { "Str_EM_RegCountry",     new[] { "代表队:", "Team:" } },
            { "Str_EM_RegCountryTip",  new[] { "从「参赛单位管理」下拉选；也可直接输入新单位名（提交时如果不存在会自动添加到单位列表）", "Pick from \"Manage Teams\"; or type a new team name (it's auto-added to the team list on submit if it doesn't exist)." } },
            { "Str_EM_RegCountryShort",new[] { "单位简称:", "Team Abbr:" } },
            { "Str_EM_RegCountryShortTip",new[] { "大屏/成绩表显示时使用的简短单位名，如 绵阳蓝鲸 → 蓝鲸", "Short team name shown on the big screen/result sheets, e.g. \"Mianyang Blue Whale\" → \"Blue Whale\"." } },
            { "Str_EM_RegPhone",       new[] { "联系电话:", "Phone:" } },
            { "Str_EM_RegCSA",         new[] { "协会注册号:", "CSA Reg. No.:" } },
            { "Str_EM_RegNotes",       new[] { "备注:", "Notes:" } },
            { "Str_EM_RegEvents",      new[] { "参赛项目", "Events" } },
            { "Str_EM_RegEventLabel",  new[] { "项目:", "Event:" } },
            { "Str_EM_RegEntryTime",   new[] { "报名成绩:", "Entry Time:" } },
            { "Str_EM_RegEntryTimeTip",new[] { "如 49.50 或 1:23.45，可留空", "e.g. 49.50 or 1:23.45; may be left blank." } },
            { "Str_EM_RegAddEvent",    new[] { "+ 添加参赛项目", "+ Add Event" } },
            { "Str_EM_RegRemoveEvent", new[] { "X 删除此项", "X Remove" } },
            { "Str_EM_RegSubmitAll",   new[] { "提交全部报名信息", "Submit Registration" } },

            // 2026-09-21 "成绩与排名"页（第四阶段）——性别/阶段/组次下拉框的 ComboBoxItem
            // 不翻译：UpdateResultHeatCombo/RefreshResultGrid 等一大串地方直接拿
            // ((ComboBoxItem)xxx.SelectedItem).Content.ToString() 去跟"男"/"女"/"混合"/
            // "预赛"/"半决赛"/"决赛"/"全部"/"第N组"做字符串比较，翻译了会破坏筛选逻辑——
            // 跟 384d37d 里"赛事管理与报名"页踩过的坑同一类。这里只翻译旁边的静态标签。
            { "Str_Results_NavTitle",   new[] { "🧭 赛程导航", "🧭 Schedule" } },
            { "Str_Results_NavSearchTip", new[] { "输入项目/组别/赛次关键字,自动展开匹配分支并高亮", "Type an event/age-group/stage keyword to auto-expand and highlight matches" } },
            { "Str_Results_FilterAll",  new[] { "全部", "All" } },
            { "Str_Results_FilterPending", new[] { "未开始", "Pending" } },
            { "Str_Results_FilterRunning", new[] { "进行中", "Running" } },
            { "Str_Results_FilterDone", new[] { "已结束", "Done" } },
            { "Str_Results_FilterCancelled", new[] { "已取消", "Cancelled" } },
            { "Str_Results_AgeGroup",   new[] { "组别:", "Group:" } },
            { "Str_Results_AgeGroupTip",new[] { "选择\"全部\"表示不按组别过滤", "Choose \"All\" to skip filtering by age group" } },
            { "Str_Results_Gender",     new[] { "性别:", "Sex:" } },
            { "Str_Results_Event",      new[] { " 项目:", " Event:" } },
            { "Str_Results_Stage",      new[] { " 阶段:", " Stage:" } },
            { "Str_Results_Heat",       new[] { " 组:", " Heat:" } },
            { "Str_Results_Promotion",  new[] { "晋级处理", "Promotion" } },
            { "Str_Results_TeamScore",  new[] { "团体计分", "Team Score" } },
            { "Str_Results_ViewRawData",new[] { "查询原始数据", "View Raw Data" } },
            { "Str_Results_ChiefJudgeOverride", new[] { "🔒 裁判长改成绩", "🔒 Chief Judge Override" } },
            { "Str_Results_ChiefJudgeOverrideTip", new[] {
                "最高权限：需输入系统密码。直接改竞赛库里选中这一组的成绩，跳过「解锁本组成绩」的状态检查——只在正常解锁流程走不通时(如几周前的历史项目)用",
                "Highest privilege: requires the system password. Edits this heat's result directly in the competition database, bypassing the \"Unlock Heat Result\" status check — use only when the normal unlock flow doesn't work (e.g. an event from weeks ago)." } },
            { "Str_Results_FinalsStatus", new[] { "📋 决赛状态", "📋 Finals Status" } },
            { "Str_Results_FinalsStatusTip", new[] { "决赛项目状态总览", "Overview of finals event status" } },
            { "Str_Results_EventTop8",  new[] { "🏆 各项前 8 名", "🏆 Top 8" } },
            { "Str_Results_EventTop8Tip", new[] { "项目成绩统计 — 各项目第 1-8 名", "Event result stats — places 1-8 for each event" } },
            { "Str_Results_DeptBulletin", new[] { "🏢 部门公告", "🏢 Dept Bulletin" } },
            { "Str_Results_DeptBulletinTip", new[] { "各部门成绩公告 (HTML 输出)", "Per-department result bulletin (HTML output)" } },
            { "Str_Results_IndividualRanking", new[] { "🥇 个人总分排名", "🥇 Individual Ranking" } },
            { "Str_Results_IndividualRankingTip", new[] { "运动员个人总分排名 (Top N)", "Individual overall score ranking (Top N)" } },
            { "Str_Results_ColRank",    new[] { "名次", "Rank" } },
            { "Str_Results_ColLane",    new[] { "道", "Lane" } },
            { "Str_Results_ColName",    new[] { "姓名", "Name" } },
            { "Str_Results_ColCountry", new[] { "代表队", "Team" } },
            { "Str_Results_ColBib",     new[] { "号码", "Bib" } },
            { "Str_Results_ColAgeGroup",new[] { "组别", "Group" } },
            { "Str_Results_ColHeatText",new[] { "组数", "Heat" } },
            { "Str_Results_ColFinalTime",new[] { "最终成绩", "Final Time" } },
            { "Str_Results_ColDiff",    new[] { "成绩差", "Diff" } },
            { "Str_Results_ColSource",  new[] { "计时源", "Source" } },
            { "Str_Results_ColReaction",new[] { "反应时间", "Reaction" } },
            { "Str_Results_ColStatus",  new[] { "状态", "Status" } },
            { "Str_Results_ColRecord",  new[] { "纪录", "Record" } },

            // 2026-09-21 组次状态标签(赛程导航树节点上的 [已确认] 这类方括号标签)——
            // 见 MainWindow.xaml.cs 的 HeatStatusText()。这几个标签是拼进 TreeViewItem.Header
            // 字符串的静态文本, 不是 DynamicResource 活绑定, 树建好之后不会自己变——
            // LanguageToggle_Click 里补了一次 BuildScheduleTree() 强制重建, 才能让已经
            // 画出来的树跟着语言切换立刻变。
            { "Str_HeatStatus_Pending",   new[] { "未开始", "Not Started" } },
            { "Str_HeatStatus_CheckedIn", new[] { "已检录", "Checked In" } },
            { "Str_HeatStatus_Running",   new[] { "进行中", "Running" } },
            { "Str_HeatStatus_Done",      new[] { "已完赛", "Done" } },
            { "Str_HeatStatus_Confirmed", new[] { "已确认", "Confirmed" } },
            { "Str_HeatStatus_Cancelled", new[] { "已取消", "Cancelled" } },

            // 2026-09-21 【中文/English 第五阶段】四块纯 C# 运行时拼出来的动态面板之一：
            // 赛事概览的报名统计(RefreshOverviewStats)。跟静态 XAML 不一样，这些字符串
            // 每次数据变化/语言切换都要重新跑一遍这个函数才会重新生成，见
            // LanguageToggle_Click 里新增的 RefreshOverviewStats() 调用。
            { "Str_EM_RegStats_Teams",   new[] { "代表队", "Teams" } },
            { "Str_EM_RegStats_Entries", new[] { "总人次", "Entries" } },
            { "Str_EM_RegStats_Male",    new[] { "男", "Male" } },
            { "Str_EM_RegStats_Female",  new[] { "女", "Female" } },
            { "Str_EM_RegStats_Mixed",   new[] { "混合", "Mixed" } },
            { "Str_EM_RegStats_EventCount", new[] { "项目数", "Events" } },
            { "Str_EM_RegStats_ColIndex",new[] { "#", "#" } },
            { "Str_EM_RegStats_ColEventName", new[] { "项目名称", "Event" } },
            { "Str_EM_RegStats_ColTotal",new[] { "合计", "Total" } },
            { "Str_EM_RegStats_Subtotal",new[] { "小计 ({0} 项)", "Subtotal ({0} events)" } },
            { "Str_EM_RegStats_Personal",new[] { "个人项目", "Individual Events" } },
            { "Str_EM_RegStats_Relay",   new[] { "接力项目", "Relay Events" } },

            // 2026-09-21 第二块动态面板：出场编排微调的"全部组"总览(RefreshEditPreview 里
            // EditAllGroupsPanel 那一段, 单组/全部组两种模式共用同一套列头拼法)。
            // 赛次名("预赛"/"半决赛"/"决赛")本身仍是数据哨兵、不翻译(见前几阶段的说明)，
            // 这里只在"显示用"的场合另外查一张纯展示用的赛次名表(StageDisplay)，
            // 不影响 stage/prevStage 变量本身参与的任何比较逻辑。
            { "Str_Stage_Prelim", new[] { "预赛", "Prelim" } },
            { "Str_Stage_Semi",   new[] { "半决赛", "Semifinal" } },
            { "Str_Stage_Final",  new[] { "决赛", "Final" } },
            { "Str_EM_LT_SeedEntry",  new[] { "报名成绩", "Entry Time" } },
            { "Str_EM_LT_SeedSuffixFmt", new[] { "{0}成绩", "{0} Time" } },
            { "Str_EM_ColGender", new[] { "性别", "Sex" } },
            { "Str_EM_LT_GroupHeaderWithAge", new[] { "{0}  第{1}组（{2}人）", "{0}  Heat {1} ({2} swimmers)" } },
            { "Str_EM_LT_GroupHeader",        new[] { "第{0}组（{1}人）", "Heat {0} ({1} swimmers)" } },
            { "Str_EM_LT_NoGroupData", new[] { "暂无分组数据", "No lineup data" } },

            // 2026-09-21 第三块动态面板：接力队管理的分组视图(RebuildRelayGroupedView)。
            { "Str_EM_Relay_NoTeams",  new[] { "暂无接力队。请通过测试机器人或手动添加接力队。", "No relay teams yet. Add one manually, or via the test bot." } },
            { "Str_EM_Relay_GroupTitleFmt", new[] { "{0}{1} {2}（{3}队）", "{0}{1} {2} ({3} teams)" } },
            { "Str_EM_Relay_ColTeamName", new[] { "队名", "Team Name" } },
            { "Str_EM_Relay_ColLegs",   new[] { "棒次", "Legs" } },
            { "Str_EM_Relay_ColStage",  new[] { "阶段", "Stage" } },
            { "Str_EM_Relay_ColHeat",   new[] { "组", "Heat" } },
            { "Str_EM_Relay_AgeCategoryPendingTip", new[] {
                "⚠ 组别待确认 — 部分队员生日缺失，请在棒次详情或赛程管理窗口补录",
                "⚠ Age group pending — some swimmers are missing a birth date; fill it in via Leg Details or Schedule Management." } },
            { "Str_EM_Relay_LegTitleFmt", new[] { "{0} — {1} 棒次安排（{2}人）", "{0} — {1} Leg Order ({2} swimmers)" } },

            // 2026-09-21 第四块动态面板：赛程管理的分组视图(RebuildScheduleGroupedView)。
            // 注意：场次标题(SessionName, "第1场（日期 上午）"这种)不在这次翻译范围内——
            // 它不是纯 UI 展示字符串, 是写回 ScheduleItem.SessionName 这个数据字段的,
            // 会被存盘/跨机同步；"上午/下午/晚上"这几个值本身在 sessionRank() 那处也是
            // 排序用的数据哨兵。翻译了会让不同语言的机器互相覆盖出中英混杂的场次名，
            // 属于跟 384d37d 同一类"数据字段不能因为本机语言设置就变"的坑，这次只翻译
            // 这块面板里纯展示、不进数据模型的部分(列头、空状态提示)。
            { "Str_EM_Sched_ColSeq",  new[] { "顺序号", "Seq" } },
            { "Str_EM_Sched_ColTime", new[] { "时间", "Time" } },
            { "Str_EM_Sched_ColEvent",new[] { "项目", "Event" } },
            { "Str_EM_Sched_ColParticipants", new[] { "人(队)数", "Entries" } },
            { "Str_EM_Sched_NoSchedule", new[] {
                "暂无赛程。请点击\"一键生成日程\"或\"添加赛程项\"。",
                "No schedule yet. Click \"Generate Schedule\" or \"Add Schedule Item\"." } },

            // 2026-09-21 【中文/English 第六阶段】"文档编辑/输出/打印"页
            { "Str_Docs_PageTitle", new[] { "文档编辑 / 输出 / 打印", "Document Edit / Output / Print" } },
            { "Str_Docs_Intro", new[] {
                "所有文档支持\"编辑\"（自定义内容）与\"输出\"（在弹出窗口中预览，并可导出为 PDF / DOC / HTML 或直接打印）。",
                "Every document supports \"Edit\" (customize content) and \"Output\" (preview in a popup window, then export as PDF / DOC / HTML or print directly)." } },
            { "Str_Docs_Section1", new[] { "一、赛前文档", "1. Pre-Event Documents" } },
            { "Str_Docs_Section2", new[] { "二、比赛成绩", "2. Race Results" } },
            { "Str_Docs_Section3", new[] { "三、排名与纪录", "3. Rankings & Records" } },
            { "Str_Docs_Section4", new[] { "四、证书与奖状", "4. Certificates & Awards" } },
            // 各卡片通用按钮文字(多张卡复用同一份文案)
            { "Str_Docs_BtnEdit",        new[] { "编辑", "Edit" } },
            { "Str_Docs_BtnPrint",       new[] { "输出 / 打印", "Output / Print" } },
            { "Str_Docs_BtnExportExcel", new[] { "导出 Excel", "Export Excel" } },
            { "Str_Docs_BtnExport",      new[] { "输出", "Output" } },
            // 卡片: 秩序册
            { "Str_Docs_ProgramBook_Title", new[] { "秩序册", "Program Book" } },
            { "Str_Docs_ProgramBook_Desc",  new[] { "封面/前言/规程/活动日程/技术官员/运动队人员等。", "Cover / foreword / rules / schedule / officials / team rosters, etc." } },
            // 卡片: 竞赛日程
            { "Str_Docs_Schedule_Title", new[] { "竞赛日程", "Competition Schedule" } },
            { "Str_Docs_Schedule_Desc",  new[] { "按场次列出比赛日期/时间/项目编号/性别/组别/项目/赛次/人(队)数/组数。", "Lists date/time/event no./sex/age group/event/stage/entries/heat count by session." } },
            // 卡片: 分组表
            { "Str_Docs_HeatAssign_Title", new[] { "分组表", "Heat Assignments" } },
            { "Str_Docs_HeatAssign_Desc",  new[] { "按项目列出 组号×道次 的运动员姓名+代表队 (网格式, 参照秩序单 .xls)。", "Grid of swimmer name + team by heat number × lane, per event (matches the .xls program sheet layout)." } },
            // 卡片: 出发表
            { "Str_Docs_StartList_Title", new[] { "出发表", "Start List" } },
            { "Str_Docs_StartList_Desc",  new[] { "按赛程顺序、每组出场名单（道次 / 号码 / 姓名 / 代表队 / 备注）。", "Heat-by-heat start list in schedule order (lane / bib / name / team / notes)." } },
            // 卡片: 成绩册
            { "Str_Docs_ResultBook_Title", new[] { "成绩册", "Result Book" } },
            { "Str_Docs_ResultBook_Desc",  new[] { "封面/奖牌榜/体育道德风尚奖/破纪录/名次公告/成绩公告。", "Cover / medal table / sportsmanship award / broken records / placement & result bulletins." } },
            // 卡片: 项目成绩
            { "Str_Docs_EventResults_Title", new[] { "项目成绩", "Event Results" } },
            { "Str_Docs_EventResults_Desc",  new[] { "按项目汇总（决赛排名总表）。", "Summarized by event (final ranking table)." } },
            // 卡片: 按组别批量公布
            { "Str_Docs_BatchByAgeGroup_Title", new[] { "按组别批量公布", "Batch Publish by Age Group" } },
            { "Str_Docs_BatchByAgeGroup_Desc",  new[] { "选项目+性别+赛次, 一键生成该项目下各组别成绩单 (整体文档含每组分页)。", "Pick event + sex + stage, generate result sheets for every age group in that event in one go (one document, one page per group)." } },
            // 卡片: 分段计时报告
            { "Str_Docs_SplitReport_Title", new[] { "分段计时报告", "Split Time Report" } },
            { "Str_Docs_SplitReport_Desc",  new[] { "每 50m / 100m 分段时间，含触板与盲表。", "Split times every 50m / 100m, including touchpad and backup watch." } },
            // 卡片: 成绩 txt 输出
            { "Str_Docs_ResultTxt_Title", new[] { "成绩 txt 输出", "Result TXT Export" } },
            { "Str_Docs_ResultTxt_Desc",  new[] { "选项目 → 场号-项号-组号.txt 文本输出 (10 道 / 8 段累计 / 4 棒反应时)。", "Pick an event → exports session-event-heat.txt (10 lanes / 8 cumulative splits / 4-leg reaction times)." } },
            // 卡片: 团体成绩
            { "Str_Docs_TeamStandings_Title", new[] { "团体成绩", "Team Standings" } },
            { "Str_Docs_TeamStandings_Desc",  new[] { "代表队总分排名（个人分/接力分/破纪录加分/金银铜）。", "Team overall score ranking (individual + relay points, record bonus, gold/silver/bronze)." } },
            // 卡片: 纪录报告
            { "Str_Docs_RecordReport_Title", new[] { "纪录报告", "Record Report" } },
            { "Str_Docs_RecordReport_Desc",  new[] { "本届破纪录情况一览（项目 / 类型 / 保持者 / 成绩）。", "Overview of records broken this meet (event / type / holder / result)." } },
            // 卡片: 奖状
            { "Str_Docs_AwardCert_Title", new[] { "奖状", "Award Certificate" } },
            { "Str_Docs_AwardCert_Desc",  new[] { "冠/亚/季军证书（自动填充姓名 / 项目 / 名次 / 成绩）。", "1st/2nd/3rd place certificates (name / event / place / result auto-filled)." } },
            // 卡片: 纪录证书
            { "Str_Docs_RecordCert_Title", new[] { "纪录证书", "Record Certificate" } },
            { "Str_Docs_RecordCert_Desc",  new[] { "破纪录证书（自动填充破纪录运动员 / 类型 / 成绩）。", "Record-breaking certificate (swimmer / record type / result auto-filled)." } },
            // 帮助提示框
            { "Str_Docs_FormatNoteTitle", new[] { "输出格式说明", "Output Format Notes" } },
            { "Str_Docs_FormatNoteIntro", new[] { "所有\"输出 / 打印\"按钮会弹出预览窗口，可在窗口内：", "Every \"Output / Print\" button opens a preview window, where you can:" } },
            { "Str_Docs_FormatPdfLabel",  new[] { "导出 PDF", "Export PDF" } },
            { "Str_Docs_FormatPdfDesc",   new[] { " — 在浏览器中按 Ctrl+P 选择 \"Microsoft Print to PDF\"", " — in the browser, press Ctrl+P and choose \"Microsoft Print to PDF\"" } },
            { "Str_Docs_FormatDocLabel",  new[] { "导出 DOC", "Export DOC" } },
            { "Str_Docs_FormatDocDesc",   new[] { " — 直接保存为 Word 兼容的 .doc 文件", " — saves directly as a Word-compatible .doc file" } },
            { "Str_Docs_FormatHtmlLabel", new[] { "导出 HTML", "Export HTML" } },
            { "Str_Docs_FormatHtmlDesc",  new[] { " — 保存原始 HTML 文档（可二次编辑）", " — saves the raw HTML document (further editable)" } },
            { "Str_Docs_FormatPrintLabel",new[] { "打印", "Print" } },
            { "Str_Docs_FormatPrintDesc", new[] { " — 直接调用系统打印对话框", " — calls the system print dialog directly" } },
            // 2026-09-21 GenerateAndOpenDocument(title, html)/RunWithEditLock(key, entityLabel, ...) 的
            // title/entityLabel 参数——纯展示(预览窗口标题/编辑锁提示/本地html备份文件名的一部分)，
            // 不是数据比较用的哨兵，翻译安全(跟"全部/男/女/混合"那类不是一回事)。只翻译了
            // Documents/Print页上按钮触发的这几处；导出对话框里建议的文件名(FileName=...)本次不动，
            // 那属于遍布全应用的文件命名惯例，留给后续阶段一起处理。
            { "Str_DocTitle_ProgramBook",     new[] { "秩序册", "Program Book" } },
            { "Str_DocTitle_Schedule",        new[] { "竞赛日程", "Competition Schedule" } },
            { "Str_DocTitle_StartList",       new[] { "出发表", "Start List" } },
            { "Str_DocTitle_HeatResults",     new[] { "分组成绩", "Heat Results" } },
            { "Str_DocTitle_ResultBook",      new[] { "成绩册", "Result Book" } },
            { "Str_DocTitle_TeamStandings",   new[] { "团体成绩", "Team Standings" } },
            { "Str_DocTitle_RecordReport",    new[] { "纪录报告", "Record Report" } },
            { "Str_DocTitle_SplitTimeReport", new[] { "分段计时报告", "Split Time Report" } },
            { "Str_DocTitle_HeatAssignments", new[] { "分组表", "Heat Assignments" } },
            { "Str_DocTitle_AwardCert",       new[] { "奖状", "Award Certificate" } },
            { "Str_DocTitle_RecordCert",      new[] { "纪录证书", "Record Certificate" } },
            { "Str_DocPicker_SplitReport", new[] { "选择已完赛组次 — 分段计时报告", "Choose a completed heat — Split Time Report" } },
            { "Str_DocPicker_ResultTxt",   new[] { "选择已完赛组次 — 成绩 txt 输出", "Choose a completed heat — Result TXT Export" } },

            // 2026-09-21 【中文/English 第六阶段】"系统日志与数据"页(只翻页面本身的静态文字，
            // 不翻 SystemLogListBox 里滚动显示的日志正文——那是 AddLog(...) 调用点生成的，
            // 全应用大概 750+ 处，属于单独一个更大的阶段，这次不碰)
            { "Str_SysLog_RunLog",       new[] { "运行日志", "Run Log" } },
            { "Str_SysLog_ExportLog",    new[] { "导出日志...", "Export Log..." } },
            { "Str_SysLog_ArchiveMgmt",  new[] { "存档管理", "Backup Management" } },
            { "Str_SysLog_LoadBackup",   new[] { "加载选中存档", "Load Selected Backup" } },
            { "Str_SysLog_DeleteBackup", new[] { "删除选中存档", "Delete Selected Backup" } },
            { "Str_SysLog_ClearDb",      new[] { "清除当前库数据", "Clear Current Database" } },
            { "Str_SysLog_ClearRecords", new[] { "删除全部纪录", "Delete All Records" } },
            { "Str_SysLog_ForceSave",    new[] { "强制保存", "Force Save" } },
            { "Str_SysLog_ShowIP",       new[] { "查询本机IP地址", "Show Local IP Address" } },
            { "Str_SysLog_ChangePassword", new[] { "修改密码", "Change Password" } },

            // 2026-09-21 【中文/English 第七阶段】MessageBox 弹窗——约315处调用点里反复出现的
            // 共享标题词(提示/错误/确认/完成等), 先收敛成一批共用 key, 别每个调用点各建一份。
            { "Str_MsgTitle_Info",    new[] { "提示", "Note" } },
            { "Str_MsgTitle_Error",   new[] { "错误", "Error" } },
            { "Str_MsgTitle_Confirm", new[] { "确认", "Confirm" } },
            { "Str_MsgTitle_Done",    new[] { "完成", "Done" } },
            { "Str_MsgTitle_FormatNote",  new[] { "格式提示", "Format Note" } },
            { "Str_MsgTitle_FormatError", new[] { "格式错误", "Format Error" } },
            { "Str_MsgTitle_ServerMismatch", new[] { "本机与主服务器不一致", "Local Data Out of Sync with Main Server" } },
            { "Str_MsgTitle_CannotEdit",   new[] { "无法编辑", "Cannot Edit" } },
            { "Str_MsgTitle_CannotDelete", new[] { "无法删除", "Cannot Delete" } },
            { "Str_MsgTitle_CannotSave",   new[] { "无法保存", "Cannot Save" } },
            { "Str_MsgTitle_CannotAdd",    new[] { "无法新增", "Cannot Add" } },
            { "Str_MsgTitle_CannotProceed",new[] { "无法执行", "Cannot Proceed" } },
            { "Str_MsgTitle_ConfirmDelete",new[] { "确认删除", "Confirm Delete" } },
            { "Str_MsgTitle_ActionBlocked",new[] { "操作被阻止", "Action Blocked" } },

            // 反复出现的具体消息(带 {0}/{1} 占位符走 Loc.F, 纯静态走 Loc.T)
            { "Str_Msg_SaveFailedFmt",   new[] { "保存失败: {0}", "Save failed: {0}" } },
            { "Str_Msg_ExportFailedFmt", new[] { "导出失败: {0}", "Export failed: {0}" } },
            { "Str_Msg_ImportFailedFmt", new[] { "导入失败: {0}", "Import failed: {0}" } },
            { "Str_Msg_PrintFailedFmt",  new[] { "打印失败: {0}", "Print failed: {0}" } },
            { "Str_Msg_ReadFailedFmt",   new[] { "读取失败: {0}", "Read failed: {0}" } },
            { "Str_Msg_WriteFailedFmt",  new[] { "写文件失败: {0}", "Failed to write file: {0}" } },
            { "Str_Msg_DeleteFailedFmt", new[] { "删除失败: {0}", "Delete failed: {0}" } },
            { "Str_Msg_CopyFailedFmt",   new[] { "复制失败: {0}", "Copy failed: {0}" } },
            { "Str_Msg_SelectRelayFirst",  new[] { "请先选中一支接力队", "Select a relay team first." } },
            { "Str_Msg_SelectRowToDelete", new[] { "请先选中要删除的行", "Select a row to delete first." } },
            { "Str_Msg_SelectRowFirst",    new[] { "请先选中一行", "Select a row first." } },
            { "Str_Msg_XlsxReadFailedTip", new[] {
                "无法直接读取 Excel 文件。请另存为 CSV UTF-8 后再导入。",
                "Can't read the Excel file directly. Save it as CSV UTF-8 first, then import that." } },
            { "Str_Msg_XlsxReadFailedTip2", new[] {
                "无法直接读取 Excel 文件。请在 Excel/WPS 中另存为 “CSV UTF-8（逗号分隔）(*.csv)” 后再导入。",
                "Can't read the Excel file directly. In Excel/WPS, save it as “CSV UTF-8 (Comma delimited) (*.csv)” first, then import that." } },
            { "Str_Msg_XlsxReadFailedTip3", new[] {
                "无法直接读取 Excel 文件。请另存为 “CSV UTF-8（逗号分隔）(*.csv)” 后再导入。",
                "Can't read the Excel file directly. Save it as “CSV UTF-8 (Comma delimited) (*.csv)” first, then import that." } },
            { "Str_Msg_XlsxReadFailedTipLong", new[] {
                "无法直接读取Excel文件（.xls/.xlsx）。\n\n请在Excel中将文件另存为：\n  文件类型: CSV UTF-8（逗号分隔）(*.csv)\n  或: CSV（逗号分隔）(*.csv)\n\n然后用导出的CSV文件重新导入。",
                "Can't read the Excel file (.xls/.xlsx) directly.\n\nIn Excel, save the file as:\n  File type: CSV UTF-8 (Comma delimited) (*.csv)\n  or: CSV (Comma delimited) (*.csv)\n\nThen re-import the exported CSV file." } },
            { "Str_Msg_ExcelNoSheet",  new[] { "Excel 中没有工作表", "The Excel file has no worksheet." } },

            // 2026-09-21 第七阶段续——批次1(L743~L19514附近), 具体消息+专属标题(非共享的部分复用词)
            { "Str_MsgTitle_FreeMemory",     new[] { "释放内存", "Free Memory" } },
            { "Str_Msg_FreeMemoryDoneFmt",   new[] { "内存释放完成\n\n之前: {0} MB\n之后: {1} MB\n释放: {2} MB", "Memory freed\n\nBefore: {0} MB\nAfter: {1} MB\nFreed: {2} MB" } },
            { "Str_Msg_NoEntryAssembly",     new[] { "无法识别入口程序集。", "Can't identify the entry assembly." } },
            { "Str_Msg_TypeNotFoundFmt",     new[] { "未找到 {0} 类型。", "Type {0} not found." } },
            { "Str_MsgTitle_NotSavedToServer", new[] { "未保存到主服务器", "Not Saved to Main Server" } },
            { "Str_Msg_NotSavedToServerFmt", new[] {
                "本次修改【没有】保存到主服务器。\n\n原因：{0}\n\n你在本机看到的修改只存在本地。\n请等主服务器点了「计时复位」/「确认本组成绩」之后，\n再点一次「保存修改」重新提交。\n\n（注：「并组 / 取消组」不走这条路，比赛中也能直接生效，\n  只有正在比的那一组改不了。）",
                "This change was NOT saved to the main server.\n\nReason: {0}\n\nWhat you see on this machine only exists locally.\nWait until the main server presses \"Clock Reset\" / \"Confirm Heat Result\", then press \"Save Changes\" again to resubmit.\n\n(Note: \"Merge/Cancel Heat\" doesn't go through this path — it applies immediately even mid-race, except for the heat currently racing.)" } },
            { "Str_MsgTitle_LocalUnsaved",   new[] { "本地有未提交的修改", "Unsaved Local Changes" } },
            { "Str_Msg_LocalUnsavedConfirm", new[] {
                "主服务器推来了新的比赛数据。\n\n但你上一次的修改【还没有】保存到主服务器。\n现在套用主服务器的数据，会覆盖掉你本地未提交的修改。\n\n  是  = 放弃本地修改，采用主服务器的数据\n  否  = 保留本地修改（本次不同步；等主服务器空闲后再点保存）",
                "The main server pushed new competition data.\n\nBut your last change hasn't been saved to the main server yet.\nApplying the main server's data now will overwrite your unsaved local change.\n\n  Yes = discard the local change, use the main server's data\n  No  = keep the local change (skip this sync; save again once the main server is idle)" } },
            { "Str_MsgTitle_HwNotConnected",  new[] { "硬件未连接", "Hardware Not Connected" } },
            { "Str_Msg_HwNotConnected",       new[] { "硬件未连接", "Hardware not connected." } },
            { "Str_MsgTitle_CheckEventBackup",new[] { "查事件备份", "Check Event Backup" } },
            { "Str_MsgTitle_ClearEventBackup",new[] { "清空事件备份", "Clear Event Backup" } },
            { "Str_MsgTitle_ClearConfirm",    new[] { "清空确认", "Confirm Clear" } },
            { "Str_Msg_ClearHwBackupConfirm", new[] { "确定清空硬件事件备份? (= 比赛结束并确认无问题后)", "Clear the hardware event backup? (Do this after the race ends and is confirmed OK.)" } },
            { "Str_MsgTitle_EventBackup",     new[] { "事件备份", "Event Backup" } },
            { "Str_Msg_EventBackupEmpty",     new[] { "事件备份: 无事件", "Event backup: no events." } },
            { "Str_MsgTitle_SaveToDisk",      new[] { "存盘", "Save" } },
            { "Str_Msg_SavedAsFmt",           new[] { "已保存: {0}", "Saved: {0}" } },
            { "Str_MsgTitle_Copy",            new[] { "复制", "Copy" } },
            { "Str_Msg_CopiedToClipboard",    new[] { "已复制到剪贴板", "Copied to clipboard." } },
            { "Str_Msg_LaneErrorFmt",         new[] { "泳道{0}\n\n{1}", "Lane {0}\n\n{1}" } },
            { "Str_MsgTitle_ManualSplitEntry",new[] { "手工补段", "Manual Split Entry" } },
            { "Str_Msg_SelectCurrentHeatFirst", new[] { "请先选择当前比赛组次。", "Select the current heat first." } },
            { "Str_Msg_NoSwimmerToSplit",     new[] { "当前组次无可补段运动员。", "No swimmers in this heat need a manual split." } },
            { "Str_Msg_LapRangeFmt",          new[] { "段次必须是 1-{0} 之间的整数。", "Lap number must be an integer between 1 and {0}." } },
            { "Str_Msg_TimeFormatHint",       new[] { "时间格式应为 m:ss.xxx 或 mm:ss.xxx (例: 1:23.456 或 23.456), 且需大于 0。", "Time must be in m:ss.xxx or mm:ss.xxx format (e.g. 1:23.456 or 23.456), and greater than 0." } },
            { "Str_Msg_SelectLaneFirst",      new[] { "请选择道次。", "Select a lane first." } },
            { "Str_Msg_LaneNoSwimmerFmt",     new[] { "第 {0} 道无运动员/接力队, 不能补段。", "Lane {0} has no swimmer/relay team; can't add a split." } },
            { "Str_MsgTitle_OverwriteConfirm",new[] { "覆盖确认", "Confirm Overwrite" } },
            { "Str_Msg_SplitOverwriteFmt",    new[] { "第{0}道 第{1}段 已有【{2}】成绩 ({3:F3}s)\n手工输入会覆盖, 确定?", "Lane {0}, split {1} already has a [{2}] time ({3:F3}s).\nEntering manually will overwrite it — continue?" } },
            { "Str_MsgTitle_Ready",           new[] { "准备就绪", "Ready" } },
            { "Str_Msg_CannotReReadyFmt",     new[] { "当前状态是「{0}」，不能再次就位。\n\n请先按【计时复位】，再按【准备就绪】。", "Current state is \"{0}\" — can't go to Ready again.\n\nPress [Clock Reset] first, then [Ready]." } },
            { "Str_MsgTitle_ReadyConfirm",    new[] { "就位确认", "Confirm Ready" } },
            { "Str_Msg_ReadyConfirmFmt",      new[] { "确定让本组进入【就位】状态？{0}\n", "Set this heat to [Ready]? {0}\n" } },
            { "Str_MsgTitle_ClockResetConfirm", new[] { "计时复位确认", "Confirm Clock Reset" } },
            { "Str_Msg_ClockResetConfirm",    new[] { "确定计时复位？", "Reset the clock?" } },
            { "Str_MsgTitle_ExportResults",   new[] { "导出成绩", "Export Results" } },
            { "Str_Msg_NoConfirmedToExport",  new[] { "本机竞赛库里还没有已确认的组，没有可导出的成绩。", "No confirmed heats in this machine's competition database yet — nothing to export." } },
            { "Str_Msg_ExportedHeatsFmt",     new[] { "已导出 {0} 个组的成绩。\n\n拿到主服务器上点「从文件导入成绩」即可。\n重复导入是安全的，不会重复计分。", "Exported results for {0} heat(s).\n\nOn the main server, click \"Import Results from File\".\nRe-importing the same file is safe and won't double-count." } },
            { "Str_Msg_ExportResultsFailedFmt", new[] { "导出成绩失败：{0}", "Failed to export results: {0}" } },
            { "Str_MsgTitle_ImportResults",   new[] { "导入成绩", "Import Results" } },
            { "Str_Msg_NotAResultsFile",      new[] { "这不是成绩导出文件。", "This isn't a results export file." } },
            { "Str_MsgTitle_EventMismatch",   new[] { "赛事对不上", "Event Mismatch" } },
            { "Str_Msg_EventMismatchFmt",     new[] { "这个文件是【{0}】的成绩，\n当前打开的是【{1}】。\n\n确定要导入吗？", "This file's results are from [{0}],\nbut the currently open event is [{1}].\n\nImport anyway?" } },
            { "Str_Msg_FileHasNoResults",     new[] { "文件里没有成绩。", "The file has no results." } },
            { "Str_Msg_ImportedHeatsFmt",     new[] { "已导入 {0} 个组的成绩。{1}{2}", "Imported results for {0} heat(s). {1}{2}" } },
            { "Str_Msg_ImportedHeatsBadNoteFmt", new[] { "\n有 {0} 个组没导进来，详见系统日志。", "\n{0} heat(s) failed to import; see the system log for details." } },
            { "Str_Msg_ImportResultsFailedFmt", new[] { "导入成绩失败：{0}", "Failed to import results: {0}" } },
            { "Str_MsgTitle_ConfirmResult",   new[] { "确认成绩", "Confirm Result" } },
            { "Str_Msg_ConfirmResultFmt",     new[] { "确认本组成绩？\n\n{0}\n\n确认后成绩将锁定保存。", "Confirm this heat's result?\n\n{0}\n\nOnce confirmed, the result is locked and saved." } },
            { "Str_MsgTitle_AutoPromote",     new[] { "自动晋级", "Auto-Promote" } },
            { "Str_Msg_AutoPromoteFmt",       new[] { "{0}{1} {2} {3} 全部{4}人已完赛！\n\n是否自动晋级前{5}名到{6}？\n（按成绩总排名）", "{0}{1} {2} {3} — all {4} swimmers have finished!\n\nAuto-promote the top {5} to {6}?\n(ranked by overall time)" } },
            { "Str_MsgTitle_HeatLocked",      new[] { "这一组动不得", "This Heat Is Locked" } },
            { "Str_Msg_HeatLockedFmt",        new[] { "第{0}组现在不能{1}。\n\n原因: 这一组{2}。\n\n别的项目、别的组照常可以改 —— 锁着的只有正在比和已完赛的组。", "Heat {0} can't {1} right now.\n\nReason: this heat {2}.\n\nOther events and heats can still be edited as usual — only the heat that's racing or already done is locked." } },
            { "Str_MsgTitle_EventHasLockedHeat", new[] { "这一项里有动不得的组", "This Event Has a Locked Heat" } },
            { "Str_Msg_EventHasLockedHeatFmt",new[] { "{0} {1} {2} 现在不能{3}。\n\n原因: 第{4}组{5}。\n\n整项重排会把所有人的组次道次重来一遍，\n锁着的那一组也跑不掉，所以整项一起挡下。", "{0} {1} {2} can't {3} right now.\n\nReason: heat {4} {5}.\n\nRe-seeding the whole event reassigns everyone's heat/lane, including the locked heat, so the whole event is blocked." } },
            { "Str_MsgTitle_HeatDone",        new[] { "这一组已完赛", "This Heat Is Already Done" } },
            { "Str_Msg_HeatDoneFmt",          new[] { "第{0}组现在不能{1}。\n\n原因: 这一组{2} —— 已完赛的组不在修改范围。\n\n成绩要在点「确认本组成绩」之前改。", "Heat {0} can't {1} right now.\n\nReason: this heat {2} — heats already done are outside the editable range.\n\nEdit the result before pressing \"Confirm Heat Result\"." } },
            { "Str_MsgTitle_MergeFailed",     new[] { "并组/取消组失败", "Merge/Cancel Heat Failed" } },
            { "Str_Msg_MergeErrorFmt",        new[] { "并组/取消组时出现意外错误, 操作已中止:\n\n{0}\n\n内存里的分组数据可能处于半途状态, 建议：\n  1) 打开「系统日志与数据」核对一下这个项目的分组是否正常；\n  2) 如果不对，重新打开一次赛事档案（不要保存这次改动）。", "Unexpected error while merging/cancelling the heat, operation aborted:\n\n{0}\n\nThe in-memory heat data may be in a half-applied state. Suggested:\n  1) Open \"System Log & Data\" and check this event's heat assignments;\n  2) If they look wrong, reopen the event file (don't save this change)." } },
            { "Str_MsgTitle_MergeNotAccepted",new[] { "未能并组", "Merge Not Accepted" } },
            { "Str_Msg_MergeNotAcceptedFmt",  new[] { "主服务器没有接受这次并组。\n\n原因: {0}\n\n本机数据未改动, 请稍后重试。", "The main server rejected this merge.\n\nReason: {0}\n\nLocal data is unchanged; try again later." } },
            { "Str_Msg_MergeMismatchFmt",     new[] { "主服务器已经完成这次并组，但本机没能跟着改。\n\n原因: {0}\n\n多半是本机这份数据旧了。请断开重连主服务器重新取一次数据。", "The main server completed this merge, but this machine failed to apply it.\n\nReason: {0}\n\nThis machine's data is probably stale — disconnect and reconnect to the main server to refetch." } },
            { "Str_MsgTitle_MergeDone",       new[] { "并组完成", "Merge Complete" } },
            { "Str_Msg_MergeDoneFmt",         new[] { "已完成。\n\n  第{0}组 {1}\n  移动 {2} 人\n\n第{0}组保留组次号并标记为已取消，\n赛程树、大屏、上一组/下一组都会跳过它。\n\n道次如需调整，用 上移/下移/交换泳道。", "Done.\n\n  Heat {0} {1}\n  Moved {2} swimmer(s)\n\nHeat {0} keeps its number and is marked cancelled;\nthe schedule tree, big screen and Prev/Next Heat will all skip it.\n\nAdjust lanes afterward with Up/Down/Swap Lane." } },
            { "Str_Msg_MergedIntoFmt",        new[] { "已并入第{0}组", "merged into heat {0}" } },
            { "Str_Msg_Cancelled",            new[] { "已取消", "cancelled" } },
            { "Str_MsgTitle_NotSavedToServer2", new[] { "未能保存到主服务器", "Failed to Save to Main Server" } },
            { "Str_Msg_ChangeNotAcceptedFmt", new[] { "主服务器没有接受这次改动。\n\n原因: {0}\n\n本机将重新从主服务器取一次数据, 你刚才的改动会被撤销。", "The main server rejected this change.\n\nReason: {0}\n\nThis machine will refetch data from the main server; your change will be reverted." } },
            { "Str_MsgTitle_UnlockHeat",      new[] { "解锁本组成绩", "Unlock Heat Result" } },
            { "Str_Msg_NoHeatLoadedForUnlock",new[] { "当前没有装载任何组次。\n\n请先在赛程导航里选到要解锁的那一组。", "No heat is loaded right now.\n\nSelect the heat to unlock in the schedule tree first." } },
            { "Str_Msg_CannotUnlockWhileTiming", new[] { "正在计时({0})，不能解锁。\n\n这一组还没比完。", "Currently timing ({0}) — can't unlock.\n\nThis heat isn't finished yet." } },
            { "Str_Msg_Ready2",               new[] { "已就位", "Ready" } },
            { "Str_Msg_Racing",               new[] { "比赛中", "Racing" } },
            { "Str_MsgTitle_NoNeedUnlock",    new[] { "不用解锁", "No Need to Unlock" } },
            { "Str_Msg_NoNeedUnlockFmt",      new[] { "第{0}组本来就没锁 —— 它还没点过「确认本组成绩」。\n\n成绩现在就能改。", "Heat {0} was never locked — \"Confirm Heat Result\" hasn't been pressed yet.\n\nYou can edit the result right now." } },
            { "Str_MsgTitle_UnlockHeat1of2",  new[] { "解锁本组成绩 (1/2)", "Unlock Heat Result (1/2)" } },
            { "Str_Msg_UnlockHeatConfirmFmt", new[] { "要把这一组从「已完赛」解开吗？\n\n  {0}\n\n解开之后:\n  · 赛程树上的 [已完赛] 会去掉\n  · 这一组的成绩可以改了(判DSQ、手动改成绩都放开)\n  · 改完必须再点一次「确认本组成绩」重新锁上\n\n这件事会记进系统日志和竞赛库的审计表。", "Unlock this heat from \"Done\"?\n\n  {0}\n\nAfter unlocking:\n  · The [Done] tag is removed from the schedule tree\n  · The result becomes editable (DSQ, manual edits, etc.)\n  · You must press \"Confirm Heat Result\" again afterward to re-lock it\n\nThis is written to the system log and the competition database's audit table." } },
            { "Str_Msg_HeatNotConfirmedFmt",  new[] { "当前组（{0} {1} 第{2}组）尚未确认成绩，不能切换到其它组。\n请先点击\"确认成绩\"或\"计时复位\"清除当前组数据。", "The current heat ({0} {1} heat {2}) hasn't had its result confirmed yet — can't switch to another heat.\nClick \"Confirm Result\" or \"Clock Reset\" to clear the current heat's data first." } },
            { "Str_Msg_CannotReselectEvent",  new[] { "比赛进行中不能重新选择比赛项目。\n\n如需切换，请先点击 \"计时复位\" 结束当前比赛。", "Can't change events while a race is in progress.\n\nTo switch, click \"Clock Reset\" to end the current race first." } },
            { "Str_MsgTitle_CannotTuneLineup",new[] { "不能微调", "Can't Adjust Lineup" } },
            { "Str_Msg_CannotTuneLineupFmt",  new[] { "{0} 第{1}组当前{2}，不能再进行出场编排微调。", "{0} heat {1} is currently {2} — lineup tuning is no longer available." } },
            { "Str_Msg_CurrentlyRacing",      new[] { "正在比赛中", "racing" } },
            { "Str_Msg_ResultConfirmed",      new[] { "成绩已确认", "result confirmed" } },
            { "Str_Msg_SelectRowFirstGeneric",new[] { "请先选中一行", "Select a row first." } },
            { "Str_Msg_SelectRecordTypeRow",  new[] { "请先选中要应用的记录类型行", "Select the record-type row to apply first." } },
            { "Str_Msg_AbbrAndFullNameRequired", new[] { "简称和完整名称都不能为空", "Both the abbreviation and full name are required." } },
            { "Str_MsgTitle_ActionTip",       new[] { "操作提示", "Note" } },
            { "Str_Msg_SelectEventInTreeFirst", new[] { "请先在赛程树选定项目", "Select an event in the schedule tree first." } },
            { "Str_Msg_NoConfirmedResultYet", new[] { "本项目还没有任何已确认成绩 (请先确认至少一组成绩)", "This event has no confirmed results yet (confirm at least one heat first)." } },
            { "Str_MsgTitle_ConfirmMark",     new[] { "确认标记", "Confirm Mark" } },
            { "Str_Msg_ConfirmMarkFmt",       new[] { "确认将泳道 {0} 标记为 {1}（{2}）？\n\n此操作将取消该泳道的成绩。", "Mark lane {0} as {1} ({2})?\n\nThis clears that lane's result." } },
            { "Str_MsgTitle_TriEmptyLane",    new[] { "空道试游(TRI)", "TRI Empty Lane" } },
            { "Str_Msg_TriNeedEventFirst",    new[] { "请先选择具体的比赛项目和组次, 再标注空道试游", "Select a specific event and heat before marking a TRI empty lane." } },
            { "Str_Msg_TriConfirmFmt",        new[] { "泳道{0} 当前无运动员。\n确定标注为【空道试游(TRI)】?\n无报名信息, 仅记录成绩并在大屏/成绩单显示, 不参与名次。", "Lane {0} currently has no swimmer.\nMark it as [TRI Empty Lane]?\nNo registration info — it will just record a time shown on the big screen/result sheet, without a ranking." } },
            { "Str_Msg_InvalidResultFormat",  new[] { "成绩格式无效", "Invalid result format." } },
            { "Str_MsgTitle_ConfirmManualTime", new[] { "确认手动输入", "Confirm Manual Time" } },
            { "Str_Msg_ConfirmManualTimeFmt", new[] { "确认将泳道 {0} 的成绩手动输入为 {1}？\n\n此操作将写入数据库。", "Manually set lane {0}'s result to {1}?\n\nThis will be written to the database." } },
            { "Str_Msg_SelectPrinterFirst",   new[] { "请先选择打印机", "Select a printer first." } },
            { "Str_Msg_CannotDisconnectNotWaiting", new[] { "非 Waiting 状态不能断开 (请先按计时复位)", "Can't disconnect unless timing is idle (press Clock Reset first)." } },
            { "Str_Msg_SwimmerBeingAddedFmt", new[] { "{0} 正在新增运动员，请稍后再试。", "{0} is currently adding a swimmer; try again shortly." } },
            { "Str_Msg_SelectRowsToDeleteMulti", new[] { "请先在列表里选中要删除的行（可按住 Ctrl/Shift 多选）。", "Select the row(s) to delete first (hold Ctrl/Shift to multi-select)." } },
            { "Str_Msg_SwimmerBeingEditedCantDeleteFmt", new[] { "运动员 [{0} {1}] 正在被 {2} 编辑，无法删除。请稍后再试。", "Swimmer [{0} {1}] is being edited by {2}; can't delete. Try again shortly." } },
            { "Str_MsgTitle_DeleteRejected",  new[] { "未能删除", "Delete Rejected" } },
            { "Str_Msg_DeleteRejectedFmt",    new[] { "主服务器没有接受这次删除。\n\n原因: {0}", "The main server rejected this delete.\n\nReason: {0}" } },
            { "Str_Msg_DeleteMismatchFmt",    new[] { "主服务器已删除，但本机没能跟着删。\n\n原因: {0}\n\n请断开重连主服务器重新取一次数据。", "The main server deleted it, but this machine failed to follow.\n\nReason: {0}\n\nDisconnect and reconnect to the main server to refetch." } },
            { "Str_MsgTitle_DeleteResult",    new[] { "删除结果", "Delete Result" } },
            { "Str_Msg_DeletePartialFmt",     new[] { "已删除 {0} 条。另有 {1} 条未在列表中找到，请检查是否被其他筛选/编辑中的操作修改。", "Deleted {0} row(s). {1} more weren't found in the list — check whether another filter/edit changed them." } },
            { "Str_Msg_SelectSwimmerToEditFirst", new[] { "请先选中要修改的运动员", "Select a swimmer to edit first." } },
            { "Str_Msg_SwimmerBeingEditedFmt",new[] { "此运动员 [{0} {1}] 正在被 {2} 编辑，请稍后再试。", "This swimmer [{0} {1}] is being edited by {2}; try again shortly." } },
            { "Str_MsgTitle_SaveRejected",    new[] { "未能保存", "Save Rejected" } },
            { "Str_Msg_SwimmerSaveRejectedFmt", new[] { "主服务器没有接受这次运动员信息修改。\n\n原因: {0}\n\n本机数据未改动, 请稍后重试。", "The main server rejected this swimmer-info change.\n\nReason: {0}\n\nLocal data is unchanged; try again later." } },
            { "Str_Msg_SwimmerSaveMismatchFmt", new[] { "主服务器已经接受这次修改，但本机没能跟着改。\n\n原因: {0}\n\n多半是本机这份数据旧了。请断开重连主服务器重新取一次数据。", "The main server accepted this change, but this machine failed to follow.\n\nReason: {0}\n\nThis machine's data is probably stale — disconnect and reconnect to refetch." } },
            { "Str_Msg_BibRequired",          new[] { "参赛号不能为空", "Bib number is required." } },
            { "Str_Msg_BibExistsFmt",         new[] { "参赛号 {0} 已存在，请换一个号码。", "Bib {0} already exists — use a different number." } },
            { "Str_Msg_TeamDupFmt",           new[] { "代表队 \"{0}\" 重复，请合并或删除重复行。", "Team \"{0}\" is duplicated — merge or delete the duplicate row." } },
            { "Str_Msg_TeamRangeInvalidFmt",  new[] { "代表队 [{0}] 的起始/结束号不合法（要求 起始 > 0 且 结束 ≥ 起始）。", "Team [{0}]'s start/end bib range is invalid (start must be > 0 and end ≥ start)." } },
            { "Str_Msg_TeamRangeOverlapFmt",  new[] { "[{0}] 的号码段 {1}-{2} 与 [{3}] 的 {4}-{5} 重叠，请调整。", "[{0}]'s range {1}-{2} overlaps [{3}]'s range {4}-{5} — please adjust." } },
            { "Str_Msg_NoRegDataToExport",    new[] { "当前没有可导出的报名数据。", "No registration data to export." } },
            { "Str_Msg_ExportedSwimmersFmt",  new[] { "已导出 {0} 条运动员报名数据：\n{1}", "Exported {0} swimmer registration record(s):\n{1}" } },
            { "Str_Msg_SwimmerTemplateSaved", new[] {
                "个人报名模板已保存（27 列）。\n\n● 一名运动员填一行，最多 8 个个人项目并列填在 \"项目1/成绩1\" 到 \"项目8/成绩8\" 列对中\n● 项目列为空 → 该列对忽略；用户用不到的项目列对可全部留空\n● 号码列留空 → 导入时由系统按代表队号码段自动分配\n● 接力项目请在「接力队管理」Tab 集中报名（需指定棒次与队级成绩）\n● 「导入CSV」严格按本模板表头校验，列名/列序不一致整文件拒绝",
                "Individual registration template saved (27 columns).\n\n● One row per swimmer; up to 8 individual events across the \"Event1/Time1\" through \"Event8/Time8\" column pairs\n● Leave an event column pair blank to skip it — unused pairs can all stay empty\n● Leave the bib column blank to auto-assign one from the team's bib range on import\n● Register relay events on the \"Relay Teams\" tab instead (needs leg order and team entry time)\n● \"Import CSV\" strictly validates the header against this template; mismatched column names/order rejects the whole file" } },
            { "Str_MsgTitle_ImportFailed",    new[] { "导入失败", "Import Failed" } },
            { "Str_Msg_CsvNoDataRows",        new[] { "CSV 文件没有数据行（只有表头或为空）。", "The CSV file has no data rows (header only, or empty)." } },
            { "Str_Msg_CsvHeaderCountMismatchFmt", new[] { "❌ CSV 表头列数不对：模板要求 {0} 列，实际 {1} 列。\n\n请用「导出个人报名模板」按钮重新导出模板编辑后再导入。", "❌ CSV header column count is wrong: the template needs {0} columns, found {1}.\n\nRe-export the template with \"Export Template\", edit it, then re-import." } },
            { "Str_Msg_CsvHeaderColMismatchFmt", new[] { "❌ CSV 表头第 {0} 列不匹配：\n   期望「{1}」\n   实际「{2}」\n\n请用「导出个人报名模板」按钮重新导出模板编辑后再导入。", "❌ CSV header column {0} doesn't match:\n   expected \"{1}\"\n   found \"{2}\"\n\nRe-export the template with \"Export Template\", edit it, then re-import." } },
            { "Str_MsgTitle_ImportSwimmerCsvDone", new[] { "导入运动员 CSV 完成", "Swimmer CSV Import Complete" } },
            { "Str_Msg_CsvImportFailedFmt",   new[] { "CSV 导入失败: {0}", "CSV import failed: {0}" } },
            { "Str_Msg_RelayBeingAddedFmt",   new[] { "{0} 正在新增接力队，请稍后再试。", "{0} is currently adding a relay team; try again shortly." } },
            { "Str_Msg_NoRelayEventsAvailable", new[] { "没有可用的接力项目。请检查项目列表。", "No relay events available. Check the event list." } },
            { "Str_Msg_SelectRelayEvent",     new[] { "请选择接力项目", "Select a relay event." } },

            // 2026-09-21 第七阶段续——批次2(合并/取消组对话框区, L11024附近)
            { "Str_Msg_OpenChangePwdFailedFmt", new[] { "打开修改密码窗口失败:\n{0}", "Failed to open the change-password window:\n{0}" } },
            { "Str_MsgTitle_MergeHeatsFailed", new[] { "并组/取消组失败", "Merge/Cancel Heat Failed" } },
            { "Str_Msg_MergeHeatsCrashFmt", new[] {
                "并组/取消组时出现意外错误, 操作已中止:\n\n{0}\n\n内存里的分组数据可能处于半途状态, 建议：\n  1) 打开「系统日志与数据」核对一下这个项目的分组是否正常；\n  2) 如果不对，重新打开一次赛事档案（不要保存这次改动）。",
                "An unexpected error occurred during merge/cancel heat, and the operation was aborted:\n\n{0}\n\nThe grouping data in memory may be half-updated. Suggested:\n  1) Open \"System Log & Data\" and check whether this event's grouping looks right;\n  2) If not, reopen the competition file (without saving this change)." } },
            { "Str_Msg_SelectFiltersFirst", new[] { "请先在上面选定 组别 / 性别 / 项目 / 赛次。", "Select Group / Sex / Event / Stage above first." } },
            { "Str_Msg_OnlyOneHeatNoMerge", new[] { "本项目只有 1 组，无法并组。", "This event has only 1 heat — nothing to merge." } },
            { "Str_Msg_FewerThan2Heats", new[] { "可用的组不足 2 个，无法并组。", "Fewer than 2 heats available — nothing to merge." } },
            { "Str_MsgTitle_MergeNotDone", new[] { "未能并组", "Merge Not Completed" } },
            { "Str_Msg_MergeRejectedFmt", new[] { "主服务器没有接受这次并组。\n\n原因: {0}\n\n本机数据未改动, 请稍后重试。", "The main server did not accept this merge.\n\nReason: {0}\n\nNo local data was changed — try again shortly." } },
            { "Str_Msg_MergeServerAheadFmt", new[] {
                "主服务器已经完成这次并组，但本机没能跟着改。\n\n原因: {0}\n\n多半是本机这份数据旧了。请断开重连主服务器重新取一次数据。",
                "The main server already completed this merge, but this machine failed to apply it.\n\nReason: {0}\n\nThis usually means this machine's data is stale — disconnect and reconnect to the main server to re-fetch it." } },
            { "Str_Msg_AssignRejectedFmt", new[] {
                "主服务器没有接受这次改动。\n\n原因: {0}\n\n本机将重新从主服务器取一次数据, 你刚才的改动会被撤销。",
                "The main server did not accept this change.\n\nReason: {0}\n\nThis machine will re-fetch data from the main server, and your change just now will be reverted." } },
            { "Str_Msg_NoHeatLoaded", new[] { "当前没有装载任何组次。\n\n请先在赛程导航里选到要解锁的那一组。", "No heat is currently loaded.\n\nSelect the heat to unlock in the schedule navigation first." } },
            { "Str_Msg_CannotUnlockRacingFmt", new[] { "正在计时({0})，不能解锁。\n\n这一组还没比完。", "Timing is in progress ({0}) — can't unlock.\n\nThis heat hasn't finished yet." } },
            { "Str_MsgTitle_NoNeedToUnlock", new[] { "不用解锁", "No Need to Unlock" } },
            { "Str_Msg_HeatNotLockedFmt", new[] { "第{0}组本来就没锁 —— 它还没点过「确认本组成绩」。\n\n成绩现在就能改。", "Heat {0} was never locked — \"Confirm Heat Result\" hasn't been pressed for it.\n\nYou can already edit its result." } },
            { "Str_Msg_SelectRecordTypeRow", new[] { "请先选中要应用的记录类型行", "Select the record-type row to apply first." } },
            { "Str_Msg_LabelAndNameRequired", new[] { "简称和完整名称都不能为空", "Both the short label and full name are required." } },
            { "Str_Msg_SelectEventInTreeFirst", new[] { "请先在赛程树选定项目", "Select an event in the schedule tree first." } },
            { "Str_Msg_NoConfirmedResultsYet", new[] { "本项目还没有任何已确认成绩 (请先确认至少一组成绩)", "This event has no confirmed results yet (confirm at least one heat first)." } },
            { "Str_MsgTitle_EmptyLaneTri", new[] { "空道试游(TRI)", "Empty-Lane Trial (TRI)" } },
            { "Str_Msg_SelectEventHeatForTri", new[] { "请先选择具体的比赛项目和组次, 再标注空道试游", "Select a specific event and heat first, then mark an empty-lane trial." } },
            { "Str_Msg_ConfirmEmptyLaneTriFmt", new[] { "泳道{0} 当前无运动员。\n确定标注为【空道试游(TRI)】?\n无报名信息, 仅记录成绩并在大屏/成绩单显示, 不参与名次。", "Lane {0} currently has no swimmer.\nMark it as an [Empty-Lane Trial (TRI)]?\nNo registration info — only records a time shown on the big screen/result sheet, not ranked." } },
            { "Str_Msg_InvalidTimeFormat", new[] { "成绩格式无效", "Invalid time format." } },
            { "Str_MsgTitle_ConfirmManualTime", new[] { "确认手动输入", "Confirm Manual Time" } },
            { "Str_Msg_ConfirmManualTimeFmt", new[] { "确认将泳道 {0} 的成绩手动输入为 {1}？\n\n此操作将写入数据库。", "Confirm manually entering lane {0}'s result as {1}?\n\nThis writes to the database." } },
            { "Str_Msg_SelectPrinterFirst", new[] { "请先选择打印机", "Select a printer first." } },
            { "Str_Msg_CannotDisconnectNotWaiting", new[] { "非 Waiting 状态不能断开 (请先按计时复位)", "Can't disconnect while not in Waiting state (press Clock Reset first)." } },
            { "Str_Msg_SelectRowsToDeleteMulti", new[] { "请先在列表里选中要删除的行（可按住 Ctrl/Shift 多选）。", "Select the row(s) to delete in the list first (hold Ctrl/Shift to multi-select)." } },
            { "Str_Msg_ConfirmDeleteOneSwimmerFmt", new[] { "确定删除运动员 {0}({1}) 的 {2} 报名记录？", "Delete swimmer {0}({1})'s entry in {2}?" } },
            { "Str_Msg_AndOthersFmt", new[] { "\n... 及其它 {0} 条", "\n... and {0} more" } },
            { "Str_Msg_ConfirmDeleteMultiSwimmersFmt", new[] { "确定删除以下 {0} 条报名记录？\n\n{1}", "Delete the following {0} entries?\n\n{1}" } },
            { "Str_Msg_SwimmerBeingEditedFmt", new[] { "运动员 [{0} {1}] 正在被 {2} 编辑，无法删除。请稍后再试。", "Swimmer [{0} {1}] is currently being edited by {2} — can't delete. Try again shortly." } },
            { "Str_MsgTitle_DeleteNotDone", new[] { "未能删除", "Delete Not Completed" } },
            { "Str_Msg_DeleteRejectedFmt", new[] { "主服务器没有接受这次删除。\n\n原因: {0}", "The main server did not accept this delete.\n\nReason: {0}" } },
            { "Str_Msg_DeleteServerAheadFmt", new[] { "主服务器已删除，但本机没能跟着删。\n\n原因: {0}\n\n请断开重连主服务器重新取一次数据。", "The main server already deleted this, but this machine failed to apply it.\n\nReason: {0}\n\nDisconnect and reconnect to the main server to re-fetch data." } },
            { "Str_MsgTitle_DeleteResult", new[] { "删除结果", "Delete Result" } },
            { "Str_Msg_DeletePartialNotFoundFmt", new[] { "已删除 {0} 条。另有 {1} 条未在列表中找到，请检查是否被其他筛选/编辑中的操作修改。", "Deleted {0}. {1} more were not found in the list — check whether another filter/edit changed them." } },
            { "Str_Msg_SelectSwimmerToEdit", new[] { "请先选中要修改的运动员", "Select the swimmer to edit first." } },
            { "Str_Msg_SwimmerBeingEditedByFmt", new[] { "此运动员 [{0} {1}] 正在被 {2} 编辑，请稍后再试。", "Swimmer [{0} {1}] is currently being edited by {2} — try again shortly." } },
            { "Str_MsgTitle_SaveNotDone", new[] { "未能保存", "Save Not Completed" } },
            { "Str_Msg_SwimmerEditRejectedFmt", new[] { "主服务器没有接受这次运动员信息修改。\n\n原因: {0}\n\n本机数据未改动, 请稍后重试。", "The main server did not accept this swimmer edit.\n\nReason: {0}\n\nNo local data was changed — try again shortly." } },
            { "Str_Msg_SwimmerEditServerAheadFmt", new[] { "主服务器已经接受这次修改，但本机没能跟着改。\n\n原因: {0}\n\n多半是本机这份数据旧了。请断开重连主服务器重新取一次数据。", "The main server already accepted this edit, but this machine failed to apply it.\n\nReason: {0}\n\nThis usually means this machine's data is stale — disconnect and reconnect to re-fetch it." } },
            { "Str_Msg_BibRequired", new[] { "参赛号不能为空", "Bib number is required." } },
            { "Str_Msg_BibAlreadyExistsFmt", new[] { "参赛号 {0} 已存在，请换一个号码。", "Bib number {0} already exists — choose a different number." } },
            { "Str_Msg_CsvNoDataRows", new[] { "CSV 文件没有数据行（只有表头或为空）。", "The CSV file has no data rows (only a header, or empty)." } },
            { "Str_Msg_TeamNameRequired", new[] { "队名不能为空", "Team name is required." } },
            { "Str_Msg_RelayTeamAlreadyRegisteredFmt", new[] { "代表队 \"{0}\" 已在该项目（{1} {2} {3}）报名过接力队。", "Team \"{0}\" already has a relay entry in this event ({1} {2} {3})." } },
            { "Str_MsgTitle_RelayRegistration", new[] { "接力队报名", "Relay Registration" } },
            { "Str_Msg_RelayLegErrorFmt", new[] { "第{0}棒 {1}", "Leg {0}: {1}" } },
            { "Str_Msg_ExportedRelayTemplateFmt", new[] { "已导出接力报名模板:\n{0}\n\n表头共 18 列（含组别），编辑后用「导入CSV」回灌；生日格式 yyyy-MM-dd，报名成绩格式 mm:ss.ff 或 ss.ff。", "Exported the relay registration template:\n{0}\n\n18 columns total (including Group). Edit it, then re-import with \"Import CSV\"; birth date format yyyy-MM-dd, entry time format mm:ss.ff or ss.ff." } },
            { "Str_Msg_NoRelayTeamsToExport", new[] { "当前没有接力队可导出", "No relay teams to export." } },
            { "Str_Msg_ExportedRelayTeamsFmt", new[] { "已导出 {0} 支接力队到:\n{1}", "Exported {0} relay team(s) to:\n{1}" } },
            { "Str_Msg_NoRelayTeamsYet", new[] { "当前没有接力队。", "No relay teams yet." } },
            { "Str_Msg_ExportedLegSheetsFmt", new[] { "已按场次导出 {0} 个 Excel 文件到:\n{1}\n\n{2}\n\n用法: 拿到哪一场的名单就打开哪个文件,\n只填黄色的「第1棒~第4棒」四列, 灰色各列请勿改动。\n填完存盘 (xlsx / xls 都行, WPS 表格存的也认),\n回程序点「读入棒次名单」。\n没填姓名的行会整行跳过, 不会影响别的队。", "Exported {0} Excel file(s) by session to:\n{1}\n\n{2}\n\nUsage: open the file for whichever session's list you received,\nfill in only the yellow \"Leg 1–4\" columns — don't touch the gray columns.\nSave (xlsx/xls both work, WPS-saved too),\nthen click \"Read Leg Roster\" back in the program.\nRows with no name filled in are skipped entirely and won't affect other teams." } },
            { "Str_MsgTitle_ExportSuccess", new[] { "导出成功", "Export Succeeded" } },
            { "Str_Msg_ReexportLegSheetSuffix", new[] { "\n\n请用「导出棒次填报表」重新导出一份再填。", "\n\nRe-export with \"Export Leg Roster\" and fill it in again." } },
            { "Str_Msg_NoDataRowsInSheet", new[] { "表里没有数据行。", "The sheet has no data rows." } },
            { "Str_Msg_SessionFileMismatchFmt", new[] { "这个文件里没有 第{0}场 的数据。\n\n文件里装的是: {1} (共 {2} 行)\n\n是不是文件拿错了? 请改选对应的场次, 或换成 第{0}场 的填报表。", "This file has no data for Session {0}.\n\nThe file actually contains: {1} ({2} row(s) total)\n\nIs this the wrong file? Reselect the matching session, or use the roster exported for Session {0}." } },
            { "Str_MsgTitle_SessionFileMismatch", new[] { "文件与场次对不上", "File / Session Mismatch" } },
            { "Str_Msg_LegImportDoneFmt", new[] { "读入完成  (第{0}场)\n\n  已填入棒次: {1} 支队\n", "Import complete (Session {0})\n\n  Filled in: {1} team(s)\n" } },
            { "Str_Msg_LegImportPerSessionFmt", new[] { "      第{0}场: {1} 支\n", "      Session {0}: {1} team(s)\n" } },
            { "Str_Msg_LegImportBlankFmt", new[] { "  尚未填写(跳过): {0} 行  ← 这些队原样不动\n", "  Not yet filled in (skipped): {0} row(s) — these teams are left unchanged\n" } },
            { "Str_Msg_LegImportOtherSessionFmt", new[] { "  其它场次(跳过): {0} 行  ← 本次只读第{1}场\n", "  Other sessions (skipped): {0} row(s) — this import only reads Session {1}\n" } },
            { "Str_Msg_LegImportNotFoundHeaderFmt", new[] { "  对不上的队伍: {0} 行\n", "  Teams that didn't match: {0} row(s)\n" } },
            { "Str_Msg_LegImportMoreRowsFmt", new[] { "      ...另有 {0} 行\n", "      ...and {0} more row(s)\n" } },
            { "Str_Msg_LegImportMismatchHint", new[] { "  (代表队/项目/性别/组别 四项必须与档案完全一致,\n   建议用「导出棒次填报表」导出的表来填, 不要自己另建表)\n", "  (Team/Event/Sex/Group must match the competition file exactly —\n   use the sheet exported by \"Export Leg Roster\" rather than building your own.)\n" } },
            { "Str_MsgTitle_ImportSuccess", new[] { "读入成功", "Import Succeeded" } },
            { "Str_MsgTitle_NoDataImported", new[] { "没有读入任何数据", "No Data Imported" } },
            { "Str_Msg_FileIsEmpty", new[] { "文件为空", "The file is empty." } },
            { "Str_Msg_RelayHeaderCountMismatchFmt", new[] { "❌ 表头列数不对：期望 {0} 列，实际 {1} 列。请用「导出接力报名模板」导出新模板后重试。", "❌ Wrong header column count: expected {0}, found {1}. Re-export with \"Export Relay Template\" and try again." } },
            { "Str_Msg_RelayHeaderColMismatchFmt", new[] { "❌ 表头第 {0} 列不匹配：期望「{1}」，实际「{2}」。请用「导出接力报名模板」导出新模板后重试。", "❌ Header column {0} doesn't match: expected \"{1}\", found \"{2}\". Re-export with \"Export Relay Template\" and try again." } },
            { "Str_Msg_RelayCsvImportSummaryFmt", new[] { "✔ 导入完成\n\n  新增接力队: {0}\n  跳过: {1}\n  组别待确认: {2} (高亮显示在列表中, 请在赛程管理窗口补录生日并锁定分组)\n", "✔ Import complete\n\n  Relay teams added: {0}\n  Skipped: {1}\n  Group pending confirmation: {2} (highlighted in the list — fill in birth dates and lock the group in Schedule Management)\n" } },
            { "Str_Msg_SkipRecordsHeader", new[] { "\n--- 跳过的记录 ---", "\n--- Skipped Records ---" } },
            { "Str_MsgTitle_RelayCsvImportResult", new[] { "接力 CSV 导入结果", "Relay CSV Import Result" } },
            { "Str_Msg_SelectRelayToEdit", new[] { "请先在接力队列表中选中要编辑的队伍。", "Select the relay team to edit in the list first." } },
            { "Str_Msg_RelayBeingEditedFmt", new[] { "接力队 [{0}] 正在被 {1} 编辑，请稍后再试。", "Relay team [{0}] is currently being edited by {1} — try again shortly." } },
            { "Str_Msg_RelayEditRejectedFmt", new[] { "主服务器没有接受这次接力队信息修改。\n\n原因: {0}\n\n本机数据未改动, 请稍后重试。", "The main server did not accept this relay team edit.\n\nReason: {0}\n\nNo local data was changed — try again shortly." } },
            { "Str_Msg_RelayEditServerAheadFmt", new[] { "主服务器已经接受这次修改，但本机没能跟着改。\n\n原因: {0}\n\n多半是本机这份数据旧了。请断开重连主服务器重新取一次数据。", "The main server already accepted this edit, but this machine failed to apply it.\n\nReason: {0}\n\nThis usually means this machine's data is stale — disconnect and reconnect to re-fetch it." } },
            { "Str_Msg_RelayBeingEditedCannotDeleteFmt", new[] { "接力队 [{0}] 正在被 {1} 编辑，无法删除。请稍后再试。", "Relay team [{0}] is currently being edited by {1} — can't delete. Try again shortly." } },
            { "Str_Msg_ConfirmDeleteRelayFmt", new[] { "确定删除接力队 [{0}] ({1})？", "Delete relay team [{0}] ({1})?" } },
            { "Str_Msg_SelectLegToReplace", new[] { "请在棒次表中选中要更换的队员", "Select the leg to replace in the leg table first." } },
            { "Str_Msg_RelayLegsSaved", new[] { "接力队棒次修改已保存！", "Relay leg changes saved!" } },
            { "Str_MsgTitle_SaveSuccess", new[] { "保存成功", "Saved" } },
            { "Str_Msg_RelayHeatInfoSynced", new[] { "已同步接力队的组数和道次信息。", "Relay team heat/lane info synced." } },
            { "Str_MsgTitle_SyncDone", new[] { "同步完成", "Sync Complete" } },
            { "Str_Msg_EventAlreadyAdded", new[] { "已添加此项目，不能重复！", "This event is already added — can't add it twice." } },
            { "Str_Msg_SelectEventToDelete", new[] { "请先选中要删除的项目", "Select the event to delete first." } },
            { "Str_Msg_SelectSwimmerToSwap", new[] { "请先选中要交换的运动员", "Select the swimmer to swap first." } },
            { "Str_Msg_NoSwapCandidates", new[] { "没有可交换的运动员或空道", "No swimmer or empty lane available to swap with." } },
            { "Str_Msg_SelectSpecificHeatNotAll", new[] { "请先选择具体的组（不能是\"全部\"）", "Select a specific heat first (not \"All\")." } },
            { "Str_Msg_NoUnassignedSwimmers", new[] { "没有未分组的运动员可以添加", "No unassigned swimmers available to add." } },
            { "Str_Msg_SwapSameHeatOnly", new[] { "只能在同一组内交换泳道位置。", "Lanes can only be swapped within the same heat." } },
            { "Str_Msg_SelectSwimmerFirst", new[] { "请先选中一名运动员", "Select a swimmer first." } },
            { "Str_Msg_SelectFiltersBeforeTempAdd", new[] { "请先在上方选择性别、项目、赛次后再临时加人。", "Select Sex, Event, and Stage above before adding a temporary entry." } },
            { "Str_Msg_NameRequired", new[] { "姓名不能为空", "Name is required." } },
            { "Str_Msg_TempAddedSwimmerFmt", new[] { "已添加 {0}。\n请使用“增加到本组”或“交换泳道→空道”将其放入具体组/道。", "{0} added.\nUse \"Add to Heat\" or \"Swap Lane → empty lane\" to place them into a specific heat/lane." } },
            { "Str_MsgTitle_TempAddDone", new[] { "临时加人完成", "Temporary Entry Added" } },
            { "Str_Msg_TeamAlreadyInEventFmt", new[] { "代表队 \"{0}\" 已在本项目报名过接力队。", "Team \"{0}\" already has a relay entry in this event." } },
            { "Str_Msg_TempAddedRelayFmt", new[] { "已添加接力队 {0}。\n请使用“增加到本组”或“交换泳道→空道”将其放入具体组/道。", "Relay team {0} added.\nUse \"Add to Heat\" or \"Swap Lane → empty lane\" to place it into a specific heat/lane." } },
            { "Str_Msg_LineupChangesSaved", new[] { "编排修改已保存！", "Lineup changes saved!" } },
            { "Str_Msg_ConfirmAddScheduleItem", new[] { "确定要添加一条新的赛程项？\n（将插入到选中行后面，未选中则添加到末尾）", "Add a new schedule item?\n(It will be inserted after the selected row, or appended to the end if none is selected.)" } },
            { "Str_Msg_SelectRowToMove", new[] { "请先选中要移动的行", "Select the row to move first." } },
            { "Str_Msg_SelectRowInScheduleToDelete", new[] { "请先在日程表中选中要删除的行", "Select the row to delete in the schedule table first." } },
            { "Str_Msg_ConfirmDeleteScheduleItemFmt", new[] { "确定要删除赛程项 [{0}]？", "Delete schedule item [{0}]?" } },
            { "Str_Msg_NoRegisteredSwimmersForHeats", new[] { "没有已注册的运动员/接力队，请先注册再生成分组。", "No registered swimmers/relay teams — register first, then generate heats." } },
            { "Str_Msg_GenerateScheduleFirst", new[] { "请先点击\"一键生成日程\"生成赛程安排，再进行分组。", "Click \"Generate Schedule\" to build the schedule first, then group heats." } },
            { "Str_Msg_ConfirmAutoGenHeats", new[] { "确定按报名成绩对预赛进行蛇形自动分组？", "Auto-seed preliminary heats by entry time (serpentine)?" } },
            { "Str_Msg_WarnReassignHeats", new[] { "警告：已有运动员分好组！\n\n重新自动分组将清除所有已有分组，重新分配。\n如需仅对新增运动员分组，请使用\"追加分组\"。\n\n确定要重新全部分组吗？", "Warning: swimmers are already grouped into heats!\n\nRe-running auto-grouping clears all existing heats and reassigns everyone.\nTo group only newly-added swimmers, use \"Append Grouping\" instead.\n\nRe-group everyone now?" } },
            { "Str_Msg_AutoHeatsDoneFmt", new[] { "分组完成！\n共{0}项已按报名成绩蛇形分组。\n\n后续赛次需在成绩与排名中通过\"晋级处理\"根据比赛成绩进行分组。{1}", "Grouping complete!\n{0} event(s) seeded by entry time.\n\nLater stages need \"Promotion\" in Results & Rankings to group by actual race results.{1}" } },
            { "Str_MsgTitle_HeatsDone", new[] { "分组完成", "Grouping Complete" } },
            { "Str_Msg_NoEventsAssigned", new[] { "没有分配任何项目。", "No events were assigned." } },
            { "Str_MsgTitle_HeatsNotDone", new[] { "分组未执行", "Grouping Not Performed" } },
            { "Str_Msg_NoSwimmersAssignedCheck", new[] { "未分配任何运动员/接力队。\n\n请检查：\n1. 项目名称是否与赛程一致\n2. 性别是否与赛程一致\n3. 是否已生成日程", "No swimmers/relay teams were assigned.\n\nCheck:\n1. Event names match the schedule\n2. Sex matches the schedule\n3. The schedule has been generated" } },
            { "Str_Msg_NoRegisteredSwimmers", new[] { "暂无已注册运动员。", "No registered swimmers yet." } },
            { "Str_Msg_NoSwimmersToGroup", new[] { "没有可分组的运动员。", "No swimmers available to group." } },
            { "Str_Msg_SelectEventFirst", new[] { "请先选择项目", "Select an event first." } },
            { "Str_Msg_ExportedToFmt", new[] { "已导出: {0}", "Exported to: {0}" } },
            { "Str_Msg_GenerateScheduleAndHeatsFirst", new[] { "请先生成日程和预赛分组。", "Generate the schedule and preliminary heats first." } },
            { "Str_Msg_AllSwimmersAssigned", new[] { "所有运动员已分组，没有需要追加的。", "All swimmers are already grouped — nothing to append." } },
            { "Str_Msg_ConfirmResetScoringDefaults", new[] { "将所有数值恢复为系统默认（个人 12/10/8/7/6/5/4/3，接力 24/20/16/14/12/10/8/6，青少年/少年=0.8、大师=0.7，取分前 8 名；种子组数 短3/长2）。继续？", "Reset all values to system defaults (individual 12/10/8/7/6/5/4/3, relay 24/20/16/14/12/10/8/6, Youth/Junior=0.8, Masters=0.7, top 8 scored; seed heats short 3/long 2). Continue?" } },
            { "Str_MsgTitle_ResetDefaults", new[] { "恢复默认", "Reset to Defaults" } },
            { "Str_Msg_RankCutoffRange", new[] { "取分人数必须是 1–50 的整数。", "The scoring cutoff must be an integer from 1 to 50." } },
            { "Str_MsgTitle_InputError", new[] { "输入错误", "Input Error" } },
            { "Str_Msg_SelectFiltersForChiefJudge", new[] { "请先在上面选好组别/性别/项目/阶段。", "Select Group/Sex/Event/Stage above first." } },
            { "Str_MsgTitle_ChiefJudgeEdit", new[] { "裁判长改成绩", "Chief Judge Edit Result" } },
            { "Str_Msg_SelectSpecificHeatForChiefJudge", new[] { "请把上面的「组」下拉框选到具体的第几组(不能选\"全部\") —— 这个窗口一次只改一组。", "Set the \"Heat\" dropdown above to a specific heat number (not \"All\") — this window edits one heat at a time." } },
            { "Str_Msg_NoRawDataFilesDirFmt", new[] { "暂无原始数据文件。\n\n原始数据在比赛确认成绩后自动保存到:\n{0}", "No raw data files yet.\n\nRaw data is automatically saved here once a heat's result is confirmed:\n{0}" } },
            { "Str_Msg_NoRawDataFiles", new[] { "暂无原始数据文件。", "No raw data files yet." } },
            { "Str_Msg_OpenFailedFmt", new[] { "打开失败: {0}", "Failed to open: {0}" } },
            { "Str_Msg_HtmlFileMissing", new[] { "对应的HTML文件不存在。\n\n该文件可能是旧版本保存的，请重新确认成绩以生成HTML版本。", "The corresponding HTML file doesn't exist.\n\nThis file may have been saved by an older version — re-confirm the result to generate an HTML version." } },
            { "Str_Msg_NoFinishedFinalsRanking", new[] { "尚无已完赛的决赛项目 (需要该项目所有组都已'确认本组成绩').", "No finished final events yet (every heat in the event must have its result confirmed)." } },
            { "Str_Msg_SelectAnEvent", new[] { "请选择一个项目", "Select an event." } },
            { "Str_Msg_NoScheduleData", new[] { "当前没有赛程数据", "No schedule data yet." } },
            { "Str_Msg_CsvImportSummaryFmt", new[] { "CSV 导入完成（个人报名模板）：\n\n  ✅ 新增 {0} 条\n  🔄 更新 {1} 条 (按身份证号+项目匹配)\n  ⏭ 跳过 {2} 行\n", "CSV import complete (individual registration template):\n\n  ✅ Added {0}\n  🔄 Updated {1} (matched by ID number + event)\n  ⏭ Skipped {2} row(s)\n" } },
            { "Str_Msg_SkipDetailsHeader", new[] { "\n--- 跳过明细 ---", "\n--- Skipped Details ---" } },
            { "Str_Msg_MoreNotShownFmt", new[] { "... 还有 {0} 条未显示\n", "... {0} more not shown\n" } },
        };

        /// <summary>查当前语言下的文字；查不到就退回中文；中文也没有就返回 key 本身兜底。</summary>
        public static string T(string key) {
            string[] pair;
            if (!Table.TryGetValue(key, out pair)) return key;
            int idx = CurrentLanguage == En ? 1 : 0;
            return (idx < pair.Length && !string.IsNullOrEmpty(pair[idx])) ? pair[idx] : pair[0];
        }

        /// <summary>2026-09-21【第七阶段: MessageBox 弹窗】T() 只管"整句话固定不变"的文字；
        /// 弹窗消息大多是 string.Format 拼出来的("确定删除 {0} 吗?")，这里把查表结果当
        /// format 模板用。中英文模板里的 {0}/{1}… 占位符个数/顺序必须跟调用处传的参数对上，
        /// 对不上会被 catch 住退回原模板文字(带着没换掉的{0})而不是崩溃。</summary>
        public static string F(string key, params object[] args) {
            try { return string.Format(T(key), args); } catch { return T(key); }
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

        /// <summary>纯展示用的赛次名翻译——"预赛"/"半决赛"/"决赛"这几个值本身在别处是拿来
        /// 做比较/存档的数据哨兵(不能翻译，见 384d37d 起的说明)，这个函数只用在拼接给人看
        /// 的标题/列头文字时，不改变调用方手里那份 stage 字符串本身。</summary>
        public static string StageDisplay(string zhStage) {
            switch (zhStage) {
                case "预赛": return T("Str_Stage_Prelim");
                case "半决赛": return T("Str_Stage_Semi");
                case "决赛": return T("Str_Stage_Final");
                default: return zhStage ?? "";
            }
        }

        public static void Toggle() {
            CurrentLanguage = CurrentLanguage == En ? Zh : En;
            Save();
            Apply();
        }
    }
}
