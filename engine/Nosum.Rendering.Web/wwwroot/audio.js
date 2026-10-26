// Nosum browser audio (Web Audio API, no packages). Sibling of renderer.js, which hands it the sound commands of each frame
// (docs/adr/ADR-039): sounds, the music track and the ambient loop ride in the frame's command buffer, so audio has no interop crossing of its own.
//
// - Buffers are fetched and decoded once at load, with an OfflineAudioContext (it needs no user gesture).
// - The real AudioContext is created on the first user gesture only (browser autoplay rule); until then every command is remembered
//   (music, ambient loop) or dropped (one-shot sounds are stale by the time the player clicks).
// - A sound or track with no loaded buffer is silence, never an error.

const SOUND = 0, LOOP = 1, TRACK = 2;
const MAX_VOICES = 16;
const MUSIC_FADE_SECONDS = 1.5;
const LOOP_FADE_SECONDS = 0.8;
const GESTURES = ["pointerdown", "pointerup", "mousedown", "keydown", "touchend"];

const sounds = [];          // sound id -> AudioBuffer
const loops = [];           // ambient loop id -> AudioBuffer
const tracks = [];          // music track id -> AudioBuffer (tools/synth_music.py, ADR-039)
let decoder = null;         // OfflineAudioContext used for decoding, created lazily
let ctx = null;             // the AudioContext, created on the first gesture
let musicBus = null;
let soundBus = null;
let musicLevel = 0.49;      // gains computed by .NET (Nosum.Rendering.VolumeCurve) from the settings, mute included
let soundLevel = 0.49;
let voices = 0;
let uiClickId = 0;

// A looping layer (the music, the ambient loop): what .NET wants, and what is playing.
function makeLayer(bank, fadeSeconds) {
    return { bank, fade: fadeSeconds, want: 0, volume: 100, id: 0, volumeNow: -1, source: null, gain: null };
}
const music = makeLayer(tracks, MUSIC_FADE_SECONDS);
const ambient = makeLayer(loops, LOOP_FADE_SECONDS);

// Fetches and decodes one file. Failures (offline, missing file, undecodable) leave the id silent.
export async function loadAudio(kind, id, url) {
    try {
        const response = await fetch(url);
        if (!response.ok) { return false; }
        decoder = decoder || new OfflineAudioContext(1, 1, 44100);
        const buffer = await decoder.decodeAudioData(await response.arrayBuffer());
        (kind === LOOP ? loops : kind === TRACK ? tracks : sounds)[id] = buffer;
        if (kind !== SOUND) { syncLayer(kind === LOOP ? ambient : music); }
        return true;
    } catch {
        return false;
    }
}

// Sets the two bus gains (0..1, from the settings; mute is gain 0). Called when the settings change, never from the frame.
export function setAudioLevels(musicGain, soundGain) {
    musicLevel = musicGain;
    soundLevel = soundGain;
    if (ctx) {
        musicBus.gain.setTargetAtTime(musicLevel, ctx.currentTime, 0.05);
        soundBus.gain.setTargetAtTime(soundLevel, ctx.currentTime, 0.05);
    }
}

// The sound the interface plays for a click on any button (played here, in JS, with no call into .NET). 0 turns it off.
export function setUiClickSound(id) {
    uiClickId = id;
}

// Starts the audio context on the first user gesture. Installed when the module loads (at page start, before the sign-in screen), so the very first click counts. The listeners stay until the context is really running (a touch gesture may need its second event).
function installAudioUnlock() {
    const unlock = () => {
        if (!ctx) {
            const Context = globalThis.AudioContext || globalThis.webkitAudioContext;
            if (!Context) { removeGestureListeners(unlock); return; }
            ctx = new Context();
            musicBus = ctx.createGain();
            soundBus = ctx.createGain();
            musicBus.gain.value = musicLevel;
            soundBus.gain.value = soundLevel;
            musicBus.connect(ctx.destination);
            soundBus.connect(ctx.destination);
        }
        ctx.resume().then(() => {
            if (ctx.state === "running") {
                removeGestureListeners(unlock);
                syncLayer(music);
                syncLayer(ambient);
            }
        }, () => { /* refused: wait for the next gesture */ });
    };
    for (const type of GESTURES) { window.addEventListener(type, unlock, { capture: true, passive: true }); }

    // A click on any button of the interface ticks. Plain DOM event: the sound does not enter .NET.
    document.addEventListener("click", e => {
        const t = e.target;
        if (uiClickId > 0 && t && typeof t.closest === "function") {
            const button = t.closest("button, [role=button], a[href]");
            if (button && !button.disabled && button.getAttribute("aria-disabled") !== "true") { playSound(uiClickId, 100, 0); }
        }
    });
}

function removeGestureListeners(listener) {
    for (const type of GESTURES) { window.removeEventListener(type, listener, { capture: true }); }
}

// One command of a frame (renderer.js calls this for the ids -7, -8 and -9; a, b, c are the srcX, srcY and srcW fields).
export function audioCommand(id, a, b, c) {
    if (id === -7) {
        playSound(a, b, c);
    } else if (id === -8) {
        music.want = a;
        music.volume = 100;
        syncLayer(music);
    } else {
        ambient.want = a;
        ambient.volume = b;
        syncLayer(ambient);
    }
}

function running() {
    return ctx !== null && ctx.state === "running";
}

function playSound(id, volume, pan) {
    const buffer = sounds[id];
    if (!buffer || !running() || voices >= MAX_VOICES) { return; }
    const source = ctx.createBufferSource();
    source.buffer = buffer;
    const gain = ctx.createGain();
    gain.gain.value = Math.max(0, Math.min(100, volume)) / 100;
    source.connect(gain);
    let tail = gain;
    if (pan !== 0 && typeof ctx.createStereoPanner === "function") {
        const panner = ctx.createStereoPanner();
        panner.pan.value = Math.max(-1, Math.min(1, pan / 100));
        gain.connect(panner);
        tail = panner;
    }
    tail.connect(soundBus);
    voices++;
    source.onended = () => { voices--; };
    source.start();
}

// Makes the playing layer match what was asked: another id cross-fades, the same id with another volume ramps.
function syncLayer(layer) {
    if (!running()) { return; }
    const buffer = layer.want > 0 ? layer.bank[layer.want] : null;
    const wantId = buffer ? layer.want : 0;
    const now = ctx.currentTime;
    if (wantId !== layer.id) {
        if (layer.source) {
            const old = layer.source, oldGain = layer.gain;
            oldGain.gain.cancelScheduledValues(now);
            oldGain.gain.setValueAtTime(oldGain.gain.value, now);
            oldGain.gain.linearRampToValueAtTime(0, now + layer.fade);
            old.stop(now + layer.fade + 0.05);
            layer.source = layer.gain = null;
        }
        layer.id = wantId;
        layer.volumeNow = -1;
        if (buffer) {
            const source = ctx.createBufferSource();
            source.buffer = buffer;
            source.loop = true;
            const gain = ctx.createGain();
            gain.gain.value = 0;
            source.connect(gain);
            gain.connect(layer === music ? musicBus : soundBus);
            source.start();
            layer.source = source;
            layer.gain = gain;
        }
    }
    if (layer.gain && layer.volumeNow !== layer.volume) {
        layer.volumeNow = layer.volume;
        layer.gain.gain.cancelScheduledValues(now);
        layer.gain.gain.setValueAtTime(layer.gain.gain.value, now);
        layer.gain.gain.linearRampToValueAtTime(layer.volume / 100, now + layer.fade);
    }
}

installAudioUnlock();
