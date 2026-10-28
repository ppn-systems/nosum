// In development, never intercept fetch requests so every network/internal call goes straight through the browser.
// On activate, evict any leftover offline caches from a previous published run on the same origin.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(
        keys
            .filter(key => key.startsWith('nosum-offline-cache-') || key.startsWith('offline-cache-'))
            .map(key => caches.delete(key))
    );
    await self.clients.claim();
})()));
