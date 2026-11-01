"""Listening proxy for the audio mix (docs/adr/ADR-090 amendment, round 4). Standard library + ffmpeg (ebur128), read-only on the audio folder.

Composite listening proxy: music bed + footsteps (every 0.5 s) + one pickaxe swing + one UI click, mixed with the AudioMix gains,
the hook volumes and the 0.49 bus gains. Writes one WAV per footstep level (before, after) and reports integrated and momentary LUFS."""
import math, os, re, struct, subprocess, wave
import sys
A = "wasm/Nosum.Wasm/wwwroot/audio"  # run from the repository root
OUT = sys.argv[1] if len(sys.argv) > 1 else "listening-proxy-out"  # where the composite WAVs are written
BUS = 0.49
def db2g(d): return 10 ** (d / 20)
def load(name):
    with wave.open(os.path.join(A, name), "rb") as w:
        ch, width, rate = w.getnchannels(), w.getsampwidth(), w.getframerate()
        raw = w.readframes(w.getnframes())
    n = len(raw) // (width * ch)
    if width == 2:
        vals = struct.unpack("<%dh" % (n * ch), raw); s = [v / 32768 for v in vals[::ch]]
    else:
        s = [(b - 128) / 128 for b in raw[::ch]]
    return s, rate
RATE = 48000
def resample_nn(s, rate):
    if rate == RATE: return s
    ratio = rate / RATE
    return [s[min(int(i * ratio), len(s) - 1)] for i in range(int(len(s) / ratio))]
def cue(name, target, measured, volume):
    s, r = load(name); s = resample_nn(s, r)
    g = db2g((target - measured) + 20 * math.log10(volume / 100) + 20 * math.log10(BUS))
    return [v * g for v in s]
def add(buf, s, at_s):
    o = int(at_s * RATE)
    for i, v in enumerate(s):
        if o + i < len(buf): buf[o + i] += v
def write(path, buf):
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", max(-32768, min(32767, int(v * 32767)))) for v in buf))
def lufs(path):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", path, "-af", "ebur128=metadata=1,ametadata=print:key=lavfi.r128.M:file=-", "-f", "null", "-"], capture_output=True, text=True)
    ms = [float(m) for m in re.findall(r"lavfi\.r128\.M=(-?[\d.]+|-inf)", r.stdout) if m != "-inf"]
    e = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", path, "-af", "ebur128", "-f", "null", "-"], capture_output=True, text=True).stderr
    return float(re.findall(r"I:\s+(-?[\d.]+) LUFS", e)[-1]), max(ms)
# music bed: 12 s of music_spring_day at its class gain and the bus
bed_s, br = load("music_spring_day.wav"); bed_s = resample_nn(bed_s, br)
gm = db2g((-16.0 - (-14.5)) + 20 * math.log10(BUS))
DUR = 12
buf_music = [v * gm for v in bed_s[: RATE * DUR]] + [0.0] * max(0, RATE * DUR - len(bed_s))
os.makedirs(OUT, exist_ok=True)
results = {}
for label, fs_target in (("before", -18.0), ("after", -16.5)):
    fs = cue("footstep.wav", fs_target, -21.8, 70)
    buf = list(buf_music)
    for k in range(int(DUR / 0.5) - 1):
        add(buf, fs, 0.5 + 0.5 * k)
    tool = cue("tool_pickaxe.wav", -16.0, -18.2, 100); add(buf, tool, 4.0)
    ui = cue("ui_click.wav", -18.0, -18.2, 100); add(buf, ui, 8.0)
    path = os.path.join(OUT, f"composite_{label}.wav"); write(path, buf)
    results[label] = lufs(path)
bed_path = os.path.join(OUT, "composite_music_only.wav"); write(bed_path, buf_music)
results["music_only"] = lufs(bed_path)
for k, (i, mx) in results.items():
    print(f"{k:12s} integrated {i:6.1f} LUFS   momentary max {mx:6.1f} LUFS")

# usage: python3 -I tools/listening_proxy.py [out-dir]   (from the repository root; needs ffmpeg on PATH)
