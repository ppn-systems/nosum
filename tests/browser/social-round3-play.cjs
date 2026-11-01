// Social round 3: two real players play end to end (docs/design/critique-social.md; grading in docs/qa/social-round3/README.md).
// Run from the repo root with the Development host on :57300 and a Development WASM client built with -p:NosumDevTools=true:
//   BASE=http://localhost:5080/ LABEL=after OUT_DIR=docs/qa/social-round3/after NODE_PATH=$(npm root -g) node tests/browser/social-round3-play.cjs
// Player A is a guest that befriends a new registered test player (window.nosumQa.befriend, docs/adr/ADR-097). Player B signs in as that test
// player in a second browser context (the remembered session is seeded from the token). Steps, in the Town: friend presence, chat, a trade with
// both confirmations, a visit to the friend's farm, a co-op founded by A with deposits from both, and the warehouse log. Every step is logged.
// SIZE=1280x720|1920x1080|390x844 (default 1280x720) and LANGS=vi,en (default both) choose the frames; each step saves a frame.
const { chromium, devices } = require('playwright'); const fs = require('fs'); const path = require('path');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const base = process.env.BASE || 'http://localhost:5080/';
const SIZE = process.env.SIZE || '1280x720';
const OUT = process.env.OUT_DIR || path.join(__dirname, '..', '..', 'docs', 'qa', 'social-round3', process.env.LABEL || 'run');
fs.mkdirSync(OUT, { recursive: true });
const LANGS = (process.env.LANGS || 'vi,en').split(',');
const SIZES = {
  '1280x720': { viewport: { width: 1280, height: 720 } },
  '1920x1080': { viewport: { width: 1920, height: 1080 } },
  '390x844': { ...devices['iPhone 14'], viewport: { width: 390, height: 844 } },
};
const L = {
  vi: { cancel: /Hủy giao dịch/, guest: /khách|guest/i, menu: /^Menu$/, trade: /Giao dịch/, invite: /Mời/, accept: /Đồng ý/, confirm: /Xác nhận/, friends: /Bạn bè/, visit: /Thăm nông trại/, coop: /Hợp tác xã/, found: /^Lập/, deposit: /^Gửi vào$/, online: /Trực tuyến/, continueAs: /Tiếp tục với/, seeds: /Hạt/ },
  en: { cancel: /Cancel trade/, guest: /khách|guest/i, menu: /^Menu$/, trade: /^Trade/, invite: /Invite/, accept: /^Accept$/, confirm: /^Confirm$/, friends: /^Friends$/, visit: /^Visit farm$/, coop: /^Co-op$/, found: /^Found/, deposit: /^Deposit$/, online: /Online/, continueAs: /Continue as/, seeds: /seeds/i, },
};
const ITEM_PARSNIP_SEEDS = 100;
const log = [];
const note = (s) => { log.push(s); console.log(s); };
let pages = [];
async function step(name, fn) {
  try { const r = await fn(); note('ok   ' + name + (r ? ' ' + r : '')); return true; }
  catch (e) {
    note('FAIL ' + name + ': ' + e.message.split('\n')[0]);
    for (const [i, pg] of pages.entries()) await pg.screenshot({ path: path.join(OUT, 'fail-' + name + '-' + 'AB'[i] + '.png') }).catch(() => {});
    return false;
  }
}
const sleepFor = sleep;

// A guest signs in and waits for the game.
async function guest(browser, opts, code) {
  const ctx = await browser.newContext({ ...opts, locale: code === 'vi' ? 'vi-VN' : 'en-US' });
  await ctx.addInitScript(l => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); } catch {} }, code === 'vi' ? 1 : 2);
  const page = await ctx.newPage(); page.errors = [];
  page.on('pageerror', e => page.errors.push(e.message));
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 }); await sleep(2500);
  await page.getByRole('button', { name: L[code].guest }).first().click();
  await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 60000 }).catch(() => {});
  await sleep(4000); return page;
}

// A second player signs in as an existing registered account, from the session token the test-friend hook returned.
async function signedInAs(browser, opts, code, nickname, token) {
  const ctx = await browser.newContext({ ...opts, locale: code === 'vi' ? 'vi-VN' : 'en-US' });
  const record = JSON.stringify({ version: 1, token, expiresAt: new Date(Date.now() + 29 * 864e5).toISOString(), nickname, kind: 'guest' });
  await ctx.addInitScript(([l, rec]) => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); localStorage.setItem('nosum.remembered-session', rec); } catch {} }, [code === 'vi' ? 1 : 2, record]);
  const page = await ctx.newPage(); page.errors = [];
  page.on('pageerror', e => page.errors.push(e.message));
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 }); await sleep(2500);
  await page.getByRole('button', { name: L[code].continueAs }).first().click({ timeout: 15000 });
  await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 60000 }).catch(() => {});
  await sleep(4000); return page;
}

// Closes every open panel by its own Close button, and the chat box with Escape. Escape with nothing open reopens the last-used menu tab
// (critique finding 14), so it is never sent to close a panel.
async function closeAll(page) {
  for (let pass = 0; pass < 3; pass++) {
    const closes = page.getByRole('button', { name: /^Close$|^Đóng$/ });
    const n = await closes.count();
    let closed = 0;
    for (let k = 0; k < n; k++) {
      if (await closes.nth(k).isVisible().catch(() => false)) { await closes.nth(k).dispatchEvent('click', null, { timeout: 2000 }).catch(() => {}); closed++; }
    }
    await sleep(400);
    if (closed === 0) break;
  }
  if (await page.locator('#nosum-chat-input').count()) { await page.keyboard.press('Escape'); await sleep(400); }
}
async function openTab(page, re) { await page.getByRole('button', { name: re }).first().click({ timeout: 6000 }); await sleep(900); }
// Clicks a seed stack in the open bag (or trade bag): the seed slot by its name. Tools cannot be traded or stored in the warehouse.
async function pickSeeds(page) {
  const found = await page.evaluate(() => {
    const all = [...document.querySelectorAll('button.slot:not(.empty)')];
    const labels = all.map(b => b.getAttribute('aria-label') || '');
    const hit = labels.map((l, i) => ({ l, i })).filter(o => /seed|Hạt/i.test(o.l));
    return { index: hit.length ? hit[hit.length - 1].i : -1, labels: labels.slice(0, 12) };
  });
  if (found.index < 0) throw new Error('no seed stack in the bag; labels ' + JSON.stringify(found.labels));
  await page.locator('button.slot:not(.empty)').nth(found.index).click({ timeout: 4000 });
  await sleep(500);
}
async function snap(page, name) { await page.screenshot({ path: path.join(OUT, name + '.png') }); }

async function play(browser, code, size) {
  const t = L[code], opts = SIZES[size], phone = size.startsWith('390'), p = `${code}-${size}-`;
  const a = await guest(browser, opts, code);
  // The test friend: A becomes a registered account and befriends a new registered player.
  const made = await a.evaluate(() => window.nosumQa.befriend());
  const [nickname, token] = String(made).split('\t');
  if (!nickname || !token) { note('FAIL befriend: the server made no test friend (' + JSON.stringify(made) + ')'); return; }
  note('ok   befriend ' + nickname);
  const b = await signedInAs(browser, opts, code, nickname, token);
  pages = [a, b];
  await a.evaluate(() => window.nosumQa.teleport('town', 20, 20));
  await b.evaluate(() => window.nosumQa.teleport('town', 22, 21));
  await sleep(4000);
  await a.evaluate(() => window.nosumQa.grant(100, 5, 0)); // A: five parsnip seeds, for the trade
  await b.evaluate(() => window.nosumQa.grant(100, 5, 0)); // B: the same, for the trade and the co-op
  await sleep(1200);

  // 1. Friend presence: A's friends list shows the friend online.
  await step(p + 'friends-online', async () => {
    if (phone) { await openTab(a, /^Menu$/); }
    await openTab(a, t.friends);
    const online = await a.getByText(nickname).first().isVisible().catch(() => false);
    await snap(a, p + 'friends-online');
    await closeAll(a);
    if (!online) throw new Error('the friend is not in the friends list');
    return '(listed)';
  });

  // 2. Chat: A says hello in the Town and B reads it.
  await step(p + 'chat', async () => {
    if (phone) { await a.getByRole('button', { name: /Trò chuyện|chat/i }).first().tap({ timeout: 4000 }); }
    else { await a.keyboard.press('Enter'); }
    await sleep(600);
    const words = code === 'vi' ? 'Chào bạn' : 'Hello friend';
    await a.locator('#nosum-chat-input').fill(words); await a.keyboard.press('Enter'); await sleep(1200);
    const seen = await b.getByText(words).first().isVisible().catch(() => false);
    await snap(b, p + 'chat-received');
    await closeAll(a);
    if (!seen) throw new Error('B did not see the line');
  });

  // 3. Trade with both confirmations: A offers seeds, B offers nothing, both confirm.
  await step(p + 'trade', async () => {
    if (phone) { await openTab(a, /^Menu$/); }
    await openTab(a, t.trade);
    await a.getByRole('button', { name: t.invite }).first().click({ timeout: 6000 }); await sleep(1200);
    await b.getByRole('button', { name: t.accept }).first().click({ timeout: 6000 }); await sleep(1500);
    await snap(b, p + 'trade-invite-receiver');
    await pickSeeds(a); await sleep(600);
    await snap(a, p + 'trade-open');
    await a.getByRole('button', { name: t.confirm }).first().click({ timeout: 6000 }); await sleep(800);
    await b.getByRole('button', { name: t.confirm }).first().click({ timeout: 6000 }); await sleep(1800);
    await snap(a, p + 'trade-after-confirm');
    const open = await a.getByRole('button', { name: t.cancel }).first().isVisible().catch(() => false);
    if (open) throw new Error('the trade window is still open after both confirmed');
    await closeAll(a); await closeAll(b);
  });

  // 4. Visit the friend's farm from the friends list.
  await step(p + 'visit-farm', async () => {
    if (phone) { await openTab(a, /^Menu$/); }
    await openTab(a, t.friends);
    await a.getByRole('button', { name: t.visit }).first().click({ timeout: 6000 }); await sleep(3500);
    await snap(a, p + 'visit-farm');
    await a.evaluate(() => window.nosumQa.teleport('town', 20, 20)); await sleep(3000);
  });

  // 5. Co-op: A founds it at the Town board, deposits seeds; B is invited, accepts, deposits; the log shows both.
  await step(p + 'coop-deposit', async () => {
    await a.evaluate(() => window.nosumQa.teleport('town', 42, 59)); await sleep(3500);
    await a.keyboard.press('e'); await sleep(4500);
    await a.getByText(t.coop, { exact: true }).first().click({ timeout: 6000 }); await sleep(1200);
    await a.locator('input').first().fill('Test Co-op'); await a.getByRole('button', { name: t.found }).first().click({ timeout: 6000 }); await sleep(1500);
    await pickSeeds(a);
    await a.getByRole('button', { name: t.deposit }).first().click({ timeout: 6000 }); await sleep(1500);
    await snap(a, p + 'coop-deposit');
  });
  await step(p + 'coop-invite', async () => {
    await b.evaluate(() => window.nosumQa.teleport('town', 44, 60)); await sleep(2500);
    await a.getByRole('button', { name: t.invite }).first().click({ timeout: 6000 }); await sleep(1500);
    await b.getByRole('button', { name: t.accept }).first().click({ timeout: 6000 }); await sleep(1500);
    await closeAll(a);
    await b.evaluate(() => window.nosumQa.teleport('town', 42, 59)); await sleep(3500);
    await b.keyboard.press('e'); await sleep(4500);
    await b.getByText(t.coop, { exact: true }).first().click({ timeout: 6000 }); await sleep(1200);
    await pickSeeds(b);
    await b.getByRole('button', { name: t.deposit }).first().click({ timeout: 6000 }); await sleep(1500);
    await b.screenshot({ path: path.join(OUT, p + 'coop-deposit-receiver.png') });
    await a.getByText(t.coop, { exact: true }).first().click({ timeout: 6000 }).catch(() => {}); await sleep(1200);
    await snap(a, p + 'coop-log');
  });

  if (a.errors.length || b.errors.length) note('     page errors: A ' + JSON.stringify(a.errors.slice(0, 3)) + ' B ' + JSON.stringify(b.errors.slice(0, 3)));
  await a.context().close(); await b.context().close();
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const code of LANGS) {
    await step(`${code}-${SIZE} play`, () => play(browser, code, SIZE));
  }
  fs.writeFileSync(path.join(OUT, `capture-log-${SIZE}.txt`), log.join('\n') + '\n');
  await browser.close();
})().catch(e => { console.error('PLAY FAIL', e.message); process.exit(1); });
