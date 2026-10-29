self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => {
    if (event.request.method !== 'GET') {
        return;
    }
    if (new URL(event.request.url).origin !== self.origin) {
        return;
    }
    event.respondWith(onFetch(event));
});
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    } else if (event.data && event.data.type === 'WARM_OFFLINE_CACHE') {
        startWarmingOnce();
    }
});

const cacheNamePrefix = 'nosum-offline-cache-';
const legacyCachePrefix = 'offline-cache-';
const cacheRevision = '1';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}-${cacheRevision}`;

const runtimeCacheOnDemand = [
    /^_framework\/icudt_.*\.dat$/,
    /^_framework\/[a-z]{2}(-[A-Za-z]+)?\//
];
const offlineAssetsExclude = [
    /^service-worker(\.published)?\.js$/,
    /^service-worker-assets\.js$/,
    /^_headers$/,
    /^appsettings(\..+)?\.json$/,
    ...runtimeCacheOnDemand
];
const shellAssetUrl = 'index.html';

async function cacheAsset(cache, asset) {
    try {
        await cache.add(new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    } catch (e) {
        console.error('[nosum-sw] offline precache failed for asset:', asset.url, e && e.message ? e.message : e);
    }
}

const WARM_CONCURRENCY = 4;
async function warmOfflineCache() {
    const cache = await caches.open(cacheName);
    const assets = self.assetsManifest.assets
        .filter(asset => asset.url !== shellAssetUrl)
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)));
    let next = 0;
    async function poolWorker() {
        while (next < assets.length) {
            await cacheAsset(cache, assets[next++]);
        }
    }
    await Promise.all(Array.from({ length: Math.min(WARM_CONCURRENCY, assets.length) }, poolWorker));
    console.info('Service worker: offline cache warmed');
}

let warmStarted = false;
function startWarmingOnce() {
    if (warmStarted) {
        return;
    }
    warmStarted = true;
    warmOfflineCache();
}
const WARM_FALLBACK_DELAY_MS = 15000;

async function toCleanResponse(response) {
    return new Response(await response.blob(), {
        status: response.status,
        statusText: response.statusText,
        headers: response.headers
    });
}

async function onInstall(event) {
    console.info('Service worker: Install');
    self.skipWaiting();
    const shellAsset = self.assetsManifest.assets.find(asset => asset.url === shellAssetUrl);
    if (!shellAsset) {
        console.error('[nosum-sw] shell asset missing from assets manifest, offline navigation will not work:', shellAssetUrl);
        return;
    }
    const cache = await caches.open(cacheName);
    try {
        let shellResponse;
        try {
            shellResponse = await fetch(new Request(shellAsset.url, { integrity: shellAsset.hash, cache: 'no-cache' }));
        } catch (integrityError) {
            shellResponse = await fetch(new Request(shellAsset.url, { cache: 'no-cache' }));
        }
        if (!shellResponse.ok) {
            throw new Error(`shell fetch returned ${shellResponse.status}`);
        }
        await cache.put(shellAsset.url, shellResponse.redirected ? await toCleanResponse(shellResponse) : shellResponse);
    } catch (e) {
        console.error('[nosum-sw] shell precache failed, offline navigation will not work:', shellAssetUrl, e && e.message ? e.message : e);
    }
}

async function onActivate(event) {
    console.info('Service worker: Activate');
    const cacheKeys = await caches.keys();
    await Promise.all(
        cacheKeys
            .filter(key => (key.startsWith(cacheNamePrefix) || key.startsWith(legacyCachePrefix)) && key !== cacheName)
            .map(key => caches.delete(key))
    );
    await self.clients.claim();
    setTimeout(startWarmingOnce, WARM_FALLBACK_DELAY_MS);
}

async function cacheOnDemand(cache, request) {
    const response = await fetch(request);
    if (response.ok) {
        try {
            await cache.put(request, response.clone());
        } catch (e) {
            console.error('[nosum-sw] runtime-cache write-through failed for:', request.url, e && e.message ? e.message : e);
        }
    }
    return response;
}

function isHtmlResponse(response) {
    const type = response.headers.get('Content-Type') || '';
    return type.toLowerCase().indexOf('text/html') !== -1;
}

async function fetchFrameworkWithRetry(request) {
    try {
        const first = await fetch(request);
        if (!isHtmlResponse(first)) {
            return first;
        }
    } catch (e) {
        // Fall through to reload retry.
    }
    return fetch(new Request(request.url, {
        cache: 'reload',
        credentials: 'same-origin',
        integrity: request.integrity
    }));
}

async function onFetch(event) {
    let cachedResponse = null;
    const request = event.request;
    if (request.method !== 'GET') {
        return fetch(request);
    }
    const requestUrl = new URL(request.url);
    if (requestUrl.origin !== self.origin) {
        return fetch(request);
    }

    // The Microsoft sign-in popup returns to msal-redirect.html: that page is served as itself, never as the app shell.
    const shouldServeIndexHtml = request.mode === 'navigate' && !requestUrl.pathname.endsWith('/msal-redirect.html');
    if (shouldServeIndexHtml) {
        cachedResponse = await caches.match(shellAssetUrl, { cacheName });
    }
    cachedResponse = cachedResponse || await caches.match(request, { cacheName });
    if (cachedResponse) {
        if (shouldServeIndexHtml && cachedResponse.redirected) {
            return toCleanResponse(cachedResponse);
        }
        return cachedResponse;
    }

    const relativeUrl = requestUrl.pathname.replace(/^\//, '');
    if (runtimeCacheOnDemand.some(pattern => pattern.test(relativeUrl))) {
        const cache = await caches.open(cacheName);
        return cacheOnDemand(cache, request);
    }
    if (requestUrl.pathname.indexOf('/_framework/') === 0) {
        return fetchFrameworkWithRetry(request);
    }
    const networkResponse = await fetch(request);
    if (shouldServeIndexHtml && networkResponse.redirected) {
        return toCleanResponse(networkResponse);
    }
    return networkResponse;
}
