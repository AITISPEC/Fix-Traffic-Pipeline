using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using PlatformLauncher.Services;

namespace PlatformLauncher.Services
{
    public static class PythonEnvironmentManager
    {
        public static async Task<bool> EnsureEnvironmentAsync(string appDataDir, IProgress<string> progress)
        {
            try
            {
                LauncherLogger.Info("=== НАЧАЛО ПРОВЕРКИ ОКРУЖЕНИЯ PYTHON ===");

                // 1. Поиск Python
                LauncherLogger.Info("Поиск Python в PATH...");
                string pythonExe = FindPythonExecutable();
                if (pythonExe == null)
                {
                    string msg = "Python 3.13+ не найден в PATH. Установите Python с официального сайта.";
                    LauncherLogger.Error(msg);
                    progress?.Report(msg);
                    return false;
                }
                LauncherLogger.Info($"Python найден: {pythonExe}");

                // 2. Путь к venv
                string venvDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".venv");
                string venvPython = Path.Combine(venvDir, "Scripts", "python.exe");
                LauncherLogger.Info($"Путь к venv: {venvDir}");
                LauncherLogger.Info($"Путь к python в venv: {venvPython}");

                // 3. Создание venv (если отсутствует)
                if (!File.Exists(venvPython))
                {
                    LauncherLogger.Info("Виртуальное окружение не найдено, создаём...");
                    progress?.Report("Создание виртуального окружения...");
                    var result = await RunProcessAsync(pythonExe, $"-m venv \"{venvDir}\"", progress);
                    if (result != 0)
                    {
                        string msg = $"Ошибка создания venv, код возврата: {result}";
                        LauncherLogger.Error(msg);
                        progress?.Report(msg);
                        return false;
                    }
                    LauncherLogger.Info("Venv успешно создан");
                }
                else
                {
                    LauncherLogger.Info("Venv уже существует");
                }

                // 4. Проверка pip
                LauncherLogger.Info("Проверка pip...");
                var pipCheck = await RunProcessAsync(venvPython, "-m pip --version", null);
                if (pipCheck != 0)
                {
                    LauncherLogger.Warning($"pip недоступен (код {pipCheck}), обновляем...");
                    progress?.Report("Обновление pip...");
                    await RunProcessAsync(venvPython, "-m ensurepip --upgrade", null);
                }
                else
                {
                    LauncherLogger.Info("pip доступен");
                }

                // 5. Установка зависимостей
                string requirementsPath = Path.Combine(appDataDir, "requirements.txt");
                LauncherLogger.Info($"Путь к requirements.txt: {requirementsPath}");

                if (!File.Exists(requirementsPath))
                {
                    string msg = $"requirements.txt не найден по пути: {requirementsPath}";
                    LauncherLogger.Error(msg);
                    progress?.Report(msg);
                    return false;
                }
                LauncherLogger.Info("requirements.txt найден, начинаем установку зависимостей...");

                int exit = await RunProcessAsync(venvPython, $"-m pip install -r \"{requirementsPath}\"", progress);
                if (exit != 0)
                {
                    string msg = $"Ошибка установки зависимостей, код возврата: {exit}";
                    LauncherLogger.Error(msg);
                    progress?.Report(msg);
                    return false;
                }

                // Очистка консоли
                MainWindow.Instance.ConsoleOutputTerminal.ConPTYTerm.ClearUITerminal(fullReset: false);

                LauncherLogger.Info("=== ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ УСПЕШНО ===");
                progress?.Report("Все проверки пройдены.");
                return true;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Критическая ошибка в EnsureEnvironmentAsync: {ex}");
                progress?.Report($"Ошибка: {ex.Message}");
                return false;
            }
        }

        private static string FindPythonExecutable()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(startInfo);
                if (proc == null) return null;
                proc.WaitForExit(2000);
                if (proc.ExitCode != 0) return null;
                string output = proc.StandardOutput.ReadToEnd();
                if (output.Contains("Python 3."))
                {
                    var parts = output.Split('.');
                    if (parts.Length >= 2 && int.TryParse(parts[0].Replace("Python ", ""), out int major) &&
                        int.TryParse(parts[1], out int minor))
                    {
                        if (major == 3 && minor >= 13)
                            return "python";
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<int> RunProcessAsync(string fileName, string arguments, IProgress<string> progress)
        {
            LauncherLogger.Info($"Запуск процесса: {fileName} {arguments}");
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var proc = new Process { StartInfo = psi };
            if (progress != null)
            {
                proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) progress.Report(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) progress.Report(e.Data); };
            }
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            LauncherLogger.Info($"Процесс завершён с кодом {proc.ExitCode}");
            return proc.ExitCode;
        }

        public static string GetVenvPythonPath()
        {
            string venvDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".venv");
            string venvPython = Path.Combine(venvDir, "Scripts", "python.exe");
            return File.Exists(venvPython) ? venvPython : null;
        }
    }
}