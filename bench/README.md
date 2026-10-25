# bench/

Browser benchmarks that back performance decisions with numbers. Projects here use the same
WASM build settings as `wasm/` (`Directory.Build.props`), so results apply to the game client.

| Project | Measures |
| :--- | :--- |
| `Nosum.Bench.Interop` | Per-frame JS interop cost: `IJSRuntime` per sprite vs one batched `[JSImport]` call per frame. Its result decides [ADR-002](../docs/adr/ADR-002-batched-render-interop.md). |

Only the project shell exists; the benchmark itself is the next step.

```bash
dotnet run --project bench/Nosum.Bench.Interop -c Release
```
