#!/usr/bin/env bash
# Сборка macOS-версии iHateCards: .app-бандл и DMG.
#
# Использование: ./installer/build-macos.sh [версия] [arch]
#   arch: arm64 (по умолчанию) | x64
set -euo pipefail

VERSION="${1:-2.1.0}"
ARCH="${2:-arm64}"
RID="osx-$ARCH"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUB="$ROOT/publish/$RID"
APP="$ROOT/publish/iHateCards.app"
DMG="$ROOT/publish/iHateCards-$VERSION-$ARCH.dmg"

echo "==> Публикация ($RID)"
dotnet publish "$ROOT/src/iHateCards/iHateCards.csproj" \
    -c Release -r "$RID" --self-contained -o "$PUB" -v quiet
rm -f "$PUB"/*.pdb

echo "==> Сборка бандла iHateCards.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUB"/* "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/iHateCards"

# Иконка: .icns из имеющегося .ico (без внешних утилит — через sips/iconutil)
ICONSET="$ROOT/publish/iHateCards.iconset"
rm -rf "$ICONSET"; mkdir -p "$ICONSET"
sips -s format png "$ROOT/src/iHateCards/Assets/app.ico" --out "$ICONSET/base.png" >/dev/null 2>&1 || true
if [ -f "$ICONSET/base.png" ]; then
    for sz in 16 32 64 128 256 512; do
        sips -z $sz $sz "$ICONSET/base.png" --out "$ICONSET/icon_${sz}x${sz}.png" >/dev/null 2>&1 || true
        sips -z $((sz*2)) $((sz*2)) "$ICONSET/base.png" --out "$ICONSET/icon_${sz}x${sz}@2x.png" >/dev/null 2>&1 || true
    done
    rm -f "$ICONSET/base.png"
    iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/iHateCards.icns" 2>/dev/null || true
fi
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>iHateCards</string>
    <key>CFBundleDisplayName</key>       <string>iHateCards</string>
    <key>CFBundleIdentifier</key>        <string>ru.ihatepdf.ihatecards</string>
    <key>CFBundleVersion</key>           <string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundlePackageType</key>       <string>APPL</string>
    <key>CFBundleExecutable</key>        <string>iHateCards</string>
    <key>CFBundleIconFile</key>          <string>iHateCards.icns</string>
    <key>LSMinimumSystemVersion</key>    <string>11.0</string>
    <key>NSHighResolutionCapable</key>   <true/>
    <key>CFBundleDocumentTypes</key>
    <array>
      <dict>
        <key>CFBundleTypeName</key>          <string>Проект iHateCards</string>
        <key>CFBundleTypeExtensions</key>    <array><string>hate</string></array>
        <key>CFBundleTypeRole</key>          <string>Editor</string>
        <key>LSHandlerRank</key>             <string>Owner</string>
        <key>CFBundleTypeIconFile</key>      <string>iHateCards.icns</string>
      </dict>
    </array>
</dict>
</plist>
PLIST

echo "==> Подпись (ad-hoc)"
codesign --force --deep --sign - "$APP" 2>/dev/null || echo "   (codesign недоступен — пропущено)"

echo "==> DMG"
rm -f "$DMG"

# Оформленный образ: слева программа, справа папка «Программы», между ними
# стрелка — пользователь перетаскивает приложение и тем самым устанавливает его.
# dmgbuild пишет оформление напрямую (без автоматизации Finder), поэтому
# работает и в сборочных скриптах: pip install --user dmgbuild
if python3 -c "import dmgbuild" >/dev/null 2>&1; then
    python3 -m dmgbuild \
        -s "$ROOT/installer/dmg-settings.py" \
        -D here="$ROOT/installer" \
        -D app="$APP" \
        "iHateCards $VERSION" "$DMG" >/dev/null
else
    echo "   dmgbuild не установлен — собираю простой образ без оформления"
    echo "   (pip install --user dmgbuild — и окно получит фон со стрелкой)"
    STAGE="$ROOT/publish/dmg-stage"
    rm -rf "$STAGE"; mkdir -p "$STAGE"
    cp -R "$APP" "$STAGE/"
    ln -s /Applications "$STAGE/Applications"
    hdiutil create -volname "iHateCards $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
    rm -rf "$STAGE"
fi

echo
echo "Готово:"
echo "  бандл: $APP"
echo "  образ: $DMG"
ls -lh "$DMG"
