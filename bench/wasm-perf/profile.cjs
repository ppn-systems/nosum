// CPU profile of the frame loop: node profile.cjs <baseUrl with ?qa=1> [--seconds N]
// Starts the page like frame.cjs, walks for N seconds under the Chromium CPU profiler and prints where the time went, by function
// and by source (the .NET runtime's wasm code, renderer.js, the page). Self time only; the profile is a split, not a benchmark.
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const base = args[0] || 'http://localhost:5081/?qa=1';
const secs = Number(args[args.indexOf('--seconds') + 1] || 6) || 6;
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN', serviceWorkers: 'block' });
  const page = await context.newPage();
  await page.goto(base, { waitUntil: 'load' });
  await wait(2500);
  await page.getByRole('button', { name: /khách|guest/i }).first().click();
  await page.waitForSelector('[role=meter][aria-label^="Sức lực"]', { timeout: 120000 });
  await wait(2000);
  const cdp = await context.newCDPSession(page);
  await cdp.send('Profiler.enable');
  await cdp.send('Profiler.setSamplingInterval', { interval: 200 });
  await cdp.send('Profiler.start');
  const keys = ['KeyD', 'KeyS', 'KeyA', 'KeyW'];
  const end = Date.now() + secs * 1000;
  let i = 0;
  while (Date.now() < end) {
    const k = keys[Math.floor(i / 2) % keys.length];
    await page.keyboard.down(k);
    await wait(700);
    await page.keyboard.up(k);
    i++;
  }
  const { profile } = await cdp.send('Profiler.stop');
  // Self time per node, attributed to a function and its source.
  const byId = new Map(profile.nodes.map(n => [n.id, n]));
  const self = new Map();
  const dt = profile.timeDeltas;
  profile.samples.forEach((id, idx) => self.set(id, (self.get(id) || 0) + (dt[idx] || 0)));
  const byFn = new Map();
  const bySource = new Map();
  let total = 0;
  for (const [id, t] of self) {
    const n = byId.get(id);
    const cf = n.callFrame;
    const name = cf.functionName || '(anonymous)';
    const src = cf.url ? cf.url.split('/').slice(-1)[0] || cf.url : `(${name})`;
    const fnKey = `${name} [${src}]`;
    byFn.set(fnKey, (byFn.get(fnKey) || 0) + t);
    const bucket = cf.url.includes('_framework') ? '.NET runtime (wasm)' : cf.url.includes('renderer.js') || cf.url.includes('audio.js') ? 'renderer/audio JS' : cf.url ? 'other JS / page' : 'native/idle';
    bySource.set(bucket, (bySource.get(bucket) || 0) + t);
    total += t;
  }
  const top = (m, n) => [...m.entries()].sort((a, b) => b[1] - a[1]).slice(0, n);
  console.log(`samples total ${(total / 1000).toFixed(0)} ms over ${secs} s`);
  console.log('by source:');
  for (const [k, t] of top(bySource, 10)) console.log(`  ${(100 * t / total).toFixed(1).padStart(5)}%  ${k}`);
  console.log('top functions:');
  for (const [k, t] of top(byFn, 25)) console.log(`  ${(100 * t / total).toFixed(1).padStart(5)}%  ${k}`);
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
