using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace ScheduleEditor
{
    // 新增 / 编辑纪录
    // 主键 = (eventName, gender, recordType, ageGroup)
    public class RecordEditDialog : Window
    {
        private TextBox _holderName, _holderCountry, _time, _date, _location;
        private ComboBox _eventCombo, _genderCombo, _recordTypeCombo, _ageGroupCombo;
        private bool _editingExisting;

        public Dictionary<string, string> Result { get; private set; }

        public RecordEditDialog(RecordRow existing, JObject data) {
            _editingExisting = (existing != null);
            Title = _editingExisting ? "编辑纪录" : "新增纪录";
            Width = 500; Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(new TextBlock {
                Text = _editingExisting ? "编辑纪录" : "新增纪录",
                FontSize = 17, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 0, 12)
            });

            var recordTypes = new List<string> { "WR", "AR", "NR", "CR", "MR" };
            _recordTypeCombo = AddComboRow(sp, "纪录类型 *", recordTypes,
                existing != null ? existing.RecordType : "WR");
            _genderCombo = AddComboRow(sp, "性别 *", ToList(data != null ? data["genderList"] as JArray : null),
                existing != null ? existing.Gender : "男");
            _eventCombo = AddComboRow(sp, "项目 *", ToList(data != null ? data["eventList"] as JArray : null),
                existing != null ? existing.EventName : "");
            var ageGroups = new List<string> { "" };
            ageGroups.AddRange(ToList(data != null ? data["ageGroups"] as JArray : null));
            _ageGroupCombo = AddComboRow(sp, "组别（可空）", ageGroups,
                existing != null ? existing.AgeGroup : "");

            if (_editingExisting) {
                // 锁主键
                _recordTypeCombo.IsEnabled = false;
                _genderCombo.IsEnabled = false;
                _eventCombo.IsEnabled = false;
                _ageGroupCombo.IsEnabled = false;
            }

            _holderName = AddTextRow(sp, "保持人姓名", existing != null ? existing.HolderName : "");
            _holderCountry = AddTextRow(sp, "代表队/国家", existing != null ? existing.HolderCountry : "");
            _time = AddTextRow(sp, "成绩 (mm:ss.ff) *", existing != null ? existing.TimeDisplay : "");
            _date = AddTextRow(sp, "日期 (yyyy-MM-dd)", existing != null ? existing.Date : "");
            _location = AddTextRow(sp, "地点", existing != null ? existing.Location : "");

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
            string rtype = _recordTypeCombo.SelectedItem as string ?? "";
            string time = (_time.Text ?? "").Trim();
            if (string.IsNullOrEmpty(ev)) { MessageBox.Show("请选择项目", "提示"); return; }
            if (string.IsNullOrEmpty(rtype)) { MessageBox.Show("请选择纪录类型", "提示"); return; }
            if (string.IsNullOrEmpty(time)) { MessageBox.Show("请填写成绩", "提示"); return; }

            Result = new Dictionary<string, string> {
                { "recordType", rtype },
                { "ageGroup", _ageGroupCombo.SelectedItem as string ?? "" },
                { "gender", _genderCombo.SelectedItem as string ?? "男" },
                { "eventName", ev },
                { "holderName", _holderName.Text ?? "" },
                { "holderCountry", _holderCountry.Text ?? "" },
                { "time", time },
                { "date", _date.Text ?? "" },
                { "location", _location.Text ?? "" }
            };
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
