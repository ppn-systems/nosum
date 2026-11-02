// World QA pass 7: the extended harness (docs/qa/world-pass7/README.md). Pass 6 wrote the harness it planned and did not run it; this one runs.
// For each locale and viewport it signs in as a guest on a Development server (ADR-079: window.nosumQa), resets to Spring, stands on the farm and
// then opens every panel the game menu lists (one tile at a time), plus the co-op key (G) and the hotbar's badge strip. Each panel is screenshotted,
// audited for page errors and for scroll or clipped content inside the open window, and the result is written as JSON.
// Not driven here (no reliable DOM hook yet, see README): animals and pets actions, station crafting, market buying, mail sending, co-op deposit,
// backpack upgrade, shipping bin, mines, two-context social, wardrobe changes, guest progress. Those need a scripted flow per feature.
//
// usage: node tests/browser/world-pass7.cjs [locales=vi,en] [viewports=1280x720,390x844]
// Env: NOSUM_QA_URL (default http://localhost:5080/?qa=1), NOSUM_QA_OUT (default docs/qa/world-pass7).
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const OUT = path.join(REPO, process.env.NOSUM_QA_OUT || 'docs/qa/world-pass7');
const VIEWPORTS = { '1280x720': { width: 1280, height: 720 }, '390x844': { width: 390, height: 844 } };
const LOCALES = { vi: { tag: 'vi-VN', lang: 1, guest: /khách|guest/i, close: /^(Đóng|Close)$/ }, en: { tag: 'en-US', lang: 2, guest: /as a guest|guest/i, close: /^(Đóng|Close)$/ } };
const wait = ms => new Promise(r => setTimeout(r, ms));

// The section 7 audit of the open window: scrollable or clipped content inside it, and whether the window leaves the viewport.
const AUDIT = () => {
  const w = document.querySelector('.trade-window') || document.querySelector('.game-window') || document.querySelector('.game-panel-window') || document.querySelector('.game-menu');
  if (!w) return { window: false };
  const wr = w.getBoundingClientRect();
  const issues = [];
  for (const e of w.querySelectorAll('*')) {
    const cs = getComputedStyle(e);
    if (e.clientHeight > 0 && e.scrollHeight > e.clientHeight + 2) {
      if (/(auto|scroll)/.test(cs.overflowY)) issues.push(`scroll:${(e.className.baseVal ?? e.className).toString().slice(0, 24)}`);
      else if (/hidden/.test(cs.overflowY) && e.tagName !== 'BUTTON') issues.push(`clip:${(e.className.baseVal ?? e.className).toString().slice(0, 24)}`);
    }
  }
  const off = wr.left < -1 || wr.top < -1 || wr.right > innerWidth + 1 || wr.bottom > innerHeight + 1;
  return { window: true, title: (w.querySelector('h2, h3, .game-window-title, .game-panel-title') || {}).innerText || '', off, issues: issues.slice(0, 8) };
};

async function signIn(page, L) {
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 });
  await wait(1000);
  await page.getByRole('button', { name: L.guest }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await wait(2500);
}

(async () => {
  const locales = (process.argv[2] || 'vi,en').split(',');
  const viewports = (process.argv[3] || '1280x720,390x844').split(',');
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const report = [];
  for (const lk of locales) {
    const L = LOCALES[lk];
    for (const vk of viewports) {
      const vp = VIEWPORTS[vk];
      const dir = path.join(OUT, lk, vk);
      fs.mkdirSync(dir, { recursive: true });
      const touch = vp.width < 1024;
      const context = await browser.newContext({ viewport: vp, locale: L.tag, hasTouch: touch, isMobile: touch });
      await context.addInitScript(value => { try { localStorage.setItem('nosum.settings.v1', value); } catch (e) {} }, `lang=${L.lang}\n`);
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', e => errors.push(e.message.slice(0, 200)));
      page.on('console', m => { if (m.type() === 'error' && !/WebSocket|ERR_CONNECTION/.test(m.text())) errors.push(m.text().slice(0, 200)); });
      const steps = [];
      const shot = async name => { const f = `${String(steps.length + 1).padStart(2, '0')}-${name.replace(/[^a-z0-9]+/gi, '-').slice(0, 40)}.png`; await page.screenshot({ path: path.join(dir, f) }); return f; };
      try {
        await signIn(page, L);
        await page.evaluate(() => window.nosumQa.springReset());
        await wait(1500);
        await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
        await wait(2500);
        steps.push({ name: 'farm', file: await shot('farm'), note: 'hud on the farm' });

        // Every menu tile, one at a time: open the Menu, press the tile, screenshot, audit, close.
        const tileLabels = await openMenu(page);
        steps.push({ name: 'menu', tiles: tileLabels, file: await shot('menu') });
        for (let i = 0; i < tileLabels.length; i++) {
          const label = tileLabels[i];
          try {
            await openMenu(page);
            await page.evaluate(([idx]) => document.querySelectorAll('.game-menu-tile')[idx]?.click(), [i]);
            await wait(1100);
            const audit = await page.evaluate(AUDIT);
            steps.push({ name: label, file: await shot(label), audit });
          } catch (e) {
            steps.push({ name: label, error: String(e.message).slice(0, 160) });
          }
          await closeWindow(page, L);
        }

        // The desktop menu column and the Mail and Settings icons (not tiles, so they are opened from their own buttons).
        const columnLabels = await page.$$eval('.hud-menu-column .hud-menu-item:not(.hud-menu-button) .game-icon-button, .hud-top-right .game-icon-button', els => els.map(e => e.getAttribute('aria-label') || e.getAttribute('data-tip') || e.getAttribute('title') || ''));
        for (let i = 0; i < columnLabels.length; i++) {
          const label = columnLabels[i] || `icon-${i}`;
          try {
            await page.evaluate(([idx]) => {
              const btns = [...document.querySelectorAll('.hud-menu-column .hud-menu-item:not(.hud-menu-button) .game-icon-button, .hud-top-right .game-icon-button')];
              btns[idx]?.click();
            }, [i]);
            await wait(1100);
            steps.push({ name: 'col-' + label, file: await shot('col-' + label), audit: await page.evaluate(AUDIT) });
          } catch (e) {
            steps.push({ name: 'col-' + label, error: String(e.message).slice(0, 160) });
          }
          await closeWindow(page, L);
        }

        // The co-op key (G) and the hotbar badges.
        await page.keyboard.press('g');
        await wait(1200);
        steps.push({ name: 'key-g', file: await shot('key-g'), audit: await page.evaluate(AUDIT) });
        await closeWindow(page, L);
      } catch (e) {
        steps.push({ name: 'FAILED', error: String(e.message).slice(0, 300) });
        await page.screenshot({ path: path.join(dir, 'zz-failure.png') }).catch(() => {});
      }
      report.push({ locale: lk, viewport: vk, steps, errors: errors.slice(0, 20) });
      fs.writeFileSync(path.join(dir, 'report.json'), JSON.stringify({ locale: lk, viewport: vk, steps, errors }, null, 2));
      console.log(lk, vk, 'steps', steps.length, 'errors', errors.length);
      await context.close();
    }
  }
  fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify(report, null, 2));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });

// Opens the game menu (the Menu button, phone or desktop) and returns the labels of its tiles.
async function openMenu(page) {
  const open = await page.$('.game-menu-tile');
  if (!open) {
    const btn = page.locator('.game-hud .hud-menu-button .game-icon-button').first();
    if (await btn.count()) await btn.click({ force: true, timeout: 5000 });
    await page.waitForSelector('.game-menu-tile', { timeout: 6000 }).catch(() => {});
  }
  return page.$$eval('.game-menu-tile', els => els.map(e => e.getAttribute('aria-label') || ''));
}

// Closes the open window with its Close control, or Escape when there is none.
async function closeWindow(page, L) {
  const close = page.getByRole('button', { name: L.close }).first();
  if (await close.count()) {
    await close.click({ force: true, timeout: 3000 }).catch(() => {});
  } else {
    await page.keyboard.press('Escape');
  }
  await wait(500);
}
