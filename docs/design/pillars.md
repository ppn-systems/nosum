# Nosum — game pillars

What makes Nosum different from single-player farming games: **farming is a shared,
server-wide activity.** The 3/4 top-down view ([ADR-005](../adr/ADR-005-three-quarter-top-down-view.md))
is the genre's camera; these pillars are Nosum's identity. A feature that serves none of them
needs a reason.

Status: direction agreed by the owner (2026-10-25). The mechanics below are the starting
design; numbers and details are open and are settled per feature.

## Pillar 1 — Cooperatives

Players form a cooperative (co-op): a small group that owns shared land and a shared
warehouse next to each member's own farm.

- Members contribute crops and items to the co-op warehouse; the co-op unlocks shared
  buildings and tools no single player can afford.
- No limit on co-op size.
- Roles inside a co-op (founder, member) decide who may spend shared stock.
- Every contribution and withdrawal is recorded, so members can see who did what.

## Pillar 2 — Shared market and player trade

Players trade with each other in two ways:

- **Consignment market.** A player lists items at a price; the server takes the items into
  escrow so they cannot be used or sold twice. Any player can buy a listing; the seller gets
  the coins (minus a market fee) even while offline. A listing that expires or is withdrawn
  returns the items to the seller.
- **Direct trade.** Two players open a trade, each puts in items and coins, both confirm, and
  the server swaps everything at once. Changing an offer clears both confirmations.

Prices come from what players ask and pay, not from a fixed shop table. Three fixed shops in the town (seeds, clothing, furniture) sell for coins (new players start with only tools and seeds); the market fee is a pure coin sink (owner decision 2026-10-25). Every purchase and every direct trade is one atomic
server transaction: items and coins move together or not at all.

## Pillar 3 — Server-wide seasons and goals

Everyone on a server lives in the same calendar.

- The season and day are computed from server time, identical for every player.
- Each season has community goals (deliver N of a crop, build a bridge) that the whole server
  contributes to; finishing one changes the shared world for everybody.
- Seasonal crops make the market move: what grows now is cheap, what is out of season is not.

## World: many maps

The world is made of many maps, not one: each player's farm, co-op land, the town and market,
and other areas reached through exits. A player is on exactly one map at a time; the server
sends each player only what happens on that map.

Settled ([ADR-012](../adr/ADR-012-maps-and-warps.md), [ADR-015](../adr/ADR-015-private-instances-and-placeables.md)): exits are doors and map edges that the
player walks onto; the server decides when the player changes map, saves where they are, and puts
them back there when they return. **A player's farm and farmhouse are private**: each player has their own copy and
nobody else enters it unless the owner lets them in: the owner keeps an allow-list (and a switch for co-op members), a visitor can walk around and water but changes nothing else ([ADR-019](../adr/ADR-019-visiting-private-instances.md), proposed). The bus stop, the town, the forest and the backwoods are public:
everyone on the same map sees each other, and that is where players meet. Inside their farmhouse a player places furniture
(chairs, tables, a couch ...) that is kept for them and travels in the inventory.

## What this asks of the architecture

| Pillar | Needs | Where |
| :--- | :--- | :--- |
| Co-ops | Co-op entity, membership, shared inventory with an audit trail | `server/Nosum.Domain`, `gameplay/Nosum.Gameplay` (rules), `contracts/` (`Coop` domain) |
| Co-ops | Push to all online members | Nalix connection groups: `UseGroups()` on the host, `MulticastAsync` to the co-op's group |
| Market | Consignment listings with escrow, expiry, fees; direct trade sessions with two-sided confirm | Server only; one database transaction per purchase or swap; `[PacketRateLimit]` on listing and trade handlers |
| Many maps | Map definitions, player-to-map assignment, map transitions, private copies of a map | One Nalix connection group per active map instance (`MapInstance`: the map, plus its owner for a private map) so updates reach only players in it; map data loaded per map, not per world |
| Seasons | Calendar from server time, community goal progress | `gameplay/` computes the calendar from a passed-in time; server persists goal progress and broadcasts milestones |

Every pillar is server-authoritative ([ADR-003](../adr/ADR-003-server-authoritative-websocket.md)):
the client never decides a contribution, a trade or a goal outcome.

## Settled so far (first farming slice, [ADR-011](../adr/ADR-011-inventory-farming-data-model.md))

- Farming happens on the player's own private farm: till with a hoe, plant a seed, water, wait, harvest into the inventory. Crops grow only while the soil is wet.
- Every item change is one atomic, logged server transaction, the base the market, trades and the co-op warehouse will use.
- The first quest (plant and harvest one crop) is the tutorial and invites a guest to link a Google or Microsoft account. Durations, starter items and inventory size (36) are defaults, not balance.

## Built so far (social layer, [ADR-013](../adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-014](../adr/ADR-014-coops-and-looks.md); proposed defaults, owner to confirm)

- Coins: a persisted balance, 500 to start; the farm's shipping bin buys crops at a fixed price (the first coin source); the town general store sells seeds, furniture and clothing at fixed prices (the main coin use besides the market).
- Market: listings are escrow, 5 % fee, 48 hours, at most 20 per player, paged browse with search by item; the seller is paid while offline; cancel and expiry return the items; listing, buying and cancelling are done at the trader's counter in Town ([ADR-034](../adr/ADR-034-market-at-the-trader-counter.md)). The fee is destroyed (anti-inflation), behind one seam (`IFeeSink`).
- Direct trade: same map, both sides set an offer, any change clears both confirmations, both confirm, one atomic swap; leaving the map aborts with nothing moved.
- Co-ops: one per player, founder and member roles, invitations, shared warehouse with an audit trail; the founder takes anything, a member takes back what they put in. Co-op land ([ADR-020](../adr/ADR-020-coop-land.md), proposed): every co-op has its own shared farm, entered through a gate on Town's west plaza by members only; members till, plant, water and harvest together, the harvester keeps the produce and every action is audited; leaving or being removed ends access at once. Co-op upgrades ([ADR-038](../adr/ADR-038-coop-upgrades.md)): the founder buys up to three levels of a bigger warehouse (more item kinds) and of more usable land, paid in wood, stone and bars taken from the warehouse; locked land is shaded and refuses tilling and placing.
- Looks: skin, hair style, hair dye and worn shirt, trousers and hat, saved by the server and shown to everyone on the map as ids; clothes are bought, nobody starts with any.
- Nicknames: every registered player has a unique display nickname (unaccented letters and digits, no profanity), chosen when a guest links a Google or Microsoft account (a new account keeps its generated name until its first free rename), changeable once a day (guests keep a generated name), drawn above their head and shown in the market, trades and co-ops; guests show as "Khách 1234".

- Visiting: the owner keeps an allow-list of registered players (by nickname, up to 50) and a switch for co-op members (on by default); a listed player or co-op member visits by typing the owner's nickname on a public map, even while the owner is offline; a visitor walks, looks, trades and waters crops, and cannot till, plant, harvest, move furniture or use the shipping bin; removing a player, switching co-op visits off or leaving the co-op sends them to the Town at once. Guests can visit when invited but keep no list and cannot be visited. Proposed defaults, owner to confirm.

## Built so far (seasons, [ADR-018](../adr/ADR-018-seasons-and-server-calendar.md); proposed defaults, owner to confirm)

- One server calendar from server time: a season is 28 game days of 12 real hours (14 real days), four seasons a year. The client gets the epoch and day length once and shows season, day and time in a HUD widget.
- Crops list the seasons they grow in (parsnip spring/fall/winter, radish summer/fall, cauliflower spring/summer). Planting out of season is refused, a crop withers at the season change (mature or not), and the seed shop only sells seeds that can be planted now.
- Not built yet: community goals, seasonal prices or events, a time-of-day tint of the world, withered-crop art.

## Built so far (crop growth in game days, [ADR-021](../adr/ADR-021-tools-resource-nodes-ground-items.md) D7)

- A crop grows one day for every game day its soil was watered, counted when that day ends: parsnip 4 watered days, radish 6, cauliflower 12. Skipped days add nothing, watering twice on a day counts once, and wet soil dries at the day rollover. Growth is still computed from stored day numbers: no timer per tile, nothing runs while a farm is offline.
- Tilled soil with no crop reverts to untilled with a 30 % chance for each dry game day (a deterministic hash of the instance, the tile and the day), so an abandoned plot goes back to grass.
- The server has a day-ended seam (`IDayClock`) that other systems (resource-node regrowth, sprinklers) subscribe to; testers can skip game days with the Development-only `DevAdvanceClock` tool.
- Not built yet: rain watering every crop, sprinklers, multi-harvest crops.

## Open questions

- Whether a player may belong to more than one co-op (built: one).
- Co-op land ([ADR-020](../adr/ADR-020-coop-land.md)): whether a harvest goes to the harvester (built) or the warehouse, and whether members may harvest what others planted (built: yes).
- Market fee rate, listing duration, and limits that stop one player cornering a crop (built defaults: 5 %, 48 hours, 20 listings; no cornering limit yet). The fee itself is closed: a coin sink.
- Shop prices and stock (built: unlimited stock; seeds 10/15/30, furniture 80-400, clothing 40-80) and whether more shops are wanted (built: seed, clothing and furniture shops in the town).
- Season length is settled (two real weeks); open: the game-day length (built: 12 real hours, 28 days a season), the calendar epoch, and whether a mature crop that is not harvested in time withers (built: yes).
- How many players share one instance of a public map (town) before it splits into copies.
- Visiting (built as a proposal, [ADR-019](../adr/ADR-019-visiting-private-instances.md)): what a visitor may touch beyond walking and watering, whether an invitation needs the owner online, how guests take part, and how a visit is started (a panel now; a gate or a profile action later). The ADR lists seven owner questions.
- Whether furniture may also go outdoors (on the farm) and what else is placeable (decorations, machines).
