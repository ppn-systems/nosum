// Captures the tool feel of docs/adr/ADR-074 at 1280x720 against a running client (the published wasm, served statically) and game server.
// Writes the frames and a manifest (elapsed milliseconds from the key press, as measured) to docs/qa/screens/tool-feel/.
// Usage: node tests/browser/tool-feel-capture.cjs [http://localhost:5080/]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(__dirname, '../../docs/qa/screens/tool-feel');
const manifest = [];

const energy = page => page.evaluate(() => {
  const meter = document.querySelector('[role=meter][aria-label^="Sức lực"]');
  return meter ? Number(meter.getAttribute('aria-valuenow')) : -1;
});

async function frames(page, name, startMs, offsets) {
  for (let i = 0; i < offsets.length; i++) {
    const wait = offsets[i] - (Date.now() - startMs);
    if (wait > 0) await page.waitForTimeout(wait);
    const file = `${name}-${i + 1}.png`;
    await page.screenshot({ path: path.join(outDir, file) });
    manifest.push({ file, elapsedMs: Date.now() - startMs });
  }
}

// One key press, then frames at the given offsets from the press (ms); the offsets are targets, the manifest records what was measured.
async function use(page, name, digitKey, offsets) {
  await page.keyboard.press(digitKey);
  await page.waitForTimeout(250);
  const start = Date.now();
  await page.keyboard.press('KeyE');
  await frames(page, name, start, offsets);
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' });
  await page.goto(base, { waitUntil: 'load' });
  await page.waitForTimeout(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('[role=meter][aria-label^="Sức lực"]', { timeout: 90000 });
  await page.waitForTimeout(2000);

  // Hoe: wind-up, hit (dirt flecks and the energy label), follow-through, recovery.
  await use(page, 'hoe', 'Digit1', [60, 220, 380, 640]);
  await page.waitForTimeout(600);

  // Watering can: the same tile, the splash at the hit.
  await use(page, 'water', 'Digit2', [60, 220, 380, 640]);
  await page.waitForTimeout(600);

  // Drag-to-sow with the hoe: the mouse held down and moved across the tiles in reach; each tile is one swing.
  await page.keyboard.press('Digit1');
  await page.waitForTimeout(250);
  const dragStart = Date.now();
  await page.mouse.move(600, 440);
  await page.mouse.down();
  for (const x of [640, 680, 720]) {
    await page.waitForTimeout(470);
    await page.mouse.move(x, 440, { steps: 4 });
  }
  await frames(page, 'drag', dragStart, [900, 1400, 1900]);
  await page.mouse.up();
  await page.waitForTimeout(800);

  // Exhaustion: till and clear the same tile until energy is at a quarter of its maximum (docs/adr/ADR-074 D4). Each pair costs four energy.
  let spent = await energy(page);
  let guard = 0;
  while (spent > 67 && guard++ < 160) {
    await page.keyboard.press('Digit1');
    await page.waitForTimeout(120);
    await page.keyboard.press('KeyE');
    await page.waitForTimeout(700);
    await page.keyboard.press('Digit7');
    await page.waitForTimeout(120);
    await page.keyboard.press('KeyE');
    await page.waitForTimeout(700);
    spent = await energy(page);
  }
  manifest.push({ note: `energy ${spent} of 270 when the exhausted frames were taken` });
  await page.keyboard.press('Digit1');
  await page.waitForTimeout(1200);
  await page.screenshot({ path: path.join(outDir, 'exhausted-1-idle.png') });
  manifest.push({ file: 'exhausted-1-idle.png', note: 'idle, sweat bubble above the head' });
  await use(page, 'exhausted-hoe', 'Digit1', [60, 260, 460, 900]);

  fs.writeFileSync(path.join(outDir, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  await browser.close();
  console.log(manifest.map(m => JSON.stringify(m)).join('\n'));
})().catch(e => { console.error(e); process.exit(1); });
