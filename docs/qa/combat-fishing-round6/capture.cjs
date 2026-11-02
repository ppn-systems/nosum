// Round-6 QA captures for the combat and fishing critique (docs/design/critique-combat-fishing.md, items 6 and N3).
// Tester build (-p:NosumDevTools=true) on a Development server, page ?qa=1, one fresh guest per run.
// usage: node capture.cjs <mode> <outDir> <w> <h> <locale: vi-VN|en-US>
// modes:
//   knockout  boot the mine, stand beside an ordinary monster without striking it, and wait for the monster to knock the farmer out.
//          Records every toast with its text (a repeated notice is merged into "×N") and captures the frame with the notice.
//   wait   grant a rod, teleport to the beach, walk the farmer down to the shore, cast, and capture the wait line
//          ("Waiting for a bite..." with the note that it takes a few seconds). Stops at the bite; the bite frame is kept too.
// Nothing here changes the game: it only plays it.
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright');

const [mode, outDir, w, h, locale = 'vi-VN'] = process.argv.slice(2);
const W = Number(w), H = Number(h), TOUCH = W < 500;
fs.mkdirSync(outDir, { recursive: true });

const lines = [];
const errors = [];
const results = {};
const say = (...a) => {
  const s = new Date().toISOString().slice(11, 23) + ' ' + a.map(x => (typeof x === 'string' ? x : JSON.stringify(x))).join(' ');
  lines.push(s);
  console.log(s);
};
const sleep = ms => new Promise(r => setTimeout(r, ms));
const shot = (page, name) => page.screenshot({ path: path.join(outDir, name + '.png') });

async function newGuest(browser) {
  const ctx = await browser.newContext({ viewport: { width: W, height: H }, locale, hasTouch: TOUCH, isMobile: TOUCH, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
  await page.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await sleep(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 120000 });
  await sleep(4000);
  return page;
}

// Walks the farmer down to the shore of the beach, then casts at the water below it.
async function wait(page) {
  await page.evaluate(() => window.nosumQa.grant(40, 1, 0));
  await page.evaluate(() => window.nosumQa.teleport('beach', 40, 42));
  await sleep(4500);
  await page.keyboard.press('9');
  await sleep(400);
  await page.keyboard.down('s');
  await sleep(1400);
  await page.keyboard.up('s');
  await sleep(600);
  await shot(page, 'wait-00-shore');
  // The water a few tiles below the farmer: the shoreline sits at about 0.66 H on the phone frame and 0.76 H on the desktop frames.
  // On touch the tap stays clear of the joystick (left) and the action buttons (right).
  const seaY = Math.round(H * (TOUCH ? 0.69 : 0.76));
  const seaX = Math.round(TOUCH ? W * 0.45 : W / 2);
  if (TOUCH) await page.touchscreen.tap(seaX, seaY);
  else await page.mouse.click(seaX, seaY);
  const body = () => page.evaluate(() => document.body.innerText.replace(/\s+/g, ' '));
  const BITE = /Cá đang cắn câu!|Something is biting!/;
  const WAIT = /Đang chờ cá|Waiting for a bite/;
  const t0 = Date.now();
  let waiting = false;
  while (Date.now() - t0 < 60000) {
    await sleep(100);
    const text = await body();
    if (!waiting && WAIT.test(text)) {
      waiting = true;
      await sleep(300);
      await shot(page, 'wait-01-waiting');
      const line = await page.evaluate(() => {
        const el = document.querySelector('.fishing-status');
        return el ? el.innerText.replace(/\s+/g, ' ').trim() : null;
      });
      results.waitLine = line;
      say('waiting line:', line);
    }
    if (BITE.test(text)) {
      await shot(page, 'wait-02-bite');
      results.bite = true;
      say('bite reached');
      return;
    }
  }
  results.bite = false;
  say('no bite within 60 s; waiting line seen:', waiting);
}

// Stands beside a mine monster and does not strike. The monsters strike on their own cycle; a knock-out shows the notice.
async function knockout(page) {
  await page.evaluate(() => window.nosumQa.grant(30, 1, 0));
  await page.evaluate(() => window.nosumQa.teleport('mine1', 12, 12));
  await sleep(4000);
  for (let i = 0; i < 2; i++) {
    await page.evaluate(() => window.nosumQa.teleportToMonster('mine1', 0, 1));
    await sleep(1500);
  }
  await page.keyboard.press('9');
  await sleep(300);
  const toasts = () => page.evaluate(() => [...document.querySelectorAll('.game-message')].map(el => el.innerText.replace(/\s+/g, ' ').trim()));
  const KO = /kiệt sức|knocked out/;
  const t0 = Date.now();
  const seen = [];
  while (Date.now() - t0 < 150000) {
    await sleep(150);
    const list = await toasts();
    const ko = list.find(t => KO.test(t));
    if (ko) {
      await sleep(200);
      seen.push(...(await toasts()));
      await shot(page, 'knockout-01-toast');
      results.toastsAtKnockout = await toasts();
      results.knockoutSeconds = Math.round((Date.now() - t0) / 1000);
      say('knock-out toast:', ko, 'after', results.knockoutSeconds, 's');
      await sleep(1200);
      results.toastsLater = await toasts();
      say('toasts after 1.2 s:', results.toastsLater);
      return;
    }
  }
  results.knockout = false;
  say('no knock-out within 150 s');
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  say('mode', mode, 'size', `${W}x${H}`, 'locale', locale, 'touch', TOUCH);
  const page = await newGuest(browser);
  if (mode === 'wait') await wait(page);
  else if (mode === 'knockout') await knockout(page);
  else throw new Error('unknown mode ' + mode);
  say('errors:', errors);
  fs.writeFileSync(path.join(outDir, 'result.json'), JSON.stringify({ mode, size: `${W}x${H}`, locale, ...results, errors }, null, 2));
  fs.writeFileSync(path.join(outDir, 'run.log'), lines.join('\n') + '\nerrors: ' + JSON.stringify(errors) + '\n');
  await browser.close();
})().catch(e => {
  console.error('FAILED', e.message);
  process.exitCode = 1;
});
