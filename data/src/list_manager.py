import os
import threading
import logging
import time
from collections import OrderedDict

logger = logging.getLogger(__name__)


class ListManager:
	def __init__(self, config, lists_path, readonly=False, flush_interval=5.0):
		self.config = config
		self.lists_path = lists_path
		self.lock = threading.RLock()
		self.readonly_mode = readonly or not lists_path

		# кэши
		self.preloaded_ips = set()
		self.preloaded_domains = set()
		self.session_added_ips = set()
		self.session_added_domains = set()
		self.exclude_ips_cache = set()
		self.exclude_domains_cache = set()

		# буферы
		self.ip_buffer = set()
		self.domain_buffer = set()
		self.exclude_ip_buffer = set()
		self.exclude_domain_buffer = set()
		self.session_buffer = set()

		# ИЗМЕНЕНИЕ: параметры таймера
		self.flush_interval = flush_interval
		self._last_flush = time.time()
		self._stop_timer = False
		self._timer_thread = None

		if not self.readonly_mode and self.lists_path:
			self._create_missing_files()
			self._load_initial_data()
			self._start_timer()

	def _create_missing_files(self):
		for fname in self.config["lists"].values():
			filepath = os.path.join(self.lists_path, fname)
			if not os.path.exists(filepath):
				open(filepath, "w", encoding="utf-8").close()

	def _load_initial_data(self):
		ip_all = os.path.join(self.lists_path, self.config["lists"]["ip_file"])
		domain_all = os.path.join(self.lists_path, self.config["lists"]["domain_file"])
		exclude_ip = os.path.join(
			self.lists_path, self.config["lists"]["exclude_ip_file"]
		)
		exclude_domain = os.path.join(
			self.lists_path, self.config["lists"]["exclude_domain_file"]
		)

		with open(ip_all, "r", encoding="utf-8") as f:
			self.preloaded_ips = {line.strip() for line in f if line.strip()}
		self.session_added_ips.update(self.preloaded_ips)

		with open(domain_all, "r", encoding="utf-8") as f:
			self.preloaded_domains = {line.strip() for line in f if line.strip()}
		self.session_added_domains.update(self.preloaded_domains)

		if os.path.exists(exclude_ip):
			with open(exclude_ip, "r", encoding="utf-8") as f:
				self.exclude_ips_cache = {line.strip() for line in f if line.strip()}
		if os.path.exists(exclude_domain):
			with open(exclude_domain, "r", encoding="utf-8") as f:
				self.exclude_domains_cache = {
					line.strip() for line in f if line.strip()
				}

	def reload_lists(self):
		if not self.lists_path or self.readonly_mode:
			return
		with self.lock:
			ip_path = os.path.join(self.lists_path, self.config["lists"]["ip_file"])
			domain_path = os.path.join(
				self.lists_path, self.config["lists"]["domain_file"]
			)
			new_preloaded_ips = set()
			new_preloaded_domains = set()
			if os.path.exists(ip_path):
				with open(ip_path, "r", encoding="utf-8") as f:
					new_preloaded_ips = {line.strip() for line in f if line.strip()}
			if os.path.exists(domain_path):
				with open(domain_path, "r", encoding="utf-8") as f:
					new_preloaded_domains = {line.strip() for line in f if line.strip()}

			excl_ip = os.path.join(
				self.lists_path, self.config["lists"]["exclude_ip_file"]
			)
			excl_dom = os.path.join(
				self.lists_path, self.config["lists"]["exclude_domain_file"]
			)
			new_exclude_ips = set()
			new_exclude_domains = set()
			if os.path.exists(excl_ip):
				with open(excl_ip, "r", encoding="utf-8") as f:
					new_exclude_ips = {line.strip() for line in f if line.strip()}
			if os.path.exists(excl_dom):
				with open(excl_dom, "r", encoding="utf-8") as f:
					new_exclude_domains = {line.strip() for line in f if line.strip()}

			self.preloaded_ips = new_preloaded_ips
			self.preloaded_domains = new_preloaded_domains
			self.exclude_ips_cache = new_exclude_ips
			self.exclude_domains_cache = new_exclude_domains
			self.session_added_ips.update(self.preloaded_ips)
			self.session_added_domains.update(self.preloaded_domains)

	# ИЗМЕНЕНИЕ: запуск фонового таймера
	def _start_timer(self):
		def timer_loop():
			while not self._stop_timer:
				time.sleep(1)
				if time.time() - self._last_flush >= self.flush_interval:
					self._flush_buffers()
					self._last_flush = time.time()

		self._timer_thread = threading.Thread(target=timer_loop, daemon=True)
		self._timer_thread.start()

	def add_to_lists_files(self, ip, domain, proc_name="Unknown"):
		if not self.lists_path or self.readonly_mode:
			return
		with self.lock:
			ip_new = (
				ip and ip not in self.session_added_ips and ip not in self.preloaded_ips
			)
			domain_new = (
				domain
				and domain != "Домен не определён"
				and domain not in self.session_added_domains
			)
			if not ip_new and not domain_new:
				return
			if ip_new:
				self.session_added_ips.add(ip)
				self.ip_buffer.add(ip)
				self.session_buffer.add(ip)
				logger.info(f"[ipset-all] + {ip} (Process: {proc_name})")
			if domain_new:
				self.session_added_domains.add(domain)
				self.domain_buffer.add(domain)
				self.session_buffer.add(domain)
				logger.info(f"[list-general] + {domain} (Process: {proc_name})")
			# ИЗМЕНЕНИЕ: убрана проверка на 50 – сброс по таймеру

	def add_to_exclude_lists(self, ip, domain, proc_name="Unknown"):
		if not self.lists_path or self.readonly_mode:
			return
		with self.lock:
			ip_new = ip and ip not in self.exclude_ips_cache
			domain_new = (
				domain
				and domain != "Домен не определён"
				and domain not in self.exclude_domains_cache
			)
			if not ip_new and not domain_new:
				return
			if ip_new:
				self.exclude_ips_cache.add(ip)
				self.exclude_ip_buffer.add(ip)
				self.session_buffer.add(ip)
				logger.info(f"[ipset-exclude] + {ip} (Process: {proc_name})")
			if domain_new:
				self.exclude_domains_cache.add(domain)
				self.exclude_domain_buffer.add(domain)
				self.session_buffer.add(domain)
				logger.info(f"[list-exclude] + {domain} (Process: {proc_name})")
			# ИЗМЕНЕНИЕ: убрана проверка на 50

	def flush_buffers(self):
		self._flush_buffers()

	def _flush_buffers(self):
		if self.readonly_mode or not self.lists_path:
			return
		with self.lock:

			def ensure_newline(filepath):
				try:
					if os.path.exists(filepath) and os.path.getsize(filepath) > 0:
						with open(filepath, "rb") as f:
							f.seek(-1, 2)
							if f.read(1) != b"\n":
								return "\n"
				except Exception:
					pass
				return ""

			if self.ip_buffer:
				ip_file = os.path.join(self.lists_path, self.config["lists"]["ip_file"])
				try:
					prefix = ensure_newline(ip_file)
					with open(ip_file, "a", encoding="utf-8") as f:
						f.write(prefix + "\n".join(self.ip_buffer) + "\n")
					self.ip_buffer.clear()
				except Exception as e:
					logger.error(f"Ошибка записи IP: {e}")
					self.readonly_mode = True

			if self.domain_buffer:
				domain_file = os.path.join(
					self.lists_path, self.config["lists"]["domain_file"]
				)
				try:
					prefix = ensure_newline(domain_file)
					with open(domain_file, "a", encoding="utf-8") as f:
						f.write(prefix + "\n".join(self.domain_buffer) + "\n")
					self.domain_buffer.clear()
				except Exception as e:
					logger.error(f"Ошибка записи доменов: {e}")
					self.readonly_mode = True

			if self.exclude_ip_buffer:
				excl_ip_file = os.path.join(
					self.lists_path, self.config["lists"]["exclude_ip_file"]
				)
				try:
					prefix = ensure_newline(excl_ip_file)
					with open(excl_ip_file, "a", encoding="utf-8") as f:
						f.write(prefix + "\n".join(self.exclude_ip_buffer) + "\n")
					self.exclude_ip_buffer.clear()
				except Exception as e:
					logger.error(f"Ошибка записи exclude IP: {e}")
					self.readonly_mode = True

			if self.exclude_domain_buffer:
				excl_dom_file = os.path.join(
					self.lists_path, self.config["lists"]["exclude_domain_file"]
				)
				try:
					prefix = ensure_newline(excl_dom_file)
					with open(excl_dom_file, "a", encoding="utf-8") as f:
						f.write(prefix + "\n".join(self.exclude_domain_buffer) + "\n")
					self.exclude_domain_buffer.clear()
				except Exception as e:
					logger.error(f"Ошибка записи exclude доменов: {e}")
					self.readonly_mode = True

			if self.session_buffer:
				session_file = os.path.join(
					self.lists_path, self.config["lists"]["session_ip_file"]
				)
				try:
					prefix = ensure_newline(session_file)
					with open(session_file, "a", encoding="utf-8") as f:
						f.write(prefix + "\n".join(self.session_buffer) + "\n")
					self.session_buffer.clear()
				except Exception as e:
					logger.error(f"Ошибка записи сессионного файла: {e}")

	# ИЗМЕНЕНИЕ: метод для остановки таймера
	def shutdown(self):
		self._stop_timer = True
		if self._timer_thread:
			self._timer_thread.join(timeout=2)
		self._flush_buffers()
