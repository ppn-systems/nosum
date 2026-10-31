# ADR-081: Backpack upgrades and merchant doors

**Status:** Proposed

Amends [ADR-043](ADR-043-npc-merchants-and-the-menu-split.md) (the merchants' dialog box, D3, and the menu column, D4) and reuses the blacksmith's counter
pattern of [ADR-026](ADR-026-tool-upgrades-and-the-mine.md) D2.

## Context

The HUD menu column held seven panels that are opened from a merchant in Town: the wardrobe, the co-op, and the rest. The merchants already offered
Buy, Sell, Build, Upgrade and Goals; each new panel is one more action. The bag was fixed at 36 slots for every player, with no way to grow it.

## Decision

- **D1 — Merchant doors.** Two actions join `NpcActions`: `Wardrobe` (tailor) and `Coop` (board keeper). The tailor also offers `Backpack`. The
  wardrobe, co-op and backpack pages open from the dialog box as dialogs of their own, like Market and Goals. Each panel has one door: the column and
  the game menu do not list the wardrobe or the co-op. The keyboard shortcuts still open them. The column is fixed by the decision table in
  `docs/design/ui-standard.md` section 2.1.
- **D2 — Backpack sizes.** A backpack grows by one row of nine: 36, 45, 54 slots. Nine columns keep the grid the width of the hotbar row, so
  the larger bag only adds a row. Prices are server rules (`BackpackRules`): 600 coins for 45, 1,500 for 54. Sizes and prices are sent on the wire
  (`BackpackState`), so the client shows what the server will charge.
- **D3 — Server-authoritative purchase.** `GetBackpack` and `UpgradeBackpack` (C2S `0x5004-0x5005`) and `BackpackState` (S2C `0x5043`) are new.
  The server checks the player stands at the tailor's counter (from its own presence record), the size is the next one, and the coins are there.
  The coins and the new size are one transaction: `TransferService` runs the coin leg and an inventory `Grow` operation under the player's inventory
  lock, so either both are saved or neither is.
- **D4 — Persistence.** The bag size is the existing `PlayerInventory.Capacity` column. No schema change is needed, so no migration is generated.
  The size is written by the same save as the coins.
- **D5 — Protocol.** New packets and opcodes only; no existing field or opcode changed. `NosumProtocol.Version` becomes 24.

## Alternatives considered

- **A backpack as an item.** Rejected for now: it needs a "use" action and an item lifecycle. A purchase at the counter is simpler and matches the
  blacksmith.
- **Rows of 12 (36, 48, 60).** Rejected: the grid would no longer match the nine-slot hotbar row, and the L panel would widen.
- **A separate tier column.** Rejected: the size already is the tier.
