// Nosum browser render backend (JS side of Nosum.Rendering.Web.WebRenderBackend).
//
// Frame flow (docs/adr/ADR-002): requestAnimationFrame -> one [JSExport] call into .NET with the
// held keys and canvas size -> .NET calls submitFrame once with the whole draw list.
// submitFrame receives a MemoryView over .NET memory that is valid only during the call:
// decode and draw synchronously, never keep the view.

const textures = [];          // texture id -> ImageBitmap
const tinted = new Map();     // "id:rgba" -> canvas with the tint applied
let canvas = null;
let ctx = null;
let keys = 0;
let edges = 0;  // one-shot keys pressed since the last frame, packed above the held bits (Nosum.Core.InputEdges)

const KEY_BITS = {
    ArrowUp: 1, KeyW: 1,
    ArrowDown: 2, KeyS: 2,
    ArrowLeft: 4, KeyA: 4,
    ArrowRight: 8, KeyD: 8,
};

// Keys that fire once per press. Bit values match Nosum.Core.InputEdges.
const EDGE_BITS = {
    KeyE: 1, Space: 1,
    Digit1: 2, Digit2: 4, Digit3: 8, Digit4: 16, Digit5: 32, Digit6: 64, Digit7: 128, Digit8: 256, Digit9: 512,
    Numpad1: 2, Numpad2: 4, Numpad3: 8, Numpad4: 16, Numpad5: 32, Numpad6: 64, Numpad7: 128, Numpad8: 256, Numpad9: 512,
    KeyI: 1024,
    KeyR: 2048,
    KeyF: 4096,
    KeyQ: 8192,
    KeyM: 16384, KeyC: 32768, KeyT: 65536, KeyG: 131072,
    Escape: 262144,
    KeyH: 524288,
};

// Typing in a form field (the upgrade form) must not move the farmer or select slots.
function isTyping(e) {
    const t = e.target;
    return !!t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT" || t.isContentEditable);
}

export async function loadTexture(id, url) {
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`texture ${url}: HTTP ${response.status}`);
    }
    textures[id] = await createImageBitmap(await response.blob());
}

export async function start(canvasId, assemblyName, exportPath) {
    canvas = document.getElementById(canvasId);
    ctx = canvas.getContext("2d");
    window.addEventListener("keydown", e => {
        if (e.code === "Escape") {
            // Escape closes the open panel even from a form field: leave the field so the game gets the keyboard back.
            if (isTyping(e)) { e.target.blur(); }
            if (!e.repeat) { edges |= EDGE_BITS.Escape; }
            return;
        }
        if (isTyping(e) || e.ctrlKey || e.altKey || e.metaKey) { return; }
        if (e.key === "?") { if (!e.repeat) { edges |= EDGE_BITS.KeyH; } e.preventDefault(); return; }
        const b = KEY_BITS[e.code];
        if (b) { keys |= b; e.preventDefault(); return; }
        const edge = EDGE_BITS[e.code];
        if (edge) { if (!e.repeat) { edges |= edge; } e.preventDefault(); }
    });
    window.addEventListener("keyup", e => { const b = KEY_BITS[e.code]; if (b) { keys &= ~b; } });
    window.addEventListener("blur", () => { keys = 0; });

    const runtime = await globalThis.getDotnetRuntime(0);
    const exports = await runtime.getAssemblyExports(assemblyName);
    const frame = exportPath.split(".").reduce((o, k) => o[k], exports);

    const tick = time => {
        const w = canvas.clientWidth, h = canvas.clientHeight;
        if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
        const pressed = edges;
        edges = 0;
        frame(time, keys | (pressed << 8), w, h);
        requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
}

export function submitFrame(view) {
    const bytes = view.slice();
    const data = new Int32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength >> 2);
    const count = data[0], scale = data[1];

    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = "#000";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(scale, 0, 0, scale, 0, 0);

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
        const image = tint === -1 ? textures[id] : tintedTexture(id, tint);
        if (!image) {
            continue;
        }
        if (flags & 1) {
            ctx.save();
            ctx.translate(dx + w, dy);
            ctx.scale(-1, 1);
            ctx.drawImage(image, sx, sy, w, h, 0, 0, w, h);
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
    result = new OffscreenCanvas(source.width, source.height);
    const c = result.getContext("2d");
    const u = tint >>> 0;
    c.drawImage(source, 0, 0);
    c.globalCompositeOperation = "multiply";
    c.fillStyle = `rgb(${u >>> 24}, ${(u >>> 16) & 255}, ${(u >>> 8) & 255})`;
    c.fillRect(0, 0, source.width, source.height);
    c.globalCompositeOperation = "destination-in";
    c.drawImage(source, 0, 0);
    tinted.set(key, result);
    return result;
}
