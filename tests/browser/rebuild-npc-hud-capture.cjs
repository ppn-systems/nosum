// A normal-day tour for the NPC, quest, journal and HUD review (docs/design/critique-npc-hud.md): the farm HUD, the quest tracker,
// the journal with its All / Found / Missing tabs, and one merchant's greeting and two Talk lines. Vietnamese and English.
// Needs the Development host on PostgreSQL and a client published with -p:NosumDevTools=true, served at the base URL with ?qa=1.
// A phone size (390x844) uses an iPhone 14 profile, so the game shows its touch controls (Device:BlockTouchDevices is false).
// Writes <outDir>/<step>-<lang>-<WxH>.png and <outDir>/tour.json (the texts read back, and the console errors).
// Run: NODE_PATH=$(npm root -g) node tests/browser/rebuild-npc-hud-capture.cjs <outDir> [base url] [WxH, default 1280x720]
const { chromium, devices } = require('playwright');
const fs = require('fs');
const path = require('path');

const outDir = process.argv[2];
const base = process.argv[3] || 'http://localhost:5080/';
const size = process.argv[4] || '1280x720';
if (!outDir) {
  throw new Error('Usage: node rebuild-npc-hud-capture.cjs <outDir> [base url] [WxH]');
}
fs.mkdirSync(outDir, { recursive: true });
const [width, height] = size.split('x').map(Number);
const phone = width <= 430;

// The counter tile of the seed keeper in Town (the same tile npc-dialogue-capture.cjs uses).
const SEED_KEEPER = [43, 57];
const GUEST = { vi: /Chơi ngay với tư cách khách/, en: /Play now as a guest/ };
const LANG = {
  vi: { english: null, all: 'Tất cả', found: 'Đã có', missing: 'Chưa có' },
  en: { english: 'English', all: 'All', found: 'Found', missing: 'Missing' },
};

async function shot(page, name, lang, log) {
  const file = path.join(outDir, `${name}-${lang}-${size}.png`);
  await page.screenshot({ path: file });
  log.shots.push(path.basename(file));
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const options = phone
    ? { ...devices['iPhone 14'], viewport: { width, height }, locale: 'vi-VN' }
    : { viewport: { width, height }, locale: 'vi-VN' };
  const context = await browser.newContext(options);
  const page = await context.newPage();
  const log = { size, shots: [], texts: {}, errors: [] };
  page.on('pageerror', (e) => log.errors.push('pageerror: ' + e.message));
  page.on('console', (m) => { if (m.type() === 'error') log.errors.push(m.text()); });

  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await page.getByRole('button', { name: GUEST.vi }).first().click({ timeout: 120000 });
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForFunction(() => !!globalThis.nosumQa, null, { timeout: 60000 });
  await page.waitForTimeout(4000);

  for (const lang of ['vi', 'en']) {
    const t = LANG[lang];
    if (lang === 'en') {
      await page.keyboard.press('KeyH');
      await page.waitForTimeout(600);
      await page.getByRole('button', { name: t.english }).first().click();
      await page.waitForTimeout(800);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(800);
    }

    // 1. The farm on a normal day: the HUD, the quest tracker with its tip.
    await shot(page, 'hud-farm', lang, log);

    // 2. The journal: All, then Found, then Missing (the tabs).
    await page.keyboard.press('KeyJ');
    await page.waitForTimeout(1200);
    await shot(page, 'journal-all', lang, log);
    // The Found and Missing tabs exist from the filter build on; the baseline build has none, so its tour ends at All.
    log.texts[`tabs-${lang}`] = await page.locator('.journal-filters').count();
    if (log.texts[`tabs-${lang}`] > 0) {
      await page.getByRole('button', { name: t.found, exact: true }).first().click();
      await page.waitForTimeout(500);
      await shot(page, 'journal-found', lang, log);
      await page.getByRole('button', { name: t.missing, exact: true }).first().click();
      await page.waitForTimeout(500);
      await shot(page, 'journal-missing', lang, log);
    }
    log.texts[`journal-${lang}`] = await page.locator('.journal-summary').first().innerText().catch(() => '');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(800);

    // 3. One merchant: the greeting, then two Talk lines (the dialogue repetition check).
    await page.evaluate(([x, y]) => globalThis.nosumQa.teleport('town', x, y), SEED_KEEPER);
    await page.waitForTimeout(1500);
    await page.keyboard.press('KeyE');
    await page.waitForTimeout(1500);
    await page.locator('.npc-line').first().click({ timeout: 3000 }).catch(() => {});
    await page.waitForTimeout(400);
    log.texts[`greeting-${lang}`] = await page.locator('.npc-box-text').first().innerText().catch(() => '');
    await shot(page, 'npc-greeting', lang, log);
    for (let talk = 1; talk <= 2; talk++) {
      await page.getByRole('button', { name: lang === 'vi' ? 'Trò chuyện' : 'Talk', exact: true }).first().click({ timeout: 5000 }).catch(() => {});
      await page.waitForTimeout(1500);
      await page.locator('.npc-line').first().click({ timeout: 3000 }).catch(() => {});
      await page.waitForTimeout(400);
      log.texts[`talk${talk}-${lang}`] = await page.locator('.npc-box-text').first().innerText().catch(() => '');
      await shot(page, `npc-talk${talk}`, lang, log);
    }
    await page.keyboard.press('Escape');
    await page.waitForTimeout(800);
  }

  fs.writeFileSync(path.join(outDir, `tour-${size}.json`), JSON.stringify(log, null, 2));
  await browser.close();
  console.log(JSON.stringify({ size, shots: log.shots.length, errors: log.errors.length }));
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
