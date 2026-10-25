---
name: csharp-style
description: Use when writing or reviewing any C# in this repo — engine, game, contracts, server, wasm code-behind, tests — or when the build fails on an analyzer rule. Naming, sealed/readonly/nullable, async, allocation rules, analyzer fixes.
---

# C# / .NET style

Razor markup: `blazor-ui`. Frame-loop specifics: `engine-core`, `render-interop`.

## Non-negotiables

- Settings come from the root `Directory.Build.props`: C# preview on `net10.0`, nullable on,
  `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, **no implicit usings** (write every
  `using`). Never `#nullable disable`; never weaken these per project.
- Library projects are `IsAotCompatible`/`IsTrimmable`: no `Activator.CreateInstance`,
  runtime expression building, or reflection-based `System.Text.Json` without a
  source-generated context.
- Zero warnings. Fix the analyzer finding; suppress only with a
  `[SuppressMessage(..., Justification = "...")]` that states a real reason.
- LF line endings, final newline, no trailing whitespace (`.editorconfig`, `.gitattributes`).
- English XML docs on every public API.
- Never log secrets or session tokens.

## Language

- File-scoped namespace matching the folder (IDE0130 is an error under `contracts/`).
- `sealed` classes unless designed for inheritance; `readonly struct` for small immutable values;
  `readonly` fields where possible.
- Explicit types; `var` only when the right-hand side names the type.
- Guard clauses: `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIf*`.
  `is null` / `is not null`, `string.Empty`.
- Header line on every `.cs` file: `// Copyright (c) 2026 PPN Systems. All rights reserved.`
- Split a class into partial files (`.cs`, `.Types.cs`, …) only past ~800 lines.

## Async

- Forward `CancellationToken`; `ConfigureAwait(false)` everywhere outside Razor components
  (CA2007 is on — `await using (x.ConfigureAwait(false))` for async disposal).
- `ValueTask` only on hot or usually-synchronous paths.
- Nothing async inside the frame loop (`engine-core`).

## Allocation

| Path | Rule |
|:--|:--|
| Frame loop (`Nosum.Core` update, `Nosum.Rendering`, `Nosum.Rendering.Web`) | No allocation per frame: no LINQ, no closures, no boxing, no `new` of reference types; preallocate and reuse |
| Packet handlers | Pooled packets (`nalix-handlers`); no LINQ |
| UI, startup, persistence | Readable code is correct; LINQ and async are fine |

## Naming

| Element | Convention | Example |
|:--|:--|:--|
| Types, methods, properties, constants | PascalCase | `FixedTimestep`, `StepSeconds` |
| Parameters, locals | camelCase | `frameSeconds` |
| Private instance fields | `_camelCase` | `_accumulator` |
| Private static fields | `s_camelCase` | `s_pool` |
| Interfaces | `I` + PascalCase | `IRenderBackend` |

Packets: precise domain names (`PlantSeed`, `PlotState`), no `*Packet` suffix.

## Analyzer fixes seen here

- CA2007 — add `.ConfigureAwait(false)`.
- CA1859 — return the concrete type the analyzer names (e.g. `Task<JSObject>`).
- CA1416 — browser-only API: mark the type `[SupportedOSPlatform("browser")]` and keep it in a
  project with `<SupportedPlatform Include="browser" />`.
