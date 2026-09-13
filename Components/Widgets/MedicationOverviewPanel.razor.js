const observers = new WeakMap();
const fallbackRowHeight = 52;
const fallbackHeaderHeight = 40;

function measureFittedRows(element) {
    const wrapHeight = element.clientHeight;
    if (wrapHeight <= 0) return 0;

    const thead = element.querySelector("thead");
    const headerH = thead?.getBoundingClientRect().height || fallbackHeaderHeight;
    const rows = element.querySelectorAll("tbody tr");
    let rowH = fallbackRowHeight;
    if (rows.length > 0) {
        let total = 0;
        rows.forEach((row) => { total += row.getBoundingClientRect().height; });
        rowH = total / rows.length;
    }
    if (rowH <= 0) rowH = fallbackRowHeight;

    const available = Math.max(0, wrapHeight - headerH);
    return Math.max(1, Math.floor(available / rowH));
}

export function observeTable(element, dotNet) {
    disposeTable(element);
    let frame = 0;

    const notify = () => {
        if (frame) cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => {
            frame = 0;
            const fitted = measureFittedRows(element);
            if (fitted <= 0) return;
            dotNet.invokeMethodAsync("OnTableResized", fitted);
        });
    };

    const observer = new ResizeObserver(notify);
    observer.observe(element);
    observers.set(element, { observer, cancel: () => { if (frame) cancelAnimationFrame(frame); } });
    notify();
}

export function disposeTable(element) {
    const entry = observers.get(element);
    if (!entry) return;
    entry.cancel();
    entry.observer.disconnect();
    observers.delete(element);
}
