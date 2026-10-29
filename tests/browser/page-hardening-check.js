// Browser check for the device gate and the game-like page behaviour (wasm/README.md "Device gate and page behaviour").
// Run: NODE_PATH=$(npm root -g) node tests/browser/page-hardening-check.js <page url> [screenshot dir]
// Needs the published client served on <page url> (default http://localhost:5080/) and the host on :57300; plays one throw-away guest (no password sign-in exists, docs/adr/ADR-071).
//   1. iPhone 14 profile: the block screen (vi, then en), no WebSocket opened, no canvas, no touch controls.
//   2. 800 px wide desktop window: the "widen the window" overlay, gone again at 1280 px.
//   3. 1280x720 desktop: the start screen (Google and Microsoft buttons, no e-mail or password field) and a WebSocket; as a guest: right-click on the canvas opens no menu, selection on the HUD is empty,
//      copy / cut / F5 / Backspace / middle button / drag are prevented outside fields, typing and pasting in the chat input still works,
//      the close-tab prompt is armed, the canvas is pixelated, and nothing logged a console error.
const { chromium, devices } = require('playwright');
const path = require('path');
const base = process.argv[2] || 'http://localhost:5080/';
const shots = process.argv[3] || null;
let failed = 0;
function check(name, ok, detail) {
  console.log((ok ? 'PASS ' : 'FAIL ') + name + (detail === undefined ? '' : '  ' + JSON.stringify(detail)));
  if (!ok) { failed++; }
}
const shot = (page, name) => shots ? page.screenshot({ path: path.join(shots, name) }) : Promise.resolve();
const settings = lang => 'lang=' + lang + '\n';   // GameLanguage: 0 = Unspecified (Vietnamese), 1 = Vietnamese, 2 = English; see ClientSettings.Parse

async function withContext(browser, options, language, fn) {
  const context = await browser.newContext(options);
  if (language !== undefined) {
    await context.addInitScript(([k, v]) => { try { localStorage.setItem(k, v); } catch { } }, ['nosum.settings.v1', settings(language)]);
  }
  const page = await context.newPage();
  const errors = [];
  const sockets = [];
  page.on('console', m => { if (m.type() === 'error') { errors.push(m.text()); } });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('websocket', s => sockets.push(s.url()));
  try { await fn(page, { errors, sockets }); } finally { await context.close(); }
}

(async () => {
  const browser = await chromium.launch();

  // 1. A phone sees the notice and never connects.
  for (const [lang, code, expected] of [[1, 'vi', /máy tính/], [2, 'en', /computers for now/]]) {
    await withContext(browser, { ...devices['iPhone 14'] }, lang, async (page, seen) => {
      await page.goto(base, { waitUntil: 'load' });
      await page.waitForSelector('.device-block', { timeout: 90000 });
      await page.waitForTimeout(4000);
      const text = await page.locator('.device-block').innerText();
      check(`phone (${code}): block screen text`, expected.test(text), text);
      check(`phone (${code}): title is Nosum`, /Nosum/.test(text));
      check(`phone (${code}): no WebSocket opened`, seen.sockets.length === 0, seen.sockets);
      check(`phone (${code}): no canvas, no touch controls, no sign-in form`,
        await page.locator('canvas, .touch-controls, [data-touch-edge], input').count() === 0);
      check(`phone (${code}): no console errors`, seen.errors.length === 0, seen.errors);
      await shot(page, `device-blocked-${code}.png`);
    });
  }

  // 2. A narrow desktop window gets the overlay, and the game is back when it is widened.
  await withContext(browser, { viewport: { width: 800, height: 600 } }, 1, async (page, seen) => {
    await page.goto(base, { waitUntil: 'load' });
    await page.waitForSelector('.device-block', { timeout: 90000 });
    check('narrow window: overlay text names the minimum', /1024/.test(await page.locator('.device-block').innerText()));
    await shot(page, 'window-too-narrow-vi.png');
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.waitForSelector('.device-block', { state: 'detached', timeout: 10000 });
    check('narrow window: widened to 1280 px, overlay gone and start screen usable', await page.locator('input').first().isVisible());
    await page.setViewportSize({ width: 1000, height: 700 });
    await page.waitForSelector('.device-block', { timeout: 10000 });
    check('narrow window: narrowed again, overlay back', true);
  });
  await withContext(browser, { viewport: { width: 800, height: 600 } }, 2, async page => {
    await page.goto(base, { waitUntil: 'load' });
    await page.waitForSelector('.device-block', { timeout: 90000 });
    check('narrow window (en): text', /widen/.test(await page.locator('.device-block').innerText()));
    await shot(page, 'window-too-narrow-en.png');
  });

  // 3. A desktop plays; the page behaves like a game.
  await withContext(browser, { viewport: { width: 1280, height: 720 }, locale: 'en-US', permissions: ['clipboard-read', 'clipboard-write'] }, 2, async (page, seen) => {
    await page.goto(base, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 90000 });
    await page.waitForTimeout(2500);
    check('desktop 1280x720: sees the sign-in screen, not the block screen', await page.locator('.device-block').count() === 0);
    check('desktop: the WebSocket opens', seen.sockets.length >= 1, seen.sockets);

    // The start screen signs in only with Google or Microsoft (docs/adr/ADR-071): no e-mail or password field, one button per provider.
    check('sign-in: no e-mail or password field on the start screen', await page.locator('input[type=email], input[type=password]').count() === 0);
    const providers = await page.getByRole('button', { name: /Google|Microsoft/ }).count();
    check('sign-in: one Google and one Microsoft button', providers === 2, providers);

    // Playing as a guest needs no provider, so the game checks below run as a guest.
    await page.getByRole('button', { name: /as a guest|tư cách khách/i }).first().click();
    await page.waitForSelector('canvas.game-canvas', { timeout: 60000 });
    await page.waitForSelector('.game-hud .hud-top-right *', { timeout: 60000 });
    await page.waitForTimeout(5000);
    await shot(page, 'game-desktop-1280.png');

    check('game: canvas is image-rendering pixelated', await page.evaluate(() => getComputedStyle(document.querySelector('canvas.game-canvas')).imageRendering) === 'pixelated');
    check('game: close-tab prompt is armed', await page.evaluate(() => window.nosumPage.unloadGuard === true));
    check('game: beforeunload asks', await page.evaluate(() => !window.dispatchEvent(new Event('beforeunload', { cancelable: true }))));

    // Right-click on the canvas: the page cancels the context menu (a native menu cannot be seen by the page, so the event's default is checked).
    const box = await page.locator('canvas.game-canvas').boundingBox();
    await page.evaluate(() => { window.__menu = []; window.addEventListener('contextmenu', e => window.__menu.push(e.defaultPrevented)); });
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: 'right' });
    await page.mouse.click(box.x + 40, box.y + 40, { button: 'right' });
    const menu = await page.evaluate(() => window.__menu);
    check('right-click on the canvas and on the HUD: context menu prevented', menu.length === 2 && menu.every(Boolean), menu);

    // Selection on the HUD stays empty, whatever the gesture.
    const hud = page.locator('.game-hud .hud-top-right').first();
    const hb = await hud.boundingBox();
    await page.mouse.dblclick(hb.x + hb.width / 2, hb.y + hb.height / 2);
    await page.mouse.move(hb.x, hb.y);
    await page.mouse.down(); await page.mouse.move(hb.x + hb.width, hb.y + hb.height + 20, { steps: 8 }); await page.mouse.up();
    await page.keyboard.press('Control+A');
    await page.evaluate(() => { try { document.execCommand('selectAll'); } catch { } });
    check('HUD: selection is empty after double-click, drag and select-all', await page.evaluate(() => window.getSelection().toString() === ''), await page.evaluate(() => window.getSelection().toString()));
    check('HUD: user-select is none', await page.evaluate(() => getComputedStyle(document.querySelector('.game-hud')).userSelect) === 'none');

    // Cancelable synthetic events: dispatchEvent returns false when the page prevented the default.
    const prevented = await page.evaluate(() => {
      const outside = document.querySelector('canvas.game-canvas');
      const ev = (T, type, init) => !outside.dispatchEvent(new T(type, { bubbles: true, cancelable: true, ...init }));
      return {
        f5: ev(KeyboardEvent, 'keydown', { key: 'F5' }),
        ctrlR: ev(KeyboardEvent, 'keydown', { key: 'r', ctrlKey: true }),
        backspace: ev(KeyboardEvent, 'keydown', { key: 'Backspace' }),
        copy: ev(ClipboardEvent, 'copy'),
        cut: ev(ClipboardEvent, 'cut'),
        middle: ev(MouseEvent, 'mousedown', { button: 1 }),
        drag: ev(DragEvent, 'dragstart'),
        ctrlWheel: ev(WheelEvent, 'wheel', { ctrlKey: true, deltaY: -100 }),
      };
    });
    for (const [name, value] of Object.entries(prevented)) { check(`outside a field: ${name} prevented`, value === true); }

    // The chat input keeps everything: typing, selecting, copy, cut, paste, Backspace, the context menu.
    await page.keyboard.press('Enter');
    await page.waitForSelector('#nosum-chat-input', { timeout: 10000 });
    await page.keyboard.type('hello world');
    check('chat: typing works', await page.inputValue('#nosum-chat-input') === 'hello world');
    await page.keyboard.press('Backspace');
    check('chat: Backspace edits the text', await page.inputValue('#nosum-chat-input') === 'hello worl');
    await page.keyboard.press('Control+A');
    check('chat: select-all selects the input text', await page.evaluate(() => { const i = document.getElementById('nosum-chat-input'); return i.selectionEnd - i.selectionStart === 10; }));
    await page.keyboard.press('Control+C');
    check('chat: copy puts the text on the clipboard', await page.evaluate(() => navigator.clipboard.readText()) === 'hello worl');
    await page.keyboard.press('Control+X');
    check('chat: cut empties the input', await page.inputValue('#nosum-chat-input') === '');
    await page.evaluate(() => navigator.clipboard.writeText('pasted text'));
    await page.keyboard.press('Control+V');
    check('chat: paste works', await page.inputValue('#nosum-chat-input') === 'pasted text');
    const inField = await page.evaluate(() => {
      const i = document.getElementById('nosum-chat-input');
      const results = {};
      for (const [name, T, type, init] of [['contextmenu', MouseEvent, 'contextmenu', {}], ['copy', ClipboardEvent, 'copy', {}], ['selectstart', Event, 'selectstart', {}], ['backspace', KeyboardEvent, 'keydown', { key: 'Backspace' }], ['f5', KeyboardEvent, 'keydown', { key: 'F5' }]]) {
        results[name] = !i.dispatchEvent(new T(type, { bubbles: true, cancelable: true, ...init }));
      }
      return results;
    });
    check('chat input: nothing is prevented (menu, copy, selection, Backspace, F5)', Object.values(inField).every(v => v === false), inField);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(1500);
    const line = await page.evaluate(() => document.body.innerText.includes('pasted text'));
    check('chat: the sent line shows in the log', line);
    // The chat log is the one piece of HUD text that may be selected.
    const logSelect = await page.evaluate(() => { const o = document.querySelector('[data-selectable]'); return o ? getComputedStyle(o).userSelect : 'missing'; });
    check('chat log: user-select is text', logSelect === 'text', logSelect);

    check('desktop: no console errors', seen.errors.length === 0, seen.errors);
  });

  await browser.close();
  console.log(failed === 0 ? 'RESULT: PASS' : `RESULT: FAIL (${failed})`);
  process.exit(failed === 0 ? 0 : 1);
})();
