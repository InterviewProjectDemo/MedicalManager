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
        utterance.rate = slow ? 0.84 : 0.9;
        utterance.pitch = 1.02;
        utterance.volume = 1;
        const voice = pickVoice();
        if (voice)
            utterance.voice = voice;
        utterance.onend = () => {
            if (token !== speakToken)
                return;
            window.setTimeout(speakNext, slow ? 320 : 220);
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
