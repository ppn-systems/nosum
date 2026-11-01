# rebuild-social: before and after captures, graded

Captures for the social review ([`docs/design/critique-social.md`](../../design/critique-social.md)). `before/` is `master`
(commit `384cb1d`); `after/` is this branch. Both were taken with `tests/browser/social-capture.cjs` at 1280×720 (vi and en),
and at 390×844 (vi and en, an iPhone 14 profile with the viewport pinned to 390×844). The capture log is `capture-log.txt`
in each folder.

The rule is [`ui-standard.md`](../../design/ui-standard.md) section 7. The 1920×1080 captures the rule asks for are **not
in this set**: the review asked for 1280×720 and 390×844. They are the next thing to add before this is merged as a UI PR.

## Frames

| Screen | 1280×720 vi / en | 390×844 vi / en |
| :-- | :-- | :-- |
| HUD on the Town | `town-hud` | `town-hud` |
| Chat line (Enter; phone: Chat button) | `chat-open` (desktop) | `chat-tap` |
| Emote wheel | `emote-wheel` | `emote-wheel` |
| Friends page (guest notice) | `friends` | `friends` |
| Mail | `mail` | `menu` (phone) |
| Trade: pick, invite, open, cancel | `trade-pick`, `trade-invite-receiver`, `trade-open-sender`, `trade-open-receiver` | `trade-pick` |
| Co-op: board, founded, invite, joined | `coop-none`, `coop-founded`, `coop-invite-receiver`, `coop-joined-receiver` | |
| Co-op with one deposit | `coop-contributions` | |
| Phone touch layer | | `touch-buttons` (log), `chat-tap` |

Frames that differ between `before/` and `after/` are the chat line, the phone chat button, the friends page
(the "Visit farm" button needs a friend, which a guest does not have, so the frame does not show it), and the co-op page.

## Grading (after)

Checklist items are from `ui-standard.md` section 7. ✔ pass, ✘ fail, — not applicable or not verified in the frames.

| Screen | 1 region | 2 overlap | 3 column | 6 text clipped | 8 icons whole | 9 spacing | 10 vi/en fit |
| :-- | :-- | :-- | :-- | :-- | :-- | :-- | :-- |
| Chat line, desktop (`after/en-1280x720-chat-open`) | ✔ bottom-left | ✔ clears hotbar | — | ✘ guest notice cut at "loca" | — | ✔ | — |
| Chat line, phone (`after/en-390x844-chat-tap`) | ✔ above hotbar | ✘ covers the walk hint ("…stick on") | — | ✘ guest notice cut | — | ✔ | — |
| Chat button on the touch layer (phone) | ✔ stacked above Run | ✔ | — | ✔ | — | ✔ | ✔ (Chat / Trò chuyện) |
| Co-op members with deposit counts (`after/en-1280x720-coop-contributions`) | ✔ | ✔ | — | ✔ | ✘ warehouse grid cut at both edges (pre-existing; same in `before/en-1280x720-coop-founded`) | ✔ "8 deposited" fits the row | — vi frame: see note |
| Friends page (guest) | ✔ | ✔ | — | ✔ | — | ✔ | — (guest notice only; the row is not visible to a guest) |

Note on the co-op rows: the vi co-op frame in `after/` was taken before the footnote was reworded. The text was checked in
the rebuild and the phone layout of the co-op page was not captured, so both are open.

Trade (`trade-open-sender`, unchanged): item 8 ✘, the bag grid cut at both edges (pre-existing, finding 9 in the critique).

## What the grades mean for the PR

Not merged as a clean UI PR yet. Three items must be done before the PR is marked ready, and they are the only blockers
the grading found:

1. The guest notice in the chat input is clipped on both sizes (the input box is too narrow for the text).
2. On a phone the open chat box covers the walk hint line. The chat's bottom offset clears the hotbar but not the hint.
3. The bag and warehouse grids are clipped in trade and co-op (finding 9). This is older than this branch and is not fixed here.

Items 1 and 2 are small layout fixes in `ChatLine.razor.css` and `GameHud.razor.css`. Item 3 is in the shared `ItemGrid`.
