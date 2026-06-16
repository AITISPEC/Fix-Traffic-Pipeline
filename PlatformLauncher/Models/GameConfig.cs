using System.Collections.Generic;

namespace PlatformLauncher.Models
{
    public class GameConfig
    {
        public List<TargetProcess> TargetProcesses { get; set; }
        public List<string> CloudflareDomains { get; set; }
        public List<string> IncludeDomains { get; set; }
        public List<string> ExcludeDomains { get; set; }
        public List<string> IncludeIps { get; set; }
        public List<string> ExcludeIps { get; set; }
        public List<string> PassIps { get; set; }
        public PortsConfig Ports { get; set; }
        public ListsConfig Lists { get; set; }
        public double ScanInterval { get; set; }
        public int LoggedConnectionsMax { get; set; }
        public double DnsTimeout { get; set; }
        public List<string> DnsResolveStatuses { get; set; }
        public ConsoleConfig Console { get; set; }
        public bool ColorConsole { get; set; }
        public bool SkipLocalIps { get; set; }
        public string HighlightStyle { get; set; }
    }

    public class TargetProcess
    {
        public string Name { get; set; }
        public bool CheckPath { get; set; }
    }

    public class PortsConfig
    {
        public List<object> Tcp { get; set; }
        public List<object> Udp { get; set; }
    }

    public class ListsConfig
    {
        public string IpFile { get; set; }
        public string DomainFile { get; set; }
        public string GeneralDomainFile { get; set; }
        public string ExcludeIpFile { get; set; }
        public string ExcludeDomainFile { get; set; }
        public string SessionIpFile { get; set; }
    }

    public class ConsoleConfig
    {
        public int MaxProcWidth { get; set; }
        public int MaxIpWidth { get; set; }
        public int MaxPortWidth { get; set; }
        public int MaxDomainWidth { get; set; }
    }
}