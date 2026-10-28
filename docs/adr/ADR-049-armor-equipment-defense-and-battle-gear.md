# ADR-049 — Armor Equipment Paperdoll, Defense Mitigation, and Craftable Battle Gear

- **Status**: Decided
- **Date**: 2026-10-28
- **Author**: Antigravity Agent & phcnguyen
- **Area**: `gameplay/Nosum.Gameplay`, `server/Nosum.Infrastructure`, `wasm/Nosum.Wasm`
- **Decides**: How worn equipment (helmets, chestplates, greaves, boss crowns) grants defense attributes that mitigate monster contact damage and counter-attack damage.

---

## 1. Context and Problem Statement

Following ADR-045 through ADR-048 (MMORPG combat, AI roaming, bosses, and magic projectiles), player survivability against deep dungeon monsters and milestone bosses requires defense gear.

Currently, the wardrobe system supports `WearSlot.Shirt`, `WearSlot.Pants`, and `WearSlot.Hat`, but clothing items are cosmetic with 0 defense rating. In an MMORPG:
1. Equipment items should provide tangible damage reduction (Defense rating).
2. Players can forge combat battle gear using ore and monster drops.
3. Legendary boss trophies (such as `BossCrown`) should be wearable on the head, granting top-tier defensive protection.
4. Server authority must validate and calculate damage mitigation using the player's worn look.

## 2. Decision Drivers

- **Server Authority**: The server computes player defense from `PlayerLook` and calculates mitigated damage `Math.Max(1, rawDamage - defense)`.
- **Zero Allocations**: Pure functional calculations in `CombatRules` without allocating memory during damage resolution.
- **Crafting Integration**: Recipes for battle gear utilize newly added monster loot (`SlimeGel`, `MonsterBone`) and forged metal bars.
- **Paperdoll Compatibility**: Reuses existing `WearSlot.Hat`, `WearSlot.Shirt`, `WearSlot.Pants` infrastructure.

## 3. Decision Outcome

1. **Equipment Items**:
   - `LeatherArmor` (ID 405, `WearSlot.Shirt`, Defense: 3, ObjectIcon: 116 from `dawnlike/Items/Armor.png`).
   - `IronChestplate` (ID 406, `WearSlot.Shirt`, Defense: 7, ObjectIcon: 117 from `dawnlike/Items/Armor.png`).
   - `IronGreaves` (ID 422, `WearSlot.Pants`, Defense: 5, ObjectIcon: 118 from `dawnlike/Items/Armor.png`).
   - `IronHelmet` (ID 442, `WearSlot.Hat`, Defense: 4, ObjectIcon: 119 from `dawnlike/Items/Armor.png`).
   - `BossCrown` (ID 518, `WearSlot.Hat`, Defense: 8, ObjectIcon: 113 from `gold_treasure`).
2. **Formulas in `CombatRules`**:
   - `CalculateDefense(hatItemId, shirtItemId, pantsItemId) => ArmorDefense(hat) + ArmorDefense(shirt) + ArmorDefense(pants)`.
   - `MitigateDamage(rawDamage, defense) => Math.Max(1, rawDamage - defense)`.
3. **Crafting Recipes**:
   - `LeatherArmor`: 4 SlimeGel + 10 Fiber (Combat Level 1).
   - `IronHelmet`: 3 IronBar + 2 MonsterBone (Combat Level 2).
   - `IronGreaves`: 4 IronBar + 2 CopperBar (Combat Level 2).
   - `IronChestplate`: 5 IronBar + 2 Coal (Combat Level 3).
4. **CombatService Integration**:
   - Optional `ILookService` injection reads the player's current look.
   - Incoming monster contact and counter-attack damage is mitigated before deducting player HP.

## 4. Consequences

### Positive
- Rewarding gear progression loop: defeat monsters -> gather drops -> forge armor -> conquer deeper dungeon floors.
- Full backwards compatibility with existing cosmetic shirts and hats.
- Sourced from CC-BY `dawnlike/Items/Armor.png` and CC0 packs.
