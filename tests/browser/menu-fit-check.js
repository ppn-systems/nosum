// Browser check for the standalone panels and HUD action bar (docs/adr/ADR-044): at each viewport and language every HUD button opens its own standalone
// window inside the screen, every page scrolls from its first line to its last, and the page logs no console errors.
// Run: NODE_PATH=$(npm root -g) node tests/browser/menu-fit-check.js <page url> <screenshot dir>
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:8080/';
const shots = process.argv[3] || '.';
const SIZES = [[802, 293], [393, 727], [1280, 720], [1920, 1080]];
const LANGS = ['vi', 'en'];

const measure = () => {
  const inside = el => {
    if (!el) return false;
    const b = el.getBoundingClientRect();
    return b.left >= -0.5 && b.top >= -0.5 && b.right <= innerWidth + 0.5 && b.bottom <= innerHeight + 0.5;
  };
  const body = document.querySelector('.menu-page-body');
  let reachable = true;
  if (body) {
    const first = body.firstElementChild;
    const last = body.lastElementChild;
    body.scrollTop = body.scrollHeight;
    if (last) {
      reachable = reachable && last.getBoundingClientRect().bottom <= body.getBoundingClientRect().bottom + 1;
    }
    body.scrollTop = 0;
    if (first) {
      reachable = reachable && first.getBoundingClientRect().top >= body.getBoundingClientRect().top - 1;
    }
  }
  const win = document.querySelector('.trade-window');
  const dialog = document.querySelector('.trade-dialog');
  return {
    windowInside: inside(win),
    menuInside: inside(dialog),
    title: document.querySelector('.trade-window .game-panel-title')?.textContent || '',
    reachable,
    scrolls: body ? body.scrollHeight > body.clientHeight : false,
  };
};

async function checkMenu(browser, width, height, lang) {
  const context = await browser.newContext({ viewport: { width, height }, locale: lang === 'en' ? 'en-US' : 'vi-VN' });
  const page = await context.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('requestfailed', r => errors.push('requestfailed: ' + r.url()));
  const tag = `${width}x${height}-${lang}`;
  const results = [];

  await page.goto(base, { waitUntil: 'load' });
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 60000 });
  await page.waitForTimeout(3000);

  await page.keyboard.press('KeyH');
  await page.waitForSelector('.trade-window', { timeout: 10000 });
  if (lang === 'en') {
    await page.getByRole('button', { name: 'English' }).click();
    await page.waitForTimeout(800);
  }
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);

  const count = await page.locator('.game-icon-button').count();
  for (let i = 0; i < count; i++) {
    const btn = page.locator('.game-icon-button').nth(i);
    const label = await btn.getAttribute('aria-label');
    let clicked = true;
    try {
      await btn.click({ timeout: 4000 });
    } catch {
      clicked = false;
    }
    await page.waitForTimeout(250);
    const m = await page.evaluate(measure);
    const ok = clicked && m.menuInside && m.windowInside && m.reachable;
    results.push({ label, clicked, ...m, ok });
    await page.screenshot({ path: path.join(shots, `menu-${tag}-${i}.png`) });
  }
  await page.keyboard.press('Escape');
  await context.close();

  const failed = results.filter(r => !r.ok).map(r => r.label);
  console.log(`${tag} buttons=${count} scrollable=${results.filter(r => r.scrolls).map(r => r.label).join(',') || 'none'}`);
  for (const r of results) {
    console.log(`  ${(r.label || '').padEnd(18)} click=${r.clicked} dialog=${r.menuInside} window=${r.windowInside} reach=${r.reachable} ${r.ok ? 'ok' : 'FAIL'}`);
  }
  if (failed.length) console.log(`  FAILED panels: ${failed.join(', ')}`);
  console.log(`  console errors: ${errors.length ? JSON.stringify(errors) : 'none'}`);
  return results.length > 0 && failed.length === 0 && errors.length === 0;
}

(async () => {
  fs.mkdirSync(shots, { recursive: true });
  const browser = await chromium.launch();
  let ok = true;
  for (const [width, height] of SIZES) {
    for (const lang of LANGS) {
      ok = (await checkMenu(browser, width, height, lang)) && ok;
    }
  }
  console.log(ok ? 'RESULT: PASS' : 'RESULT: FAIL');
  await browser.close();
  process.exit(ok ? 0 : 1);
})();
