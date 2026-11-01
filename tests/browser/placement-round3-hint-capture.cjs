// Farmhouse hint capture (docs/design/critique-placement.md, finding 13). A guest walks from the farm into the farmhouse and waits for the
// first-run hint line: a tip about the farm (the hoe, the crops) must not show indoors. Frames and the hint text go to
// <PLACEMENT_QA_ROOT or docs/qa/placement-round3>/<phase>/hint-<vp>-<lang>/ (hint.json).
//
// Needs the server (DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host) and the client
// (dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080).
// usage: node tests/browser/placement-round3-hint-capture.cjs <before|after> [desktop|wide|phone] [vi|en]   (no viewport/lang = all)

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const VIEWPORTS = { desktop: { width: 1280, height: 720 }, wide: { width: 1920, height: 1080 }, phone: { width: 390, height: 844 } };
const GUEST = { vi: /khách/i, en: /guest/i };
const DOOR_FRONT = [64, 16];   // the tile in front of the farmhouse door (FarmProps.FarmhouseDoorFront)

async function hold(page, key, ms) {
  await page.keyboard.down(key);
  await page.waitForTimeout(ms);
  await page.keyboard.up(key);
  await page.waitForTimeout(150);
}

async function run(browser, phase, vp, lang) {
  const size = VIEWPORTS[vp];
  const phone = vp === 'phone';
  const dir = path.join(REPO, process.env.PLACEMENT_QA_ROOT || 'docs/qa/placement-round3', phase, `hint-${vp}-${lang}`);
  fs.mkdirSync(dir, { recursive: true });
  const context = await browser.newContext({ viewport: size, locale: lang === 'en' ? 'en-US' : 'vi-VN', hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  await context.addInitScript((code) => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + code + '\n'); } catch (e) { /* no storage */ } }, lang === 'en' ? 2 : 1);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 160)}`); });
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: GUEST[lang] }).first().click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await page.waitForTimeout(2500);
  const hintText = () => page.evaluate(() => document.querySelector('.hint-line')?.textContent?.trim() ?? null);
  const hint = {};

  // On the farm first: the hint for the farm is what a new player sees (the walk counts as the first step).
  await page.waitForTimeout(3500);
  hint.farm = await hintText();
  await page.screenshot({ path: path.join(dir, '01-farm-hint.png') });

  await page.evaluate(([x, y]) => window.nosumQa.teleport('farm', x, y), DOOR_FRONT);
  await page.waitForTimeout(2000);
  await hold(page, 'w', 150);
  await page.keyboard.press('e');          // the door: enter the farmhouse
  await page.waitForTimeout(1500);
  await hold(page, 'w', 700);
  // The arrange panel opens indoors (the own house is an arrange room); Escape puts it away, and the hint line is what is left to see.
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  // The hints wait for the player to be idle; a new one comes up two seconds after the last key.
  await page.waitForTimeout(3500);
  hint.farmhouse = await hintText();
  await page.screenshot({ path: path.join(dir, '02-farmhouse-hint.png') });

  fs.writeFileSync(path.join(dir, 'hint.json'), JSON.stringify({ viewport: vp, lang, ...hint }, null, 2) + '\n');
  fs.writeFileSync(path.join(dir, 'errors.txt'), errors.slice(0, 10).join('\n') + '\n');
  await context.close();
}

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: placement-round3-hint-capture.cjs <before|after> [desktop|wide|phone] [vi|en]');
  const vps = process.argv[3] ? [process.argv[3]] : Object.keys(VIEWPORTS);
  const langs = process.argv[4] ? [process.argv[4]] : ['vi', 'en'];
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const vp of vps) for (const lang of langs) await run(browser, phase, vp, lang);
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
