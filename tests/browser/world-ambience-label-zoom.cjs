// World label check on a live frame (docs/design/critique-world-ambience.md section 9; docs/adr/ADR-095 D6). Signs in as a guest on a Development
// server opened with ?qa=1 (ADR-079), teleports to a map and tile, waits for the fade, and saves the whole view plus a x4 crop of the centre of the view
// (nearest-neighbour, so each pixel stays hard). Run it once per build, into a folder per build, then compare the crops.
//
// usage: MAP=mine35 TILE=5,5 OUT_DIR=docs/qa/world-ambience-round5/after LOCALE=en-US VIEWPORT=1280x720 node tests/browser/world-ambience-label-zoom.cjs
// Env: NOSUM_QA_URL (default http://localhost:5080/?qa=1). The crop uses ffmpeg (already on the image).

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const MAP = process.env.MAP || 'mine35';
const [TX, TY] = (process.env.TILE || '5,5').split(',').map(Number);
const OUT = path.resolve(process.env.OUT_DIR || 'docs/qa/world-ambience-round5/after');
const LOCALE = process.env.LOCALE || 'en-US';
const [VW, VH] = (process.env.VIEWPORT || '1280x720').split('x').map(Number);
const TAG = process.env.TAG || `${MAP}-${LOCALE.slice(0, 2)}`;
// The centre crop: 320 x 180 at the middle of the view, scaled 4x to 1280 x 720.
const CROP = { x: Math.round(VW / 2 - 160), y: Math.round(VH / 2 - 90), w: 320, h: 180 };

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
  const context = await browser.newContext({ viewport: { width: VW, height: VH }, locale: LOCALE, serviceWorkers: 'block' });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  try {
    await page.goto(BASE, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 90000 });
    await sleep(1000);
    await page.getByRole('button', { name: /khách|guest/i }).click();
    await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
    await sleep(3000);
    await page.evaluate(([id, x, y]) => window.nosumQa.teleport(id, x, y), [MAP, TX, TY]);
    await sleep(6000);
    const full = path.join(OUT, `${TAG}.png`);
    const crop = path.join(OUT, `${TAG}-centre-4x.png`);
    await page.screenshot({ path: full });
    const raw = path.join(OUT, `${TAG}-centre-raw.png`);
    await page.screenshot({ path: raw, clip: { x: CROP.x, y: CROP.y, width: CROP.w, height: CROP.h } });
    execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-i', raw, '-vf', 'scale=iw*4:ih*4:flags=neighbor', crop]);
    fs.unlinkSync(raw);
    const calendar = await page.$eval('.hud-top-right', (e) => (e.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 120)).catch(() => '');
    console.log(JSON.stringify({ map: MAP, tile: [TX, TY], locale: LOCALE, viewport: `${VW}x${VH}`, full, crop, calendar, errors: errors.slice(0, 5) }, null, 1));
  } finally {
    await browser.close();
  }
})().catch((e) => { console.error(e); process.exit(1); });
