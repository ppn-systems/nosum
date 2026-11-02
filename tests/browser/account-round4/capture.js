// Account and settings round 4: wardrobe captures, a quick-choice check and a settings reload check (Playwright, Chromium).
// Usage: BASE_URL=http://localhost:5810 OUT_DIR=/path node capture.js
// Optional: VIEWPORTS="1280x720,1920x1080,390x844" LANGS="vi,en"
// Each run signs in as a fresh guest and writes OUT_DIR/<WxH>-<lang>-<screen>.png plus OUT_DIR/report.json:
//   - wardrobe: the Look tab, its character preview height, and the quick-choice check. Two choices (a skin and a hair
//     style) are clicked in the same tick; the server must end with both, so the skin must not revert.
//   - settings: the "Show names" switch is turned off, the page is reloaded, the guest signs in again and the switch
//     must still be off (the value is read back from the stored settings and from the page).

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5810').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || path.join(__dirname, 'out'));
const ALL_VIEWPORTS = [
  { w: 1280, h: 720, mobile: false },
  { w: 1920, h: 1080, mobile: false },
  { w: 390, h: 844, mobile: true },
];
const wantedVp = (process.env.VIEWPORTS || '').split(',').map((s) => s.trim()).filter(Boolean);
const VIEWPORTS = wantedVp.length ? ALL_VIEWPORTS.filter((v) => wantedVp.includes(`${v.w}x${v.h}`)) : ALL_VIEWPORTS;
const LANGS = (process.env.LANGS || 'vi,en').split(',').map((s) => s.trim()).filter(Boolean);

// Labels as the game shows them (Language.resx = vi, Language.en.resx = en).
const T = {
  vi: { menu: 'Menu', showNames: 'Hiện tên trên đầu nhân vật', display: 'Hiển thị' },
  en: { menu: 'Menu', showNames: 'Show names over heads', display: 'Display' },
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[round4]', ...a);

async function visibleWithin(locator, ms) {
  try { await locator.waitFor({ state: 'visible', timeout: ms }); return true; } catch (e) { return false; }
}

async function shot(page, dir, name) {
  const file = path.join(dir, name + '.png');
  await page.screenshot({ path: file, animations: 'disabled' });
  log('saved', file);
}

async function closePanels(page) {
  for (let i = 0; i < 3; i++) {
    if (!(await visibleWithin(page.locator('.game-panel:not(.hud)').first(), 300))) break;
    await page.keyboard.press('Escape');
    await sleep(350);
  }
}

async function signInAsGuest(page) {
  const guest = page.getByRole('button', { name: /as a guest|khách|guest/i }).first();
  await guest.waitFor({ state: 'visible', timeout: 120000 });
  await guest.click();
  await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
  await sleep(4000);
}

// Index of the element in the list that is the current one, or -1.
async function currentIndex(page, selector, sectionIndex) {
  return page.evaluate(({ selector, sectionIndex }) => {
    const parts = document.querySelectorAll('.ward .ward-part');
    const section = parts[0]?.querySelectorAll('.game-section')[sectionIndex];
    if (!section) return -1;
    const items = [...section.querySelectorAll(selector)];
    return items.findIndex((el) => el.classList.contains('current') || el.classList.contains('active'));
  }, { selector, sectionIndex });
}

async function runOne(browser, vp, lang, dir, report) {
  const t = T[lang];
  const tag = `${vp.w}x${vp.h}-${lang}`;
  const context = await browser.newContext({ viewport: { width: vp.w, height: vp.h }, deviceScaleFactor: 1, isMobile: vp.mobile, hasTouch: vp.mobile, locale: lang === 'en' ? 'en-US' : 'vi-VN' });
  await context.addInitScript((seedEn) => {
    try {
      if (seedEn && !localStorage.getItem('nosum.settings.v1')) localStorage.setItem('nosum.settings.v1', 'lang=2\n');
    } catch (e) { /* storage blocked */ }
  }, lang === 'en');
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

  await page.goto(BASE_URL + '/', { waitUntil: 'load', timeout: 120000 });
  await signInAsGuest(page);

  // Wardrobe: the Look tab (C opens it, KeyBindings: GameAction.Wardrobe).
  await closePanels(page);
  await page.keyboard.press('KeyC');
  await sleep(900);
  await shot(page, dir, `${tag}-wardrobe`);
  const preview = await page.evaluate(() => {
    const el = document.querySelector('.ward .preview');
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { width: Math.round(r.width), height: Math.round(r.height) };
  });
  report.push({ tag, step: 'wardrobe-preview-box', ...(preview || { missing: true }) });

  // Quick choices: skin 2 (second swatch) and hair style 3 (third button) clicked in the same tick, before any answer.
  const quick = await page.evaluate(() => {
    const parts = document.querySelectorAll('.ward .ward-part');
    const sections = parts[0]?.querySelectorAll('.game-section');
    const skin = sections?.[0]?.querySelectorAll('.game-swatch')[1];
    const hair = sections?.[1]?.querySelectorAll('.game-button')[2];
    if (!skin || !hair) return false;
    skin.click();
    hair.click();
    return true;
  });
  await sleep(2500);
  const skinNow = await currentIndex(page, '.game-swatch', 0);
  const hairNow = await currentIndex(page, '.game-button', 1);
  report.push({ tag, step: 'quick-choices-both-kept', clicked: quick, skinIndex: skinNow, hairIndex: hairNow, result: quick && skinNow === 1 && hairNow === 2 ? 'yes' : 'no' });
  await shot(page, dir, `${tag}-wardrobe-after-quick`);

  // Settings: turn off "Show names" on Display, then reload and read it back.
  await closePanels(page);
  await page.keyboard.press('KeyH');
  await sleep(700);
  const displayTab = page.getByRole('button', { name: new RegExp('^' + t.display + '$', 'i') }).first();
  if (await visibleWithin(displayTab, 1500)) await displayTab.click();
  await sleep(500);
  const names = page.locator('label.game-toggle', { hasText: t.showNames }).locator('input');
  if (await visibleWithin(names, 1500)) {
    if (await names.isChecked()) await names.click();
    await sleep(400);
  }
  report.push({ tag, step: 'names-off-before-reload', stored: await page.evaluate(() => localStorage.getItem('nosum.settings.v1')) });
  await closePanels(page);

  await page.reload({ waitUntil: 'load', timeout: 120000 });
  await signInAsGuest(page);
  await page.keyboard.press('KeyH');
  await sleep(700);
  const displayAgain = page.getByRole('button', { name: new RegExp('^' + t.display + '$', 'i') }).first();
  if (await visibleWithin(displayAgain, 1500)) await displayAgain.click();
  await sleep(500);
  const namesAgain = page.locator('label.game-toggle', { hasText: t.showNames }).locator('input');
  const checkedAfterReload = (await visibleWithin(namesAgain, 1500)) ? await namesAgain.isChecked() : null;
  report.push({ tag, step: 'names-off-after-reload', checked: checkedAfterReload, result: checkedAfterReload === false ? 'yes' : 'no' });
  await shot(page, dir, `${tag}-settings-display-after-reload`);

  report.push({ tag, step: 'page-errors', count: errors.length, first: errors.slice(0, 3) });
  await context.close();
}

(async () => {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const report = [];
  try {
    for (const vp of VIEWPORTS) {
      for (const lang of LANGS) {
        log(`run ${vp.w}x${vp.h} ${lang}`);
        try {
          await runOne(browser, vp, lang, OUT_DIR, report);
        } catch (e) {
          report.push({ tag: `${vp.w}x${vp.h}-${lang}`, step: 'run-failed', error: String(e).slice(0, 300) });
        }
      }
    }
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(OUT_DIR, 'report.json'), JSON.stringify(report, null, 2));
    log('report written');
  }
})();
