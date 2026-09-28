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
        "Str_Checkin_ConfirmSwitchHeat": ["当前组有未保存的备注修改，切换后将丢失。确定切换？", "This heat has unsaved note changes that will be lost if you switch. Continue?"]
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
