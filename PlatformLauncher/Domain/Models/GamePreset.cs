using System.ComponentModel;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    public class GamePreset : INotifyPropertyChanged
    {
        [YamlMember(Alias = "id")]
        public string Id { get; set; }

        [YamlMember(Alias = "name")]
        public string Name { get; set; }

        [YamlMember(Alias = "config_url")]
        public string ConfigUrl { get; set; }

        [YamlMember(Alias = "warp_supported")]
        public bool WarpSupported { get; set; }

        [YamlMember(Alias = "version")]
        public int Version { get; set; }

        [YamlMember(Alias = "installed")]
        public bool Installed { get; set; } = false;

        private bool _isInstalling;
        public bool IsInstalling
        {
            get => _isInstalling;
            set { _isInstalling = value; OnPropertyChanged(); }
        }

        private bool _isUninstalling;
        public bool IsUninstalling
        {
            get => _isUninstalling;
            set { _isUninstalling = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}