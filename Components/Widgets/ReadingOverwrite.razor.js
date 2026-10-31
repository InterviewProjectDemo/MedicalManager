export function focusEnd(element) {
    if (!element) return;
    element.focus();
    const length = typeof element.value === "string" ? element.value.length : 0;
    if (typeof element.setSelectionRange === "function") {
        try {
            element.setSelectionRange(length, length);
        } catch {
            // Some browsers reject a selection range on this control.
        }
    }
}

export function focusIndex(list, index) {
    const option = list?.querySelector(`[data-note-option="${index}"]`);
    option?.focus();
}
