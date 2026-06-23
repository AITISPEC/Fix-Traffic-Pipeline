import socket
import threading
from concurrent.futures import ThreadPoolExecutor, TimeoutError


class DnsResolver:
	def __init__(self, dns_timeout=2.0, max_workers=64):
		self.dns_timeout = dns_timeout
		self.domain_cache = {}
		self.cache_lock = threading.RLock()
		self.executor = ThreadPoolExecutor(max_workers=max_workers)

	def resolve(self, ip):
		with self.cache_lock:
			if ip in self.domain_cache:
				return self.domain_cache[ip]
		future = self.executor.submit(self._lookup, ip)
		try:
			domain = future.result(timeout=self.dns_timeout)
		except TimeoutError:
			domain = "Домен не определён"
		except Exception:
			domain = "Домен не определён"
		with self.cache_lock:
			self.domain_cache[ip] = domain
		return domain

	def _lookup(self, ip):
		try:
			return socket.gethostbyaddr(ip)[0]
		except (socket.herror, socket.gaierror):
			return "Домен не определён"

	def shutdown(self):
		self.executor.shutdown(wait=True)
