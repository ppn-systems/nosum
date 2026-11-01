// Load measurement for the WASM client: node load.cjs <publishedWwwroot> <baseUrl> [runs] [--json out.json]
//  - sizes: the files of the published output on disk (boot set and page assets), raw, gzip -9 and brotli -q11; for reference.
//  - wire: the bytes the page actually fetches before it is playable (CDP Network, encoded size), requests and count, per run.
//  - browser: cold context, service workers blocked; first paint, guest button visible, playable (the HUD stamina meter).
// Needs a running Development server (ws://localhost:57300/ws/) and a Chromium from Playwright (preinstalled in this environment).
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { chromium } = require('playwright');

const [, , rootArg, baseArg = 'http://localhost:5080/', runsArg = '5', ...rest] = process.argv;
const root = path.resolve(rootArg);
const jsonIdx = rest.indexOf('--json');
const jsonOut = jsonIdx >= 0 ? rest[jsonIdx + 1] : null;
const runs = Number(runsArg);

function walk(dir, out = []) {
  for (const name of fs.readdirSync(dir)) {
    const p = path.join(dir, name);
    if (fs.statSync(p).isDirectory()) walk(p, out); else out.push(p);
  }
  return out;
}

// The first load: the framework boot set and the page's own assets. Symbols and the service worker are not fetched on a normal start.
function firstLoadFiles() {
  return walk(root).filter(f => {
    const rel = path.relative(root, f).replace(/\\/g, '/');
    if (rel.endsWith('.pdb') || rel.startsWith('service-worker')) return false;
    if (rel.startsWith('_framework/')) return /\.(wasm|dll|js|json|dat)$/.test(rel) && !/\.(pdb)$/.test(rel);
    return /\.(js|css|html|png|wav|ogg|tmx|json|ico)$/.test(rel) && !rel.startsWith('audio/');
  });
}

function sizes(files) {
  const row = { files: files.length, raw: 0, gzip: 0, brotli: 0, wasm: 0, wasmBrotli: 0, dll: 0, dllBrotli: 0 };
  for (const f of files) {
    const buf = fs.readFileSync(f);
    const br = zlib.brotliCompressSync(buf, { params: { [zlib.constants.BROTLI_PARAM_QUALITY]: 11, [zlib.constants.BROTLI_PARAM_SIZE_HINT]: buf.length } }).length;
    const gz = zlib.gzipSync(buf, { level: 9 }).length;
    row.raw += buf.length; row.gzip += gz; row.brotli += br;
    if (f.endsWith('.wasm')) { row.wasm += buf.length; row.wasmBrotli += br; }
    if (f.endsWith('.dll')) { row.dll += buf.length; row.dllBrotli += br; }
  }
  return row;
}

const median = a => { const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length / 2)]; };

(async () => {
  const files = firstLoadFiles();
  const size = sizes(files);
  console.log('first load (bytes):', JSON.stringify(size));

  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const timings = [];
  for (let i = 0; i < runs; i++) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN', serviceWorkers: 'block' });
    const page = await context.newPage();
    // Every response the page fetches before it is playable: encoded (on the wire) bytes and request count, from the CDP Network domain.
    const cdp = await context.newCDPSession(page);
    await cdp.send('Network.enable');
    const net = { requests: 0, bytes: 0, wasm: 0, audio: 0 };
    cdp.on('Network.responseReceived', e => { net.requests++; if (/\.wasm$/.test(e.response.url)) net.wasmSeen = (net.wasmSeen || 0) + 1; });
    cdp.on('Network.loadingFinished', e => { net.bytes += e.encodedDataLength; });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message.slice(0, 120)));
    const t0 = Date.now();
    await page.goto(baseArg, { waitUntil: 'commit' });
    const guest = page.getByRole('button', { name: /khách|guest/i }).first();
    await guest.waitFor({ state: 'visible', timeout: 120000 });
    const tGuest = Date.now() - t0;
    const fcp = await page.evaluate(() => {
      const e = performance.getEntriesByName('first-contentful-paint')[0];
      return e ? e.startTime : null;
    });
    await guest.click();
    await page.waitForSelector('[role=meter][aria-label^="Sức lực"]', { timeout: 120000 });
    const tPlayable = Date.now() - t0;
    timings.push({ fcp, tGuest, tPlayable, errors: errors.length, requests: net.requests, bytes: net.bytes });
    console.log(`run ${i + 1}: first-contentful-paint ${fcp && fcp.toFixed(0)} ms, guest button ${tGuest} ms, playable ${tPlayable} ms, ${net.requests} requests, ${(net.bytes / 1048576).toFixed(2)} MiB on the wire, page errors ${errors.length}`);
    await context.close();
  }
  await browser.close();

  const result = {
    publish: root,
    runs,
    size,
    medianFcpMs: median(timings.map(t => t.fcp ?? 0)),
    medianGuestButtonMs: median(timings.map(t => t.tGuest)),
    medianPlayableMs: median(timings.map(t => t.tPlayable)),
    medianRequests: median(timings.map(t => t.requests)),
    medianWireBytes: median(timings.map(t => t.bytes)),
    pageErrors: timings.reduce((a, t) => a + t.errors, 0),
  };
  console.log(JSON.stringify(result, null, 2));
  if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(result, null, 2));
})().catch(e => { console.error(e); process.exit(1); });
