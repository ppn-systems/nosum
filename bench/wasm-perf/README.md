# bench/wasm-perf

Browser measurements of the WASM client: load, frame time, allocation and draw calls. Results and method: [`docs/perf/wasm-perf.md`](../../docs/perf/wasm-perf.md).
They are Node scripts that drive a headless Chromium through Playwright (preinstalled in the cloud image), so they are not a .NET project and add no package.

| Script | Measures | Needs |
|:--|:--|:--|
| `serve.cjs <wwwroot> <port>` | Static server for a publish output. Serves the `.br` or `.gz` sibling the publish wrote when the browser accepts it, as the deploy does. | — |
| `load.cjs <wwwroot> <url> [runs] [--json out]` | First load on the wire (encoded bytes, requests), first contentful paint, guest button, time to playable. | Development server on `localhost:57300`, the publish output served on `<url>` |
| `frame.cjs <url?qa=1> [--seconds N] [--step ms] [--shot file] [--json out]` | Frame callback p50/p95/max on the farm (idle, then walking), `drawImage` and `fillRect` per frame, managed allocation per frame. The callback includes a one-pixel readback so the canvas raster is counted. | Tester build (`-p:NosumDevTools=true`), Development server |
| `profile.cjs <url?qa=1> [--seconds N]` | Chromium CPU profile during walking, split by source (.NET runtime, renderer, page). | As `frame.cjs` |
| `canvas-micro.cjs` | Cost of many small `drawImage` calls against a few chunk-sized ones on the same screen area. | Nothing (no server) |

```bash
export PATH=/root/.dotnet:$PATH
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -o out/pub
dotnet publish wasm/Nosum.Wasm/Nosum.Wasm.csproj -c Release -p:NosumDevTools=true -o out/qa
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host -c Release
node bench/wasm-perf/serve.cjs out/pub/wwwroot 5080 &
node bench/wasm-perf/serve.cjs out/qa/wwwroot 5081 &
node bench/wasm-perf/load.cjs out/pub/wwwroot http://localhost:5080/ 5
node bench/wasm-perf/frame.cjs "http://localhost:5081/?qa=1" --seconds 8
```

The frame and load numbers depend on the machine. Compare builds on the same machine, back to back, with the same script.
Never aim these at a server with real players: they sign in guests and teleport them (the same as the UI capture harness, ADR-079).
