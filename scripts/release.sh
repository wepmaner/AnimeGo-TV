#!/usr/bin/env bash
# Релиз на GitHub для Stash: scripts/release.sh X.Y.Z [--dry-run]
#   1. версия X.Y.Z в tizen-manifest.xml и AppInfo.Version (если другая — коммит «release: vX.Y.Z»);
#   2. тесты Core, Release-сборка;
#   3. проверки: подпись — тот же сертификат автора (иначе обновление поверх не встанет и вход пропадёт),
#      в пакете нет приватных фикстур, версия в пакете = X.Y.Z;
#   4. dist/ru.animego.tv-X.Y.Z.tpk, тег vX.Y.Z, push текущей ветки в main, gh release create с .tpk;
#   5. если задан STASH_CI_TOKEN — пинг Stash, чтобы он сразу перечитал каталог.
# --dry-run — только шаги 2–3 и dist/, без коммита версии, тега, push и релиза.
set -e -o pipefail
export MSYS_NO_PATHCONV=1

VERSION=${1:?"Использование: scripts/release.sh X.Y.Z [--dry-run]"}
DRY=0; [ "${2:-}" = "--dry-run" ] && DRY=1
REPO=wepmaner/AnimeGo-TV
PKG=ru.animego.tv
STASH_APP=animego-tv   # id в каталоге Stash (пресет wepmaner/animego-tv)
# SHA-256 сертификата автора (тестовый author_test.p12 из Tizen.NET.Sdk 1.2.1). Менять только осознанно:
# пакет с другой подписью ТВ поверх старого не поставит.
AUTHOR_SHA256="A2:25:87:C2:BE:C3:D9:2F:08:21:EB:28:2B:8B:89:D5:C9:5B:B7:B3:CE:34:FD:C1:C4:44:3B:D0:A1:0A:74:AF"

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"
MANIFEST=src/AnimeGo.Tv/tizen-manifest.xml
APPINFO=src/AnimeGo.Core/Stash.cs
TAG="v$VERSION"
fail() { echo "✗ $*" >&2; exit 1; }

[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "версия должна быть X.Y.Z, а не «$VERSION»"
[ -z "$(git status --porcelain)" ] || fail "есть незакоммиченные изменения"
if [ $DRY -eq 0 ]; then
  git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && fail "тег $TAG уже есть"
  gh release view "$TAG" -R "$REPO" >/dev/null 2>&1 && fail "релиз $TAG уже есть на GitHub"
  git fetch -q origin main
  git merge-base --is-ancestor origin/main HEAD || fail "origin/main ушёл вперёд — сначала подтяните его"
fi

# 1. Версия
CUR=$(sed -n 's/.*<manifest[^>]* version="\([^"]*\)".*/\1/p' "$MANIFEST")
if [ "$CUR" != "$VERSION" ]; then
  [ $DRY -eq 1 ] && fail "в манифесте $CUR, а не $VERSION (в --dry-run версию не меняю)"
  sed -i "s/\(<manifest[^>]* version=\"\)[^\"]*\"/\1$VERSION\"/" "$MANIFEST"
  sed -i "s/public const string Version = \"[^\"]*\";/public const string Version = \"$VERSION\";/" "$APPINFO"
  echo "• версия $CUR → $VERSION"
fi

# 2. Тесты и сборка
echo "• тесты"
dotnet test tests/AnimeGo.Core.Tests 2>&1 | grep -E "Пройден|Passed!|Не пройден|Failed!|error" || true
dotnet test tests/AnimeGo.Core.Tests >/dev/null 2>&1 || fail "тесты не прошли"
echo "• сборка Release"
rm -rf src/AnimeGo.Tv/bin/Release
dotnet build src/AnimeGo.Tv -c Release >/dev/null 2>&1 || fail "сборка не удалась (dotnet build src/AnimeGo.Tv -c Release)"
TPK="src/AnimeGo.Tv/bin/Release/tizen60/$PKG-$VERSION.tpk"
[ -f "$TPK" ] || fail "нет $TPK"

# 3. Проверки пакета
SHA=$(unzip -p "$TPK" author-signature.xml | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{const m=s.match(/<X509Certificate>([\s\S]*?)<\/X509Certificate>/);if(!m)process.exit(1);console.log(new (require("crypto").X509Certificate)(Buffer.from(m[1].replace(/\s/g,""),"base64")).fingerprint256)})') \
  || fail "в пакете нет подписи автора"
[ "$SHA" = "$AUTHOR_SHA256" ] || fail "подпись автора другая: $SHA (ждали $AUTHOR_SHA256)"
unzip -l "$TPK" | grep -qiE "private/|Fixtures/" && fail "в пакет попали фикстуры"
INNER=$(unzip -p "$TPK" tizen-manifest.xml | sed -n 's/.*<manifest[^>]* version="\([^"]*\)".*/\1/p')
[ "$INNER" = "$VERSION" ] || fail "в пакете версия $INNER"
echo "• подпись и версия в порядке ($(du -k "$TPK" | cut -f1) КБ)"

mkdir -p dist
cp "$TPK" "dist/$PKG-$VERSION.tpk"
echo "• dist/$PKG-$VERSION.tpk"
[ $DRY -eq 1 ] && { echo "✓ --dry-run: тег, push и релиз пропущены"; exit 0; }

# 4. Коммит версии, тег, push, релиз
if [ -n "$(git status --porcelain -- "$MANIFEST" "$APPINFO")" ]; then
  git add "$MANIFEST" "$APPINFO"
  git commit -q -m "release: $TAG"
fi
PREV=$(git describe --tags --abbrev=0 2>/dev/null || true)
NOTES=$(git log --format='- %s' ${PREV:+"$PREV"..}HEAD | grep -v '^- release: ' || true)
git tag -a "$TAG" -m "AnimeGoTV $TAG"
git push -q origin "HEAD:main"
git push -q origin "$TAG"
gh release create "$TAG" "dist/$PKG-$VERSION.tpk" -R "$REPO" --title "AnimeGoTV $TAG" \
  --notes "${NOTES:-Без изменений в коде.}

Установка: Stash → «Samsung TV», или вручную: \`sdb connect <ip-тв>:26101\`, \`sdb install $PKG-$VERSION.tpk\`."
echo "✓ https://github.com/$REPO/releases/tag/$TAG"

# 5. Пинг Stash: перечитать каталог сейчас, а не через ≤30 мин по cron. Токен — CI-токен приложения из админки Stash,
#    задаётся вами в окружении (STASH_CI_TOKEN=… scripts/release.sh X.Y.Z); в репозиторий и логи не попадает.
if [ -n "${STASH_CI_TOKEN:-}" ]; then
  code=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "Authorization: Bearer ${STASH_CI_TOKEN}"     "https://stash.stash-worker.workers.dev/v1/admin/refresh?app=$STASH_APP")
  [ "$code" = "200" ] && echo "✓ Stash перечитал каталог" || echo "⚠ Stash ответил $code — каталог обновится сам в течение 30 мин" >&2
else
  echo "• STASH_CI_TOKEN не задан — Stash увидит релиз сам в течение 30 мин"
fi
