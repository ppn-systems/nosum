# ADR-043 — NPC merchants and the menu split

**Status:** Proposed (awaiting the coordinator). Amends [ADR-034](ADR-034-market-at-the-trader-counter.md) D4, the counter pages of
[ADR-013](ADR-013-economy-coins-escrow-market-trade.md) (amendments), [ADR-028](ADR-028-farm-buildings-and-animals.md) (the carpenter's page) and
[ADR-030](ADR-030-community-goals-and-market-limits.md) (the board's page): they were menu tabs, they become dialogs of a merchant. Follows the counter model of those ADRs.

## Context

Selling and trading are done at counters (ADR-013, ADR-030, ADR-034), but a counter is a bare tile: the player stands on it, presses the action key, and a tab
appears in the game menu next to the settings tab. The menu then mixes two kinds of page: the player's own things (inventory, wardrobe, friends, mail, profile,
settings) and the world's (the seed shop, the carpenter, the blacksmith, the market, the Town board, an open chest or furnace). Mail is further hidden behind the
settings cog: its unread badge sits on the gear. The owner's rule: trading and selling must have someone to talk to, and the menu must not be where they live.

## Decision

- **D1 — One merchant per counter.** `Nosum.Gameplay/Npcs` adds `NpcDefinition` (id, map, counter id, sprite key, portrait key, tile, actions) and `NpcCatalog`. There is
  one merchant for every `ShopPoint` of the Town, seven in all: the seed shopkeeper, the tailor (clothing), the furniture seller, the blacksmith, the carpenter, the
  trader (the market) and the board keeper (the Town board). The counter tiles, the `ShopPoint` ids and `ShopCatalog` are unchanged; the catalogue reads each
  merchant's tile from the map's `ShopPoint`, so moving a counter moves its merchant. A test checks that every counter of every map has exactly one merchant and that
  every merchant serves a counter of its map. **The animal seller** is the carpenter: animals are bought at his counter (ADR-028 D3) and there is no separate animal
  counter. A separate seller needs a counter of its own on a map, which is a map change outside this ADR.
- **D2 — Presentation only; the server decides.** A merchant is how the client shows a counter. Nothing is sent about merchants, no packet or opcode is added or changed,
  `NosumProtocol.Version` stays, and the server still checks that the player stands at the counter for every purchase, listing and upgrade (`ShopRules.IsAtCounter`).
  A modified client that skips the dialog gains nothing. Merchants have no collision and no server presence.
- **D3 — The dialog box.** The action key at a counter, or a click or tap on its merchant while the farmer is within reach of that counter, opens a **dialog box** at the
  bottom of the screen where the hotbar is: the merchant's portrait, name, role and a greeting, then one button per action the counter supports, then Leave.
  Buy opens what the counter sells (a shop's list, or the market's browse page); Sell opens the market's page for listing a stack; Build opens the carpenter's buildings and animals;
  Upgrade opens the blacksmith's tool upgrades; Goals opens the Town board; Talk shows another line (two per merchant, taken in turn). The seed, clothing and furniture
  shopkeepers offer Buy and Talk; the trader Buy, Sell and Talk; the carpenter Buy, Build and Talk; the blacksmith Buy, Upgrade and Talk; the board keeper Goals and Talk
  (`NpcActions`). Buy, Sell, Build, Upgrade and Goals open a **standalone dialog** over the game: one window with one title and a close button, no tabs. The same
  panels as before fill it (`ShopPanel`, `MarketPanel`, `GoalsPanel`); `ShopPanel` can show only the list or only the sections under it. Its close button goes back to the
  merchant's box; Leave, Escape, walking out of reach of the counter (`PlayerActions.CounterLeft`) or opening any other page ends the conversation. An open **chest or furnace** is a
  standalone dialog too: it is a thing of the world, not of the player. The routing lives in `NpcDialogViewModel` (stages Closed, Speaking and Trading; the page and the panel it
  asks for); `Home.razor` only shows what the view model says.
- **D4 — The menu holds player things.** `ToolbarViewModel.Items` stays the list of menu tabs: inventory, wardrobe, crafting, trade, co-op, visitors, friends, **mail**, skills,
  profile and settings. The shop, chest, furnace, market and board tab items are removed. **Mail has its own HUD icon** next to the settings cog, with the unread badge, and
  its own tab that carries the badge too. **The settings cog holds settings only**: its tooltip no longer mentions mail. `PanelKind` values are unchanged (the enum is
  append-only); `Shop`, `Market`, `Goals`, `Chest` and `Furnace` stay as the kinds of the standalone dialogs.
- **D5 — Art.** A merchant is a 16 x 32 character drawn in the world with a two-frame idle (`assets/sprites/npcs/{key}.png`, 32 x 32: frame 0 stands, frame 1 breathes one pixel
  lower; with reduced motion frame 0 stays) and a 32 x 32 portrait (`{key}_portrait.png`). They are drawn by `tools/art_repack/npcs.py` in code, as the farmer and the animals are:
  the palette of ADR-040, three-tone ramps lit from the top left, a 1 px outline, and a distinct outfit for each merchant (a straw hat and a seed apron, a beret and a tape
  measure, a waistcoat and glasses, a leather apron and a hammer, a cap and overalls, a plumed hat and a coin purse, a visor and a scroll). `repack_cc0_art.py --only npcs`
  writes them, `--check` verifies them, and `docs/art/provenance.md` lists them (`ArtProvenanceTests`). Merchants are depth-sorted world sprites (`NpcDecorator`), so the farmer walks in front of and behind them.
- **D6 — Where a merchant stands.** On the counter's row, one or two tiles beside the counter tile, never on it (the farmer stands there) and never behind it: the door tile
  behind a counter carries the shop's awning on a front layer, which would draw over the merchant's chest. A test loads the Town and checks that every merchant stands on
  walkable ground and that no front-layer tile is over its feet or its head.
- **D7 — Text.** Name, role, greeting and two talk lines of every merchant, and the button labels, are `.resx` keys in both languages (`Npc_{id}_Name`, `_Role`, `_Greeting`,
  `_Talk1`, `_Talk2`; `Npc_Action_*`; `Npc_Leave`). Vietnamese is the default. No display text is on the wire. A test checks every merchant has all five in both files.
- **D8 — Clicking a merchant.** The canvas handles `click` in Blazor and passes the position to the scene (`GameLoop.PointerClicked`), which tests it against the merchants on its next
  frame with the camera and zoom of the frame before. There is no new JS and no interop in the frame; a click that misses a merchant, or hits one out of reach, does nothing.

## Alternatives considered

- **Keep the tabs, move them left of the settings tab** — still a menu for world things, and nobody to talk to.
- **Merchants as server entities** — nothing about a merchant is gameplay: it adds packets, presence and a protocol bump for a picture.
- **Stand the merchant on the door tile behind the counter** — the awning is drawn over the merchant's chest, so every merchant would wear stripes.
- **Dialogue trees and quests from merchants** — wanted later (see Revisit), not needed to give trading someone to talk to.
- **One sheet for all merchants** — an index to keep in step with the catalogue; one small file per merchant needs none, and a map loads only its own.

## Consequences

- Buying costs one more click than before (the action key, then Buy). The box makes it clear who runs the counter and what else they offer.
- The menu is shorter and the same on every map. A player at a counter and a player in the menu never see the same page.
- 14 small image files join `assets/` (about 6 KB), all generated; `Nosum.Wasm.csproj` links them into the bundle.
- Any future counter needs a merchant: the coverage test fails until it has one, with art and text in both languages.
- The animal seller the owner named is not a separate character until a counter exists for it.

## Revisit when

A second map gets counters, the final Town art moves a door or an awning (the placement test says which merchant to move), merchants should walk, close at night or give quests,
or a counter for animals is added to a map.
