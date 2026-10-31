const STORAGE_KEY = "mm-family-button";
const DRAG_THRESHOLD = 24;
const MIN_W = 168;
const MIN_H = 76;
const DEFAULT_W = 240;
const DEFAULT_H = 96;
const MAX_W = 720;
const MAX_H = 420;

let mode = null;
let moved = false;
let suppressClick = false;
let startedOnFloatFace = false;
let activePointer = null;
let startX = 0;
let startY = 0;
let originX = 0;
let originY = 0;
let originW = 0;
let originH = 0;
let opener = null;

export function bind(dotnetRef) {
    if (dotnetRef)
        opener = dotnetRef;
    if (window.__mmFamilyBound)
        return;
    window.__mmFamilyBound = true;
    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("click", onClickCapture, true);
}

function onPointerDown(event) {
    if (event.button !== undefined && event.button !== 0)
        return;
    if (event.target.closest?.(".family-menu"))
        return;

    const resize = event.target.closest?.(".family-resize");
    const dock = event.target.closest?.("[data-family-dock]");
    const floatFace = event.target.closest?.("[data-family-float] .family-face");
    if (!resize && !dock && !floatFace)
        return;

    const floating = isFloating();
    const source = floating ? document.querySelector("[data-family-float]") : dock;
    const rect = source?.getBoundingClientRect();
    if (!rect)
        return;

    mode = resize ? "resize" : "move";
    moved = false;
    startedOnFloatFace = !!floatFace;
    activePointer = event.pointerId;
    startX = event.clientX;
    startY = event.clientY;
    originX = floating ? readPx("--family-x", rect.left) : rect.left;
    originY = floating ? readPx("--family-y", rect.top) : rect.top;
    originW = floating ? readPx("--family-w", rect.width) : rect.width;
    originH = floating ? readPx("--family-h", rect.height) : rect.height;

    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", onPointerUp);
    window.addEventListener("pointercancel", onPointerUp);
}

function onPointerMove(event) {
    if (event.pointerId !== activePointer || !mode)
        return;

    const dx = event.clientX - startX;
    const dy = event.clientY - startY;
    if (!moved && Math.hypot(dx, dy) < DRAG_THRESHOLD)
        return;

    if (!moved) {
        moved = true;
        try { event.target.setPointerCapture(event.pointerId); } catch { /* synthetic events */ }
        if (mode === "move" && !isFloating()) {
            originW = DEFAULT_W;
            originH = DEFAULT_H;
            originX = event.clientX - DEFAULT_W / 2;
            originY = event.clientY - DEFAULT_H / 2;
            startX = event.clientX;
            startY = event.clientY;
        }
    }

    if (mode === "resize") {
        apply({
            floating: true,
            x: originX,
            y: originY,
            w: clamp(originW + (event.clientX - startX), MIN_W, Math.min(MAX_W, window.innerWidth - 16)),
            h: clamp(originH + (event.clientY - startY), MIN_H, Math.min(MAX_H, window.innerHeight - 16))
        });
        return;
    }

    const dx2 = event.clientX - startX;
    const dy2 = event.clientY - startY;
    apply({
        floating: true,
        x: clampPosition(originX + dx2, originW, window.innerWidth),
        y: clampPosition(originY + dy2, originH, window.innerHeight),
        w: originW,
        h: originH
    });
}

function onPointerUp(event) {
    if (event.pointerId !== activePointer)
        return;

    window.removeEventListener("pointermove", onPointerMove);
    window.removeEventListener("pointerup", onPointerUp);
    window.removeEventListener("pointercancel", onPointerUp);

    if (moved) {
        suppressClick = true;
        const host = document.querySelector("[data-family-float]");
        const rect = host?.getBoundingClientRect();
        if (mode === "move" && rect && releasedOverNav(event.clientX, event.clientY, rect))
            park();
        else
            persist();
    } else if (startedOnFloatFace && opener) {
        suppressClick = true;
        opener.invokeMethodAsync("OpenFromFace");
    }

    mode = null;
    activePointer = null;
}

function onClickCapture(event) {
    if (event.target.closest?.(".family-menu")) {
        event.preventDefault();
        event.stopPropagation();
        park();
        return;
    }

    if (event.target.closest?.(".family-resize")) {
        event.preventDefault();
        event.stopPropagation();
        return;
    }

    if (!suppressClick)
        return;

    suppressClick = false;
    const face = event.target.closest?.("[data-family-dock], [data-family-float] .family-face");
    if (!face)
        return;

    event.preventDefault();
    event.stopPropagation();
}

function releasedOverNav(clientX, clientY, rect) {
    const zones = navZones();
    if (zones.length === 0)
        return false;

    for (const zone of zones) {
        if (clientX >= zone.left && clientX <= zone.right && clientY >= zone.top && clientY <= zone.bottom)
            return true;
        const cx = rect.left + rect.width / 2;
        const cy = rect.top + rect.height / 2;
        if (cx >= zone.left && cx <= zone.right && cy >= zone.top && cy <= zone.bottom)
            return true;
        const overlapW = Math.max(0, Math.min(rect.right, zone.right) - Math.max(rect.left, zone.left));
        const overlapH = Math.max(0, Math.min(rect.bottom, zone.bottom) - Math.max(rect.top, zone.top));
        const limit = zone.width * Math.min(zone.height, rect.height) * 0.45;
        if (overlapW * overlapH >= limit)
            return true;
    }

    return false;
}

function navZones() {
    const zones = [];
    for (const el of document.querySelectorAll(".sidebar, .nav-scrollable")) {
        const style = getComputedStyle(el);
        if (style.display === "none" || style.visibility === "hidden" || style.pointerEvents === "none")
            continue;
        const rect = el.getBoundingClientRect();
        if (rect.width < 8 || rect.height < 8)
            continue;
        zones.push(rect);
    }
    return zones;
}

function park() {
    try { localStorage.removeItem(STORAGE_KEY); } catch { /* private mode */ }
    const root = document.documentElement;
    root.classList.remove("family-btn-floating");
    root.style.removeProperty("--family-x");
    root.style.removeProperty("--family-y");
    root.style.removeProperty("--family-w");
    root.style.removeProperty("--family-h");
}

function persist() {
    const state = {
        floating: true,
        x: readPx("--family-x", 16),
        y: readPx("--family-y", 16),
        w: readPx("--family-w", DEFAULT_W),
        h: readPx("--family-h", DEFAULT_H)
    };
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch { /* private mode */ }
}

function apply(state) {
    const root = document.documentElement;
    root.classList.add("family-btn-floating");
    root.style.setProperty("--family-x", `${Math.round(state.x)}px`);
    root.style.setProperty("--family-y", `${Math.round(state.y)}px`);
    root.style.setProperty("--family-w", `${Math.round(state.w)}px`);
    root.style.setProperty("--family-h", `${Math.round(state.h)}px`);
}

function isFloating() {
    return document.documentElement.classList.contains("family-btn-floating");
}

function readPx(name, fallback) {
    const raw = document.documentElement.style.getPropertyValue(name);
    const value = Number.parseFloat(raw);
    return Number.isFinite(value) ? value : fallback;
}

function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
}

function clampPosition(value, size, limit) {
    const min = 48 - size;
    const max = Math.max(min, limit - 48);
    return clamp(value, min, max);
}
