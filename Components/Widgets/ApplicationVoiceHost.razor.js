let current = null;
let activeUrl = null;

function release(audio) {
    if (!audio) return;
    audio.onended = null;
    audio.onerror = null;
    try {
        audio.pause();
    } catch {
        /* ignore */
    }
}

export function stop() {
    release(current);
    current = null;
    if (activeUrl) {
        URL.revokeObjectURL(activeUrl);
        activeUrl = null;
    }
}

function asBytes(payload) {
    if (!payload) return null;
    if (payload instanceof Uint8Array) return payload;
    if (payload instanceof ArrayBuffer) return new Uint8Array(payload);
    if (ArrayBuffer.isView(payload)) {
        return new Uint8Array(payload.buffer, payload.byteOffset, payload.byteLength);
    }
    if (Array.isArray(payload)) return Uint8Array.from(payload);
    if (typeof payload !== "string") return null;

    const cleaned = payload.replace(/\s+/g, "").replace(/-/g, "+").replace(/_/g, "/");
    const padded = cleaned + "=".repeat((4 - (cleaned.length % 4)) % 4);
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
    return bytes;
}

// Plays neural speech through this device. Never uses the browser's mechanical reader.
// .NET hands the audio over as bytes, not as a base64 string.
export function play(payload) {
    stop();
    let bytes;
    try {
        bytes = asBytes(payload);
    } catch {
        return Promise.resolve("error");
    }
    if (!bytes || bytes.length < 200) return Promise.resolve("error");

    const blob = new Blob([bytes], { type: "audio/mpeg" });
    const url = URL.createObjectURL(blob);
    activeUrl = url;
    const audio = new Audio(url);
    current = audio;

    return new Promise((resolve) => {
        let settled = false;
        const finish = (status) => {
            if (settled) return;
            settled = true;
            resolve(status);
        };

        audio.onended = () => finish("ok");
        audio.onerror = () => finish("error");

        const started = audio.play();
        const ready = started && typeof started.then === "function" ? started : Promise.resolve();
        ready.then(() => {
            const seconds = Number.isFinite(audio.duration) ? audio.duration : 20;
            const ms = Math.min(120000, Math.max(4000, seconds * 1000 + 2000));
            setTimeout(() => finish("ok"), ms);
        }).catch((err) => {
            if (err && err.name === "NotAllowedError") finish("blocked");
            else finish("error");
        });
    });
}
