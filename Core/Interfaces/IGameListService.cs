using System.Collections.Generic;
using System.Collections.ObjectModel;
using PlatformLauncher.Domain.Models;

namespace PlatformLauncher.Core.Interfaces
{
    public interface IGameListService
    {
        ObservableCollection<GamePreset> Games { get; }
        void LoadGames();
        void ApplyFilters(
            string searchText,
            bool filterInstalled,
            bool filterNotInstalled,
            bool filterCustom,
            string sortOptionId);
        string GetFilterHeader(int total, int shown);
    }
}