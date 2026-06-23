using System.Windows.Controls;
using PlatformLauncher.Presentation.ViewModels;

namespace PlatformLauncher.Presentation.Views
{
    public partial class ServiceTab : UserControl
    {
        public ServiceTab()
        {
            InitializeComponent();
        }

        public void SetViewModel(ServiceTabViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }
}