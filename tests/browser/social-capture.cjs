// Social-screen captures for the rebuild-social review (docs/design/critique-social.md; grading in docs/qa/rebuild-social/README.md).
// Run from the repo root with the host on :57300 and a Development WASM client built with -p:NosumDevTools=true:
//   LABEL=before BASE=http://localhost:5081/ OUT_DIR=docs/qa/rebuild-social/before NODE_PATH=$(npm root -g) node tests/browser/social-capture.cjs
//   (add ONLY_EXTRAS=1 to take just the Friends and co-op contribution frames)
// Two guests play in the Town (QA teleport) at 1280x720; one iPhone 14 profile at 390x844 plays next to a desktop guest. Every step is logged to capture-log.txt.
// Guests only: Development has no Google or Microsoft sign-in, so friends and visits to a friend's farm are not played here.
const { chromium, devices } = require('playwright'); const fs = require('fs'); const path = require('path');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const base = process.env.BASE || 'http://localhost:5080/';
async function guest(browser, opts, lang, qa) {
  const ctx = await browser.newContext(opts);
  await ctx.addInitScript(l => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); } catch {} }, lang);
  const page = await ctx.newPage(); page.errors = [];
  page.on('console', m => { if (m.type() === 'error') page.errors.push(m.text()); });
  page.on('pageerror', e => page.errors.push('pageerror: ' + e.message));
  await page.goto(base + (qa ? '?qa=1' : ''), { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 }); await page.waitForTimeout(2500);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 60000 });
  await page.waitForTimeout(5000);
  return page;
}


const LABEL = process.env.LABEL || 'run';
const OUT = process.env.OUT_DIR || path.join(__dirname, '..', '..', 'docs', 'qa', 'rebuild-social', LABEL);
fs.mkdirSync(OUT, { recursive: true });
const L = {
  vi: { deposit: /^Gửi vào$/, emotes: /Biểu cảm/, trade: /Giao dịch/, friends: /Bạn bè/, coop: /Hợp tác xã/, mail: /^Thư/, invite: /Mời|Invite/, cancel: /Hủy giao dịch/, found: /^Lập/, accept: /Đồng ý/, menu: /^Menu$/ },
  en: { deposit: /^Deposit$/, emotes: /^Emotes/, trade: /^Trade/, friends: /^Friends$/, coop: /^Co-op$/, mail: /^Mail$/, invite: /Mời|Invite/, cancel: /Cancel trade/, found: /^Found/, accept: /^Accept$/, menu: /^Menu$/ },
};
const log = [];
async function step(name, fn) { try { await fn(); log.push('ok   ' + name); } catch (e) { log.push('FAIL ' + name + ': ' + e.message.split('\n')[0]); } }
async function snap(page, name) { await page.screenshot({ path: path.join(OUT, name + '.png') }); }
async function clickRole(page, re) { await page.getByRole('button', { name: re }).first().click({ timeout: 5000 }); }
async function desktop(browser, code) {
  const t = L[code];
  const o = { viewport: { width: 1280, height: 720 }, locale: code === 'vi' ? 'vi-VN' : 'en-US' };
  const a = await guest(browser, o, code === 'vi' ? 1 : 2, true);
  const b = await guest(browser, o, code === 'vi' ? 1 : 2, true);
  const p = code + '-1280x720-';
  await a.evaluate(() => window.nosumQa.teleport('town', 20, 20)); await b.evaluate(() => window.nosumQa.teleport('town', 22, 21)); await sleep(4000);
  await step(p + 'town-hud', () => snap(a, p + 'town-hud'));
  await step(p + 'chat-enter', async () => { await a.keyboard.press('Enter'); await sleep(900); await snap(a, p + 'chat-open'); await a.keyboard.press('Escape'); await sleep(400); });
  await step(p + 'emote-wheel', async () => { await a.keyboard.press('b'); await sleep(700); await snap(a, p + 'emote-wheel'); await a.keyboard.press('b'); await sleep(400); });
  await step(p + 'friends', async () => { await clickRole(a, t.friends); await sleep(900); await snap(a, p + 'friends'); await a.keyboard.press('Escape'); await sleep(400); });
  await step(p + 'mail', async () => { await clickRole(a, t.mail); await sleep(900); await snap(a, p + 'mail'); await a.keyboard.press('Escape'); await sleep(400); });
  await step(p + 'trade-pick', async () => { await clickRole(a, t.trade); await sleep(900); await snap(a, p + 'trade-pick'); });
  await step(p + 'trade-invite', async () => { await a.getByRole('button', { name: t.invite }).first().click({ timeout: 5000 }); await sleep(1200); await snap(b, p + 'trade-invite-receiver'); });
  await step(p + 'trade-accept', async () => { await clickRole(b, t.accept); await sleep(1200); await snap(b, p + 'trade-open-receiver'); await snap(a, p + 'trade-open-sender'); });
  await step(p + 'trade-cancel', async () => { await clickRole(a, t.cancel); await sleep(800); await a.keyboard.press('Escape'); await sleep(400); });
  await step(p + 'coop-board', async () => { await a.evaluate(() => window.nosumQa.teleport('town', 42, 59)); await sleep(3500); await a.keyboard.press('e'); await sleep(4500); await a.getByText(t.coop).first().click({ timeout: 5000 }); await sleep(1200); await snap(a, p + 'coop-none'); });
  await step(p + 'coop-found', async () => { await a.locator('input').first().fill('Test Co-op'); await clickRole(a, t.found); await sleep(1500); await snap(a, p + 'coop-founded'); });
  await step(p + 'coop-invite', async () => { await b.evaluate(() => window.nosumQa.teleport('town', 44, 60)); await sleep(2500); await a.getByRole('button', { name: t.invite }).first().click({ timeout: 5000 }); await sleep(1500); await snap(b, p + 'coop-invite-receiver'); });
  await step(p + 'coop-accept', async () => { await clickRole(b, t.accept); await sleep(1500); await snap(b, p + 'coop-joined-receiver'); });
  for (const page of [a, b]) { if (page.errors.length) { log.push('console errors: ' + JSON.stringify(page.errors)); } }
  await a.context().close(); await b.context().close();
}
async function closePanel(page) {
  // On a phone Escape opens the inventory instead of closing the page: close the open page with its own button.
  await page.getByRole('button', { name: /^Close$|^Đóng$/ }).first().dispatchEvent('click', null, { timeout: 3000 }).catch(() => {});
  await sleep(500);
}
async function phone(browser, code) {
  const t = L[code];
  const c = await guest(browser, { ...devices['iPhone 14'], viewport: { width: 390, height: 844 }, locale: code === 'vi' ? 'vi-VN' : 'en-US' }, code === 'vi' ? 1 : 2, true);
  const b = await guest(browser, { viewport: { width: 1280, height: 720 }, locale: 'en-US' }, 2, true);
  const p = code + '-390x844-';
  await c.evaluate(() => window.nosumQa.teleport('town', 20, 20)); await b.evaluate(() => window.nosumQa.teleport('town', 22, 21)); await sleep(4000);
  await step(p + 'town-hud', () => snap(c, p + 'town-hud'));
  await step(p + 'touch-buttons', async () => { log.push('     touch buttons: ' + JSON.stringify(await c.locator('[data-touch-edge], .touch-controls button, .touch-run, .touch-use').allInnerTexts())); });
  await step(p + 'chat-tap', async () => {
    const btn = c.locator('.touch-chat');
    const n = await btn.count();
    if (n === 0) { log.push('     no chat button on the touch layer'); await snap(c, p + 'chat-tap'); return; }
    await btn.first().tap({ timeout: 3000 }); await sleep(900);
    const opened = await c.locator('#nosum-chat-input').count();
    log.push('     chat button tapped: input box opened = ' + (opened === 1));
    await snap(c, p + 'chat-tap'); await c.keyboard.press('Escape'); await sleep(300);
  });
  await step(p + 'emote-wheel', async () => { await c.getByRole('button', { name: t.emotes }).first().dispatchEvent('click', null, { timeout: 5000 }); await sleep(700); await snap(c, p + 'emote-wheel'); });
  await step(p + 'emote-close', async () => { await c.getByRole('button', { name: t.emotes }).first().dispatchEvent('click', null, { timeout: 5000 }); await sleep(500); });
  // On a phone the column holds only Menu (and Emotes, Mail, Settings): Trade and Friends are tiles of the menu.
  await step(p + 'menu', async () => { await c.getByRole('button', { name: /^Menu$/ }).first().dispatchEvent('click', null, { timeout: 5000 }); await sleep(900); await snap(c, p + 'menu'); });
  await step(p + 'friends', async () => { await c.getByRole('button', { name: t.friends }).first().dispatchEvent('click', null, { timeout: 5000 }); await sleep(900); await snap(c, p + 'friends'); await closePanel(c); });
  await step(p + 'trade-pick', async () => { await c.getByRole('button', { name: /^Menu$/ }).first().dispatchEvent('click', null, { timeout: 5000 }).catch(() => {}); await sleep(700); await c.getByRole('button', { name: t.trade }).first().dispatchEvent('click', null, { timeout: 5000 }); await sleep(900); await snap(c, p + 'trade-pick'); await closePanel(c); });
  await c.context().close(); await b.context().close();
}
// A clean Friends frame (the page opened with no chat or emote step before it, so no panel is left open by a stray Escape).
async function friendsFrame(browser, code) {
  const a = await guest(browser, { viewport: { width: 1280, height: 720 }, locale: code === 'vi' ? 'vi-VN' : 'en-US' }, code === 'vi' ? 1 : 2, true);
  await a.evaluate(() => window.nosumQa.teleport('town', 20, 20)); await sleep(3000);
  await a.getByRole('button', { name: L[code].friends }).first().click({ timeout: 5000 }); await sleep(900);
  await a.screenshot({ path: path.join(OUT, code + '-1280x720-friends.png') });
  await a.context().close();
}
// The co-op page after one deposit (the contribution counts are on the member rows).
async function contributionFrame(browser, code) {
  const t = L[code];
  const a = await guest(browser, { viewport: { width: 1280, height: 720 }, locale: code === 'vi' ? 'vi-VN' : 'en-US' }, code === 'vi' ? 1 : 2, true);
  await a.evaluate(() => window.nosumQa.teleport('town', 42, 59)); await sleep(3500);
  await a.keyboard.press('e'); await sleep(5000);
  await a.getByText(t.coop, { exact: true }).click(); await sleep(1200);
  await a.locator('input').first().fill('Test Co-op'); await a.getByRole('button', { name: t.found }).first().click(); await sleep(1500);
  // A stack inside the co-op dialog (tools cannot go into the warehouse): the seeds, by their name in either language.
  const idx = await a.evaluate(() => {
    const all = [...document.querySelectorAll('button.slot:not(.empty)')];
    const inside = all.map((b, i) => ({ i, r: b.getBoundingClientRect(), label: b.getAttribute('aria-label') || '' })).filter(o => o.r.x > 420 && o.r.x < 860 && o.r.y > 500);
    const seeds = inside.find(o => /seeds|Hạt/.test(o.label));
    return seeds ? seeds.i : -1;
  });
  if (idx >= 0) { await a.locator('button.slot:not(.empty)').nth(idx).click(); await sleep(400); }
  await a.getByRole('button', { name: t.deposit }).first().click(); await sleep(1500);
  await a.screenshot({ path: path.join(OUT, code + '-1280x720-coop-contributions.png') });
  await a.context().close();
}

(async () => {
  const browser = await chromium.launch();
  // ONLY_EXTRAS=1 writes just the Friends and co-op contribution frames (a quick re-take after a change to those screens).
  if (!process.env.ONLY_EXTRAS) { for (const code of ['vi', 'en']) { await desktop(browser, code); await phone(browser, code); } }
  for (const code of ['vi', 'en']) { await step(code + '-1280x720-friends-frame', () => friendsFrame(browser, code)); await step(code + '-1280x720-coop-contributions', () => contributionFrame(browser, code)); }
  await browser.close();
  fs.writeFileSync(path.join(OUT, 'capture-log.txt'), log.join('\n') + '\n'); console.log(log.join('\n'));
})().catch(e => { console.error('FAILED', e.message); process.exit(1); });
