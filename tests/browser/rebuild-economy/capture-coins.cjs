// Coin feedback captures (docs/design/critique-economy.md section 13): what a purchase, a market buy and a mail collect say about the coins they moved.
// Playwright, Chromium. Three guests in one run: a seller lists two parsnips at the trader's counter in Town, a buyer buys both there and buys one
// seed at the seed shop, then the seller collects the two sale mails with "Collect all".
// Usage (from the repo root):
//   NODE_PATH=$(npm root -g) BASE_URL=http://localhost:5080 OUT_DIR=docs/qa/economy-round5/after LANG_CODE=en VIEWPORTS=1280x720,390x844 \
//     node tests/browser/rebuild-economy/capture-coins.cjs
// Needs a build made with -p:NosumDevTools=true (window.nosumQa on a ?qa=1 page) and a Development server (the grant is refused elsewhere).
// Writes OUT_DIR/<WxH>/<lang>-<screen>.png and OUT_DIR/<lang>-coins-<sizes>-report.json: the screens, each text check (the line the player reads), and page errors.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || path.join(__dirname, 'out-coins'));
const LANG_CODE = process.env.LANG_CODE === 'vi' ? 'vi' : 'en';
const ALL = [
  { w: 1280, h: 720, mobile: false },
  { w: 1920, h: 1080, mobile: false },
  { w: 390, h: 844, mobile: true },
];
const wanted = (process.env.VIEWPORTS || '').split(',').map((s) => s.trim()).filter(Boolean);
const VIEWPORTS = wanted.length ? ALL.filter((v) => wanted.includes(`${v.w}x${v.h}`)) : ALL;

// Item id of the parsnip (ItemCatalog.Parsnip), the Town market counter (45, 71) and the seed shop (43, 57, GameMaps).
const PARSNIP = 200;
const TOWN = 'town';
const COUNTER = { x: 45, y: 71 };
const SEED_SHOP = { x: 43, y: 57 };
// The parsnip is the ninth slot of a new farmer's inventory (after the starter tools and seeds).
const PARSNIP_SLOT = 8;
// Two listings of one parsnip each, 60 and 40 coins, sold to the buyer (the sale mails are notices and need no collect).
const LISTINGS = [60, 40];

// Labels per language (Language.resx and Language.en.resx; the harness matches the text the player sees).
const T = LANG_CODE === 'en'
  ? { sell: 'Sell', browse: 'Browse', list: 'List for sale', buy: 'Buy', mail: /^Mail/, collectAll: /^Collect all/, npcSell: 'Sell', shopBuy: 'Buy' }
  : { sell: 'Bán', browse: 'Mua', list: 'Đăng bán', buy: 'Mua', mail: /^Thư/, collectAll: /^Nhận tất cả/, npcSell: 'Bán', shopBuy: 'Mua' };

// The text each screen must show (the line the player reads). Checked against the page, not only by eye.
const EXPECT = LANG_CODE === 'en'
  ? {
      marketBought: /Bought Parsnip x1 for 60 coins\./,
      shopBought: /Bought .+ x1 for 12 coins\. It is in your inventory\./,
      mailCollected: /Collected 2 items and 160 coins\./,
    }
  : {
      marketBought: /Đã mua .+ x1 với giá 60 xu\./,
      shopBought: /Đã mua .+ x1 với giá 12 xu\. Món đồ đã vào túi của bạn\./,
      mailCollected: /Đã nhận 2 món đồ và 160 xu\./,
    };

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Two gift mails for the seller (the guest created last, before the buyer exists): 120 coins, and 40 coins with two parsnips. A market sale puts its
// coins in the purse at once and its mail is a notice with nothing to collect, so the gifts are what "Collect all" brings. Seeds the local dev
// database as the nosum role, the way account-round2 seeds its mail.
function seedGifts() {
  const psql = (sql) => execFileSync('psql', ['-h', 'localhost', '-U', 'nosum', '-d', 'nosum_dev', '-tAc', sql], {
    env: { ...process.env, PGPASSWORD: 'dev_password_not_for_production' },
    encoding: 'utf8',
  }).trim();
  const id = psql('SELECT id FROM players WHERE is_guest ORDER BY created_at DESC LIMIT 1');
  const first = crypto.randomUUID();
  const second = crypto.randomUUID();
  psql(`INSERT INTO mail_messages (id, recipient_id, code, args, sent_at, expires_at, coins) VALUES ('${first}', '${id}', 'goal.reward', '{120}', now(), now() + interval '29 days', 120)`);
  psql(`INSERT INTO mail_messages (id, recipient_id, code, args, sent_at, expires_at, coins) VALUES ('${second}', '${id}', 'goal.reward', '{40}', now(), now() + interval '29 days', 40)`);
  psql(`INSERT INTO mail_attachment_items (mail_id, item_id, quantity) VALUES ('${second}', ${PARSNIP}, 2)`);
}
const log = (...a) => console.log('[coins-capture]', ...a);

async function newGuest(browser, vp, report, name) {
  const context = await browser.newContext({ viewport: { width: vp.w, height: vp.h }, deviceScaleFactor: 1, isMobile: vp.mobile, hasTouch: vp.mobile, locale: LANG_CODE === 'en' ? 'en-US' : 'vi-VN' });
  await context.addInitScript((code) => {
    try { if (!localStorage.getItem('nosum.settings.v1')) localStorage.setItem('nosum.settings.v1', code === 'en' ? 'lang=2\n' : 'lang=1\n'); } catch (e) { /* blocked storage: defaults */ }
  }, LANG_CODE);
  const page = await context.newPage();
  page.on('pageerror', (e) => report.errors.push(`${name}: ${String(e)}`));
  await page.goto(`${BASE_URL}/?qa=1`, { waitUntil: 'load', timeout: 120000 });
  const guest = page.getByRole('button', { name: /as a guest|guest|khách/i }).first();
  await guest.waitFor({ state: 'visible', timeout: 120000 });
  await guest.click();
  await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
  await sleep(3000);
  return page;
}

async function shot(page, dir, name, report) {
  const file = path.join(dir, `${LANG_CODE}-${name}.png`);
  await page.screenshot({ path: file, animations: 'disabled' });
  report.captured.push(path.relative(OUT_DIR, file));
  log('saved', file);
}

// Records whether the text the screen must show is on the page now.
async function check(page, report, screen, expected) {
  const found = (await page.getByText(expected).count()) > 0;
  report.checks.push({ screen, expected: String(expected), found });
  log(found ? 'text ok' : 'TEXT MISSING', screen, String(expected));
}

// The action key at the counter opens the trader's box; its Sell button opens the market (docs/adr/ADR-043, ADR-034).
async function openCounter(page) {
  await page.evaluate(([map, x, y]) => window.nosumQa.teleport(map, x, y), [TOWN, COUNTER.x, COUNTER.y]);
  await sleep(1500);
  await page.keyboard.press('KeyE');
  const npcSell = page.getByRole('button', { name: T.npcSell, exact: true }).first();
  try {
    await npcSell.waitFor({ state: 'visible', timeout: 8000 });
    await npcSell.click();
  } catch (e) {
    log('no trader box at the counter; the market may already be open');
  }
  await sleep(900);
}

// Lists one stack from the Sell tab: pick the parsnip slot, type the quantity and the total price, press List.
async function listStack(seller, qty, price) {
  await seller.locator('.item-grid button').nth(PARSNIP_SLOT).click();
  await sleep(300);
  const form = seller.locator('.sell-form');
  await form.locator('input').nth(0).fill(String(qty));
  await form.locator('input').nth(1).fill(String(price));
  await sleep(200);
  await seller.getByRole('button', { name: T.list, exact: true }).click();
  await sleep(1200);
}

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const report = { lang: LANG_CODE, base: BASE_URL, captured: [], checks: [], errors: [] };
  for (const vp of VIEWPORTS) {
    const label = `${vp.w}x${vp.h}`;
    const dir = path.join(OUT_DIR, label);
    fs.mkdirSync(dir, { recursive: true });
    const seller = await newGuest(browser, vp, report, 'seller');
    seedGifts();
    const buyer = await newGuest(browser, vp, report, 'buyer');
    await seller.evaluate(([item, n]) => window.nosumQa.grant(item, n, 0), [PARSNIP, LISTINGS.length]);
    await buyer.evaluate(() => window.nosumQa.grant(0, 0, 3000));
    await sleep(800);

    // Seller lists two single parsnips at the counter.
    await openCounter(seller);
    await seller.getByRole('button', { name: T.sell, exact: true }).first().click();
    await sleep(400);
    for (const price of LISTINGS) {
      await listStack(seller, 1, price);
    }

    // Buyer buys both listings at the market counter, newest first (the 40 listing, then the 60). The status names the last purchase: a parsnip for 60.
    await openCounter(buyer);
    await buyer.getByRole('button', { name: T.browse, exact: true }).first().click();
    await sleep(800);
    for (let i = 0; i < LISTINGS.length; i++) {
      await buyer.locator('.listing-row').getByRole('button', { name: T.buy, exact: true }).first().click();
      await sleep(1200);
    }
    await shot(buyer, dir, 'buyer-market-bought', report);
    await check(buyer, report, 'buyer-market-bought', EXPECT.marketBought);

    // Buyer buys one seed at the seed shop: the status names the seed and the coins the server took.
    await buyer.evaluate(([map, x, y]) => window.nosumQa.teleport(map, x, y), [TOWN, SEED_SHOP.x, SEED_SHOP.y]);
    await sleep(1500);
    // The action key at the shop opens the trader's box first; its Buy button opens the shop's list, and the list's own Buy buys the seed.
    await buyer.keyboard.press('KeyE');
    const boxBuy = buyer.getByRole('button', { name: T.shopBuy, exact: true }).first();
    await boxBuy.waitFor({ state: 'visible', timeout: 15000 });
    await boxBuy.click();
    await sleep(1500);
    await buyer.getByRole('button', { name: T.shopBuy, exact: true }).first().click();
    await sleep(1200);
    await shot(buyer, dir, 'buyer-shop-bought', report);
    await check(buyer, report, 'buyer-shop-bought', EXPECT.shopBought);

    // Seller opens the mail: the two seeded gifts (160 coins and two parsnips) collected in one run, the line names the total.
    await seller.getByRole('button', { name: T.mail }).first().click();
    await sleep(900);
    const collectAll = seller.getByRole('button', { name: T.collectAll }).first();
    try {
      await collectAll.waitFor({ state: 'visible', timeout: 15000 });
    } catch (e) {
      // Diagnostic: the names of the mail page's buttons, so a changed label is visible in the log.
      await shot(seller, dir, 'seller-mail-not-found', report);
      log('mail buttons:', JSON.stringify(await seller.getByRole('button').allTextContents()));
      throw e;
    }
    await collectAll.click();
    await sleep(2000);
    await shot(seller, dir, 'seller-mail-collected', report);
    await check(seller, report, 'seller-mail-collected', EXPECT.mailCollected);

    await seller.close();
    await buyer.close();
    log('done', label);
  }
  // One report per run: the language and the viewports it captured, so a run for one size does not overwrite another's checks.
  const sizes = VIEWPORTS.map((v) => `${v.w}x${v.h}`).join('_');
  fs.writeFileSync(path.join(OUT_DIR, `${LANG_CODE}-coins-${sizes}-report.json`), JSON.stringify(report, null, 2));
  await browser.close();
}

run().catch((e) => {
  console.error(e);
  process.exit(1);
});
