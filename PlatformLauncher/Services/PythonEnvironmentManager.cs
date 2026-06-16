using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace PlatformLauncher.Services
{
    public static class PythonEnvironmentManager
    {
        public static async Task<bool> EnsureEnvironmentAsync(string appDataDir, IProgress<string> progress)
        {
            string pythonExe = FindPythonExecutable();
            if (pythonExe == null)
            {
                progress?.Report("Python 3.12+ не найден в PATH. Установите Python с официального сайта.");
                return false;
            }
            progress?.Report($"Найден Python: {pythonExe}");

            string venvDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".venv");
            string venvPython = Path.Combine(venvDir, "Scripts", "python.exe");
            if (!File.Exists(venvPython))
            {
                progress?.Report("Создание виртуального окружения...");
                var result = await RunProcessAsync(pythonExe, $"-m venv \"{venvDir}\"", progress);
                if (result != 0)
                {
                    progress?.Report("Ошибка создания venv");
                    return false;
                }
            }

            progress?.Report("Проверка/установка зависимостей...");
            string requirementsPath = Path.Combine(appDataDir, "requirements.txt");
            if (!File.Exists(requirementsPath))
            {
                progress?.Report("requirements.txt не найден во встроенных ресурсах");
                return false;
            }

            // проверка установки pip
            var pipCheck = await RunProcessAsync(venvPython, "-m pip --version", null);
            if (pipCheck != 0)
            {
                progress?.Report("pip недоступен, обновляем...");
                await RunProcessAsync(venvPython, "-m ensurepip --upgrade", null);
            }

            // устанавливаем зависимости
            int exit = await RunProcessAsync(venvPython, $"-m pip install -r \"{requirementsPath}\"", progress);
            if (exit != 0)
            {
                progress?.Report("Не удалось установить зависимости");
                return false;
            }

            progress?.Report("Все проверки пройдены.");
            return true;
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
                // проверяем версию 3.12+
                if (output.Contains("Python 3."))
                {
                    var parts = output.Split('.');
                    if (parts.Length >= 2 && int.TryParse(parts[0].Replace("Python ", ""), out int major) &&
                        int.TryParse(parts[1], out int minor))
                    {
                        if (major == 3 && minor >= 12)
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