# ADR-108: The first day goes to Town early, watering a tilled tile completes its step, and the first-run hint sits in the toast line

## Status

Accepted

Amends [ADR-036](ADR-036-quest-chain-and-requests-board.md) D4 (the order of the tutorial chain) and [ADR-033](ADR-033-client-settings-touch-onboarding.md) D3 (where a
hint is shown). No wire change: `NosumProtocol.Version` does not move (the quest ids and event kinds are the same; only the chain order and one event's condition change).

## Context

World QA pass 9 played a new player's first day as a guest. After tilling and watering, the player could not get to Town: the tutorial chain put
"Buy seeds" after "Plant a seed, then harvest" and "Put something into the shipping bin", so the seed shop, the only step that sends the player to Town,
was days away. The bus stop is the farm's east exit (80,15), 16 tiles from the spawn, and the waypoint to the seed keeper is only drawn once that
step is current. A player who did not know the exit was there had no reason to walk east.

The water step never completed either. `CropWatered` was emitted only when the watered tile had a crop, but the step's tip says "water a tilled tile",
so a player who did what the tip said got "Watered." and a quest card still at 0/1.

The first-run hint was a separate plaque, styled as a card, stacked above the toast line; in the captures it sat mid-screen over the farm.
ui-standard section 5 puts hints in the toast line.

## Decision

D1. The chain order is Till, Water, **Buy seeds**, Plant and harvest, Ship, Chop, and the rest as before. The seed trip is the first-day path:
the bus stop is the farm's east exit, the bus stop's east exit is Town, and the waypoint arrow points at the seed keeper while the step is current.

D2. A watering of a tilled tile emits `CropWatered` whenever the can wets the tile. A withered crop is still not a watering for the step.

D3. The first-run hint is the toast line's own message: a toast-framed line in the bottom-centre stack, directly above the toasts and the hotbar. It keeps
its 12 s fade and its one-at-a-time rule. It is not a separate card.

D4. The top-right row (calendar bar, coins, Mail, Settings) is one row at every width. On a phone (640 px and narrower) the calendar drops its season
name and tomorrow's forecast so the row fits; nothing wraps under the Menu button.

D5. A window open in the centre hides the menu column only below 1600 px. From 1600 px the widest window (XL) is centred clear of the column, so the
column stays beside it.

## Consequences

- A new player reaches the seed shop on the first day with no waiting for a harvest. The Town trip is in the chain's second hour, not its third day.
- The chain-order test (`QuestChainTests`) and the integration test for a bare tilled tile (`FarmWireTests.Quests`) pin the new order and event.
- The minimap still hides while a window is open (its view model decides that); D5 keeps only the column.
