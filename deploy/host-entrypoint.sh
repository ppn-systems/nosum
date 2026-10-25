#!/bin/sh
# Container entrypoint of the host image (docs/adr/ADR-031 D2): writes the Nalix settings that depend on where the server is deployed,
# then starts the host. Nalix creates server.ini with every default on the first start and keeps the values already in it, so this edits
# only the keys it owns and leaves the rest (and any tuning by the operator) alone.
#
#   NOSUM_ORIGIN           required  public origin of the site, e.g. https://play.example.org; WebSocket handshakes from any other web origin are refused (CSWSH).
#   NOSUM_TRUSTED_PROXIES  optional  IPs/CIDRs of the reverse proxy (default: the compose network). Only these peers may name a client IP in X-Forwarded-For;
#                                    a peer outside the list that sends the header is dropped. Without this, every player would share the proxy's address
#                                    and its rate limits.
#
# `nosum-entrypoint --configure-only` writes the files and exits (used by the tests).
set -eu

config="${NALIX_CONFIG_PATH:-/data/config}"
origin="${NOSUM_ORIGIN:?set NOSUM_ORIGIN to the public origin of the site, for example https://play.example.org}"
proxies="${NOSUM_TRUSTED_PROXIES:-172.28.0.0/24}"
ini="$config/server.ini"

mkdir -p "$config"

# One entry per line, as trusted_proxies.txt wants.
{
  echo "# Written by nosum-entrypoint from NOSUM_TRUSTED_PROXIES at every start."
  printf '%s\n' "$proxies" | tr ', ' '\n\n' | sed '/^$/d'
} > "$config/trusted_proxies.txt"

if [ ! -f "$ini" ]; then
  cat > "$ini" <<INI
[ForwardedHeaders]
Enabled             = True
RequireTrustedProxy = True
ClientIpHeader      = XForwardedFor

[NetworkWebSocket]
AllowedOrigins = $origin
INI
else
  # Section-aware: "Enabled" exists in many sections.
  awk -v origin="$origin" '
    /^\[/ { section = $0 }
    section == "[ForwardedHeaders]" && /^Enabled[ \t]*=/             { print "Enabled             = True"; next }
    section == "[ForwardedHeaders]" && /^RequireTrustedProxy[ \t]*=/ { print "RequireTrustedProxy = True"; next }
    section == "[ForwardedHeaders]" && /^ClientIpHeader[ \t]*=/      { print "ClientIpHeader      = XForwardedFor"; next }
    section == "[NetworkWebSocket]" && /^AllowedOrigins[ \t]*=/      { print "AllowedOrigins           = " origin; next }
    { print }
  ' "$ini" > "$ini.new"
  mv "$ini.new" "$ini"
fi

if [ "${1:-}" = "--configure-only" ]; then
  exit 0
fi

exec "$@"
