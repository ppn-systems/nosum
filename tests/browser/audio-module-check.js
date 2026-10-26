// Drives audio.js directly (run: NODE_PATH=$(npm root -g) node tests/browser/audio-module-check.js <page url>): requests made before a gesture are remembered (rain) or dropped (one-shots),
// the rain loop starts after the gesture, and a music track with no loaded buffer is silence.
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch();
  const page = await (await browser.newContext()).newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error' && !m.text().includes('404')) { errors.push(m.text()); } });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  await page.goto(process.argv[2] || 'http://localhost:5080/', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 60000 });
  const pre = await page.evaluate(async () => {
    window.__started = [];
    const start = AudioBufferSourceNode.prototype.start;
    AudioBufferSourceNode.prototype.start = function (...a) { window.__started.push({ loop: this.loop, len: this.buffer.length }); return start.apply(this, a); };
    window.__audio = await import('/_content/Nosum.Rendering.Web/audio.js');
    const a = window.__audio;
    const loaded = await a.loadAudio(1, 1, 'audio/rain_loop.wav');
    const sound = await a.loadAudio(0, 14, 'audio/footstep.wav');
    a.audioCommand(-9, 1, 55, 0);   // rain wanted before any gesture: remembered
    a.audioCommand(-8, 3, 0, 0);    // a music track that is not loaded (no buffer): silence
    a.audioCommand(-7, 14, 100, 0); // a one-shot before the gesture: dropped
    return { loaded, sound, startedBeforeGesture: window.__started.length };
  });
  await page.mouse.click(300, 300);
  await page.waitForTimeout(1000);
  const post = await page.evaluate(() => {
    const a = window.__audio;
    a.audioCommand(-7, 14, 100, -50);  // a one-shot after the gesture, panned left
    a.audioCommand(-9, 0, 0, 0);       // rain off
    return { started: window.__started };
  });
  console.log('before gesture', JSON.stringify(pre));
  console.log('after gesture ', JSON.stringify(post));
  const loopStarted = post.started.some(s => s.loop === true);
  const ok = pre.loaded && pre.sound && pre.startedBeforeGesture === 0 && loopStarted && post.started.length === 2 && errors.length === 0;
  console.log('errors', JSON.stringify(errors), ok ? 'RESULT: PASS' : 'RESULT: FAIL');
  await browser.close();
  process.exit(ok ? 0 : 1);
})();
