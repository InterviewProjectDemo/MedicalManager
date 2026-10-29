let recognition;
let spokenOnce = false;
let speakToken = 0;

function notify(dotNetRef, method, arg) {
    const pending = arg === undefined
        ? dotNetRef.invokeMethodAsync(method)
        : dotNetRef.invokeMethodAsync(method, arg);
    pending.catch(() => { });
}

function pickVoice() {
    const voices = window.speechSynthesis?.getVoices?.() ?? [];
    const english = voices.filter((voice) => /^en([-_]|$)/i.test(voice.lang));
    const score = (voice) => {
        const name = voice.name.toLowerCase();
        let points = /^en[-_]us/i.test(voice.lang) ? 2 : 0;
        if (/natural|neural|online/.test(name))
            points += 6;
        if (/aria|jenny|samantha|sara|zira/.test(name))
            points += 4;
        if (/female|woman/.test(name))
            points += 1;
        if (/david|mark|guy/.test(name))
            points -= 3;
        return points;
    };

    return english.sort((left, right) => score(right) - score(left))[0] || null;
}

function sentencesOf(text) {
    const parts = String(text || "").match(/[^.!?]+[.!?]?/g) || [];
    const sentences = parts.map((part) => part.trim()).filter(Boolean);
    return sentences.length > 0 ? sentences : [String(text || "").trim()].filter(Boolean);
}

export function speakGuide(text, slow, dotNetRef) {
    stopWake();
    sessionRef = dotNetRef;
    acceptCommands = false;
    guideSpeaking = true;
    ensureSession();
    const token = ++speakToken;
    if (!window.speechSynthesis) {
        notify(dotNetRef, "OnGuideSpoken");
        return;
    }

    window.speechSynthesis.cancel();
    spokenOnce = false;
    const finish = () => {
        if (token !== speakToken || spokenOnce)
            return;
        spokenOnce = true;
        guideSpeaking = false;
        notify(dotNetRef, "OnGuideSpoken");
    };

    const sentences = sentencesOf(text);
    let index = 0;
    let started = false;
    const speakNext = () => {
        if (token !== speakToken)
            return;
        if (index >= sentences.length) {
            finish();
            return;
        }

        const utterance = new SpeechSynthesisUtterance(sentences[index]);
        index += 1;
        utterance.lang = "en-US";
        utterance.rate = slow ? 0.68 : 0.78;
        utterance.pitch = 1.02;
        utterance.volume = 1;
        const voice = pickVoice();
        if (voice)
            utterance.voice = voice;
        utterance.onend = () => {
            if (token !== speakToken)
                return;
            window.setTimeout(speakNext, slow ? 720 : 420);
        };
        utterance.onerror = finish;
        window.speechSynthesis.speak(utterance);
    };

    const begin = () => {
        if (token !== speakToken || started)
            return;
        started = true;
        if (sentences.length === 0) {
            finish();
            return;
        }
        speakNext();
    };

    if ((window.speechSynthesis.getVoices?.() ?? []).length === 0) {
        const startOnce = () => {
            window.speechSynthesis.removeEventListener("voiceschanged", startOnce);
            begin();
        };
        window.speechSynthesis.addEventListener("voiceschanged", startOnce);
        window.setTimeout(begin, 400);
        return;
    }

    begin();
}

function isEcho(text) {
    const heard = String(text || "").toLowerCase();
    return heard.includes("how can i help")
        || heard.includes("who do you want")
        || heard.includes("should i call");
}

function phraseHasEnd(text) {
    const words = String(text || "")
        .toLowerCase()
        .replace(/[^a-z\s]/g, " ")
        .split(/\s+/)
        .filter(Boolean);
    if (!words.some((word) => word === "end" || word === "ended" || word === "ending"))
        return false;
    return !sharingCall || words.length <= 8;
}

let sessionRec;
let sessionRef;
let sessionOn = false;
let sessionTimer;
let acceptCommands = false;
let guideSpeaking = false;
let sharingCall = false;
let sessionQuiet = 0;

function ensureSession() {
    sessionOn = true;
    if (sessionRec || !sessionRef)
        return;
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition)
        return;

    const rec = new Recognition();
    sessionRec = rec;
    recognition = rec;
    rec.lang = "en-US";
    rec.continuous = true;
    rec.interimResults = true;
    rec.maxAlternatives = 3;
    rec.onresult = (event) => {
        sessionQuiet = 0;
        for (let index = event.resultIndex; index < event.results.length; index += 1) {
            const result = event.results[index];
            const heard = [];
            for (let alt = 0; alt < result.length; alt += 1)
                heard.push(result[alt]?.transcript || "");
            const text = heard.map((item) => item.trim()).filter(Boolean).join("|");
            if (heard.some((item) => phraseHasEnd(item))) {
                speakToken += 1;
                window.speechSynthesis?.cancel();
                stopSession();
                notify(sessionRef, "OnGuideEnd", text);
                return;
            }
            if (!result.isFinal || guideSpeaking || !acceptCommands || isEcho(text))
                continue;
            notify(sessionRef, "OnGuideHeard", text);
        }
    };
    rec.onerror = (event) => {
        if (event.error === "not-allowed" || event.error === "service-not-allowed") {
            sessionOn = false;
            sessionRec = null;
            recognition = null;
            if (!sharingCall)
                notify(sessionRef, "OnGuideHeard", "\u0000");
            return;
        }
        if (sharingCall && (event.error === "audio-capture" || event.error === "aborted")) {
            sessionOn = false;
            sharingCall = false;
            const failed = sessionRec;
            sessionRec = null;
            recognition = null;
            if (failed) {
                failed.onresult = null;
                failed.onerror = null;
                failed.onend = null;
                try {
                    failed.stop();
                } catch {
                    // The call keeps the microphone.
                }
            }
            return;
        }
        sessionQuiet += 1;
    };
    rec.onend = () => {
        if (sessionRec !== rec)
            return;
        sessionRec = null;
        recognition = null;
        if (!sessionOn)
            return;
        sessionTimer = window.setTimeout(ensureSession, sessionQuiet > 4 ? 700 : 150);
    };
    try {
        rec.start();
    } catch {
        sessionRec = null;
        recognition = null;
        if (sessionOn)
            sessionTimer = window.setTimeout(ensureSession, 400);
    }
}

function stopSession() {
    sessionOn = false;
    acceptCommands = false;
    guideSpeaking = false;
    sharingCall = false;
    window.clearTimeout(sessionTimer);
    const current = sessionRec;
    sessionRec = null;
    recognition = null;
    if (!current)
        return;
    current.onresult = null;
    current.onerror = null;
    current.onend = null;
    try {
        current.stop();
    } catch {
        // The dialog listener is already stopped.
    }
}

export function listenGuide(dotNetRef, duringCall) {
    stopWake();
    sessionRef = dotNetRef;
    acceptCommands = true;
    sharingCall = duringCall === true;
    sessionQuiet = 0;
    ensureSession();
}

export function yieldGuideMic() {
    stopSession();
}

export function speakGoodbye(dotNetRef) {
    const token = ++speakToken;
    guideSpeaking = false;
    acceptCommands = false;
    stopSession();
    stopWake();
    window.speechSynthesis?.cancel();
    if (!window.speechSynthesis) {
        notify(dotNetRef, "OnGoodbyeSpoken");
        return;
    }

    const utterance = new SpeechSynthesisUtterance("Goodbye.");
    utterance.lang = "en-US";
    utterance.rate = 0.9;
    utterance.pitch = 1.02;
    const voice = pickVoice();
    if (voice)
        utterance.voice = voice;
    let done = false;
    const finish = () => {
        if (done || token !== speakToken)
            return;
        done = true;
        notify(dotNetRef, "OnGoodbyeSpoken");
    };
    utterance.onend = finish;
    utterance.onerror = finish;
    window.setTimeout(finish, 2200);
    window.speechSynthesis.speak(utterance);
}

let wakeRec;
let wakeOn = false;
let wakeFired = false;
let wakeNeedsGesture = false;
let wakeRef;
let wakeTimer;
let wakeWatch;
let wakeGesture;
let wakeStartedAt = 0;
let wakeStatus = "";
let wakeStream;
let wakeAudio;
let wakeAnalyser;
let wakeVadTimer = 0;
let wakeOpening = false;
let handoffFails = 0;
let wakeCooldownUntil = 0;
let vadArmed = false;
let loudSince = 0;
let nextWakeAt = 0;
let nextMicAt = 0;

function androidLike() {
    const ua = navigator.userAgent || "";
    const platform = navigator.userAgentData?.platform || navigator.platform || "";
    if (/Android|Samsung|Galaxy|SM-[A-Z]\d/i.test(ua) || /Android/i.test(platform))
        return true;
    if (navigator.userAgentData?.mobile === true && !/Windows|Mac/i.test(platform))
        return true;
    if (appleTouch())
        return false;
    if (/Windows|Macintosh|Mac OS/i.test(ua) || /Win|Mac/i.test(platform))
        return false;
    const touch = (navigator.maxTouchPoints || 0) > 0;
    const coarse = window.matchMedia("(pointer: coarse), (hover: none)").matches;
    return touch && coarse;
}

function appleTouch() {
    const ua = navigator.userAgent || "";
    return /iPhone|iPad|iPod/.test(ua)
        || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

function setWakeStatus(status) {
    if (wakeStatus === status)
        return;
    wakeStatus = status;
    notify(wakeRef, "OnWakeStatus", status);
}

function phraseHasCall(text) {
    const words = String(text || "")
        .toLowerCase()
        .replace(/[^a-z\s]/g, " ")
        .split(/\s+/)
        .filter(Boolean);
    return words.some((word) => word === "call" || word === "calls" || word === "calling" || word === "called");
}

function heardCall(result) {
    const heard = [];
    for (let index = 0; index < result.length; index += 1)
        heard.push(result[index]?.transcript || "");
    return heard.find((text) => phraseHasCall(text)) || "";
}

function triggerWake(text) {
    if (!wakeOn || wakeFired || !wakeRef)
        return;
    wakeFired = true;
    stopWake();
    notify(wakeRef, "OnWakeCall", text);
}

function releaseMic() {
    if (wakeVadTimer) {
        window.cancelAnimationFrame(wakeVadTimer);
        wakeVadTimer = 0;
    }
    if (wakeStream) {
        wakeStream.getTracks().forEach((track) => track.stop());
        wakeStream = null;
    }
    const audio = wakeAudio;
    wakeAudio = null;
    wakeAnalyser = null;
    if (audio)
        audio.close().catch(() => { });
}

function sampleLevel() {
    if (!wakeAnalyser)
        return 0;
    const data = new Uint8Array(wakeAnalyser.fftSize);
    wakeAnalyser.getByteTimeDomainData(data);
    let sum = 0;
    for (let index = 0; index < data.length; index += 1) {
        const value = (data[index] - 128) / 128;
        sum += value * value;
    }
    return Math.sqrt(sum / data.length);
}

function watchVoice() {
    if (!wakeOn || !wakeAnalyser || wakeRec || wakeFired || wakeNeedsGesture)
        return;
    const level = sampleLevel();
    const now = Date.now();
    const quiet = androidLike() ? 0.02 : 0.03;
    const loud = androidLike() ? 0.12 : 0.055;
    const hold = androidLike() ? 400 : 80;
    if (now < wakeCooldownUntil || (androidLike() && now < nextWakeAt)) {
        vadArmed = level < quiet;
        loudSince = 0;
    } else if (level < quiet) {
        vadArmed = true;
        loudSince = 0;
    } else if (vadArmed && level > loud) {
        if (!loudSince)
            loudSince = now;
        if (now - loudSince >= hold) {
            loudSince = 0;
            vadArmed = false;
            releaseMic();
            window.clearTimeout(wakeTimer);
            wakeTimer = window.setTimeout(() => {
                wakeTimer = 0;
                beginWake();
            }, 200);
            return;
        }
    } else {
        loudSince = 0;
    }
    wakeVadTimer = window.requestAnimationFrame(watchVoice);
}

async function listenForVoice() {
    if (!wakeOn || wakeFired || wakeNeedsGesture || wakeStream || wakeRec || wakeOpening)
        return;
    if (!navigator.mediaDevices?.getUserMedia) {
        beginWake();
        return;
    }
    wakeOpening = true;
    try {
        wakeStream = await navigator.mediaDevices.getUserMedia({
            audio: { echoCancellation: true, noiseSuppression: true }
        });
        if (!wakeOn || wakeFired || wakeNeedsGesture) {
            releaseMic();
            return;
        }
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        if (!AudioContext) {
            releaseMic();
            beginWake();
            return;
        }
        wakeAudio = new AudioContext();
        if (wakeAudio.state === "suspended")
            await wakeAudio.resume();
        if (wakeAudio.state === "suspended") {
            releaseMic();
            wakeNeedsGesture = true;
            setWakeStatus("needs-tap");
            waitForGesture();
            return;
        }
        const source = wakeAudio.createMediaStreamSource(wakeStream);
        wakeAnalyser = wakeAudio.createAnalyser();
        wakeAnalyser.fftSize = 512;
        source.connect(wakeAnalyser);
        vadArmed = false;
        loudSince = 0;
        setWakeStatus("listening");
        watchVoice();
    } catch (error) {
        releaseMic();
        nextMicAt = Date.now() + 20000;
        if (!wakeOn)
            return;
        wakeNeedsGesture = true;
        const denied = error?.name === "NotAllowedError" || error?.name === "SecurityError";
        setWakeStatus(denied ? "blocked" : "needs-tap");
        waitForGesture();
    } finally {
        wakeOpening = false;
    }
}

function quietAfterRecognition(ran) {
    if (!androidLike())
        return;
    const pause = ran < 4000 ? 25000 : 8000;
    nextWakeAt = Date.now() + pause;
    wakeCooldownUntil = nextWakeAt;
}

function beginWake() {
    if (!wakeOn || wakeRec || wakeFired || wakeNeedsGesture)
        return;
    if (androidLike() && Date.now() < nextWakeAt) {
        void listenForVoice();
        return;
    }
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        setWakeStatus("unsupported");
        return;
    }

    const rec = new Recognition();
    wakeRec = rec;
    wakeStartedAt = Date.now();
    rec.lang = "en-US";
    rec.continuous = !androidLike();
    rec.interimResults = true;
    rec.maxAlternatives = 3;
    rec.onstart = () => {
        wakeStartedAt = Date.now();
        handoffFails = 0;
        setWakeStatus("listening");
    };
    rec.onresult = (event) => {
        wakeStartedAt = Date.now();
        for (let index = event.resultIndex; index < event.results.length; index += 1) {
            const text = heardCall(event.results[index]);
            if (!text)
                continue;
            triggerWake(text);
            return;
        }
    };
    rec.onerror = (event) => {
        if (event.error === "audio-capture")
            handoffFails += 1;
        if (event.error === "not-allowed" || event.error === "service-not-allowed") {
            wakeRec = null;
            wakeNeedsGesture = true;
            setWakeStatus("needs-tap");
            waitForGesture();
        }
    };
    rec.onend = () => {
        if (wakeRec !== rec)
            return;
        wakeRec = null;
        if (!wakeOn || wakeFired || wakeNeedsGesture)
            return;
        if (androidLike()) {
            if (handoffFails >= 2) {
                wakeNeedsGesture = true;
                setWakeStatus("needs-tap");
                waitForGesture();
                return;
            }
            quietAfterRecognition(wakeStartedAt ? Date.now() - wakeStartedAt : 0);
            void listenForVoice();
            return;
        }
        const ran = wakeStartedAt ? Date.now() - wakeStartedAt : 0;
        wakeTimer = window.setTimeout(beginWake, ran < 2000 ? 2000 : 400);
    };
    try {
        rec.start();
    } catch {
        wakeRec = null;
        if (!wakeOn || wakeFired || wakeNeedsGesture)
            return;
        if (androidLike()) {
            quietAfterRecognition(0);
            void listenForVoice();
            return;
        }
        wakeTimer = window.setTimeout(beginWake, 500);
    }
}

function waitForGesture() {
    if (wakeGesture)
        return;
    const arm = () => {
        document.removeEventListener("pointerdown", arm, true);
        document.removeEventListener("keydown", arm, true);
        wakeGesture = null;
        if (wakeOn)
            void armWake(wakeRef);
    };
    wakeGesture = arm;
    document.addEventListener("pointerdown", arm, true);
    document.addEventListener("keydown", arm, true);
}

function micPermission() {
    if (!navigator.permissions?.query)
        return Promise.resolve("prompt");
    return navigator.permissions.query({ name: "microphone" })
        .then((status) => status.state)
        .catch(() => "prompt");
}

function ensureWatch() {
    if (wakeWatch)
        return;
    wakeWatch = window.setInterval(() => {
        if (!wakeOn || wakeFired || wakeNeedsGesture)
            return;
        if (androidLike()) {
            if (!wakeRec && !wakeStream && !wakeOpening && !wakeTimer && Date.now() >= nextMicAt)
                void listenForVoice();
            return;
        }
        if (!wakeRec) {
            beginWake();
            return;
        }
        if (wakeStartedAt && Date.now() - wakeStartedAt > 45000) {
            try {
                wakeRec.stop();
            } catch {
                wakeRec = null;
                beginWake();
            }
        }
    }, 4000);
}

async function primeWake() {
    if (!wakeOn || wakeFired)
        return;
    if (appleTouch()) {
        wakeNeedsGesture = true;
        setWakeStatus("needs-tap");
        waitForGesture();
        return;
    }
    const permission = await micPermission();
    if (!wakeOn || wakeFired)
        return;
    if (permission === "denied") {
        wakeNeedsGesture = true;
        setWakeStatus("blocked");
        waitForGesture();
        return;
    }
    if (androidLike()) {
        if (permission !== "granted") {
            wakeNeedsGesture = true;
            setWakeStatus("needs-tap");
            waitForGesture();
            return;
        }
        wakeNeedsGesture = false;
        void listenForVoice();
        return;
    }
    if (permission === "granted") {
        wakeNeedsGesture = false;
        beginWake();
        return;
    }
    wakeNeedsGesture = true;
    setWakeStatus("needs-tap");
    waitForGesture();
}

export function startWake(dotNetRef) {
    wakeOn = true;
    wakeFired = false;
    wakeNeedsGesture = false;
    handoffFails = 0;
    wakeRef = dotNetRef;
    window.clearTimeout(wakeTimer);
    ensureWatch();
    void primeWake();
}

export function armWake(dotNetRef) {
    if (dotNetRef)
        wakeRef = dotNetRef;
    wakeOn = true;
    wakeFired = false;
    wakeNeedsGesture = false;
    handoffFails = 0;
    wakeCooldownUntil = 0;
    nextWakeAt = 0;
    nextMicAt = 0;
    window.clearTimeout(wakeTimer);
    ensureWatch();
    if (androidLike()) {
        void listenForVoice();
        return;
    }
    beginWake();
}

export function stopWake() {
    wakeOn = false;
    wakeStatus = "";
    wakeOpening = false;
    handoffFails = 0;
    releaseMic();
    window.clearTimeout(wakeTimer);
    if (wakeWatch) {
        window.clearInterval(wakeWatch);
        wakeWatch = null;
    }
    if (wakeGesture) {
        document.removeEventListener("pointerdown", wakeGesture, true);
        document.removeEventListener("keydown", wakeGesture, true);
        wakeGesture = null;
    }
    const current = wakeRec;
    wakeRec = null;
    if (!current)
        return;
    current.onstart = null;
    current.onresult = null;
    current.onerror = null;
    current.onend = null;
    try {
        current.stop();
    } catch {
        // The wake listener is already stopped.
    }
}

export function stopGuideSpeech() {
    speakToken += 1;
    guideSpeaking = false;
    window.speechSynthesis?.cancel();
}

export function stopGuide() {
    stopGuideSpeech();
    stopSession();
}
