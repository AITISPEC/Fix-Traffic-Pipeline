using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;
using PlatformLauncher.Helpers;

namespace PlatformLauncher.Infrastructure.Services
{
    public class CommandRunnerService : ICommandRunnerService
    {
        private readonly ILogger _logger;
        private readonly IPythonEnvironmentManager _pythonEnvManager;

        public CommandRunnerService(ILogger logger, IPythonEnvironmentManager pythonEnvManager)
        {
            _logger = logger;
            _pythonEnvManager = pythonEnvManager;
        }

        public async Task<string> RunCommandAsync(string command, Action<string> progressCallback = null)
        {
            if (string.IsNullOrEmpty(command))
            {
                return "❌ Команда не указана";
            }

            progressCallback?.Invoke($"> {command}");

            try
            {
                string pythonExe = _pythonEnvManager.GetVenvPythonPath();
                if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                {
                    return "❌ Виртуальное окружение Python не найдено";
                }

                pythonExe = $"\"{pythonExe}\"";
                string cmd = command;

                if (cmd.StartsWith("python "))
                    cmd = cmd.Replace("python ", pythonExe + " ");
                else if (cmd.StartsWith("pip "))
                    cmd = cmd.Replace("pip ", pythonExe + " -m pip ");
                else
                    cmd = pythonExe + " -m " + cmd;

                var (exitCode, output, error) = await ProcessHelper.RunAsync(
                    "cmd.exe",
                    $"/c {cmd}",
                    _logger,
                    createNoWindow: true,
                    timeoutMs: 30000);

                if (exitCode == -1)
                {
                    return "⚠️ Команда превысила время выполнения (30 сек), принудительно завершена";
                }

                var result = new StringBuilder();
                if (!string.IsNullOrEmpty(output))
                    result.AppendLine(output);
                if (!string.IsNullOrEmpty(error))
                    result.AppendLine($"⚠️ {error}");

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка выполнения команды: {ex}");
                return $"❌ Ошибка: {ex.Message}";
            }
        }
    }
}