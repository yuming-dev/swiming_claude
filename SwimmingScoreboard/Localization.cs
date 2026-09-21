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
