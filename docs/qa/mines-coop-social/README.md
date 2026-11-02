# QA: mines, co-op on phones, two-player social (branch `claude/mines-coop-social`)

Base `origin/master` (489008a), harness `tests/browser/world-pass8.cjs`, social flow `tests/browser/social-round3-play.cjs`. Dev server
(`DOTNET_ENVIRONMENT=Development`) and dev client (`-p:NosumDevTools=true`), Chromium via Playwright, guest accounts.

## 1. Mine flow: descend floors and ore yield

**Verdict: the harness was the problem, and the game had one real bug.**

- Wire level (the game): the existing `MineWireTests` (ladder hidden under a stone, break it, use it, deepest floor written; elevator;
  another player's floor; floor reset) pass, and a new wire test `MineWireTests.Ore` (copper ore takes three pickaxe swings, the node
  goes for everyone on the floor, the ore lands in the bag) passes. The server descends and yields ore correctly.
- Harness: the steps were recorded as `skipped` and could not find the ladder (the client never learns which stone hides it, and the
  node positions are not in the page). Fixed with a dev-only QA hook `nodes()` (map id plus x,y,kind of the client's node mirror) and
  `target()` (the tile the action key works, from `PlayerActions`).
- Harness: the first attempts teleported the farmer between tiles. A relocation (a teleport included) leaves the floor and forgets it when
  nobody else is on it (`MineService`), so the broken stones and the revealed ladder reset. The harness now never teleports once on a floor.
  It tunnels instead: walk toward a goal, break what stands in the way with the pickaxe, sweep with the sword when blocked, and read the
  farmer's tile from `target()`. The ladder is searched among the far stones (the planner puts it in the farthest quarter).
- **Game bug, fixed:** `NodeChannel` (client) accepted an empty snapshot. A map change reaches the client before the server puts the
  player back on the map (`PlayerService.RelocateAsync` sends `MapChanged`, then joins), so a `GetNodes` sent in that gap came back with no
  map and no nodes, and the mirror stayed empty until the next load. Every elevator ride or ladder descent could hit it. Fix: an empty
  answer is asked again (5 times, 100 ms apart) while the load is current. Test: `NodeChannelTests.Load_AsksAgain_...`.

Harness results (`mine/<lang>/<size>/report.json`): all four combinations pass all four steps (elevator, descend, ore, fight). en 1280x720,
vi 1280x720 and en 390x844 ran on earlier harness versions (before the candidate cap and the waypoint fallback); vi 390x844 ran on the
final version.

Not verified: the monster fight records swings only (`hits: []`); no hit is asserted.

## 2. Co-op window on a phone (390x844)

Captures: `coop/vi/390x844/` and `coop/en/390x844/` (five steps each, pass). `MenuPage.razor.css`: the scroll shade is now six units tall
(it was three), the parchment covers match its height so the shade never shows over the first row at the top, and the shade is darker
(alpha 0.5). Frame `coop/vi/390x844/03-coop-deposit-seeds.png`: the bottom band is a clear dark fade and the "More land" row sits under
it. **Not fully met:** the row is still cut by the window edge and the content does not fit; the cue says it scrolls, but a fit would
need the upgrade section to change, which is a design call (ui-standard §6 allows the cue).

## 3. Two-player social flow

Captures: `social/1280x720/` (vi and en, all steps pass), `social/390x844/` (en, all steps pass; the vi files there are from a run
that shared the machine with other browsers and failed trade, visit, co-op deposit and co-op invite), `social/390x844-vi-alone/`
(vi, run alone: all steps pass). The failures in the shared run were load: the invite did not reach player B while five browsers ran.
No small defect in the social flow was reproduced on its own, so no social code changed.

## Other defects found (not fixed here)

- Mine floors 2 to 9 and 11 to 40 have no `MapName_mineN` string, so the wayfinding label shows the raw map code (`mine2`). Only `mine`,
  `mine1` and `mine10` are named (`Language.resx`, `Language.en.resx`, `WayfindingDecorator.TargetDisplayName`).
- The mine harness is slow (about 10 to 25 minutes per run: the descent searches far stones with real key presses). A future fix would
  be a walk planner with the walkability map.

## Build and tests

- `dotnet build tests/Nosum.Tests.slnx -warnaserror`: 0 warnings, 0 errors.
- `dotnet test tests/Nosum.UnitTests`: 3,745 passed, 0 failed.
- `dotnet test tests/Nosum.IntegrationTests --filter MineWireTests|NodeWire|FarmWire`: 35 passed, 0 failed. The full integration suite
  was started and ended without a summary (killed by its 40 minute timeout); it is not claimed as passing.
- `node --test tests/browser/modules/wwwroot-modules.test.mjs`: 11 passed.
