using System;
using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface INetworkFixService
    {
        Task FixInternetAsync(Action<string> progress);
    }
}