from colorama import Fore, Style

STATUS_ICONS = {
	"SYN_SENT": (Fore.YELLOW + "🟡 SYN " + Style.RESET_ALL),
	"ESTABLISHED": (Fore.GREEN + "🟢 EST " + Style.RESET_ALL),
	"LISTEN": (Fore.CYAN + "🔵 LIS " + Style.RESET_ALL),
	"TIME_WAIT": (Fore.MAGENTA + "⏳ TMW " + Style.RESET_ALL),
	"CLOSE_WAIT": (Fore.RED + "🔴 CWA " + Style.RESET_ALL),
	"SYN_RECV": (Fore.YELLOW + "🟠 SYN_R" + Style.RESET_ALL),
	"FIN_WAIT1": (Fore.RED + "🔴 FW1 " + Style.RESET_ALL),
	"FIN_WAIT2": (Fore.RED + "🔴 FW2 " + Style.RESET_ALL),
	"LAST_ACK": (Fore.RED + "🔴 LAK " + Style.RESET_ALL),
	"CLOSING": (Fore.RED + "🔴 CLS " + Style.RESET_ALL),
	"NONE": (Fore.CYAN + "🔵 UDP " + Style.RESET_ALL),
}
DEFAULT_ICON = Fore.CYAN + "🔵 UNK " + Style.RESET_ALL


def parse_style(style_str):
	"""
	Преобразует строку стиля (например, "BRIGHT_WHITE") в кортеж (attr, color).
	Возвращает (атрибут_стиля, цвет) для использования в format_connection.
	"""
	if not style_str:
		return "", ""

	color_map = {
		"BLACK": Fore.BLACK,
		"RED": Fore.RED,
		"GREEN": Fore.GREEN,
		"YELLOW": Fore.YELLOW,
		"BLUE": Fore.BLUE,
		"MAGENTA": Fore.MAGENTA,
		"CYAN": Fore.CYAN,
		"WHITE": Fore.WHITE,
		"BRIGHT_BLACK": Fore.LIGHTBLACK_EX,
		"BRIGHT_RED": Fore.LIGHTRED_EX,
		"BRIGHT_GREEN": Fore.LIGHTGREEN_EX,
		"BRIGHT_YELLOW": Fore.LIGHTYELLOW_EX,
		"BRIGHT_BLUE": Fore.LIGHTBLUE_EX,
		"BRIGHT_MAGENTA": Fore.LIGHTMAGENTA_EX,
		"BRIGHT_CYAN": Fore.LIGHTCYAN_EX,
		"BRIGHT_WHITE": Fore.LIGHTWHITE_EX,
	}
	style_map = {
		"BRIGHT": Style.BRIGHT,
		"DIM": Style.DIM,
		"NORMAL": Style.NORMAL,
		"": "",
	}

	parts = style_str.split("_")
	attr = ""
	color = ""

	for part in parts:
		if part in color_map:
			color = color_map[part]
		elif part in style_map:
			attr = style_map[part]

	if not color and style_str in color_map:
		color = color_map[style_str]
	if not color:
		color = ""  # можно задать цвет по умолчанию, но оставим пустым

	return attr, color


def format_connection(
	proc_name,
	remote_ip,
	remote_port,
	domain,
	status,
	count,
	highlight_proc_names,
	highlight_attr,
	highlight_color,
	color_enabled,
	console_cfg,
):
	max_proc_width = console_cfg.get("max_proc_width", 24)
	max_ip_width = console_cfg.get("max_ip_width", 45)
	max_port_width = console_cfg.get("max_port_width", 6)
	max_domain_width = console_cfg.get("max_domain_width", 50)

	count_badge = f"x{count} " if count > 1 else "   "
	proc_display = proc_name[:max_proc_width].ljust(max_proc_width)
	ip_display = remote_ip[:max_ip_width].ljust(max_ip_width)
	port_display = str(remote_port)[:max_port_width].rjust(max_port_width)
	main_part = f"{proc_display} -> {ip_display} :{port_display}"
	domain_display = domain[:max_domain_width]

	icon = STATUS_ICONS.get(status, DEFAULT_ICON)

	if color_enabled:
		if status == "SYN_SENT":
			status_color = Fore.YELLOW
		elif status == "ESTABLISHED":
			status_color = Fore.GREEN
		elif status == "TIME_WAIT":
			status_color = Fore.MAGENTA
		elif status in ("CLOSE_WAIT", "FIN_WAIT1", "FIN_WAIT2", "LAST_ACK", "CLOSING"):
			status_color = Fore.RED
		else:
			status_color = Fore.CYAN

		colored_main = status_color + main_part + Style.RESET_ALL
		if domain == "Домен не определён":
			domain_display = f"{Fore.LIGHTBLACK_EX}{domain_display}{Style.RESET_ALL}"
		else:
			domain_display = f"{Fore.WHITE}{domain_display}{Style.RESET_ALL}"

		highlight = proc_name.lower() in [p.lower() for p in highlight_proc_names]
		if highlight:
			attr = highlight_attr or ""
			if highlight_color:
				highlighted_proc = attr + highlight_color + proc_name
			else:
				highlighted_proc = attr + proc_name
			colored_main = colored_main.replace(proc_name, highlighted_proc, 1)

		return f"{icon}{count_badge} {colored_main} ({domain_display})"
	else:
		# монохромный режим
		if highlight_proc_names and proc_name.lower() in [
			p.lower() for p in highlight_proc_names
		]:
			main_plain = main_part.replace(proc_name, f"[{proc_name}]", 1)
		else:
			main_plain = main_part
		return f"{icon}{count_badge} {main_plain} ({domain_display})"
