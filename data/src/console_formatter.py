# Эмуляция colorama через ANSI-коды
class Fore:
	BLACK = "\033[30m"
	RED = "\033[31m"
	GREEN = "\033[32m"
	YELLOW = "\033[33m"
	BLUE = "\033[34m"
	MAGENTA = "\033[35m"
	CYAN = "\033[36m"
	WHITE = "\033[37m"
	LIGHTBLACK_EX = "\033[90m"
	LIGHTRED_EX = "\033[91m"
	LIGHTGREEN_EX = "\033[92m"
	LIGHTYELLOW_EX = "\033[93m"
	LIGHTBLUE_EX = "\033[94m"
	LIGHTMAGENTA_EX = "\033[95m"
	LIGHTCYAN_EX = "\033[96m"
	LIGHTWHITE_EX = "\033[97m"
	RESET = "\033[0m"


class Style:
	BRIGHT = "\033[1m"
	DIM = "\033[2m"
	NORMAL = "\033[22m"
	RESET_ALL = "\033[0m"


# Цветовые схемы для тем
COLOR_SCHEMES = {
	"Dark": {
		"SYN_SENT": Fore.YELLOW,
		"SYN_RECV": Fore.LIGHTYELLOW_EX,
		"ESTABLISHED": Fore.GREEN,
		"TIME_WAIT": Fore.MAGENTA,
		"CLOSE_WAIT": Fore.RED,
		"FIN_WAIT1": Fore.RED,
		"FIN_WAIT2": Fore.RED,
		"LAST_ACK": Fore.RED,
		"CLOSING": Fore.RED,
		"LISTEN": Fore.CYAN,
		"NONE": Fore.LIGHTCYAN_EX,
		"default": Fore.CYAN,
		"domain_resolved": Fore.WHITE,
		"domain_unknown": Fore.LIGHTBLACK_EX,
		"highlight": Fore.LIGHTWHITE_EX,
	},
	"Light": {
		"SYN_SENT": Fore.LIGHTYELLOW_EX,
		"SYN_RECV": Fore.YELLOW,
		"ESTABLISHED": Fore.LIGHTGREEN_EX,
		"TIME_WAIT": Fore.LIGHTMAGENTA_EX,
		"CLOSE_WAIT": Fore.LIGHTRED_EX,
		"FIN_WAIT1": Fore.LIGHTRED_EX,
		"FIN_WAIT2": Fore.LIGHTRED_EX,
		"LAST_ACK": Fore.LIGHTRED_EX,
		"CLOSING": Fore.LIGHTRED_EX,
		"LISTEN": Fore.LIGHTCYAN_EX,
		"NONE": Fore.CYAN,
		"default": Fore.BLUE,
		"domain_resolved": Fore.BLACK,
		"domain_unknown": Fore.LIGHTBLACK_EX,
		"highlight": Fore.LIGHTBLUE_EX,
	},
}

STATUS_ICONS = {
	"SYN_SENT": "🟡 SYN ",
	"SYN_RECV": "🟠 SYN_R",
	"ESTABLISHED": "🟢 EST ",
	"TIME_WAIT": "⏳ TMW ",
	"CLOSE_WAIT": "🔴 CWA ",
	"FIN_WAIT1": "🔴 FW1 ",
	"FIN_WAIT2": "🔴 FW2 ",
	"LAST_ACK": "🔴 LAK ",
	"CLOSING": "🔴 CLS ",
	"LISTEN": "🔵 LIS ",
	"NONE": "🔵 UDP ",
}
DEFAULT_ICON = "🔵 UNK "


def parse_style(style_str):
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
	return attr, color


def format_connection(
	proc_name,
	remote_ip,
	remote_port,
	domain,
	status,
	count,
	highlight,
	highlight_attr,
	highlight_color,
	color_enabled,
	console_cfg,
	theme="Dark",
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
	scheme = COLOR_SCHEMES.get(theme, COLOR_SCHEMES["Dark"])

	if color_enabled:
		status_color = scheme.get(status, scheme["default"])
		colored_main = status_color + main_part + Style.RESET_ALL

		if domain == "Домен не определён" or domain == "—":
			domain_display = scheme["domain_unknown"] + domain_display + Style.RESET_ALL
		else:
			domain_display = (
				scheme["domain_resolved"] + domain_display + Style.RESET_ALL
			)

		if highlight:
			attr = highlight_attr or ""
			color = highlight_color or scheme["highlight"]
			highlighted_proc = attr + color + proc_name + Style.RESET_ALL
			colored_main = colored_main.replace(proc_name, highlighted_proc, 1)

		return f"{icon}{count_badge} {colored_main} ({domain_display})"
	else:
		if highlight:
			main_plain = main_part.replace(proc_name, f"[{proc_name}]", 1)
		else:
			main_plain = main_part
		return f"{icon}{count_badge} {main_plain} ({domain_display})"
