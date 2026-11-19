export function stick(element) {
    if (!element)
        return;
    element.scrollTop = element.scrollHeight;
}
