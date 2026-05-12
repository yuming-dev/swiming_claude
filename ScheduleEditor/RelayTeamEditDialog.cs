using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace ScheduleEditor
{
    // 接力队 新增 / 编辑：队名 + 项目 + 性别 + 报名成绩 + 4 棒队员
    public class RelayTeamEditDialog : Window
    {
        private TextBox _team, _entry;
        private ComboBox _eventCombo, _genderCombo;
        private TextBox[] _legName = new TextBox[4];
        private TextBox[] _legBib = new TextBox[4];
        private TextBox[] _legId = new TextBox[4];
        private TextBox[] _legDob = new TextBox[4];
        private bool _editingExisting;

        public Dictionary<string, object> Result { get; private set; }

        public RelayTeamEditDialog(RelayRow existing, JObject data) {
            _editingExisting = (existing != null);
            Title = _editingExisting
                ? string.Format("编辑接力队 — {0} / {1}", existing.TeamName, existing.EventName)
                : "新增接力队";
            Width = 640; Height = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var sp = new StackPanel { Margin = new Thickness(20) };

            sp.Children.Add(new TextBlock {
                Text = _editingExisting ? "编辑接力队" : "新增接力队",
                FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 0, 12)
            });

            _team = AddTextRow(sp, "代表队 / 队名 *", existing != null ? existing.TeamName : "");
            // 编辑模式队名变灰（避免改主键）
            if (_editingExisting) _team.IsReadOnly = true;
            _genderCombo = AddComboRow(sp, "性别 *", ToList(data != null ? data["genderList"] as JArray : null),
                existing != null ? existing.Gender : "男");
            if (_editingExisting) _genderCombo.IsEnabled = false;

            // 项目下拉：只列接力项目
            var relayEvents = new List<string>();
            var allEv = ToList(data != null ? data["eventList"] as JArray : null);
            foreach (var ev in allEv) {
                if (ev.IndexOf('×') >= 0 || ev.ToLower().IndexOf('x') >= 0) relayEvents.Add(ev);
            }
            if (relayEvents.Count == 0) {
                relayEvents.AddRange(new[] { "4×50米自由泳接力", "4×100米自由泳接力",
                    "4×200米自由泳接力", "4×100米混合泳接力" });
            }
            _eventCombo = AddComboRow(sp, "项目 *", relayEvents,
                existing != null ? existing.EventName : relayEvents[0]);
            if (_editingExisting) _eventCombo.IsEnabled = false;

            _entry = AddTextRow(sp, "报名成绩 (mm:ss.ff)", existing != null ? existing.EntryTime : "");

            // 4 棒
            sp.Children.Add(new TextBlock {
                Text = "队员（4 棒）", FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                Margin = new Thickness(0, 14, 0, 6)
            });

            for (int i = 0; i < 4; i++) {
                int idx = i;
                var legGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                legGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });    // 第N棒
                legGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 姓名
                legGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });    // 编号
                legGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });   // 身份证
                legGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });    // 出生年月

                var lbl = new TextBlock {
                    Text = string.Format("第{0}棒", idx + 1), FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lbl, 0);
                legGrid.Children.Add(lbl);

                _legName[idx] = MakeMini("姓名"); Grid.SetColumn(_legName[idx], 1); legGrid.Children.Add(_legName[idx]);
                _legBib[idx]  = MakeMini("编号"); Grid.SetColumn(_legBib[idx],  2); legGrid.Children.Add(_legBib[idx]);
                _legId[idx]   = MakeMini("身份证"); Grid.SetColumn(_legId[idx],  3); legGrid.Children.Add(_legId[idx]);
                _legDob[idx]  = MakeMini("出生(yyyy-MM-dd)"); Grid.SetColumn(_legDob[idx], 4); legGrid.Children.Add(_legDob[idx]);

                sp.Children.Add(legGrid);

                // 编辑模式回填
                if (_editingExisting) {
                    if (idx == 0) { _legName[idx].Text = existing.Leg1 ?? ""; _legBib[idx].Text = existing.Leg1Bib ?? ""; _legId[idx].Text = existing.Leg1Id ?? ""; _legDob[idx].Text = existing.Leg1Dob ?? ""; }
                    else if (idx == 1) { _legName[idx].Text = existing.Leg2 ?? ""; _legBib[idx].Text = existing.Leg2Bib ?? ""; _legId[idx].Text = existing.Leg2Id ?? ""; _legDob[idx].Text = existing.Leg2Dob ?? ""; }
                    else if (idx == 2) { _legName[idx].Text = existing.Leg3 ?? ""; _legBib[idx].Text = existing.Leg3Bib ?? ""; _legId[idx].Text = existing.Leg3Id ?? ""; _legDob[idx].Text = existing.Leg3Dob ?? ""; }
                    else if (idx == 3) { _legName[idx].Text = existing.Leg4 ?? ""; _legBib[idx].Text = existing.Leg4Bib ?? ""; _legId[idx].Text = existing.Leg4Id ?? ""; _legDob[idx].Text = existing.Leg4Dob ?? ""; }
                }
            }

            // 按钮
            var btnPanel = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0)
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

        private TextBox MakeMini(string placeholder) {
            return new TextBox {
                Padding = new Thickness(4), FontSize = 12, Margin = new Thickness(4, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                ToolTip = placeholder
            };
        }

        private void OnOk(object sender, RoutedEventArgs e) {
            string team = (_team.Text ?? "").Trim();
            string ev = _eventCombo.SelectedItem as string ?? "";
            string gender = _genderCombo.SelectedItem as string ?? "男";
            if (string.IsNullOrEmpty(team)) { MessageBox.Show("队名不能为空", "提示"); return; }
            if (string.IsNullOrEmpty(ev)) { MessageBox.Show("项目不能为空", "提示"); return; }

            var legs = new List<Dictionary<string, string>>();
            for (int i = 0; i < 4; i++) {
                string n = (_legName[i].Text ?? "").Trim();
                if (string.IsNullOrEmpty(n)) continue;
                legs.Add(new Dictionary<string, string> {
                    { "legOrder", (i + 1).ToString() },
                    { "swimmerName", n },
                    { "swimmerBibNumber", (_legBib[i].Text ?? "").Trim() },
                    { "swimmerIDNumber", (_legId[i].Text ?? "").Trim() },
                    { "swimmerBirthDate", (_legDob[i].Text ?? "").Trim() }
                });
            }
            if (legs.Count == 0) {
                MessageBox.Show("请至少填写 1 棒队员姓名", "提示");
                return;
            }

            Result = new Dictionary<string, object> {
                { "teamName", team },
                { "gender", gender },
                { "eventName", ev },
                { "entryTime", _entry.Text ?? "" },
                { "legs", legs }
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
