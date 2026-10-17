#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
echo "::group::Build bundled offline Three.js scene"
pushd src/MySystem.Rendering >/dev/null
npm ci
npm test
npm run build
popd >/dev/null
echo "::endgroup::"

APP="$PWD/artifacts/MySystem.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Scene"
cp macos/Info.plist "$APP/Contents/Info.plist"
cp -R src/MySystem.Rendering/dist/. "$APP/Contents/Resources/Scene/"
cp macos/mac-bridge.js "$APP/Contents/Resources/Scene/mac-bridge.js"
python3 - "$APP/Contents/Resources/Scene/index.html" <<'PY'
import pathlib,sys
p=pathlib.Path(sys.argv[1])
s=p.read_text()
assert '<script type="module" src="scene.js"></script>' in s
s=s.replace('<script type="module" src="scene.js"></script>',
            '<script src="mac-bridge.js"></script>\n  <script type="module" src="scene.js"></script>')
s=s.replace('GENERIC ATX ASSEMBLY', 'ILLUSTRATIVE HARDWARE ATLAS')
p.write_text(s)
PY

ICON="$PWD/artifacts/MySystem.iconset"
rm -rf "$ICON"; mkdir -p "$ICON"
for entry in "16 icon_16x16.png" "32 icon_16x16@2x.png" "32 icon_32x32.png" \
             "64 icon_32x32@2x.png" "128 icon_128x128.png" "256 icon_128x128@2x.png" \
             "256 icon_256x256.png" "512 icon_256x256@2x.png" \
             "512 icon_512x512.png" "1024 icon_512x512@2x.png"; do
  read -r size name <<< "$entry"
  sips -s format png -z "$size" "$size" assets/icons/MySystem.png \
       --out "$ICON/$name" >/dev/null
done
iconutil -c icns "$ICON" -o "$APP/Contents/Resources/MySystem.icns"
rm -rf "$ICON"

echo "::group::Compile native SwiftUI arm64 application"
xcrun swiftc -O -parse-as-library -target arm64-apple-macos13.0 \
  -framework SwiftUI -framework AppKit -framework Combine \
  -framework Metal -framework WebKit -framework IOKit -framework SceneKit \
  -o "$APP/Contents/MacOS/MySystem" macos/MySystemMac.swift macos/MacHardwareScene.swift
echo "::endgroup::"
# Verify real Mach CPU ticks and memory, filesystem metrics and scene graph in CI.
"$APP/Contents/MacOS/MySystem" --self-test
# Ad-hoc signing is NOT Apple Developer ID notarization.
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
plutil -lint "$APP/Contents/Info.plist"
file "$APP/Contents/MacOS/MySystem"
ARCHS="$(lipo -archs "$APP/Contents/MacOS/MySystem")"
test "$ARCHS" = "arm64" || { echo "Expected arm64 binary, got $ARCHS"; exit 1; }
test -s "$APP/Contents/Resources/Scene/scene.js"
test -s "$APP/Contents/Resources/Scene/mac-bridge.js"
test -s "$APP/Contents/Resources/Scene/index.html"

ditto -c -k --keepParent --sequesterRsrc "$APP" artifacts/MySystem-macOS-arm64.zip
STAGING="$PWD/artifacts/mac-dmg-staging"
rm -rf "$STAGING"; mkdir -p "$STAGING"
ditto "$APP" "$STAGING/MySystem.app"
ln -s /Applications "$STAGING/Applications"
hdiutil create -volname "My System" -srcfolder "$STAGING" -ov \
  -format UDZO artifacts/MySystem-macOS-arm64.dmg
rm -rf "$STAGING"
(cd artifacts && shasum -a 256 MySystem-macOS-arm64.zip MySystem-macOS-arm64.dmg > SHA256SUMS-macOS.txt)
echo "Native arm64 .app, ZIP and DMG built; real M1 Pro hardware qualification remains."
