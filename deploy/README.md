# deploy/

Everything that turns the repository into a running production service (docs/adr/ADR-041, ADR-031): the server image, the compose file,
the nightly backup. The web client is published to Cloudflare Pages by the workflow `deploy-web`.
How to set up, deploy, roll back and look after it: the [operations runbook](../docs/ops/README.md). Secrets never go in this repository.

## Before the first deploy: what the owner sets up by hand

Do these once, in this order. The runbook has the details of each step.

1. **Cloudflare Tunnel** `nosum-api` (Zero Trust → Networks → Tunnels; the name is free, only the token and the public hostname matter).
   Copy its token into the `CLOUDFLARE_TUNNEL_TOKEN` secret.
   Public hostname `nosum.ppnsys.com` → service HTTP → `host:57300`.
2. **Cloudflare Pages** project `nosum` (Direct Upload, production branch `main`). Note its `https://nosum.pages.dev` address.
3. **GitHub secrets** (environment `production`): `PROD_HOST`, `PROD_SSH_USER`, `PROD_SSH_KEY`, `CLOUDFLARE_TUNNEL_TOKEN`,
   `POSTGRES_PASSWORD`, `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`.
4. **GitHub variables**: `NOSUM_ORIGIN` (the Pages origin), `NOSUM_SERVER_PUBLIC_KEY` (read it from the host after the first start, see the runbook).
5. **One-time server bootstrap** (as the deploy user):

   ```bash
   curl -fsSL https://get.docker.com | sudo sh          # Docker Engine with the Compose plugin
   sudo usermod -aG docker "$USER"
   sudo mkdir -p /opt/nosum/backup && sudo chown -R "$USER" /opt/nosum
   sudo ufw allow OpenSSH && sudo ufw enable             # SSH is the only inbound port
   ```

   Append the deploy key's public half to `~/.ssh/authorized_keys`; its private half goes into `PROD_SSH_KEY`.

**Open items to settle before the first run:**

- `NOSUM_SERVER_PUBLIC_KEY` must be set from the host's key before `deploy-web` runs.
- The workflow does not pin the SSH host key (ADR-041 Consequences). Accept this risk or add host-key pinning first.
- After the first deploy, confirm the tunnel passes `X-Forwarded-For` (ADR-041 Consequences), so players are not all one address.

| File | What it is |
| :--- | :--- |
| `compose.yaml` | production on one host: PostgreSQL 16, the Nosum host (GHCR image), the Cloudflare Tunnel connector, the nightly backup |
| `.env.example` | the names of the settings (`deploy/.env` holds the values on the host and is git-ignored) |
| `host.Dockerfile` | `Nosum.Host` on the .NET *runtime* image (no SDK), non-root, health check built in |
| `host-entrypoint.sh` | writes the Nalix settings that depend on the deployment (allowed web origin, trusted proxy) and starts the host |
| `backup/` | `backup.sh` (nightly dump, 14-day retention), `restore.sh`, and `test-restore.sh` which proves both |
| `host.Dockerfile.dockerignore` | what the host image build may see of the repository |

The workflows that use these files: `.github/workflows/deploy-server.yml` (image, copy, compose on the host, readiness gate, rollback) and
`.github/workflows/deploy-web.yml` (the client to Pages). Both are manual.

## Sign-in settings (ADR-069)

The client ids are public values, not secrets, and they are kept out of the code. The owner sets them as GitHub Actions variables `NOSUM_GOOGLE_CLIENT_ID`, `NOSUM_MICROSOFT_CLIENT_ID` and `NOSUM_MICROSOFT_TENANT`. They must match in two places:

- **Server:** `NOSUM_GOOGLE_CLIENT_ID`, `NOSUM_MICROSOFT_CLIENT_ID`, `NOSUM_MICROSOFT_TENANT` in `deploy/.env` (`common` is the tenant).
- **Web client:** the `ExternalSignIn` section of `wasm/Nosum.Wasm/wwwroot/appsettings.json`, which holds the same two ids.

The web client's `appsettings.json` ships with empty ids. Until the deploy-web workflow writes the variables into the published `appsettings.json`, the provider buttons stay disabled in the deployed client.

Register these in the owner's OAuth apps:

| Provider | Setting | Value |
| :--- | :--- | :--- |
| Google (Google Cloud, OAuth client) | Authorized JavaScript origins | the web origin (`NOSUM_ORIGIN`, for example `https://nosum.pages.dev` or the custom domain) and `http://localhost:<dev port>` for a local run |
| Microsoft (Entra app, SPA platform) | Redirect URI | `<web origin>/msal-redirect.html`, for example `https://nosum.pages.dev/msal-redirect.html`, and `http://localhost:<dev port>/msal-redirect.html` |

Google sign-in uses Google Identity Services, which needs origins, not redirect URIs. The Microsoft popup returns to `msal-redirect.html` on the same origin the player loads.

## Building the host image by hand

From the repository root:

```bash
docker build -f deploy/host.Dockerfile -t nosum/host:local .
```

A trial stack on one machine uses the compose file with the local image. Compose pulls `ghcr.io/ppn-systems/nosum-host:<NOSUM_TAG>`, so tag
the build with that name (`docker tag nosum/host:local ghcr.io/ppn-systems/nosum-host:local`), set `NOSUM_TAG=local`, `NOSUM_ORIGIN` and the
other values of `.env.example` in `deploy/.env`, and start it with `docker compose --env-file deploy/.env -f deploy/compose.yaml up -d --no-build`.
The production procedure is in the runbook.

The client is published ahead-of-time compiled (ADR-002 D5), which needs the wasm-tools workload:

```bash
dotnet workload install wasm-tools                    # once per build machine
dotnet publish wasm/Nosum.Wasm -c Release -p:NosumWebDeploy=true -o <out>
```

Development never needs any of this: `dotnet run --project server/Nosum.Host` and `dotnet run --project wasm/Nosum.Wasm` are enough.
