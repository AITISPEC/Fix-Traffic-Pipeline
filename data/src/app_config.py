import yaml
import os
import logging

logger = logging.getLogger(__name__)

# Дефолтные значения
_DEFAULT_CONFIG = {
	"app": {"python_stop_timeout_ms": 2000, "default_lists_path": ""},
	"terminal": {
		"theme": "Dark",
		"font_family": "Cascadia Code",
		"font_size": 12,
		"max_proc_width": 24,
		"max_ip_width": 45,
		"max_port_width": 6,
		"max_domain_width": 50,
		"color_console": True,
		"skip_local_ips": True,
		"highlight_style": "BRIGHT_WHITE",
	},
	"monitor": {
		"dns_resolve_statuses": [
			"SYN_SENT",
			# "SYN_RECV",
			# "ESTABLISHED",
			# "FIN_WAIT1",
			# "FIN_WAIT2",
			# "TIME_WAIT",
			# "CLOSE",
			# "CLOSE_WAIT",
			# "LAST_ACK",
			# "LISTEN",
			# "CLOSING",
			# "NONE"
		]
	},
	"logging": {"level": "INFO", "max_file_size": 1048576, "backup_count": 5},
}

_config = None


def load_app_config(config_dir: str = "data") -> dict:
	"""Загружает общий конфиг из data/app_config.yaml."""
	global _config
	if _config is not None:
		return _config

	path = os.path.join(config_dir, "app_config.yaml")
	if not os.path.exists(path):
		logger.warning(
			f"app_config.yaml не найден, использую значения по умолчанию: {path}"
		)
		_config = _DEFAULT_CONFIG.copy()
		return _config

	try:
		with open(path, "r", encoding="utf-8") as f:
			loaded = yaml.safe_load(f)
			if not loaded:
				_config = _DEFAULT_CONFIG.copy()
			else:
				_config = _DEFAULT_CONFIG.copy()
				_merge_dict(_config, loaded)
		logger.info(f"Загружен app_config.yaml")
	except Exception as e:
		logger.error(f"Ошибка загрузки app_config.yaml: {e}, использую дефолты")
		_config = _DEFAULT_CONFIG.copy()
	return _config


def _merge_dict(base, override):
	"""Рекурсивное слияние словарей."""
	for key, value in override.items():
		if key in base and isinstance(base[key], dict) and isinstance(value, dict):
			_merge_dict(base[key], value)
		else:
			base[key] = value


def get_app_config() -> dict:
	if _config is None:
		load_app_config()
	return _config
