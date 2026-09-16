using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    public partial class AwardCertificateWindow : Window
    {
        private readonly List<MainWindow.AwardCandidateRow> _all;

        // MainWindow 传进来处理"选中的这几条 + 用哪个模板 + 微调偏移，帮我出证书"
        public Action<List<MainWindow.AwardCandidateRow>, string, double, double> OnGenerate;

        public AwardCertificateWindow(List<MainWindow.AwardCandidateRow> candidates) {
            InitializeComponent();
            _all = candidates ?? new List<MainWindow.AwardCandidateRow>();

            EventFilterBox.Items.Add("全部项目");
            foreach (var ev in _all.Select(c => c.EventLabel).Distinct().OrderBy(s => s))
                EventFilterBox.Items.Add(ev);
            EventFilterBox.SelectedIndex = 0;

            RefreshGrid();
        }

        private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // 预印模板才需要套打微调；自画完整证书用不上，藏起来免得让人以为要填
            if (OffsetPanel == null) return;
            var item = TemplateBox.SelectedItem as ComboBoxItem;
            string tag = item != null ? (item.Tag as string) : "full_haosha";
            OffsetPanel.Visibility = (tag == "preprint_gansu" || tag == "preprint_haosha") ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshGrid() {
            string filter = EventFilterBox.SelectedItem as string;
            var view = (string.IsNullOrEmpty(filter) || filter == "全部项目")
                ? _all
                : _all.Where(c => c.EventLabel == filter).ToList();
            Grid1.ItemsSource = view;
            UpdateCount();
        }

        private void UpdateCount() {
            CountText.Text = string.Format("共 {0} 条，已选 {1} 条", _all.Count, _all.Count(c => c.Selected));
        }

        private void EventFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshGrid(); }

        private IEnumerable<MainWindow.AwardCandidateRow> CurrentView() {
            return (Grid1.ItemsSource as IEnumerable<MainWindow.AwardCandidateRow>) ?? new List<MainWindow.AwardCandidateRow>();
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
            // 2026-09-16 复选框列点完可能还没提交到绑定对象——JudgeOverrideWindow 那次
            //   "改了三个只认一个"就是这个坑，这里先强制提交一遍再读。
            try { Grid1.CommitEdit(DataGridEditingUnit.Cell, true); Grid1.CommitEdit(DataGridEditingUnit.Row, true); } catch { }
            UpdateCount();
            var selected = _all.Where(c => c.Selected).ToList();
            if (selected.Count == 0) { MessageBox.Show("请至少选择一条获奖记录。", "奖状生成"); return; }

            var templateItem = TemplateBox.SelectedItem as ComboBoxItem;
            string template = templateItem != null ? (templateItem.Tag as string) : "full_haosha";
            double offX, offY;
            if (!double.TryParse(OffsetXBox.Text, out offX)) offX = 0;
            if (!double.TryParse(OffsetYBox.Text, out offY)) offY = 0;

            if (OnGenerate != null) OnGenerate(selected, template, offX, offY);
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
