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

export async function startPreview(video) {
    if (!stream) {
        try {
            stream = await navigator.mediaDevices.getUserMedia({
                audio: false,
                video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } }
            });
        } catch {
            stream = await navigator.mediaDevices.getUserMedia({ audio: false, video: true });
        }
    }

    video.srcObject = stream;
    video.muted = true;
    await video.play();
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
