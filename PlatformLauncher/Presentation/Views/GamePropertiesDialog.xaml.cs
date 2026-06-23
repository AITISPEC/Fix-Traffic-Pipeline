using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;
using System.Windows;

namespace PlatformLauncher.Presentation.Views
{
    public partial class GamePropertiesDialog : Window
    {
        private readonly ISettingsManager _settingsManager;
        private readonly GamePreset _preset;

        public string GameName { get; }
        public bool WarpEnabled { get; set; }

        public GamePropertiesDialog(GamePreset preset, ISettingsManager settingsManager)
        {
            InitializeComponent();
            _settingsManager = settingsManager;
            _preset = preset;
            GameName = preset.Name;
            DataContext = this;
            WarpEnabled = _settingsManager.GetWarpEnabled(preset.Id);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsManager.SetWarpEnabled(_preset.Id, WarpEnabled);
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