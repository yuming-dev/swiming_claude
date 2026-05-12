using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace ScheduleEditor
{
    // 新增 / 编辑运动员对话框 — 简洁版本，把字段表单一次性放好
    // 提交时把字段打包到 Result 字典里，由 MainWindow 通过 WebSocket 发回主服务器
    public class SwimmerEditDialog : Window
    {
        private TextBox _bib, _name, _country, _birth, _age, _idNum, _phone, _entryTime, _notes;
        private ComboBox _gender, _eventCombo, _stageCombo;
        private bool _isNew;

        public Dictionary<string, string> Result { get; private set; }

        public SwimmerEditDialog(SwimmerRow existing, JObject data) {
            _isNew = (existing == null);
            Title = _isNew ? "新增运动员" : "编辑运动员";
            Width = 520; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var sp = new StackPanel { Margin = new Thickness(20) };

            sp.Children.Add(MakeTitle(_isNew ? "新增运动员" : "编辑运动员: " + existing.Name));

            _bib = AddTextRow(sp, "编号", existing != null ? existing.BibNumber : "");
            _name = AddTextRow(sp, "姓名 *", existing != null ? existing.Name : "");
            _gender = AddComboRow(sp, "性别 *", ToList(data != null ? data["genderList"] as JArray : null),
                existing != null ? existing.Gender : "男");
            _birth = AddTextRow(sp, "出生年月 (yyyy-MM-dd)", existing != null ? existing.BirthDate : "");
            _age = AddTextRow(sp, "年龄（可选，留空按出生年月算）", existing != null ? existing.Age : "");
            _country = AddTextRow(sp, "代表队 *", existing != null ? existing.Country : "");
            _eventCombo = AddComboRow(sp, "项目 *", ToList(data != null ? data["eventList"] as JArray : null),
                existing != null ? existing.EventName : "");
            _stageCombo = AddComboRow(sp, "赛次", ToList(data != null ? data["stageList"] as JArray : null),
                existing != null ? existing.CurrentStage : "决赛");
            _entryTime = AddTextRow(sp, "报名成绩 (mm:ss.ff)", existing != null ? existing.EntryTime : "");
            _idNum = AddTextRow(sp, "身份证号", existing != null ? existing.IDNumber : "");
            _phone = AddTextRow(sp, "电话", existing != null ? existing.Phone : "");
            _notes = AddTextRow(sp, "备注", existing != null ? existing.Notes : "");

            // 按钮
            var btnPanel = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
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

            scroll.Content = sp;
            Content = scroll;
        }

        private void OnOk(object sender, RoutedEventArgs e) {
            string name = (_name.Text ?? "").Trim();
            string country = (_country.Text ?? "").Trim();
            string ev = _eventCombo.SelectedItem as string ?? "";
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("姓名不能为空", "提示"); return; }
            if (string.IsNullOrEmpty(country)) { MessageBox.Show("代表队不能为空", "提示"); return; }
            if (string.IsNullOrEmpty(ev)) { MessageBox.Show("项目不能为空", "提示"); return; }

            Result = new Dictionary<string, string> {
                { "bibNumber", _bib.Text ?? "" },
                { "name", name },
                { "gender", _gender.SelectedItem as string ?? "男" },
                { "birthDate", _birth.Text ?? "" },
                { "age", _age.Text ?? "" },
                { "country", country },
                { "eventName", ev },
                { "currentStage", _stageCombo.SelectedItem as string ?? "决赛" },
                { "entryTime", _entryTime.Text ?? "" },
                { "idNumber", _idNum.Text ?? "" },
                { "phone", _phone.Text ?? "" },
                { "notes", _notes.Text ?? "" }
            };
            DialogResult = true;
            Close();
        }

        private static TextBlock MakeTitle(string s) {
            return new TextBlock {
                Text = s, FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 0, 16)
            };
        }

        private TextBox AddTextRow(StackPanel parent, string label, string value) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
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
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock {
                Text = label, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            var cb = new ComboBox { FontSize = 13, Padding = new Thickness(4) };
            if (items != null) foreach (var s in items) cb.Items.Add(s);
            if (!string.IsNullOrEmpty(selected)) {
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
