#!/bin/sh
# Nightly PostgreSQL backup (docs/adr/ADR-031 D3). Runs as the entrypoint of the compose `backup` service.
#
#   backup.sh           loop: dump every day at BACKUP_HOUR_UTC, forever
#   backup.sh --once    dump now and exit (also: how to take a manual backup)
#   backup.sh --check   exit 0 when the newest dump is younger than 26 hours (the container healthcheck)
#
# Connection: the standard libpq variables (PGHOST, PGUSER, PGPASSWORD, PGDATABASE).
# BACKUP_DIR (default /backups), BACKUP_KEEP_DAYS (default 14), BACKUP_HOUR_UTC (default 3).
set -eu

dir="${BACKUP_DIR:-/backups}"
keep="${BACKUP_KEEP_DAYS:-14}"
hour="${BACKUP_HOUR_UTC:-3}"

log() {
  printf '{"ts":"%s","level":"info","event":"backup.%s","msg":"%s"}\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2"
}

dump_once() {
  mkdir -p "$dir"
  stamp="$(date -u +%Y%m%d-%H%M%S)"
  target="$dir/nosum-$stamp.dump"
  # Custom format: compressed, and pg_restore can restore single tables. Written to a temp name first, so a half-written dump never looks like a backup.
  if pg_dump --format=custom --no-owner --no-privileges --file "$target.partial"; then
    mv "$target.partial" "$target"
    log done "Backup written."
  else
    rm -f "$target.partial"
    log failed "pg_dump failed."
    return 1
  fi
  # Retention: dumps older than $keep days go. -mtime +N means "more than N full days".
  find "$dir" -maxdepth 1 -name 'nosum-*.dump' -type f -mtime "+$keep" -print -delete | while read -r old; do
    log pruned "Old backup removed: $(basename "$old")"
  done
}

seconds_until_hour() {
  now="$(date -u +%s)"
  today="$(date -u -d "today $hour:00:00" +%s)"
  if [ "$today" -le "$now" ]; then
    today=$((today + 86400))
  fi
  echo $((today - now))
}

case "${1:-}" in
  --once)
    dump_once
    ;;
  --check)
    newest="$(find "$dir" -maxdepth 1 -name 'nosum-*.dump' -type f -mmin -1560 | head -n 1)"
    [ -n "$newest" ]
    ;;
  "")
    log started "Backups run daily at $hour:00 UTC and keep $keep days."
    while true; do
      sleep "$(seconds_until_hour)"
      dump_once || true
    done
    ;;
  *)
    echo "usage: backup.sh [--once | --check]" >&2
    exit 2
    ;;
esac
