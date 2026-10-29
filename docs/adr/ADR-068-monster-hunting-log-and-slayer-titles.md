# ADR-068: Monster hunting log and slayer titles

## Status

Accepted

## Context

The bestiary (docs/adr/ADR-059) shows what each monster is, but nothing records how often a player has hunted it. Players have no long-term goal in
the combat loop beyond the next floor. Issue #133 asks for hunting milestones per species and a cosmetic title for the last one.

Kills are decided by the server's defeat path (docs/adr/ADR-045), so the count has to be kept there too: a client can never report its own kills.

## Decision

1. **Milestones are counted on the server.** `BestiaryRules` (`Nosum.Gameplay`) holds the milestones: 10, 50 and 100 kills of one species. A kill
   is counted once per monster defeat by the killer, in the shared defeat path of `CombatService`, through `IBestiaryService.RecordKillAsync`.
2. **Storage.** `player_bestiary` holds one row per player and species with the kill count. `equipped_bestiary_titles` holds at most one row per
   player, present only while a title is worn. Both are generated migrations (`BestiaryKillLog`) and cascade with the player.
3. **Slayer titles.** Reaching the last milestone earns that species' title. A player wears one title at a time and may take it off. The title
   text is a catalogue key (`Monster_<key>_Title`) in both languages; no text goes on the wire.
4. **Wire.** Four packets in the Combat band, with `NosumProtocol.Version` bumped to 8:
   `GetBestiary` (`0xC205`) → `BestiarySnapshot` (`0xC247`), and `EquipBestiaryTitle` (`0xC206`) → `EquipBestiaryTitleResult` (`0xC248`).
   The result is a status code (`BestiaryStatus`): `Ok`, `UnknownSpecies`, `NotEarned`.
5. **Client.** The Skills page shows, under each monster, a progress bar to the next milestone and the species' title, with a button that wears or
   takes off the title once it is earned. The page asks for the log when it opens.

### Not in this decision

- The stat rewards the issue mentions (+1 Max HP, +1 % damage against the species) are not implemented. They change combat numbers and need their
  own rules and balance pass; a follow-up ADR should add them.
- Titles are not shown beside the player's name elsewhere in the game yet.
- The issue's summary lists milestones of 10 / 25 / 50 and its proposal 10 / 50 / 100; the proposal is used.

## Consequences

- Each kill costs one extra read-modify-write of one row, under the per-player lock; the defeat path does not wait on any other player.
- A title is a cosmetic goal with no balance effect, so no combat rule depends on it.
- Adding a milestone or a species later changes `BestiaryRules` and the catalogue only; the stored counts stay valid.
