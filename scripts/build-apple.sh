#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$PWD"
VERSION="$(python3 scripts/apple_packages.py version)"
OUT="$ROOT/dist/apple"
mkdir -p "$OUT" "$ROOT/.local/apple"
python3 scripts/apple_packages.py check-version
{
  xcodebuild -version
  xcrun --sdk iphonesimulator --show-sdk-version
  python3 --version
  dotnet --version
  uname -m
} > "$OUT/build-environment.log" 2>&1
command -v xcodegen >/dev/null || { echo 'Install XcodeGen: brew install xcodegen'; exit 1; }
xcodegen generate --spec apple/project.yml
python3 scripts/localize-sources.py generate
EN_SOURCE="$ROOT/.local/localized/en"
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
  dotnet publish "$EN_SOURCE/desktop/CodeLink.Host/CodeLink.Host.csproj" -c Release -r "$RID" --self-contained true -o "$BACKEND" -p:DebugType=None
  dotnet publish "$EN_SOURCE/desktop/CodeLink.Cli/CodeLink.Cli.csproj" -c Release -r "$RID" --self-contained true -o "$BACKEND" -p:DebugType=None
  clang -arch "$ARCH" -mmacosx-version-min=14.0 -Wall -Wextra -Werror -O2 apple/native/codelink-pty.c -o "$BACKEND/codelink-pty"
  cp LICENSE "$APP/Contents/Resources/LICENSE"
  cp licenses/xterm.txt "$APP/Contents/Resources/xterm-LICENSE.txt"
  cp THIRD_PARTY_NOTICES.md "$APP/Contents/Resources/THIRD_PARTY_NOTICES.md"
  cp assets/README.md "$APP/Contents/Resources/ARTWORK-NOTICE.md"
  codesign --force --deep --sign - "$APP"
  codesign --verify --deep --strict "$APP"
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUT/CodeLink-macOS-$LABEL-v$VERSION-en.zip"
done
xcodebuild -project apple/CodeLink.xcodeproj -scheme CodeLinkiOS -configuration Release \
  -derivedDataPath "$ROOT/.local/apple/ios" -destination 'generic/platform=iOS Simulator' \
  ARCHS='arm64 x86_64' ONLY_ACTIVE_ARCH=NO CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- \
  CODE_SIGN_ENTITLEMENTS="$ROOT/apple/iOS/Simulator.entitlements" build > "$OUT/build-ios-simulator.log" 2>&1 || { tail -100 "$OUT/build-ios-simulator.log"; exit 1; }
# Simulator builds need an application identity to access their own Keychain.
# This ad-hoc identity is for Simulator only; device archives remain unsigned.
codesign --verify --strict "$ROOT/.local/apple/ios/Build/Products/Release-iphonesimulator/CodeLink.app"
ditto -c -k --sequesterRsrc --keepParent "$ROOT/.local/apple/ios/Build/Products/Release-iphonesimulator/CodeLink.app" "$OUT/CodeLink-iOS-Simulator-v$VERSION-en.zip"
xcodebuild -project apple/CodeLink.xcodeproj -scheme CodeLinkiOS -configuration Release \
  -destination 'generic/platform=iOS' -archivePath "$ROOT/.local/apple/CodeLink-iOS-unsigned.xcarchive" \
  CODE_SIGNING_ALLOWED=NO archive > "$OUT/build-ios-device.log" 2>&1 || { tail -100 "$OUT/build-ios-device.log"; exit 1; }
ditto -c -k --sequesterRsrc --keepParent "$ROOT/.local/apple/CodeLink-iOS-unsigned.xcarchive" "$OUT/CodeLink-iOS-unsigned-archive-v$VERSION-en.zip"
python3 scripts/package-apple-source.py
if [ "${CODELINK_BUILD_ONLY:-0}" != 1 ]; then
  python3 -u scripts/test-apple-host.py "$ROOT/.local/apple/mac-$(uname -m)/Build/Products/Release/CodeLink.app" 2>&1 | tee "$OUT/integration.log"
fi
python3 scripts/apple_packages.py write-checksums
python3 scripts/verify-apple-packages.py "$OUT"
