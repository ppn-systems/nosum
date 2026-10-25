# deploy/

Deployment configuration for the server (`server/Nosum.Host`) and the static WASM client
(`wasm/Nosum.Wasm` publish output): container image, reverse proxy with TLS in front of the
WebSocket listener, environment configuration.

Nothing is deployable yet, so this folder holds no configuration. Secrets never go in this
repository.
