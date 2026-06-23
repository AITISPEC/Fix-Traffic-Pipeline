using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    public class GameConfig
    {
        [YamlMember(Alias = "target_processes")]
        public List<TargetProcess> TargetProcesses { get; set; }

        [YamlMember(Alias = "cloudflare_domains")]
        public List<string> CloudflareDomains { get; set; }

        [YamlMember(Alias = "include_domains")]
        public List<string> IncludeDomains { get; set; }

        [YamlMember(Alias = "exclude_domains")]
        public List<string> ExcludeDomains { get; set; }

        [YamlMember(Alias = "include_ips")]
        public List<string> IncludeIps { get; set; }

        [YamlMember(Alias = "exclude_ips")]
        public List<string> ExcludeIps { get; set; }

        [YamlMember(Alias = "pass_ips")]
        public List<string> PassIps { get; set; }

        [YamlMember(Alias = "ports")]
        public PortsConfig Ports { get; set; }

        [YamlMember(Alias = "lists")]
        public ListsConfig Lists { get; set; }

        [YamlMember(Alias = "scan_interval")]
        public double ScanInterval { get; set; }

        [YamlMember(Alias = "list_flush_interval")]
        public double? ListFlushInterval { get; set; }

        [YamlMember(Alias = "logged_connections_max")]
        public int LoggedConnectionsMax { get; set; }

        [YamlMember(Alias = "dns_timeout")]
        public double DnsTimeout { get; set; }

        [YamlMember(Alias = "dns_resolve_statuses")]
        public List<string> DnsResolveStatuses { get; set; }

        [YamlMember(Alias = "dns_ignore_statuses")]
        public List<string> DnsIgnoreStatuses { get; set; }

        [YamlMember(Alias = "console_output_statuses")]
        public List<string> ConsoleOutputStatuses { get; set; }

        [YamlMember(Alias = "console_ignore_statuses")]
        public List<string> ConsoleIgnoreStatuses { get; set; }

        [YamlMember(Alias = "console")]
        public ConsoleConfig Console { get; set; }

        [YamlMember(Alias = "color_console")]
        public bool ColorConsole { get; set; }

        [YamlMember(Alias = "skip_local_ips")]
        public bool SkipLocalIps { get; set; }

        [YamlMember(Alias = "highlight_style")]
        public string HighlightStyle { get; set; }

        [YamlMember(Alias = "warp_supported")]
        public bool? WarpSupported { get; set; } = false;

        [YamlMember(Alias = "version")]
        public int? Version { get; set; } = 1;

        [YamlMember(Alias = "list_rules")]
        public Dictionary<string, ListRule> ListRules { get; set; }
    }

    public class TargetProcess
    {
        [YamlMember(Alias = "name")]
        public string Name { get; set; }

        [YamlMember(Alias = "check_path")]
        public bool CheckPath { get; set; }
    }

    public class PortsConfig
    {
        [YamlMember(Alias = "tcp")]
        public List<object> Tcp { get; set; }

        [YamlMember(Alias = "udp")]
        public List<object> Udp { get; set; }
    }

    public class ListsConfig
    {
        [YamlMember(Alias = "ip_file")]
        public string IpFile { get; set; }

        [YamlMember(Alias = "domain_file")]
        public string DomainFile { get; set; }

        [YamlMember(Alias = "general_domain_file")]
        public string GeneralDomainFile { get; set; }

        [YamlMember(Alias = "exclude_ip_file")]
        public string ExcludeIpFile { get; set; }

        [YamlMember(Alias = "exclude_domain_file")]
        public string ExcludeDomainFile { get; set; }

        [YamlMember(Alias = "session_ip_file")]
        public string SessionIpFile { get; set; }
    }

    public class ConsoleConfig
    {
        [YamlMember(Alias = "max_proc_width")]
        public int MaxProcWidth { get; set; }

        [YamlMember(Alias = "max_ip_width")]
        public int MaxIpWidth { get; set; }

        [YamlMember(Alias = "max_port_width")]
        public int MaxPortWidth { get; set; }

        [YamlMember(Alias = "max_domain_width")]
        public int MaxDomainWidth { get; set; }
    }

    public class ListRule
    {
        [YamlMember(Alias = "action")]
        public string Action { get; set; }

        [YamlMember(Alias = "target")]
        public string Target { get; set; }
    }
}