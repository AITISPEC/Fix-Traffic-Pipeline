using EasyWindowsTerminalControl;
using PlatformLauncher.Core.Interfaces;
using System;
using System.Windows;
using System.Windows.Threading;

namespace PlatformLauncher.Presentation.Services
{
    public class TerminalOutputAdapter : ITerminalOutput
    {
        private EasyTerminalControl _terminal;

        public void AttachTerminal(EasyTerminalControl terminal)
        {
            _terminal = terminal;
        }

        public void WriteLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    WriteLineInternal(line);
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => WriteLineInternal(line)));
                }
            }
            else
            {
                Console.WriteLine(line);
            }
        }

        private void WriteLineInternal(string line)
        {
            if (_terminal != null && _terminal.ConPTYTerm != null)
            {
                _terminal.ConPTYTerm.WriteToUITerminal(line + Environment.NewLine);
                // Flush() не существует — убираем
            }
        }

        public void Clear()
        {
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    ClearInternal();
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ClearInternal));
                }
            }
        }

        private void ClearInternal()
        {
            _terminal?.ConPTYTerm?.ClearUITerminal(true);
        }
    }
}