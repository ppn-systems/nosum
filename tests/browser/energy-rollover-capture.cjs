// Gathering round three captures (docs/adr/ADR-114): the energy bar after a tiller has spent energy, at three sizes in Vietnamese and English.
// Needs a Development server (DOTNET_ENVIRONMENT=Development) and a client published with -p:NosumDevTools=true, opened at ?qa=1.
// Writes <outDir>/<lang>-<width>x<height>-<name>.png. Usage: node tests/browser/energy-rollover-capture.cjs [base] [outDir]
// The steps: the copper hoe granted, four farm tiles tilled from the tile north of each (the action key works the tile in front, 3 energy
// each), then the energy label is read from the HUD. A game-day rollover cannot be forced from here: the client reads the calendar once at sign-in.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/gathering-round3'));
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const SIZES = [[1280, 720], [1920, 1080], [390, 844]];
const COPPER_HOE = 10;
const TILL_ROW = 18;
const TILL_X = [48, 49, 50, 51];

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
  await wait(2000);
  await page.evaluate(id => window.nosumQa.grant(id, 1, 0), COPPER_HOE);
  await wait(1200);
  const names = await page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const hoe = names.findIndex(n => /cuốc đồng|copper hoe/i.test(n));
  if (hoe < 0) throw new Error(`no copper hoe on the hotbar; slots: ${names.join(' | ')}`);
  await page.keyboard.press(String(hoe + 1));
  await wait(300);

  for (const x of TILL_X) {
    await page.evaluate(([tx, ty]) => window.nosumQa.teleport('farm', tx, ty), [x, TILL_ROW - 1]);
    await wait(1200);
    await page.keyboard.press('KeyE');
    await wait(900);
  }

  const energy = await page.$$eval('[aria-label], [title]', els => {
    const hit = els.map(e => e.getAttribute('aria-label') || e.getAttribute('title') || '').find(t => /\d+\s*\/\s*270/.test(t));
    return hit || '';
  });
  console.log(`[${tag}] energy label: ${energy}`);

  await page.screenshot({ path: path.join(outDir, `${tag}-energy-spent.png`) });
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
