using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IWarpManager
    {
        Task<bool> IsInstalledAsync();
        Task<bool> InstallAsync();
        Task<bool> EnsureStartedAsync();
        Task<bool> DisconnectAsync();
        Task<string> GetStatusAsync();
        Task<bool> SetMasqueProtocolAsync();
    }
}