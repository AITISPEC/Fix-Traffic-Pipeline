using System;
using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface ICommandRunnerService
    {
        Task<string> RunCommandAsync(string command, Action<string>? progressCallback = null);
    }
}