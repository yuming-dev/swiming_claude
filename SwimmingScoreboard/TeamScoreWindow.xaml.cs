using System.Collections.ObjectModel;
using System.Windows;

namespace SwimmingScoreboard
{
    public partial class TeamScoreWindow : Window
    {
        public TeamScoreWindow(ObservableCollection<TeamScore> scores) {
            InitializeComponent();
            TeamGrid.ItemsSource = scores;
        }
    }
}
