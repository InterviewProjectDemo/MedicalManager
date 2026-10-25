let recognition;
let spokenOnce = false;

function notify(dotNetRef, method, arg) {
    const pending = arg === undefined
        ? dotNetRef.invokeMethodAsync(method)
        : dotNetRef.invokeMethodAsync(method, arg);
    pending.catch(() => { });
}

function pickVoice() {
    const voices = window.speechSynthesis?.getVoices?.() ?? [];
    return voices.find((voice) => /en(-|_)US/i.test(voice.lang) && /female|samantha|aria|jenny/i.test(voice.name))
        || voices.find((voice) => /en(-|_)US/i.test(voice.lang))
        || null;
}

export function speakGuide(text, slow, dotNetRef) {
    stopListening();
    if (!window.speechSynthesis) {
        notify(dotNetRef, "OnGuideSpoken");
        return;
    }

    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = "en-US";
    utterance.rate = slow ? 0.82 : 0.94;
    const voice = pickVoice();
    if (voice)
        utterance.voice = voice;
    spokenOnce = false;
    const done = () => {
        if (spokenOnce)
            return;
        spokenOnce = true;
        notify(dotNetRef, "OnGuideSpoken");
    };
    utterance.onend = done;
    utterance.onerror = done;
    window.speechSynthesis.speak(utterance);
}

export function listenGuide(dotNetRef) {
    stopListening();
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        notify(dotNetRef, "OnGuideHeard", "");
        return;
    }

    let reported = false;
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
        const text = Array.from(event.results)
            .map((result) => result[0]?.transcript || "")
            .join(" ")
            .trim();
        report(text);
    };
    recognition.onerror = () => report("");
    recognition.onend = () => {
        if (!reported)
            report("");
    };
    recognition.start();
}

export function stopGuide() {
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
