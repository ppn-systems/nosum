// Farming round six captures (docs/design/critique-farming.md, round-6 addendum, ADR-121).
// Plays the one case this round changes: a parsnip seed tapped on a tilled tile in a season the crop does not grow in. The refusal now names
// the seasons it does grow in ("That crop only grows in Spring, Fall, Winter." / "Cây này chỉ trồng được vào Xuân, Thu, Đông.").
// Needs a Development server (DOTNET_ENVIRONMENT=Development, dotnet run --project server/Nosum.Host, PostgreSQL role nosum and database nosum_dev)
// and a client published with -p:NosumDevTools=true, opened at ?qa=1.
// Usage: node tests/browser/farm-round6-capture.cjs [base=http://localhost:5080/] [outDir] [width=1280] [height=720]
// A 390x844 capture needs a copy of the published client whose wwwroot/appsettings.json has Device.MinimumWidth lowered (a test copy only).
// Env: LANGS=vi,en (default both).
const { chromium } = require('playwright');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/farming-round6/after'));
const W = Number(process.argv[4] || 1280);
const H = Number(process.argv[5] || 720);
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// The farmer's tile centre on screen and the tile size, as the round-four script measured them.
const CENTER = W >= 1024 ? { x: Math.round(W / 2) + 17, y: Math.round(H / 2) + 27 } : { x: Math.round(W / 2), y: 428 };
const TILE_PX = W >= 1024 ? 45 : 32;
const at = (dx, dy) => ({ x: CENTER.x + dx * TILE_PX, y: CENTER.y + dy * TILE_PX });
const BELOW = at(0, 1);
const TILE = { x: 48, y: 18 };
const NORTH = { x: TILE.x, y: TILE.y - 1 };

const SLOT = { seeds: /parsnip seeds|hạt củ cải vàng/i };
const SAYS = {
  // Parsnip grows in Spring, Fall and Winter; the test season is Summer.
  outOfSeason: { vi: /Cây này chỉ trồng được vào Xuân, Thu, Đông\./, en: /That crop only grows in Spring, Fall, Winter\./ },
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
  const hotbarNames = () => page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const selectSlot = async re => {
    const names = await hotbarNames();
    const i = names.findIndex(n => re.test(n));
    if (i < 0) throw new Error(`no hotbar slot matches ${re}; slots: ${names.join(' | ')}`);
    await page.keyboard.press(String(i + 1));
    await wait(250);
  };

  let n = 0;
  const shot = async name => {
    n += 1;
    const file = `${lang}-${String(n).padStart(2, '0')}-${name}-${W}x${H}.png`;
    await page.screenshot({ path: path.join(outDir, file) });
    console.log(`[${lang}] ${file} | line: ${await messages(page)}`);
  };
  // Polls the message line for up to timeoutMs and shoots the first frame that matches; the timeline is logged so a gap can be read.
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
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
  await wait(2500);
  // Seeds (100) land in the free hotbar slot; the starting hoe is in slot 1.
  await page.evaluate(() => window.nosumQa.grant(100, 10, 0));
  await wait(1200);

  // Move into Summer first: bare tilled soil reverts after its dry days, so the till comes after the season change.
  await page.evaluate(() => window.nosumQa.season(1));
  await wait(2000);
  // Till the tile with the hoe (one tap). Parsnip seeds do not grow in Summer.
  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [NORTH.x, NORTH.y]);
  await wait(1200);
  await page.keyboard.press('1');
  await wait(250);
  await tap(BELOW);
  await shot('tilled');
  await wait(3200);

  // The seed in hand on the tilled tile: the refusal names the seasons the crop grows in.
  await selectSlot(SLOT.seeds);
  await tap(BELOW);
  await shotWhen('out-of-season-seed', SAYS.outOfSeason[lang], 2500);
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'], executablePath: process.env.CHROME_PATH || undefined });
  const langs = (process.env.LANGS || 'vi,en').split(',').map(s => s.trim()).filter(Boolean);
  try {
    for (const lang of langs) {
      await run(browser, lang);
    }
  } finally {
    await browser.close();
  }
})().catch(err => {
  console.error(err);
  process.exit(1);
});
