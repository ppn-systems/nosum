// Account settings round 2: before/after captures and label-wrap measurements (Playwright, Chromium).
// Usage: BASE_URL=http://localhost:5810 OUT_DIR=/path node capture.js
// Optional: VIEWPORTS="1280x720,1920x1080,390x844" LANGS="vi,en" PSQL_ENV="PGPASSWORD=..."
// Each run signs in as a fresh guest, seeds one mail for that guest in the dev database (psql), then captures
// the menu (Profile tile), the mail row with its Delete button, the delete question (when the build has one),
// and the Sound settings tab. It writes OUT_DIR/<WxH>-<lang>-<screen>.png and OUT_DIR/report.json.
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
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
  vi: { mail: 'Thư', menu: 'Menu', profile: 'Hồ sơ', sound: 'Âm thanh', soundFx: 'Hiệu ứng', del: 'Xóa thư', keep: 'Giữ lại', notice: 'Thông báo' },
  en: { mail: 'Mail', menu: 'Menu', profile: 'Profile', sound: 'Sound', soundFx: 'Sound effects', del: 'Delete', keep: 'Keep', notice: 'Notice' },
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[round2]', ...a);
async function visibleWithin(locator, ms) {
  try { await locator.waitFor({ state: 'visible', timeout: ms }); return true; } catch (e) { return false; }
}

// Counts the line boxes of the first visible text node whose whole text is `needle`: 1 means one line.
async function lineCount(page, needle) {
  return page.evaluate((n) => {
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let node;
    while ((node = walker.nextNode())) {
      if (node.textContent.trim() !== n) continue;
      const range = document.createRange();
      range.selectNodeContents(node);
      const boxes = [...range.getClientRects()].filter((r) => r.width > 0 && r.height > 0);
      if (boxes.length === 0) continue;
      const tops = new Set(boxes.map((r) => Math.round(r.top)));
      return { lines: tops.size, width: Math.round(range.getBoundingClientRect().width) };
    }
    return null;
  }, needle);
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

function seedMail() {
  // One unread mail with an unknown code: no gift, so it can be deleted at once. Goes to the newest player (the guest just created).
  const sql = "INSERT INTO mail_messages (id, recipient_id, code, args, sent_at, expires_at, coins) SELECT gen_random_uuid(), id, 'something.new', '{}', now(), now() + interval '29 days', 0 FROM players WHERE id = (SELECT id FROM players ORDER BY id DESC LIMIT 1);";
  execFileSync('psql', ['-h', 'localhost', '-U', 'nosum', '-d', 'nosum_dev', '-c', sql], {
    env: { ...process.env, PGPASSWORD: 'dev_password_not_for_production' },
    stdio: 'pipe',
  });
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
  seedMail();
  await sleep(500);

  // Menu and the Profile tile.
  const menuBtn = page.locator(`[title="${t.menu}"], [aria-label="${t.menu}"]`).first();
  if (await visibleWithin(menuBtn, 2000)) await menuBtn.click();
  else await page.getByRole('button', { name: new RegExp('^' + t.menu + '$', 'i') }).first().click();
  await sleep(700);
  report.push({ tag, check: 'profile-tile-lines', ...((await lineCount(page, t.profile)) || { lines: null }) });
  await shot(page, dir, `${tag}-menu`);

  // Mail: open the page from the top row, then open the mail row.
  await closePanels(page);
  const mailBtn = page.locator(`[title="${t.mail}"], [aria-label="${t.mail}"]`).first();
  if (await visibleWithin(mailBtn, 2000)) await mailBtn.click();
  else await page.getByRole('button', { name: new RegExp(t.mail, 'i') }).first().click();
  await sleep(900);
  const row = page.getByRole('button', { name: t.notice, exact: true }).first();
  await row.waitFor({ state: 'visible', timeout: 15000 });
  await row.click();
  await sleep(600);
  await shot(page, dir, `${tag}-mail-open`);

  // Press Delete: after = a confirm box (nothing sent yet); before = the mail is deleted at once.
  await page.getByRole('button', { name: t.del, exact: true }).first().click();
  await sleep(700);
  const confirm = page.getByRole('alertdialog').first();
  if (await visibleWithin(confirm, 1500)) {
    report.push({ tag, check: 'delete-asks-first', result: 'yes' });
    await shot(page, dir, `${tag}-mail-confirm`);
    await page.getByRole('button', { name: t.keep, exact: true }).first().click();
    await sleep(600);
    report.push({ tag, check: 'keep-keeps-the-mail', result: (await page.getByRole('button', { name: t.notice, exact: true }).count()) > 0 ? 'yes' : 'no' });
  } else {
    report.push({ tag, check: 'delete-asks-first', result: 'no (deleted at once)' });
    await shot(page, dir, `${tag}-mail-after-delete`);
  }
  await closePanels(page);

  // Settings, Sound tab.
  await page.keyboard.press('KeyH');
  await sleep(700);
  const soundTab = page.getByRole('button', { name: new RegExp('^' + t.sound + '$', 'i') }).first();
  if (await visibleWithin(soundTab, 1500)) await soundTab.click();
  await sleep(600);
  report.push({ tag, check: 'sound-effects-label-lines', ...((await lineCount(page, t.soundFx)) || { lines: null }) });
  await shot(page, dir, `${tag}-settings-sound`);

  report.push({ tag, check: 'page-errors', count: errors.length, first: errors.slice(0, 3) });
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
          report.push({ tag: `${vp.w}x${vp.h}-${lang}`, check: 'run', error: String(e).slice(0, 400) });
        }
      }
    }
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(OUT_DIR, 'report.json'), JSON.stringify(report, null, 2));
    log('report', path.join(OUT_DIR, 'report.json'));
  }
})();
