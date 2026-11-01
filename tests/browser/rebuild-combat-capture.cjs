// Captures for docs/qa/rebuild-combat-fishing: combat in the mine, the strike sequence, the skills. Tester build (-p:NosumDevTools=true) on a Development server.
// usage: node tests/browser/rebuild-combat-capture.cjs <outDir> <width> <height> [locale]
// Plays combat and fishing in the tester build, saving frames. usage: node play.cjs <outDir> <width> <height> [locale]
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright');
const [outDir, w, h, locale = 'vi-VN'] = process.argv.slice(2);
const width = Number(w), height = Number(h);
fs.mkdirSync(outDir, { recursive: true });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const ctx = await browser.newContext({ viewport: { width, height }, locale, hasTouch: width < 500, isMobile: width < 500 });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text().slice(0, 200)); });
  page.on('pageerror', e => errors.push('PAGEERROR ' + e.message.slice(0, 200)));
  const shot = async n => { await page.screenshot({ path: path.join(outDir, n + '.png') }); log('shot', n); };
  await page.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForTimeout(4000);
  log('qa hook', await page.evaluate(() => typeof window.nosumQa));
  await shot('01-world');
  // Combat: go into the first mine floor, find a monster and stand beside it.
  await page.evaluate(() => window.nosumQa.grant(30, 1, 0));
  await page.evaluate(() => window.nosumQa.teleport('mine1', 12, 12));
  await page.waitForTimeout(4000);
  await page.evaluate(() => window.nosumQa.teleportToMonster('mine1'));
  await page.waitForTimeout(2000);
  await page.keyboard.press('9');
  await page.waitForTimeout(400);
  await shot('02-next-to-monster');
  // Basic strike with the action key (Space) and skills, sampling frames at short intervals.
  for (let i = 0; i < 8; i++) {
    await page.keyboard.press('Space');
    await page.waitForTimeout(120);
    await shot(`03-strike-${i}`);
  }
  await page.keyboard.press('x');
  await page.waitForTimeout(200);
  await shot('04-fireball-cast');
  await page.waitForTimeout(900);
  await shot('05-after-fireball');
  await page.keyboard.press('z');
  await page.waitForTimeout(250);
  await shot('06-whirlwind');
  await page.waitForTimeout(3000);
  await shot('07-later');
  log('errors:', errors.slice(0, 10));
  await browser.close();
})().catch(e => { console.error('FAILED', e.message); process.exitCode = 1; });
