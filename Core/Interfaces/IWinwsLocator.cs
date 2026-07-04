using System.Threading.Tasks;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IWinwsLocator
    {
        Task<string> FindListsPathAsync(int timeoutMs = 3000);
        string FindListsPath(int timeoutMs = 3000);
    }
}