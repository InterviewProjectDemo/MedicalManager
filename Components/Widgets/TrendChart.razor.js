export function svgXFromClient(element, clientX) {
    const rect = element.getBoundingClientRect();
    if (rect.width <= 0) return 0;
    const svg = element.querySelector("svg");
    const viewWidth = svg?.viewBox?.baseVal?.width || rect.width;
    return ((clientX - rect.left) / rect.width) * viewWidth;
}

export function hostWidth(element) {
    return element.getBoundingClientRect().width;
}
