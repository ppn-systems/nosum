#!/usr/bin/env python3
"""Measures the loudness of every WAV in the audio folder (docs/adr/ADR-090; the numbers are in docs/qa/world-ambience-round2/audio-levels.md).

Standard library only, read-only. For each file it prints the duration, the true-sample peak in dBFS, the RMS over the whole file
and the RMS over its active part (samples above a -50 dBFS gate, so a short click is not diluted by its silence), and the
gated momentary loudness (the mean of 400 ms blocks above -50 dBFS, a stand-in for BS.1770 for files shorter than 400 ms).

usage: python3 -I tools/measure_audio.py [folder] [--csv out.csv]
"""
import csv
import math
import os
import struct
import sys
import wave

GATE = 10 ** (-50 / 20)


def read_samples(path):
    with wave.open(path, "rb") as w:
        channels, width, rate = w.getnchannels(), w.getsampwidth(), w.getframerate()
        frames = w.readframes(w.getnframes())
    if width == 1:
        values = [(b - 128) / 128.0 for b in frames]
    elif width == 2:
        count = len(frames) // 2
        values = [v / 32768.0 for v in struct.unpack("<%dh" % count, frames)]
    else:
        raise ValueError("unsupported sample width %d in %s" % (width, path))
    if channels > 1:  # mix down to mono so every file is measured the same way
        values = [sum(values[i:i + channels]) / channels for i in range(0, len(values), channels)]
    return values, rate


def db(x):
    return 20 * math.log10(x) if x > 0 else float("-inf")


def measure(path):
    samples, rate = read_samples(path)
    n = len(samples)
    peak = max((abs(s) for s in samples), default=0.0)
    rms_all = math.sqrt(sum(s * s for s in samples) / n) if n else 0.0
    active = [s for s in samples if abs(s) >= GATE]
    rms_active = math.sqrt(sum(s * s for s in active) / len(active)) if active else 0.0
    block = int(rate * 0.4)
    blocks = []
    for start in range(0, max(n - block, 0) + 1, max(block // 4, 1)):  # 75 % overlap, as BS.1770 gating
        chunk = samples[start:start + block]
        ms = sum(s * s for s in chunk) / len(chunk)
        if db(math.sqrt(ms)) > -50:
            blocks.append(ms)
    momentary = db(math.sqrt(sum(blocks) / len(blocks))) if blocks else float("-inf")
    return {
        "file": os.path.basename(path),
        "seconds": round(n / rate, 2),
        "peak_dbfs": round(db(peak), 1),
        "rms_dbfs": round(db(rms_all), 1),
        "active_rms_dbfs": round(db(rms_active), 1),
        "gated_momentary_dbfs": round(momentary, 1),
    }


def main(argv):
    folder = "wasm/Nosum.Wasm/wwwroot/audio"
    csv_out = None
    args = list(argv)
    if "--csv" in args:
        i = args.index("--csv")
        csv_out = args[i + 1]
        del args[i:i + 2]
    if args:
        folder = args[0]
    rows = [measure(os.path.join(folder, f)) for f in sorted(os.listdir(folder)) if f.endswith(".wav")]
    header = ["file", "seconds", "peak_dbfs", "rms_dbfs", "active_rms_dbfs", "gated_momentary_dbfs"]
    print("\t".join(header))
    for r in rows:
        print("\t".join(str(r[h]) for h in header))
    if csv_out:
        with open(csv_out, "w", newline="") as fh:
            writer = csv.DictWriter(fh, fieldnames=header)
            writer.writeheader()
            writer.writerows(rows)


if __name__ == "__main__":
    main(sys.argv[1:])
