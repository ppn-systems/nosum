#!/bin/bash
# SessionStart hook for Claude Code cloud sessions. Runs async: in the background it installs the
# .NET SDK pinned in global.json and restores every solution, so an agent can build right away.
# It does not build (a background build would race the agent's own build on artifacts/).
# Ready when /tmp/nosum-dotnet-ready exists; log in /tmp/nosum-session-start.log. Idempotent.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

echo '{"async": true, "asyncTimeout": 900000}'

cd "${CLAUDE_PROJECT_DIR:-$(pwd)}"
READY=/tmp/nosum-dotnet-ready
LOG=/tmp/nosum-session-start.log
rm -f "$READY"
exec >>"$LOG" 2>&1

DOTNET_DIR="$HOME/.dotnet"
if ! "$DOTNET_DIR/dotnet" --version >/dev/null 2>&1; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --jsonfile global.json --install-dir "$DOTNET_DIR" --no-path
fi

ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet 2>/dev/null || true

export DOTNET_ROOT="$DOTNET_DIR" PATH="$DOTNET_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
    echo "export PATH=\"$DOTNET_DIR:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
  } >> "$CLAUDE_ENV_FILE"
fi

for solution in $(git ls-files '*.slnx'); do
  dotnet restore "$solution"
done

touch "$READY"
