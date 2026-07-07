/// <summary>
/// Модель пресета игры — хранит конфигурацию для конкретной целевой игры
/// (названия процессов, интервалы сканирования, список правил zapret).
/// </summary>
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using YamlDotNet.Serialization;

namespace PlatformLauncher.Domain.Models
{
    /// <summary>
    /// Пресет игры: конфигурационная единица в YAML. При установке
    /// записывается в zapret/lists/ — изменения не влияют на другие игры,
    /// потому что каждый пресет имеет отдельный файл.
    /// </summary>
    public class GamePreset : INotifyPropertyChanged
    {
        [YamlMember(Alias = "id")]
        /// <summary>Уникальный идентификатор (например, apex, hunt). Используется как ключ для поиска.</summary>
        public string Id { get; set; } = string.Empty;

        [YamlMember(Alias = "name")]
        /// <summary>Человеческое название: "Apex Legends", "Hunt: Showdown" и т.п.</summary>
        public string Name { get; set; } = string.Empty;

        [YamlMember(Alias = "config_url")]
        /// <summary>URL пресета на сервере синхронизации (опционально, для апдейтов).</summary>
        public string ConfigUrl { get; set; } = string.Empty;

        [YamlMember(Alias = "target_processes")]
        /// <summary>Названия процессов/исполняемых, за которые перехватывать DNS.</summary>
        public List<string> TargetProcesses { get; set; } = new();

        [YamlMember(Alias = "scan_interval")]
        /// <summary>Интервал сканирования трафика в миллисекундах (например, 500).</summary>
        public int ScanInterval { get; set; } = 0;

        [YamlMember(Alias = "logged_connections_max")]
        /// <summary>Ограничение количества записей в логе. Если достигли лимита — перезаписать.</summary>
        public int LoggedConnectionsMax { get; set; } = 0;

        [YamlMember(Alias = "dns_timeout")]
        /// <summary>Таймаут DNS-запроса в миллисекундах (например, 50). Не должно превышать сканирование.</summary>
        public int DnsTimeout { get; set; } = 0;

        [YamlMember(Alias = "list_flush_interval")]
        /// <summary>Интервал обновления файлов списков zapret в миллисекундах (например, 3000).</summary>
        public int ListFlushInterval { get; set; } = 0;

        [YamlMember(Alias = "list_rules")]
        /// <summary>Дополнительные правила zapret (инъекции) для конкретных доменов/IP.</summary>
        public List<string> ListRules { get; set; } = new();

        [YamlMember(Alias = "config_downloaded")]
        /// <summary>Флаг, скачан ли пресет. Устанавливается при установке через UseCase.</summary>
        public bool ConfigDownloaded { get; set; } = false;

        [YamlMember(Alias = "warp_supported")]
        /// <summary>Поддерживает ли игра Cloudflare WARP — флаг для UI-фильтрации.</summary>
        public bool WarpSupported { get; set; }

        [YamlMember(Alias = "version")]
        /// <summary>Версия формата пресета — используется при миграции конфигурации.</summary>
        public int Version { get; set; }

        // Для встроенных пресетов - хранится напрямую в объекте
        [YamlMember(Alias = "installed")]
        /// <summary>Внутреннее поле: флаг установлен ли пресет (для встроенных игр).</summary>
        public bool _installed;

        // Флаг для пользовательских пресетов (статус берётся из presets.yaml)
        /// <summary>Если true, статус устанавливается через внешний YAML — не меняем программно.</summary>
        public bool IsUserPreset { get; set; }

        /// <summary>Свойство Installed реализует разное поведение для встроенных и пользовательских игр. При записи вызывается OnPropertyChanged, чтобы обновился UI.</summary>
        public bool Installed
        {
            get
            {
                // Для пользовательских статус читается из presets.yaml — это read-only
                if (IsUserPreset)
                    return _installed;

                // Встроенные: читаем напрямую из поля
                return _installed;
            }
            set
            {
                // Пользовательские — запрещаем запись, чтобы не ломать синхронизацию
                if (IsUserPreset)
                    return;

                // Встроенные: сохраняем и уведомляем UI через PropertyChanged
                _installed = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Трёхадресное событие для связки ViewModel ↔ GamePreset (MVVM).</summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}