// The bus stop's direction label next to the local player's sprite at the farm door (critique finding 5; docs/adr/ADR-097). Vietnamese and
// English at 1280x720. Needs the Development host on PostgreSQL and a client published with -p:NosumDevTools=true (window.nosumQa), served at
// the base URL with ?qa=1. Writes <outDir>/bus-stop-label-<lang>-1280x720.png.
// Run: NODE_PATH=$(npm root -g) node tests/browser/bus-stop-label-capture.cjs <outDir> [base url]
const { chromium } = require('playwright');
const fs = require('fs');

const outDir = process.argv[2];
const base = process.argv[3] || 'http://localhost:5081/?qa=1';
if (!outDir) {
  throw new Error('Usage: node bus-stop-label-capture.cjs <outDir> [base url]');
}
fs.mkdirSync(outDir, { recursive: true });
const wait = ms => new Promise(r => setTimeout(r, ms));

// The farm door front (round 3 check, FARM_DOOR_FRONT): where the bus stop's direction label sits beside the farmer.
const DOOR_FRONT = [64, 16];
const LANGS = {
  vi: { locale: 'vi-VN', language: /Tiếng Việt/, guest: /Chơi ngay với tư cách khách/ },
  en: { locale: 'en-US', language: /^English$/, guest: /Play now as a guest/ },
};

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const errors = [];
  for (const [lang, cfg] of Object.entries(LANGS)) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: cfg.locale, serviceWorkers: 'block' });
    const page = await context.newPage();
    page.on('pageerror', e => errors.push(`${lang}: ${e.message.slice(0, 200)}`));
    await page.goto(base, { waitUntil: 'load' });
    await wait(2500);
    const language = page.getByRole('button', { name: cfg.language });
    if (await language.count()) {
      await language.first().click();
      await wait(800);
    }
    await page.getByRole('button', { name: cfg.guest }).first().click();
    await page.waitForSelector('[role=meter][aria-label^="Sức lực"], [role=meter][aria-label^="Stamina"], [role=meter]', { timeout: 120000 });
    await wait(1500);
    await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), DOOR_FRONT);
    await wait(3000);
    await page.screenshot({ path: `${outDir}/bus-stop-label-${lang}-1280x720.png` });
    await context.close();
  }
  console.log(JSON.stringify({ outDir, pageErrors: errors }, null, 2));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
