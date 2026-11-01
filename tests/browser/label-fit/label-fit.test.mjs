// Copyright (c) 2026 PPN Systems. All rights reserved.
//
// Unit tests of the world-label placement rules (engine/Nosum.Rendering.Web/wwwroot/label-fit.js, docs/adr/ADR-095). The module is pure, so
// it runs in plain Node with the built-in test runner (no package to install):
//   node --test tests/browser/label-fit/

import { test } from "node:test";
import assert from "node:assert/strict";
import { fitLabel, hudRect, stackLabel, placeLabel, overlapsPlaced, boxHits, LABEL_EDGE, LABEL_GAP, STACK_GAP, BOTTOM_BAND } from "../../../engine/Nosum.Rendering.Web/wwwroot/label-fit.js";

const W = 1280;
const H = 720;

// Rectangles as the renderer reads them: flat (left, top, right, bottom) per HUD region.
function rects(...list) {
    const a = new Int32Array(list.length * 4);
    list.forEach(([l, t, r, b], k) => {
        a[k * 4] = l; a[k * 4 + 1] = t; a[k * 4 + 2] = r; a[k * 4 + 3] = b;
    });
    return a;
}

function place(hud, x, y, w, h, canvasW = W, canvasH = H) {
    const out = new Int32Array(2);
    fitLabel(out, hud, hud.length / 4, canvasW, canvasH, x, y, w, h);
    return { x: out[0], y: out[1] };
}

// A HUD rectangle as the renderer stores it (through hudRect, so the bottom band rule applies).
function hudRectOf([l, t, r, b], canvasHeight) {
    const out = new Int32Array(4);
    hudRect(out, 0, l, t, r, b, canvasHeight);
    return [...out];
}

// Whether two boxes overlap (touching is not overlap).
const overlaps = (a, b) => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;

// The hotbar of a 1280 x 720 view: 252..918 across, 600..690 down; the bottom band takes it down to the edge (hudRect).
const HOTBAR = [252, 600, 918, 720];

test("fitLabel_FreeSpot_KeepsThePlace", () => {
    const hud = rects(HOTBAR);
    assert.deepEqual(place(hud, 100, 300, 60, 12), { x: 100, y: 300 });
});

test("fitLabel_BottomEdgeUnderHotbar_MovesBesideIt", () => {
    // A trap label on the bottom row, in the gutter under the hotbar: it moves to the left of the hotbar, clear of the bar.
    const hud = rects(HOTBAR);
    const p = place(hud, 500, 700, 60, 12);
    assert.equal(p.x, HOTBAR[0] - 60 - LABEL_GAP);
    assert.equal(p.y, 700);
});

test("hudRect_BottomBand_IsTakenDownToTheEdge", () => {
    const out = new Int32Array(4);
    hudRect(out, 0, 252, 600, 918, 690, 720);
    assert.deepEqual([...out], [252, 600, 918, 720]);
});

test("hudRect_HigherRegion_KeepsItsBottom", () => {
    // The top-right menu column ends well above the edge: its bottom is kept.
    const out = new Int32Array(4);
    hudRect(out, 0, 1200, 220, 1260, 620, 720);
    assert.deepEqual([...out], [1200, 220, 1260, 620]);
    assert.ok(BOTTOM_BAND > 0);
});

test("fitLabel_LeftBlocked_MovesBelowTheBand", () => {
    // A band across the whole bottom row blocks the left and the spot beside it: the label goes below the band, on screen.
    const band = [0, 600, 1280, 690];
    const p = place(rects(band), 300, 650, 200, 12);
    assert.equal(p.x, 300);
    assert.equal(p.y, 690 + LABEL_GAP);
});

test("fitLabel_TouchingAHudEdge_IsMovedClearOfIt", () => {
    // The gap rule: a label that only touches a HUD rectangle is moved, so text never sits on a frame edge.
    const hud = rects(HOTBAR);
    const p = place(hud, 240, 600 - 12, 40, 12);
    assert.equal(boxHits(hud, 1, p.x, p.y, 40, 12, LABEL_GAP), -1);
});

test("fitLabel_TouchStick_IsAvoided", () => {
    // A phone: the stick stands bottom-left (40 u square, 8 u from the edges). A label that would sit on it moves off it.
    const stick = [8, 844 - 8 - 40, 48, 844 - 8];
    const p = place(rects(hudRectOf(stick, 844)), 20, 790, 60, 12, 390, 844);
    assert.equal(boxHits(rects(hudRectOf(stick, 844)), 1, p.x, p.y, 60, 12, LABEL_GAP), -1);
});

test("fitLabel_OffTheRightEdge_IsPulledOnScreen", () => {
    const p = place(rects(), 1275, 100, 60, 12);
    assert.equal(p.x, W - 60 - LABEL_EDGE);
});

test("fitLabel_OffTheTopEdge_IsPulledOnScreen", () => {
    const p = place(rects(), 100, -40, 60, 12);
    assert.equal(p.y, LABEL_EDGE);
});

test("fitLabel_CrowdedBottomRow_GoesAboveTheBand", () => {
    // A label that hits the bar and has no clear spot beside, below or above the rectangle it hit goes above the bottom band (the rectangles
    // that reach the edge), not under the bar. Blockers: the chat corner on the left, a corner on the right, a strip above the bar.
    const hud = rects(
        hudRectOf([250, 536, 1030, 708], 720), // the bottom centre (the bar), taken down to the edge
        hudRectOf([0, 500, 250, 708], 720),    // bottom left
        hudRectOf([1030, 500, 1280, 708], 720), // bottom right
        [940, 500, 1030, 534],                 // a strip above the bar's gauges
    );
    const p = place(hud, 960, 700, 30, 12);
    assert.equal(boxHits(hud, 4, p.x, p.y, 30, 12, 0), -1);
    assert.equal(p.y, 500 - 12 - LABEL_GAP);
});

test("fitLabel_CrowdedCorner_StaysBesideTheHud", () => {
    // Every spot next to the rectangle is taken: the label stays at the left of the rectangle, still on screen.
    const hud = rects([100, 100, 200, 200], [0, 0, 1280, 720]);
    const p = place(hud, 150, 150, 50, 12);
    assert.equal(p.x, 100 - 50 - LABEL_GAP);
});

test("boxHits_Margin_GrowsTheBox", () => {
    const hud = rects([100, 100, 200, 200]);
    assert.equal(boxHits(hud, 1, 202, 150, 10, 10, 0), -1);
    assert.equal(boxHits(hud, 1, 202, 150, 10, 10, 2), -1);
    assert.equal(boxHits(hud, 1, 201, 150, 10, 10, 2), 0);
});

test("stackLabel_FreeSpot_KeepsThePlace", () => {
    const placed = new Int32Array([0, 0, 50, 12]);
    const out = new Int32Array(2);
    stackLabel(out, placed, 4, 100, 100, 50, 12);
    assert.deepEqual([out[0], out[1]], [100, 100]);
});

test("stackLabel_Overlapping_LiftsAboveWithAGap", () => {
    const placed = new Int32Array([100, 100, 50, 12]);
    const out = new Int32Array(2);
    stackLabel(out, placed, 4, 100, 104, 50, 12);
    assert.equal(out[1], 100 - 12 - STACK_GAP);
});

test("stackLabel_NoRoomAbove_GoesBelowInstead", () => {
    // A label at the top edge: lifting it above the placed one would leave the canvas, and clamping it back would overlap. It goes below.
    const placed = new Int32Array([100, 4, 60, 18]);
    const out = new Int32Array(2);
    stackLabel(out, placed, 4, 100, 20, 60, 18);
    assert.equal(out[1], 4 + 18 + STACK_GAP);
    assert.equal(overlaps({ x: out[0], y: out[1], w: 60, h: 18 }, { x: 100, y: 4, w: 60, h: 18 }), false);
});

test("stackLabel_TouchingLabel_IsStackedNotAbutted", () => {
    // "Backwoods" sits just above the name tag, touching it: the later one is lifted so the two keep a gap.
    const placed = new Int32Array([300, 26, 60, 12]);
    const out = new Int32Array(2);
    stackLabel(out, placed, 4, 300, 38, 60, 12);
    const lifted = { x: out[0], y: out[1], w: 60, h: 12 };
    assert.equal(overlaps(lifted, { x: 300, y: 26, w: 60, h: 12 }), false);
    assert.ok(out[1] + 12 <= 26 - STACK_GAP);
});

test("stackLabel_ChainOfLabels_EachClearsTheOthers", () => {
    const placed = new Int32Array([100, 100, 50, 12, 100, 88, 50, 12]);
    const out = new Int32Array(2);
    stackLabel(out, placed, 8, 100, 100, 50, 12);
    const box = { x: out[0], y: out[1], w: 50, h: 12 };
    assert.equal(overlaps(box, { x: 100, y: 100, w: 50, h: 12 }), false);
    assert.equal(overlaps(box, { x: 100, y: 88, w: 50, h: 12 }), false);
});

// ADR-095 D6: a label moved beside a HUD rectangle must not land on a label already drawn (the "stray letter" fault, round 4).
function placeOf(hud, placedList, x, y, w, h) {
    const placed = new Int32Array(placedList.length * 4);
    placedList.forEach(([px, py, pw, ph], k) => { placed[k * 4] = px; placed[k * 4 + 1] = py; placed[k * 4 + 2] = pw; placed[k * 4 + 3] = ph; });
    const out = new Int32Array(2);
    placeLabel(out, hud, hud.length / 4, placed, placed.length, W, H, x, y, w, h);
    return { x: out[0], y: out[1], w, h, placed };
}

test("placeLabel_HudFitOntoPlacedLabel_StaysClearOfIt", () => {
    // The label at (560, 200) hits the HUD; fitting it to the left lands it on the label at (470, 200), which is already drawn.
    const hud = rects([560, 180, 600, 230]);
    const r = placeOf(hud, [[470, 200, 80, 14]], 560, 200, 80, 14);
    assert.equal(overlapsPlaced(r.placed, r.placed.length, r.x, r.y, r.w, r.h), false);
    assert.equal(boxHits(hud, 1, r.x, r.y, r.w, r.h, 0), -1);
});

test("placeLabel_FreeSpot_KeepsTheFitResult", () => {
    const hud = rects([0, 0, 40, 40]);
    const r = placeOf(hud, [], 300, 300, 80, 14);
    assert.deepEqual([r.x, r.y], [300, 300]);
});

test("placeLabel_NoClearSpotOnCanvas_KeepsTheFitResult", () => {
    // A crowded corner: the stacked spot would leave the canvas, so the fit result stands (no label is pushed off screen).
    const hud = rects([0, 0, 40, 40]);
    const placed = [[1200, 700, 60, 14]];
    const r = placeOf(hud, placed, 1200, 700, 60, 14);
    assert.ok(r.x >= 0 && r.y >= 0 && r.x + r.w <= W && r.y + r.h <= H, `on canvas: ${r.x},${r.y}`);
});
