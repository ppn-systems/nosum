// Scales the skill-button crops of tests/browser/farm-feel-capture.cjs up six times with nearest-neighbour sampling, so the cooldown sweep reads.
// Usage: node tests/browser/farm-feel-zoom.cjs
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const outDir = path.resolve(__dirname, '../../docs/qa/farm-feel-2');
// The skill-button crops and the farmer crops (before and mid-drag), each scaled to the same look.
const crops = ['cooldown-1-skill-buttons-crop.png', 'cooldown-2-skill-buttons-crop-draining.png', 'facing-1-before-crop.png', 'facing-2-drag-turns-right-crop.png'];

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  for (const file of crops) {
    const src = 'data:image/png;base64,' + fs.readFileSync(path.join(outDir, file)).toString('base64');
    await page.setContent(`<html><body style="margin:0;background:#1a1410"><img id="c" src="${src}" style="image-rendering:pixelated;transform:scale(6);transform-origin:0 0"></body></html>`);
    await page.waitForFunction(() => document.getElementById('c').complete);
    const size = await page.evaluate(() => ({ w: document.getElementById('c').naturalWidth, h: document.getElementById('c').naturalHeight }));
    const factor = file.startsWith('facing') ? 4 : 6;
    await page.evaluate(f => { document.querySelector('img').style.transform = `scale(${f})`; }, factor);
    await page.setViewportSize({ width: Math.max(1, size.w * factor), height: Math.max(1, size.h * factor) });
    await page.screenshot({ path: path.join(outDir, file.replace('-crop', '-crop-zoom')) });
  }
  await browser.close();
  console.log('zoomed', crops.length);
})().catch(e => { console.error('FAILED', e.message); process.exit(1); });
