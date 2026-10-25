---
name: repo-navigation
description: Use to locate code, decide where a new file or project belongs, or find the packet/handler/rule/page for a feature — engine/contracts/gameplay/server/wasm/tests/bench layout, the no-root-solution rule, allowed project references.
---

# Repository navigation

## Non-negotiables

- **Never create a root `.sln`/`.slnx`.** List the solutions with `git ls-files '*.slnx'`. A
  project needing one from another area adds a `<ProjectReference>`; it is not added to the
  other area's `.slnx`.
- **Only the references in `ARCHITECTURE.md` §Project dependencies are allowed.** The two that
  break most easily: engine projects referencing Blazor/JS/Nalix, and `Nosum.Handlers`
  referencing `Nosum.Infrastructure`.
- A new area folder gets a `README.md`; a new project goes into its area's `.slnx` in the same
  commit.
- `docs/adr/` is authoritative for decisions.

## Where does it go?

| You are adding… | Put it in |
|:--|:--|
| Math, time, scene, tilemap, camera, input, anything a non-farm game could reuse | `engine/Nosum.Core/` |
| Render command, batching, culling | `engine/Nosum.Rendering/` |
| Code that calls the browser for drawing | `engine/Nosum.Rendering.Web/` (+ `wwwroot/*.js`) |
| A rule about plots, crops, items, prices, growth | `gameplay/Nosum.Gameplay/{Domain}/` |
| A packet, opcode, DTO or wire enum | `contracts/Nosum.Contracts/{Domain}/{C2S,S2C}/` |
| A persisted entity | `server/Nosum.Domain/{Domain}/` |
| A service interface / its implementation | `server/Nosum.Abstractions/Services/{Domain}/` / `server/Nosum.Infrastructure/Services/{Domain}/` |
| A packet handler | `server/Nosum.Handlers/{Domain}/` + `MapHandlers` in `server/Nosum.Host/Program.cs` |
| A page / its view model | `wasm/Nosum.Wasm/Pages/{Domain}/` / `wasm/Nosum.Wasm.ViewModels/{Domain}/` |
| Socket wiring, session lifecycle | `wasm/Nosum.Wasm.Socket/` |
| A test | `tests/Nosum.UnitTests/{Area}/` or `tests/Nosum.IntegrationTests/{Area}/` |
| A browser benchmark | `bench/Nosum.Bench.*` |
| Placeholder art and maps | `assets/` — read-only (`assets-pipeline`, ADR-007) |
| A developer tool | `tools/Nosum.*` + `tools/Nosum.Tools.slnx` |

`{Domain}` is a game domain (`Farm`, `Inventory`, `Market`, `Social`, `Player`, `Placeable`), the same name
in every area.

## Finding a feature

1. Opcode: `grep -rn "<Name>" contracts/` → the `*OpCode` class names the domain.
2. Handler: `grep -rn "PacketOpcode(<OpCodeClass>.<Name>" server/Nosum.Handlers`.
3. Rule: `gameplay/Nosum.Gameplay/<Domain>/`.
4. UI: `wasm/Nosum.Wasm/Pages/<Domain>/` and `wasm/Nosum.Wasm.ViewModels/<Domain>/`.
