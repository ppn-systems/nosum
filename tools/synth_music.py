#!/usr/bin/env python3
"""Composes Nosum's own background music (docs/adr/ADR-039): one seamless loop per music track id, written as 8-bit mono WAV.

Standard library only; the output is deterministic (each track has a fixed seed), so the files can be re-created at any time and a change shows up in review.
Every sample is computed here from square, triangle and sine voices with simple envelopes: nothing is recorded, downloaded or taken from any game.

    python3 tools/synth_music.py                      # writes wasm/Nosum.Wasm/wwwroot/audio/music_*.wav
    python3 tools/synth_music.py --out <dir>          # writes elsewhere
    python3 tools/synth_music.py --check              # exits 1 when the files on disk differ from what this script makes

Each track is a whole number of bars, so it loops without a seam: a note that runs past the end wraps to the start, and the
one-pole low-pass that softens the square voice is run twice so its state at the loop point matches the start of the loop.
8-bit samples keep the eight files near 5.7 MB at 22.05 kHz (ADR-039 D6: under about 6 MB).

The file names are the ones in Nosum.Wasm.ViewModels.Audio.AudioCatalog (a unit test checks both lists agree).
"""

import argparse
import io
import math
import os
import random
import sys
import wave
from dataclasses import dataclass
from typing import Callable, Tuple

RATE = 22050
ARP_OCTAVE = 4
BASS_OCTAVE = 2
PAD_OCTAVE = 3
MELODY_OCTAVE = 5

PITCH = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "Bb": 10}
QUALITY = {
    "maj": (0, 4, 7),
    "min": (0, 3, 7),
    "maj7": (0, 4, 7, 11),
    "min7": (0, 3, 7, 10),
    "dom7": (0, 4, 7, 10),
}
MAJOR = (0, 2, 4, 5, 7, 9, 11)
MINOR = (0, 2, 3, 5, 7, 8, 10)


def sine(p):
    return math.sin(2 * math.pi * p)


def triangle(p):
    return 4 * abs(p - math.floor(p + 0.5)) - 1


def square(p):
    return 1.0 if p % 1.0 < 0.5 else -1.0


@dataclass(frozen=True)
class Track:
    """One music track: its tempo, chords and which voices play (volumes 0 mean the voice is off)."""

    name: str
    bpm: int
    key: str
    minor: bool
    chords: Tuple[Tuple[str, str], ...]
    bars: int
    seed: int
    pad: float = 0.0
    bass: float = 0.0
    bass_beats: float = 2.0
    arp: float = 0.0
    arp_beats: float = 0.5
    arp_wave: Callable = triangle
    melody: float = 0.0
    melody_beats: float = 1.0
    melody_wave: Callable = square


def midi(name, octave):
    return 12 * (octave + 1) + PITCH[name]


def chord_tones(root, quality, octave):
    base = midi(root, octave)
    return [base + step for step in QUALITY[quality]]


# Track id -> track. Ids follow MusicTrackIds.For: 1 + season * 2 + night (spring day 1, spring night 2 ... winter night 8).
# Day is brighter and busier (major keys, arpeggios, a square melody); night is slower with fewer voices; winter is sparse.
TRACKS = {
    1: Track("spring_day", bpm=120, key="C", minor=False,
             chords=(("C", "maj"), ("G", "maj"), ("A", "min"), ("F", "maj")), bars=4, seed=1,
             bass=0.16, bass_beats=2, arp=0.10, arp_beats=0.5, melody=0.10, melody_beats=1),
    2: Track("spring_night", bpm=88, key="A", minor=True,
             chords=(("A", "min"), ("F", "maj"), ("C", "maj"), ("G", "maj")), bars=3, seed=2,
             pad=0.08, bass=0.12, bass_beats=4, melody=0.07, melody_beats=2, melody_wave=sine),
    3: Track("summer_day", bpm=128, key="G", minor=False,
             chords=(("G", "maj"), ("D", "maj"), ("E", "min"), ("C", "maj")), bars=4, seed=3,
             bass=0.14, bass_beats=1, arp=0.08, arp_beats=0.5, melody=0.09, melody_beats=0.5),
    4: Track("summer_night", bpm=96, key="E", minor=True,
             chords=(("E", "min"), ("C", "maj"), ("G", "maj"), ("D", "maj")), bars=3, seed=4,
             bass=0.12, bass_beats=2, arp=0.06, arp_beats=1, arp_wave=sine, melody=0.06, melody_beats=2,
             melody_wave=sine),
    5: Track("fall_day", bpm=96, key="F", minor=False,
             chords=(("F", "maj7"), ("D", "min7"), ("Bb", "maj7"), ("C", "dom7")), bars=3, seed=5,
             pad=0.09, bass=0.14, bass_beats=2, melody=0.07, melody_beats=1, melody_wave=sine),
    6: Track("fall_night", bpm=76, key="D", minor=True,
             chords=(("D", "min7"), ("Bb", "maj7"), ("F", "maj7"), ("C", "maj")), bars=3, seed=6,
             pad=0.08, bass=0.10, bass_beats=4),
    7: Track("winter_day", bpm=84, key="A", minor=True,
             chords=(("A", "min"), ("F", "maj"), ("C", "maj"), ("G", "maj")), bars=3, seed=7,
             pad=0.07, bass=0.12, bass_beats=4, melody=0.08, melody_beats=4, melody_wave=sine),
    8: Track("winter_night", bpm=60, key="D", minor=True,
             chords=(("D", "min"), ("Bb", "maj"), ("F", "maj"), ("C", "maj")), bars=2, seed=8,
             pad=0.07, bass=0.08, bass_beats=8, melody=0.05, melody_beats=8, melody_wave=sine),
}


def voice(buf, spb, beat, beats, note, wave, amp, attack=0.01, decay=3.0):
    """Adds one note into the loop: it starts at `beat` and lasts `beats`, with a linear attack, an exponential decay and a short fade-out."""
    n = len(buf)
    start = round(beat * spb)
    length = round((beat + beats) * spb) - start
    freq = 440.0 * 2.0 ** ((note - 69) / 12.0)
    fade = min(length, round(0.02 * RATE))
    for i in range(length):
        t = i / RATE
        e = min(1.0, t / attack) * math.exp(-decay * t)
        if length - i < fade:
            e *= (length - i) / fade
        buf[(start + i) % n] += amp * e * wave((freq * t) % 1.0)


def compose(track):
    spb = 60.0 / track.bpm * RATE
    beats_per_chord = 4 * track.bars
    n = round(beats_per_chord * len(track.chords) * spb)
    buf = [0.0] * n
    rng = random.Random(track.seed)
    steps = MINOR if track.minor else MAJOR
    scale = [midi(track.key, MELODY_OCTAVE) + 12 * octave + step for octave in (0, 1) for step in steps]
    index = 0

    for c, (root, quality) in enumerate(track.chords):
        base = c * beats_per_chord
        tones = chord_tones(root, quality, ARP_OCTAVE)
        pcs = {note % 12 for note in chord_tones(root, quality, MELODY_OCTAVE)}

        if track.pad:
            for note in chord_tones(root, quality, PAD_OCTAVE):
                voice(buf, spb, base, beats_per_chord, note, sine, track.pad, attack=0.35, decay=0.15)

        if track.bass:
            for beat in range(0, beats_per_chord, round(track.bass_beats)):
                voice(buf, spb, base + beat, track.bass_beats, midi(root, BASS_OCTAVE), triangle, track.bass, decay=1.2)

        if track.arp:
            for k in range(round(beats_per_chord / track.arp_beats)):
                note = tones[k % len(tones)]
                voice(buf, spb, base + k * track.arp_beats, track.arp_beats, note, track.arp_wave, track.arp, decay=4.0)

        if track.melody:
            strong = [i for i, note in enumerate(scale) if note % 12 in pcs]
            for k in range(round(beats_per_chord / track.melody_beats)):
                beat = k * track.melody_beats
                if beat == 0:
                    index = min(strong, key=lambda i: abs(i - index))
                else:
                    index = min(len(scale) - 1, max(0, index + rng.choice((-2, -1, -1, 1, 1, 2))))
                voice(buf, spb, base + beat, track.melody_beats, scale[index], track.melody_wave, track.melody, decay=2.0)
    return buf


def soften(samples, k=0.35):
    """One-pole low-pass, run twice: the second pass is kept, so its state at the loop point is the loop's own."""
    out = samples
    for _ in range(2):
        y = 0.0
        out = []
        for x in samples:
            y += k * (x - y)
            out.append(y)
    return out


def to_pcm8(samples, peak=0.75):
    top = max(1e-9, max(abs(v) for v in samples))
    return bytes(min(255, max(0, round(128 + 127 * v / top * peak))) for v in samples)


def encode(pcm):
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(1)
        w.setframerate(RATE)
        w.writeframes(pcm)
    return buf.getvalue()


def build():
    files = {}
    for track_id in sorted(TRACKS):
        track = TRACKS[track_id]
        files[f"music_{track.name}.wav"] = encode(to_pcm8(soften(compose(track))))
    return files


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=os.path.join(root, "wasm", "Nosum.Wasm", "wwwroot", "audio"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    files = build()
    if args.check:
        stale = []
        for name, data in files.items():
            path = os.path.join(args.out, name)
            if not os.path.exists(path) or open(path, "rb").read() != data:
                stale.append(name)
        for name in stale:
            print("out of date:", name)
        return 1 if stale else 0

    os.makedirs(args.out, exist_ok=True)
    for name, data in sorted(files.items()):
        with open(os.path.join(args.out, name), "wb") as f:
            f.write(data)
        print(f"{name}: {len(data)} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
