// Copyright (c) 2026 PPN Systems. All rights reserved.
//
// Where a world label goes on the canvas (docs/design/ui-standard.md sections 2 and 4; docs/adr/ADR-095). Pure: no DOM, no imports, no
// allocation. renderer.js calls these once per label per frame; the HUD rectangles come from the DOM (refreshHud, every 250 ms). Rectangles are
// flat Int32Arrays of (left, top, right, bottom) in canvas pixels, `count` of them used. Results go to `out[0]` (x) and `out[1]` (y).

export const LABEL_EDGE = 4;   // the smallest margin between a label and the canvas edge
export const LABEL_GAP = 2;    // the space kept between a label and a HUD rectangle
export const STACK_GAP = 2;    // the space kept between two labels that are stacked
export const MAX_STACK_STEPS = 32; // the most moves one label makes while it is stacked
export const BOTTOM_BAND = 40; // a HUD rectangle this close to the bottom edge is the bottom band: it is taken down to the edge (see hudRect)

function clampTo(v, lo, hi) {
    return v < lo ? lo : v > hi ? hi : v;
}

// Stores one HUD rectangle at out[o..o+3]. A rectangle whose bottom is within BOTTOM_BAND of the canvas bottom (the hotbar, the bottom corners and
// the touch buttons) is taken down to the edge: the strip under the hotbar is its gutter, and a label there would sit under the bar, not beside it.
export function hudRect(out, o, left, top, right, bottom, canvasHeight) {
    out[o] = left;
    out[o + 1] = top;
    out[o + 2] = right;
    out[o + 3] = canvasHeight - bottom <= BOTTOM_BAND ? canvasHeight : bottom;
}

// The number of rectangles of `hud` that a world label must keep off. The local player's sprite box, when the frame has one, is stored right
// after the HUD rectangles (at index hudCount), so it counts as one more rectangle. The player's own name plate is anchored at the player's feet
// and is drawn over the sprite by design, so it does not count that box (docs/adr/ADR-099). Pure: the renderer passes its own state.
export function labelAvoidCount(hudCount, hasPlayer, labelX, labelY, feetX, feetY) {
    return hasPlayer && !(labelX === feetX && labelY === feetY) ? hudCount + 1 : hudCount;
}

// The index of the first rectangle that the box (x, y, w, h), grown by `margin` on every side, overlaps; -1 when none does.
export function boxHits(rects, count, x, y, w, h, margin) {
    for (let k = 0; k < count; k++) {
        const o = k * 4;
        if (x < rects[o + 2] + margin && x + w > rects[o] - margin && y < rects[o + 3] + margin && y + h > rects[o + 1] - margin) {
            return k;
        }
    }
    return -1;
}

// Moves a label box inside the canvas and off the HUD, keeping LABEL_GAP from every HUD rectangle: beside the rectangle it hit, then below it,
// then above it. When every spot is taken (a crowded corner) it stays beside that rectangle, which is still on screen.
export function fitLabel(out, hud, hudCount, canvasWidth, canvasHeight, x, y, w, h) {
    const maxX = canvasWidth - w - LABEL_EDGE;
    const maxY = canvasHeight - h - LABEL_EDGE;
    x = clampTo(x, LABEL_EDGE, maxX);
    y = clampTo(y, LABEL_EDGE, maxY);
    const k = boxHits(hud, hudCount, x, y, w, h, LABEL_GAP);
    if (k < 0) {
        out[0] = x;
        out[1] = y;
        return;
    }

    const o = k * 4;
    const left = clampTo(hud[o] - w - LABEL_GAP, LABEL_EDGE, maxX);
    if (boxHits(hud, hudCount, left, y, w, h, LABEL_GAP) < 0) {
        out[0] = left;
        out[1] = y;
        return;
    }

    const below = clampTo(hud[o + 3] + LABEL_GAP, LABEL_EDGE, maxY);
    if (boxHits(hud, hudCount, x, below, w, h, LABEL_GAP) < 0) {
        out[0] = x;
        out[1] = below;
        return;
    }

    const above = clampTo(hud[o + 1] - h - LABEL_GAP, LABEL_EDGE, maxY);
    if (boxHits(hud, hudCount, x, above, w, h, LABEL_GAP) < 0) {
        out[0] = x;
        out[1] = above;
        return;
    }

    // Still nothing clear beside the rectangle it hit (a crowded bottom row): above the bottom band, the strip of rectangles that reach the edge.
    let bandTop = canvasHeight;
    for (let j = 0; j < hudCount; j++) {
        const q = j * 4;
        if (hud[q + 3] >= canvasHeight && hud[q + 1] < bandTop) {
            bandTop = hud[q + 1];
        }
    }

    const overBand = clampTo(bandTop - h - LABEL_GAP, LABEL_EDGE, maxY);
    if (bandTop < canvasHeight && boxHits(hud, hudCount, x, overBand, w, h, LABEL_GAP) < 0) {
        out[0] = x;
        out[1] = overBand;
        return;
    }

    out[0] = left;
    out[1] = y;
}

// Moves a label box off each placed label it would touch (within STACK_GAP), so names read as a column, not a smear: above the placed label when
// there is room on screen, otherwise below it (a label at the top edge cannot lift past the canvas, and clamping back would overlap it). `placed`
// holds the placed boxes as (x, y, w, h), `placedCount` numbers used. A step is capped so a crowded set ends. Result in out[0], out[1].
export function stackLabel(out, placed, placedCount, x, y, w, h) {
    let steps = 0;
    for (let k = 0; k < placedCount; k += 4) {
        if (x < placed[k] + placed[k + 2] + STACK_GAP && x + w > placed[k] - STACK_GAP
            && y < placed[k + 1] + placed[k + 3] + STACK_GAP && y + h > placed[k + 1] - STACK_GAP) {
            const above = placed[k + 1] - h - STACK_GAP;
            y = above >= LABEL_EDGE ? above : placed[k + 1] + placed[k + 3] + STACK_GAP;
            k = -4;
            if (++steps >= MAX_STACK_STEPS) {
                break;
            }
        }
    }
    out[0] = x;
    out[1] = y;
}

// Whether the box (x, y, w, h), grown by STACK_GAP, overlaps one of the placed boxes (the labels drawn this frame, as (x, y, w, h)).
export function overlapsPlaced(placed, placedCount, x, y, w, h) {
    for (let k = 0; k < placedCount; k += 4) {
        if (x < placed[k] + placed[k + 2] + STACK_GAP && x + w > placed[k] - STACK_GAP
            && y < placed[k + 1] + placed[k + 3] + STACK_GAP && y + h > placed[k + 1] - STACK_GAP) {
            return true;
        }
    }
    return false;
}

// The one placement a world label gets (docs/adr/ADR-095 D6). The HUD fit moves a label beside a HUD rectangle, and that spot can be on a label
// already drawn: the two then print one over the other, and a letter of the one underneath shows at the edge ("SFLAME AURA"). So the label is
// stacked off the placed labels, fitted off the HUD, and, if the fit landed on a placed label, stacked again from there. The stacked spot wins
// when it is clear of the placed labels and on the canvas; otherwise the fit stands (a label on the HUD edge is better than two on each other).
// Result in out[0] (x) and out[1] (y).
export function placeLabel(out, hud, hudCount, placed, placedCount, canvasWidth, canvasHeight, x, y, w, h) {
    stackLabel(out, placed, placedCount, x, y, w, h);
    fitLabel(out, hud, hudCount, canvasWidth, canvasHeight, out[0], out[1], w, h);
    if (!overlapsPlaced(placed, placedCount, out[0], out[1], w, h)) {
        return;
    }

    const fitX = out[0];
    const fitY = out[1];
    stackLabel(out, placed, placedCount, fitX, fitY, w, h);
    const onCanvas = out[0] >= 0 && out[1] >= 0 && out[0] + w <= canvasWidth && out[1] + h <= canvasHeight;
    if (!onCanvas || overlapsPlaced(placed, placedCount, out[0], out[1], w, h)) {
        out[0] = fitX;
        out[1] = fitY;
    }
}
