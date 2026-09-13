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

    video.srcObject = activeStream;
    await video.play();
}

export function capturePhoto(videoElement) {
    const video = resolveVideo(videoElement);
    if (!(video instanceof HTMLVideoElement) || !video.videoWidth || !video.videoHeight) {
        throw new Error("Camera preview is not ready yet.");
    }

    const canvas = document.createElement("canvas");
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext("2d").drawImage(video, 0, 0);
    const dataUrl = canvas.toDataURL("image/jpeg", 0.92);
    return dataUrl.split(",")[1];
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
