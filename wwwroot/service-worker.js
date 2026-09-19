const CACHE = "medical-manager-shell-v1";
const PRECACHE = [
    "/manifest.webmanifest",
    "/icons/icon-192.png",
    "/icons/icon-512.png",
    "/icons/icon-180.png"
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE).then((cache) => cache.addAll(PRECACHE)).then(() => self.skipWaiting())
    );
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key)))
        ).then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", (event) => {
    if (event.request.method !== "GET") {
        return;
    }

    const url = new URL(event.request.url);
    if (url.origin !== self.location.origin) {
        return;
    }

    if (url.pathname.startsWith("/_blazor") || url.pathname.startsWith("/_framework")) {
        return;
    }

    const isShellAsset =
        url.pathname.startsWith("/icons/") ||
        url.pathname.startsWith("/lib/") ||
        url.pathname === "/manifest.webmanifest" ||
        url.pathname.endsWith(".css") ||
        url.pathname.endsWith(".js");

    if (!isShellAsset) {
        return;
    }

    event.respondWith(
        fetch(event.request)
            .then((response) => {
                if (response.ok) {
                    const copy = response.clone();
                    caches.open(CACHE).then((cache) => cache.put(event.request, copy));
                }
                return response;
            })
            .catch(() => caches.match(event.request))
    );
});
