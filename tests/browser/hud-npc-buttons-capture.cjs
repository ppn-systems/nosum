// Captures the HUD, the tailor and board keeper dialogs, the backpack page at 36, 45 and 54 slots, the inventory at each size and the Settings panel
// (the button set) at one viewport (docs/adr/ADR-081, docs/design/ui-standard.md). Needs the Development host on PostgreSQL and a client published with
// -p:NosumDevTools=true, served at the base URL with ?qa=1. The farmer is a guest, moved with window.nosumQa.teleport; coins come from the dev grant.
// Run: NODE_PATH=$(npm root -g) node tests/browser/hud-npc-buttons-capture.cjs <outDir> [base url] [WxH]
const { chromium } = require('playwright');
const fs = require('fs');

const outDir = process.argv[2];
const base = process.argv[3] || 'http://localhost:5080/';
const [viewportWidth, viewportHeight] = (process.argv[4] || '1280x720').split('x').map(Number);
if (!outDir) {
  throw new Error('Usage: node hud-npc-buttons-capture.cjs <outDir> [base url] [WxH]');
}
fs.mkdirSync(outDir, { recursive: true });
const tag = `${viewportWidth}x${viewportHeight}`;
const GUEST = /Chơi ngay với tư cách khách/;
const TAILOR_TILE = [20, 89];

const shot = async (page, name) => {
  await page.waitForTimeout(700);
  await page.screenshot({ path: `${outDir}/${name}-${tag}.png` });
};

(async () => {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: viewportWidth, height: viewportHeight }, locale: 'vi-VN', hasTouch: viewportWidth < 500 });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push('pageerror: ' + e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await page.getByRole('button', { name: GUEST }).first().click({ timeout: 120000 });
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForFunction(() => !!globalThis.nosumQa, null, { timeout: 60000 });
  await page.waitForTimeout(4000);
  await shot(page, 'hud');

  // The tailor's dialog box and its backpack page.
  await page.evaluate(([x, y]) => globalThis.nosumQa.teleport('town', x, y), TAILOR_TILE);
  await page.waitForTimeout(1500);
  await page.keyboard.press('KeyE');
  await page.waitForTimeout(3500);
  await shot(page, 'tailor-dialog');

  // Coins for the upgrades: the dev grant asks the Development server (no item, 2,000 coins).
  await page.evaluate(async () => {
    const exports = await globalThis.getDotnetRuntime(0).getAssemblyExports('Nosum.Wasm.dll');
    exports.Nosum.Wasm.Game.DevTools.Grant(0, 0, 2000);
  });
  await page.waitForTimeout(1500);

  const box = page.locator('.npc-box');
  const backpackButton = box.getByRole('button', { name: 'Túi đồ', exact: true });
  if (await backpackButton.count()) {
    await backpackButton.click();
    await page.waitForTimeout(1500);
    await shot(page, 'backpack-36');
  }

  for (const size of ['45', '54']) {
    const upgrade = page.getByRole('button', { name: 'Nâng cấp túi', exact: true });
    if (await upgrade.count()) {
      await upgrade.click();
      await page.waitForTimeout(2000);
    }
    await shot(page, `backpack-${size}`);
  }

  // The inventory at the largest size.
  await page.keyboard.press('Escape');
  await page.waitForTimeout(600);
  await page.keyboard.press('KeyI');
  await page.waitForTimeout(1200);
  await shot(page, 'inventory-54');

  // The button set: the Settings panel (H), then the HUD again.
  await page.keyboard.press('Escape');
  await page.waitForTimeout(600);
  await page.keyboard.press('KeyH');
  await page.waitForTimeout(1200);
  await shot(page, 'settings-buttons');
  await page.keyboard.press('Escape');

  fs.writeFileSync(`${outDir}/errors-${tag}.json`, JSON.stringify(errors, null, 2));
  await browser.close();
  console.log(`captured ${tag}; ${errors.length} console errors`);
})().catch((e) => { console.error(e); process.exit(1); });
