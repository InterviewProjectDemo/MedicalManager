export function layer(panel) {
    if (!panel || !panel.isConnected)
        return;
    if (panel.parentElement !== document.body)
        document.body.appendChild(panel);
    if (!panel.dataset.floated) {
        panel.dataset.floated = "1";
        bringForward(panel);
    }
}

export function bringForward(panel) {
    const next = Math.min((window.__mmFloatZ || 4100) + 1, 5000);
    window.__mmFloatZ = next;
    panel.style.zIndex = String(next);
}

export function begin(panel, grip, pointerId) {
    layer(panel);
    bringForward(panel);
    const rect = panel.getBoundingClientRect();
    try {
        grip.setPointerCapture(pointerId);
    } catch {
        // The pointer can still drag while it stays over the grip.
    }
    return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
        viewWidth: window.innerWidth,
        viewHeight: window.innerHeight
    };
}

const PLACE_KEY = "mm-float-place";

function readPlaces() {
    try {
        const raw = localStorage.getItem(PLACE_KEY);
        const data = raw ? JSON.parse(raw) : {};
        return data && typeof data === "object" ? data : {};
    } catch {
        return {};
    }
}

export function recall(key) {
    const viewWidth = window.innerWidth;
    const viewHeight = window.innerHeight;
    const box = key ? readPlaces()[key] : null;
    if (!box || !Number.isFinite(box.left) || !Number.isFinite(box.top) || !Number.isFinite(box.width) || !Number.isFinite(box.height))
        return { found: false, left: 0, top: 0, width: 0, height: 0, viewWidth, viewHeight };
    return { found: true, left: box.left, top: box.top, width: box.width, height: box.height, viewWidth, viewHeight };
}

export function remember(key, left, top, width, height) {
    if (!key)
        return;
    try {
        const all = readPlaces();
        const current = all[key] && typeof all[key] === "object" ? all[key] : {};
        all[key] = { ...current, left, top, width, height };
        localStorage.setItem(PLACE_KEY, JSON.stringify(all));
    } catch { /* this browser may block storage */ }
}
