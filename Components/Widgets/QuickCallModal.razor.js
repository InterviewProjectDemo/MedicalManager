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
let generation = 0;
let watchTimer = 0;

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
    const mine = generation;
    device = new Twilio.Device(token, {
        closeProtection: true,
        codecPreferences: ["opus", "pcmu"]
    });
    const ended = () => {
        if (mine !== generation)
            return;
        stopWatch();
        notify(dotNetRef, "OnFamilyCallEnded");
    };
    device.on("error", (error) => {
        if (mine !== generation)
            return;
        notify(dotNetRef, "OnFamilyCallError", callErrorDetail(error));
    });
    try {
        const outputs = await navigator.mediaDevices.enumerateDevices();
        const speaker = outputs.find((item) => item.kind === "audiooutput" && /speaker/i.test(item.label));
        await device.audio.speakerDevices.set(speaker ? speaker.deviceId : "default");
    } catch {
        // The browser keeps the default speaker when it cannot list outputs.
    }

    if (mine !== generation)
        return;

    const call = await device.connect({ params: { ticket } });
    if (mine !== generation) {
        call.disconnect();
        return;
    }

    activeCall = call;
    let sidSent = false;
    const track = () => {
        if (mine !== generation || sidSent)
            return;
        sidSent = publishCallSid(call, dotNetRef);
    };
    track();
    call.on("ringing", track);
    call.on("accept", () => {
        if (mine !== generation)
            return;
        track();
        notify(dotNetRef, "OnFamilyCallConnected");
    });
    call.on("disconnect", ended);
    call.on("cancel", ended);
    call.on("reject", ended);
    call.on("error", (error) => {
        if (mine !== generation)
            return;
        notify(dotNetRef, "OnFamilyCallError", callErrorDetail(error));
    });

    stopWatch();
    watchTimer = setInterval(() => {
        if (mine !== generation || activeCall !== call) {
            stopWatch();
            return;
        }

        if (!sidSent)
            sidSent = publishCallSid(call, dotNetRef);
        const status = typeof call.status === "function" ? call.status() : "";
        if (status === "closed")
            ended();
    }, 1000);
}

export function endFamilyCall() {
    generation += 1;
    stopWatch();
    if (activeCall) {
        const call = activeCall;
        activeCall = null;
        try {
            call.disconnect();
        } catch {
            // The call is already gone.
        }
    }
    if (device) {
        const current = device;
        device = null;
        try {
            current.disconnectAll();
        } catch {
            // The device is already gone.
        }
        try {
            current.destroy();
        } catch {
            // The device is already gone.
        }
    }
}

function stopWatch() {
    if (!watchTimer)
        return;
    clearInterval(watchTimer);
    watchTimer = 0;
}

function publishCallSid(call, dotNetRef) {
    const sid = call?.parameters?.CallSid;
    if (!sid)
        return false;
    notify(dotNetRef, "OnFamilyCallSid", sid);
    return true;
}

function callErrorDetail(error) {
    const codeValue = error?.code ?? error?.twilioError?.code;
    const code = codeValue == null ? "" : String(codeValue);
    const message = `${error?.message || ""} ${error?.description || ""}`.trim();
    if (code === "31401" || /permission|microphone|notallowed|denied/i.test(message))
        return "Allow the microphone so you can talk.";
    const detail = `${code} ${message}`.trim();
    console.error("family-call", detail);
    return detail || "The call could not be placed.";
}
