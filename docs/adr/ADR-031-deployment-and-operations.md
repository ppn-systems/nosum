# ADR-031 — Deployment and operations

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Amended by ADR-041 (Caddy, TLS and the IP allow-list are replaced by Cloudflare Pages and a Cloudflare Tunnel). Roadmap M5. Builds on ADR-002 D5 (AOT web deploy),
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

## Implementation notes

Built on branch `feat/ops` (M5). Where the decision above is silent, this is what was chosen.

- **Layout.** `deploy/` holds the images, `compose.yaml`, `Caddyfile`, `backup/` and the placeholder-art gate; `docs/ops/README.md` is the runbook (deploy, update, health, logs,
  metrics, backup and restore, admin, rotating keys, load test, troubleshooting). `bench/Nosum.Bench.Load` is the load test. `Nosum.Host/Ops/` holds the endpoint, exporter,
  middleware and command line. No NuGet or npm package was added: the endpoint is the BCL `HttpListener` (so the final image stays on `dotnet/runtime`, not `aspnet`), the exporter
  is a `MeterListener`, and the load test reuses Nalix.SDK.
- **D1 images.** `host.Dockerfile` publishes on the SDK image and runs on `mcr.microsoft.com/dotnet/runtime:10.0` as the non-root `app` user. `web.Dockerfile` publishes the client with
  `-p:NosumWebDeploy=true` (installs `wasm-tools`) and serves it from a Caddy image, so Caddy and the static files ship as one unit. A per-Dockerfile `.dockerignore` keeps the
  build context to what the build reads (it must include `.editorconfig`: without it the trim analyzers fail the host publish). Compose pins the network (172.28.0.0/24) so the host
  can name Caddy as its trusted proxy.
- **D2 Caddy.** One `route` runs, in order: the IP allow-list, the operator's `access.d/*.caddyfile`, then `/ws/` (reverse proxy with an active check of the host's `/readyz` on port 57301),
  `/healthz`, and the client. Outside a `route`, Caddy sorts `handle` blocks before a bare `respond`, which silently skipped the allow-list in a first draft; the Caddyfile was therefore
  run for real with the real host (allowed, denied, WebSocket, caching headers). Fingerprinted `_framework` files (`name.<10 chars>.ext`, checked against a real publish) get
  `immutable`; everything else `no-cache`; precompressed `.br`/`.gz` files are served.
- **Nalix behind the proxy (not in the ADR, required for it to work).** Nalix limits connections and handlers per remote address. Behind Caddy every player would be the proxy's address. The
  entrypoint therefore enables `[ForwardedHeaders]` (`X-Forwarded-For`, `RequireTrustedProxy`) and writes `trusted_proxies.txt` from `NOSUM_TRUSTED_PROXIES`, and sets
  `[NetworkWebSocket] AllowedOrigins` to the site's origin (cross-site WebSocket hijacking). Checked with raw handshakes: a trusted peer with the header and a good origin gets 101, a bad
  origin 403, an untrusted peer that sends the header is dropped. The client's `GameSocketOptions.ForPageHost` now uses the page's own port for an https page (behind the proxy)
  and `57300` only for an http dev page.
- **Nalix data volume.** The server key pair (`certificate.private`/`.public`) lives in `NALIX_BASE_PATH`; the compose file mounts a volume on `/data`. Rotating it is in the runbook.
- **D3 database.** Migrations run under `pg_advisory_lock` on a dedicated connection (`NosumDatabase.MigrateAsync`); EF's Npgsql provider also takes a table lock, this makes the guarantee
  explicit. Tested with three concurrent migrations and with a held lock. Backups are `pg_dump --format=custom` once a day at `BACKUP_HOUR_UTC` into a volume, 14 days kept;
  `restore.sh` is `pg_restore --clean --if-exists`; `test-restore.sh` proves retention and a row-for-row restore and runs as a test.
- **D4 health.** `/healthz` always 200 while the process runs; `/readyz` = database reachable and no pending migration; the endpoint starts before the migration, so a slow migration
  reads as "not ready" instead of "down". `Nosum.Host.dll healthcheck` is the container check (no curl in the image).
- **D5 logs.** `OpsLog` writes one JSON object per line. A field whose name contains password, token, secret, key, hash, e-mail, mail, text, chat, authorization, cookie or credential is
  redacted whatever it holds; e-mail addresses and 32+ character token-shaped strings in any value are masked (ids pass). `Console.Error` is routed into it. A test runs a full admin session
  (register, sign in, chat, mute, ban, failed login) and asserts that no e-mail, password, session token or chat text appears.
- **D6 metrics.** Names and labels are in the runbook. The per-map gauge folds private copies under their map id (an owner id as a label would be unbounded). Handler latency covers the whole
  pipeline including the permission and rate-limit checks; handlers over 100 ms are also logged with player id and map instance. "Rejected intents" is counted for movement, chat and admin
  intents only; other features answer with their own status enums at too many places to hook honestly.
- **D7 admin.** `players.role` (smallint, 0 player, 1 admin), `banned_at`, `muted_until` and the `admin_audit` table (migration `AdminRoleBanMuteAudit`, generated). The role is set only by
  `Nosum.Host admin grant|revoke` (audited, guests refused). Packets `0x1101-0x1107` / `0x1181-0x1183` in `Nosum.Contracts/Admin`; handlers need `SYSTEM_ADMINISTRATOR`, which a connection gets
  only when it signs in as an admin account, and `AdminService` re-reads the role on every call, so a revoked admin is refused at once on a connection that is still open. Self and other
  admins are refused as targets of kick, mute and ban. Appended enum values: `AccountStatus.Banned = 15` (login and resume, after the password check, so the ban does not leak), `SessionKicked = 16`,
  `ChatStatus.Muted = 17`; no protocol version bump (new packets and appended values only). The mute list is in memory (one process) and loaded from the column at start; the client has
  texts for the three codes and shows the session-ended notice for kick and ban. "Read reports" returns the report headers (20 newest) and, per report, its stored lines.
- **D8 load test.** `Nosum.Bench.Load` bots are guests that report their position at 10 Hz with the server's own `Walker`, travel to a public map over the exits (route over the maps' warps, BFS
  over the tiles, closing tiles the server refuses as blocked), farm with the starter kit and chat. Because they share one address, the server under test runs with `NOSUM_LOAD_TEST=1`
  (rate limits off; honoured only in Staging/Development, the host refuses to start otherwise). The report puts the clients' view next to the tick histogram taken from `/metrics` before and
  after the steady phase and exits non-zero over budget. Last run (4-core container, server and bench on the same machine, 60 s steady phase after a 40 s ramp): 200/200 clients joined,
  118 of 120 travellers reached their map, **snapshot tick mean 4.6 ms, p99 at most 10 ms (budget 20 ms)**, 24 KiB per tick (244 KiB/s before encryption), farm and chat round trips
  p99 16 ms and 5 ms. Also 100 clients: mean 2.4 ms, p99 at most 10 ms. The tick percentiles are bucket upper bounds.
- **D9 gate.** The marker is `assets/PLACEHOLDER_CONTENT`. `deploy/check-placeholder-art.sh [public|private]` (exit 1 for public while it exists), the MSBuild target in
  `deploy/PlaceholderGate.targets` (imported through `server/` and `wasm/` `Directory.Build.targets`; `-p:NosumRelease=public` fails the publish before anything is copied), and both
  Dockerfiles run the script first. The private deployment is `NOSUM_ALLOW_IPS`, which compose requires. M6 (ADR-040 phase 4) removed the marker; the public check also runs
  `Nosum.AssetInventory --release` (every listed file replaced, nothing unlisted under `assets/`), so a public build needs both.
- **Weak spots.** (1) The Docker daemon was not available where this was built: the images were not built and the compose stack was not started. What was checked instead: the Dockerfile
  COPY sets were reproduced in a clean directory and published (host and client); `docker compose config` accepts the file and refuses to start without the required settings; the
  Caddyfile and the entrypoint-written Nalix settings were run together for real; every script has a test. The AOT client publish (`wasm-tools`) was not run. (2) Nalix prints its own banner and
  diagnostics as plain text, so stdout is JSON only for the server's own events; filter with `grep '^{'`. (3) Server key pinning on the client and key-rotation windows are ADR-032, not
  here; the runbook describes the rotation order assuming them. (4) A single process: the mute list and the session registry are in memory, so a second host would need them shared first.
  (5) `/metrics` and `/readyz` are unauthenticated and rely on the port not being published. (6) The audit row of a grant records the order given even when the transfer then fails
  (inventory full); the failure is logged as `admin.grant_failed`. (7) There is no admin screen in the client; the intents are packets for tools. (8) Backups sit on a volume on the same
  disk until someone copies them off; the runbook says how. (9) The per-address limits are what make the load test need its own mode; a run from many addresses without the switch
  would test them as well.
