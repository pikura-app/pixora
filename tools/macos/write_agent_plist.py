#!/usr/bin/env python3
"""
Generates a launchd user agent plist for Pixora.Agent.
The plist is installed to ~/Library/LaunchAgents/ — no root required.

Usage:
    python write_agent_plist.py <agent_exe_path> <output_path>

Example:
    python write_agent_plist.py \
        /Applications/Pixora.app/Contents/MacOS/Pixora.Agent \
        ~/Library/LaunchAgents/com.pixora.agent.plist

After generation:
    launchctl load ~/Library/LaunchAgents/com.pixora.agent.plist
    # Or on macOS 10.11+:
    launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.pixora.agent.plist
"""
import sys
import os

agent_exe = sys.argv[1]
output    = os.path.expanduser(sys.argv[2])

log_dir = os.path.expanduser("~/Library/Logs/Pixora")
os.makedirs(log_dir, exist_ok=True)

plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.pixora.agent</string>

  <key>ProgramArguments</key>
  <array>
    <string>{agent_exe}</string>
  </array>

  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>

  <key>StandardOutPath</key>
  <string>{log_dir}/agent.log</string>
  <key>StandardErrorPath</key>
  <string>{log_dir}/agent-error.log</string>

  <key>EnvironmentVariables</key>
  <dict>
    <key>DOTNET_NOLOGO</key><string>1</string>
  </dict>

  <key>ProcessType</key><string>Background</string>
  <key>LowPriorityIO</key><true/>
</dict>
</plist>
"""

os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
with open(output, "w") as f:
    f.write(plist)

print(f"Written: {output}")
print()
print("To activate:")
print(f"  launchctl load {output}")
print()
print("To deactivate:")
print(f"  launchctl unload {output}")
