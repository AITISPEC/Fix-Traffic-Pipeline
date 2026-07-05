using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Helpers
{
    public static class ProcessHelper
    {
        public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
            string fileName,
            string arguments,
            ILogger logger = null,
            bool createNoWindow = true,
            int timeoutMs = 30000)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                logger?.Warning($"Process {fileName} timed out after {timeoutMs}ms and was killed.");
                return (-1, "", "Process timed out");
            }

            return (process.ExitCode, await outputTask, await errorTask);
        }
    }
}