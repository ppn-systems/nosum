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

const KEY_BITS = {
    ArrowUp: 1, KeyW: 1,
    ArrowDown: 2, KeyS: 2,
    ArrowLeft: 4, KeyA: 4,
    ArrowRight: 8, KeyD: 8,
};

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
    window.addEventListener("keydown", e => { const b = KEY_BITS[e.code]; if (b) { keys |= b; e.preventDefault(); } });
    window.addEventListener("keyup", e => { const b = KEY_BITS[e.code]; if (b) { keys &= ~b; } });
    window.addEventListener("blur", () => { keys = 0; });

    const runtime = await globalThis.getDotnetRuntime(0);
    const exports = await runtime.getAssemblyExports(assemblyName);
    const frame = exportPath.split(".").reduce((o, k) => o[k], exports);

    const tick = time => {
        const w = canvas.clientWidth, h = canvas.clientHeight;
        if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
        frame(time, keys, w, h);
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
