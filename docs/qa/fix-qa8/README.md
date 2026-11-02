# QA fix round 8 (branch `claude/fix-qa8`)

Fixes the 12 ranked issues of `docs/qa/world-pass8/README.md`. Each fix is its own commit with tests. Captures: `before/` (pass 8, copied)
and `after/` (re-run of `tests/browser/world-pass8.cjs` on this branch), vi and en at 1280x720 and 390x844; each flow has a `report.json`.

## Results per issue

| # | Issue | Fix | Verified by |
|--:|:--|:--|:--|
| 1 | Furnace on real time, stale readout | The server already timed batches on the shifted dev clock; the client mirror counted on the system clock and slept the whole batch. `ServerClock` (calendar server time) now drives the furnace countdown; a running batch is checked once a second. The QA step jumps one game day instead of waiting 16.5 real minutes. | `FurnaceChannelTests`, `ServerClockTests`; `bin` flow collects after the jump |
| 2 | Inventory hint names right-click/Shift on phones | `Inventory_HintTouch` (vi/en) for a touch input | `InventoryViewModelTests` |
| 3 | Stage-0 sprout faint at night (rows 2, 3, 18, 19) | Seeds of those four rows redrawn in `tools/art_repack/crops.py` (`sprout_picture`, about 99 opaque pixels, was 13 to 32) | `CropSeedArtTests` (new test) |
| 4 | Mail opens with nothing selected | The newest mail is selected and read when the box arrives, once per opening | `MailTests` (new test) |
| 5 | Co-op phone: last upgrade row cut, no scroll cue | CSS scroll shade on the menu page body, shown only where content continues. **Partly met:** the row is still cut at 390x844 and the shade is faint (see `after/coop/en/390x844/03-coop-deposit-seeds.png`). A stronger cue or a fit is a follow-up. | Browser capture |
| 6 | Quest card covers a fifth of a phone | Fold state moved to the view model; a phone starts folded | `QuestTrackerViewModelTests` (new test) |
| 7 | Pet notice says "already petted" after success | State notice `Pet_PettedToday` ("Petted today. Back tomorrow."); the refusal text stays on the status line | `PetViewModel`/text tests; `farm` flow |
| 8 | Sign-out buttons wrap | Confirm dialog is the L width (220 units) | `after/account/en/1280x720/03-sign-out-question.png` (one row) |
| 9 | Unused upgrade strings | Removed from both `.resx` and the view model | Build, `Account` tests |
| 10 | Wardrobe does not list owned clothes | Wardrobe shows Worn (with Take off) and In your bag (with Wear) from the existing view model | `WardrobeBagTests` (new test) |
| 11 | `items.png` not reproducible | `--check` compares encoded bytes (it compared pixel lists, which hid an encoder change); `items.png` re-encoded, pixels unchanged | `repack_cc0_art.py --check` exits 0 |
| 12 | "Opening hours: not posted yet" on every card | No merchant hours exist in the data, so the line and its string are removed | Browser capture (`town`) |

## Test and build

- `dotnet build tests/Nosum.Tests.slnx -warnaserror`: 0 warnings, 0 errors.
- `dotnet test tests/Nosum.Tests.slnx --no-build`: unit 3,744 passed; integration 841 passed; 0 failed.
- `node --test tests/browser/modules/wwwroot-modules.test.mjs`: passes (run on the file; passing the directory fails in Node 22).
- Browser flows (`farm`, `town`, `bin`, `account`, `mine`, `market`, `coop`; vi and en; both sizes): 0 page errors. The `mine` flow fails
  `descend-floors` and `ore-mine` as it did in pass 8 (stairs not located; server-placed ore), so those are pre-existing gaps, not regressions.
  The social flow was not re-run.

## Harness changes

- The furnace step jumps one game day (the old comment said a dev clock jump does not finish a batch; it does, once the client follows the server clock).
- The pet step checks the petted-today key (`Pet_PettedToday`) instead of the removed `Pet_AlreadyPetted`.
