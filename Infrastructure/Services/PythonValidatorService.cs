using System.IO;
using PlatformLauncher.Core.Interfaces;

namespace PlatformLauncher.Infrastructure.Services
{
    public class PythonValidatorService : IPythonValidatorService
    {
        private readonly IPythonEnvironmentManager _pythonEnvManager;

        public PythonValidatorService(IPythonEnvironmentManager pythonEnvManager)
        {
            _pythonEnvManager = pythonEnvManager;
        }

        public bool IsPythonValid()
        {
            string pythonExe = GetPythonPath();
            return !string.IsNullOrEmpty(pythonExe) && File.Exists(pythonExe);
        }

        public string GetPythonPath()
        {
            return _pythonEnvManager.GetVenvPythonPath();
        }
    }
}