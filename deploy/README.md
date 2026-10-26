# deploy/

Everything that turns the repository into a running production service (docs/adr/ADR-041, ADR-031): the server image, the compose file,
the nightly backup and the placeholder-art gate. The web client is published to Cloudflare Pages by the workflow `deploy-web`.
How to set up, deploy, roll back and look after it: the [operations runbook](../docs/ops/README.md). Secrets never go in this repository.

| File | What it is |
| :--- | :--- |
| `compose.yaml` | production on one host: PostgreSQL 16, the Nosum host (GHCR image), the Cloudflare Tunnel connector, the nightly backup |
| `.env.example` | the names of the settings (`deploy/.env` holds the values on the host and is git-ignored) |
| `host.Dockerfile` | `Nosum.Host` on the .NET *runtime* image (no SDK), non-root, health check built in |
| `host-entrypoint.sh` | writes the Nalix settings that depend on the deployment (allowed web origin, trusted proxy) and starts the host |
| `backup/` | `backup.sh` (nightly dump, 14-day retention), `restore.sh`, and `test-restore.sh` which proves both |
| `check-placeholder-art.sh`, `PlaceholderGate.targets` | the gate that refuses a public release build while the placeholder marker exists or the inventory's release check fails |
| `host.Dockerfile.dockerignore` | what the host image build may see of the repository |

The workflows that use these files: `.github/workflows/deploy-server.yml` (image, copy, compose on the host, readiness gate, rollback) and
`.github/workflows/deploy-web.yml` (the client to Pages). Both are manual.

## Building the host image by hand

From the repository root. `NOSUM_RELEASE` is `private` (a trial build) or `public` (refused while the placeholder marker exists):

```bash
docker build -f deploy/host.Dockerfile --build-arg NOSUM_RELEASE=private -t nosum/host:local .
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

## The placeholder-art gate

The repository holds no Stardew Valley placeholder content any more ([ADR-007](../docs/adr/ADR-007-placeholder-content-16px.md), [ADR-040](../docs/adr/ADR-040-own-art-plan.md) phase 4), and its marker `assets/PLACEHOLDER_CONTENT` is gone. A **public** build passes only when `tools/Nosum.AssetInventory --release` reports no placeholder and no broken file, and `assets/` holds nothing unlisted except its README. The gate runs in three places, so none can be skipped by accident:

- `deploy/check-placeholder-art.sh public` (exit 1 when refused), which the deploy workflows, the release gate and the host Dockerfile run first;
- the MSBuild target in `PlaceholderGate.targets` (imported by `server/Directory.Build.targets` and `wasm/Directory.Build.targets`):
  `dotnet publish -p:NosumRelease=public` runs the same script before it copies anything;
- the deploy workflows, which set up .NET and run the script before they build or publish anything.

A **private** build passes with a warning while the marker exists. Tests: `tests/Nosum.IntegrationTests/Ops/DeployScriptTests.cs`.
