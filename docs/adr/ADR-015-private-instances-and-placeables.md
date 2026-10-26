# ADR-015 — Private per-player farm and farmhouse, Town and Backwoods, player-placed furniture

**Status:** Accepted (owner approval, 2026-10-25). Amends [ADR-012](ADR-012-maps-and-warps.md) D8 and D10 (owner decision, 2026-10-25: the farm and the farmhouse are private per player).
ADR-013 and ADR-014 are reserved by the social work and are not used here. Amended by [ADR-019](ADR-019-visiting-private-instances.md) (Proposed): D4's owner-only access policy is replaced by one that also admits invited players and co-op members.

## Context

[ADR-012](ADR-012-maps-and-warps.md) made every map one shared place: one Nalix group per map, one set of farm plots per map. The owner decided that a
player's farm and farmhouse belong to that player and nobody else enters them (visiting and invitations come later), that Town and Backwoods
join the public maps, and that players place furniture in their own farmhouse. Presence, farm plots and now placed objects must therefore be keyed
by *which copy* of a map a player is in, not by the map alone. The social features (looks broadcast, market, trade, co-ops) address "the players on the
same map" through `IMapPresence`; that abstraction has to keep working unchanged for them, so a private copy is just a more specific presence key.

## Decision

- **D1 — A place is a `MapInstance` = map id + owner id.** `Nosum.Gameplay.Maps.MapInstance` is a `readonly record struct (MapId, OwnerId)`; the owner is
  `Guid.Empty` for a shared map. Its `Key` is the map id for a shared map (so code keyed by map id still names the same place) and
  `mapid@<owner N-format guid>` for a private one. Everything that is "per place" is keyed by the instance: presence, farm plots, placed objects, the
  in-memory caches and their locks. The key is also the Nalix group name suffix (`MapGroup.Name(key)`).
- **D2 — Which maps are private.** `MapSpec.IsPrivate` marks `farm` and `farmhouse`; `busstop`, `forest`, `town` and `backwoods` stay shared (one
  instance for everybody, key = map id). `MapSpec.Placeable` marks where furniture may be placed (`farmhouse` only). Town (130 x 110) and Backwoods
  (50 x 40) are plain `MapSpec`s: their layers, tilesheets (the early town, shadow, waterfall and grave sheets) and `Warp`
  property are read by the existing reader, and the client and the host link the files like the other maps. Exits to maps the world does not host
  (Mountain, Beach, Woods, Tunnel, ...) are dropped as before. Shop and house doors in Town are `Action` tile properties, which stay unused, so they are walls.
- **D3 — Presence is keyed by instance key; `IMapPresence` keeps its shape.** `JoinAsync` takes a `MapInstance` (a `string mapId` overload joins the shared instance);
  `CopyPlayers`, `CopyActiveMapIds` and `MulticastAsync` take and return instance keys. `PlayerPresence` gains `Instance` and `Key`; `MapId` stays the id of the map
  (for looking up its definition). Rule for any feature: to reach "the players on this player's map", use `presence.Key`, not `presence.MapId`; on a shared map they are equal,
  on a private one only `Key` stays inside the copy, so a looks broadcast or a chat line can never leave a private farm by construction.
- **D4 — The server alone chooses the instance, and nobody enters another's.** `PlayerService` builds the instance for every join and every warp as
  `map.IsPrivate ? (map, the entering account) : shared`. A client never names an instance (it still sends only a map id that must be hosted, as in ADR-012 D6; the server also
  ignores it). Entry goes through `IInstanceAccessPolicy.CanEnter(instance, player)`; today's `OwnerOnlyAccessPolicy` admits everyone to shared maps and only the owner to a
  private one. Visiting and invitations replace that policy with one that also consults an allow-list; the key, the presence and the data need no change. The saved place is
  still just a map name (`player_states.map_name`): reloading a player in the `farmhouse` puts them in their own.
- **D5 — Persistence.** `farm_plots` is keyed (map id, owner id, x, y); `placed_objects` (id, map id, owner id, item id, tile x/y, rotation, timestamps) is new, with an index on
  (map id, owner id). Both are migrations generated with `dotnet ef`. The migration gives every existing plot to the player who last worked it (`last_worked_by`), else to the
  oldest account, so no tilled tile is lost; plots that several players shared on the old single farm are split by who worked them last. Server memory: `FarmService` and
  `PlaceableService` load an instance's rows once on first use and write through; one lock per instance serializes changes (the server stays a single process, ADR-009).
- **D6 — Placed objects change how a player collides, so the grid is per instance.** `IInstanceWorld.GridFor(instance, map)` returns the map's grid plus the footprints of the
  instance's placed objects (the map's own grid when nothing is placed); `PlayerService.MoveAsync` validates with it, `PrepareAsync` loads an instance before a player enters. The
  client builds the same grid from the same pieces with `PlacedSet.BuildGrid`, rebuilt only when the mirror changes, so prediction and the server agree.
- **D7 — A generic placeable, furniture first.** `PlaceableDefinition` (made from an inventory item; 1 or 4 views, each a sheet rectangle, optional mirroring and a footprint of whole tiles
  anchored bottom-left to the sprite) and `PlaceableCatalog` live in `Nosum.Gameplay/Placeables`; furniture is `ItemKind.Furniture`, ids 300-399 (oak chair, blue armchair, blue couch, oak table, bookcase, floor lamp,
  house plant, with measured rectangles of the furniture sheet `assets/sprites/furniture.png`). The rules are the pure `PlacedSet.Check`: valid rotation, footprint inside the map, on floor the map does not block, off the tiles
  the room keeps clear (`MapDefinition.ReservedTiles`: the exit tiles, the arrival points of every exit into the map and the tile north of each, so a player is never walled in), not on another piece
  (a piece being moved ignores itself); `PlacedSet.CoversFeet` refuses a piece under the player placing it. Decorations and machines reuse the same definition and set later.
- **D8 — The server decides placing.** `IPlaceableService` (`PlaceableService`): the player must be in a placeable map, in an instance they own (`Instance.OwnerId == account`), within reach (the 32 px
  farming rule, any tile of the footprint) and, to place, the slot must hold a furniture item. Place = remove one item from that slot and insert the row in one inventory transaction
  (`IInventoryService.ApplyAsync` with an `enlist` callback, reason `placed.place`); pick up = add the item and delete the row in one transaction (`placed.pickup`; `InventoryFull` refuses); move = update the row.
  An instance holds at most 200 pieces. Every change is pushed to the instance's group as `PlacedChanged` (with the changed pieces and the removed ids); the requester also gets a `PlaceResult`.
- **D9 — Wire: Placeable band `0xB000-0xB0FF`.** C2S `0xB000-0xB03F`: `GetPlaced`, `PlaceObject(slot, tile, rotation)`, `MoveObject(id, tile, rotation)`, `PickUpObject(id)`; S2C `0xB040-0xB07F`:
  `PlacedSnapshot`, `PlaceResult(PlaceStatus, id)`, `PlacedChanged`. Ids, numbers and `PlaceStatus` codes only ([ADR-010](ADR-010-internationalization.md)); handlers are `USER`-only, take the identity from the
  connection and are rate limited (`[PacketRateLimit]`: reads 4/s burst 5, edits 8/s burst 3). The packets are new and nothing existing changed, so `NosumProtocol.Version` stays 2.
- **D10 — Client.** `PlaceableChannel` mirrors the pieces (loaded after every join or map change, like the farm); `PlacedObjectsDecorator` draws them (and the ghost of the piece about to be placed, tinted when it
  looks illegal) as depth-sorted `WorldSpriteBatch` sprites anchored at the bottom of the footprint; `PlacementActions` turns keys into requests: the action key places the held piece or sets down a carried one,
  R rotates, F grabs the piece in front to move it (F again cancels), Q takes it back into the inventory. The "arrange your home" panel mirrors those keys with buttons and is built from the shared
  `GamePanel` and `GameButton` controls; refusals show on the message line through `Place_Status_*` resources (vi + en).

## Alternatives considered

- **A separate instance id on the wire.** More explicit, but a new thing to validate; the server already knows whose place a player is in, and the client only needs to know the map.
- **Keying presence by map id and filtering by owner at send time.** Every feature would have to remember to filter; a key per place makes the safe thing the default and keeps `IMapPresence` unchanged.
- **Furniture as a JSON blob on the player.** No query or constraint, no push per piece; rows with an index are as cheap and let a later "visit" read another player's room.
- **Client-side collision without the server.** Rejected: the server validates movement with the same grid (ADR-003).
- **Rotating with four separate sprites for every piece.** Some pieces only have the views they need (one view for a table); the left view mirrors the right one.

## Consequences

- Two players are never in the same farm or farmhouse unless the owner invited the other ([ADR-019](ADR-019-visiting-private-instances.md)); a shared map such as Town is where players meet. A player's farm is a place nobody else can help with yet (co-op land and visits are later).
- Per instance there is one more Nalix group and one more in-memory set while someone is there (the snapshot loop already runs per active key); an empty instance costs nothing.
- Existing dev inventories were created before furniture existed and do not get the new starter items (a new player does); the migration is lossy for plots that several players shared.
- The social branch must address "same map" through `PlayerPresence.Key`, and `JoinAsync(connection, account, MapInstance, ...)`; see the merge notes of the change that introduced this ADR.
- `placed_objects` is the first table where the owner id is part of an "instance" key; the co-op land can use the same idea with the co-op id as the owner.

## Revisit when

Visiting or invitations (swap the access policy, add an allow-list table and a way to ask for another instance), co-op land (owner = co-op), furniture beyond the farmhouse (outdoor decorations, machines: add
`Placeable` to more `MapSpec`s and kinds to the catalogue), more than 200 pieces per room, or a second server process (the in-memory instance caches and locks would need a shared store).
