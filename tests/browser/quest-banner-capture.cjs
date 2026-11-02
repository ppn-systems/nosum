// Copyright (c) 2026 PPN Systems. All rights reserved.
// Captures the quest completion banner that names the coins the quest paid (docs/design/critique-combat-fishing.md item 11).
// The first quest of the chain is tilling soil (10 coins): one till completes it, so a new guest gets the banner at once.
// Needs a Development server and a client built with -p:NosumDevTools=true, opened at ?qa=1 (see docs/qa/quest-banner-coins/README.md).
// Usage: node tests/browser/quest-banner-capture.cjs [base=http://localhost:5080/] [outDir] [width=1280] [height=720]
// Env: LANGS=vi,en (default both).
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/quest-banner-coins/after'));
const W = Number(process.argv[4] || 1280);
const H = Number(process.argv[5] || 720);
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// The farmer's tile centre on screen and the tile size, measured on the 1280x720 and 390x844 frames of the farm captures.
const CENTER = W >= 1024 ? { x: Math.round(W / 2) + 17, y: Math.round(H / 2) + 27 } : { x: Math.round(W / 2), y: 428 };
const TILE_PX = W >= 1024 ? 45 : 32;
const BELOW = { x: CENTER.x, y: CENTER.y + TILE_PX };

// The starting hoe (vi and en) on the hotbar; the new guest already holds it.
const HOE = /\bhoe\b|cuốc/i;
const BANNER = /Hoàn thành nhiệm vụ|Quest complete/;

const banner = page => page.evaluate(() => {
  const nodes = document.querySelectorAll('.quest.completed .banner');
  for (const e of nodes) {
    const r = e.getBoundingClientRect();
    if (r.width && r.height) return (e.innerText || '').trim();
  }
  return '';
});

async function run(browser, lang) {
  const locale = lang === 'en' ? 'en-US' : 'vi-VN';
  const context = await browser.newContext({ viewport: { width: W, height: H }, locale, hasTouch: W < 1024, isMobile: W < 1024 });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));

  await page.goto(`${base.replace(/\/?$/, '/')}?qa=1`, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('canvas', { timeout: 90000 });
  await page.waitForSelector('.calendar-line', { timeout: 60000 });
  await page.waitForFunction(() => globalThis.nosumQa !== undefined, null, { timeout: 30000 });
  await wait(2500);

  await page.evaluate(() => window.nosumQa.teleport('farm', 48, 17));
  await wait(2500);

  const names = await page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const slot = names.findIndex(n => HOE.test(n));
  if (slot < 0) throw new Error(`no hoe on the hotbar; slots: ${names.join(' | ')}`);
  await page.keyboard.press(String(slot + 1));
  await wait(300);

  // Till the tile below the farmer with a press and release, as the farm captures do (a quick click is not read as a use). A few tries, each
  // from a fresh teleport onto the farm row, since a press can land on a tile the farm has already tilled.
  const tap = async p => {
    await page.mouse.move(p.x, p.y);
    await page.mouse.down();
    await wait(200);
    await page.mouse.up();
  };
  let text = '';
  for (let attempt = 0; attempt < 4 && !BANNER.test(text); attempt++) {
    await page.evaluate(() => window.nosumQa.teleport('farm', 48, 17));
    await wait(1200);
    await tap(BELOW);
    const t0 = Date.now();
    while (Date.now() - t0 < 1500) {
      text = await banner(page);
      if (BANNER.test(text)) break;
      await wait(60);
    }
  }
  if (!BANNER.test(text)) throw new Error(`[${lang}] the completion banner did not show; last text: "${text}"`);

  const file = path.join(outDir, `quest-banner-${W}x${H}-${lang}.png`);
  await page.screenshot({ path: file });
  console.log(`[${lang}] ${W}x${H} banner: ${text} -> ${file}`);
  if (errors.length) console.log(`[${lang}] page errors: ${errors.length}`);
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
