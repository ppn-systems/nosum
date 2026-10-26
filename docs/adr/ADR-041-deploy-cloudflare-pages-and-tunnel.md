# ADR-041 — Deployment: web client on Cloudflare Pages, server behind a Cloudflare Tunnel

**Status:** Accepted (coordinator, 2026-10-26). Amends ADR-031 D2 (TLS and routing), D9 (the access control of the private phase) and the compose
layout of D1. Builds on ADR-002 D5 (AOT web publish), ADR-003 (WebSocket server), ADR-031 (images, health, backups, gate), ADR-032 D1 (server key pinning).

## Context

ADR-031 deploys everything to one host: Caddy terminates TLS, serves the WASM client and proxies `/ws/`, and an IP allow-list keeps the
deployment private until milestone M6. That needs a public IP, open ports 80 and 443 on the host, and certificate management on the host.

The sibling project atmosos already runs this pattern in production: the static site on Cloudflare Pages, the server reached through a
Cloudflare Tunnel, deployed by SSH from a manual workflow. Nosum adopts the same pattern for its one production host. The art is own
(ADR-040), so the IP allow-list has no reason to remain.

## Decision

- **D1 — Where things run.** The web client (the published `wwwroot`) is a Cloudflare Pages project named `nosum`. The server (PostgreSQL, the
  Nosum host, the tunnel connector, the nightly backup) runs on one host under Docker Compose. Nothing else is deployed.
- **D2 — Compose.** `deploy/compose.yaml` defines `postgres` (no published port), `host` (the GHCR image tagged with the commit SHA;
  game WebSocket published to `127.0.0.1:57300` only; the ops port 57301 not published, bound to localhost inside the container),
  `cloudflared` (`cloudflare/cloudflared`, `tunnel run`, token from the environment, no published port) and `backup`. Caddy and the `web`
  image are removed. The host's only inbound port is SSH.
- **D3 — Tunnel.** A remotely managed tunnel `nosum-api` routes the public hostname `nosum.ppnsys.com` to `http://host:57300`, the compose
  service name on the compose network. WebSockets are on by default for the tunnel. Cloudflare closes a WebSocket with no traffic after about
  100 s, so the client sends a Nalix keep-alive PING every 25 s (`GameSocketOptions.KeepAliveInterval`; Nalix sends none by default).
- **D4 — Client address.** The published `wwwroot/appsettings.json` carries `Server:Url` (`wss://nosum.ppnsys.com/ws/`), the pinned server
  key(s) (`Server:PublicKeys:0`) and `Server:RequirePin: true`. `GameSocketOptions.ApplyBootConfiguration` reads `Server:Url` and replaces the
  address the page gave; without it the client keeps the address of the page (the development behaviour). The server accepts WebSocket
  handshakes only from `NOSUM_ORIGIN`, the Pages origin (ADR-003).
- **D5 — Pages deploy.** `.github/workflows/deploy-web.yml` publishes the WASM project with `NosumRelease=public` and deploys `wwwroot` with
  `cloudflare/wrangler-action` (`pages deploy`). `wwwroot/_headers` sets the caching of the fingerprinted framework files and the security
  headers that Caddy set before. There is no SPA rewrite: the client has no routes beyond `/` and `/not-found`.
- **D6 — Server deploy.** `.github/workflows/deploy-server.yml` builds `deploy/host.Dockerfile` and pushes it to GHCR tagged with the commit SHA,
  copies `compose.yaml` and `backup/` to the host over SSH, writes `deploy/.env` (mode 600, values passed as environment variables and never
  echoed), pulls and starts the services, and gates on the host's readiness (`dotnet Nosum.Host.dll healthcheck`, which asks `/readyz`). A failed
  gate restores the previous tag (kept in `NOSUM_PREVIOUS_TAG`) and fails the run. Old images are pruned.
- **D7 — Triggers.** Both deploy workflows are `workflow_dispatch` only, run in the `production` environment, and the owner merges nothing
  automatically. A deploy is a decision.
- **D8 — Placeholder gate.** Both workflows start with `deploy/check-placeholder-art.sh public`, and the image build and the MSBuild publish
  repeat it (ADR-031 D9). The gate is not weakened: while `assets/PLACEHOLDER_CONTENT` exists, both deploys are refused. The marker is removed
  by milestone M6 (ADR-040), not by this decision.
- **D9 — Retired.** `NOSUM_ALLOW_IPS`, `deploy/access.d/`, the Caddy configuration and `deploy/web.Dockerfile` are removed.
- **D10 — Secrets.** Secrets are GitHub environment secrets (`production`): `PROD_HOST`, `PROD_SSH_USER`, `PROD_SSH_KEY`,
  `CLOUDFLARE_TUNNEL_TOKEN`, `POSTGRES_PASSWORD`, `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` and, optionally, the `NOSUM_SMTP_*` values.
  The server public key is a repository variable (`NOSUM_SERVER_PUBLIC_KEY`): a public key is not a secret. No secret enters the repository.
  Names and where each comes from: `docs/ops/README.md`.
- **D11 — Server identity.** The Nalix server key stays on the `nalix_data` volume. Pinned clients keep trusting it across deploys.

## Alternatives considered

- **Caddy on the host, with the IP allow-list kept (ADR-031 as it was).** Needs a public IP, ports 80 and 443 and certificate management on the
  host, and it leaves the origin reachable from the internet. Rejected: the tunnel removes all three.
- **The whole game on Pages or Workers.** Pages serves static files only; the game server is a long-lived WebSocket process with PostgreSQL. Rejected.
- **Staging environment.** The owner asked for one production host. Rejected for now; a second host would repeat D6 with its own secrets.
- **Deploy on push.** A deploy is a decision (D7). Rejected.

## Consequences

- The host needs no public ports and no certificates. The server is reachable only through Cloudflare, and the tunnel's token is the credential
  that connects the host to it. Losing the token stops the tunnel; rotating it is in `docs/ops/README.md`.
- The client's address is data (`Server:Url`), so a change of host is a redeploy of the page, not a code change.
- Cloudflare sits between the player and the server. The server sees the player's address in `X-Forwarded-For` through the connector; the
  `NOSUM_TRUSTED_PROXIES` range is the compose network, the only path to the host. [Assumption] Cloudflare's connector sets `X-Forwarded-For`
  to the visitor's address; if it does not, rate limits group all players under one address. Check the metric
  `nosum_intents_rejected_total` and a test request through the tunnel after the first deploy.
- The SSH host key is not pinned by the workflow. [Assumption] The runner trusts the host named by `PROD_HOST`; a hostile network could impersonate
  it and receive the deploy secrets. Pinning the host key is the next hardening step.
- A deploy cannot run while the placeholder marker exists (D8). Until M6 the production deploy was blocked by design; M6 removed the marker (ADR-040 phase 4).

## Revisit when

- Milestone M6 removes `assets/PLACEHOLDER_CONTENT` (done, 2026-10-26). The gate passes; a public deploy still needs the decision on the git history in `docs/ops/README.md`.
- The owner wants a second host or a staging environment.
- Cloudflare changes the tunnel's idle timeout, or the connector stops setting `X-Forwarded-For`.
