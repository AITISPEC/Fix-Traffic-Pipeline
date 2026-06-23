using System;
using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface ISessionOrchestrator
    {
        event Action<string> OutputReceived;
        event Action<bool> SessionEnded;
        bool IsRunning { get; }
        Task StartAsync(string gameId, string listsPath, bool monitorOnly, bool warpEnabled, bool filterProcesses = true);
        Task StopAsync();
        void KillAll();
        void SetAskUserCallback(Func<string, Task<bool>> callback);
    }
}