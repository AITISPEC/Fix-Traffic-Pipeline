#!/usr/bin/env python3
import sys
import io
import argparse
import os
import signal
import atexit

# Принудительно UTF-8
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

script_dir = os.path.dirname(os.path.abspath(__file__))
root_dir = os.path.dirname(script_dir)
data_path = os.path.join(root_dir, "data")
if data_path not in sys.path:
    sys.path.insert(0, data_path)

from src.config_loader import load_game_config
from src.network_monitor import NetworkMonitor
from src.logger_setup import setup_game_logger


def signal_handler(sig, frame):
    print("Received stop signal, exiting gracefully...")
    sys.exit(0)


signal.signal(signal.SIGINT, signal_handler)
signal.signal(signal.SIGTERM, signal_handler)


def run_monitor(game_id, lists_path, monitor_only=False, filter_processes=True):
    try:
        config = load_game_config(game_id)
    except FileNotFoundError as e:
        print(f"\033[31m❌ Ошибка: {e}\033[0m")
        sys.exit(1)

    logger = setup_game_logger(game_id)

    logger.info("Запуск мониторинга (управление портами и WARP — в GUI)")

    try:
        monitor = NetworkMonitor(
            config,
            lists_path,
            game_id,
            monitor_only=monitor_only,
            filter_by_target=filter_processes,
        )
        monitor.run()
    except Exception as e:
        import traceback

        logger.error(f"Критическая ошибка в мониторинге: {e}")
        logger.error(traceback.format_exc())
        sys.exit(1)
    finally:
        logger.info("Мониторинг завершён")


def main():
    parser = argparse.ArgumentParser(description="Game Fix Platform Core (мониторинг)")

    parser.add_argument("--game", required=True, help="ID игры")
    parser.add_argument(
        "--lists-path",
        help="Путь к папке lists (обязателен, если не указан --monitor-only)",
    )
    parser.add_argument(
        "--monitor-only",
        action="store_true",
        help="Только мониторинг (без записи в списки)",
    )
    parser.add_argument(
        "--no-filter-processes",
        action="store_true",
        help="Отключить фильтрацию по target_processes (показывать все соединения)",
    )

    args = parser.parse_args()

    if args.game:
        if args.monitor_only or args.lists_path:
            filter_processes = not args.no_filter_processes
            run_monitor(args.game, args.lists_path, args.monitor_only, filter_processes)
        else:
            print(
                "❌ --lists-path обязателен, если не используется --monitor-only",
                file=sys.stderr,
            )
            sys.exit(1)
    else:
        parser.print_help()
        sys.exit(1)


if __name__ == "__main__":
    main()
