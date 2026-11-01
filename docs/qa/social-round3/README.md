# social-round3: two real players, end to end, and the fixes that came out of it

Second browser pass on the social play (docs/design/critique-social.md; round 2 is docs/qa/social-round2/). Round 2 could not play a friend in the
browser, because Development had only guests. This round adds a Development-only test friend (docs/adr/ADR-100) and plays two Playwright contexts
through: friend presence, chat, a trade with both confirmations, a visit to the friend's farm, and a co-op founded by one player with deposits from both.

- `after/`: this branch. Captured with `tests/browser/social-round3-play.cjs` at 1280×720, 1920×1080 and 390×844 (iPhone 14 profile), in vi and en.
  Each run logs its steps to `capture-log-<size>.txt`. Frames are named `<lang>-<size>-<step>.png`; the two players' frames are the same step seen from A
  (the befriending guest) and from B (the test friend, `*-receiver.png`).
- `before/`: the master client (`origin/master` at 3102655) with one change: the renderer module fix below, without which master does not start.
  Captured with `tests/browser/social-round3-before.cjs` (a guest opens Friends). The two-player script cannot run on master at all (see Before).

## Before: master cannot be played with two players

- **Blocker, the page does not start.** On `origin/master` the renderer module declares `fitLabel` twice (`engine/Nosum.Rendering.Web/wwwroot/renderer.js`:
  an import from `label-fit.js` and a leftover local function), so the browser raises `SyntaxError: Identifier 'fitLabel' has already been declared` and
  the game never draws. The leftover came with the label-fit extraction (#305, #306). Fixed here by deleting the dead local function and its helper.
- **No friend to play with.** A friends list needs registered accounts on both sides, and Development has no sign-in (ADR-069). On master the guest sees
  "Guests keep no friends list. Register to use friends." (`before/en-1280x720-friends-guest.png`). The two-player script stops at the befriend step on
  master's client: `window.nosumQa.befriend is not a function` (`before/capture-log-1280x720.txt`).

## What changed

- **The Development test friend** (ADR-100, protocol 33): `DevBefriend` makes the sender a registered test account and creates a registered test
  player (`QaFriend` + four digits) that is befriended through the real request and accept. The reply carries the test player's session token, and
  the second browser context resumes as that player from its remembered session. Production does not map the opcode; `DevQaWireTests` probes it.
- **Renderer module** (`engine/Nosum.Rendering.Web/wwwroot/renderer.js`): removed the duplicate `fitLabel` and the unused `clampTo`. Syntax checked
  with `node --check`; the play script runs the page, so a regression fails every step.
- **Friends row** (`wasm/Nosum.Wasm.Controls/Friends/FriendsPanel.razor.css`): a nickname wrapped mid-word in the desktop row ("QaFr / iend / 4737").
  The name now takes its own line and does not break inside a word. Phone layout unchanged (it already fit).
- **Co-op invite picker** (critique finding 7): a member who is on the map was still offered as "Invite" after they joined. The picker now leaves out
  current members (`CoopInvitePicks`, unit-tested in `CoopInvitePicksTests`). After the fix the picker reads "No other players here to invite."
  (`after/en-1280x720-coop-log.png`).

## Play results (after)

Every step passes in all six combinations (vi and en at each size), with the log files as the record:

| Step | What it checks | Result |
| :-- | :-- | :-- |
| befriend | A gets a test friend and its token | ok, all six |
| friends-online | the friend is in A's Friends list with its place ("At the Town") | ok, all six |
| chat | A's line in the Town is shown to B | ok, all six |
| trade | A offers seeds, B accepts, both confirm, the trade window closes | ok, all six; the database has `trade.swap` rows for it |
| visit-farm | A visits the friend's farm from the list | step ok; see limits |
| coop-deposit | A founds "Test Co-op" at the board and deposits seeds | ok, all six; `coop.deposit` rows in the database |
| coop-invite | A invites B, B accepts, B deposits seeds, A's log shows both | ok, all six |

## Graded (ui-standard section 7)

Graded from the frames looked at. The other frames are captured, and their checks are not yet written down.

| Frame | 1 region | 2 overlap | 6 text clipped | 9 spacing | 10 vi/en fit | Note |
| :-- | :-- | :-- | :-- | :-- | :-- | :-- |
| `en-1280x720-friends-online` (after) | ✔ | ✔ | ✔ name whole | ✔ | ✔ | Name on its own line. Was ✘ (mid-word wrap) before the CSS fix. |
| `en-1280x720-coop-log` (after) | ✔ | ✘ tooltip covers the warehouse | ✔ | ✔ | — | The Axe tooltip is the hover of the mouse left over the slot; the capture does not move the mouse away. Not a layout bug, a capture artefact. |
| `en-1280x720-visit-farm` (after) | ✔ | ✔ | ✔ | ✔ | — | Shows a farm; the frame alone does not say whose. See limits. |
| `before/en-1280x720-friends-guest` | ✔ | ✔ | ✔ | ✔ | — | The state round 3 starts from: no friend list for a guest. |

## Not done, and what is left

- **Visit farm is not proven by a database row.** The step clicks "Visit farm" and the frame shows a farm, but no server row records a visit, so the
  browser check does not prove it went to the friend's farm. Next: an audit or a read of the visiting place.
- **Co-op entry point** (critique finding 4) and the phone door for Trade and Friends (finding 3) are not changed: each needs a design decision.
- **Phone frames are captured but not individually graded** (`after/*-390x844-*`). Desktop frames at 1920×1080 are captured, not graded.
- **Sign-in is still guest-only in Development**, so the test friend is made by the hook, not a real account.

## Reproduce

```
DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host
dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080
OUT_DIR=docs/qa/social-round3/after LANGS=vi,en SIZE=1280x720 NODE_PATH=$(npm root -g) node tests/browser/social-round3-play.cjs
```
The Development host needs the PostgreSQL database `nosum_dev`, owned by the `nosum` role (its Development connection string). The integration
tests use `NOSUM_TEST_POSTGRES`, or their default `nosum` role.
