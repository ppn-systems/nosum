// Phone UI pass capture (docs/qa/phone-ui). Captures the screens this pass touches at 1280x720, 1920x1080 and 390x844 (touch):
// the sign-in screen, the HUD with the hotbar, a multi-line item tooltip (boots and the speed tonic), the inventory, the Town
// merchant dialog (seed keeper) and the settings page. Each file is the full viewport at that size.
//
// Needs the Development server on PostgreSQL (DOTNET_ENVIRONMENT=Development, database nosum_dev) and a client built with
// -p:NosumDevTools=true, served at NOSUM_QA_URL (default http://localhost:5080/). The client build on Linux may leave some
// wwwroot images out; NOSUM_QA_REPO_ASSETS=1 serves them from the repository's assets/ and art/own/Maps/ folders.
//
// usage: node tests/browser/phone-ui-capture.cjs <before|after> [viewports, comma-separated]
// writes docs/qa/phone-ui/<phase>/<viewport>/*.png and docs/qa/phone-ui/audit-<phase>.txt

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
const GUEST = /Chơi ngay với tư cách khách|Play now as a guest/;
// Winged boots (411), speed tonic (715, quantity 2): the two multi-line tooltips the phone pass must fit.
const GRANTS = [[411, 1, 0], [715, 2, 0], [408, 1, 0]];
// The seed keeper's counter in Town (npc-dialogue-capture.cjs).
const MERCHANT_TILE = [43, 57];
// NOSUM_QA_LANG=2 opens the client in English (the lang query value, as in docs/qa/boots); unset is Vietnamese, the default.
const LANG = process.env.NOSUM_QA_LANG || '';

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

// Measures the open window and the HUD: scroll boxes, clipped boxes, HUD regions it covers, the hotbar edges, and off-screen.
const MEASURE = () => {
  const vw = innerWidth, vh = innerHeight;
  const box = (e) => { const r = e.getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom].map(Math.round); };
  const hotbar = document.querySelector('.hotbar');
  const out = { viewport: [vw, vh], hotbar: hotbar ? box(hotbar) : null };
  const hudBits = [...document.querySelectorAll('.game-hud > *, .hotbar-dock, .hud-top-right > *, .hud-top-left > *, .touch-controls, .touch-pad, .touch-actions')];
  out.offscreen = hudBits.filter((e) => { const r = e.getBoundingClientRect(); return r.width && r.height && (r.left < -1 || r.right > vw + 1 || r.top < -1 || r.bottom > vh + 1); }).map((e) => (e.className.baseVal ?? e.className).toString().slice(0, 40));
  if (hotbar) {
    const skills = document.querySelector('.hotbar-skills');
    const slots = document.querySelector('.hotbar-slots');
    if (skills && slots) {
      const a = skills.getBoundingClientRect(), b = slots.getBoundingClientRect();
      out.skillsOverlapSlots = !(a.right <= b.left || a.left >= b.right || a.bottom <= b.top || a.top >= b.bottom);
    }
  }
  const scrollers = [];
  for (const e of document.querySelectorAll('.trade-window *, .game-hud *')) {
    const cs = getComputedStyle(e);
    if (e.clientHeight > 0 && e.scrollHeight > e.clientHeight + 2 && /(auto|scroll)/.test(cs.overflowY)) scrollers.push((e.className.baseVal ?? e.className).toString().slice(0, 30));
  }
  out.scrollers = scrollers.slice(0, 4);
  const tip = document.getElementById('nosum-tip');
  if (tip && tip.classList.contains('shown')) {
    const r = tip.getBoundingClientRect();
    out.tip = { box: [r.left, r.top, r.right, r.bottom].map(Math.round), scrollH: tip.scrollHeight, clientH: tip.clientHeight, onScreen: r.left >= 0 && r.top >= 0 && r.right <= vw && r.bottom <= vh, fontPx: getComputedStyle(tip.querySelector('.tip-text') || tip).fontSize };
  }
  return out;
};

async function openGame(browser, name, errors) {
  const size = VIEWPORTS[name];
  const phone = name === '390x844';
  const context = await browser.newContext({ viewport: size, locale: LANG === '2' ? 'en-US' : 'vi-VN', hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 200)}`); });
  if (process.env.NOSUM_QA_REPO_ASSETS === '1') await routeRepoAssets(page);
  // The language is a client setting (nosum-settings.js, key nosum.settings.v1, lines key=value); set it before the page loads so the sign-in is in it too.
  if (LANG) await page.addInitScript((lang) => { try { localStorage.setItem('nosum.settings.v1', `lang=${lang}\n`); } catch (e) { /* private window */ } }, LANG);
  await page.goto(BASE + '?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1500);
  return { context, page };
}

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: phone-ui-capture.cjs <before|after> [viewports]');
  const names = (process.argv[3] || Object.keys(VIEWPORTS).join(',')).split(',');
  const outRoot = path.join(REPO, 'docs/qa/phone-ui', LANG === '2' ? 'en' : 'vi');
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const report = [];
  for (const name of names) {
    const dir = path.join(outRoot, phase, name);
    fs.mkdirSync(dir, { recursive: true });
    const errors = [];
    const { context, page } = await openGame(browser, name, errors);
    const shot = async (file) => page.screenshot({ path: path.join(dir, file) });
    const log = async (label) => report.push(`${name} | ${label} | ${JSON.stringify(await page.evaluate(MEASURE))}`);

    // 1. Sign-in screen (before the guest play button).
    await shot('01-signin.png');
    report.push(`${name} | signin | body font ${await page.evaluate(() => getComputedStyle(document.body).fontSize)}`);

    // 2. Enter as a guest, grant the test items.
    await page.getByRole('button', { name: GUEST }).first().click({ timeout: 120000 });
    await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 60000 });
    await page.waitForFunction(() => !!globalThis.nosumQa, null, { timeout: 60000 });
    await page.waitForTimeout(3000);
    for (const [id, qty, coins] of GRANTS) {
      await page.evaluate(([i, q, c]) => globalThis.nosumQa.grant(i, q, c), [id, qty, coins]);
      await page.waitForTimeout(400);
    }
    await page.waitForTimeout(1200);
    await shot('02-hud.png');
    await log('hud');

    // 3. Inventory with a multi-line tooltip hovered (boots, then the tonic).
    await page.keyboard.press('KeyI');
    await page.waitForTimeout(800);
    await shot('03-inventory.png');
    await log('inventory');
    const tips = await page.evaluate(() => {
      const found = [...document.querySelectorAll('.trade-window [data-tip]')].filter((e) => (e.getAttribute('data-tip') || '').includes('\n'));
      found.forEach((e, i) => e.setAttribute('data-qa-tip', String(i)));
      return found.map((e) => (e.getAttribute('data-tip') || '').split('\n')[0]).slice(0, 6);
    });
    for (let i = 0; i < Math.min(tips.length, 3); i++) {
      const target = page.locator(`[data-qa-tip="${i}"]`);
      await target.scrollIntoViewIfNeeded();
      await target.hover({ force: true });
      await page.waitForTimeout(500);
      const slug = tips[i].replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '').toLowerCase();
      await shot(`04-tooltip-${String(i + 1).padStart(2, '0')}-${slug}.png`);
      await log(`tooltip ${tips[i]}`);
    }
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);

    // 4. Settings (H).
    await page.keyboard.press('KeyH');
    await page.waitForTimeout(800);
    await shot('05-settings.png');
    await log('settings');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);

    // 5. Merchant dialogue box (the seed keeper): close any open panel, stand on the counter, press E.
    for (let i = 0; i < 2; i++) { await page.keyboard.press('Escape'); await page.waitForTimeout(400); }
    await page.evaluate(([x, y]) => globalThis.nosumQa.teleport('town', x, y), MERCHANT_TILE);
    await page.waitForTimeout(2000);
    await page.keyboard.press('KeyE');
    await page.waitForTimeout(1200);
    await shot('06-merchant.png');
    await log('merchant');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);

    const real = errors.filter((e) => !/Could not load map|could not be decoded/.test(e));
    report.push(`${name} | errors: ${real.length ? real.slice(0, 3).join(' ; ') : 'none'}`);
    await context.close();
  }
  await browser.close();
  fs.mkdirSync(outRoot, { recursive: true });
  fs.writeFileSync(path.join(outRoot, `audit-${phase}.txt`), report.join('\n') + '\n');
  console.log(report.join('\n'));
})().catch((e) => { console.error(e); process.exit(1); });
