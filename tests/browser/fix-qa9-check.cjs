// QA pass 9 fix checks (docs/qa/fix-qa9): asserts the layout rules the fixes added, in the running Development tester build.
// usage: node tests/browser/fix-qa9-check.cjs   (env: NOSUM_QA_URL, default http://localhost:5080/?qa=1)
// Each check prints ok or FAIL; the process exits 1 when any check fails.
const { chromium } = require('playwright');
const wait = ms => new Promise(r => setTimeout(r, ms));
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const LANG = { vi: { code: 1, tag: 'vi-VN' }, en: { code: 2, tag: 'en-US' } };
const VIEWPORTS = { '1280x720': [1280, 720], '1920x1080': [1920, 1080], '390x844': [390, 844] };
const results = [];
const check = (name, ok, detail = '') => { results.push(ok); console.log(`${ok ? 'ok  ' : 'FAIL'} ${name} ${detail}`); };

async function open(lang, size) {
  const [w, h] = VIEWPORTS[size];
  const touch = w < 1024;
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: w, height: h }, locale: LANG[lang].tag, hasTouch: touch, isMobile: touch });
  await context.addInitScript(code => { try { localStorage.setItem('nosum.settings.v1', `lang=${code}\n`); } catch (e) {} }, LANG[lang].code);
  const page = await context.newPage();
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await wait(1000);
  await page.getByRole('button', { name: /khách|guest/i }).first().click({ timeout: 120000 });
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 120000 });
  await wait(3000);
  return { browser, context, page };
}

(async () => {
  // 1. The top-right row is one row at 390 wide, in English (the longest calendar text).
  {
    const { browser, page } = await open('en', '390x844');
    const tops = await page.evaluate(() => [...document.querySelectorAll('.hud-bar:first-child > *')].map(e => Math.round(e.getBoundingClientRect().top)));
    // The row is one row by its rules, whatever the clock reads: no wrap, and nothing wider than the bar (a longer time or coin count must not wrap the gear).
    const row = await page.evaluate(() => {
      const bar = document.querySelector('.hud-bar:first-child');
      if (!bar) return null;
      return { wrap: getComputedStyle(bar).flexWrap, overflows: bar.scrollWidth > bar.clientWidth + 1, tops: [...bar.children].map(e => Math.round(e.getBoundingClientRect().top)) };
    });
    check('390 en: top-right row is one row (no wrap, no overflow)', row !== null && row.wrap === 'nowrap' && !row.overflows && Math.max(...row.tops) - Math.min(...row.tops) <= 4, JSON.stringify(row));
    // 4. The quest scroll keeps its size and the title starts after it (the first letter is not under the icon).
    const gap = await page.evaluate(() => {
      const icon = document.querySelector('.quest-head .quest-scroll');
      const title = document.querySelector('.quest-head .quest-title');
      if (!icon || !title) return null;
      return Math.round(title.getBoundingClientRect().left - icon.getBoundingClientRect().right);
    });
    check('390 en: quest title starts after the scroll icon', gap !== null && gap >= 0, `gap ${gap}px`);
    // 3 (phone). The hint, when shown, is in the bottom-centre toast line above the hotbar.
    const hint = await page.evaluate(() => {
      const h = document.querySelector('.onboarding-hint');
      const bar = document.querySelector('.hotbar-dock');
      if (!h || !bar) return null;
      const r = h.getBoundingClientRect();
      return { bottom: Math.round(r.bottom), barTop: Math.round(bar.getBoundingClientRect().top), vh: innerHeight };
    });
    check('390 en: hint sits above the hotbar in the toast line', hint === null || hint.bottom <= hint.barTop, JSON.stringify(hint));
    await browser.close();
  }
  // 3. The hint in the toast line at 1280 (vi and en): above the hotbar and in the lower band of the screen.
  for (const lang of ['vi', 'en']) {
    const { browser, page } = await open(lang, '1280x720');
    const hint = await page.evaluate(() => {
      const h = document.querySelector('.onboarding-hint');
      if (!h) return null;
      const r = h.getBoundingClientRect();
      const bar = document.querySelector('.hotbar-dock');
      return { top: Math.round(r.top), bottom: Math.round(r.bottom), barTop: bar ? Math.round(bar.getBoundingClientRect().top) : null, vh: innerHeight };
    });
    check(`1280 ${lang}: hint is in the bottom toast line`, hint !== null && hint.barTop !== null && hint.bottom <= hint.barTop && hint.top > hint.vh * 0.5, JSON.stringify(hint));
    await browser.close();
  }
  // 2. At 1920 a window keeps the menu column beside it; below 1600 it is hidden by the window (unchanged).
  {
    const { browser, page } = await open('en', '1920x1080');
    await page.keyboard.press('KeyI');
    await wait(1200);
    const col = await page.evaluate(() => {
      const c = document.querySelector('.hud-menu-column');
      if (!c) return null;
      const r = c.getBoundingClientRect();
      return { width: Math.round(r.width), right: Math.round(r.right), vw: innerWidth };
    });
    check('1920 en: menu column stays beside an open window', col !== null && col.width > 0 && col.right > col.vw - 200, JSON.stringify(col));
    await browser.close();
  }
  const failed = results.filter(r => !r).length;
  console.log(failed ? `${failed} check(s) FAILED` : `all ${results.length} checks ok`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('FAIL', e.message); process.exit(1); });
