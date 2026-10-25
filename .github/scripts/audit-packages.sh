#!/usr/bin/env bash
# Fails when any project of any solution restores a NuGet package with a known vulnerability of High or Critical severity,
# direct or transitive (docs/adr/ADR-032 D7). Run it from the repository root: .github/scripts/audit-packages.sh
set -uo pipefail

report="$(mktemp)"
trap 'rm -f "$report"' EXIT

status=0
while IFS= read -r solution; do
  echo "== $solution"
  # "dotnet list package --vulnerable" prints one row per advisory: "> Package  Requested  Resolved  Severity  Advisory URL".
  if ! dotnet list "$solution" package --vulnerable --include-transitive 2>&1 | tee -a "$report"; then
    echo "dotnet list failed for $solution" >&2
    status=1
  fi
done < <(git ls-files '*.slnx')

if grep -E '^\s*>.*\s(High|Critical)\s' "$report"; then
  echo "Vulnerable packages of High or Critical severity found (listed above)." >&2
  status=1
fi

exit "$status"
