# contracts/

Types that cross the wire, shared byte-for-byte by the server and the browser client:
opcodes, Nalix packets, DTOs, enums and `NosumProtocol.Version`. **No logic.**

Layout: `Nosum.Contracts/{Domain}/{C2S,S2C}/` plus one `*OpCode` class per domain.
Rules for packet classes: [`nalix-packets`](../.claude/skills/nalix-packets/SKILL.md).

Domains and opcode bands (record a new band here and in its `*OpCode` class):

| Domain | Band | Packets |
| :--- | :--- | :--- |
| Account | `0x1000-0x10FF` | register, login, resume session, start guest, upgrade guest, logout, set language (C2S `0x1001-0x107F`); account result, session ended, language result, registration prompt unlocked (S2C `0x1081-0x10FF`); replies carry `AccountStatus` codes |
| Player | `0x2000-0x20FF` | join map, move intent (C2S `0x2000-0x203F`); join result (carries the map the server chose and the facing), map snapshot, player left, move correction (S2C `0x2040-0x207F`); replies carry `JoinStatus` / `MoveRefusal` codes |
| Farm | `0x3000-0x30FF` | use item on tile, get farm (C2S `0x3000-0x303F`); use result, farm snapshot, tiles changed (pushed to the map group) (S2C `0x3040-0x307F`); replies carry `FarmStatus` / `FarmAction` codes ([ADR-011](../docs/adr/ADR-011-inventory-farming-data-model.md)) |
| Market | `0x4000-0x40FF` | reserved |
| Inventory | `0x5000-0x50FF` | get inventory, move slot, select slot (C2S `0x5000-0x503F`); inventory snapshot, result, update (push of changed slots) (S2C `0x5040-0x507F`); replies carry `InventoryStatus` codes |
| Quest | `0x6000-0x60FF` | get quests (C2S `0x6000-0x603F`); quest log, quest update (push) (S2C `0x6040-0x607F`) |
| World | `0x7000-0x70FF` | map changes. C2S `0x7000-0x703F`: none (the server changes maps when a player walks onto an exit, [ADR-012](../docs/adr/ADR-012-maps-and-warps.md)); S2C `0x7040-0x707F`: `MapChanged` (player id, map id, feet x/y, facing) |

`NosumProtocol.Version` is 2: `JoinMapResult` gained `MapId` and `Facing` (ADR-012 D6).

Shared enums that are not tied to one domain live in `Nosum.Contracts/Common` (`GameLanguage`,
with `GameLanguages` as its one mapping to BCP-47 tags).

```bash
dotnet build contracts/Nosum.Contracts.slnx
```
