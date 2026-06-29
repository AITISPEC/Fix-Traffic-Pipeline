import logging
from logging.handlers import RotatingFileHandler
import os


class SingleLineFormatter(logging.Formatter):
	def format(self, record):
		record.levelname = record.levelname.replace("WARNING", "WARN")
		return super().format(record)


def setup_game_logger(game_id: str, log_dir: str = "logs") -> logging.Logger:
	"""Создаёт логгер с ротацией, синхронизированный с C# форматом."""
	os.makedirs(log_dir, exist_ok=True)
	log_file = os.path.join(log_dir, f"{game_id}_monitor.log")
	handler = RotatingFileHandler(
		log_file, maxBytes=1024 * 1024, backupCount=5, encoding="utf-8"
	)
	formatter = SingleLineFormatter(
		"%(asctime)s.%(msecs)03d [%(levelname)s] [T:%(thread)d] %(message)s",
		datefmt="%Y-%m-%d %H:%M:%S",
	)
	handler.setFormatter(formatter)
	logger = logging.getLogger(game_id)
	logger.setLevel(logging.INFO)
	for h in logger.handlers[:]:
		logger.removeHandler(h)
	logger.addHandler(handler)
	return logger
