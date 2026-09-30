#!/usr/bin/env bash
# Замок редактора Unity для нескольких агентов — правила: .agents/rules/unity_sharing.md
#
#   unity-lock.sh acquire <кто> <что> [минут=15]  захватить; 0 — взял, 1 — занято (печатает, кем)
#   unity-lock.sh renew   <кто> [минут=15]        продлить свою аренду
#   unity-lock.sh release <кто>                   отпустить (чужой замок не отпускает)
#   unity-lock.sh status                          кто держит; 0 — свободно, 1 — занято
#
# Замок — каталог tmp/unity-lock (mkdir атомарен: из двух одновременных захватов пройдёт один),
# внутри info: владелец, операция, начало и конец аренды (epoch). Аренда истекла — замок считается
# брошенным и забирается следующим. Лог захватов — tmp/unity-lock.log.

set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOCK="$ROOT/tmp/unity-lock"
INFO="$LOCK/info"
LOG="$ROOT/tmp/unity-lock.log"
mkdir -p "$ROOT/tmp"

now() { date +%s; }
field() { sed -n "s/^$1=//p" "$INFO" 2>/dev/null; }
say() { echo "$(date '+%F %T') $*" >> "$LOG"; }

describe() {
    local owner what since until left
    owner=$(field owner); what=$(field what); since=$(field since); until=$(field until)
    left=$(( (${until:-0} - $(now)) / 60 ))
    echo "занято: $owner — $what (с $(date -d @"${since:-0}" '+%T' 2>/dev/null || echo "$since"), ещё ~${left} мин)"
}

write_info() { # кто что минут
    local t; t=$(now)
    printf 'owner=%s\nwhat=%s\nsince=%s\nuntil=%s\n' "$1" "$2" "$t" $(( t + $3 * 60 )) > "$INFO"
}

expired() { [ -f "$INFO" ] && [ "$(field until)" -lt "$(now)" ]; }

case "${1:-status}" in
  acquire)
    who="${2:?кто}"; what="${3:?что}"; minutes="${4:-15}"
    if mkdir "$LOCK" 2>/dev/null; then
        write_info "$who" "$what" "$minutes"; say "acquire $who: $what ($minutes мин)"; echo "взято: $who — $what"; exit 0
    fi
    if [ "$(field owner)" = "$who" ]; then
        write_info "$who" "$what" "$minutes"; say "reacquire $who: $what"; echo "уже ваше, продлено: $what"; exit 0
    fi
    if expired || [ ! -f "$INFO" ]; then
        say "steal $who у $(field owner): аренда истекла"
        rm -rf "$LOCK"
        if mkdir "$LOCK" 2>/dev/null; then
            write_info "$who" "$what" "$minutes"; say "acquire $who: $what ($minutes мин)"; echo "взято (прежняя аренда истекла): $who — $what"; exit 0
        fi
    fi
    describe; exit 1 ;;
  renew)
    who="${2:?кто}"; minutes="${3:-15}"
    if [ "$(field owner)" != "$who" ]; then echo "не ваш замок"; describe; exit 1; fi
    write_info "$who" "$(field what)" "$minutes"; say "renew $who ($minutes мин)"; echo "продлено на $minutes мин"; exit 0 ;;
  release)
    who="${2:?кто}"
    if [ ! -d "$LOCK" ]; then echo "свободно"; exit 0; fi
    if [ "$(field owner)" != "$who" ]; then echo "не ваш замок — не трогаю"; describe; exit 1; fi
    rm -rf "$LOCK"; say "release $who"; echo "отпущено"; exit 0 ;;
  status)
    if [ ! -d "$LOCK" ]; then echo "свободно"; exit 0; fi
    if expired; then echo "брошен (аренда истекла): $(field owner) — $(field what)"; exit 0; fi
    describe; exit 1 ;;
  *) sed -n '2,8p' "$0"; exit 2 ;;
esac
