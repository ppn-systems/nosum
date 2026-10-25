# ADR-014 — Co-operatives (membership and shared warehouse) and player looks

**Status:** Accepted (owner approval, 2026-10-25). Amended 2026-10-25: no starter wardrobe, nicknames on the wire (see Amendments).

## Context

[Pillar 1](../design/pillars.md) asks for co-ops with roles, no size limit and an audited shared warehouse. [ADR-006](ADR-006-layered-character-customization.md) D6 says the
server decides every look and the wire carries ids only; the stand-in look derived from the player id (`RemoteLook`) was to be removed once real looks flow. Both
build on [ADR-013](ADR-013-economy-coins-escrow-market-trade.md)'s atomic transfer. Co-op land is out of scope (it depends on the owner's decision about private versus shared farms).
Numbers and rules marked "default" are open owner questions.

## Decision

### Looks

- **D1 — Persisted ids.** `PlayerAppearance` (one row per player): skin, hair style, hair dye ids (1-based, ranges in `LookCatalog`) and the item ids of the worn shirt, trousers
  and hat (zero = the plain default garment, or no hat). A new player and every guest gets `PlayerLook.Default`; the row is created on first read.
- **D2 — Worn clothing leaves the inventory.** Wearing moves the item out of its inventory slot and the item worn before back in, in one `ApplyAsync` whose enlisted work writes the look
  row, so a worn item cannot also be sold, traded or deposited (no duplication) and a full inventory refuses taking something off (`NoSpace`). Clothing items are `ItemKind.Clothing` (ids 400-499; 300-399 are furniture, ADR-015) with a
  `WearSlot`, stack size 1, tradable. A new player also finds five clothes in the inventory (default) so the wardrobe has something to try.
- **D3 — Ids only on the wire.** `LookRow` (player id on the map, skin, style, dye, three item ids). A client calls `GetLooks` after joining a map or changing map: the server replies with the
  looks of everyone on that map (found through `IMapPresence.CopyPlayers(PlayerPresence.Key)`, so a private farm shows only its owner) and announces the caller's look to the rest of the map (`LookChanged`, `MulticastAsync`); every change is multicast to the map
  group. Nothing is derived from a player id, and no presence or movement code changed. Opcode band `0x8000-0x80FF`.
- **D4 — Client catalogue.** `LookPalette` (`Nosum.Wasm.ViewModels/Looks`) turns ids into sheet frames and tints (skin tint on the body, hair dye on the hair, shirt/trousers/hat frames); `Game/LookSprites` builds
  the `FarmerLook` that `FarmerSprite` draws with new body-tint and hat layers. `RemoteLook` is removed; a player whose look has not arrived is drawn with the default look. Compose-once
  caching ([ADR-006](ADR-006-layered-character-customization.md) D4) is not done: a farmer is still six batch entries per frame, which the placeholder art does not need yet.
- **D5 — Wardrobe panel.** `WardrobeViewModel` / `WardrobePanel` (key `C`): skin, hair and dye choices, what is worn, the clothing in the inventory. Equip and unequip are encrypted requests.

### Co-ops

- **D6 — Entities.** `Coop` (name as authored text), `CoopMember` (player, role `Founder`/`Member`; a unique index makes it **one co-op per player**), `CoopInvite` (open 72 hours, stored so an offline
  player sees it), `CoopStockItem` (the warehouse: quantity per item kind) and `CoopLogEntry` (member, item, signed quantity, reason `coop.deposit`/`coop.withdraw`, time).
  The warehouse is stock by item, not slots: it has no packing rules and no capacity (no size limit); moves go through the same transfer as inventories.
- **D7 — Rules.** Create (name 3-40 characters plain, or an `i18n{...}` envelope validated by `LocalizedText`, [ADR-010](ADR-010-internationalization.md) D5), invite (founder only, a player in the inviter's place
  by presence (`PlayerPresence.Key`)), accept or decline, leave, kick (founder only). A founder who leaves hands the role to the longest-standing member; a lone founder dissolves an *empty* co-op (refused while the
  warehouse holds items). Members are addressed by a membership id, never an account id.
- **D8 — Warehouse permissions (default).** Any member deposits. The founder withdraws anything in stock; a member withdraws at most their net contribution of that item (their deposits minus
  their withdrawals, from the audit trail), so a member can take back what they put in but not spend others' stock (`CoopRules`). Tools cannot be deposited.
- **D9 — Atomic and audited.** Deposit and withdrawal are one transfer (player inventory leg + warehouse leg) with the log line enlisted; one gate serializes co-op changes. After any change every online member
  gets their own `CoopSnapshot` push (their role, what they may take). Opcode band `0xA000-0xA0FF`.
- **D10 — Panel.** `CoopViewModel` / `CoopPanel` (key `G`) and an invitation dialog; the co-op name is resolved for the current language, the raw envelope is never shown.

## Alternatives considered

- **Look in the map snapshot** — 10 Hz of mostly unchanged bytes; a separate push on join and change costs nothing while idle.
- **Keep the worn item in the inventory and just reference it** — the item could be listed or traded while worn.
- **Warehouse as a slotted inventory** — reuses `InventoryGrid` but invents a capacity the pillars say does not exist and needs per-owner inventory rows that outlive their player.
- **Member withdrawal by the founder's per-member grants** — more flexible, more UI; the net-contribution rule needs no extra state.

## Consequences

- A look change while a client has not asked for looks yet is simply not seen until it does; the join flow always asks.
- Members who leave lose their claim on the warehouse (their membership id changes if they rejoin).
- Co-op state changes are pushed as full snapshots; a very large warehouse makes them large.

## Revisit when

A player may belong to several co-ops, co-op land arrives, the warehouse needs a capacity or price, pillars define finer roles, or the sprite composition cost matters (ADR-006 D4).

## Open owner questions

One co-op per player or several; who may withdraw (the default above); founder succession; whether a dissolved co-op's items go to its founder; clothing sources beyond the starter set; which hairstyles, skins and dyes ship.

## Amendments

### 2026-10-25 - no starter wardrobe, nicknames (owner decisions)

- **Looks D2 changed.** A new player no longer finds five clothes in the inventory: the wardrobe starts empty and clothing is bought at the town shop
  ([ADR-013 amendments](ADR-013-economy-coins-escrow-market-trade.md)). Everything else of D2 stands (worn clothing leaves the inventory, one transaction with the look row).
- **Looks D3 - the nickname travels with the look.** `LookRow` gained `Nickname` (string, appended, protocol version 3). Chosen over putting it in `MapSnapshot` because snapshots
  go out at 10 Hz to everyone and a name almost never changes, while a look row is sent only on join, map change and change: a 12-character name costs a few bytes once per
  join instead of 10 times a second per player. A rename calls `ILookService.AnnounceAsync`, which multicasts the look row to the whole map, so everyone's cache updates;
  `LookChannel` caches the nickname by map player id and `NicknameFormatter` shows it (a guest's neutral `guest-1234` becomes "Khách 1234" / "Guest 1234").
- **Co-op names.** `CoopMemberRow.Nickname` lists members by nickname (membership ids stay the addressing); `CoopInviteRow.InviterNickname` names the founder in the invitation.
  Trade windows and invitation pickers use the map player's nickname from the look cache (the trade packets did not change).
- **Drawn by the engine.** A nickname is drawn above each farmer as a pre-rendered label: `LabelRegistry` interns the text and uploads it once to `renderer.js` (`registerLabel`:
  thresholded glyphs with a black outline, cached as a bitmap) when the name arrives; the frame only records `RenderCommandBuffer.DrawLabel(id, x, y)` (texture id -3). Still
  one interop crossing per frame, no allocation.
