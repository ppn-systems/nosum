// UI pass capture (docs/qa/ui-pass). Opens the game as a guest at 1280x720, 1920x1080 and 390x844 (touch), screenshots the HUD and every toolbar
// panel, and reports layout faults in every panel: inner scroll boxes, content clipped by its box, windows covering the HUD, windows off screen.
//
// Needs the server (dotnet run --project server/Nosum.Host, PostgreSQL with the development database) and the client
// (dotnet run --project wasm/Nosum.Wasm, http://localhost:5080). The client build on Linux may leave some wwwroot images out (the csproj
// Link paths use backslashes); NOSUM_QA_REPO_ASSETS=1 serves those files from the repository's assets/ and art/own/Maps/ folders.
//
// usage: node tests/browser/ui-pass-capture.cjs <before|after> [viewports, comma-separated]
// writes docs/qa/ui-pass/<phase>/<viewport>/*.png and docs/qa/ui-pass/audit-<phase>.txt

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/';
const VIEWPORTS = {
  '1280x720': { width: 1280, height: 720 },
  '1920x1080': { width: 1920, height: 1080 },
  '390x844': { width: 390, height: 844 },
};
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

async function openGame(browser, name, errors) {
  const size = VIEWPORTS[name];
  const phone = name === '390x844';
  const context = await browser.newContext({ viewport: size, locale: 'vi-VN', hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 200)}`); });
  if (process.env.NOSUM_QA_REPO_ASSETS === '1') await routeRepoAssets(page);
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 60000 });
  await page.waitForTimeout(4000);
  return { context, page };
}

// Measures the open window: scroll boxes and clipped boxes inside it, HUD elements it covers, and whether it leaves the screen.
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

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: ui-pass-capture.cjs <before|after> [viewports]');
  const names = (process.argv[3] || Object.keys(VIEWPORTS).join(',')).split(',');
  const outRoot = path.join(REPO, 'docs/qa/ui-pass');
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const report = [];
  for (const name of names) {
    const dir = path.join(outRoot, phase, name);
    fs.mkdirSync(dir, { recursive: true });
    const errors = [];
    const { context, page } = await openGame(browser, name, errors);
    const buttons = page.locator('.game-hud .hud-top-right .game-icon-button');
    const labels = await page.$$eval('.game-hud .hud-top-right .game-icon-button', (els) => els.map((e) => e.getAttribute('aria-label')));
    await page.screenshot({ path: path.join(dir, '00-hud.png') });
    for (let i = 0; i < labels.length; i++) {
      await buttons.nth(i).click({ force: true });
      await page.waitForTimeout(700);
      const file = `${String(i + 1).padStart(2, '0')}-${labels[i].replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '')}.png`;
      await page.screenshot({ path: path.join(dir, file) });
      const m = await page.evaluate(MEASURE);
      const flag = m.none ? 'NONE' : m.issues.length || m.covers.length || m.off ? 'FAIL' : 'ok';
      report.push(`${name} | ${labels[i]} | ${flag} | ${JSON.stringify(m)}`);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
    }
    const real = errors.filter((e) => !/Could not load map|could not be decoded/.test(e));
    report.push(`${name} | errors: ${real.length ? real.slice(0, 3).join(' ; ') : 'none'}`);
    await context.close();
  }
  await browser.close();
  fs.writeFileSync(path.join(outRoot, `audit-${phase}.txt`), report.join('\n') + '\n');
  console.log(report.join('\n'));
})().catch((e) => { console.error(e); process.exit(1); });
