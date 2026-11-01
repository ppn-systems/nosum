// HUD info capture (docs/design/research-hud-and-loops.md, backlog items 1, 3, 4 and 7): screenshots of the toast line, the energy
// tooltip and the Today card, in Vietnamese and English, at 1280x720, 1920x1080 and 390x844. Run once on master (before) and once on the branch (after).
//
// Usage:
//   BASE_URL=http://localhost:5830 OUT_DIR=/path/to/docs/qa/hud-info/after node capture-hud-info.cjs
// Optional: LANGS=vi,en VIEWPORTS=1280x720,1920x1080,390x844
// Writes OUT_DIR/<lang>/<WxH>/<screen>.png and OUT_DIR/<lang>/capture-report.json (captured and not captured, with reasons).
// The shop screen is not driven by this script: a merchant has to be reached on the map (see the report in docs/qa/hud-info/README.md).
// Requires `require('playwright')` resolvable from here (Chromium preinstalled; no new dependency).

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5830').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || path.join(__dirname, 'after'));
// GameLanguage values on the client: Vietnamese = 1, English = 2 (the settings key holds the number).
const LANGS = { vi: 'lang=1\n', en: 'lang=2\n' };
const ALL_VIEWPORTS = [
  { w: 1280, h: 720, mobile: false },
  { w: 1920, h: 1080, mobile: false },
  { w: 390, h: 844, mobile: true },
];
const wantedLangs = (process.env.LANGS || 'vi,en').split(',').map((s) => s.trim()).filter(Boolean);
const wanted = (process.env.VIEWPORTS || '').split(',').map((s) => s.trim()).filter(Boolean);
const VIEWPORTS = wanted.length ? ALL_VIEWPORTS.filter((v) => wanted.includes(`${v.w}x${v.h}`)) : ALL_VIEWPORTS;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[hud-info]', ...a);

async function visibleWithin(locator, ms) {
  try {
    await locator.first().waitFor({ state: 'visible', timeout: ms });
    return true;
  } catch (e) {
    return false;
  }
}

async function shot(page, dir, name, report) {
  const file = path.join(dir, `${name}.png`);
  await page.screenshot({ path: file, animations: 'disabled' });
  report.push({ screen: name, file, status: 'captured' });
  log('saved', file);
}

async function runOne(browser, lang, vp, report, notCaptured) {
  const label = `${vp.w}x${vp.h}`;
  const dir = path.join(OUT_DIR, lang, label);
  fs.mkdirSync(dir, { recursive: true });
  const context = await browser.newContext({
    viewport: { width: vp.w, height: vp.h },
    deviceScaleFactor: 1,
    isMobile: vp.mobile,
    hasTouch: vp.mobile,
    locale: lang === 'vi' ? 'vi-VN' : 'en-US',
  });
  // Fresh storage each time: the language is chosen here, not left over from an earlier run.
  await context.addInitScript((settings) => {
    try {
      localStorage.setItem('nosum.settings.v1', settings);
    } catch (e) { /* storage blocked: the page falls back to its defaults */ }
  }, LANGS[lang]);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

  await page.goto(BASE_URL + '/', { waitUntil: 'load', timeout: 120000 });
  const guest = page.getByRole('button', { name: /as a guest|guest|khách/i }).first();
  await guest.waitFor({ state: 'visible', timeout: 120000 });
  await guest.click();
  await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
  await sleep(4000);

  await shot(page, dir, 'hud-idle', report);

  // Toast queue (item 1): one refusal, then a second one while the first is still on the line. The client refuses the seed on the farmhouse.
  await page.keyboard.press('Digit2');
  await page.keyboard.press('KeyE');
  if (await visibleWithin(page.locator('.game-message'), 2000)) {
    await shot(page, dir, 'toast-one', report);
    await sleep(400);
    await page.keyboard.press('KeyE');
    await sleep(300);
    const count = await page.locator('.game-message').count();
    if (count >= 2) {
      await shot(page, dir, 'toast-two', report);
    } else {
      notCaptured.push({ screen: 'toast-two', reason: `the second refusal did not stack (${count} toast on the line)` });
    }
  } else {
    notCaptured.push({ screen: 'toast-one', reason: 'the seed refusal line did not appear' });
  }
  await page.keyboard.press('Digit1');
  await sleep(3500); // let the toasts leave before the next shots

  // Energy tooltip (item 3): the hoe in the first slot, hover the energy gauge.
  const energy = page.locator('.vitals-bars > .energy-bar').nth(2);
  if (await visibleWithin(energy, 2000)) {
    await energy.hover();
    await sleep(900);
    await shot(page, dir, 'energy-tooltip', report);
    await page.mouse.move(1, 1);
  } else {
    notCaptured.push({ screen: 'energy-tooltip', reason: 'the energy gauge is not on screen' });
  }

  // Today card (item 7): a click or tap on the calendar bar opens the centre window.
  const calendar = page.locator('.calendar-line').first();
  if (await visibleWithin(calendar, 2000)) {
    await calendar.click();
    if (await visibleWithin(page.locator('.game-panel:not(.hud)'), 2000)) {
      await sleep(600);
      await shot(page, dir, 'today-card', report);
      await page.keyboard.press('Escape');
      await sleep(300);
    } else {
      notCaptured.push({ screen: 'today-card', reason: 'the calendar click did not open a window' });
    }
  } else {
    notCaptured.push({ screen: 'today-card', reason: 'the calendar bar is not on screen' });
  }

  notCaptured.push({ screen: 'shop-seeds', reason: 'a merchant has to be reached on the map; this script does not walk there' });
  if (errors.length) log(lang, label, 'page errors:', errors.length, errors.slice(0, 3));
  await context.close();
}

async function run() {
  const browser = await chromium.launch();
  for (const lang of wantedLangs) {
    if (!LANGS[lang]) throw new Error(`unknown language ${lang}`);
    const report = [];
    const notCaptured = [];
    for (const vp of VIEWPORTS) {
      await runOne(browser, lang, vp, report, notCaptured);
    }
    fs.mkdirSync(path.join(OUT_DIR, lang), { recursive: true });
    fs.writeFileSync(path.join(OUT_DIR, lang, 'capture-report.json'), JSON.stringify({ base_url: BASE_URL, lang, captured: report, not_captured: notCaptured }, null, 2));
    log(lang, 'done;', report.length, 'captured,', notCaptured.length, 'not captured');
  }
  await browser.close();
}

run().catch((e) => {
  console.error(e);
  process.exit(1);
});
