using PlatformLauncher.Domain.Models;
using PlatformLauncher.Presentation.ViewModels;
using System.Windows;
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

        private void DebugCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is ServiceTabViewModel vm)
            {
                // Если значение вернулось к исходному - не показываем диалог
                if (vm.DebugEnabled == vm.InitialDebugEnabled)
                    return;

                var dialog = new DebugRestartDialog(vm.DebugEnabled);
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                var result = dialog.ShowDialog();
                if (result == true)
                {
                    System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName);
                    System.Windows.Application.Current.Shutdown();
                }
            }
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