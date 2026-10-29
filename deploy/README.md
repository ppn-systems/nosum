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
   The sign-in client ids are public values, not secrets, and go in variables too (docs/adr/ADR-069):
   - `NOSUM_GOOGLE_CLIENT_ID`, `NOSUM_MICROSOFT_CLIENT_ID`: an empty one turns that provider's button off. `deploy-web` warns about each empty one.
   - `NOSUM_MICROSOFT_TENANT`: optional, `common` when unset.

   `deploy-web` writes all of them into `wwwroot/appsettings.json` (the `Server` and `ExternalSignIn` sections) on every run. Anything else the
   file should carry must be added to that step, or the next deploy drops it.

   Register the web client's address with each provider, for the production origin `https://farm.ppnsys.com` (and `https://nosum.pages.dev`
   too while that address is used):
   - **Google** (OAuth client of type *Web application*): *Authorized JavaScript origins* `https://farm.ppnsys.com`. Google Identity Services
     returns the credential to the page, so no redirect URI is needed.
   - **Microsoft** (Entra app registration, platform *Single-page application*): redirect URI `https://farm.ppnsys.com/msal-redirect.html`.
     The sign-in popup returns to that exact address (`external-sign-in.js` sends it), which Pages answers with a 308 to `/msal-redirect`;
     register the `.html` form, not the redirected one.
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
