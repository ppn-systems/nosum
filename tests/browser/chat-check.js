// Browser check for the chat line (docs/adr/ADR-029; run: NODE_PATH=$(npm root -g) node tests/browser/chat-check.js <page url>): opening the box with Enter,
// sending a line and closing the box with Enter or Escape raise no console or page errors (closing used to throw "heap is currently locked": the box's blur
// handler called into .NET while Blazor was removing the focused input inside a render batch), and the line shows in the chat.
const { chromium } = require('playwright');
const base = process.argv[2] || 'http://localhost:5080/';
(async () => {
  const browser = await chromium.launch();
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'en-US' })).newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(2500);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 60000 });
  await page.waitForTimeout(6000);
  const line = 'hello ' + Date.now() % 100000;
  await page.keyboard.press('Enter');
  // Read right after the key, before the box has rendered and taken focus: the game must already ignore typing (issue #144).
  const markedAtOnce = await page.evaluate(() => document.documentElement.hasAttribute('data-text-entry'));
  await page.waitForTimeout(600);
  const opened = await page.locator('input').count() === 1;
  await page.keyboard.type(line); await page.keyboard.press('Enter'); await page.waitForTimeout(1500);
  const shown = (await page.evaluate(() => document.body.innerText)).includes(line);
  await page.keyboard.press('Enter'); await page.waitForTimeout(600);
  await page.keyboard.press('Escape'); await page.waitForTimeout(800);
  const closed = await page.locator('input').count() === 0;
  const unmarked = await page.evaluate(() => !document.documentElement.hasAttribute('data-text-entry'));
  console.log('box opens with Enter'.padEnd(44), opened);
  console.log('sent line is shown'.padEnd(44), shown);
  console.log('box closes with Escape, no game menu opened'.padEnd(44), closed);
  console.log('typing is marked the moment Enter opens the box'.padEnd(44), markedAtOnce);
  console.log('the mark is cleared when the box closes'.padEnd(44), unmarked);
  console.log('console errors'.padEnd(44), JSON.stringify(errors));
  const ok = opened && shown && closed && markedAtOnce && unmarked && errors.length === 0;
  console.log(ok ? 'RESULT: PASS' : 'RESULT: FAIL');
  await browser.close();
  process.exit(ok ? 0 : 1);
})();
