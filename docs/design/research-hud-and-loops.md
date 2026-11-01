# Research: HUD, gameplay loops and co-op UX in cozy farming games

Status: design research, 2026-11-01. No code changed. Owner: design researcher (coordinator reviews).
Companion to [`pillars.md`](pillars.md), [`roadmap.md`](roadmap.md) and [`ui-standard.md`](ui-standard.md).
Every recommendation is written to fit the pillars (co-ops, shared market, server-wide seasons), the
server-authoritative rule ([ADR-003](../adr/ADR-003-server-authoritative-websocket.md)) and the nine HUD
regions of `ui-standard.md` section 2.

## How to read this

- **Good games commonly do** — patterns that recur across the genre. We do not copy any one game.
- **Nosum today** — what the repo does now, with file references.
- **Gap** — the difference that matters for the player.
- **Recommendations** — 1 to 3 per topic, ranked, each sized **S** (one area, no new ADR, no wire
  change), **M** (one ADR or a wire/data change across areas) or **L** (a new system with its own ADRs).

The consolidated, ranked backlog of 12 upgrades is at the end.

## Method and limits (read before trusting the numbers)

- Web search was used in standard and extended mode. Results for the genre are mostly wiki pages,
  press and forum threads. There is little primary design material and no retention data. Where a
  claim rests on a secondary source, the text says so.
- Two files named in the brief, `docs/design/economy.md` and `docs/design/combat-balance.md`, are not
  on `master` and have never been committed in this repository's history. The economy rules were read
  from code instead (`gameplay/Nosum.Gameplay/Economy/CoinRules.cs`, `gameplay/Nosum.Gameplay/Shops/ShopRules.cs`).
  The combat balance is out of scope here.
- Two drift points were found and should be fixed by their owners (not by this PR):
  - `pillars.md` says a time-of-day tint of the world is "not built yet", but
    `gameplay/Nosum.Gameplay/Weather/AmbientLight.cs` defines a tint with rain and storm darkening.
  - The header comment of `wasm/Nosum.Wasm.Controls/Layout/GameHud.razor` lists five regions, while
    `ui-standard.md` section 2 defines nine.
- The `GameHud` component lives in `wasm/Nosum.Wasm.Controls/Layout/`, not in `wasm/Nosum.Wasm/Components/`.

## Source index

Cited inline by short name. Access level: **[fetched]** = page read in full; **[snippet]** = seen in
search results only; treat as lower confidence.

| Short name | Source | Access |
| :-- | :-- | :-- |
| SV-Energy | Stardew Valley Wiki, Energy — https://stardewvalleywiki.com/Energy | fetched |
| SV-Inventory | Stardew Valley Wiki, Inventory — https://stardewvalleywiki.com/Inventory | snippet |
| SV-Shipping | Stardew Valley Wiki, Shipping Bin — https://stardewvalleywiki.com/Shipping_Bin | snippet |
| SV-Bundles | Stardew Valley Wiki, Bundles — https://stardewvalleywiki.com/Bundles | snippet |
| SV-Mobile | Stardew Valley Wiki, Mobile Controls — https://stardewvalleywiki.com/Mobile_Controls | snippet |
| SV-MobileForum | Stardew forum, "Mobile touch controls rework, quality of life improvements" — https://forums.stardewvalley.net/threads/mobile-touch-controls-rework-quality-of-life-improvements.15044/post-87609 | snippet (the hold-to-act request was seen in results for this thread; exact post not confirmed) |
| SV-Coop | Steam discussions on Stardew co-op (host must be online; shared vs separate wallet) — https://steamcommunity.com/app/413150/discussions/0/1692669912407029088 | snippet |
| SV-CoopPress | The Next Web, 2018, on Stardew multiplayer — https://thenextweb.com/gaming/2018/04/30/stardew-valley-multiplayer-lets-you-turn-your-friends-into-laborers/ | snippet |
| CI-Coop | PCGamesN, Coral Island multiplayer — https://www.pcgamesn.com/coral-island/multiplayer | snippet (sources conflict on when co-op arrived) |
| CI-Shop | Coral Island Wiki, Coin — https://coralisland.wiki/wiki/Coin | snippet |
| JP-Juice | Game Developer, "Is your game juicy enough?" (Jonasson and Purho's talk, GDC Europe; dated 2013 by the article, 2012 by other listings) — https://www.gamedeveloper.com/design/video-is-your-game-juicy-enough- | fetched |
| Juice-Counter | Game Developer, "Video: Indies resist the urge to juice it or lose it" (a GDC Europe Independent Games Summit talk arguing that polish can cost immersion) — https://gamedeveloper.com/design/video-indies-resist-the-urge-to-juice-it-or-lose-it- | snippet (speaker not confirmed in results) |
| Kenney | Kenney, "must see videos for indie developers" — https://kenney.nl/learn/must-see-videos-for-indie-developers | snippet |
| Unity-HUD | Unity, "Video game UI/UX basics: How games talk to players" — https://unity.com/blog/video-game-ui-ux-basics | snippet |
| Arboreal | Game Developer, "The UI and UX of Arboreal" (farming/adventure hybrid; double hotbar abandoned) — https://www.gamedeveloper.com/design/the-ui-and-ux-of-arboreal | snippet |
| Strayspark | Strayspark, game UI/UX principles (the "minimum viable HUD" idea) — https://www.strayspark.studio/blog/game-ui-ux-design-principles | snippet |
| Wayline-Onb | Wayline, "Designing effective game tutorials and onboarding" — https://www.wayline.io/blog/designing-effective-game-tutorials-and-onboarding | snippet |
| Wayline-Trap | Wayline, "Tutorial trap: onboarding players" — https://www.wayline.io/blog/tutorial-trap-onboarding-players | snippet |
| GAG-Basic | Game Accessibility Guidelines, basic level — https://gameaccessibilityguidelines.com/basic/ | fetched |
| Xbox-Text | Xbox Accessibility Guidelines, XAG 101 text display — https://devdocs.xbox.com/build/game-principles/accessibility/xag-deep-dives/xag-101-text-display.md | snippet |
| WCAG-2.5.8 | W3C, Understanding SC 2.5.8 Target Size (Minimum) — https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html | fetched |
| Apple-HIG | Apple Human Interface Guidelines, 44 pt minimum target (cited secondhand, not fetched) — https://developer.apple.com/design/human-interface-guidelines | secondary citation only |
| Boar-Fest | The Boar, "Celebrating the seasons: festivals in farm life sims" — https://theboar.org/2025/03/celebrating-the-seasons-a-look-at-festivals-in-farm-life-sims | snippet |
| SV-Seasons | Stardew Valley Wiki, Seasons — https://stardewvalleywiki.com/Seasons | snippet |

GDC: a GDC Vault talk on onboarding in Plants vs. Zombies (George Fan) was mentioned second-hand in a
student reflection. It was not found and is not cited as a source.

---

## 1. HUD layout and information density

**Good games commonly do.** They start with almost nothing on screen and add only what must be
visible all the time; inventory, map, journal and stats are on-demand screens ([Strayspark]). They
rank information by how often the player needs it and place the high-rank items first ([Unity-HUD]).
A dense HUD hides the world; information density should follow the game's pace ([Unity-HUD]). For
simulation and cozy games, in-world (diegetic) cues keep immersion, and a HUD keeps only the state the
player checks constantly ([Unity-HUD]).

**Nosum today.** The HUD is nine dock regions, specified in `docs/design/ui-standard.md` section 2 and
built in `wasm/Nosum.Wasm.Controls/Layout/GameHud.razor` with slots filled in `wasm/Nosum.Wasm/Pages/Home.razor`
(lines 104–190). The top-right row holds calendar, coins, mail, minimap, toolbar and emotes
(`wasm/Nosum.Wasm/Components/HudTopRightBar.razor`). The menu column is capped at eight buttons
(`ui-standard.md` section 2.1 table). Panels open from one door each, chosen in section 2.1.

**Gap.** The structure is sound and enforced. The gap is that the top-right row is already busy
(six items at 1280×720) and the menu column is full at seven plus Menu. Any new persistent state will
have to displace something. There is no rule yet for which information is always visible versus shown on
hover.

**Recommendations**
1. **(S)** Keep the nine-region contract and add one rule to `ui-standard.md`: a new persistent
   readout must replace one, or it lives in a tooltip on an existing element. This protects the
   layout from growth without a new ADR.
2. **(S)** Fix the drift in `GameHud.razor`'s header comment so it lists the nine regions. Doc-only.

## 2. Hotbar and inventory UX

**Good games commonly do.** A quick-select row of 9 to 12 slots driven by number keys, with a second
row or a switch for more ([SV-Inventory]: rows of 12 with keys 1–9, 0, minus and plus, and a Tab
key to switch toolbar). A separate tool bar is a recurring player request in Stardew forums
([SV-Inventory]). Stardew auto-sort skips tools, which keeps the tool slot stable ([SV-Inventory]).
The Arboreal team abandoned a double hotbar because of edge cases in managing items and interacting
in the world; their lesson was cheap, consistent solutions and fewer presses ([Arboreal]).

**Nosum today.** `gameplay/Nosum.Gameplay/Inventory/InventoryLimits.cs` sets `HotbarSlots = 9`, and the
bag is 36 slots (`roadmap.md`, Settled ADR-011). Number keys select a hotbar slot
(`wasm/Nosum.Wasm/Game/PlayerActions.cs:26`). On phones the hotbar wraps to two rows
(`wasm/Nosum.Wasm/Game/MapScene.cs:432`). The inventory is opened from the column with `I`
(`ui-standard.md` section 2.1).

**Gap.** Nine slots is fewer than a farming player's working set (seeds, tools, a fishing rod, food
and a bait or a seed mix). There is no toolbar switch, so the player moves items into the bar by hand
or uses the full panel.

**Recommendations**
1. **(S)** Show the item's energy cost (for tools) and its sell price in the slot tooltip. No new
   state; the data exists in `EnergyRules.BaseCost` and `CoinRules`.
2. **(M)** A second hotbar page on `Tab`, using the existing 36-slot bag, selected on the client only.
   Keeps the wire format unchanged; the server still checks slot ownership on every use.

## 3. Energy and stamina loops

**Good games commonly do.** Stardew's energy is a daily budget. At zero the character is exhausted and
slow; energy can go negative down to a passing-out threshold; sleep restores energy, and going to bed
late restores less; eating restores energy, and the exhaustion status can persist through the day
([SV-Energy]). The maximum starts at 270 and rises only through permanent items ([SV-Energy]). The loop
is a decision each night: when to go to bed and whether to eat. That is where the design tension sits.

**Nosum today.** `gameplay/Nosum.Gameplay/Energy/EnergyRules.cs` sets `MaxEnergy = 270` (the same figure
as Stardew, a deliberate choice), charges 2 energy for hoe, watering can, axe and pickaxe, makes the
scythe free, and discounts cost by one per four skill levels and one more per upgraded tool tier
(`Cost`, lines 35–60). `TrySpend` is a hard stop with no negative energy. `Refill` restores full energy
when the game day rolls over (`Refill`, lines 63–64). `Eat` adds energy up to the cap. The client shows
the bar in `wasm/Nosum.Wasm.Controls/Progress/EnergyBar.razor`. No sleep action is modelled in the
gameplay rules.

**Gap.** The loop has no decision at night: energy refills on rollover whether the player sleeps or
not, so the day has no end-of-day ritual and no choice about bedtime. There is also no warning before
energy runs out; the player learns it when a tool refuses.

**Recommendations**
1. **(S)** In `EnergyBar.razor`, a tooltip with "about N tool uses left" for the selected tool. The
   server still decides every use; the client only estimates from the same cost function.
2. **(M)** A bedtime action (an ADR amendment to ADR-023): going to bed at a set time restores full
   energy; later bedtime restores less, following the Stardew pattern of a daily decision
   ([SV-Energy]). Shows a short day summary (coins, harvests, season day). Server-owned time and refill.
   Reject negative energy and exhaustion for now; they add punishment before the loop is proven.

## 4. Day and season pacing

**Good games commonly do.** Seasons of about 28 days are the genre's default ([SV-Seasons]). Each
season has festivals on fixed days that change the daily list of things to do ([Boar-Fest]); a
festival is a bounded event that is cheap to run and gives the season a shape. Weather re-mixes the
daily plan ([Boar-Fest] on rain and festivals). Animal Crossing pacing is driven by a real-time clock
with seasons and daily events, which gives urgency without a timer per task ([Boar-Fest]).

**Nosum today.** The calendar is server-computed: two real weeks per season, 28 game days, 12 real hours
per game day (`gameplay/Nosum.Gameplay/Calendar/GameCalendar.cs`; `pillars.md`, ADR-018). The client shows
season, day and time in `CalendarClock.razor`, weather in `WeatherIcon.razor`, and a banner at a season
change in `SeasonBanner.razor`. Weather exists (`gameplay/Nosum.Gameplay/Weather/`). No festivals exist.
Community goals (pillar 3) are not built (`pillars.md`).

**Gap.** Pacing is correct but flat: every day looks the same to a returning player. Nothing announces
what today or tomorrow is for, and there is no shared, server-wide event to plan around, which is the
point of Pillar 3.

**Recommendations**
1. **(S)** A "Today" card under the calendar bar's tooltip: day, weather today and tomorrow (the
   server already has the weather rule), and which crops are in season now. No new state.
2. **(M)** One festival day per season, decided by the server calendar, with one server-wide event
   (a shared market bonus or a free seed day). Pillar 3 fit; needs an ADR.

## 5. Crop and animal progression

**Good games commonly do.** Crops differ by growth length and season, with a few multi-harvest
crops later in the game; a player sees at a glance what is ready ([SV-Seasons] for season-bound crops).
Animals need a daily care action that gives a small reward; a missed day has a visible cost. These are
the "needs attention" loops that bring players back each day.

**Nosum today.** Crops list their seasons and grow in watered game days (`gameplay/Nosum.Gameplay/Farm/CropCatalog.cs`;
`pillars.md`, ADR-021). Out-of-season crops wither at the season change. Animals are in
`gameplay/Nosum.Gameplay/Animals/AnimalRules.cs` and `AnimalCatalog.cs`; the coop and barn are M3 on
`roadmap.md`. Multi-harvest crops are M2. I did not verify whether a ready crop shows any marker in the
world; check the farm decorators under `wasm/Nosum.Wasm/Game/` before implementing item 2 of the backlog.

**Gap.** The stick (withering) is clear; the carrot (what is ready, what needs care) is not yet visible
at a glance on the farm. Multi-harvest and a daily animal chore are still ahead.

**Recommendations**
1. **(S)** Ready-to-harvest markers over crops and ready-to-collect markers over animals, drawn by the
   engine as world-anchored items. `ui-standard.md` section 4 allows exactly this. If the client does
   not yet have the crop state, this becomes M.
2. **(M)** Multi-harvest crops (roadmap M2) with a visible "next harvest in N days" in the tile
   tooltip. Growth stays computed from stored day numbers, as today.

## 6. Shop and market UX

**Good games commonly do.** Buying takes a quantity, a price check and a confirm. Selling is usually
a separate, batch action. Stardew's shipping bin pays the same price as a merchant, but the money
arrives the next morning ([SV-Shipping]). Coral Island pays the same through shops and the bin, and the
bin pays at the end of the day ([CI-Shop]). Shops often refuse certain items, so the player needs to
know why an item is not sellable.

**Nosum today.** The shop is a dialog (`wasm/Nosum.Wasm.Controls/Shop/ShopPanel.razor`): a quantity
stepper with minus and plus (lines 16–19, max 2 digits), a Buy button disabled when the player cannot
afford it (line 40), and a Yes/No confirm (lines 28–29). The price rule is `gameplay/Nosum.Gameplay/Shops/ShopRules.cs`
(`Price`, and `IsInSeason`, which limits seeds to those that can be planted now, as `pillars.md` states).
The shipping bin price is `CoinRules.cs` (line 83). Unlimited stock is a settled default (`pillars.md`).

**Gap.** The confirm shows the item and quantity, not the total and the balance after the purchase.
A seed that is out of season is not shown with a reason; the player may think the shop is empty or
broken. There is no sell preview at the bin.

**Recommendations**
1. **(S)** Confirm dialog shows total and the balance left (`ShopPanel.razor` lines 28–29 and its
   view model). Pure client display of server-supplied values.
2. **(S)** Show out-of-season seeds greyed out with the reason ("Plants in Spring") rather than
   hidden. The server keeps the rule; the client only explains it. The reason text goes through `.resx`.

## 7. Onboarding

**Good games commonly do.** Teach one mechanic at a time, just before the player needs it, and by
doing rather than by reading ([Wayline-Onb]). Complex systems wait until later; one developer account
describes an RPG that taught too much in the first hour and recommended hiding systems until hour five
([Wayline-Onb]). A wall of text in a tutorial was reported to correlate with a retention drop after the
first hour ([Wayline-Trap]). Sources here are blogs; the principle is consistent across them.

**Nosum today.** `wasm/Nosum.Wasm.ViewModels/Settings/OnboardingViewModel.cs` runs a step list
(`TutorialStep`, including Sell and Shop) and shows one line for `HintSeconds = 12`, shown in
`wasm/Nosum.Wasm.Controls/Hints/OnboardingHint.razor`. The first quest is the tutorial (`pillars.md`,
`gameplay/Nosum.Gameplay/Quests/QuestCatalog.cs`). ADR-033 D3 sets the hint rule.

**Gap.** Hints are one line and vanish after 12 seconds; a player who looks away misses them, and
there is no way to see them again. The quest tracker shows the next step but does not show the
first-day sequence as a whole.

**Recommendations**
1. **(M)** A "First day" checklist in the quest tracker (top left) that lists the tutorial steps and
   ticks them off, plus a Help tile to replay the hints. Uses existing step data; no wire change if the
   step state is already client-visible.
2. **(S)** Keep the hint on screen until the step is done rather than for 12 seconds, when the step
   is not yet complete. Reuses `OnboardingViewModel` state.

## 8. Touch and mobile HUD

**Good games commonly do.** Farming games on touch use a joystick on the left and two action buttons
on the right, tap-to-move as an alternative, and a toggle to hide the controls ([SV-Mobile]). Thumbs
hide the screen, so critical information must sit outside the thumb zones. Genre sets the scheme:
tap-to-act suits farming more than a platformer ([SV-Mobile]; general guidance, not standard).
Players ask for hold-to-act on crops, since repeated taps on every tile are tedious ([SV-MobileForum]).

**Standards.** WCAG 2.2 SC 2.5.8 (AA) asks for 24 by 24 CSS px per pointer target, or spacing that
keeps a 24 px circle clear of other targets ([WCAG-2.5.8]). Apple's guidance is 44 pt
([Apple-HIG], secondary citation). The Game Accessibility Guidelines ask for large, well-spaced
virtual controls, a simpler alternative, and controls that can be remapped ([GAG-Basic]). Xbox's
guidance is that text size should be the player's choice, and that platform magnifiers do not satisfy
the minimum text size for HUD meters ([Xbox-Text], snippet only; one blog's 28 px figure was not
confirmed).

**Nosum today.** `ui-standard.md` section 6 says touch targets are at least 14 `--u`, the menu column
collapses into one Menu button, and the phone layout uses 14 px body text (section 1). Touch controls
are in `wasm/Nosum.Wasm.Controls/Touch/TouchControls.razor`. The UI scale setting runs 2 to 6 in
`wasm/Nosum.Wasm.ViewModels/Settings/ClientSettings.cs` (line 41). At UI scale 2, `--u` is 6 px, so
14 `--u` is 84 px, far above both 24 px and 44 px.

**Gap.** Touch targets are comfortably large. The gap is interaction: a tool action is one tap per
tile, and there is no hold-to-repeat. The phone exception in `ui-standard.md` keeps the UI pixel at 2,
so the HUD on a phone is tight; the hotbar wraps to two rows.

**Recommendations**
1. **(S)** Hold-to-repeat on the touch action button: a held press repeats the selected tool on the
   next tile the player moves onto, rate-limited by the server's cooldown. Client-only input change.
2. **(S)** Keep the 14 `--u` minimum and add a WCAG 2.5.8 check to the `ui-standard.md` review
   checklist (section 7) for any new small control.

## 9. Feedback: sound, juice and toasts

**Good games commonly do.** Juice is the small response to each action: wobble, squash, a short
sound and a number (Jonasson and Purho's talk: "maximum output for minimum input") ([JP-Juice]). Some
designers argue that too much polish costs immersion ([Juice-Counter]), so juice needs a budget.
Toasts are short, stack at most two, and never cover the action ([Unity-HUD]; matches
`ui-standard.md` section 5).

**Nosum today.** Hit feedback is in place: white flash, knockback, hit-stop and camera shake
(ADR-076), synthesised combat sound effects (ADR-053), a level-up fanfare (ADR-062), and sound
commands in the frame buffer (ADR-039). Banners queue one at a time (`LevelUpBanner.razor`,
`SeasonBanner.razor`). The message line is `wasm/Nosum.Wasm.Controls/Feedback/GameMessage.razor`,
fed by `wasm/Nosum.Wasm.ViewModels/Feedback/GameMessagesViewModel.cs`, which holds one message
for `MessageDuration = 2.5 s`.

**Gap.** `ui-standard.md` section 5 says toasts stack up to two, last 3 s and fade. The code holds one
message at a time for 2.5 s, so a second refusal replaces the first. Farming actions (till, plant,
harvest, sell) have no juice of their own; the combat juice does not reach them.

**Recommendations**
1. **(S)** Make `GameMessagesViewModel` match the spec: a queue of two visible toasts, 3 s each.
   Brings code to `ui-standard.md`; no new kind of popup.
2. **(M)** A small juice pass on farming and selling: squash on the tile when tilled or planted, a
   short pop on the coin badge when the bin pays. Uses ADR-039 sound hooks and the ADR-076 hit-stop
   pattern; world effects are engine-drawn (`ui-standard.md` section 4).

## 10. Multiplayer co-op UX

**Good games commonly do.** Co-op works best when a visitor can act without the host present, shared
and separate wallets are a visible choice, and the group can see what each member did. The complaints
that recur are about host dependency and money ([SV-Coop], [SV-CoopPress]): guests cannot use the farm
while the host is offline, and players argue over shared versus separate wallets. Coral Island added
online co-op for up to four players ([CI-Coop]; sources conflict on the date).

**Nosum today.** A co-op has a shared warehouse with an audit trail, founder and member roles
(`gameplay/Nosum.Gameplay/Coops/CoopRules.cs`), and co-op land where members farm together, with the
harvester keeping the produce (ADR-020; `roadmap.md`). The co-op panel is
`wasm/Nosum.Wasm.Controls/Coop/CoopPanel.razor`. Co-op land is entered from Town's west plaza, and the
visiting rules are in ADR-019. Pillar 1 and ADR-020 describe the warehouse and land as server-owned, so
they should not depend on a host being online; I did not verify that in code. The audit trail is
server-side; I did not verify that the panel shows it.

**Gap.** Members may not see who did what, although Pillar 1 requires it. Presence on co-op land (who
is here now) is not shown in the HUD.

**Recommendations**
1. **(S)** A "Log" tab in `CoopPanel` that lists the warehouse audit entries, newest first, paged. Read
   only; reads the audit trail the server already keeps (verify the channel first).
2. **(M)** Presence on co-op land: a small list of members on the land in the Friends or Co-op panel,
   updated on map join and leave. Uses the existing map-group membership.

## 11. Retention loops

**Good games commonly do.** Retention comes from a daily loop plus a long goal: a daily chore that is
quick, a season that changes what is worth doing, and a shared goal the community works toward. Stardew's
Community Center is a shared goal in six rooms and 30 bundles; rooms unlock as bundles are completed, and
finishing a room gives a reward ([SV-Bundles]). Stardew's festivals and seasons add a scheduled
reason to return ([Boar-Fest]). Sources here are wiki and press; no retention data was found.

**Nosum today.** The daily loop is energy and crops; the Requests board offers daily deliveries
(`gameplay/Nosum.Gameplay/Requests/RequestRules.cs`); the quest chain is in `QuestCatalog.cs`; seasons
and the calendar are live (section 4). The Goals panel (ADR-030) belongs to the board; server-wide
community goals (Pillar 3, roadmap M4) are not built (`pillars.md`).

**Gap.** Nosum has the daily loop and the seasons but no shared long-term goal that the whole server
works on. That is the single feature the pillars name and the one Stardew's Community Center does best.

**Recommendations**
1. **(L)** Server-wide community goals as bundle rooms: each season has rooms of goals ("deliver 200
   parsnips"); completing a room unlocks a shared world change for everybody (Pillar 3, roadmap M4).
   Needs an ADR, a server-owned goal store and a board panel.
2. **(S)** Show the server-wide total of the current goal on the Goals panel as soon as it exists,
   so the contribution feels shared before the full system is built.

---

## Prioritised backlog: the 12 best upgrades

Ranked by player impact per unit of work, with Pillar 3 and the HUD contract weighted up. Each brief
is written to be handed to an implementation agent; each names its owner area and the rule it must
follow. Sizes: S, M, L as defined above. Every brief follows `ui-standard.md` section 7 for any visible
change, with screenshots at 1280×720, 1920×1080 and 390×844.

**1. Toast queue to spec (S).** Make the message line match `ui-standard.md` section 5: up to two
toasts visible at once, each for 3 s with a fade, the newest on top, and no toast covering the hotbar
or touch controls. Owner: `wasm/Nosum.Wasm.ViewModels/Feedback/GameMessagesViewModel.cs` and
`wasm/Nosum.Wasm.Controls/Feedback/GameMessage.razor`. Keep the `.resx` text unchanged. Acceptance:
two refusals in a row both show; neither overlaps the hotbar at 390×844; the existing tests for
`GameMessagesViewModel` still pass.

**2. Ready markers over crops and animals (S, M if the client lacks state).** Draw a small engine
sprite over a tile whose crop is ripe and over an animal that has produced or needs care. Drawn by
the engine as a world-anchored item (`ui-standard.md` section 4), never a Blazor card. First verify in
the farm decorators under `wasm/Nosum.Wasm/Game/` whether the crop state is already on the client;
if not, the server pushes a flag as a tile field, which is a wire change and makes this M. Acceptance:
markers at 1280×720 and 390×844 do not cover the hotbar, and nothing is drawn over the HUD.

**3. Energy tooltip with tool uses left (S).** In `wasm/Nosum.Wasm.Controls/Progress/EnergyBar.razor`
add a tooltip: "about N uses left" for the selected tool, computed on the client with the same cost
function as `gameplay/Nosum.Gameplay/Energy/EnergyRules.cs` (`Cost`). Read-only; the server still
refuses any use it does not allow. Text through `.resx`, body size only. Acceptance: the number matches
the server's refusal point in a test; no new state is stored.

**4. Shop confirm with total and balance; out-of-season seeds explained (S).** In
`wasm/Nosum.Wasm.Controls/Shop/ShopPanel.razor` (confirm at lines 28–29) show the total and the coins
left after purchase. In the seed list, show out-of-season seeds greyed with the reason "Plants in
[season]" rather than hiding them; the rule stays in `ShopRules.IsInSeason`. Acceptance: total equals
`ShopRules.Price`; a greyed seed is not clickable and its reason fits in one line at 390×844.

**5. Hold-to-repeat on the touch action (S).** In the touch controls
(`wasm/Nosum.Wasm.Controls/Touch/TouchControls.razor`), a held action button repeats the selected tool
on each tile the player steps onto, at the server's cooldown rate. The server validates every use; the
client never sends faster than the cooldown. Acceptance: holding for two seconds across four tiles
produces the same server outcomes as four taps; no packets beyond the cooldown.

**6. Co-op activity log (S).** Add a "Log" tab to `wasm/Nosum.Wasm.Controls/Coop/CoopPanel.razor` that
lists the warehouse audit entries (who, what, when), newest first, paged 10 at a time, read-only. This
meets Pillar 1 ("every contribution and withdrawal is recorded, so members can see who did what").
First check the existing channel exposes the audit; if not, the handler change is M. Acceptance:
entries show member nicknames (`ui-standard.md` `i18n` rules: codes on the wire, text in `.resx`).

**7. "Today" card on the calendar (S).** Give the calendar bar's tooltip a card: today's season day,
weather today and tomorrow, and the crops in season now. Data comes from `GameCalendar` and the weather
rule already on the server; no new packets if the forecast is derivable from the epoch and day. Owner:
`wasm/Nosum.Wasm.Controls/Calendar/CalendarClock.razor` (and `WeatherIcon.razor`). Acceptance: the card
is a tooltip inside the existing region; nothing new floats over the world.

**8. Community goal rooms (L).** Server-wide goals per season, as Stardew's Community Center rooms
(`SV-Bundles`): each season offers rooms of goals ("deliver 200 parsnips"); a completed room unlocks a
shared world change visible to every player. The server owns the goal store and progress (Pillar 3);
the client shows the board in the existing Goals panel (ADR-030). Needs an ADR before code, a
PostgreSQL table through `data-model-ef-core`, and a broadcast on completion. Acceptance: two players
on different maps contribute to the same goal; progress survives a restart.

**9. Farming juice pass (M).** Short, bounded feedback for till, plant, water, harvest and the bin
payment: a squash on the tile, a pop on the coin badge when the bin pays, a short sound from the
ADR-039 hooks. Reuse the ADR-076 hit-stop pattern rather than a new effect system. Budget: no effect
longer than 200 ms, and none blocks input (`JP-Juice` on talk and `Juice-Counter` on restraint).
Acceptance: frame time unchanged; effects are engine-drawn; the sound list is in ADR-039.

**10. First-day checklist and hint replay (M).** A "First day" list in the quest tracker (top left,
`QuestTracker.razor`) that shows the tutorial steps and ticks them as they are done, plus a Help tile in
the game menu that replays the hints. Hints stay on until their step is done (`OnboardingViewModel`).
Teach by doing, one step at a time (`Wayline-Onb`). Acceptance: a new player finishes the list without
reading any text outside the quest tracker; the Help tile sits in the game menu, not the column (`ui-standard.md` section 2.1).

**11. Festival day per season (M).** One day per season, decided by `GameCalendar`, with one
server-wide event: a shared bonus at the shipping bin or a free seed day at the seed shop. Pillar 3
fit; `SV-Seasons` and `Boar-Fest` show festivals as the genre's scheduled return. Needs an ADR (it
changes the calendar and the market), server-side enforcement, and a banner through `SeasonBanner`.
Acceptance: every client on a server sees the same festival day; the bonus is applied only by the server.

**12. Bedtime action and day summary (M).** A "Go to bed" action that ends the day: full energy at or
before a set hour, less after it, following the Stardew decision pattern (`SV-Energy`), with a short
summary (coins, harvests, next day's weather). Changes ADR-023 D4 (refill on rollover) and needs an
ADR amendment. Server owns the time and the refill; no negative energy or exhaustion in this first
version. Acceptance: sleeping before the set hour refills to full; sleeping after restores less; the
summary matches the server's ledger.

### Considered and not ranked in the top 12

- A separate tool bar or a second hotbar page (section 2, recommendation 2): useful, but the single
  hotbar works and the top 12 already cover the energy and feedback gaps.
- Diegetic UI for energy (a bar over the player): rejected for now; `ui-standard.md` section 4 keeps
  only the listed world items on the canvas.
- Stardew's negative energy and exhaustion: punishment before the loop is proven; deferred.
- A hold-to-act for every tool on desktop: the mouse already repeats; not needed.

## Verification of this document

- This is a documentation change. No code, tests or assets were modified.
- Every Nosum file reference was checked to exist on `master` at `035d354`. Two file paths named in
  the brief do not exist (`docs/design/economy.md`, `docs/design/combat-balance.md`) and are noted above.
- External claims marked "snippet" or "secondary citation" should be read against the source before a
  design decision depends on them.
