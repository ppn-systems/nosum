// Inventory interaction check (docs/qa/rebuild-inventory-crafting). Opens the bag as a guest at 1280x720 and checks, against the real server, that
// a right click splits half a stack into an empty bag slot and that a shift click moves a hotbar stack into the bag. It also reads the hint line
// in each state. Exits non-zero when a check fails.
//
// usage: node tests/browser/inventory-interact-check.cjs [vi|en]
// writes docs/qa/rebuild-inventory-crafting/interact-<lang>.txt

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/';

(async () => {
  const lang = process.argv[2] || 'en';
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: lang === 'en' ? 'en-US' : 'vi-VN', serviceWorkers: 'block' });
  if (lang === 'en') await context.addInitScript(() => { try { window.localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch { /* private window */ } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 60000 });
  await page.waitForTimeout(4000);

  const labels = await page.$$eval('.game-hud .hud-top-right .game-icon-button', (els) => els.map((e) => e.getAttribute('aria-label') || ''));
  await page.locator('.game-hud .hud-top-right .game-icon-button').nth(labels.findIndex((l) => /túi|bag|inventory/i.test(l))).click({ force: true });
  await page.waitForSelector('.inv-grids .item-grid', { timeout: 10000 });
  await page.waitForTimeout(500);

  const grids = page.locator('.inv-grids .item-grid');
  const bagSlots = grids.nth(0).locator('button.slot');
  const hotbarSlots = grids.nth(1).locator('button.slot');
  const results = [];
  const check = (name, ok, detail = '') => results.push(`${ok ? 'PASS' : 'FAIL'} | ${name}${detail ? ' | ' + detail : ''}`);
  const quantityOf = async (slot) => (await slot.locator('.quantity').count()) ? (await slot.locator('.quantity').textContent()).trim() : '';
  const emptyBagSlot = async () => {
    const count = await bagSlots.count();
    for (let i = 0; i < count; i++) {
      if (await bagSlots.nth(i).evaluate((e) => e.classList.contains('empty'))) return i;
    }
    return -1;
  };

  // Right click splits: the parsnip seeds in hotbar slot 3 (the third seed stack the account starts with) become half in an empty bag slot.
  const hintIdle = await page.locator('.inv-hint').textContent();
  check('hint names the clicks while idle', /right|chuột phải|Shift/i.test(hintIdle), hintIdle.trim());
  const seedIndex = await (async () => {
    for (let i = 0; i < 9; i++) {
      const title = await hotbarSlots.nth(i).getAttribute('aria-label');
      if (title && /seed|hạt/i.test(title)) return i;
    }
    return -1;
  })();
  if (seedIndex < 0) {
    check('a seed stack is in the hotbar to split', false, 'none found; check the starter kit');
  } else {
    const before = await quantityOf(hotbarSlots.nth(seedIndex));
    const bagTarget = await emptyBagSlot();
    await hotbarSlots.nth(seedIndex).click({ button: 'right' });
    await page.waitForTimeout(300);
    const pickedHint = await page.locator('.inv-hint').textContent();
    check('right click picks up part of the stack and the hint says so', /Holding|Đang nhấc/.test(pickedHint), pickedHint.trim());
    await bagSlots.nth(bagTarget).click();
    await page.waitForTimeout(1200);
    const after = await quantityOf(hotbarSlots.nth(seedIndex));
    const moved = await quantityOf(bagSlots.nth(bagTarget));
    const half = Math.ceil(Number(before) / 2);
    check('split leaves the rest on the source slot', Number(after) === Number(before) - half, `before ${before}, after ${after}`);
    check('split puts the part in the empty bag slot', Number(moved) === half, `bag slot shows ${moved}`);
  }

  // Shift click: the hoe in hotbar slot 0 goes to the bag and its hotbar slot empties.
  const hoeEmptyBefore = await hotbarSlots.nth(0).evaluate((e) => e.classList.contains('empty'));
  await hotbarSlots.nth(0).click({ modifiers: ['Shift'] });
  await page.waitForTimeout(1200);
  const hoeEmptyAfter = await hotbarSlots.nth(0).evaluate((e) => e.classList.contains('empty'));
  check('shift click moves the hotbar stack into the bag', !hoeEmptyBefore && hoeEmptyAfter, `empty before=${hoeEmptyBefore} after=${hoeEmptyAfter}`);

  check('no page errors', errors.length === 0, errors.slice(0, 2).join(' ; '));
  await context.close();
  await browser.close();
  const out = path.join(REPO, 'docs/qa/rebuild-inventory-crafting', `interact-${lang}.txt`);
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, results.join('\n') + '\n');
  console.log(results.join('\n'));
  if (results.some((r) => r.startsWith('FAIL'))) process.exit(1);
})().catch((e) => { console.error(e); process.exit(1); });
