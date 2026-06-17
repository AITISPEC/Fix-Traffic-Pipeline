#!/usr/bin/env python3
import sys
import io
import argparse
import os
import subprocess
import signal
import atexit
import threading

# Принудительно UTF-8
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "src"))

from src.config_loader import load_game_config
from src.ports_manager import PortsManager
from src.warp_manager import WarpManager
from src.backup_manager import BackupManager
from src.network_monitor import NetworkMonitor
from src.logger_setup import setup_game_logger


def install_rules(game_id):
	"""Устанавливает правила портов для игры."""
	print(f"⏳ Загрузка конфига для {game_id}...")
	config = load_game_config(game_id)
	ports_cfg = config.get("ports")
	if not ports_cfg:
		print("ℹ️ В конфиге нет портов, ничего не делаю.")
		return

	tcp_ports = ports_cfg.get("tcp", [])
	udp_ports = ports_cfg.get("udp", [])
	print(f"📌 Добавление правил портов: TCP={len(tcp_ports)}, UDP={len(udp_ports)}")

	pm = PortsManager(rule_prefix=f"GameFix_{game_id}")
	pm.add_rules(tcp_ports, udp_ports)
	print("✅ Все правила портов установлены.")


def remove_rules(game_id):
	"""Удаляет правила портов для игры."""
	print(f"⏳ Загрузка конфига для {game_id}...")
	config = load_game_config(game_id)
	ports_cfg = config.get("ports")
	if not ports_cfg:
		print("ℹ️ В конфиге нет портов, ничего не делаю.")
		return

	tcp_ports = ports_cfg.get("tcp", [])
	udp_ports = ports_cfg.get("udp", [])
	print(f"📌 Удаление правил портов: TCP={len(tcp_ports)}, UDP={len(udp_ports)}")

	pm = PortsManager(rule_prefix=f"GameFix_{game_id}")
	pm.remove_rules(tcp_ports, udp_ports)
	print("✅ Все правила портов удалены.")


def warp_status():
	"""Проверяет статус WARP."""
	try:
		result = subprocess.run(
			["warp-cli", "status"], capture_output=True, text=True, timeout=5
		)
		if "Connected" in result.stdout:
			print("connected")
			return True
		else:
			print("disconnected")
			return False
	except Exception:
		print("disconnected")
		return False


def warp_connect():
	"""Запускает WARP."""
	print("⏳ Запуск WARP...")
	if WarpManager.ensure_started():
		print("✅ WARP запущен")
		return True
	else:
		print("❌ Ошибка запуска WARP")
		return False


def warp_disconnect():
	"""Останавливает WARP."""
	print("⏳ Остановка WARP...")
	if WarpManager.disconnect():
		print("✅ WARP остановлен")
		return True
	else:
		print("❌ Ошибка остановки WARP")
		return False


def run_monitor(game_id, lists_path, backup_root, warp_enabled, no_ports, monitor_only):
	"""Запускает мониторинг (полная логика с бэкапами)."""
	config = load_game_config(game_id)
	logger = setup_game_logger(game_id)

	if monitor_only:
		logger.info("Запуск в режиме мониторинга (без бэкапов и записи)")
		monitor = NetworkMonitor(config, lists_path, game_id, monitor_only=True)
		monitor.run()
		return

	# Обычный режим с бэкапом
	backup_mgr = BackupManager(backup_root, game_id, max_backups=10)

	# Восстанавливаем незавершённый бэкап
	latest_backup = backup_mgr.get_latest_unrestored_backup()
	if latest_backup:
		logger.info(
			f"Обнаружен незавершённый бэкап: {latest_backup}. Восстанавливаем..."
		)
		backup_mgr.restore_backup(lists_path, latest_backup)
		logger.info("Восстановление выполнено")

	# Создаём новый бэкап
	logger.info(f"Создание бэкапа папки {lists_path}")
	backup_dir = backup_mgr.create_backup(lists_path)
	if not backup_dir:
		logger.error("Не удалось создать бэкап lists. Завершение.")
		sys.exit(1)

	def restore_lists():
		logger.info("Восстановление lists из бэкапа")
		backup_mgr.restore_backup(lists_path, backup_dir)

	atexit.register(restore_lists)
	signal.signal(signal.SIGTERM, lambda *_: restore_lists())
	signal.signal(signal.SIGINT, lambda *_: restore_lists())

	# Слушаем stdin для команды exit
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
	if not no_ports and "ports" in config:
		ports_cfg = config["ports"]
		pm = PortsManager(rule_prefix=f"GameFix_{game_id}")
		pm.add_rules(ports_cfg.get("tcp", []), ports_cfg.get("udp", []))

	if warp_enabled:
		if not WarpManager.ensure_started():
			logger.warning("Не удалось запустить WARP, продолжаем без него")

	monitor = NetworkMonitor(config, lists_path, game_id, monitor_only=False)
	try:
		monitor.run()
	finally:
		restore_lists()


def main():
	parser = argparse.ArgumentParser(description="Game Fix Platform Core")

	# Команды
	parser.add_argument("--install-rules", help="Установить правила портов для игры")
	parser.add_argument("--remove-rules", help="Удалить правила портов для игры")
	parser.add_argument(
		"--warp-status", action="store_true", help="Проверить статус WARP"
	)
	parser.add_argument("--warp-connect", action="store_true", help="Запустить WARP")
	parser.add_argument(
		"--warp-disconnect", action="store_true", help="Остановить WARP"
	)

	# Аргументы для мониторинга
	parser.add_argument("--game", help="ID игры")
	parser.add_argument("--lists-path", help="Путь к папке lists")
	parser.add_argument("--backup-root", default="./backups", help="Корень для бэкапов")
	parser.add_argument("--warp", action="store_true", help="Запускать с WARP")
	parser.add_argument(
		"--no-ports", action="store_true", help="Не применять правила портов"
	)
	parser.add_argument("--monitor-only", action="store_true", help="Только мониторинг")

	args = parser.parse_args()

	# Обработка отдельных команд
	if args.install_rules:
		install_rules(args.install_rules)
		return

	if args.remove_rules:
		remove_rules(args.remove_rules)
		return

	if args.warp_status:
		warp_status()
		return

	if args.warp_connect:
		warp_connect()
		return

	if args.warp_disconnect:
		warp_disconnect()
		return

	# Если нет отдельных команд – запускаем мониторинг
	if args.game and args.lists_path:
		run_monitor(
			args.game,
			args.lists_path,
			args.backup_root,
			args.warp,
			args.no_ports,
			args.monitor_only,
		)
	else:
		parser.print_help()
		sys.exit(1)


if __name__ == "__main__":
	main()
