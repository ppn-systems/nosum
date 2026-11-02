// World QA pass 10: captures the screens of the open items at one size and language (ui-standard section 7). A guest signs in on the
// Development server, the farm is shown, and the menu, its Profile tile and the quest card are saved, the quest card and the menu tile also as 2x
// zoomed clips. The emote cell and the menu column are checked as geometry (their boxes, and whether they overlap), so the report says it in numbers.
// Development only: the grants and teleports are the Development tester calls (ADR-079), so the server runs with DOTNET_ENVIRONMENT=Development and
// the client is built with -p:NosumDevTools=true.
//
// usage: node tests/browser/world-pass10-capture.cjs <locale vi|en> <WxH> [outDir]
// Env: BASE (default http://localhost:5080/?qa=1). Writes into outDir (default: docs/qa/world-pass10/capture/<locale>/<WxH>).
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.BASE || 'http://localhost:5080/?qa=1';
const [lk = 'en', size = '1280x720', outArg] = process.argv.slice(2);
const [width, height] = size.split('x').map(Number);
const OUT = path.resolve(REPO, outArg || `docs/qa/world-pass10/capture/${lk}/${size}`);
const LANG_TAG = { vi: 'vi-VN', en: 'en-US' };
const wait = ms => new Promise(r => setTimeout(r, ms));
fs.mkdirSync(OUT, { recursive: true });

const qaCall = (page, fn, args = []) => page.evaluate(([f, a]) => window.nosumQa[f](...a), [fn, args]);

// The geometry the standard checks: the emote cell and the column's bottom button, as boxes (CSS px), and whether they overlap.
async function geometry(page) {
  return page.evaluate(() => {
    const box = el => {
      if (!el) return null;
      const r = el.getBoundingClientRect();
      return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), r: Math.round(r.right), b: Math.round(r.bottom) };
    };
    const emote = document.querySelector('.hud-bottom-right .game-icon-button, .hud-bottom-right button');
    const column = [...document.querySelectorAll('.hud-menu-column .hud-menu-item')].filter(el => el.offsetParent !== null);
    const last = column[column.length - 1];
    const e = box(emote), c = box(last);
    const overlap = e && c && !(e.r <= c.x || c.r <= e.x || e.b <= c.y || c.b <= e.y);
    return { emote: e, columnBottom: c, columnCount: column.length, overlap: Boolean(overlap) };
  });
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({
    viewport: { width, height },
    locale: LANG_TAG[lk],
    isMobile: width <= 640,
    hasTouch: width <= 640,
    deviceScaleFactor: 1,
  });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(String(e.message).slice(0, 200)));

  const report = { locale: lk, size, steps: [], errors };
  const shot = async name => { await page.screenshot({ path: path.join(OUT, name + '.png') }); };
  // A clip of one element with a few pixels of its surroundings (the page's own scale: the frame is what the player sees).
  const clip = async (locator, name) => {
    const box = await locator.first().boundingBox();
    if (!box) return;
    const pad = 4;
    await page.screenshot({ path: path.join(OUT, name + '.png'), clip: { x: Math.max(0, box.x - pad), y: Math.max(0, box.y - pad), width: box.width + pad * 2, height: box.height + pad * 2 } });
  };

  try {
    await page.goto(BASE, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 90000 });
    await wait(1000);
    await page.getByRole('button', { name: /khách|guest/i }).first().click({ timeout: 90000 });
    await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
    await wait(2500);
    await qaCall(page, 'springReset');
    await wait(1500);

    await shot('01-farm');
    await clip(page.locator('.quest-wrap'), '01-quest-zoom');
    // Probe (not the game): the longest quest titles put in the card, to see whether any hides its first letter under the scroll icon.
    const titles = (process.env.PROBE_TITLES || '').split('|').filter(Boolean);
    for (const [i, t] of titles.entries()) {
      await page.evaluate(text => { const el = document.querySelector('.quest-title'); if (el) el.textContent = text; }, t);
      await wait(150);
      await clip(page.locator('.quest-wrap'), `01-quest-probe-${String(i + 1).padStart(2, '0')}`);
    }
    report.steps.push({ name: 'farm', geometry: await geometry(page) });

    // The menu: the phone's Menu button, or the column's last button on a desktop.
    const menuButton = page.locator('.hud-top-right .hud-menu-button').first();
    await menuButton.click({ timeout: 10000 });
    await wait(900);
    await shot('02-menu-window');
    // The Profile tile: its name is the Toolbar_Profile text of the locale ("Profile" in English).
    const profile = page.locator('.game-menu-tile').filter({ has: page.locator('.game-menu-tile-name', { hasText: lk === 'en' ? /^Profile$/ : /Hồ sơ|Profile/ }) }).first();
    if (await profile.count()) {
      const nameLines = await profile.locator('.game-menu-tile-name').evaluate(el => Math.round(el.getBoundingClientRect().height));
      await clip(profile, '03-profile-tile');
      report.steps.push({ name: 'profile-tile', nameHeightPx: nameLines });
    } else {
      report.steps.push({ name: 'profile-tile', error: 'no tile' });
    }
    await shot('03-menu-tiles');
  } catch (e) {
    report.steps.push({ name: 'fatal', error: String(e.message || e).split('\n')[0].slice(0, 220) });
    await shot('99-fatal').catch(() => {});
  }
  report.pageErrors = errors.slice(0, 5);
  fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
  await browser.close();
})().catch(e => { console.error('CAPTURE FAIL', e.message); process.exit(1); });
