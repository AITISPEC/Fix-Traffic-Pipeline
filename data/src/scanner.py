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


def get_connections(target_processes, game_id, skip_local_ips=True):
	connections = []
	try:
		net_conns = psutil.net_connections(kind="all")
	except Exception:
		return connections

	for conn in net_conns:
		if conn.family not in (socket.AF_INET, socket.AF_INET6):
			continue
		if not conn.raddr:
			continue
		remote_ip = conn.raddr.ip
		if is_local_ip(remote_ip, skip_local_ips):
			continue

		# имя процесса
		try:
			proc = psutil.Process(conn.pid)
			proc_name = proc.name()
			if proc_name.endswith(".exe"):
				proc_name = proc_name[:-4]
		except (psutil.NoSuchProcess, psutil.AccessDenied):
			proc_name = "Unknown"

		# проверка, входит ли процесс в целевые
		target = False
		for rule in target_processes:
			if proc_name.lower() == rule["name"].lower():
				if rule.get("check_path", False):
					try:
						exe = proc.exe().lower()
						# вместо жёсткого списка проверяем, что путь содержит game_id
						if game_id.lower() not in exe:
							continue
					except Exception:
						continue
				target = True
				break
		if not target:
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
			}
		)
	return connections
