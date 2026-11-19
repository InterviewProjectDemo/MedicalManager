let pc = null;
let localStream = null;
let dotNet = null;
const pendingIce = [];

export async function startCall(localVideo, remoteVideo, dotNetRef) {
    hangup(localVideo, remoteVideo);
    dotNet = dotNetRef;
    localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });
    localVideo.srcObject = localStream;
    await localVideo.play().catch(() => { });

    pc = new RTCPeerConnection({ iceServers: [{ urls: "stun:stun.l.google.com:19302" }] });
    for (const track of localStream.getTracks())
        pc.addTrack(track, localStream);

    pc.ontrack = (event) => {
        const [stream] = event.streams;
        if (!stream)
            return;
        remoteVideo.srcObject = stream;
        remoteVideo.play().catch(() => { });
    };
    pc.onicecandidate = (event) => {
        if (!event.candidate || !dotNet)
            return;
        dotNet.invokeMethodAsync("OnLocalIce", JSON.stringify(event.candidate.toJSON()));
    };
    pc.onconnectionstatechange = () => {
        if (dotNet && pc)
            dotNet.invokeMethodAsync("OnConnectionState", pc.connectionState);
    };
}

export async function makeOffer() {
    if (!pc)
        throw new Error("Call is not ready.");
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    return JSON.stringify(pc.localDescription);
}

export async function takeOffer(sdpJson) {
    if (!pc)
        throw new Error("Call is not ready.");
    await pc.setRemoteDescription(JSON.parse(sdpJson));
    await flushIce();
    const answer = await pc.createAnswer();
    await pc.setLocalDescription(answer);
    return JSON.stringify(pc.localDescription);
}

export async function takeAnswer(sdpJson) {
    if (!pc)
        throw new Error("Call is not ready.");
    if (pc.signalingState === "stable")
        return;
    await pc.setRemoteDescription(JSON.parse(sdpJson));
    await flushIce();
}

export async function addIce(candidateJson) {
    const candidate = JSON.parse(candidateJson);
    if (!pc || !pc.remoteDescription)
    {
        pendingIce.push(candidate);
        return;
    }
    await pc.addIceCandidate(candidate);
}

export function resume(localVideo, remoteVideo) {
    localVideo?.play?.().catch(() => { });
    remoteVideo?.play?.().catch(() => { });
}

export function clearRemote(remoteVideo) {
    if (remoteVideo)
        remoteVideo.srcObject = null;
}

export function hangup(localVideo, remoteVideo) {
    pendingIce.length = 0;
    if (pc)
    {
        pc.ontrack = null;
        pc.onicecandidate = null;
        pc.onconnectionstatechange = null;
        pc.close();
        pc = null;
    }
    if (localStream)
    {
        for (const track of localStream.getTracks())
            track.stop();
        localStream = null;
    }
    if (localVideo)
        localVideo.srcObject = null;
    if (remoteVideo)
        remoteVideo.srcObject = null;
    dotNet = null;
}

async function flushIce() {
    while (pendingIce.length > 0 && pc)
        await pc.addIceCandidate(pendingIce.shift());
}
