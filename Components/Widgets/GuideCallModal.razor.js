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
    stopListening();
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
        utterance.rate = slow ? 0.74 : 0.82;
        utterance.pitch = 1.02;
        utterance.volume = 1;
        const voice = pickVoice();
        if (voice)
            utterance.voice = voice;
        utterance.onend = () => {
            if (token !== speakToken)
                return;
            window.setTimeout(speakNext, slow ? 480 : 340);
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

export function listenGuide(dotNetRef) {
    stopWake();
    stopListening();
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        notify(dotNetRef, "OnGuideHeard", "");
        return;
    }

    let reported = false;
    let quiet = 0;
    const report = (text) => {
        if (reported)
            return;
        reported = true;
        notify(dotNetRef, "OnGuideHeard", text || "");
    };

    recognition = new Recognition();
    recognition.lang = "en-US";
    recognition.interimResults = false;
    recognition.maxAlternatives = 3;
    recognition.onresult = (event) => {
        const result = event.results[event.results.length - 1];
        const heard = [];
        for (let index = 0; index < result.length; index += 1)
            heard.push(result[index]?.transcript || "");
        const text = heard.map((item) => item.trim()).filter(Boolean).join("|");
        if (isEcho(text)) {
            quiet += 1;
            return;
        }
        report(text);
    };
    recognition.onerror = (event) => {
        if (event.error === "no-speech" || event.error === "aborted") {
            quiet += 1;
            return;
        }
        report("\u0000");
    };
    recognition.onend = () => {
        if (reported || recognition === null)
            return;
        if (quiet < 4) {
            window.setTimeout(() => {
                if (reported || recognition === null)
                    return;
                try {
                    recognition.start();
                } catch {
                    report("");
                }
            }, 700);
            return;
        }
        report("");
    };
    window.setTimeout(() => {
        if (reported || recognition === null)
            return;
        try {
            recognition.start();
        } catch {
            report("");
        }
    }, 900);
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

function beginWake() {
    if (!wakeOn || wakeRec || wakeFired || wakeNeedsGesture)
        return;
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        notify(wakeRef, "OnWakeStatus", "unsupported");
        return;
    }

    const rec = new Recognition();
    wakeRec = rec;
    wakeStartedAt = Date.now();
    rec.lang = "en-US";
    rec.continuous = true;
    rec.interimResults = true;
    rec.maxAlternatives = 3;
    rec.onstart = () => {
        wakeStartedAt = Date.now();
        notify(wakeRef, "OnWakeStatus", "listening");
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
        if (event.error === "not-allowed" || event.error === "service-not-allowed") {
            wakeRec = null;
            wakeNeedsGesture = true;
            notify(wakeRef, "OnWakeStatus", "needs-tap");
            waitForGesture();
        }
    };
    rec.onend = () => {
        if (wakeRec !== rec)
            return;
        wakeRec = null;
        if (wakeOn && !wakeFired && !wakeNeedsGesture)
            wakeTimer = window.setTimeout(beginWake, 150);
    };
    try {
        rec.start();
    } catch {
        wakeRec = null;
        if (wakeOn && !wakeFired && !wakeNeedsGesture)
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
    const permission = await micPermission();
    if (!wakeOn || wakeFired)
        return;
    if (permission === "granted") {
        wakeNeedsGesture = false;
        beginWake();
        return;
    }
    wakeNeedsGesture = true;
    notify(wakeRef, "OnWakeStatus", permission === "denied" ? "blocked" : "needs-tap");
    waitForGesture();
}

export function startWake(dotNetRef) {
    wakeOn = true;
    wakeFired = false;
    wakeRef = dotNetRef;
    window.clearTimeout(wakeTimer);
    ensureWatch();
    void primeWake();
}

export async function armWake(dotNetRef) {
    if (dotNetRef)
        wakeRef = dotNetRef;
    wakeOn = true;
    wakeFired = false;
    wakeNeedsGesture = false;
    window.clearTimeout(wakeTimer);
    ensureWatch();
    if (!navigator.mediaDevices?.getUserMedia) {
        beginWake();
        return;
    }
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        stream.getTracks().forEach((track) => track.stop());
    } catch {
        wakeNeedsGesture = true;
        notify(wakeRef, "OnWakeStatus", "blocked");
        waitForGesture();
        return;
    }
    beginWake();
}

export function stopWake() {
    wakeOn = false;
    window.clearTimeout(wakeTimer);
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

export function stopGuide() {
    speakToken += 1;
    stopListening();
    window.speechSynthesis?.cancel();
}

function stopListening() {
    if (!recognition)
        return;
    const current = recognition;
    recognition = null;
    current.onresult = null;
    current.onerror = null;
    current.onend = null;
    try {
        current.stop();
    } catch {
        // The recognizer is already stopped.
    }
}
