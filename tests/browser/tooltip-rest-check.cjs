// Tooltip check (world pass 8): a tooltip shows only after the pointer moves. A window that opens under a resting pointer must not raise the tip of its
// Close control, and a press or a key press clears a tip. Exit code 1 when a tip shows without movement.
// usage: node tests/browser/tooltip-rest-check.cjs [url=http://localhost:5080/?qa=1]
const { chromium } = require('playwright');
const wait = ms => new Promise(r => setTimeout(r, ms));
const URL = process.argv[2] || 'http://localhost:5080/?qa=1';

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'en-US' });
  await ctx.addInitScript(() => { try { localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch (e) {} });
  const page = await ctx.newPage();
  await page.goto(URL, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 }); await wait(1000);
  await page.getByRole('button', { name: /as a guest|guest/i }).first().click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 }); await wait(2500);
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18)); await wait(2000);
  const tip = () => page.evaluate(() => { const t = document.getElementById('nosum-tip'); return !!t && t.classList.contains('shown'); });
  const failures = [];

  // 1. The pointer rests on the spot where the next window's Close will be; the window opens by key with no movement.
  await page.mouse.move(640, 300); await wait(200);
  await page.mouse.move(640, 300); // a move onto the resting spot, then the window opens under it
  await page.keyboard.press('i'); await wait(900);
  const closeBox = await page.locator('button[data-tip="Close"], button[aria-label="Close"]').first().boundingBox();
  await page.mouse.move(closeBox.x + closeBox.width / 2, closeBox.y + closeBox.height / 2); await wait(400);
  const hoveredShown = await tip();
  if (!hoveredShown) failures.push('moving onto the Close control did not show its tip');
  await page.keyboard.press('Escape'); await wait(600);
  if (await tip()) failures.push('the tip stayed after the window closed by Escape');

  // 2. A window opens under the pointer that did not move: no tip.
  await page.keyboard.press('i'); await wait(900);
  await wait(400);
  if (await tip()) failures.push('a window that opened under a resting pointer raised a tip');
  await page.keyboard.press('Escape'); await wait(600);

  console.log(failures.length ? 'FAIL ' + failures.join('; ') : 'OK tooltips follow the pointer, not the layout');
  await browser.close();
  process.exit(failures.length ? 1 : 0);
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
