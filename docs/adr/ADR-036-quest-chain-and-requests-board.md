# ADR-036 — Quest chain and the Town requests board

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M2 ("Quest chain: tutorial → town requests board
with daily delivery quests paying coins"). Builds on ADR-011 (quests), ADR-013 (one atomic transfer), ADR-018 (calendar,
`IDayClock`), ADR-030 (community board), ADR-033 (onboarding hints).

## Context

One tutorial quest exists (plant, then harvest), and ADR-033 D3 left the server quest chain unbuilt: the first-run hints
are client-side and teach nothing about the shop, the tools or crafting. The Town board is a counter-only panel for
community goals; nothing there gives a player a reason to visit it every day.

## Decision

- **D1 — The tutorial is an ordered chain of quests.** Quest ids stay append-only: the existing `FirstHarvest` (1) keeps its two
  objectives; `TillSoil` (2), `WaterCrop` (3), `ShipProduce` (4), `BuySeeds` (5), `ChopTree` (6) and `CraftChest` (7) are new.
  `QuestCatalog.Chain` gives the order (till → water → plant and harvest → ship at the bin → buy at the shop → chop a tree → craft a
  chest); the current step is the first unfinished one. Every active quest counts the events it matches whatever the order, so a
  player who got ahead is not held back, and every quest is created for a player the first time their quests are read.
- **D2 — Each step pays a small reward through the single transfer.** A `QuestDefinition` carries `RewardCoins`. Completing a quest
  that has one is one transfer (`quest.reward`): the coins arrive and the quest row becomes `Completed` in the same transaction,
  guarded by the row still being `Active`. There is therefore no state "completed but unpaid", no `RewardPaid` column and no
  settle sweep, and a quest completed before this release stays completed and is never paid retroactively. A wallet that cannot
  hold the coins completes the quest without them.
- **D3 — Quest events come from the services that already decide the action.** Appended `QuestEventKind`s: `SoilTilled`,
  `CropWatered` (farm service), `ItemShipped` (shipping bin), `SeedBought` (shop, only for seeds), `NodeBroken` (resource node, subject = node
  kind), `ItemCrafted` (crafting, subject = output item). Each service reports after its own success; none re-validates.
  A failing report never fails the action. Services take `IQuestService` as an optional trailing constructor argument.
- **D4 — Hints.** The client shows the current chain step as the same fading one-line hint as ADR-033 D3 (text by quest key in
  `Catalogue.resx`, objectives and reward from the shared catalogue). The client-side Sell and Shop hints are superseded by
  the chain and are skipped; their stored bits stay valid.
- **D5 — The requests board is a second tab of the Town board panel.** Same counter (`CommunityGoalCatalog.BoardCounterId`), no new counter.
- **D6 — Daily requests are server-wide rows keyed by game day.** For each game day the server holds three requests (slots 0–2):
  "deliver N of item X". They are rolled deterministically from the day number (`RequestRules.Roll`: a SplitMix64 stream seeded
  by the day) from the items in season that the bin prices — the harvest of crops that grow in the day's season plus its forage. The
  three items are distinct; the quantity is the slot's value target divided by the item's base price (clamped), and the reward is the
  base value of the delivery plus a premium of 30 %, rounded down. Rows are written once per day by `DayEnded` (and lazily on first
  read after a restart, which is the same roll), so a later balance change never rewrites a day that was already shown. Rows older
  than a fortnight are deleted by the same sweep. Nothing runs per player.
- **D7 — Each player fills each request once per day.** A fill row `(day, slot, player)` is the primary key, which makes the second fill impossible.
  A delivery is one transfer (`request.deliver`): the items leave the player, the coins arrive, and the fill row is added in the same
  transaction. It is refused away from the board counter (the server's own position record), for a day that is not the current one,
  for a slot that does not exist, and when the player does not carry the items. Deliveries by one player are serialized in process.
- **D8 — Wire.** A new slice `0x6100–0x61FF` of the Quest band: `GetRequests` 0x6100, `DeliverRequest` 0x6101 (encrypted), `RequestsSnapshot`
  0x6140, `RequestResult` 0x6141. Status codes only; names, hints and rewards are text in the resx pair (vi default, en). The client reads
  the board when the tab opens and again after any non-Ok answer, so a board left open over midnight refreshes itself.
- **D9 — Data.** Two tables, `daily_requests (day, slot)` and `request_fills (day, slot, player_id)`, one generated migration.
- **D10 — Early bonus.** A delivery made in the first four in-game hours of its game day (`RequestRules.EarlyWindowMs`: 4/24 of the day
  length, two real hours at the default twelve-hour day) is paid 20 % more on the base reward, rounded down (`RequestRules.PaidFor`). The
  server decides the window from its own clock; the fill row records the amount paid. The snapshot tells the client when the window ends
  (`EarlyUntilMs`) and the share (`EarlyBonusPercent`), and the result says whether the bonus was paid (`RequestResult.Bonus`). The wire
  change is appended only: `RequestsSnapshot` 4–5, `RequestResult` 2. Stored requests keep their base reward.

## Alternatives considered

- **Per-player generated requests** (rolled on login). Rejected: it ticks per player, cannot be shared in chat ("anyone need turnips?"), and the
  pillars are server-wide.
- **Computing the day's requests on demand, no rows.** Rejected: a catalogue or price change would rewrite a day that players already saw.
- **Paying after completion with a settle sweep** (the community-goal pattern). Rejected for quests: a one-row completion fits in the reward's
  transaction, which removes the crash window the sweep exists for.
- **Gating counting by chain order.** Rejected: it punishes players who craft before they ship and would leave existing players with a chain
  that cannot be advanced by what they already do.

## Consequences

- Coins enter the economy at a fixed rate: twenty small rewards once per account (the chain grew from seven to twenty steps when the onboarding
  chain was extended to the fishing, mining, crafting, animals, food, Town board, decorating, pet and emote systems; `QuestCatalog.Chain` is the list) and at most three requests a day per player at +30 % of the
  bin price. The premium is a balance value in `RequestRules` and may change; stored rows keep their reward.
- Quest-reward coin legs have audit reason `quest.reward`, requests `request.deliver`.
- The early bonus adds at most 20 % of a request's base reward, and only for deliveries in the first four in-game hours of the day.
- A board left open across midnight shows yesterday's list until the next action; the first non-Ok answer refreshes it.

## Revisit when

- Quests beyond the tutorial are added (a second chain, repeatable quests): `QuestCatalog.Chain` then becomes one chain among several.
- Requests need rarity (a rare item once a week) or co-op-wide requests.
- `request_fills` grows large enough that the fortnight prune is not enough.
