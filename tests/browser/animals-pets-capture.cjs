// Captures the animals and pets screens (docs/design/critique-animals-pets.md): the carpenter's animal section, a coop
// with an empty and a fed feeding bench, and the pet panel. Each run is one language and one viewport on a fresh guest.
// Usage: BASE_URL=http://localhost:5080 OUT_DIR=<dir> LANG_CODE=en|vi VIEWPORT=1280x720|390x844 node animals-pets-capture.cjs
// The server runs in Development (dev grants); the client is built with -p:NosumDevTools=true and opened at /?qa=1.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || '.');
const LANG_CODE = process.env.LANG_CODE === 'vi' ? 'vi' : 'en';
const [W, H] = (process.env.VIEWPORT || '1280x720').split('x').map(Number);
const MOBILE = W < 600;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Labels as the game shows them in each language (Language.resx / Language.en.resx).
const L = {
  en: { guest: /as a guest|guest/i, build: 'Build', pets: 'Pets', buyChicken: /Buy Chicken/, pet: 'Pet', menuPet: 'Pet', choose: /Choose/, next: 'Next', confirm: 'Confirm' },
  vi: { guest: /khách|guest/i, build: 'Xây dựng', pets: 'Thú cưng', buyChicken: /Mua Gà/, pet: 'Vuốt ve', menuPet: 'Thú cưng', choose: /Chọn/, next: /Tiếp/, confirm: /Xác nhận/ },
}[LANG_CODE];

const report = { captured: [], failed: [] };
const shot = async (page, name) => {
  await page.screenshot({ path: path.join(OUT_DIR, name), animations: 'disabled' });
  report.captured.push(name);
};
const step = async (name, fn) => {
  try {
    await fn();
  } catch (e) {
    report.failed.push({ step: name, reason: String(e).slice(0, 160) });
  }
};
const qa = (page, fn, args) => page.evaluate(([f, a]) => window.nosumQa[f](...a), [fn, args]);
const hold = async (page, key, ms) => {
  await page.keyboard.down(key);
  await sleep(ms);
  await page.keyboard.up(key);
  await sleep(250);
};

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 1, isMobile: MOBILE, hasTouch: MOBILE, locale: LANG_CODE === 'vi' ? 'vi-VN' : 'en-US' });
  await context.addInitScript((code) => { try { localStorage.setItem('nosum.settings.v1', `lang=${code}\n`); } catch (e) { /* blocked */ } }, LANG_CODE === 'vi' ? 1 : 2);
  const page = await context.newPage();
  try {
    await page.goto(BASE_URL + '/?qa=1', { waitUntil: 'load', timeout: 120000 });
    await page.getByRole('button', { name: L.guest }).first().click({ timeout: 120000 });
    await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
    await sleep(3000);

    // A finished coop on the farm: the blueprint is granted (its purchase is in the Buy blueprint frame), then placed and left to build for two days.
    await qa(page, 'grant', [0, 0, 20000]); await sleep(900);
    await qa(page, 'grant', [610, 1, 0]); await sleep(1200);
    await qa(page, 'teleport', ['farm', 64, 20]); await sleep(2500);
    await page.keyboard.press('Digit9'); await sleep(400);
    await page.keyboard.press('KeyE'); await sleep(1200);
    await qa(page, 'advanceClock', [2]); await sleep(2500);

    // Hay for the bench, then the carpenter's animal section with a chicken bought.
    await qa(page, 'grant', [508, 30, 0]); await sleep(1500);
    await qa(page, 'teleport', ['town', 10, 86]); await sleep(2500);
    await page.keyboard.press('KeyE'); await sleep(1800);
    await page.getByRole('button', { name: L.build, exact: true }).click(); await sleep(1200);
    await step('carpenter-animals', () => shot(page, 'carpenter-animals.png'));
    await step('buy-chicken', async () => {
      await page.locator('input').first().fill('Hen'); await sleep(300);
      await page.getByRole('button', { name: L.buyChicken }).first().click(); await sleep(2000);
    });
    await step('carpenter-bought', () => shot(page, 'carpenter-bought.png'));
    await step('close-carpenter', async () => { await page.locator('.game-close').first().click(); await sleep(600); });

    // Walk into the coop, stand under the bench, and look at it empty; then feed once.
    await qa(page, 'teleport', ['farm', 63, 24]); await sleep(2500);
    await hold(page, 'ArrowUp', 900); await sleep(900);
    await page.keyboard.press('Digit9'); await sleep(300);
    await hold(page, 'KeyD', 900); await hold(page, 'KeyW', 370); await sleep(300);
    await step('coop-empty-bench', () => shot(page, 'coop-empty-bench.png'));
    await step('feed-once', async () => { await page.keyboard.press('KeyE'); await sleep(1500); });
    await step('coop-fed', () => shot(page, 'coop-fed.png'));
  } catch (e) {
    report.failed.push({ step: 'setup', reason: String(e).slice(0, 200) });
  } finally {
    await context.close();
    await browser.close();
    const suffix = `${W}x${H}-${LANG_CODE}`;
    fs.writeFileSync(path.join(OUT_DIR, `report-${suffix}.json`), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report));
  }
}

run();
