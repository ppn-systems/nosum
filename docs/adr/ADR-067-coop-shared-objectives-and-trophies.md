# ADR-067: Co-op shared objectives and guild trophies

**Status:** Proposed (agent-written, 2026-10-29; owner approval pending). Builds on [ADR-014](ADR-014-coops-and-looks.md) (co-ops and the warehouse), [ADR-030](ADR-030-community-goals.md) (server-wide goals) and [ADR-038](ADR-038-coop-upgrades.md) (co-op upgrades and the snapshot). Answers issue #137

## Context

Co-op upgrades ([ADR-038](ADR-038-coop-upgrades.md)) spend the warehouse on capacity, so a co-op's progress is infrastructure only. Issue #137 asks for shared goals that a co-op works toward together (for example, harvesting 500 strawberries as a co-op), with a visible reward that every member sees. The server-wide goals of [ADR-030](ADR-030-community-goals.md) are for the whole server; a co-op goal is for the members of one co-op.

## Decision

- **D1 — Objectives are data.** `CoopObjectiveCatalog` (`gameplay/Nosum.Gameplay/Coops/CoopObjectives.cs`) lists objectives: a stable numeric id (append-only, the only name that crosses the wire), a key for the localization entries, the crop whose harvests count, and the target. Three crops ship first (parsnip, radish, strawberry). Each crop belongs to at most one objective.
- **D2 — Harvests feed the ledger, kept on the co-op row.** A harvest of a crop that an objective counts adds to that co-op's ledger: `coops.objectives`, a `jsonb` map from objective id to `CoopObjectiveProgress` (`contributed`, `completed_at`), defaulting to `{}`. Only a harvest on the co-op's own land counts, by a member of the co-op that owns it: `FarmService` already verifies that membership on co-op land, so the count costs no lookup. It reports the harvested item and count in `FarmUseOutcome` (`Subject`, `Quantity`), and the farm handler calls `ICoopService.ContributeAsync` after the harvest is committed. A harvest on a member's personal farm does not count (see Alternatives). The call runs under the co-op gate, so the ledger is read, added to and written without a lost update, and it drops the co-op's directory entry after the save (ADR-035 D5).
- **D3 — The trophy is earned once.** A harvest that reaches the target sets `completed_at`; later harvests add to the total but never change it (`CoopObjectiveRules.EarnsTrophy`). A completed objective is the co-op's trophy, shown to every member. There is no separate trophy table.
- **D4 — A failing ledger is not a failing harvest.** The harvest is committed and answered before the contribution runs. A failure in the contribution is swallowed in the handler (as quest progress is in `FarmService.ReportAsync`), and the next harvest counts again.
- **D5 — Wire.** `CoopSnapshot` gains `Objectives` (a list of `CoopObjectiveRow`: id, contributed, completed) appended at `[SerializeOrder(10)]`. Every online member gets the new snapshot when the ledger moves, as for any co-op change. `NosumProtocol.Version` becomes 10. No new opcode and no client request: progress is read with the co-op.
- **D6 — Client.** The co-op panel has a "Shared objectives" section: each objective's title (named after the crop through the item catalogue), a progress bar, the count, and a "Trophy earned" mark once complete. Titles and the trophy line are `Language.resx` strings in Vietnamese and English.

## Alternatives considered

- **A placeable trophy item on completion.** The issue suggests a golden scythe placeable for the co-op farmhouse. A placeable needs a sprite, a placement rule and a map slot, which is art and placement work beyond this change. A trophy that the panel lists needs none of it; a placeable can follow as a reward on the same completion event.
- **Mining counts toward objectives too.** The issue names mining as well. Mined ore is granted through the mine handlers, which have no co-op hook today; adding one is a second call site, so it waits for a follow-up.
- **Quest-style progress through `IQuestService`.** Quests are per player. A co-op total would need a second ledger keyed by co-op anyway, and the quest path does not know co-op membership.
- **A table of its own for the ledger.** A `coop_objective_progress` table was the first design. Every co-op view would then read one more table, and `CreateCoop` went from 7 to 8 database reads, which the round-trip budget (ADR-035) does not allow us to raise. The ledger is a small fixed set of counters that belong to the co-op, like the upgrade levels of ADR-038, so it lives on the co-op row and is read with it.
- **Contributions held per member.** A per-member breakdown would make the panel show who harvested what. It is more rows and a wire change for a display nobody asked for; the total is what the objective needs.
- **Any member's harvest, on any map.** Counting a personal-farm harvest needs the harvester's co-op on every harvest, which is one more database read on the hot path of an action the round-trip budget (ADR-035) measures: the `UseItem / harvest a mature crop` probe went from 5 reads to 6. The co-op land is where members already work together and where the membership is already read, so the rule is cheap and matches the pillar. Counting personal-farm harvests is an owner question.
- **Counting the harvest inside `FarmService`.** The farm service is constructed before the co-op service (the co-op service depends on it), so the farm service cannot call the co-op service without a construction cycle. The handler is the one place that knows both.

## Consequences

- One new column (`coops.objectives`, migration `CoopObjectives`) and a snapshot that grows by one list. The co-op view reads no extra row, so the `CreateCoop` budget is unchanged. A harvest on co-op land that counts costs one ledger read and write in its own transaction, with the membership from the shared directory (a cache hit in production). A harvest anywhere else reads nothing extra.
- The ledger is a JSON document, so its keys are checked by the code (`CoopObjectiveCatalog`), not by the database. A catalogue entry that is removed leaves its key in old rows; the view ignores keys it does not know.
- The objectives are permanent for the server's life. Seasons (the issue's "seasonal milestones") are not modelled yet; they need a season field and a rule for resetting a ledger, which is a separate decision.
- A co-op that dissolves takes its ledger with it (cascade).
- Harvests are counted at the moment of harvest, not when the produce is picked up from the ground. A harvester who never picks the produce up has still harvested.

## Revisit when

Mining or other sources count toward objectives, seasons reset the ledger, a placeable trophy is wanted, or members should see each other's contributions.

## Open owner questions

The three crops and their targets (300 parsnip, 200 radish, 500 strawberry) are defaults; whether the trophy should also be a placeable; whether seasonal objectives replace the permanent ones; whether harvests on a member's own farm should count too.
