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
    const twilioDevice = new Twilio.Device(token, {
        closeProtection: true,
        codecPreferences: ["opus", "pcmu"]
    });
    device = twilioDevice;
    const processor = new VoicemailAudioProcessor();
    try {
        await twilioDevice.audio.addProcessor(processor);
        if (mine === generation)
            fallbackProcessor = processor;
    } catch {
        console.error("family-call", "Voicemail audio path is unavailable.");
    }
    if (mine !== generation)
        return;

    const ended = () => {
        if (mine !== generation)
            return;
        stopWatch();
        cancelVoicemailFallback();
        notify(dotNetRef, "OnFamilyCallEnded");
    };
    twilioDevice.on("error", (error) => {
        if (mine !== generation)
            return;
        notify(dotNetRef, "OnFamilyCallError", callErrorDetail(error));
    });
    try {
        const outputs = await navigator.mediaDevices.enumerateDevices();
        const speaker = outputs.find((item) => item.kind === "audiooutput" && /speaker/i.test(item.label));
        await twilioDevice.audio.speakerDevices.set(speaker ? speaker.deviceId : "default");
    } catch {
        // The browser keeps the default speaker when it cannot list outputs.
    }

    if (mine !== generation)
        return;

    const call = await twilioDevice.connect({ params: { ticket } });
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
    cancelVoicemailFallback();
    if (activeCall) {
        const call = activeCall;
        activeCall = null;
        try {
            call.disconnect();
        } catch {
            // The call is already gone.
        }
    }
    releaseVoicemailProcessor();
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

// Leave-voicemail silence fallback.
// Speech signal: RMS of the raw microphone time-domain samples from an AnalyserNode
// tapped on the MediaStream passed into AudioProcessor.createProcessedStream, before
// the gain that mutes that mic while the fallback sentence plays. Twilio's inputVolume
// event is not used, because it follows the processed stream and would hear this TTS.
// Speech is RMS >= speechRms (0.025) sustained for speechHoldMs (300). That resets a
// 30s silence accumulator. Samples are ignored unless AudioContext.state is "running",
// so a suspended context is not treated as silence and does not close the dialog.
// The sentence is the existing onboarding neural voice (MP3), decoded into the same
// processed stream Twilio sends to the mailbox. The call disconnects only after that
// buffer ends and an 800ms send tail, which is the same close path as a finished mailbox.

const VOICEMAIL_SEND_TAIL_MS = 800;

let fallbackToken = 0;
let fallbackArmed = false;
let fallbackLeaving = false;
let fallbackProcessor = null;
let fallbackSentence = "";
let fallbackLimits = null;
let silenceState = null;
let silenceTimer = 0;
let silenceTickAt = 0;
let armedGeneration = 0;

export function voicemailPhaseAllowsFallback(phase) {
    return phase === "leave-voicemail";
}

export function createVoicemailSilence() {
    return {
        silenceMs: 0,
        speechHoldMs: 0,
        leaving: false,
        played: false,
        closed: false
    };
}

export function advanceVoicemailSilence(state, sample, limits) {
    const next = {
        silenceMs: state.silenceMs,
        speechHoldMs: state.speechHoldMs,
        leaving: state.leaving,
        played: state.played,
        closed: state.closed
    };
    if (next.closed || next.leaving)
        return next;
    const elapsed = sample?.elapsedMs;
    if (!sample?.hearing || sample.rms == null || !(elapsed > 0))
        return next;
    if (sample.rms >= limits.speechRms) {
        next.speechHoldMs += elapsed;
        if (next.speechHoldMs >= limits.speechHoldMs)
            next.silenceMs = 0;
        return next;
    }
    next.speechHoldMs = 0;
    next.silenceMs += elapsed;
    if (next.silenceMs >= limits.silenceMs)
        next.leaving = true;
    return next;
}

export function markVoicemailSpoken(state, played) {
    if (played) {
        return {
            silenceMs: state.silenceMs,
            speechHoldMs: state.speechHoldMs,
            leaving: false,
            played: true,
            closed: true
        };
    }
    return createVoicemailSilence();
}

export async function beginVoicemailFallback(phase, sentence, silenceMs, speechRms, speechHoldMs) {
    if (!voicemailPhaseAllowsFallback(phase))
        return false;
    if (fallbackArmed)
        return true;
    if (fallbackLeaving || !activeCall || typeof sentence !== "string" || sentence.trim().length === 0)
        return false;

    const mine = generation;
    const processor = fallbackProcessor;
    if (!processor?.canPlay())
        return false;
    if (processor.context?.state === "suspended") {
        try {
            await processor.context.resume();
        } catch {
            return false;
        }
    }
    if (mine !== generation || !activeCall || !processor.hearing())
        return false;

    const silenceLimit = Number(silenceMs);
    const rmsLimit = Number(speechRms);
    const holdLimit = Number(speechHoldMs);
    fallbackLimits = {
        silenceMs: Number.isFinite(silenceLimit) && silenceLimit >= 1000 ? silenceLimit : 30000,
        speechRms: Number.isFinite(rmsLimit) && rmsLimit > 0 && rmsLimit < 1 ? rmsLimit : 0.025,
        speechHoldMs: Number.isFinite(holdLimit) && holdLimit >= 0 ? holdLimit : 300
    };
    fallbackSentence = sentence;
    silenceState = createVoicemailSilence();
    fallbackArmed = true;
    armedGeneration = mine;
    silenceTickAt = Date.now();
    startSilenceTimer();
    return true;
}

function startSilenceTimer() {
    stopSilenceTimer();
    silenceTimer = setInterval(() => {
        if (!fallbackArmed || fallbackLeaving || armedGeneration !== generation || !silenceState || !fallbackLimits)
            return;
        const now = Date.now();
        const elapsed = Math.min(1000, Math.max(0, now - silenceTickAt));
        silenceTickAt = now;
        const processor = fallbackProcessor;
        const hearing = !!processor?.hearing();
        const previous = silenceState;
        silenceState = advanceVoicemailSilence(silenceState, {
            elapsedMs: elapsed,
            rms: hearing ? processor.rms() : null,
            hearing
        }, fallbackLimits);
        if (silenceState.leaving && !previous.leaving)
            void leaveFallbackMessage();
    }, 100);
}

function stopSilenceTimer() {
    if (!silenceTimer)
        return;
    clearInterval(silenceTimer);
    silenceTimer = 0;
}

function cancelVoicemailFallback() {
    fallbackToken += 1;
    fallbackArmed = false;
    fallbackLeaving = false;
    silenceState = null;
    stopSilenceTimer();
}

function releaseVoicemailProcessor() {
    const processor = fallbackProcessor;
    fallbackProcessor = null;
    processor?.destroyProcessedStream();
}

async function leaveFallbackMessage() {
    if (!silenceState?.leaving || silenceState.played || fallbackLeaving)
        return;
    const token = fallbackToken;
    const mine = armedGeneration;
    fallbackLeaving = true;
    stopSilenceTimer();
    let played = false;
    try {
        played = await playSentenceIntoCall(fallbackSentence);
    } catch {
        played = false;
    }
    if (token !== fallbackToken || mine !== generation)
        return;
    silenceState = markVoicemailSpoken(silenceState, played);
    fallbackLeaving = false;
    if (!silenceState.closed) {
        if (fallbackArmed && activeCall) {
            silenceTickAt = Date.now();
            startSilenceTimer();
        }
        return;
    }
    const call = activeCall;
    if (!call)
        return;
    try {
        call.disconnect();
    } catch {
        // The mailbox leg is already gone. The disconnect watch still closes the dialog.
    }
}

async function playSentenceIntoCall(sentence) {
    const processor = fallbackProcessor;
    if (!processor?.canPlay())
        return false;
    if (processor.context.state === "suspended") {
        try {
            await processor.context.resume();
        } catch {
            return false;
        }
    }
    const response = await fetch("/onboarding-speech", {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/json",
            "Accept": "audio/mpeg"
        },
        body: JSON.stringify({ text: sentence })
    });
    if (!response.ok)
        return false;
    const bytes = await response.arrayBuffer();
    if (bytes.byteLength < 200)
        return false;
    const audioBuffer = await processor.context.decodeAudioData(bytes.slice(0));
    return processor.playBuffer(audioBuffer);
}

class VoicemailAudioProcessor {
    constructor() {
        this.context = null;
        this.micSource = null;
        this.micGain = null;
        this.ttsGain = null;
        this.analyser = null;
        this.samples = null;
        this.ready = false;
    }

    async createProcessedStream(stream) {
        await this.destroyProcessedStream();
        try {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            this.context = new AudioCtx();
            if (this.context.state === "suspended") {
                try {
                    await this.context.resume();
                } catch {
                    // The call can still connect. The fallback stays disarmed until resume works.
                }
            }
            this.micSource = this.context.createMediaStreamSource(stream);
            this.micGain = this.context.createGain();
            this.ttsGain = this.context.createGain();
            this.analyser = this.context.createAnalyser();
            this.analyser.fftSize = 2048;
            this.analyser.smoothingTimeConstant = 0;
            const destination = this.context.createMediaStreamDestination();
            this.micSource.connect(this.micGain);
            this.micGain.connect(destination);
            this.micSource.connect(this.analyser);
            this.ttsGain.connect(destination);
            this.ready = true;
            return destination.stream;
        } catch {
            console.error("family-call", "Voicemail audio path is unavailable.");
            await this.destroyProcessedStream();
            return stream;
        }
    }

    async destroyProcessedStream() {
        this.ready = false;
        const context = this.context;
        this.context = null;
        this.analyser = null;
        this.micGain = null;
        this.ttsGain = null;
        try {
            this.micSource?.disconnect();
        } catch {
            // The graph is already gone.
        }
        this.micSource = null;
        if (context && context.state !== "closed") {
            try {
                await context.close();
            } catch {
                // The context is already closed.
            }
        }
    }

    canPlay() {
        return !!(this.ready && this.context && this.ttsGain && this.context.state !== "closed");
    }

    hearing() {
        return !!(this.ready && this.analyser && this.context && this.context.state === "running");
    }

    rms() {
        if (!this.analyser)
            return null;
        try {
            const data = this.samples || (this.samples = new Float32Array(this.analyser.fftSize));
            this.analyser.getFloatTimeDomainData(data);
            let sum = 0;
            for (let i = 0; i < data.length; i++)
                sum += data[i] * data[i];
            return Math.sqrt(sum / data.length);
        } catch {
            return null;
        }
    }

    async playBuffer(audioBuffer) {
        if (!this.canPlay())
            return false;
        if (this.context.state === "suspended") {
            try {
                await this.context.resume();
            } catch {
                return false;
            }
        }
        const previous = this.micGain ? this.micGain.gain.value : 1;
        if (this.micGain)
            this.micGain.gain.setValueAtTime(0, this.context.currentTime);
        let played = false;
        try {
            const durationMs = Math.min(20000, Math.ceil((audioBuffer?.duration || 0) * 1000) + 1500);
            await new Promise((resolve, reject) => {
                const source = this.context.createBufferSource();
                source.buffer = audioBuffer;
                source.connect(this.ttsGain);
                const timer = setTimeout(() => reject(new Error("timeout")), durationMs);
                source.onended = () => {
                    clearTimeout(timer);
                    resolve();
                };
                try {
                    source.start();
                } catch (error) {
                    clearTimeout(timer);
                    reject(error);
                }
            });
            await new Promise((resolve) => setTimeout(resolve, VOICEMAIL_SEND_TAIL_MS));
            played = true;
            return true;
        } catch {
            return false;
        } finally {
            if (!played && this.micGain && this.context && this.context.state !== "closed")
                this.micGain.gain.setValueAtTime(previous, this.context.currentTime);
        }
    }
}
