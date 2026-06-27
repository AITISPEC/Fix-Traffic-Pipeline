using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.ViewModels;
using System.Windows.Controls;

namespace PlatformLauncher.Presentation.Views
{
    public partial class SettingsTab : UserControl
    {
        public SettingsTab()
        {
            InitializeComponent();
        }

        public void SetViewModel(ServiceTabViewModel viewModel)
        {
            DataContext = viewModel;
        }

        private void ThemesListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is ThemeItem theme)
            {
                if (DataContext is ServiceTabViewModel vm)
                {
                    vm.SelectedTheme = theme;
                    var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                    mainWindow?.ApplyTheme(theme.Id);
                }
            }
        }
    }
}