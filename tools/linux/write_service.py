#!/usr/bin/env python3
"""
Generates a systemd user service unit file for Pikura.Agent.
Usage:
    python write_service.py <agent_exe_path> <output_path>

Example:
    python write_service.py ~/.local/bin/Pikura.Agent \
        ~/.config/systemd/user/pikura-agent.service

After generation:
    systemctl --user daemon-reload
    systemctl --user enable --now pikura-agent
    # Optional: survive logout
    loginctl enable-linger $USER
"""
import sys
import os

agent_exe = sys.argv[1]
output    = sys.argv[2]

# Resolve the dotnet root — prefer DOTNET_ROOT env var, fall back to common paths
dotnet_root = os.environ.get("DOTNET_ROOT", "/usr/share/dotnet")

unit = f"""[Unit]
Description=Pikura Download Agent
Documentation=https://github.com/pikura-app/pikura
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart={agent_exe}
Restart=on-failure
RestartSec=10

# Share environment with the interactive user session
Environment=HOME=%h
Environment=DOTNET_ROOT={dotnet_root}
Environment=DOTNET_NOLOGO=1

# Use the same %APPDATA%-equivalent path as the main app
Environment=XDG_CONFIG_HOME=%h/.config
Environment=XDG_DATA_HOME=%h/.local/share

StandardOutput=journal
StandardError=journal
SyslogIdentifier=pikura-agent

[Install]
WantedBy=default.target
"""

os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
with open(output, "w") as f:
    f.write(unit)

print(f"Written: {output}")
print()
print("To activate:")
print("  systemctl --user daemon-reload")
print("  systemctl --user enable --now pikura-agent")
print()
print("To survive after logout:")
print("  loginctl enable-linger $USER")
