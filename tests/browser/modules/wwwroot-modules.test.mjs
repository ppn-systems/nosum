// Copyright (c) 2026 PPN Systems. All rights reserved.
//
// Load guard for the browser scripts (docs/adr/ADR-104). The render backend's ES modules (engine/Nosum.Rendering.Web/wwwroot/*.js) are imported
// here as real modules, with minimal browser stubs, so a link or parse error fails a unit run instead of a blank page: a missing export
// (renderer.js imported LABEL_EDGE from label-fit.js without it) and a duplicate declaration (a second fitLabel) both stop the import.
// The scripts of the web host (wasm/Nosum.Wasm/wwwroot/*.js) are classic scripts, syntax-checked with vm.Script, except the ones
// with `export` (external-sign-in.js), which Blazor imports as modules and are imported here the same way.
// Runs in plain Node with the built-in test runner (no package to install):
//   node --test tests/browser/modules/*.mjs
// Whether the modules run in a browser is the job of tests/browser/smoke/boot-smoke.cjs (the live client, Playwright).

import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { Script } from "node:vm";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "..", "..", "..");
const moduleDir = join(root, "engine", "Nosum.Rendering.Web", "wwwroot");
const classicDir = join(root, "wasm", "Nosum.Wasm", "wwwroot");

// Minimal browser stubs. Module top level only registers listeners and creates state; nothing is drawn here.
// Every stub is an EventTarget, so add/removeEventListener work; anything else a module touches at import time would throw and fail the test.
class Stub extends EventTarget {
    constructor() {
        super();
        return new Proxy(this, {
            get: (target, key) => (key in target ? Reflect.get(target, key) : () => new Stub()),
        });
    }
}
const element = () => new Stub();
const context2d = () => new Proxy({}, { get: () => () => undefined, set: () => true });

// window is an EventTarget (the modules call window.addEventListener); the globals above hang off it as well.
globalThis.window ??= Object.assign(new EventTarget(), { innerWidth: 1280, innerHeight: 720, devicePixelRatio: 1 });
globalThis.document ??= Object.assign(new EventTarget(), {
    createElement: element,
    querySelector: () => null,
    querySelectorAll: () => [],
    getElementById: () => null,
    body: element(),
    documentElement: element(),
});
globalThis.navigator ??= { language: "vi-VN", languages: ["vi-VN", "en"], userAgent: "node-test" };
globalThis.performance ??= { now: () => 0 };
globalThis.requestAnimationFrame ??= () => 0;
globalThis.cancelAnimationFrame ??= () => undefined;
globalThis.localStorage ??= { getItem: () => null, setItem: () => undefined, removeItem: () => undefined };
globalThis.AudioContext ??= class { constructor() { return element(); } };
globalThis.OfflineAudioContext ??= class { constructor() { return element(); } };
globalThis.OffscreenCanvas ??= class { constructor() { return Object.assign(element(), { getContext: context2d }); } };
globalThis.Image ??= class { constructor() { return element(); } };
globalThis.ImageBitmap ??= class {};

const esModules = readdirSync(moduleDir).filter(f => f.endsWith(".js")).sort();
const hostScripts = readdirSync(classicDir).filter(f => f.endsWith(".js")).sort();
const isModule = source => /^export\s/m.test(source);

test("wwwroot_EngineModules_AreFound", () => {
    // A guard that finds nothing passes for the wrong reason.
    assert.ok(esModules.includes("renderer.js"), "renderer.js is in the render backend's wwwroot");
    assert.ok(esModules.includes("label-fit.js"));
    assert.ok(esModules.includes("audio.js"));
});

for (const file of esModules) {
    test(`importModule_${file}_LoadsAsAnEsModule`, async () => {
        // A link error (a name not exported) or a parse error (a duplicate declaration) rejects here.
        const url = pathToFileURL(join(moduleDir, file)).href;
        const mod = await import(url);
        assert.ok(Object.keys(mod).length > 0 || file === "label-fit.js", `${file} exports something`);
    });
}

test("importModule_renderer_ExportsTheEntryPointsDotNetCalls", async () => {
    // Nosum.Rendering.Web imports these by name through [JSImport]; a renamed or dropped export fails at the first frame, not here.
    const mod = await import(pathToFileURL(join(moduleDir, "renderer.js")).href);
    for (const name of ["start", "submitFrame", "loadTexture", "registerLabel", "setKeyBindings", "loadAudio", "setAudioLevels", "setUiClickSound"]) {
        assert.equal(typeof mod[name], "function", `renderer.js exports ${name}`);
    }
});

test("syntaxCheck_HostScripts_Parse", () => {
    // Classic scripts run in the page's global scope; a parse error here is a blank boot screen.
    assert.ok(hostScripts.length > 0, "the web host has scripts to check");
    for (const file of hostScripts) {
        const source = readFileSync(join(classicDir, file), "utf8");
        if (!isModule(source)) {
            assert.doesNotThrow(() => new Script(source, { filename: file }), `${file} parses`);
        }
    }
});

for (const file of hostScripts.filter(f => isModule(readFileSync(join(classicDir, f), "utf8")))) {
    test(`importModule_host_${file}_LoadsAsAnEsModule`, async () => {
        await import(pathToFileURL(join(classicDir, file)).href);
    });
}

// A name used but never declared or imported fails only when its code runs (the first frame in which a quest arrow is drawn, for
// LABEL_EDGE), so the import test cannot see it. This check reads the module's source: every ALL_CAPS constant it uses must be declared,
// imported or be a platform global. It is a text check, not a parser: comments and string literals are removed first.
const PLATFORM_CAPS = new Set(["URL", "JSON", "NaN", "PI", "DOM", "RGBA"]);
function stripLiterals(source) {
    return source
        .replace(/\/\*[\s\S]*?\*\//g, " ")
        .replace(/\/\/[^\n]*/g, " ")
        .replace(/`(?:\\[\s\S]|[^`\\])*`/g, "``")
        .replace(/"(?:\\.|[^"\\\n])*"/g, '""')
        .replace(/'(?:\\.|[^'\\\n])*'/g, "''");
}
function undeclaredConstants(source) {
    const code = stripLiterals(source);
    const used = new Set(code.match(/\b[A-Z][A-Z0-9_]{2,}\b/g) ?? []);
    const declared = new Set();
    for (const m of code.matchAll(/\b(?:const|let|var|function|class)\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
    for (const m of code.matchAll(/\b(?:const|let|var)\s*\{([^}]*)\}/g)) m[1].split(",").forEach(s => declared.add(s.split(":").pop().trim()));
    for (const m of code.matchAll(/\bimport\s*\{([^}]*)\}\s*from/g)) m[1].split(",").forEach(s => declared.add(s.split(" as ").pop().trim()));
    for (const m of code.matchAll(/\b(?:const|let|var)\s+([A-Z][A-Z0-9_]+)\s*=/g)) declared.add(m[1]);
    for (const m of code.matchAll(/,\s*([A-Z][A-Z0-9_]{2,})\s*=/g)) declared.add(m[1]);
    return [...used].filter(name => !declared.has(name) && !PLATFORM_CAPS.has(name)).sort();
}

for (const file of esModules) {
    test(`staticCheck_${file}_UsesOnlyDeclaredConstants`, () => {
        // The name that the first failure used (LABEL_EDGE in renderer.js) would be listed here if its import were dropped.
        const source = readFileSync(join(moduleDir, file), "utf8");
        assert.deepEqual(undeclaredConstants(source), []);
    });
}

test("staticCheck_UndeclaredConstant_IsFlagged", () => {
    // The check itself: a constant used without a declaration is reported; one that is imported is not.
    assert.deepEqual(undeclaredConstants("function f() { return LABEL_EDGE + 1; }"), ["LABEL_EDGE"]);
    assert.deepEqual(undeclaredConstants('import { LABEL_EDGE } from "./x.js";\nfunction f() { return LABEL_EDGE; }'), []);
    assert.deepEqual(undeclaredConstants("const TILE = 16; // SIZE\nconst s = 'MISSING';"), []);
});
