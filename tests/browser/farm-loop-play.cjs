// Playthrough of the farming loop, the farmer standing on one tile (docs/design/critique-farming.md). Needs a Development server on ws://localhost:57300
// and a tester client (-p:NosumDevTools=true) on http://localhost:5080. Weather is rolled per run, so rain can refuse the can on some runs.
// Plays the farming loop with the farmer standing still on one tile: till, sow, water, water each game day, harvest, then a wide use on the
// bare line. Usage: node tests/browser/farm-loop-play.cjs <w> <h> <locale> <outDir>; locale en-US sets the English UI.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const [w, h, locale, outDir] = [Number(process.argv[2]), Number(process.argv[3]), process.argv[4], process.argv[5]];
const wait = ms => new Promise(r => setTimeout(r, ms));
const log = [];
fs.mkdirSync(outDir, { recursive: true });
(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: w, height: h }, locale });
  page.on('pageerror', e => log.push('PAGEERROR ' + e.message.slice(0, 160)));
  // The UI language is a client setting (ClientSettings, lang: 1 Vietnamese, 2 English), not the browser locale.
  if (locale.startsWith('en')) await page.addInitScript(() => { try { localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch (e) {} });
  await page.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await wait(2500);
  const guest = page.getByRole('button', { name: /khách|guest/i }).first();
  if (await guest.count()) { await guest.click(); }
  await page.waitForSelector('[role=meter]', { timeout: 150000 });
  await wait(2000);
  const toast = () => page.evaluate(() => document.querySelector('.game-message')?.innerText?.replace(/\n/g, ' ') ?? '');
  const shot = async (name, note, clip) => {
    const opts = { path: path.join(outDir, name + '.png') };
    if (clip) opts.clip = clip;
    await page.screenshot(opts);
    log.push(`${name}: ${note} | toast="${await toast()}"`);
  };
  const press = async (key) => { await page.keyboard.press(key); await wait(600); };
  const step = async (key, ms = 330) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); await wait(300); };
  const dev = async (days) => page.evaluate(async (days) => {
    const a = await getDotnetRuntime(0).getAssemblyExports('Nosum.Wasm.dll');
    a.Nosum.Wasm.Game.DevTools.AdvanceClock(days);
  }, days).catch(e => log.push('DEV ERR ' + String(e).slice(0, 100)));
  const zoom = { x: 520, y: 260, width: 240, height: 280 };
  const count = { uses: 0 };
  const use = async (key, note) => { count.uses++; await press(key); return note; };

  await page.keyboard.press('Digit1'); await wait(300);
  await step('KeyS');
  await shot('01-facing-down', 'walked one tile south, hoe selected (the farmer now stays on this tile)', zoom);
  await use('KeyE'); await wait(700);
  await shot('02-till', 'E with hoe: tile in front tilled (presses so far: ' + count.uses + ')', zoom);
  await use('KeyE'); await wait(700);
  await shot('03-till-again', 'E with hoe on the tilled tile', zoom);
  await page.keyboard.press('Digit3'); await wait(300); await use('KeyE'); await wait(700);
  await shot('04-sow', 'seeds selected, E: sown (presses so far: ' + count.uses + ')', zoom);
  await page.keyboard.press('Digit2'); await wait(300); await use('KeyE'); await wait(700);
  await shot('05-water-day0', 'can selected, E: watered on day 0', zoom);
  await use('KeyE'); await wait(700);
  await shot('06-water-again-same-day', 'E with can on the same wet tile', zoom);
  // Four game days, watered each day (ADR-021 D7): the crop grows one stage per watered day.
  for (let d = 1; d <= 4; d++) {
    await dev(1); await wait(2000);
    await shot(`07-day${d}-before-water`, `day ${d} begins (crop before watering)`, zoom);
    await use('KeyE'); await wait(700);
    await shot(`08-day${d}-watered`, `watered on day ${d}`, zoom);
  }
  await shot('09-grown-full', 'after four watered days: full view', null);
  await page.keyboard.press('Digit8'); await wait(300); await use('KeyE'); await wait(900);
  await shot('10-harvest-scythe', 'scythe selected, E on the crop: harvested?', zoom);
  // The copper hoe: granted to slot 9 by the tester tool, selected, then one wide swing over the bare line.
  await page.evaluate(async () => {
    const a = await getDotnetRuntime(0).getAssemblyExports('Nosum.Wasm.dll');
    a.Nosum.Wasm.Game.DevTools.Grant(10, 1, 0);
  });
  await wait(1500);
  await page.keyboard.press('Digit9'); await wait(400);
  await press('KeyR'); await wait(900);
  await shot('11-wide-hoe', 'copper hoe, R: wide swing over the line in front (1 bare + 2 untilled)', zoom);
  fs.writeFileSync(path.join(outDir, 'playlog.txt'), log.join('\n') + '\n');
  console.log(log.join('\n'));
  await browser.close();
})().catch(e => { console.error('FAILED', e); process.exit(1); });
