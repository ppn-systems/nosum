// Canvas2D micro-benchmark behind docs/perf/wasm-perf.md round 5: the minimap draws about 110 rectangles a frame (fillRect), and each one of the
// renderer's coloured rectangles (command -6, renderer.js) builds its CSS colour string and assigns fillStyle. This compares the same 110 rectangles
// drawn with a fresh colour string per call against a colour string cached per tint, with the same one-pixel readback as the frame harness.
// Usage: node fill-micro.cjs   (no server needed)
const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  await page.setContent('<canvas id="c" width="1920" height="1080"></canvas>');
  const result = await page.evaluate(() => {
    const canvas = document.getElementById('c');
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    // 110 runs of minimap-like rectangles: 4 px tall rows, a few tints (the palette has a handful of colours), spread over a 320 x 240 corner.
    const tints = [0x2a7d3cff, 0x6b4f2cff, 0x8c8c8cff, 0x3d6ec2ff, 0xd9c48cff, 0x111111ff];
    const rects = [];
    for (let i = 0; i < 110; i++) rects.push({ x: 1500 + ((i * 37) % 60) * 4, y: 40 + (i % 30) * 4, w: 8 + ((i * 13) % 40), h: 4, tint: tints[i % tints.length] });
    const fresh = (u) => `rgba(${u >>> 24}, ${(u >>> 16) & 255}, ${(u >>> 8) & 255}, ${(u & 255) / 255})`;
    const cache = new Map();
    const cached = (u) => { let s = cache.get(u); if (s === undefined) { s = fresh(u); cache.set(u, s); } return s; };
    const frame = (mode) => {
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.fillStyle = '#000';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      if (mode === 'fresh') {
        for (const r of rects) { ctx.fillStyle = fresh(r.tint); ctx.fillRect(r.x, r.y, r.w, r.h); }
      } else if (mode === 'cached') {
        for (const r of rects) { ctx.fillStyle = cached(r.tint); ctx.fillRect(r.x, r.y, r.w, r.h); }
      } else {
        // Cached and only assigning fillStyle when the colour changes (the rectangles are drawn in tint runs, so most assignments are skipped).
        let last = -1;
        for (const r of rects) { if (r.tint !== last) { ctx.fillStyle = cached(r.tint); last = r.tint; } ctx.fillRect(r.x, r.y, r.w, r.h); }
      }
      ctx.getImageData(0, 0, 1, 1);
    };
    const time = (mode) => {
      for (let i = 0; i < 30; i++) frame(mode);
      const runs = [];
      for (let r = 0; r < 5; r++) {
        const t0 = performance.now();
        for (let i = 0; i < 200; i++) frame(mode);
        runs.push((performance.now() - t0) / 200);
      }
      runs.sort((a, b) => a - b);
      return { medianMsPerFrame: Math.round(runs[2] * 1000) / 1000, minMs: Math.round(runs[0] * 1000) / 1000, maxMs: Math.round(runs[4] * 1000) / 1000 };
    };
    // Interleaved, so drift on the machine hits every mode alike.
    const out = { fresh: [], cached: [], tintRuns: [] };
    for (let round = 0; round < 3; round++) {
      out.fresh.push(time('fresh'));
      out.cached.push(time('cached'));
      out.tintRuns.push(time('tintRuns'));
    }
    return out;
  });
  console.log(JSON.stringify(result, null, 2));
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
