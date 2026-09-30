using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace SwimmingScoreboard
{
    // 2026-08-21 接力棒次填报表 —— 导出 / 读入
    //
    // 由来：接力名单赛前一小时才按场次发下来（如 22 日上午只拿到第 1 场的），
    //       31 支队 124 个名字手工敲既来不及也容易串行。
    // 做法：一场一个 Excel 文件，文件内按项目分块；队名/组次/泳道都已填好，
    //       只留「第1棒~第4棒」四列空着（黄底）。填完存盘，程序一键读回。
    //
    // 为什么用 NPOI 不用 Excel COM：现场机器不一定装 Microsoft Excel（多半只有 WPS），
    //   NPOI 读写 .xls/.xlsx 不依赖任何 Office，WPS 存出来的文件也照样能读。
    //
    // 解析的稳妥之处：
    //   · 表头按**列名**找，不认死列号 —— WPS 存盘挪了列也不影响；
    //   · 数据行靠「场次」是不是数字来判定，项目标题行/空行自然被跳过；
    //   · 四个关键列（代表队/项目/性别/组别）原样带在每一行上，不靠上下文推断。
    public static class RelayLegSheetService
    {
        public const string SheetName = "接力棒次";

        // 2026-09-30 现场反馈: English模式导出这份Excel表头还是中文——原来是 static readonly
        //   字段, 改成按当前语言现取; Import() 那边把实际表头文字(不管中/英)都规整回中文
        //   canonical名, 下面一大堆 col["代表队"]/col["第1棒"] 这类中文字面量查表就不用再改。
        private static readonly string[] HeaderKeys = new[] {
            "Str_DocC_ColSession", "Str_RelaySheet_ColEvNum", "Str_RelaySheet_ColHeatNum", "Str_RelaySheet_ColLane", "Str_Col_Event",
            "Str_DocC_ColGender", "Str_Col_Group", "Str_Col_Team",
            "Str_RegTool_Leg1", "Str_RegTool_Leg2", "Str_RegTool_Leg3", "Str_RegTool_Leg4"
        };
        public static string[] Headers { get { return HeaderKeys.Select(Loc.T).ToArray(); } }
        private static Dictionary<string, string> BuildHeaderAliasToZh() {
            var d = new Dictionary<string, string>();
            foreach (var k in HeaderKeys) {
                string zh = Loc.TChinese(k), en = Loc.TEnglish(k);
                d[zh] = zh;
                if (!d.ContainsKey(en)) d[en] = zh;
            }
            return d;
        }
        private static readonly Dictionary<string, string> HeaderAliasToZh = BuildHeaderAliasToZh();

        public class LegRow
        {
            public int Session;
            public int EvNum;
            public int Heat;
            public int Lane;
            public string EventName;
            public string Gender;
            public string AgeGroup;
            public string TeamName;
            public string[] Legs = new string[4];
            public int ExcelRow;          // 1-based，报错时告诉用户是第几行
        }

        // ───────────────────────── 导出 ─────────────────────────
        // rows 需已按 项次 → 组次 → 泳道 排好；本方法只负责一个场次的一个文件
        public static void ExportSession(string path, string competitionName,
                                         int session, string sessionName, string date, string time,
                                         IList<LegRow> rows)
        {
            var wb = new XSSFWorkbook();
            var sh = wb.CreateSheet(SheetName);

            var fTitle = wb.CreateFont(); fTitle.FontHeightInPoints = 16; fTitle.IsBold = true;
            var fSub   = wb.CreateFont(); fSub.FontHeightInPoints = 11;
            var fGroup = wb.CreateFont(); fGroup.FontHeightInPoints = 12; fGroup.IsBold = true;
            var fHead  = wb.CreateFont(); fHead.FontHeightInPoints = 11; fHead.IsBold = true;

            var stTitle = wb.CreateCellStyle(); stTitle.SetFont(fTitle);
            stTitle.Alignment = HorizontalAlignment.Center; stTitle.VerticalAlignment = VerticalAlignment.Center;

            var stSub = wb.CreateCellStyle(); stSub.SetFont(fSub);
            stSub.Alignment = HorizontalAlignment.Center;

            var stGroup = wb.CreateCellStyle(); stGroup.SetFont(fGroup);
            stGroup.Alignment = HorizontalAlignment.Left; stGroup.VerticalAlignment = VerticalAlignment.Center;
            stGroup.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
            stGroup.FillPattern = FillPattern.SolidForeground;

            var stHead = wb.CreateCellStyle(); stHead.SetFont(fHead);
            stHead.Alignment = HorizontalAlignment.Center; stHead.VerticalAlignment = VerticalAlignment.Center;
            stHead.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightBlue.Index;
            stHead.FillPattern = FillPattern.SolidForeground;
            Box(stHead);

            var stKey = wb.CreateCellStyle();          // 已填好的列：浅灰，提示"别动"
            stKey.Alignment = HorizontalAlignment.Center; stKey.VerticalAlignment = VerticalAlignment.Center;
            stKey.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
            stKey.FillPattern = FillPattern.SolidForeground;
            Box(stKey);

            var stTeam = wb.CreateCellStyle();
            stTeam.Alignment = HorizontalAlignment.Center; stTeam.VerticalAlignment = VerticalAlignment.Center;
            stTeam.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
            stTeam.FillPattern = FillPattern.SolidForeground;
            Box(stTeam);

            var stInput = wb.CreateCellStyle();        // 要填的四列：黄底
            stInput.Alignment = HorizontalAlignment.Center; stInput.VerticalAlignment = VerticalAlignment.Center;
            stInput.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.LightYellow.Index;
            stInput.FillPattern = FillPattern.SolidForeground;
            Box(stInput);

            int last = Headers.Length - 1;
            int r = 0;

            var rowT = sh.CreateRow(r); rowT.HeightInPoints = 26;
            SetCell(rowT, 0, competitionName + "   接力棒次填报表", stTitle);
            sh.AddMergedRegion(new CellRangeAddress(r, r, 0, last));
            r++;

            var rowS = sh.CreateRow(r); rowS.HeightInPoints = 18;
            SetCell(rowS, 0, string.Format("{0}    {1} {2}    共 {3} 支队    ——  只填黄色的「第1棒~第4棒」四列，其余请勿改动",
                string.IsNullOrEmpty(sessionName) ? ("第" + session + "场") : sessionName, date, time, rows.Count), stSub);
            sh.AddMergedRegion(new CellRangeAddress(r, r, 0, last));
            r++;
            r++;   // 空一行

            int headerRowIndex = -1;
            string curKey = null;

            foreach (var d in rows)
            {
                string key = d.EvNum + "|" + d.EventName + "|" + d.Gender + "|" + d.AgeGroup;
                if (key != curKey)
                {
                    curKey = key;
                    if (headerRowIndex >= 0) r++;             // 上一个项目块后空一行
                    var rg = sh.CreateRow(r); rg.HeightInPoints = 20;
                    SetCell(rg, 0, string.Format("第 {0} 项    {1} {2} {3}    决赛", d.EvNum, d.Gender, d.AgeGroup, d.EventName), stGroup);
                    sh.AddMergedRegion(new CellRangeAddress(r, r, 0, last));
                    r++;

                    var rh = sh.CreateRow(r); rh.HeightInPoints = 18;
                    for (int c = 0; c < Headers.Length; c++) SetCell(rh, c, Headers[c], stHead);
                    if (headerRowIndex < 0) headerRowIndex = r;
                    r++;
                }

                var rw = sh.CreateRow(r); rw.HeightInPoints = 17;
                SetCell(rw, 0, d.Session.ToString(), stKey);
                SetCell(rw, 1, d.EvNum.ToString(), stKey);
                SetCell(rw, 2, d.Heat.ToString(), stKey);
                SetCell(rw, 3, d.Lane.ToString(), stKey);
                SetCell(rw, 4, d.EventName ?? "", stKey);
                SetCell(rw, 5, d.Gender ?? "", stKey);
                SetCell(rw, 6, d.AgeGroup ?? "", stKey);
                SetCell(rw, 7, d.TeamName ?? "", stTeam);
                for (int i = 0; i < 4; i++) SetCell(rw, 8 + i, d.Legs[i] ?? "", stInput);
                r++;
            }

            int[] widths = { 6, 6, 6, 6, 22, 8, 10, 12, 14, 14, 14, 14 };
            for (int c = 0; c < widths.Length; c++) sh.SetColumnWidth(c, widths[c] * 256);
            if (headerRowIndex >= 0) sh.CreateFreezePane(0, headerRowIndex + 1);

            WriteHelpSheet(wb);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) wb.Write(fs);
        }

        private static void WriteHelpSheet(IWorkbook wb)
        {
            var sh = wb.CreateSheet("填写说明");
            string[] lines = {
                "接力棒次填报表 — 填写说明",
                "",
                "1. 只填黄色的「第1棒」「第2棒」「第3棒」「第4棒」四列，按实际出场棒次顺序填姓名。",
                "2. 灰色各列（场次/项次/组次/泳道/项目/性别/组别/代表队）是程序用来对号入座的，请勿改动，",
                "   也不要插入/删除列、不要改表头文字。行的顺序可以不管，程序按「代表队+项目+性别+组别」认队。",
                "3. 没拿到名单的队伍空着即可 —— 空行会整行跳过，不会把已有的名单清掉。",
                "4. 一个文件只管一个场次。拿到哪一场的名单就填哪个文件。",
                "5. 填完存盘（xlsx 或 xls 都行，WPS 存的也认），回程序：",
                "       接力队管理  →  读入棒次名单  →  选这个文件",
                "6. 读入后对话框会报「第N场：X 支」，和你填的支数对一下。",
                "7. 姓名里的空格会自动去掉（如「尤  艺」按「尤艺」处理）。",
                "",
                "注意：本表用 NPOI 生成/读取，现场机器不需要装 Microsoft Excel，",
                "      用 WPS 表格打开编辑保存同样可以读回。"
            };
            for (int i = 0; i < lines.Length; i++) {
                var row = sh.CreateRow(i);
                var cell = row.CreateCell(0);
                cell.SetCellValue(lines[i]);
            }
            sh.SetColumnWidth(0, 100 * 256);
        }

        // ───────────────────────── 读入 ─────────────────────────
        public static List<LegRow> Import(string path, out string warning)
        {
            warning = "";
            var list = new List<LegRow>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                IWorkbook wb = path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
                    ? (IWorkbook)new NPOI.HSSF.UserModel.HSSFWorkbook(fs)
                    : new XSSFWorkbook(fs);

                ISheet sh = wb.GetSheet(SheetName);
                if (sh == null) {
                    // 表名被改过就退回第一个非"填写说明"的表
                    for (int i = 0; i < wb.NumberOfSheets; i++) {
                        var s = wb.GetSheetAt(i);
                        if (s != null && s.SheetName != "填写说明") { sh = s; break; }
                    }
                }
                if (sh == null) { warning = "文件里没有可用的工作表"; return list; }

                // 表头行：从上往下找第一行同时含「代表队」和「第1棒」的
                int headRow = -1;
                var col = new Dictionary<string, int>();
                for (int rr = sh.FirstRowNum; rr <= Math.Min(sh.LastRowNum, sh.FirstRowNum + 30); rr++) {
                    var row = sh.GetRow(rr);
                    if (row == null) continue;
                    var m = new Dictionary<string, int>();
                    for (int c = 0; c < row.LastCellNum; c++) {
                        string h = CellStr(row, c);
                        string canon;
                        if (h.Length > 0 && HeaderAliasToZh.TryGetValue(h, out canon)) h = canon;
                        if (h.Length > 0 && !m.ContainsKey(h)) m[h] = c;
                    }
                    if (m.ContainsKey("代表队") && m.ContainsKey("第1棒")) { headRow = rr; col = m; break; }
                }
                if (headRow < 0) { warning = "找不到表头行（需要同时有「代表队」和「第1棒」两列）"; return list; }

                foreach (var need in new[] { "场次", "项目", "性别", "组别", "代表队", "第1棒", "第2棒", "第3棒", "第4棒" }) {
                    if (!col.ContainsKey(need)) { warning = "表头缺少必需的列：" + need; return list; }
                }

                for (int rr = headRow + 1; rr <= sh.LastRowNum; rr++) {
                    var row = sh.GetRow(rr);
                    if (row == null) continue;
                    // 数据行判据：「场次」是数字。项目标题行、空行、重复表头都会被这一条挡掉
                    int ses;
                    if (!int.TryParse(CellStr(row, col["场次"]), out ses)) continue;
                    string team = CellStr(row, col["代表队"]);
                    string ev = CellStr(row, col["项目"]);
                    if (team.Length == 0 || ev.Length == 0) continue;

                    var d = new LegRow {
                        Session = ses,
                        EvNum = col.ContainsKey("项次") ? CellInt(row, col["项次"]) : 0,
                        Heat = col.ContainsKey("组次") ? CellInt(row, col["组次"]) : 0,
                        Lane = col.ContainsKey("泳道") ? CellInt(row, col["泳道"]) : 0,
                        EventName = ev,
                        Gender = CellStr(row, col["性别"]),
                        AgeGroup = CellStr(row, col["组别"]),
                        TeamName = team,
                        ExcelRow = rr + 1
                    };
                    for (int i = 0; i < 4; i++)
                        d.Legs[i] = CellStr(row, col["第" + (i + 1) + "棒"]).Replace(" ", "").Replace("　", "");
                    list.Add(d);
                }
            }
            return list;
        }

        // ───────────────────────── 小工具 ─────────────────────────
        private static void Box(ICellStyle st) {
            st.BorderTop = BorderStyle.Thin; st.BorderBottom = BorderStyle.Thin;
            st.BorderLeft = BorderStyle.Thin; st.BorderRight = BorderStyle.Thin;
        }
        private static void SetCell(IRow row, int c, string v, ICellStyle st) {
            var cell = row.CreateCell(c);
            cell.SetCellValue(v ?? "");
            if (st != null) cell.CellStyle = st;
        }
        private static string CellStr(IRow row, int c) {
            if (row == null || c < 0) return "";
            ICell cell = row.GetCell(c);
            if (cell == null) return "";
            try {
                if (cell.CellType == CellType.Numeric) {
                    double d = cell.NumericCellValue;
                    if (d == Math.Floor(d)) return ((long)d).ToString();
                    return d.ToString();
                }
                return (cell.ToString() ?? "").Trim();
            } catch { return ""; }
        }
        private static int CellInt(IRow row, int c) {
            int v; return int.TryParse(CellStr(row, c), out v) ? v : 0;
        }
    }
}
