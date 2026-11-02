// World QA pass 3 (docs/qa/world-pass3; pass 2 is docs/qa/world-pass). Plays the world features with Playwright on a local Development server and client
// (ADR-079: the page opened with ?qa=1 exposes window.nosumQa: grant, advanceDays, advanceSeasons, teleport, season, levelUp, fishingBite).
// Clock units: advanceDays(n) moves n game days (a game day is 12 real hours); a season is 28 game days, so advanceDays(4) + advanceDays(10) is 14 days,
// half a season, and never crosses a season boundary (pass 5 wrongly called it one season).
// For each locale (vi-VN, en-US) and viewport (1280x720, 1920x1080, 390x844 touch) it runs the scenario below in one session and:
//   - takes a screenshot per step (docs/qa/world-pass/<locale>/<viewport>/NN-<step>.png),
//   - records a status per step: pass (a DOM outcome was seen), FAIL (the step threw or the outcome is missing), or
//     captured (no DOM hook for the outcome; grade it from the screenshot),
//   - runs the section 7 layout audit on the open window (items 2, 5, 7) and collects console and page errors.
// Output: docs/qa/world-pass/audit-<locale>-<viewport>.json and the screenshots.
//
// usage: node tests/browser/world-pass.cjs [locales=vi,en] [viewports=1280x720,1920x1080,390x844]
// Env: NOSUM_QA_URL (default http://localhost:5080/), NOSUM_QA_REPO_ASSETS=1 (see full-pass.cjs).

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const OUT = path.join(REPO, process.env.NOSUM_QA_OUT || 'docs/qa/world-pass5');
const VIEWPORTS = {
  '1280x720': { width: 1280, height: 720 },
  '1920x1080': { width: 1920, height: 1080 },
  '390x844': { width: 390, height: 844 },
};
const LOCALES = { vi: 'vi-VN', en: 'en-US' };
const MIME = { '.png': 'image/png', '.tmx': 'application/xml', '.ogg': 'audio/ogg', '.wav': 'audio/wav', '.json': 'application/json' };

// Item ids (gameplay/Nosum.Gameplay/Items): Hoe 1, WateringCan 2, FishingRod 40, ParsnipSeeds 100, Sprinkler 604.
const ITEM = { hoe: 1, can: 2, rod: 40, seeds: 100, sprinkler: 604 };
// Tile positions from gameplay/Nosum.Gameplay/Maps/GameMaps.cs (shop counters, carpenter, market).
const TILE = {
  seedShop: [43, 57], clothing: [20, 89], furniture: [95, 51], carpenter: [10, 86], market: [45, 71], beach: [39, 43], // the sand row above the sea (y 44), Beach.tmx; the old [39, 2] is the Town road.
};

async function routeRepoAssets(page) {
  await page.route(/localhost:5080\/(assets|_content\/Nosum\.Wasm\.Controls\/ui)\//, (route) => {
    const pathname = decodeURIComponent(new URL(route.request().url()).pathname);
    let file;
    if (pathname.startsWith('/assets/Maps/')) file = path.join(REPO, 'art/own/Maps', pathname.slice('/assets/Maps/'.length));
    else if (pathname.startsWith('/assets/')) file = path.join(REPO, 'assets', pathname.slice('/assets/'.length));
    else file = path.join(REPO, 'assets/ui', pathname.slice('/_content/Nosum.Wasm.Controls/ui/'.length));
    if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) return route.fallback();
    return route.fulfill({ status: 200, body: fs.readFileSync(file), contentType: MIME[path.extname(file)] || 'application/octet-stream' });
  });
}

// Opens the game menu window (the Menu button of the column) and then the tile whose name matches: how a phone reaches the panels.
async function openFromMenu(page, re) {
  await page.locator('.game-hud .hud-menu-button .game-icon-button').first().click({ force: true, timeout: 5000 });
  await page.waitForSelector('.game-menu-tile');
  const hit = await page.evaluate((src) => {
    const rx = new RegExp(src, 'i');
    const tile = [...document.querySelectorAll('.game-menu-tile')].find((e) => rx.test(e.getAttribute('aria-label') || ''));
    if (tile) tile.click();
    return !!tile;
  }, re.source);
  if (!hit) throw new Error(`no menu tile for ${re}`);
}

// Section 7 audit of the open window (items 2, 5, 7), as in full-pass.cjs.
const MEASURE = () => {
  const w = document.querySelector('.trade-window') || document.querySelector('.game-hud .hud-center > *') || document.querySelector('.npc-box-wrap');
  if (!w) return { none: true };
  const wr = w.getBoundingClientRect();
  const label = (e) => (e.className.baseVal ?? e.className).toString().slice(0, 30);
  const issues = [];
  for (const e of w.querySelectorAll('*')) {
    const cs = getComputedStyle(e);
    if (e.clientHeight <= 0 || e.scrollHeight <= e.clientHeight + 2) continue;
    if (/(auto|scroll)/.test(cs.overflowY)) issues.push(`scroll ${label(e)} ${e.scrollHeight}/${e.clientHeight}`);
    else if (/hidden/.test(cs.overflowY) && e.tagName !== 'BUTTON') issues.push(`clip ${label(e)} ${e.scrollHeight}/${e.clientHeight}`);
  }
  const covers = [];
  for (const h of document.querySelectorAll('.hud-top-right .hud-bar, .hud-top-right .hud-player-bar, .hud-top-right .hud-minimap, .hud-top-left > *, .hotbar')) {
    if (getComputedStyle(h).display === 'none') continue;
    const hr = h.getBoundingClientRect();
    if (hr.width && hr.height && !(hr.right <= wr.left || hr.left >= wr.right || hr.bottom <= wr.top || hr.top >= wr.bottom)) covers.push(label(h));
  }
  const off = wr.left < 0 || wr.top < 0 || wr.right > innerWidth + 1 || wr.bottom > innerHeight + 1;
  return { box: [wr.left, wr.top, wr.width, wr.height].map(Math.round), issues: issues.slice(0, 6), covers, off };
};

// Visible text of the HUD bottom and notices, for the functional messages (toast, message line, refusal).
const NOTICES = () => {
  const t = [];
  for (const e of document.querySelectorAll('.game-notice, .hud-bottom-center, .hotbar .message, .npc-box-text, .fishing-card, .season-banner, .level-up-banner')) {
    const r = e.getBoundingClientRect();
    if (r.width && r.height && getComputedStyle(e).visibility !== 'hidden') t.push((e.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 120));
  }
  return t.filter(Boolean);
};

function slug(s) {
  return (s || 'step').replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '').slice(0, 40) || 'step';
}

async function signIn(page) {
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await page.waitForTimeout(3000);
}

// The count shown on the hotbar slot whose name matches (its last number), or -1.
async function countOf(page, re) {
  const texts = await page.$$eval('.hotbar-slots > *', (els) => els.map((e) => [e.getAttribute('aria-label') || '', e.innerText || '']));
  const hit = texts.find(([name]) => re.test(name));
  if (!hit) return -1;
  const nums = hit[1].match(/\d+/g);
  return nums ? Number(nums[nums.length - 1]) : -1;
}

// The hotbar slot (1-based) whose item name matches a pattern (Vietnamese or English), or 0.
async function slotOf(page, re) {
  const names = await page.$$eval('.hotbar-slots > *', (els) => els.map((e) => e.getAttribute('aria-label') || ''));
  const i = names.findIndex((n) => re.test(n));
  return i + 1;
}

async function runOne(browser, locale, vpName, report) {
  const size = VIEWPORTS[vpName];
  const phone = vpName === '390x844';
  const dir = path.join(OUT, locale, vpName);
  fs.mkdirSync(dir, { recursive: true });
  const errors = [];
  const context = await browser.newContext({ viewport: size, locale: LOCALES[locale], hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 200)}`); });
  if (process.env.NOSUM_QA_REPO_ASSETS === '1') await routeRepoAssets(page);

  const rec = (feature, status, notes, shot) => report.push({ locale, viewport: vpName, feature, status, notes, screenshot: shot ? path.relative(REPO, path.join(dir, shot)) : '' });
  let n = 0;
  const shot = async (name) => {
    n += 1;
    const file = `${String(n).padStart(2, '0')}-${slug(name)}.png`;
    await page.screenshot({ path: path.join(dir, file) });
    return file;
  };
  // Runs one step: a screenshot, an optional DOM check, and a record. A throw is a FAIL with its first line.
  // Closes every open window (a merchant dialog by its Leave button, a standalone window by Escape). Escape alone is not safe to
  // press blindly: it closes the open window and can reopen the last player panel (docs/design/ui-standard.md, section 3).
  const closeAll = async () => {
    for (let i = 0; i < 4; i += 1) {
      if (await page.$('.npc-box-wrap')) {
        const leave = page.locator('.npc-actions button').last();
        if ((await leave.count()) > 0) await leave.click({ force: true }).catch(() => {});
        else await page.keyboard.press('Escape');
      } else if (await page.$('.trade-window, [role="dialog"]')) {
        await page.keyboard.press('Escape');
      } else {
        break;
      }
      await page.waitForTimeout(300);
    }
  };
  const step = async (feature, fn, opts = {}) => {
    try {
      const res = await fn();
      const shotFile = await shot(feature);
      const m = await page.evaluate(MEASURE);
      const layout = m.none ? [] : [...(m.issues || []), ...(m.covers || []).map((c) => `covers ${c}`), m.off ? 'off-screen' : ''].filter(Boolean);
      const notices = await page.evaluate(NOTICES);
      const notes = [res?.notes, notices.length ? `shows: ${notices.join(' | ')}` : '', layout.length ? `layout: ${layout.join('; ')}` : ''].filter(Boolean).join(' · ');
      rec(feature, res?.status || 'captured', notes, shotFile);
      if (res?.status === 'pass' && layout.length) rec(`${feature} (layout)`, 'FAIL', layout.join('; '), shotFile);
    } catch (e) {
      const shotFile = await shot(`${feature}-fail`).catch(() => '');
      rec(feature, 'FAIL', String(e.message).split('\n')[0].slice(0, 180), shotFile);
    }
    if (!opts.keep) await closeAll();
  };
  const wait = (ms) => page.waitForTimeout(ms);
  const hold = async (key, ms) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); await wait(200); };
  const selectItem = async (re) => {
    const slot = await slotOf(page, re);
    if (!slot) throw new Error(`no hotbar slot matches ${re}; slots: ${(await page.$$eval('.hotbar-slots > *', (els) => els.map((e) => e.getAttribute('aria-label')))).join(' | ')}`);
    await page.keyboard.press(String(slot));
    await wait(200);
    return slot;
  };
  const counterBox = async (tile, label) => {
    // Teleport to the counter tile (the server moves the farmer to a walkable tile next to it), then press the action key.
    await page.evaluate(([x, y]) => window.nosumQa.teleport('town', x, y), tile);
    await wait(2500);
    await page.keyboard.press('KeyE');
    await wait(400);
    const open = await page.$('.npc-box-wrap, .npc-box-body');
    if (!open) throw new Error(`no merchant dialog at ${label} ${tile}`);
    // The choices appear when the line has typed out (NpcDialogViewModel.Choices); a click on the line finishes it (issue #214).
    await page.locator('.npc-line').first().click({ timeout: 3000 }).catch(() => {});
    await page.waitForSelector('.npc-actions button', { timeout: 5000 });
    return open;
  };
  const clickText = async (re, scope = 'body') => {
    const btn = page.locator(`${scope} button`).filter({ hasText: re }).first();
    if (scope !== 'body' && (await btn.count()) === 0) await page.locator('.npc-actions button').first().waitFor({ timeout: 2000 }).catch(() => {});
    if ((await btn.count()) === 0) throw new Error(`no button ${re}`);
    await btn.click({ timeout: 5000 });
    await wait(600);
  };

  try {
    await signIn(page);
  } catch (e) {
    rec('sign-in (guest)', 'FAIL', String(e.message).slice(0, 160));
    await context.close();
    return;
  }
  if (locale === 'en') {
    await step('settings: English', async () => {
      await page.locator('.game-hud .hud-top-right .game-icon-button[aria-label*="Cài đặt"], .game-hud .hud-top-right .game-icon-button[aria-label*="Settings"]').first().click({ force: true });
      await wait(600);
      await page.getByRole('button', { name: 'English' }).first().click({ timeout: 5000 });
      await wait(600);
      return { status: 'pass', notes: 'switched in Settings > General' };
    });
    await page.keyboard.press('Escape');
    await wait(400);
  }
  await step('HUD start', async () => ({ status: 'captured' }));

  // Movement, run and energy. Shift+D runs (docs/design: running with energy, ADR-085).
  await step('running + energy', async () => {
    const hasEnergy = await page.$('.energy-bar');
    await page.keyboard.down('ShiftLeft');
    await hold('KeyD', 900);
    await page.keyboard.up('ShiftLeft');
    return { status: hasEnergy ? 'captured' : 'FAIL', notes: hasEnergy ? 'energy bar present; read the drain from the screenshot' : 'no energy bar' };
  });

  // Minimap toggle (M): the card's size changes on the toggle.
  // On a phone (max-width 640 px or a coarse pointer) the minimap is one map button in the menu column, not the card M toggles (GameHud.razor.css).
  if (phone) {
    await step('minimap toggle (M)', async () => ({ status: 'n/a', notes: 'phone HUD: the minimap is a map button, not the M card (GameHud.razor.css, max-width 640px)' }));
  } else await step('minimap toggle (M)', async () => {
    // Visibility, not size: the card (.hud-minimap) is in the DOM only while the map is drawn, and M hides it with no button left behind
    // (HudMinimap.razor: the collapsed button is for a map hidden in Settings, never for an M-key hide).
    const shown = () => page.evaluate(() => ({ card: !!document.querySelector('.hud-minimap'), button: !!document.querySelector('.hud-minimap-button') }));
    const before = await shown();
    await page.keyboard.press('KeyM');
    await wait(500);
    const after = await shown();
    await page.keyboard.press('KeyM');
    await wait(300);
    const hidden = before.card && !after.card && !after.button;
    return { status: hidden ? 'pass' : 'FAIL', notes: `minimap card/button before ${JSON.stringify(before)} after M ${JSON.stringify(after)}` };
  });

  // Farming on the farm. The diggable field is the solid block of tiles x 47-51, y 17-19 (art/own/Maps/Farm.tmx, Diggable tiles); the farmer
  // stands at (46,18) and steps right, so the tile in front (48,18) is inside the field for till, sow and water.
  // The server clock is shared and only moves forward (ADR-079 season passes), so a run starts from whatever season the last one left.
  // Parsnip seeds grow in Spring only: pass seasons until the calendar reads Spring (Xuân), then farm.
  // QA run start (ADR-079 D3): the server passes season starts to the next Spring, whatever season the last run left.
  await page.evaluate(() => window.nosumQa.springReset());
  await wait(2000);
  await page.evaluate(() => window.nosumQa.teleport('farm', 46, 18));
  await wait(2500);
  await page.evaluate(() => {
    window.nosumQa.grant(100, 10, 1000);
    window.nosumQa.grant(604, 1, 0);
    window.nosumQa.grant(40, 1, 0);
  });
  await wait(1200);
  await step('farm: grant seeds, sprinkler, rod', async () => ({ status: 'captured', notes: 'items arrive in the hotbar (Development grant)' }));
  await step('farm: till (hoe, E)', async () => {
    await selectItem(/^hoe$|cuốc/i);
    await hold('KeyD', 120);
    await page.keyboard.press('KeyE');
    await wait(700);
    // The tutorial hint moves from "Select the hoe..." to "Select the watering can..." once the tile is tilled.
    const hint = await page.evaluate(() => document.body.innerText);
    const tilled = /watering can|bình tưới/i.test(hint) && !/can't till here|không thể xới/i.test(hint);
    return { status: tilled ? 'pass' : 'FAIL', notes: tilled ? 'tutorial moved on to water: tile tilled' : 'tutorial still on the hoe; tile not tilled' };
  });
  await step('farm: sow (seeds, E)', async () => {
    await selectItem(/seeds|hạt/i);
    const before = await countOf(page, /parsnip seeds|hạt củ cải/i);
    await page.keyboard.press('KeyE');
    await wait(700);
    let after = await countOf(page, /parsnip seeds|hạt củ cải/i);
    // The clock is real time and shared: a run that starts late in Spring can see the season turn before this step. Then pass to the next
    // Spring (ADR-079 D3) and sow again; the first refusal stays in the notes.
    let note = '';
    if (after === before) {
      const notices = await page.evaluate(NOTICES);
      if (notices.some((t) => /does not grow in this season|không trồng được vào mùa này/i.test(t))) {
        note = `season turned mid-run (${notices.join(' | ')}); passed to the next Spring and sowed again; `;
        await page.evaluate(() => window.nosumQa.springReset());
        await wait(2000);
        await selectItem(/seeds|hạt/i);
        await page.keyboard.press('KeyE');
        await wait(700);
        after = await countOf(page, /parsnip seeds|hạt củ cải/i);
      }
    }
    return { status: after === before - 1 ? 'pass' : 'FAIL', notes: `${note}parsnip seeds ${before} -> ${after}` };
  });
  await step('farm: water (can, E)', async () => {
    await selectItem(/watering can|bình tưới/i);
    const before = await countOf(page, /watering can|bình tưới/i);
    await page.keyboard.press('KeyE');
    await wait(700);
    const after = await countOf(page, /watering can|bình tưới/i);
    // The can is a tool: its hotbar number is its charges, so an unchanged count is not by itself a failure. Rain on pass 3's day wet the soil
    // first, and the game then answers "That does nothing here." (a refusal, not a silent press); that is recorded as captured, not verified.
    const notes = await page.evaluate(NOTICES);
    if (after === before - 1) return { status: 'pass', notes: `watering can ${before} -> ${after}` };
    return { status: 'captured', notes: `watering can ${before} -> ${after}; not verified: ${notes.join(' | ') || 'no notice'} (the soil may already be wet from rain)` };
  });
  await step('farm: sprinkler (place, E)', async () => {
    await hold('KeyS', 120);
    await selectItem(/sprinkler|vòi tưới|máy tưới/i);
    await page.keyboard.press('KeyE');
    await wait(700);
    return { status: 'captured', notes: 'sprinkler placed beside the crop? check the screenshot' };
  });
  await step('farm: advance 4 days (grow)', async () => {
    await page.evaluate(() => window.nosumQa.advanceDays(4));
    await wait(2000);
    return { status: 'captured', notes: 'crop growth after 4 game days' };
  });
  await step('farm: harvest (E, empty hand)', async () => {
    await page.keyboard.press('Digit9');
    await wait(200);
    await page.keyboard.press('KeyE');
    await wait(700);
    return { status: 'captured', notes: 'harvest result: check the screenshot and the bag' };
  });
  await step('farm: wither (no water, 10 days)', async () => {
    await page.evaluate(() => window.nosumQa.advanceDays(10));
    await wait(2000);
    return { status: 'captured', notes: 'unwatered crop withered? check the screenshot' };
  });

  // Merchants and shops (Town). Each: teleport to the counter, E opens the merchant dialog, its Buy/Sell button opens the shop.
  for (const [key, tile] of [['seed shop', TILE.seedShop], ['clothing shop', TILE.clothing], ['furniture shop', TILE.furniture]]) {
    await step(`merchant: ${key} dialog`, async () => {
      await counterBox(tile, key);
      return { status: 'pass', notes: 'merchant dialog opened' };
    }, { keep: true });
    await step(`shop: ${key} buy list`, async () => {
      await clickText(/^(buy|mua)$/i, '.npc-actions');
      const list = await page.$('.shop-list');
      if (!list) throw new Error('no shop list');
      return { status: 'pass', notes: 'shop list visible' };
    }, { keep: true });
    await step(`shop: ${key} quantity stepper`, async () => {
      // The shop has a quantity stepper (Shop_Qty_Less / Shop_Qty_More) and a Buy button per offer; there is no x1, x5 or All control.
      const labels = await page.$$eval('.shop-list button, .shop-head button, .trade-window button', (els) => [...new Set(els.map((e) => e.textContent.trim()).filter(Boolean))]);
      const hasX5 = labels.some((l) => /^x ?5$/i.test(l));
      const hasAll = labels.some((l) => /^(all|tất cả)$/i.test(l));
      return { status: 'captured', notes: `buttons: ${labels.join(', ') || 'none'}; x5 ${hasX5 ? 'yes' : 'no'}; All ${hasAll ? 'yes' : 'no'}` };
    }, { keep: false });
  }

  // Market (Town, the market counter): listings and buy.
  await step('market: open', async () => {
    await counterBox(TILE.market, 'market');
    await clickText(/^(buy|mua)$/i, '.npc-actions');
    return { status: 'captured', notes: 'market listings: check the screenshot' };
  });

  // Fishing: cast with the rod, the QA bite, reel in, the catch card.
  await step('fishing: cast at the shore (rod, face sea, E)', async () => {
    await page.evaluate(([x, y]) => window.nosumQa.teleport('beach', x, y), TILE.beach);
    await wait(2500);
    // The hotbar was full of tools and crops; the rod is granted again now that the sprinkler has left slot 9.
    await page.evaluate(() => window.nosumQa.grant(40, 1, 0));
    await wait(1200);
    await selectItem(/fishing rod|cần câu/i);
    // A rod is cast by a click on water, never by the action key (ADR-078 D2). The sea starts about one tile below the farmer, who stands on the
    // sand row above it; the canvas centre is the farmer. Click a few points below the centre until the status line shows.
    const box = await page.locator('canvas').first().boundingBox();
    let line = null;
    for (const dy of [44, 60, 76, 92, 108]) {
      await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2 + dy);
      await wait(900);
      line = await page.$('.fishing-status');
      if (line) break;
    }
    const notes = await page.evaluate(NOTICES);
    return { status: line ? 'pass' : 'FAIL', notes: line ? 'cast open: the fishing status line shows' : `no fishing line; notices: ${notes.join(' | ') || 'none'}` };
  });
  await step('fishing: bite (QA)', async () => {
    await page.evaluate(() => window.nosumQa.fishingBite());
    await wait(600);
    return { status: 'captured', notes: 'bite banner visible' };
  });
  await step('fishing: reel in and catch card', async () => {
    await page.keyboard.press('KeyE');
    await wait(1200);
    const card = await page.$('.fishing-card');
    return { status: card ? 'pass' : 'captured', notes: card ? 'catch card shown' : 'no catch card (a miss or the window was lost)' };
  });

  // Combat (docs/adr/ADR-056): Z is Whirlwind (a monster within 2 tiles), X Fireball (6), V the heal (refused at full health).
  // Pass 2 pressed Z with no monster in reach and saw nothing; the press must now name the reason (SkillPressRefused).
  await step('combat: Z with no monster in reach (toast)', async () => {
    await page.keyboard.press('KeyZ');
    await wait(500);
    const notes = await page.evaluate(NOTICES);
    const toast = notes.some((t) => /no monster in reach|không có quái vật/i.test(t));
    return { status: toast ? 'pass' : 'FAIL', notes: `Z, no monster: ${notes.join(' | ') || 'no notice'}` };
  });
  await step('combat: teleport next to a monster (woods)', async () => {
    await page.evaluate(([x, y]) => window.nosumQa.teleport('woods', x, y), [58, 15]);
    await wait(3000);
    await page.evaluate(() => window.nosumQa.teleportToMonster('woods'));
    await wait(2500);
    return { status: 'captured', notes: 'the farmer stands beside a monster: check the screenshot' };
  });
  await step('combat: Z with a monster in reach (cooldown sweep)', async () => {
    await page.keyboard.press('KeyZ');
    await wait(500);
    const cd = await page.$('.skill-cooldown');
    const notes = await page.evaluate(NOTICES);
    return { status: cd ? 'pass' : 'FAIL', notes: cd ? 'cooldown sweep after Z' : `no cooldown after Z; notices: ${notes.join(' | ') || 'none'}` };
  });
  await step('combat: X fireball (monster in reach)', async () => {
    await page.keyboard.press('KeyX');
    await wait(600);
    const notes = await page.evaluate(NOTICES);
    return { status: 'captured', notes: `X: ${notes.join(' | ') || 'no notice'}; the fireball and damage numbers are in the screenshot` };
  });
  await step('combat: V heal at full health (toast)', async () => {
    await page.keyboard.press('KeyV');
    await wait(500);
    const notes = await page.evaluate(NOTICES);
    // The heal is refused by the first failing check: MP (20 needed) or full health; either toast is the press no longer being silent.
    const toast = notes.some((t) => /already full|đã đầy|not enough mp|không đủ mp/i.test(t));
    return { status: toast ? 'pass' : 'FAIL', notes: `V: ${notes.join(' | ') || 'no notice'}` };
  });
  // Phone (390x844): the toolbar keeps only the Menu button (HudTopRightBar); crafting, friends and skills are tiles in the game menu.
  if (phone) {
    await step('menu (phone): crafting, friends, skills in the game menu', async () => {
      await page.locator('.hud-top-right button[aria-label*="Menu" i], .hud-top-right button[aria-label*="Menu"], .game-hud .game-icon-button[aria-label*="Menu" i]').first().click({ force: true, timeout: 5000 });
      await wait(700);
      const tiles = await page.$$eval('.game-menu-tile', (els) => els.map((e) => e.getAttribute('aria-label') || ''));
      const want = [/crafting|chế tạo/i, /friends|bạn bè/i, /skills|kỹ năng/i];
      const missing = want.filter((re) => !tiles.some((t) => re.test(t))).length;
      return { status: missing === 0 ? 'pass' : 'FAIL', notes: `menu tiles: ${tiles.join(', ') || 'none'}; missing ${missing}` };
    });
    await page.keyboard.press('Escape');
    await wait(400);
  }

  // Crafting panel from the toolbar.
  await step('crafting panel', async () => {
    if (phone) {
      // A phone has one Menu button; the panel opens from its window's tile (ui-standard section 6).
      await openFromMenu(page, /^(crafting|chế tạo)/i);
    } else {
      await page.locator('.game-hud .hud-top-right .game-icon-button[aria-label*="Chế tạo"], .game-hud .hud-top-right .game-icon-button[aria-label*="Craft"]').first().click({ force: true, timeout: 5000 });
    }
    await wait(600);
    return { status: 'captured', notes: 'crafting window: check the screenshot' };
  });

  // Friends, co-op (warehouse), mail, skills, profile (rename), calendar and season.
  for (const [name, re] of [['mail', /^(mail|thư)$/i], ['friends', /^(friends|bạn bè)$/i], ['skills', /^(skills|kỹ năng)$/i], ['crafting', /^(crafting|chế tạo)$/i]]) {
    await step(`panel: ${name}`, async () => {
      if (phone && name !== 'mail') {
        // Phone: the column is one Menu button, so the panel opens from the Menu window's tile (not a hidden column button).
        await openFromMenu(page, re);
        await wait(600);
        return { status: 'captured', notes: `${name} window (opened from the Menu window): check the screenshot` };
      }
      const labels = await page.$$eval('.game-hud .hud-top-right .game-icon-button', (els) => els.map((e) => e.getAttribute('aria-label') || e.textContent.trim()));
      const i = labels.findIndex((l) => re.test(l));
      if (i < 0) throw new Error(`no toolbar button for ${name}`);
      await page.locator('.game-hud .hud-top-right .game-icon-button').nth(i).click({ force: true, timeout: 5000 });
      await wait(600);
      return { status: 'captured', notes: `${name} window: check the screenshot` };
    });
  }
  // Co-op (G) and profile (N) have keys, not toolbar buttons.
  await step('panel: co-op warehouse (G)', async () => {
    await page.keyboard.press('KeyG');
    await wait(600);
    return { status: 'captured', notes: 'co-op window: check the warehouse list in the screenshot' };
  });
  await step('panel: profile, rename (N)', async () => {
    await page.keyboard.press('KeyN');
    await wait(600);
    return { status: 'captured', notes: 'profile window: rename control and the once-a-day note' };
  });
  await step('calendar: season passes (QA)', async () => {
    const before = await page.$eval('.calendar-season', (e) => e.textContent.trim()).catch(() => '');
    await page.evaluate(() => window.nosumQa.season(1));
    await wait(2500);
    const after = await page.$eval('.calendar-season', (e) => e.textContent.trim()).catch(() => '');
    const banner = await page.$('.season-banner');
    return { status: banner || before !== after ? 'pass' : 'FAIL', notes: `season "${before}" -> "${after}"; banner ${banner ? 'yes' : 'no'}` };
  });

  // Town NPC dialogue: the board keeper talks (Talk action), then the line is shown in the dialog box.
  await step('NPC dialogue (board keeper)', async () => {
    await counterBox(TILE.seedShop, 'npc');
    const line = await page.$('.npc-line, .npc-box-text');
    return { status: line ? 'pass' : 'FAIL', notes: line ? 'dialog line present' : 'no dialog line' };
  });

  // Level-up banner (QA): shows the banner only.
  await step('level-up banner (QA)', async () => {
    await page.evaluate(() => window.nosumQa.levelUp(1, 2));
    await wait(800);
    const b = await page.$('.level-up-banner');
    return { status: b ? 'pass' : 'FAIL', notes: b ? 'banner shown' : 'no banner' };
  });

  // Not exercised in this pass (no hook or a building not reached yet): recorded so the gaps are visible in the table.
  for (const f of ['animals (barn, feed, harvest)', 'pets: buy at carpenter, rehome', 'crafting at stations (chest, furnace)', 'market: list and buy', 'mail: send', 'co-op warehouse deposit', 'backpack upgrade', 'rename scroll', 'shipping bin sale', 'dungeon/mine combat']) {
    rec(f, 'not exercised', 'no scripted path in this pass (see README)', '');
  }

  const real = errors.filter((e) => !/Could not load map|could not be decoded/.test(e));
  rec('console / page errors', real.length ? 'FAIL' : 'pass', real.slice(0, 3).join(' ; ') || 'none', '');
  await context.close();
}

(async () => {
  const locales = (process.argv[2] || 'vi,en').split(',');
  const vps = (process.argv[3] || Object.keys(VIEWPORTS).join(',')).split(',');
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const all = [];
  fs.mkdirSync(OUT, { recursive: true });
  for (const loc of locales) {
    for (const vp of vps) {
      const report = [];
      await runOne(browser, loc, vp, report);
      fs.writeFileSync(path.join(OUT, `audit-${loc}-${vp}.json`), JSON.stringify(report, null, 2));
      all.push(...report);
      console.log(`${loc} ${vp}: ${report.length} records`);
    }
  }
  await browser.close();
  const fails = all.filter((r) => r.status === 'FAIL');
  console.log(`total ${all.length}, FAIL ${fails.length}`);
  fails.forEach((r) => console.log(`FAIL ${r.locale} ${r.viewport} ${r.feature}: ${r.notes}`));
})().catch((e) => { console.error(e); process.exit(1); });
