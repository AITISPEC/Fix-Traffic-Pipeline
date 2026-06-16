import os
import shutil
import time
import logging
from pathlib import Path
from typing import Optional

logger = logging.getLogger(__name__)


class BackupManager:
	def __init__(self, backup_root: str, game_id: str, max_backups: int = 10):
		self.backup_root = Path(backup_root) / game_id
		self.backup_root.mkdir(parents=True, exist_ok=True)
		self.max_backups = max_backups

	def _get_backup_dirs(self):
		backups = []
		for item in self.backup_root.iterdir():
			if item.is_dir() and item.name.startswith("lists_"):
				try:
					ts = int(item.name.split("_")[1])
				except (IndexError, ValueError):
					ts = item.stat().st_mtime
				backups.append((ts, item))
		backups.sort(key=lambda x: x[0])
		return [b[1] for b in backups]

	def _prune_old_backups(self):
		backups = self._get_backup_dirs()
		if len(backups) > self.max_backups:
			to_delete = backups[: len(backups) - self.max_backups]
			for d in to_delete:
				shutil.rmtree(d, ignore_errors=True)
				logger.info(f"Удалён старый бэкап: {d}")

	def create_backup(self, source_lists_path: str) -> Optional[Path]:
		source = Path(source_lists_path)
		if not source.exists() or not source.is_dir():
			logger.error(f"Исходная папка lists не существует: {source}")
			return None

		timestamp = int(time.time())
		backup_dir = self.backup_root / f"lists_{timestamp}"
		shutil.copytree(
			source, backup_dir, symlinks=False, ignore_dangling_symlinks=True
		)
		logger.info(f"Создан бэкап: {backup_dir}")
		self._prune_old_backups()
		return backup_dir

	def restore_backup(self, target_lists_path: str, backup_dir: Path) -> bool:
		target = Path(target_lists_path)
		if not backup_dir.exists():
			logger.error(f"Бэкап не найден: {backup_dir}")
			return False

		# Очищаем содержимое папки, но не удаляем саму папку
		if target.exists():
			logger.info(f"Очистка папки {target} перед восстановлением")
			for item in target.iterdir():
				try:
					if item.is_dir():
						shutil.rmtree(item, ignore_errors=True)
					else:
						item.unlink()
				except Exception as e:
					logger.warning(f"Не удалось удалить {item}: {e}")
		else:
			target.mkdir(parents=True, exist_ok=True)

		# Копируем содержимое бэкапа
		for item in backup_dir.iterdir():
			dest = target / item.name
			if item.is_dir():
				shutil.copytree(item, dest)
			else:
				shutil.copy2(item, dest)
		logger.info(f"Восстановлено из бэкапа: {backup_dir} -> {target}")

		# ===== НОВОЕ: ставим маркер, что бэкап уже использован =====
		marker_file = backup_dir / ".restored"
		marker_file.touch()
		logger.info(f"Бэкап помечен как восстановленный: {backup_dir}")

		return True

	def is_backup_restored(self, backup_dir: Path) -> bool:
		return (backup_dir / ".restored").exists() or (
			backup_dir / ".notrestored"
		).exists()

	def get_latest_backup(self) -> Optional[Path]:
		backups = self._get_backup_dirs()
		return backups[-1] if backups else None

	def get_latest_unrestored_backup(self) -> Optional[Path]:
		"""Возвращает самый свежий бэкап, который не помечен как восстановленный или отклонённый."""
		backups = self._get_backup_dirs()
		for backup in reversed(backups):
			if not self.is_backup_restored(backup):
				return backup
		return None
