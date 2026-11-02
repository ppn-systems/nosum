// World QA pass 10 fix checks (docs/qa/world-pass10, issues 9, 22 and 23): asserts the geometry the fixes set, in the running Development tester build.
// usage: node tests/browser/world-pass10-fix-check.cjs   (env: NOSUM_QA_URL, default http://localhost:5080/?qa=1)
// Each check prints ok or FAIL; the process exits 1 when any check fails. The trade, co-op and friends windows (issue 21) are checked by the
// social harness (tests/browser/social-round3-play.cjs) and the fit check (tests/browser/menu-fit-check.js), which need a second player.
const { chromium } = require('playwright');
const wait = ms => new Promise(r => setTimeout(r, ms));
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const LANG = { vi: { code: 1, tag: 'vi-VN' }, en: { code: 2, tag: 'en-US' } };
const results = [];
const check = (name, ok, detail = '') => { results.push(ok); console.log(`${ok ? 'ok  ' : 'FAIL'} ${name} ${detail}`); };

async function open(lang, width, height) {
  const touch = width < 1024;
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width, height }, locale: LANG[lang].tag, hasTouch: touch, isMobile: touch });
  await context.addInitScript(code => { try { localStorage.setItem('nosum.settings.v1', `lang=${code}\n`); } catch (e) {} }, LANG[lang].code);
  const page = await context.newPage();
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await wait(1000);
  await page.getByRole('button', { name: /khách|guest/i }).first().click({ timeout: 120000 });
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 120000 });
  await wait(3000);
  await page.evaluate(() => window.nosumQa.springReset());
  await wait(1500);
  return { browser, page };
}

// The rectangle of a selector as plain numbers, or null when it is not drawn.
const rect = (page, sel) => page.evaluate(s => {
  const e = document.querySelector(s);
  if (!e) return null;
  if (getComputedStyle(e).display === 'none') return null;
  const r = e.getBoundingClientRect();
  return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height };
}, sel);
const overlaps = (a, b) => a && b && a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;

(async () => {
  // 22. The emote cell stands clear of the menu column at every desktop height, including a 1080p monitor less its browser chrome.
  {
    const { browser, page } = await open('en', 1920, 1080);
    const sizes = [[1280, 720], [1366, 700], [1536, 864], [1920, 840], [1920, 857], [1920, 1080]];
    for (const [w, h] of sizes) {
      await page.setViewportSize({ width: w, height: h });
      await wait(600);
      const emote = await rect(page, '.hud-bottom-right .game-icon-button, .hud-bottom-right button');
      const column = await page.evaluate(() => {
        const items = [...document.querySelectorAll('.hud-menu-column .hud-menu-item')].filter(el => el.offsetParent !== null);
        const el = items[items.length - 1];
        if (!el) return null;
        const r = el.getBoundingClientRect();
        return { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
      });
      check(`${w}x${h} en: emote cell clear of the menu column`, emote !== null && column !== null && !overlaps(emote, column), JSON.stringify({ emote, column }));
    }
    await browser.close();
  }

  // 22. A window in the centre takes the chat line away, and the chat comes back when it closes (1280x720).
  {
    const { browser, page } = await open('en', 1280, 720);
    const before = await rect(page, '.hud-bottom-left');
    await page.keyboard.press('KeyI');
    await wait(1000);
    const during = await rect(page, '.hud-bottom-left');
    await page.keyboard.press('Escape');
    await wait(800);
    const after = await rect(page, '.hud-bottom-left');
    check('1280 en: the chat line is drawn with no window open', before !== null, JSON.stringify({ before }));
    check('1280 en: the chat line steps aside while a window is open', during === null, JSON.stringify({ during }));
    check('1280 en: the chat line comes back after the window closes', after !== null, JSON.stringify({ after }));
    await browser.close();
  }

  // 9. The phone quest card: the first letter of the title is clear of the scroll icon, for the longest English titles.
  {
    const { browser, page } = await open('en', 390, 844);
    const titles = ['Till the soil', 'Defeat three monsters', 'Craft a fishing rod', 'Fill a Town request', 'Mine copper ore'];
    for (const title of titles) {
      // A probe of the card's text only: the first character's box against the icon's box.
      await page.evaluate(text => { const el = document.querySelector('.quest-title'); if (el) el.textContent = text; }, title);
      await wait(150);
      const geo = await page.evaluate(() => {
        const title = document.querySelector('.quest-title');
        const icon = document.querySelector('.quest-head .game-icon.quest-scroll');
        if (!title || !icon || !title.firstChild) return null;
        const range = document.createRange();
        range.setStart(title.firstChild, 0);
        range.setEnd(title.firstChild, 1);
        const glyph = range.getBoundingClientRect();
        const iconBox = icon.getBoundingClientRect();
        return { glyphLeft: Math.round(glyph.left), glyphRight: Math.round(glyph.right), iconRight: Math.round(iconBox.right) };
      });
      check(`390 en quest "${title}": first letter clear of the scroll icon`, geo !== null && geo.glyphLeft >= geo.iconRight, JSON.stringify(geo));
    }
    await browser.close();
  }

  // 23. The menu's tiles: each name is one line (a word is never left alone), and the hotkey is not a line of its own.
  for (const [w, h] of [[390, 844], [1280, 720]]) {
    const { browser, page } = await open('en', w, h);
    await page.locator('.hud-top-right .hud-menu-button').first().click({ timeout: 10000 });
    await wait(900);
    const tiles = await page.evaluate(() => [...document.querySelectorAll('.game-menu-tile')].map(t => {
      const name = t.querySelector('.game-menu-tile-name');
      const key = t.querySelector('.game-menu-tile-key');
      const lh = parseFloat(getComputedStyle(name).lineHeight) || 17;
      return { label: name.textContent.trim(), lines: Math.round(name.getBoundingClientRect().height / lh), key: key ? key.textContent.trim() : null, keyTop: key ? Math.round(key.getBoundingClientRect().top - name.getBoundingClientRect().bottom) : null };
    }));
    check(`${w}x${h} en: the menu has tiles`, tiles.length > 0, JSON.stringify(tiles.length));
    check(`${w}x${h} en: every tile name is one line`, tiles.every(t => t.lines <= 1), JSON.stringify(tiles.filter(t => t.lines > 1)));
    const profile = tiles.find(t => t.label === 'Profile');
    check(`${w}x${h} en: the Profile tile has its hotkey as a corner badge`, profile !== undefined && profile.key === 'N' && profile.keyTop <= 0, JSON.stringify(profile));
    await browser.close();
  }

  const failed = results.filter(ok => !ok).length;
  console.log(`== world pass 10 fix checks: ${results.length - failed} of ${results.length} ok`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('CHECK FAIL', e.message); process.exit(1); });
