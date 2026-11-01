# ADR-090: Audio mix — loudness targets per class of sound

## Status

Accepted (2026-11-01); amended in round 4 (2026-11-01): the footstep target (D2). Amends ADR-039 (D3 and D6: the files and the hooks are unchanged; the buses and the per-cue volumes are unchanged; the
load now applies one gain per file). No wire change, no render command change: `NosumProtocol.Version` stays as it is. Evidence:
[docs/design/critique-world-ambience.md](../design/critique-world-ambience.md) (section 2, the open listening item) and the measured table in
[docs/qa/world-ambience-round2/audio-levels.md](../qa/world-ambience-round2/audio-levels.md).

## Context

The world-ambience critique measured the committed files and found that the mix was not set by any rule. The music sat at −13.6 to −16.2 dBFS
RMS, the footsteps at −21.8 and the interface clicks at about −18, and the hooks' per-cue volumes (70 for a footstep, 55 or 85 for rain) were
the only thing that set a cue's level. The sounds were made at different levels by the synthesis script, so a cue's loudness depended on the
file rather than on what the cue is. A listening pass was the open item; a headless browser cannot hear, so this decision uses the measured
levels, and a person still has to listen to the result.

## Decision

- **D1 — Measure every file.** `tools/measure_audio.py` (standard library only, read-only) reports for each WAV the peak, the RMS over the whole
  file and the RMS over its **active** samples (those above −50 dBFS, so a short click is not diluted by the silence after it). The active RMS
  is the level of record (`AudioFile.MeasuredDbfs`). Integrated loudness (LUFS, BS.1770 gating) needs 400 ms and reads −70 on the cues, so it is
  not used for the cues; the music's integrated loudness (ffmpeg `ebur128`, −14.4 to −18.0 LUFS) agrees with its active RMS to about one decibel.
- **D2 — One target per class.** `AudioMix` names the classes and their targets, in dBFS of active RMS, before the bus gains:

  | Class | Target | Files |
  |:--|--:|:--|
  | Music | −16.0 | the eight tracks |
  | Action | −16.0 | tools, harvest, planting, pick-ups, coin, door, sword, staff, monsters, potion, food |
  | Stinger | −16.0 | level up, combo finisher, doorbell, mail chime, craft, thunder |
  | Footstep | −16.5 | footstep (about 3.6 decibels under the music at the hook's volume, 70; amended in round 4, was −18.0) |
  | Interface | −18.0 | UI click, panel open and close |
  | Ambience | −16.0 | rain loop (played at 55 for rain, about 5 dB under the music, or 85 for a storm, about 1.4 dB under; the same relation the critique measured before) |

  The order is fixed by a test: footstep < action = music = ambience, and interface < action.
- **D3 — Normalise once, at load.** `AudioFile` carries its measured level and target; `GainDb = Target − Measured`. `GameAudio` passes the gain to
  `loadAudio` (a fourth argument, default 0 in `audio.js`), and `audio.js` scales the decoded buffer in place (`applyGain`). It runs once per
  file, when the file is decoded, so nothing runs per frame and the frame's commands are unchanged. Tracks keep their gain until they are
  fetched (`trackGains`). Per-cue volumes (the hooks, the neighbour pickup at 40, the rain at 55 and 85) still scale on top of the class level.
- **D4 — No file is rewritten.** The WAVs stay as the synthesis scripts wrote them. Scaling in the browser keeps the committed audio and the
  `synth_*.py --check` tests unchanged, and a later re-recording only changes its row in the table.
- **D5 — Headroom.** After its gain no file may peak above −0.5 dBFS (the decoded buffer is not clamped), and no gain is more than 12 dB, so a
  bad measurement or a wrong class fails a test.

The resulting numbers, the measured and the normalised levels per file, are in `docs/qa/world-ambience-round2/audio-levels.md`.

## Alternatives considered

- **Rewrite the WAV files to the targets.** Changes ~40 binary files, makes the synthesis `--check` a moving target and hides the measurement.
- **Retune each hook's volume.** Scatters the mix over call sites; a re-recorded file would silently change its loudness.
- **ffmpeg `loudnorm` at build time.** Not a repo tool, and it needs a gate longer than the cues (400 ms) to say anything about them.
- **Normalise in .NET at load.** Would need the samples on the .NET side; `audio.js` already holds the buffers, so it is one loop there.

## Consequences

- The load call has one more argument. Callers that pass three arguments (the browser check `tests/browser/audio-module-check.js`) still work.
- A new recording is measured with `tools/measure_audio.py`, its `MeasuredDbfs` updated, and the tests `Files_MeasuredLevelsMatchTheFilesOnDisk`
  and `Files_GainLeavesHeadroomBelowFullScale` decide whether it fits.
- The levels are a starting point from measurement, not a judgement by ear. The owner's listening pass is still open; a change to a class
  target is one line in `AudioMix`.
- Footsteps sit at about −25.8 dBFS effective (target −16.5 at volume 70), about 3.6 decibels under the music (round 4; was −27.3 and 5.1
  decibels under at −18). Before the mix they sat about nine decibels under. The listening pass should check this first.

## Revisit when

A listening pass says a class is too loud or too quiet against the others; or real recordings replace the synthesised ones (M6), in which case
measure them, update `MeasuredDbfs`, and keep the class targets.

## Amendment (round 4): the footstep target

**Why.** The round-2 numbers put the footstep about five decibels under the music at its hook volume (70). A footstep is about 70 ms long,
and loudness is judged over a window of roughly 200 ms, so a short click reads quieter than its active RMS says. The first listening
proxy (below) confirmed the footstep was the one cue that sat clearly under the bed; the interface click and the tool swing did not.

**What changed.** `AudioMix.Footstep` goes from −18.0 to −16.5 (`AudioCatalog.cs`). Nothing else moves: the interface stays at −18.0, the
action and music classes at −16.0. Footsteps stay under the actions (`Mix_ClassesKeepTheirOrder` holds). The footstep's gain rises from +3.8 to
+5.3 dB; its peak after gain is −3.8 dBFS (the headroom test needs −0.5 or lower), and the gain is well inside the 12 dB bound.

**How it was measured (no ears).** `tools/measure_audio.py` levels from round 2, the in-game hook volumes (footstep 70, tools and clicks 100)
and the bus gain of 0.49 (−6.2 dB) on both buses. The effective level is what reaches the output. Music's effective RMS is −22.2 dBFS.

| Cue (hook volume) | Before: effective RMS (under music) | After: effective RMS (under music) |
|:--|:--|:--|
| Footstep (70) | −27.3 (+5.1 dB) | **−25.8 (+3.6 dB)** |
| Pickaxe swing (100) | −22.2 (0.0 dB) | −22.2 (0.0 dB), unchanged |
| UI click (100) | −24.2 (+2.0 dB) | −24.2 (+2.0 dB), unchanged |
| UI panel open (100) | −24.2 (+2.0 dB) | −24.2 (+2.0 dB), unchanged |
| Rain bed (55) | −27.4 (+5.2 dB) | −27.4 (+5.2 dB), unchanged |

Listening proxy (ffmpeg `ebur128`, the files rendered at their gains, 48 kHz). The momentary (400 ms) maximum of each cue on its own, against
the music's integrated level, is a fair comparison only for the longer cues; for the footstep it reads low, because a 400 ms window holds a 70 ms
click mostly in silence. Figures: footstep −12.7 LU before and about −11.2 after; pickaxe −2.3; UI click −12.8; harvest −0.8; rain +0.8. A
composite of 12 s (music, a footstep every 0.5 s, one pickaxe swing at 4 s, one click at 8 s): integrated −23.1 LUFS before, −23.0 after, music
alone −23.3. So footsteps change the mix's loudness by a tenth of a LU; their effect is in how they sit under the bed, not in the total.

**What it does not settle.** A headless browser cannot hear the mix. The footstep is still about 3.6 dB under the music; if the listening pass
says it is still quiet, the next step is −15.0 (about 2 dB under), and the order test then moves to `Footstep <= Action`.
Numbers and scripts: [`docs/qa/world-ambience-round4/audio-proxy.md`](../qa/world-ambience-round4/audio-proxy.md).

**Consequences.** `NosumProtocol.Version` is unchanged; no wire, no render command, no new file. The rule is one line in `AudioMix`.
