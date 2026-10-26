# Nosum

A browser-based multiplayer farming game in 3/4 top-down pixel art. The client is Blazor WebAssembly with its own
2D engine; the server is a .NET 10 realtime server on the Nalix networking framework.
Client and server talk over one Nalix WebSocket channel and share the same C# contracts.

> Status: single-player slice. The client loads the farm map and a layered farmer you can walk
> around (Canvas2D, 60 FPS); the server accepts WebSocket connections but has no game handlers
> yet. Art and maps are Nosum's own or CC0 art; the early content is gone from the repository ([ADR-007](docs/adr/ADR-007-own-art-16px.md), [ADR-040](docs/adr/ADR-040-own-art-plan.md)), though its git history is not (`docs/ops/README.md`).

## Layout

| Folder | What it holds | Solution |
| :--- | :--- | :--- |
| [`engine/`](engine/) | 2D engine: loop, scene, rendering front end, browser render backend | `Nosum.Engine.slnx` |
| [`contracts/`](contracts/) | Wire contracts shared by server and client (opcodes, packets) | `Nosum.Contracts.slnx` |
| [`gameplay/`](gameplay/) | Farm rules shared by server and client | `Nosum.Gameplay.slnx` |
| [`server/`](server/) | Realtime server (Nalix) | `Nosum.slnx` |
| [`wasm/`](wasm/) | Blazor WebAssembly client | `Nosum.Wasm.slnx` |
| [`tests/`](tests/) | Unit and integration tests | `Nosum.Tests.slnx` |
| [`bench/`](bench/) | Browser performance benchmarks | `Nosum.Bench.slnx` |
| [`assets/`](assets/) | The replacement sprite sheets the game loads (own or CC0, 16 px; ADR-040) | — |
| [`tools/`](tools/) | Developer tools (atlas packer) | `Nosum.Tools.slnx` |
| [`deploy/`](deploy/) | Deployment configuration | — |
| [`docs/`](docs/) | Game pillars (`docs/design/pillars.md`), architecture decision records | — |

How the parts fit together: [`ARCHITECTURE.md`](ARCHITECTURE.md). Rules for coding agents:
[`AGENTS.md`](AGENTS.md).

## Build and test

Requires the .NET SDK pinned in [`global.json`](global.json).

```bash
dotnet build engine/Nosum.Engine.slnx
dotnet build server/Nosum.slnx
dotnet build wasm/Nosum.Wasm.slnx
dotnet test tests/Nosum.Tests.slnx
```

There is no root solution on purpose; each area has its own.

## Run

```bash
dotnet run --project server/Nosum.Host      # WebSocket listener on ws://localhost:57300/ws/
dotnet run --project wasm/Nosum.Wasm        # Blazor dev server
```
