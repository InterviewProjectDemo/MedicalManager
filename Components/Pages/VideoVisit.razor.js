export async function startPreview(video) {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia)
        return "unavailable";

    try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: true });
        video.srcObject = stream;
        await video.play();
        return "live";
    } catch {
        return "blocked";
    }
}

export function stopPreview(video) {
    const stream = video && video.srcObject;
    if (!stream)
        return;

    for (const track of stream.getTracks())
        track.stop();
    video.srcObject = null;
}
