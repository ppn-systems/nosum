// Toast dedupe capture (toast queue: the same text already on the line counts up as "×N" instead of adding a second line).
// Drives the real client: the seed refusal on the farmhouse is pressed twice within three seconds.
//
// Usage:
//   BASE_URL=http://localhost:5830 OUT_DIR=/path/to/docs/qa/toast-dedupe node capture-toast-dedupe.cjs
// Writes OUT_DIR/toast-repeat-vi-<WxH>.png for 1280x720, 1920x1080 and 390x844, and OUT_DIR/capture-report.json.
// Requires `require('playwright')` resolvable from here (Chromium preinstalled; no new dependency).

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5830').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || __dirname);
const LANG_VI = 'lang=1\n'; // GameLanguage Vietnamese = 1 (the settings key holds the number)
const VIEWPORTS = [
  { w: 1280, h: 720, mobile: false },
  { w: 1920, h: 1080, mobile: false },
  { w: 390, h: 844, mobile: true },
];

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[toast-dedupe]', ...a);

async function runOne(browser, vp, report) {
  const label = `${vp.w}x${vp.h}`;
  const context = await browser.newContext({
    viewport: { width: vp.w, height: vp.h },
    deviceScaleFactor: 1,
    isMobile: vp.mobile,
    hasTouch: vp.mobile,
    locale: 'vi-VN',
  });
  await context.addInitScript((settings) => {
    try {
      localStorage.setItem('nosum.settings.v1', settings);
    } catch (e) { /* storage blocked: the page falls back to its defaults */ }
  }, LANG_VI);
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

  // The seed in the first hotbar slot, used on the farmhouse: the server refuses it, the toast shows once.
  await page.keyboard.press('Digit2');
  await page.keyboard.press('KeyE');
  await page.locator('.game-message').first().waitFor({ state: 'visible', timeout: 5000 });
  await sleep(300);
  await page.keyboard.press('KeyE');
  await sleep(400);

  const toasts = await page.locator('.game-message').allInnerTexts();
  const ok = toasts.length === 1 && /×2/.test(toasts[0]);
  const file = path.join(OUT_DIR, `toast-repeat-vi-${label}.png`);
  // Toasts fade with a CSS animation: animations stay on so the shot shows the line as the player sees it.
  await page.screenshot({ path: file, animations: 'allow' });
  report.push({ viewport: label, file, toasts, one_line_with_count: ok });
  log(label, ok ? 'one toast with ×2' : `UNEXPECTED: ${JSON.stringify(toasts)}`);
  if (errors.length) log(label, 'page errors:', errors.length, errors.slice(0, 3));
  await context.close();
}

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const report = [];
  for (const vp of VIEWPORTS) {
    await runOne(browser, vp, report);
  }
  fs.writeFileSync(path.join(OUT_DIR, 'capture-report.json'), JSON.stringify({ base_url: BASE_URL, lang: 'vi', captured: report }, null, 2));
  await browser.close();
}

run().catch((e) => {
  console.error(e);
  process.exit(1);
});
