window.mmPrefersReducedMotion = () =>
    window.matchMedia("(prefers-reduced-motion: reduce)").matches;

(() => {
    if ("serviceWorker" in navigator) {
        window.addEventListener("load", () => {
            navigator.serviceWorker.register("/service-worker.js").catch(() => {
                /* Offline install cache is optional for Blazor Server. */
            });
        });
    }
})();
