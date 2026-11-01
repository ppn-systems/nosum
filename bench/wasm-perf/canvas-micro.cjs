// Canvas2D micro-benchmark behind docs/perf/wasm-perf.md: the same screen area drawn as many 16 px tiles at zoom 3 versus a few
// 256 px chunk bitmaps at zoom 3. Shows whether the cost of drawImage follows the number of calls or the pixels they fill.
// Usage: node canvas-micro.cjs   (no server needed)
const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage();
  await page.setContent('<canvas id="c" width="1280" height="720"></canvas>');
  const result = await page.evaluate(() => {
    const canvas = document.getElementById('c');
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    // A 16 px tile sheet, and a 256 px chunk bitmap made of the same tiles.
    const sheet = new OffscreenCanvas(256, 256);
    const sc = sheet.getContext('2d');
    for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) { sc.fillStyle = `hsl(${(x * 37 + y * 91) % 360} 60% 45%)`; sc.fillRect(x * 16, y * 16, 16, 16); }
    const chunk = sheet; // the bake of 16 x 16 tiles is the same picture
    const frame = (mode) => {
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.fillStyle = '#000';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.setTransform(3, 0, 0, 3, 0, 0);
      if (mode === 'tiles') {
        // 27 x 15 visible tiles at zoom 3 cover 1296 x 720 screen pixels: one drawImage per tile.
        for (let ty = 0; ty < 15; ty++) for (let tx = 0; tx < 27; tx++) ctx.drawImage(sheet, (tx % 16) * 16, (ty % 16) * 16, 16, 16, tx * 16, ty * 16, 16, 16);
      } else {
        // The same area as six 256 px chunks (2 x 3 chunks cover 32 x 24 tiles), each one drawImage.
        for (let cy = 0; cy < 2; cy++) for (let cx = 0; cx < 3; cx++) ctx.drawImage(chunk, 0, 0, 256, 256, cx * 256, cy * 256, 256, 256);
      }
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      // Canvas2D records calls and rasterizes later; a one-pixel readback forces the frame's drawing to finish inside the timed loop.
      ctx.getImageData(0, 0, 1, 1);
    };
    const time = (mode) => {
      for (let i = 0; i < 20; i++) frame(mode);
      const runs = [];
      for (let r = 0; r < 5; r++) {
        const t0 = performance.now();
        for (let i = 0; i < 100; i++) frame(mode);
        runs.push((performance.now() - t0) / 100);
      }
      runs.sort((a, b) => a - b);
      return runs[2];
    };
    return { tilesMsPerFrame: time('tiles'), chunksMsPerFrame: time('chunks'), tileCalls: 27 * 15, chunkCalls: 6 };
  });
  console.log(JSON.stringify(result, null, 2));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
