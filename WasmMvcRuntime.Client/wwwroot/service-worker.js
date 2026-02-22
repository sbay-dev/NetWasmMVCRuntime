// ✅ Development Service Worker - No caching for development
// This allows changes to be reflected immediately without cache issues

// ✅ Install event - skip waiting to activate immediately
self.addEventListener('install', event => {
    console.log('[SW] Installing service worker...');
    self.skipWaiting();
});

// ✅ Activate event - take control immediately
self.addEventListener('activate', event => {
    console.log('[SW] Activating service worker...');
    event.waitUntil(self.clients.claim());
});

// ✅ No fetch handler in development - removes the "no-op" warning
// The browser will handle all fetch requests normally
// For production caching, see service-worker.published.js
