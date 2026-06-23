import socket
import ipaddress
import psutil


def is_local_ip(ip, skip_local_ips=True):
	if not skip_local_ips:
		return False
	try:
		addr = ipaddress.ip_address(ip)
		return addr.is_private or addr.is_loopback or addr.is_link_local
	except ValueError:
		return False


def get_connections(
	target_processes, game_id, skip_local_ips=True, filter_by_target=True
):
	connections = []
	try:
		net_conns = psutil.net_connections(kind="inet")
	except Exception as e:
		print(f"Ошибка получения соединений: {e}")
		return connections

	for conn in net_conns:
		if conn.family not in (socket.AF_INET, socket.AF_INET6):
			continue
		if not conn.raddr:
			continue
		remote_ip = conn.raddr.ip

		try:
			proc = psutil.Process(conn.pid)
			proc_name = proc.name()
			if proc_name.endswith(".exe"):
				proc_name = proc_name[:-4]
		except (psutil.NoSuchProcess, psutil.AccessDenied):
			proc_name = "Unknown"

		# Всегда вычисляем is_target
		is_target = False
		if target_processes:
			for rule in target_processes:
				if proc.name().lower() == rule["name"].lower():
					if rule.get("check_path", False):
						try:
							exe = proc.exe().lower()
							if game_id.lower() not in exe:
								continue
						except Exception:
							continue
					is_target = True
					break
		else:
			is_target = True  # если нет целевых процессов, считаем все целевыми

		# Если включена фильтрация – пропускаем нецелевые
		if filter_by_target and not is_target:
			continue

		connections.append(
			{
				"pid": conn.pid,
				"proc_name": proc_name,
				"laddr_ip": conn.laddr.ip if conn.laddr else "0.0.0.0",
				"laddr_port": conn.laddr.port if conn.laddr else 0,
				"raddr_ip": remote_ip,
				"raddr_port": conn.raddr.port,
				"status": conn.status,
				"family": conn.family,
				"is_target": is_target,
			}
		)
	return connections
