# Critique: sign-in, settings, wardrobe, profile and mail (rebuild-account-settings)

Branch `claude/rebuild-account-settings`, based on `master` at 384cb1d. Frames are in
[`docs/qa/rebuild-account-settings/`](../qa/rebuild-account-settings/): `before/` is the master build,
`after/` is this branch. Names are `<WxH>-<lang>-<screen>.png` (`vi` or `en`; `390x844` is touch). Each
folder has a `report.json` per configuration with the measured overflow and console errors.

## How this was checked

- Played in headless Chromium against the dev server (Development environment, PostgreSQL), as a guest,
  at 1280×720, 1920×1080 and 390×844 (touch), in Vietnamese and English.
- Every settings tab was opened; every checkbox and slider was changed and the stored value
  (`nosum.settings.v1` in localStorage) was read back. All 11 controls on General, Display and Sound
  changed their state and wrote the store.
- Overflow was measured on each tab body (`scrollHeight` against `clientHeight`), not judged by eye.
- Mail was seeded with two gifts and one expired gift for the new guest, then opened, claimed and deleted.
- **Not played:** Google and Microsoft sign-in (no client ids in this container, and they are never
  committed), the remembered-session card on a second visit, a page reload restoring settings, and the
  in-world effect of each setting (the captures show the controls, not the farm after the change).
  Guest-to-account linking needs a finished quest and a linked identity, so the upgrade button itself was
  not reached.

## 1. Bugs found

1. **The account section never showed in the game menu (fixed).** `AccountFormViewModel` was registered as
   transient. The start screen signed the guest in on one instance; the game menu's Profile and Settings
   received another, with no session. So Profile never showed the account block, and for any signed-in
   player the Sign out button there was unreachable. The Settings language buttons also acted on that
   second instance, so a language change from the menu did not reach the account. This comes from the code
   (the second instance has no session and `SetLanguageAsync` only saves for a session); I did not replay
   the cross-session language case. Frames: `before/1280x720-vi-06-profile.png` (no account block),
   `after/1280x720-vi-06-profile.png` (Tài khoản, Khách, Đăng xuất).
2. **Mail gift marks clipped the row (fixed, found while building).** Adding a gift tag to the mail header
   made the title, badges and expiry overrun the 480 px window (`flex-wrap: nowrap`). The header now wraps.
   The clipping was caused by this change, not by master.
3. **Claim-all sent parallel requests and one gift was left (fixed, found while testing).** The first version
   sent every collect at once. In the run, one of two gifts stayed collectable with the status "Đã nhận quà."
   The server reply for the refused one was not captured, so I have not named the exact cause; the likely
   one is the shared bag and wallet. Claim-all now sends the next gift only after the last one is accepted, and
   stops at the first refusal with its reason on the status line. The after run ends with zero gifts left.

## 2. Settings: confusing options, layout, live apply and persistence

| Tab (vi) | What it held on master | Problem on master | After |
| :-- | :-- | :-- | :-- |
| Chung | Language, Tutorial | Short; interface size lived on Display | Language, **Giao diện** (zoom, panel size), Tutorial |
| Hiển thị | Zoom, panel size, names, minimap, motion, contrast, touch, haptics | 601 px of content in 520 px at 1280×720; 896 in 750 at 1920×1080 | Names, minimap, motion, contrast, touch (fits) |
| Âm thanh | Music, SFX, mute | Vibration ("Rung khi mua và bán") was under Display | Volumes and mute, then **Phản hồi** (vibration) |
| Điều khiển | 20 key rows, two per row | 651 px in 468 px at 1280×720; 947 in 660 at 1920×1080; labels wrapped in the 480 px window; the tab bar wrapped "Điều khiển" onto a second row | Window is now L (660 px); three pairs per row (four from 1600 px); phone keeps two |

Frames: `before/1280x720-vi-04-settings-1.png` and `before/1280x720-vi-04-settings-3.png` (the clipped last
row of keys and the wrapped tab), `after/1280x720-vi-04-settings-3.png`, `after/1920x1080-vi-04-settings-3.png`.

**Live apply and persistence.** Every checkbox and slider on General, Display and Sound changed its state and
wrote the store, so none of them waits for a Save. Not checked: a reload restoring the values, and the in-world
effect of each one. The English frames also fit (`after/1280x720-en-04-settings-3.png`).

## 3. Wardrobe

- Hair styles are bare numbers 1–8 under the heading "Kiểu tóc" (`before/1280x720-vi-05-wardrobe.png`). The
  names exist only as tooltips. **Fixed in round 3** ([ADR-102](../adr/ADR-102-guest-progress-and-sign-out-question.md)):
  the name shows under the row ("Đang đeo: …").
- Nothing says that a change is immediate and that everyone on the map sees it, or where clothes are changed.
  Added a note (`after/1280x720-vi-05-wardrobe.png`).
- "Preview before buying" does not apply: looks are free, the server validates each change, and this panel has
  no purchase. No preview was built.
- The character preview was called "about 40 px tall" in round 1. **That was wrong.** Measured in round 4 on the
  current build (`.preview` box, `docs/qa/account-round4/before/report.json`): 96 px tall at 1280×720 and 1920×1080
  (80 px wide and 160 px tall at 1920), 64 px at 390×844. It is the same 16×32 frame as the world sprite, and it fits
  beside the swatches at every size, so it is not changed.
- **Fixed in round 4 ([ADR-109](../adr/ADR-109-wardrobe-appearance-requests-in-order.md)):** choosing a skin and then a
  hair style within a few milliseconds reverted the skin. Each request sends the whole look, built from the last look the
  server had sent, so the second request sent the old skin again. Reproduced on master in all six configurations
  (`before/report.json`: `quick-choices-both-kept` = `no`, skin index 0 after two clicks). Now the requests go one at a
  time, each with the newest choice, and the chosen look shows at once. After: `yes` in all six
  (`after/report.json`), and the frames are in `after/*-wardrobe-after-quick.png`.

## 4. Mail

- No "collect all" on master: each gift opened and collected on its own (`before/1280x720-vi-mail-1-open.png`).
  Added **Nhận tất cả (n)** / **Collect all (n)**, which shows only while a gift is collectable.
- A row with a gift looked the same as one without until it was opened. Added a **Có quà** / **Gift** tag.
- Result (`after/1280x720-vi-mail-2-after-collect-all.png`): both gifts collected, no tags left, and the
  collected mail deletes in one click (`after/1280x720-vi-mail-3-after-delete.png`).
- **Open:** deleting a mail has no confirmation, although the UI standard uses `ConfirmDialog` for removals.
  Not changed; it is the next thing to fix here.

## 5. Guest to account: the upgrade path

- Before: the guest notice said only that a guest "keeps the generated name" and could link a Google or
  Microsoft account, with no statement of what happens to progress or when linking opens. The account block
  was missing, so there was no way to reach linking from Profile.
- After: the account block shows (Tài khoản, Khách, Đăng xuất). The notice says that after the first quest
  the guest can link, that progress is kept, and that the player chooses a nickname. The progress claim follows
  ADR-069 (player id, state and data are kept on link). The unlock follows `RegistrationPromptOnQuestCompleted`.
- **Open:** the upgrade button stays hidden until that first quest, and I did not verify what a guest loses by
  signing out. The notice says nothing about it.

## 6. Start screen

The guest path renders correctly in both languages. The start screen offers Google, Microsoft and the guest
button. The remembered-session card was not exercised, so it is not graded here.

## 7. Grading against `docs/design/ui-standard.md` section 7 (after)

| # | Check | Result |
| :-- | :-- | :-- |
| 1 | Each element in its region | Pass: all panels are centred panels, no floating cards. |
| 2 | Nothing overlaps | Pass in the captures; not measured per element. |
| 3 | Menu column | n/a (not touched). |
| 4 | Edges line up | n/a. |
| 5 | `GamePanel` with S/M/L/XL | Pass. Settings moved to L, which is an allowed size; the rest unchanged. |
| 6 | Three font sizes, no word-by-word wrap | Partial: the Sound label "Hiệu ứng âm thanh" still breaks onto two lines in the M-width windows, and the wardrobe note now wraps to two lines. |
| 7 | No inner scrollbar at 1280×720 and up | Pass: every settings tab at every size measured with zero overflow (`report.json` files). Master failed at all sizes. |
| 8 | Whole, centred item icons | n/a. |
| 9 | `--u` spacing and gap tokens | Pass: no new spacing values; only the existing tokens and the media query for phone. |
| 10 | Vietnamese and English both fit | Pass on the checked frames (Controls, Profile, Mail in EN). |

Buttons (section 8): claim-all is `primary` (the one main action of the mail page); the key list and reset use
`secondary`; the account sign-out keeps `danger`.

## 8. Still open

Round 5 (the list below is the state after it):

- **Closed in round 5:** the wardrobe's equip and unequip were not sequenced with the appearance requests (ADR-109, consequences), and a
  wear showed only when the server answered. Every look change now goes out one at a time, in the order chosen, and shows at once
  ([ADR-120](../adr/ADR-120-wardrobe-look-changes-in-order.md), section 10).
- **Closed since round 1, with the record in the ADR:** hair-style names and the wardrobe caption ([ADR-102](../adr/ADR-102-guest-progress-and-sign-out-question.md));
  the sign-out question for a guest and what a guest loses ([ADR-102](../adr/ADR-102-guest-progress-and-sign-out-question.md)); the
  confirmation before deleting a mail (`MailDeleteOverlay`, round 2); the Sound label that wrapped (round 2).
- **Closed in round 4:** the wardrobe's quick choices reverting each other ([ADR-109](../adr/ADR-109-wardrobe-appearance-requests-in-order.md)).
  Settings reload persistence is now played: the "Show names" switch was turned off, the page reloaded, the guest signed in again, and
  the switch was still off in all six configurations (`docs/qa/account-round4/after/report.json`, `names-off-after-reload`).
  The same store is covered by `SettingsTests` (`Settings_StoredValuesAreLoaded`, `Serialize_RoundTrips`).
- **Still open, not played here:**
  - The in-world effect of each setting. Round 4 checked the switch and the stored value, not that a name plate disappears from
    the farm; the minimap, motion, contrast and touch settings are not played either.
  - The mail "collect all" run that left one gift collectable with the status "Đã nhận quà." (section 4). The round-1 cause was
    not captured. `CollectAll_StopsAtTheFirstRefusal_AndShowsItsReason` covers the refusal path with a fake socket; the live reply was
    not recorded, so the cause is still unknown.
  - Google and Microsoft sign-in, and the remembered-session card (ADR-082). They need client ids, which this container does not have
    and which are never committed.

## 9. Round 4: what was checked and changed

- Self-critique of the account and settings features on the current `master`, where the round 1 to 3 work is already in (ADR-102, the round-2 mail and Sound fixes).
- Played in headless Chromium against the Development server (PostgreSQL), as a guest, at 1280×720, 1920×1080 and 390×844, in
  Vietnamese and English (`tests/browser/account-round4/capture.js`).
- Found and fixed: the wardrobe's quick choices reverting each other (section 3, ADR-109). Four unit tests use the fake socket's held
  answers (`WardrobeAppearanceTests`), and the browser check reproduced the bug before the change.
- Checked, no change: settings reload persistence (six of six), the wardrobe preview size (measured, section 3).
- Frames: `docs/qa/account-round4/before/` (master) and `after/` (this change); see the README there.

## 10. Round 5: what was checked and changed

- Self-critique of the account, settings and wardrobe on `master` at `707138e7`, against the open items in section 8. Picked the one
  open item that was code-fixable without a new feature: the wardrobe's equip and unequip.
- **Changed ([ADR-120](../adr/ADR-120-wardrobe-look-changes-in-order.md), amends [ADR-109](../adr/ADR-109-wardrobe-appearance-requests-in-order.md)).**
  Every look change (skin, hair, wear, take-off) now goes out one at a time, in the order the player chose. A wear or take-off shows on the
  wardrobe at once; a refused change drops out when its answer arrives. Client only: no wire change, no server change, protocol 34.
  Code: `LookChannel` (one queue), `PendingLook` (the three kinds and how each shows), `WardrobeViewModel` (the look, the bag and take-off).
- **Measured, not asserted** (`docs/qa/account-round5/`, `tests/browser/account-round5/capture.js`): on master the wear showed on the
  wardrobe at once in 2 of 6 configurations and was absent at once in the rest; on this branch it shows at once in 6 of 6.
- **Not reproduced:** the take-off and wear sent in one tick. Master passed it in 6 of 6, so the server applied them in the order sent in
  practice. The change removes an ordering guarantee the client did not have; it does not fix a bug seen in the run.
- **Played:** 6 configurations (1280×720, 1920×1080, 390×844, vi and en) on the Development server. A guest claimed a mail gift
  with a shirt and a hat in the game (the starter kit has no clothing, so the gift was seeded in the database). 24 of 24 checks passed
  on this branch, with no page error. The round-4 quick-choice and settings-reload checks still pass on the new queue (six of six).
- **Tests:** `WardrobeAppearanceTests` (take-off waits for its wear; an appearance choice after a wear is sent after it and newer
  choices replace it; each change applies to the look in turn). Full unit suite: 3,779 passed. Browser module tests: 40 passed.
  `wasm/Nosum.Wasm.slnx` builds with `-warnaserror`.
- **Checked, no change:** the Sound label (`Settings_SoundFx` is "Hiệu ứng" since round 2) was not re-graded in round 5.
- **Still open (not changed here):** the in-world effect of each setting (section 8); the mail collect-all run that left one gift
  collectable (section 4); Google and Microsoft sign-in and the remembered-session card (section 8); a refused equip and the bag count
  after a refused change, which are not played.
