#!/usr/bin/env python3
import sys

version = sys.argv[1]
output  = sys.argv[2]

plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Pikura</string>
  <key>CFBundleDisplayName</key><string>Pikura</string>
  <key>CFBundleExecutable</key><string>Pikura</string>
  <key>CFBundleIdentifier</key><string>com.pikura.app</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>Pikura</string>
  <key>CFBundleShortVersionString</key><string>{version}</string>
  <key>CFBundleVersion</key><string>{version}</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppTransportSecurity</key><dict><key>NSAllowsArbitraryLoads</key><true/></dict>
</dict>
</plist>
"""

with open(output, "w") as f:
    f.write(plist)
