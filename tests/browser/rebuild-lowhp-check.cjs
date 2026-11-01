// Checks the low-HP note of the vitals column (docs/adr/ADR-045 amendment): fights until the note shows and prints its box and the HP meter.
// Fights a monster until the low-HP note appears, then records its box and the HP meter (usage: node lowhp.cjs <outDir> <w> <h> <locale>).
const path = require('path'); const fs = require('fs');
const { chromium } = require('playwright');
const [outDir, w, h, locale = 'vi-VN'] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const ctx = await browser.newContext({ viewport: { width: Number(w), height: Number(h) }, locale, hasTouch: Number(w) < 500, isMobile: Number(w) < 500 });
  const page = await ctx.newPage();
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
  let found = null;
  for (let i = 0; i < 120 && !found; i++) {
    await page.keyboard.press('Space');
    await page.waitForTimeout(400);
    const hpNow = await page.evaluate(() => { const b = document.querySelector('.hp-bar'); return b ? b.getAttribute('aria-valuenow') + '/' + b.getAttribute('aria-valuemax') : 'none'; });
    if (i % 6 === 0) console.log('press', i, 'hp', hpNow);
    found = await page.evaluate(() => {
      const note = document.querySelector('.vitals-hp-note');
      const bar = document.querySelector('.hp-bar');
      if (!note || !bar) return null;
      const r = note.getBoundingClientRect();
      const b = bar.getBoundingClientRect();
      const heart = document.querySelector('.hud img, .game-icon-button');
      return { text: note.textContent, now: bar.getAttribute('aria-valuenow'), max: bar.getAttribute('aria-valuemax'), note: [r.x, r.y, r.width, r.height].map(Math.round), tube: [b.x, b.y, b.width, b.height].map(Math.round), vw: innerWidth, vh: innerHeight };
    });
  }
  console.log('NOTE', JSON.stringify(found));
  await page.waitForTimeout(300);
  await page.screenshot({ path: path.join(outDir, 'lowhp-note.png') });
  await browser.close();
})().catch(e => { console.error('FAILED', e.message); process.exitCode = 1; });
