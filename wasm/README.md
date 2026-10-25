# wasm/

The Nosum browser client, Blazor WebAssembly. Blazor renders the UI overlay (inventory, shop,
chat); the game world is drawn on a `<canvas>` by the engine
([ADR-002](../docs/adr/ADR-002-batched-render-interop.md)).

| Project | Holds |
| :--- | :--- |
| `Nosum.Wasm` | Blazor host: `Program.cs`, `App.razor`, pages by domain, `wwwroot/` |
| `Nosum.Wasm.ViewModels` | MVVM view models, by domain; no `.razor` files |
| `Nosum.Wasm.Socket` | The single Nalix `WebSocketSession`: `GameSocket`/`IGameSocket` (connect, handshake, reconnect with backoff, typed requests, push subscriptions that survive reconnects) and `Player/PlayerChannel` (join, 10 Hz move reports, spawn and correction hand-off to the frame loop) |

`Directory.Build.props` here turns AOT off and trimming on, and keeps `.pdb`/`.xml` out of the
published bundle (everything in it is downloaded by every player).

Skills: [`blazor-ui`](../.claude/skills/blazor-ui/SKILL.md),
[`render-interop`](../.claude/skills/render-interop/SKILL.md),
[`nalix-client-sdk`](../.claude/skills/nalix-client-sdk/SKILL.md).

Account UI: `Components/Account/AccountForm.razor` is a reusable login/register form bound to
`AccountFormViewModel` (`Nosum.Wasm.ViewModels/Account`). Its text is in
`Nosum.Wasm.ViewModels/Resources/Language.resx` (Vietnamese) and `Language.en.resx`. `Pages/Home.razor`
shows it first; the game starts after sign-in (the canvas listens to the whole window's keys).
`GameSocketAccountClient` implements `IAccountClient` over `IGameSocket`; `AccountSessionKeeper`
applies the account language on sign-in and signs the connection in again (resume token, kept in
memory only) after every reconnect.

Multiplayer ([ADR-008](../docs/adr/ADR-008-snapshot-movement-sync.md)): the server address lives in
`GameSocketOptions` (same host as the page, port 57300, `/ws/`; `ForPageHost`). `Program.cs` creates
one `GameSocket` and one `PlayerChannel`. `Game/FarmScene` predicts the local farmer, hands its state
to `PlayerChannel.Report` (no await, no allocation), snaps back on a `MoveCorrection`, and adds the
other farmers (`Game/RemotePlayers`: a `SnapshotBuffer` from `Nosum.Core.Interpolation`, drawn 200 ms
in the past) to the same `WorldSpriteBatch` so they y-sort with the local farmer and the buildings.
Without a server the farm still plays offline. Remote farmers use a look derived from their player id
until the wire carries looks (ADR-006). `ConnectionBadge` shows the connection state in the current language.

Playable now: `Pages/Home.razor` starts `Game/GameLoop`, which loads `assets/Maps/Farm.tmx` and the
farmer sheets (linked from `assets/` in the `.csproj`), then runs `Game/FarmScene`: walk with
WASD or the arrow keys, the camera follows, layers draw as Back → Buildings → depth-sorted world (farmer, farmhouse, greenhouse, shipping bin from `Game/FarmProps.cs`) → Front → roof parts. Animated tiles and a water ripple run from the scene clock; building sheets are linked from `assets/Buildings/` in the `.csproj`.
`TrimmerRoots.xml` keeps this assembly whole because Razor components are found by reflection.

```bash
dotnet build wasm/Nosum.Wasm.slnx
dotnet run --project wasm/Nosum.Wasm
```
