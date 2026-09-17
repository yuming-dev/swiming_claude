using System;
using System.Windows;

namespace SwimmingScoreboard
{
    public partial class CertSettingsWindow : Window
    {
        private readonly string _defaultCommittee;

        // 确认保存后回调：(主办单位, 组委会落款)。取消则不会触发。
        public Action<string, string> OnConfirm;

        public CertSettingsWindow(string organizer, string committee, string competitionName) {
            InitializeComponent();
            _defaultCommittee = (competitionName ?? "") + "组织委员会";
            OrganizerInput.Text = organizer ?? "";
            CommitteeInput.Text = committee ?? "";
            PreviewText.Text = "留空时将使用：" + _defaultCommittee;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) {
            if (OnConfirm != null) OnConfirm(OrganizerInput.Text.Trim(), CommitteeInput.Text.Trim());
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
