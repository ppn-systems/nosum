// Round-3 QA captures (docs/adr/ADR-092): the monster strike telegraph, the step-away check, the toast lift with the combo chip,
// the fishing bite prompt, and the touch Fireball. Tester build (-p:NosumDevTools=true) on a Development server, page ?qa=1.
// usage: node capture.cjs <mode> <outDir> <w> <h> <locale: vi-VN|en-US> [key=cycle] [attempts=3]
// modes:
//   telegraph   stand beside an ordinary monster, stand still; frames from 800 ms to 1650 ms after the first landing (the 400 ms windup
//               of the next strike sits at +1100..+1500 in the after build). The landings are also the stand-still control.
//   stepaway    stand beside, let the first hit land (control), then step one tile out (key) 150 ms into the next windup; the HP timeline
//               shows whether the strike landed. Repeats for each attempt.
//   toast       build a combo with Space, then Z twice (the second press is refused on cooldown); captures the toast and the combo chip together.
//   fishing     cast at the sea (tap on touch, click otherwise), wait for the bite, record the prompt text, press (tap or Space).
//   touchskill  Fireball with a monster in reach: the X key, then the hotbar Fireball button (tap on touch); records the box size.
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright');

const [mode, outDir, w, h, locale = 'vi-VN', keyArg = 'cycle', attemptsArg = '3'] = process.argv.slice(2);
const W = Number(w), H = Number(h), TOUCH = W < 500;
const SEA_Y = TOUCH ? 560 : 512;
const ATTEMPTS = Number(attemptsArg);
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

async function clock(page) {
  return page.evaluate(() => performance.now());
}
async function until(page, t) {
  while ((await clock(page)) < t) await sleep(5);
}
async function shot(page, name) {
  await page.screenshot({ path: path.join(outDir, name + '.png') });
}

// Opens the game as a new guest. The mine setup grants the sword (item 30) into slot 9; the fishing run skips it so the rod can take slot 9.
async function boot(page, mineSetup = true) {
  await page.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 120000 });
  await sleep(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 120000 });
  await sleep(4000);
  if (mineSetup) {
    await page.evaluate(() => window.nosumQa.grant(30, 1, 0));
    await page.evaluate(() => window.nosumQa.teleport('mine1', 12, 12));
    await sleep(4000);
  }
}

// Stands the farmer beside the nearest living monster and selects the sword (slot 9).
async function stand(page) {
  await page.evaluate(() => window.nosumQa.teleportToMonster('mine1'));
  await sleep(1800);
  await page.keyboard.press('9');
  await sleep(300);
}

// Records every change of the HP bar as [pageTimeMs, value]. The bar is found again on each mutation, so a re-render keeps the watch.
async function installHpWatch(page) {
  await page.evaluate(() => {
    if (window.__hpInstalled) return;
    window.__hpInstalled = true;
    window.__hp = [];
    let last = null;
    const push = () => {
      const bar = document.querySelector('.hp-bar');
      const v = bar ? Number(bar.getAttribute('aria-valuenow')) : null;
      if (v !== null && v !== last) {
        last = v;
        window.__hp.push([performance.now(), v]);
      }
    };
    push();
    new MutationObserver(push).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['aria-valuenow'] });
  });
}
async function hpSeries(page) {
  return page.evaluate(() => window.__hp.slice());
}
// The first HP drop after afterT: a landed strike. Returns { t, from, to } or null on timeout.
async function nextDrop(page, afterT, timeoutMs) {
  const t0 = Date.now();
  while (Date.now() - t0 < timeoutMs) {
    const ev = await hpSeries(page);
    for (let i = 1; i < ev.length; i++) {
      if (ev[i][0] > afterT && ev[i][1] < ev[i - 1][1]) {
        return { t: ev[i][0], from: ev[i - 1][1], to: ev[i][1] };
      }
    }
    await sleep(20);
  }
  return null;
}
function hpAt(series, t) {
  let v = null;
  for (const [et, ev] of series) {
    if (et <= t) v = ev;
  }
  return v;
}
function dropsAfter(series, t) {
  const out = [];
  for (let i = 1; i < series.length; i++) {
    if (series[i][0] > t && series[i][1] < series[i - 1][1]) {
      out.push({ at: Math.round(series[i][0]), from: series[i - 1][1], to: series[i][1] });
    }
  }
  return out;
}

async function telegraph(page) {
  await installHpWatch(page);
  await stand(page);
  const armed = await clock(page);
  const L1 = await nextDrop(page, armed, 40000);
  if (!L1) {
    say('no first landing within 40 s');
    results.error = 'no first landing';
    return;
  }
  say('landing 1 at', Math.round(L1.t), 'hp', L1.from, '->', L1.to);
  await until(page, L1.t + 800);
  const offsets = [];
  let i = 0;
  for (;;) {
    const t = await clock(page);
    if (t > L1.t + 1650) break;
    const off = Math.round(t - L1.t);
    await shot(page, `tg-${String(i).padStart(2, '0')}-at${off}ms`);
    offsets.push(off);
    i++;
  }
  const L2 = await nextDrop(page, L1.t + 300, 4000);
  const series = await hpSeries(page);
  results.landings = dropsAfter(series, L1.t - 1).map(d => ({ atRelMs: Math.round(d.at - L1.t), from: d.from, to: d.to }));
  results.frameOffsetsMs = offsets;
  say('frames', offsets.length, 'offsets', offsets);
  say('landings (ms after landing 1):', results.landings);
  if (!L2) say('no second landing within 4 s after landing 1');
}

// One attempt in a fresh guest session (full HP). The monster's side is not visible from the DOM (the world is drawn on the canvas), so the
// attempts cycle through the four arrow keys. The control is inside the attempt: the farmer stands still until the second strike lands
// (L2), which shows the monster is adjacent and striking on its 1.5 s cadence. The farmer then steps out 50 ms into the windup of the
// third strike (L2 + 1100 .. L2 + 1500); if that strike does not land, the step got out of reach. An attempt with no second landing is void.
async function stepawayOne(page, key, a) {
  const tag = key.replace('Arrow', '').toLowerCase();
  await installHpWatch(page);
  await stand(page);
  const armed = await clock(page);
  const L1 = await nextDrop(page, armed, 40000);
  if (!L1) return { attempt: a, key, void: true, error: 'no first landing' };
  const L2 = await nextDrop(page, L1.t + 300, 3000);
  if (!L2) return { attempt: a, key, void: true, error: 'no second landing while standing still (control)', landing1: L1.to };
  // Third strike: windup at L2 + 1100, lands at L2 + 1500. Step out 50 ms into the windup, held 260 ms (walk 80 px/s; a 16 px tile is about 200 ms).
  await until(page, L2.t + 1000);
  await shot(page, `sa-a${a}-${tag}-1-before-step`);
  await until(page, L2.t + 1150);
  const pressAt = await clock(page);
  await page.keyboard.down(key);
  await sleep(260);
  await page.keyboard.up(key);
  await until(page, L2.t + 1420);
  await shot(page, `sa-a${a}-${tag}-2-after-step-in-windup`);
  await until(page, L2.t + 2300);
  const series = await hpSeries(page);
  // Conservative window: any drop from 1000 ms after landing 2 counts, including a second monster's strike that could land while the farmer
  // is still in reach.
  const later = dropsAfter(series, L2.t + 1000);
  const every = dropsAfter(series, L1.t - 1);
  return {
    attempt: a,
    key,
    control: {
      landing1: { hpBefore: L1.from, hpAfter: L1.to },
      landing2: { hpBefore: L2.from, hpAfter: L2.to, gapMsFromLanding1: Math.round(L2.t - L1.t) },
    },
    pressOffsetMsFromLanding2: Math.round(pressAt - L2.t),
    hpBeforeStep: L2.to,
    hpEnd: series[series.length - 1][1],
    allDropsRelToLanding2: every.map(d => ({ atRelMs: Math.round(d.at - L2.t), from: d.from, to: d.to })),
    hitsFromPlus1000: later.length,
    success: later.length === 0,
  };
}

async function stepaway(browser) {
  const KEYS = ['ArrowUp', 'ArrowLeft', 'ArrowDown', 'ArrowRight'];
  results.attempts = [];
  for (let a = 1; a <= ATTEMPTS; a++) {
    const key = keyArg === 'cycle' ? KEYS[(a - 1) % 4] : keyArg;
    const { page, close } = await newGuestPage(browser);
    await boot(page);
    const row = await stepawayOne(page, key, a);
    results.attempts.push(row);
    say('attempt', a, row);
    await close();
    if (row.success) break;
  }
  const ok = results.attempts.filter(r => r.success).length;
  results.voidAttempts = results.attempts.filter(r => r.void).length;
  results.successfulAttempts = ok;
  results.attemptsUsed = results.attempts.length;
  say('step-away: attempts used', results.attempts.length, 'successful', ok);
}

async function newGuestPage(browser) {
  const ctx = await browser.newContext({ viewport: { width: W, height: H }, locale, hasTouch: TOUCH, isMobile: TOUCH });
  const page = await ctx.newPage();
  page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text().slice(0, 240)); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message.slice(0, 240)));
  return { page, close: () => ctx.close() };
}

async function toast(page) {
  results.tries = [];
  for (let tr = 1; tr <= ATTEMPTS; tr++) {
    await stand(page);
    for (let i = 0; i < 6; i++) {
      await page.keyboard.press('Space');
      await sleep(350);
    }
    await page.keyboard.press('z');
    await sleep(120);
    await page.keyboard.press('z');
    await sleep(200);
    const data = await page.evaluate(() => {
      const box = el => {
        if (!el) return null;
        const b = el.getBoundingClientRect();
        return [Math.round(b.x), Math.round(b.y), Math.round(b.width), Math.round(b.height)];
      };
      const toasts = [...document.querySelectorAll('.game-message')].map(el => ({ text: el.innerText.replace(/\s+/g, ' ').trim(), box: box(el) }));
      const chip = document.querySelector('.hotbar-badges');
      return { toasts, chip: chip ? { text: chip.innerText.replace(/\s+/g, ' ').trim(), box: box(chip) } : null };
    });
    const overlap = (a, b) => {
      if (!a || !b) return null;
      const x = Math.max(0, Math.min(a[0] + a[2], b[0] + b[2]) - Math.max(a[0], b[0]));
      const y = Math.max(0, Math.min(a[1] + a[3], b[1] + b[3]) - Math.max(a[1], b[1]));
      return x * y > 0 ? { px: [x, y] } : false;
    };
    const toastBox = data.toasts.length ? data.toasts[data.toasts.length - 1].box : null;
    const row = { try: tr, toasts: data.toasts, chip: data.chip, overlapToastAndChip: overlap(toastBox, data.chip && data.chip.box) };
    results.tries.push(row);
    say('try', tr, row);
    await shot(page, `toast-with-chip-try${tr}`);
    if (data.chip && data.toasts.length) {
      results.final = row;
      return;
    }
    await sleep(3200);
  }
}

async function fishing(page) {
  await page.evaluate(() => window.nosumQa.grant(40, 1, 0));
  await page.evaluate(() => window.nosumQa.teleport('beach', 40, 42));
  await sleep(4500);
  await page.keyboard.press('9');
  await sleep(400);
  await shot(page, 'fish-01-beach-rod');
  if (TOUCH) await page.touchscreen.tap(Math.round(W / 2), SEA_Y);
  else await page.mouse.click(Math.round(W / 2), SEA_Y);
  const BITE = /Cá đang cắn câu!|Something is biting!/;
  const status = () => page.evaluate(() => {
    const el = document.querySelector('.fishing-status');
    return el ? el.innerText.replace(/\s+/g, ' ').trim() : null;
  });
  const t0 = Date.now();
  let waiting = false, bite = false;
  while (Date.now() - t0 < 60000 && !bite) {
    await sleep(100);
    const body = await page.evaluate(() => document.body.innerText.replace(/\s+/g, ' '));
    if (!waiting && /Đang chờ cá|Waiting for a bite/.test(body)) {
      waiting = true;
      await shot(page, 'fish-01b-waiting');
      say('waiting status:', await status());
    }
    if (BITE.test(body)) bite = true;
  }
  if (!bite) {
    say('no bite within 60 s');
    results.bite = null;
    return;
  }
  await sleep(120);
  results.bite = { statusAtBite: await status(), inputMode: TOUCH ? 'touch' : 'keyboard' };
  await shot(page, 'fish-02-bite');
  say('bite; prompt text:', results.bite.statusAtBite);
  if (TOUCH) await page.touchscreen.tap(Math.round(W / 2), SEA_Y);
  else await page.keyboard.press('Space');
  await sleep(300);
  await shot(page, 'fish-03-reeling');
  await sleep(2000);
  results.afterPress = await status();
  await shot(page, 'fish-04-result');
  say('after press:', results.afterPress);
}

async function touchskill(page) {
  await stand(page);
  const btn = page.getByRole('button', { name: /Cầu lửa|Fireball/ }).first();
  const bb = await btn.boundingBox();
  results.fireballButtonBox = bb ? [Math.round(bb.x), Math.round(bb.y), Math.round(bb.width), Math.round(bb.height)] : null;
  say('fireball button box (css px, x y w h):', results.fireballButtonBox);
  const probe = async tag => {
    await sleep(200);
    await shot(page, `ts-${tag}-200ms`);
    await sleep(1300);
    await shot(page, `ts-${tag}-1500ms`);
    return page.evaluate(() => ({
      cooldownOverlays: document.querySelectorAll('.skill-cooldown').length,
      toasts: [...document.querySelectorAll('.game-message')].map(el => el.innerText.replace(/\s+/g, ' ').trim()),
    }));
  };
  await shot(page, 'ts-00-before');
  await page.keyboard.press('x');
  results.xKey = await probe('x-key');
  say('X key result:', results.xKey);
  // Wait for the cooldown overlay to clear, then tap the hotbar Fireball button.
  const t0 = Date.now();
  while (Date.now() - t0 < 30000) {
    const n = await page.evaluate(() => document.querySelectorAll('.skill-cooldown').length);
    if (n === 0) break;
    await sleep(250);
  }
  const bb2 = await btn.boundingBox();
  await page.touchscreen.tap(Math.round(bb2.x + bb2.width / 2), Math.round(bb2.y + bb2.height / 2));
  results.buttonTap = await probe('button-tap');
  say('button tap result:', results.buttonTap);
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  say('mode', mode, 'size', `${W}x${H}`, 'locale', locale, 'touch', TOUCH, 'key', keyArg, 'attempts', ATTEMPTS);
  if (mode === 'stepaway') {
    await stepaway(browser);
  } else {
    const { page } = await newGuestPage(browser);
    await boot(page, mode !== 'fishing');
    if (mode === 'telegraph') await telegraph(page);
    else if (mode === 'toast') await toast(page);
    else if (mode === 'fishing') await fishing(page);
    else if (mode === 'touchskill') await touchskill(page);
    else throw new Error('unknown mode ' + mode);
  }
  say('errors:', errors);
  fs.writeFileSync(path.join(outDir, 'result.json'), JSON.stringify({ mode, size: `${W}x${H}`, locale, ...results, errors }, null, 2));
  fs.writeFileSync(path.join(outDir, 'run.log'), lines.join('\n') + '\nerrors: ' + JSON.stringify(errors) + '\n');
  await browser.close();
})().catch(e => {
  console.error('FAILED', e.message);
  process.exitCode = 1;
});
