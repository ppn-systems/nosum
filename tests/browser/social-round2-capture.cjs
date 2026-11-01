// Social round 2 captures (docs/design/critique-social.md; grading in docs/qa/social-round2/README.md).
// Run from the repo root with the Development host on :57300 and a Development WASM client built with -p:NosumDevTools=true:
//   LABEL=after BASE=http://localhost:5080/ OUT_DIR=docs/qa/social-round2/after NODE_PATH=$(npm root -g) node tests/browser/social-round2-capture.cjs
// Frames per size (1280x720, 1920x1080, 390x844 iPhone 14 profile) and language (vi, en):
//   chat-open (Enter on desktop, the Chat button on a phone), trade-invite-receiver, trade-open-sender.
// Two guests play in the Town (QA teleport). Every step is logged to capture-log.txt in OUT_DIR.
const { chromium, devices } = require('playwright'); const fs = require('fs'); const path = require('path');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const base = process.env.BASE || 'http://localhost:5080/';
const OUT = process.env.OUT_DIR || path.join(__dirname, '..', '..', 'docs', 'qa', 'social-round2', process.env.LABEL || 'run');
fs.mkdirSync(OUT, { recursive: true });
const log = [];
const L = {
  vi: { trade: /Giao dịch/, invite: /Mời/, accept: /Đồng ý/, menu: /^Menu$/, chat: /chat|Trò chuyện/i },
  en: { trade: /^Trade/, invite: /Invite/, accept: /^Accept$/, menu: /^Menu$/, chat: /chat/i },
};
async function guest(browser, opts, code) {
  const ctx = await browser.newContext({ ...opts, locale: code === 'vi' ? 'vi-VN' : 'en-US' });
  await ctx.addInitScript(l => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); } catch {} }, code === 'vi' ? 1 : 2);
  const page = await ctx.newPage();
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 }); await sleep(2500);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 60000 }).catch(() => {});
  await sleep(4000); return page;
}
async function step(name, fn) { try { await fn(); log.push('ok   ' + name); } catch (e) { log.push('FAIL ' + name + ': ' + e.message.split('\n')[0]); } }
const sizes = {
  '1280x720': { viewport: { width: 1280, height: 720 } },
  '1920x1080': { viewport: { width: 1920, height: 1080 } },
  '390x844': { ...devices['iPhone 14'], viewport: { width: 390, height: 844 } },
};
(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const code of ['vi', 'en']) {
    for (const [size, opts] of Object.entries(sizes)) {
      const t = L[code], p = `${code}-${size}-`, phone = size.startsWith('390');
      const a = await guest(browser, opts, code);
      const b = await guest(browser, { viewport: { width: 1280, height: 720 } }, code);
      await a.evaluate(() => window.nosumQa.teleport('town', 20, 20)); await b.evaluate(() => window.nosumQa.teleport('town', 22, 21)); await sleep(4000);
      await a.bringToFront();
      await step(p + 'chat-open', async () => {
        if (phone) { await a.getByRole('button', { name: t.chat }).first().tap({ timeout: 3000 }); }
        else { await a.keyboard.press('Enter'); }
        await sleep(900);
        const opened = await a.locator('#nosum-chat-input').count();
        log.push('     chat input open = ' + (opened === 1));
        await a.screenshot({ path: path.join(OUT, p + 'chat-open.png') });
        await a.keyboard.press('Escape'); await sleep(400);
      });
      await step(p + 'trade', async () => {
        if (phone) { await a.getByRole('button', { name: t.menu }).first().click({ timeout: 5000 }); await sleep(600); }
        await a.getByRole('button', { name: t.trade }).first().click({ timeout: 5000 }); await sleep(900);
        await a.getByRole('button', { name: t.invite }).first().click({ timeout: 5000 }); await sleep(1200);
        await b.screenshot({ path: path.join(OUT, p + 'trade-invite-receiver.png') });
        await b.getByRole('button', { name: t.accept }).first().click({ timeout: 5000 }); await sleep(1500);
        await a.screenshot({ path: path.join(OUT, p + 'trade-open-sender.png') });
      });
      await a.context().close(); await b.context().close();
    }
  }
  fs.writeFileSync(path.join(OUT, 'capture-log.txt'), log.join('\n') + '\n');
  console.log(log.join('\n'));
  await browser.close();
})().catch(e => { console.error('CAPTURE FAIL', e.message); process.exit(1); });
