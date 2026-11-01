// Full QA pass (docs/qa/full-pass). Signs in as a guest on the Development server with the tester client (ADR-079, ?qa=1 not required
// for a plain run), then for each locale (vi-VN, en-US) and viewport (1280x720, 1920x1080, 390x844 touch):
//   - screenshots the HUD and every top-right panel button, the menu column and the movement check,
//   - runs the layout audit (scroll boxes, clipped boxes, HUD overlap, off-screen windows),
//   - records console errors and page errors, and any panel that opened without content.
// Output: docs/qa/full-pass/<locale>/<viewport>/*.png and docs/qa/full-pass/audit-<locale>-<viewport>.json
//
// Needs the server (dotnet run --project server/Nosum.Host with DOTNET_ENVIRONMENT=Development, PostgreSQL nosum_dev) and the client
// (dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080). Set NOSUM_QA_REPO_ASSETS=1 if the client
// build leaves wwwroot images out (see ui-pass-capture.cjs).
//
// usage: node tests/browser/full-pass.cjs [locales=vi,en] [viewports=1280x720,1920x1080,390x844]

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/';
const OUT = path.join(REPO, 'docs/qa/full-pass');
const VIEWPORTS = {
  '1280x720': { width: 1280, height: 720 },
  '1920x1080': { width: 1920, height: 1080 },
  '390x844': { width: 390, height: 844 },
};
const LOCALES = { vi: 'vi-VN', en: 'en-US' };
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

// Layout audit of the open window (same rules as ui-pass-capture.cjs, section 7 items 2, 4, 5, 7).
const MEASURE = () => {
  const w = document.querySelector('.trade-window') || document.querySelector('.game-hud .hud-center > *');
  if (!w) return { none: true };
  const wr = w.getBoundingClientRect();
  const label = (e) => (e.className.baseVal ?? e.className).toString().slice(0, 30);
  const issues = [];
  for (const e of w.querySelectorAll('*')) {
    const cs = getComputedStyle(e);
    if (e.clientHeight <= 0 || e.scrollHeight <= e.clientHeight + 2) continue;
    if (/(auto|scroll)/.test(cs.overflowY)) issues.push(`scroll ${label(e)} ${e.scrollHeight}/${e.clientHeight}`);
    else if (/hidden/.test(cs.overflowY) && e.tagName !== 'BUTTON') issues.push(`clip ${label(e)} ${e.scrollHeight}/${e.clientHeight}`);
  }
  const covers = [];
  for (const h of document.querySelectorAll('.hud-top-right .hud-bar, .hud-top-right .hud-player-bar, .hud-top-right .hud-minimap, .hud-top-left > *, .hotbar')) {
    if (getComputedStyle(h).display === 'none') continue;
    const hr = h.getBoundingClientRect();
    if (hr.width && hr.height && !(hr.right <= wr.left || hr.left >= wr.right || hr.bottom <= wr.top || hr.top >= wr.bottom)) covers.push(label(h));
  }
  const off = wr.left < 0 || wr.top < 0 || wr.right > innerWidth + 1 || wr.bottom > innerHeight + 1;
  return { box: [wr.left, wr.top, wr.width, wr.height].map(Math.round), issues: issues.slice(0, 6), covers, off };
};

function slug(s) {
  return (s || 'item').replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '').slice(0, 40) || 'item';
}

async function signIn(page) {
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 60000 });
  await page.waitForTimeout(4000);
}

async function runOne(browser, locale, vpName, report) {
  const size = VIEWPORTS[vpName];
  const phone = vpName === '390x844';
  const dir = path.join(OUT, locale, vpName);
  fs.mkdirSync(dir, { recursive: true });
  const errors = [];
  const context = await browser.newContext({ viewport: size, locale: LOCALES[locale], hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 200)}`); });
  if (process.env.NOSUM_QA_REPO_ASSETS === '1') await routeRepoAssets(page);
  const rec = (feature, status, notes) => report.push({ locale, viewport: vpName, feature, status, notes });
  try {
    await signIn(page);
  } catch (e) {
    rec('sign-in (guest)', 'FAIL', String(e.message).slice(0, 160));
    await context.close();
    return;
  }
  // The UI language is an account setting (Settings > General), not the browser locale: switch it for the en pass.
  if (locale === 'en') {
    try {
      const settings = page.locator('.game-hud .hud-top-right .game-icon-button[aria-label*="Cài đặt"], .game-hud .hud-top-right .game-icon-button[aria-label*="Settings"]').first();
      await settings.click({ force: true, timeout: 5000 });
      await page.waitForTimeout(600);
      await page.getByRole('button', { name: 'English' }).first().click({ timeout: 5000 });
      await page.waitForTimeout(600);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(400);
      rec('settings: language English', 'pass', 'switched in Settings > General');
    } catch (e) {
      rec('settings: language English', 'FAIL', String(e.message).split('\n')[0].slice(0, 160));
    }
  }
  await page.screenshot({ path: path.join(dir, '00-hud.png') });
  rec('HUD', 'captured', '');

  // Movement: hold a key for 800 ms and check the camera/farmer position changed (screenshot before and after).
  await page.keyboard.down('d');
  await page.waitForTimeout(800);
  await page.keyboard.up('d');
  await page.waitForTimeout(300);
  await page.screenshot({ path: path.join(dir, '01-moved.png') });
  rec('movement (WASD)', 'captured', 'screenshot only; position is not read back (no test hook)');

  // Every top-right panel button.
  const buttons = page.locator('.game-hud .hud-top-right .game-icon-button');
  const labels = await page.$$eval('.game-hud .hud-top-right .game-icon-button', (els) => els.map((e) => e.getAttribute('aria-label') || e.textContent.trim()));
  for (let i = 0; i < labels.length; i++) {
    const file = `${String(i + 2).padStart(2, '0')}-${slug(labels[i])}.png`;
    try {
      await buttons.nth(i).click({ force: true, timeout: 5000 });
      await page.waitForTimeout(700);
      await page.screenshot({ path: path.join(dir, file) });
      const m = await page.evaluate(MEASURE);
      const bad = m.none ? 'no window opened' : [...(m.issues || []), ...(m.covers || []).map((c) => `covers ${c}`), m.off ? 'off-screen' : ''].filter(Boolean).join('; ');
      rec(`panel: ${labels[i]}`, m.none ? 'n/a' : bad ? 'FAIL' : 'pass', m.none ? 'no window: a toggle or wheel, checked by screenshot only' : bad || `box ${JSON.stringify(m.box)}`);
    } catch (e) {
      rec(`panel: ${labels[i]}`, 'FAIL', String(e.message).split('\n')[0].slice(0, 160));
    }
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);
  }
  const real = errors.filter((e) => !/Could not load map|could not be decoded/.test(e));
  rec('console / page errors', real.length ? 'FAIL' : 'pass', real.slice(0, 3).join(' ; ') || 'none');
  await context.close();
}

(async () => {
  const locales = (process.argv[2] || 'vi,en').split(',');
  const vps = (process.argv[3] || Object.keys(VIEWPORTS).join(',')).split(',');
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const all = [];
  for (const loc of locales) {
    for (const vp of vps) {
      const report = [];
      await runOne(browser, loc, vp, report);
      fs.writeFileSync(path.join(OUT, `audit-${loc}-${vp}.json`), JSON.stringify(report, null, 2));
      all.push(...report);
      console.log(`${loc} ${vp}: ${report.length} checks`);
    }
  }
  await browser.close();
  const fails = all.filter((r) => r.status === 'FAIL');
  console.log(`total ${all.length}, FAIL ${fails.length}`);
  fails.forEach((r) => console.log(`FAIL ${r.locale} ${r.viewport} ${r.feature}: ${r.notes}`));
})().catch((e) => { console.error(e); process.exit(1); });
