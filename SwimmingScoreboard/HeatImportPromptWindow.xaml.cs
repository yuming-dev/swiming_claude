using System.Windows;

namespace SwimmingScoreboard
{
    public partial class HeatImportPromptWindow : Window
    {
        public bool AutoRegister { get; private set; }

        public HeatImportPromptWindow() { InitializeComponent(); }

        private void Ok_Click(object sender, RoutedEventArgs e) {
            AutoRegister = AutoRegisterBox.IsChecked == true;
            DialogResult = true;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    }
}
