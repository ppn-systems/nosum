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
- **Placement mirrors the source:** `tests/Nosum.UnitTests/Engine/`, `.../Game/`,
  `.../Contracts/`; server-over-the-wire tests in `tests/Nosum.IntegrationTests/Server/`.
- **Integration tests bind a free port** (`TcpListener` on port 0, as in
  `tests/Nosum.IntegrationTests/Server/HostWebSocketTests.cs`), build the app through
  `Program.BuildApplication`, and always deactivate it in `finally`.
- Every game rule tests its accepted path and each refusal (`game-rules`).
- Never skip, disable or delete a failing test to get green.

## Run

```bash
dotnet test tests/Nosum.Tests.slnx
dotnet test tests/Nosum.UnitTests --filter FullyQualifiedName~FixedTimestep
```
