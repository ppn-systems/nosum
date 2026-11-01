// Captures every merchant's dialogue box in Town at 1280x720 (docs/adr/ADR-080, the Development QA hook of docs/adr/ADR-079).
// Needs the Development host on PostgreSQL and a client published with -p:NosumDevTools=true, served at the base URL with ?qa=1.
// The farmer is a guest (no sign-in), moved with window.nosumQa.teleport onto each counter; the action key (E) opens the box.
// Writes <outDir>/<merchant>-<lang>.png and <outDir>/lines.json (the line, the buttons and the reveal state of each box).
// Run: NODE_PATH=$(npm root -g) node tests/browser/npc-dialogue-capture.cjs <outDir> [base url] [WxH, default 1280x720]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const outDir = process.argv[2];
const base = process.argv[3] || 'http://localhost:5080/';
const [viewportWidth, viewportHeight] = (process.argv[4] || '1280x720').split('x').map(Number);
if (!outDir) {
  throw new Error('Usage: node npc-dialogue-capture.cjs <outDir> [base url]');
}
fs.mkdirSync(outDir, { recursive: true });

// The counter tile of each merchant in Town (GameMaps, MineWorld.BlacksmithCounter); the farmer stands on it, as the catalogue places the merchant beside it.
const MERCHANTS = [
  { id: 'seed_keeper', tile: [43, 57] },
  { id: 'tailor', tile: [20, 89] },
  { id: 'furnisher', tile: [95, 51] },
  { id: 'blacksmith', tile: [94, 82] },
  { id: 'carpenter', tile: [10, 86] },
  { id: 'trader', tile: [45, 71] },
  { id: 'board_keeper', tile: [42, 57] },
];

const GUEST = { vi: /Chơi ngay với tư cách khách/, en: /Play now as a guest/ };
const BUY = { vi: 'Mua', en: 'Buy' };

(async () => {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: viewportWidth, height: viewportHeight }, locale: 'vi-VN' });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await page.getByRole('button', { name: GUEST.vi }).first().click({ timeout: 120000 });
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForFunction(() => !!globalThis.nosumQa, null, { timeout: 60000 });
  await page.waitForTimeout(4000);

  const report = {};
  for (const lang of ['vi', 'en']) {
    if (lang === 'en') {
      // Settings (H): the language is the first choice of the settings page.
      await page.keyboard.press('KeyH');
      await page.waitForTimeout(600);
      await page.getByRole('button', { name: 'English' }).first().click();
      await page.waitForTimeout(800);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(800);
    }

    for (const merchant of MERCHANTS) {
      await page.evaluate(([x, y]) => globalThis.nosumQa.teleport('town', x, y), merchant.tile);
      await page.waitForTimeout(1500);
      await page.keyboard.press('KeyE');
      await page.waitForSelector('.npc-box', { timeout: 10000 });
      // The line types out; a click on it finishes it (issue #214), then the buttons show.
      await page.click('.npc-line');
      await page.waitForTimeout(400);
      const box = await page.evaluate(() => ({
        name: document.querySelector('.npc-box .game-panel-title')?.textContent?.trim() ?? '',
        role: document.querySelector('.npc-role')?.textContent?.trim() ?? '',
        line: document.querySelector('.npc-line')?.textContent?.replace(/\s+/g, ' ').trim() ?? '',
        buttons: [...document.querySelectorAll('.npc-actions button')].map((b) => b.textContent.trim()),
        revealing: !!document.querySelector('.npc-line-typing'),
      }));
      report[`${merchant.id}-${lang}`] = box;
      await page.screenshot({ path: path.join(outDir, `${merchant.id}-${lang}.png`) });
      if (merchant.id === 'carpenter') {
        // Buy opens the counter's shop as a dialog of its own: the supplies, then the buildings and animals (docs/adr/ADR-028, ADR-043).
        await page.getByRole('button', { name: BUY[lang] }).click();
        await page.waitForTimeout(900);
        await page.screenshot({ path: path.join(outDir, `carpenter-shop-${lang}.png`) });
        report[`carpenter-shop-${lang}`] = await page.evaluate(() => [...document.querySelectorAll('.trade-window .shop-row span:nth-child(2)')].map((e) => e.textContent.trim()));
        await page.locator('.trade-window .game-close').click();
        await page.waitForTimeout(600);
      }
      await page.keyboard.press('Escape');
      await page.waitForTimeout(600);
    }
  }

  fs.writeFileSync(path.join(outDir, 'lines.json'), JSON.stringify({ report, errors }, null, 2));
  console.log(JSON.stringify({ merchants: Object.keys(report).length, errors: errors.length }));
  await browser.close();
})().catch((e) => {
  console.error(e);
  process.exitCode = 1;
});
