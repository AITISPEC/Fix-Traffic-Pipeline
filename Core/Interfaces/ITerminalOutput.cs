namespace PlatformLauncher.Core.Interfaces
{
    public interface ITerminalOutput
    {
        void WriteLine(string line);
        void Clear();
    }
}