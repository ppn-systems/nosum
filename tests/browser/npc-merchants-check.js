// Browser check for the merchants (docs/adr/ADR-043): at the carpenter's counter in Town the action key opens the merchant's dialog box, Buy and Build open
// standalone dialogs (not menu tabs), the close button goes back to the box, Escape ends the conversation, the menu holds 11 player tabs and the mail icon opens
// the mail tab. Takes a screenshot at each step and logs which pieces are on screen.
// Needs a registered account standing on the carpenter's counter (Town tile (10, 86), saved place x = 168, y = 1391 in player_states, server restarted after the edit).
// Run: NODE_PATH=$(npm root -g) NOSUM_QA_EMAIL=... NOSUM_QA_PASSWORD=... node tests/browser/npc-merchants-check.js <vi|en> <screenshot prefix> [page url]
const { chromium } = require('playwright');
const lang = process.argv[2] || 'vi';
const prefix = process.argv[3] || 'out';
const base = process.argv[4] || 'http://localhost:5080/';
const email = process.env.NOSUM_QA_EMAIL;
const password = process.env.NOSUM_QA_PASSWORD;
if (!email || !password) { console.error('Set NOSUM_QA_EMAIL and NOSUM_QA_PASSWORD (a registered account).'); process.exit(2); }
const T = lang === 'en'
  ? { email: 'Gmail address', pw: 'Password', login: /^Log in$|^Sign in$/i, buy: /^Buy$/, build: /^Build$/, talk: /^Talk$/, leave: /^Leave$/ }
  : { email: 'Địa chỉ Gmail', pw: 'Mật khẩu', login: /^Đăng nhập$/, buy: /^Mua$/, build: /^Xây dựng$/, talk: /^Trò chuyện$/, leave: /^Tạm biệt$/ };
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button');
  await page.getByLabel('Địa chỉ Gmail').fill(email);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(password);
  await page.locator('button[type=submit]').click();
  await page.waitForSelector('canvas', { timeout: 60000 });
  await page.waitForTimeout(6000);
  // language: the settings page holds it
  await page.keyboard.press('KeyH');
  await page.waitForTimeout(600);
  await page.getByRole('button', { name: lang === 'en' ? 'English' : /Tiếng Việt/ }).first().click();
  await page.waitForTimeout(800);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(800);
  const log = async (label) => console.log(label, JSON.stringify(await page.evaluate(() => ({
    box: !!document.querySelector('.npc-box'), dialog: !!document.querySelector('.trade-dialog'), menu: !!document.querySelector('.game-menu'),
    title: document.querySelector('.trade-window .game-panel-title')?.textContent || document.querySelector('.npc-box .game-panel-title')?.textContent || '',
    tabs: [...document.querySelectorAll('.menu-tab')].length, hotbar: !!document.querySelector('.hotbar'),
  }))));
  await page.screenshot({ path: `${prefix}-1-npc-at-counter-${lang}.png` });
  await log('world');
  await page.keyboard.press('KeyE');
  await page.waitForTimeout(900);
  await page.screenshot({ path: `${prefix}-2-npc-dialog-${lang}.png` });
  await log('dialog');
  await page.getByRole('button', { name: T.buy }).click();
  await page.waitForTimeout(900);
  await page.screenshot({ path: `${prefix}-3-shop-dialog-${lang}.png` });
  await log('shop');
  // The close button of the dialog goes back to the merchant's box; Build opens the carpenter's sections as a dialog of their own.
  await page.locator('.trade-window .game-close').click();
  await page.waitForTimeout(700);
  await log('back-to-box');
  await page.getByRole('button', { name: T.build }).click();
  await page.waitForTimeout(900);
  await page.screenshot({ path: `${prefix}-4-build-dialog-${lang}.png` });
  await log('build');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(700);
  await log('escape');
  // The menu holds player things only; mail has its own icon.
  await page.keyboard.press('KeyI');
  await page.waitForTimeout(700);
  await page.screenshot({ path: `${prefix}-5-menu-${lang}.png` });
  await log('menu');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(500);
  await page.locator('button.game-icon-button').first().click();
  await page.waitForTimeout(900);
  await page.screenshot({ path: `${prefix}-6-mail-${lang}.png` });
  console.log('mail', JSON.stringify(await page.evaluate(() => ({ tabs: [...document.querySelectorAll('.menu-tab')].map(t => t.getAttribute('aria-label') + (t.classList.contains('active') ? '*' : '')), title: document.querySelector('.menu-window .game-panel-title')?.textContent }))));
  console.log('errors', JSON.stringify(errors));
  await browser.close();
})();
