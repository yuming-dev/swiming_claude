using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace SwimmingScoreboard
{
    // 分组表 Excel 导入/导出服务（NPOI XSSF）
    // 三个 sheet：分组明细（行式）/ 分组表(网格) / 填写说明
    public static class HeatExcelService
    {
        // ───── 行式数据 ─────
        public class HeatRow
        {
            public int SessionNumber;
            public string Date;
            public string SessionPeriod;
            public int EventNumber;
            public string AgeGroup;
            public string Gender;
            public string EventName;
            public string Stage;
            public string SortMethod;
            public int Heat;
            public int Lane;
            public string BibNumber;
            public string Name;
            public string Country;
            public string CountryShort;
            public string EntryTime;
            public string BirthDate;
            public int Age;
            public string Notes;
        }

        public static readonly string[] Sheet1Headers = new[] {
            "场次","比赛日期","时段","项目编号","组别","性别","项目","赛次","排序方式",
            "组号","道次","参赛号","姓名","代表队","单位简称","报名成绩","出生年月","年龄","备注"
        };

        // ───── 导出 ─────
        public static void Export(string path, string competitionName, string startDate, string endDate, string location,
                                  int laneCount, IEnumerable<HeatRow> rows, IEnumerable<EventInfo> events,
                                  IEnumerable<string> teams) {
            var wb = new XSSFWorkbook();
            var styles = new Styles(wb);

            // 标题段
            var sh1 = wb.CreateSheet("分组明细");
            BuildSheet1(sh1, styles, rows);

            var sh2 = wb.CreateSheet("分组表(网格)");
            BuildSheet2(sh2, styles, competitionName, startDate, endDate, location, laneCount, rows);

            var sh3 = wb.CreateSheet("填写说明");
            BuildSheet3(sh3, styles, events, teams);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) {
                wb.Write(fs);
            }
        }

        // ───── 模板（空数据，仅说明 + 示例）─────
        public static void WriteTemplate(string path, int laneCount, IEnumerable<EventInfo> events, IEnumerable<string> teams) {
            var samples = new List<HeatRow> {
                new HeatRow { SessionNumber=1, Date="2026-01-01", SessionPeriod="下午", EventNumber=1, AgeGroup="戊组", Gender="女", EventName="50米仰泳", Stage="决赛", SortMethod="按成绩排名", Heat=1, Lane=4, BibNumber="F012", Name="李沐桐", Country="南山启美", EntryTime="0:34.20", BirthDate="2014-05-12", Age=11 },
                new HeatRow { SessionNumber=1, Date="2026-01-01", SessionPeriod="下午", EventNumber=1, AgeGroup="戊组", Gender="女", EventName="50米仰泳", Stage="决赛", SortMethod="按成绩排名", Heat=1, Lane=5, BibNumber="F013", Name="叶曼羽", Country="绵阳蓝鲸", EntryTime="0:34.55", BirthDate="2014-08-03", Age=11 },
                new HeatRow { SessionNumber=1, Date="2026-01-01", SessionPeriod="下午", EventNumber=2, AgeGroup="丁组", Gender="男", EventName="50米自由泳", Stage="预赛", SortMethod="按成绩排名", Heat=1, Lane=4, BibNumber="M021", Name="张三", Country="金五环", EntryTime="0:28.10", BirthDate="2013-02-15", Age=12 }
            };
            Export(path, "比赛名称", "", "", "", laneCount, samples, events, teams);
        }

        // ───── 导入 ─────
        public static List<HeatRow> Import(string path, out string warning) {
            warning = "";
            var result = new List<HeatRow>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) {
                IWorkbook wb = path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
                    ? (IWorkbook)new NPOI.HSSF.UserModel.HSSFWorkbook(fs)
                    : new XSSFWorkbook(fs);
                ISheet sh = wb.GetSheet("分组明细") ?? wb.GetSheetAt(0);
                if (sh == null) { warning = "未找到工作表"; return result; }

                int firstRow = sh.FirstRowNum;
                IRow header = sh.GetRow(firstRow);
                if (header == null) { warning = "首行(表头)为空"; return result; }
                var colMap = new Dictionary<string, int>();
                for (int c = 0; c < header.LastCellNum; c++) {
                    var hc = header.GetCell(c);
                    string h = (hc != null ? hc.ToString() : "");
                    h = (h ?? "").Trim();
                    if (h.Length > 0 && !colMap.ContainsKey(h)) colMap[h] = c;
                }
                Func<string, int> col = name => colMap.ContainsKey(name) ? colMap[name] : -1;
                int cSession = col("场次"), cDate = col("比赛日期"), cPeriod = col("时段"), cEvNum = col("项目编号");
                int cAge = col("组别"), cGender = col("性别"), cEvent = col("项目"), cStage = col("赛次"), cSort = col("排序方式");
                int cHeat = col("组号"), cLane = col("道次");
                int cBib = col("参赛号"), cName = col("姓名"), cCountry = col("代表队"), cShort = col("单位简称");
                int cEntry = col("报名成绩"), cBirth = col("出生年月"), cAgeY = col("年龄"), cNotes = col("备注");

                if (cGender < 0 || cEvent < 0 || cStage < 0 || cHeat < 0 || cLane < 0) {
                    warning = "表头缺少必填列：性别 / 项目 / 赛次 / 组号 / 道次"; return result;
                }

                for (int r = firstRow + 1; r <= sh.LastRowNum; r++) {
                    IRow row = sh.GetRow(r);
                    if (row == null) continue;
                    string gender = CellStr(row, cGender);
                    string ev = CellStr(row, cEvent);
                    string stage = CellStr(row, cStage);
                    if (string.IsNullOrEmpty(gender) && string.IsNullOrEmpty(ev) && string.IsNullOrEmpty(stage)) continue;
                    int heat = CellInt(row, cHeat), lane = CellInt(row, cLane);
                    if (heat <= 0 || string.IsNullOrEmpty(gender) || string.IsNullOrEmpty(ev) || string.IsNullOrEmpty(stage)) continue;
                    result.Add(new HeatRow {
                        SessionNumber = CellInt(row, cSession),
                        Date = CellStr(row, cDate),
                        SessionPeriod = CellStr(row, cPeriod),
                        EventNumber = CellInt(row, cEvNum),
                        AgeGroup = CellStr(row, cAge),
                        Gender = gender,
                        EventName = System.Text.RegularExpressions.Regex.Replace(ev, @"\s+", ""),
                        Stage = stage,
                        SortMethod = CellStr(row, cSort),
                        Heat = heat,
                        Lane = lane,
                        BibNumber = CellStr(row, cBib),
                        Name = CellStr(row, cName),
                        Country = CellStr(row, cCountry),
                        CountryShort = CellStr(row, cShort),
                        EntryTime = CellStr(row, cEntry),
                        BirthDate = CellStr(row, cBirth),
                        Age = CellInt(row, cAgeY),
                        Notes = CellStr(row, cNotes)
                    });
                }
            }
            return result;
        }

        // ───── 内部：sheet 构建 ─────
        public class EventInfo { public int Number; public string AgeGroup, Gender, EventName, Stage, SortMethod; public int Entrants, HeatCount; }

        private static void BuildSheet1(ISheet sh, Styles st, IEnumerable<HeatRow> rows) {
            IRow header = sh.CreateRow(0);
            for (int i = 0; i < Sheet1Headers.Length; i++) {
                ICell c = header.CreateCell(i);
                c.SetCellValue(Sheet1Headers[i]);
                c.CellStyle = st.HeaderStyle;
            }
            int r = 1;
            foreach (var x in rows) {
                IRow row = sh.CreateRow(r++);
                int c = 0;
                row.CreateCell(c++).SetCellValue(x.SessionNumber > 0 ? x.SessionNumber : 0);
                row.CreateCell(c++).SetCellValue(x.Date ?? "");
                row.CreateCell(c++).SetCellValue(x.SessionPeriod ?? "");
                row.CreateCell(c++).SetCellValue(x.EventNumber > 0 ? x.EventNumber : 0);
                row.CreateCell(c++).SetCellValue(x.AgeGroup ?? "");
                row.CreateCell(c++).SetCellValue(x.Gender ?? "");
                row.CreateCell(c++).SetCellValue(x.EventName ?? "");
                row.CreateCell(c++).SetCellValue(x.Stage ?? "");
                row.CreateCell(c++).SetCellValue(x.SortMethod ?? "");
                row.CreateCell(c++).SetCellValue(x.Heat);
                row.CreateCell(c++).SetCellValue(x.Lane);
                row.CreateCell(c++).SetCellValue(x.BibNumber ?? "");
                row.CreateCell(c++).SetCellValue(x.Name ?? "");
                row.CreateCell(c++).SetCellValue(x.Country ?? "");
                row.CreateCell(c++).SetCellValue(x.CountryShort ?? "");
                row.CreateCell(c++).SetCellValue(x.EntryTime ?? "");
                row.CreateCell(c++).SetCellValue(x.BirthDate ?? "");
                row.CreateCell(c++).SetCellValue(x.Age > 0 ? x.Age : 0);
                row.CreateCell(c++).SetCellValue(x.Notes ?? "");
            }
            // 列宽
            int[] widths = { 8, 14, 8, 10, 10, 8, 22, 10, 14, 8, 8, 12, 16, 18, 14, 12, 14, 8, 14 };
            for (int i = 0; i < widths.Length; i++) sh.SetColumnWidth(i, widths[i] * 256);
            sh.CreateFreezePane(0, 1);
        }

        private static void BuildSheet2(ISheet sh, Styles st, string compName, string startDate, string endDate,
                                        string location, int laneCount, IEnumerable<HeatRow> rows) {
            // 标题
            int r = 0;
            IRow t1 = sh.CreateRow(r++);
            ICell tc1 = t1.CreateCell(0);
            tc1.SetCellValue(string.IsNullOrEmpty(compName) ? "竞赛分组表" : compName);
            tc1.CellStyle = st.TitleStyle;
            sh.AddMergedRegion(new CellRangeAddress(0, 0, 0, laneCount + 1));
            t1.HeightInPoints = 26;

            IRow t2 = sh.CreateRow(r++);
            ICell tc2 = t2.CreateCell(0);
            tc2.SetCellValue(string.Format("竞赛分组表  {0}{1}{2}",
                startDate ?? "", string.IsNullOrEmpty(endDate) || endDate == startDate ? "" : " 至 " + endDate,
                string.IsNullOrEmpty(location) ? "" : "    " + location));
            tc2.CellStyle = st.SubtitleStyle;
            sh.AddMergedRegion(new CellRangeAddress(1, 1, 0, laneCount + 1));
            r++;

            // 分场次→项目→组
            var bySession = rows
                .GroupBy(x => new { x.SessionNumber, x.Date, x.SessionPeriod })
                .OrderBy(g => g.Key.SessionNumber).ThenBy(g => g.Key.Date ?? "");
            foreach (var sess in bySession) {
                // 场次标题
                IRow sRow = sh.CreateRow(r);
                ICell sC = sRow.CreateCell(0);
                sC.SetCellValue(string.Format("第 {0} 场　{1}{2}",
                    sess.Key.SessionNumber > 0 ? sess.Key.SessionNumber.ToString() : "—",
                    sess.Key.Date ?? "", string.IsNullOrEmpty(sess.Key.SessionPeriod) ? "" : "　" + sess.Key.SessionPeriod));
                sC.CellStyle = st.SessionStyle;
                sh.AddMergedRegion(new CellRangeAddress(r, r, 0, laneCount + 1));
                sRow.HeightInPoints = 22;
                r += 2;

                var byEvent = sess
                    .GroupBy(x => new { x.EventNumber, x.AgeGroup, x.Gender, x.EventName, x.Stage, x.SortMethod })
                    .OrderBy(g => g.Key.EventNumber).ThenBy(g => g.Key.AgeGroup ?? "");
                foreach (var ev in byEvent) {
                    int totalEntrants = ev.Select(x => (x.BibNumber ?? "") + "|" + (x.Name ?? "")).Distinct().Count();
                    int heatCount = ev.Select(x => x.Heat).Distinct().Count();
                    string evHeader = string.Format("{0}.  {1}  {2}  {3}      {4}    {5}人    {6}组    {7}",
                        ev.Key.EventNumber > 0 ? ev.Key.EventNumber.ToString() : "—",
                        ev.Key.AgeGroup ?? "", ev.Key.Gender ?? "", ev.Key.EventName ?? "",
                        ev.Key.Stage ?? "", totalEntrants, heatCount, ev.Key.SortMethod ?? "");
                    IRow eRow = sh.CreateRow(r);
                    ICell eC = eRow.CreateCell(0);
                    eC.SetCellValue(evHeader);
                    eC.CellStyle = st.EventStyle;
                    sh.AddMergedRegion(new CellRangeAddress(r, r, 0, laneCount + 1));
                    eRow.HeightInPoints = 18;
                    r++;

                    // 道次表头行
                    IRow lh = sh.CreateRow(r);
                    ICell lh0 = lh.CreateCell(0); lh0.SetCellValue("组号"); lh0.CellStyle = st.SubHeaderStyle;
                    ICell lh1 = lh.CreateCell(1); lh1.SetCellValue("道次"); lh1.CellStyle = st.SubHeaderStyle;
                    for (int ln = 1; ln <= laneCount; ln++) {
                        ICell c = lh.CreateCell(1 + ln);
                        c.SetCellValue(ln);
                        c.CellStyle = st.SubHeaderStyle;
                    }
                    r++;

                    bool isRelay = (ev.Key.EventName ?? "").IndexOf("接力", StringComparison.Ordinal) >= 0;
                    foreach (var heatGrp in ev.GroupBy(x => x.Heat).OrderBy(g => g.Key)) {
                        var byLane = heatGrp.ToDictionary(x => x.Lane, x => x);
                        // 行1：姓名
                        IRow nRow = sh.CreateRow(r);
                        ICell nA = nRow.CreateCell(0); nA.SetCellValue(heatGrp.Key + "组"); nA.CellStyle = st.HeatLabelStyle;
                        ICell nB = nRow.CreateCell(1); nB.SetCellValue("姓名"); nB.CellStyle = st.RoleLabelStyle;
                        // 接力时第一行只放代表队（队伍名）/ 队号；姓名放在多行
                        for (int ln = 1; ln <= laneCount; ln++) {
                            ICell cell = nRow.CreateCell(1 + ln);
                            if (byLane.ContainsKey(ln)) {
                                cell.SetCellValue(byLane[ln].Name ?? "");
                            }
                            cell.CellStyle = st.DataStyle;
                        }
                        r++;

                        // 接力：再加 4 棒姓名行（如果 Notes 中包含 "接力队 棒次:" 列出 4 棒，则展开）
                        // 简化：从 Name 字段中按 / 或 、 拆分；此处不依赖 Notes
                        if (isRelay) {
                            int maxLegs = 4;
                            for (int leg = 1; leg <= maxLegs; leg++) {
                                IRow legRow = sh.CreateRow(r);
                                ICell legA = legRow.CreateCell(0); legA.SetCellValue(""); legA.CellStyle = st.HeatLabelStyle;
                                ICell legB = legRow.CreateCell(1); legB.SetCellValue("第" + leg + "棒"); legB.CellStyle = st.RoleLabelStyle;
                                for (int ln = 1; ln <= laneCount; ln++) {
                                    ICell cell = legRow.CreateCell(1 + ln);
                                    if (byLane.ContainsKey(ln)) {
                                        var rec = byLane[ln];
                                        // 从 Notes 中尝试提取棒次队员（"接力队 棒次:张三、李四、王五、赵六"）
                                        var legs = ExtractRelayLegs(rec.Notes);
                                        if (legs != null && legs.Count >= leg) cell.SetCellValue(legs[leg - 1]);
                                    }
                                    cell.CellStyle = st.DataStyle;
                                }
                                r++;
                            }
                        }

                        // 代表队行
                        IRow tRow = sh.CreateRow(r);
                        ICell tA = tRow.CreateCell(0); tA.SetCellValue(""); tA.CellStyle = st.HeatLabelStyle;
                        ICell tB = tRow.CreateCell(1); tB.SetCellValue("代表队"); tB.CellStyle = st.RoleLabelStyle;
                        for (int ln = 1; ln <= laneCount; ln++) {
                            ICell cell = tRow.CreateCell(1 + ln);
                            if (byLane.ContainsKey(ln)) {
                                cell.SetCellValue(byLane[ln].Country ?? "");
                            }
                            cell.CellStyle = st.DataStyle;
                        }
                        r++;
                    }
                    r++; // 项目段间隔
                }
            }

            // 列宽
            sh.SetColumnWidth(0, 6 * 256);
            sh.SetColumnWidth(1, 8 * 256);
            for (int ln = 1; ln <= laneCount; ln++) sh.SetColumnWidth(1 + ln, 12 * 256);
        }

        private static List<string> ExtractRelayLegs(string notes) {
            if (string.IsNullOrEmpty(notes)) return null;
            const string p = "接力队 棒次:";
            int i = notes.IndexOf(p);
            if (i < 0) return null;
            string body = notes.Substring(i + p.Length);
            return body.Split(new[] { '/', '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        private static void BuildSheet3(ISheet sh, Styles st, IEnumerable<EventInfo> events, IEnumerable<string> teams) {
            int r = 0;
            IRow t = sh.CreateRow(r++);
            ICell tc = t.CreateCell(0);
            tc.SetCellValue("分组表填写说明");
            tc.CellStyle = st.TitleStyle;
            sh.AddMergedRegion(new CellRangeAddress(0, 0, 0, 5));
            r++;

            string[] notes = new[] {
                "1. 主导入数据来自第 1 个工作表「分组明细」(每行一个运动员)。",
                "2. 必填列：性别 / 项目 / 赛次 / 组号 / 道次。",
                "3. 运动员匹配优先级：参赛号 → 「姓名+代表队」。匹配失败可在导入对话框中选择 \"未注册时自动添加\"。",
                "4. 接力项目：「姓名」可以是代表队/接力队号；4 棒姓名可写在 \"备注\" 中（用 / 或 、 分隔）。",
                "5. 项目编号需与赛程编号一致；填写错误时会自动忽略并按性别+项目+赛次匹配。",
                "6. 工作表「分组表(网格)」是面向裁判打印/审核的视图，仅导出，导入时不读取。",
                "7. 出生年月格式建议为 yyyy-MM-dd；年龄列在导入时若空白会按出生日期推算。"
            };
            foreach (var n in notes) {
                IRow row = sh.CreateRow(r++);
                ICell c = row.CreateCell(0);
                c.SetCellValue(n);
                c.CellStyle = st.NoteStyle;
                sh.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 5));
            }
            r++;

            IRow eh = sh.CreateRow(r++);
            ICell ehc = eh.CreateCell(0); ehc.SetCellValue("本次比赛项目列表"); ehc.CellStyle = st.SectionStyle;
            sh.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 5));
            IRow eh2 = sh.CreateRow(r++);
            string[] eHeaders = { "项目编号", "组别", "性别", "项目", "赛次", "排序方式" };
            for (int i = 0; i < eHeaders.Length; i++) {
                ICell c = eh2.CreateCell(i); c.SetCellValue(eHeaders[i]); c.CellStyle = st.HeaderStyle;
            }
            foreach (var ev in events ?? new List<EventInfo>()) {
                IRow er = sh.CreateRow(r++);
                er.CreateCell(0).SetCellValue(ev.Number);
                er.CreateCell(1).SetCellValue(ev.AgeGroup ?? "");
                er.CreateCell(2).SetCellValue(ev.Gender ?? "");
                er.CreateCell(3).SetCellValue(ev.EventName ?? "");
                er.CreateCell(4).SetCellValue(ev.Stage ?? "");
                er.CreateCell(5).SetCellValue(ev.SortMethod ?? "");
            }
            r++;

            IRow th = sh.CreateRow(r++);
            ICell thc = th.CreateCell(0); thc.SetCellValue("已注册代表队"); thc.CellStyle = st.SectionStyle;
            sh.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 5));
            int colIdx = 0;
            IRow tr = sh.CreateRow(r);
            foreach (var team in teams ?? new List<string>()) {
                if (colIdx >= 6) { r++; tr = sh.CreateRow(r); colIdx = 0; }
                tr.CreateCell(colIdx++).SetCellValue(team);
            }

            int[] widths = { 12, 12, 8, 24, 10, 14 };
            for (int i = 0; i < widths.Length; i++) sh.SetColumnWidth(i, widths[i] * 256);
        }

        // ───── 单元格读取辅助 ─────
        private static string CellStr(IRow row, int c) {
            if (c < 0 || row == null) return "";
            ICell cell = row.GetCell(c);
            if (cell == null) return "";
            try {
                if (cell.CellType == CellType.Numeric) {
                    if (DateUtil.IsCellDateFormatted(cell)) return cell.DateCellValue.ToString("yyyy-MM-dd");
                    double d = cell.NumericCellValue;
                    if (d == Math.Floor(d)) return ((long)d).ToString();
                    return d.ToString();
                }
                return (cell.ToString() ?? "").Trim();
            } catch { return ""; }
        }

        private static int CellInt(IRow row, int c) {
            string s = CellStr(row, c); int v;
            return int.TryParse(s, out v) ? v : 0;
        }

        // ───── 样式工厂 ─────
        private class Styles
        {
            public ICellStyle HeaderStyle, TitleStyle, SubtitleStyle, SessionStyle, EventStyle,
                              SubHeaderStyle, HeatLabelStyle, RoleLabelStyle, DataStyle,
                              NoteStyle, SectionStyle;

            public Styles(IWorkbook wb) {
                IFont fontTitle = wb.CreateFont(); fontTitle.IsBold = true; fontTitle.FontHeightInPoints = 16;
                IFont fontSub = wb.CreateFont(); fontSub.FontHeightInPoints = 11; fontSub.IsItalic = true;
                IFont fontSession = wb.CreateFont(); fontSession.IsBold = true; fontSession.FontHeightInPoints = 13;
                IFont fontEvent = wb.CreateFont(); fontEvent.IsBold = true; fontEvent.FontHeightInPoints = 11;
                IFont fontHeader = wb.CreateFont(); fontHeader.IsBold = true; fontHeader.FontHeightInPoints = 10;

                TitleStyle = wb.CreateCellStyle();
                TitleStyle.SetFont(fontTitle); TitleStyle.Alignment = HorizontalAlignment.Center; TitleStyle.VerticalAlignment = VerticalAlignment.Center;

                SubtitleStyle = wb.CreateCellStyle();
                SubtitleStyle.SetFont(fontSub); SubtitleStyle.Alignment = HorizontalAlignment.Center;

                SessionStyle = wb.CreateCellStyle();
                SessionStyle.SetFont(fontSession); SessionStyle.Alignment = HorizontalAlignment.Center;
                SessionStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightYellow.Index;
                SessionStyle.FillPattern = FillPattern.SolidForeground;
                AllBorders(SessionStyle);

                EventStyle = wb.CreateCellStyle();
                EventStyle.SetFont(fontEvent); EventStyle.Alignment = HorizontalAlignment.Left;
                EventStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.PaleBlue.Index;
                EventStyle.FillPattern = FillPattern.SolidForeground;
                AllBorders(EventStyle);

                HeaderStyle = wb.CreateCellStyle();
                HeaderStyle.SetFont(fontHeader); HeaderStyle.Alignment = HorizontalAlignment.Center;
                HeaderStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
                HeaderStyle.FillPattern = FillPattern.SolidForeground;
                AllBorders(HeaderStyle);

                SubHeaderStyle = wb.CreateCellStyle();
                SubHeaderStyle.SetFont(fontHeader); SubHeaderStyle.Alignment = HorizontalAlignment.Center;
                SubHeaderStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
                SubHeaderStyle.FillPattern = FillPattern.SolidForeground;
                AllBorders(SubHeaderStyle);

                HeatLabelStyle = wb.CreateCellStyle();
                HeatLabelStyle.SetFont(fontEvent); HeatLabelStyle.Alignment = HorizontalAlignment.Center;
                AllBorders(HeatLabelStyle);

                RoleLabelStyle = wb.CreateCellStyle();
                RoleLabelStyle.Alignment = HorizontalAlignment.Center;
                RoleLabelStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
                RoleLabelStyle.FillPattern = FillPattern.SolidForeground;
                AllBorders(RoleLabelStyle);

                DataStyle = wb.CreateCellStyle();
                DataStyle.Alignment = HorizontalAlignment.Center; DataStyle.WrapText = true;
                AllBorders(DataStyle);

                NoteStyle = wb.CreateCellStyle();
                NoteStyle.WrapText = true; NoteStyle.Alignment = HorizontalAlignment.Left;

                SectionStyle = wb.CreateCellStyle();
                SectionStyle.SetFont(fontSession); SectionStyle.Alignment = HorizontalAlignment.Left;
                SectionStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightYellow.Index;
                SectionStyle.FillPattern = FillPattern.SolidForeground;
            }

            private static void AllBorders(ICellStyle s) {
                s.BorderBottom = BorderStyle.Thin;
                s.BorderTop = BorderStyle.Thin;
                s.BorderLeft = BorderStyle.Thin;
                s.BorderRight = BorderStyle.Thin;
            }
        }
    }
}
