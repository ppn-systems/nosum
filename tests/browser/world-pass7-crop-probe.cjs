// World QA pass 7, issue 1: is a parsnip visible at stage 0 right after sowing? (docs/qa/world-pass7/crop-stage0/)
// Needs a Development server and a client built with -p:NosumDevTools=true, opened at ?qa=1 (ADR-079).
// Usage: node tests/browser/world-pass7-crop-probe.cjs [base=http://localhost:5080/] [outDir] [width=1280] [height=720] [lang=en]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/?qa=1';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/world-pass7/crop-stage0'));
const W = Number(process.argv[4] || 1280);
const H = Number(process.argv[5] || 720);
const lang = process.argv[6] || 'en';
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const CENTER = { x: Math.round(W / 2) + 17, y: Math.round(H / 2) + 27 };
const TILE_PX = 45;
const at = (dx, dy) => ({ x: CENTER.x + dx * TILE_PX, y: CENTER.y + dy * TILE_PX });
const SOW = { x: 48, y: 18 };

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: W, height: H }, locale: lang === 'en' ? 'en-US' : 'vi-VN' });
  await context.addInitScript(value => { try { localStorage.setItem('nosum.settings.v1', value); } catch (e) {} }, `lang=${lang === 'en' ? 2 : 1}\n`);
  const page = await context.newPage();
  page.on('pageerror', e => console.log('pageerror:', e.message.slice(0, 200)));
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 });
  await wait(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await wait(3000);
  await page.evaluate(() => window.nosumQa.springReset());
  await wait(2000);
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
  await wait(2500);
  await page.evaluate(() => { window.nosumQa.grant(100, 10, 0); window.nosumQa.grant(1, 1, 0); });
  await wait(1200);
  const names = () => page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const slotOf = async re => { const n = await names(); const i = n.findIndex(x => re.test(x)); return i + 1; };
  const select = async n => { await page.keyboard.press(String(n)); await wait(300); };
  const tap = async p => { await page.mouse.move(p.x, p.y); await page.mouse.down(); await wait(200); await page.mouse.up(); await wait(900); };
  const seedSlot = await slotOf(/parsnip seeds|hạt củ cải vàng/i);
  const hoeSlot = await slotOf(/hoe|cuốc/i);
  console.log('hotbar', (await names()).join(' | '), 'seed slot', seedSlot, 'hoe slot', hoeSlot);
  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), [SOW.x, SOW.y - 1]);
  await wait(1500);
  await select(hoeSlot || 1);
  await tap(at(0, 1));
  await wait(600);
  await page.mouse.move(2, 2);
  await wait(400);
  await page.screenshot({ path: path.join(outDir, `tilled-${W}x${H}-${lang}.png`) });
  await select(seedSlot);
  await tap(at(0, 1));
  await wait(1200);
  await page.mouse.move(2, 2);
  await wait(4500); // the notices fade after about 4 s: capture the tile without them
  await page.screenshot({ path: path.join(outDir, `sown-${W}x${H}-${lang}.png`) });
  // The sown tile (48,18) is one tile south of the farmer's tile (48,17), which is the screen centre.
  const sx = CENTER.x;
  const sy = CENTER.y + TILE_PX;
  const notices = await page.$$eval('.game-notice, .hotbar .message, .hud-bottom-center', els => els.map(e => (e.innerText || '').trim()).filter(Boolean));
  console.log('notices', JSON.stringify(notices));
  await page.screenshot({ path: path.join(outDir, `sown-zoom-${W}x${H}-${lang}.png`), clip: { x: sx - 60, y: sy - 50, width: 120, height: 100 } });
  console.log('sown tile centre', sx, sy);
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
