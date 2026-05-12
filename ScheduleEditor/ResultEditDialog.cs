using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScheduleEditor
{
    // 编辑成绩对话框 — 只改成绩字段（finalTime / status / recordNote）
    // 不改运动员姓名 / 项目 — 那些走 SwimmerEditDialog
    public class ResultEditDialog : Window
    {
        private TextBox _finalTime, _recordNote;
        private ComboBox _statusCombo;
        private string _eventName, _stage;
        private ResultRow _row;

        public Dictionary<string, string> Result { get; private set; }

        public ResultEditDialog(ResultRow row, string eventName, string stage) {
            _row = row; _eventName = eventName; _stage = stage;
            Title = string.Format("编辑成绩 — {0}({1})", row.Name, row.BibNumber);
            Width = 480; Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            var sp = new StackPanel { Margin = new Thickness(20) };

            // 头：当前运动员信息（只读）
            sp.Children.Add(new TextBlock {
                Text = string.Format("{0}({1})  {2}", row.Name, row.BibNumber, row.Country),
                FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            sp.Children.Add(new TextBlock {
                Text = string.Format("{0}  {1}组  道{2}", eventName, row.Heat, row.Lane),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            _finalTime = AddTextRow(sp, "成绩 (mm:ss.ff)", row.FinalTime ?? "");
            _statusCombo = AddComboRow(sp, "状态",
                new List<string> { "", "DNS", "DNF", "DSQ" },
                row.Status ?? "");
            _recordNote = AddTextRow(sp, "破纪录标识", row.RecordNote ?? "");
            sp.Children.Add(new TextBlock {
                Text = "示例：WR / =WR / WR/AR / NR / =CR 等；DSQ/DNS/DNF 优先级高于纪录标识",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            // 按钮
            var btnPanel = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var btnCancel = new Button {
                Content = "取消", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnCancel.Click += delegate { DialogResult = false; Close(); };
            var btnOK = new Button {
                Content = "确定", Padding = new Thickness(24, 6, 24, 6), IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            btnOK.Click += OnOk;
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOK);
            sp.Children.Add(btnPanel);

            Content = sp;
        }

        private void OnOk(object sender, RoutedEventArgs e) {
            int heat = 0;
            int.TryParse(_row.Heat, out heat);
            Result = new Dictionary<string, string> {
                { "bibNumber", _row.BibNumber ?? "" },
                { "eventName", _eventName ?? "" },
                { "stage", _stage ?? "" },
                { "heat", heat.ToString() },
                { "finalTime", _finalTime.Text ?? "" },
                { "status", _statusCombo.SelectedItem as string ?? "" },
                { "recordNote", _recordNote.Text ?? "" }
            };
            DialogResult = true;
            Close();
        }

        private TextBox AddTextRow(StackPanel parent, string label, string value) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var tb = new TextBox {
                Text = value ?? "", Padding = new Thickness(6), FontSize = 13,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1))
            };
            Grid.SetColumn(tb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(tb);
            parent.Children.Add(grid);
            return tb;
        }

        private ComboBox AddComboRow(StackPanel parent, string label, List<string> items, string selected) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var cb = new ComboBox { FontSize = 13, Padding = new Thickness(4) };
            foreach (var s in items) cb.Items.Add(s);
            foreach (var it in cb.Items) if (string.Equals(it as string, selected)) { cb.SelectedItem = it; break; }
            if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
            Grid.SetColumn(cb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(cb);
            parent.Children.Add(grid);
            return cb;
        }
    }

    // 晋级到下一赛次的小弹窗 — 输入 "晋级人数" + "目标赛次"
    public class PromotionDialog : Window
    {
        private TextBox _countBox;
        private ComboBox _stageCombo;
        public int PromoCount { get; private set; }
        public string NextStage { get; private set; }

        public PromotionDialog(string gender, string eventName, string fromStage) {
            Title = "晋级到下一赛次";
            Width = 380; Height = 240;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock {
                Text = string.Format("{0}  {1}  {2} → ?", gender, eventName, fromStage),
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            var nextDefault = fromStage == "预赛" ? "半决赛" : (fromStage == "半决赛" ? "决赛" : "决赛");
            _stageCombo = AddComboRow(sp, "目标赛次", new List<string> { "半决赛", "决赛" }, nextDefault);
            _countBox = AddTextRow(sp, "晋级人数（前 N 名）", "8");

            // 按钮
            var btnPanel = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var btnCancel = new Button {
                Content = "取消", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0)
            };
            btnCancel.Click += delegate { DialogResult = false; Close(); };
            var btnOK = new Button {
                Content = "执行晋级", Padding = new Thickness(20, 6, 20, 6), IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            btnOK.Click += OnOk;
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOK);
            sp.Children.Add(btnPanel);

            Content = sp;
        }

        private void OnOk(object sender, RoutedEventArgs e) {
            int n;
            if (!int.TryParse((_countBox.Text ?? "").Trim(), out n) || n <= 0) {
                MessageBox.Show("请填写有效的晋级人数（正整数）", "提示");
                return;
            }
            PromoCount = n;
            NextStage = _stageCombo.SelectedItem as string ?? "决赛";
            DialogResult = true;
            Close();
        }

        private TextBox AddTextRow(StackPanel parent, string label, string value) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var tb = new TextBox {
                Text = value ?? "", Padding = new Thickness(6), FontSize = 13,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1))
            };
            Grid.SetColumn(tb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(tb);
            parent.Children.Add(grid);
            return tb;
        }

        private ComboBox AddComboRow(StackPanel parent, string label, List<string> items, string selected) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var cb = new ComboBox { FontSize = 13, Padding = new Thickness(4) };
            foreach (var s in items) cb.Items.Add(s);
            foreach (var it in cb.Items) if (string.Equals(it as string, selected)) { cb.SelectedItem = it; break; }
            if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
            Grid.SetColumn(cb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(cb);
            parent.Children.Add(grid);
            return cb;
        }
    }
}
