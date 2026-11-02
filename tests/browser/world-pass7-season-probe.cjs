// World QA pass 7, issue 3: does the season label follow a QA season jump? (docs/qa/world-pass7/season-label/)
// For each case it jumps the server clock, then samples the calendar label (.calendar-season) at several delays after the call.
// Usage: node tests/browser/world-pass7-season-probe.cjs [base=http://localhost:5080/?qa=1] [outDir]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const base = process.argv[2] || 'http://localhost:5080/?qa=1';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/world-pass7/season-label'));
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'en-US' });
  await context.addInitScript(value => { try { localStorage.setItem('nosum.settings.v1', value); } catch (e) {} }, 'lang=2\n');
  const page = await context.newPage();
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 });
  await wait(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.calendar-season', { timeout: 90000 });
  await wait(2500);
  const label = () => page.$eval('.calendar-season', e => e.textContent.trim()).catch(() => '?');
  const results = [];
  const trial = async (name, action) => {
    const before = await label();
    await action();
    const samples = [];
    const t0 = Date.now();
    for (const at of [0, 300, 1000, 2000, 4000]) {
      await wait(Math.max(0, at - (Date.now() - t0)));
      samples.push(`${at}ms:${await label()}`);
    }
    results.push({ name, before, samples });
    console.log(name, 'before', before, '|', samples.join(' '));
  };
  await trial('season(2) to Fall', () => page.evaluate(() => window.nosumQa.season(2)).then(() => wait(0)));
  await wait(2000);
  await trial('springReset from Fall', () => page.evaluate(() => window.nosumQa.springReset()));
  await wait(2000);
  await trial('season(1) to Summer', () => page.evaluate(() => window.nosumQa.season(1)));
  await wait(2000);
  await trial('springReset from Summer', () => page.evaluate(() => window.nosumQa.springReset()));
  fs.writeFileSync(path.join(outDir, 'season-label.json'), JSON.stringify(results, null, 2));
  await page.screenshot({ path: path.join(outDir, 'after-spring-reset.png') });
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
