// Social round 3: two real players play end to end (docs/design/critique-social.md; grading in docs/qa/social-round3/README.md).
// Run from the repo root with the Development host on :57300 and a Development WASM client built with -p:NosumDevTools=true:
//   BASE=http://localhost:5080/ LABEL=after OUT_DIR=docs/qa/social-round3/after NODE_PATH=$(npm root -g) node tests/browser/social-round3-play.cjs
// Player A is a guest that befriends a new registered test player (window.nosumQa.befriend, docs/adr/ADR-105). Player B signs in as that test
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
  vi: { cancel: /Hủy giao dịch/, guest: /khách|guest/i, menu: /^Menu$/, trade: /Giao dịch/, invite: /Mời/, accept: /Đồng ý/, confirm: /Xác nhận/, friends: /Bạn bè/, visit: /Thăm nông trại/, coop: /Hợp tác xã/, found: /^Lập/, deposit: /^Gửi vào$/, online: /Trực tuyến/, continueAs: /Tiếp tục với/, seeds: /Hạt/, stockTab: /^Kho$/, membersTab: /^Thành viên$/, logTab: /^Nhật ký$/ },
  en: { cancel: /Cancel trade/, guest: /khách|guest/i, menu: /^Menu$/, trade: /^Trade/, invite: /Invite/, accept: /^Accept$/, confirm: /^Confirm$/, friends: /^Friends$/, visit: /^Visit farm$/, coop: /^Co-op$/, found: /^Found/, deposit: /^Deposit$/, online: /Online/, continueAs: /Continue as/, seeds: /seeds/i, stockTab: /^Stock$/, membersTab: /^Members$/, logTab: /^Log$/ },
};
const ITEM_PARSNIP_SEEDS = 100;
const log = [];
const note = (s) => { log.push(s); console.log(s); };
let pages = [];
async function step(name, fn) {
  try { const r = await fn(); note('ok   ' + name + (r ? ' ' + r : '')); return true; }
  catch (e) {
    note('FAIL ' + name + ': ' + e.message.split('\n').slice(0, 4).join(' | ').slice(0, 400));
    for (const [i, pg] of pages.entries()) await pg.screenshot({ path: path.join(OUT, 'fail-' + name + '-' + 'AB'[i] + '.png') }).catch(() => {});
    return false;
  }
}
// Waits for a control to be visible (the DOM state the step needs), never for a fixed time. Throws when it is not seen in time.
async function seen(locator, ms = 20000) { await locator.first().waitFor({ state: 'visible', timeout: ms }); return locator.first(); }
const isSeen = (locator, ms = 20000) => seen(locator, ms).then(() => true, () => false);
const escRe = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// The game is running: the QA hook is installed (window.nosumQa) and the world canvas is in the page.
async function gameReady(page) {
  await page.waitForFunction(() => typeof window.nosumQa === 'object' && window.nosumQa !== null, null, { timeout: 120000 });
  await page.waitForSelector('canvas', { timeout: 60000 });
}
// Leaves the sign-in screen: every input of the sign-in form is gone, then the game is ready.
async function enterGame(page) {
  await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 90000 });
  await gameReady(page);
}

// A guest signs in and waits for the game.
async function guest(browser, opts, code) {
  const ctx = await browser.newContext({ ...opts, locale: code === 'vi' ? 'vi-VN' : 'en-US' });
  await ctx.addInitScript(l => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); } catch {} }, code === 'vi' ? 1 : 2);
  const page = await ctx.newPage(); page.errors = [];
  page.on('pageerror', e => page.errors.push(e.message));
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await seen(page.getByRole('button', { name: L[code].guest }), 120000).then(b => b.click());
  await enterGame(page);
  return page;
}

// A second player signs in as an existing registered account, from the session token the test-friend hook returned.
async function signedInAs(browser, opts, code, nickname, token) {
  const ctx = await browser.newContext({ ...opts, locale: code === 'vi' ? 'vi-VN' : 'en-US' });
  const record = JSON.stringify({ version: 1, token, expiresAt: new Date(Date.now() + 29 * 864e5).toISOString(), nickname, kind: 'guest' });
  await ctx.addInitScript(([l, rec]) => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); localStorage.setItem('nosum.remembered-session', rec); } catch {} }, [code === 'vi' ? 1 : 2, record]);
  const page = await ctx.newPage(); page.errors = [];
  page.on('pageerror', e => page.errors.push(e.message));
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await seen(page.getByRole('button', { name: L[code].continueAs }), 120000).then(b => b.click());
  await enterGame(page);
  return page;
}

// Closes every open panel by its own Close button, and the chat box with Escape. Escape with nothing open reopens the last-used menu tab
// (critique finding 14), so it is never sent to close a panel. Waits for each close button to go away.
async function closeAll(page) {
  for (let pass = 0; pass < 3; pass++) {
    const closes = page.getByRole('button', { name: /^Close$|^Đóng$/ });
    let closed = 0;
    for (let k = 0, n = await closes.count(); k < n; k++) {
      if (await closes.nth(k).isVisible().catch(() => false)) {
        await closes.nth(k).dispatchEvent('click', null, { timeout: 2000 }).catch(() => {});
        closed++;
        await closes.nth(k).waitFor({ state: 'detached', timeout: 5000 }).catch(() => {});
      }
    }
    if (closed === 0) break;
  }
  if (await page.locator('#nosum-chat-input').count()) { await page.keyboard.press('Escape'); await page.locator('#nosum-chat-input').waitFor({ state: 'detached', timeout: 5000 }).catch(() => {}); }
}
// Opens one menu tab by its button (on a phone the Menu button first).
async function openTab(page, re) { const b = await seen(page.getByRole('button', { name: re }), 15000); await b.click({ timeout: 6000 }); }
// Clicks a seed stack in the open bag (or trade bag): the seed slot by its name. Tools cannot be traded or stored in the warehouse.
// The bag fills a moment after the grant, so the slot is waited for.
async function pickSeeds(page) {
  const find = () => page.evaluate(() => {
    const all = [...document.querySelectorAll('button.slot:not(.empty)')];
    const labels = all.map(b => b.getAttribute('aria-label') || '');
    const hit = labels.map((l, i) => ({ l, i })).filter(o => /seed|Hạt/i.test(o.l));
    return { index: hit.length ? hit[hit.length - 1].i : -1, labels: labels.slice(0, 12) };
  });
  let found = await find();
  for (let k = 0; found.index < 0 && k < 20; k++) { await page.waitForTimeout(500); found = await find(); }
  if (found.index < 0) throw new Error('no seed stack in the bag; labels ' + JSON.stringify(found.labels));
  await page.locator('button.slot:not(.empty)').nth(found.index).click({ timeout: 4000 });
  await seen(page.locator('button.slot.picked, button.slot.selected, button.slot[aria-pressed="true"]').first(), 3000).catch(() => {});
}
async function snap(page, name) { await page.screenshot({ path: path.join(OUT, name + '.png') }); }

// Walks the player onto the Town board and opens it with the action key; retries the key until the board's window is up.
async function openBoard(page, t, tile = [42, 59]) {
  for (let k = 0; k < 8; k++) {
    // A teleport that lands late under load leaves the player out of reach: put the player back on the board tile after two misses.
    if (k === 2) await page.evaluate(([x, y]) => window.nosumQa.teleport('town', x, y), tile);
    await page.keyboard.press('e');
    if (await isSeen(page.getByRole('button', { name: t.found }), 4000)) return;
    if (await isSeen(page.getByText(t.coop, { exact: true }), 1000)) return;
    await page.waitForTimeout(800);
  }
  throw new Error('the co-op board did not open');
}

// The co-op shows one part at a time at every size (world pass 9, #346): the warehouse (where seeds are deposited) is its Stock tab.
async function membersTab(page, t) { await seen(page.getByRole('button', { name: t.membersTab })).then(b => b.dispatchEvent('click', null, { timeout: 6000 })); }
async function stockTab(page, t) { await seen(page.getByRole('button', { name: t.stockTab })).then(b => b.dispatchEvent('click', null, { timeout: 6000 })); }

// Invites a nearby player: the picker lists the players in reach by name, so the invite waits for the name to appear, and the
// invitation waits for the other player's Accept. Each stage is retried (teleporting the guest back into reach) rather than
// timed, because a busy server can delay the other browser's arrival.
async function invitePlayer(host, guestPage, nickname, t, guestTile) {
  for (let attempt = 1; attempt <= 3; attempt++) {
    if (!(await isSeen(host.getByRole('button', { name: new RegExp(escRe(nickname)) }), 15000))) {
      await guestPage.evaluate(([x, y]) => window.nosumQa.teleport('town', x, y), guestTile);
      continue;
    }
    await host.getByRole('button', { name: new RegExp(escRe(nickname) + '.*' + t.invite.source) }).first().dispatchEvent('click', null, { timeout: 6000 });
    if (await isSeen(guestPage.getByRole('button', { name: t.accept }), 25000)) {
      await guestPage.getByRole('button', { name: t.accept }).first().dispatchEvent('click', null, { timeout: 6000 });
      return;
    }
  }
  throw new Error('the invitation did not reach ' + nickname);
}

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
  // Both players in the Town: the friend's name is in A's picker of nearby players once B has arrived.
  await a.evaluate(() => window.nosumQa.grant(100, 5, 0)); // A: five parsnip seeds, for the trade
  await b.evaluate(() => window.nosumQa.grant(100, 5, 0)); // B: the same, for the trade and the co-op

  // 1. Friend presence: A's friends list shows the friend online.
  await step(p + 'friends-online', async () => {
    if (phone) { await openTab(a, /^Menu$/); }
    await openTab(a, t.friends);
    const online = await isSeen(a.getByText(nickname), 20000);
    await snap(a, p + 'friends-online');
    await closeAll(a);
    if (!online) throw new Error('the friend is not in the friends list');
    return '(listed)';
  });

  // 2. Chat: A says hello in the Town and B reads it.
  await step(p + 'chat', async () => {
    if (phone) { await seen(a.getByRole('button', { name: /Trò chuyện|chat/i })).then(c => c.tap({ timeout: 4000 })); }
    else { await a.keyboard.press('Enter'); }
    const words = code === 'vi' ? 'Chào bạn' : 'Hello friend';
    await seen(a.locator('#nosum-chat-input')); await a.locator('#nosum-chat-input').fill(words); await a.keyboard.press('Enter');
    const seenByB = await isSeen(b.getByText(words), 20000);
    await snap(b, p + 'chat-received');
    await closeAll(a);
    if (!seenByB) throw new Error('B did not see the line');
  });

  // 3. Trade with both confirmations: A offers seeds, B offers nothing, both confirm.
  await step(p + 'trade', async () => {
    if (phone) { await openTab(a, /^Menu$/); }
    await openTab(a, t.trade);
    await invitePlayer(a, b, nickname, t, [22, 21]);
    await snap(b, p + 'trade-invite-receiver');
    await pickSeeds(a);
    await snap(a, p + 'trade-open');
    await seen(a.getByRole('button', { name: t.confirm })).then(c => c.click({ timeout: 6000 }));
    await seen(b.getByRole('button', { name: t.confirm })).then(c => c.click({ timeout: 6000 }));
    await a.getByRole('button', { name: t.cancel }).first().waitFor({ state: 'detached', timeout: 20000 }).catch(() => {});
    await snap(a, p + 'trade-after-confirm');
    const open = await isSeen(a.getByRole('button', { name: t.cancel }), 1000);
    if (open) throw new Error('the trade window is still open after both confirmed');
    await closeAll(a); await closeAll(b);
  });

  // 4. Visit the friend's farm from the friends list.
  await step(p + 'visit-farm', async () => {
    if (phone) { await openTab(a, /^Menu$/); }
    await openTab(a, t.friends);
    await seen(a.getByRole('button', { name: t.visit })).then(v => v.click({ timeout: 6000 }));
    await a.getByRole('button', { name: t.visit }).first().waitFor({ state: 'detached', timeout: 20000 }).catch(() => {});
    await snap(a, p + 'visit-farm');
    await a.evaluate(() => window.nosumQa.teleport('town', 20, 20));
  });

  // 5. Co-op: A founds it at the Town board, deposits seeds; B is invited, accepts, deposits; the log shows both.
  await step(p + 'coop-deposit', async () => {
    await a.evaluate(() => window.nosumQa.teleport('town', 42, 59));
    await openBoard(a, t, [42, 59]);
    await seen(a.getByText(t.coop, { exact: true })).then(c => c.click({ timeout: 6000 }));
    await seen(a.locator('input').first()).then(i => i.fill('Test Co-op'));
    await a.getByRole('button', { name: t.found }).first().click({ timeout: 6000 });
    await stockTab(a, t);
    await seen(a.getByRole('button', { name: t.deposit }));
    await pickSeeds(a);
    await a.getByRole('button', { name: t.deposit }).first().click({ timeout: 6000 });
    await snap(a, p + 'coop-deposit');
  });
  await step(p + 'coop-invite', async () => {
    await b.evaluate(() => window.nosumQa.teleport('town', 44, 60));
    await membersTab(a, t); // the invite picker is on the co-op's Members part
    await invitePlayer(a, b, nickname, t, [44, 60]);
    await closeAll(a);
    await b.evaluate(() => window.nosumQa.teleport('town', 42, 59));
    await openBoard(b, t, [42, 59]);
    await seen(b.getByText(t.coop, { exact: true })).then(c => c.click({ timeout: 6000 }));
    await stockTab(b, t);
    await pickSeeds(b);
    await b.getByRole('button', { name: t.deposit }).first().click({ timeout: 6000 });
    await b.screenshot({ path: path.join(OUT, p + 'coop-deposit-receiver.png') });
  });
  // The co-op log is one tab of the co-op window. It is a checked step: if the log cannot be opened, this step fails (no silent catch).
  await step(p + 'coop-log', async () => {
    // The invite step closed every window on A, so the board is reopened from A's tile before the co-op window is clicked.
    if (await a.getByRole('button', { name: t.logTab }).count() === 0) {
      await openBoard(a, t, [44, 60]);
      await seen(a.getByText(t.coop, { exact: true })).then(c => c.click({ timeout: 6000 }));
    }
    await seen(a.getByRole('button', { name: t.logTab })).then(b => b.dispatchEvent('click', null, { timeout: 6000 }));
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
