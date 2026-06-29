import os
import threading
from datetime import datetime


class OutputHandler:
	def __init__(self, logs_dir="logs/Monitor"):
		self.logs_dir = logs_dir
		os.makedirs(logs_dir, exist_ok=True)
		self.ip_file = os.path.join(logs_dir, "ips.txt")
		self.domains_file = os.path.join(logs_dir, "domains.txt")
		self.log_file = os.path.join(logs_dir, "log.txt")
		self.ip_dir = os.path.join(logs_dir, "ips")
		self.domains_dir = os.path.join(logs_dir, "domains")
		os.makedirs(self.ip_dir, exist_ok=True)
		os.makedirs(self.domains_dir, exist_ok=True)

		self.seen_ips = set()
		self.seen_domains = set()
		self.seen_proc_ips = {}
		self.seen_proc_domains = {}
		self.logged_connections = {}
		self.ip_counter = {}
		self.lock = threading.RLock()

		self.buffered_global_ips = set()
		self.buffered_global_domains = set()
		self.buffered_log_lines = []
		self.buffered_proc_ips = {}
		self.buffered_proc_domains = {}

		self._write_session_markers()

	def _ensure_newline(self, filepath):
		if not os.path.exists(filepath):
			return
		with open(filepath, "rb") as f:
			f.seek(0, 2)
			if f.tell() == 0:
				return
			f.seek(-1, 2)
			if f.read(1) != b"\n":
				with open(filepath, "a", encoding="utf-8") as f2:
					f2.write("\n")

	def _write_session_markers(self):
		marker = f"\n# SES {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n"
		for fpath in (self.ip_file, self.domains_file):
			self._ensure_newline(fpath)
			with open(fpath, "a", encoding="utf-8") as f:
				f.write(marker)
		for dir_path in (self.ip_dir, self.domains_dir):
			for fname in os.listdir(dir_path):
				if fname.endswith(".txt"):
					fpath = os.path.join(dir_path, fname)
					self._ensure_newline(fpath)
					with open(fpath, "a", encoding="utf-8") as f:
						f.write(marker)

	def add_connection(
		self,
		proc_name,
		remote_ip,
		domain,
		timestamp,
		remote_port,
		pid,
		local_ip,
		local_port,
		status,
	):
		conn_key = (pid, remote_ip, remote_port)
		with self.lock:
			if conn_key in self.logged_connections:
				return False
			self.logged_connections[conn_key] = datetime.now().timestamp()
			if remote_ip in self.ip_counter:
				cnt, _ = self.ip_counter[remote_ip]
				self.ip_counter[remote_ip] = (cnt + 1, datetime.now().timestamp())
			else:
				self.ip_counter[remote_ip] = (1, datetime.now().timestamp())

		self._add_global_ip(remote_ip, status)
		if domain not in ("—", "Домен не определён"):
			self._add_global_domain(domain, status)
		self._add_proc_ip(proc_name, remote_ip, status)
		if domain not in ("—", "Домен не определён"):
			self._add_proc_domain(proc_name, domain, status)

		log_line = f"{timestamp} | {proc_name} | {pid} | {local_ip}:{local_port} | {remote_ip}:{remote_port} | {status} | {domain}"
		self._add_log_line(log_line)
		return True

	def _add_global_ip(self, ip, status):
		with self.lock:
			key = (ip, status)
			if key in self.seen_ips:
				return
			self.seen_ips.add(key)
			self.buffered_global_ips.add((ip, status))

	def _add_global_domain(self, domain, status):
		with self.lock:
			key = (domain, status)
			if key in self.seen_domains:
				return
			self.seen_domains.add(key)
			self.buffered_global_domains.add((domain, status))

	def _add_proc_ip(self, proc_name, ip, status):
		with self.lock:
			if proc_name not in self.seen_proc_ips:
				self.seen_proc_ips[proc_name] = set()
			key = (ip, status)
			if key in self.seen_proc_ips[proc_name]:
				return
			self.seen_proc_ips[proc_name].add(key)
			if proc_name not in self.buffered_proc_ips:
				self.buffered_proc_ips[proc_name] = set()
			self.buffered_proc_ips[proc_name].add((ip, status))

	def _add_proc_domain(self, proc_name, domain, status):
		with self.lock:
			if proc_name not in self.seen_proc_domains:
				self.seen_proc_domains[proc_name] = set()
			key = (domain, status)
			if key in self.seen_proc_domains[proc_name]:
				return
			self.seen_proc_domains[proc_name].add(key)
			if proc_name not in self.buffered_proc_domains:
				self.buffered_proc_domains[proc_name] = set()
			self.buffered_proc_domains[proc_name].add((domain, status))

	def _add_log_line(self, line):
		with self.lock:
			self.buffered_log_lines.append(line)

	def flush(self):
		with self.lock:
			if self.buffered_global_ips:
				self._write_batch(self.ip_file, self.buffered_global_ips)
				self.buffered_global_ips.clear()
			if self.buffered_global_domains:
				self._write_batch(self.domains_file, self.buffered_global_domains)
				self.buffered_global_domains.clear()
			for proc, items in self.buffered_proc_ips.items():
				if items:
					filepath = os.path.join(self.ip_dir, f"{proc}.txt")
					self._write_batch(filepath, items)
			self.buffered_proc_ips.clear()
			for proc, items in self.buffered_proc_domains.items():
				if items:
					filepath = os.path.join(self.domains_dir, f"{proc}.txt")
					self._write_batch(filepath, items)
			self.buffered_proc_domains.clear()
			if self.buffered_log_lines:
				with open(self.log_file, "a", encoding="utf-8") as f:
					f.write("\n".join(self.buffered_log_lines) + "\n")
				self.buffered_log_lines.clear()

	def _write_batch(self, filepath, items_set):
		if not items_set:
			return
		try:
			with open(filepath, "a", encoding="utf-8") as f:
				lines = []
				for ip, status in items_set:
					lines.append(f"{status}|{ip}")
				f.write("\n".join(lines) + "\n")
		except Exception as e:
			print(f"Ошибка записи в {filepath}: {e}")

	def rebuild_files_by_status(self):
		if not os.path.exists(self.log_file):
			return

		global_data = {"ip": {}, "domain": {}}
		process_data = {}

		with open(self.log_file, "r", encoding="utf-8") as f:
			for line in f:
				line = line.strip()
				if not line or line.startswith("#"):
					continue
				parts = line.split("|")
				if len(parts) < 7:
					continue
				proc_name = parts[1].strip()
				remote_part = parts[4].strip()
				remote_ip = remote_part.split(":")[0]
				status = parts[5].strip()
				domain = parts[6].strip()

				if status not in global_data["ip"]:
					global_data["ip"][status] = set()
				global_data["ip"][status].add(remote_ip)

				if domain not in ("—", "Домен не определён"):
					if status not in global_data["domain"]:
						global_data["domain"][status] = set()
					global_data["domain"][status].add(domain)

				if proc_name not in process_data:
					process_data[proc_name] = {"ip": {}, "domain": {}}
				if status not in process_data[proc_name]["ip"]:
					process_data[proc_name]["ip"][status] = set()
				process_data[proc_name]["ip"][status].add(remote_ip)

				if domain not in ("—", "Домен не определён"):
					if status not in process_data[proc_name]["domain"]:
						process_data[proc_name]["domain"][status] = set()
					process_data[proc_name]["domain"][status].add(domain)

		def write_groups(filepath, data_by_status):
			if not data_by_status:
				return
			lines = [f"\n# SES {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}", ""]
			for status in sorted(data_by_status.keys()):
				values = sorted(data_by_status[status])
				if not values:
					continue
				lines.append(status)
				lines.extend(values)
				lines.append("")
			if lines and lines[-1] == "":
				lines.pop()
			with open(filepath, "w", encoding="utf-8") as f:
				f.write("\n".join(lines))

		write_groups(self.ip_file, global_data["ip"])
		write_groups(self.domains_file, global_data["domain"])

		for proc_name, proc_dict in process_data.items():
			ip_file = os.path.join(self.ip_dir, f"{proc_name}.txt")
			if proc_dict["ip"]:
				write_groups(ip_file, proc_dict["ip"])
			domain_file = os.path.join(self.domains_dir, f"{proc_name}.txt")
			if proc_dict["domain"]:
				write_groups(domain_file, proc_dict["domain"])
