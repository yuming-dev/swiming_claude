// webloc.js — 网页端(display/query/register/checkin/race_control/control/leaderboard)中文/English 双语支持
// 跟桌面WPF端 SwimmingScoreboard/Localization.cs 的 Loc 类是同一个思路, 但这是纯浏览器JS实现,
// 不共享任何代码(技术栈不同), 只共享"key -> [中文, English]"这个总表 + 同样的安全网原则:
//   - t(key) 查不到key就退化显示key本身, 不抛异常/不崩页面(参照 Loc.T 的安全网)。
//   - 只翻译"纯UI文案"(按钮/标签/提示/表头等), 不碰任何会 send 给服务器或参与 ===/==
//     比较的协议字段值(比如 gender/stage 的原始中文值、status 的 DNS/DNF/DSQ 代码等)——
//     这些值本身永远保持不变, 只在"给人看"的位置上包一层 t()/genderDisplay()/stageDisplay()
//     做展示层转换, 参照桌面端 Loc.GenderDisplay/Loc.StageDisplay 的模式。
var WebLoc = (function () {
    var table = {
        // ── 通用 ──────────────────────────────────────────────
        "Str_Common_Confirm": ["确定", "OK"],
        "Str_Common_Cancel": ["取消", "Cancel"],
        "Str_Common_Close": ["关闭", "Close"],
        "Str_Common_Print": ["打印", "Print"],
        "Str_Common_Server": ["服务器", "Server"],
        "Str_Common_Connect": ["连接", "Connect"],
        "Str_Common_ScheduleNav": ["赛程导航", "Schedule"],
        "Str_Common_WaitingSchedule": ["等待赛程数据…", "Waiting for schedule data…"],
        "Str_Common_NoSchedule": ["暂无赛程", "No schedule yet"],
        "Str_Common_ConnectServerTitle": ["连接主服务器", "Connect to Server"],
        "Str_Common_ServerIpLabel": ["服务器IP地址", "Server IP Address"],
        "Str_Common_ServerIpPlaceholder": ["如 192.168.1.100", "e.g. 192.168.1.100"],
        "Str_Common_Connecting": ["连接中...", "Connecting..."],
        "Str_Common_Connected": ["已连接", "Connected"],
        "Str_Common_Disconnected": ["未连接", "Not connected"],
        "Str_Common_NotConnectedParen": ["（未连接）", "(Not connected)"],
        "Str_Common_ConnClosed": ["已断开", "Disconnected"],
        "Str_Common_ConnError": ["连接错误", "Connection error"],
        "Str_Common_ConnFailedFmt": ["无法连接: {0}", "Unable to connect: {0}"],
        "Str_Common_SessionNumFmt": ["第{0}场", "Session {0}"],
        "Str_Common_HeatOfFmt": ["第{0}组（共{1}组）", "Heat {0} (of {1})"],
        "Str_Common_HeatNumFmt": ["第{0}组", "Heat {0}"],
        "Str_Common_LegFmt": ["第{0}棒", "Leg {0}"],
        "Str_Common_Leg1": ["第1棒", "Leg 1"],
        "Str_Common_Leg2": ["第2棒", "Leg 2"],
        "Str_Common_Leg3": ["第3棒", "Leg 3"],
        "Str_Common_Leg4": ["第4棒", "Leg 4"],
        "Str_Common_CountParenFmt": ["（共 {0} {1}）", " ({0} {1})"],
        "Str_Common_LangToggle": ["English", "中文"],
        // 组次状态(数字 HEAT_STATUS -> 展示文本; 底层比较用的是数字, 这些只是展示层)
        "Str_HeatStatus_Pending": ["未开始", "Not started"],
        "Str_HeatStatus_CheckedIn": ["已检录", "Checked in"],
        "Str_HeatStatus_Running": ["进行中", "In progress"],
        "Str_HeatStatus_Done": ["已完赛", "Finished"],
        "Str_HeatStatus_Confirmed": ["已确认", "Confirmed"],
        "Str_HeatStatus_Cancelled": ["已取消", "Cancelled"],
        // 性别/赛次 展示层转换(底层值保持中文, 参照 Loc.GenderDisplay/Loc.StageDisplay)
        "Str_Gender_Male": ["男", "Male"],
        "Str_Gender_Female": ["女", "Female"],
        "Str_Gender_Mixed": ["混合", "Mixed"],
        "Str_Stage_Prelim": ["预赛", "Prelim"],
        "Str_Stage_Semi": ["半决赛", "Semifinal"],
        "Str_Stage_Final": ["决赛", "Final"],

        // ── checkin.html ──────────────────────────────────────
        "Str_Checkin_PageTitle": ["游泳竞赛检录台", "Swimming Check-in Station"],
        "Str_Checkin_ConfirmBtn": ["确认检录", "Confirm Check-in"],
        "Str_Checkin_SelectHeatPrompt": ["请从左侧选择比赛组", "Select a heat from the left"],
        "Str_Checkin_SelectHeatFull": ["请从左侧赛程导航中选择一个比赛组", "Select a heat from the schedule navigation on the left"],
        "Str_Checkin_ResultSubmitted": ["检录结果已提交", "Check-in result submitted"],
        "Str_Checkin_ResultRejectedFmt": ["检录未提交：{0}", "Check-in not submitted: {0}"],
        "Str_Checkin_ServerRejectedDefault": ["服务器拒绝了这次提交", "The server rejected this submission"],
        "Str_Checkin_HeatRunningLocked": ["该组正在比赛中", "This heat is currently racing"],
        "Str_Checkin_HeatConfirmedLocked": ["该组成绩已确认", "This heat's results are confirmed"],
        "Str_Checkin_LockedSuffixFmt": ["{0}，不能再检录，仅供查看。", "{0} — check-in is locked, view only."],
        "Str_Checkin_TeamCountUnit": ["队", "team(s)"],
        "Str_Checkin_PersonCountUnit": ["人", "athlete(s)"],
        "Str_Checkin_DefaultCompName": ["游泳竞赛", "Swimming Competition"],
        "Str_Checkin_PrintSuffix": ["检录表", "Check-in Sheet"],
        "Str_Checkin_PrintCountFmt": ["共 {0} {1}", "Total {0} {1}"],
        "Str_Checkin_PrintTimeFmt": ["打印时间: {0}", "Printed: {0}"],
        "Str_Checkin_RefereeSignature": ["检录裁判签字: ________________", "Check-in Referee Signature: ________________"],
        "Str_Checkin_ColLane": ["泳道", "Lane"],
        "Str_Checkin_ColBib": ["号码", "Bib"],
        "Str_Checkin_ColName": ["姓名", "Name"],
        "Str_Checkin_ColNameRelaySuffix": ["（队 / 队员）", " (Team / Member)"],
        "Str_Checkin_ColId": ["身份证号", "ID Number"],
        "Str_Checkin_ColTeam": ["单位", "Team"],
        "Str_Checkin_ColNote": ["备注（检录）", "Note (Check-in)"],
        "Str_Checkin_NoSwimmers": ["该组暂无运动员报名", "No swimmers registered for this heat"],
        "Str_Checkin_StatusNormal": ["正常", "Normal"],
        "Str_Checkin_StatusDNS": ["DNS 弃权/未到", "DNS Absent/No-show"],
        "Str_Checkin_StatusDNF": ["DNF 中途退出", "DNF Did not finish"],
        "Str_Checkin_StatusDSQ": ["DSQ 犯规", "DSQ Disqualified"],
        "Str_Checkin_StatPresent": ["实到", "Present"],
        "Str_Checkin_StatTeamCount": ["队数", "Teams"],
        "Str_Checkin_StatTotalCount": ["总数", "Total"],
        "Str_Checkin_StatMemberPresent": ["队员实到", "Members present"],
        "Str_Checkin_StatAbsent": ["弃权", "Absent"],
        "Str_Checkin_StatDNF": ["中退", "DNF"],
        "Str_Checkin_StatDSQ": ["犯规", "DSQ"],
        "Str_Checkin_StatMemberAbsent": ["缺席队员", "Absent members"],
        "Str_Checkin_Unsaved": ["(未保存)", "(unsaved)"],
        "Str_Checkin_SelectHeatFirst": ["请先选择比赛组", "Please select a heat first"],
        "Str_Checkin_NotConnected": ["未连接主服务器", "Not connected to server"],
        "Str_Checkin_CannotCheckinLocked": ["该组正在比赛中或成绩已确认，不能再检录。", "This heat is racing or confirmed — check-in is locked."],
        "Str_Checkin_ConfirmSubmit": ["确认提交本组检录结果？\n\n将把备注中的 DNS/DNF/DSQ 状态同步到主服务器。", "Submit check-in results for this heat?\n\nThe DNS/DNF/DSQ statuses in the notes will be synced to the server."],
        "Str_Checkin_ConfirmSwitchHeat": ["当前组有未保存的备注修改，切换后将丢失。确定切换？", "This heat has unsaved note changes that will be lost if you switch. Continue?"],

        // ── 更多通用词（register/query/display 共用） ──────────
        "Str_Common_Fullscreen": ["全屏", "Fullscreen"],
        "Str_Common_Query": ["查询", "Search"],
        "Str_Common_Name": ["姓名", "Name"],
        "Str_Common_Gender": ["性别", "Gender"],
        "Str_Common_SelectGender": ["请选择性别", "Select gender"],
        "Str_Common_PleaseSelectGender": ["请选择性别", "Please select a gender"],
        "Str_Common_BirthDate": ["出生日期", "Date of Birth"],
        "Str_Common_Country": ["代表队", "Team"],
        "Str_Common_CountryShort": ["单位简称", "Team Abbr."],
        "Str_Common_AgeGroup": ["组别", "Age Group"],
        "Str_Common_SelectAgeGroup": ["请选择组别", "Select age group"],
        "Str_Common_OptionalParen": ["（可选）", "(optional)"],
        "Str_Common_Event": ["项目", "Event"],
        "Str_Common_EntryTime": ["报名成绩", "Entry Time"],
        "Str_Common_ClearForm": ["清空当前表单", "Clear Form"],
        "Str_Common_Notes": ["备注", "Notes"],
        "Str_Common_IdNumber": ["身份证号", "ID Number"],
        "Str_Common_BibShort": ["参赛号", "Bib"],
        "Str_Common_NotSpecified": ["不指定", "Not specified"],
        "Str_Common_Querying": ["查询中…", "Searching…"],
        "Str_Common_NotConnectedShort": ["未连接服务器", "Not connected to server"],
        "Str_Common_NotConnectedQuery": ["未连接服务器，无法查询", "Not connected to server — cannot search"],
        "Str_Common_FormErrorHeader": ["表单未通过校验，请补全以下字段：", "The form did not pass validation — please complete the following fields:"],
        "Str_Common_Edit": ["修改", "Edit"],
        "Str_Common_Delete": ["删除", "Delete"],
        "Str_Common_Know": ["知道了", "Got it"],
        "Str_Common_BackToEdit": ["返回修改", "Back to Edit"],
        "Str_Common_UnnamedEntryFmt": ["第{0}条", "Entry {0}"],
        "Str_Common_UnknownReasonDefault": ["未知原因", "Unknown reason"],
        "Str_Common_ValidationFailedDefault": ["校验未通过", "Validation failed"],
        "Str_Common_VersionMismatchFmt": ["⚠ 本页版本 {0} 与主服务器 {1} 不一致 —— 请按 Ctrl+F5 强制刷新。仍不一致说明这台机器上的 Web 目录没跟 exe 一起更新。(点此关闭)",
            "⚠ This page's version {0} does not match the server's {1} — press Ctrl+F5 to force-refresh. If it still mismatches, this machine's Web folder wasn't updated together with the exe. (Click to dismiss)"],

        // ── register.html ─────────────────────────────────────
        "Str_Register_PageTitle": ["运动员注册", "Athlete Registration"],
        "Str_Register_ChangeServerBtn": ["🔄 切换服务器IP", "🔄 Change Server IP"],
        "Str_Register_ChangeServerTip": ["点此随时切换到另一台主服务器（无需先断开）", "Click to switch to another server at any time (no need to disconnect first)"],
        "Str_Register_SubmittedDefault": ["报名信息已提交成功！", "Registration submitted successfully!"],
        "Str_Register_EditThisSubmission": ["修改本次报名", "Edit This Registration"],
        "Str_Register_TabPersonal": ["👤 个人项目报名", "👤 Individual Events"],
        "Str_Register_TabRelay": ["👥 接力队报名", "👥 Relay Teams"],
        "Str_Register_RelayCardTitle": ["接力队报名", "Relay Team Registration"],
        "Str_Register_QueryCardTitle": ["查询 / 修改我已经提交的报名", "Look Up / Edit My Registration"],
        "Str_Register_LabelBib": ["参赛号", "Bib Number"],
        "Str_Register_BibPlaceholder": ["如 001", "e.g. 001"],
        "Str_Register_LabelNameCountry": ["或 姓名 + 代表队", "Or Name + Team"],
        "Str_Common_NamePlaceholder": ["姓名", "Name"],
        "Str_Common_CountryPlaceholder": ["代表队", "Team"],
        "Str_Register_PersonalCardTitle": ["个人项目报名", "Individual Event Registration"],
        "Str_Register_RequiredNote": ["（带 * 为必填）", " (* required)"],
        "Str_Register_DateFormatTip": ["格式：yyyy-MM-dd（4 位年份）", "Format: yyyy-MM-dd (4-digit year)"],
        "Str_Register_IdLabel": ["身份证号（可选，国际选手可用护照号等证件号）", "ID Number (optional — international athletes may use a passport or other ID number)"],
        "Str_Register_IdPlaceholder": ["选填，不限位数/格式", "Optional, any format"],
        "Str_Register_Phone": ["联系电话", "Phone"],
        "Str_Register_CsaNumber": ["协会注册号", "Association Reg. No."],
        "Str_Register_AgeGroupTip": ["组别由服务器同步，可在主控软件「比赛参数设置管理→组别」里维护", "Age groups are synced from the server; maintain them in the main app under \"Competition Parameters → Age Groups\""],
        "Str_Register_EventsTitle": ["参赛项目", "Events"],
        "Str_Register_AtLeastOne": ["*（至少 1 项）", "* (at least 1)"],
        "Str_Register_AddEventBtn": ["+ 添加参赛项目", "+ Add Event"],
        "Str_Register_AddToQueueBtn": ["+ 添加到报名列表", "+ Add to Registration List"],
        "Str_Register_PersonalHint": ["录入完一人后点\"添加到报名列表\"，再录入下一人；全部录入完成后在下方\"待提交报名列表\"统一提交。",
            "After entering one athlete, click \"Add to Registration List\", then enter the next; once done, submit all at once from the \"Pending Registrations\" list below."],
        "Str_Register_QueueTitle": ["待提交报名列表", "Pending Registrations"],
        "Str_Register_QueueHint": ["提交后服务器会逐条返回结果。未通过的条目会保留在列表里并显示原因，修改后再次\"全部提交\"。",
            "The server returns a result for each entry after submitting. Entries that fail stay in the list with the reason shown — fix them and \"Submit All\" again."],
        "Str_Register_QueryRelayTitle": ["查询已报名接力队（改棒次用）", "Look Up a Registered Relay Team (to edit legs)"],
        "Str_Register_TeamNamePlaceholder": ["输入队名，如 北京队A", "Enter team name, e.g. Beijing Team A"],
        "Str_Register_QueryAndFillBtn": ["查询并回填", "Look Up & Fill"],
        "Str_Register_TeamNameLabel": ["队名", "Team Name"],
        "Str_Register_LegArrangement": ["棒次安排（4 棒姓名必填，同一队伍报多个接力项目共用这组棒次）", "Leg Assignments (all 4 names required; the same team sharing multiple relay events uses this one set of legs)"],
        "Str_Register_NameRequiredPlaceholder": ["姓名 *", "Name *"],
        "Str_Register_RelayEventsTitle": ["接力项目", "Relay Events"],
        "Str_Register_AddRelayEventBtn": ["+ 添加接力项目", "+ Add Relay Event"],
        "Str_Register_AddRelayQueueBtn": ["+ 添加到接力报名列表", "+ Add to Relay Registration List"],
        "Str_Register_RelayHint": ["录入完一支队（棒次+项目）后点\"添加到接力报名列表\"，再录入下一支队；全部录入完成后统一提交。",
            "After entering one team (legs + events), click \"Add to Relay Registration List\", then enter the next; submit all once done."],
        "Str_Register_RelayQueueTitle": ["待提交接力报名列表", "Pending Relay Registrations"],
        "Str_Register_RelayQueueHint": ["提交后服务器会逐条（队×项目）返回结果。未通过的条目请回到上方表单修改后重新添加。",
            "The server returns a result for each (team × event) entry. For entries that fail, go back to the form above, fix them, and re-add."],
        "Str_Register_ConfirmClose": ["确定要关闭注册终端？", "Close the registration terminal?"],
        "Str_Register_ConnectedFmt": ["已连接服务器 {0}", "Connected to server {0}"],
        "Str_Register_ConnLostRetrying": ["连接断开，重连中...", "Disconnected, reconnecting..."],
        "Str_Register_ConnFailRetry": ["连接失败，是否重新设置服务器地址？", "Connection failed — reset the server address?"],
        "Str_Register_EnterServerIpPrompt": ["请输入服务器IP地址:", "Enter the server IP address:"],
        "Str_Register_DefaultAthlete": ["运动员", "the athlete"],
        "Str_Register_SuccessBannerFmt": ["报名成功！{0}（参赛号 {1}），表单已自动重置可继续注册下一位。", "Registration succeeded! {0} (Bib {1}) — the form has been reset so you can register the next athlete."],
        "Str_Register_SuccessStatusFmt": ["报名成功！参赛号: {0}", "Registration succeeded! Bib: {0}"],
        "Str_Register_FailStatusFmt": ["报名失败: {0}", "Registration failed: {0}"],
        "Str_Register_UnknownError": ["未知错误", "Unknown error"],
        "Str_Register_QueryNeedInput": ["请输入参赛号，或同时输入姓名+代表队", "Enter a bib number, or both name and team"],
        "Str_Register_RelayQueryNeedTeam": ["请输入队名", "Enter a team name"],
        "Str_Register_RelayNotFoundDefault": ["未找到该队", "Team not found"],
        "Str_Register_RelayFilledFmt": ["✔ 已回填: {0} ({1} 棒)，修改后点「+ 添加接力项目」「+ 添加到接力报名列表」「全部提交接力报名」，服务器会按队名+性别+项目+组别覆盖更新",
            "✔ Filled in: {0} ({1} legs). After editing, click \"+ Add Relay Event\" → \"+ Add to Relay Registration List\" → \"Submit All Relay Registrations\" — the server will overwrite by team name + gender + event + age group"],
        "Str_Register_QueryNotFoundDefault": ["未找到对应报名记录", "No matching registration found"],
        "Str_Register_LoadedForEditFmt": ["已加载报名信息（参赛号 {0}），可修改后再次提交。", "Registration loaded (Bib {0}) — edit and submit again."],
        "Str_Register_EditModeStatusFmt": ["修改模式：参赛号 {0}。提交将覆盖旧记录。", "Edit mode: Bib {0}. Submitting will overwrite the previous record."],
        "Str_Register_DuplicateEvent": ["已添加此项目，不能重复！", "This event has already been added!"],
        "Str_Register_NoEventsYet": ["尚未添加参赛项目", "No events added yet"],
        "Str_Register_NoRelayEventsYet": ["尚未添加接力项目", "No relay events added yet"],
        "Str_Register_EntryTimeLabelFmt": ["报名: {0}", "Entry: {0}"],
        "Str_Register_RemoveEventBtn": ["X 删除此项", "X Remove"],
        "Str_Register_PleaseName": ["请填写姓名", "Please enter a name"],
        "Str_Register_PleaseBirthDate": ["请填写出生日期", "Please enter a date of birth"],
        "Str_Register_BirthDateFormatErr": ["出生日期格式应为 yyyy-MM-dd", "Date of birth must be in yyyy-MM-dd format"],
        "Str_Register_PleaseAgeGroup": ["请选择组别", "Please select an age group"],
        "Str_Register_PleaseCountry": ["请填写代表队", "Please enter a team"],
        "Str_Register_PleaseAtLeastOneEvent": ["请至少添加一个参赛项目", "Please add at least one event"],
        "Str_Register_JoinedQueueMsg": ["已加入报名列表，可继续录入下一位", "Added to the registration list — you can enter the next athlete"],
        "Str_Register_EditingQueueFmt": ["正在编辑队列第 {0} 条，修改后请再次点\"添加到报名列表\"", "Editing entry {0} — after making changes, click \"Add to Registration List\" again"],
        "Str_Register_QueueEmptyHint": ["列表为空，请先在上方录入运动员后\"添加到报名列表\"", "The list is empty — enter an athlete above, then \"Add to Registration List\""],
        "Str_Register_SubmitAllPersonFmt": ["全部提交报名（{0} 人）", "Submit All ({0} athletes)"],
        "Str_Register_QueueCountFmt": ["共 {0} 人", "{0} total"],
        "Str_Register_QiMetaEventsFmt": ["{0} 项", "{0} event(s)"],
        "Str_Register_QiMetaBibFmt": [" / 号 {0}", " / Bib {0}"],
        "Str_Register_QueueEmptySubmit": ["列表为空，请先添加", "The list is empty — add an entry first"],
        "Str_Register_SubmittingFmt": ["正在提交 {0} 人报名……等待服务器结果", "Submitting {0} registration(s)… waiting for the server"],
        "Str_Register_ResultOkTitle": ["报名提交成功", "Registration Submitted"],
        "Str_Register_ResultOkSummaryFmt": ["主服务器已通过本次报名，共 {0} 人，相关数据已写入比赛系统。", "The server accepted this registration — {0} athlete(s), and the data has been written to the competition system."],
        "Str_Register_ResultOkBibFmt": ["（参赛号 {0}）", " (Bib {0})"],
        "Str_Register_ResultOkAddedFmt": ["：{0} 项", ": {0} event(s)"],
        "Str_Register_ResultFailTitle": ["报名未通过", "Registration Rejected"],
        "Str_Register_ResultFailDefaultMsg": ["主服务器反馈：部分条目未通过校验。请修改下方红色标记的条目后再次\"全部提交报名\"。",
            "Server feedback: some entries failed validation. Fix the entries marked red below, then \"Submit All\" again."],
        "Str_Register_PassedHeader": ["报名通过：", "Accepted:"],
        "Str_Register_FailedHeader": ["未通过项：", "Rejected:"],
        "Str_Register_FailedHint": ["提示：在下方\"待提交报名列表\"中点对应条目\"修改\"按钮调出表单更正后，再次点\"全部提交报名\"。",
            "Tip: click \"Edit\" on the entry in the \"Pending Registrations\" list below to correct it, then \"Submit All\" again."],
        "Str_Register_PleaseTeamName": ["请填写队名", "Please enter a team name"],
        "Str_Register_PleaseLegNameFmt": ["请填写第{0}棒姓名", "Please enter the name for leg {0}"],
        "Str_Register_LegBirthFormatErrFmt": ["第{0}棒出生日期格式应为 yyyy-MM-dd", "Leg {0}'s date of birth must be in yyyy-MM-dd format"],
        "Str_Register_PleaseAtLeastOneRelayEvent": ["请至少添加一个接力项目", "Please add at least one relay event"],
        "Str_Register_PleaseSelectRelayEventFirst": ["请先选择接力项目", "Please select a relay event first"],
        "Str_Register_RelayJoinedQueueMsg": ["已加入接力报名列表，可继续录入下一支队", "Added to the relay registration list — you can enter the next team"],
        "Str_Register_RelayEditingQueueFmt": ["正在编辑接力队列第 {0} 条，修改后请再次点\"添加到接力报名列表\"", "Editing relay entry {0} — after making changes, click \"Add to Relay Registration List\" again"],
        "Str_Register_RelayQueueEmptyHint": ["列表为空，请先在上方录入接力队后\"添加到接力报名列表\"", "The list is empty — enter a relay team above, then \"Add to Relay Registration List\""],
        "Str_Register_RelaySubmitAllFmt": ["全部提交接力报名（{0} 队）", "Submit All Relay Registrations ({0} teams)"],
        "Str_Register_RelayQueueCountFmt": ["共 {0} 队", "{0} team(s) total"],
        "Str_Register_RelayQiMetaFmt": ["{0} 项 / {1} 棒", "{0} event(s) / {1} legs"],
        "Str_Register_RelayQueueEmptySubmit": ["接力列表为空，请先添加", "The relay list is empty — add an entry first"],
        "Str_Register_RelaySubmittingFmt": ["正在提交 {0} 支接力队……等待服务器结果", "Submitting {0} relay team(s)… waiting for the server"],
        "Str_Register_RelayResultOkTitle": ["接力报名提交成功", "Relay Registration Submitted"],
        "Str_Register_RelayUpdatedSummary": ["主服务器已更新该接力队（队名+性别+项目唯一），原有报名被新数据覆盖。", "The server updated this relay team (unique by team name + gender + event) — the previous registration was overwritten."],
        "Str_Register_RelayNewSummary": ["主服务器已通过本次接力报名，相关数据已写入比赛系统。", "The server accepted this relay registration, and the data has been written to the competition system."],
        "Str_Register_RelayBibFmt": ["（队伍号 {0}）", " (Team No. {0})"],
        "Str_Register_RelayLegCountFmt": ["：{0} 棒", ": {0} legs"],
        "Str_Register_RelayResultFailTitle": ["接力报名未通过", "Relay Registration Rejected"],
        "Str_Register_RelayResultFailDefault": ["主服务器反馈：接力队报名未通过。请修改后重新提交。", "Server feedback: the relay team registration was rejected. Fix it and resubmit."],
        "Str_Register_RelayMultiOkSummaryFmt": ["共 {0} 条（队×项目）全部通过。", "All {0} entries (team × event) were accepted."],
        "Str_Register_RelayUpdatedTag": [" [更新]", " [updated]"],
        "Str_Register_RelayNewTag": [" [新建]", " [new]"],
        "Str_Register_RelayPartialTitle": ["接力报名部分通过", "Relay Registration Partially Accepted"],
        "Str_Register_RelayMultiSummaryFmt": ["通过 {0} 条，未通过 {1} 条。未通过的请修改后重新添加到列表提交。", "{0} accepted, {1} rejected. Fix the rejected entries and re-add them to the list."],
        "Str_Register_RelayBibShortFmt": [" 队伍号 {0}", " Team No. {0}"]
    };

    var STORAGE_KEY = 'swimWebLang';

    function currentLang() {
        try { return localStorage.getItem(STORAGE_KEY) || 'zh'; } catch (e) { return 'zh'; }
    }
    function setLang(lang) {
        try { localStorage.setItem(STORAGE_KEY, lang); } catch (e) {}
    }
    function t(key) {
        var row = table[key];
        if (!row) return key;   // 缺key不崩, 退化显示key本身(安全网, 跟 Loc.T 一致)
        var idx = currentLang() === 'en' ? 1 : 0;
        return row[idx] != null ? row[idx] : row[0];
    }
    function f(key) {
        var s = t(key);
        var args = Array.prototype.slice.call(arguments, 1);
        for (var i = 0; i < args.length; i++) s = s.split('{' + i + '}').join(args[i]);
        return s;
    }
    // 性别/赛次 展示层转换 —— 只用于"给人看"的地方, 传入/传出服务器的原始值不要用这个
    function genderDisplay(zh) {
        if (zh === '男') return t('Str_Gender_Male');
        if (zh === '女') return t('Str_Gender_Female');
        if (zh === '混合') return t('Str_Gender_Mixed');
        return zh || '';
    }
    function stageDisplay(zh) {
        if (zh === '预赛') return t('Str_Stage_Prelim');
        if (zh === '半决赛') return t('Str_Stage_Semi');
        if (zh === '决赛') return t('Str_Stage_Final');
        return zh || '';
    }
    function apply() {
        document.querySelectorAll('[data-i18n]').forEach(function (el) {
            var key = el.getAttribute('data-i18n');
            var attr = el.getAttribute('data-i18n-attr');
            var val = t(key);
            if (attr) el.setAttribute(attr, val); else el.textContent = val;
            // data-i18n-title: 同一个元素除了主文案(textContent/属性)以外, 还要翻译 title 提示,
            // 值是另一个 key(不复用主 key, 因为 title 提示文案通常跟可见文字不一样)。
            var titleKey = el.getAttribute('data-i18n-title');
            if (titleKey) el.setAttribute('title', t(titleKey));
        });
        try { document.documentElement.lang = currentLang() === 'en' ? 'en' : 'zh-CN'; } catch (e) {}
        if (typeof window.onWebLocApplied === 'function') { try { window.onWebLocApplied(); } catch (e) {} }
    }
    function toggle() { setLang(currentLang() === 'en' ? 'zh' : 'en'); apply(); }

    return {
        table: table, t: t, f: f, apply: apply, toggle: toggle, currentLang: currentLang,
        genderDisplay: genderDisplay, stageDisplay: stageDisplay
    };
})();
