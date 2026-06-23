namespace PlatformLauncher.Core.Interfaces
{
    public interface ISettingsManager
    {
        bool GetWarpEnabled(string gameId);
        void SetWarpEnabled(string gameId, bool enabled);
        string GetTheme();
        void SetTheme(string themeId);
    }
}