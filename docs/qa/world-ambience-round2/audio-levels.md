# Audio levels: measured, targeted and normalised

Measured with `tools/measure_audio.py` (standard library, read-only) on the committed files in `wasm/Nosum.Wasm/wwwroot/audio/`, and
normalised by the mix table in `AudioCatalog.cs` (`AudioMix`, [ADR-090](../../adr/ADR-090-audio-mix-loudness-targets.md)). Levels are the RMS
over the **active** samples (those above −50 dBFS), in dBFS, before the bus gains of the settings and before the per-cue volume of the hook. A
headless browser cannot judge how a sound feels: these numbers are the first pass, and a listening pass still decides the classes.

Integrated loudness (LUFS) reads −70 on every cue shorter than 400 ms, so the cues are compared by active RMS. For the music, ffmpeg's `ebur128`
gives −14.4 to −18.0 LUFS integrated, which agrees with the active RMS in the table (−13.6 to −16.1) to about one decibel.

## The classes

| Class | Target (dBFS) | Files | Per-cue volume in the hooks | Effective level at the hook (before the bus) |
|:--|--:|:--|:--|:--|
| Music | −16.0 | the eight tracks | 100 | −16.0 |
| Action | −16.0 | tools, harvest, planting, pick-ups, coin, door, sword, staff, monsters, potion, food | 100 (own pick-up), 40 (a neighbour's) | −16.0 (−24.0 for a neighbour) |
| Stinger | −16.0 | level up, combo finisher, doorbell, mail chime, craft, thunder | 100 | −16.0 |
| Footstep | −18.0 | footstep | 70 | −21.1 (about 5 dB under the music) |
| Interface | −18.0 | UI click, panel open, panel close | the browser's click; 100 for open and close | −18.0 |
| Ambience | −16.0 | rain loop | 55 (rain), 85 (storm) | −21.2 (rain, 5.2 dB under the music), −17.4 (storm, 1.4 dB under) |

## What changed, in numbers

| Pair | Before (measured) | After (targeted) |
|:--|:--|:--|
| Footstep under the music (both at the hook's volume) | 9.4 dB (footstep −24.9, music −15.5) | 5.1 dB (footstep −21.1, music −16.0) |
| Tool swing under the music (`tool_pickaxe`, `tool_water`, volume 100) | 2.7 and 2.8 dB under | equal |
| Rain under the music (volume 55) | 5.2 dB under | 5.2 dB under (kept) |
| Storm under the music (volume 85) | 1.4 dB under | 1.4 dB under (kept) |
| Thunder (volume 80) against the music | about 0.7 dB over | about 1.9 dB under |
| UI click | −18.2 (no change in the mix: the browser plays it at its own volume) | −18.2 |
| Largest peak after gain | — | −0.8 dBFS (`eat.wav`, a +3.6 dB gain); the test requires −0.5 |

Gains are between −3.7 dB (`level_up.wav`) and +3.8 dB (`footstep.wav`); the test `Mix_GainsStayWithinTwelveDecibels` checks the bound.

## Per file

The "before" columns are what the committed files measure; the gain is what `audio.js` applies once when the file is decoded.

| File | Length (s) | Peak before (dBFS) | Active RMS before (dBFS) | Class | Target (dBFS) | Gain (dB) | Active RMS after (dBFS) | Peak after (dBFS) |
|:--|--:|--:|--:|:--|--:|--:|--:|--:|
| `coin.wav` | 0.37 | -4.4 | -14.9 | Action | -16.0 | -1.1 | -16.0 | -5.5 |
| `combo_finisher.wav` | 0.34 | -0.9 | -13.3 | Stinger | -16.0 | -2.7 | -16.0 | -3.6 |
| `craft.wav` | 0.19 | -3.1 | -16.5 | Stinger | -16.0 | +0.5 | -16.0 | -2.6 |
| `door.wav` | 0.32 | -3.7 | -17.8 | Action | -16.0 | +1.8 | -16.0 | -1.9 |
| `doorbell.wav` | 1.05 | -3.1 | -13.3 | Stinger | -16.0 | -2.7 | -16.0 | -5.8 |
| `eat.wav` | 0.14 | -4.4 | -19.6 | Action | -16.0 | +3.6 | -16.0 | -0.8 |
| `footstep.wav` | 0.07 | -9.1 | -21.8 | Footstep | -18.0 | +3.8 | -18.0 | -5.3 |
| `harvest.wav` | 0.34 | -3.1 | -13.3 | Action | -16.0 | -2.7 | -16.0 | -5.8 |
| `level_up.wav` | 0.63 | -1.4 | -12.3 | Stinger | -16.0 | -3.7 | -16.0 | -5.1 |
| `mail_chime.wav` | 0.47 | -5.2 | -15.6 | Stinger | -16.0 | -0.4 | -16.0 | -5.6 |
| `monster_defeat.wav` | 0.35 | -1.4 | -14.3 | Action | -16.0 | -1.7 | -16.0 | -3.1 |
| `monster_hit.wav` | 0.16 | -1.9 | -14.1 | Action | -16.0 | -1.9 | -16.0 | -3.8 |
| `music_fall_day.wav` | 30.00 | -2.6 | -15.3 | Music | -16.0 | -0.7 | -16.0 | -3.3 |
| `music_fall_night.wav` | 37.89 | -2.6 | -15.7 | Music | -16.0 | -0.3 | -16.0 | -2.9 |
| `music_spring_day.wav` | 32.00 | -2.6 | -14.5 | Music | -16.0 | -1.5 | -16.0 | -4.1 |
| `music_spring_night.wav` | 32.73 | -2.6 | -15.4 | Music | -16.0 | -0.6 | -16.0 | -3.2 |
| `music_summer_day.wav` | 30.00 | -2.6 | -13.6 | Music | -16.0 | -2.4 | -16.0 | -5.0 |
| `music_summer_night.wav` | 30.00 | -2.6 | -16.1 | Music | -16.0 | +0.1 | -16.0 | -2.5 |
| `music_winter_day.wav` | 34.29 | -2.6 | -15.8 | Music | -16.0 | -0.2 | -16.0 | -2.8 |
| `music_winter_night.wav` | 32.00 | -2.6 | -15.3 | Music | -16.0 | -0.7 | -16.0 | -3.3 |
| `pickup.wav` | 0.12 | -4.4 | -14.7 | Action | -16.0 | -1.3 | -16.0 | -5.7 |
| `plant.wav` | 0.10 | -5.2 | -17.6 | Action | -16.0 | +1.6 | -16.0 | -3.6 |
| `potion_drink.wav` | 0.22 | -3.7 | -14.4 | Action | -16.0 | -1.6 | -16.0 | -5.3 |
| `rain_loop.wav` | 2.75 | -6.0 | -15.5 | Ambience | -16.0 | -0.5 | -16.0 | -6.5 |
| `staff_cast.wav` | 0.22 | -3.1 | -16.8 | Action | -16.0 | +0.8 | -16.0 | -2.3 |
| `sword_slash.wav` | 0.14 | -2.5 | -16.0 | Action | -16.0 | +0.0 | -16.0 | -2.5 |
| `thunder.wav` | 1.60 | -0.9 | -12.9 | Stinger | -16.0 | -3.1 | -16.0 | -4.0 |
| `tool_axe.wav` | 0.12 | -1.4 | -15.2 | Action | -16.0 | -0.8 | -16.0 | -2.2 |
| `tool_hoe.wav` | 0.16 | -1.9 | -14.2 | Action | -16.0 | -1.8 | -16.0 | -3.7 |
| `tool_pickaxe.wav` | 0.18 | -3.1 | -18.2 | Action | -16.0 | +2.2 | -16.0 | -0.9 |
| `tool_scythe.wav` | 0.20 | -6.0 | -17.9 | Action | -16.0 | +1.9 | -16.0 | -4.1 |
| `tool_water.wav` | 0.32 | -5.2 | -18.3 | Action | -16.0 | +2.3 | -16.0 | -2.9 |
| `ui_click.wav` | 0.03 | -8.0 | -18.2 | Interface | -18.0 | +0.2 | -18.0 | -7.8 |
| `ui_close.wav` | 0.14 | -8.0 | -18.1 | Interface | -18.0 | +0.1 | -18.0 | -7.9 |
| `ui_open.wav` | 0.14 | -8.0 | -18.0 | Interface | -18.0 | +0.0 | -18.0 | -8.0 |

The unit test `Files_MeasuredLevelsMatchTheFilesOnDisk` re-measures every file in C# and checks it against the `MeasuredDbfs` column (within
0.15 dB), so a changed file fails the test until its row is updated. `Files_GainLeavesHeadroomBelowFullScale` checks the peak after the gain.
