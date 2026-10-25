# Nosum

A browser-based multiplayer farming game. The client is Blazor WebAssembly with its own
2D engine; the server is a .NET 10 realtime server on the Nalix networking framework.
Client and server talk over one Nalix WebSocket channel and share the same C# contracts.

> Status: scaffold. The projects build and the server accepts WebSocket connections; the
> engine, the renderer and the game rules are not implemented yet.

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
| [`assets/`](assets/) | Source pixel art (32 px grid) | — |
| [`deploy/`](deploy/) | Deployment configuration | — |
| [`docs/`](docs/) | Architecture decision records | — |

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
