// Placement capture (docs/qa/rebuild-placement). Signs in as a guest, grants furniture through the Development QA hook (a client built with
// -p:NosumDevTools=true, wasm/README.md), walks into the farmhouse and plays one placement run: held ghost, valid spot, place, rotate, an invalid
// spot, stow, and a place-then-stow for the pick-up confirmation. Frames go to docs/qa/rebuild-placement/<phase>/<viewport>-<lang>/.
//
// Needs the server (DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host) and the client
// (dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true, http://localhost:5080).
// usage: node tests/browser/placement-capture.cjs <before|after> [desktop|phone] [vi|en]   (no viewport/lang = all four)
// TAP_PLACE=1 also taps the strip's Place button once (the touch check).

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/';
const VIEWPORTS = { desktop: { width: 1280, height: 720 }, wide: { width: 1920, height: 1080 }, phone: { width: 390, height: 844 } };

async function grant(page, items) {
  await page.evaluate(async (list) => {
    const ex = await (await globalThis.getDotnetRuntime(0)).getAssemblyExports('Nosum.Wasm.dll');
    for (const [id, q, c] of list) ex.Nosum.Wasm.Game.DevTools.Grant(id, q, c);
  }, items);
}

async function hold(page, key, ms) {
  await page.keyboard.down(key);
  await page.waitForTimeout(ms);
  await page.keyboard.up(key);
  await page.waitForTimeout(150);
}

async function run(browser, phase, vp, lang) {
  const size = VIEWPORTS[vp];
  const phone = vp === 'phone';
  const dir = path.join(REPO, process.env.PLACEMENT_QA_ROOT || 'docs/qa/rebuild-placement', phase, `${vp}-${lang}`);
  fs.mkdirSync(dir, { recursive: true });
  const context = await browser.newContext({ viewport: size, locale: lang === 'en' ? 'en-US' : 'vi-VN', hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  // The language is read from the client settings at start-up: lang=1 Vietnamese, lang=2 English (ClientSettings.Parse).
  await context.addInitScript((code) => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + code + '\n'); } catch (e) { /* no storage */ } }, lang === 'en' ? 2 : 1);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 160)}`); });
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 60000 });
  await page.waitForTimeout(3000);

  const shot = async (name) => { await page.waitForTimeout(700); await page.screenshot({ path: path.join(dir, `${name}.png`) }); };
  await grant(page, [[300, 5, 0]]);
  await page.waitForTimeout(800);
  await shot('00-arrive');
  await hold(page, 'w', 120);
  await page.keyboard.press('e');          // the door: enter the farmhouse
  await page.waitForTimeout(1500);
  await hold(page, 'w', 700);
  await page.keyboard.press('9');         // the chair slot
  await page.waitForTimeout(400);
  await shot('01-held-ghost');
  await hold(page, 'd', 350);
  await shot('02-ghost-open-floor');      // valid spot
  await page.keyboard.press('e');
  await shot('03-after-place');
  await page.keyboard.press('r');
  await shot('04-after-rotate');
  await hold(page, 'w', 350);
  await shot('05-ghost-over-bed-or-table'); // invalid spot
  await page.keyboard.press('q');
  await shot('06-after-stow');
  if (process.env.TAP_PLACE === '1') {
    await grant(page, [[300, 1, 0]]);
    await page.waitForTimeout(700);
    await hold(page, 'a', 260);
    await page.locator('.strip-row button').first().click({ force: true });
    await shot('07-tapped-place');
  }
  await page.keyboard.press('e');          // place, then stow it for the pick-up confirmation
  await page.waitForTimeout(600);
  await page.keyboard.press('q');
  await shot('08-stow-confirmation');
  fs.writeFileSync(path.join(dir, 'errors.txt'), errors.slice(0, 10).join('\n') + '\n');
  await context.close();
}

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: placement-capture.cjs <before|after> [desktop|phone] [vi|en]');
  const vps = process.argv[3] ? [process.argv[3]] : Object.keys(VIEWPORTS);
  const langs = process.argv[4] ? [process.argv[4]] : ['vi', 'en'];
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const vp of vps) for (const lang of langs) await run(browser, phase, vp, lang);
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
