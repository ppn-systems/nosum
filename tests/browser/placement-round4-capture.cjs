// Placement round 4 capture (docs/design/critique-placement.md, findings 16 and 17: the feet rule with the ghost stepping aside, and the craft confirmation).
// Round 3's flow (the chests, furnaces and sprinklers, and the carpenter's counter) at the two sizes that matter, 1280x720 and 390x844.
// One run per viewport and language. A guest signs in and plays:
//   1. the coop blueprint, bought at the carpenter's counter (Build > Buildings > Buy blueprint), with the coins and materials granted through the
//      Development QA hook (window.nosumQa.grant; a client built with -p:NosumDevTools=true and opened with ?qa=1, wasm/README.md);
//   2. on the farm: a chest, crafted at the crafting page (50 wood) and placed, then opened with E;
//   3. a furnace and a sprinkler, whose recipes need Mining 1 and Farming 2 (the recipe survey is in notes.json): the finished piece is granted
//      through the QA hook, and its placement is played;
//   4. the coop: the blueprint held, the first E asks, the second E on the same tile builds.
// Frames go to <PLACEMENT_QA_ROOT or docs/qa/placement-round4>/<phase>/<vp>-<lang>/; notes.json holds the strip text at each step and the
// recipe survey. errors.txt lists console and page errors.
//
// Needs the server (DOTNET_ENVIRONMENT=Development dotnet run --project server/Nosum.Host) and the client
// (dotnet run --project wasm/Nosum.Wasm -p:NosumDevTools=true --urls http://localhost:5080).
// usage: node tests/browser/placement-round4-capture.cjs <before|after> [desktop|wide|phone] [vi|en]   (no viewport/lang = all)

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const VIEWPORTS = { desktop: { width: 1280, height: 720 }, phone: { width: 390, height: 844 } };
const ITEM = { wood: 500, stone: 501, chest: 600, sprinkler: 604, furnace: 605, coopBlueprint: 610 };
// The labels and item names the flow looks for, per language (Language.resx, Catalogue.resx).
const LABEL = {
  vi: { standing: /đứng/, guest: /khách/i, menu: /^Menu$/, build: /Xây dựng/, order: /Mua bản vẽ/, confirm: /Xác nhận/, crafting: /Chế tạo/, craft: /^Chế tạo$/,
    blueprint: /bản vẽ/i, chest: /^Rương$/, furnace: /Lò nung/, sprinkler: /Vòi tưới/ },
  en: { standing: /standing/, guest: /guest/i, menu: /^Menu$/, build: /^Build$/, order: /Buy blueprint/, confirm: /Confirm/, crafting: /Crafting/, craft: /^Craft$/,
    blueprint: /blueprint/i, chest: /^Chest$/, furnace: /^Furnace$/, sprinkler: /^Sprinkler$/ },
};
const COUNTER = [10, 86];      // the carpenter's counter tile in Town (GameMaps, s_townCarpenterTile)
const FARM_SPOT = [64, 22];    // a farm tile in the open (the carpenter-build flow of round 2)

async function hold(page, key, ms) {
  await page.keyboard.down(key);
  await page.waitForTimeout(ms);
  await page.keyboard.up(key);
  await page.waitForTimeout(150);
}

async function run(browser, phase, vp, lang) {
  const size = VIEWPORTS[vp];
  const phone = vp === 'phone';
  const root = path.join(REPO, process.env.PLACEMENT_QA_ROOT || 'docs/qa/placement-round4');
  const dir = path.join(root, phase, `${vp}-${lang}`);
  fs.mkdirSync(dir, { recursive: true });
  const context = await browser.newContext({ viewport: size, locale: lang === 'en' ? 'en-US' : 'vi-VN', hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  await context.addInitScript((code) => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + code + '\n'); } catch (e) { /* no storage */ } }, lang === 'en' ? 2 : 1);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 160)}`); });
  const L = LABEL[lang];
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: L.guest }).first().click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await page.waitForTimeout(3000);

  const notes = {};
  const shot = async (name) => { await page.waitForTimeout(700); await page.screenshot({ path: path.join(dir, `${name}.png`) }); };
  const strip = () => page.evaluate(() => document.querySelector('.game-notice')?.textContent?.trim() ?? null);
  // One server transfer per grant: sent one at a time, a short wait apart, so none is refused by a transfer still in flight.
  const grant = async (id, quantity, coins) => { await page.evaluate(([i, q, c]) => window.nosumQa.grant(i, q, c), [id, quantity, coins]); await page.waitForTimeout(700); };
  const teleport = async (map, [x, y]) => { await page.evaluate(([m, a, b]) => window.nosumQa.teleport(m, a, b), [map, x, y]); await page.waitForTimeout(2500); };
  // The key of the held hotbar slot (1 to 9) whose item name matches the pattern.
  const selectSlot = async (pattern) => {
    const index = await page.evaluate((src) => {
      const re = new RegExp(src, 'i');
      return [...document.querySelectorAll('.hotbar-slots button')].findIndex((b) => re.test(b.getAttribute('aria-label') || ''));
    }, pattern.source);
    if (index < 0) { throw new Error('no hotbar slot for ' + pattern); }
    await page.keyboard.press(String(index + 1));
    await page.waitForTimeout(400);
  };
  // A bag item into hotbar slot 9. The inventory window is open; its bottom row is the hotbar (the first nine slots), slot 9 its right-hand slot.
  const moveToHotbar = async (pattern) => {
    await page.keyboard.press('KeyI');
    await page.waitForTimeout(700);
    const bagIndex = await page.evaluate((src) => [...document.querySelectorAll('button.slot')].filter((b) => !b.closest('.hotbar-slots'))
      .findIndex((b) => new RegExp(src, 'i').test(b.getAttribute('aria-label') || '')), pattern.source);
    if (bagIndex < 0) { throw new Error('not in the bag: ' + pattern); }
    await page.evaluate((i) => [...document.querySelectorAll('button.slot')].filter((b) => !b.closest('.hotbar-slots'))[i].click(), bagIndex);
    await page.waitForTimeout(400);
    await page.evaluate(() => {
      const bag = [...document.querySelectorAll('button.slot')].filter((b) => !b.closest('.hotbar-slots'));
      const corner = (b) => { const r = b.getBoundingClientRect(); return r.top + r.left; };
      bag.sort((a, b) => corner(b) - corner(a));
      bag[0].click();
    });
    await page.waitForTimeout(500);
    await page.keyboard.press('KeyI');
    await page.waitForTimeout(600);
  };
  const closeWindow = async () => {
    await page.locator('.trade-window .game-close').first().click({ timeout: 3000 }).catch(() => {});
    await page.waitForTimeout(600);
  };

  // 1. The purchase. Coins and the coop's materials (4,000 coins, 300 wood, 100 stone) are granted; the blueprint itself is bought.
  // A stack holds 99 (ItemCatalog.DefaultStack), so wood comes in four stacks (the chest takes 50 more).
  await grant(0, 0, 4000);
  for (let n = 0; n < 4; n++) { await grant(ITEM.wood, 99, 0); }
  await grant(ITEM.stone, 99, 0);
  await grant(ITEM.stone, 26, 0);
  await grant(504, 20, 0);   // copper ore, for the furnace recipe (not crafted this pass)
  await page.waitForTimeout(1000);
  await teleport('town', COUNTER);
  await page.keyboard.press('KeyE');
  await page.waitForSelector('.npc-box', { timeout: 15000 });
  await page.click('.npc-line');
  await page.waitForTimeout(400);
  await shot('10-carpenter-talk');
  await page.getByRole('button', { name: L.build }).first().click();
  await page.waitForTimeout(900);
  await shot('11-carpenter-build-page');
  // The coop is the first building of the list (the barn follows it), so its blueprint button is the first one.
  await page.getByRole('button', { name: L.order }).first().click();
  await page.waitForTimeout(600);
  if (await page.getByRole('button', { name: L.confirm }).count()) {
    await shot('12-order-confirm');
    await page.getByRole('button', { name: L.confirm }).first().click();
    await page.waitForTimeout(800);
  }
  await shot('13-blueprint-bought');
  notes.bought = await strip();
  await closeWindow();
  await page.keyboard.press('Escape');
  await page.waitForTimeout(600);

  // 2. The coop first, on open ground at the farm spot (the largest piece goes down before the small ones): the blueprint held, the first E only
  // asks, the second E on the same tile builds.
  await teleport('farm', FARM_SPOT);
  await moveToHotbar(L.blueprint);
  await selectSlot(L.blueprint);
  await hold(page, 's', 200);
  await shot('40-blueprint-held');
  notes.blueprintGhost = await strip();
  await page.keyboard.press('e');
  await shot('41-first-press-confirm');
  notes.confirm = await strip();
  await page.keyboard.press('e');
  await page.waitForTimeout(1200);
  await shot('42-second-press-built');
  notes.afterBuild = await strip();

  // 3. The farm, away from the coop. A chest: the crafting page (every recipe is read first, for the notes), 50 wood, then the chest into the hotbar.
  await hold(page, 'd', 900);
  // A phone has no column (ui-standard section 6): the crafting page is one tile of the Menu window there.
  if (!(await page.getByRole('button', { name: L.crafting }).first().isVisible().catch(() => false))) {
    await page.getByRole('button', { name: L.menu }).first().click();
    await page.waitForTimeout(600);
  }
  await page.getByRole('button', { name: L.crafting }).first().click();
  await page.waitForTimeout(900);
  await shot('20-crafting-page');
  const recipeCount = await page.locator('.cell button').count();
  notes.recipes = [];
  for (let i = 0; i < recipeCount; i++) {
    const name = await page.locator('.cell button').nth(i).getAttribute('aria-label');
    await page.locator('.cell button').nth(i).click();
    await page.waitForTimeout(120);
    notes.recipes.push(await page.evaluate((n) => ({
      name: n,
      makes: document.querySelector('.recipe-line')?.textContent?.trim() ?? null,
      locked: document.querySelector('.recipe-locked')?.textContent?.trim() ?? null,
    }), name));
  }
  const pickRecipe = async (pattern) => {
    const idx = await page.evaluate((src) => [...document.querySelectorAll('.cell button')].findIndex((b) => new RegExp(src, 'i').test(b.getAttribute('aria-label') || '')), pattern.source);
    if (idx < 0) { throw new Error('no recipe ' + pattern); }
    await page.locator('.cell button').nth(idx).click();
    await page.waitForTimeout(250);
  };
  await pickRecipe(L.chest);
  await shot('21-chest-recipe');
  await page.getByRole('button', { name: L.craft }).first().click();
  await page.waitForTimeout(900);
  notes.chestCrafted = await strip();
  await shot('22-chest-crafted');
  await closeWindow();
  await moveToHotbar(L.chest);
  await selectSlot(L.chest);
  await hold(page, 's', 200);
  await shot('23-chest-held');
  notes.chestGhost = await strip();
  await page.keyboard.press('e');
  await page.waitForTimeout(900);
  await shot('24-chest-placed');
  notes.chestPlaced = await strip();
  // The farmer still faces the chest just placed: E opens it.
  await page.keyboard.press('e');
  await page.waitForTimeout(900);
  await shot('25-chest-open');
  notes.chestOpen = await strip();
  await closeWindow();

  // 4. A furnace and a sprinkler: granted (their recipes are locked to a skill, see notes.recipes), then placed.
  for (const [key, pattern, n] of [['furnace', L.furnace, 3], ['sprinkler', L.sprinkler, 4]]) {
    await grant(ITEM[key], 1, 0);
    await moveToHotbar(pattern);
    await selectSlot(pattern);
    // The furnace is walked away from to the right; the sprinkler goes down, clear of the furnace.
    await hold(page, key === 'furnace' ? 'd' : 's', 600);
    await shot(`3${n}0-${key}-held`);
    notes[`${key}Ghost`] = await strip();
    // Finding 16: the ghost is shown where the piece can go; the press places it there without a step back.
    notes[`${key}GhostFeetClear`] = await page.evaluate(() => document.querySelector('.game-notice')?.textContent ?? null);
    await page.keyboard.press('e');
    await page.waitForTimeout(900);
    await shot(`3${n}1-${key}-placed`);
    notes[`${key}Placed`] = await strip();
  }

  fs.writeFileSync(path.join(dir, 'notes.json'), JSON.stringify({ viewport: vp, lang, ...notes }, null, 2) + '\n');
  fs.writeFileSync(path.join(dir, 'errors.txt'), errors.slice(0, 10).join('\n') + '\n');
  await context.close();
}

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: placement-round4-capture.cjs <before|after> [desktop|wide|phone] [vi|en]');
  const vps = process.argv[3] ? [process.argv[3]] : Object.keys(VIEWPORTS);
  const langs = process.argv[4] ? [process.argv[4]] : ['vi', 'en'];
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const vp of vps) for (const lang of langs) await run(browser, phase, vp, lang);
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
