using System.Windows;

namespace SwimmingScoreboard
{
    public partial class HeatEditWindow : Window
    {
        public HeatEditWindow() {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}
