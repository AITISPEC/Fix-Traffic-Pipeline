using System;
using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IPythonEnvironmentManager
    {
        Task<bool> EnsureEnvironmentAsync(string appDataDir, IProgress<string> progress);
        string GetVenvPythonPath();
        Task<string> FindSystemPythonAsync(); // New method for robust Python finding
    }
}