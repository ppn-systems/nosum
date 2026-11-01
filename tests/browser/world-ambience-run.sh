#!/usr/bin/env bash
# Runs one world-ambience scenario: restarts the Development server with the calendar epoch that shows the scenario's time, season
# and weather (NOSUM_CALENDAR_EPOCH; docs/adr/ADR-018), then runs world-ambience-capture.cjs. The client (wasm, -p:NosumDevTools=true) must
# already be serving on :5080 and Postgres on :5432 with database "nosum".
# usage: EPOCH=2026-10-30T10:40:40.000Z SCENARIO=noon-spring-rain OUT_DIR=... tests/browser/world-ambience-run.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
for pid in $(pgrep -f '[s]erver/Nosum[.]Host|[b]in/Nosum[.]Host|[d]ebug/Nosum[.]Host' || true); do kill "$pid" 2>/dev/null || true; done
sleep 3
(cd "$ROOT" && ASPNETCORE_ENVIRONMENT=Development NOSUM_CALENDAR_EPOCH="$EPOCH" \
  ConnectionStrings__Default="Host=localhost;Database=nosum;Username=nosum;Password=nosum" \
  nohup dotnet run --no-build --project server/Nosum.Host > "/tmp/server-$SCENARIO.log" 2>&1 &)
for _ in $(seq 1 60); do grep -q "host.started" "/tmp/server-$SCENARIO.log" 2>/dev/null && break; sleep 2; done
cd "$ROOT" && SCENARIO="$SCENARIO" node tests/browser/world-ambience-capture.cjs
