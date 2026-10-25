#!/bin/sh
# Proves backup and restore work (docs/adr/ADR-031 D3): dumps the database named by PGDATABASE with backup.sh, restores the dump into a scratch
# database with restore.sh, and compares the row count of every table. Also checks the 14-day retention. Needs a server the role may create databases on.
# Exit 0 = the backup restores to the same data. Connection: the standard libpq variables (PGDATABASE = the database to back up).
set -eu

here="$(cd "$(dirname "$0")" && pwd)"
source_db="$PGDATABASE"
scratch="nosum_restore_$$"
dir="$(mktemp -d)"
trap 'PGDATABASE=postgres psql -q -c "DROP DATABASE IF EXISTS \"$scratch\" WITH (FORCE)" >/dev/null 2>&1 || true; rm -rf "$dir"' EXIT

counts() {
  # One line per table: name and row count, sorted.
  psql -At -d "$1" -c "SELECT format('%I.%I', schemaname, tablename) FROM pg_tables WHERE schemaname = 'public' ORDER BY 1" |
    while read -r table; do
      printf '%s %s\n' "$table" "$(psql -At -d "$1" -c "SELECT count(*) FROM $table")"
    done
}

# 1. A backup is written, and only dumps older than the retention are removed.
touch -d '20 days ago' "$dir/nosum-20000101-000000.dump"
touch -d '13 days ago' "$dir/nosum-20000102-000000.dump"
BACKUP_DIR="$dir" BACKUP_KEEP_DAYS=14 "$here/backup.sh" --once >/dev/null
[ ! -e "$dir/nosum-20000101-000000.dump" ] || { echo "FAIL: a 20-day-old dump was kept" >&2; exit 1; }
[ -e "$dir/nosum-20000102-000000.dump" ] || { echo "FAIL: a 13-day-old dump was removed" >&2; exit 1; }
fresh="$(find "$dir" -name 'nosum-*.dump' -newer "$dir/nosum-20000102-000000.dump" | head -n 1)"
[ -n "$fresh" ] || { echo "FAIL: no new dump was written" >&2; exit 1; }
BACKUP_DIR="$dir" "$here/backup.sh" --check || { echo "FAIL: --check does not see the fresh dump" >&2; exit 1; }

# 2. The dump restores into an empty database with identical contents.
PGDATABASE=postgres psql -q -c "CREATE DATABASE \"$scratch\""
PGDATABASE="$scratch" "$here/restore.sh" "$fresh" >/dev/null
before="$(counts "$source_db")"
after="$(counts "$scratch")"
[ -n "$before" ] || { echo "FAIL: the source database has no tables to compare" >&2; exit 1; }
if [ "$before" != "$after" ]; then
  echo "FAIL: restored row counts differ" >&2
  printf 'source:\n%s\nrestored:\n%s\n' "$before" "$after" >&2
  exit 1
fi

echo "ok: $(printf '%s\n' "$before" | wc -l) tables restored with identical row counts"
