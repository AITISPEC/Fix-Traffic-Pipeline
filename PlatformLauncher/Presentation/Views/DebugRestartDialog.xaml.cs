using System.Windows;

namespace PlatformLauncher.Presentation.Views
{
    public partial class DebugRestartDialog : Window
    {
        public DebugRestartDialog(bool isEnabled)
        {
            InitializeComponent();
            MessageText.Text = isEnabled
                ? "Дебаггер будет включен при следующем запуске программы."
                : "Дебаггер будет выключен при следующем запуске программы.";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}