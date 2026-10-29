# Game-feel and UX audit, 2026-10-30

The index for the issues opened from this audit. Each issue names its code evidence and, where a live capture exists, links the screenshot.

## Method

- The client (`wasm/Nosum.Wasm`, Blazor WebAssembly dev build on port 5000) and the server (`server/Nosum.Host`, port 57300, PostgreSQL 16 with the dev role) were run locally.
- Headless Chromium (Playwright) drove the landing page and the guest sign-in.
- Every finding is backed by code read at the file and line named in the issue. Live captures exist only for the first two screens listed below.

## Capture status

The in-game UI could not be reached in this run. After "Play as guest", the start-up overlay (`Game_Loading`, `wasm/Nosum.Wasm/Pages/Home.razor:150-154`) stayed up for more than six minutes. The server logs show the guest joined the farm map, the page was not cross-origin isolated (not required by the client), requests were not pending, and `requestAnimationFrame` fired, but the game canvas kept its default 300x150 bitmap. The cause is not confirmed. It may be the headless browser or the interpreted dev build. It is tracked as the start-up issue below. Issues that need in-game screenshots are marked "code evidence only" and need a live capture before they are judged.

## Screenshots

| File | What it shows |
|:--|:--|
| [01-landing-signin-1280x720.png](01-landing-signin-1280x720.png) | Sign-in screen at 1280x720: clipped labels, disabled providers |
| [02-entering-world-stalled-1280x720.png](02-entering-world-stalled-1280x720.png) | "Entering the world" overlay after the guest sign-in, still up after six minutes |

## Issues

| Issue | Priority | Area | Title |
|:--|:--|:--|:--|
| [#207](https://github.com/ppn-systems/nosum/issues/207) | P3-UX | Onboarding | Sign-in buttons are clipped and low-contrast; disabled providers are dead ends |
| [#208](https://github.com/ppn-systems/nosum/issues/208) | P3-UX | Onboarding | "Entering the world" overlay shows only a spinner, with no progress, tips or timeout |
| [#209](https://github.com/ppn-systems/nosum/issues/209) | P2-FEEL | Audio, crafting | Crafting and furnace completions are silent |
| [#210](https://github.com/ppn-systems/nosum/issues/210) | P2-FEEL | Audio, animals | Animal care (egg, milk, petting) is silent |
| [#211](https://github.com/ppn-systems/nosum/issues/211) | P2-FEEL | Food and energy | Eating restores energy with a text line only |
| [#212](https://github.com/ppn-systems/nosum/issues/212) | P2-FEEL | Combat | Monster hits have no flash, knockback, hit-stop or screen shake |
| [#213](https://github.com/ppn-systems/nosum/issues/213) | P2-FEEL | Weather | Storms have rain but no lightning flash or thunder |
| [#214](https://github.com/ppn-systems/nosum/issues/214) | P2-FEEL | NPC dialogue | Dialogue appears in full at once; no typewriter reveal |
| [#215](https://github.com/ppn-systems/nosum/issues/215) | P3-UX | Shop and market | Buy and sell fire at once with no confirmation, unlike trade |
| [#216](https://github.com/ppn-systems/nosum/issues/216) | P2-FEEL | Mobile | Touch play is blocked by default; touch play has no haptics |
| [#217](https://github.com/ppn-systems/nosum/issues/217) | P2-FEEL | Social | Incoming mail, friend events and visitor arrivals make no sound |
| [#218](https://github.com/ppn-systems/nosum/issues/218) | P3-UX | Calendar | A season change is not announced |

## Coverage gaps

The brief named more systems than these twelve. The following were not turned into issues, because the code evidence was not yet strong enough or a live capture is needed first: mining floors and biomes, monster AI, quests and the town board, market economy and price balance, co-op and visiting flow, chests and inventory, chat, settings, and the day-night cycle. A follow-up pass with a working in-game capture should cover them.

## Notes

- No fixes were implemented. Each issue proposes a solution that reuses assets already in `assets/` and `wasm/Nosum.Wasm/wwwroot/audio/`.
- The 15 to 30 issue target was not met: twelve issues have verified evidence. Padding the list with unverified findings would have been worse.
