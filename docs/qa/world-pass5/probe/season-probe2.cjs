const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch({ args: ['--no-sandbox'] });
  const c = await b.newContext({ viewport: { width: 1280, height: 720 }, locale: 'en-US', serviceWorkers: 'block' });
  const p = await c.newPage();
  await p.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await p.waitForSelector('button', { timeout: 90000 }); await p.waitForTimeout(1000);
  await p.getByRole('button', { name: /khách|guest/i }).click();
  await p.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 }); await p.waitForTimeout(3000);
  const label = () => p.$eval('.calendar-season', e => e.textContent.trim()).catch(() => 'none');
  const slots = () => p.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  console.log('before:', await label());
  await p.evaluate(() => window.nosumQa.springReset());
  for (const s of [2, 8, 15]) { await p.waitForTimeout(5000); console.log(`label after ${s*0+5}s x${s/s}:`, await label()); }
  await p.evaluate(() => window.nosumQa.teleport('farm', 46, 18)); await p.waitForTimeout(2500);
  await p.evaluate(() => { window.nosumQa.grant(100, 10, 1000); window.nosumQa.grant(1, 1, 0); }); await p.waitForTimeout(1500);
  const names = await slots(); console.log('slots:', names.join(' | ').slice(0, 300));
  const idx = names.findIndex(n => /seeds|hạt/i.test(n)) + 1;
  const hoe = names.findIndex(n => /^hoe$|cuốc/i.test(n)) + 1;
  await p.keyboard.press(String(hoe)); await p.waitForTimeout(200); await p.keyboard.down('KeyD'); await p.waitForTimeout(120); await p.keyboard.up('KeyD');
  await p.keyboard.press('KeyE'); await p.waitForTimeout(700);
  await p.keyboard.press(String(idx)); await p.waitForTimeout(200); await p.keyboard.press('KeyE'); await p.waitForTimeout(800);
  console.log('after sow notices:', await p.evaluate(() => [...document.querySelectorAll('.game-notice,.hud-bottom-center')].map(e => e.innerText.trim().replace(/\s+/g,' ').slice(0,160)).join(' | ')));
  await b.close();
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
