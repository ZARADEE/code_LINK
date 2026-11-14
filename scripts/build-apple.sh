#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$PWD"
VERSION=1.3.0
OUT="$ROOT/dist/apple"
mkdir -p "$OUT" "$ROOT/.local/apple"
command -v xcodegen >/dev/null || { echo 'Install XcodeGen: brew install xcodegen'; exit 1; }
xcodegen generate --spec apple/project.yml
swiftc apple/Shared/Protocol.swift apple/Shared/Reminders.swift apple/Tests/main.swift -o "$ROOT/.local/apple/logic-tests"
"$ROOT/.local/apple/logic-tests"
swiftc apple/Shared/Protocol.swift apple/Shared/Keychain.swift apple/Shared/LinkConnection.swift apple/Tests/NetworkProbe.swift -o "$ROOT/.local/apple/network-probe"
for ARCH in arm64 x86_64; do
  RID=osx-arm64
  LABEL=AppleSilicon
  if [ "$ARCH" = x86_64 ]; then RID=osx-x64; LABEL=Intel; fi
  BUILD="$ROOT/.local/apple/mac-$ARCH"
  xcodebuild -project apple/CodeLink.xcodeproj -scheme CodeLinkMac -configuration Release \
    -derivedDataPath "$BUILD" -destination 'generic/platform=macOS' ARCHS="$ARCH" ONLY_ACTIVE_ARCH=NO CODE_SIGNING_ALLOWED=NO build > "$OUT/build-mac-$ARCH.log" 2>&1 || { tail -100 "$OUT/build-mac-$ARCH.log"; exit 1; }
  APP="$BUILD/Build/Products/Release/CodeLink.app"
  test -f "$APP/Contents/Info.plist"
  BACKEND="$APP/Contents/Resources/backend"
  dotnet publish desktop/CodeLink.Host/CodeLink.Host.csproj -c Release -r "$RID" --self-contained true -o "$BACKEND" -p:DebugType=None
  dotnet publish desktop/CodeLink.Cli/CodeLink.Cli.csproj -c Release -r "$RID" --self-contained true -o "$BACKEND" -p:DebugType=None
  clang -arch "$ARCH" -mmacosx-version-min=14.0 -Wall -Wextra -Werror -O2 apple/native/codelink-pty.c -o "$BACKEND/codelink-pty"
  cp LICENSE "$APP/Contents/Resources/LICENSE"
  cp licenses/xterm.txt "$APP/Contents/Resources/xterm-LICENSE.txt"
  codesign --force --deep --sign - "$APP"
  codesign --verify --deep --strict "$APP"
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUT/CodeLink-macOS-$LABEL-v$VERSION.zip"
done
xcodebuild -project apple/CodeLink.xcodeproj -scheme CodeLinkiOS -configuration Release \
  -derivedDataPath "$ROOT/.local/apple/ios" -destination 'generic/platform=iOS Simulator' \
  ARCHS='arm64 x86_64' ONLY_ACTIVE_ARCH=NO CODE_SIGNING_ALLOWED=NO build > "$OUT/build-ios-simulator.log" 2>&1 || { tail -100 "$OUT/build-ios-simulator.log"; exit 1; }
ditto -c -k --sequesterRsrc --keepParent "$ROOT/.local/apple/ios/Build/Products/Release-iphonesimulator/CodeLink.app" "$OUT/CodeLink-iOS-Simulator-v$VERSION.zip"
xcodebuild -project apple/CodeLink.xcodeproj -scheme CodeLinkiOS -configuration Release \
  -destination 'generic/platform=iOS' -archivePath "$ROOT/.local/apple/CodeLink-iOS-unsigned.xcarchive" \
  CODE_SIGNING_ALLOWED=NO archive > "$OUT/build-ios-device.log" 2>&1 || { tail -100 "$OUT/build-ios-device.log"; exit 1; }
ditto -c -k --sequesterRsrc --keepParent "$ROOT/.local/apple/CodeLink-iOS-unsigned.xcarchive" "$OUT/CodeLink-iOS-unsigned-archive-v$VERSION.zip"
python3 scripts/package-apple-source.py
python3 scripts/test-apple-host.py "$ROOT/.local/apple/mac-$(uname -m)/Build/Products/Release/CodeLink.app"
python3 - <<'PY'
from pathlib import Path
import hashlib
root=Path('dist/apple')
(root/'APPLE-SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in sorted(root.glob('*.zip'))))
PY
