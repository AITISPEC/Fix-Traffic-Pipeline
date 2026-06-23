using System;
using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IPythonProcessManager
    {
        event Action<string> OutputReceived;
        event Action<int> ProcessExited;
        bool IsRunning { get; }
        Task StartAsync(string gameId, string listsPath, bool monitorOnly = false, bool filterProcesses = true);
        Task StopAsync(int timeoutMs = 2000);
    }
}