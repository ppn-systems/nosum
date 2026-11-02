// HUD check (world pass 8): the Menu button's name is shown on a phone only. On a desktop the label sat under the bottom icon of the menu column at
// 1280x720 ("Menu" overlapped the icon). Exit code 1 when the label's display is wrong at either size.
// usage: node tests/browser/hud-menu-label-check.cjs [url=http://localhost:5080/?qa=1]
const { chromium } = require('playwright');
const wait = ms => new Promise(r => setTimeout(r, ms));
const URL = process.argv[2] || 'http://localhost:5080/?qa=1';
const SIZES = [
  { name: '1280x720', viewport: { width: 1280, height: 720 }, touch: false, phone: false },
  { name: '390x844', viewport: { width: 390, height: 844 }, touch: true, phone: true },
];

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const failures = [];
  for (const size of SIZES) {
    const ctx = await browser.newContext({ viewport: size.viewport, locale: 'en-US', hasTouch: size.touch, isMobile: size.touch });
    await ctx.addInitScript(() => { try { localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch (e) {} });
    const page = await ctx.newPage();
    await page.goto(URL, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 90000 }); await wait(1000);
    await page.getByRole('button', { name: /as a guest|guest/i }).first().click();
    await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 }); await wait(2000);
    const shown = await page.evaluate(() => {
      const label = document.querySelector('.hud-menu-label');
      if (!label) return 'missing';
      return getComputedStyle(label).display !== 'none' ? 'shown' : 'hidden';
    });
    const want = size.phone ? 'shown' : 'hidden';
    console.log(size.name, 'Menu label', shown, '(want ' + want + ')');
    if (shown !== want) failures.push(`${size.name}: the Menu label is ${shown}, want ${want}`);
    await ctx.close();
  }
  await browser.close();
  console.log(failures.length ? 'FAIL ' + failures.join('; ') : 'OK the Menu label shows on a phone only');
  process.exit(failures.length ? 1 : 0);
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
