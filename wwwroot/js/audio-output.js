/* Plays help-call audio on phones. iOS and Android ignore audio.play() that
   happens after the tap has gone to the server, so the tap itself unlocks
   the audio context and, when the clip is already on the device, starts it. */
(function () {
    var ctx = null;
    var ctxReady = Promise.resolve();
    var ctxPrimed = false;
    var element = null;
    var active = null;
    var pending = null;

    function sessionPlayback() {
        try {
            if (navigator.audioSession && navigator.audioSession.type !== "playback")
                navigator.audioSession.type = "playback";
        } catch (e) { /* this browser has no audio session */ }
    }

    function outputContext() {
        var Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx)
            return null;
        if (!ctx)
            ctx = new Ctx();
        return ctx;
    }

    function outputElement() {
        if (element && element.isConnected)
            return element;
        element = document.createElement("audio");
        element.setAttribute("playsinline", "true");
        element.setAttribute("webkit-playsinline", "true");
        element.playsInline = true;
        element.preload = "auto";
        element.volume = 1;
        element.style.cssText = "position:fixed;width:0;height:0;opacity:0;pointer-events:none;";
        (document.body || document.documentElement).appendChild(element);
        return element;
    }

    function primeContext() {
        sessionPlayback();
        var audio = outputContext();
        if (!audio)
            return;
        if (audio.state !== "running") {
            var pendingResume = audio.resume();
            ctxReady = Promise.resolve(pendingResume).catch(function () { });
        }
        if (ctxPrimed)
            return;
        try {
            var buffer = audio.createBuffer(1, 1, audio.sampleRate || 22050);
            var source = audio.createBufferSource();
            source.buffer = buffer;
            source.connect(audio.destination);
            source.start(0);
            ctxPrimed = true;
        } catch (e) { /* the next tap tries again */ }
    }

    function loop(blob, gapMs) {
        primeContext();
        if (active)
            active.stop();

        var stopped = false;
        var mode = "";
        var el = outputElement();
        var url = URL.createObjectURL(blob);
        var timer = null;
        var source = null;
        var decoded = null;
        var playing = false;
        var reported = false;
        var resolveStarted;
        var started = new Promise(function (resolve) { resolveStarted = resolve; });

        function report(ok) {
            if (reported)
                return;
            reported = true;
            resolveStarted(!!ok);
        }

        function clearSource() {
            if (!source)
                return;
            try {
                source.onended = null;
                source.stop(0);
            } catch (e) { /* already stopped */ }
            source = null;
        }

        function schedule(next) {
            timer = setTimeout(next, gapMs);
        }

        function playContext(first) {
            if (stopped)
                return;
            var audio = outputContext();
            if (!audio) {
                if (first)
                    report(false);
                return;
            }
            mode = "context";
            ctxReady.then(function () {
                if (audio.state === "suspended")
                    return audio.resume();
            }).then(function () {
                if (stopped)
                    return null;
                if (decoded)
                    return decoded;
                return blob.arrayBuffer().then(function (raw) {
                    return audio.decodeAudioData(raw.slice(0));
                }).then(function (buffer) {
                    decoded = buffer;
                    return buffer;
                });
            }).then(function (buffer) {
                if (!buffer || stopped)
                    return;
                clearSource();
                var next = audio.createBufferSource();
                var gain = audio.createGain();
                gain.gain.value = 1;
                next.buffer = buffer;
                next.connect(gain);
                gain.connect(audio.destination);
                next.onended = function () {
                    if (stopped || source !== next)
                        return;
                    playing = false;
                    schedule(function () { playContext(false); });
                };
                source = next;
                next.start(0);
                playing = true;
                report(true);
            }).catch(function () {
                if (first)
                    report(false);
            });
        }

        function playElement(first) {
            if (stopped)
                return;
            clearSource();
            mode = "element";
            el.loop = false;
            el.volume = 1;
            el.muted = false;
            el.dataset.live = "1";
            el.dataset.blessed = "1";
            if (el.dataset.clip !== url) {
                el.src = url;
                el.dataset.clip = url;
            }
            try { el.currentTime = 0; } catch (e) { /* metadata may still be loading */ }
            el.onended = function () {
                if (stopped || mode !== "element")
                    return;
                playing = false;
                schedule(function () { playElement(false); });
            };
            var played;
            try { played = el.play(); }
            catch (e) { played = Promise.reject(e); }
            Promise.resolve(played).then(function () {
                playing = true;
                report(true);
            }).catch(function () {
                try { el.pause(); } catch (e) { /* the speaker path is next */ }
                if (stopped) {
                    report(false);
                    return;
                }
                playContext(first);
            });
        }

        var player = {
            started: started,
            nudge: function () {
                if (stopped || !playing)
                    return;
                primeContext();
                if (mode === "element" && el.paused) {
                    var again = el.play();
                    if (again && again.catch)
                        again.catch(function () { });
                }
            },
            stop: function () {
                stopped = true;
                playing = false;
                if (timer)
                    clearTimeout(timer);
                clearSource();
                if (el.dataset.live === "1") {
                    el.onended = null;
                    try { el.pause(); } catch (e) { /* already quiet */ }
                    el.dataset.live = "";
                }
                try { URL.revokeObjectURL(url); } catch (e) { /* already released */ }
                if (active === player)
                    active = null;
            }
        };
        active = player;
        playElement(true);
        return player;
    }

    function play(blob) {
        primeContext();
        var audio = outputContext();
        if (!audio)
            return Promise.resolve(false);
        return ctxReady.then(function () {
            return blob.arrayBuffer();
        }).then(function (raw) {
            return audio.decodeAudioData(raw.slice(0));
        }).then(function (buffer) {
            var source = audio.createBufferSource();
            source.buffer = buffer;
            source.connect(audio.destination);
            source.start(0);
            return true;
        }).catch(function () { return false; });
    }

    function phraseFrom(event) {
        var target = event.target;
        if (!target || !target.closest)
            return "";
        if (target.closest("[data-tile-resize]"))
            return "";
        var button = target.closest(".dialysis-call");
        return button ? (button.getAttribute("data-phrase") || "") : "";
    }

    function slop(pointerType) {
        return pointerType === "mouse" ? 16 : 32;
    }

    document.addEventListener("touchstart", function () {
        primeContext();
    }, { capture: true, passive: true });

    document.addEventListener("pointerdown", function (event) {
        primeContext();
        if (event.button !== undefined && event.button !== 0) {
            pending = null;
            return;
        }
        var phrase = phraseFrom(event);
        pending = {
            id: event.pointerId,
            x: event.clientX,
            y: event.clientY,
            phrase: phrase,
            pointerType: event.pointerType || ""
        };
    }, true);

    document.addEventListener("pointerup", function (event) {
        var tap = pending;
        pending = null;
        if (!tap || tap.id !== event.pointerId || !tap.phrase)
            return;
        if (Math.hypot(event.clientX - tap.x, event.clientY - tap.y) > slop(tap.pointerType))
            return;
        var target = event.target;
        if (!target || !target.closest || !target.closest(".dialysis-call"))
            return;
        primeContext();
        if (!window.mmAccessibility || !window.mmAccessibility.playHelpNow)
            return;
        var blob = window.mmVoice && window.mmVoice.peekPhrase ? window.mmVoice.peekPhrase(tap.phrase) : null;
        window.mmAccessibility.playHelpNow(tap.phrase, blob);
    }, true);

    document.addEventListener("pointercancel", function () {
        pending = null;
    }, true);

    document.addEventListener("click", function () {
        primeContext();
    }, true);

    window.mmAudio = {
        unlock: primeContext,
        loop: loop,
        play: play
    };
})();
