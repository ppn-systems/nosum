// Captures the farm-feel pass 2 (docs/adr/ADR-021 D7, ADR-056 D5, ADR-074 D7) at 1280x720 against a running Development server and a
// tester build of the client (-p:NosumDevTools=true, served statically). Writes the frames and a manifest to docs/qa/farm-feel-2/.
// Usage: node tests/browser/farm-feel-capture.cjs [http://localhost:5080/?qa=1]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/?qa=1';
const outDir = path.resolve(__dirname, '../../docs/qa/farm-feel-2');
const manifest = [];

const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

async function shot(page, file, note) {
  await page.screenshot({ path: path.join(outDir, file) });
  manifest.push({ file, note });
}

// The skill buttons at native pixels, so the sweep on each button can be read.
async function skillsCrop(page, file, note) {
  const box = await page.evaluate(() => {
    const r = document.querySelector('.hotbar-skills').getBoundingClientRect();
    return { x: r.x, y: r.y, width: r.width, height: r.height };
  });
  await page.screenshot({ path: path.join(outDir, file), clip: { x: box.x - 6, y: box.y - 6, width: box.width + 12, height: box.height + 12 } });
  manifest.push({ file, note });
}

// A skill button shows a cooldown sweep only after the server admitted the cast (docs/adr/ADR-056 D5, amended).
const cooling = page => page.evaluate(() => document.querySelector('.hotbar-skills .skill-cooldown') !== null);

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' });
  page.on('pageerror', e => console.log('pageerror:', e.message.slice(0, 160)));
  await page.goto(base, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('[role=meter][aria-label^="Sức lực"]', { timeout: 90000 });
  await wait(2000);

  // Facing: walk left so the farmer looks left, then hold the mouse on the tile on its right and till it. The body must turn right before the swing.
  await page.keyboard.press('Digit1');
  await wait(200);
  await page.keyboard.down('KeyA');
  await wait(700);
  await page.keyboard.up('KeyA');
  await wait(400);
  await shot(page, 'facing-1-before-faces-left.png', 'The farmer after walking left: facing left, the hoe in hand.');
  await page.screenshot({ path: path.join(outDir, 'facing-1-before-crop.png'), clip: { x: 580, y: 290, width: 170, height: 150 } });
  await page.mouse.move(680, 400);
  await page.mouse.down();
  // The swing lasts about 420 ms (ADR-074 D1): a crop at each stage shows the wind-up, the strike and the settle on the right side.
  for (const [i, ms] of [[1, 60], [2, 150], [3, 240], [4, 330]]) {
    await wait(i === 1 ? ms : ms - [60, 150, 240, 330][i - 2]);
    await page.screenshot({ path: path.join(outDir, `facing-2-swing-${i}-crop.png`), clip: { x: 580, y: 290, width: 170, height: 150 } });
  }
  manifest.push({ file: 'facing-2-swing-1..4-crop.png', note: 'Crops across the swing after the mouse goes down: the farmer faces the tile it works.' });
  await shot(page, 'facing-2-drag-turns-right-mid-swing.png', 'The mouse held on the tile to the right: the body turned right and the hoe swings to the right.');
  await wait(600);
  await page.mouse.up();
  await wait(700);
  await shot(page, 'facing-3-after-till.png', 'The tile to the right is tilled and the farmer still faces it.');
  // The same moment, cropped around the farmer at native pixels, so the body's side is readable.
  await page.screenshot({ path: path.join(outDir, 'facing-2-drag-turns-right-crop.png'), clip: { x: 580, y: 290, width: 170, height: 150 } });
  manifest.push({ file: 'facing-2-drag-turns-right-crop.png', note: 'Crop of the frame above around the farmer.' });

  // Cooldown: teleport to mine floor 1, where monsters are near the entrance, and cast Whirlwind (Z) and Fireball (X) until the server admits one.
  await page.evaluate(() => window.nosumQa.teleport('mine1', 8, 8));
  await wait(3000);
  let admitted = false;
  // Walk a square while casting: monsters roam, and a cast is admitted only when one is in the skill's reach.
  const square = ['KeyD', 'KeyS', 'KeyA', 'KeyW'];
  for (let i = 0; i < 240 && !admitted; i++) {
    const dir = square[Math.floor(i / 3) % square.length];
    await page.keyboard.down(dir);
    await page.keyboard.press(i % 2 === 0 ? 'KeyX' : 'KeyZ');
    await wait(300);
    await page.keyboard.up(dir);
    admitted = await cooling(page);
  }

  if (admitted) {
    await shot(page, 'cooldown-1-overlay-just-cast.png', 'A skill was admitted: its hotbar button shows the cooldown sweep and is disabled until it ends.');
    await skillsCrop(page, 'cooldown-1-skill-buttons-crop.png', 'The Z, X and V buttons at native pixels right after the cast: the cast one shows the sweep.');
    // The same overlay at the other two sizes of the UI standard (docs/design/ui-standard.md).
    await page.setViewportSize({ width: 1920, height: 1080 });
    await wait(300);
    await shot(page, 'cooldown-1-overlay-1920x1080.png', 'The same cast at 1920x1080.');
    await page.setViewportSize({ width: 1024, height: 768 });
    await wait(300);
    await shot(page, 'cooldown-1-overlay-1024x768.png', 'The same cast at 1024x768, the narrowest supported width.');
    await page.setViewportSize({ width: 1280, height: 720 });
    await wait(300);
    await wait(1500);
    await shot(page, 'cooldown-2-overlay-draining.png', 'The sweep drains from the top over the skill\'s cooldown.');
    await skillsCrop(page, 'cooldown-2-skill-buttons-crop-draining.png', 'The same buttons 1.5 s later: the sweep has drained part of the way.');
    // The cast skill (X, Fireball) is pressed again while it cools: the button is disabled, so no swing or effect starts.
    await page.keyboard.press('KeyX');
    await wait(150);
    await shot(page, 'cooldown-3-press-while-cooling-is-refused.png', 'A press while the skill cools down starts no swing and no skill animation.');
  } else {
    manifest.push({ file: null, note: 'No skill was admitted in the time allowed: no cooldown overlay captured.' });
  }

  await browser.close();
  fs.writeFileSync(path.join(outDir, 'manifest.json'), JSON.stringify({ viewport: '1280x720', admittedCast: admitted, frames: manifest }, null, 2));
  console.log(JSON.stringify({ admitted, frames: manifest.length }));
})().catch(e => { console.error('FAILED', e.message); process.exit(1); });
