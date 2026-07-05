namespace PlatformLauncher.Core.Interfaces
{
    public interface IPythonValidatorService
    {
        bool IsPythonValid();
        string GetPythonPath();
    }
}