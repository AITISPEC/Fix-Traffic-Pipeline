import subprocess
import os
import time
import logging
from pathlib import Path
import psutil  # <-- добавлен импорт

logger = logging.getLogger(__name__)


class WarpManager:
	WARP_URL = (
		"https://github.com/AITISPEC/Helpful/releases/download/apex-fix/Cloudflare.msi"
	)

	@staticmethod
	def is_installed() -> bool:
		try:
			subprocess.run(["warp-cli", "--version"], capture_output=True, check=True)
			return True
		except (subprocess.SubprocessError, FileNotFoundError):
			return False

	@staticmethod
	def install() -> bool:
		"""Скачивает и устанавливает Cloudflare WARP (требует прав администратора)."""
		if WarpManager.is_installed():
			logger.info("WARP уже установлен")
			return True

		msi_path = Path(os.environ.get("TEMP", ".")) / "Cloudflare.msi"
		logger.info(f"Скачивание {WarpManager.WARP_URL} -> {msi_path}")
		try:
			import urllib.request

			urllib.request.urlretrieve(WarpManager.WARP_URL, msi_path)
		except Exception as e:
			logger.error(f"Ошибка скачивания WARP: {e}")
			return False

		try:
			subprocess.run(
				["msiexec", "/i", str(msi_path), "/quiet", "/norestart"], check=True
			)
			time.sleep(5)
			msi_path.unlink(missing_ok=True)
			logger.info("WARP установлен")
			return True
		except subprocess.CalledProcessError as e:
			logger.error(f"Ошибка установки WARP: {e}")
			return False

	@staticmethod
	def set_masque_protocol() -> bool:
		try:
			subprocess.run(
				["warp-cli", "tunnel", "protocol", "set", "MASQUE"],
				check=True,
				capture_output=True,
			)
			logger.info("Протокол WARP установлен на MASQUE")
			return True
		except subprocess.CalledProcessError as e:
			logger.warning(f"Не удалось установить протокол MASQUE: {e}")
			return False

	@staticmethod
	def connect() -> bool:
		try:
			subprocess.run(["warp-cli", "connect"], check=True, capture_output=True)
			logger.info("WARP подключён")
			return True
		except subprocess.CalledProcessError as e:
			logger.error(f"Ошибка подключения WARP: {e}")
			return False

	@staticmethod
	def disconnect() -> bool:
		try:
			subprocess.run(["warp-cli", "disconnect"], check=True, capture_output=True)
			logger.info("WARP отключён")
			return True
		except subprocess.CalledProcessError:
			return False

	@staticmethod
	def ensure_started() -> bool:
		"""Запускает WARP GUI, если нужно, и подключает."""
		if not WarpManager.is_installed():
			if not WarpManager.install():
				return False

		# Запускаем GUI только если он ещё не запущен
		prog_files = os.environ.get("ProgramFiles", "C:\\Program Files")
		warp_exe = (
			Path(prog_files) / "Cloudflare" / "Cloudflare WARP" / "Cloudflare WARP.exe"
		)
		if warp_exe.exists():
			# Проверяем, запущен ли процесс
			proc_running = False
			for proc in psutil.process_iter(["name"]):
				try:
					if proc.info["name"] and "Cloudflare WARP.exe" in proc.info["name"]:
						proc_running = True
						break
				except (psutil.NoSuchProcess, psutil.AccessDenied):
					continue
			if not proc_running:
				subprocess.Popen([str(warp_exe)], shell=False)
				logger.info("Запущен GUI WARP")
			else:
				logger.info("GUI WARP уже запущен")

		WarpManager.set_masque_protocol()
		return WarpManager.connect()
