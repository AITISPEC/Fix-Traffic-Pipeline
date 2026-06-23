namespace PlatformLauncher.Core.Interfaces
{
    public interface IProcessKiller
    {
        void KillPythonVenvProcesses();
        void KillWinwsProcess(string expectedPath);
        void KillAllManagedProcesses();
    }
}