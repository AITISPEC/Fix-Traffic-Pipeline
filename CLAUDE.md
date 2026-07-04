# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## 🏗️ Архитектура проекта

**Fix Traffic Pipeline** — WPF-приложение на .NET 8 с чистой архитектурой:

```
PlatformLauncher/
├── Domain/           — доменные модели (GameConfig, GamePreset, AppConfig)
├── Core/             — интерфейсы, UseCases, исключения
│   ├── Interfaces/   — контракты сервисов
│   └── UseCases/     — бизнес-логика (InstallGameUseCase, StartMonitoringUseCase)
├── Infrastructure/    — реализации сервисов и менеджеров
│   ├── Network/      — ZapretManager, PortsManager, WinwsLocator
│   ├── ProcessManagement/ — WarpManager, PythonProcessManager
│   └── Services/     — UpdateService, SettingsManager, AppConfigService
├── Presentation/      — UI слой (WPF)
│   ├── Views/        — XAML-интерфейсы и код-бэки
│   ├── ViewModels/    — MVVM ViewModel'ы
│   └── Services/      — TerminalOutputAdapter, ThemeApplier
├── Helpers/           — утилитарные расширения
└── DebugLogger.cs     — централизованное логирование
```

---

## 🛠️ Команды разработки

### Сборка
```bash
dotnet build PlatformLauncher/PlatformLauncher.csproj -c Release -r win-x64
```

### Запуск
```bash
# Из папки с исходниками
dotnet run --project PlatformLauncher/PlatformLauncher.csproj -c Release -r win-x64

# Или из собранной папки
./PlatformLauncher/bin/x64/Release/net8.0-windows/PlatformLauncher.exe
```

### Чистка и сброс
```bash
dotnet clean
clear-obj
```

---

## 🔑 Ключевые сервисы

| Сервис | Файл | Описание |
|--------|------|----------|
| **WarpManager** | `Infrastructure/ProcessManagement/WarpManager.cs` | Управление Cloudflare WARP (установка, подключение MASQUE) |
| **ZapretManager** | `Infrastructure/Network/ZapretManager.cs` | Обход DPI через winws — распаковка и применение правил YAML |
| **PythonProcessManager** | `Infrastructure/ProcessManagement/PythonProcessManager.cs` | Запуск изолированного Python 3.13 окружения |
| **SessionOrchestrator** | `Infrastructure/ProcessManagement/SessionOrchestrator.cs` | Оркестрация сессий мониторинга трафика |
| **PortsManager** | `Infrastructure/Network/PortsManager.cs` | Управление портами фаервола |
| **UpdateService** | `Infrastructure/Services/UpdateService.cs` | Проверка и установка обновлений |

---

## 📁 Структура данных

- **Пресеты игр**: `Domain/Models/GamePreset.cs`, `Domain/Models/GameConfig.cs`
- **Конфигурация приложения**: `Infrastructure/Configuration/AppConfigService.cs` → `AppConfig.cs`
- **Списки DNS/Zapret**: `Infrastructure/Lists/ListsSanitizer.cs` — санитизация и синхронизация списков |

---

## 🎨 UI компоненты

- **MainWindow** — главный интерфейс с вкладками (Games, Services, Settings)
- **ServiceTabViewModel** — ViewModel для вкладки сервисов (мониторинг, фаервол, DNS)
- **TerminalOutputAdapter** — адаптер вывода терминала с цветовой индикацией |

---

## 🔧 Полезные паттерны

1. **Dependency Injection** — через `Microsoft.Extensions.DependencyInjection`
2. **Clean Architecture** — Domain → Infrastructure → Presentation
3. **RelayCommand** — для реактивных команд в WPF (`Presentation/Commands/RelayCommand.cs`)
4. **ThemeApplier** — поддержка Light/Dark тем |

---

## 📦 Внешние зависимости

- HandyControls, Hardcodet.NotifyIcon.Wpf
- Microsoft-WindowsAPICodePack (Shell, Core)
- YamlDotNet
- CI.Microsoft.Terminal.Wpf / Console.ConPTY (терминал) |

## 🧪 Тестирование

**Запуск тестов**: `dotnet test PlatformLauncher/PlatformLauncher.Tests.csproj`

**Один тест** — через xUnit console: `dotnet test "tests/TestXxx.XxxTest.cs"`
(тесты лежат в `tests/`, один файл = один `.cs` с одной классовой, начинающейся на *Test).
