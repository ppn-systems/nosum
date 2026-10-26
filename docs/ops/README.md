# Operations runbook

How to set up, deploy, roll back, watch, back up, restore and administer the Nosum production server. The decisions are in
[ADR-041](../adr/ADR-041-deploy-cloudflare-pages-and-tunnel.md) (the current design) and [ADR-031](../adr/ADR-031-deployment-and-operations.md)
(images, health, backups, metrics, admin). The files are in [`deploy/`](../../deploy/README.md).

Production is **one server** and the **web client on Cloudflare Pages**:

```
player's browser ──https──► Cloudflare Pages      (the WASM client: the project "nosum")
player's game    ──wss────► Cloudflare edge ──tunnel "nosum-api"──► cloudflared ──► host:57300   (nosum.ppnsys.com/ws/)
                                                                              │
                                                                              ├── postgres (nothing published)
                                                                              └── backup (nightly pg_dump)
```

The host has **one inbound port: SSH** (22). The tunnel connector dials out to Cloudflare; nothing dials in.

## Secrets and values

Set these in GitHub → repository → **Settings → Environments → production** (secrets), and **Settings → Secrets and variables → Actions → Variables** (values).

| Name | Kind | Value and where to get it |
| :--- | :--- | :--- |
| `PROD_HOST` | secret | The host's address: its public IP or DNS name. SSH is the only port that must answer. |
| `PROD_SSH_USER` | secret | The deploy user on that host. It must be in the `docker` group. |
| `PROD_SSH_KEY` | secret | A private key for that user (section "Bootstrap", step 3). |
| `CLOUDFLARE_TUNNEL_TOKEN` | secret | Zero Trust dashboard → Networks → Tunnels → `nosum-api` → the Docker install command: the value after `--token`. |
| `POSTGRES_PASSWORD` | secret | A new random value: `openssl rand -hex 24`. Letters and digits only. |
| `CLOUDFLARE_API_TOKEN` | secret | Dashboard → My Profile → API Tokens → Create Custom token: permission **Account · Cloudflare Pages · Edit**, for your account only. |
| `CLOUDFLARE_ACCOUNT_ID` | secret | Dashboard → any account page, right-hand column ("Account ID"). |
| `NOSUM_SMTP_HOST`, `_PORT`, `_USER`, `_PASSWORD`, `_FROM`, `_SSL` | secret, optional | Your mail provider (password recovery codes). Without `NOSUM_SMTP_HOST` no mail is sent. |
| `NOSUM_ORIGIN` | variable | The address players open: production uses `https://farm.ppnsys.com` (the Pages custom domain); without one, the Pages address such as `https://nosum.pages.dev`. The server accepts WebSocket handshakes from this origin only, so a second address (for example the `pages.dev` one) cannot connect. |
| `NOSUM_SERVER_PUBLIC_KEY` | variable | The server's public key: see "Read the server key" below. Public, so a variable, not a secret. |
| `NOSUM_SERVER_URL` | variable, optional | Default `wss://nosum.ppnsys.com/ws/`. |
| `NOSUM_CALENDAR_EPOCH` | variable, optional | The start of the server calendar (ISO 8601 UTC). Default: the built-in epoch. |

`deploy/.env` is written on the host by the deploy and is never committed (`.gitignore`). Compose requires `POSTGRES_PASSWORD`,
`CLOUDFLARE_TUNNEL_TOKEN`, `NOSUM_ORIGIN` and `NOSUM_TAG`; `deploy/.env.example` lists every name.

## Bootstrap (once, per host)

1. **Install Docker Engine with the Compose plugin** (`docker compose version` must work) and create the deploy user:
   `sudo usermod -aG docker <user>`.
2. **Create the directory** the deploy copies into, and make it the user's: `sudo mkdir -p /opt/nosum/backup && sudo chown -R <user> /opt/nosum`.
3. **Create the deploy key** on your own computer and install it:
   `ssh-keygen -t ed25519 -f nosum-deploy -C nosum-deploy -N ""`; append `nosum-deploy.pub` to `~<user>/.ssh/authorized_keys` on the host;
   paste the **private** file `nosum-deploy` into the `PROD_SSH_KEY` secret; then delete the local copy.
4. **Close every port but SSH.** `sudo ufw allow OpenSSH && sudo ufw enable`. Ports 80 and 443 are not needed: the tunnel makes the outbound connection.
5. **Set up the tunnel and Pages** (sections below), then run the two deploy workflows (section "Deploy").

The first deploy writes `deploy/.env` and starts PostgreSQL, the host and the connector, so there is no separate database step. To check the
database password before the first deploy, start PostgreSQL alone by hand: `cd /opt/nosum && docker compose --env-file .env -f compose.yaml up -d postgres`.

## The Cloudflare Tunnel (`nosum-api`)

One-time, in the Cloudflare Zero Trust dashboard:

1. **Networks → Tunnels → Create a tunnel**. Connector type **Cloudflared**. Name it `nosum-api` (the name is free: only the token and the public hostname matter to the deploy).
2. On the install page choose **Docker**, copy the token (the value after `--token`) and store it as the `CLOUDFLARE_TUNNEL_TOKEN` secret.
   You do not run the install command: the deploy starts the connector (`cloudflared` in `deploy/compose.yaml`).
3. **Public hostname**: subdomain `nosum`, domain `ppnsys.com`, path empty, service type **HTTP**, URL **`host:57300`**. Use the service name
   `host`, not `localhost`: the connector runs in the compose network, where `host` is the game server.
4. **WebSockets** are on by default for a tunnel. Leave them on; the game is a WebSocket. Nothing else to set.

Cloudflare closes a WebSocket that carries no traffic for about 100 seconds. The client therefore sends a keep-alive PING every 25 seconds
(`GameSocketOptions.KeepAliveInterval`, ADR-041 D3). If players drop after exactly about 100 seconds of stillness, check the PING is sent.

After the first deploy, the tunnel shows **Healthy** in the dashboard. Check the WebSocket from anywhere (expect `101 Switching Protocols`):

```bash
curl -i -N -H "Connection: Upgrade" -H "Upgrade: websocket" -H "Sec-WebSocket-Version: 13" \
  -H "Sec-WebSocket-Key: $(openssl rand -base64 16)" -H "Origin: https://nosum.pages.dev" https://nosum.ppnsys.com/ws/
```

A **different** origin must get no upgrade: the same command with `Origin: https://example.org` should not return `101`.

## Cloudflare Pages (the web client)

One-time, in the dashboard (**Workers & Pages → Create → Pages → Upload assets** / Direct Upload):

1. Create the project named **`nosum`**. The deploy uploads the built files with `wrangler pages deploy`, so no Git connection is needed.
   Production branch: `main` (the deploy uses `--branch=main`).
2. Note the address, `https://nosum.pages.dev`, and set the `NOSUM_ORIGIN` variable to it.
3. A custom domain is optional: add it under the project's **Custom domains**, then set `NOSUM_ORIGIN` to that domain's `https://` origin and
   redeploy the server (the server's allowed origin changes with it).

`wwwroot/_headers` (in the repository) sets the caching and security headers. After the first deploy, `curl -I https://nosum.pages.dev/_framework/`
should show the caching headers of the framework files.

## Read the server key

The client trusts only the server key it is given (ADR-032 D1). The host prints its public key at start-up and keeps it in the volume:

```bash
cd /opt/nosum && docker compose --env-file .env -f compose.yaml exec host cat /data/config/certificate.public
```

Put that value (64 hex digits) in the `NOSUM_SERVER_PUBLIC_KEY` variable. The web deploy refuses to publish without it.

## Deploy

Deploy the **server first**, then the **web client** (the client needs the server key and the address).

1. **Actions → deploy-server → Run workflow.** It runs on the branch you pick and deploys that commit. The steps: the image build and push to `ghcr.io/ppn-systems/nosum-host:<commit>`, the copy of `compose.yaml` and `backup/` to `/opt/nosum`, the write of
   `deploy/.env`, `docker compose pull` and `up -d`, and the readiness gate (up to five minutes).
2. **Actions → deploy-web → Run workflow.** It publishes the client with the server address and pinned key, and deploys it to Pages.
3. Check: the tunnel is Healthy, and the WebSocket check in "The Cloudflare Tunnel" returns `101`. Open the Pages address and sign in.

Both workflows are manual only and run in the `production` environment. A deploy is a decision.

**Rollback.** The deploy keeps the previous tag in `NOSUM_PREVIOUS_TAG`. If the readiness gate fails, the deploy restores that tag automatically
and reports it. To roll back by hand (to any commit whose image is still in GHCR):

```bash
cd /opt/nosum
sed -i 's/^NOSUM_TAG=.*/NOSUM_TAG=<commit sha>/' .env
docker compose --env-file .env -f compose.yaml up -d host
```

A rollback does **not** undo database migrations. If the update you roll back ran a migration (`ls server/Nosum.Infrastructure/Persistence/Migrations`
shows the newest), restore the backup taken before it (below). For the client, re-run **deploy-web** from the older commit, or use Pages' own
rollback in the dashboard.

## Health, logs and metrics

On the host, `cd /opt/nosum` and use `dc` as a shorthand: `dc() { docker compose --env-file .env -f compose.yaml "$@"; }`.

| Check | Command | Answer |
| :--- | :--- | :--- |
| Every service | `dc ps` | `postgres`, `host`, `cloudflared`, `backup` running, the first three healthy |
| Ready (database, migrations) | `dc exec -T host dotnet Nosum.Host.dll healthcheck` | `ready`, exit 0 |
| Logs | `dc logs -f host` | JSON lines; filter with `grep '^{'` |
| Metrics | `dc exec host bash -c 'exec 3<>/dev/tcp/127.0.0.1/57301; printf "GET /metrics HTTP/1.0\r\n\r\n" >&3; cat <&3'` | Prometheus text |

The ops port (**57301**) is bound to localhost inside the container and is **not published**: nothing outside the container can reach
`/healthz`, `/readyz` or `/metrics`. Only the game WebSocket is published, and only to the host's loopback (`127.0.0.1:57300`), for debugging on the host.

`/healthz` says the process is up; `/readyz` says the database answers and no migration is pending. Players reach the host only through the tunnel,
and the connector starts only after the host is healthy.

**Logs** are JSON lines on standard output. Events you will meet: `host.started`, `ops.listening`, `admin.kick|mute|ban|unban|grant`,
`handler.slow`, `console.error`. They never carry passwords, tokens, e-mail addresses, chat text or keys (`AdminWireTests` checks it).
Lines that do not start with `{` are Nalix's own start-up banner.

**Metrics** (all on `/metrics`; no label carries a player id, nickname or text): `nosum_players_online` (gauge), `nosum_instance_players` (gauge,
`instance`), `nosum_snapshot_tick_duration_ms` (histogram; **budget p99 < 20 ms**), `nosum_snapshot_bytes_per_tick` (histogram),
`nosum_handler_duration_ms` (histogram, `opcode`), `nosum_db_transaction_duration_ms` (histogram), `nosum_intents_rejected_total` (counter,
`area`, `status`).

Prometheus is not part of the compose file. A monitor that scrapes `/metrics` must run inside the compose network, or on the host with the
same `exec` as above.

**Check the client address.** Players' addresses reach the server in `X-Forwarded-For` through the connector (ADR-041 Consequences). If every
player shows the same address, the rate limits are shared; look at `nosum_intents_rejected_total` after a test with two networks.

## Backups and restore

The `backup` service runs `pg_dump` (custom format) every day at `BACKUP_HOUR_UTC` (default 03:00 UTC) into the `backups` volume as
`nosum-YYYYMMDD-HHMMSS.dump`, and deletes dumps older than `BACKUP_KEEP_DAYS` (default 14).

```bash
dc exec backup /backup/backup.sh --once           # a backup now
dc exec backup ls -lh /backups                    # what exists
dc cp backup:/backups/nosum-20261026-030000.dump ./   # copy one off the host
```

**Copy dumps off the host** on a schedule (for example `scp` from a cron job on another machine). A volume on the same disk does not survive the disk.

**Restore** replaces the whole database with the dump:

```bash
dc stop host                                                          # nobody writes during a restore
dc exec backup /backup/restore.sh /backups/nosum-20261026-030000.dump
dc start host                                                         # applies any newer migrations
```

`restore.sh` drops and recreates every table from the dump (`pg_restore --clean --if-exists`). To restore a dump from another machine,
copy it into the volume first: `dc cp ./file backup:/backups/`.

**Test the backups** on a development machine with PostgreSQL client tools, against a scratch database:
`PGHOST=localhost PGUSER=nosum PGPASSWORD=… PGDATABASE=nosum_dev sh deploy/backup/test-restore.sh`. The same script runs as a test in
`tests/Nosum.IntegrationTests/Ops/DeployScriptTests.cs`. Restore a real dump into a scratch database once a quarter.

## Administration

An **admin** is a registered account whose role was set from the server's command line, never over the wire.

```bash
dc exec host dotnet Nosum.Host.dll admin grant  SomeNickname
dc exec host dotnet Nosum.Host.dll admin revoke SomeNickname
dc exec host dotnet Nosum.Host.dll admin list
```

The next sign-in of that player has the admin permission level; revoking takes effect at once for an open connection. Both commands are audited
in `admin_audit`:

```sql
SELECT a.created_at, adm.nickname AS admin, a.action, t.nickname AS target, a.detail
FROM admin_audit a
LEFT JOIN players adm ON adm.id = a.admin_id
LEFT JOIN players t   ON t.id = a.target_id
ORDER BY a.created_at DESC LIMIT 50;
```

`action`: 1 kick, 2 mute, 3 ban, 4 unban, 5 grant, 6 role granted, 7 role revoked. The admin intents (`Nosum.Contracts.Admin`, opcodes
`0x1101`–`0x1107`) are sent by a tool or a test client; there is no admin screen yet.

## Rotate keys and secrets

**Server key (Nalix handshake).** The key pair is `certificate.private` / `certificate.public` in the `nalix_data` volume. Rotate it only when
the private file may have leaked, and only with a plan: every pinned client refuses a server with a different key.

1. Ship a client that pins **both** the old and the new key (`Server:PublicKeys:0` and `:1`), so players are not cut off.
2. `dc stop host`
3. `dc run --rm --no-deps --entrypoint sh host -c 'rm -f /data/config/certificate.private /data/config/certificate.public'`
4. `dc start host`; read the new key (section "Read the server key") and update `NOSUM_SERVER_PUBLIC_KEY`.
5. After the clients have updated, drop the old key from the client configuration.

Everyone is disconnected by the restart; login tokens are not affected.

**Tunnel token.** In the dashboard, rotate the connector token of `nosum-api`, update `CLOUDFLARE_TUNNEL_TOKEN`, and run **deploy-server**.
The old token stops working at once.

**Database password.** PostgreSQL reads `POSTGRES_PASSWORD` only when its data volume is created. Changing the secret alone breaks the host.
Change it inside PostgreSQL first (`ALTER ROLE nosum PASSWORD '…';` through `dc exec postgres psql -U nosum nosum`), then update the secret and
run **deploy-server**.

**Cloudflare API token.** Roll it in the dashboard and update `CLOUDFLARE_API_TOKEN`; nothing on the host uses it.
