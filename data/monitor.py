#!/usr/bin/env python3
import sys
import io

# Принудительно UTF-8
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

import argparse
import signal
import os
import atexit
import threading

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "src"))

from src.config_loader import load_game_config
from src.logger_setup import setup_game_logger
from src.network_monitor import NetworkMonitor
from src.backup_manager import BackupManager
from src.ports_manager import PortsManager
from src.warp_manager import WarpManager


def main():
	parser = argparse.ArgumentParser(description="Game Fix Platform Core")
	parser.add_argument("--game", required=True, help="ID игры")
	parser.add_argument("--lists-path", required=True, help="Путь к папке lists")
	parser.add_argument("--backup-root", default="./backups", help="Корень для бэкапов")
	parser.add_argument("--warp", action="store_true", help="Запускать с WARP")
	parser.add_argument(
		"--no-ports", action="store_true", help="Не применять правила портов"
	)
	parser.add_argument(
		"--monitor-only",
		action="store_true",
		help="Только мониторинг (без бэкапов и записи)",
	)
	args = parser.parse_args()

	config = load_game_config(args.game)
	logger = setup_game_logger(args.game)

	# Если monitor-only, то никаких бэкапов и портов
	if args.monitor_only:
		logger.info("Запуск в режиме мониторинга (без бэкапов и записи)")
		monitor = NetworkMonitor(config, args.lists_path, args.game, monitor_only=True)
		monitor.run()
		return

	# Обычный режим с бэкапом
	backup_mgr = BackupManager(args.backup_root, args.game, max_backups=10)

	# Восстанавливаем незавершённый бэкап
	latest_backup = backup_mgr.get_latest_unrestored_backup()
	if latest_backup:
		logger.info(
			f"Обнаружен незавершённый бэкап: {latest_backup}. Восстанавливаем..."
		)
		backup_mgr.restore_backup(args.lists_path, latest_backup)
		logger.info("Восстановление выполнено")

	# Создаём новый бэкап
	logger.info(f"Создание бэкапа папки {args.lists_path}")
	backup_dir = backup_mgr.create_backup(args.lists_path)
	if not backup_dir:
		logger.error("Не удалось создать бэкап lists. Завершение.")
		sys.exit(1)

	def restore_lists():
		logger.info("Восстановление lists из бэкапа")
		backup_mgr.restore_backup(args.lists_path, backup_dir)

	atexit.register(restore_lists)
	signal.signal(signal.SIGTERM, lambda *_: restore_lists())
	signal.signal(signal.SIGINT, lambda *_: restore_lists())

	def stdin_listener():
		try:
			while True:
				line = sys.stdin.readline()
				if not line:
					break
				if line.strip().lower() == "exit":
					logger.info("Получена команда exit из stdin")
					restore_lists()
					os._exit(0)
		except Exception as e:
			logger.error(f"Ошибка чтения stdin: {e}")

	stdin_thread = threading.Thread(target=stdin_listener, daemon=True)
	stdin_thread.start()

	# Применяем порты, если не запрещено
	if not args.no_ports and "ports" in config:
		ports_cfg = config["ports"]
		pm = PortsManager(rule_prefix=f"GameFix_{args.game}")
		pm.add_rules(ports_cfg.get("tcp", []), ports_cfg.get("udp", []))

	if args.warp:
		if not WarpManager.ensure_started():
			logger.warning("Не удалось запустить WARP, продолжаем без него")

	monitor = NetworkMonitor(config, args.lists_path, args.game)
	try:
		monitor.run()
	finally:
		restore_lists()


if __name__ == "__main__":
	main()
