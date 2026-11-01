# QA: account round 3 (guest progress from Profile, guest sign-out question, hair-style names)

Captured with `tests/browser/account-round3/capture.js` (Playwright, Chromium) against the Development server (PostgreSQL, `nosum_dev`)
and the client run with `dotnet run` in Development. Each run is one fresh guest, at 1280x720 and 390x844 (touch), in Vietnamese (default)
and English. Decision record: [docs/adr/ADR-101](../../adr/ADR-101-guest-progress-and-sign-out-question.md).

- `before/`: `origin/master` (e2e224f), the client before this change.
- `after/`: this branch.
- `report.json` in each folder: what the run found on the page (`found`, `result`) and the page-error count (zero on every run).

| Frame | Before (master) | After |
|:--|:--|:--|
| `*-profile` | Account block: "Khách" and Sign out only. No way to keep the progress from Profile. | "Lưu tiến trình" heading, the explanation, "Lưu tiến trình bằng tài khoản" button, then Sign out. |
| `*-save-progress-form` | not reached | The link form (upgrade mode: "Save your account" / "Lưu tài khoản của bạn"). With no client ids on this server it shows the nickname field and the notice that Google and Microsoft are off, so no provider button is drawn. |
| `*-signout-question` | not reached: Sign out ends the session at once. | Centre confirm box over Profile: what is lost (farm, coins, belongings), and three answers: "Liên kết tài khoản trước" (link first), "Đăng xuất" (sign out), "Ở lại chơi" (keep playing). |
| `*-wardrobe` | Hair styles as bare numbers 1 to 8. | Each number has its name as a tooltip, and the caption under the row reads "Đang đeo: Tóc ngắn gọn" / "Wearing: Short and neat". |

Measured in `report.json`: the guest sees the save-progress block and the sign-out question at both sizes in both languages. Pressing "Ở lại chơi"
keeps the session (the save-progress block is still there afterwards). On master, Sign out signs the guest out with no question.

## Hair-style names (`Wardrobe_HairStyle_Name_1` to `_8`)

| Id | vi | en | Silhouette in `assets/farmer/hair.png` (row 1) |
|--:|:--|:--|:--|
| 1 | Tóc ngắn gọn | Short and neat | rounded cap |
| 2 | Tóc ngang trán | Straight fringe | flat fringe |
| 3 | Tóc bồng | Bowl cut | round bowl |
| 4 | Tóc dài | Long hair | tall, long at the sides |
| 5 | Tóc gợn sóng | Wavy | wavy top |
| 6 | Tóc vuốt ngược | Swept back | swept to the side |
| 7 | Tóc nhọn | Spiky | peaked top |
| 8 | Tóc xoăn | Curly | curled edge |

The names come from the silhouettes, not from the art brief. An art review may rename a style; it may not renumber one (the id is on the wire).

## Grading against `docs/design/ui-standard.md` section 7 (after)

Grades: **P** pass, **F** fail, **N/A** not applicable.

| # | Check | 1280×720 | 390×844 | Note |
|--:|:--|:--|:--|:--|
| 1 | Every element sits in its region | P | P | Profile block in its section; the question is a centre `ConfirmDialog`, mounted at game level like the mail and pet questions. |
| 2 | Nothing overlaps | P | P | The question covers Profile on purpose (modal). The caption under the hair buttons sits clear of the next section. |
| 3 | Menu column | N/A | N/A | Not touched. |
| 4 | Edges line up | P | P | Profile and wardrobe rows keep their left edges. |
| 5 | `GamePanel` at S/M/L/XL | P | P | No new panel size; the question uses the shared dialog. |
| 6 | Three font sizes, no word-by-word wrap | P | P | The question title is one line ("Đăng xuất?"). The message and the explanation wrap as sentences. |
| 7 | No inner scrollbar at 1280×720 and up | P | P | Profile fits at 1280×720 in both languages; not measured per element. |
| 8 | Whole, centred item icons | N/A | N/A | No item icon changed. |
| 9 | `--u` spacing and gap tokens | P | P | Existing `GameRow`, `GameButton`, `GameSection`; no new CSS. |
| 10 | Vietnamese and English both fit | P | P | Checked in both languages; the English save-progress notice and the question wrap cleanly. |

Buttons (section 8): "Lưu tiến trình bằng tài khoản" is the one main action of the Profile block (`primary` by default); Sign out keeps `danger`.
In the question, "Liên kết tài khoản trước" is `primary` (the advised answer), "Đăng xuất" and "Ở lại chơi" are `secondary`.

## Not verified here

- **Google and Microsoft linking in a browser.** This container has no client ids (and they are never committed), so the provider buttons are off.
  The link path is covered by `AccountServiceTests` (fake verifier from `ExternalSignInTestKit`): a guest linking Google or Microsoft keeps the player id
  and the saved state, and a guest's token cannot resume after sign-out.
- **The 1280×720 question** puts the link answer on its own row, with the two others below it. It is readable and inside the panel; a wider dialog
  would fit all three on one row, but the dialog is shared, so that is left for a later round.
