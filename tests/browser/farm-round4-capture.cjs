// Farming round four captures (docs/design/critique-farming.md, round-4 addendum; ADR-105).
// Replays the sprinkler and the shipping bin, which round three did not reach, and reads the scythe TAP on an unripe crop (round-3 finding 1).
// Needs a Development server (DOTNET_ENVIRONMENT=Development, dotnet run --project server/Nosum.Host, PostgreSQL role nosum_dev) and a client
// published with -p:NosumDevTools=true, opened at ?qa=1.
// Usage: node tests/browser/farm-round4-capture.cjs [base=http://localhost:5080/] [outDir] [width=1280] [height=720]
// A 390x844 capture needs a copy of the published client whose wwwroot/appsettings.json has Device.MinimumWidth lowered (a test copy only).
// Env: LANGS=vi,en (default both).
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/farming-round4/after'));
const W = Number(process.argv[4] || 1280);
const H = Number(process.argv[5] || 720);
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// The farmer's tile centre on screen and the tile size, measured on the round-three frames.
const CENTER = W >= 1024 ? { x: Math.round(W / 2) + 17, y: Math.round(H / 2) + 27 } : { x: Math.round(W / 2), y: 428 };
const TILE_PX = W >= 1024 ? 45 : 32;
const at = (dx, dy) => ({ x: CENTER.x + dx * TILE_PX, y: CENTER.y + dy * TILE_PX });
const BELOW = at(0, 1);
const ROW_X = { A: 48, B: 49 };
const ROW_Y = 18;
const NORTH_Y = ROW_Y - 1;
const SPRINKLER_TILE = { x: 47, y: 18 }; // west of A: a sprinkler waters its four orthogonal tiles (SprinklerRules)
const BIN_FRONT = { x: 71, y: 15 };       // the shipping bin is at 71,14 (FarmProps.Placements); stand south of it, facing up

const SLOT = {
  seeds: /parsnip seeds|hạt củ cải vàng/i,
  can: /watering can|bình tưới/i,
  scythe: /scythe|liềm/i,
  sprinkler: /sprinkler|vòi tưới/i,
  parsnip: /^parsnip$|^củ cải vàng$/i,
  radishOrCauliflower: /radish seeds|cauliflower seeds|hạt củ cải đỏ|hạt súp lơ/i,
};
const SAYS = {
  unripe: { vi: /Chưa chín, còn \d+ ngày|Chưa chín, còn 1 ngày/, en: /Not ready, \d+ days? left|Not ready, 1 day left/ },
  alreadyWet: { vi: /Đất đã đủ nước hôm nay/, en: /The soil is already wet today/ },
  sold: { vi: /Đã bán được \d+ xu/, en: /Sold for \d+ coins/ },
};

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
  const context = await browser.newContext({ viewport: { width: W, height: H }, locale, hasTouch: W < 1024, isMobile: W < 1024 });
  await context.addInitScript(value => { try { localStorage.setItem('nosum.settings.v1', value); } catch (e) {} }, `lang=${lang === 'en' ? 2 : 1}\n`);
  const page = await context.newPage();
  page.on('pageerror', e => console.log(`[${lang}] pageerror:`, e.message.slice(0, 160)));

  const tap = async p => {
    await page.mouse.move(p.x, p.y);
    await page.mouse.down();
    await wait(200);
    await page.mouse.up();
    await wait(700);
  };
  const workRowTile = async tileX => {
    await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [tileX, NORTH_Y]);
    await wait(1200);
    await tap(BELOW);
  };
  // Teleport to a tile, then one step: the farmer faces the tile beyond it without a use.
  const stepInto = async (x, y, key, holdMs = 120) => {
    await page.evaluate(([tx, ty]) => window.nosumQa.teleport('farm', tx, ty), [x, y]);
    await wait(1200);
    await page.keyboard.down(key);
    await wait(holdMs);
    await page.keyboard.up(key);
    await wait(500);
  };
  const hotbarNames = () => page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const selectSlot = async re => {
    const names = await hotbarNames();
    const i = names.findIndex(n => re.test(n));
    if (i < 0) throw new Error(`no hotbar slot matches ${re}; slots: ${names.join(' | ')}`);
    await page.keyboard.press(String(i + 1));
    await wait(250);
  };
  // Opens the inventory (I), picks the bag item matching fromRe and swaps it onto the hotbar slot holding swapOut, then closes the panel.
  const moveToHotbar = async (fromRe, swapOut) => {
    await page.keyboard.press('KeyI');
    await wait(700);
    const labels = () => page.$$eval('.inv-grids .slot[aria-label]', els => els.map(e => e.getAttribute('aria-label') || ''));
    const names = await labels();
    const from = names.findIndex(n => fromRe.test(n));
    const to = names.findIndex(n => swapOut.test(n));
    if (from < 0 || to < 0) throw new Error(`moveToHotbar: from=${from} to=${to}; panel slots: ${names.join(' | ')}`);
    const slots = page.locator('.inv-grids .slot[aria-label]');
    await slots.nth(from).click();
    await wait(300);
    await slots.nth(to).click();
    await wait(400);
    await page.keyboard.press('Escape');
    await wait(500);
    console.log(`[${lang}] hotbar after the swap: ${(await hotbarNames()).join(' | ')}`);
  };
  const seasonText = () => page.locator('.calendar-line .calendar-season').first().innerText().catch(() => '');

  let n = 0;
  const shot = async (name, note = '') => {
    n += 1;
    const file = `${lang}-${String(n).padStart(2, '0')}-${name}-${W}x${H}.png`;
    await page.screenshot({ path: path.join(outDir, file) });
    console.log(`[${lang}] ${file} | line: ${await messages(page)}${note}`);
  };
  // Poll the message line for up to `timeoutMs` and shoot the first frame that matches; the timeline is logged so a gap can be read.
  const shotWhen = async (name, re, timeoutMs = 2500) => {
    n += 1;
    const file = `${lang}-${String(n).padStart(2, '0')}-${name}-${W}x${H}.png`;
    const t0 = Date.now();
    const seen = [];
    let hit = false;
    while (Date.now() - t0 < timeoutMs) {
      const text = await messages(page);
      if (seen.length < 12 && seen[seen.length - 1] !== text) seen.push(`${Date.now() - t0}ms:${text || '(empty)'}`);
      if (re.test(text)) { hit = true; break; }
      await wait(60);
    }
    await page.screenshot({ path: path.join(outDir, file) });
    console.log(`[${lang}] ${file} hit=${hit} at ${Date.now() - t0}ms | timeline: ${seen.join(' ~> ')}`);
    return hit;
  };

  await page.goto(`${base.replace(/\/?$/, '/')}?qa=1`, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('canvas', { timeout: 90000 });
  await page.waitForSelector('.calendar-line', { timeout: 60000 });
  await page.waitForFunction(() => globalThis.nosumQa !== undefined, null, { timeout: 30000 });
  await wait(2500);

  await page.evaluate(() => window.nosumQa.springReset());
  await wait(2000);
  for (let i = 0; i < 3 && /Hè|Summer/i.test(await seasonText()); i++) {
    await page.evaluate(() => window.nosumQa.season(1));
    await wait(3000);
  }
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
  await wait(2500);
  // Seeds (100) and a sprinkler (604) land in the free hotbar slot and the bag. Two parsnips (200) go to the bag; one is swapped into the
  // hotbar from the inventory panel, because the bag is not a hotbar slot.
  await page.evaluate(() => { window.nosumQa.grant(100, 10, 0); window.nosumQa.grant(604, 1, 0); window.nosumQa.grant(200, 2, 0); });
  await wait(1200);
  await moveToHotbar(SLOT.parsnip, SLOT.radishOrCauliflower);
  await shot('farm-start');

  // 1. Till A and B with the starting hoe (hotbar 1), sow parsnip on both (taps).
  await page.keyboard.press('1');
  await wait(250);
  await workRowTile(ROW_X.A);
  await workRowTile(ROW_X.B);
  await selectSlot(SLOT.seeds);
  await workRowTile(ROW_X.A);
  await workRowTile(ROW_X.B);
  await shot('sown-row');

  // 2. Day 0: the can waters B. The sprinkler goes on the tile west of A, placed by E with the farmer facing it.
  await selectSlot(SLOT.can);
  await workRowTile(ROW_X.B);
  await selectSlot(SLOT.sprinkler);
  await stepInto(SPRINKLER_TILE.x, SPRINKLER_TILE.y - 2, 'KeyS');
  await page.keyboard.press('KeyE');
  await wait(900);
  await shot('sprinkler-placed');

  // 3. Day 1: the sprinkler has watered A (from the day after it was placed). A can tap on A should say the soil is already wet.
  await page.evaluate(() => window.nosumQa.advanceClock(1));
  await wait(1500);
  await selectSlot(SLOT.can);
  await workRowTile(ROW_X.A);
  await shotWhen('sprinkler-watered-a', SAYS.alreadyWet[lang], 2500);
  await wait(3200);

  // 4. Day 2: A has the sprinkler's watered days only, so it is unripe. The scythe TAP on A must name the days left.
  await page.evaluate(() => window.nosumQa.advanceClock(1));
  await wait(1500);
  await selectSlot(SLOT.scythe);
  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [ROW_X.A, NORTH_Y]);
  await wait(1200);
  await tap(BELOW);
  // One retry: a tap on a phone can land a beat before the swing is over (seen once in the vi phone run); the second tap is the same action.
  if (!(await shotWhen('scythe-tap-unripe', SAYS.unripe[lang], 1500))) {
    await tap(BELOW);
    await shotWhen('scythe-tap-unripe-retry', SAYS.unripe[lang], 2500);
  }
  await wait(3200);

  // 5. Shipping: a parsnip in hand, the farmer south of the bin facing up, E sells the stack.
  await selectSlot(SLOT.parsnip);
  // A 400 ms hold crosses the tile edge; 120 ms only turns the farmer, and E then works the tile south of the bin.
  await stepInto(BIN_FRONT.x, BIN_FRONT.y + 1, 'KeyW', 400);
  await page.keyboard.press('KeyE');
  await shotWhen('bin-sold', SAYS.sold[lang], 3000);
  await wait(3200);
  await shot('bin-after');

  await context.close();
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium', args: ['--no-sandbox'] });
  try {
    const langs = (process.env.LANGS || 'vi,en').split(',');
    for (const lang of langs) await run(browser, lang);
  } finally {
    await browser.close();
  }
  console.log('done');
})().catch(e => { console.error('FAILED', e.message.slice(0, 600)); process.exit(1); });
