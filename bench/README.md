# bench/

Benchmarks that back performance decisions with numbers.

| Project | Measures |
| :--- | :--- |
| `Nosum.Bench.Interop` | Per-frame JS interop cost: `IJSRuntime` per sprite vs one batched `[JSImport]` call per frame. Its result decides [ADR-002](../docs/adr/ADR-002-batched-render-interop.md). Browser project: same WASM build settings as `wasm/` (`Directory.Build.props`). |
| `Nosum.Bench.Load` | Headless load test of a running server ([ADR-031](../docs/adr/ADR-031-deployment-and-operations.md) D8): N guests over real WebSockets that walk through the exits to public maps, farm and chat; the report puts what the clients saw next to the server's own snapshot tick time and bandwidth from its `/metrics`. A console program (it ignores the WASM settings). |

`Nosum.Bench.Interop`: only the project shell exists; the benchmark itself is the next step.

```bash
dotnet run --project bench/Nosum.Bench.Interop -c Release
```

## Nosum.Bench.Load

The budget for milestone M5: **200 clients, snapshot tick p99 under 20 ms, on a 4-core server.**

```bash
# the server under test: not Production, and with the per-address rate limits off, because every bot shares one address
DOTNET_ENVIRONMENT=Staging NOSUM_LOAD_TEST=1 NOSUM_OPS_HOST=+ dotnet run --project server/Nosum.Host -c Release

dotnet run --project bench/Nosum.Bench.Load -c Release -- --clients 200 --ramp 30 --duration 120 --host <server>
```

Options: `--clients`, `--ramp` (seconds to connect them all), `--duration` (steady phase), `--travellers` (share that walks to a public map, default 0.6; the rest farm
at home), `--farm-every`, `--metrics <url>` (default `http://<host>:57301/metrics`, `off` to skip), `--max-tick-p99` (default 20), `--host/--port/--path/--tls`, `--seed`,
`--maps <dir>` (the `.tmx` files, default `assets/Maps`). Exit code 0 = budget met, 1 = missed, 2 = bad arguments.

How the bots work: each is a guest (`StartGuest`), joins like the browser client and reports its feet position 10 times a second, moving with the server's own `Walker` on the
same walk grids the server builds from the same maps. Travellers plan a route over the maps' exits and a path over the tiles (`WorldMaps`, `TilePath`, `Navigator`), push onto the exit tile
and are moved by the server; a move refused as blocked closes that tile on the bot's own copy of the grid (a tree or a placed object the map file does not show) and it goes around. Farmers
walk to soil and till, plant and water it with the starter kit. All chat now and then. The thinking is network-free and tested against the real maps and the server's movement rules
(`tests/Nosum.UnitTests/Bench/NavigatorTests.cs`). The report's tick numbers are bucket upper bounds read from the metrics before and after the steady phase, so they never understate.

The bots create guest accounts in the server's database (idle guests are deleted after 30 days): never aim the load test at a server with real players. Running the bots on the same
machine as the server shares its cores, so a single-machine number is pessimistic for the server and optimistic for the network.
