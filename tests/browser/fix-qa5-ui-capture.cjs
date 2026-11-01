// QA5 UI fix capture (docs/qa/fix-qa5-ui). Opens the game as a guest in a fresh session per locale and viewport, walks the HUD steps
// listed below, and writes for each one a PNG and a DOM measurement. It is tolerant: a step that cannot run is recorded as
// {"status":"skipped","why":...} and the run goes on. The same script runs against the baseline (origin/master) and the modified build;
// only the URL differs.
//
// usage: node tests/browser/fix-qa5-ui-capture.cjs <outDir> [locales=vi,en] [viewports=1280x720,1920x1080,390x844]
// env:   NOSUM_QA_URL (default http://localhost:5080/?qa=1), e.g. NOSUM_QA_URL=http://localhost:5081/?qa=1 for the modified build.
// output: <outDir>/<locale>/<viewport>/NN-<name>.png and <outDir>/<locale>-<viewport>.json
//         where <locale> is the short key used on the command line (vi or en), <viewport> is e.g. 390x844.
//
// Needs the server (DOTNET_ENVIRONMENT=Development, PostgreSQL nosum_dev) and a client built with -p:NosumDevTools=true, so that
// window.nosumQa exists (ADR-079). The page is opened with ?qa=1.

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const VIEWPORTS = {
  '1280x720': { width: 1280, height: 720 },
  '1920x1080': { width: 1920, height: 1080 },
  '390x844': { width: 390, height: 844 },
};
// setting: value of lang= in the stored settings (1 vi, 2 en), as hud-menu-column-capture.cjs writes it.
const LOCALES = {
  vi: { locale: 'vi-VN', setting: 1 },
  en: { locale: 'en-US', setting: 2 },
};
// Seed shop counter (gameplay/Nosum.Gameplay/Maps/GameMaps.cs, town counter used by world-pass.cjs).
const SEED_SHOP = [43, 57];
const MENU_RE = /^(menu|trình đơn|menu game)$/i;
const SETTINGS_RE = /cài đặt|settings/i;
const MENU_TILE_CRAFTING_RE = /chế tạo|crafting/i;
const MENU_TILE_INVENTORY_RE = /túi đồ|inventory/i;

const [outArg, localesArg, viewportsArg] = process.argv.slice(2);
if (!outArg) {
  console.error('usage: node tests/browser/fix-qa5-ui-capture.cjs <outDir> [locales=vi,en] [viewports=1280x720,1920x1080,390x844]');
  process.exit(2);
}
const OUT = path.resolve(outArg);
const LOCALE_KEYS = (localesArg || 'vi,en').split(',').map((s) => s.trim()).filter(Boolean);
const VP_KEYS = (viewportsArg || '1280x720,1920x1080,390x844').split(',').map((s) => s.trim()).filter(Boolean);

// In-page helpers, passed to page.evaluate as source (they must not close over Node scope).
const IN_PAGE = `
  window.__qa = {
    rect(e) { if (!e) return null; const r = e.getBoundingClientRect(); return [r.left, r.top, r.width, r.height].map((v) => Math.round(v)); },
    visible(e) { if (!e) return false; const r = e.getBoundingClientRect(); const cs = getComputedStyle(e); return r.width > 0 && r.height > 0 && cs.display !== 'none' && cs.visibility !== 'hidden'; },
    cls(e) { return String((e && (e.className && e.className.baseVal !== undefined ? e.className.baseVal : e.className)) || '').slice(0, 60); },
    txt(e) { return ((e && (e.innerText || e.getAttribute('aria-label') || '')) || '').trim().replace(/\\s+/g, ' ').slice(0, 60); },
    // Every visible game panel (title and rect), so a window that is not the first one is still measured.
    panels() {
      return [...document.querySelectorAll('.game-panel')].filter((p) => window.__qa.visible(p)).map((p) => ({
        title: window.__qa.txt(p.querySelector('.game-panel-title')),
        rect: window.__qa.rect(p),
      }));
    },
  };
`;

async function run() {
  fs.mkdirSync(OUT, { recursive: true });
  for (const loc of LOCALE_KEYS) {
    if (!LOCALES[loc]) {
      console.error(`unknown locale ${loc}; use vi or en`);
      continue;
    }
    for (const vp of VP_KEYS) {
      if (!VIEWPORTS[vp]) {
        console.error(`unknown viewport ${vp}`);
        continue;
      }
      // A fresh browser per combination: one renderer crash must not take the rest of the run with it.
      let browser = null;
      try {
        browser = await chromium.launch({ args: ['--no-sandbox', '--disable-dev-shm-usage'] });
        await captureOne(browser, loc, vp);
      } catch (e) {
        // Never abort the whole run: record the failure for this combination and carry on.
        const file = path.join(OUT, `${loc}-${vp}.json`);
        fs.writeFileSync(file, JSON.stringify({ locale: loc, viewport: vp, fatal: String(e && e.message).slice(0, 500) }, null, 2));
        console.error(`${loc} ${vp} failed: ${e && e.message}`);
      } finally {
        if (browser) await browser.close().catch(() => {});
      }
    }
  }
}

async function captureOne(browser, loc, vp) {
  const size = VIEWPORTS[vp];
  const phone = vp === '390x844';
  const dir = path.join(OUT, loc, vp);
  fs.mkdirSync(dir, { recursive: true });
  const report = {
    locale: loc,
    viewport: vp,
    base: BASE,
    startedAt: new Date().toISOString(),
    topRight: [],
    steps: [],
    errors: [],
  };
  const errors = report.errors;

  const context = await browser.newContext({
    viewport: size,
    locale: LOCALES[loc].locale,
    hasTouch: phone,
    isMobile: phone,
    serviceWorkers: 'block',
  });
  // Language only: the first-run hint and onboarding are left as the game shows them.
  const settings = `lang=${LOCALES[loc].setting}\n`;
  await context.addInitScript((text) => {
    try {
      if (!sessionStorage.getItem('nosum-qa-fix-seeded')) {
        localStorage.setItem('nosum.settings.v1', text);
        sessionStorage.setItem('nosum-qa-fix-seeded', '1');
      }
    } catch {
      /* storage blocked: the default language applies */
    }
  }, settings);
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`page: ${String(e.message).slice(0, 300)}`));
  page.on('console', (m) => {
    if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 300)}`);
  });
  await page.addInitScript(IN_PAGE);

  const wait = (ms) => page.waitForTimeout(ms);
  const rec = (name, status, extra = {}) => {
    const entry = { name, status, ...extra };
    report.steps.push(entry);
    return entry;
  };
  let shotNo = 0;
  // Runs one step: fn returns {measure, shot?} data. A throw is recorded as skipped with its first line.
  const step = async (name, fn, { shot = null, phoneOnly = false, desktopOnly = false } = {}) => {
    if (phoneOnly && !phone) return rec(name, 'skipped', { why: 'phone only' });
    if (desktopOnly && phone) return rec(name, 'skipped', { why: 'desktop only' });
    try {
      const data = (await fn()) || {};
      let file = null;
      if (shot) {
        shotNo += 1;
        file = `${String(shot.n).padStart(2, '0')}-${shot.name}.png`;
        await page.screenshot({ path: path.join(dir, file) });
      }
      return rec(name, 'ok', { ...(file ? { file: path.relative(REPO, path.join(dir, file)) } : {}), ...data });
    } catch (e) {
      return rec(name, 'skipped', { why: String((e && e.message) || e).split('\n')[0].slice(0, 220) });
    }
  };

  // Closes open windows: an NPC or merchant dialog by its last action (Leave), anything else by Escape. Bounded.
  const closeAll = async () => {
    for (let i = 0; i < 4; i += 1) {
      const npc = await page.$('.npc-box-wrap');
      const panel = await page.$('.game-panel, .trade-window, .shop-list');
      if (!npc && !panel) break;
      if (npc) {
        const leave = page.locator('.npc-actions button').last();
        if ((await leave.count()) > 0) await leave.click({ force: true }).catch(() => {});
        else await page.keyboard.press('Escape');
      } else {
        await page.keyboard.press('Escape');
      }
      await wait(300);
    }
  };

  // Opens a HUD panel: its column button when the column shows it, otherwise the Menu window's tile.
  const openPanel = async (re) => {
    const col = page.locator('.hud-top-right .game-icon-button, .hud-menu-column button');
    const n = await col.count();
    for (let i = 0; i < n; i += 1) {
      const b = col.nth(i);
      const label = (await b.getAttribute('aria-label')) || '';
      if (re.test(label) && (await b.isVisible())) {
        await b.click({ force: true });
        await wait(500);
        return label;
      }
    }
    await openMenu();
    const tiles = page.locator('.game-menu-tile');
    const tn = await tiles.count();
    for (let i = 0; i < tn; i += 1) {
      const t = tiles.nth(i);
      const label = (await t.getAttribute('aria-label')) || '';
      if (re.test(label)) {
        await t.click({ force: true });
        await wait(500);
        return label;
      }
    }
    throw new Error(`no column button or menu tile matches ${re}`);
  };

  // Opens the game menu window with the Menu button of the column (on desktop it is also a column button).
  const openMenu = async () => {
    if (await page.$('.game-menu-tiles')) return 'already open';
    const col = page.locator('.hud-menu-column button, .hud-top-right .game-icon-button');
    const n = await col.count();
    for (let i = 0; i < n; i += 1) {
      const b = col.nth(i);
      const label = (await b.getAttribute('aria-label')) || '';
      if (MENU_RE.test(label) && (await b.isVisible())) {
        await b.click({ force: true });
        await wait(600);
        return label;
      }
    }
    const last = page.locator('.hud-menu-column button');
    const ln = await last.count();
    if (ln > 0) {
      await last.nth(ln - 1).click({ force: true });
      await wait(600);
      return 'last column button';
    }
    throw new Error('no Menu button in .hud-menu-column');
  };

  // Teleports to the seed-shop counter and talks to the merchant (E), finishing the typed line.
  const talkToSeedMerchant = async () => {
    await page.evaluate(([x, y]) => window.nosumQa.teleport('town', x, y), SEED_SHOP);
    await wait(2500);
    await page.keyboard.press('KeyE');
    await wait(400);
    if (!(await page.$('.npc-box-wrap, .npc-box-body'))) throw new Error('no merchant dialog at the seed shop counter');
    await page.locator('.npc-line').first().click({ timeout: 3000 }).catch(() => {});
    await page.waitForSelector('.npc-actions button', { timeout: 5000 }).catch(() => {});
  };

  const minimapState = () =>
    page.evaluate(() => {
      const m = document.querySelector('.hud-minimap');
      const b = document.querySelector('.hud-minimap-button');
      return {
        minimap: !!m,
        minimapVisible: window.__qa.visible(m),
        minimapRect: window.__qa.rect(m),
        minimapButton: !!b,
        minimapButtonVisible: window.__qa.visible(b),
        minimapButtonRect: window.__qa.rect(b),
      };
    });

  // 00 sign in as a guest.
  try {
    await page.goto(BASE, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 90000 });
    await wait(1000);
    await page.getByRole('button', { name: /khách|guest/i }).click();
    await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
    await wait(3000);
  } catch (e) {
    report.fatal = `sign-in: ${String(e.message).slice(0, 200)}`;
    fs.writeFileSync(path.join(OUT, `${loc}-${vp}.json`), JSON.stringify(report, null, 2));
    await context.close();
    return;
  }

  const topRight = () =>
    page.evaluate(() =>
      [...document.querySelectorAll('.hud-top-right .game-icon-button')].map((b) => ({
        label: b.getAttribute('aria-label') || b.innerText.trim(),
        rect: window.__qa.rect(b),
        w: Math.round(b.getBoundingClientRect().width),
        h: Math.round(b.getBoundingClientRect().height),
      })),
    );
  report.topRight = await topRight();
  report.startTopLeftQuest = await page.evaluate(() => window.__qa.rect(document.querySelector('.quest-wrap')));
  report.phoneHintAtStart = await page.evaluate(() => {
    const sel = '.game-field-hint, .hint-line, .game-message, .hint-dock';
    return [...document.querySelectorAll(sel)].filter(window.__qa.visible).map((e) => ({ cls: window.__qa.cls(e), text: window.__qa.txt(e), rect: window.__qa.rect(e) }));
  });

  // 01 HUD after start.
  await step('01 hud start', async () => ({ hud: await page.$('.game-hud') !== null }), { shot: { n: 1, name: 'hud-start' } });

  // 02-04 minimap: M toggles it.
  await step('02 minimap before M', async () => {
    await page.evaluate(() => document.querySelector('canvas')?.focus());
    return { measure: await minimapState() };
  }, { shot: { n: 2, name: 'minimap-before-M' } });
  await step('03 minimap after M', async () => {
    await page.keyboard.press('KeyM');
    await wait(600);
    return { measure: await minimapState() };
  }, { shot: { n: 3, name: 'minimap-after-M' } });
  await step('04 minimap after second M', async () => {
    await page.keyboard.press('KeyM');
    await wait(600);
    return { measure: await minimapState() };
  }, { shot: { n: 4, name: 'minimap-after-second-M' } });

  // 05-06 settings window and its scroll state.
  await step('05 settings open', async () => {
    const label = await openPanel(SETTINGS_RE);
    const measure = await page.evaluate(() => {
      const hc = document.querySelector('.hud-center');
      const scrollers = [];
      if (hc) {
        for (const e of hc.querySelectorAll('*')) {
          const cs = getComputedStyle(e);
          if (e.clientHeight > 0 && e.scrollHeight > e.clientHeight + 2 && /(auto|scroll)/.test(cs.overflowY)) {
            scrollers.push({ cls: window.__qa.cls(e), tag: e.tagName, scrollHeight: e.scrollHeight, clientHeight: e.clientHeight, overflowY: cs.overflowY });
          }
        }
      }
      return { hudCenterRect: window.__qa.rect(hc), panelRect: window.__qa.rect(document.querySelector('.game-panel')), scrollers };
    });
    return { label, measure };
  }, { shot: { n: 5, name: 'settings-open' } });
  await step('06 settings close', async () => {
    await page.keyboard.press('Escape');
    await wait(400);
    return { measure: { panels: await page.evaluate(() => window.__qa.panels()) } };
  }, { shot: { n: 6, name: 'settings-close' } });

  // 07 the game menu column and the menu window.
  await step('07 column menu open', async () => {
    const clicked = await openMenu();
    const measure = await page.evaluate(() => {
      const btns = [...document.querySelectorAll('.hud-menu-column button')];
      const vis = btns.filter(window.__qa.visible);
      const top = [...document.querySelectorAll('.hud-top-right .game-icon-button')].filter(window.__qa.visible);
      return {
        menuColumnTotal: btns.length,
        menuColumnVisible: vis.length,
        menuColumnLabels: vis.map((b) => b.getAttribute('aria-label') || b.innerText.trim()),
        menuColumnRect: window.__qa.rect(document.querySelector('.hud-menu-column')),
        topRightVisible: top.length,
        topRightLabels: top.map((b) => b.getAttribute('aria-label') || b.innerText.trim()),
        menuTiles: [...document.querySelectorAll('.game-menu-tile')].map((t) => t.getAttribute('aria-label') || window.__qa.txt(t)),
      };
    });
    return { clicked, measure };
  }, { shot: { n: 7, name: 'column-menu-open' } });

  // 08 open a feature panel from the menu window's tile, then close it.
  await step('08 panel from menu', async () => {
    await openMenu();
    const tiles = page.locator('.game-menu-tile');
    const n = await tiles.count();
    let picked = null;
    for (const re of [MENU_TILE_CRAFTING_RE, MENU_TILE_INVENTORY_RE]) {
      for (let i = 0; i < n && !picked; i += 1) {
        const label = (await tiles.nth(i).getAttribute('aria-label')) || '';
        if (re.test(label)) picked = { i, label };
      }
      if (picked) break;
    }
    if (!picked) {
      const labels = await tiles.evaluateAll((els) => els.map((e) => e.getAttribute('aria-label') || e.innerText.trim()));
      throw new Error(`no menu tile for crafting or inventory; tiles: ${labels.join(' | ')}`);
    }
    await tiles.nth(picked.i).click({ force: true });
    await wait(600);
    return { label: picked.label, measure: { panelTitle: await page.evaluate(() => window.__qa.txt(document.querySelector('.game-panel-title'))) } };
  }, { shot: { n: 8, name: 'panel-from-menu' } });
  await step('08b panel from menu closed', async () => {
    await page.keyboard.press('Escape');
    await wait(400);
    await closeAll();
    return { measure: {} };
  });

  // 09 seed shop: buy list, quantity controls, small targets, clipped text.
  await step('09 shop seed', async () => {
    await closeAll();
    await talkToSeedMerchant();
    const buy = page.locator('.npc-actions button').filter({ hasText: /^(buy|mua)$/i }).first();
    if ((await buy.count()) === 0) throw new Error('no Buy button in the merchant dialog');
    await buy.click({ timeout: 5000 });
    await page.waitForSelector('.shop-list', { timeout: 5000 });
    await wait(600);
    const measure = await page.evaluate(() => {
      const list = document.querySelector('.shop-list');
      const root = list.closest('.game-panel, [role="dialog"], .trade-window, .npc-box-wrap') || list.parentElement;
      const buttons = [...root.querySelectorAll('button')].map((b) => ({
        text: window.__qa.txt(b),
        rect: window.__qa.rect(b),
      }));
      const small = buttons.filter((b) => b.rect[3] < 42 && b.rect[2] > 0);
      const stepper = [...root.querySelectorAll('[class*="stepper"], [class*="shop-qty"]')].map((e) => ({ cls: window.__qa.cls(e), text: window.__qa.txt(e), rect: window.__qa.rect(e) }));
      const more = buttons.filter((b) => /\+|more|thêm|less|bớt|-|−/i.test(b.text));
      const clipped = [];
      for (const e of root.querySelectorAll('*')) {
        if (e.clientWidth > 0 && e.scrollWidth > e.clientWidth + 1) clipped.push({ cls: window.__qa.cls(e), text: window.__qa.txt(e), scrollWidth: e.scrollWidth, clientWidth: e.clientWidth });
      }
      return { rootRect: window.__qa.rect(root), buttonCount: buttons.length, buttons, smallTargets: small, stepper, moreControls: more, clipped: clipped.slice(0, 20) };
    });
    return { measure };
  }, { shot: { n: 9, name: 'shop-seed' } });

  // 10 NPC dialog box and its close controls (reopened if the shop closed it).
  await step('10 npc dialog', async () => {
    await page.keyboard.press('Escape');
    await wait(400);
    if (!(await page.$('.npc-box-wrap'))) await talkToSeedMerchant();
    if (!(await page.$('.npc-box-wrap'))) throw new Error('no NPC dialog box');
    const measure = await page.evaluate(() => {
      const wrap = document.querySelector('.npc-box-wrap');
      const buttons = [...wrap.querySelectorAll('button')].map((b) => ({ label: b.getAttribute('aria-label') || window.__qa.txt(b), rect: window.__qa.rect(b) }));
      // Close controls: an explicit close, or a leave/goodbye action (Tạm biệt = goodbye, the NPC's way out of the dialog).
      const closeLike = buttons.filter((b) => /close|đóng|×|✕|✖|leave|rời|tạm biệt|goodbye|bye/i.test(b.label));
      return { wrapRect: window.__qa.rect(wrap), buttonCount: buttons.length, buttons, closeControlCount: closeLike.length, closeControls: closeLike.map((b) => b.label) };
    });
    return { measure };
  }, { shot: { n: 10, name: 'npc-dialog' } });

  // 11 level-up banner (nosumQa.levelUp(skill, level): skill 1 = Farming, level 2).
  await step('11 levelup banner', async () => {
    await closeAll();
    await page.evaluate(() => window.nosumQa.levelUp(1, 2));
    await wait(400);
    const measure = await page.evaluate(() => {
      const banner = document.querySelector('.level-up-banner');
      const dock = document.querySelector('.furniture-dock');
      const hotbar = document.querySelector('.hotbar-dock, .hotbar-slots');
      return {
        bannerVisible: window.__qa.visible(banner),
        bannerRect: window.__qa.rect(banner),
        furnitureDockRect: window.__qa.rect(dock), // only present while the furniture window is open
        hotbarRect: window.__qa.rect(hotbar),
        hintDockRect: window.__qa.rect(document.querySelector('.hint-dock')),
      };
    });
    return { measure };
  }, { shot: { n: 11, name: 'levelup-banner' } });

  // 12 first-run hint toast and the touch stick (phone).
  await step('12 hint toast phone', async () => {
    await closeAll();
    const measure = await page.evaluate(() => {
      const hints = [...document.querySelectorAll('.game-field-hint, .hint-line, .game-message, .hint-dock')].filter(window.__qa.visible).map((e) => ({ cls: window.__qa.cls(e), text: window.__qa.txt(e), rect: window.__qa.rect(e) }));
      return { hints, touchStickRect: window.__qa.rect(document.querySelector('.touch-stick')) };
    });
    return { measure };
  }, { shot: { n: 12, name: 'hint-toast-phone' }, phoneOnly: true });

  // 13 a window open on the phone: the bottom of the HUD and what covers it.
  await step('13 open window bottom', async () => {
    await closeAll();
    await openMenu();
    const measure = await page.evaluate(() => {
      const hc = document.querySelector('.hud-center');
      const hotbar = document.querySelector('.hotbar-dock, .hotbar-slots');
      const touch = document.querySelector('.touch-controls');
      let coveredBy = null;
      if (hotbar) {
        const r = hotbar.getBoundingClientRect();
        const el = document.elementFromPoint(r.left + r.width / 2, r.top + Math.min(4, r.height / 2));
        coveredBy = el ? { cls: window.__qa.cls(el), insideHotbar: hotbar.contains(el) } : null;
      }
      return { hudCenterRect: window.__qa.rect(hc), hotbarRect: window.__qa.rect(hotbar), touchControlsRect: window.__qa.rect(touch), elementAtHotbarTop: coveredBy };
    });
    return { measure };
  }, { shot: { n: 13, name: 'open-window-bottom' } });

  // 14 co-op window (G): which panel opens.
  await step('14 coop G', async () => {
    await closeAll();
    await page.keyboard.press('KeyG');
    await wait(800);
    const measure = await page.evaluate(() => ({ panels: window.__qa.panels() }));
    return { measure };
  }, { shot: { n: 14, name: 'coop-G' } });

  // 15 quest tracker (top-left), with nothing open.
  await step('15 quest header', async () => {
    await closeAll();
    await wait(300);
    const measure = await page.evaluate(() => {
      const tl = document.querySelector('.hud-region.hud-top-left');
      return {
        topLeftRect: window.__qa.rect(tl),
        topLeftText: tl ? window.__qa.txt(tl) : null,
        questRect: window.__qa.rect(document.querySelector('.quest-wrap')),
        questTitle: window.__qa.txt(document.querySelector('.quest-title')),
      };
    });
    return { measure };
  }, { shot: { n: 15, name: 'quest-header' } });

  report.finishedAt = new Date().toISOString();
  report.errorCount = errors.length;
  fs.writeFileSync(path.join(OUT, `${loc}-${vp}.json`), JSON.stringify(report, null, 2));
  console.log(`${loc} ${vp}: ${report.steps.filter((s) => s.status === 'ok').length} ok, ${report.steps.filter((s) => s.status === 'skipped').length} skipped, ${errors.length} errors`);
  await context.close();
}

run().catch((e) => {
  console.error(e);
  process.exit(1);
});
