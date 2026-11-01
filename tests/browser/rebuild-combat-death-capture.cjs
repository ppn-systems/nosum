// Captures for docs/qa/rebuild-combat-fishing: fight to the death (the knock-out, the farmhouse) and then the beach. See rebuild-combat-capture.cjs.
// Fight to the end (death and respawn frames), then fish at the beach. usage: node play2.cjs <outDir> <w> <h> <locale>
const path = require('path'); const fs = require('fs');
const { chromium } = require('playwright');
const [outDir, w, h, locale = 'vi-VN'] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const ctx = await browser.newContext({ viewport: { width: Number(w), height: Number(h) }, locale, hasTouch: Number(w) < 500, isMobile: Number(w) < 500 });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text().slice(0, 160)); });
  page.on('pageerror', e => errors.push('PAGEERROR ' + e.message.slice(0, 160)));
  const shot = async n => { await page.screenshot({ path: path.join(outDir, n + '.png') }); };
  await page.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForTimeout(4000);
  await page.evaluate(() => window.nosumQa.grant(30, 1, 0));
  await page.evaluate(() => window.nosumQa.teleport('mine1', 12, 12));
  await page.waitForTimeout(4000);
  await page.evaluate(() => window.nosumQa.teleportToMonster('mine1'));
  await page.waitForTimeout(1500);
  await page.keyboard.press('9');
  await page.waitForTimeout(300);
  // Fight without skills: stand and strike; sample a frame every few strikes so the death shows.
  for (let i = 0; i < 40; i++) {
    await page.keyboard.press('Space');
    await page.waitForTimeout(500);
    if (i % 5 === 4) await shot(`fight-${String(i).padStart(2, '0')}`);
  }
  await page.waitForTimeout(2500);
  await shot('fight-end');
  // Fishing: the beach. A teleport to the sand row above the sea, then a cast at the water below.
  await page.evaluate(() => window.nosumQa.teleport('beach', 40, 42));
  await page.waitForTimeout(4000);
  await shot('fish-00-beach');
  log('errors:', errors.slice(0, 6));
  await browser.close();
})().catch(e => { console.error('FAILED', e.message); process.exitCode = 1; });
