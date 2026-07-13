using EasyWindowsTerminalControl;
using PlatformLauncher.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace PlatformLauncher.Presentation.Services
{
    public class TerminalOutputAdapter : ITerminalOutput, IDisposable
    {
        private EasyTerminalControl? _terminal;
        private readonly List<string> _buffer = new List<string>();
        private readonly DispatcherTimer _flushTimer;
        private readonly object _bufferLock = new object();
        private bool _disposed;

        public TerminalOutputAdapter()
        {
            _flushTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _flushTimer.Tick += (s, e) => FlushBuffer();
            _flushTimer.Start();
        }

        public void AttachTerminal(EasyTerminalControl terminal)
        {
            _terminal = terminal;
        }

        public void WriteLine(string line)
        {
            if (string.IsNullOrEmpty(line) || _disposed)
                return;

            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    AddToBuffer(line);
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => AddToBuffer(line)));
                }
            }
            else
            {
                Console.WriteLine(line);
            }
        }

        private void AddToBuffer(string line)
        {
            lock (_bufferLock)
            {
                _buffer.Add(line);
                if (_buffer.Count >= 50)
                    FlushBuffer();
            }
        }

        private void FlushBuffer()
        {
            List<string> lines;
            lock (_bufferLock)
            {
                if (_buffer.Count == 0) return;
                lines = new List<string>(_buffer);
                _buffer.Clear();
            }

            if (_terminal?.ConPTYTerm != null)
            {
                string text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
                _terminal.ConPTYTerm.WriteToUITerminal(text);
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
            lock (_bufferLock)
            {
                _buffer.Clear();
            }
            if (_terminal?.ConPTYTerm != null)
                _terminal.ConPTYTerm.ClearUITerminal(true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer?.Stop();
            FlushBuffer();
        }
    }
}