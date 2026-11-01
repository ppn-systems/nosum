// World ambience capture (docs/qa/rebuild-world-ambience). For one scenario (a calendar state the server is started in, see
// world-ambience-run.sh) it signs in as a guest on a Development server opened with ?qa=1 (ADR-079), walks the five maps the
// game has to show (farm, town, beach, forest, the first mine floor) and for each one:
//   - waits for the map's fade-in, then takes a screenshot of the whole view,
//   - logs every audio buffer the page starts during the visit (duration, looped or not): the sound events of that map, since
//     the sound itself cannot be heard from a headless browser. Music tracks are the long looped ones; the rain loop is a 2.75 s loop.
//   - records the calendar text the HUD shows (season, day, time) and the map id, so a capture can be graded against its state.
//
// usage: SCENARIO=noon-spring-sun OUT_DIR=docs/qa/rebuild-world-ambience/before node tests/browser/world-ambience-capture.cjs
// Env: NOSUM_QA_URL (default http://localhost:5080/?qa=1), LOCALES=en,vi (default en), VIEWPORTS=1280x720,390x844 (default both).

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/?qa=1';
const SCENARIO = process.env.SCENARIO || 'noon-spring-sun';
const OUT = path.resolve(process.env.OUT_DIR || path.join(REPO, 'docs/qa/rebuild-world-ambience/before'));
const LOCALES = { en: 'en-US', vi: 'vi-VN' };
const VIEWPORTS = {
  '1280x720': { width: 1280, height: 720, phone: false },
  '390x844': { width: 390, height: 844, phone: true },
};
const wantedLocales = (process.env.LOCALES || 'en').split(',').map((s) => s.trim()).filter(Boolean);
const wantedViewports = (process.env.VIEWPORTS || '1280x720,390x844').split(',').map((s) => s.trim()).filter(Boolean);

// Map id and a tile that the game's arrival rule moves to a walkable tile near (gameplay/Nosum.Gameplay/Maps; the town road and the beach row are from world-pass).
const MAPS = [
  { id: 'farm', tile: [46, 18] },
  { id: 'town', tile: [39, 2] },
  { id: 'beach', tile: [39, 43] },
  { id: 'forest', tile: [20, 20] },
  { id: 'mine1', tile: [5, 5] },
];

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function signIn(page) {
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 });
  await sleep(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 });
  await sleep(3000);
}

// Records every audio buffer the page starts (the sound events), before the page runs.
async function hookAudio(page) {
  await page.addInitScript(() => {
    window.__sounds = [];
    const start = AudioBufferSourceNode.prototype.start;
    AudioBufferSourceNode.prototype.start = function (...args) {
      window.__sounds.push({ at: Math.round(performance.now()), dur: Number(this.buffer ? this.buffer.duration.toFixed(2) : 0), loop: this.loop });
      return start.apply(this, args);
    };
  });
}

async function calendarText(page) {
  return page.$eval('.calendar-clock, .hud-top-right', (e) => (e.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 120)).catch(() => '');
}

async function runOne(browser, locale, vpName, report) {
  const vp = VIEWPORTS[vpName];
  const dir = path.join(OUT, locale, vpName);
  fs.mkdirSync(dir, { recursive: true });
  const context = await browser.newContext({
    viewport: { width: vp.width, height: vp.height },
    locale: LOCALES[locale],
    hasTouch: vp.phone,
    isMobile: vp.phone,
    serviceWorkers: 'block',
  });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text().slice(0, 200)}`); });
  await hookAudio(page);
  await signIn(page);

  for (const map of MAPS) {
    // The window opens before the teleport, so the door sound and the new map's music or loop start are in it. Loops that were already playing do not restart.
    const before = await page.evaluate(() => window.__sounds.length);
    await page.evaluate(([id, x, y]) => window.nosumQa.teleport(id, x, y), [map.id, map.tile[0], map.tile[1]]);
    await sleep(6000);
    const sounds = await page.evaluate((from) => window.__sounds.slice(from), before);
    const file = path.join(dir, `${SCENARIO}-${map.id}.png`);
    await page.screenshot({ path: file });
    report.push({ scenario: SCENARIO, locale, viewport: vpName, map: map.id, calendar: await calendarText(page), audioStartsInWindow: sounds.length, audioStarts: sounds.map((s) => `${s.loop ? 'loop' : 'once'} ${s.dur}s`), file: path.relative(REPO, file) });
  }

  if (errors.length) report.push({ scenario: SCENARIO, locale, viewport: vpName, errors: errors.slice(0, 10) });
  await context.close();
}

(async () => {
  const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
  const report = [];
  try {
    for (const locale of wantedLocales) {
      for (const vp of wantedViewports) {
        await runOne(browser, locale, vp, report);
      }
    }
  } finally {
    await browser.close();
  }
  fs.mkdirSync(OUT, { recursive: true });
  fs.writeFileSync(path.join(OUT, `report-${SCENARIO}-${wantedLocales.join("+")}.json`), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report.filter((r) => r.map), null, 1));
})();
