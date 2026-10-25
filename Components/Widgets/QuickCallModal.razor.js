export function watchGps(dotNetRef) {
    if (!navigator.geolocation) {
        dotNetRef.invokeMethodAsync("OnGpsError", 2);
        return 0;
    }

    return navigator.geolocation.watchPosition(
        (position) => {
            dotNetRef.invokeMethodAsync(
                "OnGpsFix",
                position.coords.latitude,
                position.coords.longitude,
                position.coords.accuracy);
        },
        (error) => {
            dotNetRef.invokeMethodAsync("OnGpsError", error.code);
        },
        { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 });
}

export function stopGps(id) {
    if (id)
        navigator.geolocation.clearWatch(id);
}

let sdkPromise;
let device;
let activeCall;

function loadSdk() {
    if (window.Twilio?.Device)
        return Promise.resolve(window.Twilio);
    if (!sdkPromise) {
        sdkPromise = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = "https://cdn.jsdelivr.net/npm/@twilio/voice-sdk@2.15.0/dist/twilio.min.js";
            script.onload = () => window.Twilio?.Device
                ? resolve(window.Twilio)
                : reject(new Error("Calling audio could not be loaded."));
            script.onerror = () => reject(new Error("Calling audio could not be loaded."));
            document.head.appendChild(script);
        });
    }
    return sdkPromise;
}

function notify(dotNetRef, method, arg) {
    const pending = arg === undefined
        ? dotNetRef.invokeMethodAsync(method)
        : dotNetRef.invokeMethodAsync(method, arg);
    pending.catch(() => { });
}

export async function startFamilyCall(token, ticket, dotNetRef) {
    const Twilio = await loadSdk();
    endFamilyCall();
    device = new Twilio.Device(token, {
        closeProtection: true,
        codecPreferences: ["opus", "pcmu"]
    });
    device.on("error", (error) => {
        notify(dotNetRef, "OnFamilyCallError", friendlyDeviceError(error));
    });
    await device.register();
    try {
        const outputs = await navigator.mediaDevices.enumerateDevices();
        const speaker = outputs.find((item) => item.kind === "audiooutput" && /speaker/i.test(item.label));
        await device.audio.speakerDevices.set(speaker ? speaker.deviceId : "default");
    } catch {
        // The browser keeps the default speaker when it cannot list outputs.
    }

    const call = await device.connect({ params: { ticket } });
    activeCall = call;
    call.on("accept", () => notify(dotNetRef, "OnFamilyCallConnected"));
    call.on("disconnect", () => notify(dotNetRef, "OnFamilyCallEnded"));
    call.on("cancel", () => notify(dotNetRef, "OnFamilyCallEnded"));
    call.on("reject", () => notify(dotNetRef, "OnFamilyCallError", "The call was declined."));
    call.on("error", (error) => notify(dotNetRef, "OnFamilyCallError", friendlyDeviceError(error)));
}

export function endFamilyCall() {
    if (activeCall) {
        activeCall.disconnect();
        activeCall = null;
    }
    if (device) {
        device.disconnectAll();
        device.destroy();
        device = null;
    }
}

function friendlyDeviceError(error) {
    const message = error?.message || "";
    console.error("family-call", message);
    if (/permission|microphone|notallowed|denied/i.test(message))
        return "Allow the microphone so you can talk.";
    return "The call could not be placed.";
}
