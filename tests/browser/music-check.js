// Browser check for the own music (ADR-039, M6): after the first click the music starts as the looping track the server calendar asks for.
// Run: NODE_PATH=$(npm root -g) node tests/browser/music-check.js <page url> <track: spring_day | spring_night | ...> [screenshot.png]
// The host decides the time of day: start it with NOSUM_CALENDAR_EPOCH set so that the wanted hour is now (see docs/adr/ADR-018).
// The track is recognised by its length: audio.js decodes each file into a 44.1 kHz buffer (its decoder context), so the
// expected length is the file's 22.05 kHz frame count (one byte per frame in 8-bit mono) resampled to 44.1 kHz.
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const audioDir = path.join(__dirname, '..', '..', 'wasm', 'Nosum.Wasm', 'wwwroot', 'audio');
const frames = {};
for (const file of fs.readdirSync(audioDir).filter(f => f.startsWith('music_') && f.endsWith('.wav'))) {
  frames[file.slice('music_'.length, -'.wav'.length)] = fs.readFileSync(path.join(audioDir, file)).readUInt32LE(40) * 2;
}

(async () => {
  const base = process.argv[2] || 'http://localhost:5080/';
  const expected = process.argv[3];
  const browser = await chromium.launch();
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' })).newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  await page.addInitScript(() => {
    window.__loops = [];
    const start = AudioBufferSourceNode.prototype.start;
    AudioBufferSourceNode.prototype.start = function (...a) {
      if (this.loop && this.buffer) { window.__loops.push({ len: this.buffer.length, at: performance.now() }); }
      return start.apply(this, a);
    };
  });
  const loops = () => page.evaluate(() => window.__loops.slice());
  const trackOf = len => Object.keys(frames).find(name => frames[name] === len) || `unknown(${len})`;

  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(3000);
  const before = await loops();
  console.log('loops before any gesture:', before.length);
  let ok = before.length === 0;

  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForSelector('canvas', { timeout: 60000 }).catch(() => {});

  // The music waits for the server's calendar, so give the first snapshot time to arrive.
  let heard = [];
  for (let i = 0; i < 30 && heard.length === 0; i++) {
    await page.waitForTimeout(1000);
    heard = await loops();
  }
  const tracks = [...new Set(heard.map(l => trackOf(l.len)))];
  console.log('music tracks started:', JSON.stringify(tracks));
  ok = ok && tracks.length === 1 && tracks[0] === expected;
  if (process.argv[4]) { await page.screenshot({ path: process.argv[4] }); }

  // The same track keeps going: no second loop starts while it plays.
  await page.waitForTimeout(5000);
  const later = [...new Set((await loops()).map(l => trackOf(l.len)))];
  console.log('music tracks after 5 s:', JSON.stringify(later));
  ok = ok && later.length === 1 && later[0] === expected;

  console.log('console errors:', JSON.stringify(errors));
  ok = ok && errors.length === 0;
  console.log(ok ? 'RESULT: PASS' : 'RESULT: FAIL');
  await browser.close();
  process.exit(ok ? 0 : 1);
})();
