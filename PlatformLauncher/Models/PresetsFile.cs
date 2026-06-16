using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Models
{
    public class PresetsFile
    {
        [YamlMember(Alias = "games")]
        public List<GamePreset> Games { get; set; } = new List<GamePreset>();
    }
}