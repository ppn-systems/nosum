// World QA pass 8: drives every feature the pass-7 harness only opened (docs/qa/world-pass8/README.md). Each flow performs its action, verifies it
// (a DOM text or the hotbar count where one is readable, else the screenshot is the evidence), and saves a frame per step.
// Development only: the grants, teleports and clock jumps are the Development tester calls (ADR-079, window.nosumQa), so the server is run with
// DOTNET_ENVIRONMENT=Development and the client is built with -p:NosumDevTools=true.
//
// usage: node tests/browser/world-pass8.cjs <flow> [locales=vi,en] [viewports=1280x720,390x844]
//   flow = farm | town | mine | social | account
// Env: NOSUM_QA_URL (default http://localhost:5080/?qa=1), NOSUM_QA_OUT (a directory relative to the repo, default docs/qa/world-pass8).
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const OUT_ROOT = path.resolve(REPO, process.env.NOSUM_QA_OUT || 'docs/qa/world-pass8');
const VIEWPORTS = { '1280x720': { width: 1280, height: 720 }, '1920x1080': { width: 1920, height: 1080 }, '390x844': { width: 390, height: 844 } };
const LANG = { vi: { tag: 'vi-VN', code: 1, files: ['Language.resx'] }, en: { tag: 'en-US', code: 2, files: ['Language.en.resx'] } };
const wait = ms => new Promise(r => setTimeout(r, ms));
const RES_DIR = path.join(REPO, 'wasm/Nosum.Wasm.ViewModels/Resources');

// Resource strings per locale, keyed by name (the same .resx the game ships; Vietnamese is the default file).
const STRINGS = {};
for (const lk of Object.keys(LANG)) {
  const map = {};
  for (const f of ['Language.resx', 'Catalogue.resx', 'Language.en.resx', 'Catalogue.en.resx']) {
    const isEn = f.includes('.en.');
    if ((lk === 'en') !== isEn) continue;
    const p = path.join(RES_DIR, f);
    if (!fs.existsSync(p)) continue;
    const t = fs.readFileSync(p, 'utf8');
    for (const m of t.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)) {
      map[m[1]] = m[2].trim().replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'");
    }
  }
  STRINGS[lk] = map;
}
// A resource with its placeholders filled in (the exact text the game shows), as a button-name regex.
const fmt = (lk, key, ...args) => STRINGS[lk][key].replace(/\{(\d+)\}/g, (_, i) => args[Number(i)]);
const esc = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
// The exact button name of a resource key, with its {n} placeholders as wildcards. `prefix` anchors only the start.
const nameRe = (lk, key, prefix = false) => {
  const s = STRINGS[lk][key];
  if (s === undefined) throw new Error('missing resource ' + key);
  const body = s.split(/\{\d+\}/).map(esc).join('.*');
  return new RegExp(prefix ? '^' + body : '^' + body + '$');
};

const report = { flow: null, locale: null, viewport: null, steps: [], errors: [] };
let frameNo = 0;
let dir = '';
let page = null;
const L = () => report.locale;

async function shot(name) {
  frameNo += 1;
  const f = `${String(frameNo).padStart(2, '0')}-${name.replace(/[^a-z0-9]+/gi, '-').slice(0, 44)}.png`;
  await page.screenshot({ path: path.join(dir, f) });
  return f;
}
// One checked step: runs fn, saves a frame (named after the step), records ok/fail and the value fn returns.
let players = null; // { A, B } while a two-player flow runs
async function step(name, fn, { frame = true, who = null } = {}) {
  const rec = { name, ok: false };
  const keep = page;
  if (who && players) { page = players[who]; await page.bringToFront().catch(() => {}); }
  try {
    const value = await fn();
    rec.ok = value !== false;
    if (value !== undefined && value !== true) rec.value = value;
  } catch (e) {
    rec.error = String(e.message || e).split('\n')[0].slice(0, 220);
    rec.buttons = await page.evaluate(() => [...document.querySelectorAll('button')].map(b => (b.getAttribute('aria-label') || b.innerText || '').trim()).filter(Boolean).slice(0, 60)).catch(() => []);
  }
  if (frame) rec.file = await shot(name).catch(() => null);
  page = keep;
  report.steps.push(rec);
  console.log(rec.ok ? 'ok  ' : 'FAIL', name, rec.value !== undefined ? JSON.stringify(rec.value) : '', rec.error || '');
  return rec;
}
const qa = (fn, args = []) => page.evaluate(([f, a]) => window.nosumQa[f](...a), [fn, args]);
const btn = (lk, key, prefix = false) => page.getByRole('button', { name: nameRe(lk, key, prefix) }).first();
const clickKey = async (lk, key, { prefix = false, timeout = 6000 } = {}) => btn(lk, key, prefix).click({ timeout });
const visibleText = re => page.getByText(re).first().isVisible().catch(() => false);
const pressKey = async key => { await page.keyboard.press(key); await wait(500); };
const hold = async (key, ms) => { await page.keyboard.down(key); await wait(ms); await page.keyboard.up(key); await wait(250); };
// The hotbar count of slot 9 (the hay or the blueprint stack), -1 when it cannot be read.
const slot9 = () => page.evaluate(() => {
  const slot = [...document.querySelectorAll('[role=toolbar] button')].find(b => /^9\d*$/.test(b.textContent.trim()));
  return slot ? Number(slot.textContent.trim().slice(1) || 0) : -1;
});
const coins = () => page.evaluate(() => Number((document.querySelector('.coin-amount')?.textContent || '').replace(/[^0-9]/g, '')));
// Closes the open window with its Close control (or Escape); the panel closes before the next step.
async function closeWindow(lk) {
  // The first visible close control of the open window (a named Close button, or the window's X), else Escape.
  const header = page.locator('.game-panel-header button:visible, .game-panel-header .game-close:visible').first();
  const named = page.getByRole('button', { name: nameRe(lk, 'Panel_Close') }).filter({ visible: true }).first();
  const x = page.locator('.game-close:visible').first();
  if (await header.count().catch(() => 0)) await header.click({ force: true, timeout: 2000 }).catch(() => {});
  else if (await named.count().catch(() => 0)) await named.click({ force: true, timeout: 2000 }).catch(() => {});
  else if (await x.count().catch(() => 0)) await x.click({ force: true, timeout: 2000 }).catch(() => {});
  else await page.keyboard.press('Escape');
  await wait(500);
  if (await page.locator('.game-close:visible').count().catch(() => 0)) await page.keyboard.press('Escape');
  await wait(300);
}
// Teleport and wait for the map to load.
async function goto(map, x, y, ms = 2500) {
  await qa('teleport', [map, x, y]);
  await wait(ms);
}

// ---------------------------------------------------------------- flows
// Guest sign-in on the Development server, spring reset, and the farm.
async function signInGuest(lk) {
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 });
  await wait(1000);
  await page.getByRole('button', { name: /khách|guest/i }).first().click({ timeout: 90000 });
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await wait(2500);
  await qa('springReset');
  await wait(1500);
}

// ---- farm: animals (coop, buy, feed at the trough, collect produce), pets (buy, pet, forage)
async function farmFlow(lk) {
  await signInGuest(lk);
  await goto('farm', 46, 18);
  await step('farm-start', () => true);

  // Coins and the coop blueprint (hotbar slot 9); set it down on the farm and build it (two presses, two game days).
  await step('grant-coop', async () => { await qa('grant', [0, 0, 20000]); await qa('grant', [610, 1, 0]); await wait(1200); return true; }, { frame: false });
  await goto('farm', 40, 40, 2500);
  await pressKey('Digit9'); await pressKey('KeyE'); await pressKey('KeyE');
  await qa('advanceClock', [2]); await wait(2500);
  await step('coop-built', async () => true);

  // The carpenter: hay for the bench, then the Animals tab (two hens), then the Pets tab (a cat).
  await step('grant-hay', async () => { await qa('grant', [508, 30, 0]); await wait(1500); return true; }, { frame: false });
  await goto('town', 10, 86, 2500);
  const coinsBefore = await coins();
  await pressKey('KeyE'); await wait(1500);
  await step('carpenter-open', async () => { await clickKey(lk, 'Npc_Action_Build'); await wait(800); await clickKey(lk, 'Carpenter_Tab_Animals'); return true; });
  await step('buy-hen', async () => {
    for (const name of ['Hen', 'Dot']) {
      await page.locator('input').first().fill(name); await wait(300);
      await page.getByRole('button', { name: new RegExp('^' + esc(STRINGS[lk].Carpenter_BuyAnimal.split('{1}')[0].replace('{0}', STRINGS[lk].Animal_chicken_Name))) }).first().click({ timeout: 6000 }); await wait(2000);
    }
    return true;
  });
  await step('buy-cat', async () => {
    await clickKey(lk, 'Carpenter_Pets'); await wait(800);
    await clickKey(lk, 'Pet_Choose'); await wait(800);
    await page.locator('input').first().fill('Mochi'); await wait(300);
    await clickKey(lk, 'Pet_NameNext'); await wait(800);
    await clickKey(lk, 'Pet_BuyConfirm'); await wait(2000);
    return true;
  });
  await step('carpenter-after-buys', async () => {
    const after = await coins();
    if (!(coinsBefore > after)) throw new Error(`coins did not drop: ${coinsBefore} -> ${after}`);
    return { coins: [coinsBefore, after] };
  });
  await closeWindow(lk);

  // Into the coop through its door, face the trough with hay in hand, press the action key until the hay count drops.
  await goto('farm', 39, 44, 2500);
  await hold('ArrowUp', 900);
  await pressKey('Digit9');
  await hold('KeyD', 900); await hold('KeyW', 1200);
  const before = await slot9();
  await step('feed-trough', async () => {
    for (const [k, ms] of [[null, 0], ['KeyD', 400], ['KeyW', 500], ['KeyA', 400], ['KeyW', 300]]) {
      if (k) await hold(k, ms);
      await pressKey('KeyE'); await wait(1200);
      const after = await slot9();
      if (after >= 0 && before >= 0 && after < before) return { hay: [before, after] };
    }
    return false;
  });

  // The day rolls over; a hen that laid is collected by the action key (ADR-092 D1: the cue shows, then the press takes the produce).
  await step('day-rolls', async () => { await qa('advanceClock', [1]); await wait(2500); return true; });
  const youGot = new RegExp(esc(STRINGS[lk].Building_Done_Collect.split('{0}')[0]));
  await step('collect-produce', async () => {
    await goto('farm', 39, 44, 2000);
    await hold('ArrowUp', 900);
    for (const [k, ms] of [[null, 0], ['KeyD', 400], ['KeyW', 500], ['KeyA', 400], ['KeyS', 300], ['KeyD', 300]]) {
      if (k) await hold(k, ms);
      await pressKey('KeyE'); await wait(1200);
      if (await visibleText(youGot)) return { got: await page.getByText(youGot).first().innerText().catch(() => '') };
    }
    return false;
  });

  // The pet: the Menu, its Pet tile, and the Pet button (the first petting of the day may bring back forage, ADR-092 D2).
  await step('pet-open', async () => {
    await page.getByRole('button', { name: /^Menu$/ }).first().click({ timeout: 8000 }); await wait(900);
    await page.locator(`.game-menu-tile[aria-label="${STRINGS[lk].Toolbar_Pet}"]`).first().click({ timeout: 8000 }); await wait(900);
    return true;
  });
  // The action key also pets an animal in reach: the press during the collect may have petted the cat already, so the reply is read, and the pet
  // is petted again after the next game day (the first petting of a day can bring back forage, ADR-092 D2).
  const petReply = async () => (await page.evaluate(() => document.body.innerText || '').catch(() => '')).replace(/\s+/g, ' ');
  await step('pet-pet-today', async () => {
    await clickKey(lk, 'Pet_Pet'); await wait(1500);
    const text = await petReply();
    if (!/Done\.|petted today/i.test(text) && !new RegExp(esc(STRINGS[lk].Pet_PettedToday)).test(text) && !new RegExp(esc(STRINGS[lk].Pet_Status_Ok)).test(text)) throw new Error('no petting reply: ' + text.slice(0, 120));
    return { reply: text.slice(-80) };
  });
  await closeWindow(lk);
  await step('day-rolls-again', async () => { await qa('advanceClock', [1]); await wait(2000); return true; });
  await step('pet-open-next-day', async () => {
    await page.getByRole('button', { name: /^Menu$/ }).first().click({ timeout: 8000 }); await wait(900);
    await page.locator(`.game-menu-tile[aria-label="${STRINGS[lk].Toolbar_Pet}"]`).first().click({ timeout: 8000 }); await wait(900);
    await clickKey(lk, 'Pet_Pet'); await wait(1500);
    const text = (await petReply());
    return { reply: text.slice(-90) };
  });
  await closeWindow(lk);
}


// ---- town: the backpack upgrade (the tailor's counter), the wardrobe (hair and skin), the world map, the guest link and the sign-out question
async function townFlow(lk) {
  await signInGuest(lk);
  await qa('grant', [0, 0, 20000]); await wait(900);
  await goto('town', 20, 89, 2500);
  await step('tailor-talk', async () => { await pressKey('KeyE'); await wait(1200); return true; });
  const bagBefore = await page.evaluate(() => document.querySelectorAll('.inventory-window button.slot, .game-window button.slot').length).catch(() => -1);
  await step('backpack-upgrade', async () => {
    await clickKey(lk, 'Npc_Action_Backpack'); await wait(900);
    await clickKey(lk, 'Backpack_Upgrade', { timeout: 6000 }); await wait(1500);
    const size = await page.getByText(new RegExp(esc(STRINGS[lk].Backpack_Size.split('{0}')[0]))).first().innerText().catch(() => '');
    return { size: size.slice(0, 60), bagBefore };
  });
  await closeWindow(lk);

  await goto('town', 20, 89, 1200);
  await step('wardrobe-open', async () => {
    await pressKey('KeyE'); await wait(1000);
    await clickKey(lk, 'Npc_Action_Wardrobe'); await wait(1200);
    return true;
  });
  // The swatches are named by their number (hair style 3 is Bowl cut); the window is the open panel, not the hotbar.
  const wardrobe = () => page.locator('.game-window, .game-panel-window, .trade-window').getByRole('button', { name: /^3$/ }).first();
  await step('wardrobe-hair', async () => {
    await wardrobe().click({ timeout: 6000 }); await wait(1200);
    const cur = await page.getByText(new RegExp(esc(STRINGS[lk].Wardrobe_HairStyle_Current.split('{0}')[0]))).first().innerText().catch(() => '');
    return { wearing: cur.slice(0, 60) };
  });
  // Clothes are worn from the Inventory panel, not the wardrobe window (its text says so): the hat is picked up from the bag, then the Hat slot of the
  // equipment card is clicked (InventoryPanel / EquipmentCard). The equipment card then lists it as worn.
  await closeWindow(lk);
  await step('wear-hat-from-bag', async () => {
    await qa('grant', [440, 1, 0]); await wait(1000);
    const HAT_NAME = STRINGS[lk].Item_hat_straw_Name, SLOT_NAME = STRINGS[lk].Wardrobe_Slot_Hat;
    await pressKey('KeyI'); await wait(900);
    const hatBag = await page.evaluate((HAT_NAME) => {
      const b = [...document.querySelectorAll('section.game-panel button.slot')].find(x => (x.getAttribute('aria-label') || x.getAttribute('data-tip') || '').startsWith(HAT_NAME));
      if (!b) return null;
      b.click();
      return (b.getAttribute('aria-label') || b.getAttribute('data-tip') || '').slice(0, 40);
    }, HAT_NAME);
    if (!hatBag) throw new Error('no hat in the bag: ' + (await windowText()).slice(0, 80));
    await wait(500);
    const hatSlot = await page.evaluate((SLOT_NAME) => {
      // The equipment card's Hat slot is an item slot whose tip reads the slot's name while it is empty ("Hat"; "Mũ" in Vietnamese).
      const b = [...document.querySelectorAll('section.game-panel button')].find(x => (x.getAttribute('data-tip') || x.getAttribute('aria-label') || '').split('\n')[0] === SLOT_NAME);
      if (!b) return null;
      b.click();
      return (b.getAttribute('aria-label') || b.getAttribute('data-tip') || '').slice(0, 40);
    }, SLOT_NAME);
    await wait(1200);
    // Worn: the Hat slot's tip now names the straw hat (it reads the slot's name while empty).
    const worn = await page.evaluate((n) => [...document.querySelectorAll('section.game-panel button')].some(x => (x.getAttribute('data-tip') || '').startsWith(n)), HAT_NAME);
    return { picked: hatBag, slot: hatSlot, worn };
  });
  await pressKey('KeyI'); await wait(600);
  await step('wardrobe-open-again', async () => { await goto('town', 20, 89, 1200); await pressKey('KeyE'); await wait(1000); await clickKey(lk, 'Npc_Action_Wardrobe'); await wait(1200); return true; }, { frame: false });
  await step('wardrobe-skin', async () => {
    await page.locator('.game-window, .game-panel-window, .trade-window').getByRole('button', { name: nameRe(lk, 'Wardrobe_Skin', true) }).first().click({ timeout: 6000 }); await wait(1000);
    return true;
  });
  await closeWindow(lk);

  await step('world-map', async () => {
    await pressKey('Tab'); await wait(1500);
    const here = await page.getByText(new RegExp(esc(STRINGS[lk].WorldMap_Here))).first().isVisible().catch(() => false);
    return here || false;
  });
  await closeWindow(lk);
}


// ---- probe helpers (dev only): the text of the open window, for finding a control's real name
const windowText = () => page.evaluate(() => (document.querySelector('.game-window, .game-panel-window, .trade-window')?.innerText || '').replace(/\s+/g, ' ').slice(0, 220));


// ---- farm: the shipping bin sale and the furnace (station crafting)
async function binFlow(lk) {
  await signInGuest(lk);
  await qa('grant', [200, 5, 0]); await wait(900);
  // The bin: the parsnips in hotbar slot 9, held, and the action key at the bin (ShippingBinRules.InReach).
  // The sale fires when the farmer faces the bin tile (PlayerActions: a crop in hand, the target tile is the bin), so the farmer walks up to it.
  await goto('farm', 71, 16, 2500);
  await hold('KeyW', 350);
  const before = await coins();
  await step('bin-sell', async () => {
    await pressKey('Digit9'); await wait(400);
    // The coin pop lives 200 ms after the bin answers (FarmJuice): a burst of frames straight after the press catches it.
    await page.keyboard.press('KeyE');
    for (let k = 0; k < 4; k++) { await shot('bin-coin-pop-' + k).then(f => report.steps.push({ name: 'bin-coin-pop-' + k, ok: true, file: f })).catch(() => {}); }
    await wait(1800);
    const after = await coins();
    if (!(after > before)) throw new Error(`coins did not rise: ${before} -> ${after}`);
    return { coins: [before, after] };
  }, { frame: false });
  await step('bin-after', async () => true);
  // The parsnip stack is sold, so hotbar slot 9 is free for the furnace.

  // The furnace: a placeable (item 605) on a clear farm tile, set down like the coop; ore and coal bring it to work (station crafting).
  await qa('grant', [605, 1, 0]); await qa('grant', [504, 5, 0]); await qa('grant', [503, 1, 0]); await wait(1200);
  await goto('farm', 50, 22, 2200);
  await pressKey('Digit9');
  await step('furnace-place', async () => {
    await pressKey('Digit9'); await pressKey('KeyE'); await pressKey('KeyE'); await wait(800);
    return true;
  });
  await step('furnace-open', async () => {
    await qa('advanceClock', [1]); await wait(1500);
    await pressKey('KeyE'); await wait(1200);
    const smelt = new RegExp('^' + esc(STRINGS[lk].Furnace_Start.split('{0}')[0]));
    await page.getByRole('button', { name: smelt }).first().click({ timeout: 6000 }); await wait(1500);
    return { window: (await windowText()).slice(0, 140) };
  });
  await closeWindow(lk);
  // The batch takes 30 game minutes (FurnaceRules.GameMinutes). The server times it on its game clock, and the client counts it down on the same
  // clock (ServerClock, fed by the calendar after each dev clock jump), so one game day moves the batch to ready at once. The harness then polls
  // the furnace briefly and collects the bar. Real time would be 15 minutes per batch (docs/qa/fix-qa8/README.md).
  await step('furnace-wait-and-collect', async () => {
    await qa('advanceClock', [1]); await wait(2500);
    const deadline = Date.now() + 60 * 1000;
    const ready = nameRe(lk, 'Furnace_Collect');
    while (Date.now() < deadline) {
      await pressKey('Digit1'); // the hoe in hand, so E opens the furnace and does not set the furnace down again
      await pressKey('KeyE'); await wait(900);
      if (await page.getByRole('button', { name: ready }).first().isVisible().catch(() => false)) {
        await clickKey(lk, 'Furnace_Collect', { timeout: 6000 }); await wait(1500);
        return { collected: true, window: (await windowText()).slice(0, 120) };
      }
      await closeWindow(lk);
      await wait(2000);
    }
    throw new Error('the bar was not ready after a dev clock jump of one game day');
  });
  await closeWindow(lk);
}

// ---- the account: the guest progress link prompt on the profile, and the sign-out question (a guest loses the farm, so it asks first)
async function accountFlow(lk) {
  await signInGuest(lk);
  await step('profile-open', async () => {
    await pressKey('KeyN'); await wait(1200);
    const prompt = await page.getByText(new RegExp(esc(STRINGS[lk].Account_SaveProgress_Heading))).first().isVisible().catch(() => false);
    return { prompt, window: (await windowText()).slice(0, 120) };
  });
  // The guest's link block in Profile: "Save progress with an account" opens the upgrade form (Account_SaveProgress_Action).
  await step('link-prompt', async () => {
    await clickKey(lk, 'Account_SaveProgress_Action', { timeout: 6000 }); await wait(1200);
    return { window: (await windowText()).slice(0, 120) };
  });
  // The upgrade form closes with Escape (its Cancel button is not a panel close); the profile is reopened only when it is not open.
  await page.keyboard.press('Escape'); await wait(700);
  await step('sign-out-question', async () => {
    if (!(await page.getByRole('button', { name: nameRe(lk, 'Account_SignOut') }).first().isVisible().catch(() => false))) {
      await pressKey('KeyN'); await wait(900);
    }
    await clickKey(lk, 'Account_SignOut', { timeout: 6000 }); await wait(1200);
    const title = await page.getByText(nameRe(lk, 'Account_SignOutGuest_Title')).first().isVisible().catch(() => false);
    if (!title) throw new Error('no sign-out question');
    return { question: (await windowText()).slice(0, 160) };
  });
  await clickKey(lk, 'Account_SignOutGuest_Stay', { timeout: 6000 }).catch(() => {});
  await wait(900);
}


// ---- the mines: the entrance elevator, a fight with a monster (teleported beside it, ADR-079 D9), the ladder down from floor 1 to floor 2, and copper ore
// from floor 2. The client never learns which stone hides the ladder (the server sends it as a stone, docs/adr/ADR-026 D3), so the farmer tunnels: it
// walks toward a goal, breaks what stands in its way with the pickaxe in hand, and walks on into the broken tile. A relocation of the player (a teleport
// included) forgets a floor nobody else stands on (MineService), so no teleport is made once the farmer is on a floor.
// The node mirror and the farmer's tile are read through QA hooks: `nodes()` (map id, then x,y,kind per node; 4 stone, 5 copper ore, 10 ladder) and
// `target()` (the tile the action key works, and the Facing that works it; the farmer stands one step back from it).
const KIND = { Stone: 4, CopperOre: 5, Ladder: 10 };
const mirror = async () => {
  const [map, ...entries] = (await qa('nodes')).split(';');
  return { map, nodes: entries.filter(Boolean).map(e => { const [x, y, kind] = e.split(',').map(Number); return { x, y, kind }; }) };
};
// The farmer's tile: the action target minus the step the facing points to (Facing: Down 0, Right 1, Up 2, Left 3).
const STEP = [[0, 1], [1, 0], [0, -1], [-1, 0]];
const farmerTile = async () => {
  const [tx, ty, facing] = (await qa('target')).split(',').map(Number);
  const [dx, dy] = STEP[facing];
  return [tx - dx, ty - dy];
};
// The hotbar key (a digit) of the slot holding an item, found by the item's name (a slot's aria-label); null when the item is not on the hotbar.
const hotkeyOf = (lk, key) => page.evaluate(name => {
  const slot = [...document.querySelectorAll('.hotbar-slots button')].find(b => b.getAttribute('aria-label') === name);
  const digit = slot ? slot.textContent.trim().match(/^\d/) : null;
  return digit ? digit[0] : null;
}, STRINGS[lk][key]);
// The walk keys and their steps.
const KEYS = { KeyD: [1, 0], KeyA: [-1, 0], KeyS: [0, 1], KeyW: [0, -1] };
const OPPOSITE = { KeyD: 'KeyA', KeyA: 'KeyD', KeyS: 'KeyW', KeyW: 'KeyS' };
// The keys to try, best first: the axis with the larger distance to the goal, then the other axis, then the two away from it.
function keysToward(fx, fy, goal) {
  const ddx = goal.x - fx;
  const ddy = goal.y - fy;
  const h = ddx > 0 ? 'KeyD' : 'KeyA';
  const v = ddy > 0 ? 'KeyS' : 'KeyW';
  const order = Math.abs(ddx) >= Math.abs(ddy) ? [h, v] : [v, h];
  return [...order, OPPOSITE[order[0]], OPPOSITE[order[1]]];
}
// Tunnels toward `goal` until until() holds. A node in the way is swung at with the pickaxe in hand, one press a swing, until it is gone (a ladder
// takes the farmer down on the press); a free tile is walked into. A step that does not move the farmer is a wall or a monster: the farmer tries the next
// key, and after three blocked steps in a row it swings the sword at what is in front (hotbar slot 9, granted by the flow). A swing in recovery ignores
// the walk keys, so every swing is followed by a pause. A key that failed is not tried again for eight actions (a monster moves). False when the actions
// run out.
async function tunnel(goal, until, maxActions, pick) {
  const failed = new Map();
  let blocked = 0;
  for (let action = 0; action < maxActions; action++) {
    if (await until()) return true;
    const [fx, fy] = await farmerTile();
    const { nodes } = await mirror();
    const key = keysToward(fx, fy, goal).find(k => !(failed.has(fx + ',' + fy + ',' + k) && action - failed.get(fx + ',' + fy + ',' + k) < 8));
    if (!key) { failed.clear(); continue; }
    const [dx, dy] = KEYS[key];
    const ahead = nodes.find(n => n.x === fx + dx && n.y === fy + dy);
    if (process.env.DEBUG_TUNNEL) console.log('TUNNEL', action, 'at', fx, fy, 'key', key, 'ahead', ahead ? ahead.kind : '-', 'blocked', blocked, 'map', (await mirror()).map);
    if (ahead) {
      await hold(key, 100); // turns the farmer to the node (a blocked key turns without a step)
      await pressKey('KeyE'); await wait(600);
      blocked = 0;
      continue;
    }
    await hold(key, 160);
    const [nx, ny] = await farmerTile();
    if (nx !== fx || ny !== fy) { blocked = 0; continue; }
    failed.set(fx + ',' + fy + ',' + key, action);
    blocked += 1;
    if (blocked >= 3) {
      blocked = 0;
      await hold(key, 100);
      await pressKey('Digit9'); await pressKey('KeyE'); await wait(900);
      await pressKey('Digit' + pick); await wait(300);
    }
  }
  return false;
}
// The four sides of a node: the offset of the standing tile and the key that turns the farmer toward the node from it.
const SIDES = [[0, 1, 'KeyW'], [0, -1, 'KeyS'], [1, 0, 'KeyA'], [-1, 0, 'KeyD']];
// The tile the action key works now (the QA hook `target()`): [tileX, tileY].
const aimed_tile = async () => (await qa('target')).split(',').slice(0, 2).map(Number);
// True when the node (its tile and kind) is no longer on the list: broken, or changed into something else (a stone that breaks into a ladder).
const nodeGone = (nodes, node) => !nodes.some(n => n.x === node.x && n.y === node.y && n.kind === node.kind);
// Waits up to about 3 s for the client to be on `map` (a floor change arrives as a join; the node list reloads after it).
async function onMap(map) {
  for (let i = 0; i < 12; i++) { if ((await mirror()).map === map) return true; await wait(250); }
  return false;
}
// The bag count of an item by name: the inventory window is opened and closed around the read. Every slot of the bag and the hotbar that holds the
// item counts; a slot's count is its quantity span (one when the slot has no span).
async function bagCount(lk, key) {
  await pressKey('KeyI'); await wait(700);
  const count = await page.evaluate(name => [...document.querySelectorAll('button.slot[aria-label]')]
    .filter(b => b.getAttribute('aria-label') === name)
    .reduce((sum, b) => { const q = b.querySelector('.quantity'); return sum + (q ? Number(q.textContent.replace(/[^0-9]/g, '')) : 1); }, 0), STRINGS[lk][key]);
  await closeWindow(lk);
  return count;
}

async function mineFlow(lk) {
  await signInGuest(lk);
  await qa('grant', [0, 0, 5000]);
  await qa('grant', [30, 1, 0]); // a wooden sword: hotbar slot 9 when it is free
  await wait(1000);
  await goto('mine', 17, 4, 2500);
  await step('elevator-open', async () => {
    await hold('KeyW', 120);
    await pressKey('KeyE'); await wait(1200);
    const title = await page.getByText(nameRe(lk, 'Elevator_Title')).first().isVisible().catch(() => false);
    if (!title) throw new Error('the elevator panel did not open: ' + (await windowText()).slice(0, 100));
    return { floors: (await windowText()).slice(0, 160) };
  });
  await closeWindow(lk);

  // The first arrival on floor 1 (the shaft from the entrance); nothing is broken yet, so the teleport loses nothing.
  await goto('mine1', 8, 8, 2500);

  // Descend: the planner hides the ladder under a stone among the farthest quarter of the floor's stones from the arrival tile (docs/adr/ADR-026 D3).
  // The farmer tunnels to those candidates, nearest first, and breaks each one; a ladder that shows is used from the tile it stands on.
  await step('descend-floors', async () => {
    const pick = await hotkeyOf(lk, 'Item_pickaxe_Name');
    if (!pick) throw new Error('no pickaxe on the hotbar');
    if (!(await onMap('mine1'))) throw new Error('not on floor 1: ' + (await mirror()).map);
    await pressKey('Digit' + pick);
    const [sx, sy] = await farmerTile();
    const tried = new Set();
    let candidates = 0;
    for (let round = 0; round < 24; round++) {
      if ((await mirror()).map === 'mine2') break;
      const [fx, fy] = await farmerTile();
      const stones = (await mirror()).nodes.filter(n => n.kind === KIND.Stone);
      stones.sort((a, b) => Math.hypot(b.x - sx, b.y - sy) - Math.hypot(a.x - sx, a.y - sy));
      const far = stones.slice(0, Math.max(1, Math.ceil(stones.length / 4))).filter(n => !tried.has(n.x + ',' + n.y));
      if (far.length === 0) {
        // Nothing far is loaded yet (the client mirrors the chunks near the farmer): tunnel toward the next waypoint of a sweep, which loads more.
        const wp = [[44, 8], [44, 36], [4, 36], [4, 8], [44, 22]][round % 5];
        await tunnel({ x: wp[0], y: wp[1] }, async () => (await mirror()).map === 'mine2', 40, pick);
        if ((await mirror()).map === 'mine2') break;
        continue;
      }
      far.sort((a, b) => (Math.abs(a.x - fx) + Math.abs(a.y - fy)) - (Math.abs(b.x - fx) + Math.abs(b.y - fy)));
      const target = far[0];
      tried.add(target.x + ',' + target.y);
      candidates += 1;
      const gone = async () => nodeGone((await mirror()).nodes, target) || (await mirror()).map === 'mine2';
      await tunnel({ x: target.x, y: target.y }, gone, 40, pick);
      const ladder = (await mirror()).nodes.find(n => n.kind === KIND.Ladder);
      if (ladder) {
        // The ladder stands where the stone was: the farmer faces it (the tunnel broke it from the tile in front), so one press takes it down.
        await tunnel({ x: ladder.x, y: ladder.y }, async () => (await mirror()).map === 'mine2', 20, pick);
      }
    }
    if (!(await onMap('mine2'))) throw new Error(`no ladder took the farmer down to floor 2 (${candidates} far stones tried)`);
    return { map: 'mine2', candidates };
  });

  // Ore: a copper ore of floor 2 (the planner places it on floors 1 to 39). Nothing on the floor is broken before it, so a teleport beside the ore
  // loses nothing (a floor is rebuilt from its seed; the ore stays) and spares the walk through the monsters. The farmer stands on a free side of the
  // ore, turns to it, and reads back the tile the action key works: the press is made only when that is the ore's tile.
  await step('ore-mine', async () => {
    if (!(await onMap('mine2'))) throw new Error('not on floor 2: ' + (await mirror()).map);
    const floor = await mirror();
    const pick = await hotkeyOf(lk, 'Item_pickaxe_Name');
    const ore = floor.nodes.find(n => n.kind === KIND.CopperOre);
    if (!ore) throw new Error('no copper ore node on floor 2 in the client mirror');
    const taken = new Set(floor.nodes.map(n => n.x + ',' + n.y));
    const before = await bagCount(lk, 'Item_copper_ore_Name');
    await pressKey('Digit' + pick);
    const gone = async () => nodeGone((await mirror()).nodes, ore);
    let broke = false;
    for (const [dx, dy, turn] of SIDES) {
      if (broke) break;
      if (taken.has((ore.x + dx) + ',' + (ore.y + dy))) continue;
      await goto('mine2', ore.x + dx, ore.y + dy, 900);
      await wait(800);
      let aimed = false;
      for (let attempt = 0; attempt < 3 && !aimed; attempt++) {
        await hold(turn, 150); await wait(250);
        const [tx, ty] = await aimed_tile();
        aimed = tx === ore.x && ty === ore.y;
      }
      if (!aimed) continue;
      for (let i = 0; i < 3; i++) { await pressKey('KeyE'); await wait(700); }
      await wait(500);
      broke = await gone();
    }
    if (!broke) throw new Error(`the copper ore at ${ore.x},${ore.y} did not break from any side`);
    const after = await bagCount(lk, 'Item_copper_ore_Name');
    if (!(after > before)) throw new Error(`no copper ore in the bag: ${before} -> ${after}`);
    return { ore: [ore.x, ore.y], bag: [before, after] };
  });

  // Combat, last (a knock-out carries the farmer home, which would end the descent): a monster of floor 2 is beside the farmer (the server places the
  // farmer; ADR-079 D9). The sword is in hand; the action key swings it.
  await step('fight-monster', async () => {
    if (!(await onMap('mine2'))) throw new Error('not on floor 2: ' + (await mirror()).map);
    await qa('teleportToMonster', ['mine2']); await wait(2000);
    await pressKey('Digit9');
    const hits = [];
    for (let i = 0; i < 6; i++) { await pressKey('KeyE'); await wait(700); }
    return { swings: 6, hits };
  });
  await closeWindow(lk);
}


// ---- two players: B lists a parsnip for 1,200 coins at the trader; A buys it through the confirm of a large purchase (Market.LargePurchase, 1,000);
// B collects the sale mail (Mail_CollectAll). The harness switches `page` between the two players; each step names the player it acts for.
// The trader's counter: the greeting plays first, then the Buy and Sell actions show (Npc_Action_Buy opens the market's Browse tab).
async function openMarket(lk) {
  await hold('KeyD', 150);
  await pressKey('KeyE'); await wait(1200);
  for (let i = 0; i < 5; i++) {
    if (await page.getByRole('button', { name: nameRe(lk, 'Market_Tab_Mine') }).first().isVisible().catch(() => false)) return true;
    const buy = page.getByRole('button', { name: nameRe(lk, 'Npc_Action_Buy') }).first();
    if (await buy.isVisible().catch(() => false)) { await buy.click({ timeout: 4000 }); await wait(1200); continue; }
    await pressKey('KeyE'); await wait(900); // skip the greeting line
  }
  return page.getByRole('button', { name: nameRe(lk, 'Market_Tab_Mine') }).first().isVisible().catch(() => false);
}

// The Buy of the newest listing: the first Buy under the search field (the Browse tab can read "Buy" too in Vietnamese, and the list is Newest first).
async function newestBuy(lk) {
  const buys = page.locator('section.game-panel button').filter({ hasText: nameRe(lk, 'Market_Buy') });
  const n = await buys.count();
  const search = await page.locator('section.game-panel input').first().boundingBox().catch(() => null);
  for (let i = 0; i < n; i++) {
    const box = await buys.nth(i).boundingBox().catch(() => null);
    if (box && search && box.y > search.y + search.height) return buys.nth(i);
  }
  return buys.first();
}

async function marketFlow(lk) {
  await signInGuest(lk);
  const made = await qa('befriend');
  const [nickname, token] = String(made).split('\t');
  if (!nickname || !token) throw new Error('the server made no test friend: ' + String(made).slice(0, 80));
  const vp = VIEWPORTS[report.viewport];
  const touch = vp.width < 1024;
  const a = page;
  const ctxB = await a.context().browser().newContext({ viewport: vp, locale: LANG[lk].tag, hasTouch: touch, isMobile: touch });
  const record = JSON.stringify({ version: 1, token, expiresAt: new Date(Date.now() + 29 * 864e5).toISOString(), nickname, kind: 'guest' });
  await ctxB.addInitScript(([code, rec]) => { try { localStorage.setItem('nosum.settings.v1', `lang=${code}\n`); localStorage.setItem('nosum.remembered-session', rec); } catch (e) {} }, [LANG[lk].code, record]);
  const b = await ctxB.newPage();
  b.on('pageerror', e => report.errors.push(e.message.slice(0, 200)));
  await b.goto(BASE, { waitUntil: 'load' });
  await b.waitForSelector('button', { timeout: 90000 }); await wait(2500);
  await b.getByRole('button', { name: nameRe(lk, 'Account_Continue_As', true) }).first().click({ timeout: 15000 });
  await b.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 }); await wait(2500);
  players = { A: a, B: b };
  // Keyboard input goes to the page in front, so each player's page is brought to the front before its keys are sent.
  const forPlayer = async (who, fn) => { const keep = page; page = players[who]; await page.bringToFront().catch(() => {}); try { return await fn(); } finally { page = keep; await keep.bringToFront().catch(() => {}); } };
  // The trader's counter (45,71): stand on it and press the action key toward the trader. The buyer needs the coins (a 1,200-coin buy is off below that).
  await forPlayer('A', async () => { await qa('grant', [0, 0, 5000]); await wait(700); });
  await forPlayer('B', async () => { await qa('grant', [200, 3, 0]); await wait(900); await goto('town', 45, 71, 2500); });
  // A stands on the counter tile (B is moved off it after the listing, so the two never share the tile).
  await forPlayer('A', async () => { await goto('town', 45, 71, 2500); });

  // A teleport lands on a walkable tile near the one asked for, so the farmer faces the trader (east of the counter) before the action key.
  await step('B-list-open', async () => {
    if (!(await openMarket(lk))) throw new Error('the trader did not open the market for B');
    await clickKey(lk, 'Market_Tab_Sell');
    await wait(800);
    return { window: (await windowText()).slice(0, 140) };
  }, { who: 'B' });
  await step('B-list-for-sale', async () => {
    // The bag slot holding the parsnip, the quantity and the price, then List for sale.
    // The bag grid of the Sell tab: the parsnip stack is the slot that shows its count (3, granted above).
    const slot = page.locator('section.game-panel button').filter({ hasText: /^3$/ }).first();
    await slot.click({ timeout: 6000 }); await wait(500);
    const inputs = page.locator('section.game-panel input');
    await inputs.nth(0).fill('1'); await inputs.nth(1).fill('1200'); await wait(400);
    await clickKey(lk, 'Market_List', { timeout: 6000 }); await wait(1500);
    return { window: (await windowText()).slice(0, 140) };
  }, { who: 'B' });
  await step('B-close-market', async () => { await closeWindow(lk); await goto('town', 43, 73, 1500); return true; }, { frame: false, who: 'B' });

  await step('A-buy-confirm', async () => {
    if (!(await openMarket(lk))) throw new Error('the trader did not open the market for A');
    // The listing's Buy button, inside the market panel (the HUD has no Buy).
    // (The Browse tab has the same word in Vietnamese, so the listing's Buy is the last one: the tabs come first.)
    const buyState = async () => (await page.locator('section.game-panel button').filter({ hasText: nameRe(lk, 'Market_Buy') }).count());
    // The listings arrive from the server after the window opens: wait for the first Buy.
    await page.locator('section.game-panel button').filter({ hasText: nameRe(lk, 'Market_Buy') }).last().waitFor({ state: 'visible', timeout: 8000 }).catch(() => {});
    const before = await buyState();
    // The row of B's own listing (its seller's nickname is in the row): wait until the list shows it, then its Buy.
    await page.waitForFunction(n => [...document.querySelectorAll('section.game-panel')].some(p => p.innerText.includes(n)), nickname, { timeout: 10000 });
    const row = page.locator('section.game-panel').getByText(nickname).first().locator('xpath=ancestor::*[.//button][1]');
    await row.getByRole('button', { name: nameRe(lk, 'Market_Buy') }).first().click({ timeout: 6000 });
    await wait(1200);
    if (before.length === 0) throw new Error('the market did not open for A');
    report.debugBuy = { before, after: await buyState() };
    // The large-purchase question is a ConfirmDialog (role alertdialog) in the HUD overlay.
    const dialog = page.getByRole('alertdialog').first();
    const confirm = await dialog.isVisible().catch(() => false);
    if (!confirm) {
      const status = await page.evaluate(() => [...document.querySelectorAll('[role=status], [role=alert], .toast, .hud-toast, .game-toast, .notice')].map(e => e.innerText).join(' | ').slice(0, 200));
      throw new Error('no confirm for a 1,200-coin purchase; status ' + JSON.stringify(status) + '; ' + (await windowText()).slice(0, 80));
    }
    return { confirm: (await dialog.innerText().catch(() => '')).replace(/\s+/g, ' ').slice(0, 160) };
  }, { who: 'A' });
  await step('A-buy-done', async () => {
    // The dialog's own Confirm (Shop_Confirm_Yes): only Yes buys (the server prices and checks the purchase).
    await page.getByRole('alertdialog').first().getByRole('button', { name: nameRe(lk, 'Shop_Confirm_Yes') }).click({ timeout: 6000 });
    await wait(1500);
    return { bought: await page.getByText(nameRe(lk, 'Market_Done_Buy')).first().isVisible().catch(() => false) };
  }, { who: 'A' });
  await step('A-close-market', async () => { await closeWindow(lk); return true; }, { frame: false, who: 'A' });

  await step('B-mail-collect-all', async () => {
    // The HUD's Mail button: its name reads "Unread mail: N" while mail waits, so it is found by its tip.
    await page.locator(`button[data-tip^="${STRINGS[lk].Toolbar_Mail}"]`).first().click({ timeout: 6000 });
    await wait(1200);
    // The list opens with no mail selected: the entry is selected (its body and its gift show), then the gift is collected if there is one.
    await page.getByRole('button', { name: nameRe(lk, 'Mail_Title_MarketSold', true) }).first().click({ timeout: 6000 }); await wait(900);
    const body = (await windowText()).slice(0, 200);
    const all = page.getByRole('button', { name: nameRe(lk, 'Mail_CollectAll', true) }).first();
    const collect = page.getByRole('button', { name: nameRe(lk, 'Mail_Collect') }).first();
    if (await all.count()) { await all.click({ timeout: 6000 }); await wait(1500); }
    else if (await collect.count()) { await collect.click({ timeout: 6000 }); await wait(1500); }
    return { body, collected: (await windowText()).slice(0, 120) };
  }, { who: 'B' });
  await step('B-close-mail', async () => { await closeWindow(lk); return true; }, { frame: false, who: 'B' });
  players = null;
  await ctxB.close();
}


// ---- co-op: found a co-op at the board keeper, deposit seeds, withdraw some, and read the warehouse log (Coop_Log_*).
// The co-op shows one part at a time on its tab bar at every size (CoopPanel.razor). Each tab step clicks its tab, saves a frame and
// records the page body's scroll overflow (scrollHeight - clientHeight, 0 = nothing is cut, no inner scroll: ui-standard section 7).
// The last part opened is remembered for the page session: the reopen step checks that the Log tab is still the active one.
async function coopFlow(lk) {
  const tab = async key => { await clickKey(lk, key, { timeout: 6000 }); await wait(700); };
  const overflow = () => page.evaluate(() => { const b = document.querySelector('.menu-page-body'); return b ? b.scrollHeight - b.clientHeight : -1; });
  await signInGuest(lk);
  await qa('grant', [100, 6, 0]); await wait(900);
  await goto('town', 42, 59, 2500);
  await step('coop-board-open', async () => {
    await pressKey('KeyE'); await wait(1200);
    await clickKey(lk, 'Npc_Action_Coop', { timeout: 6000 }).catch(() => {});
    await wait(1000);
    return { window: (await windowText()).slice(0, 100) };
  });
  await step('coop-found', async () => {
    await page.locator('section.game-panel input').first().fill('Pass8 Co-op'); await wait(300);
    await clickKey(lk, 'Coop_Create', { timeout: 6000 }); await wait(1500);
    return { window: (await windowText()).slice(0, 120) };
  });
  await step('coop-members', async () => {
    await tab('Coop_Tab_Members');
    return { overflow: await overflow(), clientH: await page.evaluate(() => document.querySelector('.menu-page-body')?.clientHeight ?? -1) };
  });
  await step('coop-upgrades', async () => {
    await tab('Coop_Tab_Upgrades');
    return { overflow: await overflow(), buy: await page.getByRole('button', { name: nameRe(lk, 'Coop_Upgrade_Buy') }).count() };
  });
  await step('coop-objectives', async () => {
    await tab('Coop_Tab_Objectives');
    return { overflow: await overflow() };
  });
  await step('coop-deposit-seeds', async () => {
    // Seeds in the bag: the slot that shows the seed count (6, granted above); deposit one stack.
    await tab('Coop_Tab_Warehouse');
    const slot = page.locator('section.game-panel button').filter({ hasText: /^6$/ }).first();
    await slot.click({ timeout: 6000 }); await wait(500);
    await clickKey(lk, 'Coop_Deposit', { timeout: 6000 }); await wait(1500);
    return { window: (await windowText()).slice(0, 160), overflow: await overflow() };
  });
  await step('coop-withdraw', async () => {
    const item = page.locator('section.game-panel button').filter({ hasText: /^6$/ }).first();
    await item.click({ timeout: 6000 }); await wait(500);
    await clickKey(lk, 'Coop_Withdraw', { timeout: 6000 }); await wait(1500);
    return { window: (await windowText()).slice(0, 160), overflow: await overflow() };
  });
  // The warehouse log keeps the latest 20 lines (CoopRules.RecentLogLines) and shows ten per page: ten deposit and withdraw pairs fill both pages.
  await step('coop-log-full', async () => {
    await tab('Coop_Tab_Warehouse');
    const seeds = () => page.locator('section.game-panel button').filter({ hasText: /^6$/ }).first();
    for (let i = 0; i < 10; i++) {
      await seeds().click({ timeout: 6000 }); await wait(300);
      await clickKey(lk, 'Coop_Deposit', { timeout: 6000 }); await wait(900);
      await seeds().click({ timeout: 6000 }); await wait(300);
      await clickKey(lk, 'Coop_Withdraw', { timeout: 6000 }); await wait(900);
    }
    await tab('Coop_Tab_Log');
    // Page one holds ten lines and the pager shows; the next page holds the other ten. Each page is measured for an inner scroll.
    const first = { lines: await page.locator('.coop-log').count(), overflow: await overflow() };
    await clickKey(lk, 'Pager_Next', { timeout: 6000 }); await wait(700);
    const second = { lines: await page.locator('.coop-log').count(), position: await page.locator('.pager-position').innerText().catch(() => ''), overflow: await overflow() };
    if (first.lines !== 10 || second.lines !== 10) throw new Error(`log pages hold ${first.lines} and ${second.lines} lines, expected 10 and 10`);
    return { first, second };
  });
  await step('coop-log', async () => {
    await tab('Coop_Tab_Log');
    const log = page.getByRole('heading', { name: nameRe(lk, 'Coop_Log_Heading') }).first();
    const shown = await log.isVisible().catch(() => false);
    if (!shown) throw new Error('no warehouse log heading');
    return { log: (await windowText()).slice(0, 200), overflow: await overflow() };
  });
  await closeWindow(lk);
  await step('coop-reopen-remembers', async () => {
    await pressKey('KeyE'); await wait(1200);
    await clickKey(lk, 'Npc_Action_Coop', { timeout: 6000 }).catch(() => {});
    await wait(1000);
    const active = await page.locator('.coop-tabs .game-button.active').first().innerText().catch(() => '');
    const want = fmt(lk, 'Coop_Tab_Log');
    if (active.trim() !== want) throw new Error(`active tab "${active.trim()}", expected "${want}"`);
    return { active: active.trim(), overflow: await overflow() };
  });
  await closeWindow(lk);
}

// ---------------------------------------------------------------- driver
const FLOWS = { farm: farmFlow, town: townFlow, bin: binFlow, account: accountFlow, mine: mineFlow, market: marketFlow, coop: coopFlow };

(async () => {
  const flow = process.argv[2] || 'farm';
  const locales = (process.argv[3] || 'vi,en').split(',');
  const viewports = (process.argv[4] || '1280x720,390x844').split(',');
  if (!FLOWS[flow]) throw new Error('unknown flow ' + flow);
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  for (const lk of locales) {
    for (const vk of viewports) {
      dir = path.join(OUT_ROOT, flow, lk, vk);
      fs.mkdirSync(dir, { recursive: true });
      frameNo = 0;
      const vp = VIEWPORTS[vk];
      const touch = vp.width < 1024;
      const context = await browser.newContext({ viewport: vp, locale: LANG[lk].tag, hasTouch: touch, isMobile: touch });
      await context.addInitScript(code => { try { localStorage.setItem('nosum.settings.v1', `lang=${code}\n`); } catch (e) {} }, LANG[lk].code);
      page = await context.newPage();
      page.on('pageerror', e => report.errors.push(e.message.slice(0, 200)));
      page.on('console', m => { if (m.type() === 'error' && !/WebSocket|ERR_CONNECTION/.test(m.text())) report.errors.push(m.text().slice(0, 200)); });
      report.flow = flow; report.locale = lk; report.viewport = vk; report.steps = []; report.errors = [];
      try {
        await FLOWS[flow](lk);
      } catch (e) {
        report.steps.push({ name: 'FLOW-ABORTED', ok: false, error: String(e.message || e).slice(0, 300) });
        await shot('aborted').catch(() => {});
      }
      fs.writeFileSync(path.join(dir, 'report.json'), JSON.stringify(report, null, 2));
      console.log(`== ${flow} ${lk} ${vk}: steps ${report.steps.length}, failed ${report.steps.filter(s => !s.ok).length}, page errors ${report.errors.length}`);
      await context.close();
    }
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
