using System.IO;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Services
{
    public class ZapretValidatorService : IZapretValidatorService
    {
        public bool IsZapretValid(string listsPath)
        {
            if (string.IsNullOrEmpty(listsPath) || !Directory.Exists(listsPath))
                return false;

            string parentDir = Path.GetDirectoryName(
                listsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(parentDir))
                return false;

            return File.Exists(Path.Combine(parentDir, "service.bat"));
        }
    }
}