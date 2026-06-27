# Соответствие XAML-ресурсов и полей YAML-тем

| Поле в YAML | Ключи XAML-ресурсов, которые перекрывает |
|---|---|
| `background` | `RegionBrush`, `BackgroundColor`, `MainBackground`, `LightPrimaryBrush`, `DarkDefaultBrush` |
| `foreground` | `PrimaryTextBrush`, `TextIconBrush`, `MainForeground` |
| `accent` | `PrimaryBrush`, `PrimaryColor`, `AccentBrush` (через `ThemeManager.AccentColor`), `DarkPrimaryBrush` |
| `control_background` | `SecondaryRegionBrush`, `ThirdlyRegionBrush`, `SecondaryRegionColor`, `ThirdlyRegionColor`, `DefaultBrush`, `ControlBackground` |
| `control_foreground` | `SecondaryTextBrush`, `ThirdlyTextBrush`, `ControlForeground` |
| `border_brush` | `BorderColor`, `BorderBrush`, `SecondaryBorderBrush` |
| `scrollbar_background` | `ScrollBarBackground` (применяется отдельно в `ApplyTerminalScrollBarStyle`) |
| `scrollbar_foreground` | `ScrollBarForeground` (применяется отдельно в `ApplyTerminalScrollBarStyle`) |

## Дополнительно

- `terminal_theme` (`Light`/`Dark`) → переключает глобальную тему HandyControl (`ApplicationTheme.Light`/`Dark`) и тему терминала.
- `id` → используется для сопоставления в `SelectedTheme` и сохранения в `app_config.yaml` (`selected_theme`).
- `display_name` → отображается в UI (ListBox тем).

## Источник соответствий

Метод `MainWindow.ApplyCustomColors(ThemeItem theme)` — именно он формирует `_customColorDict` и вставляет его в `Application.Current.Resources.MergedDictionaries` с индексом 0 (приоритет выше HandyControl).

Скроллбар терминала обрабатывается отдельно в `ApplyTerminalScrollBarStyle()` — читает `theme.ScrollBarBackground` и `theme.ScrollBarForeground` напрямую, минуя `_customColorDict`.

Предлагаю добавить 12 параметров для детального контроля UI. Это покроет 90% случаев без overkill.

## Новые параметры YAML

| Параметр | Назначение | Текущее поведение |
|---|---|---|
| `hover_brush` | Цвет при наведении мыши | Используется `border_brush` или `accent` |
| `selected_brush` | Цвет выделенного элемента | Используется `accent` |
| `disabled_brush` | Фон неактивных элементов | Hardcoded `#808080` |
| `disabled_foreground` | Текст неактивных элементов | Hardcoded `#A0A0A0` |
| `input_background` | Фон полей ввода (TextBox) | Используется `control_background` |
| `input_foreground` | Текст полей ввода | Используется `foreground` |
| `input_border_brush` | Рамка полей ввода | Используется `border_brush` |
| `error_brush` | Цвет ошибок | Hardcoded красный |
| `warning_brush` | Цвет предупреждений | Hardcoded жёлтый |
| `success_brush` | Цвет успеха | Hardcoded зелёный |
| `separator_brush` | Цвет разделителей | Используется `border_brush` |
| `overlay_color` | Цвет оверлея (модальные окна) | Hardcoded полупрозрачный чёрный |
