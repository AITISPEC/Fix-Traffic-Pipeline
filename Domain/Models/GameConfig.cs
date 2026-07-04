/// <summary>Настройка игры: правила DPI, списки, тайминги. Сериализуется в zapret/lists/*.yaml.</summary>
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    /// <summary><c>target_processes</c>: списки процессов, за которые перехватывать DNS (например, apex.exe, hunt.exe). Если указан <c>check_path: true</c>, учитывается только полный путь.</summary>
    public class GameConfig
    {
        [YamlMember(Alias = "target_processes")]
        /// <summary>Процессы (например, apex.exe): если <c>check_path: true</c> — фильтр по полному пути к исполняемому файлу.</summary>
        public List<TargetProcess> TargetProcesses { get; set; }

        [YamlMember(Alias = "cloudflare_domains")]
        /// <summary>Если домен из этого списка — трафик обрабатывается по WARP (MASQUE).</summary>
        public List<string> CloudflareDomains { get; set; }

        [YamlMember(Alias = "include_domains")]
        /// <summary><c>true</c> — добавлять эти домены в списки zapret.</summary>
        public List<string> IncludeDomains { get; set; }

        [YamlMember(Alias = "exclude_domains")]
        /// <summary><c>true</c> — не добавлять эти домены (например, обновления клиента).</summary>
        public List<string> ExcludeDomains { get; set; }

        [YamlMember(Alias = "include_ips")]
        /// <summary><c>true</c> — добавлять IP в список обхода.</summary>
        public List<string> IncludeIps { get; set; }

        [YamlMember(Alias = "exclude_ips")]
        /// <summary><c>true</c> — исключать из правил.</summary>
        public List<string> ExcludeIps { get; set; }

        [YamlMember(Alias = "pass_ips")]
        /// <summary><c>true</c> — разрешить через фаервол, но не добавлять в списки обхода DPI.</summary>
        public List<string> PassIps { get; set; }

        [YamlMember(Alias = "ports")]
        /// <summary>Порт/диапазон портов для применения правил (например, TCP 80, UDP 53).</summary>
        public PortsConfig Ports { get; set; }

        [YamlMember(Alias = "lists")]
        /// <summary>Путь к файлам списков: ip_file, domain_file — файлы с IP/доменами для zapret.</summary>
        public ListsConfig Lists { get; set; }

        [YamlMember(Alias = "scan_interval")]
        /// <summary>Интервал сканирования в секундах (например, 5.0).</summary>
        public double ScanInterval { get; set; }

        [YamlMember(Alias = "list_flush_interval")]
        /// <summary>Интервал обновления файлов списков запретом — опционально.</summary>
        public double? ListFlushInterval { get; set; }

        [YamlMember(Alias = "logged_connections_max")]
        /// <summary><c>true</c> — логировать только N последних соединений (например, 10).</summary>
        public int LoggedConnectionsMax { get; set; }

        [YamlMember(Alias = "dns_timeout")]
        /// <summary>Тайм-аут DNS-запроса в секундах (~50 мс).</summary>
        public double DnsTimeout { get; set; }

        [YamlMember(Alias = "dns_resolve_statuses")]
        /// <summary>Какой статус DNS отслеживать (SYN_SENT, ESTABLISHED … CLOSE_WAIT).</summary>
        public List<string> DnsResolveStatuses { get; set; }

        [YamlMember(Alias = "dns_ignore_statuses")]
        /// <summary><c>true</c> — игнорировать эти статусы в логировании.</summary>
        public List<string> DnsIgnoreStatuses { get; set; }

        [YamlMember(Alias = "console_output_statuses")]
        /// <summary><c>true</c> — выводить эти статусы в терминал (зелёный/жёлтый).</summary>
        public List<string> ConsoleOutputStatuses { get; set; }

        [YamlMember(Alias = "console_ignore_statuses")]
        /// <summary><c>true</c> — не показывать в терминале.</summary>
        public List<string> ConsoleIgnoreStatuses { get; set; }

        [YamlMember(Alias = "console")]
        /// <summary>Ширина колонок вывода для UI-терминала (process, ip, port).</summary>
        public ConsoleConfig Console { get; set; }

        [YamlMember(Alias = "color_console")]
        /// <summary><c>true</c> — цветной вывод в терминале.</summary>
        public bool ColorConsole { get; set; }

        [YamlMember(Alias = "skip_local_ips")]
        /// <summary><c>true</c> — скрывать локальные адреса 127.0.0.1, ::1 и т.п.</summary>
        public bool SkipLocalIps { get; set; }

        [YamlMember(Alias = "highlight_style")]
        /// <summary>Стиль подсветки для UI-терминала (BRIGHT_WHITE).</summary>
        public string HighlightStyle { get; set; }

        [YamlMember(Alias = "warp_supported")]
        /// <summary><c>true</c>, если игра поддерживает Cloudflare WARP — флаг для UI-фильтрации.</summary>
        public bool? WarpSupported { get; set; } = false;

        [YamlMember(Alias = "version")]
        /// <summary>Формат версии — используется при миграции конфигов (например, 1).</summary>
        public int? Version { get; set; } = 1;

        [YamlMember(Alias = "list_rules")]
        /// <summary><c>dict</c>: уникальные правила zapret для доменов/IP (инъекция в списки — action: add|remove).</summary>
        public Dictionary<string, ListRule> ListRules { get; set; }
    }

    /// <summary><c>Name</c> — процесс (apex.exe), <c>check_path: true</c> проверяет полный путь к исполняемому.</summary>
    public class TargetProcess
    {
        [YamlMember(Alias = "name")]
        public string Name { get; set; }

        [YamlMember(Alias = "check_path")]
        /// <summary><c>true</c> — считать только процесс с точным путём (например, C:\Games\apex.exe).</summary>
        public bool CheckPath { get; set; }
    }

    /// <summary><c>Tcp/UDP</c>: порт и протокол для применения правил.</summary>
    public class PortsConfig
    {
        [YamlMember(Alias = "tcp")]
        /// <summary>Протоколы TCP — список объектов (порт, например).</summary>
        public List<object> Tcp { get; set; }

        [YamlMember(Alias = "udp")]
        /// <summary>Протоколы UDP — список объектов.</summary>
        public List<object> Udp { get; set; }
    }

    /// <summary><c>ip_file</c> — список IP, <c>domain_file</c> — список доменов, <c>general_domain_file</c> — общие.</summary>
    public class ListsConfig
    {
        [YamlMember(Alias = "ip_file")]
        /// <summary>Путь к файлу с IP-адресами для применения правил zapret.</summary>
        public string IpFile { get; set; }

        [YamlMember(Alias = "domain_file")]
        /// <summary>Путь к файлу с доменами для DPI bypass.</summary>
        public string DomainFile { get; set; }

        [YamlMember(Alias = "general_domain_file")]
        /// <summary>Общий файл доменов — используется при запуске через службу.</summary>
        public string GeneralDomainFile { get; set; }

        [YamlMember(Alias = "exclude_ip_file")]
        /// <summary>Исключения по IP (исключить из правил).</summary>
        public string ExcludeIpFile { get; set; }

        [YamlMember(Alias = "exclude_domain_file")]
        /// <summary>Исключения по домену.</summary>
        public string ExcludeDomainFile { get; set; }

    }

    /// <summary><c>max_proc_width</c> — ширина колонки имени процесса в UI (например, 24 символа).</summary>
    public class ConsoleConfig
    {
        [YamlMember(Alias = "max_proc_width")]
        /// <summary>Макс. ширина колонки «process» в терминале UI (~24).</summary>
        public int MaxProcWidth { get; set; }

        [YamlMember(Alias = "max_ip_width")]
        /// <summary>Макс. ширина IP-адресов (~45).</summary>
        public int MaxIpWidth { get; set; }

        [YamlMember(Alias = "max_port_width")]
        /// <summary>Ширина порта (~6 символов).</summary>
        public int MaxPortWidth { get; set; }

        [YamlMember(Alias = "max_domain_width")]
        /// <summary>Макс. ширина домена (~50 символов).</summary>
        public int MaxDomainWidth { get; set; }
    }

    /// <summary><c>Action</c>: add|remove, <c>Target</c>: IP/домен — правило для запрета.</summary>
    public class ListRule
    {
        [YamlMember(Alias = "action")]
        /// <summary>"add" | "remove" — добавить или удалить правило из списка zapret.</summary>
        public string Action { get; set; }

        [YamlMember(Alias = "target")]
        /// <summary>Целевой IP/домен для правила (например, 1.2.3.4).</summary>
        public string Target { get; set; }
    }
}