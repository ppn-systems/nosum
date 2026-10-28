self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsExclude = [ /^service-worker(\.published)?\.js$/, /^service-worker-assets\.js$/, /^_headers$/, /^appsettings(\..+)?\.json$/, /^_framework\// ];

const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestAssetsByUrl = new Map(
    self.assetsManifest.assets
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => [new URL(asset.url, baseUrl).href, asset.hash])
);

function toCanonicalUrl(requestUrl) {
    const url = new URL(requestUrl);
    url.search = '';
    url.hash = '';
    return url.href;
}

async function fetchAndCache(cache, canonicalUrl, hash) {
    const bustUrl = hash ? `${canonicalUrl}?v=${encodeURIComponent(hash)}` : canonicalUrl;
    const response = await fetch(new Request(bustUrl, { cache: 'no-cache' }));
    if (response.ok) {
        await cache.put(canonicalUrl, response.clone());
    }
    return response;
}

async function onInstall(event) {
    await self.skipWaiting();
}

async function onActivate(event) {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    if (event.request.method !== 'GET') {
        return fetch(event.request);
    }

    const requestUrl = new URL(event.request.url);
    if (requestUrl.origin !== self.origin) {
        return fetch(event.request);
    }

    const canonicalUrl = toCanonicalUrl(event.request.url);
    const shouldServeIndexHtml = event.request.mode === 'navigate'
        && !manifestAssetsByUrl.has(canonicalUrl);

    const lookupUrl = shouldServeIndexHtml ? new URL('index.html', baseUrl).href : canonicalUrl;
    if (!manifestAssetsByUrl.has(lookupUrl)) {
        return fetch(event.request);
    }

    const cache = await caches.open(cacheName);
    const cachedResponse = await cache.match(lookupUrl);
    if (cachedResponse) {
        return cachedResponse;
    }

    const hash = manifestAssetsByUrl.get(lookupUrl);
    return fetchAndCache(cache, lookupUrl, hash);
}
