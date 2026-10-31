// Captures the fishing banner (the status line while a cast is open) and the catch card (top centre) on the farm pond (docs/adr/ADR-078).
// Usage: BASE_URL=http://localhost:5820 OUT_DIR=<dir> [VIEWPORTS=...] node capture-fishing.js
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const BASE_URL = (process.env.BASE_URL || 'http://localhost:5820').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || '.');
const ALL = [{ w: 1280, h: 720, mobile: false }, { w: 1920, h: 1080, mobile: false }, { w: 390, h: 844, mobile: true }];
const wanted = (process.env.VIEWPORTS || '').split(',').filter(Boolean);
const VIEWPORTS = wanted.length ? ALL.filter((v) => wanted.includes(`${v.w}x${v.h}`)) : ALL;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const FISHING_ROD = 40;
const POND_EDGE = [63, 44]; // north of the pond's water tile (63, 45) on the farm map (art/own/Maps/Farm.tmx, Back layer)

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const report = { captured: [], failed: [] };
  for (const vp of VIEWPORTS) {
    const dir = path.join(OUT_DIR, `${vp.w}x${vp.h}`);
    fs.mkdirSync(dir, { recursive: true });
    let context;
    try {
      context = await browser.newContext({ viewport: { width: vp.w, height: vp.h }, deviceScaleFactor: 1, isMobile: vp.mobile, hasTouch: vp.mobile, locale: 'en-US' });
      await context.addInitScript(() => { try { if (!localStorage.getItem('nosum.settings.v1')) localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch (e) { /* blocked */ } });
      const p = await context.newPage();
      await p.goto(BASE_URL + '/?qa=1', { waitUntil: 'load', timeout: 120000 });
      await p.getByRole('button', { name: /as a guest|guest/i }).first().click({ timeout: 120000 });
      await p.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
      await sleep(3000);
      await p.evaluate((id) => window.nosumQa.grant(id, 1, 0), FISHING_ROD);
      await sleep(2000);
      await p.keyboard.press('Digit9');
      await p.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), POND_EDGE);
      await sleep(4500);
      // Walk south until the pond stops the farmer, so the water is the tile in front.
      await p.keyboard.down('KeyS');
      await sleep(1200);
      await p.keyboard.up('KeyS');
      await sleep(300);
      await p.keyboard.press('KeyE'); // cast
      await sleep(1500);
      await p.screenshot({ path: path.join(dir, 'fishing-banner.png'), animations: 'disabled' });
      report.captured.push('fishing-banner');
      await p.evaluate(() => window.nosumQa.fishingBite());
      await sleep(1200);
      await p.keyboard.press('KeyE'); // reel in inside the window
      await sleep(2500);
      await p.screenshot({ path: path.join(dir, 'fishing-catch.png'), animations: 'disabled' });
      report.captured.push('fishing-catch');
    } catch (e) {
      report.failed.push({ viewport: `${vp.w}x${vp.h}`, reason: String(e).slice(0, 300) });
    } finally {
      if (context) await context.close();
    }
  }
  await browser.close();
  fs.writeFileSync(path.join(OUT_DIR, 'capture-fishing-report.json'), JSON.stringify(report, null, 2));
  console.log('done', JSON.stringify(report));
}
run().catch((e) => { console.error('FAIL', e); process.exit(1); });
