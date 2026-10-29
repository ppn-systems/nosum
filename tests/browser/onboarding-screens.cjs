// Captures the onboarding and feel screens for the issue #207 - #216 PR (docs/qa/screens/): the sign-in screen at desktop and phone sizes,
// the "Entering the world" overlay, and the game with the touch controls on a phone. Run by hand against a published client (on :5080) and
// the game server (on :57300): node tests/browser/onboarding-screens.cjs [base-url] [out-dir]
const { chromium } = require('playwright');
const path = require('path');

const base = process.argv[2] || 'http://127.0.0.1:5080/';
const outDir = path.resolve(process.argv[3] || path.join(__dirname, '../../docs/qa/screens'));
const guestButton = /tư cách khách/;

async function openGame(page) {
  await page.getByRole('button', { name: guestButton }).click();
}

async function main() {
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium', args: ['--no-sandbox'] });
  try {
    const desktop = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true, deviceScaleFactor: 2 });

    const d = await desktop.newPage();
    await d.goto(base, { waitUntil: 'load' });
    await d.getByRole('button', { name: guestButton }).waitFor({ timeout: 60000 });
    await d.waitForTimeout(800);
    await d.screenshot({ path: path.join(outDir, '01-sign-in-1280x720.png') });

    const p = await phone.newPage();
    await p.goto(base, { waitUntil: 'load' });
    await p.getByRole('button', { name: guestButton }).waitFor({ timeout: 60000 });
    await p.waitForTimeout(800);
    await p.screenshot({ path: path.join(outDir, '02-sign-in-390x844-touch.png') });

    await openGame(p);
    await p.getByText(/Đang vào thế giới/).waitFor({ timeout: 15000 });
    await p.waitForTimeout(400);
    await p.screenshot({ path: path.join(outDir, '03-entering-world-390x844-touch.png') });

    await p.locator('canvas#game-canvas').waitFor({ state: 'visible', timeout: 90000 });
    await p.waitForTimeout(2500);
    await p.screenshot({ path: path.join(outDir, '04-game-390x844-touch.png') });
  } finally {
    await browser.close();
  }
}

main().catch(e => {
  console.error('FAIL', e.message);
  process.exit(1);
});
