# Operations runbook

How to deploy, watch, back up, restore and administer a Nosum server. The decisions behind it are in
[ADR-031](../adr/ADR-031-deployment-and-operations.md); the files are in [`deploy/`](../../deploy/README.md).

The deployment is **one server**: PostgreSQL 16, the Nosum host, and Caddy (TLS, the static client, the WebSocket proxy), started
by `deploy/compose.yaml`, plus a nightly backup container. State lives in five named Docker volumes:

| Volume | Holds | Lose it and… |
| :--- | :--- | :--- |
| `db_data` | the PostgreSQL data | every account and farm is gone — restore the newest backup |
| `backups` | the nightly `pg_dump` files (14 days) | you have no backup; copy them off the machine (see below) |
| `nalix_data` | the server key (`certificate.private` / `.public`), `server.ini`, block lists | the server gets a new key (see "Rotate keys") |
| `caddy_data` | the TLS certificates | Caddy asks for new ones (rate limits of the CA apply) |
| `caddy_config` | Caddy's autosaved config | nothing important |

## First deployment

You need a machine with Docker (Compose v2), ports 80 and 443 open, and a DNS name pointing at it.

```bash
cp deploy/.env.example deploy/.env       # then fill every value; .env is never committed
docker compose -f deploy/compose.yaml --env-file deploy/.env up -d --build
```

`--build` compiles the host and publishes the WASM client ahead-of-time (several minutes the first time).

What the values mean:

- `POSTGRES_PASSWORD` — letters and digits only (it is put in a connection string). It is read by PostgreSQL **only when the data volume is
  created**; changing it later is "Rotate the database password" below.
- `NOSUM_DOMAIN` — the public host name. Caddy gets a Let's Encrypt certificate for it. `localhost` gives a self-signed one for a trial run.
- `NOSUM_ALLOW_IPS` — **required.** Who may connect, space-separated addresses or CIDRs. Until milestone M6 the deployment is private (see
  "Private until M6"); compose refuses to start without it.
- `NOSUM_RELEASE` — `private` (default) or `public`. A `public` build fails while `assets/PLACEHOLDER_CONTENT` exists.

Check it is up:

```bash
docker compose -f deploy/compose.yaml ps                      # every service "healthy"
curl -fsS https://$NOSUM_DOMAIN/healthz                       # Caddy answers "ok" (from an allowed address)
docker compose -f deploy/compose.yaml exec host dotnet Nosum.Host.dll healthcheck   # "ready": database reachable, migrations applied
docker compose -f deploy/compose.yaml logs host | grep '"event":"host.started"'
```

### Private until M6

The repository still contains the Stardew Valley placeholder content ([ADR-007](../adr/ADR-007-placeholder-content-16px.md)), which must not be
shared publicly. So:

1. `assets/PLACEHOLDER_CONTENT` marks it. While it exists, `deploy/check-placeholder-art.sh public` and any `dotnet publish -p:NosumRelease=public`
   **fail**. The Dockerfiles run the same check (build arg `NOSUM_RELEASE`), so `docker build` cannot be used to get around it.
2. `NOSUM_ALLOW_IPS` is the access control: Caddy answers 403 to every other address, for the client and the WebSocket alike. For passwords in front
   of the client, add Caddy rules in `deploy/access.d/*.caddyfile` (see its README).
3. Milestone M6 replaces the art and deletes the marker as its last step. Then set `NOSUM_RELEASE=public` and `NOSUM_ALLOW_IPS=0.0.0.0/0 ::/0`.

## Updating

```bash
git pull
docker compose -f deploy/compose.yaml --env-file deploy/.env up -d --build
```

The host applies pending migrations when it starts, under a PostgreSQL advisory lock (a second host that starts at the same time waits for the first).
While it migrates, `/healthz` answers and `/readyz` says "not ready", and Caddy sends no players to it. **Take a backup first** when the update has a
migration (`ls server/Nosum.Infrastructure/Persistence/Migrations`): migrations are not reverted automatically; to go back, restore the backup.

## Health, logs and metrics

The host serves a small HTTP endpoint on its own port, **57301**, which is never published to the internet (inside the compose network only):

| Path | Meaning |
| :--- | :--- |
| `/healthz` | the process is up (always `200` while it runs) |
| `/readyz` | `200` when the database answers and no migration is pending, else `503` |
| `/metrics` | Prometheus text format |

Caddy checks `/readyz` of the host every 10 seconds and stops proxying WebSockets to a host that is not ready. The compose healthcheck runs
`dotnet Nosum.Host.dll healthcheck`, which asks the same `/readyz`.

**Logs** are JSON lines on standard output (`docker compose logs host`): `ts`, `level`, `event`, `msg`, and fields such as `player_id`,
`map_instance`, `latency_ms`. Events you will meet: `host.started`, `ops.listening`, `admin.kick|mute|ban|unban|grant` (admin id, target id),
`handler.slow` (a handler over 100 ms, with its opcode), `console.error` (the server's own error output, captured). They never carry passwords,
tokens, e-mail addresses, chat text or keys: fields named like a secret are replaced by `[redacted]`, e-mail addresses and long token-like strings are
masked in every value, and a test runs a whole admin session and asserts it (`AdminWireTests`). Lines that do **not** start with `{` are Nalix's own
start-up banner and diagnostics; filter them out with `grep '^{'` before feeding a JSON parser.

**Metrics** (all on `/metrics`; no label carries a player id, nickname or text):

| Metric | Type | Labels | Use |
| :--- | :--- | :--- | :--- |
| `nosum_players_online` | gauge | – | players on any map |
| `nosum_instance_players` | gauge | `instance` (map id) | where the crowds are (private farms count under `farm`) |
| `nosum_snapshot_tick_duration_ms` | histogram | – | time to build and send one round of snapshots — **the budget is p99 < 20 ms** |
| `nosum_snapshot_bytes_per_tick` | histogram | – | snapshot bytes per round, before encryption — bandwidth is this × 10 per second |
| `nosum_handler_duration_ms` | histogram | `opcode` | handler latency per packet type |
| `nosum_db_transaction_duration_ms` | histogram | – | time of one database save |
| `nosum_intents_rejected_total` | counter | `area`, `status` | refused intents (`move`, `chat`, `admin`), a cheat or lag signal |

Example Prometheus alerts:

```yaml
- alert: NosumTickOverBudget
  expr: histogram_quantile(0.99, sum(rate(nosum_snapshot_tick_duration_ms_bucket[5m])) by (le)) > 20
  for: 10m
- alert: NosumNotReady
  expr: probe_success{job="nosum-readyz"} == 0
  for: 2m
```

Prometheus is not part of the compose file; scrape `host:57301` from another container on the `nosum` network, or run
`docker compose exec host dotnet Nosum.Host.dll healthcheck` from a monitor.

## Backups and restore

The `backup` service runs `pg_dump` (custom format) every day at `BACKUP_HOUR_UTC` (default 03:00) into the `backups` volume as
`nosum-YYYYMMDD-HHMMSS.dump` and deletes dumps older than `BACKUP_KEEP_DAYS` (default 14). Its healthcheck fails when the newest dump is older than
26 hours.

```bash
docker compose -f deploy/compose.yaml exec backup /backup/backup.sh --once          # a backup now
docker compose -f deploy/compose.yaml exec backup ls -lh /backups                   # what exists
docker compose -f deploy/compose.yaml cp backup:/backups/nosum-20261025-030000.dump ./   # copy one off the machine
```

**Copy dumps off the machine** (a cron job with `docker compose cp` and `rclone`/`scp`): a volume on the same disk does not survive the disk.

**Restore** (this replaces the whole database with the dump):

```bash
docker compose -f deploy/compose.yaml stop host                                # nobody writes during a restore
docker compose -f deploy/compose.yaml exec backup /backup/restore.sh /backups/nosum-20261025-030000.dump
docker compose -f deploy/compose.yaml start host                               # applies any newer migrations
```

`restore.sh` drops and recreates every table from the dump (`pg_restore --clean --if-exists`). A restore from another machine: copy the file into the
`backups` volume first (`docker compose cp ./file backup:/backups/`).

**Test the backups** — on a development machine with PostgreSQL client tools, against any database (here the dev one):

```bash
PGHOST=localhost PGUSER=nosum PGPASSWORD=… PGDATABASE=nosum_dev sh deploy/backup/test-restore.sh
```

It takes a backup, checks the 14-day retention, restores into a scratch database and compares the row count of every table. The same script runs as
a test in `tests/Nosum.IntegrationTests/Ops/DeployScriptTests.cs`. Restore a real dump into a scratch database once a quarter to be sure it works.

## Administration

An **admin** is a registered account whose role was set from the server's command line — never over the wire, so nothing a player sends can
create one. A guest cannot be an admin.

```bash
docker compose -f deploy/compose.yaml exec host dotnet Nosum.Host.dll admin grant  SomeNickname
docker compose -f deploy/compose.yaml exec host dotnet Nosum.Host.dll admin revoke SomeNickname
docker compose -f deploy/compose.yaml exec host dotnet Nosum.Host.dll admin list
```

The next time that player signs in their connection has the admin permission level. Revoking takes effect at once even for a connection that is
already open: every admin intent re-reads the role from the database. Both commands are audited.

The admin intents are packets in the Account band slice `0x1100–0x11FF` (`Nosum.Contracts.Admin`); there is no admin screen in the client yet, so
they are sent by a tool or a test client:

| Intent | Opcode | Effect |
| :--- | :--- | :--- |
| `AdminKick` | `0x1101` | ends the player's connection (they get `SessionKicked`); they may sign in again |
| `AdminMute` | `0x1102` | chat is refused for N hours (1–720); 0 lifts the mute. Survives a restart |
| `AdminBan` | `0x1103` | the connection ends with `Banned`; login and resume are refused until unbanned |
| `AdminUnban` | `0x1104` | lifts a ban |
| `AdminGrantItem` | `0x1105` | gives items (up to one stack) and/or coins through the transfer service |
| `AdminListReports` / `AdminReadReport` | `0x1106` / `0x1107` | the newest chat reports and the messages stored with one |

An admin cannot kick, mute or ban themselves or another admin (revoke the role first). Replies carry `AdminStatus` codes.

Every action is a row in `admin_audit` (who, what, whom, when):

```sql
SELECT a.created_at, adm.nickname AS admin, a.action, t.nickname AS target, a.detail
FROM admin_audit a
LEFT JOIN players adm ON adm.id = a.admin_id
LEFT JOIN players t   ON t.id = a.target_id
ORDER BY a.created_at DESC LIMIT 50;
```

`action`: 1 kick, 2 mute, 3 ban, 4 unban, 5 grant, 6 role granted, 7 role revoked. A role change from the command line has no `admin_id`.

## Rotate keys

**Server key (Nalix handshake).** The server's static X25519 key pair is `certificate.private` / `certificate.public` in the `nalix_data` volume
(`/data/config`), created on the first start. Rotate it when the private file may have leaked, or on a schedule:

1. Once clients pin the key ([ADR-032](../adr/ADR-032-security-hardening.md) D1), first ship a client that lists **both** the old and the new
   fingerprint (the new one is shown after step 3 — start the host once on a spare volume to read it, or accept one short window of TOFU).
2. `docker compose -f deploy/compose.yaml stop host`
3. `docker compose -f deploy/compose.yaml run --rm --no-deps --entrypoint sh host -c 'rm -f /data/config/certificate.private /data/config/certificate.public'`
4. `docker compose -f deploy/compose.yaml start host` — a new pair is generated; `cat /data/config/certificate.public` is its public key.
5. After the clients have updated, drop the old fingerprint from the client config.

Everyone is disconnected by the restart; sessions (login tokens) are unaffected because they do not depend on this key.

**Database password.**

```bash
docker compose -f deploy/compose.yaml exec db psql -U nosum -d nosum -c "ALTER ROLE nosum PASSWORD 'NEWPASSWORD'"
# put NEWPASSWORD in deploy/.env, then
docker compose -f deploy/compose.yaml --env-file deploy/.env up -d host backup
```

**TLS certificates** rotate by themselves (Caddy renews them); nothing to do while `caddy_data` is kept. **Session tokens** are random and stored
only as hashes, there is no signing key to rotate; to end every session at once run `UPDATE player_session_tokens SET revoked_at = now() WHERE revoked_at IS NULL;`.

## Load test

`bench/Nosum.Bench.Load` simulates guests that walk through the exits to public maps, farm and chat, then reports what the clients saw next to the
server's own tick time and bandwidth over the same steady phase (read from `/metrics`). The M5 budget is **200 clients with a snapshot tick p99 under
20 ms on a 4-core server**.

The bots share one address, so the server's per-address rate limits would throttle all of them as one player. The server under test therefore runs in
**load-test mode**: `NOSUM_LOAD_TEST=1` with `DOTNET_ENVIRONMENT=Staging` (or `Development`) leaves the rate-limit middleware out and logs a
`host.load_test_mode` warning at start. With the variable set in any other environment — Production included — the host refuses to start. Never put
real players on a server in this mode; the compose file does not pass the variable on.

```bash
# on the server under test (a staging copy, not the public one); the ops port must be reachable by the bench, hence NOSUM_OPS_HOST=+
DOTNET_ENVIRONMENT=Staging NOSUM_LOAD_TEST=1 NOSUM_OPS_HOST=+ dotnet Nosum.Host.dll

# from another machine
dotnet run --project bench/Nosum.Bench.Load -c Release -- --clients 200 --ramp 30 --duration 120 --host <server>
```

The bots create 200 guest accounts (idle guests are deleted after 30 days) and farm in them, so use a throw-away database. Read the report top to bottom:
`clients` (did they all get in), `journeys` (travellers that reached their map), `moves` (refused by the server), `tick` (the budget), `bandwidth`. The exit code is 0
when the budget is met and at least 95 % of the clients joined. Running the bench on the same machine as the server steals its cores: a local run is pessimistic for the
server; judge the budget with the bench on a second machine. The numbers of the last run are in [ADR-031](../adr/ADR-031-deployment-and-operations.md), "Implementation notes".

## Troubleshooting

| Symptom | Look at |
| :--- | :--- |
| Players cannot connect, the page loads | `docker compose logs caddy` — is the host `healthy`? Caddy sends no players to a host whose `/readyz` is 503 |
| Everyone is rate limited at once | `server.ini` `[ForwardedHeaders]` must be enabled and `trusted_proxies.txt` must contain Caddy's address (the entrypoint writes both); otherwise every player looks like the proxy's address |
| WebSocket handshake gives 403 | `AllowedOrigins` in `server.ini` must equal `https://$NOSUM_DOMAIN` (the entrypoint rewrites it at every start from `NOSUM_ORIGIN`) |
| 403 from Caddy | the visitor's address is not in `NOSUM_ALLOW_IPS` |
| Host restarts, log says `ConnectionStrings__Default is not set` | the compose file passes it; running the image by hand needs it (Production refuses the development database) |
| `/readyz` stays 503 | `docker compose logs host`; the database is down or a migration is failing |
| Backups stopped | `docker compose ps` (backup `unhealthy`), `docker compose logs backup` |
