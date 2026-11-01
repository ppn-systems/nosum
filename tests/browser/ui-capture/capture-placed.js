// Captures decorating, chest and furnace (docs/adr/ADR-079): each on a fresh guest, whose first granted item lands in hotbar slot 9.
// Usage: BASE_URL=http://localhost:5820 OUT_DIR=<dir> [VIEWPORTS=1280x720,...] node capture-placed.js
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const BASE_URL = (process.env.BASE_URL || 'http://localhost:5820').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || '.');
const ALL = [{ w: 1280, h: 720, mobile: false }, { w: 1920, h: 1080, mobile: false }, { w: 390, h: 844, mobile: true }];
const wanted = (process.env.VIEWPORTS || '').split(',').filter(Boolean);
const VIEWPORTS = wanted.length ? ALL.filter((v) => wanted.includes(`${v.w}x${v.h}`)) : ALL;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const report = { captured: [], failed: [] };

async function freshGuest(browser, vp) {
  const context = await browser.newContext({ viewport: { width: vp.w, height: vp.h }, deviceScaleFactor: 1, isMobile: vp.mobile, hasTouch: vp.mobile, locale: 'en-US' });
  await context.addInitScript(() => { try { if (!localStorage.getItem('nosum.settings.v1')) localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch (e) { /* blocked */ } });
  const page = await context.newPage();
  await page.goto(BASE_URL + '/?qa=1', { waitUntil: 'load', timeout: 120000 });
  await page.getByRole('button', { name: /as a guest|guest/i }).first().click({ timeout: 120000 });
  await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
  await sleep(3000);
  return { context, page };
}

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  for (const vp of VIEWPORTS) {
    const dir = path.join(OUT_DIR, `${vp.w}x${vp.h}`);
    fs.mkdirSync(dir, { recursive: true });
    for (const [name, itemId] of [['decorating', 300], ['chest', 600], ['furnace', 605]]) {
      let ctx;
      try {
        ctx = await freshGuest(browser, vp);
        const p = ctx.page;
        await p.evaluate((id) => window.nosumQa.grant(id, 1, 0), itemId);
        await sleep(2000);
        await p.keyboard.press('Digit9');
        await sleep(600);
        if (name === 'decorating') {
          await sleep(400);
          await p.screenshot({ path: path.join(dir, `${name}.png`), animations: 'disabled' });
          report.captured.push(name);
        } else {
          // Place it on the tile in front of the farmer, then use it.
          await p.keyboard.press('KeyE');
          await sleep(1200);
          await p.keyboard.press('KeyE');
          await sleep(1500);
          await p.screenshot({ path: path.join(dir, `${name}.png`), animations: 'disabled' });
          report.captured.push(name);
        }
      } catch (e) {
        report.failed.push({ screen: name, reason: String(e).slice(0, 200) });
      } finally {
        if (ctx) await ctx.context.close();
      }
    }
  }
  await browser.close();
  fs.writeFileSync(path.join(OUT_DIR, 'capture-placed-report.json'), JSON.stringify(report, null, 2));
  console.log('done', JSON.stringify(report));
}
run().catch((e) => { console.error('FAIL', e); process.exit(1); });
