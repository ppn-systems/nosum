# ADR-082: Footwear, boots and the one movement speed multiplier

## Status

Accepted

## Context

Walking speed is fixed at `Walker.Speed` pixels per second for every player. The only thing that raises it is the speed buff in the buff
slot (Swiftness, Brotherhood in Arms; docs/adr/ADR-051, issue #146): the server keeps a per-player factor on the presence, the move validator
(`MoveValidator`, docs/adr/ADR-008) honours it and the client walker applies the same factor. Players have no way to build speed into their
character, and nothing on the paperdoll covers the feet.

The owner asked for boots that raise walking speed, a short speed tonic, and one multiplier that every speed source feeds through, so a
later sprint (running) combines with the same value. The clothing slots (docs/adr/ADR-006, ADR-014, ADR-049) already give the paperdoll
shirt, pants and hat; the feet are the missing slot.

## Decision

### D1. A Feet slot and four boots

- `WearSlot.Feet = 4` (append-only wire value; `WearSlot` is the wardrobe's slot number). Boots are `ItemKind.Clothing` with
  `Wear = WearSlot.Feet`. The wardrobe, `EquipItem` and `UnequipItem` accept the slot like the others.
- `PlayerLook` and `LookRow` gain `FeetItemId`, appended at the end (`LookRow` order 10, protocol version 24). `PlayerAppearance.FeetItemId`
  is persisted (migration `AddFootwearSlot`, generated).
- Four boots, ids 408-411, each with a speed bonus in `ItemDefinition.MoveSpeedPercent`:

| Boots | Id | Walking speed | Running energy saved | Where it comes from |
|:--|:--|:--|:--|:--|
| Leather boots | 408 | +5% | none | Tailor (150 coins) |
| Traveler boots | 409 | +10% | none | Tailor (380 coins) |
| Swift boots | 410 | +15% | 20% (D5) | Crafted at Combat level 3 |
| Winged boots | 411 | +20% | 30% (D5) | Crafted at Combat level 6 |

- The tailor sells the two entry pairs; the faster pairs are crafted with the existing combat recipes, like the armour (ADR-049).
- Prices follow the clothing shop (shirts 60, pants 80, hats 40-50) and the sell prices of the armour (ADR-049). `docs/design/economy.md`
  named by the request does not exist in the repository; the prices here are set against the shop and armour values already in the catalogue.

### D2. The bonus is data on the item

`ItemDefinition.MoveSpeedPercent` carries the bonus. `MovementSpeed.FootwearFactor(itemId)` reads it (1 for no boots and for any item
that is not worn on the feet). No code path names a boot to decide its speed.

### D3. One combined multiplier, clamped

`MovementSpeed.Combine(footwear, buff, run)` returns `clamp(footwear × buff × run, 1, MaxFactor)` with `MaxFactor = 1.5`. The server and
the client call the same function (`MovementSpeed.For(feetItemId, buff, run)`), so the factor the validator uses is the factor the walker
walks at.

- The server keeps the factor on `PlayerPresence`: `SetFootwear` (called when the look changes or the player joins, from `LookService`)
  and `SetSpeedBuff` (from the buff path in `CombatService`) recompute it. Both writes go through a small lock that is only held for the
  assignment; it never waits for `PlayerPresence.Gate`, because the look path can run while the move path holds the gate.
- `MoveValidator.Validate` keeps its `speedFactor` parameter: the budget earns `MaxBudgetSeconds × Speed × factor` and each claim spends its
  distance. A claim above the cap is refused as `TooFast` with a correction (docs/adr/ADR-008).
- The client walker (`MapScene`) passes `MovementSpeed.For(worn boots of the local look, the buff in the client's vitals)`.
- `run` is the running multiplier. It is `1` until running is in effect. The running feature (a separate branch) passes its factor into the
  same function and the same cap; it does not add a second multiplier.

The cap is the hard limit on any stack of sources. Winged boots with Swiftness reach it exactly (1.20 × 1.25 = 1.5).

### D4. The speed tonic uses the Swiftness buff

`ItemCatalog.SpeedTonic` (id 715, food range) is a potion in the existing table `CombatRules.PotionEffect`: `(Swiftness, 5400 s, 1)`. It
reuses `StatusEffectKind.Swiftness` and the buff slot (ADR-051, issue #146); no new buff kind exists. Its duration is three game hours,
`StatusEffectRules.SpeedTonicSeconds = 3 × (DefaultDayLengthMs / 24 / 1000)`: a game hour is a twenty-fourth of the day, 30 real minutes at the
default 12-hour day. The tonic therefore lasts 90 real minutes. The buff slot holds one buff at a time, so the tonic never shortens a
stronger Swiftness.

The tonic's bonus is the Swiftness factor (+25%), not the "+15%" of the first request: the buff slot holds one kind with one factor, and
the request asked for no second speed buff. The tonic is recipe-crafted at Combat level 2 (`RecipeCatalog`, `SpeedTonic`, id 33).

### D5. Running energy saved by footwear

`ItemDefinition.RunEnergySavePercent` (Swift 20%, Winged 30%) and `FootwearRules.RunEnergyCost(baseCost, feetItemId)` give the energy a run
costs with the boots on. Nothing consumes it on master, since running is not on master. The running feature calls this function when it
charges energy; until then the saving has no effect in the game and the tooltip does not claim one.

### D6. Protocol and persistence

- Protocol version 24: `FeetItemId` appended to `LookRow`; `WearSlot.Feet` is a new value. No opcode or packet changed.
- The column `player_appearances.feet_item_id` (integer, default 0) is added by the generated migration `AddFootwearSlot`.

### D7. Art and display

- `assets/farmer/boots.png` (96 × 384, four tier bands, the same frame layout as `body.png`) is drawn by `tools/art_repack/farmer.py`
  (`boot_layer`) and layered after the body and arms, before the hair (`FarmerSprite.Draw`). The default boots in `body.png` stay.
- The four boots and the tonic have inventory icons on `assets/sprites/items.png` (cells 144-148; the sheet is now 384 × 112).
- The tooltip shows one line, `Walking speed: +N%`, when `MoveSpeedPercent > 0` (`ItemTooltip`). Every string is in `Language` and
  `Catalogue`, in Vietnamese and English (ADR-010).
- The wardrobe has a Feet slot in the worn list and the paperdoll preview draws the boots tier.

## Amendments

- **ADR-008 (movement sync)**: the validator's speed factor is now the combined, clamped multiplier of D3. The validator, its budget and its
  corrections are unchanged.
- **ADR-049 (armour)**: a Feet slot joins Shirt, Pants and Hat in the wardrobe. Boots grant no defense.

## Consequences

- Speed has one source of truth on the server and on the client. A new source adds an item field or a buff entry and goes through
  `MovementSpeed.Combine`; it never edits the validator.
- The cap bounds every stack of speed. Winged boots with Swiftness are the fastest a player can walk.
- The presence now holds the footwear mirror, which the join and the look change keep current. A player who logs in with boots walks with
  them from the first move.
- The tonic lasts 90 real minutes at the default day length. This follows the request's "three game hours" literally; the owner may prefer a
  shorter real duration, which is one constant to change.

## Alternatives considered

- **A separate speed buff for the tonic (+15%).** Rejected: the request said to reuse the buff slot, and the slot holds one kind with one
  factor.
- **Speed computed per move from the look cache.** Rejected: the move path runs under the presence gate and the look cache is async. A
  mirror on the presence, updated by the look path, keeps the move path synchronous.
- **Boots as cosmetic only, speed on the client.** Rejected: speed is server-authoritative (ADR-003).

## Revisit when

- Running (the sprint feature) lands: it passes its factor into `MovementSpeed.For` and charges energy through `FootwearRules.RunEnergyCost`.
- The tonic duration or the boots' prices need balancing against the economy: adjust the constants and the catalogue, not the rules.
