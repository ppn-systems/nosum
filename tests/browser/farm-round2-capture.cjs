// Farming round two captures (docs/adr/ADR-089): one short farming scenario at 1280x720 in Vietnamese (lang=1) and English (lang=2).
// Needs a Development server (DOTNET_ENVIRONMENT=Development) and a client built with -p:NosumDevTools=true, opened at ?qa=1.
// Writes docs/qa/farming-round2/before/<lang>-<nn>-<name>.png (the "before" set is the master build; pass another dir for "after").
// Usage: node tests/browser/farm-round2-capture.cjs [base=http://localhost:5080/] [outDir=docs/qa/farming-round2/before]
// The scenario, on the farm: the row is tiles (48,18), (49,18), (50,18). The farmer stands on the tile north of a row tile (teleported
// there, since a tile is reached only from next to it) and works the tile below it. Steps:
//   1. the copper hoe held over the row (the reticle), then R tills the line (a wide swing) from (47,18);
//   2. parsnip seeds in (48,18) and (49,18), watered on four game days (the wet-soil refusal on a second watering of a day);
//      (50,18) is sown on day 2, so it is not ripe at the end;
//   3. E on (50,18) with the scythe in hand (there is no empty slot to hold nothing) shows the unripe refusal;
//   4. (48,18) is ripe: E with seeds in hand harvests it, and the message line shows what the game says.
// Each step prints the message line it saw; the PNG is the full page.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/farming-round2/before'));
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// Screen offset of a tile from the farmer at 1280x720, measured on the reticle of the tile in front (45 px tiles): the farmer's tile
// centre is (657, 387). The camera follows the farmer, so the offset is the same at any position.
const CENTER = { x: 657, y: 387 };
const TILE_PX = 45;
const at = (dx, dy) => ({ x: CENTER.x + dx * TILE_PX, y: CENTER.y + dy * TILE_PX });
const BELOW = at(0, 1);
const ROW_X = { A: 48, B: 49, C: 50 };
const ROW_Y = 18;
const NORTH_Y = ROW_Y - 1;

// The hotbar names the slot; a name that is missing fails with the names it has, so a wrong label is easy to fix.
const SLOT = {
  hoe: /cuốc đồng|copper hoe/i,
  seeds: /hạt củ cải vàng|parsnip seeds/i,
  can: /bình tưới|watering can/i,
  scythe: /liềm|scythe/i,
};

// The message line (the same selectors the other captures read), joined into one string for the log.
const messages = page => page.evaluate(() => {
  const out = [];
  for (const e of document.querySelectorAll('.game-notice, .game-message, .hud-bottom-center, .hotbar .message')) {
    const r = e.getBoundingClientRect();
    if (r.width && r.height && getComputedStyle(e).visibility !== 'hidden') out.push((e.innerText || '').trim().replace(/\s+/g, ' '));
  }
  return out.filter(Boolean).join(' | ');
});

async function run(browser, lang) {
  const locale = lang === 'en' ? 'en-US' : 'vi-VN';
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale });
  // The language is the player's saved setting (lang=1 Vietnamese, lang=2 English), not the browser's: set it before the game reads it.
  await context.addInitScript(value => { try { localStorage.setItem('nosum.settings.v1', value); } catch (e) {} }, `lang=${lang === 'en' ? 2 : 1}\n`);
  const page = await context.newPage();
  page.on('pageerror', e => console.log(`[${lang}] pageerror:`, e.message.slice(0, 160)));

  // A tile action is a press, a short hold and a release, as the farm captures do: a bare click can be read as a tap that does nothing.
  const tap = async p => {
    await page.mouse.move(p.x, p.y);
    await page.mouse.down();
    await wait(200);
    await page.mouse.up();
    await wait(700);
  };
  // Stand on the tile north of a row tile and work the tile below it.
  const workRowTile = async tileX => {
    await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [tileX, NORTH_Y]);
    await wait(1200);
    await tap(BELOW);
  };
  // The count the hotbar shows on the slot whose name matches (seeds, the can's charges): logged so a run can be checked.
  const count = re => page.$$eval('.hotbar-slots > *', (els, src) => {
    const r = new RegExp(src, 'i');
    const hit = els.find(e => r.test(e.getAttribute('aria-label') || ''));
    const nums = hit ? (hit.innerText || '').match(/\d+/g) : null;
    return nums ? Number(nums[nums.length - 1]) : -1;
  }, re.source);
  const selectSlot = async re => {
    const names = await page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
    const i = names.findIndex(n => re.test(n));
    if (i < 0) throw new Error(`no hotbar slot matches ${re}; slots: ${names.join(' | ')}`);
    await page.keyboard.press(String(i + 1));
    await wait(250);
  };
  const seasonText = () => page.locator('.calendar-line .calendar-season').first().innerText().catch(() => '');

  let n = 0;
  const shot = async name => {
    n += 1;
    const file = `${lang}-${String(n).padStart(2, '0')}-${name}.png`;
    await page.screenshot({ path: path.join(outDir, file) });
    console.log(`[${lang}] ${file} | line: ${await messages(page)}`);
  };

  await page.goto(`${base.replace(/\/?$/, '/')}?qa=1`, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('canvas', { timeout: 90000 });
  await page.waitForSelector('.calendar-line', { timeout: 60000 });
  await page.waitForFunction(() => globalThis.nosumQa !== undefined, null, { timeout: 30000 });
  await wait(2500);

  // Parsnip grows in Spring, Fall and Winter, not Summer (CropCatalog). The server clock only moves forward, so pass seasons from
  // whatever the last run left until the calendar leaves Summer (springReset did not reach Spring on the QA server in this run).
  await page.evaluate(() => window.nosumQa.springReset());
  await wait(2000);
  for (let i = 0; i < 3 && /Hè|Summer/i.test(await seasonText()); i++) {
    await page.evaluate(() => window.nosumQa.season(1));
    await wait(3000);
  }
  console.log(`[${lang}] season on the farm: ${await seasonText()}`);
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
  await wait(2500);
  await page.evaluate(() => { window.nosumQa.grant(100, 10, 0); window.nosumQa.grant(10, 1, 0); });
  await wait(1200);
  await shot('farm-start');

  // 1. The copper hoe over the row: the reticle, then the wide swing. One step right puts the farmer at (47,18), in front of the row.
  await page.keyboard.down('KeyD');
  await wait(120);
  await page.keyboard.up('KeyD');
  await wait(400);
  await selectSlot(SLOT.hoe);
  await page.mouse.move(...Object.values(at(1, 0)));
  await wait(400);
  await shot('wide-hoe-reticle');
  await page.keyboard.press('KeyR');
  await wait(900);
  await shot('wide-hoe-tilled-row');

  // 2. Sow (48,18) and (49,18), then water them on four game days; the second watering of a day is the wet-soil refusal.
  await selectSlot(SLOT.seeds);
  const seedsBefore = await count(SLOT.seeds);
  await workRowTile(ROW_X.A);
  await workRowTile(ROW_X.B);
  console.log(`[${lang}] seeds in hand ${seedsBefore} -> ${await count(SLOT.seeds)} after sowing A and B`);
  await shot('sown-row');
  // C is sown on day 2 (after two game days), so at the end it has two watered days and A and B have four.
  let seedsC = -1;
  for (let day = 1; day <= 4; day++) {
    await selectSlot(SLOT.can);
    await workRowTile(ROW_X.A);
    await workRowTile(ROW_X.B);
    if (day === 1) {
      await workRowTile(ROW_X.A);
      await shot('wet-soil-refusal');
    }
    if (day === 2) {
      await selectSlot(SLOT.seeds);
      seedsC = await count(SLOT.seeds);
      await workRowTile(ROW_X.C);
      console.log(`[${lang}] seeds in hand ${seedsC} -> ${await count(SLOT.seeds)} after sowing C`);
      await selectSlot(SLOT.can);
      await workRowTile(ROW_X.C);
    }
    await page.evaluate(() => window.nosumQa.advanceClock(1));
    await wait(1500);
  }
  await shot('a-b-after-four-days');

  // 3. (50,18) is one day short of ripe: the scythe in hand (a harvesting tool, so E on a crop asks whether it is ripe), then E.
  await selectSlot(SLOT.scythe);
  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [ROW_X.C, NORTH_Y]);
  await wait(1200);
  await tap(BELOW);
  await page.keyboard.press('KeyE');
  await wait(600);
  await shot('unripe-refusal');

  // 4. Seeds in hand, E on the ripe crop (48,18): the harvest, and the line the game shows with another item in hand.
  await selectSlot(SLOT.seeds);
  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [ROW_X.A, NORTH_Y]);
  await wait(1200);
  await tap(BELOW);
  await page.keyboard.press('KeyE');
  await wait(700);
  await shot('harvest-with-item-in-hand');

  await context.close();
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium', args: ['--no-sandbox'] });
  try {
    for (const lang of ['vi', 'en']) await run(browser, lang);
  } finally {
    await browser.close();
  }
  console.log('done');
})().catch(e => { console.error('FAILED', e.message.slice(0, 400)); process.exit(1); });
