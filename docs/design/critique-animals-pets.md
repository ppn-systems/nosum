# Critique: animals and pets

Played on the local development build of `master` at `384cb1d`: a guest on the Development server, the client built with
`NosumDevTools=true`, Chromium at 1280×720 in English. The loop was: buy a coop blueprint at the carpenter (4 000 coins, 300 wood,
100 stone), place it on the farm, let two game days pass, buy two chickens, walk into the coop, feed the bench, let a day pass, pet
and collect. Then adopt a cat, follow it on the farm and pet it from the menu. Frames are in
[`docs/qa/rebuild-animals-pets/critique/`](../qa/rebuild-animals-pets/critique/). Numbers below are from the frames or from the
design values in [ADR-028](../adr/ADR-028-farm-buildings-and-animals.md) and [ADR-075](../adr/ADR-075-emotes-and-pet-companion.md).
Anything marked *computed* was not measured in the game.

The reviewer's test for each finding: does the player have to guess, repeat a chore, or get wrong feedback? Findings are ranked by
how much play they cost.

## What works

- The coop reads as a place: depth-sorted animals walk in the room, the produce bobs above a hen that has an egg, and a petting
  shows hearts for a moment (`03-coop-empty-bench-no-cue.png`, `05-new-day-no-produce-cue.png`).
- The carpenter's animal section says what it needs when nothing is ready: "No finished coop or barn with room yet."
- Names are checked before the purchase, and the price sits on the buy button ("Buy Chicken · 800 coins").

## Findings

### 1. Feeding is a chore: one press puts in four hay (high)

A press at the trough called `Feed(slot, interior.Capacity)`, so one press put in the animal capacity (four), not what the bench
holds (24). Frames: hay 30 → 26 after the first press (`04-one-press-takes-4-hay-bench-looks-same.png`), 22 after the second.
Filling an empty bench from one stack of 30 hay takes eight presses. *Computed:* a 24-ration bench refilled by a player who feeds
every few days is six or more extra presses each time.

**Status: fixed in this change.** A press now asks for `AnimalRules.MaxFeedAtOnce`; the server takes the smaller of the stack and
the room (`BuildingService.Animals.cs`, `take`).

### 2. The bench's state is invisible (high)

The wire sends the bench's rations (`AnimalsSnapshot.Rations`, `AnimalsChanged.Rations`) and the client keeps them, but nothing draws
them. An empty bench and a full one look the same (`03` vs `04`). The player cannot tell that the animals will go hungry tonight.
Animals have no per-animal hunger: the bench is the only input, so the bench state is the state the player needs.

**Status: partly fixed.** The trough now shows an exclamation mark (the emote sheet, drawn by the engine) while the bench is empty
and the animals live there, and a hay sheaf when it holds feed. The count is not drawn yet (follow-up, below).

### 3. Produce waits for a touch, with no cue (medium)

Produce is settled lazily: the first touch of a building on a new game day runs the night's feeding (ADR-028 D3). After the day
rolls over, the hens show no produce (`05-new-day-no-produce-cue.png`). The egg appears only after the player acts in the room
(`06-action-key-swings-hoe-at-bench.png`, the egg above the hen). Nothing tells the player that eggs are waiting, so a player who
does not open the coop sees a quiet room and a full bench. ADR-028 chose lazy settlement to keep idle buildings free.

**Status: fixed in round two (docs/adr/ADR-090 D1).** The room is read again on a day change, and the ready sparkle marks the animals with produce. The original options were: either settle the day's feeding when a player first comes near the coop, or
push the produce on the day rollover. Both need a decision on cost (ADR-028 says the day hook is not used).

### 4. One action key, three outcomes, no preview (medium)

The action key feeds at the trough, collects produce, or pets the nearest animal, depending on what the farmer faces, what is in the
hand and how far the animal is. With hay in hand one tile from a hen, the same press pets the hen (toast: "You pet Hen. Friendship:
40."). With the hoe in hand the press swings at the bench (`06`). The outcome is only a line of text that may have passed by.

**Status: fixed in round two (docs/adr/ADR-090 D3).** The engine draws "Feed", "Collect" or "Pet" over the target, decided by the same rule as the press.

### 5. Wrong feedback after a purchase (medium, fixed)

Buying a chicken in a coop says "Bought, and on its way to the barn." (`Building_Done_BuyAnimal`, English). The Vietnamese string
("đưa về chuồng", back to the pen) is generic; the English one is wrong for a coop. (`02-bought-notice-says-barn-and-is-clipped.png`)

**Status: fixed** in English: "Bought, and on its way to its new home." Vietnamese was already generic and is unchanged.

### 6. The carpenter's animal section is hard to read (medium, not changed)

- The "Animal name" field sits above the species rows, so the player types a name before choosing a species. The hint "3 to 16
  letters or digits" is below the field, and the field keeps a red underline after a successful purchase (`02`).
- The purchase notice was at the bottom of the panel and clipped at 1280×720 (`02`); it now sits above the section (docs/adr/ADR-090 D4). `ui-standard.md` section 7 item 6 forbids
  clipping.
- "Coop (2/4)" repeats for each species row and does not say which animals live there. In Vietnamese both rows read
  "Chuồng gà (1/4)", the duck's row included (`after/1280x720-vi/carpenter-bought.png`): the building is named after its chickens.

### 7. Payback: chickens are a poor buy, cows are not (design values, computed)

With the ADR-028 numbers: an egg sells for 50 coins, hay costs 30 coins at the carpenter, and a chicken costs 800 coins.
*Computed:* one hay per chicken per day leaves 20 coins a day, so a chicken pays for itself in 40 days. A cow gives 125 coins of milk
for the same hay, so it pays for itself (1 500 coins) in about 16 days. Large and gold produce raise these numbers, but the chicken
is the first animal a new player buys, and it is the one that pays back slowest. Whether that is intended is a balance question
(`docs/design/economy.md`), not a bug, and it is not changed here.

### 8. A pet does almost nothing (high, not changed)

- The cat trails the farmer as a small sprite (`07-cat-trails-farmer-small-no-reaction.png`). It does not react to being petted,
  and nothing in the world says it is there to be petted.
- Petting is only in the menu: Menu → Pet → Pet (`08-pet-only-via-menu.png`). A player who walks up to the cat cannot pet it.
- The pet panel shows mood 60 and "You already petted them today." after the first press (`09`). The Pet button looks the same
  after that as before. The energy from petting (+5, ADR-075 D4) is not reported.
- Mood has no effect on anything. The only gameplay is the energy once a day. A pet with no effect is a chore to keep up with.

**Status: round two adds option 3's forage (docs/adr/ADR-090 D2): a daily chance of a forage item on the first petting.** The brief asks for a small helpful behaviour, but a pet that does something useful is a new rule (what it
does, how often, what it costs the player) and needs an ADR. Options for that decision:

1. The pet's mood sets the energy the farmer gets from petting (a higher mood gives more). No wire change; a rule change in
   `PetRules`.
2. The pet reacts in the world when petted (a heart over it) and shows the energy it gave. Client only.
3. A helper: the dog brings back one ration of hay from the carpenter's chest once a day. A new system; out of scope here.

## Priorities

| # | Finding | Impact | Cost | Done here |
| :-- | :-- | :-- | :-- | :-- |
| 1 | One press feeds 4 hay | High | Client, one line | yes |
| 2 | Bench state invisible | High | Client, engine icons | yes (count left) |
| 5 | Barn notice for a coop | Medium | i18n string | yes |
| 3 | Produce waits for a touch, no cue | Medium | Server | no |
| 4 | Action key has no preview | Medium | Client + ADR amendment | no |
| 6 | Carpenter animal section layout | Medium | Client layout | no |
| 8 | Pet has no in-world presence or effect | High | Client + rule ADR | no |
| 7 | Chicken payback | Balance | Economy | no |
