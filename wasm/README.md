# wasm/

The Nosum browser client, Blazor WebAssembly. Blazor renders the UI overlay (inventory, shop,
chat); the game world is drawn on a `<canvas>` by the engine
([ADR-002](../docs/adr/ADR-002-batched-render-interop.md)).

| Project | Holds |
| :--- | :--- |
| `Nosum.Wasm` | Blazor host: `Program.cs`, `App.razor`, pages by domain, `wwwroot/` |
| `Nosum.Wasm.ViewModels` | MVVM view models, by domain; no `.razor` files |
| `Nosum.Wasm.Socket` | The single Nalix `WebSocketSession` (connect, handshake, resume, requests, pushes) |

`Directory.Build.props` here turns AOT off and trimming on, and keeps `.pdb`/`.xml` out of the
published bundle (everything in it is downloaded by every player).

Skills: [`blazor-ui`](../.claude/skills/blazor-ui/SKILL.md),
[`render-interop`](../.claude/skills/render-interop/SKILL.md),
[`nalix-client-sdk`](../.claude/skills/nalix-client-sdk/SKILL.md).

Playable now: `Pages/Home.razor` starts `Game/GameLoop`, which loads `assets/Maps/Farm.tmx` and the
farmer sheets (linked from `assets/` in the `.csproj`), then runs `Game/FarmScene`: walk with
WASD or the arrow keys, the camera follows, layers draw as Back → Buildings → farmer → Front.
`TrimmerRoots.xml` keeps this assembly whole because Razor components are found by reflection.

```bash
dotnet build wasm/Nosum.Wasm.slnx
dotnet run --project wasm/Nosum.Wasm
```
