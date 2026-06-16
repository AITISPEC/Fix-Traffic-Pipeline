using PlatformLauncher.Services;
using System.Windows;

namespace PlatformLauncher.Views
{
    public partial class GamePropertiesDialog : Window
    {
        public bool WarpEnabled { get; private set; }
        public string GameName { get; }

        public GamePropertiesDialog(Models.GamePreset preset)
        {
            InitializeComponent();
            GameName = preset.Name;
            DataContext = this;
            WarpCheckBox.IsChecked = SettingsManager.GetWarpEnabled(preset.Id);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            WarpEnabled = WarpCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}