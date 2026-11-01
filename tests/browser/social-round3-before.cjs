// Social round 3, before: what a player sees on the friends page of a guest, on the master client (docs/qa/social-round3/README.md).
// Run with the Development host on :57300 and a client built from origin/master (plus the renderer module fix, see the README):
//   BASE=http://localhost:5081/ OUT_DIR=docs/qa/social-round3/before NODE_PATH=$(npm root -g) node tests/browser/social-round3-before.cjs
const { chromium, devices } = require('playwright'); const fs = require('fs'); const path = require('path');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const base = process.env.BASE || 'http://localhost:5080/';
const OUT = process.env.OUT_DIR || path.join(__dirname, '..', '..', 'docs', 'qa', 'social-round3', 'before');
fs.mkdirSync(OUT, { recursive: true });
const SIZES = {
  '1280x720': { viewport: { width: 1280, height: 720 } },
  '1920x1080': { viewport: { width: 1920, height: 1080 } },
  '390x844': { ...devices['iPhone 14'], viewport: { width: 390, height: 844 } },
};
const NAMES = { vi: { guest: /khách|guest/i, friends: /Bạn bè/, menu: /^Menu$/ }, en: { guest: /khách|guest/i, friends: /^Friends$/, menu: /^Menu$/ } };
(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const code of ['vi', 'en']) for (const [size, opts] of Object.entries(SIZES)) {
    const ctx = await browser.newContext({ ...opts, locale: code === 'vi' ? 'vi-VN' : 'en-US' });
    await ctx.addInitScript(l => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); } catch {} }, code === 'vi' ? 1 : 2);
    const page = await ctx.newPage();
    await page.goto(base + '?qa=1', { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 90000 }); await sleep(2500);
    await page.getByRole('button', { name: NAMES[code].guest }).first().click();
    await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 60000 }).catch(() => {});
    await sleep(4000);
    if (size.startsWith('390')) { await page.getByRole('button', { name: NAMES[code].menu }).first().dispatchEvent('click', null, { timeout: 5000 }).catch(() => {}); await sleep(700); }
    await page.getByRole('button', { name: NAMES[code].friends }).first().dispatchEvent('click', null, { timeout: 5000 }).catch(() => {});
    await sleep(1000);
    await page.screenshot({ path: path.join(OUT, `${code}-${size}-friends-guest.png`) });
    console.log('ok', code, size);
    await ctx.close();
  }
  await browser.close();
})().catch(e => { console.error('BEFORE FAIL', e.message); process.exit(1); });
