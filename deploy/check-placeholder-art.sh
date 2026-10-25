#!/bin/sh
# Placeholder-art gate (docs/adr/ADR-031 D9, docs/adr/ADR-007).
#
#   check-placeholder-art.sh [public|private] [repository-root]
#
# While assets/PLACEHOLDER_CONTENT exists the repository holds the Stardew Valley placeholder content, which must never be
# shipped publicly. A "public" build then fails (exit 1); a "private" build (the team's own deployment) passes with a warning.
# The release defaults to $NOSUM_RELEASE, then to "private". Exit 2 = bad arguments.
set -eu

release="${1:-${NOSUM_RELEASE:-private}}"
root="${2:-$(cd "$(dirname "$0")/.." && pwd)}"
marker="$root/assets/PLACEHOLDER_CONTENT"

case "$release" in
  public | private) ;;
  *)
    echo "check-placeholder-art: release must be 'public' or 'private', got '$release'" >&2
    exit 2
    ;;
esac

if [ -e "$marker" ]; then
  if [ "$release" = "public" ]; then
    {
      echo "REFUSED: this is a public release build, but assets/PLACEHOLDER_CONTENT exists."
      echo "The repository still contains the copyrighted Stardew Valley placeholder content (docs/adr/ADR-007)."
      echo "Public releases wait for milestone M6, which replaces the art and removes the marker. Build with NOSUM_RELEASE=private for the team deployment."
    } >&2
    exit 1
  fi
  echo "warning: this build contains placeholder art (docs/adr/ADR-007): deploy it privately only (IP allow-list)." >&2
fi

exit 0
