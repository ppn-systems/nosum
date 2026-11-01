---
name: testing-style
description: Use when writing, changing or reviewing tests under tests/ — xUnit + FluentAssertions, MethodUnderTest_Scenario_ExpectedBehavior naming, unit vs integration placement, deterministic time, free-port integration tests.
---

# Testing style

## Non-negotiables

- **xUnit + FluentAssertions.** No other test framework or mocking library without asking.
- **Name:** `MethodUnderTest_Scenario_ExpectedBehavior`
  (`Advance_LongPause_IsClampedToMaxFrame`). Class: `<TypeUnderTest>Tests`, `sealed`.
- **Arrange / act / assert** separated by blank lines; one behavior per test.
- **Deterministic.** Time and randomness are passed in; no `Thread.Sleep`, no wall-clock
  assertions, no dependence on test order.
- **Placement mirrors the source:** `tests/Nosum.UnitTests/Engine/`, `.../Gameplay/`,
  `.../Contracts/`; server-over-the-wire tests in `tests/Nosum.IntegrationTests/Server/`.
- **Integration tests bind a free port** (`TcpListener` on port 0, as in
  `tests/Nosum.IntegrationTests/Server/HostWebSocketTests.cs`), build the app through
  `Program.BuildApplication`, and always deactivate it in `finally`.
- Every game rule tests its accepted path and each refusal (`gameplay-rules`).
- Never skip, disable or delete a failing test to get green.

## Run

```bash
dotnet test tests/Nosum.Tests.slnx
dotnet test tests/Nosum.UnitTests --filter FullyQualifiedName~FixedTimestep
```

Browser checks (no test project; plain Node, Chromium preinstalled — never run `playwright install`):

```bash
# Unit: label placement, and every browser module of the render backend loads as an ES module (no link or parse error),
# every ALL_CAPS constant it uses is declared, and the host's scripts parse (tests/browser/modules, docs/adr/ADR-104).
node --test tests/browser/label-fit/*.mjs tests/browser/modules/*.mjs

# Smoke: the published client boots against the Development server, draws frames, and raises no page or console error.
# Needs PostgreSQL with the role and database `NosumDatabase.DevelopmentConnectionString` names (nosum, nosum_dev), the server and a static server for the publish output.
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -o artifacts/smoke-pub
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host -c Release &
node bench/wasm-perf/serve.cjs artifacts/smoke-pub/wwwroot 5080 &
node tests/browser/smoke/boot-smoke.cjs http://localhost:5080/ --timeout-ms 120000
```

The smoke exits 1 on a page error, a console error (except a resource 404), no frames, or a flat canvas. CI runs no tests today
(`.github/workflows` holds the deploy and security workflows), so these run by hand before a PR that touches `wasm/` or the render backend.

