// Nosum UI theme, script side (docs/adr/ADR-017). Three jobs:
//
// 1. The UI pixel. --u is one UI pixel in CSS pixels and equals one WORLD pixel: the engine zooms the open map by
//    round(canvas width / (26 tiles * 16 px)) (MapScene.ScaleFor, never less than 2), so the same formula gives --u. It is also
//    capped so the fixed-size menu window always fits the height. --tp is one pixel of the UI font (2/3 of --u, whole numbers).
// 2. The sprites. Cuts the pieces the UI needs out of the early UI sheets (docs/adr/ADR-007: assets/, linked into this
//    package's wwwroot/ui/) and publishes each as a CSS custom property on :root for border-image / background-image:
//      --nosum-img-<name>   url("data:image/png;...")  the piece at its native size
//      --nosum-w-<name>, --nosum-h-<name>              size in sheet pixels (unitless; multiply by --u)
//      --nosum-cursor, --nosum-cursor-pointer          a `cursor` value, scaled by --u
//    "fat" sheets (menu.png, text_box.png) use 4 x 4 pixel blocks: a piece is drawn at (sheet px) / 4 * --u CSS pixels;
//    "thin" art (cursors.png, glyphs below) is 1:1: (sheet px) * --u.
// 3. The tooltip. One element, created here, shows the data-tip text of whatever is under the mouse (first line = name).

const SHEETS = {
    menu: "ui/menu.png",     // fat: window frames, inset and slot bevels
    cursors: "ui/cursors.png",    // thin: tabs, buttons, icons, mouse cursors (a few fat pieces too, noted below)
    textbox: "ui/text_box.png",    // fat: the text field
    emotes: "ui/emotes.png",    // thin: the twelve emote icons in one row, index = emote code
    pet: "sprites/pets/cat.png",    // thin: the cat sheet; its first frame is the pet tab
};

// Own 1-bit pictures for the faint hints in the empty equipment slots (Nosum art, not early content). '#' is ink, anything else clear.
const GLYPHS = {
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
};
const GLYPH_INK = "#5a3a2e";

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
    // Window frame: the Pixel Adventure cream panel, a wood frame with steel corner brackets (thin, 16 x 16, 5 px slice, drawn 5 u thick). The middle is
    // the parchment, painted flat by the CSS.
    ["panel", "menu", 0, 256, 16, 16],
    // The small frame of the HUD widgets, the tooltip and the message line: a plain wood frame (thin, 16 x 16, 4 px slice).
    ["plaque", "menu", 32, 256, 16, 16],
    // Inventory slot: a 1 px inset bevel (thin, 3 x 3, 1 px slice), transparent inside.
    ["slot", "menu", 128, 128, 3, 3],
    // The frame around the selected hotbar slot (fat): a gold line in a dark outline, two steps of glow outside; 20 px (5 u) slice.
    ["select", "cursors", 448, 128, 80, 80],
    // Text field: sunk light box (the 4 transparent columns on the left of the sheet are cut off); 12 px slice.
    ["field", "textbox", 4, 0, 188, 48],
    // Block buttons (thin, 9 x 11, slice 3 on the top, right and left and 5 on the bottom, which holds the shadow): wood (primary), stone (neutral), gold
    // (the active tab), red (destructive), from the Pixel UI pack; each has a pressed frame with the shadow gone.
    ["button", "cursors", 0, 432, 9, 11],
    ["button-plain", "cursors", 10, 432, 9, 11],
    ["button-hot", "cursors", 20, 432, 9, 11],
    ["button-danger", "cursors", 30, 432, 9, 11],
    ["button-down", "cursors", 0, 444, 9, 11],
    ["button-plain-down", "cursors", 10, 444, 9, 11],
    ["button-hot-down", "cursors", 20, 444, 9, 11],
    ["button-danger-down", "cursors", 30, 444, 9, 11],
    // The title bar of a window (thin, 8 x 18, repeats along the bar).
    ["title", "cursors", 0, 400, 8, 18],
    // The frame of the energy gauge: gold end caps, a dark track (thin, 10 x 26, 3 px slice).
    ["gauge", "cursors", 16, 400, 10, 26],
    // The close button of a window: an orange cross on a white square (Pixel UI pack, thin, 14 x 14).
    ["close", "cursors", 337, 494, 14, 14],
    // Left and right arrows (thin).
    ["arrow-left", "cursors", 352, 495, 12, 11],
    ["arrow-right", "cursors", 365, 495, 12, 11],
    // The gold coin (thin, 16 x 16).
    ["coin", "cursors", 240, 384, 16, 16],
    ["coin-pouch", "cursors", 224, 384, 16, 16],
    // Season icons (thin, 16 x 16 each).
    ["season-spring", "cursors", 16, 384, 16, 16],
    ["season-summer", "cursors", 32, 384, 16, 16],
    ["season-fall", "cursors", 48, 384, 16, 16],
    ["season-winter", "cursors", 64, 384, 16, 16],
    // Weather icons (thin, 16 x 16 each).
    ["weather-sun", "cursors", 80, 384, 16, 16],
    ["weather-rain", "cursors", 96, 384, 16, 16],
    ["weather-storm", "cursors", 112, 384, 16, 16],
    ["weather-snow", "cursors", 128, 384, 16, 16],
    // Quest and UI icons (thin, 16 x 16 each).
    ["quest-scroll", "cursors", 144, 384, 16, 16],
    ["sound-on", "cursors", 160, 384, 16, 16],
    ["sound-off", "cursors", 176, 384, 16, 16],
    ["music", "cursors", 192, 384, 16, 16],
    ["dialog-arrow", "cursors", 208, 384, 16, 16],
    // Menu tabs (thin, 16 x 16 each): a bevelled plate with an outlined pictogram.
    ["tab-inventory", "cursors", 0, 368, 16, 16],
    ["tab-wardrobe", "cursors", 16, 368, 16, 16],
    ["tab-coop", "cursors", 32, 368, 16, 16],
    ["tab-trade", "cursors", 48, 368, 16, 16],
    ["tab-visitors", "cursors", 64, 368, 16, 16],
    ["tab-market", "cursors", 80, 368, 16, 16],
    ["tab-friends", "cursors", 96, 368, 16, 16],
    ["tab-mail", "cursors", 112, 368, 16, 16],
    ["tab-skills", "cursors", 128, 368, 16, 16],
    ["tab-profile", "cursors", 144, 368, 16, 16],
    ["tab-settings", "cursors", 160, 368, 16, 16],
    ["tab-goals", "cursors", 176, 368, 16, 16],
    ["tab-shop", "cursors", 0, 384, 16, 16],
    ["tab-menu", "cursors", 256, 384, 16, 16],
    ["tab-pet", "cursors", 272, 384, 16, 16],
    // The emote wheel's icons (thin, 16 x 16 each, the emote code is the index in the sheet).
    ["emote-0", "emotes", 0, 0, 16, 16],
    ["emote-1", "emotes", 16, 0, 16, 16],
    ["emote-2", "emotes", 32, 0, 16, 16],
    ["emote-3", "emotes", 48, 0, 16, 16],
    ["emote-4", "emotes", 64, 0, 16, 16],
    ["emote-5", "emotes", 80, 0, 16, 16],
    ["emote-6", "emotes", 96, 0, 16, 16],
    ["emote-7", "emotes", 112, 0, 16, 16],
    ["emote-8", "emotes", 128, 0, 16, 16],
    ["emote-9", "emotes", 144, 0, 16, 16],
    ["emote-10", "emotes", 160, 0, 16, 16],
    ["emote-11", "emotes", 176, 0, 16, 16],
    // The faint picture in an empty equipment slot (glyph only, no sheet).
    ["glyph-hat", null, 0, 0, 11, 4, "hat"],
    ["glyph-shirt", null, 0, 0, 11, 8, "shirt"],
    ["glyph-pants", null, 0, 0, 9, 9, "pants"],
];

// Mouse cursors: [name, sheet, x, y, width, height, hotspot x, hotspot y]; scaled by --u when published.
const CURSORS = [
    ["cursor", "cursors", 0, 0, 12, 16, 0, 0],            // the arrow (Pixel UI pack)
    ["cursor-pointer", "cursors", 32, 0, 12, 14, 5, 0],   // the hand, over anything clickable
];

const root = document.documentElement;
const base = new URL("./", import.meta.url);

// --- 1. the pixel ---------------------------------------------------------------------------------------------------------

const TILES_ACROSS = 26, TILE = 16, MIN_UNIT = 2, MAX_UNIT = 6, MENU_UNITS_TALL = 204;

// The zoom the player picked in the settings (2..6), or 0 for automatic (docs/adr/ADR-033). It lives on <html> because this file can be loaded twice
// (once by the page's own tag, once under its fingerprinted name by the import map) and both copies must agree on the unit.
function chosenUnit() {
    const value = parseInt(document.documentElement.dataset.nosumScale ?? "0", 10);
    return value >= MIN_UNIT && value <= MAX_UNIT ? value : 0;
}

export function unitFor(width, height) {
    const world = chosenUnit() || Math.min(MAX_UNIT, Math.max(MIN_UNIT, Math.round(width / (TILES_ACROSS * TILE))));
    const fits = Math.max(MIN_UNIT, Math.floor(height / MENU_UNITS_TALL));
    return Math.min(world, fits);
}

// Sets the zoom the player picked (0 = automatic) and redraws the theme at the new pixel size.
export function setUnitChoice(unit) {
    document.documentElement.dataset.nosumScale = unit >= MIN_UNIT && unit <= MAX_UNIT ? String(unit) : "0";
    applyUnit();
    window.dispatchEvent(new Event("resize"));
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
