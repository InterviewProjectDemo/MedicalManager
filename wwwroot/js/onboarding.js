window.mmOnboarding = {
    _speakToken: 0,
    _visemeTimer: null,
    _currentViseme: 0,
    _reducedMotion: false,
    _imagesPreloaded: false,
    _visemeMinMs: 130,
    _visemeMaxMs: 175,
    _speechUnlocked: false,
    _resumeTimer: null,
    _gestureArmed: false,
    _unlockNotified: false,
    _dotNetRef: null,
    /** Test hook: delay speakAsync resolution (ms) to simulate iOS onend hang. */
    debugHangMs: 0,

    /** Adjacent-only viseme transitions — avoid jarring closed→wide→closed jumps. */
    _visemeTransitions: {
        0: [0, 1, 4],
        1: [0, 1, 2, 4],
        2: [1, 2, 3, 5],
        3: [2, 3, 5],
        4: [0, 1, 4, 2],
        5: [2, 3, 5, 1]
    },

    isSpeechSupported() {
        return typeof Audio !== "undefined" || "speechSynthesis" in window;
    },

    _isAppleTouch() {
        const ua = navigator.userAgent || "";
        return /iPad|iPhone|iPod/.test(ua)
            || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
    },

    _isAndroid() {
        return /Android/i.test(navigator.userAgent || "");
    },

    /** iOS/Android block speech until a real user gesture unlocks the synth. */
    _requiresSpeechGesture() {
        return this._isAppleTouch() || this._isAndroid();
    },

    needsSpeechUnlock() {
        return this.isSpeechSupported() && !this._speechUnlocked && this._requiresSpeechGesture();
    },

    isSpeechUnlocked() {
        return this._speechUnlocked;
    },

    _startResumeWatch() {
        if (this._resumeTimer || !window.speechSynthesis) return;
        this._resumeTimer = setInterval(() => {
            try {
                if (window.speechSynthesis.speaking && window.speechSynthesis.paused) {
                    window.speechSynthesis.resume();
                }
            } catch {
                /* ignore */
            }
        }, 4000);
    },

    _stopResumeWatch() {
        if (this._resumeTimer) {
            clearInterval(this._resumeTimer);
            this._resumeTimer = null;
        }
    },

    /** Must run inside a user gesture so later human-voice playback is allowed. */
    unlockSpeech() {
        try {
            const audio = this._ensureAudio();
            audio.src = this._silentWav;
            const played = audio.play();
            if (played && typeof played.catch === "function") {
                played.catch(() => { });
            }
            this._speechUnlocked = true;
            return true;
        } catch {
            this._speechUnlocked = true;
            return false;
        }
    },

    _notifyUnlocked() {
        if (this._unlockNotified) return;
        this._unlockNotified = true;
        if (this._dotNetRef) {
            this._dotNetRef.invokeMethodAsync("OnSpeechUnlocked").catch(() => { });
        }
    },

    unlockFromUserGesture() {
        this.unlockSpeech();
        this._notifyUnlocked();
    },

    /** Capture taps so Blazor's delayed @onclick still unlocks iOS speech. */
    armGestureUnlock(dotNetRef) {
        this._dotNetRef = dotNetRef ?? this._dotNetRef;
        if (this._gestureArmed) return;
        this._gestureArmed = true;

        const onGesture = () => {
            if (this._speechUnlocked || !this._requiresSpeechGesture()) return;
            this.unlockSpeech();
            this._notifyUnlocked();
        };

        document.addEventListener("pointerdown", onGesture, { capture: true, passive: true });
        document.addEventListener("touchstart", onGesture, { capture: true, passive: true });
        document.addEventListener("click", onGesture, { capture: true, passive: true });
    },

    _estimateChunkMs(text) {
        const rate = 0.88;
        const charsPerSec = 14 * rate;
        const raw = (String(text || "").length / charsPerSec) * 1000 + 2500;
        return Math.min(20000, Math.max(8000, Math.round(raw)));
    },

    /** Score voices for a warm, caring tone — prefer female/neutral English voices. */
    pickWarmVoice(voices) {
        if (!voices?.length) return null;

        const warmNamePatterns = [
            /samantha/i, /zira/i, /karen/i, /victoria/i, /moira/i, /fiona/i,
            /google uk english female/i, /google us english/i, /microsoft (aria|jenny|michelle)/i,
            /female/i, /natural/i
        ];

        const english = voices.filter(v => v.lang?.startsWith("en"));
        const pool = english.length ? english : voices;

        let best = null;
        let bestScore = -1;

        for (const voice of pool) {
            let score = 0;
            const name = voice.name || "";

            for (let i = 0; i < warmNamePatterns.length; i++) {
                if (warmNamePatterns[i].test(name)) {
                    score += 10 - i;
                }
            }

            if (/local/i.test(name)) score += 2;
            if (voice.default) score += 1;

            if (score > bestScore) {
                bestScore = score;
                best = voice;
            }
        }

        return best ?? pool[0] ?? null;
    },

    preloadPresenterImages() {
        if (this._imagesPreloaded) return;
        for (let i = 0; i <= 5; i++) {
            const img = new Image();
            img.src = `images/onboarding-presenter-${i}.jpg`;
        }
        this._imagesPreloaded = true;
    },

    setPresenterViseme(index) {
        const viseme = Math.max(0, Math.min(5, index | 0));
        if (viseme === this._currentViseme) return;
        this._currentViseme = viseme;
        document.querySelectorAll("[data-walkthrough-presenter]").forEach((el) => {
            el.dataset.viseme = String(viseme);
        });
    },

    _nextVisemeDelay() {
        return this._visemeMinMs + Math.floor(Math.random() * (this._visemeMaxMs - this._visemeMinMs + 1));
    },

    _nextViseme() {
        const options = this._visemeTransitions[this._currentViseme] ?? [0, 1, 2];
        const next = options[Math.floor(Math.random() * options.length)];
        this.setPresenterViseme(next);
    },

    /** Single source of truth for mouth movement — one debounced timer, no onboundary. */
    _startVisemeCycle() {
        if (this._reducedMotion || this._visemeTimer) return;
        const tick = () => {
            this._nextViseme();
            this._visemeTimer = setTimeout(tick, this._nextVisemeDelay());
        };
        this._visemeTimer = setTimeout(tick, this._nextVisemeDelay());
    },

    _stopVisemeCycle() {
        if (this._visemeTimer) {
            clearTimeout(this._visemeTimer);
            this._visemeTimer = null;
        }
    },

    setPresenterSpeaking(speaking) {
        document.querySelectorAll("[data-walkthrough-presenter]").forEach((el) => {
            el.classList.toggle("is-speaking", !!speaking);
        });

        if (speaking && !this._reducedMotion) {
            this._startVisemeCycle();
        } else {
            this._stopVisemeCycle();
            this.setPresenterViseme(0);
        }
    },

    _applyWarmVoice(utterance) {
        utterance.rate = 0.88;
        utterance.pitch = 0.95;
        utterance.volume = 0.88;
        utterance.lang = "en-US";

        const voices = window.speechSynthesis.getVoices();
        const preferred = this.pickWarmVoice(voices);
        if (preferred) {
            utterance.voice = preferred;
        }
    },

    /** Split long narration into sentence-sized chunks so browsers finish every phrase. */
    _splitSpeechChunks(text) {
        const trimmed = (text || "").trim();
        if (!trimmed) return [];

        const sentences = trimmed.match(/[^.!?]+[.!?]+|[^.!?]+$/g);
        if (!sentences?.length) return [trimmed];

        const chunks = [];
        let current = "";

        for (const sentence of sentences) {
            const piece = sentence.trim();
            if (!piece) continue;

            const candidate = current ? `${current} ${piece}` : piece;
            if (candidate.length > 180 && current) {
                chunks.push(current);
                current = piece;
            } else {
                current = candidate;
            }
        }

        if (current) chunks.push(current);
        return chunks.length ? chunks : [trimmed];
    },

    _silentWav: "data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=",
    _audio: null,
    _audioUrl: null,

    _ensureAudio() {
        if (!this._audio) {
            this._audio = new Audio();
            this._audio.preload = "auto";
        }
        return this._audio;
    },

    _revokeAudioUrl() {
        if (!this._audioUrl) return;
        URL.revokeObjectURL(this._audioUrl);
        this._audioUrl = null;
    },

    _stopAudio() {
        const audio = this._audio;
        if (!audio) return;
        audio.onended = null;
        audio.onerror = null;
        audio.onplay = null;
        try {
            audio.pause();
        } catch {
            /* ignore */
        }
        this._revokeAudioUrl();
    },

    _notifySpeechBlocked() {
        if (!this._dotNetRef) return;
        this._dotNetRef.invokeMethodAsync("OnSpeechBlocked").catch(() => { });
    },

    /** Human neural voice. Device speechSynthesis is only a backup if the recording cannot be fetched. */
    async _speakHuman(text, token) {
        const response = await fetch("/onboarding-speech", {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                "Accept": "audio/mpeg"
            },
            body: JSON.stringify({ text })
        });

        if (!response.ok) {
            throw new Error("Human voice unavailable");
        }

        if (token !== this._speakToken) return "cancelled";
        const blob = await response.blob();
        if (token !== this._speakToken) return "cancelled";
        if (!blob || blob.size < 200) throw new Error("Human voice was empty");
        return this._playBlob(blob, token);
    },

    _playBlob(blob, token) {
        return new Promise((resolve) => {
            const audio = this._ensureAudio();
            this._revokeAudioUrl();
            const url = URL.createObjectURL(blob);
            this._audioUrl = url;

            let settled = false;
            const finish = (status) => {
                if (settled) return;
                settled = true;
                audio.onended = null;
                audio.onerror = null;
                audio.onplay = null;
                if (token === this._speakToken) {
                    this.setPresenterSpeaking(false);
                }
                resolve(status);
            };

            audio.onplay = () => {
                if (token !== this._speakToken) return;
                this._speechUnlocked = true;
                this.setPresenterSpeaking(true);
            };
            audio.onended = () => finish("ok");
            audio.onerror = () => finish("error");
            audio.onloadedmetadata = () => {
                const seconds = Number.isFinite(audio.duration) ? audio.duration : 20;
                const ms = Math.min(120000, Math.max(4000, seconds * 1000 + 2500));
                setTimeout(() => finish("ok"), ms);
            };
            audio.src = url;

            const played = audio.play();
            if (played && typeof played.then === "function") {
                played.then(() => {
                    if (token === this._speakToken) {
                        this._speechUnlocked = true;
                        this.setPresenterSpeaking(true);
                    }
                }).catch((err) => {
                    if (err && err.name === "NotAllowedError") {
                        this._notifySpeechBlocked();
                        finish("blocked");
                        return;
                    }
                    finish("error");
                });
            }
        });
    },

    speak(text, muted) {
        if (muted || !text) {
            return;
        }

        void this.speakAsync(text, false);
    },

    /** Resolves when the human voice finishes. Falls back only if that recording cannot play. */
    async speakAsync(text, muted) {
        if (muted || !text) {
            return;
        }

        if (!this._speechUnlocked && this._requiresSpeechGesture()) {
            return;
        }

        const token = ++this._speakToken;
        this._stopAudio();
        try {
            window.speechSynthesis?.cancel();
        } catch {
            /* ignore */
        }

        try {
            const status = await this._speakHuman(text, token);
            if (status === "ok" || status === "cancelled" || status === "blocked" || token !== this._speakToken) {
                return;
            }
        } catch {
            if (token !== this._speakToken) return;
        }

        await this._speakWithDeviceVoice(text, token);
    },

    /** Last resort. The browser reader is the mechanical voice patients asked us to stop using. */
    _speakWithDeviceVoice(text, token) {
        if (!window.speechSynthesis) {
            return Promise.resolve();
        }

        try {
            window.speechSynthesis.cancel();
            window.speechSynthesis.resume();
        } catch {
            /* ignore */
        }

        const chunks = this._splitSpeechChunks(text);
        if (!chunks.length) {
            return Promise.resolve();
        }

        const speakChunk = (index) => new Promise((resolve) => {
            if (token !== this._speakToken) {
                resolve();
                return;
            }

            const utterance = new SpeechSynthesisUtterance(chunks[index]);
            this._applyWarmVoice(utterance);

            let settled = false;
            const finish = () => {
                if (settled) return;
                settled = true;
                clearTimeout(watchdog);
                if (token !== this._speakToken) {
                    resolve();
                    return;
                }
                if (index === chunks.length - 1) {
                    this.setPresenterSpeaking(false);
                }
                resolve();
            };

            const watchdog = setTimeout(finish, this._estimateChunkMs(chunks[index]));

            utterance.onstart = () => {
                if (token === this._speakToken) {
                    this._speechUnlocked = true;
                    this.setPresenterSpeaking(true);
                    this._startResumeWatch();
                    try {
                        window.speechSynthesis.resume();
                    } catch {
                        /* ignore */
                    }
                }
            };
            utterance.onend = finish;
            utterance.onerror = finish;

            const startSpeak = () => {
                if (token !== this._speakToken) {
                    finish();
                    return;
                }
                try {
                    window.speechSynthesis.speak(utterance);
                    window.speechSynthesis.resume();
                } catch {
                    finish();
                }
            };

            if (this._isAppleTouch()) {
                setTimeout(startSpeak, 40);
            } else {
                startSpeak();
            }
        });

        const spoken = chunks.reduce(
            (chain, _, index) => chain.then(() => speakChunk(index)),
            Promise.resolve()
        );

        if (this.debugHangMs < 0) {
            void spoken;
            return new Promise(() => { });
        }

        if (this.debugHangMs > 0) {
            void spoken;
            const hangMs = this.debugHangMs;
            return new Promise((resolve) => setTimeout(resolve, hangMs));
        }

        return spoken;
    },

    cancelSpeech() {
        this._speakToken += 1;
        this._stopAudio();
        this._stopVisemeCycle();
        this.setPresenterSpeaking(false);
        if (window.speechSynthesis) {
            window.speechSynthesis.cancel();
        }
    },

    walkthrough: {
        _session: null,

        scenes: [
            {
                id: "dashboard",
                title: "Your Dashboard at a Glance",
                narration: "Let's start with your dashboard — your personalized home screen. Here you'll see blood pressure trends, medications due today, upcoming appointments, and your to-do list at a glance. You can customize which widgets appear using Dashboard Studio from the menu.",
                durationMs: 24000
            },
            {
                id: "medication",
                title: "Adding a Medication",
                narration: "To add a medication, open Medications from the menu and tap Add. Enter the medication name, dose, and schedule — just the basics for now. Medical Manager helps you track reminders so nothing is missed on busy days.",
                durationMs: 23000
            },
            {
                id: "blood-pressure",
                title: "Logging Blood Pressure",
                narration: "For blood pressure, open Blood Pressure from the menu and tap Add. Enter your systolic, diastolic, and pulse, then save. Your readings build a gentle trend chart on the dashboard so patterns are easier to spot over time.",
                durationMs: 23000
            },
            {
                id: "todo",
                title: "Creating a To Do",
                narration: "For anything you don't want to forget, visit To Do and tap Add. Write a short description, set a finish-by date, and choose a priority. It's a simple way to keep health tasks organized and top of mind.",
                durationMs: 22000
            },
            {
                id: "closing",
                title: "You're Ready!",
                narration: "That's the essentials — dashboard, medications, blood pressure, and to-dos. You're ready to explore on your own. We're glad you're here, and we wish you well on your health journey.",
                durationMs: 16000
            }
        ],

        start(rootId, options) {
            this.stop();

            const root = document.getElementById(rootId);
            if (!root) return;

            const reducedMotion = !!options?.reducedMotion;
            const muted = !!options?.muted;
            const dotNetRef = options?.dotNetRef ?? null;

            window.mmOnboarding._reducedMotion = reducedMotion;
            window.mmOnboarding.unlockSpeech();
            window.mmOnboarding.preloadPresenterImages();
            window.mmOnboarding.setPresenterViseme(0);

            const sceneEls = root.querySelectorAll("[data-walkthrough-scene]");
            const titleEl = root.querySelector("[data-walkthrough-title]");
            const progressEl = root.querySelector("[data-walkthrough-progress]");
            const timeEl = root.querySelector("[data-walkthrough-time]");
            const playBtn = root.querySelector("[data-walkthrough-play]");
            const totalMs = this.scenes.reduce((sum, s) => sum + s.durationMs, 0);

            let index = 0;
            let paused = false;
            let sceneTimer = null;
            let progressTimer = null;
            let sceneStartedAt = 0;
            let elapsedBeforePause = 0;
            let globalElapsed = 0;
            let scenePlayPromise = null;
            let scenePlayResolve = null;

            const formatTime = (ms) => {
                const sec = Math.max(0, Math.floor(ms / 1000));
                const m = Math.floor(sec / 60);
                const s = sec % 60;
                return `${m}:${s.toString().padStart(2, "0")}`;
            };

            const setPlayIcon = (playing) => {
                if (!playBtn) return;
                playBtn.textContent = playing ? "⏸" : "▶";
                playBtn.setAttribute("aria-label", playing ? "Pause walkthrough" : "Play walkthrough");
            };

            const updateProgress = () => {
                const scene = this.scenes[index];
                const sceneElapsed = paused
                    ? elapsedBeforePause
                    : elapsedBeforePause + (Date.now() - sceneStartedAt);
                const priorMs = this.scenes.slice(0, index).reduce((sum, s) => sum + s.durationMs, 0);
                globalElapsed = Math.min(totalMs, priorMs + sceneElapsed);
                const pct = (globalElapsed / totalMs) * 100;

                if (progressEl) progressEl.style.width = `${pct}%`;
                if (timeEl) timeEl.textContent = `${formatTime(globalElapsed)} / ${formatTime(totalMs)}`;
            };

            const showSceneVisuals = (i) => {
                index = i;
                const scene = this.scenes[i];
                elapsedBeforePause = 0;
                sceneStartedAt = Date.now();

                sceneEls.forEach((el) => {
                    el.classList.toggle("active", el.dataset.walkthroughScene === scene.id);
                });

                if (titleEl) titleEl.textContent = scene.title;
                updateProgress();
            };

            const waitForScene = async (i) => {
                const scene = this.scenes[i];
                showSceneVisuals(i);

                scenePlayPromise = new Promise((resolve) => {
                    scenePlayResolve = resolve;
                });

                if (!muted) {
                    await window.mmOnboarding.speakAsync(scene.narration, false);
                } else {
                    const waitMs = reducedMotion ? 8000 : scene.durationMs;
                    await new Promise((resolve) => {
                        sceneTimer = setTimeout(resolve, waitMs);
                    });
                }

                if (scenePlayResolve) {
                    scenePlayResolve();
                    scenePlayResolve = null;
                    scenePlayPromise = null;
                }

                if (!paused) {
                    await new Promise((resolve) => {
                        sceneTimer = setTimeout(resolve, reducedMotion ? 400 : 600);
                    });
                }
            };

            const playScenes = async (startIndex = 0) => {
                for (let i = startIndex; i < this.scenes.length; i++) {
                    if (paused) return;
                    await waitForScene(i);
                    if (paused) return;
                }
                finish();
            };

            const finish = () => {
                this.stop();
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync("OnWalkthroughComplete").catch(() => { });
                }
            };

            const pause = () => {
                if (paused) return;
                paused = true;
                elapsedBeforePause += Date.now() - sceneStartedAt;
                clearTimeout(sceneTimer);
                window.mmOnboarding.cancelSpeech();
                if (scenePlayResolve) {
                    scenePlayResolve();
                    scenePlayResolve = null;
                    scenePlayPromise = null;
                }
                setPlayIcon(false);
            };

            const resume = () => {
                if (!paused) return;
                paused = false;
                sceneStartedAt = Date.now();
                setPlayIcon(true);
                playScenes(index);
            };

            const skip = () => finish();

            const onPlayClick = () => (paused ? resume() : pause());
            const onSkipClick = () => skip();
            const onCloseClick = () => skip();

            if (playBtn) playBtn.addEventListener("click", onPlayClick);
            root.querySelector("[data-walkthrough-skip]")?.addEventListener("click", onSkipClick);
            root.querySelector("[data-walkthrough-close]")?.addEventListener("click", onCloseClick);

            progressTimer = setInterval(updateProgress, 250);

            this._session = {
                root,
                pause,
                resume,
                skip,
                cleanup: () => {
                    clearTimeout(sceneTimer);
                    clearInterval(progressTimer);
                    window.mmOnboarding.cancelSpeech();
                    if (playBtn) playBtn.removeEventListener("click", onPlayClick);
                    root.querySelector("[data-walkthrough-skip]")?.removeEventListener("click", onSkipClick);
                    root.querySelector("[data-walkthrough-close]")?.removeEventListener("click", onCloseClick);
                }
            };

            setPlayIcon(true);
            playScenes(0);
        },

        stop() {
            if (this._session) {
                this._session.cleanup();
                this._session = null;
            }
            window.mmOnboarding.cancelSpeech();
        }
    }
};

if (window.speechSynthesis) {
    window.speechSynthesis.onvoiceschanged = () => {
        window.speechSynthesis.getVoices();
    };
}
