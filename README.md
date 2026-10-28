# Nosum

A browser-based multiplayer farming game in 3/4 top-down pixel art. The client is Blazor WebAssembly with its own
2D engine; the server is a .NET 10 realtime server on the Nalix networking framework.
Client and server talk over one Nalix WebSocket channel and share the same C# contracts.

> **Status**: Full multiplayer MMORPG farming & dungeon-crawler. Real-time WebSocket networking,
> Canvas2D 60 FPS rendering, server-authoritative combat, HP/MP vitals, 8 monster species + 4 dungeon floor bosses,
> arcane magic projectiles, craftable armor defense mitigation, defeat death poofs, status effects (regen/poison),
> co-op party proximity Combat XP sharing, and synthesized 16-bit sound effects. Art and sound assets are 100% CC0/CC-BY
> repacked ([ADR-045](docs/adr/ADR-045-mmorpg-combat-hp-mp-and-monster-assets.md) through [ADR-054](docs/adr/ADR-054-mmorpg-expansion-system-audit-and-completion.md)).

## Layout

| Folder | What it holds | Solution |
| :--- | :--- | :--- |
| [`engine/`](engine/) | 2D engine: loop, scene, rendering front end, browser render backend | `Nosum.Engine.slnx` |
| [`contracts/`](contracts/) | Wire contracts shared by server and client (opcodes, packets) | `Nosum.Contracts.slnx` |
| [`gameplay/`](gameplay/) | Farm rules shared by server and client | `Nosum.Gameplay.slnx` |
| [`server/`](server/) | Realtime server (Nalix) | `Nosum.slnx` |
| [`wasm/`](wasm/) | Blazor WebAssembly client | `Nosum.Wasm.slnx` |
| [`tests/`](tests/) | Unit and integration tests | `Nosum.Tests.slnx` |
| [`bench/`](bench/) | Browser and server load performance benchmarks | `Nosum.Bench.slnx` |
| [`art/`](art/) | Tiled `.tmx` maps (`art/own/Maps/`), map layouts (`art/own/source/`) and raw CC0/CC-BY source packs (`art/source/`; ADR-040) | — |
| [`assets/`](assets/) | The runtime sprite sheets and synthesized audio the game loads (own or CC0, 16 px; ADR-040) | — |
| [`tools/`](tools/) | Developer tools (`Nosum.AtlasPacker`, `Nosum.MapGen`, `tools/art_repack/` Python asset pipeline, audio synthesizers) | `Nosum.Tools.slnx` |
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
