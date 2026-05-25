using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SwimmingScoreboard
{
    // 2026-05-24 接力队编辑窗 — 双按钮 + 实时校验 + 二次确认
    public partial class EditRelayTeamWindow : Window
    {
        private readonly RelayTeam _team;
        private readonly Snapshot _backup;
        private readonly Brush _normalBorder;
        private static readonly Brush ErrorBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
        private bool _suppressValidate;

        // 留给外部读取的"用户最终确认"标志
        public bool Confirmed { get; private set; }

        public EditRelayTeamWindow(RelayTeam team, IEnumerable<string> events, IEnumerable<Unit> units) {
            InitializeComponent();
            _team = team;
            _backup = Snapshot.Capture(team);
            _normalBorder = new TextBox().BorderBrush;   // 取系统默认

            // 项目下拉：仅含「接力」的项目，按字母排序
            var relayEvents = (events ?? new string[0])
                .Where(ev => !string.IsNullOrEmpty(ev) && ev.Contains("接力"))
                .Distinct().OrderBy(e => e).ToList();
            foreach (var e in relayEvents) EventBox.Items.Add(e);
            // 当前项目若不在列表中（如手动旧数据），也加进来以免选不上
            if (!string.IsNullOrEmpty(team.EventName) && !EventBox.Items.Contains(team.EventName))
                EventBox.Items.Add(team.EventName);

            // 单位下拉：从 _units 取 Name 唯一列表
            var unitNames = (units ?? new Unit[0])
                .Where(u => u != null && !string.IsNullOrEmpty(u.Name))
                .Select(u => u.Name).Distinct().OrderBy(n => n).ToList();
            foreach (var n in unitNames) CountryBox.Items.Add(n);

            _suppressValidate = true;
            TeamNameBox.Text = team.TeamName ?? "";
            CountryBox.Text = team.Country ?? team.TeamName ?? "";
            EventBox.SelectedItem = team.EventName;
            foreach (ComboBoxItem item in GenderBox.Items) {
                if ((item.Content as string) == team.Gender) { GenderBox.SelectedItem = item; break; }
            }
            if (GenderBox.SelectedItem == null && GenderBox.Items.Count > 0) GenderBox.SelectedIndex = 0;
            EntryTimeBox.Text = team.EntryTime ?? "";

            // 4 棒姓名
            var legNames = new[] { "", "", "", "" };
            for (int i = 0; i < team.Legs.Count && i < 4; i++) legNames[i] = team.Legs[i].SwimmerName ?? "";
            Leg1Box.Text = legNames[0];
            Leg2Box.Text = legNames[1];
            Leg3Box.Text = legNames[2];
            Leg4Box.Text = legNames[3];

            // 单位 ComboBox 文本变化也触发校验
            CountryBox.AddHandler(TextBoxBase.TextChangedEvent,
                new System.Windows.Controls.TextChangedEventHandler(ValidateAll));

            _suppressValidate = false;
            ValidateAll(null, null);
        }

        public void ValidateAll(object sender, System.Windows.RoutedEventArgs e) {
            if (_suppressValidate) return;
            var errors = new List<string>();

            bool teamOk = !string.IsNullOrWhiteSpace(TeamNameBox.Text);
            TeamNameBox.BorderBrush = teamOk ? _normalBorder : ErrorBorder;
            TeamNameBox.BorderThickness = teamOk ? new Thickness(1) : new Thickness(2);
            if (!teamOk) errors.Add("队名");

            bool countryOk = !string.IsNullOrWhiteSpace(CountryBox.Text);
            CountryBox.BorderBrush = countryOk ? _normalBorder : ErrorBorder;
            CountryBox.BorderThickness = countryOk ? new Thickness(1) : new Thickness(2);
            if (!countryOk) errors.Add("单位");

            bool evOk = EventBox.SelectedItem != null && !string.IsNullOrEmpty(EventBox.SelectedItem as string);
            EventBox.BorderBrush = evOk ? _normalBorder : ErrorBorder;
            EventBox.BorderThickness = evOk ? new Thickness(1) : new Thickness(2);
            if (!evOk) errors.Add("项目");

            var legBoxes = new[] { Leg1Box, Leg2Box, Leg3Box, Leg4Box };
            for (int i = 0; i < 4; i++) {
                bool ok = !string.IsNullOrWhiteSpace(legBoxes[i].Text);
                legBoxes[i].BorderBrush = ok ? _normalBorder : ErrorBorder;
                legBoxes[i].BorderThickness = ok ? new Thickness(1) : new Thickness(2);
                if (!ok) errors.Add(string.Format("第 {0} 棒姓名", i + 1));
            }

            if (errors.Count == 0) {
                ValidationBar.Visibility = Visibility.Collapsed;
                SaveBtn.IsEnabled = true;
            } else {
                ValidationText.Text = "⚠ 以下必填字段未填: " + string.Join(", ", errors);
                ValidationBar.Visibility = Visibility.Visible;
                SaveBtn.IsEnabled = false;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            // 收集修改摘要
            var changes = new List<string>();
            string newName = TeamNameBox.Text.Trim();
            string newCountry = CountryBox.Text.Trim();
            string newEvent = EventBox.SelectedItem as string;
            string newGender = (GenderBox.SelectedItem as ComboBoxItem) != null
                ? (GenderBox.SelectedItem as ComboBoxItem).Content.ToString() : _backup.Gender;
            string newEntryTime = EntryTimeBox.Text.Trim();
            var newLegs = new[] { Leg1Box.Text.Trim(), Leg2Box.Text.Trim(), Leg3Box.Text.Trim(), Leg4Box.Text.Trim() };

            if (newName != (_backup.TeamName ?? "")) changes.Add(string.Format("队名: {0} → {1}", _backup.TeamName, newName));
            if (newCountry != (_backup.Country ?? "")) changes.Add(string.Format("单位: {0} → {1}", _backup.Country, newCountry));
            if (newEvent != (_backup.EventName ?? "")) changes.Add(string.Format("项目: {0} → {1}", _backup.EventName, newEvent));
            if (newGender != (_backup.Gender ?? "")) changes.Add(string.Format("性别: {0} → {1}", _backup.Gender, newGender));
            if (newEntryTime != (_backup.EntryTime ?? "")) changes.Add(string.Format("报名成绩: {0} → {1}", _backup.EntryTime, newEntryTime));
            for (int i = 0; i < 4; i++) {
                string oldLeg = i < _backup.LegNames.Length ? _backup.LegNames[i] : "";
                if (newLegs[i] != oldLeg) changes.Add(string.Format("第 {0} 棒: {1} → {2}", i + 1, oldLeg, newLegs[i]));
            }

            if (changes.Count == 0) {
                MessageBox.Show("没有任何修改", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 二次确认
            string summary = "确认保存以下修改？\n\n  • " + string.Join("\n  • ", changes);
            var res = MessageBox.Show(summary, "保存确认", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (res != MessageBoxResult.OK) return;

            // 应用到 _team
            _team.TeamName = newName;
            _team.Country = newCountry;
            _team.EventName = newEvent;
            _team.Gender = newGender;
            _team.EntryTime = newEntryTime;
            _team.EntryTimeSeconds = string.IsNullOrEmpty(newEntryTime) ? 0 : TimeFormatter.Parse(newEntryTime);
            // 4 棒姓名
            while (_team.Legs.Count < 4) _team.Legs.Add(new RelayLeg { LegOrder = _team.Legs.Count + 1 });
            for (int i = 0; i < 4; i++) _team.Legs[i].SwimmerName = newLegs[i];

            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            bool hasChanges = TeamNameBox.Text.Trim() != (_backup.TeamName ?? "")
                || CountryBox.Text.Trim() != (_backup.Country ?? "")
                || (EventBox.SelectedItem as string) != (_backup.EventName ?? "")
                || ((GenderBox.SelectedItem as ComboBoxItem) != null && (GenderBox.SelectedItem as ComboBoxItem).Content.ToString() != (_backup.Gender ?? ""))
                || EntryTimeBox.Text.Trim() != (_backup.EntryTime ?? "")
                || Leg1Box.Text.Trim() != (_backup.LegNames.Length > 0 ? _backup.LegNames[0] : "")
                || Leg2Box.Text.Trim() != (_backup.LegNames.Length > 1 ? _backup.LegNames[1] : "")
                || Leg3Box.Text.Trim() != (_backup.LegNames.Length > 2 ? _backup.LegNames[2] : "")
                || Leg4Box.Text.Trim() != (_backup.LegNames.Length > 3 ? _backup.LegNames[3] : "");
            if (hasChanges) {
                var res = MessageBox.Show("有未保存的修改，确认放弃？", "取消确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;
            }
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        // 旧值快照
        private class Snapshot
        {
            public string TeamName, Country, EventName, Gender, EntryTime;
            public string[] LegNames = new string[0];
            public static Snapshot Capture(RelayTeam t) {
                var s = new Snapshot {
                    TeamName = t.TeamName, Country = t.Country,
                    EventName = t.EventName, Gender = t.Gender,
                    EntryTime = t.EntryTime
                };
                var legs = t.Legs ?? new ObservableCollection<RelayLeg>();
                s.LegNames = new string[4];
                for (int i = 0; i < 4; i++) s.LegNames[i] = i < legs.Count ? (legs[i].SwimmerName ?? "") : "";
                return s;
            }
        }
    }
}
