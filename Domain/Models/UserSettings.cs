using System.Collections.Generic;

namespace PlatformLauncher.Domain.Models
{
    public class UserSettings
    {
        public Dictionary<string, bool> WarpEnabled { get; set; } = new();
        public bool FilterInstalled { get; set; }
        public bool FilterNotInstalled { get; set; }
        public bool FilterCustom { get; set; }
        public string ListsPath { get; set; } = string.Empty;
    }
}