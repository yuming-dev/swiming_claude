using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    public partial class RecordCertificateWindow : Window
    {
        private readonly List<MainWindow.BrokenRecordRow> _all;

        public Action<List<MainWindow.BrokenRecordRow>, string> OnGenerate;
        // 2026-09-17 "证书参数设置"按钮——本窗口不直接持有主办单位/组委会这些设置
        // (它们挂在 MainWindow 上), 点了就交给 MainWindow 弹出设置窗口。
        public Action OnOpenCertSettings;

        public RecordCertificateWindow(List<MainWindow.BrokenRecordRow> records) {
            InitializeComponent();
            _all = records ?? new List<MainWindow.BrokenRecordRow>();

            // 2026-09-19 用户实拍到: 这个窗口一直没有组别筛选、表格也没有组别这一列——
            //   同一个项目不同组别破的纪录混在一起, 看不出哪条是哪个组别破的。
            AgeGroupFilterBox.Items.Add(Loc.T("Str_Win_AwardCert_AllAgeGroups"));
            foreach (var ag in _all.Select(r => r.AgeGroup).Distinct().OrderBy(s => s))
                if (!string.IsNullOrEmpty(ag)) AgeGroupFilterBox.Items.Add(ag);
            AgeGroupFilterBox.SelectedIndex = 0;

            EventFilterBox.Items.Add(Loc.T("Str_Win_AwardCert_AllEvents"));
            foreach (var ev in _all.Select(r => r.EventLabel).Distinct().OrderBy(s => s))
                EventFilterBox.Items.Add(ev);
            EventFilterBox.SelectedIndex = 0;

            RefreshGrid();
        }

        private void RefreshGrid() {
            string evFilter = EventFilterBox.SelectedItem as string;
            string agFilter = AgeGroupFilterBox.SelectedItem as string;
            IEnumerable<MainWindow.BrokenRecordRow> view = _all;
            if (!string.IsNullOrEmpty(evFilter) && evFilter != Loc.T("Str_Win_AwardCert_AllEvents"))
                view = view.Where(r => r.EventLabel == evFilter);
            if (!string.IsNullOrEmpty(agFilter) && agFilter != Loc.T("Str_Win_AwardCert_AllAgeGroups"))
                view = view.Where(r => r.AgeGroup == agFilter);
            Grid1.ItemsSource = view.ToList();
            UpdateCount();
        }

        private void UpdateCount() {
            CountText.Text = Loc.F("Str_Win_AwardCert_CountFmt", _all.Count, _all.Count(r => r.Selected));
        }

        private void EventFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshGrid(); }
        private void AgeGroupFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshGrid(); }

        private IEnumerable<MainWindow.BrokenRecordRow> CurrentView() {
            return (Grid1.ItemsSource as IEnumerable<MainWindow.BrokenRecordRow>) ?? new List<MainWindow.BrokenRecordRow>();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) {
            foreach (var row in CurrentView()) row.Selected = true;
            Grid1.Items.Refresh();
            UpdateCount();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e) {
            foreach (var row in CurrentView()) row.Selected = false;
            Grid1.Items.Refresh();
            UpdateCount();
        }

        private void Generate_Click(object sender, RoutedEventArgs e) {
            try { Grid1.CommitEdit(DataGridEditingUnit.Cell, true); Grid1.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
            UpdateCount();
            var selected = _all.Where(r => r.Selected).ToList();
            if (selected.Count == 0) {
                if (_all.Count == 0) MessageBox.Show(Loc.T("Str_Win_RecordCert_MsgNoRecords"), Loc.T("Str_Win_RecordCert_Title"));
                else MessageBox.Show(Loc.T("Str_Win_RecordCert_MsgSelectAtLeastOne"), Loc.T("Str_Win_RecordCert_Title"));
                return;
            }
            var templateItem = TemplateBox.SelectedItem as ComboBoxItem;
            string template = templateItem != null ? (templateItem.Tag as string) : "full";
            if (OnGenerate != null) OnGenerate(selected, template);
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private void CertSettings_Click(object sender, RoutedEventArgs e) {
            if (OnOpenCertSettings != null) OnOpenCertSettings();
        }
    }
}
