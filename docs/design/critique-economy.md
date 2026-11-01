# Economy critique (branch `claude/rebuild-economy`)

**Status:** Review and fixes, 2026-11-01. Reads with [ADR-013](../adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-081](../adr/ADR-081-economy-balance-targets.md) and
[`economy.md`](economy.md). The numbers below come from the simulation in `tests/Nosum.UnitTests/Gameplay/EconomyReportTests.cs` (it reads the live catalogs; set
`NOSUM_ECONOMY_REPORT` to write the full table) and from playing the shops and the market in a browser at 1280×720 and 390×844 (captures in
[`docs/qa/rebuild-economy/`](../qa/rebuild-economy/README.md)).

## 1. What one player earns and spends in a season

A season is 28 game days (14 real days). A game day has 270 energy. Tools cost 3, a fishing cast costs 10, and planting, harvesting and animals cost none.

| Source | Per unit | Per game day | Energy | Notes |
|:--|--:|--:|--:|:--|
| One plot of crops (21 crops) | 160–245 per season, average **200** | 5.7–8.8, average 7.1 | 75–87 per plot-season | Replanted; tended by hand every day |
| Forty-plot farm (reference) | **286** a game day | | 120 a day | Watering is the only daily energy cost |
| Fishing | **27.1** expected per cast | | 10 per cast = **2.7 coins per energy** | Crops average 2.4 per energy |
| Fishing with the energy the farm leaves (150) | 15 casts | **406** a game day | | Not limited by land |
| Animals (one each of six kinds, 8,800 coins) | 15–35 a day each, averaged over the season | **156** a game day | **none** | Payback 57 game days (about two seasons) |

So the ceiling for a forty-plot player with a rod is about **690 coins a game day, about 19,000 a season**. Animals are the only income that does not use energy.

Sinks, one time or per season:

| Sink | Coins | Note |
|:--|--:|:--|
| All 21 seeds, once each | 675 | Replanting a regrowing crop costs nothing more |
| Shop furniture (39 items) | 6,275 | Whole catalogue |
| Shop clothing (10 items) | 1,160 | Whole catalogue |
| Carpenter: hay (per ration) / milk pail | 10 (was 30) / 300 | Hay is one ration per animal per night |
| Animals (six kinds) | 8,800 | The herd |
| Backpack, 36 → 45 → 54 | 600 + 1,500 | |
| Copper / iron tool upgrade | 2,000 / 5,000 | Plus bars |
| Cat / dog | 600 / 900 | No income; rehome refund 50 % |
| Market fee | 5 % of each sale | The only sink that recurs |

## 2. Money balance

- **Early game is tight and about right.** The starting 500 buys a few seeds and a chair, and the first harvest of every crop pays back its seed (checked for all 21).
- **Late game has no sink.** From the second season a forty-plot farm earns about 19,000 a season. Everything in the shops, the herd, the backpack and both tool upgrades
  together costs about 27,000 once. A player who has bought it all is left with coins that have nothing to buy but the market's 5 % fee. This is the largest
  economic risk in the current design: coins accumulate without bound, and prices stop mattering. The fix is a new recurring sink (upkeep, a building, a town project),
  which is a new system, not a data change, so it is listed for the owner (section 7) rather than built here.
- **Animals are not throttled by energy.** The energy budget is the one throttle the balance targets assume ("one currency per energy point"). Animals
  bypass it: a herd earns 156 a day with no energy at all. That is fine at six animals, but nothing caps how many animals a player can own beyond the price.

## 3. Dominant strategies

1. **Spare energy goes to fishing.** Forty plots take 120 energy a day. The other 150 fish 15 casts for about 406 coins, more than the whole farm earns in a day
   (286). Land is limited by the farm, so nothing scales the crops with the energy; the rod does. A player with a rod does not need more plots.
2. **Animals are the only uncapped income.** Their payback is two seasons and they cost no energy. A player who has the coins buys the herd, and that makes the
   farm a secondary source by the second season.
3. **Feeding was a dead option** until this pass: a fed chicken lost 420 coins a season against fiber at zero. Players either fed with fiber or did not keep
   animals fed, so the sink on hay never existed. Fixed (section 5).

Neither 1 nor 2 is a bug in the numbers; both follow from energy and land being separate limits. They are the owner's call (section 7).

## 4. Prices that make no sense

| Price | Before | Finding | Action |
|:--|--:|:--|:--|
| Hay (one ration) | 30 | More than the everyday product of a chicken (15), duck (20) or cow (27). Feeding cost more than it returned | **Retuned to 10** (ADR-081 amendment) |
| Backpack, second upgrade | 1,500 for 9 slots | 167 coins a slot, against 67 for the first upgrade: the second step costs 2.5× the first per slot | Flagged, not changed (an owner decision) |
| Copper ore at the smithy | 75 | 37× its bin price (2). No arbitrage: the ore is only bought, never sold back. Expensive but consistent with a sink | Kept |
| Coal at the smithy | 150 | 37× its bin price (4) | Kept |
| Leather boots / traveler boots | 150 / 380 | Their resale value is 60 / 150: a buyer who resells loses 60 % of the leather pair. A sink, as intended | Kept |
| Milk pail | 300 | A tool with no income of its own | Kept |
| Rename scroll | 200 | A one-off nickname change; the most expensive clothing-shop item, and not a recurring cost | Kept |
| Single-harvest seeds | 10–55 | The seed costs 20–50 % of one harvest (ADR-081 D2): compliant | Kept |
| Regrowing seeds (strawberry, tomato, corn, grape, cranberries) | 35–55 | The first harvest barely covers the seed (83–95 %). Their season profit is in band, so this is slow cash flow, not a loss | Kept |

Nothing else in the shops is out of line with the seasons' profits: every furniture and clothing price is below one game day of a forty-plot farm (about 690 coins).

## 5. What this pass changed

Economy and market (data and rules, each with a test):

- **Hay 30 → 10** (ADR-081 amendment). A fed season is now positive for every animal: chicken +140, duck +280, cow +476, goat +616, sheep +440, pig +700.
  Tests: `EconomyBalanceTests.HayRation_*`.
- **Market browse order** (ADR-013 amendment A6). Newest (the default), lowest and highest unit price. A stack of five at 300 is 60 each and is compared with a
  stack of one at 30. Wire: `BrowseListings.SortBy`, protocol 30. Tests: `MarketSortPacketTests`, `SocialServiceTests.Browse_SortsByUnitPrice_*`,
  `MarketSortViewModelTests`.
- **Unit prices and the bin's price at the listing** (A7). Listings of more than one unit show the price per unit. The sell form shows what the shipping bin pays
  for the picked stack, so a seller has a reference before typing a price.
- **A clear listing result** (A8). "Listed 4 × Parsnip for 120 coins. You receive 114 coins when it sells, after the 5% fee." Before: "Listed for sale."
- **`BuyListing` reads 4 times, not 6** (A9; ADR-035 amendment). Section 6.

## 6. Market UX, and what the captures show

Captured before and after in English and Vietnamese at 1280×720 and 390×844 (`docs/qa/rebuild-economy/`).

What a player meets, before this pass:

1. **Finding an item.** The browse page is newest first with search by name. On a busy server a cheap listing of a common crop can sit many pages back, and the only
   way to find it is to read every page. **Fixed:** the sort row (newest, lowest and highest unit price).
2. **Pricing a listing.** The sell form asks for a total price for the whole stack and says nothing about the bin's price or the unit price. A seller who types 200
   for five parsnips does not know that the bin pays 200 for the same five. **Fixed:** the bin hint and the unit price.
3. **Comparing listings.** A listing of five at 300 and one of one at 30 look the same to a buyer scanning for the cheapest parsnip. **Fixed:** the unit price line.
4. **Feedback after listing.** "Listed for sale." tells the seller nothing about what was listed or what they will receive. **Fixed.**
5. **Two steps to reach the market.** The action key at the counter opens the trader's box, which types out its greeting before its options appear (about four
   seconds on this machine, so the counter feels dead at first). The market is one more click (Sell). Not changed: the flow is an NPC design decision (ADR-034/043).
6. **Buying has no confirm.** A big purchase completes at once; a big listing asks for a confirm (ten or more units). The asymmetry is deliberate in the code and
   worth a decision (section 7).
7. **No expiry on "My listings".** A seller cannot see when a listing ends (48 hours) without cancelling and relisting. Not changed.
8. **Price history is sales only.** The sparkline shows what sold, not what is asked. That is the honest signal for a market, but a seller has no view of the
   current asks. Not changed.

## 7. Owner decisions

- A late-game coin sink (section 2). This is a new system, so the fix needs an owner's design.
- Animals bypass the energy throttle (section 2). Capping herds, or tying animals to energy, is a mechanic.
- Fishing beats the farm at the margin (section 3). Retuning fish prices down would make the rod merely equal, not worse; the owner should choose.
- The backpack's second step (section 4).
- A confirm for a large purchase on the buy side (section 6).
- Expiry shown on "My listings" (section 6).

## 8. Database cost of a purchase

`BuyListing` measured 6 reads, 2 writes, 1 transaction and 2 SaveChanges before this pass (`DbBudgetBaseline`, ADR-035 target: at most 4 reads):

| # | Read | Why |
|--:|:--|:--|
| 1 | `market_listings` | The price, under the listing lock |
| 2 | `market_listings` | Inside the transfer, to get a tracked row |
| 3 | `player_wallets` | Buyer |
| 4 | `player_wallets` | Seller |
| 5 | `mail_messages` count | The seller's sale notice: room in the mailbox |
| 6 | `mail_messages` unread count | The seller's unread badge |

**Now 4 reads.** The listing is read once; the save writes it with its `Active` status as the concurrency token, so a row another process closed fails the save
instead of selling twice. The two wallets come in one query (for every transfer, not only this one). The two mail reads remain: they are the sale notice, and
they are the price of telling the seller. The DbBudget row and ADR-035 are updated in the same change.

## 9. Result of this pass

Captured before and after in English and Vietnamese at 1280×720 and 390×844 (`docs/qa/rebuild-economy/`, graded in its
[README](../qa/rebuild-economy/README.md)).

| Check | Before | After |
|:--|:--|:--|
| `BuyListing` reads (ADR-035 limit 4) | 6 | **4** |
| Two-party transfer wallet reads | 2 queries | **1 query** (`LinkExternalIdentity` 5 → 4, `CommissionBuilding` 4 → 3) |
| Browse order | newest only | newest, cheapest each, priciest each |
| Listing price line | total | total and each (`300 coins (60 each)`) |
| Sell form, a stack picked | nothing | what the shipping bin pays for it, until a price is typed |
| Listing result | "Listed for sale." | what was listed, the price, what the seller receives after the fee |
| Hay ration | 30 coins (a fed chicken loses 420 a season) | 10 coins (every fed animal is ahead over a season) |
| Protocol | 28 | 29 (`BrowseListings.SortBy`, appended) |

Tests: `EconomyBalanceTests` (two hay rules), `EconomyReportTests` (the simulation harness, writes `NOSUM_ECONOMY_REPORT`),
`MarketSortPacketTests` (wire), `MarketSortViewModelTests` (order, unit price, bin hint, listing result), `SocialServiceTests.Browse_SortsByUnitPrice_*`
(service), and the DbBudget rows (`BuyListing` 4 reads). The final build passes the full suites: 3,433 unit tests and 825 integration tests.
An intermediate build failed `PacketBoundsTests.EveryClientPacket_Fuzzed_*` on a stale generated formatter (an `int` sent to a `byte` member); a clean
rebuild passes it, and the test is unchanged.

Still open, for the owner: the late-game sink (section 2), the energy-free herd and fishing at the margin (section 3), the backpack's second step (section 4),
a confirm for large buys (section 6), and the expiry on "My listings" (section 6).

## 10. Round 2: what changed, what did not

Branch `claude/economy-round2`. Captures and grading: [`docs/qa/economy-round2/`](../qa/economy-round2/README.md). The numbers are the simulation's
(`docs/qa/economy-round2/economy-report-after.txt`).

| Section | Item | Result |
|:--|:--|:--|
| 2, 7 | Late-game coin sink | **Partly.** Iron tool upgrade 5 000 → 8 000 (one-time, ADR-081 round-2 D11); the backpack step is in §4. One-time sinks go from about 27 000 to about 29 500. A sink that recurs and scales with a season's income (about 19 000) is a new system (upkeep, a building, a town project): **open for the owner**. |
| 2, 7 | Energy-free herds | **Partly.** Feed is the only recurring cost that grows with a herd: hay 10 → 12 a ration (ADR-081 D9). A fed season is still positive for every animal (chicken +84 to pig +644). The herd's gross income (156 a game day) and the lack of a cap are unchanged: **open for the owner**. |
| 4 | Backpack step price | **Done.** The second step 1 500 → 1 000 (ADR-081 D10). Per slot 67 then 111 (was 67 then 167). |
| 6, 7 | Confirm for a large buy | **Done.** A purchase of 1 000 coins or more asks first in a `ConfirmDialog` (ADR-013 A10). |
| 6 | Six rows at 1280×720 without an inner scroll | **Not met.** Six rows now fit at 720 px, but the pager falls below the panel and a page of ten scrolls. Fixing it needs a layout decision (the 18-unit item slot in the rows, or the search and sort on one row). See the QA README. |

Not changed: fiber stays free (the owner's question on free feed), fishing against the farm (section 3), the expiry on "My listings" (section 6), and the
price history (section 6).

Tests: `EconomyBalanceTests.HayRation_*`, `BackpackRulesTests`, `BlacksmithRulesTests`, `MineViewModelTests` (iron offers), `BackpackServiceTests`
(the second step is 1 000 on the wire and in the balance), `MarketSortViewModelTests.Buy_*` (the confirm), `MarketSortPacketTests` (protocol 30, the test renamed to match).

## 11. Round 3: what changed, what did not

Branch `claude/economy-round3`, ADR-101. Captures and grading: [`docs/qa/economy-round3/`](../qa/economy-round3/README.md).

| Section | Item | Result |
|:--|:--|:--|
| 6 | Expiry on "My listings" (item 7) | **Done.** Each of the seller's listings shows the hours left (or "Expired"), from the expiry the server already sends. No wire change. |
| 6, round-2 open | Six or eight rows without an inner scroll | **Not met.** Search and sort now share one row and listing rows are one line with a 12 u picture, but the 14 u row buttons set the row height. A page of eight scrolls by 31 px at 1280x720 and 157 px at 1920x1080 (measured, [QA README](../qa/economy-round3/README.md)). **Open for the owner:** a smaller row button (a shared control), a page size per viewport, or a shorter header. |
| 6 | Price of current asks (item 8) | **Not done.** Needs a server query for open asks; a wire change, so it is for a later round. |
| 2 | Recurring late-game sink | **Open for the owner** (unchanged from round 2). |

Not changed: the NPC route to the market (item 5), the confirm asymmetry (item 6, owner decision), the sales-only price history.
