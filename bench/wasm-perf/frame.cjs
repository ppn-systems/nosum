// Frame benchmark for the WASM client on the farm map: node frame.cjs <baseUrl with ?qa=1> [--seconds N] [--json out]
// Needs a tester build (-p:NosumDevTools=true: window.nosumQa) served statically, and the Development server on :57300.
//  1. guest sign-in, teleport to the farm, grant a hoe and seeds, till and sow a block of tiles so the map carries many plots and crops;
//  2. idle for N seconds, then walk for N seconds: each game frame is timed around its requestAnimationFrame callback
//     (the game loop is the one rAF callback that submits a frame; Blazor's own callbacks are rare and small);
//  3. allocations per frame: GC.GetAllocatedBytesForCurrentThread() before and after each phase, through nosumQa.allocatedBytes(). The frame's
//     own work (GameLoop.Frame) is counted apart, through nosumQa.frameBodyBytes(): that is the number the idle gate reads.
//  4. --max-idle-body N (default 0): exit 1 when the idle phase's frame body allocates more than N bytes a frame (docs/adr/ADR-097 D1).
const fs = require('fs');
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const base = args[0] || 'http://localhost:5081/?qa=1';
const opt = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
const seconds = Number(opt('--seconds') || 8);
const jsonOut = opt('--json');
const STEP_MS = Number(opt('--step') || 260);
const MAX_IDLE_BODY = Number(opt('--max-idle-body') ?? 0);
const wait = ms => new Promise(r => setTimeout(r, ms));

const pct = (arr, p) => { if (!arr.length) return null; const s = [...arr].sort((a, b) => a - b); return s[Math.min(s.length - 1, Math.floor(p * s.length))]; };
const r2 = v => (v == null ? null : Math.round(v * 100) / 100);

// Times every requestAnimationFrame callback that runs while measuring is on.
const initScript = () => {
  const stats = { on: false, cb: [], dt: [], last: 0, draws: 0, fills: 0, flush: true };
  window.__frameStats = stats;
  // Counts canvas calls while measuring (measurement only: the wrapper adds a few nanoseconds per call and is not part of the game).
  const proto = CanvasRenderingContext2D.prototype;
  const drawImage = proto.drawImage;
  proto.drawImage = function (...a) { if (stats.on) stats.draws++; return drawImage.apply(this, a); };
  const fillRect = proto.fillRect;
  proto.fillRect = function (...a) { if (stats.on) stats.fills++; return fillRect.apply(this, a); };
  const raf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = fn => raf(t => {
    if (!stats.on) { fn(t); return; }
    const s = performance.now();
    fn(t);
    // Canvas2D records draw calls and rasterizes them later, outside the callback. A one-pixel readback of the game canvas makes the
    // frame's drawing finish inside the timed callback, so the number is the whole cost of the frame on this thread (both builds alike).
    if (stats.flush) {
      const c = document.querySelector('canvas');
      if (c) c.getContext('2d').getImageData(0, 0, 1, 1);
    }
    stats.cb.push(performance.now() - s);
    if (stats.last) stats.dt.push(t - stats.last);
    stats.last = t;
  });
};

async function phase(page, name, secs, act) {
  await page.evaluate(() => { const s = window.__frameStats; s.on = true; s.cb = []; s.dt = []; s.last = 0; s.draws = 0; s.fills = 0; });
  const before = await page.evaluate(() => window.nosumQa.allocatedBytes());
  const bodyBefore = await page.evaluate(() => [window.nosumQa.frameBodyBytes(), window.nosumQa.frameBodyFrames()]);
  const end = Date.now() + secs * 1000;
  if (act) await act(end);
  else await wait(secs * 1000);
  const after = await page.evaluate(() => window.nosumQa.allocatedBytes());
  const bodyAfter = await page.evaluate(() => [window.nosumQa.frameBodyBytes(), window.nosumQa.frameBodyFrames()]);
  const bodyFrames = bodyAfter[1] - bodyBefore[1];
  const bodyBytesPerFrame = bodyFrames ? Math.round(((bodyAfter[0] - bodyBefore[0]) / bodyFrames) * 10) / 10 : null;
  const s = await page.evaluate(() => { const v = window.__frameStats; v.on = false; return { cb: v.cb, dt: v.dt, draws: v.draws, fills: v.fills }; });
  const frames = s.cb.length;
  return {
    name,
    frames,
    callbackP50Ms: r2(pct(s.cb, 0.5)), callbackP95Ms: r2(pct(s.cb, 0.95)), callbackMaxMs: r2(Math.max(...s.cb)),
    intervalP50Ms: r2(pct(s.dt, 0.5)), intervalP95Ms: r2(pct(s.dt, 0.95)),
    allocBytesPerFrame: frames ? Math.round((after - before) / frames) : null,
    frameBodyBytesPerFrame: bodyBytesPerFrame,
    frameBodyFrames: bodyFrames,
    drawImagePerFrame: frames ? r2(s.draws / frames) : null,
    fillRectPerFrame: frames ? r2(s.fills / frames) : null,
  };
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN', serviceWorkers: 'block' });
  await context.addInitScript(initScript);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => { errors.push(e.message.slice(0, 300)); console.log('pageerror:', e.message.slice(0, 300)); });
  await page.goto(base, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('[role=meter][aria-label^="Sức lực"]', { timeout: 120000 });
  await wait(1500);

  // The farm, with the tools and seeds a block of tiles needs.
  await page.evaluate(() => window.nosumQa.teleport('farm', 64, 16));
  await wait(2500);
  await page.evaluate(() => { window.nosumQa.grant(1, 1, 0); window.nosumQa.grant(100, 60, 0); });
  await wait(2500);
  const hotbar = await page.evaluate(() => [...document.querySelectorAll('.hotbar-items button, .hotbar button, [class*=hotbar] button')].slice(0, 12).map(b => b.getAttribute('aria-label') || b.textContent.trim()));
  console.log('hotbar:', JSON.stringify(hotbar));

  // Till and sow a serpentine block: the action key (E) works the tile in front, so the farmer walks a row and uses the held tool at each step.
  // Drag-to-sow reaches only the tiles beside the farmer, so the walk is what covers the block (docs/adr/ADR-074).
  const walkRows = async (tool, rows, cols) => {
    await page.keyboard.press(tool);
    await wait(200);
    for (let r = 0; r < rows; r++) {
      const dir = r % 2 === 0 ? 'KeyD' : 'KeyA';
      for (let c = 0; c < cols; c++) {
        await page.keyboard.press('KeyE');
        await wait(340);
        await page.keyboard.down(dir);
        await wait(STEP_MS);
        await page.keyboard.up(dir);
        await wait(60);
      }
      await page.keyboard.press('KeyE');
      await wait(340);
      await page.keyboard.down('KeyS');
      await wait(STEP_MS);
      await page.keyboard.up('KeyS');
      await wait(60);
    }
  };
  await walkRows('Digit1', 6, 10);
  console.log('seeds after till:', await page.evaluate(() => document.querySelectorAll('.hotbar-items button, [class*=hotbar] button')[2]?.textContent));
  await wait(800);
  await walkRows('Digit3', 6, 10);
  console.log('seeds after sow:', await page.evaluate(() => document.querySelectorAll('.hotbar-items button, [class*=hotbar] button')[2]?.textContent));
  await wait(3000);

  // Park the farmer on the block so the camera and the walk stay on the plots.
  const results = [];
  results.push(await phase(page, 'idle', seconds));
  results.push(await phase(page, 'walk', seconds, async end => {
    const keys = ['KeyD', 'KeyS', 'KeyA', 'KeyW'];
    let i = 0;
    while (Date.now() < end) {
      const k = keys[Math.floor(i / 2) % keys.length];
      await page.keyboard.down(k);
      await wait(700);
      await page.keyboard.up(k);
      i++;
    }
  }));

  const onScreenCrops = await page.evaluate(() => (document.querySelector('canvas') ? 'canvas present' : 'no canvas'));
  if (opt('--shot')) await page.screenshot({ path: opt('--shot') });
  const idle = results.find(r => r.name === 'idle');
  const gate = { maxIdleBodyBytesPerFrame: MAX_IDLE_BODY, idleBodyBytesPerFrame: idle ? idle.frameBodyBytesPerFrame : null };
  gate.pass = gate.idleBodyBytesPerFrame !== null && gate.idleBodyBytesPerFrame <= MAX_IDLE_BODY;
  if (!gate.pass) process.exitCode = 1;
  const report = { build: base, seconds, errors: errors.length, canvas: onScreenCrops, gate, phases: results };
  console.log(JSON.stringify(report, null, 2));
  if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(report, null, 2));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
