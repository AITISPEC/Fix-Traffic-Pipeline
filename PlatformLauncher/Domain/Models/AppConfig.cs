using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    public class AppConfig
    {
        [YamlMember(Alias = "app")]
        public AppSettings App { get; set; }

        [YamlMember(Alias = "terminal")]
        public TerminalSettings Terminal { get; set; }

        [YamlMember(Alias = "monitor")]
        public MonitorSettings Monitor { get; set; }

        [YamlMember(Alias = "logging")]
        public LoggingSettings Logging { get; set; }

        [YamlMember(Alias = "selected_theme")]
        public string SelectedTheme { get; set; } = "fluent-light";

        [YamlMember(Alias = "lists")]
        public ListsConfig Lists { get; set; }

        [YamlMember(Alias = "cloudflare_domains")]
        public List<string> CloudflareDomains { get; set; }
    }

    public class AppSettings
    {
        [YamlMember(Alias = "app_version")]
        public string AppVersion { get; set; } = "1.0.0";

        [YamlMember(Alias = "python_stop_timeout_ms")]
        public int PythonStopTimeoutMs { get; set; } = 2000;

        [YamlMember(Alias = "default_lists_path")]
        public string DefaultListsPath { get; set; } = "";
    }

    public class TerminalSettings
    {
        [YamlMember(Alias = "theme")]
        public string Theme { get; set; } = "Dark";

        [YamlMember(Alias = "font_family")]
        public string FontFamily { get; set; } = "Cascadia Code";

        [YamlMember(Alias = "font_size")]
        public int FontSize { get; set; } = 12;

        [YamlMember(Alias = "max_proc_width")]
        public int MaxProcWidth { get; set; } = 24;

        [YamlMember(Alias = "max_ip_width")]
        public int MaxIpWidth { get; set; } = 45;

        [YamlMember(Alias = "max_port_width")]
        public int MaxPortWidth { get; set; } = 6;

        [YamlMember(Alias = "max_domain_width")]
        public int MaxDomainWidth { get; set; } = 50;

        [YamlMember(Alias = "color_console")]
        public bool ColorConsole { get; set; } = true;

        [YamlMember(Alias = "skip_local_ips")]
        public bool SkipLocalIps { get; set; } = true;

        [YamlMember(Alias = "highlight_style")]
        public string HighlightStyle { get; set; } = "BRIGHT_WHITE";
    }

    public class MonitorSettings
    {
        [YamlMember(Alias = "dns_resolve_statuses")]
        public List<string> DnsResolveStatuses { get; set; } = new List<string> { "SYN_SENT" };
    }

    public class LoggingSettings
    {
        [YamlMember(Alias = "level")]
        public string Level { get; set; } = "INFO";

        [YamlMember(Alias = "max_file_size")]
        public int MaxFileSize { get; set; } = 1048576;

        [YamlMember(Alias = "backup_count")]
        public int BackupCount { get; set; } = 5;

        [YamlMember(Alias = "debug_enabled")]
        public bool DebugEnabled { get; set; } = false;
    }
}