#!/usr/bin/env bash
# Сборка и установка на ТВ: scripts/deploy.sh [Debug|Release] [доп. параметры dotnet build]
set -e -o pipefail
export MSYS_NO_PATHCONV=1
CONF=${1:-Release}; shift || true
SDB=~/tizen-tools/sdb/data/tools/sdb.exe
TV=${TV:-192.168.0.16:26101}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
WROOT=$(cygpath -w "$ROOT")
# Выход сборки решает её код, а не grep: полный вывод — во временный файл, на экран — только важные строки.
LOG=$(mktemp)
trap 'rm -f "$LOG"' EXIT
STATUS=0
dotnet build "$WROOT\src\AnimeGo.Tv" -c "$CONF" "$@" >"$LOG" 2>&1 || STATUS=$?
grep -E "error|->|Ошибок" "$LOG" || true
if [ "$STATUS" -ne 0 ]; then echo "Сборка не удалась (код $STATUS)" >&2; exit "$STATUS"; fi
TPK=$(cygpath -w "$(ls "$ROOT/src/AnimeGo.Tv/bin/$CONF/tizen40/"*.tpk)")
$SDB connect $TV >/dev/null
$SDB -s $TV install "$TPK" | tail -1
$SDB -s $TV shell 0 execute ru.animego.tv | tail -1
