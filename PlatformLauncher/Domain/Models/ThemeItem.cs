using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    public class ThemeItem
    {
        [YamlMember(Alias = "id")]
        public string Id { get; set; }

        [YamlMember(Alias = "display_name")]
        public string DisplayName { get; set; }

        [YamlMember(Alias = "terminal_theme")]
        public string TerminalTheme { get; set; } // "Light" или "Dark"

        [YamlMember(Alias = "background")]
        public string Background { get; set; }

        [YamlMember(Alias = "foreground")]
        public string Foreground { get; set; }

        [YamlMember(Alias = "accent")]
        public string Accent { get; set; }

        [YamlMember(Alias = "control_background")]
        public string ControlBackground { get; set; }

        [YamlMember(Alias = "control_foreground")]
        public string ControlForeground { get; set; }

        [YamlMember(Alias = "border_brush")]
        public string BorderBrush { get; set; }
    }
}