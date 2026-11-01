// Captures the screens that need the Development QA hook (docs/adr/ADR-079): run against a client built with
// -p:NosumDevTools=true and a server started with DOTNET_ENVIRONMENT=Development.
// Usage: BASE_URL=http://localhost:5820 OUT_DIR=<dir> [VIEWPORTS=1280x720,390x844] node capture-blocked.js
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5820').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || '.');
const ALL = [
  { w: 1280, h: 720, mobile: false },
  { w: 1920, h: 1080, mobile: false },
  { w: 390, h: 844, mobile: true },
];
const wanted = (process.env.VIEWPORTS || '').split(',').filter(Boolean);
const VIEWPORTS = wanted.length ? ALL.filter((v) => wanted.includes(`${v.w}x${v.h}`)) : ALL;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const report = { captured: [], failed: [] };

// Town counters (gameplay/Nosum.Gameplay/Maps/GameMaps.cs): the tile the player stands on to talk to the keeper.
const TOWN = { seed: [43, 57], blacksmith: [94, 82], board: [42, 57], market: [45, 71], carpenter: [10, 86] };

async function shot(page, dir, name) {
  await page.screenshot({ path: path.join(dir, `${name}.png`), animations: 'disabled' });
  report.captured.push(name);
}
async function fail(name, reason) {
  report.failed.push({ screen: name, reason });
}
async function closeAll(page) {
  // An NPC box closes with its Leave button, a panel with Escape.
  const leave = page.getByRole('button', { name: 'Leave', exact: true }).first();
  if (await leave.isVisible().catch(() => false)) {
    await leave.click({ timeout: 3000 }).catch(() => {});
    await sleep(600);
  }
  for (let i = 0; i < 3; i++) { await page.keyboard.press('Escape'); await sleep(250); }
  await sleep(400);
}
// Stands on a counter, talks (E advances the greeting until the option buttons show), then presses the option. Tries twice.
async function talkOnce(page, tile, option) {
  await page.evaluate(([x, y]) => window.nosumQa.teleport('town', x, y), tile);
  await sleep(4500);
  const btn = page.getByRole('button', { name: option, exact: true }).first();
  let seen = [];
  for (let i = 0; i < 8; i++) {
    await page.keyboard.press('KeyE');
    await sleep(900);
    if (await btn.isVisible().catch(() => false)) {
      await btn.click({ timeout: 5000 });
      await sleep(1500);
      return;
    }
    seen = (await page.getByRole('button').allInnerTexts()).map((t) => t.trim()).filter(Boolean);
  }
  throw new Error(`no "${option}" option after talking; buttons seen: ${JSON.stringify(seen.slice(-6))}`);
}

async function talk(page, tile, option) {
  try {
    await talkOnce(page, tile, option);
  } catch (e) {
    await closeAll(page);
    await talkOnce(page, tile, option);
  }
}

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  for (const vp of VIEWPORTS) {
    const label = `${vp.w}x${vp.h}`;
    const dir = path.join(OUT_DIR, label);
    fs.mkdirSync(dir, { recursive: true });
    const context = await browser.newContext({ viewport: { width: vp.w, height: vp.h }, deviceScaleFactor: 1, isMobile: vp.mobile, hasTouch: vp.mobile, locale: 'en-US' });
    await context.addInitScript(() => { try { if (!localStorage.getItem('nosum.settings.v1')) localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch (e) { /* storage blocked */ } });
    const page = await context.newPage();
    await page.goto(BASE_URL + '/?qa=1', { waitUntil: 'load', timeout: 120000 });
    await page.getByRole('button', { name: /as a guest|guest/i }).first().click({ timeout: 120000 });
    await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
    await sleep(3000);

    // hud-banner: the level-up and season banners (top centre).
    try {
      await page.evaluate(() => window.nosumQa.levelUp(0, 2));
      await sleep(1200);
      await shot(page, dir, 'hud-banner-level');
      await sleep(4500);
    } catch (e) { await fail('hud-banner', String(e).slice(0, 200)); }

    const screens = [
      ['shop', TOWN.seed, 'Buy'],
      ['blacksmith', TOWN.blacksmith, 'Buy'],
      ['market', TOWN.market, 'Buy'],
      ['quests-board', TOWN.board, 'Goals'],
      ['animals', TOWN.carpenter, 'Buy'], // the carpenter's animal goods (the coop and barn pages need a building of the player's own)
    ];
    for (const [name, tile, option] of screens) {
      try {
        await closeAll(page);
        await talk(page, tile, option);
        await shot(page, dir, name);
      } catch (e) { await fail(name, String(e).slice(0, 200)); }
    }

    // hud-banner (season): last, because a season change moves the clock to midnight and the shops close.
    try {
      await closeAll(page);
      await page.evaluate(() => window.nosumQa.season(1));
      await sleep(1200);
      await shot(page, dir, 'hud-banner-season');
    } catch (e) { await fail('hud-banner-season', String(e).slice(0, 200)); }

    // dialogue: the seed keeper's Talk option (a plain dialogue box).
    try {
      await closeAll(page);
      await talk(page, TOWN.seed, 'Talk');
      await shot(page, dir, 'dialogue');
    } catch (e) { await fail('dialogue', String(e).slice(0, 200)); }

    // bestiary: the Skills page scrolled to the bestiary section.
    try {
      await closeAll(page);
      await closeAll(page);
      // Skills is a tile of the game menu (docs/design/ui-standard.md section 2): open Menu, then the Skills tile.
      await page.getByRole('button', { name: 'Menu', exact: true }).first().click({ timeout: 4000 });
      await sleep(500);
      await page.getByRole('button', { name: 'Skills', exact: true }).first().click({ timeout: 4000 });
      await sleep(800);
      await page.getByText('Bestiary', { exact: false }).first().scrollIntoViewIfNeeded({ timeout: 3000 });
      await sleep(400);
      await shot(page, dir, 'bestiary');
    } catch (e) { await fail('bestiary', String(e).slice(0, 200)); }

    // chat: Enter opens the chat line.
    try {
      await closeAll(page);
      await page.keyboard.press('Enter');
      await sleep(800);
      await shot(page, dir, 'chat');
    } catch (e) { await fail('chat', String(e).slice(0, 200)); }

    await context.close();
  }
  await browser.close();
  fs.writeFileSync(path.join(OUT_DIR, 'capture-blocked-report.json'), JSON.stringify(report, null, 2));
  console.log('done', JSON.stringify({ captured: report.captured.length, failed: report.failed }));
}

run().catch((e) => { console.error('FAIL', e); process.exit(1); });
