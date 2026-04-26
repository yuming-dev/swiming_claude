using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace SwimmingScoreboard
{
    public partial class ResultBookEditWindow : Window
    {
        private readonly Func<List<ResultBookSwimmerInfo>> _swimmerProvider;
        private readonly Func<string> _previewProvider;

        public ResultBookData Result { get; private set; }
        public bool Saved { get; private set; }
        public bool RequestPrint { get; private set; }
        public bool RequestExportDoc { get; private set; }

        private ObservableCollection<ResultBookSwimmerInfo> _swimmerInfos;

        public ResultBookEditWindow(ResultBookData source,
                                    Func<List<ResultBookSwimmerInfo>> swimmerProvider,
                                    Func<string> previewProvider) {
            InitializeComponent();
            _swimmerProvider = swimmerProvider;
            _previewProvider = previewProvider;

            var src = source != null ? source.Clone() : new ResultBookData();

            CoverTitleBox.Text = src.CoverTitle ?? "";
            CoverSubtitleBox.Text = src.CoverSubtitle ?? "";
            VenueImagePathBox.Text = src.VenueImagePath ?? "";
            ForewordBox.Text = src.Foreword ?? "";
            ClosingBox.Text = src.ClosingNote ?? "";

            SportsTeamsBox.Text = string.Join(Environment.NewLine, (src.SportsTeams ?? new List<string>()).ToArray());
            SportsAthletesBox.Text = string.Join(Environment.NewLine, (src.SportsAthletes ?? new List<string>()).ToArray());
            SportsJudgesBox.Text = string.Join(Environment.NewLine, (src.SportsJudges ?? new List<string>()).ToArray());

            _swimmerInfos = new ObservableCollection<ResultBookSwimmerInfo>(src.SwimmerInfos ?? new List<ResultBookSwimmerInfo>());
            SwimmerInfoGrid.ItemsSource = _swimmerInfos;

            OptMedalCount.IsChecked = src.IncludeMedalCount;
            OptSportsAwards.IsChecked = src.IncludeSportsAwards;
            OptRecordStats.IsChecked = src.IncludeRecordStats;
            OptFinalRanking.IsChecked = src.IncludeFinalRanking;
            OptFullResults.IsChecked = src.IncludeFullResults;
            OptShowSplits.IsChecked = src.ShowSplitTimes;
            OptShowDiff.IsChecked = src.ShowTimeDifference;
        }

        private static List<string> SplitLines(string s) {
            if (string.IsNullOrWhiteSpace(s)) return new List<string>();
            return s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();
        }

        private ResultBookData Collect() {
            SwimmerInfoGrid.CommitEdit(); SwimmerInfoGrid.CommitEdit();
            return new ResultBookData {
                CoverTitle = CoverTitleBox.Text,
                CoverSubtitle = CoverSubtitleBox.Text,
                VenueImagePath = VenueImagePathBox.Text,
                Foreword = ForewordBox.Text,
                ClosingNote = ClosingBox.Text,
                SportsTeams = SplitLines(SportsTeamsBox.Text),
                SportsAthletes = SplitLines(SportsAthletesBox.Text),
                SportsJudges = SplitLines(SportsJudgesBox.Text),
                SwimmerInfos = _swimmerInfos.Where(s => !string.IsNullOrWhiteSpace(s.BibNumber)
                                                     || !string.IsNullOrWhiteSpace(s.Coach)
                                                     || !string.IsNullOrWhiteSpace(s.JointTrainingUnit)).ToList(),
                IncludeMedalCount = OptMedalCount.IsChecked == true,
                IncludeSportsAwards = OptSportsAwards.IsChecked == true,
                IncludeRecordStats = OptRecordStats.IsChecked == true,
                IncludeFinalRanking = OptFinalRanking.IsChecked == true,
                IncludeFullResults = OptFullResults.IsChecked == true,
                ShowSplitTimes = OptShowSplits.IsChecked == true,
                ShowTimeDifference = OptShowDiff.IsChecked == true
            };
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            Result = Collect(); Saved = true; DialogResult = true; Close();
        }

        private void SavePrint_Click(object sender, RoutedEventArgs e) {
            Result = Collect(); Saved = true; RequestPrint = true; DialogResult = true; Close();
        }

        private void ExportDoc_Click(object sender, RoutedEventArgs e) {
            Result = Collect(); Saved = true; RequestExportDoc = true; DialogResult = true; Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void Reset_Click(object sender, RoutedEventArgs e) {
            if (MessageBox.Show("确认清空所有自定义内容？", "重置成绩册", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            CoverTitleBox.Text = "游泳比赛";
            CoverSubtitleBox.Text = "成 绩 册";
            VenueImagePathBox.Text = "";
            ForewordBox.Text = "";
            ClosingBox.Text = "";
            SportsTeamsBox.Text = "";
            SportsAthletesBox.Text = "";
            SportsJudgesBox.Text = "";
            _swimmerInfos.Clear();
            OptMedalCount.IsChecked = true;
            OptSportsAwards.IsChecked = true;
            OptRecordStats.IsChecked = true;
            OptFinalRanking.IsChecked = true;
            OptFullResults.IsChecked = true;
            OptShowSplits.IsChecked = true;
            OptShowDiff.IsChecked = true;
        }

        private void Preview_Click(object sender, RoutedEventArgs e) {
            if (_previewProvider == null) { MessageBox.Show("预览不可用。"); return; }
            Result = Collect();
            try {
                string html = _previewProvider();
                if (!string.IsNullOrEmpty(html)) {
                    string tmp = Path.Combine(Path.GetTempPath(), "成绩册_预览_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
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
                Title = "选择封面图"
            };
            if (dlg.ShowDialog() == true) VenueImagePathBox.Text = dlg.FileName;
        }

        private void ClearVenueImage_Click(object sender, RoutedEventArgs e) { VenueImagePathBox.Text = ""; }

        private void AutoFillSwimmers_Click(object sender, RoutedEventArgs e) {
            if (_swimmerProvider == null) return;
            var existing = new HashSet<string>(_swimmerInfos.Select(s => s.BibNumber ?? ""));
            int added = 0;
            foreach (var sw in _swimmerProvider() ?? new List<ResultBookSwimmerInfo>()) {
                if (string.IsNullOrEmpty(sw.BibNumber) || existing.Contains(sw.BibNumber)) continue;
                _swimmerInfos.Add(sw);
                added++;
            }
            MessageBox.Show(string.Format("已添加 {0} 名运动员空白行。", added), "提示");
        }
    }
}
