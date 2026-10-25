# Nosum — roadmap to a complete game

The owner delegated command of the project and ADR approval to the coordinating agent (2026-10-25) with one goal:
finish the game. This file defines "finished" as milestones with a definition of done each, so every wave of work
can be judged against it. Pillars: [`pillars.md`](pillars.md). Decisions: [`../adr/README.md`](../adr/README.md).

A milestone is done when every item is merged into `master`, all `.slnx` build with 0 warnings, the full test suite
is green twice, and a browser run (Playwright, 1280×720 and 1920×1080, vi and en) shows the feature working with no
console errors.

## M1 — Core farming loop (wave 4, in progress)

- Tools: hoe, watering can with capacity, axe, pickaxe, scythe (ADR-021).
- Resource nodes on maps (trees, stumps, twigs, stones, ore, weeds), regrowth on public maps (ADR-021).
- Ground items with automatic pickup; harvests drop (ADR-021).
- Server calendar: seasons of two real weeks, day clock, seasonal crops, growth in watered game days (ADR-018).
- Chunked interest management (ADR-022).
- Visiting private farms and houses (ADR-019); co-op land (ADR-020).
- Tidy UI: one game menu, Minecraft-style inventory, language switch (ADR-017).

## M2 — Progression and content

- At least five crops per season (spring, summer, fall; winter: forage only), including multi-harvest crops.
- Crafting: recipes unlocked by level; chest (storage, placeable outdoors), fence, path, scarecrow, sprinkler,
  furnace (ore → bar). Placeables allowed on the farm, not only in the farmhouse.
- Tool upgrades (copper, iron) at a blacksmith counter: coins + bars, upgraded tools hit harder or water more tiles.
- Energy: every tool use costs energy, restored by sleeping (day rollover) and by eating food; exhausted players
  cannot use tools. Food from crops and simple cooking.
- Skills: farming, foraging, mining with XP and levels 1–10; levels unlock recipes and small bonuses.
- Mine: the `Mine` maps as a shared dungeon-lite of floors with stones and ore (no combat in M2).
- Quest chain: tutorial → town requests board with daily delivery quests paying coins.

## M3 — A living world

- Weather (sun, rain, storm per day from the calendar; rain waters every outdoor crop).
- Day and night lighting; lamps (placeables) light the night.
- Animals: coop and barn buildings on the farm (built with coins + materials), chickens and cows that produce
  eggs and milk when fed and petted daily.
- More public maps: Mountain (carpenter counter moves there), Beach, Woods, Mine entrance.
- Ambient sound and music hooks (client audio through one interop call per frame budget; placeholder audio only if
  licensed for private use) — done as ADR-039: sound commands in the frame buffer, hooks for tools, harvest, pick-up, coins,
  doors, UI, footsteps, rain and season/day-night music ids; the sounds are generated, the music is silent until M6.

## M4 — Social completeness

- Chat: map, co-op and whisper channels, nickname-addressed, profanity-filtered, rate-limited, mute/block lists.
- Friends list with online status; visiting uses it (ADR-019).
- Community goals per season (Pillar 3): the whole server delivers N of a crop to unlock a shared reward.
- Co-op upgrades bought with the warehouse (bigger warehouse, co-op land size).
- Market: anti-cornering limit per item per player; price history per item.
- Mail: system messages (sold items, community goal rewards, co-op invitations) delivered to a mailbox.

## M5 — Production readiness

- Deployment: container images for the host, reverse proxy with TLS in front of the WebSocket, PostgreSQL backups,
  migrations run at deploy, health checks (`deploy/`).
- Security: server key pinning on the client (TOFU) before production; audit of every handler's permission and rate
  limits; account recovery by email for Gmail accounts.
- Operations: structured logs without secrets, metrics (players online, tick time, bytes sent), admin commands
  (kick, ban, mute, grant) behind an admin role.
- Load test: 200 simulated players across maps with tick time and bandwidth inside budget.
- Client: touch controls for mobile browsers, settings (volume, zoom, keybinds), onboarding tips.

## M6 — Own art and audio (required before any public release, ADR-007)

- Replace every Stardew Valley placeholder (maps, tilesheets, characters, items, UI sheets, fonts) with own art in
  the same layouts so the code does not change; then delete the placeholder content from the repository history
  of the public release branch.
- Own music and sound effects.

## Rules for every wave

- Each feature: an ADR (approved by the coordinator after a written critique), one branch, one PR into `master`.
- Owners of hot spots are named in each brief (`review-gates` → "Parallel agents").
- The coordinator merges `master` into each branch, resolves conflicts, rebuilds clean, runs the suite twice, and
  merges; then removes the worktree or branch.
