export function initDropZone(element, dotNetRef) {
    const prevent = (event) => {
        event.preventDefault();
        event.stopPropagation();
    };

    const onDragOver = (event) => {
        prevent(event);
        element.classList.add("profile-photo-drop-active");
    };

    const onDragLeave = (event) => {
        prevent(event);
        element.classList.remove("profile-photo-drop-active");
    };

    const onDrop = async (event) => {
        prevent(event);
        element.classList.remove("profile-photo-drop-active");

        const files = event.dataTransfer?.files;
        if (!files?.length) {
            return;
        }

        const file = files[0];
        const base64 = await readFileAsBase64(file);
        await dotNetRef.invokeMethodAsync("OnDroppedFileAsync", file.name, file.type || "", file.size, base64);
    };

    element.addEventListener("dragenter", prevent);
    element.addEventListener("dragover", onDragOver);
    element.addEventListener("dragleave", onDragLeave);
    element.addEventListener("drop", onDrop);

    return {
        dispose: () => {
            element.removeEventListener("dragenter", prevent);
            element.removeEventListener("dragover", onDragOver);
            element.removeEventListener("dragleave", onDragLeave);
            element.removeEventListener("drop", onDrop);
        }
    };
}

function readFileAsBase64(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => {
            const result = reader.result;
            if (typeof result !== "string") {
                reject(new Error("Unable to read file."));
                return;
            }

            const comma = result.indexOf(",");
            resolve(comma >= 0 ? result.slice(comma + 1) : result);
        };
        reader.onerror = () => reject(reader.error ?? new Error("Unable to read file."));
        reader.readAsDataURL(file);
    });
}

let activeStream = null;

function resolveVideo(videoElement) {
    if (videoElement instanceof HTMLVideoElement) {
        return videoElement;
    }

    return document.getElementById("profile-photo-camera")
        || document.querySelector("video.profile-photo-video");
}

export async function startCamera(videoElement) {
    const video = resolveVideo(videoElement);
    stopCamera(video);

    if (!(video instanceof HTMLVideoElement)) {
        throw new Error("Camera preview is not ready yet.");
    }

    if (!navigator.mediaDevices?.getUserMedia) {
        throw new Error("Camera is not supported in this browser.");
    }

    try {
        activeStream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: "user" },
            audio: false
        });
    } catch {
        activeStream = await navigator.mediaDevices.getUserMedia({
            video: true,
            audio: false
        });
    }

    video.muted = true;
    video.playsInline = true;
    video.autoplay = true;
    video.srcObject = activeStream;
    await video.play();
}

function waitForFrame(video) {
    if (video.videoWidth > 0 && video.readyState >= 2)
        return Promise.resolve();

    return new Promise((resolve, reject) => {
        const timer = window.setTimeout(() => {
            reject(new Error("Camera preview is not ready yet."));
        }, 3000);
        const finish = () => {
            if (!video.videoWidth)
                return;
            window.clearTimeout(timer);
            video.removeEventListener("loadeddata", finish);
            resolve();
        };
        video.addEventListener("loadeddata", finish);
        if (typeof video.requestVideoFrameCallback === "function")
            video.requestVideoFrameCallback(() => finish());
        finish();
    });
}

async function frameFromCamera(video) {
    const started = performance.now();
    while (!activeStream && performance.now() - started < 4000)
        await new Promise((resolve) => window.setTimeout(resolve, 100));

    if (activeStream && video.srcObject !== activeStream) {
        video.muted = true;
        video.playsInline = true;
        video.srcObject = activeStream;
        try { await video.play(); } catch { }
    }

    const track = activeStream?.getVideoTracks?.()[0];
    if (track && typeof ImageCapture === "function") {
        try {
            const shot = new ImageCapture(track);
            if (typeof shot.grabFrame === "function")
                return await shot.grabFrame();
        } catch { }
    }

    await waitForFrame(video);
    if (!video.videoWidth || !video.videoHeight)
        throw new Error("Camera preview is not ready yet.");
    return video;
}

function jpegPayload(source, sourceWidth, sourceHeight) {
    const scale = Math.min(1, 800 / Math.max(sourceWidth, sourceHeight));
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(sourceWidth * scale));
    canvas.height = Math.max(1, Math.round(sourceHeight * scale));
    const context = canvas.getContext("2d");
    if (!context)
        throw new Error("Could not capture the photo.");
    context.drawImage(source, 0, 0, canvas.width, canvas.height);

    let quality = 0.86;
    let dataUrl = canvas.toDataURL("image/jpeg", quality);
    while (dataUrl.length > 380000 && quality > 0.5) {
        quality -= 0.08;
        dataUrl = canvas.toDataURL("image/jpeg", quality);
    }

    const comma = dataUrl.indexOf(",");
    return comma >= 0 ? dataUrl.slice(comma + 1) : dataUrl;
}

export async function capturePhoto(videoElement) {
    const video = resolveVideo(videoElement);
    if (!(video instanceof HTMLVideoElement))
        throw new Error("Camera preview is not ready yet.");

    const source = await frameFromCamera(video);
    const width = source.videoWidth || source.width;
    const height = source.videoHeight || source.height;
    if (!width || !height)
        throw new Error("Camera preview is not ready yet.");
    try {
        return jpegPayload(source, width, height);
    } finally {
        if (source !== video && typeof source.close === "function")
            source.close();
    }
}

export function stopCamera(videoElement) {
    if (activeStream) {
        activeStream.getTracks().forEach((track) => track.stop());
        activeStream = null;
    }

    const video = resolveVideo(videoElement);
    if (video instanceof HTMLVideoElement) {
        video.srcObject = null;
    }
}
