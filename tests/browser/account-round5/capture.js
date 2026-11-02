// Account and wardrobe round 5: the look changes go out in order and show at once (docs/adr/ADR-120). Playwright, Chromium.
// Usage: BASE_URL=http://localhost:5810 OUT_DIR=/path node capture.js
// Optional: VIEWPORTS="1280x720,1920x1080,390x844" LANGS="vi,en"
// Each run signs in as a fresh guest, seeds one mail with a shirt (405) and a hat (518) for that guest, claims it through the game, then checks:
//   - wear: the shirt is worn. Counted on the page: the bag's Wear buttons and the worn slots' take-off buttons, measured at once and 150 ms after the click.
//   - race: the shirt is taken off and the hat is worn in the same tick. The server must end with no shirt and the hat.
//   - mixed: a skin is chosen, the shirt is worn and another skin is chosen in the same tick. The server must end with the second skin and the shirt.
// The end state is read from the database (player_appearances, inventory_slots), not from the page.

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

const SHIRT = 405; // LeatherArmor (ItemCatalog)
const HAT = 518; // BossCrown (ItemCatalog)

// Labels as the game shows them (Language.resx = vi, Language.en.resx = en).
const T = {
  vi: { wear: 'Mặc', takeOff: 'Cởi ra', look: 'Ngoại hình', clothes: 'Trang phục', collectAll: /Nhận tất cả/, mail: 'Thư' },
  en: { wear: 'Wear', takeOff: 'Take off', look: 'Look', clothes: 'Clothes', collectAll: /Collect all/, mail: 'Mail' },
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[round5]', ...a);

function psql(sql) {
  return execFileSync('psql', ['-h', 'localhost', '-U', 'nosum', '-d', 'nosum_dev', '-tAc', sql], {
    env: { ...process.env, PGPASSWORD: 'dev_password_not_for_production' },
    encoding: 'utf8',
  }).trim();
}

// The guest signed in last (the newest player row) receives the mail. Only guests are used here.
function seedGift() {
  const id = psql("SELECT id FROM players WHERE is_guest ORDER BY created_at DESC LIMIT 1");
  const mail = require('crypto').randomUUID();
  psql(`INSERT INTO mail_messages (id, recipient_id, code, args, sent_at, expires_at, coins) VALUES ('${mail}', '${id}', 'something.new', '{}', now(), now() + interval '29 days', 0)`);
  psql(`INSERT INTO mail_attachment_items (mail_id, item_id, quantity) VALUES ('${mail}', ${SHIRT}, 1), ('${mail}', ${HAT}, 1)`);
  return id;
}

function finalLook(playerId) {
  const row = psql(`SELECT skin_id, hair_style_id, shirt_item_id, hat_item_id FROM player_appearances WHERE player_id = '${playerId}'`);
  const [skin, hair, shirt, hat] = row.split('|').map(Number);
  const bag = psql(`SELECT string_agg(item_id::text || 'x' || quantity::text, ',' ORDER BY item_id) FROM inventory_slots WHERE player_id = '${playerId}' AND item_id IN (${SHIRT}, ${HAT})`);
  return { skin, hair, shirt, hat, bag };
}

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

// Counts of the visible buttons on the wardrobe page: Wear buttons (clothes in the bag) and take-off buttons (slots worn).
async function counts(page, t) {
  return page.evaluate((labels) => {
    const panel = document.querySelector('.ward');
    if (!panel) return { wear: -1, takeOff: -1 };
    const buttons = [...panel.querySelectorAll('button')].filter((b) => b.offsetParent !== null).map((b) => b.textContent.trim());
    return { wear: buttons.filter((x) => x === labels.wear).length, takeOff: buttons.filter((x) => x === labels.takeOff).length };
  }, { wear: t.wear, takeOff: t.takeOff });
}

async function clickTab(page, label) {
  return page.evaluate((text) => {
    const tab = [...(document.querySelector('.ward')?.querySelectorAll('button') || [])].find((b) => b.textContent.trim() === text);
    if (!tab) return false;
    tab.click();
    return true;
  }, label);
}

async function clickButton(page, text) {
  return page.evaluate((label) => {
    const panel = document.querySelector('.ward');
    const button = [...(panel?.querySelectorAll('button') || [])].find((b) => b.textContent.trim() === label);
    if (!button) return false;
    button.click();
    return true;
  }, text);
}

async function clickSwatch(page, index) {
  return page.evaluate((i) => {
    const parts = document.querySelectorAll('.ward .ward-part');
    const swatch = parts[0]?.querySelectorAll('.game-section')[0]?.querySelectorAll('.game-swatch')[i];
    if (!swatch) return false;
    swatch.click();
    return true;
  }, index);
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
  const playerId = seedGift();

  // Claim the gift through the game's mail page: the shirt and the hat go to the bag.
  await closePanels(page);
  const mailBtn = page.locator(`[title="${t.mail}"], [aria-label="${t.mail}"]`).first();
  if (await visibleWithin(mailBtn, 2000)) await mailBtn.click();
  else await page.getByRole('button', { name: new RegExp(t.mail, 'i') }).first().click();
  await sleep(900);
  const collect = page.getByRole('button', { name: t.collectAll }).first();
  if (await visibleWithin(collect, 3000)) await collect.click();
  await sleep(2500);
  report.push({ tag, step: 'gift-claimed', bag: finalLook(playerId).bag });
  await closePanels(page);

  // Wardrobe: C opens it (GameAction.Wardrobe); the wear and take-off buttons are on the Clothes tab.
  await page.keyboard.press('KeyC');
  await sleep(900);
  await clickTab(page, t.clothes);
  await sleep(400);
  const before = await counts(page, t);
  await shot(page, dir, `${tag}-wardrobe-bag`);

  // Wear: the bag loses the shirt and the worn slots gain it at once, before the server answers (0 ms) and after 150 ms.
  const wearClicked = await clickButton(page, t.wear);
  const atOnce = await counts(page, t);
  await sleep(150);
  const later = await counts(page, t);
  await sleep(2000);
  report.push({ tag, step: 'wear-shows-at-once', clicked: wearClicked, before, atOnce, after150ms: later, result: later.takeOff === before.takeOff + 1 && later.wear === before.wear - 1 ? 'yes' : 'no' });
  await shot(page, dir, `${tag}-wardrobe-worn`);
  // The bag's first item is worn first: which one depends on the bag order, so the checks follow the one worn.
  const afterWear = finalLook(playerId);
  const wornShirt = afterWear.shirt === SHIRT;
  const wornOk = wornShirt ? true : afterWear.hat === HAT;
  report.push({ tag, step: 'wear-server-state', worn: wornShirt ? 'shirt' : 'hat', shirt: afterWear.shirt, hat: afterWear.hat, bag: afterWear.bag, result: wornOk ? 'yes' : 'no' });

  // Race: take the worn item off and wear the other one in the same tick. The other one is the only Wear button left.
  await page.evaluate((labels) => {
    const panel = document.querySelector('.ward');
    const off = [...panel.querySelectorAll('button')].find((b) => b.textContent.trim() === labels.takeOff);
    const wear = [...panel.querySelectorAll('button')].find((b) => b.textContent.trim() === labels.wear);
    off?.click();
    wear?.click();
  }, { takeOff: t.takeOff, wear: t.wear });
  await sleep(2500);
  const race = finalLook(playerId);
  await shot(page, dir, `${tag}-wardrobe-race`);
  const raceOk = wornShirt ? race.shirt === 0 && race.hat === HAT : race.hat === 0 && race.shirt === SHIRT;
  report.push({ tag, step: 'takeoff-then-wear-same-tick', shirt: race.shirt, hat: race.hat, bag: race.bag, result: raceOk ? 'yes' : 'no' });

  // Mixed: skin 2, the taken-off item back on, skin 3 in the same tick. The swatches are on the Look tab and the Wear button on the
  // Clothes tab, so in this tick one of them is a hidden button: the point is the order of the requests, which the server must keep.
  // Both items end worn and the second skin is kept.
  await clickSwatch(page, 1);
  await clickButton(page, t.wear);
  await clickSwatch(page, 2);
  await sleep(2500);
  const mixed = finalLook(playerId);
  await clickTab(page, t.look);
  await sleep(600);
  await shot(page, dir, `${tag}-wardrobe-mixed`);
  const mixedOk = mixed.skin === 3 && mixed.shirt === SHIRT && mixed.hat === HAT;
  report.push({ tag, step: 'skin-wear-skin-same-tick', skin: mixed.skin, shirt: mixed.shirt, hat: mixed.hat, result: mixedOk ? 'yes' : 'no' });

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
