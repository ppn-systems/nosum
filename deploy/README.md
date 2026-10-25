# deploy/

Deployment configuration for the server (`server/Nosum.Host`) and the static WASM client
(`wasm/Nosum.Wasm` publish output): container image, reverse proxy with TLS in front of the
WebSocket listener, environment configuration.

Nothing is deployable yet, so this folder holds no configuration. Secrets never go in this
repository.

When the web client is deployed, publish it ahead-of-time compiled (ADR-002 D5); development
never does this:

```bash
dotnet workload install wasm-tools            # once per build machine
dotnet publish wasm/Nosum.Wasm -c Release -p:NosumWebDeploy=true -o <out>
```

The server is published normally (no Native AOT).
