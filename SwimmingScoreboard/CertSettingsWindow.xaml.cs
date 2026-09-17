using System;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    public partial class CertSettingsWindow : Window
    {
        private readonly string _defaultCommittee;
        private readonly bool _showRankLimit;

        // 确认保存后回调：(主办单位, 承办单位, 组委会落款, 奖状打印到第几名——非奖状场景为 null)。取消则不会触发。
        public Action<string, string, string, int?> OnConfirm;

        public CertSettingsWindow(string organizer, string host, string committee, string competitionName, int? awardRankLimit) {
            InitializeComponent();
            _defaultCommittee = (competitionName ?? "") + "组织委员会";
            OrganizerInput.Text = organizer ?? "";
            HostInput.Text = host ?? "";
            CommitteeInput.Text = committee ?? "";
            PreviewText.Text = "留空时将使用：" + _defaultCommittee;

            // 2026-09-17 "打印到第几名"只有奖状才有意义(纪录证书没有名次范围这回事),
            //   RecordCertificateWindow 打开本窗口时 awardRankLimit 传 null, 这块不显示。
            _showRankLimit = awardRankLimit.HasValue;
            if (_showRankLimit) {
                RankLimitPanel.Visibility = Visibility.Visible;
                int limit = awardRankLimit.Value;
                foreach (ComboBoxItem item in RankLimitBox.Items) {
                    if ((item.Tag as string) == limit.ToString()) { item.IsSelected = true; break; }
                }
                if (RankLimitBox.SelectedIndex < 0) RankLimitBox.SelectedIndex = 2; // 兜底选"前3名"
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) {
            int? rankLimit = null;
            if (_showRankLimit) {
                var item = RankLimitBox.SelectedItem as ComboBoxItem;
                int parsed;
                rankLimit = (item != null && int.TryParse(item.Tag as string, out parsed)) ? parsed : 3;
            }
            if (OnConfirm != null) OnConfirm(OrganizerInput.Text.Trim(), HostInput.Text.Trim(), CommitteeInput.Text.Trim(), rankLimit);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
