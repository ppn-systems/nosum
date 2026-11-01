// Visual capture of the farm states that tile chunk baking must keep (docs/perf/chunk-baking/README.md):
// tilled and sown soil, the thirsty marker after a dry day, a season change, and a map transition. Writes one PNG per state at 1280 x 720.
// Usage: node capture.cjs <baseUrl with ?qa=1> <outDir> <prefix>
// Needs a tester build (window.nosumQa) and the Development server on :57300.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const [, , base, outDirArg, prefixArg = 'run'] = process.argv;
const outDir = path.resolve(outDirArg);
const prefix = prefixArg;
const wait = ms => new Promise(r => setTimeout(r, ms));
fs.mkdirSync(outDir, { recursive: true });

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN', serviceWorkers: 'block' });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message.slice(0, 200)));
  await page.goto(base, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('[role=meter][aria-label^="Sức lực"]', { timeout: 120000 });
  await wait(1500);
  const shot = async name => { await wait(700); await page.screenshot({ path: path.join(outDir, `${prefix}-${name}.png`) }); console.log('captured', name); };

  // The farm spawn, with a hoe and parsnip seeds; then the same serpentine walk as frame.cjs tills and sows a block.
  await page.evaluate(() => window.nosumQa.teleport('farm', 64, 16));
  await wait(2500);
  await page.evaluate(() => { window.nosumQa.grant(1, 1, 0); window.nosumQa.grant(100, 40, 0); });
  await wait(2500);
  const walkRows = async (tool, rows, cols) => {
    await page.keyboard.press(tool);
    await wait(200);
    for (let r = 0; r < rows; r++) {
      const dir = r % 2 === 0 ? 'KeyD' : 'KeyA';
      for (let c = 0; c < cols; c++) {
        await page.keyboard.press('KeyE'); await wait(340);
        await page.keyboard.down(dir); await wait(260); await page.keyboard.up(dir); await wait(60);
      }
      await page.keyboard.press('KeyE'); await wait(340);
      await page.keyboard.down('KeyS'); await wait(260); await page.keyboard.up('KeyS'); await wait(60);
    }
  };
  await walkRows('Digit1', 4, 6);
  await wait(800);
  await walkRows('Digit3', 4, 6);
  await wait(2500);
  await shot('1-tilled-and-sown');

  // A day without water: bare soil one dry day from reverting shows the thirsty marker (ADR-021 D7).
  await page.evaluate(() => window.nosumQa.advanceClock(1));
  await wait(3000);
  await shot('2-after-a-dry-day');

  // A season change (the season banner and the crops that do not survive it).
  await page.evaluate(() => window.nosumQa.season(1));
  await wait(3000);
  await shot('3-season-change');

  // A map transition: the farm to the town square (the fade, then the new map).
  await page.evaluate(() => window.nosumQa.teleport('town', 40, 40));
  await wait(4000);
  await shot('4-map-transition-town');

  console.log('page errors:', errors.length, JSON.stringify(errors.slice(0, 3)));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
