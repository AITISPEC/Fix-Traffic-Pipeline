#!/usr/bin/env python3
import sys
import io
import argparse
import os
import signal
import atexit
import colorama
from colorama import Fore, Style

# Принудительно UTF-8
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "src"))

from src.config_loader import load_game_config
from src.network_monitor import NetworkMonitor
from src.logger_setup import setup_game_logger


def run_monitor(game_id, lists_path, monitor_only=False):
	config = load_game_config(game_id)
	logger = setup_game_logger(game_id)

	logger.info("Запуск мониторинга (управление портами и WARP — в GUI)")

	try:
		monitor = NetworkMonitor(config, lists_path, game_id, monitor_only=monitor_only)
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

	# Аргументы для мониторинга
	parser.add_argument("--game", required=True, help="ID игры")
	parser.add_argument("--lists-path", required=True, help="Путь к папке lists")
	parser.add_argument(
		"--monitor-only",
		action="store_true",
		help="Только мониторинг (без записи в списки)",
	)

	args = parser.parse_args()

	if args.game and args.lists_path:
		run_monitor(args.game, args.lists_path, args.monitor_only)
	else:
		parser.print_help()
		sys.exit(1)


if __name__ == "__main__":
	main()
