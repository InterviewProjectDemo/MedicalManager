const observers = new WeakMap();

export function svgXFromClient(element, clientX) {
    const rect = element.getBoundingClientRect();
    if (rect.width <= 0) return 0;
    const svg = element.querySelector("svg");
    const viewWidth = svg?.viewBox?.baseVal?.width || rect.width;
    return ((clientX - rect.left) / rect.width) * viewWidth;
}

export function hostSize(element) {
    return {
        width: element.clientWidth,
        height: element.clientHeight
    };
}

export function observeHost(element, dotNet) {
    disposeHost(element);
    let frame = 0;

    const notify = () => {
        if (frame) cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => {
            frame = 0;
            const width = element.clientWidth;
            const height = element.clientHeight;
            if (width <= 0 || height <= 0) return;
            dotNet.invokeMethodAsync("OnHostResized", width, height);
        });
    };

    const observer = new ResizeObserver(notify);
    observer.observe(element);
    observers.set(element, { observer, cancel: () => { if (frame) cancelAnimationFrame(frame); } });
    notify();
}

export function disposeHost(element) {
    const entry = observers.get(element);
    if (!entry) return;
    entry.cancel();
    entry.observer.disconnect();
    observers.delete(element);
}
