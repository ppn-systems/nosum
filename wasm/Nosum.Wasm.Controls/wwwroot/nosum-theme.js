// Nosum UI theme, script side (docs/adr/ADR-017). Three jobs:
//
// 1. The UI pixel. --u is one UI pixel in CSS pixels and equals one WORLD pixel: the engine zooms the open map by
//    round(canvas width / (26 tiles * 16 px)) (MapScene.ScaleFor, never less than 2), so the same formula gives --u. It is also
//    capped so the fixed-size menu window always fits the height. --tp is one pixel of the UI font (2/3 of --u, whole numbers).
// 2. The sprites. Cuts the pieces the UI needs out of the placeholder UI sheets (docs/adr/ADR-007: assets/, linked into this
//    package's wwwroot/ui/) and publishes each as a CSS custom property on :root for border-image / background-image:
//      --nosum-img-<name>   url("data:image/png;...")  the piece at its native size
//      --nosum-w-<name>, --nosum-h-<name>              size in sheet pixels (unitless; multiply by --u)
//      --nosum-cursor, --nosum-cursor-pointer          a `cursor` value, scaled by --u
//    "fat" sheets (MenuTiles.png, textBox.png) use 4 x 4 pixel blocks: a piece is drawn at (sheet px) / 4 * --u CSS pixels;
//    "thin" art (Cursors.png, glyphs below) is 1:1: (sheet px) * --u.
// 3. The tooltip. One element, created here, shows the data-tip text of whatever is under the mouse (first line = name).

const SHEETS = {
    menu: "ui/MenuTiles.png",     // fat: window frames, inset and slot bevels
    cursors: "ui/Cursors.png",    // thin: tabs, buttons, icons, mouse cursors (a few fat pieces too, noted below)
    textbox: "ui/textBox.png",    // fat: the text field
};

// Own 1-bit pictures for the tabs the sheets have no icon for (Nosum art, not placeholder content). '#' is ink, anything else clear.
const GLYPHS = {
    gear: [
        "....###....",
        ".##.###.##.",
        ".#########.",
        "..###.###..",
        "####...####",
        "####...####",
        "####...####",
        "..###.###..",
        ".#########.",
        ".##.###.##.",
        "....###....",
    ],
    shirt: [
        "..##...##..",
        ".#########.",
        "###########",
        "##.#####.##",
        "##.#####.##",
        "...#####...",
        "...#####...",
        "...#####...",
    ],
    person: [
        "...###...",
        "..#####..",
        "..#####..",
        "...###...",
        ".........",
        ".#######.",
        "#########",
        "#########",
        "#########",
        "#########",
    ],
    people: [
        "..##...##..",
        ".####.####.",
        ".####.####.",
        "..##...##..",
        "...........",
        "#####.#####",
        "#####.#####",
        "#####.#####",
        "#####.#####",
    ],
    heart: [
        ".###...###.",
        "#####.#####",
        "###########",
        "###########",
        ".#########.",
        "..#######..",
        "...#####...",
        "....###....",
        ".....#.....",
    ],
    envelope: [
        "###########",
        "##.#####.##",
        "###.###.###",
        "####...####",
        "###########",
        "###########",
        "###########",
    ],
    hat: [
        "...#####...",
        "..#######..",
        "..#######..",
        "###########",
    ],
    pants: [
        "#########",
        "#########",
        "#########",
        "####.####",
        "###...###",
        "###...###",
        "###...###",
        "###...###",
        "###...###",
    ],
    swap: [
        "...#.......",
        "...##......",
        "###########",
        "...##......",
        "...#.......",
        "...........",
        ".......#...",
        "......##...",
        "###########",
        "......##...",
        ".......#...",
    ],
};
const GLYPH_INK = "#5a2a0a";

// Item pictures the sheets have no usable art for (Nosum art): 16 x 16, drawn in one colour with four shades. '#' outline, 'L' light, 'o' base,
// 'D' shade, 'B' belt and cuff. Published as --nosum-img-<name> and used by ItemIcons (ItemIcon.ThemeImage).
const PANTS_ART = [
    "................",
    "..############..",
    ".#LLLLLLLLLLLL#.",
    ".#BBBBBBBBBBBB#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#LoooD##LoooD#.",
    ".#BBBBB##BBBBB#.",
    ".##############.",
    "................",
];
const ITEM_ART = {
    "item-pants-blue": ["#3e5f8a", PANTS_ART],
    "item-pants-khaki": ["#c2b280", PANTS_ART],
};

// Shade a #rrggbb colour: factor < 1 darkens, > 1 lightens (towards white).
function shade(hex, factor) {
    const n = parseInt(hex.slice(1), 16);
    const channel = (v) => Math.round(factor <= 1 ? v * factor : v + (255 - v) * (factor - 1));
    return `rgb(${channel((n >> 16) & 255)},${channel((n >> 8) & 255)},${channel(n & 255)})`;
}

function drawItem(color, rows) {
    const palette = { "#": shade(color, 0.38), L: shade(color, 1.35), o: color, D: shade(color, 0.72), B: shade(color, 0.58) };
    const canvas = document.createElement("canvas");
    canvas.width = 16;
    canvas.height = 16;
    const context = canvas.getContext("2d");
    rows.forEach((row, y) => [...row].forEach((c, x) => {
        if (palette[c]) { context.fillStyle = palette[c]; context.fillRect(x, y, 1, 1); }
    }));
    return canvas.toDataURL("image/png");
}

// [name, sheet, x, y, width, height, glyph drawn in the middle (optional)]
const SPRITES = [
    // Window frame: dark outline, two wood bands, an inner highlight; 16 px (4 u) on every side; the middle is the parchment.
    ["panel", "menu", 0, 256, 60, 60],
    // A recessed card (section inside a window): dark outline, shaded top and right; 8 px (2 u) slice.
    ["inset", "menu", 0, 320, 60, 60],
    // Inventory slot bevel (cream on the left/bottom, brown on the top/right), transparent inside; 4 px (1 u) edge.
    ["slot", "menu", 128, 128, 64, 64],
    // The red frame around the selected hotbar slot (fat; 4 px line); 8 px slice.
    ["select", "cursors", 448, 128, 64, 64],
    // Text field: recessed peach box (the 4 transparent columns on the left of the sheet are cut off); 12 px slice.
    ["field", "textbox", 4, 0, 188, 48],
    // Buttons (thin): orange pill; hot (yellow) pill for the active tab; red pill for destructive actions; 3 px slice.
    ["button", "cursors", 145, 338, 13, 9],
    ["button-hot", "cursors", 159, 338, 13, 9],
    ["button-danger", "cursors", 137, 338, 7, 9],
    // Red X in a wooden square: the close button of a window (thin, 12 x 12).
    ["close", "cursors", 337, 494, 12, 12],
    // Left and right arrows (thin).
    ["arrow-left", "cursors", 352, 495, 12, 11],
    ["arrow-right", "cursors", 365, 495, 12, 11],
    // The gold coin (thin, 9 x 9).
    ["coin", "cursors", 193, 373, 9, 9],
    // Menu tabs (thin, 16 x 16 each): the sheet's own icons, and its blank tab with a glyph on it.
    ["tab-inventory", "cursors", 0, 368, 16, 16],
    ["tab-coop", "cursors", 32, 368, 16, 16],
    ["tab-market", "cursors", 80, 368, 16, 16],
    ["tab-shop", "cursors", 0, 384, 16, 16],
    ["tab-wardrobe", "cursors", 16, 368, 16, 16, "shirt"],
    ["tab-trade", "cursors", 16, 368, 16, 16, "swap"],
    ["tab-profile", "cursors", 16, 368, 16, 16, "person"],
    ["tab-visitors", "cursors", 16, 368, 16, 16, "people"],
    ["tab-friends", "cursors", 16, 368, 16, 16, "heart"],
    ["tab-mail", "cursors", 16, 368, 16, 16, "envelope"],
    ["tab-settings", "cursors", 16, 368, 16, 16, "gear"],
    // The faint picture in an empty equipment slot (glyph only, no sheet).
    ["glyph-hat", null, 0, 0, 11, 4, "hat"],
    ["glyph-shirt", null, 0, 0, 11, 8, "shirt"],
    ["glyph-pants", null, 0, 0, 9, 9, "pants"],
];

// Mouse cursors: [name, sheet, x, y, width, height, hotspot x, hotspot y]; scaled by --u when published.
const CURSORS = [
    ["cursor", "cursors", 0, 0, 8, 10, 0, 0],             // the brown arrow
    ["cursor-pointer", "cursors", 32, 0, 10, 10, 2, 1],   // the hand, over anything clickable
];

const root = document.documentElement;
const base = new URL("./", import.meta.url);

// --- 1. the pixel ---------------------------------------------------------------------------------------------------------

const TILES_ACROSS = 26, TILE = 16, MIN_UNIT = 2, MAX_UNIT = 6, MENU_UNITS_TALL = 204;

export function unitFor(width, height) {
    const world = Math.min(MAX_UNIT, Math.max(MIN_UNIT, Math.round(width / (TILES_ACROSS * TILE))));
    const fits = Math.max(MIN_UNIT, Math.floor(height / MENU_UNITS_TALL));
    return Math.min(world, fits);
}

function applyUnit() {
    const u = unitFor(window.innerWidth, window.innerHeight);
    root.style.setProperty("--u", `${u}px`);
    root.style.setProperty("--tp", `${Math.max(1, Math.floor((u * 2) / 3))}px`);
    return u;
}

function unit() {
    const value = parseFloat(getComputedStyle(root).getPropertyValue("--u"));
    return Number.isFinite(value) && value >= 1 ? Math.round(value) : 3;
}

// --- 2. the sprites -------------------------------------------------------------------------------------------------------

function loadImage(url) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error(`theme sheet ${url}`));
        image.src = url;
    });
}

function cut(image, x, y, w, h, scale, glyph) {
    const canvas = document.createElement("canvas");
    canvas.width = w * scale;
    canvas.height = h * scale;
    const context = canvas.getContext("2d");
    context.imageSmoothingEnabled = false;
    if (image) { context.drawImage(image, x, y, w, h, 0, 0, w * scale, h * scale); }
    if (glyph) {
        const rows = GLYPHS[glyph];
        const gx = Math.floor((w - rows[0].length) / 2), gy = Math.floor((h - rows.length) / 2);
        context.fillStyle = GLYPH_INK;
        rows.forEach((row, r) => [...row].forEach((c, i) => { if (c === "#") { context.fillRect((gx + i) * scale, (gy + r) * scale, scale, scale); } }));
    }
    return canvas.toDataURL("image/png");
}

async function publish() {
    const sheets = {};
    await Promise.all(Object.entries(SHEETS).map(async ([key, path]) => {
        sheets[key] = await loadImage(new URL(path, base).href);
    }));

    for (const [name, sheet, x, y, w, h, glyph] of SPRITES) {
        root.style.setProperty(`--nosum-img-${name}`, `url("${cut(sheet ? sheets[sheet] : null, x, y, w, h, 1, glyph)}")`);
        root.style.setProperty(`--nosum-w-${name}`, String(w));
        root.style.setProperty(`--nosum-h-${name}`, String(h));
    }

    for (const [name, [color, rows]] of Object.entries(ITEM_ART)) {
        root.style.setProperty(`--nosum-img-${name}`, `url("${drawItem(color, rows)}")`);
    }

    const paintCursors = () => {
        const scale = unit();
        const values = {};
        for (const [name, sheet, x, y, w, h, hx, hy] of CURSORS) {
            values[name] = `url("${cut(sheets[sheet], x, y, w, h, scale)}") ${hx * scale} ${hy * scale}`;
        }
        root.style.setProperty("--nosum-cursor", `${values["cursor"]}, auto`);
        root.style.setProperty("--nosum-cursor-pointer", `${values["cursor-pointer"]}, pointer`);
    };
    paintCursors();

    // The unit changes with the window size: repaint the cursors when it does.
    let painted = unit();
    window.addEventListener("resize", () => {
        const now = unit();
        if (now !== painted) {
            painted = now;
            paintCursors();
        }
    });
    root.dataset.nosumTheme = "ready";
}

// --- 3. the tooltip -------------------------------------------------------------------------------------------------------

function installTooltip() {
    const tip = document.createElement("div");
    tip.id = "nosum-tip";
    tip.setAttribute("role", "tooltip");
    document.body.appendChild(tip);
    let target = null;

    const hide = () => { target = null; tip.classList.remove("shown"); };
    const place = (element) => {
        const r = element.getBoundingClientRect();
        const gap = unit();
        const w = tip.offsetWidth, h = tip.offsetHeight;
        let x = r.left + r.width / 2 - w / 2;
        let y = r.top - h - gap;
        if (y < gap) { y = r.bottom + gap; }
        x = Math.max(gap, Math.min(x, window.innerWidth - w - gap));
        tip.style.left = `${Math.round(x)}px`;
        tip.style.top = `${Math.round(y)}px`;
    };

    document.addEventListener("mouseover", (e) => {
        const element = e.target instanceof Element ? e.target.closest("[data-tip]") : null;
        if (!element) { hide(); return; }
        const text = element.getAttribute("data-tip") || "";
        if (!text) { hide(); return; }
        if (element === target && tip.classList.contains("shown")) { return; }
        target = element;
        const [name, ...rest] = text.split("\n");
        tip.replaceChildren();
        const head = document.createElement("span");
        head.className = "tip-name";
        head.textContent = name;
        tip.appendChild(head);
        if (rest.length) {
            const body = document.createElement("span");
            body.className = "tip-text";
            body.textContent = rest.join("\n");
            tip.appendChild(body);
        }
        tip.classList.add("shown");
        place(element);
    });
    document.addEventListener("mousedown", hide);
    document.addEventListener("mouseout", (e) => { if (!e.relatedTarget) { hide(); } });
    // The element under the mouse can be removed (the menu closes): never leave the tip behind.
    new MutationObserver(() => { if (target && !target.isConnected) { hide(); } }).observe(document.body, { childList: true, subtree: true });
    window.addEventListener("blur", hide);
}

applyUnit();
window.addEventListener("resize", applyUnit);
if (document.body) { installTooltip(); } else { window.addEventListener("DOMContentLoaded", installTooltip); }

// Without the sheets the controls keep their plain CSS borders (the theme file defines them as the fallback).
export const ready = publish().catch(error => console.warn("Nosum theme sprites unavailable:", error));
