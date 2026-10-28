export function begin(panel, grip, pointerId) {
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
