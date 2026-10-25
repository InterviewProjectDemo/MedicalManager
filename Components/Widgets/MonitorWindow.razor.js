let stream;
let pc;
let localIce = [];
let remoteIce = [];

function rtcServers(servers) {
    const list = (servers || [])
        .map((server) => ({
            urls: server.urls || server.Urls,
            username: server.username || server.Username || undefined,
            credential: server.credential || server.Credential || undefined
        }))
        .filter((server) => server.urls);
    return list.length > 0 ? list : [{ urls: "stun:stun.l.google.com:19302" }];
}

function resetPeer(servers) {
    if (pc) {
        pc.onicecandidate = null;
        pc.ontrack = null;
        pc.onconnectionstatechange = null;
        try { pc.close(); } catch { /* already closed */ }
    }
    localIce = [];
    remoteIce = [];
    pc = new RTCPeerConnection({ iceServers: rtcServers(servers) });
    pc.onicecandidate = (event) => {
        if (event.candidate)
            localIce.push(JSON.stringify(event.candidate));
    };
}

async function flushRemote() {
    if (!pc)
        return;
    const batch = remoteIce;
    remoteIce = [];
    for (const candidate of batch) {
        try { await pc.addIceCandidate(candidate); } catch { /* stale candidate */ }
    }
}

let arming;
let gestureInstalled = false;

function liveStream() {
    return stream && stream.getTracks().some((track) => track.readyState === "live") ? stream : null;
}

async function openCamera() {
    const existing = liveStream();
    if (existing)
        return existing;

    if (stream) {
        stream.getTracks().forEach((track) => track.stop());
        stream = null;
    }

    const attempts = [
        { audio: false, video: true },
        { audio: false, video: { facingMode: "user" } }
    ];
    let lastError;
    for (const constraints of attempts) {
        try {
            stream = await navigator.mediaDevices.getUserMedia(constraints);
            return stream;
        } catch (error) {
            lastError = error;
            if (error?.name === "NotAllowedError" || error?.name === "SecurityError")
                break;
        }
    }

    const error = lastError || new Error("NotAllowedError");
    const name = error.name || "Error";
    throw new Error(name + ": " + (error.message || "The camera did not start."));
}

export function installCameraGesture() {
    if (gestureInstalled)
        return;
    gestureInstalled = true;
    document.addEventListener("click", (event) => {
        const target = event.target instanceof Element
            ? event.target.closest("[data-monitor-camera]")
            : null;
        if (!target || arming)
            return;
        const pending = openCamera();
        arming = pending;
        pending.catch(() => {
            if (arming === pending)
                arming = null;
        });
    }, true);
}

export async function startPreview(video) {
    const preview = video instanceof HTMLVideoElement
        ? video
        : document.querySelector(".monitor-preview");
    if (!(preview instanceof HTMLVideoElement))
        throw new Error("NotReady: The picture window is not ready.");

    try {
        stream = await (arming || openCamera());
    } finally {
        arming = null;
    }

    preview.srcObject = stream;
    preview.muted = true;
    preview.playsInline = true;
    try { await preview.play(); } catch { /* The picture is attached even if autoplay waits. */ }
    return true;
}

export async function createOffer(servers) {
    if (!stream)
        throw new Error("Camera is off.");

    resetPeer(servers);
    stream.getTracks().forEach((track) => pc.addTrack(track, stream));
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    return pc.localDescription.sdp;
}

export async function acceptAnswer(sdp) {
    if (!pc || !sdp || pc.signalingState !== "have-local-offer")
        return false;

    await pc.setRemoteDescription({ type: "answer", sdp });
    await flushRemote();
    return true;
}

export async function createAnswer(video, sdp, servers) {
    resetPeer(servers);
    pc.ontrack = (event) => {
        const remoteStream = event.streams[0] || new MediaStream([event.track]);
        video.srcObject = remoteStream;
        video.play().catch(() => { });
    };
    await pc.setRemoteDescription({ type: "offer", sdp });
    await flushRemote();
    const answer = await pc.createAnswer();
    await pc.setLocalDescription(answer);
    return pc.localDescription.sdp;
}

export async function addIce(candidates) {
    for (const json of candidates || []) {
        let candidate;
        try { candidate = JSON.parse(json); } catch { continue; }
        if (!candidate?.candidate)
            continue;
        if (!pc || !pc.remoteDescription) {
            remoteIce.push(candidate);
            continue;
        }
        try { await pc.addIceCandidate(candidate); } catch { /* already applied */ }
    }
}

export function takeIce() {
    const batch = localIce;
    localIce = [];
    return batch;
}

export function connectionState() {
    return pc ? pc.connectionState : "new";
}

export function stopPeer() {
    if (!pc)
        return;
    pc.onicecandidate = null;
    pc.ontrack = null;
    try { pc.close(); } catch { /* already closed */ }
    pc = null;
    localIce = [];
    remoteIce = [];
}

export function stopAll(video) {
    stopPeer();
    if (stream) {
        stream.getTracks().forEach((track) => track.stop());
        stream = null;
    }
    if (video)
        video.srcObject = null;
}

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(String(text || ""));
        return true;
    } catch {
        return false;
    }
}
