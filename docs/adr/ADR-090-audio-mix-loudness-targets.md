# ADR-090: Audio mix — loudness targets per class of sound

## Status

Accepted (2026-11-01). Amends ADR-039 (D3 and D6: the files and the hooks are unchanged; the buses and the per-cue volumes are unchanged; the
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
  | Footstep | −18.0 | footstep (about five decibels under the music at full volume) |
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
- Footsteps sit at about −21 dBFS effective (target −18 at volume 70), about five decibels under the music. Before, they sat about nine
  decibels under (measured −24.9 against −15.5). The listening pass should check this first.

## Revisit when

A listening pass says a class is too loud or too quiet against the others; or real recordings replace the synthesised ones (M6), in which case
measure them, update `MeasuredDbfs`, and keep the class targets.
