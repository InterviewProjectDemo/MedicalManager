let stream;
let pc;
let localIce = [];
let remoteIce = [];

function rtcServers(servers) {
    const list = (servers || [])
        .map((server) => {
            const urls = server.urls || server.Urls;
            const username = server.username || server.Username || "";
            const credential = server.credential || server.Credential || "";
            const ice = { urls };
            if (username)
                ice.username = username;
            if (credential)
                ice.credential = credential;
            return ice;
        })
        .filter((server) => server.urls);
    if (!list.some((server) => String(server.urls).startsWith("stun:")))
        list.push({ urls: "stun:stun.l.google.com:19302" });
    return list;
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
    pc = new RTCPeerConnection({
        iceServers: rtcServers(servers),
        bundlePolicy: "max-bundle",
        rtcpMuxPolicy: "require"
    });
    pc.onicecandidate = (event) => {
        if (event.candidate)
            localIce.push(JSON.stringify(event.candidate.toJSON ? event.candidate.toJSON() : event.candidate));
        else
            localIce.push("end");
    };
}

function gather(peer) {
    if (!peer || peer.iceGatheringState === "complete")
        return Promise.resolve();

    return new Promise((resolve) => {
        const finish = () => {
            peer.removeEventListener("icegatheringstatechange", onState);
            clearTimeout(timer);
            resolve();
        };
        const onState = () => {
            if (peer.iceGatheringState === "complete")
                finish();
        };
        peer.addEventListener("icegatheringstatechange", onState);
        const timer = setTimeout(finish, 4000);
    });
}

function watchVideo(video) {
    if (video instanceof HTMLVideoElement)
        return video;
    const found = document.querySelector(".watch-video");
    return found instanceof HTMLVideoElement ? found : null;
}

function showRemote(video, event) {
    const element = watchVideo(video);
    if (!element)
        return;
    const remoteStream = event.streams && event.streams[0]
        ? event.streams[0]
        : new MediaStream([event.track]);
    element.srcObject = remoteStream;
    element.muted = true;
    element.playsInline = true;
    element.autoplay = true;
    const start = () => element.play().catch(() => { });
    if (element.readyState >= 2)
        start();
    else
        element.onloadedmetadata = start;
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
    const peer = pc;
    stream.getTracks().forEach((track) => peer.addTrack(track, stream));
    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    await gather(peer);
    if (pc !== peer || !peer.localDescription)
        throw new Error("Camera is off.");
    return peer.localDescription.sdp;
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
    const peer = pc;
    peer.ontrack = (event) => showRemote(video, event);
    await peer.setRemoteDescription({ type: "offer", sdp });
    await flushRemote();
    const answer = await peer.createAnswer();
    await peer.setLocalDescription(answer);
    await gather(peer);
    if (pc !== peer || !peer.localDescription)
        throw new Error("Camera is off.");
    return peer.localDescription.sdp;
}

export async function addIce(candidates) {
    for (const json of candidates || []) {
        if (json === "end") {
            try { if (pc?.remoteDescription) await pc.addIceCandidate(); } catch { /* already complete */ }
            continue;
        }
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

export function captureFrame() {
    const video = document.querySelector(".monitor-preview");
    if (!(video instanceof HTMLVideoElement) || !video.videoWidth || !video.videoHeight)
        return "";

    const width = Math.min(360, video.videoWidth);
    const height = Math.max(1, Math.round(video.videoHeight * (width / video.videoWidth)));
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d", { alpha: false });
    if (!ctx)
        return "";
    ctx.drawImage(video, 0, 0, width, height);

    let quality = 0.46;
    let data = "";
    for (let attempt = 0; attempt < 4; attempt++) {
        data = canvas.toDataURL("image/jpeg", quality);
        if (data.length <= 32000)
            break;
        quality -= 0.1;
    }

    const comma = data.indexOf(",");
    const jpeg = comma >= 0 ? data.slice(comma + 1) : "";
    return jpeg.length > 0 && jpeg.length <= 48000 ? jpeg : "";
}

export function connectionState() {
    if (!pc)
        return "new";
    const ice = pc.iceConnectionState;
    if (ice === "connected" || ice === "completed")
        return "connected";
    if (ice === "failed" || ice === "closed")
        return "failed";
    if (ice === "disconnected")
        return "disconnected";
    return pc.connectionState || ice || "new";
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
