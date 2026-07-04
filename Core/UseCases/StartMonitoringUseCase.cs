using System;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Core.UseCases
{
    public class StartMonitoringUseCase
    {
        private readonly ISessionOrchestrator _sessionOrchestrator;
        private readonly ILogger _logger;

        public StartMonitoringUseCase(ISessionOrchestrator sessionOrchestrator, ILogger logger)
        {
            _sessionOrchestrator = sessionOrchestrator;
            _logger = logger;
        }

        public async Task ExecuteAsync(string gameId, string listsPath, bool monitorOnly, bool warpEnabled, bool filterProcesses = true)
        {
            try
            {
                await _sessionOrchestrator.StartAsync(gameId, listsPath, monitorOnly, warpEnabled, filterProcesses);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка запуска мониторинга: {ex}");
                throw;
            }
        }
    }
}