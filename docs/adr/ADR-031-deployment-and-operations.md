# ADR-031 — Deployment and operations

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M5. Builds on ADR-002 D5 (AOT web deploy),
ADR-003 (WebSocket server), ADR-009 (PostgreSQL).

## Context

Nothing is deployable yet (`deploy/README.md`). A browser MMO needs a reproducible server image, TLS in front of the
WebSocket, a static host for the WASM client, database migrations and backups, health checks, logs and metrics, and
admin tools — before any player outside the team connects.

## Decision

- **D1 — Images.** `deploy/host.Dockerfile` (multi-stage: SDK build → `mcr.microsoft.com/dotnet/runtime` image, non-root
  user, no SDK in the final image) for `Nosum.Host`; the WASM client is published with `-p:NosumWebDeploy=true` and
  served as static files by the reverse proxy. `deploy/compose.yaml` wires PostgreSQL 16, the host and Caddy for a
  single-server deployment; secrets come from environment files that are never committed (`.env.example` lists the
  names only).
- **D2 — TLS and routing.** Caddy terminates TLS (automatic certificates), serves the client at `/`, proxies `/ws/` to
  the host's WebSocket port; compression and long cache headers for fingerprinted `_framework` files, no cache for
  `index.html`. Nalix encryption stays on inside TLS (ADR-003).
- **D3 — Database.** Migrations run once at start by the host (already) with an advisory lock so two hosts never
  migrate together; nightly `pg_dump` to a volume with 14-day retention (a compose service); restore documented and
  tested in a script.
- **D4 — Health.** An HTTP health endpoint on a separate port (`/healthz`: process up; `/readyz`: database reachable and
  migrations applied); the compose healthcheck and Caddy use it.
- **D5 — Logs.** Structured JSON logs to stdout (event id, player id when known, map instance, latency) — never
  passwords, tokens, email addresses or chat text; a log-scrubbing test asserts it.
- **D6 — Metrics.** `System.Diagnostics.Metrics` counters/histograms (players online, per-instance players, snapshot
  tick duration, bytes sent per tick, handler latency per opcode, DB transaction duration, rejected intents by status)
  exposed in Prometheus text format on the health port.
- **D7 — Admin.** An `Admin` role on accounts (set only by a CLI command against the database, never over the wire);
  admin intents (kick, mute for N hours, ban, grant items, read reports from ADR-029) in the Account band slice `0x1100–0x11FF`,
  permission-checked by the existing fail-closed middleware and audited.
- **D8 — Load test.** `bench/Nosum.Bench.Load`: N headless WebSocket clients (the test client code) that sign in as
  guests, walk, farm and chat across maps; reports tick time and bandwidth. Target for M5: 200 clients with snapshot tick
  p99 < 20 ms on a 4-core server.
- **D9 — Placeholder content gate.** A deploy script refuses to build a public image while `assets/` contains the
  Stardew placeholder content (ADR-007): it checks for a marker file that M6 removes. Until M6 the deployment is private
  (allow-list by IP or basic auth on the client) for the team only.

## Consequences

The host gains a tiny HTTP listener (health + metrics); everything else is configuration in `deploy/`.
