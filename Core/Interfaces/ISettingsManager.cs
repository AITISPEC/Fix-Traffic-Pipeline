namespace PlatformLauncher.Core.Interfaces
{
    public interface ISettingsManager
    {
        bool GetWarpEnabled(string gameId);
        void SetWarpEnabled(string gameId, bool enabled);
        string GetTheme();
        string GetListsPath();
        void SetListsPath(string path);
        void SetTheme(string themeId);
        // Фильтры
        (bool Installed, bool NotInstalled, bool Custom) GetFilterState();
        void SetFilterState(bool installed, bool notInstalled, bool custom);
    }
}