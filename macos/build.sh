#!/bin/sh
# Build UsageTray.app (menu bar app). Requires Xcode Command Line Tools: xcode-select --install
set -e
cd "$(dirname "$0")"

APP=UsageTray.app
ARCH=$(uname -m)

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"

swiftc -O -parse-as-library \
  -target "$ARCH-apple-macos12.0" \
  -o "$APP/Contents/MacOS/UsageTray" \
  Sources/UsageTray.swift

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>com.usagetray.app</string>
  <key>CFBundleName</key><string>UsageTray</string>
  <key>CFBundleExecutable</key><string>UsageTray</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
</dict>
</plist>
PLIST

# Ad-hoc signature so macOS allows notifications and a stable Keychain identity
codesign --force --sign - "$APP" >/dev/null 2>&1 || echo "Peringatan: codesign gagal (aplikasi tetap bisa dijalankan)"

echo "Build OK: $(pwd)/$APP"
echo "Jalankan: open $APP   |   Tes tanpa GUI: $APP/Contents/MacOS/UsageTray --test"
