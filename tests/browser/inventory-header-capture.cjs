// Inventory header capture (docs/qa/phone-inventory). Needs the server and the client on localhost:5080 (see the README).
// usage: node tests/browser/inventory-header-capture.cjs <outDir> <prefix>
// Outputs <outDir>/<prefix>-<locale>-<viewport>.png plus a layout measurement JSON.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const [outDir, prefix] = process.argv.slice(2);
const VIEWPORTS = { '1280x720': { width: 1280, height: 720 }, '390x844': { width: 390, height: 844 } };
const LOCALES = { vi: 'vi-VN', en: 'en-US' };
const BASE = 'http://localhost:5080/';

const MEASURE = () => {
  const w = document.querySelector('.trade-window');
  if (!w) return null;
  const r = (el) => { const b = el.getBoundingClientRect(); return [Math.round(b.left), Math.round(b.top), Math.round(b.right), Math.round(b.bottom)]; };
  const sort = [...document.querySelectorAll('.inv-tools button')][0];
  const lock = document.querySelector('.inv-lock');
  const side = document.querySelector('.inv-side');
  const body = document.querySelector('.inv-body');
  const scrolls = [...w.querySelectorAll('*')].filter((e) => e.clientHeight > 0 && e.scrollHeight > e.clientHeight + 2 && /(auto|scroll)/.test(getComputedStyle(e).overflowY)).map((e) => (e.className.baseVal ?? e.className).toString().slice(0, 40) + ` ${e.scrollHeight}/${e.clientHeight}`);
  const boxes = { window: r(w), sort: sort && r(sort), lock: lock && r(lock), side: side && r(side), body: body && r(body) };
  const overlap = (a, b) => a && b && !(a[2] <= b[0] || a[0] >= b[2] || a[3] <= b[1] || a[1] >= b[3]);
  const overlaps = [];
  if (overlap(boxes.side, boxes.sort)) overlaps.push('side/sort');
  if (overlap(boxes.side, boxes.lock)) overlaps.push('side/lock');
  if (overlap(boxes.sort, boxes.lock)) overlaps.push('sort/lock');
  return { boxes, overlaps, scrolls };
};

async function main() {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const results = [];
  for (const loc of Object.keys(LOCALES)) {
    for (const vp of Object.keys(VIEWPORTS)) {
      const phone = vp === '390x844';
      const context = await browser.newContext({ viewport: VIEWPORTS[vp], locale: LOCALES[loc], hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', (e) => errors.push(e.message));
      await page.goto(BASE, { waitUntil: 'load' });
      await page.waitForSelector('button', { timeout: 90000 });
      await page.waitForTimeout(1000);
      await page.getByRole('button', { name: /khách|guest/i }).click();
      await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
      await page.waitForTimeout(3000);
      if (loc === 'en') {
        const settings = page.locator('.game-hud .hud-top-right .game-icon-button[aria-label*="Cài đặt"], .game-hud .hud-top-right .game-icon-button[aria-label*="Settings"]').first();
        await settings.click({ force: true, timeout: 5000 });
        await page.waitForTimeout(600);
        await page.getByRole('button', { name: 'English' }).first().click({ timeout: 5000 });
        await page.waitForTimeout(600);
        await page.keyboard.press('Escape');
        await page.waitForTimeout(400);
      }
      if (phone) {
        await page.locator('.game-hud .hud-top-right .hud-menu-button .game-icon-button').first().click({ force: true, timeout: 5000 });
        await page.waitForTimeout(400);
        await page.locator('.game-menu-tile').filter({ hasText: loc === 'vi' ? /Túi đồ/ : /Inventory/ }).first().click({ force: true, timeout: 5000 });
      } else {
        await page.locator('.game-hud .hud-top-right .game-icon-button').filter({ hasText: /.?/ }).nth(0); // ensure HUD up
        await page.keyboard.press('i');
      }
      await page.waitForSelector('.inv-tools', { timeout: 10000 });
      await page.waitForTimeout(700);
      const file = path.join(outDir, `${prefix}-${loc}-${vp}.png`);
      await page.screenshot({ path: file });
      const m = await page.evaluate(MEASURE);
      results.push({ loc, vp, file, ...m, errors: errors.slice(0, 3) });
      await context.close();
    }
  }
  await browser.close();
  const json = path.join(outDir, `${prefix}-measure.json`);
  fs.writeFileSync(json, JSON.stringify(results, null, 2));
  for (const r of results) console.log(r.loc, r.vp, 'overlaps:', JSON.stringify(r.overlaps), 'scrolls:', JSON.stringify(r.scrolls), 'errors:', r.errors.length);
  console.log('written', json);
}
main().catch((e) => { console.error(e); process.exit(1); });
