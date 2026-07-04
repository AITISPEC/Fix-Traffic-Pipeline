/// <summary>Топ-уровень пресетов YAML — содержит список игр (<c>games</c>). </summary>
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    /// <summary>Файл пресетов (presets.yaml) хранит все игры приложения.</summary>
    public class PresetsFile
    {
        [YamlMember(Alias = "games")]
        /// <summary>Список всех игр (<c>GamePreset</c>) — apex, hunt, roblox и т.п.</summary>
        public List<GamePreset> Games { get; set; } = new List<GamePreset>();
    }
}