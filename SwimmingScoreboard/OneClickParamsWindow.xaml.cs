using System;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingScoreboard
{
    // 2026-05-24 P0-1 一键全自动 — 比赛日程参数收集对话框
    public partial class OneClickParamsWindow : Window
    {
        public DateTime ParamStartDate { get; private set; }
        public int ParamDays { get; private set; }
        public int ParamMorningStartMin { get; private set; }
        public int ParamAfternoonStartMin { get; private set; }
        public int ParamEveningStartMin { get; private set; }
        public int ParamMaxSessionMinutes { get; private set; }
        public bool ParamStrategyFinalOnly { get; private set; }
        public bool ParamFirstDayMorningSkip { get; private set; }
        public bool ParamLastDayAfternoonSkip { get; private set; }

        public OneClickParamsWindow(DateTime defaultStartDate, int defaultDays) {
            InitializeComponent();
            StartDateBox.Text = defaultStartDate.ToString("yyyy-MM-dd");
            int idx = Math.Max(0, Math.Min(6, defaultDays - 1));
            DaysCombo.SelectedIndex = idx;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) {
            DateTime sd;
            if (!DateTime.TryParse(StartDateBox.Text.Trim(), out sd)) {
                MessageBox.Show("起始日期格式不正确 (yyyy-MM-dd)", "提示"); return;
            }
            int days;
            if (!int.TryParse(((ComboBoxItem)DaysCombo.SelectedItem).Content.ToString(), out days) || days < 1) days = 1;
            int mor = ParseHhMm(MorningBox.Text, 8 * 60 + 30);
            int aft = ParseHhMm(AfternoonBox.Text, 13 * 60 + 30);
            int eve = ParseHhMm(EveningBox.Text, 19 * 60);
            int maxSession;
            if (!int.TryParse(MaxSessionMinBox.Text.Trim(), out maxSession) || maxSession < 30) maxSession = 240;

            ParamStartDate = sd;
            ParamDays = days;
            ParamMorningStartMin = mor;
            ParamAfternoonStartMin = aft;
            ParamEveningStartMin = eve;
            ParamMaxSessionMinutes = maxSession;
            ParamStrategyFinalOnly = StrategyFinalOnly.IsChecked == true;
            ParamFirstDayMorningSkip = FirstDayMorningSkip.IsChecked == true;
            ParamLastDayAfternoonSkip = LastDayAfternoonSkip.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }

        private static int ParseHhMm(string s, int fallback) {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            var parts = s.Trim().Split(':');
            int h, m;
            if (parts.Length == 2 && int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m)
                && h >= 0 && h <= 23 && m >= 0 && m <= 59) return h * 60 + m;
            return fallback;
        }
    }
}
