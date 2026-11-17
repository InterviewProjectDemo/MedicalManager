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
        viewWidth: window.innerWidth,
        viewHeight: window.innerHeight
    };
}
