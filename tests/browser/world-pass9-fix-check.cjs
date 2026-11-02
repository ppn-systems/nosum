// World QA pass 9 fix checks (docs/qa/world-pass9, issues 4, 5 and 13): asserts the HUD geometry the fixes set, in the running Development tester build.
// usage: node tests/browser/world-pass9-fix-check.cjs   (env: NOSUM_QA_URL, default http://localhost:5080/?qa=1)
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
  return { browser, page };
}

// The rectangle of a selector as plain numbers, or null when it is not drawn.
const rect = (page, sel) => page.evaluate(s => {
  const e = document.querySelector(s);
  if (!e) return null;
  const cs = getComputedStyle(e);
  if (cs.display === 'none') return null;
  const r = e.getBoundingClientRect();
  return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height };
}, sel);
const overlaps = (a, b) => a && b && a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;

(async () => {
  // 4. A window at 1920 keeps the minimap and the menu column beside it (the window is centred, clear of the top-right corner).
  {
    const { browser, page } = await open('en', '1920x1080');
    await page.keyboard.press('KeyI');
    await wait(1200);
    const win = await rect(page, '.game-hud .hud-center .game-panel');
    const mini = await rect(page, '.hud-minimap');
    const col = await rect(page, '.hud-menu-column');
    check('1920 en: minimap card stays drawn beside an open window', mini !== null && !overlaps(win, mini), JSON.stringify({ win, mini }));
    check('1920 en: menu column stays beside an open window', col !== null && !overlaps(win, col), JSON.stringify({ col }));
    await browser.close();
  }
  // 4b. Below 1600 a window covers the minimap, as before.
  {
    const { browser, page } = await open('en', '1280x720');
    await page.keyboard.press('KeyI');
    await wait(1200);
    const mini = await rect(page, '.hud-minimap');
    check('1280 en: an open window hides the minimap card', mini === null, JSON.stringify({ mini }));
    await browser.close();
  }
  // 13. The minimap card's right edge is the top-right row's right edge at 1920 (the same gutter), not 10 px inside it.
  {
    const { browser, page } = await open('en', '1920x1080');
    await wait(500);
    const bar = await rect(page, '.hud-top-right .hud-bar:first-child');
    const mini = await rect(page, '.hud-minimap');
    const col = await rect(page, '.hud-menu-column');
    // The card is drawn one unit past the row's edge (its margin-right is -1 u, HudMinimap.razor.css), so its wood, which has a transparent
    // unit on its outer side, ends on the row's edge. The gap must equal that margin.
    const margin = await page.evaluate(() => parseFloat(getComputedStyle(document.querySelector('.hud-minimap')).marginRight));
    const gap = bar && mini ? Math.round(bar.right - mini.right) : null;
    check('1920 en: minimap card is drawn one unit past the top-right row (its wood meets the row edge)', gap !== null && Math.abs(gap - margin) <= 1 && margin < 0, `row right - card right = ${gap}px, margin-right = ${margin}px`);
    const colGap = bar && col ? Math.round(bar.right - col.right) : null;
    check('1920 en: menu column shares the top-right row\'s right edge', colGap !== null && Math.abs(colGap) <= 1, `row right - column right = ${colGap}px`);
    await browser.close();
  }
  // 5. At 1920 the emote button (bottom right) does not sit under the menu column.
  {
    const { browser, page } = await open('en', '1920x1080');
    await wait(500);
    const emote = await rect(page, '.hud-bottom-right .hud-cell');
    const col = await rect(page, '.hud-menu-column');
    check('1920 en: emote button is clear of the menu column', !overlaps(emote, col), JSON.stringify({ emote, col }));
    await browser.close();
  }
  // 6. The wardrobe's two parts (look, clothes) fit their window with no inner scroll (issue 6). The tailor's dialog opens it.
  const NPC = { vi: /^Thay đồ$/, en: /^Change clothes$/ }, CLOTHES = { vi: /^Trang phục$/, en: /^Clothes$/ };
  for (const size of ['1280x720', '1920x1080']) {
    for (const lang of ['vi', 'en']) {
      const { browser, page } = await open(lang, size);
      await page.evaluate(() => window.nosumQa.teleport('town', 20, 89));
      await wait(2500);
      await page.keyboard.press('KeyE');
      await wait(1000);
      await page.getByRole('button', { name: NPC[lang] }).first().click({ timeout: 10000 }).catch(() => {});
      await wait(1200);
      await page.getByRole('button', { name: CLOTHES[lang] }).first().click({ timeout: 10000 }).catch(() => {});
      await wait(800);
      const fit = await page.evaluate(() => {
        const part = document.querySelector('.ward-part.active');
        const body = document.querySelector('.ward .menu-page-body') || document.querySelector('.ward')?.closest('.menu-page-body');
        if (!part || !body) return null;
        return { partH: part.scrollHeight, partClientH: part.clientHeight, bodyH: body.scrollHeight, bodyClientH: body.clientHeight, worn: part.querySelectorAll('button.slot').length };
      });
      check(`${size} ${lang}: wardrobe clothes part fits without an inner scroll`, fit !== null && fit.bodyH <= fit.bodyClientH + 1 && fit.worn >= 4, JSON.stringify(fit));
      await browser.close();
    }
  }
  const failed = results.filter(ok => !ok).length;
  console.log(`\n${results.length - failed} of ${results.length} checks ok`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('CHECK ERROR', e.message); process.exit(1); });
