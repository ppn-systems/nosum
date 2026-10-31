// QA run for issue #248 (docs/adr/ADR-079 amendment D5, docs/adr/ADR-078 amendment, protocol 23): sign in as a guest on a Development build
// with ?qa=1, pass one season with window.nosumQa.season(1), and check that the calendar label and the season banner follow the server clock
// without a reload. Then open the Skills page and check the fifth row (Fishing). Screenshots of the banner and the Skills page per viewport.
// Run: node tests/browser/season-fix-capture.cjs <base url> <out dir> <label>
//   base url, default http://localhost:5080/ ; out dir, default docs/qa/screens/season-fix ; label, e.g. "before" or "after" (file name prefix).
// Needs Playwright with Chromium installed (no `playwright install`). Exits 1 when the season does not turn on the page within 20 s.
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/screens/season-fix'));
const label = process.argv[4] || 'run';
const viewports = [
  { name: '1280x720', width: 1280, height: 720 },
  { name: '1920x1080', width: 1920, height: 1080 },
  { name: '390x844', width: 390, height: 844, mobile: true },
];
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

async function seasonText(page) {
  return (await page.locator('.calendar-line .calendar-season').first().innerText().catch(() => '')).trim();
}

async function bannerText(page) {
  const banner = page.locator('.season-banner');
  if ((await banner.count()) === 0) {
    return null;
  }
  return (await banner.first().innerText().catch(() => '')).replace(/\s+/g, ' ').trim();
}

async function openSkills(page) {
  const direct = page.locator('[aria-label="Kỹ năng"]').first();
  if ((await direct.count()) === 0) {
    // Narrow layouts keep the skills under the Menu button (docs/design/ui-standard.md section 6).
    await page.getByRole('button', { name: /^(Menu|Thực đơn)$/ }).first().click();
    await page.waitForTimeout(400);
  }
  await page.locator('[aria-label="Kỹ năng"], button:has-text("Kỹ năng")').first().click();
  await page.waitForSelector('.skill-row', { timeout: 10000 });
  await page.waitForTimeout(500);
}

async function run() {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch();
  const results = [];
  let failed = false;

  for (const vp of viewports) {
    const context = await browser.newContext({
      viewport: { width: vp.width, height: vp.height },
      locale: 'vi-VN',
      isMobile: vp.mobile === true,
      hasTouch: vp.mobile === true,
    });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    page.on('console', (m) => {
      if (m.type() === 'error') errors.push(m.text());
    });

    await page.goto(base.replace(/\/?$/, '/') + '?qa=1', { waitUntil: 'load' });
    await page.getByRole('button', { name: /khách|guest/i }).first().click();
    await page.waitForSelector('canvas', { timeout: 60000 });
    await page.waitForSelector('.calendar-line', { timeout: 30000 });
    await page.waitForFunction(() => globalThis.nosumQa !== undefined, null, { timeout: 30000 });
    await page.waitForTimeout(3000);

    const before = await seasonText(page);
    await page.evaluate(() => globalThis.nosumQa.season(1));
    const started = Date.now();

    let after = before;
    let bannerAt = null;
    let banner = null;
    while (Date.now() - started < 20000) {
      after = await seasonText(page);
      banner = await bannerText(page);
      if (banner && bannerAt === null) bannerAt = Date.now() - started;
      if (after !== before && bannerAt !== null) break;
      await page.waitForTimeout(250);
    }
    const labelAt = after !== before ? Date.now() - started : null;

    if (banner) {
      await page.screenshot({ path: path.join(outDir, `${label}-season-banner-${vp.name}.png`) });
    }

    await openSkills(page);
    // The Skills section lists farming, foraging, mining, combat and then fishing (docs/adr/ADR-078 amendment): fishing is the fifth row.
    const rows = (await page.locator('.skill-row .skill-name').allInnerTexts()).map((t) => t.trim()).slice(0, 5);
    const fishingRow = rows[4] === 'Câu cá';
    await page.screenshot({ path: path.join(outDir, `${label}-skills-panel-${vp.name}.png`) });

    const ok = after !== before && banner !== null;
    if (!ok) failed = true;
    const row = { viewport: vp.name, before, after, labelAfterMs: labelAt, bannerAfterMs: bannerAt, banner, skillNames: rows, fishingRow, errors: errors.slice(0, 5) };
    results.push(row);
    log(JSON.stringify(row));
    await context.close();
  }

  await browser.close();
  fs.writeFileSync(path.join(outDir, `${label}-results.json`), JSON.stringify(results, null, 2));
  process.exitCode = failed ? 1 : 0;
}

run().catch((e) => {
  log('ERROR', e.message);
  process.exitCode = 1;
});
