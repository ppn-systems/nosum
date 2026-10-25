# ADR-012 — Maps and warps: one catalogue, exits as data, the server moves the player

**Status:** Accepted (owner approval, 2026-10-25). Amendment pending: farm and farmhouse become private per player (owner decision, same date).

## Context

The world is made of many maps ([pillars](../design/pillars.md)): the farm, the farmhouse, the roads
and woods around it, later the town and co-op land. Until now the server and the client each knew one
map, `Farm.tmx`. Moving between maps touches everything: what is loaded and drawn, who sees whom, what
is saved, and which rules a player is walking under. The placeholder maps
([ADR-007](ADR-007-placeholder-content-16px.md)) already describe their exits: a `Warp` map property of
groups `fromX fromY targetMap targetX targetY`, where an exit one tile outside the map (`x = -1`, `x =
width`) is an edge exit. The server decides everything that matters
([ADR-003](ADR-003-server-authoritative-websocket.md)); a client must never pick a map, and a map name
from the wire must never become a file path.

## Decision

- **D1 — One catalogue, shared.** `Nosum.Gameplay.Maps.GameMaps` lists every hosted map as a `MapSpec`:
  stable id (lower-case key, the only map name on the wire), Tiled file name, spawn tile, building props
  with their footprints, doorsteps to open, and exits the file does not list. Tilesets come from the
  file. Server and client both build the walk grid (`MapSpec.BuildWalkGrid`) and the exits
  (`MapSpec.ResolveWarps`) from it, so they collide and open doors identically. Adding a map is a new
  `MapSpec` plus linking its `.tmx` and tilesheets into `Nosum.Wasm.csproj` and `Nosum.Host.csproj`; a
  unit test fails when one is missing. `assets/` is never edited.
- **D2 — Exits are data from the maps.** `WarpParser` reads the Tiled `Warp` property; a target is a map
  name resolved case-insensitively to a catalogue id (`BusStop` is `busstop`). An exit to a map the world
  does not host is dropped, so its edge stays a plain wall: the world can grow one map at a time.
  Hand-made doors (the farmhouse, which the original game hard-codes) are `MapSpec.ExtraWarps`. The
  `Action`/`TouchAction` tile properties are not used by the enabled maps and are not read.
- **D3 — The rule lives in gameplay.** `WarpRule` takes the position the server keeps, not a client claim.
  The player must stand on the exit's tile. An edge exit counts from the nearest edge tile and also needs
  the player walking and facing outwards, so walking along the edge does not trigger it. The feet row
  uses the same exclusive bottom edge as `WalkGrid.Overlaps`. Arrival is the walkable tile nearest the
  target (a tile south of it wins a tie, since doors open southwards), facing away from the map edge when
  the tile is on one.
- **D4 — A player who arrives on an exit tile must step off it first** (`PlayerPresence.WarpArmed`).
  Arrival points sit on or next to the return exit (the road out of the bus stop lands on the farm's
  edge exit); without this a player would bounce straight back.
- **D5 — The server moves the player; nobody asks.** There is no "change map" request. When an accepted
  `MoveIntent` puts the player on an exit, `PlayerService` marks the player in transit (later intents are
  ignored), leaves the old map's group (its players get `PlayerLeft`, the old place is saved), pushes
  `MapChanged` to that player, joins the new map's group and saves map and place. `MapChanged` is sent
  while the player is on no map, so no snapshot of the new map can reach the client before it knows it
  has changed map. The player id stays the same for the whole connection.
- **D6 — The server also chooses the map at join.** `JoinMapResult` carries the map and facing. The
  `MapId` in `JoinMap` only has to name a hosted map (`UnknownMap` otherwise) and does not choose: the
  saved map if it is still hosted and the saved place still legal, that map's spawn if the place is not,
  the starting map (`farm`) for everything else. This is a wire change, so `NosumProtocol.Version` is 2.
- **D7 — Wire: World band `0x7000-0x70FF`.** One push so far, `MapChanged` (`0x7040`: player id, map id,
  feet x and y, facing). Ids and numbers only, no display text ([ADR-010](ADR-010-internationalization.md)).
- **D8 — Presence is per map, not per player.** Every map is one Nalix group (`MapGroup.Name`); snapshots,
  `PlayerLeft` and everything built on `IMapPresence` already reach only that map's players, so other
  players are shown only on the same map without new code.
- **D9 — The client loads in the background and fades.** `MapScene` (the former `FarmScene`) draws any
  `MapContent`: the map's layers by `TileLayerPlan` (names starting `Front`/`AlwaysFront` over the
  characters, `Paths` not drawn, the rest ground), its walk grid, and `IMapDecorator`s for what a map
  draws besides tiles (buildings now; crops and soil plug in here). `MapLoader` fetches a map and its
  sheets once, through a `TextureCache` that gives each image one texture id however many maps use it, and
  prefetches the maps the current one leads to. On `MapChanged` the scene fades to black (0.2 s), freezes
  input and reports, waits without blocking until the loader has the map, swaps, and fades in (0.25 s). The
  fade is a black-fill render command (`RenderCommandBuffer.FillBlack`), so the render contract is still one
  draw list per frame. A small map is zoomed until it fits the screen.
- **D10 — Maps are shared by default.** Every map is one shared instance. Making the farmhouse private per
  player is a presence-key change (group name from map id plus owner) and is left open below.

## Alternatives considered

- **A `UseWarp` request from the client.** Explicit, but adds a packet, a rate limit and a way to ask for
  a door from anywhere; the server already knows where the player stands, so it decides.
- **Trigger on any tile of the exit group, ignore facing.** Simpler, but walking along the farm's east
  edge would throw the player out of the map.
- **Loading the target map after the server's answer, on the frame.** Blocks the loop or shows a half
  loaded map; background loading with a fade hides the wait.
- **Per-map `.tsx` tilesets or a custom map format.** The placeholder maps embed their tilesets; the
  reader already handles them.

## Consequences

- A map change costs two database writes (the leave saves the old place, then the new place is saved);
  a crash between them restores the old place, which is harmless.
- Edge exits trigger one tile earlier than in the original game (on the edge tile, not outside it).
- Every shared map is a place where all players meet; a busy map still needs the interest management or
  splitting noted in [ADR-008](ADR-008-snapshot-movement-sync.md) and the pillars.
- Exits to unhosted maps are silently walls; the catalogue test only checks the hosted ones.
- A client that missed `MapChanged` (a dropped connection) is brought back by the next join, which names
  the saved map.

## Revisit when

A map must be private or instanced (farmhouse, co-op land), the world has more than a handful of maps
(load them on demand with a size budget instead of keeping all of them), or the `Action`/`TouchAction`
door properties are needed.
