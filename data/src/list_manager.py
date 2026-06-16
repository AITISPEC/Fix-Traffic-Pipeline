import os
import threading
import logging
from collections import OrderedDict

logger = logging.getLogger(__name__)


class ListManager:
	def __init__(self, config, lists_path, readonly=False):
		self.config = config
		self.lists_path = lists_path
		self.lock = threading.RLock()
		self.readonly_mode = readonly or not lists_path  # если путь пустой – readonly

		self.preloaded_ips = set()
		self.preloaded_domains = set()
		self.session_added_ips = set()
		self.session_added_domains = set()
		self.exclude_ips_cache = set()
		self.exclude_domains_cache = set()

		self.ip_buffer = set()
		self.domain_buffer = set()
		self.exclude_ip_buffer = set()
		self.exclude_domain_buffer = set()
		self.session_buffer = set()

		if not self.readonly_mode and self.lists_path:
			self._create_missing_files()
			self._load_initial_data()

	# остальные методы без изменений (они уже проверяют readonly_mode)
