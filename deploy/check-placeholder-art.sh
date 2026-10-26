#!/bin/sh
# Placeholder-art gate (docs/adr/ADR-031 D9, docs/adr/ADR-007, docs/adr/ADR-040 D8).
#
#   check-placeholder-art.sh [public|private] [repository-root]
#
# A public build passes only when all three hold: assets/PLACEHOLDER_CONTENT is gone; Nosum.AssetInventory --release reports no placeholder
# and no broken file; and assets/ holds nothing but the listed files and assets/README.md (the tool checks the last two). A private build
# (the team's own deployment) passes with a warning while the marker exists. The release defaults to $NOSUM_RELEASE, then to "private".
# Exit 1 = refused, 2 = bad arguments.
set -eu

here="$(cd "$(dirname "$0")/.." && pwd)"
release="${1:-${NOSUM_RELEASE:-private}}"
root="${2:-$here}"
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
      echo "Public releases wait for milestone M6, which replaces the art and removes the marker. Do not work around this gate (docs/ops/README.md)."
    } >&2
    exit 1
  fi
  echo "warning: this build contains placeholder art (docs/adr/ADR-007): do not publish it." >&2
fi

if [ "$release" = "public" ]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "REFUSED: the release check runs tools/Nosum.AssetInventory, and it needs dotnet." >&2
    exit 1
  fi

  # No reusable MSBuild nodes or build server: they inherit the output pipe and keep it open, so the capture below would never end.
  export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
  if ! report="$(dotnet run --project "$here/tools/Nosum.AssetInventory" -- --repo "$root" --release 2>&1)"; then
    printf '%s\n' "$report" >&2
    echo "REFUSED: assets/ does not pass the release check: a file is still a placeholder, missing, or not listed (docs/adr/ADR-040 D8)." >&2
    exit 1
  fi
fi

exit 0
