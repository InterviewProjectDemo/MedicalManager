const STORAGE_KEY = "mm-family-button";
const DRAG_THRESHOLD = 24;
const MIN_W = 168;
const MIN_H = 76;
const DEFAULT_W = 240;
const DEFAULT_H = 96;

const state = {
    dock: null,
    float: null,
    gesture: null
};

let dockAbort = null;
let floatAbort = null;

export function bindDock(root) {
    if (!root) return;
    if (state.dock === root && dockAbort) {
        applyFromStorage();
        return;
    }

    dockAbort?.abort();
    dockAbort = new AbortController();
    state.dock = root;
    listenForDrag(root, dockAbort.signal, false);
    applyFromStorage();
}

export function bindFloat(root) {
    if (!root) return;
    if (state.float === root && floatAbort) {
        applyFromStorage();
        return;
    }

    floatAbort?.abort();
    floatAbort = new AbortController();
    state.float = root;
    const signal = floatAbort.signal;
    listenForDrag(root, signal, true);

    const resize = root.querySelector(".family-resize");
    resize?.addEventListener("pointerdown", (event) => startResize(event, root), { signal });

    const dock = root.querySelector(".family-dock");
    dock?.addEventListener("click", (event) => {
        event.preventDefault();
        event.stopPropagation();
        park();
    }, { signal });

    applyFromStorage();
}

export function unbindDock(root) {
    if (state.dock !== root) return;
    dockAbort?.abort();
    dockAbort = null;
    state.dock = null;
}

export function unbindFloat(root) {
    if (state.float !== root) return;
    floatAbort?.abort();
    floatAbort = null;
    state.float = null;
}

function listenForDrag(root, signal, fromFloat) {
    const face = root.querySelector(".family-face");
    if (!face) return;

    face.addEventListener("pointerdown", (event) => {
        if (!event.isPrimary || event.button !== 0) return;
        startDrag(event, face, fromFloat);
    }, { signal });

    face.addEventListener("click", (event) => {
        if (face.dataset.suppressClick !== "1") return;
        face.dataset.suppressClick = "0";
        event.preventDefault();
        event.stopPropagation();
    }, true);

    if (fromFloat) {
        window.addEventListener("resize", () => {
            const saved = load();
            if (!saved || !state.float) return;
            const placed = save(saved);
            place(state.float, placed);
        }, { signal });
    }
}

function startDrag(event, face, fromFloat) {
    const rect = face.getBoundingClientRect();
    const saved = load();
    state.gesture = {
        kind: "drag",
        fromFloat,
        root: face,
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        offsetX: event.clientX - rect.left,
        offsetY: event.clientY - rect.top,
        origin: saved && fromFloat
            ? saved
            : { x: rect.left, y: rect.top, w: rect.width, h: rect.height },
        moved: false,
        ghost: null,
        box: null
    };
    try { face.setPointerCapture(event.pointerId); } catch { /* Capture is optional; move events still update the button. */ }
    face.addEventListener("pointermove", onPointerMove);
    face.addEventListener("pointerup", onPointerUp);
    face.addEventListener("pointercancel", onPointerCancel);
}

function startResize(event, host) {
    if (!event.isPrimary || event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    const saved = load();
    if (!saved) return;
    const handle = event.currentTarget;
    state.gesture = {
        kind: "resize",
        fromFloat: true,
        root: handle,
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        origin: saved,
        moved: false,
        ghost: null,
        box: null
    };
    try { handle.setPointerCapture(event.pointerId); } catch { /* Capture is optional; move events still update the button. */ }
    handle.addEventListener("pointermove", onPointerMove);
    handle.addEventListener("pointerup", onPointerUp);
    handle.addEventListener("pointercancel", onPointerCancel);
}

function onPointerMove(event) {
    const gesture = state.gesture;
    if (!gesture || event.pointerId !== gesture.pointerId) return;

    const dx = event.clientX - gesture.startX;
    const dy = event.clientY - gesture.startY;
    if (!gesture.moved && Math.hypot(dx, dy) < (gesture.kind === "resize" ? 2 : DRAG_THRESHOLD)) {
        return;
    }

    gesture.moved = true;
    document.body.classList.add("family-dragging");
    event.preventDefault();

    if (gesture.kind === "resize") {
        gesture.box = clamp({
            x: gesture.origin.x,
            y: gesture.origin.y,
            w: gesture.origin.w + dx,
            h: gesture.origin.h + dy
        });
        if (state.float) place(state.float, gesture.box);
        return;
    }

    let width = gesture.origin.w;
    let height = gesture.origin.h;
    if (!gesture.fromFloat && !pointerInsideSidebar(event.clientX, event.clientY)) {
        width = Math.max(width, DEFAULT_W);
        height = Math.max(height, DEFAULT_H);
    }

    const next = clamp({
        x: event.clientX - gesture.offsetX,
        y: event.clientY - gesture.offsetY,
        w: width,
        h: height
    });
    gesture.box = next;
    gesture.overNav = releasedOverNav(event, next);
    if (gesture.overNav) {
        showNavHome();
        if (gesture.ghost) gesture.ghost.style.visibility = "hidden";
        return;
    }

    if (gesture.ghost) gesture.ghost.style.visibility = "";

    if (gesture.fromFloat && state.float) {
        place(state.float, next);
        return;
    }

    if (!gesture.ghost) {
        gesture.ghost = document.createElement("div");
        gesture.ghost.className = "family-drag-ghost";
        gesture.ghost.textContent = "Family";
        gesture.ghost.setAttribute("aria-hidden", "true");
        document.body.appendChild(gesture.ghost);
        if (state.dock) state.dock.style.opacity = "0.35";
    }

    gesture.ghost.style.left = next.x + "px";
    gesture.ghost.style.top = next.y + "px";
    gesture.ghost.style.width = next.w + "px";
    gesture.ghost.style.height = next.h + "px";
    gesture.ghost.style.fontSize = fontSizeFor(next.h);
}

function onPointerUp(event) {
    finishGesture(event, true);
}

function onPointerCancel(event) {
    finishGesture(event, false);
}

function finishGesture(event, commit) {
    const gesture = state.gesture;
    if (!gesture || (event && event.pointerId !== gesture.pointerId)) return;
    state.gesture = null;
    detachGesture(gesture);
    document.body.classList.remove("family-dragging");
    gesture.ghost?.remove();
    if (state.dock) state.dock.style.opacity = "";

    if (!gesture.moved || !commit) {
        if (gesture.fromFloat) {
            const saved = load();
            if (saved && state.float) place(state.float, saved);
            else showNavHome();
        }
        return;
    }

    if (gesture.kind === "drag") {
        gesture.root.dataset.suppressClick = "1";
        window.setTimeout(() => {
            if (gesture.root.dataset.suppressClick === "1") gesture.root.dataset.suppressClick = "0";
        }, 400);
    }

    const next = gesture.box ?? clamp({
        x: gesture.origin.x,
        y: gesture.origin.y,
        w: gesture.origin.w,
        h: gesture.origin.h
    });

    if (gesture.kind === "drag" && (gesture.overNav || releasedOverNav(event, next))) {
        park();
        return;
    }

    const placed = clamp(next);
    save(placed);
    if (state.float) place(state.float, placed);
    else remember(placed);
}

function detachGesture(gesture) {
    gesture.root.removeEventListener("pointermove", onPointerMove);
    gesture.root.removeEventListener("pointerup", onPointerUp);
    gesture.root.removeEventListener("pointercancel", onPointerCancel);
}

function showNavHome() {
    document.documentElement.classList.remove("family-btn-floating");
    if (state.dock) state.dock.style.opacity = "";
    if (state.float) {
        state.float.style.left = "";
        state.float.style.top = "";
        state.float.style.width = "";
        state.float.style.height = "";
    }
}

function park() {
    localStorage.removeItem(STORAGE_KEY);
    const root = document.documentElement;
    for (const name of ["--family-x", "--family-y", "--family-w", "--family-h"]) {
        root.style.removeProperty(name);
    }
    showNavHome();
}

function applyFromStorage() {
    const saved = load();
    if (!saved) return;
    if (state.float) place(state.float, saved);
    else remember(saved);
}

function place(element, box) {
    remember(box);
    element.style.left = box.x + "px";
    element.style.top = box.y + "px";
    element.style.width = box.w + "px";
    element.style.height = box.h + "px";
}

function remember(box) {
    const root = document.documentElement;
    root.classList.add("family-btn-floating");
    root.style.setProperty("--family-x", box.x + "px");
    root.style.setProperty("--family-y", box.y + "px");
    root.style.setProperty("--family-w", box.w + "px");
    root.style.setProperty("--family-h", box.h + "px");
}

function load() {
    try {
        const parsed = JSON.parse(localStorage.getItem(STORAGE_KEY) || "null");
        if (!parsed || parsed.floating !== true) return null;
        const box = {
            x: numberOrNull(parsed.x),
            y: numberOrNull(parsed.y),
            w: numberOrNull(parsed.w),
            h: numberOrNull(parsed.h)
        };
        if (box.x == null || box.y == null || box.w == null || box.h == null) return null;
        return clamp(box);
    } catch {
        return null;
    }
}

function save(box) {
    const placed = clamp(box);
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
        floating: true,
        x: Math.round(placed.x),
        y: Math.round(placed.y),
        w: Math.round(placed.w),
        h: Math.round(placed.h)
    }));
    return placed;
}

function clamp(box) {
    const maxW = Math.max(MIN_W, Math.min(720, window.innerWidth - 16));
    const maxH = Math.max(MIN_H, Math.min(420, window.innerHeight - 16));
    const w = Math.min(maxW, Math.max(MIN_W, box.w));
    const h = Math.min(maxH, Math.max(MIN_H, box.h));
    const minX = Math.min(8, 72 - w);
    const maxX = Math.max(minX, window.innerWidth - 72);
    const minY = 28;
    const maxY = Math.max(minY, window.innerHeight - 72);
    return {
        x: Math.min(maxX, Math.max(minX, box.x)),
        y: Math.min(maxY, Math.max(minY, box.y)),
        w,
        h
    };
}

function pointerInsideSidebar(x, y) {
    const sidebar = document.querySelector(".sidebar");
    if (!sidebar) return false;
    const rect = sidebar.getBoundingClientRect();
    return x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
}

function releasedOverNav(event, box) {
    if (event && pointerInsideSidebar(event.clientX, event.clientY)) return true;
    if (!box) return false;
    const sidebar = document.querySelector(".sidebar");
    if (!sidebar) return false;
    const rect = sidebar.getBoundingClientRect();
    const overlapW = Math.min(box.x + box.w, rect.right) - Math.max(box.x, rect.left);
    const overlapH = Math.min(box.y + box.h, rect.bottom) - Math.max(box.y, rect.top);
    if (overlapW <= 0 || overlapH <= 0) return false;
    if (pointerInsideSidebar(box.x + box.w / 2, box.y + box.h / 2)) return true;
    const sidebarArea = Math.max(1, rect.width * Math.min(rect.height, box.h));
    return overlapW * overlapH >= sidebarArea * 0.45;
}

function numberOrNull(value) {
    return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function fontSizeFor(height) {
    return `clamp(1.35rem, ${Math.round(height * 0.34)}px, 3.25rem)`;
}
