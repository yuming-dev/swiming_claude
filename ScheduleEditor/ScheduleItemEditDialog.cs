using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace ScheduleEditor
{
    // 新增 / 编辑赛程项
    // 主键 = (Gender, EventName, Stage, AgeGroup)
    // 编辑模式下这 4 个字段锁住，避免改主键导致服务器侧创建新项而不是更新
    public class ScheduleItemEditDialog : Window
    {
        private TextBox _sessionNumber, _sessionName, _date, _time, _heatCount;
        private ComboBox _gender, _eventCombo, _stageCombo, _ageGroup;
        private CheckBox _isRelay;
        private bool _editingExisting;

        public Dictionary<string, object> Result { get; private set; }

        public ScheduleItemEditDialog(ScheduleRow existing, JObject data) {
            _editingExisting = (existing != null);
            Title = _editingExisting ? "编辑赛程项" : "新增赛程项";
            Width = 540; Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock {
                Text = _editingExisting ? "编辑赛程项" : "新增赛程项",
                FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 0, 12)
            });

            // 主键 4 项
            _gender = AddComboRow(sp, "性别 *", ToList(data != null ? data["genderList"] as JArray : null),
                existing != null ? existing.Gender : "男");
            _eventCombo = AddComboRow(sp, "项目 *", ToList(data != null ? data["eventList"] as JArray : null),
                existing != null ? existing.EventName : "");
            _stageCombo = AddComboRow(sp, "赛次 *", ToList(data != null ? data["stageList"] as JArray : null),
                existing != null ? existing.Stage : "预赛");
            // 组别下拉允许"空"（不限组别）
            var ageGroups = new List<string> { "" };
            ageGroups.AddRange(ToList(data != null ? data["ageGroups"] as JArray : null));
            _ageGroup = AddComboRow(sp, "组别（可空）", ageGroups, existing != null ? existing.AgeGroup : "");

            if (_editingExisting) {
                // 锁主键
                _gender.IsEnabled = false;
                _eventCombo.IsEnabled = false;
                _stageCombo.IsEnabled = false;
                _ageGroup.IsEnabled = false;
            }

            // 其它字段
            _sessionNumber = AddTextRow(sp, "单元#",
                existing != null && existing.SessionNumber > 0 ? existing.SessionNumber.ToString() : "");
            _sessionName = AddTextRow(sp, "单元名（如\"第1单元\"）",
                existing != null ? existing.SessionName : "");
            _date = AddTextRow(sp, "日期 (yyyy-MM-dd)",
                existing != null ? existing.Date : "");
            _time = AddTextRow(sp, "开始时间 (HH:mm)",
                existing != null ? existing.Time : "");
            _heatCount = AddTextRow(sp, "组数",
                existing != null && existing.HeatCount > 0 ? existing.HeatCount.ToString() : "1");

            _isRelay = new CheckBox {
                Content = "接力项目", FontSize = 13, Margin = new Thickness(0, 6, 0, 6),
                IsChecked = existing != null && existing.IsRelay
            };
            sp.Children.Add(_isRelay);

            // 按钮
            var btnPanel = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0)
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
            string ev = _eventCombo.SelectedItem as string ?? "";
            if (string.IsNullOrEmpty(ev)) { MessageBox.Show("请选择项目", "提示"); return; }
            int sn, hc;
            int.TryParse((_sessionNumber.Text ?? "").Trim(), out sn);
            int.TryParse((_heatCount.Text ?? "").Trim(), out hc);
            Result = new Dictionary<string, object> {
                { "sessionNumber", sn },
                { "sessionName", _sessionName.Text ?? "" },
                { "date", _date.Text ?? "" },
                { "time", _time.Text ?? "" },
                { "gender", _gender.SelectedItem as string ?? "男" },
                { "eventName", ev },
                { "stage", _stageCombo.SelectedItem as string ?? "预赛" },
                { "ageGroup", _ageGroup.SelectedItem as string ?? "" },
                { "heatCount", hc },
                { "isRelay", _isRelay.IsChecked == true }
            };
            DialogResult = true;
            Close();
        }

        private TextBox AddTextRow(StackPanel parent, string label, string value) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var cb = new ComboBox { FontSize = 13, Padding = new Thickness(4) };
            if (items != null) foreach (var s in items) cb.Items.Add(s ?? "");
            if (selected != null) {
                foreach (var it in cb.Items) if (string.Equals(it as string, selected)) { cb.SelectedItem = it; break; }
                if (cb.SelectedItem == null) { cb.Items.Add(selected); cb.SelectedItem = selected; }
            } else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
            Grid.SetColumn(cb, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(cb);
            parent.Children.Add(grid);
            return cb;
        }

        private static List<string> ToList(JArray arr) {
            var r = new List<string>();
            if (arr == null) return r;
            foreach (var t in arr) r.Add(t.ToString());
            return r;
        }
    }
}
