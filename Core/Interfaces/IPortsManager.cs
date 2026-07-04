using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IPortsManager
    {
        Task<(bool Success, string Error)> AddRulesAsync(List<object> tcpPorts, List<object> udpPorts, string gameId, Action<string> progressCallback = null);
        Task<(bool Success, string Error)> RemoveAllRulesAsync(string gameId, Action<string> progressCallback = null);
    }
}