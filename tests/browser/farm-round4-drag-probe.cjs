const path = require('path');
const S = path.resolve(__dirname, '../../docs/qa/farming-round4/drag');
const { chromium } = require('playwright');
const wait = ms => new Promise(r => setTimeout(r, ms));
const base = process.argv[2] || 'http://localhost:5080/';
require('fs').mkdirSync(path.resolve(__dirname, '../../docs/qa/farming-round4/drag'), { recursive: true });
const W = 1280, H = 720;
const CENTER = { x: W / 2 + 17, y: H / 2 + 27 }; const TILE = 45;
const at = (dx, dy) => ({ x: CENTER.x + dx * TILE, y: CENTER.y + dy * TILE });
(async () => {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium', args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: W, height: H }, locale: 'en-US' });
  await context.addInitScript(v => { try { localStorage.setItem('nosum.settings.v1', v); } catch (e) {} }, 'lang=2\n');
  const page = await context.newPage();
  page.on('pageerror', e => console.log('pageerror:', e.message.slice(0, 160)));
  await page.goto(`${base}?qa=1`, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('canvas', { timeout: 90000 });
  await page.waitForSelector('.calendar-line', { timeout: 60000 });
  await page.waitForFunction(() => globalThis.nosumQa !== undefined, null, { timeout: 30000 });
  await wait(2500);
  await page.evaluate(() => window.nosumQa.springReset()); await wait(2000);
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18)); await wait(2500);
  await page.evaluate(() => { window.nosumQa.grant(100, 10, 0); }); await wait(1000);
  const names = () => page.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const pick = async re => { const n = await names(); const i = n.findIndex(x => re.test(x)); if (i < 0) throw new Error('no slot ' + re + ' in ' + n.join('|')); await page.keyboard.press(String(i + 1)); await wait(300); };
  const line = () => page.evaluate(() => (document.body.innerText || '').split('\n').map(t => t.trim()).filter(t => /Soil tilled|Seeds planted|Not ready|Watered|Growth stage|Placed|already wet/i.test(t)).join(' | '));
  // Stand north of the row y=20 at x=49 (feet on tile 49,19), then drag across 48..50 at y=20.
  await page.evaluate(() => window.nosumQa.teleport('farm', 49, 19)); await wait(1200);
  const drag = async (label, fromX, toX, y) => {
    const a = at(fromX - 49, y - 19), b = at(toX - 49, y - 19);
    await page.mouse.move(a.x, a.y); await page.mouse.down(); await wait(120);
    for (let i = 1; i <= 12; i++) { await page.mouse.move(a.x + (b.x - a.x) * i / 12, a.y + (b.y - a.y) * i / 12); await wait(90); }
    await wait(300); await page.mouse.up(); await wait(900);
    console.log(label, '=>', await line());
  };
  await page.keyboard.press('1'); await wait(300);
  await drag('till drag 48..50', 48, 50, 20);
  await page.screenshot({ path: `${S}/drag-till.png` });
  await pick(/parsnip seeds|hạt củ cải vàng/i);
  await drag('sow drag 48..50', 48, 50, 20);
  await page.screenshot({ path: `${S}/drag-sow.png` });
  await browser.close();
})().catch(e => { console.error('FAILED', e.message.slice(0, 300)); process.exit(1); });
