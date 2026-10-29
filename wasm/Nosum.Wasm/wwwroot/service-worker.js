// In development, never intercept fetch requests so every network/internal call goes straight through the browser.
// On activate, evict any leftover offline caches from a previous published run on the same origin.
// A new version waits for the page to ask for it (SKIP_WAITING, from the update banner in index.html); see service-worker.published.js.
self.addEventListener('install', () => {});
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});
self.addEventListener('activate', event => event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(
        keys
            .filter(key => key.startsWith('nosum-offline-cache-') || key.startsWith('offline-cache-'))
            .map(key => caches.delete(key))
    );
    await self.clients.claim();
})()));
