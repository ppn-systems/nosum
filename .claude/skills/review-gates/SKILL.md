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
| `wasm/` | `dotnet build wasm/Nosum.Wasm.slnx` |
| `bench/` | `dotnet build bench/Nosum.Bench.slnx` |
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
