// Nosum UI theme, sprite side (docs/adr/ADR-016). Cuts the pieces the UI needs out of the placeholder UI sheets
// (docs/adr/ADR-007: assets/, linked into this package's wwwroot/ui/) and publishes each as a CSS custom property
// on :root, so the controls' CSS can use them as border-image / background-image:
//
//   --nosum-img-<name>   url("data:image/png;...")  the cut-out piece, at its native size
//   --nosum-w-<name>     width in sheet pixels      (unitless; multiply by --u for CSS pixels)
//   --nosum-h-<name>     height in sheet pixels
//   --nosum-cursor, --nosum-cursor-pointer          a `cursor` value, scaled by --u
//
// This table is the one place that lists the rectangles. Two kinds of art are mixed:
//   "fat"  sheets (MenuTiles.png, textBox.png) are drawn with 4 x 4 pixel blocks: 1 art pixel = 4 sheet pixels,
//          so a piece is drawn at  (sheet px) / 4 * --u  CSS pixels (a 16 px slice becomes a 4 u border);
//   "thin" art (Cursors.png) is 1:1: a piece is drawn at  (sheet px) * --u  CSS pixels.
// The controls' CSS does that arithmetic next to each border-image; nothing here knows about it.

const SHEETS = {
    menu: "ui/MenuTiles.png",     // fat: wooden window frames and slot bevels
    cursors: "ui/Cursors.png",    // thin: buttons, icons, mouse cursors (a few fat pieces too, noted below)
    textbox: "ui/textBox.png",    // fat: the text field
};

// [name, sheet, x, y, width, height, what it is for]
const SPRITES = [
    // Window frame: dark outline, two wood bands, an inner highlight; 16 px (4 u) on every side; the middle is the parchment.
    ["panel", "menu", 0, 256, 60, 60],
    // Inventory slot bevel (cream on the left/bottom, brown on the top/right), transparent inside; 4 px (1 u) edge.
    ["slot", "menu", 128, 128, 64, 64],
    // The red frame around the selected hotbar slot (fat; 4 px line, rounded corners); 8 px slice.
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
];

// Mouse cursors: [name, sheet, x, y, width, height, hotspot x, hotspot y]; scaled by --u when published.
const CURSORS = [
    ["cursor", "cursors", 0, 0, 8, 10, 0, 0],             // the brown arrow
    ["cursor-pointer", "cursors", 32, 0, 10, 10, 2, 1],   // the hand, over anything clickable
];

const root = document.documentElement;
const base = new URL("./", import.meta.url);

function loadImage(url) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error(`theme sheet ${url}`));
        image.src = url;
    });
}

function cut(image, x, y, w, h, scale) {
    const canvas = document.createElement("canvas");
    canvas.width = w * scale;
    canvas.height = h * scale;
    const context = canvas.getContext("2d");
    context.imageSmoothingEnabled = false;
    context.drawImage(image, x, y, w, h, 0, 0, w * scale, h * scale);
    return canvas.toDataURL("image/png");
}

function unit() {
    const value = parseFloat(getComputedStyle(root).getPropertyValue("--u"));
    return Number.isFinite(value) && value >= 1 ? Math.round(value) : 2;
}

async function publish() {
    const sheets = {};
    await Promise.all(Object.entries(SHEETS).map(async ([key, path]) => {
        sheets[key] = await loadImage(new URL(path, base).href);
    }));

    for (const [name, sheet, x, y, w, h] of SPRITES) {
        root.style.setProperty(`--nosum-img-${name}`, `url("${cut(sheets[sheet], x, y, w, h, 1)}")`);
        root.style.setProperty(`--nosum-w-${name}`, String(w));
        root.style.setProperty(`--nosum-h-${name}`, String(h));
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

    // The unit changes with the window size (media queries in nosum-theme.css): repaint the cursors when it does.
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

// Without the sheets the controls keep their plain CSS borders (the theme file defines them as the fallback).
export const ready = publish().catch(error => console.warn("Nosum theme sprites unavailable:", error));
