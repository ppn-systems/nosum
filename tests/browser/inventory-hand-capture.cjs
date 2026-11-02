// Inventory round four captures (docs/design/critique-inventory-crafting.md section 8, ADR-116's open "In hand" item): the held-item strip of the bag
// at three sizes in Vietnamese and English, and a measurement of every held item the guest is granted: whether the strip ends inside the window.
// Needs a Development server (DOTNET_ENVIRONMENT=Development) and a client published with -p:NosumDevTools=true, opened at ?qa=1.
// Writes <outDir>/<lang>-<width>x<height>-hand-<item>.png and prints one JSON line per frame. Usage:
//   node tests/browser/inventory-hand-capture.cjs [base] [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/inventory-round4'));
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const SIZES = [[1280, 720], [1920, 1080], [390, 844]];
// Items granted to the guest, in the order the bag fills: tools with the longest descriptions first.
const ITEMS = [
  { id: 22, name: 'iron axe' },
  { id: 23, name: 'iron pickaxe' },
  { id: 21, name: 'iron watering can' },
  { id: 40, name: 'fishing rod' },
  { id: 10, name: 'copper hoe' },
];

// Reads the held strip against the window: the strip's bottom and the window's bottom, and whether the body clips (scrollHeight beyond clientHeight).
async function measure(page) {
  return page.evaluate(() => {
    const strip = document.querySelector('.inv-hand-strip');
    const panel = strip && strip.closest('.game-panel');
    if (!strip || !panel) return null;
    const body = panel.querySelector('.game-panel-body');
    const s = strip.getBoundingClientRect();
    const p = panel.getBoundingClientRect();
    const text = (strip.querySelector('.hand-line') || strip.querySelector('.hand-title') || strip).getBoundingClientRect();
    return {
      stripBottom: Math.round(s.bottom),
      textBottom: Math.round(text.bottom),
      panelBottom: Math.round(p.bottom),
      panelTop: Math.round(p.top),
      bodyOverflow: body ? body.scrollHeight - body.clientHeight : null,
      clipsText: text.bottom > p.bottom - 2,
    };
  });
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
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
  await wait(1500);
  for (const item of ITEMS) {
    await page.evaluate(id => window.nosumQa.grant(id, 1, 0), item.id);
    await wait(400);
  }
  await wait(800);

  // Every non-empty hotbar slot is held in turn (the key of the slot), with the bag open, and the strip is measured for it.
  const names = await page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const held = names.map((n, i) => ({ n, i })).filter(x => x.n);
  console.log(`[${tag}] hotbar: ${held.map(x => x.n).join(' | ')}`);
  for (const { n, i } of held) {
    await page.keyboard.press(String(i + 1));
    await wait(250);
    await page.keyboard.press('KeyI');
    await wait(900);
    const m = await measure(page);
    console.log(JSON.stringify({ tag, item: n, ...m }));
    await page.screenshot({ path: path.join(outDir, `${tag}-hand-${i + 1}.png`) });
    await page.keyboard.press('KeyI');
    await wait(400);
  }
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
