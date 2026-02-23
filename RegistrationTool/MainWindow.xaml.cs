using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SwimmingRegistrationTool
{
    public class RegEntry
    {
        public string Name { get; set; }
        public string Gender { get; set; }
        public string AgeGroup { get; set; }
        public string EventId { get; set; }
        public string ReportedTime { get; set; }
        public string BirthDate { get; set; }
        public string Phone { get; set; }
    }

    public partial class MainWindow : Window
    {
        private ObservableCollection<RegEntry> _entries = new ObservableCollection<RegEntry>();

        public MainWindow()
        {
            InitializeComponent();
            RegGrid.ItemsSource = _entries;
        }

        private void AddToList_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(RegName.Text) || RegGender.SelectedItem == null)
            {
                MessageBox.Show("请至少填写姓名和性别"); return;
            }

            string team = TeamNameBox.Text;
            string birthStr = RegBirth.SelectedDate.HasValue ? RegBirth.SelectedDate.Value.ToString("yyyy-MM-dd") : "";
            string ageGroup = RegAgeGroup.SelectedItem != null ? RegAgeGroup.SelectedItem.ToString() : "";
            string gender = RegGender.SelectedItem.ToString();

            int added = 0;
            added += TryAdd(Dist1, Stroke1, Time1, gender, ageGroup, birthStr);
            added += TryAdd(Dist2, Stroke2, Time2, gender, ageGroup, birthStr);
            added += TryAdd(Dist3, Stroke3, Time3, gender, ageGroup, birthStr);

            if (added == 0)
            {
                MessageBox.Show("请至少选择一个参赛项目"); return;
            }

            // 清空项目选择
            Dist1.SelectedIndex = -1; Stroke1.SelectedIndex = -1; Time1.Clear();
            Dist2.SelectedIndex = -1; Stroke2.SelectedIndex = -1; Time2.Clear();
            Dist3.SelectedIndex = -1; Stroke3.SelectedIndex = -1; Time3.Clear();
        }

        private int TryAdd(ComboBox distCb, ComboBox strokeCb, TextBox timeTb, string gender, string ageGroup, string birthStr)
        {
            if (distCb.SelectedItem == null || strokeCb.SelectedItem == null) return 0;
            string eventId = gender + "子" + distCb.SelectedItem.ToString() + strokeCb.SelectedItem.ToString();

            if (_entries.Any(e => e.Name == RegName.Text && e.EventId == eventId))
            {
                MessageBox.Show(RegName.Text + " 已报名 " + eventId); return 0;
            }

            _entries.Add(new RegEntry
            {
                Name = RegName.Text,
                Gender = gender,
                AgeGroup = ageGroup,
                EventId = eventId,
                ReportedTime = timeTb.Text,
                BirthDate = birthStr,
                Phone = RegPhone.Text
            });
            return 1;
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (RegGrid.SelectedItem != null)
            {
                _entries.Remove(RegGrid.SelectedItem as RegEntry);
            }
        }

        private void ExportCSV_Click(object sender, RoutedEventArgs e)
        {
            if (_entries.Count == 0) { MessageBox.Show("报名列表为空"); return; }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV文件 (*.csv)|*.csv",
                FileName = string.Format("{0}_报名表.csv", string.IsNullOrEmpty(TeamNameBox.Text) ? "未命名" : TeamNameBox.Text)
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8))
                    {
                        writer.WriteLine("姓名,性别,参赛单位,项目,报名成绩,年龄组,出生日期");
                        foreach (var entry in _entries)
                        {
                            writer.WriteLine(string.Format("{0},{1},{2},{3},{4},{5},{6}",
                                entry.Name, entry.Gender, TeamNameBox.Text, entry.EventId,
                                entry.ReportedTime, entry.AgeGroup, entry.BirthDate));
                        }
                    }
                    MessageBox.Show(string.Format("成功导出 {0} 条报名记录至:\n{1}", _entries.Count, dialog.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message);
                }
            }
        }
    }
}
