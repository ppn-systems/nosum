// Round-5 captures for the social review (docs/design/critique-social.md, findings 7 and 9): the co-op invite picker after an invite is
// sent ("Invited", disabled) and the co-op warehouse grid, at 1280x720, 1920x1080 and 390x844, in vi and en.
// Run from the repo root with the host on :57300 and a Development WASM client built with -p:NosumDevTools=true:
//   BASE=http://localhost:5080/ OUT_DIR=docs/qa/social-round5 NODE_PATH=$(npm root -g) node tests/browser/social-round5-capture.cjs
// Two guests play in the Town (QA teleport). Guests only: Development has no Google or Microsoft sign-in (ADR-069).
// Every step is logged to capture-log.txt in OUT_DIR; a failed step is logged and the run goes on.
const { chromium, devices } = require('playwright'); const fs = require('fs'); const path = require('path');
const sleep = ms => new Promise(r => setTimeout(r, ms));
const base = process.env.BASE || 'http://localhost:5080/';
const OUT = process.env.OUT_DIR || path.join(__dirname, '..', '..', 'docs', 'qa', 'social-round5');
fs.mkdirSync(OUT, { recursive: true });
const log = [];
async function step(name, fn) { try { await fn(); log.push('ok   ' + name); } catch (e) { log.push('FAIL ' + name + ': ' + e.message.split('\n')[0]); } }
async function snap(page, name) { await page.screenshot({ path: path.join(OUT, name + '.png') }); }

const LANG = {
  vi: { found: /^Lập$/, invite: /Mời/, invited: /Đã mời/, stock: /^Kho$/, coop: /Hợp tác xã/, accept: /Đồng ý/ },
  en: { found: /^Found$/, invite: /Invite/, invited: /Invited/, stock: /^Stock$/, coop: /^Co-op$/, accept: /^Accept$/ },
};
const SIZES = {
  '1280x720': { viewport: { width: 1280, height: 720 } },
  '1920x1080': { viewport: { width: 1920, height: 1080 } },
  '390x844': { ...devices['iPhone 14'], viewport: { width: 390, height: 844 } },
};

async function guest(browser, opts, lang) {
  const ctx = await browser.newContext(opts);
  await ctx.addInitScript(l => { try { localStorage.setItem('nosum.settings.v1', 'lang=' + l + '\n'); } catch {} }, lang);
  const page = await ctx.newPage();
  await page.goto(base + '?qa=1', { waitUntil: 'load' });
  await page.waitForSelector('button', { timeout: 90000 }); await page.waitForTimeout(2500);
  await page.getByRole('button', { name: /khách|guest/i }).click();
  await page.waitForFunction(() => document.querySelectorAll('input').length === 0, null, { timeout: 60000 });
  await page.waitForTimeout(5000);
  return page;
}

async function run(browser, code, size) {
  const t = LANG[code];
  const p = `${code}-${size}-`;
  const o = { ...SIZES[size], locale: code === 'vi' ? 'vi-VN' : 'en-US' };
  const a = await guest(browser, o, code === 'vi' ? 1 : 2);
  const b = await guest(browser, o, code === 'vi' ? 1 : 2);
  try {
    await a.evaluate(() => window.nosumQa.teleport('town', 20, 20)); await b.evaluate(() => window.nosumQa.teleport('town', 22, 21)); await sleep(4000);
    await step(p + 'board', async () => {
      await a.evaluate(() => window.nosumQa.teleport('town', 42, 59)); await sleep(3500);
      await a.keyboard.press('e'); await sleep(4500);
      await a.getByText(t.coop).first().click({ timeout: 5000 }); await sleep(1200);
    });
    await step(p + 'found', async () => {
      await a.locator('input').first().fill('Test Co-op');
      await a.getByRole('button', { name: t.found }).first().click({ timeout: 5000 }); await sleep(1500);
    });
    await step(p + 'invite', async () => {
      await b.evaluate(() => window.nosumQa.teleport('town', 44, 60)); await sleep(2500);
      await a.getByRole('button', { name: t.invite }).first().click({ timeout: 5000 }); await sleep(1500);
    });
    await step(p + 'coop-invited', async () => {
      const pending = a.getByRole('button', { name: t.invited }).first();
      await pending.waitFor({ timeout: 5000 });
      log.push('     invited button disabled: ' + (await pending.isDisabled()));
      await snap(a, p + 'coop-invited');
    });
    await step(p + 'coop-stock', async () => {
      await a.getByRole('button', { name: t.stock, exact: true }).first().click({ timeout: 5000 }); await sleep(1000);
      await snap(a, p + 'coop-stock');
    });
    for (const page of [a, b]) {
      if (page.errors && page.errors.length) log.push('console errors: ' + JSON.stringify(page.errors));
    }
  } finally {
    await a.context().close(); await b.context().close();
  }
}

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox'] });
  try {
    for (const code of ['vi', 'en']) {
      for (const size of Object.keys(SIZES)) {
        await step(`${code}-${size} session`, () => run(browser, code, size));
      }
    }
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(OUT, 'capture-log.txt'), log.join('\n') + '\n');
    console.log(log.join('\n'));
  }
})();
