using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace SwimmingScoreboard
{
    public partial class ProgramBookEditWindow : Window
    {
        private readonly Func<List<string>> _teamNamesProvider;
        private readonly Func<string> _previewProvider;

        public ProgramBookData Result { get; private set; }
        public bool Saved { get; private set; }

        private ObservableCollection<ProgramBookActivityItem> _activities;
        private ObservableCollection<ProgramBookTrainingItem> _training;
        private ObservableCollection<ProgramBookOfficialItem> _officials;
        private ObservableCollection<ProgramBookTeamStaff> _teamStaff;

        public ProgramBookEditWindow(ProgramBookData source, Func<List<string>> teamNamesProvider, Func<string> previewProvider) {
            InitializeComponent();
            _teamNamesProvider = teamNamesProvider;
            _previewProvider = previewProvider;

            var src = source != null ? source.Clone() : new ProgramBookData();
            CoverTitleBox.Text = src.CoverTitle ?? "";
            CoverSubtitleBox.Text = src.CoverSubtitle ?? "";
            VenueImagePathBox.Text = src.VenueImagePath ?? "";
            ForewordBox.Text = src.Foreword ?? "";
            RegulationsBox.Text = src.Regulations ?? "";
            NoticeBox.Text = src.SupplementaryNotice ?? "";
            ClosingBox.Text = src.ClosingNote ?? "";

            _activities = new ObservableCollection<ProgramBookActivityItem>(src.KeyActivities ?? new List<ProgramBookActivityItem>());
            _training = new ObservableCollection<ProgramBookTrainingItem>(src.TrainingSchedule ?? new List<ProgramBookTrainingItem>());
            _officials = new ObservableCollection<ProgramBookOfficialItem>(src.Officials ?? new List<ProgramBookOfficialItem>());
            _teamStaff = new ObservableCollection<ProgramBookTeamStaff>(src.TeamStaffList ?? new List<ProgramBookTeamStaff>());

            ActivitiesGrid.ItemsSource = _activities;
            TrainingGrid.ItemsSource = _training;
            OfficialsGrid.ItemsSource = _officials;
            TeamStaffGrid.ItemsSource = _teamStaff;
        }

        private ProgramBookData Collect() {
            // 提交所有 DataGrid 中正在编辑的单元格
            ActivitiesGrid.CommitEdit(); ActivitiesGrid.CommitEdit();
            TrainingGrid.CommitEdit(); TrainingGrid.CommitEdit();
            OfficialsGrid.CommitEdit(); OfficialsGrid.CommitEdit();
            TeamStaffGrid.CommitEdit(); TeamStaffGrid.CommitEdit();

            return new ProgramBookData {
                CoverTitle = CoverTitleBox.Text,
                CoverSubtitle = CoverSubtitleBox.Text,
                VenueImagePath = VenueImagePathBox.Text,
                Foreword = ForewordBox.Text,
                Regulations = RegulationsBox.Text,
                SupplementaryNotice = NoticeBox.Text,
                ClosingNote = ClosingBox.Text,
                KeyActivities = _activities.Where(IsNonEmptyActivity).ToList(),
                TrainingSchedule = _training.Where(IsNonEmptyTraining).ToList(),
                Officials = _officials.Where(o => !string.IsNullOrWhiteSpace(o.Title) || !string.IsNullOrWhiteSpace(o.Name)).ToList(),
                TeamStaffList = _teamStaff.Where(t => !string.IsNullOrWhiteSpace(t.TeamName)).ToList()
            };
        }

        private static bool IsNonEmptyActivity(ProgramBookActivityItem a) {
            return !string.IsNullOrWhiteSpace(a.Date) || !string.IsNullOrWhiteSpace(a.Time)
                || !string.IsNullOrWhiteSpace(a.Activity) || !string.IsNullOrWhiteSpace(a.Participants)
                || !string.IsNullOrWhiteSpace(a.Venue);
        }

        private static bool IsNonEmptyTraining(ProgramBookTrainingItem t) {
            return !string.IsNullOrWhiteSpace(t.Date) || !string.IsNullOrWhiteSpace(t.Time) || !string.IsNullOrWhiteSpace(t.Venue);
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            Result = Collect();
            Saved = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }

        private void Reset_Click(object sender, RoutedEventArgs e) {
            if (MessageBox.Show("确认清空所有自定义内容？", "重置秩序册", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            CoverTitleBox.Text = "游泳比赛";
            CoverSubtitleBox.Text = "秩 序 册";
            VenueImagePathBox.Text = "";
            ForewordBox.Text = "";
            RegulationsBox.Text = "";
            NoticeBox.Text = "";
            ClosingBox.Text = "";
            _activities.Clear();
            _training.Clear();
            _officials.Clear();
            _teamStaff.Clear();
        }

        private void Preview_Click(object sender, RoutedEventArgs e) {
            if (_previewProvider == null) { MessageBox.Show("预览不可用。"); return; }
            // 临时收集当前值供预览
            var snapshot = Collect();
            // 把当前 dialog 的值压入 result 临时让 caller 用快照渲染
            Result = snapshot;
            try {
                string html = _previewProvider();
                if (!string.IsNullOrEmpty(html)) {
                    string tmp = Path.Combine(Path.GetTempPath(), "秩序册_预览_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
                    File.WriteAllText(tmp, html, System.Text.Encoding.UTF8);
                    System.Diagnostics.Process.Start(tmp);
                }
            } catch (Exception ex) {
                MessageBox.Show("预览失败：" + ex.Message);
            }
        }

        private void PickVenueImage_Click(object sender, RoutedEventArgs e) {
            var dlg = new Microsoft.Win32.OpenFileDialog {
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
                Title = "选择比赛场地示意图"
            };
            if (dlg.ShowDialog() == true) {
                VenueImagePathBox.Text = dlg.FileName;
            }
        }

        private void ClearVenueImage_Click(object sender, RoutedEventArgs e) {
            VenueImagePathBox.Text = "";
        }

        private void AutoFillTeamStaff_Click(object sender, RoutedEventArgs e) {
            if (_teamNamesProvider == null) return;
            var teams = _teamNamesProvider() ?? new List<string>();
            var existing = new HashSet<string>(_teamStaff.Select(t => t.TeamName ?? ""));
            int added = 0;
            foreach (var t in teams) {
                if (string.IsNullOrEmpty(t) || existing.Contains(t)) continue;
                _teamStaff.Add(new ProgramBookTeamStaff { TeamName = t, Leader = "", Coaches = "", Doctors = "", Staff = "" });
                added++;
            }
            MessageBox.Show(string.Format("已添加 {0} 支队伍空白行。", added), "提示");
        }
    }
}
