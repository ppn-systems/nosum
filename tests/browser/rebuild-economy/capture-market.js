// Market captures for the economy rebuild (docs/adr/ADR-013, ADR-034, ADR-081). Playwright, Chromium.
// Two fresh guests: a seller lists stacks at the trader's counter in Town through the Sell tab; a buyer browses, sorts and buys.
// Usage (from the repo root):
//   NODE_PATH=$(npm root -g) BASE_URL=http://localhost:5810 OUT_DIR=docs/qa/rebuild-economy/after LANG_CODE=en VIEWPORTS=1280x720,390x844 \
//     node tests/browser/rebuild-economy/capture-market.js
// Needs a build made with -p:NosumDevTools=true (window.nosumQa on a ?qa=1 page) and a Development server (the grant is refused elsewhere).
// Writes OUT_DIR/<WxH>/<lang>-<screen>.png and OUT_DIR/<lang>-report.json (what was captured, what was skipped and why, page errors).
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5810').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || path.join(__dirname, 'out'));
const LANG_CODE = process.env.LANG_CODE === 'vi' ? 'vi' : 'en';
const ALL = [
  { w: 1280, h: 720, mobile: false },
  { w: 1920, h: 1080, mobile: false },
  { w: 390, h: 844, mobile: true },
];
const wanted = (process.env.VIEWPORTS || '').split(',').map((s) => s.trim()).filter(Boolean);
const VIEWPORTS = wanted.length ? ALL.filter((v) => wanted.includes(`${v.w}x${v.h}`)) : ALL;

// Item id of the parsnip (gameplay ItemCatalog.Parsnip) and the Town market counter (GameMaps: the market tile 45, 71).
const PARSNIP = 200;
const TOWN = 'town';
const COUNTER = { x: 45, y: 71 };
// The parsnip is the ninth slot of a new farmer's inventory (after the starter tools and seeds).
const PARSNIP_SLOT = 8;
// Listings the seller makes: quantity, total price. Unit prices 30, 25 and 60: the sort order differs from the list order.
// Round 3: eight listings (a full page of eight at 1280x720 and 1920x1080). The seller is granted 12 parsnips, so the stacks sum to 12.
const LISTINGS = [
  { qty: 1, price: 30 },
  { qty: 1, price: 25 },
  { qty: 1, price: 60 },
  { qty: 1, price: 20 },
  { qty: 2, price: 120 },
  { qty: 2, price: 75 },
  { qty: 2, price: 300 },
  { qty: 2, price: 90 },
];

// Labels per language (Language.resx). Sort labels exist only once the sort control is built; the script skips the step otherwise.
const T = LANG_CODE === 'en'
  ? { sell: 'Sell', browse: 'Browse', list: 'List for sale', buy: 'Buy', quantity: 'Quantity', sortLow: 'Cheapest each', sortHigh: 'Priciest each', npcSell: 'Sell', confirm: 'Confirm' }
  : { sell: 'Bán', browse: 'Mua', list: 'Đăng bán', buy: 'Mua', quantity: 'Số lượng', sortLow: 'Rẻ nhất/món', sortHigh: 'Đắt nhất/món', npcSell: 'Bán', confirm: 'Xác nhận' };

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (...a) => console.log('[market-capture]', ...a);

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

async function skip(report, name, why) {
  report.skipped.push({ screen: name, reason: why });
  log('skipped', name, why);
}

// The action key at the counter opens the trader's box; its Sell button opens the market (docs/adr/ADR-043, ADR-034).
async function openCounter(page) {
  await page.evaluate(([map, x, y]) => window.nosumQa.teleport(map, x, y), [TOWN, COUNTER.x, COUNTER.y]);
  await sleep(1500);
  await page.keyboard.press('KeyE');
  // The trader's line types out before its options (Buy, Sell, Talk, Leave) appear: wait for Sell, if this counter opens the box at all.
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
  const report = { lang: LANG_CODE, base: BASE_URL, captured: [], skipped: [], errors: [] };
  for (const vp of VIEWPORTS) {
    const label = `${vp.w}x${vp.h}`;
    const dir = path.join(OUT_DIR, label);
    fs.mkdirSync(dir, { recursive: true });
    const seller = await newGuest(browser, vp, report, 'seller');
    const buyer = await newGuest(browser, vp, report, 'buyer');
    await seller.evaluate((item) => window.nosumQa.grant(item, 12, 0), PARSNIP);
    await buyer.evaluate(() => window.nosumQa.grant(0, 0, 3000));
    await buyer.evaluate((item) => window.nosumQa.grant(item, 6, 0), PARSNIP);
    await sleep(800);

    // Seller: the Sell tab, then the three stacks.
    await openCounter(seller);
    await seller.getByRole('button', { name: T.sell, exact: true }).first().click();
    await sleep(400);
    await shot(seller, dir, 'seller-sell-empty', report);
    await seller.locator('.item-grid button').nth(PARSNIP_SLOT).click();
    await sleep(300);
    await seller.locator('.sell-form input').nth(1).fill('120');
    await sleep(300);
    await shot(seller, dir, 'seller-sell-picked', report);
    for (const l of LISTINGS) {
      await listStack(seller, l.qty, l.price);
    }
    await shot(seller, dir, 'seller-listed', report);

    // Buyer: the Browse tab shows the listings (newest first by default).
    await openCounter(buyer);
    await buyer.getByRole('button', { name: T.browse, exact: true }).first().click();
    await sleep(800);
    await shot(buyer, dir, 'buyer-browse', report);
    // The no-inner-scroll check (ui-standard section 7, item 7): the menu page body must not scroll on a desktop size.
    const body = await buyer.evaluate(() => {
      const b = document.querySelector('.menu-page-body');
      if (!b) return null;
      const parts = [...b.children].map((c) => `${c.className || c.tagName}:${Math.round(c.getBoundingClientRect().height)}`);
      const f = document.querySelector('.market-filter');
      const filter = f ? { h: Math.round(f.getBoundingClientRect().height), w: Math.round(f.getBoundingClientRect().width), kids: [...f.children].map((c) => `${c.tagName}.${c.className}:${Math.round(c.getBoundingClientRect().width)}x${Math.round(c.getBoundingClientRect().height)}`) } : null;
      const stack = b.querySelector('.game-stack');
      const kids = stack ? [...stack.children].map((c) => `${c.tagName}.${(c.className || '').toString().split(' ')[0]}:${Math.round(c.getBoundingClientRect().height)}`) : [];
      return { clientHeight: b.clientHeight, scrollHeight: b.scrollHeight, listings: document.querySelectorAll('.listing-row').length, kids, filter };
    });
    report.measures = report.measures || [];
    report.measures.push({ viewport: label, ...body });

    // Sort (added by the rebuild): skipped where the control does not exist yet.
    const low = buyer.getByRole('button', { name: T.sortLow, exact: true });
    if (await low.count()) {
      await low.first().click();
      await sleep(800);
      await shot(buyer, dir, 'buyer-browse-sorted', report);
    } else {
      await skip(report, 'buyer-browse-sorted', 'no sort control in this build');
    }

    // Large purchase (ADR-013 A10, round 2): opt-in with LARGE_BUY=1, after the browse shots so the rows above match the before set.
    // The seller lists nine for 1,500 (nine is below the seller's own confirm, ten), the buyer buys it and sees the confirm box.
    if (process.env.LARGE_BUY === '1') {
      await seller.evaluate((item) => window.nosumQa.grant(item, 9, 0), PARSNIP);
      await sleep(400);
      // The granted parsnips land in a free slot, not in the slot the first stacks used: pick the parsnip by its name.
      await seller.locator('.item-grid button', { has: seller.locator('.quantity', { hasText: /^9$/ }) }).first().click();
      await sleep(300);
      await seller.locator('.sell-form input').nth(0).fill('9');
      await seller.locator('.sell-form input').nth(1).fill('1500');
      await sleep(200);
      await seller.getByRole('button', { name: T.list, exact: true }).click();
      await sleep(1200);
      // Choosing a sort asks the server for the page again, so the new listing is in it; priciest each puts it first.
      await buyer.getByRole('button', { name: T.sortHigh, exact: true }).first().click();
      await sleep(800);
      const big = buyer.locator('.listing-row', { hasText: /1[.,]500/ }).getByRole('button', { name: T.buy, exact: true });
      await big.first().click();
      await sleep(600);
      await shot(buyer, dir, 'buyer-buy-confirm', report);
      await buyer.getByRole('button', { name: T.confirm, exact: true }).first().click();
      await sleep(1200);
      await shot(buyer, dir, 'buyer-bought-large', report);
    }

    // Buyer's own sell form, a price typed: the fee line and, once built, the bin price hint.
    await buyer.getByRole('button', { name: T.sell, exact: true }).first().click();
    await sleep(400);
    await buyer.locator('.item-grid button').nth(PARSNIP_SLOT).click();
    await sleep(300);
    // The stack is picked and no price typed yet: the line under the form is what the shipping bin pays.
    await shot(buyer, dir, 'buyer-sell-picked', report);
    await buyer.locator('.sell-form input').nth(1).fill('150');
    await sleep(300);
    await shot(buyer, dir, 'buyer-sell-priced', report);

    await seller.close();
    await buyer.close();
    log('done', label);
  }
  fs.writeFileSync(path.join(OUT_DIR, `${LANG_CODE}-report.json`), JSON.stringify(report, null, 2));
  await browser.close();
}

run().catch((e) => {
  console.error(e);
  process.exit(1);
});
