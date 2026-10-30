// Nosum browser render backend (JS side of Nosum.Rendering.Web.WebRenderBackend).
//
// Frame flow (docs/adr/ADR-002): requestAnimationFrame -> one [JSExport] call into .NET with the
// held keys and canvas size -> .NET calls submitFrame once with the whole draw list.
// submitFrame receives a MemoryView over .NET memory that is valid only during the call:
// decode and draw synchronously, never keep the view.

import { audioCommand } from "./audio.js";
// The sound commands (-7 sound, -8 music, -9 ambient loop; docs/adr/ADR-039) go to audio.js; these are the module's exports .NET calls.
export { loadAudio, setAudioLevels, setUiClickSound } from "./audio.js";

const textures = [];          // texture id -> ImageBitmap
const placed = new Int32Array(256); // rectangles of the labels drawn this frame (x, y, w, h), to stack names that overlap
let placedCount = 0;
const labels = [];            // label id -> OffscreenCanvas with the pre-rendered text (glyph cache)
const tinted = new Map();     // "id:rgba" -> canvas with the tint applied
let canvas = null;
let ctx = null;
let keys = 0;
const lights = new Int32Array(64 * 3); // light sources of this frame (x, y, radius), cut out of the ambient tint (command -5, docs/adr/ADR-027)
let lightCount = 0;
let ambientLayer = null;      // full-canvas offscreen layer the ambient tint is built on (command -4)
let lightSprite = null;       // white-to-transparent radial gradient, scaled per light
let edges = 0;  // one-shot keys pressed since the last frame, packed above the held bits (Nosum.Core.InputEdges)
let mouseX = 0, mouseY = 0, mouseHeld = 0;  // the mouse over the canvas, CSS pixels, and 1 while its button is down (drag-to-sow)

// Keys that never change: the arrows, Space and the number keys (the hotbar), Escape. The other keys are the player's bindings (setKeyBindings),
// so the maps below are built from both. Bit values match Nosum.Core.InputState (held) and Nosum.Core.InputEdges (pressed once).
const FIXED_HELD = { ArrowUp: 1, ArrowDown: 2, ArrowLeft: 4, ArrowRight: 8 };
const FIXED_EDGES = {
    Space: 1,
    Digit1: 2, Digit2: 4, Digit3: 8, Digit4: 16, Digit5: 32, Digit6: 64, Digit7: 128, Digit8: 256, Digit9: 512,
    Numpad1: 2, Numpad2: 4, Numpad3: 8, Numpad4: 16, Numpad5: 32, Numpad6: 64, Numpad7: 128, Numpad8: 256, Numpad9: 512,
    Escape: 262144,
    KeyZ: 2097152, KeyX: 4194304, KeyV: 8388608,
};

// The names are Nosum.Wasm.ViewModels.Settings.GameAction.
const ACTION_BITS = {
    MoveUp: { held: 1 }, MoveDown: { held: 2 }, MoveLeft: { held: 4 }, MoveRight: { held: 8 },
    Use: { edge: 1 }, Inventory: { edge: 1024 }, Rotate: { edge: 2048 }, Grab: { edge: 4096 }, PickUp: { edge: 8192 },
    Wardrobe: { edge: 32768 }, Trade: { edge: 65536 }, Coop: { edge: 131072 },
    Help: { edge: 524288 }, Profile: { edge: 1048576 },
    Minimap: { edge: 16777216 }, WorldMap: { edge: 33554432 }, Journal: { edge: 67108864 },
};
const DEFAULT_BINDINGS = "MoveUp=KeyW;MoveDown=KeyS;MoveLeft=KeyA;MoveRight=KeyD;Use=KeyE;Inventory=KeyI;Wardrobe=KeyC;Trade=KeyT;Coop=KeyG;Profile=KeyN;Help=KeyH;Rotate=KeyR;Grab=KeyF;PickUp=KeyQ;Minimap=KeyM;WorldMap=Tab;Journal=KeyJ";

let KEY_BITS = {};
let EDGE_BITS = {};

// Sets the keys of the actions: "Action=Code;Action=Code" (every action). Called when the settings change, never from the frame.
export function setKeyBindings(text) {
    const held = { ...FIXED_HELD }, edge = { ...FIXED_EDGES };
    for (const pair of String(text || DEFAULT_BINDINGS).split(";")) {
        const at = pair.indexOf("=");
        const bits = at > 0 ? ACTION_BITS[pair.slice(0, at)] : null;
        if (!bits) { continue; }
        const code = pair.slice(at + 1);
        if (bits.held) { held[code] = (held[code] || 0) | bits.held; } else { edge[code] = (edge[code] || 0) | bits.edge; }
    }
    KEY_BITS = held;
    EDGE_BITS = edge;
    keys = 0;
}

setKeyBindings(DEFAULT_BINDINGS);

// Typing in a form field (the upgrade form) must not move the farmer or select slots. So must typing in the chat box, which marks the page the
// moment Enter opens it, before the box has focus (chat-input.js, issue #144).
function isTyping(e) {
    const t = e.target;
    if (document.documentElement.hasAttribute("data-text-entry")) { return true; }
    return !!t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT" || t.isContentEditable);
}

// The settings page is waiting for the next key to bind: the key belongs to the page, not the game. The page answers the key (and stops waiting) before this
// handler sees it, so the answer is read in the capture phase, first of all, and remembered on the event.
function isCapturing(e) {
    return e.nosumCapture === true;
}

function markCapturing(e) {
    const t = e.target;
    e.nosumCapture = !!t && typeof t.closest === "function" && !!t.closest("[data-capture-key]");
}

// Space and Enter on a focused button or link activate it (keyboard users): the game must not also act on the key.
function isActivating(e) {
    const t = e.target;
    return !!t && (e.code === "Space" || e.code === "Enter") && (t.tagName === "BUTTON" || t.tagName === "A" || t.getAttribute?.("role") === "button");
}

// --- touch (docs/adr/ADR-033 D2) ---
// The controls are plain elements drawn by the Razor TouchControls component: a stick ([data-touch=stick] with a [data-touch=knob] inside) and buttons
// ([data-touch-edge=<InputEdges bit>]). The pointer events are handled here and only change `touchHeld` / `edges`, which join the input of the next
// frame: there is no call into .NET per touch event.
let touchHeld = 0;
let stickPointer = -1;
const STICK_DEAD = 0.3;
const LONG_PRESS_MS = 500;
const LONG_PRESS_SLOP = 12;
let press = null;

function moveStick(stick, e) {
    const rect = stick.getBoundingClientRect();
    const r = rect.width / 2;
    let dx = e.clientX - (rect.left + r), dy = e.clientY - (rect.top + r);
    const len = Math.hypot(dx, dy);
    if (len > r) { dx = dx / len * r; dy = dy / len * r; }
    let bits = 0;
    if (dx > STICK_DEAD * r) { bits |= 8; } else if (dx < -STICK_DEAD * r) { bits |= 4; }
    if (dy > STICK_DEAD * r) { bits |= 2; } else if (dy < -STICK_DEAD * r) { bits |= 1; }
    touchHeld = bits;
    const knob = stick.querySelector("[data-touch=knob]");
    if (knob) { knob.style.transform = `translate(${dx}px, ${dy}px)`; }
}

function releaseStick() {
    stickPointer = -1;
    touchHeld = 0;
    const knob = document.querySelector("[data-touch=knob]");
    if (knob) { knob.style.transform = ""; }
}

function installTouch() {
    document.addEventListener("pointerdown", e => {
        const t = e.target;
        if (!t || typeof t.closest !== "function") { return; }
        const stick = t.closest("[data-touch=stick]");
        if (stick) {
            e.preventDefault();
            stickPointer = e.pointerId;
            try { stick.setPointerCapture(e.pointerId); } catch { /* the pointer ended already */ }
            moveStick(stick, e);
            return;
        }
        const button = t.closest("[data-touch-edge]");
        if (button) {
            e.preventDefault();
            edges |= Number(button.getAttribute("data-touch-edge")) || 0;
            button.classList.add("down");
        }
    });
    document.addEventListener("pointermove", e => {
        if (e.pointerId === stickPointer) {
            const stick = document.querySelector("[data-touch=stick]");
            if (stick) { moveStick(stick, e); }
        }
        if (press && e.pointerId === press.id && Math.hypot(e.clientX - press.x, e.clientY - press.y) > LONG_PRESS_SLOP) {
            clearTimeout(press.timer);
            press = null;
        }
    });
    const end = e => {
        if (e.pointerId === stickPointer) { releaseStick(); }
        if (press && e.pointerId === press.id) { clearTimeout(press.timer); press = null; }
        document.querySelectorAll("[data-touch-edge].down").forEach(b => b.classList.remove("down"));
    };
    document.addEventListener("pointerup", end);
    document.addEventListener("pointercancel", end);
    // Holding a finger on the world uses the held item on the tile in front, like the action key (the world tile under the finger is not chosen).
    canvas.addEventListener("pointerdown", e => {
        if (e.pointerType === "mouse") { return; }
        clearTimeout(press?.timer);
        press = { id: e.pointerId, x: e.clientX, y: e.clientY, timer: setTimeout(() => { edges |= 1; press = null; }, LONG_PRESS_MS) };
    });
    canvas.addEventListener("contextmenu", e => e.preventDefault());
    // The mouse over the world: where it is and whether its button is down. The frame reads both for drag-to-sow (GameLoop.Frame).
    canvas.addEventListener("pointermove", e => { if (e.pointerType === "mouse") { mouseX = e.offsetX; mouseY = e.offsetY; } });
    canvas.addEventListener("pointerdown", e => { if (e.pointerType === "mouse" && e.button === 0) { mouseHeld = 1; mouseX = e.offsetX; mouseY = e.offsetY; } });
    canvas.addEventListener("pointerleave", () => { mouseHeld = 0; });
    document.addEventListener("pointerup", e => { if (e.pointerType === "mouse") { mouseHeld = 0; } });
}

export async function loadTexture(id, url) {
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`texture ${url}: HTTP ${response.status}`);
    }
    textures[id] = await createImageBitmap(await response.blob());
}

// Renders a text label once into a bitmap: the glyph shape is thresholded to hard pixels (no soft edges) and
// outlined in black, so a nickname stays crisp when the world scale magnifies it. Called when a name arrives,
// never from the frame; the frame only draws the bitmap by id (command texture -3).
const LABEL_FILL = ["#ffffff", "#ffe08a", "#ff5757", "#63c74d", "#c084fc"];
const LABEL_FONT = "bold 11px 'Segoe UI', system-ui, sans-serif";

export function registerLabel(id, text, style) {
    const probe = new OffscreenCanvas(1, 1).getContext("2d");
    probe.font = LABEL_FONT;
    const m = probe.measureText(text);
    const ascent = Math.ceil(m.fontBoundingBoxAscent || 9);
    const descent = Math.ceil(m.fontBoundingBoxDescent || 3);
    const w = Math.ceil(m.width) + 2, h = ascent + descent + 2;

    const shape = new OffscreenCanvas(w, h);
    const sc = shape.getContext("2d", { willReadFrequently: true });
    sc.font = LABEL_FONT;
    sc.textBaseline = "alphabetic";
    sc.fillStyle = "#fff";
    sc.fillText(text, 1, ascent + 1);
    const px = sc.getImageData(0, 0, w, h);
    for (let i = 3; i < px.data.length; i += 4) { px.data[i] = px.data[i] >= 110 ? 255 : 0; }
    sc.putImageData(px, 0, 0);

    const colour = (css) => {
        const c = new OffscreenCanvas(w, h), cc = c.getContext("2d");
        cc.drawImage(shape, 0, 0);
        cc.globalCompositeOperation = "source-in";
        cc.fillStyle = css;
        cc.fillRect(0, 0, w, h);
        return c;
    };
    const dark = colour("#000"), fill = colour(LABEL_FILL[style] || LABEL_FILL[0]);

    const out = new OffscreenCanvas(w + 2, h + 2), oc = out.getContext("2d");
    for (let dy = 0; dy <= 2; dy++) { for (let dx = 0; dx <= 2; dx++) { if (dx !== 1 || dy !== 1) { oc.drawImage(dark, dx, dy); } } }
    oc.drawImage(fill, 1, 1);
    labels[id] = out;
}

export async function start(canvasId, assemblyName, exportPath) {
    canvas = document.getElementById(canvasId);
    ctx = canvas.getContext("2d");
    window.addEventListener("keydown", markCapturing, true);
    window.addEventListener("keydown", e => {
        if (isCapturing(e)) { return; }
        if (e.code === "Escape") {
            // Escape closes the open panel even from a form field: leave the field so the game gets the keyboard back.
            if (isTyping(e)) { e.target.blur(); }
            if (!e.repeat) { edges |= EDGE_BITS.Escape; }
            return;
        }
        if (isTyping(e) || isActivating(e) || e.ctrlKey || e.altKey || e.metaKey) { return; }
        if (e.key === "?") { if (!e.repeat) { edges |= ACTION_BITS.Help.edge; } e.preventDefault(); return; }
        const b = KEY_BITS[e.code];
        if (b) { keys |= b; e.preventDefault(); return; }
        const edge = EDGE_BITS[e.code];
        if (edge) { if (!e.repeat) { edges |= edge; } e.preventDefault(); }
    });
    window.addEventListener("keyup", e => { const b = KEY_BITS[e.code]; if (b) { keys &= ~b; } });
    window.addEventListener("blur", () => { keys = 0; releaseStick(); });
    installTouch();

    const runtime = await globalThis.getDotnetRuntime(0);
    const exports = await runtime.getAssemblyExports(assemblyName);
    const frame = exportPath.split(".").reduce((o, k) => o[k], exports);

    const tick = time => {
        const w = canvas.clientWidth, h = canvas.clientHeight;
        if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
        const pressed = edges;
        edges = 0;
        frame(time, (keys | touchHeld) | (pressed << 4), w, h, mouseX, mouseY, mouseHeld);
        requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
}

// A MemoryView has no buffer or byteOffset (it is a .NET Span, not a typed array), so its bytes are copied into this scratch with copyTo, which
// allocates nothing once the scratch is big enough. The Int32 view shares the scratch's buffer.
let frameBytes = new Uint8Array(65536);
let frameWords = new Int32Array(frameBytes.buffer);

export function submitFrame(view) {
    const size = view.byteLength;
    if (frameBytes.length < size) {
        frameBytes = new Uint8Array(Math.max(size, frameBytes.length * 2));
        frameWords = new Int32Array(frameBytes.buffer);
    }
    view.copyTo(frameBytes);
    const data = frameWords;
    const count = data[0], scale = data[1];

    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = "#000";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    placedCount = 0;
    lightCount = 0;

    for (let i = 0, p = 2; i < count; i++, p += 9) {
        const id = data[p], sx = data[p + 1], sy = data[p + 2], w = data[p + 3], h = data[p + 4];
        const dx = data[p + 5], dy = data[p + 6], flags = data[p + 7], tint = data[p + 8];
        if (id === -2) {
            // Black fill (scene fades): tint holds the opacity, 0..255.
            ctx.globalAlpha = tint / 255;
            ctx.fillStyle = "#000";
            ctx.fillRect(dx, dy, w, h);
            ctx.globalAlpha = 1;
            continue;
        }
        if (id <= -7 && id >= -9) {
            // Sound, music track or ambient loop: srcX, srcY and srcW are the arguments.
            audioCommand(id, sx, sy, w);
            continue;
        }
        if (id === -4) {
            // Ambient tint: one full-canvas composite, with this frame's lights cut out of it. sx..h hold r, g, b and opacity.
            applyAmbient(sx, sy, w, h, scale);
            continue;
        }
        if (id === -5) {
            // Light source for the ambient pass: (dx, dy) is the centre, w the radius, both in unscaled screen pixels.
            if (lightCount < 64) {
                const l = lightCount * 3;
                lights[l] = dx; lights[l + 1] = dy; lights[l + 2] = w;
                lightCount++;
            }
            continue;
        }
        if (id === -6) {
            // Coloured rectangle (rain, snow): tint holds 0xRRGGBBAA.
            const u = tint >>> 0;
            ctx.fillStyle = `rgba(${u >>> 24}, ${(u >>> 16) & 255}, ${(u >>> 8) & 255}, ${(u & 255) / 255})`;
            ctx.fillRect(dx, dy, w, h);
            continue;
        }
        if (id === -3) {
            // Pre-rendered label: sx is the label id; (dx, dy) is its bottom centre.
            // Drawn in screen space at 1x at every zoom (a name stays small and its pixels stay hard). Two names that would overlap are
            // stacked: the later one is lifted above the earlier one, so close players read as a tidy column, not a smear.
            const label = labels[sx];
            if (label) {
                const lw = label.width, lh = label.height;
                const x = Math.round(dx * scale - lw / 2);
                let y = Math.round(dy * scale - lh);
                for (let k = 0; k < placedCount; k += 4) {
                    if (x < placed[k] + placed[k + 2] && x + lw > placed[k] && y < placed[k + 1] + placed[k + 3] && y + lh > placed[k + 1]) {
                        y = placed[k + 1] - lh;
                        k = -4;
                    }
                }
                if (placedCount + 4 <= placed.length) {
                    placed[placedCount] = x; placed[placedCount + 1] = y; placed[placedCount + 2] = lw; placed[placedCount + 3] = lh;
                    placedCount += 4;
                }
                ctx.save();
                ctx.setTransform(1, 0, 0, 1, 0, 0);
                ctx.drawImage(label, x, y);
                ctx.restore();
            }
            continue;
        }
        const image = (flags & 2) !== 0 ? whitenedTexture(id, tint) : tint === -1 ? textures[id] : tintedTexture(id, tint);
        if (!image) {
            continue;
        }
        const flip = (flags & 1) !== 0;
        const deg = flags >> 8;
        if (flip || deg !== 0) {
            ctx.save();
            ctx.translate(dx + w / 2, dy + h / 2);
            if (deg !== 0) {
                ctx.rotate((deg * Math.PI) / 180);
            }
            if (flip) {
                ctx.scale(-1, 1);
            }
            ctx.drawImage(image, sx, sy, w, h, -w / 2, -h / 2, w, h);
            ctx.restore();
        } else {
            ctx.drawImage(image, sx, sy, w, h, dx, dy, w, h);
        }
    }
}

// Multiply-tints a whole sheet once and caches it (hair, clothing colours).
function tintedTexture(id, tint) {
    const key = `${id}:${tint}`;
    let result = tinted.get(key);
    if (result) {
        return result;
    }
    const source = textures[id];
    if (!source) {
        return null;
    }
    if (tinted.size >= 256) {
        tinted.delete(tinted.keys().next().value);
    }
    result = new OffscreenCanvas(source.width, source.height);
    const c = result.getContext("2d");
    const u = tint >>> 0;
    c.drawImage(source, 0, 0);
    c.globalCompositeOperation = "multiply";
    c.fillStyle = `rgb(${u >>> 24}, ${(u >>> 16) & 255}, ${(u >>> 8) & 255})`;
    c.fillRect(0, 0, source.width, source.height);
    c.globalCompositeOperation = "destination-in";
    c.globalAlpha = (u & 255) / 255;
    c.drawImage(source, 0, 0);
    c.globalAlpha = 1;
    tinted.set(key, result);
    return result;
}

// Washes a sheet toward white by the alpha byte of the tint, over the sprite's own silhouette (the monster hit flash, docs/adr/ADR-076).
function whitenedTexture(id, tint) {
    const alpha = tint & 255;
    const key = `w${id}:${alpha}`;
    let result = tinted.get(key);
    if (result) {
        return result;
    }
    const source = textures[id];
    if (!source) {
        return null;
    }
    if (tinted.size >= 256) {
        tinted.delete(tinted.keys().next().value);
    }
    result = new OffscreenCanvas(source.width, source.height);
    const c = result.getContext("2d");
    c.drawImage(source, 0, 0);
    c.globalCompositeOperation = "source-atop";
    c.globalAlpha = alpha / 255;
    c.fillStyle = "#fff";
    c.fillRect(0, 0, source.width, source.height);
    c.globalAlpha = 1;
    c.globalCompositeOperation = "source-over";
    tinted.set(key, result);
    return result;
}

// Builds the night tint on an offscreen layer the size of the canvas, cuts a soft hole for every light, then composites the layer once.
// A faint warm glow is added inside each hole so a lamp reads as a lamp. Runs once per frame, only when the tint is visible.
function applyAmbient(r, g, b, a, scale) {
    if (a <= 0) {
        return;
    }
    if (!ambientLayer) {
        ambientLayer = new OffscreenCanvas(canvas.width, canvas.height);
        lightSprite = makeLightSprite();
    }
    if (ambientLayer.width !== canvas.width || ambientLayer.height !== canvas.height) {
        ambientLayer.width = canvas.width;
        ambientLayer.height = canvas.height;
    }
    const lc = ambientLayer.getContext("2d");
    lc.setTransform(1, 0, 0, 1, 0, 0);
    lc.globalCompositeOperation = "source-over";
    lc.clearRect(0, 0, ambientLayer.width, ambientLayer.height);
    lc.fillStyle = `rgba(${r}, ${g}, ${b}, ${a / 255})`;
    lc.fillRect(0, 0, ambientLayer.width, ambientLayer.height);
    lc.globalCompositeOperation = "destination-out";
    for (let i = 0; i < lightCount; i++) {
        const radius = lights[i * 3 + 2] * scale;
        lc.drawImage(lightSprite, lights[i * 3] * scale - radius, lights[i * 3 + 1] * scale - radius, radius * 2, radius * 2);
    }

    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.drawImage(ambientLayer, 0, 0);
    if (lightCount > 0) {
        ctx.globalCompositeOperation = "lighter";
        ctx.globalAlpha = 0.16 * (a / 255);
        for (let i = 0; i < lightCount; i++) {
            const radius = lights[i * 3 + 2] * scale * 0.8;
            ctx.drawImage(lightSprite, lights[i * 3] * scale - radius, lights[i * 3 + 1] * scale - radius, radius * 2, radius * 2);
        }
    }
    ctx.restore();
}

function makeLightSprite() {
    const size = 128;
    const c = new OffscreenCanvas(size, size);
    const cc = c.getContext("2d");
    const grad = cc.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    grad.addColorStop(0, "rgba(255, 214, 140, 1)");
    grad.addColorStop(0.45, "rgba(255, 200, 120, 0.8)");
    grad.addColorStop(1, "rgba(255, 190, 100, 0)");
    cc.fillStyle = grad;
    cc.fillRect(0, 0, size, size);
    return c;
}
