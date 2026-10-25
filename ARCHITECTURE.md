# Nosum — Architecture

Directory layout, project boundaries and the two runtime paths (frame loop, network).
Product overview and build commands: [`README.md`](README.md). Agent rules: [`AGENTS.md`](AGENTS.md).
Decisions behind this layout: [`docs/adr/`](docs/adr/README.md).

## Directory layout

```text
nosum/
├── engine/                       # Nosum.Engine.slnx — the 2D engine, reusable, knows nothing about farming
│   ├── Nosum.Core/               # loop (FixedTimestep), time, scene, tilemap, camera, math, normalized input
│   ├── Nosum.Rendering/          # render commands, sprite batching, culling, IRenderBackend
│   └── Nosum.Rendering.Web/      # browser backend: [JSImport] + wwwroot/renderer.js (canvas)
├── contracts/                    # Nosum.Contracts.slnx
│   └── Nosum.Contracts/          # opcodes, Nalix packets, DTOs, enums, NosumProtocol.Version — no logic
├── gameplay/                     # Nosum.Gameplay.slnx
│   └── Nosum.Gameplay/           # rules shared by server and client: the map catalogue, warps and instances (Maps/), walking (Movement/), farm (Farm/), resource nodes and tools (Nodes/), the server calendar and seasons (Calendar/), placeable objects (Placeables/), ground items (GroundItems/), tool tiers (Items/ToolRules), the blacksmith (Blacksmith/) and the mine (Mines/)
├── server/                       # Nosum.slnx
│   ├── Nosum.Domain/             # persisted entities and enums
│   ├── Nosum.Abstractions/       # service interfaces, DTO rows
│   ├── Nosum.Infrastructure/     # persistence and service implementations
│   ├── Nosum.Handlers/           # stateless Nalix packet handlers by opcode
│   └── Nosum.Host/               # composition root, WebSocket listener (:57300 /ws/)
├── wasm/                         # Nosum.Wasm.slnx — own Directory.Build.props (trimmed, no AOT)
│   ├── Nosum.Wasm/               # Blazor host: canvas page + UI overlay
│   ├── Nosum.Wasm.ViewModels/    # MVVM view models (no .razor)
│   ├── Nosum.Wasm.Controls/      # reusable Razor controls and the pixel-art theme (ADR-016, ADR-017): the game menu, panel frame, button, input, item grid, dialogs, hotbar, HUD dock layout, help, arrange panel, market/wardrobe/trade/co-op panels
│   └── Nosum.Wasm.Socket/        # the single Nalix WebSocketSession
├── tests/                        # Nosum.Tests.slnx — Nosum.UnitTests, Nosum.IntegrationTests
├── bench/                        # Nosum.Bench.slnx — browser benchmarks (same WASM settings as wasm/)
├── assets/                       # placeholder content (unpacked Stardew Valley, 16 px, Tiled maps) — ADR-007, never shipped
├── tools/                        # Nosum.Tools.slnx — developer tools (Nosum.AtlasPacker); only area allowed an image package
├── deploy/                       # deployment configuration
├── docs/                         # design/pillars.md (game identity) · adr/ (architecture decisions)
└── Directory.Build.props · Directory.Packages.props · Version.props · global.json
```

Every area folder has its own `README.md` and, when it holds .NET projects, its own `.slnx`.
An area adds a `Directory.Build.props` only when its build settings differ from the root
(`wasm/`, `bench/`, `tests/`, `tools/`); it imports the root one first.

## No root solution

There is no root `.sln`/`.slnx`. A project that needs one from another area adds a
`<ProjectReference>`; it does not add that project to its own `.slnx`. MSBuild still builds
the whole graph. Build output from every area goes to `artifacts/` at the repo root
(`ArtifactsPath` in the root `Directory.Build.props`).

## Project dependencies

Arrows point at what a project may reference. Anything not listed is forbidden.

```text
Nosum.Core              → (nothing)
Nosum.Rendering         → Core
Nosum.Rendering.Web     → Rendering
Nosum.Contracts         → Nalix.Abstractions, Nalix.Codec
Nosum.Gameplay          → Core
Nosum.Domain            → Gameplay, Contracts                 (Contracts for shared enums such as GameLanguage)
Nosum.Abstractions      → Domain, Contracts
Nosum.Infrastructure    → Abstractions, Nalix.Runtime          (groups, sending to connections)
Nosum.Handlers          → Abstractions, Nalix.Runtime          (never Infrastructure)
Nosum.Host              → Handlers, Infrastructure, Nalix.Hosting
Nosum.Wasm.Socket       → Contracts, Nalix.SDK
Nosum.Wasm.ViewModels   → Wasm.Socket, Gameplay                (catalogue keys: item, crop and quest ids to localization keys)
Nosum.Wasm.Controls     → Wasm.ViewModels                      (reusable Razor controls; no socket, no text of their own)
Nosum.Wasm              → Wasm.Controls, Wasm.ViewModels, Wasm.Socket, Contracts, Rendering.Web, Gameplay   (Game/ code, not .razor, uses the socket for the frame loop)
Nosum.AtlasPacker       → SkiaSharp                            (tools/ only; nothing references it)
```

Two boundaries matter most:

- **The engine (`Nosum.Core`, `Nosum.Rendering`) references no Blazor, JS or Nalix code.**
  Only `Nosum.Rendering.Web` touches the browser. This keeps the engine unit-testable and the
  backend replaceable ([ADR-001](docs/adr/ADR-001-separate-engine.md)).
- **`Nosum.Gameplay` is pure rules.** The server runs it to decide outcomes; the client runs the
  same code only to predict and display them ([ADR-003](docs/adr/ADR-003-server-authoritative-websocket.md)).

## Runtime path 1 — the frame loop

Blazor renders the UI overlay only. The game world is drawn on a `<canvas>` by the engine,
with at most two interop crossings per frame
([ADR-002](docs/adr/ADR-002-batched-render-interop.md)):

```text
JS requestAnimationFrame
  └─► [JSExport] frame entry (C#)           crossing 1: JS → .NET, input events batched in
        FixedTimestep.Advance → N fixed simulation steps
        culling → encode render commands into a preallocated buffer
        └─► IRenderBackend.Submit(Span<byte>)
              [JSImport] submitFrame(MemoryView)   crossing 2: .NET → JS, no copy, no JSON
                renderer.js decodes and draws, synchronously
```

No allocation, no `async` and no `StateHasChanged` inside the frame.

## Runtime path 2 — the network

```text
Blazor page → ViewModel → Nosum.Wasm.Socket (WebSocketSession)
      ⇅  Nalix frames over WebSocket (/ws/), opcode + [SerializeOrder] wire format
Nosum.Host listener → Nalix dispatch → Nosum.Handlers → Abstractions → Infrastructure
```

The server is authoritative: the client sends intents ("plant seed X in plot Y"); the server
validates them with `Nosum.Gameplay`, persists the result and replies or pushes the new state.

Multiplayer movement follows that rule ([ADR-008](docs/adr/ADR-008-snapshot-movement-sync.md)):

```text
client frame loop ──Report(x,y,facing,moving)──► PlayerChannel timer ──MoveIntent @10 Hz──► PlayerHandlers
                                                                                                │ IPlayerService → MoveValidator (Nosum.Gameplay)
client SnapshotBuffer ◄── RemotePlayers ◄── MapSnapshot @10 Hz ◄── PlayerService loop ◄── IMapPresence (one Nalix group per map)
```

Socket callbacks write into the buffer, the frame reads from it: no await and no allocation in the frame.

## Runtime path 3 — many maps, private copies

The world is a catalogue of maps, `GameMaps` in `Nosum.Gameplay/Maps` ([ADR-012](docs/adr/ADR-012-maps-and-warps.md)):
id, Tiled file, spawn, buildings, doors. Tilesets and exits (the Tiled `Warp` property) come from the
map files. Server and client build the walk grid and the exits from the same `MapSpec`.

```text
client walks onto an exit ──MoveIntent──► PlayerService (accepts it, WarpRule: standing on the exit tile?)
                                            │ leave old map group ─► PlayerLeft to the old map's players
                                            │ MapChanged ──────────► this player only (id, map, place)
                                            │ join new map group ──► snapshots of the new map; saved map + place
client MapScene: fade out ─ MapLoader (background fetch, shared TextureCache) ─ swap ─ fade in
```

A player is in one map group at a time, so snapshots, `PlayerLeft` and every feature built on
`IMapPresence` reach only the players on that map. The server also picks the map at join (the saved
one), so a returning player lands where they left.

The farm and the farmhouse are private per player ([ADR-015](docs/adr/ADR-015-private-instances-and-placeables.md)): the place a player is in is a
`MapInstance` (map id, plus the owner for a private map) and its `Key` names the presence group, the farm plots and the
placed furniture. `PlayerService` enters a player into their own copy of a private map when they join or walk through a door from a public map; entry to anyone else's copy is checked by
`IInstanceAccessPolicy` (`VisitAccess`: the owner's allow-list and co-op setting, [ADR-019](docs/adr/ADR-019-visiting-private-instances.md)); the busstop, town, forest and backwoods are one shared instance each, keyed by the map id.

```text
client E/R/F/Q in the farmhouse ──PlaceObject / MoveObject / PickUpObject──► PlaceableService (owner of the instance, reach, free footprint, item in the slot)
                                                                              │ inventory change + placed_objects row: one transaction
client PlaceableChannel ◄── PlacedChanged ◄── IMapPresence (the instance's group) ◄─┘      PlayerService validates movement on IInstanceWorld.GridFor(instance)
```

Resource nodes follow the same instance rule ([ADR-021](docs/adr/ADR-021-tools-resource-nodes-ground-items.md)): the trees, stones, twigs, weeds and grass of an instance are server state
(`ResourceNodeService`), the blocking ones are part of `IInstanceWorld.GridFor` (so prediction and validation agree), and a tool swing is the farm `UseItem` intent: the farm layer checks slot, reach and the 300 ms cooldown,
then the node layer or the farm layer decides. What a broken node gives leaves through one seam, `INodeYieldSink`, in the same transaction as the node's change.

A visit is a server-side move, not a client-chosen map ([ADR-019](docs/adr/ADR-019-visiting-private-instances.md)):

```text
client (on a public map) ──VisitPlayer(nickname)──► VisitService (nickname → owner, VisitAccess: listed, or same co-op and the setting on)
                                                      │ IPlayerService.TravelAsync ─► MapChanged ─► visitor joins farm@<owner>'s presence group
owner removes / kicks / turns the co-op setting off / leaves the co-op ──► sweep: every visitor no longer admitted is travelled to the Town
FarmService · PlaceableService · MarketService.ShipAsync ──► VisitRules.MayDo: a visitor may walk and water, nothing else
```

## Runtime path 4 — items, coins and the social layer

Every change of items or coins is one server transaction ([ADR-013](docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-014](docs/adr/ADR-014-coops-and-looks.md)):

```text
handler (identity from the connection) ─► IMarketService / ITradeService / ICoopService / ILookService
                                              │ legs: (party, item ±, coins ±) + enlisted work (listing row, log line, look row)
                                              ▼
                                       ITransferService ─► IInventoryService.ApplyManyAsync
                                              locks every involved player (stripe order) · one unit of work · one SaveChanges · audit rows
                                              ▼ pushes: InventoryUpdate, WalletUpdate · CoopSnapshot · LookChanged (to the map group) · TradeState
```

Looks travel as ids (`LookChanged`, `LooksSnapshot`) to everyone on the map and are resolved by the client's `LookPalette`; the player's nickname rides in the same row (cached by map player id, drawn by the engine as a pre-rendered label); shop purchases (`BuyFromShop`) are one transfer like the others; the market fee leaves through `IFeeSink` (a coin sink); trade and co-op invitations address a player by their id on the map and find them through `IMapPresence`.

## Runtime path 5 — items on the ground

Harvests (and later broken nodes) do not go straight into a bag: they land on the ground ([ADR-021](docs/adr/ADR-021-tools-resource-nodes-ground-items.md)), and the server hands them to whoever walks near.

```text
FarmService harvest ──tile reset + ground_items row: one transaction──► IGroundItems (chunk buckets, 16 × 16 tiles)
                                                                          │ GroundItemSpawned ─► the instance's players (one PublishAsync seam)
PlayerService.MoveAsync (accepted) ─► OnPlayerMovedAsync ─► 9 chunks around the player: in reach, allowed, unclaimed
                                                                          │ claim in memory ─► ITransferService (item in, row out: one transaction)
client GroundItemChannel ◄── GroundItemTaken (flies to the taker) ◄───────┘      full inventory: the claim is released, the item stays
```

## Organize by game domain

Inside each area, code is grouped by game domain (`Farm`, `Inventory`, `Market`, `Social`,
`Player`, `World`, `Placeable`), the same axis in every area:

- `contracts/Nosum.Contracts/{Domain}/{C2S,S2C}/`
- `server/Nosum.Handlers/{Domain}/`
- `wasm/Nosum.Wasm/Pages/{Domain}/` and `wasm/Nosum.Wasm.ViewModels/{Domain}/`

The wire format is the opcode plus `[SerializeOrder]`, not the type name, so renaming a type
or folder is wire-safe. Never change an opcode value or a `[SerializeOrder]` value, and never
reuse a retired opcode.
