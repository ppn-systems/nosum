const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');
const http = require('http');
const net = require('net');

const base = process.argv[2] || 'http://localhost:5080/';
const outDir = path.resolve(__dirname, '../../docs/qa');

// 1. Start WS Proxy
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
  
  // 1280x720 Run
  {
    console.log('\n--- Capturing 1280x720 ---');
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, locale: 'vi-VN' });
    const page = await context.newPage();
    page.on('console', m => {
      if (m.type() === 'error') console.log('[Browser Error]', m.text());
    });
    page.on('pageerror', e => console.error('[Page Error]', e.message));

    console.log('Navigating to', base);
    await page.goto(base, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 60000 });
    await page.waitForTimeout(1000);

    // Guest login
    console.log('Clicking guest button...');
    await page.getByRole('button', { name: /khách|guest/i }).click();
    console.log('Waiting for canvas / game load...');
    await page.waitForSelector('canvas', { timeout: 60000 });
    console.log('Waiting for HUD and initial assets...');
    await page.waitForSelector('.game-hud, .calendar-clock, .hud', { timeout: 30000 });
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

    // 2. Open Menu by clicking settings button
    console.log('Opening menu...');
    const iconButtons = page.locator('.game-icon-button');
    await iconButtons.last().click();
    await page.waitForSelector('.game-menu', { timeout: 10000 });
    await page.waitForTimeout(600);

    // Crop tab row
    const tabsEl = page.locator('.menu-tabs');
    if (await tabsEl.count() > 0) {
      await tabsEl.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-tabs-1280x720.png') });
    }

    // Click inventory tab (first tab: "Túi đồ")
    console.log('Selecting inventory tab...');
    await page.locator('.menu-tab').first().click();
    await page.waitForTimeout(800);
    console.log('Capturing Inventory Menu 1280x720...');
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-inventory-1280x720.png') });

    // 3. Switch to Settings panel & Sound sub-tab
    console.log('Switching to settings tab...');
    const settingsTab = page.locator('.menu-tab[aria-label*="Cài đặt"], .menu-tab[aria-label*="Settings"]');
    await settingsTab.click();
    await page.waitForTimeout(600);

    console.log('Selecting Sound sub-tab...');
    const soundSubTab = page.getByRole('button', { name: /Âm thanh|Sound/i });
    if (await soundSubTab.count() > 0) {
      await soundSubTab.click();
      await page.waitForTimeout(600);
    }
    console.log('Capturing Settings Sound Panel 1280x720...');
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-settings-1280x720.png') });

    await context.close();
  }

  // 1920x1080 Run
  {
    console.log('\n--- Capturing 1920x1080 ---');
    const context = await browser.newContext({ viewport: { width: 1920, height: 1080 }, locale: 'vi-VN' });
    const page = await context.newPage();
    page.on('console', m => {
      if (m.type() === 'error') console.log('[Browser Error]', m.text());
    });

    await page.goto(base, { waitUntil: 'load' });
    await page.waitForSelector('button', { timeout: 60000 });
    await page.waitForTimeout(1000);

    // Guest login
    await page.getByRole('button', { name: /khách|guest/i }).click();
    await page.waitForSelector('canvas', { timeout: 60000 });
    await page.waitForSelector('.game-hud, .calendar-clock, .hud', { timeout: 30000 });
    await page.waitForTimeout(4000);

    // 1. HUD screenshot
    console.log('Capturing HUD 1920x1080...');
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-hud-1920x1080.png') });

    // 2. Open Inventory Menu
    console.log('Opening menu...');
    const iconButtons = page.locator('.game-icon-button');
    await iconButtons.last().click();
    await page.waitForSelector('.game-menu', { timeout: 10000 });
    await page.waitForTimeout(600);

    await page.locator('.menu-tab').first().click();
    await page.waitForTimeout(800);
    console.log('Capturing Inventory Menu 1920x1080...');
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-inventory-1920x1080.png') });

    // 3. Settings panel
    const settingsTab = page.locator('.menu-tab[aria-label*="Cài đặt"], .menu-tab[aria-label*="Settings"]');
    await settingsTab.click();
    await page.waitForTimeout(600);

    const soundSubTab = page.getByRole('button', { name: /Âm thanh|Sound/i });
    if (await soundSubTab.count() > 0) {
      await soundSubTab.click();
      await page.waitForTimeout(600);
    }
    console.log('Capturing Settings Menu 1920x1080...');
    await page.screenshot({ path: path.join(outDir, 'ui-overhaul-menu-settings-1920x1080.png') });

    await context.close();
  }

  await browser.close();
  proxy.close();
  console.log('\n[Done] All screenshots captured successfully!');
}

capture().catch(err => {
  console.error('Capture failed:', err);
  process.exit(1);
});
