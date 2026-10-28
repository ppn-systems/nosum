#!/usr/bin/env python3
"""Synthesises Nosum's early sound effects (docs/adr/ADR-039): short blips and one rain loop, written as 16-bit mono WAV.

Standard library only; the output is deterministic (fixed random seed), so the files can be re-created at any time and a change shows up in review.
Every sample is computed here from sine waves, filtered noise and envelopes: nothing is recorded, downloaded or taken from any game.

    python3 tools/synth_sounds.py                      # writes wasm/Nosum.Wasm/wwwroot/audio/*.wav
    python3 tools/synth_sounds.py --out <dir>          # writes elsewhere
    python3 tools/synth_sounds.py --check              # exits 1 when the files on disk differ from what this script makes

The file names are the ones in Nosum.Wasm.ViewModels.Audio.AudioCatalog (a unit test checks both lists agree).
"""

import argparse
import math
import os
import random
import struct
import sys
import wave

RATE = 22050


def seconds(n):
    return int(n * RATE)


def env(i, n, attack=0.005, release=0.5):
    """Linear attack, then an exponential-ish decay over the rest of the sound."""
    t = i / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    if release is None:
        return a  # sustained: no decay (the rain loop)
    r = max(0.0, 1.0 - i / n)
    return a * (r ** (1.0 + 3.0 * release))


def sine(freq, dur, amp=1.0, release=0.5, attack=0.004, glide=0.0):
    n = seconds(dur)
    out, phase = [], 0.0
    for i in range(n):
        f = freq * (1.0 + glide * (i / n))
        phase += 2 * math.pi * f / RATE
        out.append(math.sin(phase) * amp * env(i, n, attack, release))
    return out


def noise(dur, rng, amp=1.0, release=0.5, attack=0.002, lowpass=1.0, highpass=0.0):
    """White noise through a one-pole low-pass (lowpass 1.0 = none) and high-pass (0.0 = none), both in 0..1 coefficient terms."""
    n = seconds(dur)
    out, lp, hp_prev_in, hp = [], 0.0, 0.0, 0.0
    for i in range(n):
        x = rng.uniform(-1.0, 1.0)
        lp += lowpass * (x - lp)
        y = lp
        if highpass > 0.0:
            hp = (1.0 - highpass) * (hp + y - hp_prev_in)
            hp_prev_in = y
            y = hp
        out.append(y * amp * env(i, n, attack, release))
    return out


def mix(*tracks, offsets=None):
    offsets = offsets or [0.0] * len(tracks)
    length = max(seconds(o) + len(t) for t, o in zip(tracks, offsets))
    out = [0.0] * length
    for t, o in zip(tracks, offsets):
        s = seconds(o)
        for i, v in enumerate(t):
            out[s + i] += v
    return out


def normalise(samples, peak):
    top = max(1e-9, max(abs(v) for v in samples))
    return [v / top * peak for v in samples]


def build():
    rng = random.Random(39)  # ADR-039: fixed seed, deterministic files
    sounds = {}

    # Tools: a dull thud for the hoe, a splash for the can, a chop, a metallic clink, a swish.
    sounds["tool_hoe"] = normalise(mix(noise(0.16, rng, release=0.8, lowpass=0.12), sine(95, 0.14, 0.9, release=0.7, glide=-0.4)), 0.8)
    sounds["tool_water"] = normalise(noise(0.32, rng, release=0.35, attack=0.05, lowpass=0.5, highpass=0.15), 0.55)
    sounds["tool_axe"] = normalise(mix(noise(0.1, rng, release=0.9, lowpass=0.3), sine(150, 0.12, 1.0, release=0.8, glide=-0.5), sine(310, 0.06, 0.3, release=0.9)), 0.85)
    sounds["tool_pickaxe"] = normalise(mix(noise(0.04, rng, release=0.9, highpass=0.2), sine(1650, 0.18, 0.8, release=0.9), sine(2480, 0.12, 0.35, release=0.9)), 0.7)
    sounds["tool_scythe"] = normalise(noise(0.2, rng, release=0.4, attack=0.06, lowpass=0.35, highpass=0.3), 0.5)

    # Farming: a soft pat for a seed, a rising two-note chime for a harvest.
    sounds["plant"] = normalise(mix(noise(0.07, rng, release=0.9, lowpass=0.15), sine(210, 0.1, 0.5, release=0.8, glide=0.3)), 0.55)
    sounds["harvest"] = normalise(mix(sine(660, 0.16, 0.7, release=0.6), sine(990, 0.26, 0.6, release=0.7), offsets=[0.0, 0.08]), 0.7)

    # Items and coins.
    sounds["pickup"] = normalise(sine(520, 0.12, 1.0, release=0.8, glide=1.1), 0.6)
    sounds["coin"] = normalise(mix(sine(1320, 0.1, 0.8, release=0.7), sine(1760, 0.3, 0.8, release=0.8), offsets=[0.0, 0.07]), 0.6)

    # World: a door/warp swoosh with a latch click, a footstep, the interface ticks.
    sounds["door"] = normalise(mix(noise(0.28, rng, release=0.5, attack=0.08, lowpass=0.25), sine(120, 0.12, 0.7, release=0.8), offsets=[0.0, 0.2]), 0.65)
    sounds["footstep"] = normalise(noise(0.07, rng, release=0.9, lowpass=0.18), 0.35)
    sounds["ui_click"] = normalise(sine(1200, 0.035, 1.0, release=0.9, attack=0.001), 0.4)
    sounds["ui_open"] = normalise(sine(500, 0.14, 1.0, release=0.8, glide=0.9), 0.4)
    sounds["ui_close"] = normalise(sine(800, 0.14, 1.0, release=0.8, glide=-0.5), 0.4)

    # Combat (docs/adr/ADR-053): sword slash, arcane staff cast, monster damage hit, monster defeat poof burst, potion drink.
    sounds["sword_slash"] = normalise(mix(noise(0.12, rng, release=0.8, attack=0.005, lowpass=0.45, highpass=0.15), sine(800, 0.14, 0.9, release=0.8, glide=-0.6)), 0.75)
    sounds["staff_cast"] = normalise(mix(sine(440, 0.22, 0.8, release=0.7, glide=0.8), sine(880, 0.18, 0.6, release=0.8, glide=-0.3), noise(0.18, rng, release=0.7, lowpass=0.25)), 0.7)
    sounds["monster_hit"] = normalise(mix(noise(0.15, rng, release=0.7, lowpass=0.2), sine(130, 0.16, 0.9, release=0.7, glide=-0.5)), 0.8)
    sounds["monster_defeat"] = normalise(mix(noise(0.35, rng, release=0.6, attack=0.01, lowpass=0.3), sine(160, 0.28, 0.9, release=0.6, glide=-0.7)), 0.85)
    sounds["potion_drink"] = normalise(mix(sine(360, 0.1, 0.7, release=0.7, glide=0.4), sine(480, 0.14, 0.8, release=0.7, glide=0.3), offsets=[0.0, 0.08]), 0.65)

    # Rain: soft filtered noise, a few seconds long; the tail is cross-faded into the head so the loop has no seam.
    loop = noise(3.0, rng, release=None, attack=0.0, lowpass=0.55, highpass=0.08)
    fade = seconds(0.25)
    for i in range(fade):
        w = i / fade
        loop[i] = loop[i] * w + loop[len(loop) - fade + i] * (1.0 - w)
    sounds["rain_loop"] = normalise(loop[: len(loop) - fade], 0.5)

    # Celebratory level-up fanfare (docs/adr/ADR-062): ascending four-note major arpeggio (C5 - E5 - G5 - C6).
    sounds["level_up"] = normalise(
        mix(
            sine(523.25, 0.14, 0.75, release=0.6),
            sine(659.25, 0.14, 0.80, release=0.6),
            sine(783.99, 0.14, 0.85, release=0.6),
            sine(1046.50, 0.36, 0.95, release=0.7),
            offsets=[0.0, 0.09, 0.18, 0.27],
        ),
        0.85,
    )
    return sounds


def encode(samples):
    frames = b"".join(struct.pack("<h", max(-32767, min(32767, int(round(v * 32767))))) for v in samples)
    import io

    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(frames)
    return buf.getvalue()


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=os.path.join(root, "wasm", "Nosum.Wasm", "wwwroot", "audio"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    files = {name + ".wav": encode(samples) for name, samples in build().items()}
    if args.check:
        stale = [n for n, data in files.items() if not os.path.exists(os.path.join(args.out, n)) or open(os.path.join(args.out, n), "rb").read() != data]
        for n in stale:
            print("out of date:", n)
        return 1 if stale else 0

    os.makedirs(args.out, exist_ok=True)
    for name, data in sorted(files.items()):
        with open(os.path.join(args.out, name), "wb") as f:
            f.write(data)
        print(f"{name}: {len(data)} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
