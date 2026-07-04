using System.Collections.Generic;
using System.Threading.Tasks;
using PlatformLauncher.Domain.Models;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Core.UseCases
{
    public class SyncPresetsUseCase
    {
        private readonly IUpdateService _updateService;

        public SyncPresetsUseCase(IUpdateService updateService)
        {
            _updateService = updateService;
        }

        public async Task<bool> ExecuteAsync()
        {
            return await _updateService.SyncFromGitHubAsync();
        }
    }
}