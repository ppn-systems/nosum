---
name: review-gates
description: Use before committing, opening a PR, or declaring a change done — which builds and tests to run per area, commit message convention, do-not-merge conditions.
---

# Review gates

## Validate by what changed

| Changed | Run |
|:--|:--|
| Only markdown / skills | nothing |
| `engine/` | `dotnet build engine/Nosum.Engine.slnx` + `dotnet test tests/Nosum.UnitTests` |
| `gameplay/` or `contracts/` | `dotnet build gameplay/Nosum.Gameplay.slnx` / `contracts/Nosum.Contracts.slnx` + `dotnet build server/Nosum.slnx` + `dotnet build wasm/Nosum.Wasm.slnx` + `dotnet test tests/Nosum.Tests.slnx` |
| `server/` | `dotnet build server/Nosum.slnx` + `dotnet test tests/Nosum.Tests.slnx` |
| `wasm/` | `dotnet build wasm/Nosum.Wasm.slnx` + `node --test tests/browser/label-fit/*.mjs tests/browser/modules/*.mjs` |
| `engine/Nosum.Rendering.Web/wwwroot/`, `wasm/Nosum.Wasm/wwwroot/`, the boot path | the node line above + the browser smoke in `testing-style` (published client, Development server) |
| `bench/` | `dotnet build bench/Nosum.Bench.slnx` |
| `tools/` | `dotnet build tools/Nosum.Tools.slnx` + `dotnet test tests/Nosum.Tests.slnx` |
| Root `Directory.*.props`, `global.json` | every solution above + all tests |

Builds are warning-free by construction (`TreatWarningsAsErrors`); a warning is a failure.

## Commits and PRs

- Commit subject: `<type>(<scope>): <subject>` — type `feat|fix|refactor|test|docs|build|chore`,
  scope = area (`engine`, `game`, `contracts`, `server`, `wasm`, `tests`, `bench`, `docs`).
- One concern per commit and per PR; docs-only changes separate from code where practical.
- Never commit `artifacts/`, `bin/`, `obj/`, secrets or keys.

## Do not merge if

- A build or test fails, or a test was skipped/disabled to pass.
- An opcode or `[SerializeOrder]` value changed, or a field was removed/inserted, without a
  `NosumProtocol.Version` bump and a note in the PR (`nalix-packets`).
- An engine project gained a Blazor/JS/Nalix reference, or `Nosum.Handlers` references
  `Nosum.Infrastructure`.
- The frame loop gained an allocation, an `IJSRuntime` call or a per-item interop call.
- A server handler trusts a client-supplied result (coins, items, time) (`gameplay-rules`).
- An architecture change has no ADR.

## Parallel agents

Several agents may work at once, each in its own worktree on its own `feat/*` branch; one
coordinator merges them into the session branch.

- **One owner per hot spot.** At most one running agent may touch each of: a new EF Core
  migration, `Home.razor`/`GameHud` layout, `renderer.js`, `InputEdges`, a given opcode band,
  the `.resx` pair's structure (adding keys is fine for everyone). The brief names the owner.
- **Merge the base before finishing.** Merge the session branch into your branch, resolve
  conflicts there, then run the gates. If both sides added a migration, delete yours and
  regenerate it with `dotnet ef migrations add` on top of the merged snapshot — never merge
  `NosumDbContextModelSnapshot.cs` by hand.
- **Share the machine.** The container has 4 cores and a fixed disk allowance. Build with
  `-m:2`; while iterating run only the tests of the area you changed (`--filter`), and the
  full suite once at the end. Run `dotnet build --no-incremental` once before reporting.
  Kill only processes you started, by PID.
- **Clean up.** The coordinator removes a worktree (`git worktree remove`) as soon as its
  branch is merged; each worktree holds about 2 GB of build output.
