using PlatformLauncher.Domain.Models;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IAppConfigService
    {
        AppConfig Load();
        void Save(AppConfig config);
    }
}