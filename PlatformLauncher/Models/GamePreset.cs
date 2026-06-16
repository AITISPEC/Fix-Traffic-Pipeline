using YamlDotNet.Serialization;

namespace PlatformLauncher.Models
{
    public class GamePreset
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
    }
}