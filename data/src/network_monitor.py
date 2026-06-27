import time
import signal
import threading
import logging
import os
import sys
from collections import OrderedDict
import fnmatch

from .thread_pool import BoundedThreadPool
from .scanner import get_connections
from .dns_resolver import DnsResolver
from .console_formatter import format_connection, parse_style
from .list_manager import ListManager
from .app_config import get_app_config
from .config_loader import load_game_config  # <-- ДОБАВЛЕН ИМПОРТ

logger = logging.getLogger(__name__)


class NetworkMonitor:
	def __init__(
		self, config, lists_path, game_name, monitor_only=False, filter_by_target=True
	):
		app_cfg = get_app_config()
		list_files = app_cfg.get("lists", {})
		term_cfg = app_cfg.get("terminal", {})

		# Загружаем пресет Monitor (monitor.yaml)
		monitor_config = load_game_config("monitor")

		# Формируем эффективный конфиг
		if game_name == "Monitor":
			# Для мониторинга пресета Monitor используем только его конфиг
			effective_config = monitor_config.copy()
		else:
			# Для фикса или мониторинга игры:
			# берём target_processes, list_rules и технические параметры из игрового,
			# всё остальное – из Monitor
			effective_config = monitor_config.copy()
			effective_config["target_processes"] = config.get("target_processes", [])
			effective_config["list_rules"] = config.get("list_rules", {})
			for key in [
				"include_domains",
				"exclude_domains",
				"include_ips",
				"exclude_ips",
				"pass_ips",
			]:
				if key in config:
					effective_config[key] = config[key]

			# Технические параметры с приоритетом игровых
			for key in [
				"scan_interval",
				"logged_connections_max",
				"dns_timeout",
				"list_flush_interval",
			]:
				if key in config:
					effective_config[key] = config[key]

		self.config = effective_config
		self.game_name = game_name
		self.monitor_only = monitor_only
		self.filter_by_target = filter_by_target

		# Настройки терминала (из app_config)
		self.theme = term_cfg.get("theme", "Dark")
		self.max_proc_width = term_cfg.get("max_proc_width", 24)
		self.max_ip_width = term_cfg.get("max_ip_width", 45)
		self.max_port_width = term_cfg.get("max_port_width", 6)
		self.max_domain_width = term_cfg.get("max_domain_width", 50)
		self.color_enabled = term_cfg.get("color_console", True)
		self.skip_local_ips = term_cfg.get("skip_local_ips", True)
		self.highlight_style = term_cfg.get("highlight_style", "BRIGHT_WHITE")

		# DNS-резолвинг – из эффективного конфига (т.е. из Monitor)
		self.dns_resolve_statuses = self.config.get(
			"dns_resolve_statuses", ["SYN_SENT"]
		)

		# Для Monitor отключаем пропуск локальных IP
		if game_name == "Monitor":
			self.skip_local_ips = False

		self.highlight_attr, self.highlight_color = parse_style(self.highlight_style)

		# Инициализация менеджера списков (списки из app_config)
		flush_interval = self.config.get("list_flush_interval", 5.0)
		if monitor_only:
			self.list_manager = ListManager(
				list_files, lists_path, readonly=True, flush_interval=flush_interval
			)
		else:
			self.list_manager = ListManager(
				list_files, lists_path, flush_interval=flush_interval
			)

		# DNS-резолвер с таймаутом из effective_config
		self.dns = DnsResolver(self.config.get("dns_timeout", 2.0))

		self.stop_flag_path = (
			os.path.join(self.list_manager.lists_path, ".stop_monitor")
			if self.list_manager.lists_path
			else None
		)

		self.stop_requested = False

		self.print_lock = threading.Lock()
		self.ip_counter = OrderedDict()
		self.logged_connections = OrderedDict()
		self.lock = threading.Lock()

		# Подсветка процессов – из effective_config (target_processes)
		self.highlight_proc_names = [
			p["name"] for p in self.config.get("target_processes", [])
		]

		self.console_cfg = {
			"max_proc_width": self.max_proc_width,
			"max_ip_width": self.max_ip_width,
			"max_port_width": self.max_port_width,
			"max_domain_width": self.max_domain_width,
		}

		self.executor = BoundedThreadPool(max_workers=64, max_queue_size=128)

		# Правила для списков – из effective_config
		self.list_rules = self.config.get("list_rules", {})
		if not self.list_rules:
			self.list_rules = {
				"SYN_SENT": {"action": "add_to_main", "target": "both"},
				"ESTABLISHED": {"action": "ignore", "target": "none"},
			}

	def _process_connection(self, conn, count):
		remote_ip = conn["raddr_ip"]
		remote_port = conn["raddr_port"]
		proc_name = conn["proc_name"]
		status = conn["status"]
		is_target = conn.get("is_target", False)

		# 1. pass_ips — полный пропуск
		pass_ips = self.config.get("pass_ips", [])
		if any(fnmatch.fnmatch(remote_ip, pat) for pat in pass_ips):
			return

		# ----- Фильтры вывода в консоль (из monitor.yaml) -----
		output_statuses = self.config.get("console_output_statuses", [])
		ignore_statuses = self.config.get("console_ignore_statuses", [])
		if output_statuses and status not in output_statuses:
			return
		if status in ignore_statuses:
			return

		# DNS-разрешение (если нужно)
		need_dns = status in self.dns_resolve_statuses
		domain = self.dns.resolve(remote_ip) if need_dns else "—"

		# 2. exclude — добавляем в exclude-списки, не выводим
		exclude_ips = self.config.get("exclude_ips", [])
		exclude_domains = self.config.get("exclude_domains", [])
		if remote_ip in exclude_ips or (
			domain != "—"
			and any(fnmatch.fnmatch(domain, pat) for pat in exclude_domains)
		):
			if not self.monitor_only and is_target:
				self.list_manager.add_to_exclude_lists(
					remote_ip, domain if domain != "—" else None, proc_name
				)
		# 	return  # не выводим

		# 3. include — добавляем в основные списки, не выводим
		include_ips = self.config.get("include_ips", [])
		include_domains = self.config.get("include_domains", [])
		if remote_ip in include_ips or (
			domain != "—"
			and any(fnmatch.fnmatch(domain, pat) for pat in include_domains)
		):
			if not self.monitor_only and is_target:
				self.list_manager.add_to_lists_files(
					remote_ip, domain if domain != "—" else None, proc_name
				)
		# 	return  # не выводим

		# 4. Если не в include и не в exclude — действуем по list_rules
		rule = self.list_rules.get(status, {})
		action = rule.get("action", "ignore")
		target = rule.get("target", "none")

		if not self.monitor_only and is_target:
			if action == "add_to_main":
				if target in ("ip_only", "both"):
					self.list_manager.add_to_lists_files(remote_ip, None, proc_name)
				if target in ("domain_only", "both") and domain != "—":
					self.list_manager.add_to_lists_files(None, domain, proc_name)
			elif action == "add_to_exclude":
				if target in ("ip_only", "both"):
					self.list_manager.add_to_exclude_lists(remote_ip, None, proc_name)
				if target in ("domain_only", "both") and domain != "—":
					self.list_manager.add_to_exclude_lists(None, domain, proc_name)

		# Подсветка и вывод
		should_highlight = is_target or proc_name.lower() in [
			p.lower() for p in self.highlight_proc_names
		]
		formatted = format_connection(
			proc_name,
			remote_ip,
			remote_port,
			domain,
			status,
			count,
			should_highlight,
			self.highlight_attr,
			self.highlight_color,
			self.color_enabled,
			self.console_cfg,
			self.theme,
		)
		with self.print_lock:
			print(formatted)
			sys.stdout.flush()

	def run(self):
		signal.signal(signal.SIGINT, self._stop)
		signal.signal(signal.SIGTERM, self._stop)

		print(
			f"\033[36m\n=== {self.game_name} {'MONITOR' if self.monitor_only else 'FIX'} запущен ===\n\033[0m"
		)
		print("-" * 75)
		sys.stdout.flush()

		last_clean = time.time()
		try:
			while not self.stop_requested:
				if self.stop_flag_path and os.path.exists(self.stop_flag_path):
					logger.info("Обнаружен флаг остановки. Завершаемся.")
					break
				conns = get_connections(
					self.config["target_processes"],
					self.game_name,
					self.skip_local_ips,
					filter_by_target=self.filter_by_target,
				)

				now = time.time()
				for conn in conns:
					key = (conn["pid"], conn["raddr_ip"], conn["raddr_port"])
					with self.lock:
						if key in self.logged_connections:
							continue
						if len(self.logged_connections) > self.config.get(
							"logged_connections_max", 5000
						):
							self.logged_connections.popitem(last=False)
						self.logged_connections[key] = now

						ip = conn["raddr_ip"]
						if ip in self.ip_counter:
							cnt, _ = self.ip_counter[ip]
							self.ip_counter[ip] = (cnt + 1, now)
						else:
							self.ip_counter[ip] = (1, now)
						count = self.ip_counter[ip][0]

					self.executor.submit(self._process_connection, conn, count)

				if now - last_clean > 60:
					timeout = 300
					with self.lock:
						to_del = [
							k
							for k, ts in self.logged_connections.items()
							if now - ts > timeout
						]
						for k in to_del:
							del self.logged_connections[k]
						to_del_ip = [
							ip
							for ip, (_, ts) in self.ip_counter.items()
							if now - ts > timeout
						]
						for ip in to_del_ip:
							del self.ip_counter[ip]
					last_clean = now

				time.sleep(self.config.get("scan_interval", 0.1))

		finally:
			if self.stop_flag_path and os.path.exists(self.stop_flag_path):
				try:
					os.remove(self.stop_flag_path)
				except:
					pass
			if not self.monitor_only:
				self.list_manager.flush_buffers()
				self.list_manager.shutdown()
			self.dns.shutdown()
			self.executor.shutdown(wait=True)
			logger.info("Мониторинг остановлен.")
			sys.stdout.flush()

	def _stop(self, signum=None, frame=None):
		logger.info("Остановка по сигналу.")
		if self.stop_flag_path:
			try:
				with open(self.stop_flag_path, "w") as f:
					f.write("stop")
			except Exception as e:
				logger.warning(f"Не удалось создать флаг остановки: {e}")
		self.stop_requested = True
		print("\n" + "-" * 50)
		sys.stdout.flush()
