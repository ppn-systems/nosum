// Account round 3: before/after captures (Playwright, Chromium) of guest save-progress, the guest sign-out question and the wardrobe hair names.
// Usage: BASE_URL=http://localhost:5810 OUT_DIR=/path node capture.js
// Optional: VIEWPORTS="1280x720,390x844" LANGS="vi,en"
// Each run is one fresh guest. It writes OUT_DIR/<WxH>-<lang>-<step>.png and OUT_DIR/report.json (what was found on the page and what was missing).
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5810').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || path.join(__dirname, 'out'));
const ALL_VIEWPORTS = [
  { w: 1280, h: 720, mobile: false },
  { w: 390, h: 844, mobile: true },
];
const wantedVp = (process.env.VIEWPORTS || '').split(',').map((s) => s.trim()).filter(Boolean);
const VIEWPORTS = wantedVp.length ? ALL_VIEWPORTS.filter((v) => wantedVp.includes(`${v.w}x${v.h}`)) : ALL_VIEWPORTS;
const LANGS = (process.env.LANGS || 'vi,en').split(',').map((s) => s.trim()).filter(Boolean);

// Labels as the game shows them (Language.resx = vi, Language.en.resx = en).
const T = {
  vi: { menu: 'Menu', profile: 'Hồ sơ', wardrobe: 'Tủ đồ', wardrobeHair: 'Kiểu tóc', saveAction: 'Lưu tiến trình bằng tài khoản', signOut: 'Đăng xuất', stay: 'Ở lại chơi', link: 'Liên kết tài khoản trước', cancel: 'Hủy' },
  en: { menu: 'Menu', profile: 'Profile', wardrobe: 'Wardrobe', wardrobeHair: 'Hair style', saveAction: 'Save progress with an account', signOut: 'Sign out', stay: 'Keep playing', link: 'Link an account first', cancel: 'Cancel' },
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[round3]', ...a);
async function visibleWithin(locator, ms) {
  try { await locator.waitFor({ state: 'visible', timeout: ms }); return true; } catch (e) { return false; }
}
// Clicks the first visible button whose text is `text`. Returns false when the page has none (for example on master).
async function clickButton(page, text, ms = 2500) {
  const btn = page.locator('button', { hasText: new RegExp('^\\s*' + text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\s*$') }).first();
  if (!(await visibleWithin(btn, ms))) return false;
  await btn.click();
  return true;
}
// Clicks a button, or a menu tile by its visible label (the menu tiles are not buttons).
async function clickLabel(page, text, ms = 2500) {
  if (await clickButton(page, text, ms)) return true;
  const el = page.getByText(text, { exact: true }).first();
  if (!(await visibleWithin(el, ms))) return false;
  await el.click();
  return true;
}
async function shot(page, dir, name) {
  const file = path.join(dir, name + '.png');
  await page.mouse.move(1, 1); // no hover tooltip over the frame
  await sleep(150);
  await page.screenshot({ path: file, animations: 'disabled' });
  log('saved', file);
  return file;
}
async function closePanels(page) {
  for (let i = 0; i < 3; i++) {
    if (!(await visibleWithin(page.locator('.game-panel:not(.hud)').first(), 300))) break;
    await page.keyboard.press('Escape');
    await sleep(350);
  }
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
  const guest = page.getByRole('button', { name: /as a guest|khách|guest/i }).first();
  await guest.waitFor({ state: 'visible', timeout: 120000 });
  await guest.click();
  await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
  await sleep(4000);

  // Profile: the account block (guest save-progress is the new part).
  const menuBtn = page.locator(`[title="${t.menu}"], [aria-label="${t.menu}"]`).first();
  if (await visibleWithin(menuBtn, 2000)) await menuBtn.click();
  else await clickLabel(page, t.menu);
  await sleep(700);
  report.push({ tag, step: 'profile-opened', found: await clickLabel(page, t.profile) });
  await sleep(700);
  await shot(page, dir, `${tag}-profile`);
  report.push({ tag, step: 'save-progress-button-visible', found: await visibleWithin(page.getByRole('button', { name: t.saveAction, exact: true }).first(), 1000) });

  // The save-progress form (upgrade mode), then cancel back to Profile.
  if (await clickButton(page, t.saveAction, 1000)) {
    await sleep(700);
    await shot(page, dir, `${tag}-save-progress-form`);
    await clickButton(page, t.cancel, 1500);
    await sleep(500);
  }

  // Wardrobe: hair style names. The wardrobe opens with C (KeyBindings: GameAction.Wardrobe).
  await closePanels(page);
  await page.keyboard.press('KeyC');
  await sleep(900);
  report.push({ tag, step: 'wardrobe-opened', found: await visibleWithin(page.getByText(t.wardrobeHair, { exact: true }).first(), 2000) });
  await shot(page, dir, `${tag}-wardrobe`);

  // Back to Profile for the sign-out (the wardrobe closed it).
  await closePanels(page);
  if (await visibleWithin(menuBtn, 1500)) await menuBtn.click();
  else await clickLabel(page, t.menu);
  await sleep(600);
  await clickLabel(page, t.profile);
  await sleep(600);

  // Sign-out from Profile: a guest sees the question first (after); on master it signs out at once (before).
  const signedOutBefore = !(await clickButton(page, t.signOut, 1000));
  await sleep(700);
  const question = page.getByRole('alertdialog').first();
  if (await visibleWithin(question, 1500)) {
    report.push({ tag, step: 'signout-asks-first', result: 'yes' });
    await shot(page, dir, `${tag}-signout-question`);
    await clickButton(page, t.stay, 1500);
    await sleep(500);
    report.push({ tag, step: 'stay-keeps-session', result: (await page.getByRole('button', { name: t.saveAction, exact: true }).count()) > 0 ? 'yes' : 'no' });
  } else {
    report.push({ tag, step: 'signout-asks-first', result: signedOutBefore ? 'no (no sign-out button on this build)' : 'no (signed out at once)' });
    await shot(page, dir, `${tag}-after-signout-click`);
  }
  await closePanels(page);

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
        log('run', vp.w + 'x' + vp.h, lang);
        try {
          await runOne(browser, vp, lang, OUT_DIR, report);
        } catch (e) {
          report.push({ tag: `${vp.w}x${vp.h}-${lang}`, step: 'run', error: String(e).slice(0, 400) });
        }
      }
    }
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(OUT_DIR, 'report.json'), JSON.stringify(report, null, 2));
    log('report', path.join(OUT_DIR, 'report.json'));
  }
})();
