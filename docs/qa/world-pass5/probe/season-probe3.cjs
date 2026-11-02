const { chromium } = require('playwright');
(async () => {
  const b = await chromium.launch({ args: ['--no-sandbox'] });
  const c = await b.newContext({ viewport: { width: 1280, height: 720 }, locale: 'en-US', serviceWorkers: 'block' });
  const p = await c.newPage();
  const label = () => p.$eval('.calendar-season', e => e.textContent.trim()).catch(() => 'none');
  await p.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await p.waitForSelector('button', { timeout: 90000 }); await p.waitForTimeout(1000);
  await p.getByRole('button', { name: /khách|guest/i }).click();
  await p.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 90000 }); await p.waitForTimeout(3000);
  console.log('fresh load label:', await label());
  await p.evaluate(() => window.nosumQa.teleport('farm', 46, 18)); await p.waitForTimeout(2000);
  await p.evaluate(() => { window.nosumQa.grant(100, 10, 1000); }); await p.waitForTimeout(1200);
  const names = await p.$$eval('.hotbar-slots > *', els => els.map(e => e.getAttribute('aria-label') || ''));
  const idx = names.findIndex(n => /seeds/i.test(n)) + 1;
  await p.keyboard.press(String(idx)); await p.waitForTimeout(200); await p.keyboard.press('KeyE'); await p.waitForTimeout(800);
  console.log('sow on fresh page:', await p.evaluate(() => [...document.querySelectorAll('.game-notice,.hud-bottom-center')].map(e => e.innerText.trim().replace(/\s+/g,' ').slice(0,90)).join(' | ')));
  await b.close();
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
