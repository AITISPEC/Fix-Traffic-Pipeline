using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Python
{
    public class PythonEnvironmentManager : IPythonEnvironmentManager
    {
        private readonly ILogger _logger;
        private readonly string _extraPythonDir;
        private readonly string _runtimePythonDir;
        private readonly string _pythonZip;
        private readonly string _pythonExe;
        private readonly string _venvDir;
        private readonly string _venvPython;

        public PythonEnvironmentManager(ILogger logger)
        {
            _logger = logger;
            _extraPythonDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extra", "python");
            _runtimePythonDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "python");
            _pythonZip = Path.Combine(_extraPythonDir, "python-3.13.14-embed-amd64.zip");
            _pythonExe = Path.Combine(_runtimePythonDir, "python.exe");
            _venvDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".venv");
            _venvPython = Path.Combine(_venvDir, "Scripts", "python.exe");
        }

        public async Task<bool> EnsureEnvironmentAsync(string appDataDir, IProgress<string> progress)
        {
            try
            {
                _logger.Info("=== НАЧАЛО ПРОВЕРКИ ОКРУЖЕНИЯ PYTHON ===");

                // 1. Проверяем, есть ли системный Python 3.13+
                string systemPython = FindSystemPython();
                string embeddedPython = null;

                if (systemPython != null)
                    _logger.Info("Найден системный Python: " + systemPython);
                else
                    _logger.Info("Системный Python не найден, будет распакован встроенный.");

                // 2. Если системного нет – распаковываем встроенный
                if (systemPython == null)
                {
                    progress?.Report("Системный Python не найден, распаковываем встроенный...");
                    if (!File.Exists(_pythonZip))
                        throw new FileNotFoundException("Архив Python не найден", _pythonZip);

                    if (Directory.Exists(_runtimePythonDir))
                        Directory.Delete(_runtimePythonDir, true);
                    Directory.CreateDirectory(_runtimePythonDir);
                    ZipFile.ExtractToDirectory(_pythonZip, _runtimePythonDir);
                    _logger.Info("Встроенный Python распакован в " + _runtimePythonDir);

                    // В embedded Python нужно включить возможность установки pip (изменить python._pth)
                    string pthPath = Path.Combine(_runtimePythonDir, "python._pth");
                    if (File.Exists(pthPath))
                    {
                        string content = File.ReadAllText(pthPath);
                        content = content.Replace("#import site", "import site");
                        File.WriteAllText(pthPath, content);
                    }

                    embeddedPython = _pythonExe;
                    if (!File.Exists(embeddedPython))
                        throw new Exception("Не удалось найти python.exe после распаковки");
                    progress?.Report("Встроенный Python распакован.");
                }

                // 3. Определяем, какой Python использовать для создания venv
                string basePython = systemPython ?? embeddedPython;
                if (string.IsNullOrEmpty(basePython))
                    throw new Exception("Не найден Python для создания venv");

                // 4. Создаём виртуальное окружение, если отсутствует
                if (!File.Exists(_venvPython))
                {
                    progress?.Report("Создание виртуального окружения...");
                    var result = await RunProcessAsync(basePython, $"-m venv \"{_venvDir}\"", progress);
                    if (result != 0)
                        throw new Exception($"Ошибка создания venv, код: {result}");
                    else
                        _logger.Info("Виртуальное окружение создано в " + _venvDir);
                }

                // 5. Устанавливаем pip (если нужно)
                var pipCheck = await RunProcessAsync(_venvPython, "-m pip --version", null);
                if (pipCheck != 0)
                {
                    progress?.Report("Обновление pip...");
                    await RunProcessAsync(_venvPython, "-m ensurepip --upgrade", null);
                }

                // 6. Установка зависимостей из whl-файлов (только если не установлены)
                string[] whlFiles = {
                    Path.Combine(_extraPythonDir, "watchdog-6.0.0-py3-none-win_amd64.whl"),
                    Path.Combine(_extraPythonDir, "pyyaml-6.0.3-cp313-cp313-win_amd64.whl"),
                    Path.Combine(_extraPythonDir, "psutil-7.2.2-cp313-cp313t-win_amd64.whl")
                };
                foreach (var whl in whlFiles)
                {
                    if (File.Exists(whl))
                    {
                        string packageName = Path.GetFileName(whl).Split('-')[0];
                        var checkResult = await RunProcessAsync(_venvPython, $"-m pip show {packageName}", null);
                        if (checkResult == 0)
                        {
                            _logger.Info($"Пакет {packageName} уже установлен, пропускаем.");
                            continue;
                        }
                        progress?.Report($"Установка {Path.GetFileName(whl)}...");
                        await RunProcessAsync(_venvPython, $"-m pip install \"{whl}\"", progress);
                    }
                    else
                    {
                        _logger.Warning($"Файл {whl} не найден, пропускаем.");
                    }
                }

                // 7. Дополнительно устанавливаем из requirements.txt (если есть)
                string reqPath = Path.Combine(appDataDir, "requirements.txt");
                if (File.Exists(reqPath))
                {
                    progress?.Report("Установка зависимостей из requirements.txt...");
                    await RunProcessAsync(_venvPython, $"-m pip install -r \"{reqPath}\"", progress);
                }

                _logger.Info("=== ОКРУЖЕНИЕ PYTHON ГОТОВО ===");
                progress?.Report("Окружение Python готово.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Критическая ошибка: {ex}");
                progress?.Report($"Ошибка: {ex.Message}");
                return false;
            }
        }

        public string GetVenvPythonPath()
        {
            return File.Exists(_venvPython) ? _venvPython : null;
        }

        private string FindSystemPython()
        {
            try
            {
                var psi = new ProcessStartInfo("python", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(2000);
                if (p.ExitCode != 0) return null;
                string output = p.StandardOutput.ReadToEnd();
                if (output.Contains("Python 3.13"))
                    return "python";
                return null;
            }
            catch { return null; }
        }

        private async Task<int> RunProcessAsync(string fileName, string arguments, IProgress<string> progress)
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
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) progress.Report(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) progress.Report(e.Data); };
            }
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }
    }
}