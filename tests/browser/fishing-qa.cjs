// QA walk of the fishing feature in the running game (docs/adr/ADR-071): sign in as a guest, gather 5 Wood and 4 Fiber, craft the Fishing rod
// from the crafting panel, hold it in the hotbar, cast at the water next to the Woods pond, press at the bite, and keep casting until a fish is
// caught. Screenshots at 1280x720: fishing-cast.png (the line is out, the banner shows Waiting), fishing-catch.png (the result of a catch).
// Any step that does not work writes fishing-failure-<step>.png and exits with code 1. Nothing here changes the game: it only plays it.
// Run: node tests/browser/fishing-qa.cjs [base url, default http://localhost:5000/]
// Needs Playwright with Chromium installed (no `playwright install`). Gathering is random (the scythe's fiber chance), so the script retries
// the sweep until the fiber is there; the walking route assumes the starter spawn of a new guest on the Farm.
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright');

const base = process.argv[2] || 'http://localhost:5000/';
const outDir = path.resolve(__dirname, '../../docs/qa/screens');
const errors = [];
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

async function shot(page, name) {
  await page.screenshot({ path: path.join(outDir, name) });
}

async function fail(page, step, detail) {
  log('FAILED at', step, detail || '');
  await shot(page, `fishing-failure-${step}.png`).catch(() => {});
  const banner = await page.locator('.fishing-banner').innerText().catch(() => '(no banner)');
  log('banner:', banner.replace(/\s+/g, ' '));
  log('console errors:', errors.slice(0, 8));
  process.exitCode = 1;
  throw new Error(step);
}

async function press(page, steps) {
  for (const [key, ms] of steps) {
    if (ms) {
      await page.keyboard.down(key);
      await page.waitForTimeout(ms);
      await page.keyboard.up(key);
    } else {
      await page.keyboard.press(key);
    }
    await page.waitForTimeout(120);
  }
}

async function craftNeeds(page) {
  await page.locator('.game-icon-button[aria-label*="Chế tạo"]').click();
  await page.waitForTimeout(700);
  await page.mouse.click(468, 290); // the Fishing rod cell in the recipe grid
  await page.waitForTimeout(400);
  const text = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
  return {
    wood: Number((/Gỗ (\d+) \/ 5/.exec(text) || [0, -1])[1]),
    fiber: Number((/Sợi (\d+) \/ 4/.exec(text) || [0, -1])[1]),
    selected: text.includes('Cần câu'),
  };
}

// Walks to the tree below the spawn path and chops it with the axe (slot 6) until the craft panel reports enough wood.
async function gatherWood(page) {
  await press(page, [['KeyS', 2400], ['KeyA', 2200], ['Digit6'], ['KeyS', 1500]]);
  await page.waitForTimeout(300);
  for (let i = 0; i < 12; i++) {
    await press(page, [['KeyE']]);
    await page.waitForTimeout(800);
  }
  const needs = await craftNeeds(page);
  log('after chopping:', JSON.stringify(needs));
  return needs;
}

// Sweeps the scythe (slot 8) over the grass in a snake until the craft panel reports 4 fiber. Each row ends with a step south.
async function gatherFiber(page) {
  await press(page, [['Digit8']]);
  for (let batch = 0; batch < 10; batch++) {
    const needs = await craftNeeds(page);
    if (needs.fiber >= 4) {
      return needs;
    }
    // A new patch of grass: the sweep walks east first when the last one found little.
    await press(page, [['KeyD', 1000], ['KeyS', 500]]);
    // A random walk with the scythe: a step, then a swing at the tile ahead. A snake sweep stalls at the first tree in its way.
    const dirs = ['KeyW', 'KeyA', 'KeyS', 'KeyD'];
    for (let step = 0; step < 160; step++) {
      await press(page, [['KeyE']]);
      await page.waitForTimeout(450);
      await press(page, [[dirs[Math.floor(Math.random() * dirs.length)], 220]]);
      if (step % 40 === 39 && (await craftNeeds(page)).fiber >= 4) {
        break;
      }
    }
    log('after sweep', batch, JSON.stringify(await craftNeeds(page)));
  }
  return craftNeeds(page);
}

// Crafts the Fishing rod, moves it into the hotbar (the bag's bottom row is the hotbar: a click picks an item up, a second click moves it)
// and selects it. Returns when the bag shows the rod held.
async function craftAndEquipRod(page) {
  await page.locator('.game-icon-button[aria-label*="Chế tạo"]').click();
  await page.waitForTimeout(700);
  await page.mouse.click(468, 290);
  await page.waitForTimeout(400);
  const craft = page.locator('button:not(.game-icon-button)', { hasText: /^Chế tạo$/ });
  if (await craft.count() === 0) {
    await fail(page, 'craft-button', 'the craft button is not on the panel');
  }
  await craft.first().click();
  await page.waitForTimeout(800);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);

  await page.locator('.game-icon-button[aria-label*="Túi đồ"]').click();
  await page.waitForTimeout(700);
  const cells = await page.evaluate(() => [...document.querySelectorAll('.trade-window [data-tip]')].map(e => {
    const r = e.getBoundingClientRect();
    return { tip: e.getAttribute('data-tip') || '', x: Math.round(r.x + r.width / 2), y: Math.round(r.y + r.height / 2) };
  }));
  const rod = cells.find(c => c.tip.startsWith('Cần câu'));
  const hotbar9 = cells.filter(c => c.y > 590 && c.y < 640).sort((a, b) => b.x - a.x)[0];
  if (!rod || !hotbar9) {
    await fail(page, 'bag-cells', `rod=${JSON.stringify(rod)} hotbar9=${JSON.stringify(hotbar9)}`);
  }
  await page.mouse.click(rod.x, rod.y);
  await page.waitForTimeout(300);
  await page.mouse.click(hotbar9.x, hotbar9.y);
  await page.waitForTimeout(800);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
  await page.keyboard.press('Digit9');
  await page.waitForTimeout(400);
  await page.locator('.game-icon-button[aria-label*="Túi đồ"]').click();
  await page.waitForTimeout(700);
  const held = (await page.locator('.trade-window').innerText()).replace(/\s+/g, ' ');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
  if (!held.includes('Đang cầm Cần câu')) {
    await fail(page, 'rod-held', held.slice(0, 120));
  }
  log('fishing rod held in slot 9');
}

async function waitForWorld(page) {
  for (let i = 0; i < 90; i++) {
    await page.waitForTimeout(2000);
    const loading = (await page.locator('body').innerText()).includes('Đang vào');
    if (!loading && await page.locator('.game-hud').count() > 0) {
      return;
    }
  }
  throw new Error('the world did not load within three minutes');
}

// The fishing banner's message and phase markers.
async function bannerState(page) {
  const banner = page.locator('.fishing-banner');
  if (await banner.count() === 0) {
    return { shown: false, message: '', biting: false };
  }
  return {
    shown: true,
    message: (await banner.locator('.fishing-message').innerText().catch(() => '')).replace(/\s+/g, ' '),
    biting: await banner.locator('.fishing-window').count() > 0,
  };
}


// Finds the water on screen: the pixels that read as blue, outside the HUD (the top bars and the hotbar). The player stands at the centre
// of the view (the camera follows), so the nearest water pixel to the centre is the one a cast can reach.
async function nearestWater(page) {
  const png = await page.screenshot({ type: 'png' });
  const pts = await page.evaluate(async (b64) => {
    const img = new Image();
    img.src = 'data:image/png;base64,' + b64;
    await img.decode();
    const c = document.createElement('canvas');
    c.width = img.width;
    c.height = img.height;
    const g = c.getContext('2d');
    g.drawImage(img, 0, 0);
    const d = g.getImageData(0, 0, c.width, c.height).data;
    const out = [];
    for (let y = 140; y < 590; y += 4) {
      for (let x = 0; x < c.width; x += 4) {
        const i = (y * c.width + x) * 4;
        const r = d[i], gg = d[i + 1], b = d[i + 2];
        if (b - r >= 65 && b >= 80 && b > gg + 30 && b < 235) {
          out.push([x, y]);
        }
      }
    }
    return out;
  }, png.toString('base64'));
  // A small blue tile (the mine ladder's frame) is not a pond: a pond fills a large part of the view.
  if (pts.length < 400) {
    return null;
  }
  let best = null;
  for (const [x, y] of pts) {
    const dist = Math.hypot(x - 640, y - 360);
    if (!best || dist < best.dist) {
      best = { x, y, dist };
    }
  }
  return best;
}

// Walks toward the water until it is within reach (about two tiles), exploring in a square when no water is in view.
async function reachWater(page) {
  const moves = [['KeyD', 1200], ['KeyS', 1200], ['KeyA', 2400], ['KeyW', 2400], ['KeyD', 2400]];
  for (let step = 0; step < 60; step++) {
    const water = await nearestWater(page);
    if (water && water.dist <= 100) {
      log('water in reach at', water.x, water.y, 'distance', Math.round(water.dist));
      return water;
    }
    if (!water) {
      const [key, ms] = moves[step % moves.length];
      await press(page, [[key, ms]]);
      continue;
    }
    const dx = water.x - 640;
    const dy = water.y - 360;
    if (Math.abs(dx) > Math.abs(dy)) {
      await press(page, [[dx > 0 ? 'KeyD' : 'KeyA', 200]]);
    } else {
      await press(page, [[dy > 0 ? 'KeyS' : 'KeyW', 200]]);
    }
  }
  return null;
}

// One cast: click the water next to the player, wait for the bite and press when the bar is up. Returns the result message of the cast
// (a catch says "câu được" or "caught") and whether the cast landed a fish.
async function castOnce(page, attempt, shots) {
  let water = await nearestWater(page);
  if (!water || water.dist > 100) {
    water = await reachWater(page);
    if (!water) {
      return { result: 'no water in reach', caught: false };
    }
  }
  await page.mouse.click(water.x, water.y);
  let state = { shown: false };
  for (let i = 0; i < 40; i++) {
    await page.waitForTimeout(150);
    state = await bannerState(page);
    if (state.shown) {
      break;
    }
  }
  if (!state.shown) {
    return { result: 'no banner after the click', caught: false };
  }
  log('cast', attempt, 'banner:', state.message);
  if (!shots.cast && /chờ|Waiting/.test(state.message)) {
    await shot(page, 'fishing-cast.png');
    shots.cast = true;
    log('saved fishing-cast.png');
  }
  if (/chỉ quăng|only go into water|xa mặt nước|too far/i.test(state.message)) {
    await page.waitForTimeout(2500);
    await reachWater(page);
    return { result: state.message, caught: false };
  }
  // Wait for the bite (the bar appears), up to 30 seconds.
  let biteSeen = false;
  for (let i = 0; i < 300; i++) {
    await page.waitForTimeout(100);
    state = await bannerState(page);
    if (!state.shown) {
      return { result: 'banner cleared before the bite', caught: false };
    }
    if (state.biting) {
      biteSeen = true;
      break;
    }
  }
  if (!biteSeen) {
    return { result: 'no bite within 30 s: ' + state.message, caught: false };
  }
  log('bite: banner says', state.message);
  await page.waitForTimeout(150);
  await page.keyboard.press('Space');
  // Wait for the result (the banner leaves the reeling state).
  let result = '';
  for (let i = 0; i < 60; i++) {
    await page.waitForTimeout(100);
    state = await bannerState(page);
    if (!state.shown) {
      break;
    }
    if (!state.biting && state.message && !/kéo cần|Reeling/.test(state.message)) {
      result = state.message;
      break;
    }
  }
  const caught = /câu được|You caught/.test(result);
  if (caught && !shots.catch) {
    await shot(page, 'fishing-catch.png');
    shots.catch = true;
    log('saved fishing-catch.png');
  }
  log('result:', result || '(banner cleared)');
  await page.waitForTimeout(2600);
  return { result, caught };
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' });
  const page = await context.newPage();
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('PAGEERROR ' + e.message));

  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(1000);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 60000 });
  await waitForWorld(page);
  log('in the world');

  try {
    // 1. Wood, fiber, craft, equip.
    const wood = await gatherWood(page);
    if (wood.wood < 5) {
      await fail(page, 'wood', JSON.stringify(wood));
    }
    const fiber = await gatherFiber(page);
    if (fiber.fiber < 4) {
      await fail(page, 'fiber', JSON.stringify(fiber));
    }
    await craftAndEquipRod(page);

    // 2. Find the water on screen and walk to within reach of it.
    if (!await reachWater(page)) {
      await fail(page, 'water', 'no water within reach after walking');
    }

    // 3. Cast, bite, press, until a fish is caught.
    const shots = {};
    let caught = false;
    for (let attempt = 0; attempt < 15 && !caught; attempt++) {
      const cast = await castOnce(page, attempt, shots);
      caught = cast.caught;
    }
    if (!caught) {
      await fail(page, 'catch', 'no fish in 15 casts');
    }
    log('catch done');
  } catch (err) {
    log('stopped:', err.message);
  }

  log('console errors:', errors.length ? errors.slice(0, 8) : 'none');
  await browser.close();
})().catch(err => {
  console.error(err);
  process.exitCode = 1;
});
