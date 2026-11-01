// Captures for docs/qa/rebuild-combat-fishing: a cast on the beach, the bite, the press and the catch. Tester build on a Development server.
// Fishing pass in the tester build: beach, rod granted into slot 9, cast at the sea, wait for the bite text, press Space.
// usage: node fish.cjs <outDir> <w> <h> <locale> <clickY>
const path = require('path'); const fs = require('fs');
const { chromium } = require('playwright');
const [outDir, w, h, locale = 'vi-VN', clickYArg] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const BITE = /Cá đang cắn câu!|Something is biting!/;
(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const ctx = await browser.newContext({ viewport: { width: Number(w), height: Number(h) }, locale, hasTouch: Number(w) < 500, isMobile: Number(w) < 500 });
  const page = await ctx.newPage();
  const shot = async n => { await page.screenshot({ path: path.join(outDir, n + '.png') }); log('shot', n); };
  const body = () => page.evaluate(() => document.body.innerText.replace(/\s+/g, ' ')).catch(() => '');
  await page.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForTimeout(4000);
  await page.evaluate(() => window.nosumQa.grant(40, 1, 0));
  await page.evaluate(() => window.nosumQa.teleport('beach', 40, 42));
  await page.waitForTimeout(4500);
  await page.keyboard.press('9');
  await page.waitForTimeout(400);
  await shot('fish-01-beach-rod');
  await page.mouse.click(Math.round(Number(w) / 2), Number(clickYArg || 512));
  const t0 = Date.now();
  let biteAt = 0, waitingShot = false;
  for (let i = 0; i < 400 && !biteAt; i++) {
    await page.waitForTimeout(100);
    const t = await body();
    if (!waitingShot && /Đang chờ cá|Waiting for a bite/.test(t)) { waitingShot = true; log(`+${Date.now() - t0}ms waiting shown`); await shot('fish-01b-waiting'); }
    if (BITE.test(t)) { biteAt = Date.now(); log(`+${Date.now() - t0}ms BITE`); await shot('fish-02-bite'); }
  }
  if (biteAt) {
    await page.keyboard.press('Space');
    await page.waitForTimeout(300);
    await shot('fish-03-reeling');
    await page.waitForTimeout(2000);
    log('after press:', (await body()).slice(0, 300));
    await shot('fish-04-result');
  } else {
    log('no bite; last text:', (await body()).slice(0, 300));
  }
  await browser.close();
})().catch(e => { console.error('FAILED', e.message); process.exitCode = 1; });
