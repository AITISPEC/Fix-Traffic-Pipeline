using System.IO;

namespace PlatformLauncher.Helpers
{
    public static class FileSystemHelper
    {
        public static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        public static void DeleteDirectoryIfEmpty(string path)
        {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                Directory.Delete(path);
        }
    }
}