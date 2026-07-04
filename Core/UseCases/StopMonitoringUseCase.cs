using System;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Core.UseCases
{
    public class StopMonitoringUseCase
    {
        private readonly ISessionOrchestrator _sessionOrchestrator;
        private readonly ILogger _logger;

        public StopMonitoringUseCase(ISessionOrchestrator sessionOrchestrator, ILogger logger)
        {
            _sessionOrchestrator = sessionOrchestrator;
            _logger = logger;
        }

        public async Task ExecuteAsync()
        {
            try
            {
                await _sessionOrchestrator.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка остановки мониторинга: {ex}");
                throw;
            }
        }

        public void KillAll()
        {
            _sessionOrchestrator.KillAll();
        }
    }
}