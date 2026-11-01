# Audio listening proxy (world-ambience round 4)

A headless browser cannot hear the mix, so this is the measured proxy for "does the footstep sit right under the music". It renders the
committed WAVs at the gains the game applies (ADR-090 `AudioMix` gain, the hook's volume, the 0.49 bus of the default settings) and measures
them with ffmpeg's `ebur128`. Script: [`tools/listening_proxy.py`](../../../tools/listening_proxy.py) (`python3 -I tools/listening_proxy.py <out>`,
from the repository root). The decision is in [ADR-090 (round-4 amendment)](../../adr/ADR-090-audio-mix-loudness-targets.md).

## Effective levels (RMS over the active part, dBFS, at the output)

Music's effective level is −22.2 dBFS (target −16.0 and the 0.49 bus).

| Cue (hook volume) | Before (target) | After (target) | Under the music, before → after |
|:--|:--|:--|:--|
| Footstep (70) | −27.3 (−18.0) | **−25.8 (−16.5)** | 5.1 → **3.6** dB |
| Pickaxe swing (100) | −22.2 (−16.0) | −22.2 (−16.0) | 0.0 (unchanged) |
| UI click (100) | −24.2 (−18.0) | −24.2 (−18.0) | 2.0 (unchanged) |
| UI panel open (100) | −24.2 (−18.0) | −24.2 (−18.0) | 2.0 (unchanged) |
| Rain bed (55) | −27.4 (−16.0) | −27.4 (−16.0) | 5.2 (unchanged) |

## Momentary maximum (400 ms window, ebur128), cue alone against the music's integrated level

| Cue | Before | After |
|:--|--:|--:|
| Footstep | −12.7 LU | **−11.2 LU** (measured) |
| Pickaxe swing | −2.3 LU | unchanged |
| UI click | −12.8 LU | unchanged |
| Harvest | −0.8 LU | unchanged |
| Rain loop (55) | +0.8 LU | unchanged |

The window is 400 ms, so a 70 ms footstep reads low against a sustained cue (it sits mostly in silence). Read the footstep row as a
relative change, not an absolute level.

## Composite (12 s)

Music bed, a footstep every 0.5 s (volume 70), one pickaxe swing at 4 s, one UI click at 8 s.

| Mix | Integrated (LUFS) | Momentary maximum (LUFS) |
|:--|--:|--:|
| Music only | −23.3 | −21.8 |
| With footsteps at −18.0 target | −23.1 | −20.6 |
| With footsteps at −16.5 target | −23.0 | −20.5 |

The footstep change moves the mix total by a tenth of a LU: its effect is in how the footsteps sit under the bed, not in the total.

## Reading

- The footstep was the one cue that sat clearly under the bed (5.1 dB by active RMS). It moves to 3.6 dB. Still under the music, by design
  (the interface and the tools are not changed).
- The interface click is 2 dB under the music at full volume: it is a tick, and it stays quieter than the actions.
- The tools and harvest sit level with the music, as the class table says.
- This is a proxy. The owner's listening pass decides; if the footstep still reads quiet, the next step is −15.0 (about 2 dB under) and the
  order test changes to `Footstep <= Action` (ADR-090, Consequences).
