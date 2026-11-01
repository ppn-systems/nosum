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
  names exist only as tooltips. **Not changed.**
- Nothing says that a change is immediate and that everyone on the map sees it, or where clothes are changed.
  Added a note (`after/1280x720-vi-05-wardrobe.png`).
- "Preview before buying" does not apply: looks are free, the server validates each change, and this panel has
  no purchase. No preview was built.
- The character preview is about 40 px tall beside three rows of swatches. **Not changed.**

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

## 8. Still open (not in this change)

- Hair-style names in the wardrobe; the wardrobe preview size.
- A confirmation before deleting a mail.
- What a guest loses by signing out, stated in the account block.
- Reload persistence of settings and the in-world effect of each setting, not yet played.
- Google and Microsoft sign-in, and the remembered-session card, need a container with client ids before they can be played.
