// Farming round three captures (docs/design/critique-farming.md, round-3 addendum; ADR-100).
// The round-two script with two gaps closed: the message line is polled and captured the moment the line shows (a toast lasts 3 s,
// MessageDuration), and the unripe refusal and the harvest-with-item line are read from the E press alone, after a timeline of the line.
// Needs a Development server (DOTNET_ENVIRONMENT=Development) and a client built with -p:NosumDevTools=true, opened at ?qa=1.
// Usage: node tests/browser/farm-round3-capture.cjs [base=http://localhost:5080/] [outDir] [width=1280] [height=720]
// A 390x844 capture needs a copy of the published client whose appsettings.json has Device.MinimumWidth lowered (a test copy only; the
// product keeps 1024), served at its own base. The viewport size sets the tile centre below.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/farming-round3/after'));
const W = Number(process.argv[4] || 1280);
const H = Number(process.argv[5] || 720);
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// The farmer's tile centre on screen, and the size of a tile, measured on the 1280x720 and 390x844 frames.
// On the phone copy the camera is zoomed out: the farmer stands at the middle of the canvas and a tile is 32 px (measured on the frames).
const CENTER = W >= 1024 ? { x: Math.round(W / 2) + 17, y: Math.round(H / 2) + 27 } : { x: Math.round(W / 2), y: 428 };
const TILE_PX = W >= 1024 ? 45 : 32;
const at = (dx, dy) => ({ x: CENTER.x + dx * TILE_PX, y: CENTER.y + dy * TILE_PX });
const BELOW = at(0, 1);
const ROW_X = { A: 48, B: 49, C: 50 };
const ROW_Y = 18;
const NORTH_Y = ROW_Y - 1;

const SLOT = {
  hoe: /cuốc đồng|copper hoe/i,
  seeds: /hạt củ cải vàng|parsnip seeds/i,
  can: /bình tưới|watering can/i,
  scythe: /liềm|scythe/i,
};
// What the game says on the message line for each step (vi, en), read from Language*.resx.
const SAYS = {
  unripe: { vi: /Chưa chín, còn \d+ ngày|Chưa chín, còn 1 ngày/, en: /Not ready, \d+ days? left|Not ready, 1 day left/ },
  keyHarvest: { vi: /Đã thu hoạch, dù đang cầm đồ khác/, en: /Harvested, even with another item in hand/ },
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
  // Stand two tiles north of a row tile, then one step south: the farmer faces the tile below it, with no use made.
  const approachDown = async tileX => {
    await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [tileX, NORTH_Y - 1]);
    await wait(1200);
    await page.keyboard.down('KeyS');
    await wait(120);
    await page.keyboard.up('KeyS');
    await wait(500);
  };
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
  const shot = async (name, note = '') => {
    n += 1;
    const file = `${lang}-${String(n).padStart(2, '0')}-${name}.png`;
    await page.screenshot({ path: path.join(outDir, file) });
    console.log(`[${lang}] ${file} | line: ${await messages(page)}${note}`);
  };
  // Poll the line for up to `timeoutMs` and shoot the first frame where it matches; the timeline is logged so a gap can be read.
  const shotWhen = async (name, re, timeoutMs = 2500) => {
    n += 1;
    const file = `${lang}-${String(n).padStart(2, '0')}-${name}.png`;
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
  console.log(`[${lang}] season on the farm: ${await seasonText()}`);
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
  await wait(2500);
  await page.evaluate(() => { window.nosumQa.grant(100, 10, 0); window.nosumQa.grant(10, 1, 0); });
  await wait(1200);
  await shot('farm-start');

  // 1. The copper hoe: a wide swing over the row.
  await page.keyboard.down('KeyD');
  await wait(120);
  await page.keyboard.up('KeyD');
  await wait(400);
  await selectSlot(SLOT.hoe);
  await page.keyboard.press('KeyR');
  await wait(900);
  await shot('wide-hoe-tilled-row');

  // 2. Parsnip seeds on A and B, watered on four game days (the second watering of a day is the wet-soil refusal); C on day 2.
  await selectSlot(SLOT.seeds);
  await workRowTile(ROW_X.A);
  await workRowTile(ROW_X.B);
  await shot('sown-row');
  for (let day = 1; day <= 4; day++) {
    await selectSlot(SLOT.can);
    await workRowTile(ROW_X.A);
    await workRowTile(ROW_X.B);
    if (day === 1) {
      await workRowTile(ROW_X.A);
      await shot('wet-soil-refusal');
    }
    // C is sown on day 3 and watered on days 3 and 4: two watered days at the end, so it is still unripe (the refusal names the days left).
    if (day === 3) {
      await selectSlot(SLOT.seeds);
      await workRowTile(ROW_X.C);
      await selectSlot(SLOT.can);
      await workRowTile(ROW_X.C);
    }
    if (day === 4) {
      await selectSlot(SLOT.can);
      await workRowTile(ROW_X.C);
    }
    await page.evaluate(() => window.nosumQa.advanceClock(1));
    await wait(1500);
  }
  await shot('a-b-after-four-days');

  // 3. C is one day short of ripe. Crops are walkable, so the farmer is set one tile north of the row (two north of C), then a step
  //    south faces it without a use (a tap would be a use). E with the scythe is then the action key on C: the unripe refusal.
  await selectSlot(SLOT.scythe);
  await approachDown(ROW_X.C);
  await page.keyboard.press('KeyE');
  await shotWhen('unripe-refusal', SAYS.unripe[lang]);
  await wait(3200);

  // 4. Seeds in hand, E on A (ripe): the harvest with another item in hand.
  await selectSlot(SLOT.seeds);
  await approachDown(ROW_X.A);
  await page.keyboard.press('KeyE');
  await shotWhen('harvest-with-item-in-hand', SAYS.keyHarvest[lang]);
  await wait(3200);

  // 5. A tap, not E: the can in hand, a tap on B (ripe). A tap is a use too, so the harvest is named or, before the change, silent.
  await selectSlot(SLOT.can);
  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [ROW_X.B, NORTH_Y]);
  await wait(1200);
  await page.mouse.move(BELOW.x, BELOW.y);
  await page.mouse.down();
  await wait(200);
  await page.mouse.up();
  await shotWhen('harvest-by-tap-with-can', SAYS.keyHarvest[lang], 3000);

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
})().catch(e => { console.error('FAILED', e.message.slice(0, 400)); process.exit(1); });
