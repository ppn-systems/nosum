// Nosum UI screenshot harness (Playwright, Chromium).
// Usage:
//   BASE_URL=http://localhost:5810 OUT_DIR=/path/to/out node capture.js
// Optional: VIEWPORTS="1280x720,1920x1080,390x844" (default: all three), LOCALE=en-US
// The script signs in as a guest, waits for the world, then captures the HUD and each
// reachable panel at each viewport. It writes OUT_DIR/<WxH>/<screen>.png and
// OUT_DIR/capture-report.json (what was captured and what was skipped, with reasons).
// Requires `require('playwright')` resolvable from this directory (Chromium preinstalled).

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const BASE_URL = (process.env.BASE_URL || 'http://localhost:5810').replace(/\/$/, '');
const OUT_DIR = path.resolve(process.env.OUT_DIR || path.join(__dirname, '..', 'before'));
const LOCALE = process.env.LOCALE || 'en-US';
const ALL_VIEWPORTS = [
  { w: 1280, h: 720, mobile: false },
  { w: 1920, h: 1080, mobile: false },
  { w: 390, h: 844, mobile: true },
];
const wanted = (process.env.VIEWPORTS || '').split(',').map((s) => s.trim()).filter(Boolean);
const VIEWPORTS = wanted.length
  ? ALL_VIEWPORTS.filter((v) => wanted.includes(`${v.w}x${v.h}`))
  : ALL_VIEWPORTS;

// Toolbar panels: label as shown in English (Language.en.resx), hotkey if any.
const TOOLBAR = {
  inventory: { label: 'Inventory', key: 'KeyI' },
  wardrobe: { label: 'Wardrobe', key: 'KeyC' },
  crafting: { label: 'Crafting' },
  trade: { label: 'Trade', key: 'KeyT' },
  coop: { label: 'Co-op', key: 'KeyG' },
  visitors: { label: 'Visitors' },
  friends: { label: 'Friends' },
  skills: { label: 'Skills' },
  pet: { label: 'Pet' },
  profile: { label: 'Profile', key: 'KeyN' },
  'world-map': { label: 'World map', key: 'Tab' },
  journal: { label: 'Journal', key: 'KeyJ' },
};

function addSkip(list, screen, reason) { if (!list.some((x) => x.screen === screen)) list.push({ screen, reason }); }

const log = (...a) => console.log('[capture]', ...a);
async function visibleWithin(locator, ms) {
  try { await locator.waitFor({ state: 'visible', timeout: ms }); return true; } catch (e) { return false; }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function safe(fn, fallback = null) {
  try { return await fn(); } catch (e) { return fallback; }
}

// Opens a toolbar panel by clicking its button (accessible name or title contains the label).
async function openByButton(page, label) {
  const byRole = page.getByRole('button', { name: new RegExp(label.replace(/[-]/g, '[- ]?'), 'i') });
  if (await visibleWithin(byRole.first(), 1000)) {
    await byRole.first().click({ timeout: 3000 });
    return true;
  }
  const byTitle = page.locator(`[title*="${label}" i], [aria-label*="${label}" i]`);
  if (await visibleWithin(byTitle.first(), 1000)) {
    await byTitle.first().click({ timeout: 3000 });
    return true;
  }
  return false;
}

// Escape closes an open panel, but with no panel open it opens the menu on the last tab (Home.razor),
// so press it only while a panel is still on screen.
async function closePanels(page) {
  for (let i = 0; i < 3; i++) {
    if (!(await visibleWithin(page.locator('.game-panel:not(.hud)').first(), 300))) break;
    await page.keyboard.press('Escape');
    await sleep(350);
  }
}

async function shot(page, dir, name, report, status) {
  const file = path.join(dir, `${name}.png`);
  await page.screenshot({ path: file, animations: 'disabled' });
  report.push({ screen: name, file, status: status || 'captured' });
  log('saved', file);
}

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const report = [];
  const notCaptured = [];

  for (const vp of VIEWPORTS) {
    const label = `${vp.w}x${vp.h}`;
    const dir = path.join(OUT_DIR, label);
    fs.mkdirSync(dir, { recursive: true });
    const context = await browser.newContext({
      viewport: { width: vp.w, height: vp.h },
      deviceScaleFactor: 1,
      isMobile: vp.mobile,
      hasTouch: vp.mobile,
      locale: LOCALE,
    });
    // The UI defaults to Vietnamese; seed English (lang=2) once, only when no settings are stored yet.
    await context.addInitScript(() => {
      try {
        if (!localStorage.getItem('nosum.settings.v1')) localStorage.setItem('nosum.settings.v1', 'lang=2\n');
      } catch (e) { /* storage blocked: the page falls back to its defaults */ }
    });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

    await page.goto(BASE_URL + '/', { waitUntil: 'load', timeout: 120000 });
    // Sign-in: the guest button ("Play now as a guest").
    const guest = page.getByRole('button', { name: /as a guest|guest/i }).first();
    await guest.waitFor({ state: 'visible', timeout: 120000 });
    await guest.click();
    // World loaded: the HUD toolbar is on screen (Home.razor shows it once the game started).
    await page.getByRole('toolbar').first().waitFor({ state: 'visible', timeout: 120000 });
    await sleep(4000); // map, farmer sheets and the first snapshot

    await shot(page, dir, 'hud-idle', report);

    // hud-toast: select the seed slot and use it on the farmhouse; the client refuses ("Till the soil first.")
    // and the refusal shows in the HUD message line (GameMessage). This is a client-side refusal, not a server one.
    await page.keyboard.press('Digit2');
    await page.keyboard.press('KeyE');
    await sleep(600);
    const toast = page.getByText(/Till the soil first/i).first();
    if (await visibleWithin(toast, 1500)) {
      await shot(page, dir, 'hud-toast', report);
    } else {
      addSkip(notCaptured, 'hud-toast', 'the use-seed refusal line did not appear');
    }
    await page.keyboard.press('Digit1');
    addSkip(notCaptured, 'hud-banner', 'no level-up or season banner in this pass (needs game progress; not forced)');

    // visitors: the Visitors panel with a refused nickname (the refusal shows inside the panel).
    await closePanels(page);
    if (await openByButton(page, 'Visitors')) {
      await sleep(600);
      const nick = page.getByPlaceholder(/nickname/i).first();
      if (await visibleWithin(nick, 1000)) {
        await nick.fill('nobodyzz');
        await page.getByRole('button', { name: /^visit$/i }).first().click({ timeout: 3000 }).catch(() => {});
        await sleep(1200);
      }
      await shot(page, dir, 'visitors', report);
    } else {
      addSkip(notCaptured, 'visitors', 'Visitors toolbar button not found');
    }

    // Panels: hotkeys where the game has one, toolbar buttons otherwise.
    for (const [name, spec] of Object.entries(TOOLBAR)) {
      if (name === 'visitors') continue; // captured in the Visitors step above
      await closePanels(page);
      let opened = false;
      if (spec.key === 'Tab') {
        await page.keyboard.press('Tab');
        opened = true;
      } else if (spec.key) {
        await page.keyboard.press(spec.key.replace('Key', ''));
        opened = true;
      }
      if (!opened || !(await visibleWithin(page.locator('.game-panel:not(.hud)').first(), 1200))) {
        opened = (await openByButton(page, spec.label)) || opened;
      }
      await sleep(700);
      if (await visibleWithin(page.locator('.game-panel:not(.hud)').first(), 800)) {
        await shot(page, dir, name, report);
      } else {
        addSkip(notCaptured, name, 'panel did not open by hotkey or toolbar button');
      }
      await closePanels(page);
      await sleep(300);
    }

    // Settings / help: H, or the "?" key.
    await closePanels(page);
    await page.keyboard.press('KeyH');
    await sleep(700);
    if (await visibleWithin(page.locator('.game-panel:not(.hud)').first(), 800)) {
      await shot(page, dir, 'settings', report);
    } else {
      addSkip(notCaptured, 'settings', 'H did not open the panel');
    }
    await closePanels(page);

    // Mail: top-right button (no hotkey).
    await sleep(300);
    if (await openByButton(page, 'Mail')) {
      await sleep(700);
      await shot(page, dir, 'mail', report);
    } else {
      addSkip(notCaptured, 'mail', 'Mail button not found');
    }
    await closePanels(page);

    // Not captured by design of this pass: NPC- or world-object-bound panels.
    for (const s of ['chest', 'furnace', 'shop', 'blacksmith', 'market', 'dialogue', 'decorating', 'animals', 'bestiary', 'fishing']) {
      addSkip(notCaptured, s, 'needs standing next to an NPC or world object; not reached in this pass');
    }

    if (errors.length) log(label, 'page errors:', errors.length, errors.slice(0, 3));
    await context.close();
  }

  await browser.close();
  fs.writeFileSync(path.join(OUT_DIR, 'capture-report.json'), JSON.stringify({ base_url: BASE_URL, captured: report, not_captured: notCaptured }, null, 2));
  log('done;', report.length, 'captured,', notCaptured.length, 'not captured (see capture-report.json)');
}

run().catch((e) => { console.error(e); process.exit(1); });
