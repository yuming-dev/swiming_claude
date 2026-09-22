using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    public partial class AwardCertificateWindow : Window
    {
        private List<MainWindow.AwardCandidateRow> _all;

        // MainWindow 传进来处理"选中的这几条 + 用哪个模板 + 微调偏移，帮我出证书"
        public Action<List<MainWindow.AwardCandidateRow>, string, double, double> OnGenerate;
        // 2026-09-17 "证书参数设置"按钮——本窗口不直接持有主办单位/组委会这些设置
        // (它们挂在 MainWindow 上), 点了就交给 MainWindow 弹出设置窗口。
        public Action OnOpenCertSettings;

        public AwardCertificateWindow(List<MainWindow.AwardCandidateRow> candidates) {
            InitializeComponent();
            _all = candidates ?? new List<MainWindow.AwardCandidateRow>();

            RebuildAgeGroupFilter();
            RebuildEventFilter();
            RefreshGrid();
        }

        // 2026-09-19 用户实拍到: 奖状窗口一直没有组别筛选、表格也没有组别这一列——
        //   同一个项目(比如"男 200米蛙泳")甲组/乙组各有各的决赛, 名单混在一起
        //   看不出谁是哪个组别的, 选错组别的人生成证书都发现不了。这里补上跟
        //   "项目筛选"并列的"组别筛选", 两个筛选条件同时生效(AND)。
        private void RebuildAgeGroupFilter() {
            AgeGroupFilterBox.Items.Clear();
            AgeGroupFilterBox.Items.Add(Loc.T("Str_Win_AwardCert_AllAgeGroups"));
            foreach (var ag in _all.Select(c => c.AgeGroup).Distinct().OrderBy(s => s))
                if (!string.IsNullOrEmpty(ag)) AgeGroupFilterBox.Items.Add(ag);
            AgeGroupFilterBox.SelectedIndex = 0;
        }

        private void RebuildEventFilter() {
            EventFilterBox.Items.Clear();
            EventFilterBox.Items.Add(Loc.T("Str_Win_AwardCert_AllEvents"));
            foreach (var ev in _all.Select(c => c.EventLabel).Distinct().OrderBy(s => s))
                EventFilterBox.Items.Add(ev);
            EventFilterBox.SelectedIndex = 0;
        }

        // 2026-09-17 "证书参数设置"里改了"打印到第几名"以后, MainWindow 用新名次限制
        //   重新 CollectAwardCandidates() 一遍, 拿新名单整体替换本窗口正在显示的这份——
        //   不重开窗口, 直接刷新表格 + 标题里的名次数字, 用户不用退出重进就能看到变化。
        public void RefreshCandidates(List<MainWindow.AwardCandidateRow> candidates, int rankLimit) {
            _all = candidates ?? new List<MainWindow.AwardCandidateRow>();
            HeaderText.Text = Loc.F("Str_Win_AwardCert_HeaderFmt", rankLimit);
            RebuildAgeGroupFilter();
            RebuildEventFilter();
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
            string evFilter = EventFilterBox.SelectedItem as string;
            string agFilter = AgeGroupFilterBox.SelectedItem as string;
            IEnumerable<MainWindow.AwardCandidateRow> view = _all;
            if (!string.IsNullOrEmpty(evFilter) && evFilter != Loc.T("Str_Win_AwardCert_AllEvents"))
                view = view.Where(c => c.EventLabel == evFilter);
            if (!string.IsNullOrEmpty(agFilter) && agFilter != Loc.T("Str_Win_AwardCert_AllAgeGroups"))
                view = view.Where(c => c.AgeGroup == agFilter);
            Grid1.ItemsSource = view.ToList();
            UpdateCount();
        }

        private void UpdateCount() {
            CountText.Text = Loc.F("Str_Win_AwardCert_CountFmt", _all.Count, _all.Count(c => c.Selected));
        }

        private void EventFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshGrid(); }
        private void AgeGroupFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { RefreshGrid(); }

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
            if (selected.Count == 0) { MessageBox.Show(Loc.T("Str_Win_AwardCert_MsgSelectAtLeastOne"), Loc.T("Str_Win_AwardCert_Title")); return; }

            var templateItem = TemplateBox.SelectedItem as ComboBoxItem;
            string template = templateItem != null ? (templateItem.Tag as string) : "full_haosha";
            double offX, offY;
            if (!double.TryParse(OffsetXBox.Text, out offX)) offX = 0;
            if (!double.TryParse(OffsetYBox.Text, out offY)) offY = 0;

            if (OnGenerate != null) OnGenerate(selected, template, offX, offY);
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        private void CertSettings_Click(object sender, RoutedEventArgs e) {
            if (OnOpenCertSettings != null) OnOpenCertSettings();
        }
    }
}
