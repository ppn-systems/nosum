// The round 3 check of the merchant's talk with the quest at its counter (docs/adr/ADR-113, amending ADR-093 D3): the seed keeper's greeting,
// then Talk, then Talk again. The greeting and the second step name the active quest (buy seeds); the first Talk is a pool line.
// Vietnamese and English, at 1280x720, 1920x1080 and 390x844 (an iPhone 14 profile). Each frame is written as talk-<step>-<lang>-<WxH>.png,
// and <outDir>/talk-<WxH>.json holds the line each step showed and whether the quest line was there.
// Needs the Development host on PostgreSQL (role nosum, database nosum_dev) and a client published with -p:NosumDevTools=true, served at the base URL.
// The quest stage is set in the database (the tester's shortcut, as npc-hud-round3-capture.cjs does).
// Run: NODE_PATH=$(npm root -g) node tests/browser/npc-talk-quest-capture.cjs <outDir> [base url] [WxH, default 1280x720]
const { chromium, devices } = require('playwright');
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const outDir = process.argv[2];
const base = process.argv[3] || 'http://localhost:5080/';
const size = process.argv[4] || '1280x720';
if (!outDir) {
  throw new Error('Usage: node npc-talk-quest-capture.cjs <outDir> [base url] [WxH]');
}
fs.mkdirSync(outDir, { recursive: true });
const [width, height] = size.split('x').map(Number);
const phone = width <= 430;

// The seed keeper's counter in Town, and the quest's line (Npc_seed_keeper_Quest_buy_seeds) in each language.
const SEED_COUNTER = [43, 57];
const QUEST_LINE = {
  vi: 'Đang định trồng gì? Hạt giống mua ngay trên quầy này.',
  en: 'Planning what to grow? The seeds are right here on this counter.',
};
const TALK = { vi: 'Trò chuyện', en: 'Talk' };
const GUEST = { vi: /Chơi ngay với tư cách khách/, en: /Play now as a guest/ };
const LANG = { vi: { switch: null }, en: { switch: 'English' } };
// Quest ids (QuestCatalog): 1 first harvest, 2 till, 3 water, 4 ship, 5 buy seeds.
const DONE_BEFORE_BUY_SEEDS = [1, 2, 3, 4];
const BUY_SEEDS = 5;

const log = { size, shots: [], steps: {}, errors: [] };

function sql(query) {
  return execFileSync('su', ['postgres', '-c', `psql -d nosum_dev -tAc "${query.replace(/"/g, '\\"')}"`], { encoding: 'utf8' }).trim();
}

function setBuySeedsActive(playerId) {
  const rows = [
    ...DONE_BEFORE_BUY_SEEDS.map((id) => `('${playerId}', ${id}, 2, '{0}', now(), now())`),
    `('${playerId}', ${BUY_SEEDS}, 1, '{0}', now(), NULL)`,
  ];
  sql(`DELETE FROM player_quests WHERE player_id = '${playerId}'; INSERT INTO player_quests (player_id, quest_id, state, progress, started_at, completed_at) VALUES ${rows.join(', ')};`);
}

async function enter(page, first) {
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  const pattern = first ? new RegExp(`${GUEST.vi.source}|${GUEST.en.source}`) : /Continue as|Tiếp tục/;
  await page.getByRole('button', { name: pattern }).first().click({ timeout: 120000 });
  await page.waitForSelector('canvas', { timeout: 120000 });
  await page.waitForFunction(() => !!globalThis.nosumQa, null, { timeout: 60000 });
  await page.waitForTimeout(3000);
}

async function shot(page, name, lang) {
  const file = path.join(outDir, `${name}-${lang}-${size}.png`);
  await page.screenshot({ path: file });
  log.shots.push(path.basename(file));
}

// The dialog box's text (the merchant's name, role and line), read back from the DOM.
async function boxText(page) {
  return page.evaluate(() => document.body.innerText);
}

// Waits for the typed line to finish, then presses the Talk button and waits for the next line to type out.
async function talk(page, lang) {
  await page.getByRole('button', { name: TALK[lang], exact: true }).first().waitFor({ timeout: 15000 });
  await page.getByRole('button', { name: TALK[lang], exact: true }).first().click();
  await page.waitForTimeout(4000);
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

  await enter(page, true);
  const playerId = sql('SELECT id FROM players WHERE is_guest ORDER BY created_at DESC LIMIT 1');
  setBuySeedsActive(playerId);
  await page.reload({ waitUntil: 'load' });
  await enter(page, false);

  for (const lang of ['vi', 'en']) {
    if (lang === 'en') {
      await page.keyboard.press('KeyH');
      await page.waitForTimeout(600);
      await page.getByRole('button', { name: LANG.en.switch }).first().click();
      await page.waitForTimeout(800);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(800);
    }

    await page.evaluate(([x, y]) => globalThis.nosumQa.teleport('town', x, y), SEED_COUNTER);
    await page.waitForTimeout(1500);
    await page.keyboard.press('KeyE');
    await page.waitForTimeout(3500);
    const greeting = await boxText(page);
    log.steps[`greeting-${lang}`] = { questLine: greeting.includes(QUEST_LINE[lang]) };
    await shot(page, 'talk-greeting', lang);

    await talk(page, lang);
    const first = await boxText(page);
    log.steps[`talk1-${lang}`] = { questLine: first.includes(QUEST_LINE[lang]) };
    await shot(page, 'talk-1', lang);

    await talk(page, lang);
    const second = await boxText(page);
    log.steps[`talk2-${lang}`] = { questLine: second.includes(QUEST_LINE[lang]) };
    await shot(page, 'talk-2', lang);

    await page.keyboard.press('Escape');
    await page.waitForTimeout(800);
  }

  fs.writeFileSync(path.join(outDir, `talk-${size}.json`), JSON.stringify(log, null, 2));
  await browser.close();
  console.log(JSON.stringify({ size, shots: log.shots.length, steps: log.steps, errors: log.errors.length }));
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
