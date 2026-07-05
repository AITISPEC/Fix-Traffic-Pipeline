using PlatformLauncher.Core.Interfaces;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace PlatformLauncher.Infrastructure.Python
{
    /// <summary>Management of the embedded Python 3.13 virtual environment - unpacks zip, writes .pth hacks, installs pip and dependencies via get-pip.py and wheel files.</summary>
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
                // Multithreaded input to IProgress — does not block UI.
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) progress.Report(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) progress.Report(e.Data); };
            }
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }

        /// <summary>Check for system Python 3.13 using Process.Start() - bottleneck: fixed 2000 ms timeout.</summary>
        public async Task<string> FindSystemPythonAsync()
        {
            try
            {
                var psi = new ProcessStartInfo("python", "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                p.WaitForExit(2000);
                if (p.ExitCode != 0) return null;

                string output = p.StandardOutput.ReadToEnd();
                return output.Contains("Python 3.13") ? "python" : null;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Ошибка проверки системного Python: {ex.Message}");
                return null;
            }
        }

        /// <summary>Асинхронная инициализация окружения Python: проверяет наличие системного Python → создает venv или распаковывает embed-архив → устанавливает пакеты.</summary>
        public async Task<bool> EnsureEnvironmentAsync(string appDataDir, IProgress<string> progress)
        {
            try
            {
                _logger.Info("=== НАЧАЛО ПРОВЕРКИ ОКРУЖЕНИЯ PYTHON ===");
                progress?.Report("⏳ Поиск системного Python...");

                // 1. Надежный поиск системного Python
                string systemPython = await FindSystemPythonAsync();

                if (!string.IsNullOrEmpty(systemPython) && File.Exists(systemPython))
                {
                    _logger.Info($"✅ Найден системный Python: {systemPython}");
                    progress?.Report($"✅ Найден системный Python: {systemPython}");

                    string venvPythonExe = Path.Combine(_venvDir, "Scripts", "python.exe");
                    if (!File.Exists(venvPythonExe))
                    {
                        progress?.Report("⏳ Создание виртуального окружения (.venv)...");
                        // Экранируем путь для корректной работы с пробелами
                        var result = await RunProcessAsync($"\"{systemPython}\"", $"-m venv \"{_venvDir}\"", progress);
                        if (result != 0)
                            throw new Exception($"Ошибка создания venv через системный Python. Код: {result}");

                        _logger.Info("✅ Venv успешно создан.");
                        progress?.Report("✅ Venv успешно создан.");
                    }

                    await InstallPackagesAsync(venvPythonExe, appDataDir, progress);
                    return true;
                }
                else
                {
                    // 2. Только если системный НЕТ → используем встроенный
                    _logger.Info("❌ Системный Python не найден. Переход к встроенному runtime.");
                    progress?.Report("⚠️ Системный Python не найден, распаковываем встроенный...");

                    if (!File.Exists(_pythonZip))
                        throw new FileNotFoundException($"Архив встроенного Python не найден: {_pythonZip}");

                    if (Directory.Exists(_runtimePythonDir))
                        Directory.Delete(_runtimePythonDir, true);
                    Directory.CreateDirectory(_runtimePythonDir);

                    progress?.Report("⏳ Распаковка встроенного Python...");
                    await Task.Run(() => ZipFile.ExtractToDirectory(_pythonZip, _runtimePythonDir, true));

                    string pthPath = Path.Combine(_runtimePythonDir, "python313._pth");
                    if (File.Exists(pthPath))
                    {
                        string content = File.ReadAllText(pthPath);
                        content = content.Replace("#import site", "import site");
                        if (!content.Contains("Lib/site-packages")) content += "\nLib/site-packages";
                        File.WriteAllText(pthPath, content);
                    }

                    await InstallPackagesAsync(_pythonExe, appDataDir, progress);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"❌ Критическая ошибка подготовки Python: {ex.Message}\n{ex.StackTrace}");
                progress?.Report($"❌ Ошибка: {ex.Message}");
                return false; // Явный возврат false, без скрытых фоллбэков
            }
        }

        private async Task InstallPackagesAsync(string pythonExe, string appDataDir, IProgress<string> progress)
        {
            var pipCheck = await RunProcessAsync(pythonExe, "-m pip --version", null);
            if (pipCheck != 0)
            {
                progress?.Report("Installing pip...");
                string getPipPath = Path.Combine(_extraPythonDir, "get-pip.py");
                if (!File.Exists(getPipPath))
                    throw new FileNotFoundException("get-pip.py not found in extra/python/", getPipPath);

                var pipInstallResult = await RunProcessAsync(pythonExe, $"\"{getPipPath}\"", progress);
                if (pipInstallResult != 0)
                    throw new Exception($"Error installing pip, code: {pipInstallResult}");
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
                    progress?.Report($"Installing {Path.GetFileName(whl)}...");
                    await RunProcessAsync(pythonExe, $"-m pip install \"{whl}\"", progress);
                }
            }

            string reqPath = Path.Combine(appDataDir, "requirements.txt");
            if (File.Exists(reqPath))
            {
                progress?.Report("Installing dependencies from requirements.txt...");
                await RunProcessAsync(pythonExe, $"-m pip install -r \"{reqPath}\"", progress);
            }
        }

        private async Task SetupEmbeddedPythonAsync(string appDataDir, IProgress<string> progress)
        {
            string pthPath = Path.Combine(_runtimePythonDir, "python313._pth");
            if (File.Exists(pthPath))
            {
                // .pth-файл — встроенный механизм Python для инициализации путей
                string content = File.ReadAllText(pthPath);

                // Раскомментируем import site
                content = content.Replace("#import site", "import site");

                // Добавляем пути к пакетам, если их нет
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

        public async Task<string> FindPythonViaWhereQuiet()
        {
            string registryResult = await FindPythonInRegistry();
            if (string.IsNullOrEmpty(registryResult)) return null;

            // Correct async pattern - don't use Task.Run here
            var knownPathResult = await FindPythonInKnownPaths();

            return !string.IsNullOrEmpty(knownPathResult) ? knownPathResult : null;
        }

        private async Task<string> FindPythonInRegistry()
        {
            try
            {
                string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\\Windows";
                var psi = new ProcessStartInfo("reg.exe", $"query \"{key}\" /v Software")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit();
                if (p.ExitCode != 0) return null;

                string output = p.StandardOutput.ReadToEnd();
                // Look for Python paths in registry
                int pos = output.IndexOf("C:\\");
                int end = output.IndexOf('\\', pos + 1);
                while (pos >= 0)
                {
                    if (end == -1) return null;

                    string candidate = output.Substring(pos, end - pos);
                    if (candidate.Contains("python") || candidate.Contains(".exe"))
                    {
                        try { File.Exists(candidate); }  catch { } }
                    return candidate;
                }
                // Find next 'C:\\'
                pos = output.IndexOf('\\', end + 1);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Registry Python search failed");
                return null;
            }
            return null;
        }

        private async Task<string> FindPythonInKnownPaths()
        {
            // Check common python installation paths
            string[] candidatePaths = {
                @"C:\Program Files\Python313",
                @"C:\Python313",
                @"C:\Users\" + Environment.UserName + @"\\AppData\\Local\\Programs\\Python\\Python313",
                @"C:\Python"
            };

            foreach (var path in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                string searchPath = Path.Combine(path, "python.exe");
                if (File.Exists(searchPath))
                    return searchPath;
            }
            return null;
        }

        public event EventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] System.String propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}