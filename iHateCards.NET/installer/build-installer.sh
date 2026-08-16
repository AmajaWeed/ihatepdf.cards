#!/usr/bin/env bash
# Сборка Windows-установщика iHateCards (MSI).
#
# Работает на macOS/Linux (msitools) и на Windows (WiX v3: candle/light).
#   macOS:  brew install msitools
#   Linux:  apt install msitools
#
# Использование:  ./installer/build-installer.sh [версия]
set -euo pipefail

VERSION="${1:-2.0.0}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBDIR="$ROOT/publish/win-x64"
OUT="$ROOT/publish/iHateCards-$VERSION-x64.msi"

echo "==> Публикация приложения (win-x64, self-contained)"
dotnet publish "$ROOT/src/iHateCards/iHateCards.csproj" \
    -c Release -r win-x64 --self-contained -o "$PUBDIR" -v quiet

# Отладочные символы нативных библиотек в дистрибутиве не нужны (~100 МБ)
rm -f "$PUBDIR"/*.pdb

echo "==> Сбор списка файлов"
find "$PUBDIR" -type f | LC_ALL=C sort |
    wixl-heat --var var.SourceDir \
              --directory-ref INSTALLDIR \
              --component-group AppFiles \
              --prefix "$PUBDIR/" > "$ROOT/installer/files.wxs"

# wixl-heat заворачивает файлы в служебную директорию Name="." — убираем,
# чтобы всё легло непосредственно в INSTALLDIR.
python3 - "$ROOT/installer/files.wxs" <<'PY'
import re, sys
p = sys.argv[1]
s = open(p, encoding='utf-8').read()
s = re.sub(r'\n\s*<Directory Id="dir[0-9A-F]+" Name="\.">', '', s)
s = re.sub(r'\n\s*</Directory>\n(\s*</DirectoryRef>)', r'\n\1', s)
open(p, 'w', encoding='utf-8').write(s)
PY

echo "==> Сборка MSI"
wixl -v --arch x64 -D SourceDir="$PUBDIR" -o "$OUT" \
     "$ROOT/installer/iHateCards.wixl.wxs" "$ROOT/installer/files.wxs"

echo
echo "Готово: $OUT"
ls -lh "$OUT"
