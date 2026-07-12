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

        public async Task<string> RunCommandAsync(string command, Action<string>? progressCallback = null)
        {
            if (string.IsNullOrEmpty(command))
                return "❌ Команда не указана";

            progressCallback?.Invoke($"> {command}");

            try
            {
                string pythonExe = _pythonEnvManager.GetVenvPythonPath();
                if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                    return "❌ Виртуальное окружение Python не найдено";

                string? venvRoot = Path.GetDirectoryName(pythonExe);
                if (string.IsNullOrEmpty(venvRoot))
                    return "❌ Не удалось определить корень окружения";

                // Спецобработка для python -c с перенаправлением ввода
                if (command.StartsWith("python -c ") && command.Contains("<"))
                {
                    return await RunPythonCWithRedirectAsync(command, progressCallback);
                }

                string cmd = command;
                bool isPythonCommand = cmd.StartsWith("python ", StringComparison.OrdinalIgnoreCase);
                bool isPipCommand = cmd.StartsWith("pip ", StringComparison.OrdinalIgnoreCase);

                if (isPythonCommand)
                {
                    cmd = cmd.Replace("python ", $"\"{pythonExe}\" ");
                }
                else if (isPipCommand)
                {
                    cmd = cmd.Replace("pip ", $"\"{pythonExe}\" -m pip ");
                }
                else
                {
                    var parts = cmd.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        string exeName = parts[0];
                        string? exePath = FindExeInVenv(venvRoot, exeName);
                        if (exePath != null)
                        {
                            cmd = $"\"{exePath}\" {(parts.Length > 1 ? parts[1] : "")}";
                        }
                        else
                        {
                            // Не найден .exe – пробуем через python -m (с коррекцией имени модуля)
                            string moduleName = exeName;
                            // Известные команды, у которых имя модуля отличается
                            if (exeName.Equals("pip-review", StringComparison.OrdinalIgnoreCase))
                                moduleName = "pip_review";
                            cmd = $"\"{pythonExe}\" -m {moduleName} {(parts.Length > 1 ? parts[1] : "")}";
                        }
                    }
                    else
                    {
                        cmd = $"\"{pythonExe}\" -m {cmd}";
                    }
                }

                if (string.IsNullOrEmpty(cmd))
                    return "❌ Не удалось построить команду";

                // Устанавливаем кодировку UTF-8
                string fullCommand = $"chcp 65001 >nul && {cmd}";

                var (exitCode, output, error) = await ProcessHelper.RunAsync(
                    "cmd.exe",
                    $"/c {fullCommand}",
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

        /// <summary>
        /// Ищет исполняемый файл в корне окружения и в папке Scripts.
        /// </summary>
        private string? FindExeInVenv(string venvRoot, string exeName)
        {
            // 1. Проверяем Scripts\exeName.exe
            string scriptsPath = Path.Combine(venvRoot, "Scripts", exeName + ".exe");
            if (File.Exists(scriptsPath))
                return scriptsPath;

            // 2. Проверяем Scripts\exeName (без .exe, для .bat/.cmd)
            string scriptsPathNoExt = Path.Combine(venvRoot, "Scripts", exeName);
            if (File.Exists(scriptsPathNoExt))
                return scriptsPathNoExt;

            // 3. Проверяем корень venv\exeName.exe
            string rootPath = Path.Combine(venvRoot, exeName + ".exe");
            if (File.Exists(rootPath))
                return rootPath;

            // 4. Проверяем корень venv\exeName (без .exe)
            string rootPathNoExt = Path.Combine(venvRoot, exeName);
            if (File.Exists(rootPathNoExt))
                return rootPathNoExt;

            return null;
        }

        private async Task<string> RunPythonCWithRedirectAsync(string command, Action<string>? progressCallback)
        {
            string tempBat = Path.Combine(Path.GetTempPath(), $"upgrade_packages_{Guid.NewGuid()}.bat");
            try
            {
                // Парсим команду: python -c "..." < input
                var parts = command.Split(new[] { " < " }, StringSplitOptions.None);
                if (parts.Length != 2)
                    return "❌ Неверный формат команды с перенаправлением";

                string pythonPart = parts[0].Trim(); // python -c "..."
                string inputPart = parts[1].Trim();  // python -m pip list --outdated --format=freeze

                // Создаём .bat, который сначала выполняет input и передаёт в python -c
                string batContent = $"@echo off\r\n{inputPart} | {pythonPart}\r\n";
                await File.WriteAllTextAsync(tempBat, batContent, Encoding.UTF8);

                var (exitCode, output, error) = await ProcessHelper.RunAsync(
                    tempBat,
                    "",
                    _logger,
                    createNoWindow: true,
                    timeoutMs: 60000);

                var result = new StringBuilder();
                if (!string.IsNullOrEmpty(output))
                    result.AppendLine(output);
                if (!string.IsNullOrEmpty(error))
                    result.AppendLine($"⚠️ {error}");

                return result.ToString();
            }
            finally
            {
                try { File.Delete(tempBat); } catch { }
            }
        }
    }
}