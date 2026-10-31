// The sign-in backdrop, candidate A: one frame of the real game (docs/qa/signin/source/). Starts a guest on the farm, hides the HUD, the tutorial
// hints and the name tags (the player's own "names" setting, off), and saves the frame at 1920x1080. Daytime is whatever the server clock says:
// the script prints the in-game clock so the frame can be checked; re-run it when the clock shows daylight.
//
// Needs the server (dotnet run --project server/Nosum.Host, PostgreSQL nosum_dev) and the client (http://localhost:5080).
// usage: node tests/browser/signin-yard-capture.cjs [out-file]

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/';
const OUT = path.resolve(process.argv[2] || path.join(REPO, 'docs/qa/signin/source/yard-1920x1080.png'));
const MIME = { '.png': 'image/png', '.tmx': 'application/xml', '.ogg': 'audio/ogg', '.wav': 'audio/wav', '.json': 'application/json' };

async function routeRepoAssets(page) {
  await page.route(/localhost:5080\/(assets|_content\/Nosum\.Wasm\.Controls\/ui)\//, (route) => {
    const pathname = decodeURIComponent(new URL(route.request().url()).pathname);
    let file;
    if (pathname.startsWith('/assets/Maps/')) file = path.join(REPO, 'art/own/Maps', pathname.slice('/assets/Maps/'.length));
    else if (pathname.startsWith('/assets/')) file = path.join(REPO, 'assets', pathname.slice('/assets/'.length));
    else file = path.join(REPO, 'assets/ui', pathname.slice('/_content/Nosum.Wasm.Controls/ui/'.length));
    if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) return route.fallback();
    return route.fulfill({ status: 200, body: fs.readFileSync(file), contentType: MIME[path.extname(file)] || 'application/octet-stream' });
  });
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1920, height: 1080 }, locale: 'vi-VN', serviceWorkers: 'block' });
  // Names off and no tutorial: the player's own settings, set before the game reads them.
  await context.addInitScript(() => {
    try { window.localStorage.setItem('nosum.settings.v1', 'names=0\ntutorial=0\n'); } catch (e) { /* storage blocked */ }
  });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  await routeRepoAssets(page);
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await page.waitForTimeout(6000);
  // Daytime: wait (up to NOSUM_QA_WAIT_MINUTES, default 40) until the in-game clock shows 09:00 to 15:59.
  const readClock = () => page.evaluate(() => {
    const bar = document.querySelector('.game-hud .hud-top-right') || document.body;
    return (bar.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 160);
  });
  const deadline = Date.now() + Number(process.env.NOSUM_QA_WAIT_MINUTES || 40) * 60000;
  let clock = await readClock();
  for (;;) {
    const m = /(\d{2}):(\d{2})/.exec(clock);
    const hour = m ? Number(m[1]) : -1;
    if (hour >= 9 && hour < 16) break;
    if (Date.now() > deadline) throw new Error(`no daylight within the wait; last clock: ${clock}`);
    await page.waitForTimeout(20000);
    clock = await readClock();
    console.log(`in-game clock: ${clock}`);
  }
  console.log(`in-game clock (frame): ${clock}`);
  // The frame: the HUD, the tutorial line and any notice are hidden; the world is the canvas.
  await page.addStyleTag({ content: '.game-hud, .hud-minimap, .hud-minimap-button, .hud-top-right, .hud-top-left, .hotbar, .toast, .hint, .connection-badge, .tutorial, [class*="toast"], [class*="hint"] { display: none !important; }' });
  // The minimap is drawn on the canvas (the M key shows or hides it); press M once to hide it for the frame.
  await page.keyboard.press('KeyM');
  await page.waitForTimeout(800);
  const hidden = await page.evaluate(() => ['.hud-minimap', '.game-hud', '.hud-top-right'].map((sel) => {
    const el = document.querySelector(sel);
    return `${sel}=${el ? getComputedStyle(el).display : 'absent'}`;
  }));
  console.log(`hud display: ${hidden.join(' ')}`);
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  await page.screenshot({ path: OUT });
  console.log(`saved ${OUT}`);
  if (errors.length) console.log(`page errors: ${errors.slice(0, 3).join(' ; ')}`);
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
