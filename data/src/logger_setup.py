import logging
from logging.handlers import RotatingFileHandler
import os


def setup_game_logger(game_id: str, log_dir: str = "logs") -> logging.Logger:
	"""Создаёт логгер с ротацией (10 файлов по 100 КБ) для конкретной игры."""
	os.makedirs(log_dir, exist_ok=True)
	log_file = os.path.join(log_dir, f"{game_id}_monitor.log")
	handler = RotatingFileHandler(
		log_file, maxBytes=100 * 1024, backupCount=10, encoding="utf-8"
	)
	handler.setFormatter(logging.Formatter("%(asctime)s - %(levelname)s - %(message)s"))
	logger = logging.getLogger(game_id)
	logger.setLevel(logging.INFO)
	# удаляем старые хендлеры, чтобы не дублировать
	for h in logger.handlers[:]:
		logger.removeHandler(h)
	logger.addHandler(handler)
	return logger
