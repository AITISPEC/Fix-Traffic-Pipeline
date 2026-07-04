# Схема конфигурации игры (YAML)

Все конфигурационные файлы игр хранятся в `data/configs/{game_id}.yaml`.  
Поля:

## Обязательные
- `target_processes` (список) – процессы, за которыми следить.
  - `name` (строка) – имя процесса без расширения `.exe`.
  - `check_path` (bool) – если `true`, проверяется, что путь к процессу содержит `game_id`.
- `lists` (объект) – имена файлов списков.
  - `ip_file`, `domain_file`, `exclude_ip_file`, `exclude_domain_file`, `session_ip_file`.

## Опциональные
- `ports` – правила брандмауэра.
  - `tcp` (список чисел или диапазонов, например `[443, "1000-2000"]`)
  - `udp` (аналогично)
- `list_rules` – правила добавления в списки по статусу соединения.
  - Для каждого статуса (`SYN_SENT`, `ESTABLISHED` и т.д.) указывается:
    - `action`: `add_to_main`, `add_to_exclude`, `ignore`
    - `target`: `ip_only`, `domain_only`, `both`, `none`
- `dns_resolve_statuses` (список статусов) – для каких статусов выполнять DNS-разрешение.
- `scan_interval` (число, секунды) – интервал сканирования.
- `logged_connections_max` (число) – максимальное количество запоминаемых соединений.
- `dns_timeout` (число, секунды) – таймаут DNS.
- `console` – настройки вывода.
  - `max_proc_width`, `max_ip_width`, `max_port_width`, `max_domain_width`
- `color_console` (bool) – цветной вывод.
- `skip_local_ips` (bool) – исключать локальные IP.
- `highlight_style` – стиль подсветки (например, `BRIGHT_WHITE`).
- `warp_supported` (bool) – поддерживает ли игра WARP.
- `version` (число) – версия конфига.