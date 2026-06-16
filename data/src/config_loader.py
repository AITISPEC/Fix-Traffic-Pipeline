import yaml
import os
import logging

logger = logging.getLogger(__name__)


def load_game_config(game_id: str, config_dir: str = "data/configs") -> dict:
	"""Загружает YAML-конфиг для указанной игры из папки data/configs."""
	path = os.path.join(config_dir, f"{game_id}.yaml")
	if not os.path.exists(path):
		raise FileNotFoundError(f"Конфиг для игры {game_id} не найден: {path}")
	with open(path, "r", encoding="utf-8") as f:
		config = yaml.safe_load(f)
	return config


def load_presets(presets_path: str = "data/configs/presets.yaml") -> dict:
	"""Загружает метаданные доступных фиксов."""
	with open(presets_path, "r", encoding="utf-8") as f:
		return yaml.safe_load(f)


def update_presets_from_github(
	url: str, local_path: str = "data/configs/presets.yaml"
) -> bool:
	"""Скачивает свежий presets.yaml с GitHub."""
	import urllib.request

	try:
		urllib.request.urlretrieve(url, local_path)
		logger.info(f"Presets обновлён из {url}")
		return True
	except Exception as e:
		logger.error(f"Ошибка обновления presets: {e}")
		return False
