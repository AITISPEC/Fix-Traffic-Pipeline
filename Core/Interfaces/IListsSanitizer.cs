using PlatformLauncher.Domain.Models;
using System.Collections.Generic;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IListsSanitizer
    {
        void Sanitize(string listsPath, GameConfig config);
        void WriteCloudflareDomains(string listsPath, List<string> cloudflareDomains);
    }
}