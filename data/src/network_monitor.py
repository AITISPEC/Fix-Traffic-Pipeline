import time
import signal
import threading
import logging
from collections import OrderedDict
import fnmatch
from colorama import Fore, Style, init

from .thread_pool import BoundedThreadPool
from .scanner import get_connections
from .dns_resolver import DnsResolver
from .console_formatter import format_connection, parse_style
from .list_manager import ListManager
from .app_config import get_app_config

init(autoreset=True)
logger = logging.getLogger(__name__)


class NetworkMonitor:
	def __init__(self, config, lists_path, game_name, monitor_only=False):
		self.config = config
		self.game_name = game_name
		self.monitor_only = monitor_only

		# Загружаем общий конфиг
		app_cfg = get_app_config()
		term_cfg = app_cfg.get("terminal", {})
		mon_cfg = app_cfg.get("monitor", {})

		# Настройки форматирования
		self.max_proc_width = term_cfg.get("max_proc_width", 24)
		self.max_ip_width = term_cfg.get("max_ip_width", 45)
		self.max_port_width = term_cfg.get("max_port_width", 6)
		self.max_domain_width = term_cfg.get("max_domain_width", 50)
		self.color_enabled = term_cfg.get("color_console", True)
		self.skip_local_ips = term_cfg.get("skip_local_ips", True)
		self.highlight_style = term_cfg.get("highlight_style", "BRIGHT_WHITE")
		self.dns_resolve_statuses = mon_cfg.get("dns_resolve_statuses", ["SYN_SENT"])

		# Парсим стиль подсветки
		self.highlight_attr, self.highlight_color = parse_style(self.highlight_style)

		# Инициализация list_manager с flush_interval из игрового конфига
		flush_interval = config.get("list_flush_interval", 5.0)
		if monitor_only:
			self.list_manager = ListManager(
				config, lists_path, readonly=True, flush_interval=flush_interval
			)
		else:
			self.list_manager = ListManager(
				config, lists_path, flush_interval=flush_interval
			)

		# DNS-резолвер
		self.dns = DnsResolver(config.get("dns_timeout", 2.0))

		# Флаг остановки
		self.stop_requested = False

		# Блокировки и счётчики
		self.print_lock = threading.Lock()
		self.ip_counter = OrderedDict()
		self.logged_connections = OrderedDict()
		self.lock = threading.Lock()

		# Имена процессов для подсветки
		self.highlight_proc_names = [p["name"] for p in config["target_processes"]]

		# Настройки консоли для форматтера
		self.console_cfg = {
			"max_proc_width": self.max_proc_width,
			"max_ip_width": self.max_ip_width,
			"max_port_width": self.max_port_width,
			"max_domain_width": self.max_domain_width,
		}

		# Пул потоков
		self.executor = BoundedThreadPool(max_workers=64, max_queue_size=256)

		# Правила для списков из конфига
		self.list_rules = config.get("list_rules", {})
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

		need_dns = status in self.dns_resolve_statuses
		domain = self.dns.resolve(remote_ip) if need_dns else "—"

		exclude_domains = self.config.get("exclude_domains", [])
		if domain != "—" and any(
			fnmatch.fnmatch(domain.lower(), pat.lower()) for pat in exclude_domains
		):
			return

		# Применяем правила из конфига
		rule = self.list_rules.get(status, {})
		action = rule.get("action", "ignore")
		target = rule.get("target", "none")

		if not self.monitor_only:
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

		# Подсветка
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
		)

		with self.print_lock:
			print(formatted)

	def run(self):
		signal.signal(signal.SIGINT, self._stop)
		signal.signal(signal.SIGTERM, self._stop)

		print(
			Fore.CYAN
			+ f"\n=== {self.game_name} {'MONITOR' if self.monitor_only else 'FIX'} запущен ===\n"
			+ Style.RESET_ALL
		)

		print("-" * 75)

		last_clean = time.time()
		try:
			while not self.stop_requested:
				conns = get_connections(
					self.config["target_processes"],
					self.game_name,
					self.skip_local_ips,
					filter_by_target=not self.monitor_only,
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
			if not self.monitor_only:
				self.list_manager.flush_buffers()
				self.list_manager.shutdown()
			self.dns.shutdown()
			self.executor.shutdown(wait=True)
			logger.info("Мониторинг остановлен.")
			print(Fore.GREEN + "Мониторинг остановлен." + Style.RESET_ALL)

	def _stop(self, signum=None, frame=None):
		print("\n" + "-" * 50)
		self.stop_requested = True
