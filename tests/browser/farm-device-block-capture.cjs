// Captures what a phone-width visitor sees: the device guard (docs/design/ui-standard.md section 6). Usage: node tests/browser/farm-device-block-capture.cjs <outDir> <locale> <vi|en>
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const [outDir, locale, lang] = [process.argv[2], process.argv[3], process.argv[4]];
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 390, height: 844 }, locale });
  if (lang === 'en') await page.addInitScript(() => { try { localStorage.setItem('nosum.settings.v1', 'lang=2\n'); } catch (e) {} });
  await page.goto('http://localhost:5080/?qa=1', { waitUntil: 'load' });
  await wait(4000);
  const text = await page.evaluate(() => document.querySelector('.device-block')?.innerText ?? '(no device block)');
  await page.screenshot({ path: path.join(outDir, 'device-block.png') });
  fs.writeFileSync(path.join(outDir, 'device-block.txt'), text + '\n');
  console.log(text.replace(/\n/g, ' | '));
  await browser.close();
})().catch(e => { console.error('FAILED', e.message.slice(0, 200)); process.exit(1); });
