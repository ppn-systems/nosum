#!/bin/sh
# Restores a dump made by backup.sh into the database named by PGDATABASE (docs/adr/ADR-031 D3).
#
#   restore.sh /backups/nosum-20261025-030000.dump
#
# Stop the host first (docs/ops/README.md): the restore drops and recreates every table. Connection: the standard libpq variables.
set -eu

if [ "$#" -ne 1 ] || [ ! -f "$1" ]; then
  echo "usage: restore.sh <dump file>" >&2
  exit 2
fi

# --clean --if-exists replaces what is there; a dump of an older schema also brings its __EFMigrationsHistory, and the host applies the newer migrations at its next start.
pg_restore --clean --if-exists --no-owner --no-privileges --exit-on-error --dbname "$PGDATABASE" "$1"
echo "restored $1 into $PGDATABASE"
