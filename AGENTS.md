# AGENTS.md — Nosum Agent Rules

> The single agent entry point (`CLAUDE.md` only imports this file). **Detail lives in the
> skills under [`.claude/skills/`](.claude/skills/) (other agents: open
> `.claude/skills/<name>/SKILL.md`); load one only when the task touches its area.** Each
> section names the skill that owns the full rule.

## Project identity

| Field | Value |
|:--|:--|
| Product | Nosum — browser-based multiplayer farming game, 3/4 top-down pixel art |
| Stack | C# / .NET 10 · own 2D engine (`engine/`) · Blazor WebAssembly client · Nalix WebSocket server · shared contracts |
| Layout | [`ARCHITECTURE.md`](ARCHITECTURE.md) — areas, project dependencies, frame loop, network path |
| Game pillars | [`docs/design/pillars.md`](docs/design/pillars.md) — co-ops, shared market, server-wide seasons |
| Roadmap | [`docs/design/roadmap.md`](docs/design/roadmap.md) — milestones M1–M6 to a complete game |
| Decisions | [`docs/adr/README.md`](docs/adr/README.md) |

## Before any agent starts work

1. Read the `README.md` of the area you edit and load the skill(s) for it (table at the end).
2. Where a file or project belongs → `repo-navigation`, then `ARCHITECTURE.md`.
3. Architecture question → `docs/adr/README.md` → the ADR. **No architecture or stack change
   without a new ADR.**
4. State assumptions before broad changes; one concern per commit/PR; no refactor beyond the
   request; no new NuGet or npm dependency unless asked.
5. Before commit/PR/"done" → `review-gates`.
6. Cloud sessions: the .NET SDK installs in the background (`.claude/hooks/session-start.sh`) —
   wait for `/tmp/nosum-dotnet-ready` before building.

## Architecture rules (summaries — owner skill in brackets)

- **No root solution.** Each area has its own `.slnx`; cross-area references are
  `<ProjectReference>` only. [`repo-navigation`]
- **The engine is platform-free.** `Nosum.Core` and `Nosum.Rendering` reference no Blazor, JS
  or Nalix code; only `Nosum.Rendering.Web` touches the browser. [`engine-core`]
- **One interop round trip per frame.** Never call JS per sprite or per event; never use
  `IJSRuntime` inside the frame loop. [`render-interop`]
- **Blazor draws UI, not the world.** No `StateHasChanged` per frame; `.razor` files never
  touch the socket. [`blazor-ui`]
- **The server decides.** Clients send intents; the server validates with `Nosum.Gameplay` and
  owns every outcome (money, items, time). [`gameplay-rules`]
- **Wire format is append-only.** Never change an opcode or `[SerializeOrder]` value; never
  reuse a retired opcode. [`nalix-packets`]
- **Handlers never reference `Nosum.Infrastructure`.** [`nalix-handlers`]
- **i18n: Vietnamese default + English.** No display text on the wire (codes only); every
  visible string through `.resx`; game data by key. [`i18n`]

## C# / .NET style

C# preview on .NET 10, nullable on, `TreatWarningsAsErrors`, no implicit usings, file-scoped
namespaces, `sealed` by default, English XML docs on public APIs, LF line endings. Hot paths
(frame loop, packet handling) allocate nothing. Full rules: `csharp-style`.

## Documentation

All docs, skills, ADRs, code comments and XML docs are in **English**. No placeholder docs.
Owner: `docs-style`.

## Skills — load by area

| Working on… | Skill |
|:--|:--|
| Finding code, placing a new file or project | `repo-navigation` |
| Any C# | `csharp-style` |
| `engine/Nosum.Core`, `engine/Nosum.Rendering` | `engine-core` |
| Frame loop, `Nosum.Rendering.Web`, `renderer.js`, anything crossing .NET ↔ JS | `render-interop` |
| Anything under `wasm/` (pages, ViewModels, socket wiring) | `blazor-ui` |
| `gameplay/Nosum.Gameplay`, server-side validation of player actions | `gameplay-rules` |
| Packet class or opcode in `contracts/` | `nalix-packets` |
| Server packet handler | `nalix-handlers` |
| Entity, `DbContext`, migration, accounts, passwords/tokens | `data-model-ef-core` |
| `server/Nosum.Host` entry point, listeners | `nalix-server-hosting` |
| `wasm/Nosum.Wasm.Socket`, client transport | `nalix-client-sdk` |
| Anything under `assets/`, sprite sheets, `.tmx` maps (placeholder content, ADR-007) | `assets-pipeline` |
| Any user-visible text, status codes, game data names, language, formatting | `i18n` |
| Tests | `testing-style` |
| Any markdown, ADRs | `docs-style` |
| Before commit/PR/"done" | `review-gates` |
