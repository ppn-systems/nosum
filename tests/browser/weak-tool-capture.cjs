// Inventory and gathering round four (docs/adr/ADR-123): a basic axe swung at a large stump, which needs an iron axe. The refusal reads
// "Your Axe is too weak for that. Upgrade it at the blacksmith." (vi: "Rìu chưa đủ sức cho thứ này. Nâng cấp ở thợ rèn nhé.") on the message line.
// Needs a Development server (DOTNET_ENVIRONMENT=Development) and a client published with -p:NosumDevTools=true, opened at ?qa=1.
// Finds a large stump (node kind 11) on the outdoor maps with the QA hook, stands on the tile south of it, faces it, swings with the basic axe.
// Writes <outDir>/<lang>-<width>x<height>-weak-tool.png and prints the toast text read from the page. Usage:
//   node tests/browser/weak-tool-capture.cjs [base] [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/inventory-round4'));
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const SIZES = [[1280, 720], [1920, 1080], [390, 844]];
const LARGE_STUMP = 11;
const MAPS = ['woods', 'forest'];

// The Woods (map "woods") hold the large stumps (OutdoorWorld.WoodsStumps); the first is at 10,12. The node list arrives after the map loads,
// so the search waits for it, and a stump is read from the hook's list ("map;x,y,kind;...", kind 11 = large stump).
async function findLargeStump(page) {
  for (const map of MAPS) {
    await page.evaluate(m => window.nosumQa.teleport(m, 10, 13), map);
    let text = '';
    for (let i = 0; i < 10 && !text.includes(',11'); i++) {
      await wait(1000);
      text = await page.evaluate(() => window.nosumQa.nodes());
    }
    const [mapId, ...rest] = text.split(';');
    for (const entry of rest) {
      if (!entry) continue;
      const [x, y, kind] = entry.split(',').map(Number);
      if (kind === LARGE_STUMP) return { map: mapId, x, y };
    }
    console.log(`[${map}] no large stump in the node list: ${text.slice(0, 120)}`);
  }
  return null;
}

async function run(browser, lang, width, height) {
  const locale = lang === 'en' ? 'en-US' : 'vi-VN';
  const context = await browser.newContext({ viewport: { width, height }, locale, hasTouch: width < 600 });
  await context.addInitScript(value => { try { localStorage.setItem('nosum.settings.v1', value); } catch (e) {} }, `lang=${lang === 'en' ? 2 : 1}\n`);
  const page = await context.newPage();
  page.on('pageerror', e => console.log(`[${lang} ${width}] pageerror:`, e.message.slice(0, 160)));
  const tag = `${lang}-${width}x${height}`;

  await page.goto(`${base.replace(/\/?$/, '/')}?qa=1`, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('canvas', { timeout: 90000 });
  await page.waitForSelector('.calendar-line', { timeout: 60000 });
  await page.waitForFunction(() => globalThis.nosumQa !== undefined, null, { timeout: 30000 });
  await wait(2500);

  const stump = await findLargeStump(page);
  if (!stump) throw new Error(`[${tag}] no large stump found on ${MAPS.join(', ')}`);
  // The tile south of the stump: the farmer walks there, then turns up to face the stump (it blocks the step, so the facing changes).
  await page.evaluate(([m, x, y]) => window.nosumQa.teleport(m, x, y + 1), [stump.map, stump.x, stump.y]);
  await wait(2000);
  await page.keyboard.down('ArrowUp');
  await wait(120);
  await page.keyboard.up('ArrowUp');
  await wait(400);

  // The basic axe on the hotbar ("Axe" or "Rìu"); the number key selects it, the action key uses it on the tile in front.
  const names = await page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const axe = names.findIndex(n => /^(Axe|Rìu)$/.test(n));
  if (axe < 0) throw new Error(`[${tag}] no basic axe on the hotbar; slots: ${names.join(' | ')}`);
  await page.keyboard.press(String(axe + 1));
  await wait(300);
  await page.keyboard.press('KeyE');
  await wait(900);

  const toast = await page.$$eval('.toast, .game-toast, [role="status"]', els => els.map(e => e.textContent.trim()).filter(Boolean).join(' | '));
  console.log(`[${tag}] stump at ${stump.map} ${stump.x},${stump.y}; line: ${toast}`);
  await page.screenshot({ path: path.join(outDir, `${tag}-weak-tool.png`) });
  await context.close();
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium', args: ['--no-sandbox'] });
  try {
    for (const lang of ['vi', 'en']) {
      for (const [w, h] of SIZES) await run(browser, lang, w, h);
    }
  } finally {
    await browser.close();
  }
  console.log('done');
})().catch(e => { console.error('FAILED', e.message.slice(0, 400)); process.exit(1); });
