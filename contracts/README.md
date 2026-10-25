# contracts/

Types that cross the wire, shared byte-for-byte by the server and the browser client:
opcodes, Nalix packets, DTOs, enums and `NosumProtocol.Version`. **No logic.**

Layout: `Nosum.Contracts/{Domain}/{C2S,S2C}/` plus one `*OpCode` class per domain.
Rules for packet classes: [`nalix-packets`](../.claude/skills/nalix-packets/SKILL.md).

Domains and opcode bands (record a new band here and in its `*OpCode` class):

| Domain | Band | Packets |
| :--- | :--- | :--- |
| Account | `0x1000-0x10FF` | register, login, resume session, start guest, upgrade guest, logout, set language, set nickname (C2S `0x1001-0x107F`); account result, session ended, language result, registration prompt unlocked, nickname result (S2C `0x1081-0x10FF`); replies carry `AccountStatus` codes |
| Player | `0x2000-0x20FF` | join map, move intent (C2S `0x2000-0x203F`); join result (carries the map the server chose and the facing), map snapshot, player left, move correction (S2C `0x2040-0x207F`); replies carry `JoinStatus` / `MoveRefusal` codes |
| Farm | `0x3000-0x30FF` | use item on tile, get farm (C2S `0x3000-0x303F`); use result, farm snapshot, tiles changed (pushed to the map group) (S2C `0x3040-0x307F`); replies carry `FarmStatus` / `FarmAction` codes ([ADR-011](../docs/adr/ADR-011-inventory-farming-data-model.md)) |
| Market | `0x4000-0x40FF` | get wallet, ship items, browse listings, create/buy/cancel listing, get my listings (C2S `0x4000-0x403F`); wallet snapshot, wallet update (push), ship result, listings page, market result (S2C `0x4040-0x407F`); replies carry `MarketStatus` codes ([ADR-013](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md)) |
| Inventory | `0x5000-0x50FF` | get inventory, move slot, select slot (C2S `0x5000-0x503F`); inventory snapshot, result, update (push of changed slots) (S2C `0x5040-0x507F`); replies carry `InventoryStatus` codes |
| Quest | `0x6000-0x60FF` | get quests (C2S `0x6000-0x603F`); quest log, quest update (push) (S2C `0x6040-0x607F`) |
| Shop | `0xC000-0xC0FF` | buy from a shop (C2S `0xC000-0xC03F`); shop result (S2C `0xC040-0xC07F`); replies carry `ShopStatus` ([ADR-013 amendments](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md)) |
| Dev | `0xF000-0xF0FF` | tester tools, handled only when the host runs in Development: dev grant (C2S `0xF000`); result (S2C `0xF040`). In Production the opcode is not registered. |
| Placeable | `0xB000-0xB0FF` | objects a player places in their own place (furniture in the farmhouse): get placed, place, move, pick up (C2S `0xB000-0xB03F`); placed snapshot, place result (carries a `PlaceStatus`), placed changed (pushed to the instance) (S2C `0xB040-0xB07F`); [ADR-015](../docs/adr/ADR-015-private-instances-and-placeables.md) |
| World | `0x7000-0x70FF` | map changes. C2S `0x7000-0x703F`: none (the server changes maps when a player walks onto an exit, [ADR-012](../docs/adr/ADR-012-maps-and-warps.md)); S2C `0x7040-0x707F`: `MapChanged` (player id, map id, feet x/y, facing) |
| Look (wardrobe) | `0x8000-0x80FF` | get looks, set appearance, equip, unequip (C2S `0x8000-0x803F`); looks snapshot, look result, look changed (push to the map group) (S2C `0x8040-0x807F`); looks are ids only; replies carry `LookStatus` codes ([ADR-014](../docs/adr/ADR-014-coops-and-looks.md)) |
| Trade | `0x9000-0x90FF` | request, respond, set offer, confirm, cancel (C2S `0x9000-0x903F`); result, invite (push), state (push to both parties), ended (push) (S2C `0x9040-0x907F`); replies carry `TradeStatus`, endings `TradeEndReason` ([ADR-013](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md)) |
| Visit | `0xE000-0xE0FF` | get visitors, add visitor (nickname), remove visitor, kick visitor, set co-op visits, visit player (nickname) (C2S `0xE000-0xE03F`); visit result (carries a `VisitStatus`), visitors snapshot (S2C `0xE040-0xE07F`); [ADR-019](../docs/adr/ADR-019-visiting-private-instances.md) |
| Coop | `0xA000-0xA0FF` | create, get, invite, respond to invite, leave, kick, deposit, withdraw (C2S `0xA000-0xA03F`); result, snapshot (also pushed to every online member) (S2C `0xA040-0xA07F`); replies carry `CoopStatus`; the co-op name is authored text (plain or `i18n{` envelope) ([ADR-014](../docs/adr/ADR-014-coops-and-looks.md)) |

`NosumProtocol.Version` is 3: `JoinMapResult` gained `MapId` and `Facing` (ADR-012 D6, version 2); nicknames appended fields to `Register`, `UpgradeGuest`, `AccountResult`, `LookRow`, `ListingRow`, `CoopMemberRow` and `CoopInviteRow` (version 3, [ADR-009 amendments](../docs/adr/ADR-009-postgresql-persistence-and-accounts.md)). The Placeable band only adds packets, so the version stays 2. The Visit band only adds packets and three appended status values (`FarmStatus.NotYourFarm`, `PlaceStatus.NotYourPlace`, `MarketStatus.NotYourBin`), so it needs no bump. The social layer only added packets in new bands too.
A place is a map instance (a shared map, or one player's private farm or farmhouse); none of that is on the wire: `FarmSnapshot.MapId`, `MapChanged.MapId` and `PlacedSnapshot.MapId` are map ids and the server knows whose copy the connection is in.

Shared enums that are not tied to one domain live in `Nosum.Contracts/Common` (`GameLanguage`,
with `GameLanguages` as its one mapping to BCP-47 tags).

```bash
dotnet build contracts/Nosum.Contracts.slnx
```
