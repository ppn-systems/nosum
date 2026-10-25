// Browser check for ADR-039 (run: NODE_PATH=$(npm root -g) node tests/browser/audio-check.js <page url> <screenshot.png>): the page loads with no console errors, the audio context starts only after a user gesture,
// and in the game the hooks produce sounds (footsteps, tool swing, panel open/close, button click).
const { chromium } = require('playwright');
const base = process.argv[2] || 'http://localhost:5080/';
const lang = 'vi';
(async () => {
  const browser = await chromium.launch();
  const ctxOpts = { viewport: { width: 1280, height: 720 }, locale: lang === 'en' ? 'en-US' : 'vi-VN' };
  const page = await (await browser.newContext(ctxOpts)).newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('requestfailed', r => errors.push('requestfailed: ' + r.url()));
  await page.addInitScript(() => {
    window.__audio = { created: 0, states: [], played: [] };
    const Native = window.AudioContext;
    window.AudioContext = class extends Native {
      constructor(...a) { super(...a); window.__audio.created++; window.__audio.states.push('created:' + this.state); this.addEventListener('statechange', () => window.__audio.states.push(this.state)); }
    };
    const start = AudioBufferSourceNode.prototype.start;
    AudioBufferSourceNode.prototype.start = function (...a) { window.__audio.played.push({ len: this.buffer ? this.buffer.length : -1, loop: this.loop }); return start.apply(this, a); };
  });
  const report = (label, v) => console.log(label.padEnd(44), JSON.stringify(v));
  const audio = () => page.evaluate(() => ({ created: window.__audio.created, states: window.__audio.states.slice(), played: window.__audio.played.length }));

  await page.goto(base, { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  await page.waitForTimeout(4000);
  const before = await audio();
  report('before any gesture', before);
  let ok = before.created === 0 && before.played === 0;

  // The gesture: click the guest button.
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForTimeout(500);
  const afterClick = await audio();
  report('after the first click', afterClick);
  ok = ok && afterClick.created === 1 && afterClick.states.includes('running');

  // Wait for the game canvas to be in play (a nickname step may come first).
  await page.waitForSelector('canvas', { timeout: 60000 }).catch(() => {});
  await page.waitForTimeout(8000);
  await page.screenshot({ path: process.argv[3] || 'audio-game.png' });

  const sum = async () => (await audio()).played;
  let n = await sum();
  await page.keyboard.down('KeyD'); await page.waitForTimeout(1500); await page.keyboard.up('KeyD');
  const steps = (await sum()) - n; report('footsteps while walking 1.5 s', steps); ok = ok && steps >= 2;

  n = await sum(); await page.keyboard.press('Digit1'); await page.keyboard.press('KeyE'); await page.waitForTimeout(600);
  const swing = (await sum()) - n; report('tool swing (action key)', swing); ok = ok && swing >= 1;

  n = await sum(); await page.keyboard.press('KeyI'); await page.waitForTimeout(500);
  const open = (await sum()) - n; report('panel open (I)', open); ok = ok && open >= 1;
  n = await sum(); await page.keyboard.press('Escape'); await page.waitForTimeout(500);
  const close = (await sum()) - n; report('panel close (Escape)', close); ok = ok && close >= 1;

  const lens = await page.evaluate(() => [...new Set(window.__audio.played.map(p => p.len))]);
  report('distinct buffers played (lengths)', lens);
  report('console errors', errors);
  ok = ok && errors.length === 0;
  console.log(ok ? 'RESULT: PASS' : 'RESULT: FAIL');
  await browser.close();
  process.exit(ok ? 0 : 1);
})();
