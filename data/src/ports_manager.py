import subprocess
import logging
from typing import List, Union

logger = logging.getLogger(__name__)


class PortsManager:
	def __init__(self, rule_prefix: str = "GameFix"):
		self.rule_prefix = rule_prefix

	def _expand_port_range(self, port_spec: Union[int, str]) -> List[Union[int, str]]:
		"""Возвращает список портов или диапазонов в виде строк для netsh."""
		if isinstance(port_spec, int):
			return [port_spec]
		if "-" in str(port_spec):
			# возвращаем как есть (диапазон)
			return [port_spec]
		return [int(port_spec)]

	def _add_rule(self, port_spec: Union[int, str], protocol: str, direction: str):
		rule_name = f"{self.rule_prefix}_{protocol}_{direction}_{port_spec}"
		# для диапазона используем строку, для числа - число
		cmd = [
			"netsh",
			"advfirewall",
			"firewall",
			"add",
			"rule",
			f"name={rule_name}",
			f"dir={direction}",
			"action=allow",
			f"protocol={protocol}",
			f"localport={port_spec}",
		]
		try:
			subprocess.run(cmd, check=True, capture_output=True, text=True)
			logger.debug(f"Добавлено правило: {rule_name}")
		except subprocess.CalledProcessError as e:
			logger.error(f"Ошибка добавления правила {rule_name}: {e.stderr}")

	def add_rules(
		self, tcp_ports: List[Union[int, str]], udp_ports: List[Union[int, str]]
	):
		"""Добавляет TCP и UDP правила для всех портов/диапазонов."""
		for port_spec in tcp_ports:
			specs = self._expand_port_range(port_spec)
			for p in specs:
				self._add_rule(p, "TCP", "in")
				self._add_rule(p, "TCP", "out")
		for port_spec in udp_ports:
			specs = self._expand_port_range(port_spec)
			for p in specs:
				self._add_rule(p, "UDP", "in")
				self._add_rule(p, "UDP", "out")

	def _remove_rule(self, port_spec: Union[int, str], protocol: str, direction: str):
		rule_name = f"{self.rule_prefix}_{protocol}_{direction}_{port_spec}"
		cmd = [
			"netsh",
			"advfirewall",
			"firewall",
			"delete",
			"rule",
			f"name={rule_name}",
		]
		try:
			subprocess.run(cmd, check=True, capture_output=True, text=True)
			logger.debug(f"Удалено правило: {rule_name}")
		except subprocess.CalledProcessError:
			# Правило могло уже не существовать – не критично
			pass

	def remove_rules(
		self, tcp_ports: List[Union[int, str]], udp_ports: List[Union[int, str]]
	):
		"""Удаляет правила для указанных портов/диапазонов."""
		for port_spec in tcp_ports:
			specs = self._expand_port_range(port_spec)
			for p in specs:
				self._remove_rule(p, "TCP", "in")
				self._remove_rule(p, "TCP", "out")
		for port_spec in udp_ports:
			specs = self._expand_port_range(port_spec)
			for p in specs:
				self._remove_rule(p, "UDP", "in")
				self._remove_rule(p, "UDP", "out")
