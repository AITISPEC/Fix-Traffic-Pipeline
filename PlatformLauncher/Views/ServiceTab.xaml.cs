using PlatformLauncher.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PlatformLauncher.Views
{
    public partial class ServiceTab : UserControl
    {
        public ServiceTab()
        {
            InitializeComponent();
            Loaded += ServiceTab_Loaded;
        }

        private async void ServiceTab_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateWarpStatus();
        }

        private async Task<bool> RunPythonScript(string arguments, Action<string> outputCallback = null)
        {
            string pythonExe = PythonEnvironmentManager.GetVenvPythonPath();
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
            {
                outputCallback?.Invoke("❌ Виртуальное окружение Python не найдено.");
                return false;
            }

            string monitorScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "monitor.py");
            if (!File.Exists(monitorScript))
            {
                outputCallback?.Invoke($"❌ Скрипт монитора не найден: {monitorScript}");
                return false;
            }

            var psi = new ProcessStartInfo(pythonExe, $"\"{monitorScript}\" {arguments}")
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            string output = "";
            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) { output += e.Data; outputCallback?.Invoke(e.Data); } };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) outputCallback?.Invoke($"⚠️ {e.Data}"); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            // Для команды статуса возвращаем содержимое вывода
            if (arguments.Contains("--warp-status"))
            {
                return output.Contains("connected");
            }
            return process.ExitCode == 0;
        }

        private async Task UpdateWarpStatus()
        {
            try
            {
                bool isConnected = await RunPythonScript("--warp-status");
                StartWarpButton.IsEnabled = !isConnected;
                StopWarpButton.IsEnabled = isConnected;
            }
            catch (Exception ex)
            {
                LauncherLogger.Error($"Ошибка проверки статуса WARP: {ex}");
                StartWarpButton.IsEnabled = true;
                StopWarpButton.IsEnabled = false;
            }
        }

        private async void StartWarpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartWarpButton.IsEnabled = false;
                bool ok = await RunPythonScript("--warp-connect", msg => LauncherLogger.Info($"WARP: {msg}"));
                if (ok)
                {
                    MessageBox.Show("WARP запущен", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    await UpdateWarpStatus();
                }
                else
                {
                    MessageBox.Show("Ошибка запуска WARP", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StartWarpButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StartWarpButton.IsEnabled = true;
            }
        }

        private async void StopWarpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopWarpButton.IsEnabled = false;
                bool ok = await RunPythonScript("--warp-disconnect", msg => LauncherLogger.Info($"WARP: {msg}"));
                if (ok)
                {
                    MessageBox.Show("WARP отключён", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    await UpdateWarpStatus();
                }
                else
                {
                    MessageBox.Show("Ошибка отключения WARP", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    StopWarpButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StopWarpButton.IsEnabled = true;
            }
        }
    }
}