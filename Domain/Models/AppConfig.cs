/// <summary>Глобальная конфигурация приложения: тема, настройки терминала и логов.</summary>
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    /// <summary>Версия приложения (например, "1.0.0") и тайм-аут остановки Python (~2 с) — защита от зависаний при выключении.</summary>
    public class AppConfig
    {
        [YamlMember(Alias = "app")]
        /// <summary>Настройки версии приложения и времени ожидания остановки процесса Python.</summary>
        public AppSettings App { get; set; } = null!;

        [YamlMember(Alias = "terminal")]
        /// <summary>Цвета шрифта, размера и ширина колонок в терминальном дисплее (DNS, IP, порт).</summary>
        public TerminalSettings Terminal { get; set; } = null!;

        [YamlMember(Alias = "monitor")]
        /// <summary>Какой статус DNS отслеживать — по умолчанию SYN_SENT (установлен пакет, запрос в сети).</summary>
        public MonitorSettings Monitor { get; set; } = null!;

        [YamlMember(Alias = "logging")]
        /// <summary>Уровень лога (INFO/DEBUG), размер файла до ротации и количество бэкапов (~5 файлов).</summary>
        public LoggingSettings Logging { get; set; } = null!;

        [YamlMember(Alias = "selected_theme")]
        /// <summary>Активная тема интерфейса: "fluent-light", "fluent-dark" или пользовательская.</summary>
        public string SelectedTheme { get; set; } = "fluent-light";

        [YamlMember(Alias = "lists")]
        /// <summary>Путь к файлам списков zapret и исключений (ip_file, domain_file, exclude_ip_file).</summary>
        public ListsConfig Lists { get; set; } = null!;

        [YamlMember(Alias = "cloudflare_domains")]
        /// <summary>Список доменов Cloudflare — если домен в списке, трафик обрабатывается по WARP.</summary>
        public List<string> CloudflareDomains { get; set; } = new();

        [YamlMember(Alias = "progress_bar")]
        /// <summary>Показывать ли прогресс-бар при установке/удалении пресета (Installing, Uninstalling).</summary>
        public ProgressBarSettings ProgressBar { get; set; } = new();
    }

    /// <summary><c>app_version</c> — семантическая версия. <c>python_stop_timeout_ms</c> (~2000 мс) обрезает зависания при закрытии.</summary>
    public class AppSettings
    {
        [YamlMember(Alias = "app_version")]
        /// <summary>Семантическая версия приложения — используется в логах и UI (например, "1.0.0").</summary>
        public string AppVersion { get; set; } = "1.0.0";

        [YamlMember(Alias = "python_stop_timeout_ms")]
        /// <summary>Тайм-аут остановки процесса Python при выключении (по умолчанию 2000 мс).</summary>
        public int PythonStopTimeoutMs { get; set; } = 2000;

        [YamlMember(Alias = "default_lists_path")]
        /// <summary>Заданная директория для списка zapret — если пусто, берётся из автообнаружения.</summary>
        public string DefaultListsPath { get; set; } = "";
    }

    /// <summary>Внешний вид терминала: шрифт Cascadia Code (12pt), ширина колонок под IP/port/dns.</summary>
    public class TerminalSettings
    {
        [YamlMember(Alias = "theme")]
        /// <summary>"Dark" или "Light" — фон и цвета текста консоли.</summary>
        public string Theme { get; set; } = "Dark";

        [YamlMember(Alias = "font_family")]
        /// <summary>Моноширинный шрифт для читаемости IP-адресов и портов (Cascadia Code, Fira Code).</summary>
        public string FontFamily { get; set; } = "Cascadia Code";

        [YamlMember(Alias = "font_size")]
        /// <summary>Размер шрифта — по умолчанию 12 pt.</summary>
        public int FontSize { get; set; } = 12;

        [YamlMember(Alias = "max_proc_width")]
        /// <summary>Макс. ширина колонки "process" (например, 24 символа) — обрезка с троеточием.</summary>
        public int MaxProcWidth { get; set; } = 24;

        [YamlMember(Alias = "max_ip_width")]
        /// <summary>Макс. ширина IP (по умолчанию ~45) — достаточно для IPv4 + короткого IPv6.</summary>
        public int MaxIpWidth { get; set; } = 45;

        [YamlMember(Alias = "max_port_width")]
        /// <summary>Ширина порта (~6 символов).</summary>
        public int MaxPortWidth { get; set; } = 6;

        [YamlMember(Alias = "max_domain_width")]
        /// <summary>Макс. ширина домена (~50 символов).</summary>
        public int MaxDomainWidth { get; set; } = 50;

        [YamlMember(Alias = "color_console")]
        /// <summary><c>true</c> — терминал выводит цветной текст, на основе статуса DNS.</summary>
        public bool ColorConsole { get; set; } = true;

        [YamlMember(Alias = "skip_local_ips")]
        /// <summary><c>true</c> — скрывать локальные адреса (127.0.0.1, ::1) и loopback.</summary>
        public bool SkipLocalIps { get; set; } = true;

        [YamlMember(Alias = "highlight_style")]
        /// <summary>BRIGHT_WHITE — стиль подсветки выбранных записей в UI-терминале.</summary>
        public string HighlightStyle { get; set; } = "BRIGHT_WHITE";
    }

    /// <summary><c>DnsResolveStatuses</c>: SYN_SENT, ESTABLISHED, FIN_WAIT_1 … CLOSE_WAIT и т.д. — определяет цвет (зелёный/жёлтый/красный).</summary>
    public class MonitorSettings
    {
        [YamlMember(Alias = "dns_resolve_statuses")]
        /// <summary>Какой статус DNS отображать в терминале; по умолчанию SYN_SENT (активный запрос).</summary>
        public List<string> DnsResolveStatuses { get; set; } = new List<string> { "SYN_SENT" };
    }

    /// <summary><c>Level</c> — INFO/DEBUG, <c>MaxFileSize</c> ~1 МБ до ротации.</summary>
    public class LoggingSettings
    {
        [YamlMember(Alias = "level")]
        /// <summary>"INFO" / "DEBUG" — минимальный уровень лога в консоли и файле.</summary>
        public string Level { get; set; } = "INFO";

        [YamlMember(Alias = "max_file_size")]
        /// <summary>Макс. размер логов (~1048576 байт / 1 МБ) до создания бэкапа.</summary>
        public int MaxFileSize { get; set; } = 1048576;

        [YamlMember(Alias = "backup_count")]
        /// <summary>Количество резервных копий (по умолчанию ~5 файлов).</summary>
        public int BackupCount { get; set; } = 5;

        [YamlMember(Alias = "debug_enabled")]
        /// <summary><c>true</c> — включить подробный лог для отладки трафика/протокола.</summary>
        public bool DebugEnabled { get; set; } = false;
    }

    /// <summary>Показывает ли UI полосу прогресса при установке/удалении (Installing, Uninstalling).</summary>
    public class ProgressBarSettings
    {
        [YamlMember(Alias = "Installing")]
        /// <summary><c>true</c> — рисовать полоску во время установки пресета.</summary>
        public bool Installing { get; set; } = false;

        [YamlMember(Alias = "Uninstalling")]
        /// <summary><c>true</c> — рисовать полоску во время удаления пресета.</summary>
        public bool Uninstalling { get; set; } = false;
    }
}