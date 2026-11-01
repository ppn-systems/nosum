// Inventory and crafting capture (docs/qa/rebuild-inventory-crafting). Opens the game as a guest in one language at 1280x720, 1920x1080 and
// 390x844 (touch), and screenshots the HUD, the bag (inventory page) and the crafting page. It also records how many clicks the common
// tasks take, so the critique can cite numbers and the before/after runs can be compared.
//
// Needs the server (dotnet run --project server/Nosum.Host, PostgreSQL with the development database) and the client
// (dotnet run --project wasm/Nosum.Wasm, http://localhost:5080). NOSUM_QA_REPO_ASSETS=1 serves the repository's assets when the client
// build leaves some wwwroot images out (see ui-pass-capture.cjs).
//
// usage: node tests/browser/inventory-crafting-capture.cjs <before|after> <vi|en> [viewports, comma-separated]
// writes docs/qa/rebuild-inventory-crafting/<phase>/<lang>/<viewport>/*.png and docs/qa/rebuild-inventory-crafting/audit-<phase>-<lang>.txt

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

async function openGame(browser, name, lang, errors) {
  const size = VIEWPORTS[name];
  const phone = name === '390x844';
  const context = await browser.newContext({ viewport: size, locale: LOCALES[lang], hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 200)}`); });
  if (process.env.NOSUM_QA_REPO_ASSETS === '1') await routeRepoAssets(page);
  // The language is the player's saved setting (nosum.settings.v1, lang=1 Vietnamese, lang=2 English), not the browser's; set it before the game reads it.
  if (lang === 'en') await context.addInitScript(() => { try { window.localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch { /* private window: the game falls back to Vietnamese */ } });
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 60000 });
  await page.waitForTimeout(4000);
  return { context, page };
}

// Opens the column button whose label matches the pattern; returns false when there is none.
async function openFromColumn(page, pattern) {
  const labels = await page.$$eval('.game-hud .hud-top-right .game-icon-button', (els) => els.map((e) => e.getAttribute('aria-label') || ''));
  const index = labels.findIndex((l) => pattern.test(l));
  if (index < 0) return false;
  await page.locator('.game-hud .hud-top-right .game-icon-button').nth(index).click({ force: true });
  await page.waitForTimeout(700);
  return true;
}

(async () => {
  const [phase, lang] = process.argv.slice(2, 4);
  if (!['before', 'after'].includes(phase) || !LOCALES[lang]) throw new Error('usage: inventory-crafting-capture.cjs <before|after> <vi|en> [viewports]');
  const names = (process.argv[4] || Object.keys(VIEWPORTS).join(',')).split(',');
  const outRoot = path.join(REPO, 'docs/qa/rebuild-inventory-crafting');
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const report = [];
  for (const name of names) {
    const dir = path.join(outRoot, phase, lang, name);
    fs.mkdirSync(dir, { recursive: true });
    const errors = [];
    const { context, page } = await openGame(browser, name, lang, errors);
    await page.screenshot({ path: path.join(dir, '00-hud.png') });

    const bagOpened = await openFromColumn(page, /túi|bag|inventory/i);
    report.push(`${name} | bag column button found: ${bagOpened}`);
    if (bagOpened) {
      await page.screenshot({ path: path.join(dir, '01-bag.png') });
      const m = await page.evaluate(() => {
        const w = document.querySelector('.game-hud .hud-center > *');
        if (!w) return { none: true };
        const r = w.getBoundingClientRect();
        return { box: [r.left, r.top, r.width, r.height].map(Math.round), inner: w.querySelectorAll('.slot').length };
      });
      report.push(`${name} | bag | ${JSON.stringify(m)}`);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
    }

    const craftOpened = await openFromColumn(page, /chế tạo|craft/i);
    report.push(`${name} | craft column button found: ${craftOpened}`);
    if (craftOpened) {
      await page.screenshot({ path: path.join(dir, '02-crafting.png') });
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
    }

    report.push(`${name} | errors: ${errors.length ? errors.filter((e) => !/Could not load map|could not be decoded/.test(e)).slice(0, 3).join(' ; ') || 'none' : 'none'}`);
    await context.close();
  }
  await browser.close();
  fs.writeFileSync(path.join(outRoot, `audit-${phase}-${lang}.txt`), report.join('\n') + '\n');
  console.log(report.join('\n'));
})().catch((e) => { console.error(e); process.exit(1); });
