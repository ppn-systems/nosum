// Browser smoke test of the published client (docs/adr/ADR-102): boots it against the Development server, waits for the first game frame and
// fails on any page error. Headless Chromium from Playwright (preinstalled in the cloud container; no `playwright install`).
//
//   node tests/browser/smoke/boot-smoke.cjs <baseUrl> [--timeout-ms 120000] [--shot out.png]
//
// Needs: the published client served statically (e.g. `node bench/wasm-perf/serve.cjs artifacts/smoke-pub/wwwroot 5080`) and the Development
// host on :57300 (`DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host -c Release`, PostgreSQL with the nosum_dev database).
// Exit 0 when the client signs in as a guest, draws game frames onto the canvas and raises no page error; exit 1 otherwise.
// Checks: (1) the first requestAnimationFrame callbacks run (counted by an init script), (2) the canvas holds drawn content, not one flat colour,
// (3) no `pageerror` event at any point (a module that fails to load, a ReferenceError in a frame, an unhandled rejection).
// A console error fails the run too, except a plain resource 404 ("Failed to load resource"): a module that does not load (the renderer's
// import) surfaces as a console error when the page's own import fails, and the run must say so rather than time out.

const { chromium } = require('playwright');

const args = process.argv.slice(2);
const base = args.find(a => !a.startsWith('--')) || 'http://localhost:5080/';
const opt = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
const timeoutMs = Number(opt('--timeout-ms') || 120000);
const shot = opt('--shot');
const wait = ms => new Promise(r => setTimeout(r, ms));

// Counts the animation frames the page runs, from the first script of the document.
const initScript = () => {
  window.__smokeFrames = 0;
  const raf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = fn => raf(t => { window.__smokeFrames++; fn(t); });
};

// Distinct colours in a coarse grid over the game canvas (a blank or flat canvas has one).
const colourVariety = () => {
  const c = document.querySelector('canvas');
  if (!c || !c.width || !c.height) return 0;
  const ctx = c.getContext('2d');
  const seen = new Set();
  for (let y = 0; y < 18; y++) {
    for (let x = 0; x < 32; x++) {
      const px = ctx.getImageData(Math.floor((x + 0.5) * c.width / 32), Math.floor((y + 0.5) * c.height / 18), 1, 1).data;
      seen.add(px[0] << 24 | px[1] << 16 | px[2] << 8 | px[3]);
    }
  }
  return seen.size;
};

(async () => {
  const started = Date.now();
  const pageErrors = [];
  const consoleErrors = [];
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN', serviceWorkers: 'block' });
  await context.addInitScript(initScript);
  const page = await context.newPage();
  page.on('pageerror', e => { pageErrors.push(e.message.slice(0, 400)); console.log('pageerror:', e.message.slice(0, 400)); });
  page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) consoleErrors.push(m.text().slice(0, 300)); });

  const result = { base, pageErrors, consoleErrors: 0, frames: 0, colours: 0, firstFrameMs: null, ok: false };
  try {
    await page.goto(base, { waitUntil: 'load' });
    await page.getByRole('button', { name: /khách|guest/i }).first().click({ timeout: timeoutMs });
    // The HUD stamina meter appears once the game is playable (the same readiness mark the other browser checks use).
    await page.waitForSelector('[role=meter][aria-label^="Sức lực"]', { timeout: timeoutMs });
    // The first frame: the loop has run and the canvas holds drawing (a frame that drew nothing is a failed frame).
    await page.waitForFunction(() => window.__smokeFrames >= 3 && document.querySelector('canvas') !== null, null, { timeout: timeoutMs });
    result.firstFrameMs = Date.now() - started;
    await wait(500);
    result.frames = await page.evaluate(() => window.__smokeFrames);
    result.colours = await page.evaluate(colourVariety);
    if (shot) await page.screenshot({ path: shot });
  } catch (e) {
    result.error = String(e.message || e).split('\n')[0].slice(0, 400);
  }
  result.consoleErrors = consoleErrors;
  result.ok = !result.error && pageErrors.length === 0 && consoleErrors.length === 0 && result.frames >= 3 && result.colours >= 2;
  console.log(JSON.stringify(result, null, 2));
  await browser.close();
  process.exitCode = result.ok ? 0 : 1;
})().catch(e => { console.error(e); process.exit(1); });
