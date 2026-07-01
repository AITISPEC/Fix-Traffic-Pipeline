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
            _runtimePythonDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "venv");
            _pythonZip = Path.Combine(_extraPythonDir, "python-3.13.14-embed-amd64.zip");
            _pythonExe = Path.Combine(_runtimePythonDir, "python.exe");
            _venvDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".venv");
            _venvPython = Path.Combine(_venvDir, "Scripts", "python.exe");
        }

        public string GetVenvPythonPath()
        {
            if (File.Exists(_venvPython)) return _venvPython;
            if (File.Exists(_pythonExe)) return _pythonExe;
            return null;
        }

        public async Task<bool> EnsureEnvironmentAsync(string appDataDir, IProgress<string> progress)
        {
            try
            {
                _logger.Info("=== НАЧАЛО ПРОВЕРКИ ОКРУЖЕНИЯ PYTHON ===");

                string systemPython = FindSystemPython();

                if (systemPython != null)
                {
                    _logger.Info("Найден системный Python: " + systemPython);

                    if (!File.Exists(_venvPython))
                    {
                        progress?.Report("Создание виртуального окружения (.venv)...");
                        var result = await RunProcessAsync(systemPython, $"-m venv \"{_venvDir}\"", progress);
                        if (result != 0)
                            throw new Exception($"Ошибка создания venv, код: {result}");
                    }

                    await InstallPackagesAsync(_venvPython, appDataDir, progress);
                }
                else
                {
                    _logger.Info("Системный Python не найден, используем встроенный.");
                    progress?.Report("Системный Python не найден, распаковываем встроенный...");

                    if (!File.Exists(_pythonZip))
                        throw new FileNotFoundException("Архив Python не найден", _pythonZip);

                    if (!File.Exists(_pythonExe))
                    {
                        if (Directory.Exists(_runtimePythonDir))
                            Directory.Delete(_runtimePythonDir, true);
                        Directory.CreateDirectory(_runtimePythonDir);
                        ZipFile.ExtractToDirectory(_pythonZip, _runtimePythonDir);

                        string pthPath = Path.Combine(_runtimePythonDir, "python313._pth");
                        if (File.Exists(pthPath))
                        {
                            string content = File.ReadAllText(pthPath);
                            content = content.Replace("#import site", "import site");
                            if (!content.Contains("Lib/site-packages"))
                                content += Environment.NewLine + "Lib/site-packages";
                            if (!content.Contains("Scripts"))
                                content += Environment.NewLine + "Scripts";
                            if (!content.Contains("../data"))
                                content += Environment.NewLine + "../data";
                            if (!content.Contains("../data/src"))
                                content += Environment.NewLine + "../data/src";
                            if (!content.Contains("../data/configs"))
                                content += Environment.NewLine + "../data/configs";
                            File.WriteAllText(pthPath, content);
                        }
                    }

                    await InstallPackagesAsync(_pythonExe, appDataDir, progress);
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

        private async Task InstallPackagesAsync(string pythonExe, string appDataDir, IProgress<string> progress)
        {
            var pipCheck = await RunProcessAsync(pythonExe, "-m pip --version", null);
            if (pipCheck != 0)
            {
                progress?.Report("Установка pip...");
                string getPipPath = Path.Combine(_extraPythonDir, "get-pip.py");
                if (!File.Exists(getPipPath))
                    throw new FileNotFoundException("get-pip.py не найден в extra/python/", getPipPath);

                var pipInstallResult = await RunProcessAsync(pythonExe, $"\"{getPipPath}\"", progress);
                if (pipInstallResult != 0)
                    throw new Exception($"Ошибка установки pip, код: {pipInstallResult}");
            }

            string[] whlFiles = {
                Path.Combine(_extraPythonDir, "watchdog-6.0.0-py3-none-win_amd64.whl"),
                Path.Combine(_extraPythonDir, "pyyaml-6.0.3-cp313-cp313-win_amd64.whl"),
                Path.Combine(_extraPythonDir, "psutil-7.2.2-cp37-abi3-win_amd64.whl")
            };
            foreach (var whl in whlFiles)
            {
                if (File.Exists(whl))
                {
                    string packageName = Path.GetFileName(whl).Split('-')[0];
                    var checkResult = await RunProcessAsync(pythonExe, $"-m pip show {packageName}", null);
                    if (checkResult == 0) continue;
                    progress?.Report($"Установка {Path.GetFileName(whl)}...");
                    await RunProcessAsync(pythonExe, $"-m pip install \"{whl}\"", progress);
                }
            }

            string reqPath = Path.Combine(appDataDir, "requirements.txt");
            if (File.Exists(reqPath))
            {
                progress?.Report("Установка зависимостей из requirements.txt...");
                await RunProcessAsync(pythonExe, $"-m pip install -r \"{reqPath}\"", progress);
            }
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
                if (output.Contains("Python 3.13")) return "python";
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка проверки системного Python: {ex.Message}");
                return null;
            }
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
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
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