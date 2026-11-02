// Captures the carpenter's animal section with a coop and a barn (docs/adr/ADR-107): the coop's duck and chicken rows and the barn's four rows,
// before and after a purchase, with the scroll state of the section. Each run is one language and one viewport on a fresh guest.
// Usage: BASE_URL=http://localhost:5080 OUT_DIR=<dir> LANG_CODE=en|vi VIEWPORT=1280x720|1920x1080|390x844 BUILDINGS=coop|barn node animals-barn-capture.cjs
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

// Labels as the game shows them (Language.resx / Language.en.resx): the buy button reads "Buy <animal> · <price>" or "Mua <animal> · <price> xu".
const L = {
  en: { guest: /as a guest|guest/i, build: 'Build', buy: (name) => new RegExp(`^Buy ${name} `), next: 'Next', name: 'Animal name', animalsTab: 'Animals' },
  vi: { guest: /khách|guest/i, build: 'Xây dựng', buy: (name) => new RegExp(`^Mua ${name} `), next: 'Tiếp', name: 'Tên vật nuôi', animalsTab: 'Vật nuôi' },
}[LANG_CODE];

const COOP_TILES = [[40, 40], [44, 36], [36, 44], [30, 40]];
const BARN_TILES = [[30, 48], [26, 46], [34, 50], [22, 46], [44, 50], [52, 40]];
const BUILDINGS = process.env.BUILDINGS === 'coop' ? 'coop' : 'barn';
const WOOD = 500; const STONE = 501; const COOP_BLUEPRINT = 610; const BARN_BLUEPRINT = 611;

const report = { captured: [], failed: [], scroll: null };
const shot = async (page, name) => {
  await page.screenshot({ path: path.join(OUT_DIR, `${name}.png`), animations: 'disabled' });
  report.captured.push(name);
};
const step = async (name, fn) => {
  try { await fn(); } catch (e) { report.failed.push({ step: name, reason: String(e).slice(0, 180) }); }
};

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 1, isMobile: MOBILE, hasTouch: MOBILE, locale: LANG_CODE === 'vi' ? 'vi-VN' : 'en-US' });
  await context.addInitScript((code) => { try { localStorage.setItem('nosum.settings.v1', `lang=${code}\n`); } catch (e) { /* blocked */ } }, LANG_CODE === 'vi' ? 1 : 2);
  const page = await context.newPage();
  const qa = (fn, args) => page.evaluate(([f, a]) => window.nosumQa[f](...a), [fn, args]);

  try {
    await page.goto(BASE_URL + '/?qa=1', { waitUntil: 'load', timeout: 120000 });
    await page.getByRole('button', { name: L.guest }).first().click({ timeout: 120000 });
    await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
    await sleep(3000);

    // A fresh server starts on spring day 14, where a tree stands on the first barn tile; two days on (day 16) the tile is clear. The clock is
    // shared by every guest of a server, so each run starts its own server (docs/qa/animals-round4/README.md).
    await qa('advanceClock', [2]); await sleep(1500);
    // Coins and materials for a barn (6 000, 350 wood, 150 stone) and a coop (4 000, 300 wood, 100 stone); both blueprints placed on the farm.
    await qa('grant', [0, 0, 40000]); await sleep(900);
    await qa('grant', [WOOD, 700, 0]); await sleep(700);
    await qa('grant', [STONE, 300, 0]); await sleep(900);
    // The coop is set down and built first (two game days); then the barn on its own tile, built the same way.
    // BUILDINGS picks the building: "coop" (the chicken and duck rows) or "barn" (the cow, goat, sheep and pig rows). One of them per guest:
    // a second building placed in the same guest was refused in this flow, so each capture is one building.
    const placed = [[COOP_BLUEPRINT, COOP_TILES, 'coop'], [BARN_BLUEPRINT, BARN_TILES, 'barn']].filter(([, , k]) => k === BUILDINGS);
    for (const [blueprint, tiles] of placed) {
      await qa('grant', [blueprint, 1, 0]); await sleep(1200);
      // A tile takes the blueprint when the hotbar's slot 9 changes: the blueprint leaves the hand once the building is set down. The game says
      // "in the way" on a blocked tile (a tree or a weed); the server can also refuse a footprint without a message, so the slot is what is read.
      // The placement panel stays open after Escape in some states; its close button shuts it, so the next tile starts clean.
      const closeFurniture = async () => { await page.locator('.game-close').first().click({ timeout: 2000 }).catch(() => {}); await sleep(400); };
      const slot9 = () => page.evaluate(() => { const b = [...document.querySelectorAll('[role=toolbar] button')].find((x) => /^9\d*$/.test(x.textContent.trim())); return b ? (b.getAttribute('aria-label') || '') : ''; });
      for (const tile of tiles) {
        await qa('teleport', ['farm', tile[0], tile[1]]); await sleep(2000);
        await page.keyboard.press('Digit9'); await sleep(900);
        const before = await slot9();
        await page.keyboard.press('KeyE'); await sleep(1200);
        const blocked = await page.evaluate(() => /in the way|chắn/.test(document.body.innerText));
        if (blocked) { report.blocked = [...(report.blocked || []), tile]; await closeFurniture(); continue; }
        await page.keyboard.press('KeyE'); await sleep(1500);
        if ((await slot9()) !== before) { report.placedAt = tile; break; }
        report.refused = [...(report.refused || []), tile];
        await closeFurniture();
      }
      await qa('advanceClock', [2]); await sleep(2500);
    }

    await qa('teleport', ['town', 10, 86]); await sleep(2500);
    await page.keyboard.press('KeyE'); await sleep(1800);
    await page.getByRole('button', { name: L.build, exact: true }).click(); await sleep(1200);

    // The Buildings tab: the blueprints. Then the Animals tab (the after build; the before build has the animals on the same page).
    await step('buildings', async () => {
      await shot(page, 'carpenter-buildings');
    });
    const animalsTab = page.getByRole('button', { name: L.animalsTab, exact: true });
    const hasTab = await animalsTab.count().catch(() => 0);
    if (hasTab > 0) {
      await step('open-animals-tab', async () => { await animalsTab.first().click(); await sleep(900); });
    }
    await step('animals-before', async () => {
      await shot(page, 'carpenter-animals');
      report.scroll = await page.evaluate(() => {
        const wrap = document.querySelector('.carpenter-wrap');
        const panel = wrap ? wrap.closest('section.game-panel') : null;
        const body = panel ? panel.querySelector('.game-panel-body') : null;
        const pr = panel ? panel.getBoundingClientRect() : null;
        const buttons = [...document.querySelectorAll('.carpenter-wrap button')];
        const hidden = buttons.filter((b) => { const r = b.getBoundingClientRect(); return pr && (r.bottom > pr.bottom - 1 || r.top < pr.top); }).map((b) => b.textContent.trim());
        return {
          bodyScrollHeight: body ? body.scrollHeight : null,
          bodyClientHeight: body ? body.clientHeight : null,
          bodyOverflowY: body ? getComputedStyle(body).overflowY : null,
          panelBottom: pr ? Math.round(pr.bottom) : null,
          viewport: window.innerHeight,
          buttonsCut: hidden,
          buttons: buttons.map((b) => b.textContent.trim()),
        };
      });
    });

    // Buy a cow and a goat (the barn): the name is typed first, as the player does.
    for (const [species, label] of [['Cow', 'cow'], ['Goat', 'goat']]) {
      await step(`buy-${label}`, async () => {
        await page.locator('input').first().fill(`${species}1`); await sleep(300);
        await page.getByRole('button', { name: L.buy(LANG_CODE === 'vi' ? (species === 'Cow' ? 'Bò' : 'Dê') : species) }).first().click();
        await sleep(2000);
      });
    }
    await step('animals-after', () => shot(page, 'carpenter-animals-after'));
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
