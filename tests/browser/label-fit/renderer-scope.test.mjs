// Copyright (c) 2026 PPN Systems. All rights reserved.
//
// Static check of renderer.js (docs/adr/ADR-095 D6, docs/design/critique-world-ambience.md section 9). renderer.js cannot be imported in plain Node
// (audio.js reads `window` at load), so this reads its source. Every LABEL_* constant it uses must be imported from label-fit.js or declared in
// renderer.js, and every helper it calls must be declared there. A missing name is a ReferenceError on the frame that reaches it: the quest
// arrow (fitArrow) read LABEL_EDGE without an import, which the frame path would hit on the first off-screen waypoint.
//   node --test tests/browser/label-fit/

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, "../../../engine/Nosum.Rendering.Web/wwwroot/renderer.js"), "utf8");
const labelFit = readFileSync(join(here, "../../../engine/Nosum.Rendering.Web/wwwroot/label-fit.js"), "utf8");

// Names bound at module level: imports (the braces of an import line) and function or const/let declarations.
function boundNames(src) {
    const names = new Set();
    for (const m of src.matchAll(/^import\s*\{([^}]*)\}/gm)) {
        m[1].split(",").map((s) => s.trim().split(/\s+as\s+/).pop()).filter(Boolean).forEach((n) => names.add(n));
    }
    for (const m of src.matchAll(/^(?:export\s+)?(?:function|const|let|var|class)\s+(\w+)/gm)) {
        names.add(m[1]);
    }
    return names;
}

test("renderer_LabelConstants_AreImportedOrDeclared", () => {
    const bound = boundNames(source);
    const used = new Set([...source.matchAll(/\bLABEL_[A-Z_]+\b/g)].map((m) => m[0]));
    assert.ok(used.size > 0, "renderer.js uses label constants");
    const missing = [...used].filter((n) => !bound.has(n));
    assert.deepEqual(missing, [], `label constants used but not imported or declared in renderer.js`);
});

test("renderer_ImportsFromLabelFit_AreExported", () => {
    // Every name renderer.js imports from label-fit.js must be an export of it (an import of a name that is not exported fails at load).
    for (const m of source.matchAll(/^import\s*\{([^}]*)\}\s*from\s*"\.\/label-fit\.js"/gm)) {
        for (const n of m[1].split(",").map((s) => s.trim()).filter(Boolean)) {
            assert.match(labelFit, new RegExp(`export (const|function) ${n}\\b`), `${n} is exported by label-fit.js`);
        }
    }
});

test("renderer_HelpersCalled_AreDeclared", () => {
    // The local helpers of the frame path. Each must be declared in renderer.js (a removed helper still called is a ReferenceError).
    const bound = boundNames(source);
    for (const helper of ["clampTo", "fitLabel", "stackLabel", "hudHit", "fitArrow", "drawArrowGlyph", "placeLabel", "hudRect"]) {
        const called = new RegExp(`\\b${helper}\\(`).test(source);
        if (called) {
            assert.ok(bound.has(helper), `${helper}() is called in renderer.js but not declared or imported`);
        }
    }
});

test("renderer_OneLabelPlacement_IsLabelFitsPlaceLabel", () => {
    // ADR-095 D6: renderer.js places each label with placeLabel, and keeps no second copy of the fit rules.
    assert.match(source, /placeLabel\(labelPlace,/);
    assert.doesNotMatch(source, /function fitLabel\(/);
    assert.doesNotMatch(source, /function stackLabel\(/);
});
