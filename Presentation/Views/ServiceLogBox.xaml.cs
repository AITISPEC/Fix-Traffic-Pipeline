using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlatformLauncher.Presentation.Views
{
    public partial class ServiceLogBox : UserControl
    {
        // ========== Dependency Property для LogId ==========
        public static readonly DependencyProperty LogIdProperty =
            DependencyProperty.Register(
                nameof(LogId),
                typeof(string),
                typeof(ServiceLogBox),
                new PropertyMetadata(null, OnLogIdChanged));

        public string? LogId
        {
            get => (string?)GetValue(LogIdProperty);
            set => SetValue(LogIdProperty, value);
        }

        private static void OnLogIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ServiceLogBox)d;
            string? newId = e.NewValue as string;
            control.InitializeForLogId(newId);
        }

        // ========== Поля, допускающие null ==========
        private string? _logId;
        private string? _cachePath;

        // ========== Конструкторы ==========
        // 1. Конструктор по умолчанию – для использования в XAML
        public ServiceLogBox()
        {
            try
            {
                InitializeComponent();
                Loaded += OnLoaded;
            }
            catch (Exception ex)
            {
                DebugLogger.Write($"Ошибка инициализации LogBox (конструктор по умолчанию): {ex}");
            }
        }

        // 2. Конструктор с параметром – для программного создания (опционально)
        public ServiceLogBox(string logId) : this()
        {
            LogId = logId; // запустит инициализацию через свойство
        }

        // ========== Обработчик события Loaded ==========
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyThemeToContextMenu();
            // Если LogId уже был установлен до загрузки, инициализация уже выполнена.
            // Если нет – ничего не делаем (можем инициализировать позже при установке LogId).
        }

        // ========== Инициализация по LogId ==========
        private void InitializeForLogId(string? logId)
        {
            if (string.IsNullOrEmpty(logId))
            {
                // Если logId пустой – очищаем состояние
                _logId = null;
                _cachePath = null;
                LogTextBox?.Clear();
                return;
            }

            _logId = logId;
            _cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", $"{logId}_log.txt");

            string? directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            LoadCache();
        }

        private void LoadCache()
        {
            if (string.IsNullOrEmpty(_cachePath)) return;
            try
            {
                if (File.Exists(_cachePath))
                {
                    LogTextBox.Text = File.ReadAllText(_cachePath);
                    LogTextBox.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Failed to load cache for {_logId ?? "unknown"}: {ex.Message}");
            }
        }

        // ========== Публичные методы ==========
        public void AppendLine(string message)
        {
            if (string.IsNullOrEmpty(_cachePath))
            {
                // Если не инициализирован – игнорируем или можно инициализировать сейчас
                if (!string.IsNullOrEmpty(LogId))
                    InitializeForLogId(LogId);
                else
                    return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{timestamp}] {message}\n";

            if (Dispatcher.CheckAccess())
            {
                LogTextBox.AppendText(line);
                LogTextBox.ScrollToEnd();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogTextBox.AppendText(line);
                    LogTextBox.ScrollToEnd();
                }));
            }

            try
            {
                if (_cachePath != null)
                    File.AppendAllText(_cachePath, line);
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Cache write failed for {_logId ?? "unknown"}: {ex.Message}");
            }
        }

        public void Clear()
        {
            if (Dispatcher.CheckAccess())
                LogTextBox.Clear();
            else
                Dispatcher.BeginInvoke(new Action(() => LogTextBox.Clear()));

            try
            {
                if (!string.IsNullOrEmpty(_cachePath) && File.Exists(_cachePath))
                    File.Delete(_cachePath);
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Cache clear failed for {_logId ?? "unknown"}: {ex.Message}");
            }
        }

        // ========== Обработчик для пункта меню "Очистить" ==========
        private void ClearMenuItem_Click(object sender, RoutedEventArgs e) => Clear();

        // ========== Применение темы к контекстному меню ==========
        private void ApplyThemeToContextMenu()
        {
            if (LogTextBox.ContextMenu == null) return;
            try
            {
                var bg = FindResource("SecondaryRegionBrush") as Brush;
                var fg = FindResource("PrimaryTextBrush") as Brush;

                if (bg != null) LogTextBox.ContextMenu.Background = bg;
                if (fg != null) LogTextBox.ContextMenu.Foreground = fg;

                foreach (var item in LogTextBox.ContextMenu.Items)
                {
                    if (item is MenuItem menuItem)
                    {
                        if (bg != null) menuItem.Background = bg;
                        if (fg != null) menuItem.Foreground = fg;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Error($"Theme apply failed: {ex.Message}");
            }
        }
    }
}