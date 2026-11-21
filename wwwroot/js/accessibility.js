(function () {
    var KEY = "mm-accessibility";
    var SIZE_CLASSES = ["a11y-text-sm", "a11y-text-lg", "a11y-text-xl"];
    var SIZES = { sm: "a11y-text-sm", lg: "a11y-text-lg", xl: "a11y-text-xl" };
    var FLAGS = [
        ["highContrast", "a11y-contrast"],
        ["reduceMotion", "a11y-motion"],
        ["largeTargets", "a11y-targets"],
        ["strongFocus", "a11y-focus"],
        ["underlineLinks", "a11y-links"],
        ["comfortableSpacing", "a11y-spacing"],
        ["dialysisLargeNumbers", "a11y-dialysis-numbers"],
        ["dialysisSimple", "a11y-dialysis-simple"],
        ["dialysisContrast", "a11y-dialysis-contrast"],
        ["authLarge", "a11y-auth-large"],
        ["authPlain", "a11y-auth-plain"],
        ["authConfirm", "a11y-auth-confirm"],
        ["socialReading", "a11y-social-reading"],
        ["socialContacts", "a11y-social-contacts"],
        ["govForms", "a11y-gov-forms"],
        ["govPrint", "a11y-gov-print"],
        ["govSticky", "a11y-gov-sticky"],
        ["medicareDates", "a11y-medicare-dates"],
        ["medicareAmounts", "a11y-medicare-amounts"],
        ["medicarePlain", "a11y-medicare-plain"]
    ];

    function load() {
        try {
            var raw = localStorage.getItem(KEY);
            return raw ? JSON.parse(raw) : null;
        } catch (e) {
            return null;
        }
    }

    function doctorView() {
        return document.documentElement.dataset.a11yScope === "doctor";
    }

    function apply(state) {
        var root = document.documentElement;
        var classes = SIZE_CLASSES.concat(FLAGS.map(function (flag) { return flag[1]; }));
        root.classList.remove.apply(root.classList, classes);
        if (doctorView() || !state)
            return;
        var size = SIZES[state.textSize];
        if (size)
            root.classList.add(size);
        FLAGS.forEach(function (flag) {
            if (state[flag[0]])
                root.classList.add(flag[1]);
        });
    }

    function save(state) {
        try {
            localStorage.setItem(KEY, JSON.stringify(state));
        } catch (e) { /* private mode */ }
        apply(state);
    }

    var helpCall = null;
    var helpWatch = null;
    var helpGeneration = 0;

    function pickVoice() {
        var voices = window.speechSynthesis ? window.speechSynthesis.getVoices() : [];
        var english = voices.filter(function (voice) { return /^en([-_]|$)/i.test(voice.lang); });
        if (english.length === 0)
            return null;
        function rank(voice) {
            var name = voice.name.toLowerCase();
            var score = 0;
            if (/en-us/i.test(voice.lang)) score += 5;
            if (/natural|neural|premium|enhanced/.test(name)) score += 4;
            if (/google/.test(name)) score += 3;
            if (/aria|jenny|guy|zira|samantha|daniel/.test(name)) score += 2;
            if (/compact|espeak/.test(name)) score -= 2;
            return score;
        }
        english.sort(function (a, b) { return rank(b) - rank(a); });
        return english[0];
    }

    function sayHelp() {
        if (!helpCall || helpCall.stopped || helpCall.pending)
            return;
        var generation = helpCall.generation;
        helpCall.pending = true;
        var utterance = new SpeechSynthesisUtterance(helpCall.phrase);
        utterance.volume = 1;
        utterance.rate = 0.82;
        utterance.pitch = 1;
        utterance.lang = "en-US";
        var voice = pickVoice();
        if (voice)
            utterance.voice = voice;
        function current() {
            return helpCall && helpCall.generation === generation && !helpCall.stopped;
        }
        function again(delay) {
            if (!current())
                return;
            helpCall.pending = false;
            setTimeout(function () {
                if (current())
                    sayHelp();
            }, delay);
        }
        utterance.onend = function () { again(400); };
        utterance.onerror = function () {
            if (!current())
                return;
            again(700);
        };
        window.speechSynthesis.speak(utterance);
    }

    function watchHelp() {
        if (!helpCall || helpCall.stopped)
            return;
        if (helpCall.usingVoice) {
            if (helpCall.between)
                return;
            if (helpCall.audio && helpCall.audio.paused)
                helpCall.audio.play().catch(function () { });
            return;
        }
        if (window.speechSynthesis.paused)
            window.speechSynthesis.resume();
        if (window.speechSynthesis.speaking || helpCall.pending) {
            helpCall.quietSince = 0;
            return;
        }
        helpCall.quietSince += 1;
        if (helpCall.quietSince >= 2) {
            helpCall.quietSince = 0;
            sayHelp();
        }
    }

    function playOwnVoice(blob) {
        var url = URL.createObjectURL(blob);
        var audio = new Audio(url);
        audio.volume = 1;
        helpCall.audio = audio;
        helpCall.audioUrl = url;
        audio.onended = function () {
            if (!helpCall || helpCall.stopped || helpCall.audio !== audio)
                return;
            helpCall.between = true;
            setTimeout(function () {
                if (!helpCall || helpCall.stopped || helpCall.audio !== audio)
                    return;
                helpCall.between = false;
                audio.currentTime = 0;
                audio.play().catch(function () { });
            }, 350);
        };
        return audio.play().then(function () { return true; }).catch(function () { return false; });
    }

    function beginSynthesized(phrase) {
        if (!window.speechSynthesis || !phrase)
            return false;
        helpCall = {
            phrase: String(phrase),
            stopped: false,
            pending: false,
            quietSince: 0,
            generation: ++helpGeneration,
            usingVoice: false
        };
        if (window.speechSynthesis.getVoices().length === 0)
            window.speechSynthesis.getVoices();
        sayHelp();
        helpWatch = setInterval(watchHelp, 1000);
        return true;
    }

    function beginVoiceLoop(phrase, blob) {
        helpCall = {
            phrase: String(phrase),
            stopped: false,
            pending: false,
            quietSince: 0,
            generation: ++helpGeneration,
            usingVoice: true,
            between: false
        };
        return playOwnVoice(blob).then(function (played) {
            if (!played) {
                stopHelpCall();
                return false;
            }
            if (!helpWatch)
                helpWatch = setInterval(watchHelp, 1000);
            return true;
        });
    }

    async function startHelpCall(phrase) {
        if (doctorView()) {
            stopHelpCall();
            return false;
        }
        stopHelpCall();
        var ready = false;
        if (window.mmVoice && window.mmVoice.ensureVoice) {
            try { ready = await window.mmVoice.ensureVoice(); } catch (e) { ready = false; }
        }
        if (ready && window.mmVoice.cachedPhrase) {
            try {
                var clip = await window.mmVoice.cachedPhrase(phrase);
                if (clip)
                    return await beginVoiceLoop(phrase, clip);
            } catch (e) { /* the device voice still speaks */ }
        }
        var started = beginSynthesized(phrase);
        if (ready && window.mmVoice.speakPhrase) {
            window.mmVoice.speakPhrase(phrase).then(function (blob) {
                if (!blob || !helpCall || helpCall.stopped || helpCall.phrase !== String(phrase))
                    return;
                helpGeneration += 1;
                helpCall.generation = helpGeneration;
                helpCall.usingVoice = true;
                helpCall.pending = false;
                helpCall.between = false;
                if (window.speechSynthesis)
                    window.speechSynthesis.cancel();
                return playOwnVoice(blob).then(function (played) {
                    if (played)
                        document.dispatchEvent(new CustomEvent("mm-own-voice"));
                });
            }).catch(function () { });
        }
        return started;
    }

    function stopHelpCall() {
        helpGeneration += 1;
        if (helpCall && helpCall.audio) {
            helpCall.audio.onended = null;
            helpCall.audio.pause();
        }
        if (helpCall && helpCall.audioUrl)
            URL.revokeObjectURL(helpCall.audioUrl);
        if (helpCall)
            helpCall.stopped = true;
        helpCall = null;
        if (helpWatch) {
            clearInterval(helpWatch);
            helpWatch = null;
        }
        if (window.speechSynthesis)
            window.speechSynthesis.cancel();
    }

    function helpCallPhrase() {
        return helpCall && !helpCall.stopped ? helpCall.phrase : "";
    }

    function deviceSpeak(text) {
        if (helpCall || !window.speechSynthesis || !text)
            return;
        var utterance = new SpeechSynthesisUtterance(text);
        utterance.rate = 0.95;
        utterance.volume = 1;
        window.speechSynthesis.cancel();
        window.speechSynthesis.speak(utterance);
    }

    function speak(text) {
        if (doctorView() || helpCall)
            return;
        if (document.documentElement.classList.contains("a11y-motion"))
            return;
        if (window.mmVoice && window.mmVoice.ensureVoice) {
            window.mmVoice.ensureVoice().then(function (ready) {
                if (helpCall)
                    return;
                if (!ready)
                    return deviceSpeak(text);
                return window.mmVoice.speakPhrase(text).then(function (blob) {
                    if (helpCall)
                        return;
                    if (!blob)
                        return deviceSpeak(text);
                    var audio = new Audio(URL.createObjectURL(blob));
                    audio.volume = 1;
                    audio.play().catch(function () { deviceSpeak(text); });
                });
            }).catch(function () { deviceSpeak(text); });
            return;
        }
        deviceSpeak(text);
    }

    document.addEventListener("submit", function (event) {
        if (!document.documentElement.classList.contains("a11y-auth-confirm"))
            return;
        var form = event.target;
        if (!(form instanceof HTMLFormElement))
            return;
        var submitter = event.submitter;
        var label = ((submitter && submitter.textContent) || "") + " " + (form.getAttribute("aria-label") || "");
        if (!/approv|authoriz|consent|grant/i.test(label))
            return;
        if (!window.confirm("Send this authorization?"))
            event.preventDefault();
    }, true);

    function holdForDoctor() {
        document.documentElement.dataset.a11yScope = "doctor";
        stopHelpCall();
        apply(null);
    }

    function releaseHold() {
        if (document.documentElement.dataset.a11yScope === "doctor")
            delete document.documentElement.dataset.a11yScope;
        apply(load());
    }

    window.mmAccessibility = {
        load: load,
        save: save,
        apply: apply,
        speak: speak,
        holdForDoctor: holdForDoctor,
        releaseHold: releaseHold,
        startHelpCall: startHelpCall,
        stopHelpCall: stopHelpCall,
        helpCallPhrase: helpCallPhrase
    };
    apply(doctorView() ? null : load());
})();
