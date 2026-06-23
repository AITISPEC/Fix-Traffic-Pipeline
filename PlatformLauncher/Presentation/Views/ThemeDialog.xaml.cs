using System.Windows;

namespace PlatformLauncher.Views
{
    public partial class ThemeDialog : Window
    {
        public string SelectedTheme { get; private set; }

        public ThemeDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DarkThemeRadio.IsChecked == true)
                SelectedTheme = "Dark";
            else if (LightThemeRadio.IsChecked == true)
                SelectedTheme = "Light";
            else
                SelectedTheme = "Dark"; // fallback

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