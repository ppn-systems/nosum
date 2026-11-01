// Captures the animals and pets screens (docs/design/critique-animals-pets.md, docs/adr/ADR-092): the carpenter's animal and pet sections, a coop
// with an empty and a fed feeding bench, the produce sparkle after a day rolls over inside the coop, and pet forage with an open and a full bag.
// Each run is one language and one viewport on a fresh guest.
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
const FORAGE_DAYS = Number(process.env.FORAGE_DAYS || 8);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Labels as the game shows them in each language (Language.resx / Language.en.resx).
const L = {
  en: { guest: /as a guest|guest/i, build: 'Build', pets: 'Pets', buyChicken: /Buy Chicken/, menu: 'Menu', menuPet: 'Pet', pet: 'Pet', choose: 'Choose', next: 'Next', confirm: 'Confirm' },
  vi: { guest: /khách|guest/i, build: 'Xây dựng', pets: 'Thú cưng', buyChicken: /Mua Gà/, menu: 'Menu', menuPet: 'Thú cưng', pet: 'Vuốt ve', choose: 'Chọn', next: 'Tiếp', confirm: 'Xác nhận' },
}[LANG_CODE];

// Tile of the farm where the coop is placed: the server accepts it and its door is open to the tile below (39, 44).
const COOP_TARGET = [40, 40];
const DOOR_APPROACH = [39, 44];
// Distinct stackless items that fill a 36-slot bag (seeds and produce); a forage item cannot stack onto any of them.
const BAG_FILLERS = [100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212];

const report = { captured: [], failed: [] };
const shot = async (page, name) => {
  await page.screenshot({ path: path.join(OUT_DIR, `${name}.png`), animations: 'disabled' });
  report.captured.push(name);
};
const step = async (name, fn) => {
  try {
    await fn();
  } catch (e) {
    report.failed.push({ step: name, reason: String(e).slice(0, 160) });
  }
};
// The hay count in hotbar slot 9 (-1 when the slot cannot be read).
const hayInSlot9 = (page) => page.evaluate(() => {
  const slot = [...document.querySelectorAll('[role=toolbar] button')].find((b) => /^9\d*$/.test(b.textContent.trim()));
  return slot ? Number(slot.textContent.trim().slice(1) || 0) : -1;
});
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
  const qa = (fn, args) => page.evaluate(([f, a]) => window.nosumQa[f](...a), [fn, args]);
  // Opens the game menu on the Pet tile and pets the pet once (the petting is once a game day).
  // The column's Menu button opens the game menu; its Pet tile (a tile, not a button) opens the pet panel with its Pet button.
  const pet = async (label) => {
    await step(`${label}-open-menu`, async () => {
      await page.getByRole('button', { name: L.menu, exact: true }).first().click({ timeout: 8000 }); await sleep(900);
      await page.getByText(L.menuPet, { exact: true }).first().click({ timeout: 8000 }); await sleep(900);
    });
    await step(`${label}-pet`, async () => {
      await page.getByRole('button', { name: L.pet, exact: true }).first().click({ timeout: 8000 }); await sleep(1800);
    });
    await step(label, () => shot(page, label));
    // The panel covers the farmer's tile: the world frame after closing it shows whatever the forage left on the grass at the farmer's feet.
    await step(`${label}-close`, async () => { await page.locator('.game-close').first().click({ timeout: 4000 }); await sleep(900); });
    await step(`${label}-world`, () => shot(page, `${label}-world`));
  };

  try {
    await page.goto(BASE_URL + '/?qa=1', { waitUntil: 'load', timeout: 120000 });
    await page.getByRole('button', { name: L.guest }).first().click({ timeout: 120000 });
    await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
    await sleep(3000);

    // A coop on the farm: coins and the blueprint are granted (the blueprint takes hotbar slot 9), it is set down on a clear tile in
    // front of the farmer and the second press builds it (the first press only asks; docs/adr/ADR-028 D1). It takes two game days.
    await qa('grant', [0, 0, 20000]); await sleep(900);
    await qa('grant', [610, 1, 0]); await sleep(1200);
    await qa('teleport', ['farm', COOP_TARGET[0], COOP_TARGET[1]]); await sleep(2500);
    await page.keyboard.press('Digit9'); await sleep(900);
    await page.keyboard.press('KeyE'); await sleep(1200);
    await page.keyboard.press('KeyE'); await sleep(1500);
    await qa('advanceClock', [2]); await sleep(2500);
    await step('coop-built', () => shot(page, 'coop-built'));

    // Hay for the bench (slot 9 again), two chickens from the carpenter's Build page, then one cat from the Pets page.
    await qa('grant', [508, 30, 0]); await sleep(1500);
    await qa('teleport', ['town', 10, 86]); await sleep(2500);
    await page.keyboard.press('KeyE'); await sleep(1800);
    await page.getByRole('button', { name: L.build, exact: true }).click(); await sleep(1200);
    await step('carpenter-animals', () => shot(page, 'carpenter-animals'));
    await step('buy-chickens', async () => {
      for (const name of ['Hen', 'Dot']) {
        await page.locator('input').first().fill(name); await sleep(300);
        await page.getByRole('button', { name: L.buyChicken }).first().click(); await sleep(2000);
      }
    });
    await step('carpenter-bought', () => shot(page, 'carpenter-bought'));
    await step('buy-cat', async () => {
      await page.getByRole('button', { name: L.pets, exact: true }).first().click(); await sleep(1000);
      await page.getByRole('button', { name: L.choose, exact: true }).first().click(); await sleep(900);
      await page.locator('input').first().fill('Mochi'); await sleep(300);
      await page.getByRole('button', { name: L.next, exact: true }).first().click(); await sleep(900);
      await page.getByRole('button', { name: L.confirm, exact: true }).first().click(); await sleep(2000);
    });
    await step('carpenter-pet-bought', () => shot(page, 'carpenter-pet-bought'));
    await step('close-carpenter', async () => { await page.locator('.game-close').first().click(); await sleep(600); });

    // Walk into the coop through its door (the tile below the door is DOOR_APPROACH), stand under the bench with hay in hand.
    await qa('teleport', ['farm', DOOR_APPROACH[0], DOOR_APPROACH[1]]); await sleep(2500);
    await hold(page, 'ArrowUp', 900); await sleep(900);
    await page.keyboard.press('Digit9'); await sleep(300);
    await hold(page, 'KeyD', 900); await hold(page, 'KeyW', 1200); await sleep(300);
    await step('coop-empty-bench', () => shot(page, 'coop-empty-bench'));
    // The press is retried until the hay in hand goes down. A press that misses means the walk stopped short of the trough, so each retry first
    // steps toward the trough (right, then up, then left) and presses again: the hotbar's slot 9 reads its number and then the count ("930").
    await step('feed-once', async () => {
      const steps = [[null, 0], ['KeyD', 400], ['KeyW', 500], ['KeyA', 400], ['KeyW', 300]];
      let attempt = 0;
      for (const [key, ms] of steps) {
        if (key) { await hold(page, key, ms); }
        const before = await hayInSlot9(page);
        await page.keyboard.press('KeyE'); await sleep(1500);
        const after = await hayInSlot9(page);
        attempt += 1;
        report.feed = { before, after, presses: attempt };
        if (after >= 0 && after < before) { break; }
      }
    });
    await step('coop-fed', () => shot(page, 'coop-fed'));

    // The day rolls over while the farmer stays inside: the room is read again, and the produce sparkle marks the hens that laid (ADR-092 D1).
    await qa('advanceClock', [1]); await sleep(3000);
    await step('coop-new-day-sparkle', () => shot(page, 'coop-new-day-sparkle'));

    // Hoe in hand, walking toward the hens in short steps: the hint says Collect once a hen with produce is within reach. The hens wander,
    // so every stop is a frame and the report says which one shows the label.
    await step('hint-collect', async () => {
      await page.keyboard.press('Digit1'); await sleep(400);
      for (let i = 0; i < 6; i += 1) {
        await hold(page, 'KeyA', 180);
        await shot(page, `coop-hint-collect-${i}`);
      }
    });
    // Next to a hen with no produce the hint says Pet.
    await step('hint-pet', async () => {
      await hold(page, 'KeyS', 200);
      await shot(page, 'coop-hint-pet');
    });

    // Out to the farm; the pet is petted once a game day. The first petting finds a forage item 30 times in 100 (ADR-092 D2).
    await qa('teleport', ['farm', DOOR_APPROACH[0], DOOR_APPROACH[1]]); await sleep(2500);
    await pet('pet-day-1');

    // A full bag: the next forage item has no slot, so it lands on the grass at the farmer's feet.
    await step('fill-bag', async () => {
      for (const id of BAG_FILLERS) { await qa('grant', [id, 1, 0]); await sleep(650); }
      await sleep(1200);
    });
    await step('bag-full', () => shot(page, 'bag-full'));
    for (let day = 2; day <= FORAGE_DAYS; day += 1) {
      await qa('advanceClock', [1]); await sleep(2000);
      await qa('teleport', ['farm', DOOR_APPROACH[0], DOOR_APPROACH[1]]); await sleep(2000);
      await pet(`pet-day-${day}-full-bag`);
    }
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
