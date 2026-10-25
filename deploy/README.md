# deploy/

Everything that turns the repository into a running server (docs/adr/ADR-031): container images, the compose file, the reverse proxy, backups
and the placeholder-art gate. How to run and look after it: the [operations runbook](../docs/ops/README.md). Secrets never go in this repository.

| File | What it is |
| :--- | :--- |
| `compose.yaml` | PostgreSQL 16 + the host + Caddy + the nightly backup, one network, five volumes |
| `.env.example` | the names of the settings (`deploy/.env` holds the values and is git-ignored) |
| `host.Dockerfile` | `Nosum.Host` on the .NET *runtime* image (no SDK), non-root, health check built in |
| `host-entrypoint.sh` | writes the Nalix settings that depend on the deployment (allowed web origin, trusted proxy) and starts the host |
| `web.Dockerfile` | the AOT-compiled WASM client plus Caddy, as one image |
| `Caddyfile` | TLS, the IP allow-list, the `/ws/` proxy with a readiness check, caching rules for the fingerprinted framework files |
| `access.d/` | extra Caddy rules for one machine (basic auth …), git-ignored except its README |
| `backup/` | `backup.sh` (nightly dump, 14-day retention), `restore.sh`, and `test-restore.sh` which proves both |
| `check-placeholder-art.sh`, `PlaceholderGate.targets` | the gate that refuses a public release build while `assets/PLACEHOLDER_CONTENT` exists |
| `*.Dockerfile.dockerignore` | what each image build may see of the repository |

```bash
cp deploy/.env.example deploy/.env      # fill it in
docker compose -f deploy/compose.yaml --env-file deploy/.env up -d --build
```

Images by hand (from the repository root; `NOSUM_RELEASE` is `private` or `public`):

```bash
docker build -f deploy/host.Dockerfile --build-arg NOSUM_RELEASE=private -t nosum/host .
docker build -f deploy/web.Dockerfile  --build-arg NOSUM_RELEASE=private -t nosum/web .
```

The client is published ahead-of-time compiled (ADR-002 D5); development never does this. Without Docker, for a trial publish:

```bash
dotnet workload install wasm-tools            # once per build machine
dotnet publish wasm/Nosum.Wasm -c Release -p:NosumWebDeploy=true -o <out>
dotnet publish server/Nosum.Host -c Release -o <out>      # the server is published normally (no Native AOT)
```

## The placeholder-art gate

While `assets/PLACEHOLDER_CONTENT` exists the repository holds the Stardew Valley placeholder content ([ADR-007](../docs/adr/ADR-007-placeholder-content-16px.md)).
A **public** release build is then refused in three places, so none can be skipped by accident:

- `deploy/check-placeholder-art.sh public` (exit 1), which both Dockerfiles run first;
- the MSBuild target in `PlaceholderGate.targets` (imported by `server/Directory.Build.targets` and `wasm/Directory.Build.targets`):
  `dotnet publish -p:NosumRelease=public` fails before it copies anything;
- the compose file, which will not start Caddy without `NOSUM_ALLOW_IPS`: until M6 the deployment is an IP allow-list for the team.

Milestone M6 deletes the marker as its last step. Tests: `tests/Nosum.IntegrationTests/Ops/DeployScriptTests.cs`.
