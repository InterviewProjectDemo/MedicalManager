function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
}

export function frame(panel, mode) {
    const rect = panel.getBoundingClientRect();
    const viewWidth = window.innerWidth;
    const viewHeight = window.innerHeight;
    let left = rect.left;
    let top = rect.top;
    let width = rect.width;
    let height = rect.height;

    if (mode === "full") {
        if (panel.dataset.floatFull !== "1") {
            panel.dataset.floatRestore = JSON.stringify({
                left: rect.left,
                top: rect.top,
                width: rect.width,
                height: rect.height
            });
        }
        panel.dataset.floatFull = "1";
        left = 0;
        top = 0;
        width = viewWidth;
        height = viewHeight;
    } else if (panel.dataset.floatFull === "1" && panel.dataset.floatRestore) {
        const saved = JSON.parse(panel.dataset.floatRestore);
        delete panel.dataset.floatFull;
        delete panel.dataset.floatRestore;
        left = saved.left;
        top = saved.top;
        width = saved.width;
        height = saved.height;
    } else {
        delete panel.dataset.floatFull;
        delete panel.dataset.floatRestore;
        width = clamp(Math.round(rect.width * 0.82), 240, rect.width);
        height = clamp(Math.round(rect.height * 0.82), 180, rect.height);
        left = clamp(rect.left, 0, Math.max(0, viewWidth - width));
        top = clamp(rect.top, 0, Math.max(0, viewHeight - height));
    }

    const z = Number(panel.style.zIndex) || (window.__mmFloatZ || 4100);
    return { left, top, width, height, z, viewWidth, viewHeight };
}
