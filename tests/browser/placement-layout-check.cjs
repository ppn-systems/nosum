// Placement layout check (docs/design/ui-standard.md section 7, checklist items 1, 2 and 7). With the furniture panel open in the farmhouse (a granted
// chair held in slot 9), it measures the boxes of the panel, the top-right column, the toast line and the hotbar at 1280x720, 1920x1080 and
// 390x844, and prints every overlapping pair. It also checks that the panel stays out of the room's centre (the middle of the viewport) and that
// the panel has no inner scroll at 1280x720. Needs the same server and client as placement-capture.cjs.
// usage: node tests/browser/placement-layout-check.cjs [vi|en]   (default vi). Exits 1 when a check fails.

const { chromium } = require('playwright');

const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const VIEWPORTS = { desktop: { width: 1280, height: 720 }, wide: { width: 1920, height: 1080 }, phone: { width: 390, height: 844 } };

// Returns the boxes that must not overlap each other, as { name, x, y, w, h } in CSS pixels; empty boxes are dropped.
async function boxes(page) {
  return page.evaluate(() => {
    const pick = (label, selector) => {
      const el = document.querySelector(selector);
      if (!el) return null;
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) return null;
      return { name: label, x: r.left, y: r.top, w: r.width, h: r.height };
    };
    const list = [
      pick('furniture panel', '.furniture-dock .game-panel'),
      pick('top-right bar', '.hud-top-right .hud-bar'),
      pick('minimap', '.hud-top-right .hud-minimap'),
      pick('menu column', '.hud-top-right .hud-menu-column'),
      pick('toast line', '.hud-bottom-center .game-message'),
      pick('hotbar', '.hud-bottom-center .hotbar-slots'),
    ];
    return list.filter(Boolean);
  });
}

function overlap(a, b) {
  const x = Math.max(0, Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x));
  const y = Math.max(0, Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y));
  return x > 0.5 && y > 0.5;
}

async function check(browser, vp, lang) {
  const size = VIEWPORTS[vp];
  const phone = vp === 'phone';
  const context = await browser.newContext({ viewport: size, locale: lang === 'en' ? 'en-US' : 'vi-VN', hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  await context.addInitScript((code) => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + code + '\n'); } catch (e) { /* no storage */ } }, lang === 'en' ? 2 : 1);
  const page = await context.newPage();
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await page.waitForTimeout(3000);
  await page.evaluate(async () => {
    const ex = await (await globalThis.getDotnetRuntime(0)).getAssemblyExports('Nosum.Wasm.dll');
    ex.Nosum.Wasm.Game.DevTools.Grant(300, 5, 0);
  });
  await page.waitForTimeout(800);
  await page.evaluate(() => window.nosumQa?.teleport('farm', 64, 22));
  await page.waitForTimeout(3000);
  await page.keyboard.press('9');
  await page.waitForTimeout(700);

  const failures = [];
  const panel = await page.evaluate(() => {
    const el = document.querySelector('.furniture-dock .trade-window');
    if (!el) return null;
    const r = el.getBoundingClientRect();
    const body = el.querySelector('.game-panel-body');
    return { x: r.left, y: r.top, w: r.width, h: r.height, scrolls: body ? body.scrollHeight > body.clientHeight + 1 : false };
  });
  if (!panel) {
    failures.push('the furniture panel is not shown (the chair is not held, or the farm is not a place to arrange)');
  } else {
    const cx = size.width / 2;
    const cy = size.height / 2;
    const overCentre = panel.x < cx && panel.x + panel.w > cx && panel.y < cy && panel.y + panel.h > cy;
    if (overCentre) failures.push('the panel covers the centre of the room');
    if (vp === 'desktop' && panel.scrolls) failures.push('the panel scrolls at 1280x720 (checklist item 7)');
  }

  const all = await boxes(page);
  for (let i = 0; i < all.length; i++) {
    for (let j = i + 1; j < all.length; j++) {
      if (overlap(all[i], all[j])) failures.push(`${all[i].name} overlaps ${all[j].name}`);
    }
  }
  console.log(`${vp}-${lang}: ${failures.length === 0 ? 'PASS' : 'FAIL'}${panel ? ` (panel ${Math.round(panel.x)},${Math.round(panel.y)} ${Math.round(panel.w)}x${Math.round(panel.h)})` : ''}`);
  for (const f of failures) console.log(`  - ${f}`);
  await context.close();
  return failures.length;
}

(async () => {
  const lang = process.argv[2] || 'vi';
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  let failed = 0;
  for (const vp of Object.keys(VIEWPORTS)) failed += await check(browser, vp, lang);
  await browser.close();
  process.exit(failed === 0 ? 0 : 1);
})().catch((e) => { console.error(e); process.exit(1); });
