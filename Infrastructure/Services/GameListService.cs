using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Domain.Models;

namespace PlatformLauncher.Infrastructure.Services
{
    public class GameListService : IGameListService
    {
        private readonly IUpdateService _updateService;
        private readonly ILogger _logger;
        public ObservableCollection<GamePreset> Games { get; } = new ObservableCollection<GamePreset>();

        public GameListService(IUpdateService updateService, ILogger logger)
        {
            _updateService = updateService;
            _logger = logger;
        }

        public void LoadGames()
        {
            var presets = _updateService.LoadPresets();
            Games.Clear();
            foreach (var p in presets)
            {
                string configPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "data", "configs", $"{p.Id}.yaml");
                bool isValid = false;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var config = _updateService.LoadGameConfig(p.Id);
                        isValid = config != null;
                    }
                    catch
                    {
                        isValid = false;
                    }
                }
                p.ConfigDownloaded = isValid;
                Games.Add(p);
            }
        }

        public void ApplyFilters(
            string searchText,
            bool filterInstalled,
            bool filterNotInstalled,
            bool filterCustom,
            string sortOptionId)
        {
            var allPresets = _updateService.LoadPresets();
            IEnumerable<GamePreset> filtered = allPresets.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string search = searchText.ToLower();
                filtered = filtered.Where(p =>
                    p.Name.ToLower().Contains(search) ||
                    p.Id.ToLower().Contains(search));
            }

            if (filterInstalled)
                filtered = filtered.Where(p => p.Installed);
            else if (filterNotInstalled)
                filtered = filtered.Where(p => !p.Installed);

            // ✅ ИСПРАВЛЕНИЕ: Используем IsUserPreset вместо проверки наличия файла
            if (filterCustom)
                filtered = filtered.Where(p => p.IsUserPreset);

            List<GamePreset> list = sortOptionId switch
            {
                "alphabetical" => filtered.OrderBy(p => p.Name).ToList(),
                "not_installed" => filtered
                    .OrderBy(p => p.Installed ? 1 : 0)
                    .ThenBy(p => p.Name)
                    .ToList(),
                _ => filtered
                    .OrderBy(p => p.Installed ? 0 : 1)
                    .ThenBy(p => p.Name)
                    .ToList()
            };

            Games.Clear();
            foreach (var p in list)
                Games.Add(p);
        }

        public string GetFilterHeader(int total, int shown)
        {
            return shown == total
                ? $"Фильтры ({shown})"
                : $"Фильтры (показано {shown} из {total})";
        }
    }
}