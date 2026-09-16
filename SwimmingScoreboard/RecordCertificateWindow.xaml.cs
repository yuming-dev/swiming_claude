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

        public RecordCertificateWindow(List<MainWindow.BrokenRecordRow> records) {
            InitializeComponent();
            _all = records ?? new List<MainWindow.BrokenRecordRow>();

            EventFilterBox.Items.Add("全部项目");
            foreach (var ev in _all.Select(r => r.EventLabel).Distinct().OrderBy(s => s))
                EventFilterBox.Items.Add(ev);
            EventFilterBox.SelectedIndex = 0;

            RefreshGrid();
        }

        private void RefreshGrid() {
            string filter = EventFilterBox.SelectedItem as string;
            var view = (string.IsNullOrEmpty(filter) || filter == "全部项目")
                ? _all
                : _all.Where(r => r.EventLabel == filter).ToList();
            Grid1.ItemsSource = view;
            UpdateCount();
        }

        private void UpdateCount() {
            CountText.Text = string.Format("共 {0} 条，已选 {1} 条", _all.Count, _all.Count(r => r.Selected));
        }

        private void EventFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshGrid(); }

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
                if (_all.Count == 0) MessageBox.Show("本次比赛暂无破纪录记录。", "纪录证书生成");
                else MessageBox.Show("请至少选择一条破纪录记录。", "纪录证书生成");
                return;
            }
            var templateItem = TemplateBox.SelectedItem as ComboBoxItem;
            string template = templateItem != null ? (templateItem.Tag as string) : "full";
            if (OnGenerate != null) OnGenerate(selected, template);
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
