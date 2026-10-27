const { chromium } = require('playwright');
const path = require('path');
const http = require('http');
const net = require('net');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(__dirname, '../../docs/qa');

function startProxy(port = 57301, targetPort = 57300) {
  const server = http.createServer((req, res) => {
    res.writeHead(404);
    res.end();
  });

  server.on('upgrade', (req, clientSocket, head) => {
    const targetSocket = net.connect(targetPort, '127.0.0.1', () => {
      let reqLines = [`${req.method} ${req.url} HTTP/${req.httpVersion}`];
      for (let i = 0; i < req.rawHeaders.length; i += 2) {
        const key = req.rawHeaders[i];
        const val = req.rawHeaders[i + 1];
        if (key.toLowerCase() === 'origin') {
          reqLines.push('Origin: https://farm.ppnsys.com');
        } else if (key.toLowerCase() === 'host') {
          reqLines.push(`Host: 127.0.0.1:${targetPort}`);
        } else {
          reqLines.push(`${key}: ${val}`);
        }
      }
      reqLines.push('', '');
      targetSocket.write(reqLines.join('\r\n'));
      if (head && head.length > 0) targetSocket.write(head);
      clientSocket.pipe(targetSocket);
      targetSocket.pipe(clientSocket);
    });

    clientSocket.on('error', () => targetSocket.destroy());
    targetSocket.on('error', () => clientSocket.destroy());
  });

  return new Promise((resolve) => {
    server.listen(port, '127.0.0.1', () => {
      console.log(`[Proxy] Listening on ${port} -> ${targetPort}`);
      resolve(server);
    });
  });
}

async function capture() {
  const proxy = await startProxy();

  console.log('Launching browser...');
  const browser = await chromium.launch();
  const errors = [];

  // 1280x720 Run
  {
    console.log('\n--- Capturing 1280x720 ---');
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' });
    const page = await context.newPage();
    page.on('console', m => {
      if (m.type() === 'error') {
        console.log('[Browser Error]', m.text());
        errors.push(m.text());
      }
    });
    page.on('pageerror', e => {
      console.error('[Page Error]', e.message);
      errors.push(e.message);
    });

    await page.goto(base, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 60000 });
    await page.waitForTimeout(1000);

    await page.getByRole('button', { name: /khách|guest/i }).click();
    await page.waitForSelector('canvas', { timeout: 60000 });
    await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 30000 });
    await page.waitForTimeout(4000);

    // 1. HUD screenshot
    console.log('Capturing HUD 1280x720...');
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-hud-1280x720.png') });

    // Crop quest tracker
    const questEl = page.locator('.quest-tracker, .quest');
    if (await questEl.count() > 0) {
      await questEl.first().screenshot({ path: path.join(outDir, 'ui-overhaul-quest-1280x720.png') });
    }

    // Crop calendar & clock
    const clockEl = page.locator('.calendar-clock, .calendar');
    if (await clockEl.count() > 0) {
      await clockEl.first().screenshot({ path: path.join(outDir, 'ui-overhaul-clock-1280x720.png') });
    }

    // Crop hotbar
    const hotbarEl = page.locator('.hotbar');
    if (await hotbarEl.count() > 0) {
      await hotbarEl.first().screenshot({ path: path.join(outDir, 'ui-overhaul-hotbar-1280x720.png') });
    }

    // Crop HUD player action bar
    const playerBarEl = page.locator('.hud-player-bar');
    if (await playerBarEl.count() > 0) {
      await playerBarEl.first().screenshot({ path: path.join(outDir, 'ui-overhaul-menu-tabs-1280x720.png') });
    }

    // 2. Open standalone Inventory window via HUD button
    console.log('Opening standalone Inventory window...');
    await page.locator('.game-icon-button[aria-label*="Túi đồ"]').click();
    await page.waitForSelector('.trade-window', { timeout: 10000 });
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-inventory-1280x720.png') });

    // 3. Open standalone Crafting window via HUD button
    console.log('Opening standalone Crafting window...');
    await page.locator('.game-icon-button[aria-label*="Chế tạo"]').click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-panel-crafting-1280x720.png') });

    // 4. Open standalone Profile & Account window via HUD button
    console.log('Opening standalone Profile & Account window...');
    await page.locator('.game-icon-button[aria-label*="Hồ sơ"]').click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-panel-profile-1280x720.png') });

    // 5. Open standalone Mail window via HUD button
    console.log('Opening standalone Mail window...');
    await page.locator('.game-icon-button[aria-label*="Thư"]').click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-panel-mail-1280x720.png') });

    // 6. Open standalone Settings window via HUD button
    console.log('Opening standalone Settings window...');
    await page.locator('.game-icon-button[aria-label*="Cài đặt"]').click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-settings-general-1280x720.png') });

    // Switch to Sound sub-tab in Settings
    console.log('Selecting Sound sub-tab...');
    const soundSubTab = page.getByRole('button', { name: /Âm thanh|Sound/i });
    await soundSubTab.click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-settings-1280x720.png') });

    // Switch to Credits sub-tab in Settings
    console.log('Selecting Credits sub-tab...');
    const creditsSubTab = page.getByRole('button', { name: /Ghi công|Credits/i });
    await creditsSubTab.click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-settings-credits-1280x720.png') });

    await context.close();
  }

  // 1920x1080 Run
  {
    console.log('\n--- Capturing 1920x1080 ---');
    const context = await browser.newContext({ viewport: { width: 1920, height: 1080 }, locale: 'vi-VN' });
    const page = await context.newPage();
    page.on('console', m => {
      if (m.type() === 'error') {
        console.log('[Browser Error]', m.text());
        errors.push(m.text());
      }
    });

    await page.goto(base, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 60000 });
    await page.waitForTimeout(1000);

    await page.getByRole('button', { name: /khách|guest/i }).click();
    await page.waitForSelector('canvas', { timeout: 60000 });
    await page.waitForSelector('.game-hud .hud-top-right .game-icon-button', { timeout: 30000 });
    await page.waitForTimeout(4000);

    console.log('Capturing HUD 1920x1080...');
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-hud-1920x1080.png') });

    console.log('Opening standalone Inventory 1920x1080...');
    await page.locator('.game-icon-button[aria-label*="Túi đồ"]').click();
    await page.waitForSelector('.trade-window', { timeout: 10000 });
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-inventory-1920x1080.png') });

    console.log('Opening standalone Settings 1920x1080...');
    await page.locator('.game-icon-button[aria-label*="Cài đặt"]').click();
    await page.waitForTimeout(600);
    await page.getByRole('button', { name: /Âm thanh|Sound/i }).click();
    await page.waitForTimeout(600);
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-settings-1920x1080.png') });

    await context.close();
  }

  await browser.close();
  proxy.close();
  if (errors.length > 0) {
    console.error('Console errors detected:', errors);
    process.exit(1);
  }
  console.log('\n[Done] All screenshots captured with 0 console errors!');
}

capture().catch(err => {
  console.error('Capture failed:', err);
  process.exit(1);
});
