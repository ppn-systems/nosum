// HUD and menu column capture (docs/qa/hud-menu-column). For each language (vi, en) and viewport (1280x720, 1920x1080, 390x844 touch) it opens the
// game as a guest and writes: 00-hud.png (HUD after start), 01-inventory.png, 02-crafting.png and 03-chest.png (the bag and a chest together, XL panel).
// The chest is reached with DevTools: a Development server and a client built with -p:NosumDevTools=true grant one chest, it is placed on the tile
// in front of the farmer and the action key opens it. For vi at 1280x720 and 390x844 it also writes dom-buttons.txt (column aria-labels in order and
// the bounding boxes of .hud-player-bar and .hud-minimap).
//
// Needs the server (dotnet run --project server/Nosum.Host with DOTNET_ENVIRONMENT=Development, PostgreSQL nosum_dev) and the client published with
// -p:NosumDevTools=true and served on http://localhost:5080 (NOSUM_QA_URL overrides). See the commands in the QA report.
//
// usage: node tests/browser/hud-menu-column-capture.cjs <before|after> [viewports, comma-separated]
// writes <NOSUM_QA_OUT or /home/user/nosum/docs/qa/hud-menu-column>/<phase>/<lang>/<viewport>/*.png

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/';
const OUT = process.env.NOSUM_QA_OUT || '/home/user/nosum/docs/qa/hud-menu-column';
const VIEWPORTS = {
  '1280x720': { width: 1280, height: 720 },
  '1920x1080': { width: 1920, height: 1080 },
  '390x844': { width: 390, height: 844 },
};
const LANGS = {
  vi: { setting: 1, locale: 'vi-VN', bag: /Túi đồ/, crafting: /^Chế tạo$/ },
  en: { setting: 2, locale: 'en-US', bag: /^Inventory/, crafting: /^Crafting$/ },
};
const CHEST_ITEM = 600; // ItemCatalog.Chest
const BAG_KEY = 'Digit9'; // a free hotbar slot: the granted chest lands there

async function openGame(browser, lang, viewport) {
  const size = VIEWPORTS[viewport];
  const phone = viewport === '390x844';
  const settings = `lang=${LANGS[lang].setting}\ntutorial=0\n`;
  const context = await browser.newContext({ viewport: size, locale: LANGS[lang].locale, hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  await context.addInitScript((text) => {
    try { if (!sessionStorage.getItem('nosum-qa-seeded')) { localStorage.setItem('nosum.settings.v1', text); sessionStorage.setItem('nosum-qa-seeded', '1'); } } catch { /* storage blocked */ }
  }, settings);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 160)}`); });
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await page.waitForTimeout(4000);
  return { context, page, errors };
}

async function labels(page) {
  return page.$$eval('.game-hud .hud-top-right .game-icon-button', (els) => els.map((e) => e.getAttribute('aria-label')));
}

async function clickColumn(page, pattern) {
  const all = await page.$$('.game-hud .hud-top-right .game-icon-button');
  for (const b of all) {
    const label = await b.getAttribute('aria-label');
    if (label && pattern.test(label)) {
      await b.click({ force: true });
      return label;
    }
  }
  throw new Error(`no column button matches ${pattern}`);
}

async function closePanel(page) {
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
}

// Grants one chest, places it on the tile in front of the farmer (facing down from the farmhouse door), then faces it and opens it.
async function openChest(page) {
  await page.evaluate(async (itemId) => {
    const exports = await (await globalThis.getDotnetRuntime(0)).getAssemblyExports('Nosum.Wasm.dll');
    exports.Nosum.Wasm.Game.DevTools.Grant(itemId, 1, 0);
  }, CHEST_ITEM);
  await page.waitForTimeout(1500);
  await page.keyboard.press(BAG_KEY);
  await page.waitForTimeout(400);
  await page.keyboard.down('KeyS');
  await page.waitForTimeout(450);
  await page.keyboard.up('KeyS');
  await page.waitForTimeout(500);
  await page.keyboard.press('KeyE'); // place
  await page.waitForTimeout(1200);
  await page.keyboard.press('KeyE'); // open the chest in front
  await page.waitForTimeout(1500);
  return (await page.locator('.chest-grid').count()) > 0;
}

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: hud-menu-column-capture.cjs <before|after> [viewports]');
  const viewports = (process.argv[3] || Object.keys(VIEWPORTS).join(',')).split(',');
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const report = [];
  const dom = [];
  for (const lang of Object.keys(LANGS)) {
    for (const viewport of viewports) {
      const dir = path.join(OUT, phase, lang, viewport);
      fs.mkdirSync(dir, { recursive: true });
      const { context, page, errors } = await openGame(browser, lang, viewport);
      const column = await labels(page);
      const bagOk = column.some((l) => LANGS[lang].bag.test(l));
      if (!bagOk) throw new Error(`${lang} ${viewport}: HUD is not in ${lang} (column: ${column.join(' | ')})`);
      report.push(`${lang} ${viewport} | column: ${column.join(' | ')}`);
      if (lang === 'vi' && (viewport === '1280x720' || viewport === '390x844')) {
        const boxes = await page.evaluate(() => {
          const box = (sel) => { const e = document.querySelector(sel); if (!e) return 'absent'; const r = e.getBoundingClientRect(); return `x=${Math.round(r.left)} y=${Math.round(r.top)} w=${Math.round(r.width)} h=${Math.round(r.height)}`; };
          return { player: box('.hud-player-bar'), minimap: box('.hud-minimap') };
        });
        dom.push(`## ${viewport} (vi)`, ...column.map((l, i) => `${i + 1}. ${l}`), `.hud-player-bar: ${boxes.player}`, `.hud-minimap: ${boxes.minimap}`, '');
      }
      await page.screenshot({ path: path.join(dir, '00-hud.png') });
      await clickColumn(page, LANGS[lang].bag);
      await page.waitForTimeout(900);
      await page.screenshot({ path: path.join(dir, '01-inventory.png') });
      await closePanel(page);
      await clickColumn(page, LANGS[lang].crafting);
      await page.waitForTimeout(900);
      await page.screenshot({ path: path.join(dir, '02-crafting.png') });
      await closePanel(page);
      const opened = await openChest(page);
      if (opened) {
        await page.screenshot({ path: path.join(dir, '03-chest.png') });
        await closePanel(page);
      }
      report.push(`${lang} ${viewport} | chest: ${opened ? 'captured' : 'NOT OPENED'} | errors: ${errors.length ? errors.slice(0, 3).join(' ; ') : 'none'}`);
      await context.close();
    }
  }
  await browser.close();
  if (dom.length) fs.writeFileSync(path.join(OUT, phase, 'dom-buttons.txt'), dom.join('\n') + '\n');
  fs.writeFileSync(path.join(OUT, `run-${phase}.txt`), report.join('\n') + '\n');
  console.log(report.join('\n'));
})().catch((e) => { console.error(e); process.exit(1); });
