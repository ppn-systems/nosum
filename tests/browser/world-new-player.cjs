// World QA pass 10: a new player's first day as a guest (docs/qa/world-pass10, the new-player section of its README).
// A fresh guest on the Development tester build (window.nosumQa, ADR-079): no spring reset, no grants. The run captures the farm start, the
// hotbar, the inventory and the world map, then walks to the farm door and presses the bus stop sign, which is the first exit a player has.
// Every step saves a frame; a step's ok means its check passed, the frames are graded by eye.
// usage: node tests/browser/world-new-player.cjs [locales=vi,en] [viewports=1280x720,1920x1080,390x844]
// Env: NOSUM_QA_URL (default http://localhost:5080/?qa=1), NOSUM_QA_OUT (default docs/qa/world-pass10/new-player).
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const OUT_ROOT = path.resolve(REPO, process.env.NOSUM_QA_OUT || 'docs/qa/world-pass10/new-player');
const VIEWPORTS = { '1280x720': [1280, 720], '1920x1080': [1920, 1080], '390x844': [390, 844] };
const LANG = { vi: { tag: 'vi-VN', code: 1, guest: /khách|guest/i }, en: { tag: 'en-US', code: 2, guest: /khách|guest/i } };
const DOOR_FRONT = [64, 16]; // the farm door, where the bus stop sign sits (bus-stop-label-capture.cjs)
const wait = ms => new Promise(r => setTimeout(r, ms));

const locales = (process.argv[2] || 'vi,en').split(',');
const viewports = (process.argv[3] || '1280x720,1920x1080,390x844').split(',');
const summary = [];

async function run(lk, size) {
  const [w, h] = VIEWPORTS[size];
  const touch = w < 1024;
  const dir = path.join(OUT_ROOT, lk, size);
  fs.mkdirSync(dir, { recursive: true });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: w, height: h }, locale: LANG[lk].tag, hasTouch: touch, isMobile: touch });
  await context.addInitScript(code => { try { localStorage.setItem('nosum.settings.v1', `lang=${code}\n`); } catch (e) {} }, LANG[lk].code);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message.slice(0, 200)));
  const steps = [];
  let n = 0;
  const shot = async name => {
    n += 1;
    const f = `${String(n).padStart(2, '0')}-${name}.png`;
    await page.screenshot({ path: path.join(dir, f) });
    return f;
  };
  const step = async (name, fn) => {
    const rec = { name, ok: false };
    try { const v = await fn(); rec.ok = v !== false; if (v !== undefined && v !== true) rec.value = v; }
    catch (e) { rec.error = String(e.message || e).split('\n')[0].slice(0, 200); }
    rec.file = await shot(name);
    steps.push(rec);
    console.log(`${lk} ${size} ${rec.ok ? 'ok  ' : 'FAIL'} ${name} ${rec.value !== undefined ? JSON.stringify(rec.value) : ''} ${rec.error || ''}`);
    return rec;
  };
  // Closes the window on top: its X control, else Escape. The frame after it shows the world with no modal.
  const closeTopWindow = async () => {
    const x = page.locator('.game-panel-header .game-close:visible').first();
    if (await x.count().catch(() => 0)) await x.click({ force: true, timeout: 2000 }).catch(() => {});
    else await page.keyboard.press('Escape');
    await wait(600);
  };
  const toastText = () => page.evaluate(() => [...document.querySelectorAll('.toast, .game-toast, [role=status], [role=alert]')].map(e => e.innerText.trim()).filter(Boolean).join(' | ').slice(0, 200));

  // 1. Sign in as a guest: the first screen of a new player.
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await wait(1000);
  await page.getByRole('button', { name: LANG[lk].guest }).first().click({ timeout: 120000 });
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 120000 });
  await wait(3000);
  await step('01-farm-start', () => true);

  // 2. The hotbar: each tool slot, in order.
  for (const key of ['1', '2', '3']) {
    await page.keyboard.press(key);
    await wait(600);
    await step(`02-hotbar-${key}`, () => true);
  }

  // 3. The inventory and the world map from the menu column.
  // The menu window hides the menu column at 1280 (pass-9 item 19), so the window is closed before the map is clicked in the column.
  await step('03-menu-open', async () => {
    const menu = page.getByRole('button', { name: /menu|thực đơn/i }).first();
    await menu.click({ timeout: 6000 });
    await wait(700);
    return true;
  });
  await closeTopWindow();
  await step('04-world-map', async () => {
    const map = page.getByRole('button', { name: /map|bản đồ/i }).first();
    await map.click({ timeout: 6000 });
    await wait(700);
    return true;
  });
  // The map is a window too: it must close before the farm door is walked to, or the presses land on the modal.
  await closeTopWindow();
  await step('04b-map-closed', () => page.locator('.game-close:visible').count().then(n => n === 0));

  // 4. The first exit: teleport to the farm door (the same spot the bus stop label uses) and press the bus stop sign.
  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), DOOR_FRONT);
  await wait(2500);
  await step('05-at-farm-door', () => true);
  await step('06-bus-stop-press', async () => {
    await page.keyboard.press('e');
    await wait(900);
    const t = await toastText();
    return { toast: t };
  });
  summary.push({ lk, size, steps, errors });
  await browser.close();
}

(async () => {
  for (const lk of locales) for (const size of viewports) {
    try { await run(lk, size); } catch (e) { console.log(`FAIL run ${lk} ${size}: ${e.message.split('\n')[0]}`); }
  }
  fs.mkdirSync(OUT_ROOT, { recursive: true });
  fs.writeFileSync(path.join(OUT_ROOT, 'summary.json'), JSON.stringify(summary, null, 2));
  const failed = summary.flatMap(r => r.steps.filter(s => !s.ok).map(s => `${r.lk} ${r.size} ${s.name}`));
  console.log(`new-player: ${summary.length} runs, ${failed.length} failed steps${failed.length ? ': ' + failed.join('; ') : ''}`);
  console.log(`page errors: ${summary.reduce((a, r) => a + r.errors.length, 0)}`);
})();
