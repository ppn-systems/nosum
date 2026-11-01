// Carpenter build capture (docs/design/critique-placement.md, finding 7). Signs in as a guest on a client built with -p:NosumDevTools=true and
// opened with ?qa=1 (wasm/README.md, window.nosumQa). It grants the Coop blueprint (item 610, the carpenter's sale; the counter purchase itself is not
// played here), teleports to the farm, selects the blueprint in hotbar slot 9 and plays the build: the ghost, the first E (the confirmation on the
// strip), the second E on the same tile (the build), then Q with nothing in front (the reason line). Frames go to
// docs/qa/placement-round2/<phase>/build-<viewport>-<lang>/.
//
// Needs the server (DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host) and the client
// (dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080).
// usage: node tests/browser/carpenter-build-capture.cjs <before|after> [desktop|wide|phone] [vi|en]   (no viewport/lang = all)

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const VIEWPORTS = { desktop: { width: 1280, height: 720 }, wide: { width: 1920, height: 1080 }, phone: { width: 390, height: 844 } };
const COOP_BLUEPRINT = 610;
const HOTBAR_SLOT_KEY = '9';

async function run(browser, phase, vp, lang) {
  const size = VIEWPORTS[vp];
  const phone = vp === 'phone';
  const dir = path.join(REPO, process.env.PLACEMENT_QA_ROOT || 'docs/qa/placement-round2', phase, `build-${vp}-${lang}`);
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
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await page.waitForTimeout(3000);

  const shot = async (name) => { await page.waitForTimeout(700); await page.screenshot({ path: path.join(dir, `${name}.png`) }); };
  const strip = () => page.evaluate(() => document.querySelector('.game-notice')?.textContent?.trim() ?? null);
  const notes = {};

  await page.evaluate((id) => window.nosumQa.grant(id, 1, 0), COOP_BLUEPRINT);
  await page.waitForTimeout(800);
  await page.evaluate(() => window.nosumQa.teleport('farm', 64, 22));
  await page.waitForTimeout(3000);
  await page.keyboard.press(HOTBAR_SLOT_KEY);
  await page.waitForTimeout(500);
  await shot('00-blueprint-held');
  notes.ghost = await strip();

  // Walk one tile down so the ghost sits in open ground in front of the farmer, then aim the footprint.
  await page.keyboard.down('s');
  await page.waitForTimeout(200);
  await page.keyboard.up('s');
  await page.waitForTimeout(300);
  await shot('01-ghost-on-farm');
  notes.ghostNotice = await strip();

  await page.keyboard.press('e');          // the first press only asks
  await shot('02-first-press-confirm');
  notes.confirm = await strip();

  await page.keyboard.press('e');          // the second press on the same tile builds
  await page.waitForTimeout(1200);
  await shot('03-second-press-built');
  notes.afterBuild = await strip();

  await page.keyboard.press('q');          // nothing held, nothing in front: a reason, not silence
  await shot('04-stow-nothing-in-front');
  notes.stowNothing = await strip();

  fs.writeFileSync(path.join(dir, 'notes.json'), JSON.stringify({ viewport: vp, lang, ...notes }, null, 2) + '\n');
  fs.writeFileSync(path.join(dir, 'errors.txt'), errors.slice(0, 10).join('\n') + '\n');
  await context.close();
}

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: carpenter-build-capture.cjs <before|after> [desktop|wide|phone] [vi|en]');
  const vps = process.argv[3] ? [process.argv[3]] : Object.keys(VIEWPORTS);
  const langs = process.argv[4] ? [process.argv[4]] : ['vi', 'en'];
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const vp of vps) for (const lang of langs) await run(browser, phase, vp, lang);
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
