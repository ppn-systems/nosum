// Browser check for the merchants (docs/adr/ADR-043): at the carpenter's counter in Town the action key opens the merchant's dialog box, Buy and Build open
// standalone dialogs (not menu tabs), the close button goes back to the box, Escape ends the conversation, the menu holds 11 player tabs and the mail icon opens
// the mail tab. Takes a screenshot at each step and logs which pieces are on screen.
// Needs a QA account standing on the carpenter's counter (Town tile (10, 86), saved place x = 168, y = 1391 in player_states, server restarted after the edit).
// The account signs in with Google or Microsoft (docs/adr/ADR-069), which a script cannot do: the browser opens headed and the operator signs in by hand;
// the check starts when the game canvas appears (up to five minutes).
// Run: NODE_PATH=$(npm root -g) node tests/browser/npc-merchants-check.js <vi|en> <screenshot prefix> [page url]
const { chromium } = require('playwright');
const lang = process.argv[2] || 'vi';
const prefix = process.argv[3] || 'out';
const base = process.argv[4] || 'http://localhost:5080/';
const T = lang === 'en'
  ? { buy: /^Buy$/, build: /^Build$/, talk: /^Talk$/, leave: /^Leave$/ }
  : { buy: /^Mua$/, build: /^Xây dựng$/, talk: /^Trò chuyện$/, leave: /^Tạm biệt$/ };
(async () => {
  const browser = await chromium.launch({ headless: false });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button');
  console.log('Sign in with the QA account in the browser window (Google or Microsoft); the check starts when the game is up.');
  await page.waitForSelector('canvas', { timeout: 300000 });
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
  if (errors.length > 0) { process.exitCode = 1; throw new Error(errors.join('; ')); }
})();
