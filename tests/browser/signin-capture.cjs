// Sign-in screen capture (docs/qa/signin). Screenshots the start screen at 1280x720, 1920x1080 and 390x844 (touch), for a first visit
// (nothing remembered) and a returning visit (a remembered session is stored in this browser, docs/adr/ADR-081).
//
// Needs only the client (dotnet run --project wasm/Nosum.Wasm, http://localhost:5080). The start screen does not need the server.
// NOSUM_QA_REPO_ASSETS=1 serves the images from the repository's assets/ folder, as ui-pass-capture.cjs does.
//
// usage: node tests/browser/signin-capture.cjs <before|after> [viewports, comma-separated]
// writes docs/qa/signin/<phase>/<viewport>/{first-visit,returning-visit}.png

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const REPO = path.resolve(__dirname, '../..');
const BASE = process.env.NOSUM_QA_URL || 'http://localhost:5080/';
// The key ADR-081 uses for the remembered session. The value is a JSON record; the token in it is a QA placeholder, not a credential.
const STORAGE_KEY = 'nosum.remembered-session';
const VIEWPORTS = {
  '1280x720': { width: 1280, height: 720 },
  '1920x1080': { width: 1920, height: 1080 },
  '390x844': { width: 390, height: 844 },
};
const MIME = { '.png': 'image/png', '.json': 'application/json' };

async function routeRepoAssets(page) {
  await page.route(/localhost:5080\/(assets|_content\/Nosum\.Wasm\.Controls\/ui)\//, (route) => {
    const pathname = decodeURIComponent(new URL(route.request().url()).pathname);
    let file;
    const name = pathname.slice('/_content/Nosum.Wasm.Controls/ui/'.length);
    if (pathname.startsWith('/assets/')) file = path.join(REPO, 'assets', pathname.slice('/assets/'.length));
    else file = path.join(REPO, name === 'start_meadow.png' ? 'art/own/ui' : 'assets/ui', name);
    if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) return route.fallback();
    return route.fulfill({ status: 200, body: fs.readFileSync(file), contentType: MIME[path.extname(file)] || 'application/octet-stream' });
  });
}

// With preview set, also writes the same first visit with the Microsoft button's disabled flag removed, to show its enabled look. The dev build has no
// client id, so the button is off; the preview is a picture only, the click is not wired up in it.
async function shoot(browser, name, file, seed, errors, preview = false) {
  const size = VIEWPORTS[name];
  const phone = name === '390x844';
  const context = await browser.newContext({ viewport: size, locale: 'vi-VN', hasTouch: phone, isMobile: phone, serviceWorkers: 'block' });
  if (seed) {
    await context.addInitScript(([key, value]) => {
      try { window.localStorage.setItem(key, value); } catch (e) { /* storage blocked: the page must still render */ }
    }, [STORAGE_KEY, seed]);
  }
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`page: ${e.message}`));
  if (process.env.NOSUM_QA_REPO_ASSETS === '1') await routeRepoAssets(page);
  await page.goto(BASE, { waitUntil: 'load' });
  await page.waitForSelector('.start-screen', { timeout: 60000 });
  await page.waitForTimeout(1500);
  await page.screenshot({ path: file });
  if (preview) {
    await page.evaluate(() => document.querySelectorAll('.ms-signin').forEach((b) => { b.disabled = false; }));
    await page.waitForTimeout(300);
    await page.screenshot({ path: file.replace(/first-visit\.png$/, 'first-visit-enabled-preview.png') });
  }
  await context.close();
}

(async () => {
  const phase = process.argv[2];
  if (phase !== 'before' && phase !== 'after') throw new Error('usage: signin-capture.cjs <before|after> [viewports]');
  const names = (process.argv[3] || Object.keys(VIEWPORTS).join(',')).split(',');
  const outRoot = path.join(REPO, 'docs/qa/signin', phase);
  // A remembered session as ADR-081 stores it: a session token, its expiry, the nickname and the provider. Never an ID token.
  const seed = JSON.stringify({ version: 1, token: 'qa-placeholder-token', expiresAt: '2099-01-01T00:00:00Z', nickname: 'Lan', kind: 'google' });
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  const errors = [];
  for (const name of names) {
    const dir = path.join(outRoot, name);
    fs.mkdirSync(dir, { recursive: true });
    await shoot(browser, name, path.join(dir, 'first-visit.png'), null, errors, phase === 'after');
    await shoot(browser, name, path.join(dir, 'returning-visit.png'), seed, errors);
    console.log(`${phase} ${name}: captured`);
  }
  await browser.close();
  if (errors.length) console.log(`page errors: ${errors.slice(0, 5).join(' ; ')}`);
})().catch((e) => { console.error(e); process.exit(1); });
