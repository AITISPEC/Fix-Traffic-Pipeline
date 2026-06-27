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

        [YamlMember(Alias = "scrollbar_background")]
        public string ScrollBarBackground { get; set; }

        [YamlMember(Alias = "scrollbar_foreground")]
        public string ScrollBarForeground { get; set; }

        [YamlMember(Alias = "hover_brush")]
        public string HoverBrush { get; set; }

        [YamlMember(Alias = "selected_brush")]
        public string SelectedBrush { get; set; }

        [YamlMember(Alias = "disabled_brush")]
        public string DisabledBrush { get; set; }

        [YamlMember(Alias = "disabled_foreground")]
        public string DisabledForeground { get; set; }

        [YamlMember(Alias = "input_background")]
        public string InputBackground { get; set; }

        [YamlMember(Alias = "input_foreground")]
        public string InputForeground { get; set; }

        [YamlMember(Alias = "input_border_brush")]
        public string InputBorderBrush { get; set; }

        [YamlMember(Alias = "error_brush")]
        public string ErrorBrush { get; set; }

        [YamlMember(Alias = "warning_brush")]
        public string WarningBrush { get; set; }

        [YamlMember(Alias = "success_brush")]
        public string SuccessBrush { get; set; }

        [YamlMember(Alias = "separator_brush")]
        public string SeparatorBrush { get; set; }

        [YamlMember(Alias = "overlay_color")]
        public string OverlayColor { get; set; }
    }
}