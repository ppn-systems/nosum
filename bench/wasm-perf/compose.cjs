// Puts a before and an after capture side by side, labelled, for docs/perf/chunk-baking. Usage: node compose.cjs <dir> <name> ...
// Reads <dir>/before-<name>.png and <dir>/after-<name>.png and writes <dir>/side-<name>.png.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

(async () => {
  const [, , dirArg, ...names] = process.argv;
  const dir = path.resolve(dirArg);
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 2580, height: 800 } });
  for (const name of names) {
    const before = path.join(dir, `before-${name}.png`);
    const after = path.join(dir, `after-${name}.png`);
    const html = path.join(dir, `.side-${name}.html`);
    fs.writeFileSync(html, `<body style="margin:0;background:#111;color:#fff;font:20px sans-serif;display:flex;gap:20px;padding:20px">
      <div><div style="margin-bottom:8px">Before (origin/master)</div><img src="file://${before}" width="1280" height="720"></div>
      <div><div style="margin-bottom:8px">After (chunk baking)</div><img src="file://${after}" width="1280" height="720"></div>
    </body>`);
    await page.goto('file://' + html);
    await page.evaluate(() => Promise.all([...document.images].map(i => i.decode())));
    await page.screenshot({ path: path.join(dir, `side-${name}.png`), fullPage: true });
    fs.unlinkSync(html);
    console.log('composed', name);
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
