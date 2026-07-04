/// <summary>Цветовая схема темы — задает цвета для UI и терминала. </summary>
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    /// <summary>Кастомная тема (флеш-тема): ID, название и цвета элементов.</summary>
    public class ThemeItem
    {
        [YamlMember(Alias = "id")]
        /// <summary>Уникальный ключ темы — используется в AppConfig.SelectedTheme ("fluent-light", "fluent-dark").</summary>
        public string Id { get; set; }

        [YamlMember(Alias = "display_name")]
        /// <summary>Человеческое название: "Light", "Dark" или кастомное.</summary>
        public string DisplayName { get; set; }

        [YamlMember(Alias = "terminal_theme")]
        /// <summary>"Light" или "Dark" — определяет фон терминала (консоли zapret).</summary>
        public string TerminalTheme { get; set; } // "Light" или "Dark"

        [YamlMember(Alias = "background")]
        /// <summary>Цвет фона окна приложения (например, "#2b2d31").</summary>
        public string Background { get; set; }

        [YamlMember(Alias = "foreground")]
        /// <summary>Цвет текста — основной цвет интерфейса.</summary>
        public string Foreground { get; set; }

        [YamlMember(Alias = "accent")]
        /// <summary>Акцентный (выделенный) цвет — кнопки, прогресс-бары.</summary>
        public string Accent { get; set; }

        [YamlMember(Alias = "control_background")]
        /// <summary>Фон кнопок/поля ввода.</summary>
        public string ControlBackground { get; set; }

        [YamlMember(Alias = "control_foreground")]
        /// <summary>Текст внутри элементов управления (кнопки, поля).</summary>
        public string ControlForeground { get; set; }

        [YamlMember(Alias = "border_brush")]
        /// <summary>Цвет границ (рамки) элементов.</summary>
        public string BorderBrush { get; set; }

        [YamlMember(Alias = "scrollbar_background")]
        /// <summary>Фон полосы прокрутки.</summary>
        public string ScrollBarBackground { get; set; }

        [YamlMember(Alias = "scrollbar_foreground")]
        /// <summary>Цвет элементов управления в полосе прокрутки (стрелочки).</summary>
        public string ScrollBarForeground { get; set; }

        [YamlMember(Alias = "hover_brush")]
        /// <summary>Цвет при наведении мыши.</summary>
        public string HoverBrush { get; set; }

        [YamlMember(Alias = "selected_brush")]
        /// <summary>Цвет выбранного элемента (в списках).</summary>
        public string SelectedBrush { get; set; }

        [YamlMember(Alias = "disabled_brush")]
        /// <summary>Фон неактивного/отключенного элемента.</summary>
        public string DisabledBrush { get; set; }

        [YamlMember(Alias = "disabled_foreground")]
        /// <summary>Цвет текста неактивных элементов.</summary>
        public string DisabledForeground { get; set; }

        [YamlMember(Alias = "input_background")]
        /// <summary>Фон текстовых полей ввода (TextBox).</summary>
        public string InputBackground { get; set; }

        [YamlMember(Alias = "input_foreground")]
        /// <summary>Текст внутри поля ввода.</summary>
        public string InputForeground { get; set; }

        [YamlMember(Alias = "input_border_brush")]
        /// <summary>Граница поля ввода.</summary>
        public string InputBorderBrush { get; set; }

        [YamlMember(Alias = "error_brush")]
        /// <summary>Цвет ошибок (красные сообщения).</summary>
        public string ErrorBrush { get; set; }

        [YamlMember(Alias = "warning_brush")]
        /// <summary>Цвет предупреждений (жёлтые сообщения).</summary>
        public string WarningBrush { get; set; }

        [YamlMember(Alias = "success_brush")]
        /// <summary>Цвет успешных действий (зелёные сообщения).</summary>
        public string SuccessBrush { get; set; }

        [YamlMember(Alias = "separator_brush")]
        /// <summary>Граница разделителя.</summary>
        public string SeparatorBrush { get; set; }

        [YamlMember(Alias = "overlay_color")]
        /// <summary>Цвет наложения (например, для затемнения при диалогах).</summary>
        public string OverlayColor { get; set; }
    }
}