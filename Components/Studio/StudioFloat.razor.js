import { frame as computeFrame } from "../../js/window-frame.js";

const handles = new WeakMap();

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

function bringForward(panel) {
    const next = Math.min((window.__mmFloatZ || 4100) + 1, 5000);
    window.__mmFloatZ = next;
    panel.style.zIndex = String(next);
    return next;
}

function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
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

function rememberPlace(key, left, top, width, height, z) {
    if (!key)
        return;
    try {
        const all = readPlaces();
        all[key] = { left, top, width, height, z };
        localStorage.setItem(PLACE_KEY, JSON.stringify(all));
    } catch { /* this browser may block storage */ }
}

function recallPlace(key) {
    if (!key)
        return null;
    const box = readPlaces()[key];
    if (!box || !Number.isFinite(box.left) || !Number.isFinite(box.top))
        return null;
    return box;
}

function applyPlace(panel, box) {
    const viewWidth = window.innerWidth;
    const viewHeight = window.innerHeight;
    const width = clamp(box.width, 420, Math.max(420, viewWidth - 16));
    const height = clamp(box.height, 280, Math.max(280, viewHeight - 16));
    const left = clamp(box.left, 8, Math.max(8, viewWidth - Math.min(width, viewWidth - 16)));
    const top = clamp(box.top, 8, Math.max(8, viewHeight - 48));
    panel.style.left = left + "px";
    panel.style.top = top + "px";
    panel.style.width = width + "px";
    panel.style.height = height + "px";
    panel.style.right = "auto";
    panel.style.bottom = "auto";
    panel.style.transform = "none";
    return { left, top, width, height, z: box.z || (window.__mmFloatZ || 4100) };
}

export function frame(panel, mode, memoryKey) {
    const box = computeFrame(panel, mode);
    panel.style.left = box.left + "px";
    panel.style.top = box.top + "px";
    panel.style.width = box.width + "px";
    panel.style.height = box.height + "px";
    panel.style.right = "auto";
    panel.style.bottom = "auto";
    panel.style.maxWidth = "none";
    panel.style.maxHeight = "none";
    panel.style.transform = "none";
    panel.style.borderRadius = mode === "full" ? "0" : "";
    if (memoryKey)
        rememberPlace(memoryKey, box.left, box.top, box.width, box.height, box.z);
    return box;
}

export function attach(panel, dotNet, memoryKey) {
    if (!panel || handles.has(panel))
        return;

    const grip = panel.querySelector("[data-float-grip]");
    const resize = panel.querySelector("[data-float-resize]");
    if (!grip || !resize)
        return;

    const state = { mode: null };

    const finish = (event) => {
        if (!state.mode || (event.pointerId != null && state.pointerId != null && event.pointerId !== state.pointerId))
            return;
        state.mode = null;
        for (const handle of [grip, resize]) {
            try {
                if (state.pointerId != null && handle.hasPointerCapture?.(state.pointerId))
                    handle.releasePointerCapture(state.pointerId);
            } catch { /* already released */ }
        }
        const rect = panel.getBoundingClientRect();
        const z = Number(panel.style.zIndex) || (window.__mmFloatZ || 4100);
        delete panel.dataset.floatFull;
        delete panel.dataset.floatRestore;
        if (memoryKey)
            rememberPlace(memoryKey, rect.left, rect.top, rect.width, rect.height, z);
        dotNet.invokeMethodAsync("Remember", rect.left, rect.top, rect.width, rect.height, z);
    };

    const move = (event) => {
        if (!state.mode || event.pointerId !== state.pointerId)
            return;
        const dx = event.clientX - state.startX;
        const dy = event.clientY - state.startY;
        const viewWidth = window.innerWidth;
        const viewHeight = window.innerHeight;
        if (state.mode === "move") {
            const left = clamp(state.left + dx, 8, Math.max(8, viewWidth - 96));
            const top = clamp(state.top + dy, 8, Math.max(8, viewHeight - 48));
            panel.style.left = left + "px";
            panel.style.top = top + "px";
            return;
        }

        const width = clamp(state.width + dx, 420, Math.max(420, viewWidth - 16));
        const height = clamp(state.height + dy, 280, Math.max(280, viewHeight - 16));
        panel.style.width = width + "px";
        panel.style.height = height + "px";
    };

    const begin = (mode) => (event) => {
        if (event.button !== 0)
            return;
        const under = document.elementFromPoint(event.clientX, event.clientY);
        const chrome = (under instanceof Element && under.closest(".studio-float-actions, .studio-float-resize"))
            || (event.target instanceof Element && event.target.closest(".studio-float-actions, .studio-float-resize"));
        if (chrome) {
            state.mode = null;
            for (const handle of [grip, resize]) {
                try {
                    if (handle.hasPointerCapture?.(event.pointerId))
                        handle.releasePointerCapture(event.pointerId);
                } catch { /* the press is a button, not a drag */ }
            }
            return;
        }

        const rect = panel.getBoundingClientRect();
        state.mode = mode;
        state.pointerId = event.pointerId;
        state.startX = event.clientX;
        state.startY = event.clientY;
        state.left = rect.left;
        state.top = rect.top;
        state.width = rect.width;
        state.height = rect.height;
        panel.style.left = rect.left + "px";
        panel.style.top = rect.top + "px";
        panel.style.width = rect.width + "px";
        panel.style.height = rect.height + "px";
        panel.style.right = "auto";
        panel.style.bottom = "auto";
        panel.style.transform = "none";
        bringForward(panel);
        try { event.currentTarget.setPointerCapture(event.pointerId); } catch { /* drag still follows while the pointer stays down */ }
    };

    const onGrip = begin("move");
    const onResize = begin("resize");
    grip.addEventListener("pointerdown", onGrip);
    resize.addEventListener("pointerdown", onResize);
    grip.addEventListener("pointermove", move);
    resize.addEventListener("pointermove", move);
    grip.addEventListener("pointerup", finish);
    resize.addEventListener("pointerup", finish);
    grip.addEventListener("pointercancel", finish);
    resize.addEventListener("pointercancel", finish);
    window.addEventListener("pointerup", finish, true);
    window.addEventListener("pointercancel", finish, true);
    handles.set(panel, { grip, resize, onGrip, onResize });

    if (memoryKey) {
        const stored = recallPlace(memoryKey);
        if (stored) {
            const placed = applyPlace(panel, stored);
            dotNet.invokeMethodAsync("Remember", placed.left, placed.top, placed.width, placed.height, placed.z);
        }
    }
}
