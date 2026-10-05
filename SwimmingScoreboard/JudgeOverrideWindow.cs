using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwimmingScoreboard
{
    /// <summary>
    /// 2026-09-16 一行可编辑的成绩(裁判长改成绩窗口用)。
    /// 只暴露"状态/最终成绩/备注"三个可改字段, 姓名/单位/道次只读——
    /// 这不是给日常改分用的, 是给"某个几周前就已经[已完赛]、连'解锁本组成绩'
    /// 都因为内存里的确认标记跟历史数据对不上而解不开"这种场外情况用的最后一道口子:
    /// 直接改库, 不走"解锁→改→重新确认"那条正常流程。
    /// </summary>
    public class JudgeEditRow
    {
        // 2026-09-16 【WPF 绑定的坑】DataGridTextColumn 的 Binding 只认属性(get/set),
        //   不认普通字段 —— 这几列原来写成字段(public long HeatEntryId; 等), 编译不报错,
        //   但界面上道次/姓名/单位三列永远显示空白(用户实拍到, 只有状态/最终成绩两个
        //   本来就是属性的列显示对了)。全部改成属性。
        public long HeatEntryId { get; set; }
        public int Lane { get; set; }
        public string Name { get; set; }
        public string UnitName { get; set; }
        public string StatusText { get; set; }
        public string FinalTimeText { get; set; }
        public string NoteText { get; set; }

        public string OriginalStatus;
        public string OriginalFinalTimeText;
        public string OriginalNoteText;
        public string OriginalRecordNote;
        public string OriginalDsqCode;
        public int OriginalDsqLeg;

        public bool Changed {
            get {
                return (StatusText ?? "") != (OriginalStatus ?? "")
                    || (FinalTimeText ?? "") != (OriginalFinalTimeText ?? "")
                    || (NoteText ?? "") != (OriginalNoteText ?? "");
            }
        }
    }

    /// <summary>
    /// 2026-09-16 "裁判长改成绩"—— 最高权限、跳过锁定状态直接改库的成绩修正窗口。
    ///
    /// 为什么要有这个口子: 正常改分走「解锁本组成绩→改→重新确认」。但这条路依赖内存里的
    /// _confirmedHeats(一份 JSON 快照恢复出来的"这组是不是已确认"记录) —— 一个几周前就已经
    /// 完赛的历史项目, 重新装载档案后这份内存记录可能跟赛程树上显示的[已完赛]对不上号,
    /// 「解锁本组成绩」点下去就直接被挡在"这组本来就没锁"或"正在计时"这类前置检查上,
    /// 根本走不到能改成绩那一步(用户实拍到)。
    ///
    /// 这个窗口不碰 _confirmedHeats/赛程树那套状态机, 直接对着竞赛库的 heat_entries 操作
    /// (通过 IMeetService.GetHeat / SaveResult —— 跟"赛后改成绩"用的是同一条写入通道,
    /// 一样要填修改理由、一样会记审计表、一样会触发 RecomputeRanks 重排名次), 不受
    /// "这组有没有解锁"这层状态影响。写完之后由调用方(MainWindow)负责重新生成组排名表、
    /// 刷新赛程树和"成绩与排名"表格。
    ///
    /// 权限: 打开前必须输入系统账号密码(跟 query.html/register.html 用的是同一套
    /// credentials.json, 见 AuthHelper) —— 这不是给日常改分用的, 只给裁判长处理
    /// 赛后争议/补救这种场外情况用, 密码门必须挡住普通操作员。
    /// </summary>
    public class JudgeOverrideWindow : Window
    {
        private readonly SwimmingScoreboard.Db.MeetDbBridge _meetDb;
        private readonly string _ageGroup, _gender, _eventName, _stage;
        private readonly int _heat;

        /// <summary>
        /// 保存成功后回调(ageGroup,gender,eventName,stage,heat) —— 主窗口据此把这一组从竞赛库
        /// 重新读回内存(Status/FinalTime/备注这些跟着刷新, 不然"成绩与排名"/大屏还是改之前
        /// 内存里的旧值 —— 这里写库了, 内存没人管), 再重新生成组排名表、刷新界面。
        /// </summary>
        public Action<string, string, string, string, int> AfterSaved;

        private readonly ObservableCollection<JudgeEditRow> _rows = new ObservableCollection<JudgeEditRow>();
        private DataGrid _grid;
        private TextBox _reasonBox;
        private TextBlock _tip;
        private Button _saveBtn;

        public JudgeOverrideWindow(SwimmingScoreboard.Db.MeetDbBridge meetDb,
            string ageGroup, string gender, string eventName, string stage, int heat)
        {
            _meetDb = meetDb; _ageGroup = ageGroup ?? ""; _gender = gender ?? ""; _eventName = eventName ?? ""; _stage = stage ?? ""; _heat = heat;

            Title = "裁判长成绩修改（最高权限，直接写库）";
            Width = 880; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            BuildUi();
            LoadRows();
        }

        private void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new TextBlock {
                Text = string.Format("{0} {1} {2} {3} 第{4}组 —— 直接改竞赛库, 不走「解锁本组成绩」那套状态检查。\n" +
                    "只给裁判长处理赛后争议/补救用, 改完会记入审计表(谁改的、改了什么、为什么)。",
                    _gender, string.IsNullOrEmpty(_ageGroup) ? "" : _ageGroup, _eventName, _stage, _heat),
                FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.Firebrick,
                Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(head, 0);
            root.Children.Add(head);

            _grid = new DataGrid {
                AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                ItemsSource = _rows, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                HeadersVisibility = DataGridHeadersVisibility.Column
            };
            _grid.Columns.Add(new DataGridTextColumn { Header = "道次", Binding = new System.Windows.Data.Binding("Lane"), IsReadOnly = true, Width = 50 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "姓名", Binding = new System.Windows.Data.Binding("Name"), IsReadOnly = true, Width = 110 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "单位", Binding = new System.Windows.Data.Binding("UnitName"), IsReadOnly = true, Width = 130 });
            // 2026-09-16 三列可编辑列的 Binding 都显式 TwoWay + UpdateSourceTrigger=PropertyChanged。
            //   原来用默认触发(文本列默认 LostFocus, combobox 默认 PropertyChanged 但没显式写) ——
            //   用户改了三行、存盘只认一行: 默认 LostFocus 要等这个单元格真正失去焦点(点别的格子/
            //   换行/Tab)才会把编辑框里的值推回 JudgeEditRow 对象, 如果在没离开焦点前就直接点了
            //   "确认写入数据库", 那一格改动还停在编辑控件里, 没写进对象, Changed 属性当然读不到。
            //   改成 PropertyChanged 后, 每敲一个字符/每选一次下拉都立刻同步进对象, 不再依赖
            //   "有没有点开别的格子"这个时机。
            var statusCol = new DataGridComboBoxColumn {
                Header = "状态(留空=正常)", Width = 130,
                ItemsSource = new[] { "", "DSQ", "DNF", "DNS", "TRI" },
                SelectedItemBinding = new System.Windows.Data.Binding("StatusText") {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                }
            };
            _grid.Columns.Add(statusCol);
            _grid.Columns.Add(new DataGridTextColumn {
                Header = "最终成绩(如 1:14.32 / 44.77)", Width = 190,
                Binding = new System.Windows.Data.Binding("FinalTimeText") {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                }
            });
            _grid.Columns.Add(new DataGridTextColumn {
                Header = "备注", Width = 150,
                Binding = new System.Windows.Data.Binding("NoteText") {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                }
            });
            Grid.SetRow(_grid, 1);
            root.Children.Add(_grid);

            var reasonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
            reasonRow.Children.Add(new TextBlock { Text = "修改原因(必填):", VerticalAlignment = VerticalAlignment.Center, Width = 110 });
            _reasonBox = new TextBox { Width = 500, Padding = new Thickness(4), FontSize = 13 };
            reasonRow.Children.Add(_reasonBox);
            Grid.SetRow(reasonRow, 2);
            root.Children.Add(reasonRow);

            _tip = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            _saveBtn = new Button {
                Content = "确认写入数据库", Width = 150, Height = 34, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold
            };
            var cancelBtn = new Button { Content = "取消(不保存)", Width = 120, Height = 34 };
            _saveBtn.Click += Save_Click;
            cancelBtn.Click += delegate { DialogResult = false; Close(); };
            btns.Children.Add(_saveBtn); btns.Children.Add(cancelBtn);

            var bottom = new StackPanel();
            bottom.Children.Add(_tip);
            bottom.Children.Add(btns);
            Grid.SetRow(bottom, 3);
            root.Children.Add(bottom);

            Content = root;
        }

        private void LoadRows()
        {
            _rows.Clear();
            try {
                long rid = _meetDb.ResolveRound(_ageGroup, _gender, _eventName, _stage);
                if (rid == 0) {
                    _tip.Text = "竞赛库里找不到这个项目/赛次 —— 可能项目还没建档, 或组别/性别/项目/赛次没选对。";
                    _saveBtn.IsEnabled = false;
                    return;
                }
                var list = _meetDb.Service.GetHeat(rid, _heat);
                if (list == null || list.Count == 0) {
                    _tip.Text = string.Format("第{0}组在竞赛库里没有道次记录。", _heat);
                    _saveBtn.IsEnabled = false;
                    return;
                }
                foreach (var r in list.Where(x => x.Lane != null && x.ReserveNo == null).OrderBy(x => x.Lane)) {
                    string ft = r.FinalTime > 0 ? TimeFormatter.Format(r.FinalTime) : "";
                    var row = new JudgeEditRow {
                        HeatEntryId = r.Id, Lane = r.Lane.Value, Name = r.Name ?? "", UnitName = r.UnitName ?? "",
                        StatusText = r.Status ?? "", FinalTimeText = ft, NoteText = r.Note ?? "",
                        OriginalStatus = r.Status ?? "", OriginalFinalTimeText = ft, OriginalNoteText = r.Note ?? "",
                        OriginalRecordNote = r.RecordNote ?? "", OriginalDsqCode = r.DsqCode ?? "", OriginalDsqLeg = r.DsqLeg
                    };
                    _rows.Add(row);
                }
                if (_rows.Count == 0) _tip.Text = "这一组没有正选道次(都是替补?)。";
            } catch (Exception ex) {
                _tip.Text = "读取失败: " + ex.Message;
                _saveBtn.IsEnabled = false;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 先提交单元格再提交行(顺序反了会在还有格子处于编辑态时提交行失败) ——
            // 配合上面 PropertyChanged 触发, 这里其实是双保险, 不是唯一防线。
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            string reason = (_reasonBox.Text ?? "").Trim();
            if (reason.Length < 2) {
                _tip.Text = "请把修改原因写清楚(至少两个字) —— 这条要进审计表。";
                _reasonBox.Focus();
                return;
            }

            var changed = _rows.Where(r => r.Changed).ToList();
            if (changed.Count == 0) {
                _tip.Text = "没有任何改动。";
                return;
            }

            if (AppMessageBox.Show(
                string.Format("确定要把这 {0} 条改动直接写入竞赛库吗？\n\n这个动作跳过「解锁本组成绩」的状态检查，" +
                    "改完立即生效，且会触发本项目名次重新计算。\n\n原因: {1}", changed.Count, reason),
                "确认写入(裁判长)", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            string op = "裁判长改成绩:" + Environment.MachineName;
            int ok = 0; var errors = new List<string>();
            foreach (var r in changed) {
                try {
                    bool judged = r.StatusText == "DSQ" || r.StatusText == "DNF" || r.StatusText == "DNS";
                    bool unranked = judged || r.StatusText == "TRI";
                    double ft = judged ? 0 : TimeFormatter.Parse(r.FinalTimeText);

                    var row = new SwimmingScoreboard.Db.LaneRow {
                        Id = r.HeatEntryId,
                        FinalTime = ft,
                        Status = r.StatusText ?? "",
                        // 判罚/试游不许带纪录标识进库 —— 跟 CommitHeatFrom 的同一条硬规矩
                        RecordNote = unranked ? "" : r.OriginalRecordNote,
                        DsqCode = r.StatusText == "DSQ" ? r.OriginalDsqCode : "",
                        DsqLeg = r.StatusText == "DSQ" ? r.OriginalDsqLeg : 0,
                        Note = r.NoteText ?? "",
                        DisputeNote = ""
                    };
                    _meetDb.Service.SaveResult(row, reason, op);
                    ok++;
                } catch (Exception ex) {
                    errors.Add(string.Format("道次{0} {1}: {2}", r.Lane, r.Name, ex.Message));
                }
            }

            if (errors.Count > 0) {
                _tip.Text = string.Format("写入完成 {0}/{1} 条, {2} 条失败:\n{3}",
                    ok, changed.Count, errors.Count, string.Join("\n", errors));
                if (ok == 0) return;
            }

            try { if (AfterSaved != null) AfterSaved(_ageGroup, _gender, _eventName, _stage, _heat); } catch { }

            AppMessageBox.Show(string.Format("已写入 {0} 条修改。\n\n名次已按新成绩重新计算；如本项目全部组已确认，" +
                "组排名表(定稿)也已同步重新生成。", ok),
                "裁判长改成绩", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
    }
}
