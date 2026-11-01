// The round 3 check of the quest waypoint, the merchant's quest greeting, the name plate lift and the hint's idle wait (docs/adr/ADR-096,
// amending ADR-093; docs/design/critique-npc-hud.md). Vietnamese and English, at 1280x720, 1920x1080 and 390x844 (an iPhone 14 profile).
// Needs the Development host on PostgreSQL (role nosum, database nosum_dev) and a client published with -p:NosumDevTools=true, served at the base URL.
// The quest stage is set in the database (the tester's shortcut, as the browser acceptance runs do): the server reads quests at sign-in, so each stage
// signs the same guest in again after the change.
// Writes <outDir>/<name>-<lang>-<WxH>.png and <outDir>/round3-<WxH>.json (the metrics read back from the canvas and the DOM, and the console errors).
// Run: NODE_PATH=$(npm root -g) node tests/browser/npc-hud-round3-capture.cjs <outDir> [base url] [WxH, default 1280x720]
const { chromium, devices } = require('playwright');
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const outDir = process.argv[2];
const base = process.argv[3] || 'http://localhost:5080/';
const size = process.argv[4] || '1280x720';
if (!outDir) {
  throw new Error('Usage: node npc-hud-round3-capture.cjs <outDir> [base url] [WxH]');
}
fs.mkdirSync(outDir, { recursive: true });
const [width, height] = size.split('x').map(Number);
const phone = width <= 430;

// Quest ids (QuestCatalog): 1 first harvest, 2 till, 3 water, 4 ship, 5 buy seeds, 6 chop tree, 7 chest, 8 rod, 9 catch fish, 10 mine ore.
const STAGES = {
  // Buy seeds is the active quest: its waypoint is the seed keeper's counter in Town.
  seeds: { done: [1, 2, 3, 4], active: 5 },
  // Chop a tree is the active quest: its waypoint is the tree at the Forest's entrance.
  forest: { done: [1, 2, 3, 4, 5], active: 6 },
  // Mine ore is the active quest: its waypoint is the cave shaft down to the ore.
  mine: { done: [1, 2, 3, 4, 5, 6, 7, 8, 9], active: 10 },
};

// The seed keeper's counter (Town) and the spots used by the checks; each is a tile.
const SEED_COUNTER = [43, 57];
const TOWN_FAR = [10, 30];
const FOREST_FAR = [20, 60];
const MINE_FAR = [60, 15];
const FARM_DOOR_FRONT = [64, 16];

const GUEST = { vi: /Chơi ngay với tư cách khách/, en: /Play now as a guest/ };
const LANG = {
  vi: { label: 'vi', button: 'Tiếng Việt' },
  en: { label: 'en', button: 'English' },
};
const QUEST_GREETING = {
  vi: 'Đang định trồng gì? Hạt giống mua ngay trên quầy này.',
  en: 'Planning what to grow? The seeds are right here on this counter.',
};

function sql(query) {
  return execFileSync('su', ['postgres', '-c', `psql -d nosum_dev -tAc "${query.replace(/"/g, '\\"')}"`], { encoding: 'utf8' }).trim();
}

function setStage(playerId, stage) {
  const { done, active } = STAGES[stage];
  const rows = [
    ...done.map((id) => `('${playerId}', ${id}, 2, '{0}', now(), now())`),
    `('${playerId}', ${active}, 1, '{0}', now(), NULL)`,
  ];
  sql(`DELETE FROM player_quests WHERE player_id = '${playerId}'; INSERT INTO player_quests (player_id, quest_id, state, progress, started_at, completed_at) VALUES ${rows.join(', ')};`);
}

const log = { size, shots: [], metrics: {}, errors: [] };

async function shot(page, name, lang) {
  const file = path.join(outDir, `${name}-${lang}-${size}.png`);
  await page.screenshot({ path: file });
  log.shots.push(path.basename(file));
}

// Enters the game as the guest (first time) or continues the remembered guest (after a stage change).
async function enter(page, first) {
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  const pattern = first ? new RegExp(`${GUEST.vi.source}|${GUEST.en.source}`) : /Continue as|Tiếp tục/;
  await page.getByRole('button', { name: pattern }).first().click({ timeout: 120000 });
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForFunction(() => !!globalThis.nosumQa, null, { timeout: 60000 });
  await page.waitForTimeout(3000);
}

// The waypoint on the canvas: the gold arrow and the green minimap dot, and whether the arrow touches a HUD element (CSS pixels).
async function waypointMetrics(page) {
  return page.evaluate(() => {
    const GOLD = [245, 197, 66];
    const GREEN = [74, 222, 128];
    const near = (d, c) => Math.abs(d[0] - c[0]) <= 8 && Math.abs(d[1] - c[1]) <= 8 && Math.abs(d[2] - c[2]) <= 8;
    const main = document.querySelector('canvas');
    const rect = main.getBoundingClientRect();
    const sx = main.width / rect.width;
    const sy = main.height / rect.height;
    const data = main.getContext('2d').getImageData(0, 0, main.width, main.height).data;
    let gold = 0, minX = Infinity, minY = Infinity, maxX = -1, maxY = -1, greenMain = 0;
    const minimap = document.querySelector('.hud-minimap');
    const mm = minimap ? minimap.getBoundingClientRect() : null;
    for (let y = 0; y < main.height; y++) {
      for (let x = 0; x < main.width; x++) {
        const i = (y * main.width + x) * 4;
        const px = [data[i], data[i + 1], data[i + 2]];
        if (near(px, GOLD)) {
          gold++;
          if (x < minX) minX = x;
          if (y < minY) minY = y;
          if (x > maxX) maxX = x;
          if (y > maxY) maxY = y;
        }
        if (mm && near(px, GREEN)) {
          const cx = x / sx + rect.left, cy = y / sy + rect.top;
          if (cx >= mm.left && cx <= mm.right && cy >= mm.top && cy <= mm.bottom) greenMain++;
        }
      }
    }

    const arrow = gold > 0
      ? { left: rect.left + minX / sx, top: rect.top + minY / sy, right: rect.left + (maxX + 1) / sx, bottom: rect.top + (maxY + 1) / sy }
      : null;
    const overlaps = [];
    if (arrow) {
      const hud = document.querySelectorAll('.game-hud *');
      for (const el of hud) {
        if (el.classList.contains('hud-row') || el.classList.contains('hud-region') || el.classList.contains('hud-backdrop') || el.classList.contains('hud-overlay')) continue;
        const r = el.getBoundingClientRect();
        if (r.width <= 0 || r.height <= 0) continue;
        if (r.left < arrow.right && r.right > arrow.left && r.top < arrow.bottom && r.bottom > arrow.top) {
          overlaps.push(`${el.tagName.toLowerCase()}.${String(el.className.baseVal ?? el.className).split(' ').join('.')}`);
        }
      }
    }

    return { goldPixels: gold, arrowBox: arrow, arrowOverlapsHud: overlaps.slice(0, 12), greenInMinimap: greenMain, minimapRect: mm ? [mm.left, mm.top, mm.width, mm.height] : null };
  });
}

// Holds an arrow key for a while, then measures how long the hint takes to come up after the player stops.
async function hintIdleWait(page) {
  await page.keyboard.down('ArrowRight');
  await page.waitForTimeout(3000);
  const duringHold = await page.locator('.hint-line').count();
  await page.keyboard.up('ArrowRight');
  const released = Date.now();
  let appeared = null;
  try {
    await page.waitForSelector('.hint-line', { timeout: 12000 });
    appeared = Date.now() - released;
  } catch {
    appeared = null;
  }

  return { hintOnScreenDuringHold: duringHold > 0, msFromReleaseToHint: appeared };
}

// Measures the zero-allocation frame: bytes allocated by the page over a few seconds of held input (ADR-096 D4).
async function allocationPerSecond(page) {
  const sample = async (hold) => {
    const before = await page.evaluate(() => globalThis.nosumQa.allocatedBytes());
    if (hold) await page.keyboard.down('ArrowLeft');
    await page.waitForTimeout(3000);
    if (hold) await page.keyboard.up('ArrowLeft');
    const after = await page.evaluate(() => globalThis.nosumQa.allocatedBytes());
    return Math.round((Number(after) - Number(before)) / 3);
  };
  return { idleBytesPerSecond: await sample(false), heldBytesPerSecond: await sample(true) };
}

async function teleport(page, mapId, tile) {
  await page.evaluate(([map, x, y]) => globalThis.nosumQa.teleport(map, x, y), [mapId, tile[0], tile[1]]);
  await page.waitForTimeout(1500);
}

async function greeting(page, lang) {
  await teleport(page, 'town', SEED_COUNTER);
  await page.keyboard.press('KeyE');
  await page.waitForTimeout(3500);
  const text = await page.evaluate(() => document.body.innerText);
  log.metrics[`greeting-${lang}`] = { expected: QUEST_GREETING[lang], shown: text.includes(QUEST_GREETING[lang]) };
  await shot(page, 'greeting-seed-keeper', lang);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(800);
}

async function switchLanguage(page, lang) {
  await page.keyboard.press('KeyH');
  await page.waitForTimeout(600);
  await page.getByRole('button', { name: LANG[lang].button }).first().click();
  await page.waitForTimeout(800);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(800);
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const options = phone
    ? { ...devices['iPhone 14'], viewport: { width, height }, locale: 'vi-VN' }
    : { viewport: { width, height }, locale: 'vi-VN' };
  const context = await browser.newContext(options);
  const page = await context.newPage();
  page.on('pageerror', (e) => log.errors.push('pageerror: ' + e.message));
  page.on('console', (m) => { if (m.type() === 'error') log.errors.push(m.text()); });

  // The first stage: a new guest, its quest set to buy seeds.
  await enter(page, true);
  const playerId = sql("SELECT id FROM players WHERE is_guest ORDER BY created_at DESC LIMIT 1");
  log.playerId = playerId;
  setStage(playerId, 'seeds');
  await page.reload({ waitUntil: 'load' });
  await enter(page, false);

  for (const lang of ['vi', 'en']) {
    if (lang === 'en') {
      await switchLanguage(page, 'en');
    }

    // 1. Edge arrow and minimap dot: the seed keeper's counter is off screen in Town.
    await teleport(page, 'town', TOWN_FAR);
    log.metrics[`seeds-arrow-${lang}`] = await waypointMetrics(page);
    await shot(page, 'arrow-town', lang);

    // 2. The merchant's greeting names the quest.
    await greeting(page, lang);

    // 3. The hint waits for two seconds of idle; the allocation check runs on the same farm moment.
    await teleport(page, 'town', TOWN_FAR);
    log.metrics[`hint-${lang}`] = await hintIdleWait(page);
    log.metrics[`alloc-${lang}`] = await allocationPerSecond(page);
  }

  // 4. The forest stage: the tree's waypoint.
  await switchLanguage(page, 'vi');
  setStage(playerId, 'forest');
  await page.reload({ waitUntil: 'load' });
  await enter(page, false);
  for (const lang of ['vi', 'en']) {
    if (lang === 'en') {
      await switchLanguage(page, 'en');
    }

    await teleport(page, 'forest', FOREST_FAR);
    log.metrics[`forest-arrow-${lang}`] = await waypointMetrics(page);
    await shot(page, 'arrow-forest', lang);
  }

  // 5. The mine stage: the cave shaft's waypoint, and the name plate beside the farm's door (on the farm).
  await switchLanguage(page, 'vi');
  setStage(playerId, 'mine');
  await page.reload({ waitUntil: 'load' });
  await enter(page, false);
  for (const lang of ['vi', 'en']) {
    if (lang === 'en') {
      await switchLanguage(page, 'en');
    }

    await teleport(page, 'mine', MINE_FAR);
    log.metrics[`mine-arrow-${lang}`] = await waypointMetrics(page);
    await shot(page, 'arrow-mine', lang);

    await teleport(page, 'farm', FARM_DOOR_FRONT);
    log.metrics[`door-${lang}`] = await waypointMetrics(page);
    await shot(page, 'name-lift-farm-door', lang);
  }

  log.metrics.errors = log.errors;
  fs.writeFileSync(path.join(outDir, `round3-${size}.json`), JSON.stringify(log, null, 2));
  await browser.close();
  console.log(JSON.stringify({ size, shots: log.shots.length, errors: log.errors.length }));
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
