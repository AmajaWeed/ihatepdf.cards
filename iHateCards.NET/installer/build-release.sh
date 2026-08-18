#!/usr/bin/env bash
# Сборка релиза iHateCards: пакеты обновления для всех платформ, контрольные
# суммы и манифест updates.json, который читает встроенный автообновлятор.
#
# Использование:
#   ./installer/build-release.sh 2.1.0            — только собрать
#   ./installer/build-release.sh 2.1.0 --publish  — ещё и выложить релиз
#
# Публикация идёт в ОТДЕЛЬНЫЙ ПУБЛИЧНЫЙ репозиторий (исходники остаются
# закрытыми): обновления должны скачиваться без токена.
set -euo pipefail

VERSION="${1:?Укажите версию, например 2.1.0}"
PUBLISH="${2:-}"
UPDATES_REPO="${IHATECARDS_UPDATES_REPO:-AmajaWeed/ihatecards-updates}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/publish/release-$VERSION"
NOTES_FILE="$ROOT/installer/release-notes.txt"
rm -rf "$OUT"; mkdir -p "$OUT"

# --- пакеты ---------------------------------------------------------------

pack() {                       # pack <rid>
    local rid="$1"
    local dir="$ROOT/publish/$rid"
    echo "==> Публикация $rid"
    dotnet publish "$ROOT/src/iHateCards/iHateCards.csproj" \
        -c Release -r "$rid" --self-contained -o "$dir" -p:Version="$VERSION" -v quiet
    rm -f "$dir"/*.pdb

    local zip="$OUT/iHateCards-$VERSION-$rid.zip"
    if [[ "$rid" == osx-* ]]; then
        # macOS обновляется целым бандлом .app
        "$ROOT/installer/build-macos.sh" "$VERSION" "${rid#osx-}" >/dev/null
        (cd "$ROOT/publish" && zip -qry "$zip" "iHateCards.app")
    else
        (cd "$dir" && zip -qry "$zip" .)
    fi
    echo "    $(basename "$zip") — $(du -h "$zip" | cut -f1)"
}

pack win-x64
if [[ "$(uname)" == "Darwin" ]]; then
    pack osx-arm64
    pack osx-x64
else
    echo "==> macOS-пакеты пропущены (нужна сборка на macOS)"
fi

# --- установщик Windows ---------------------------------------------------

if command -v wixl >/dev/null 2>&1; then
    echo "==> Установщик MSI"
    "$ROOT/installer/build-installer.sh" "$VERSION" >/dev/null
    cp "$ROOT/publish/iHateCards-$VERSION-x64.msi" "$OUT/" 2>/dev/null || true
fi

# --- манифест обновлений --------------------------------------------------

echo "==> Манифест updates.json"
python3 - "$OUT" "$VERSION" "$NOTES_FILE" <<'PY'
import hashlib, json, os, sys, datetime
out, version, notes_file = sys.argv[1], sys.argv[2], sys.argv[3]

notes = []
if os.path.exists(notes_file):
    notes = [l.strip() for l in open(notes_file, encoding='utf-8') if l.strip()]

packages = {}
for name in sorted(os.listdir(out)):
    if not name.endswith('.zip'):
        continue
    rid = name.replace(f'iHateCards-{version}-', '').replace('.zip', '')
    path = os.path.join(out, name)
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    packages[rid] = {
        'url': f'https://github.com/REPO/releases/download/v{version}/{name}',
        'sha256': h.hexdigest().upper(),
        'size': os.path.getsize(path),
    }

manifest = {
    'latest': version,
    'published': datetime.date.today().strftime('%d.%m.%Y'),
    'notes': notes,
    'packages': packages,
}
with open(os.path.join(out, 'updates.json'), 'w', encoding='utf-8') as f:
    json.dump(manifest, f, ensure_ascii=False, indent=2)
print(json.dumps(manifest, ensure_ascii=False, indent=2))
PY

# Подставляем адрес репозитория обновлений в ссылки
python3 - "$OUT/updates.json" "$UPDATES_REPO" <<'PY'
import sys
p, repo = sys.argv[1], sys.argv[2]
s = open(p, encoding='utf-8').read().replace('github.com/REPO/', f'github.com/{repo}/')
open(p, 'w', encoding='utf-8').write(s)
PY

echo
echo "Готово: $OUT"
ls -lh "$OUT"

# --- публикация (по флагу) ------------------------------------------------

if [ "$PUBLISH" = "--publish" ]; then
    echo
    echo "==> Публикация релиза v$VERSION в $UPDATES_REPO"
    gh release create "v$VERSION" "$OUT"/* \
        --repo "$UPDATES_REPO" \
        --title "iHateCards $VERSION" \
        --notes "$(cat "$NOTES_FILE" 2>/dev/null || echo "Версия $VERSION")"
    echo "Манифест: https://github.com/$UPDATES_REPO/releases/latest/download/updates.json"
else
    echo
    echo "Публикация: ./installer/build-release.sh $VERSION --publish"
    echo "(репозиторий обновлений: $UPDATES_REPO — он должен быть публичным)"
fi
