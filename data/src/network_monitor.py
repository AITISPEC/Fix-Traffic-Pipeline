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

init(autoreset=True)
logger = logging.getLogger(__name__)


class NetworkMonitor:
	def __init__(self, config, lists_path, game_name, monitor_only=False):
		self.config = config
		self.game_name = game_name
		self.monitor_only = monitor_only

		if monitor_only:
			# В режиме мониторинга ListManager создаётся, но в readonly и без инициализации файлов
			self.list_manager = ListManager(config, lists_path, readonly=True)
		else:
			self.list_manager = ListManager(config, lists_path)

		self.dns = DnsResolver(config.get("dns_timeout", 2.0))
		self.stop_requested = False

		self.print_lock = threading.Lock()
		self.ip_counter = OrderedDict()
		self.logged_connections = OrderedDict()
		self.lock = threading.Lock()

		self.highlight_proc_names = [p["name"] for p in config["target_processes"]]
		self.highlight_attr, self.highlight_color = parse_style(
			config.get("highlight_style", "BRIGHT_WHITE")
		)
		self.color_enabled = config.get("color_console", True)
		self.console_cfg = config.get("console", {})

		self.executor = BoundedThreadPool(max_workers=64, max_queue_size=256)

	def _process_connection(self, conn, count):
		remote_ip = conn["raddr_ip"]
		remote_port = conn["raddr_port"]
		proc_name = conn["proc_name"]
		status = conn["status"]

		need_dns = status in self.config.get("dns_resolve_statuses", ["SYN_SENT"])
		domain = self.dns.resolve(remote_ip) if need_dns else "—"

		exclude_domains = self.config.get("exclude_domains", [])
		if domain != "—" and any(
			fnmatch.fnmatch(domain.lower(), pat.lower()) for pat in exclude_domains
		):
			return

		# Запись в списки только если НЕ monitor_only
		if not self.monitor_only:
			if status == "SYN_SENT":
				self.list_manager.add_to_lists_files(remote_ip, domain, proc_name)
			elif status == "ESTABLISHED":
				self.list_manager.add_to_exclude_lists(remote_ip, domain, proc_name)

		formatted = format_connection(
			proc_name,
			remote_ip,
			remote_port,
			domain,
			status,
			count,
			self.highlight_proc_names,
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
			+ f"\n    === {self.game_name} {'МОНИТОРИНГ' if self.monitor_only else 'FIX'} запущен ===\n"
			+ Style.RESET_ALL
		)
		if not self.monitor_only:
			print(f"    Папка с листами: {self.list_manager.lists_path}")
		print(
			Fore.YELLOW
			+ "\n    Запуск перед каждой игрой - необязательно."
			+ Style.RESET_ALL
		)
		print("-" * 63)

		last_clean = time.time()
		try:
			while not self.stop_requested:
				conns = get_connections(
					self.config["target_processes"],
					self.game_name,
					self.config.get("skip_local_ips", True),
				)
				now = time.time()
				for conn in conns:
					key = (conn["pid"], conn["raddr_ip"], conn["raddr_port"])
					with self.lock:
						if key in self.logged_connections:
							continue
						if len(self.logged_connections) > self.config.get(
							"max_logged_connections", 5000
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
			self.dns.shutdown()
			self.executor.shutdown(wait=True)
			logger.info("Мониторинг остановлен.")
			print(Fore.GREEN + "Мониторинг остановлен." + Style.RESET_ALL)

	def _stop(self, signum=None, frame=None):
		print("\n" + "-" * 50)
		self.stop_requested = True
