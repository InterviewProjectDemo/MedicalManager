(function () {
    var PROMPTS = [
        { id: "doing", label: "How are you", phrase: "How are you doing?" },
        { id: "birthday", label: "Birthday", phrase: "Today is my birthday." },
        { id: "water", label: "Water", phrase: "I would like a glass of water." },
        { id: "weather", label: "Weather", phrase: "The weather is nice today." }
    ];
    var BUTTONS = [
        { id: "help", label: "Help", phrase: "Please help.", image: "/studio/dialysis-help.jpg" },
        { id: "family", label: "Family", phrase: "Please call my family.", image: "/studio/dialysis-family.jpg" },
        { id: "pain", label: "Pain", phrase: "I am in pain. Please help.", image: "/studio/dialysis-pain.jpg" },
        { id: "breathing", label: "Breathing", phrase: "I am having a breathing issue. Please help.", image: "/studio/dialysis-breathing.jpg" },
        { id: "blood", label: "Blood", phrase: "I am bleeding. Please help.", image: "/studio/dialysis-blood.jpg" }
    ];
    var PHRASE_KEY = "mm-speech-lines";
    var NEEDED = 4;
    var boundUser = "";
    var voiceReady = false;
    var dbPromise = null;
    var stream = null;
    var audioCtx = null;
    var analyser = null;
    var meterTimer = null;
    var stopTimer = null;
    var recorder = null;
    var recorderMime = "";
    var chunks = [];
    var samples = [];
    var takeStarted = 0;
    var speechHeardAt = 0;
    var activeUser = "";
    var finishing = false;
    var finishPromise = null;
    var synthChain = Promise.resolve();
    var prewarmToken = 0;
    var resetGeneration = 0;
    var phraseBlobs = new Map();

    function speechLines() {
        var saved = {};
        try {
            saved = JSON.parse(localStorage.getItem(PHRASE_KEY) || "{}") || {};
        } catch (e) {
            saved = {};
        }
        return BUTTONS.map(function (button) {
            var custom = typeof saved[button.id] === "string" ? saved[button.id].trim() : "";
            return {
                id: button.id,
                label: button.label,
                image: button.image,
                phrase: custom || button.phrase
            };
        });
    }

    function saveSpeechLine(id, phrase) {
        var button = BUTTONS.filter(function (item) { return item.id === id; })[0];
        if (!button)
            return speechLines();
        var text = String(phrase || "").replace(/\s+/g, " ").trim();
        if (!text || text.length > 180)
            text = text ? text.slice(0, 180) : button.phrase;
        var saved = {};
        try {
            saved = JSON.parse(localStorage.getItem(PHRASE_KEY) || "{}") || {};
        } catch (e) {
            saved = {};
        }
        if (text === button.phrase)
            delete saved[id];
        else
            saved[id] = text;
        try { localStorage.setItem(PHRASE_KEY, JSON.stringify(saved)); } catch (e) { /* this browser may block storage */ }
        if (boundUser && voiceReady)
            speakPhrase(text);
        return speechLines();
    }

    function helpLines() {
        return speechLines().map(function (line) { return line.phrase; });
    }

    function rememberPhrase(text, blob) {
        if (!text || !blob)
            return blob || null;
        phraseBlobs.set(String(text), blob);
        return blob;
    }

    function peekPhrase(phrase) {
        return phraseBlobs.get(String(phrase)) || null;
    }

    function warmPhraseCache(userId) {
        helpLines().forEach(function (line) {
            readKey(spokenKey(userId, line)).then(function (blob) {
                if (blob && boundUser === userId)
                    rememberPhrase(line, blob);
            }).catch(function () { });
        });
    }

    function openDb() {
        if (!dbPromise) {
            dbPromise = new Promise(function (resolve, reject) {
                var request = indexedDB.open("mm-voice", 1);
                request.onupgradeneeded = function () {
                    if (!request.result.objectStoreNames.contains("clips"))
                        request.result.createObjectStore("clips");
                };
                request.onsuccess = function () { resolve(request.result); };
                request.onerror = function () { reject(request.error); };
            });
        }
        return dbPromise;
    }

    function profileKey(userId) {
        return userId + "\nprofile";
    }

    function spokenKey(userId, text) {
        return userId + "\nsay\n" + text;
    }

    function readKey(key) {
        return openDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction("clips", "readonly");
                var request = tx.objectStore("clips").get(key);
                request.onsuccess = function () { resolve(request.result || null); };
                request.onerror = function () { reject(request.error); };
            });
        });
    }

    function writeKey(key, value) {
        return openDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction("clips", "readwrite");
                tx.objectStore("clips").put(value, key);
                tx.oncomplete = function () { resolve(); };
                tx.onerror = function () { reject(tx.error); };
            });
        });
    }

    function readProfile(userId) {
        return readKey(profileKey(userId));
    }

    function mimeType() {
        var types = ["audio/webm;codecs=opus", "audio/webm", "audio/mp4"];
        if (!window.MediaRecorder)
            return "";
        for (var i = 0; i < types.length; i++) {
            if (MediaRecorder.isTypeSupported(types[i]))
                return types[i];
        }
        return "";
    }

    function setMeter(level) {
        var bar = document.querySelector("[data-voice-meter] span");
        if (bar)
            bar.style.width = Math.max(0, Math.min(100, Math.round(level * 280))) + "%";
    }

    function sampleLevel() {
        if (!analyser)
            return { rms: 0, peak: 0 };
        var data = new Uint8Array(analyser.fftSize);
        analyser.getByteTimeDomainData(data);
        var sum = 0;
        var peak = 0;
        for (var i = 0; i < data.length; i++) {
            var value = (data[i] - 128) / 128;
            var size = Math.abs(value);
            if (size > peak)
                peak = size;
            sum += value * value;
        }
        return { rms: Math.sqrt(sum / data.length), peak: peak };
    }

    function ensureMic() {
        if (stream)
            return Promise.resolve();
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia)
            return Promise.reject(new Error("This browser cannot use a microphone."));
        var audioConstraints = { echoCancellation: true, noiseSuppression: true, channelCount: 1 };
        var opening = (window.mmDevices && window.mmDevices.openStream)
            ? window.mmDevices.openStream({ audio: audioConstraints, video: false })
            : navigator.mediaDevices.getUserMedia({ audio: audioConstraints });
        return opening.then(function (next) {
            stream = next;
            var Ctx = window.AudioContext || window.webkitAudioContext;
            audioCtx = new Ctx();
            if (audioCtx.state === "suspended")
                audioCtx.resume();
            var source = audioCtx.createMediaStreamSource(stream);
            analyser = audioCtx.createAnalyser();
            analyser.fftSize = 2048;
            source.connect(analyser);
        });
    }

    function judge(elapsed) {
        var loud = samples.filter(function (sample) { return sample.rms > 0.02; });
        var mean = 0;
        var peak = 0;
        loud.forEach(function (sample) {
            mean += sample.rms;
            if (sample.peak > peak)
                peak = sample.peak;
        });
        mean = loud.length ? mean / loud.length : 0;
        var speechSeconds = Math.round(loud.length * 0.08 * 10) / 10;
        if (elapsed < 0.7 || speechSeconds < 0.8)
            return { clear: false, reason: "That was too short. Read the line, then pause.", seconds: 0, score: 0 };
        if (mean < 0.035)
            return { clear: false, reason: "That was too quiet. Move closer and speak in a full voice.", seconds: 0, score: 0 };
        if (peak > 0.97 && mean > 0.22)
            return { clear: false, reason: "That was too loud and muddy. Hold the device a little farther away.", seconds: 0, score: 0 };
        var level = Math.max(0, 1 - Math.abs(mean - 0.09) / 0.09);
        return { clear: true, reason: "", seconds: speechSeconds, score: level };
    }

    function writeWav(samplesOut, sampleRate) {
        var buffer = new ArrayBuffer(44 + samplesOut.length * 2);
        var view = new DataView(buffer);
        function write(offset, text) {
            for (var i = 0; i < text.length; i++)
                view.setUint8(offset + i, text.charCodeAt(i));
        }
        write(0, "RIFF");
        view.setUint32(4, 36 + samplesOut.length * 2, true);
        write(8, "WAVE");
        write(12, "fmt ");
        view.setUint32(16, 16, true);
        view.setUint16(20, 1, true);
        view.setUint16(22, 1, true);
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);
        view.setUint16(32, 2, true);
        view.setUint16(34, 16, true);
        write(36, "data");
        view.setUint32(40, samplesOut.length * 2, true);
        var offset = 44;
        for (var i = 0; i < samplesOut.length; i++) {
            var sample = Math.max(-1, Math.min(1, samplesOut[i]));
            view.setInt16(offset, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true);
            offset += 2;
        }
        return new Blob([buffer], { type: "audio/wav" });
    }

    function combinePieces(pieces) {
        var Ctx = window.AudioContext || window.webkitAudioContext;
        var ctx = new Ctx();
        return Promise.all(pieces.map(function (piece) {
            return piece.blob.arrayBuffer().then(function (raw) {
                return ctx.decodeAudioData(raw.slice(0));
            });
        })).then(function (buffers) {
            var rate = buffers[0].sampleRate;
            var length = 0;
            buffers.forEach(function (buffer) { length += buffer.length; });
            var merged = new Float32Array(length);
            var offset = 0;
            buffers.forEach(function (buffer) {
                merged.set(buffer.getChannelData(0), offset);
                offset += buffer.length;
            });
            var start = 0;
            var end = merged.length;
            while (start < end && Math.abs(merged[start]) < 0.012)
                start++;
            while (end > start && Math.abs(merged[end - 1]) < 0.012)
                end--;
            var voice = merged.subarray(start, Math.max(start + 1, end));
            var peak = 0;
            for (var i = 0; i < voice.length; i++)
                peak = Math.max(peak, Math.abs(voice[i]));
            if (peak > 0.02 && peak < 0.92) {
                var gain = 0.9 / peak;
                for (var j = 0; j < voice.length; j++)
                    voice[j] *= gain;
            }
            return ctx.close().catch(function () { }).then(function () {
                return writeWav(voice, rate);
            });
        });
    }

    function finishTake() {
        if (!recorder || finishing)
            return;
        finishing = true;
        if (meterTimer) {
            clearInterval(meterTimer);
            meterTimer = null;
        }
        if (stopTimer) {
            clearTimeout(stopTimer);
            stopTimer = null;
        }
        var elapsed = (Date.now() - takeStarted) / 1000;
        var userId = activeUser;
        var mime = recorderMime;
        var generation = resetGeneration;
        return new Promise(function (resolve) {
            if (!recorder) {
                resolve("");
                return;
            }
            recorder.onstop = function () { resolve(""); };
            if (recorder.state !== "inactive")
                recorder.stop();
            else
                resolve("");
        }).then(function () {
            setMeter(0);
            var active = recorder;
            recorder = null;
            finishing = false;
            if (generation !== resetGeneration)
                return { discarded: true, clear: false, reason: "", seconds: 0, ready: false, micDenied: false, score: 0 };
            if (!active)
                return { clear: false, reason: "The recording window was closed.", seconds: 0, ready: false, micDenied: false, score: 0 };
            var verdict = judge(elapsed);
            if (!verdict.clear)
                return Object.assign({ ready: false, micDenied: false }, verdict);
            var blob = new Blob(chunks, { type: mime || "audio/webm" });
            if (generation !== resetGeneration)
                return { discarded: true, clear: false, reason: "", seconds: 0, ready: false, micDenied: false, score: 0 };
            return readProfile(userId).then(function (profile) {
                var pieces = (profile && profile.pieces) ? profile.pieces.slice() : [];
                pieces.push({ score: verdict.score, seconds: verdict.seconds, blob: blob });
                var seconds = 0;
                pieces.forEach(function (piece) { seconds += piece.seconds || 0; });
                seconds = Math.round(seconds * 10) / 10;
                var ready = seconds >= NEEDED;
                var next = {
                    pieces: pieces,
                    seconds: seconds,
                    ready: ready,
                    revision: Date.now(),
                    wav: null
                };
                var store = generation !== resetGeneration
                    ? Promise.resolve()
                    : ready
                        ? combinePieces(pieces).then(function (wav) {
                            if (generation !== resetGeneration)
                                return;
                            next.wav = wav;
                            return writeKey(profileKey(userId), next);
                        })
                        : writeKey(profileKey(userId), next);
                return store.then(function () {
                    if (generation !== resetGeneration)
                        return { discarded: true, clear: false, reason: "", seconds: 0, ready: false, micDenied: false, score: 0 };
                    if (userId === boundUser)
                        voiceReady = ready;
                    verdict.ready = ready;
                    verdict.seconds = seconds;
                    verdict.micDenied = false;
                    verdict.reason = ready
                        ? "Your voice is saved. Accessibility messages will be spoken in this voice."
                        : "Clear. Say another line. " + seconds.toFixed(1) + " of " + NEEDED + " seconds so far.";
                    if (ready)
                        prewarm(userId, next.revision);
                    return verdict;
                });
            });
        });
    }

    function runTake(userId) {
        activeUser = userId;
        chunks = [];
        samples = [];
        speechHeardAt = 0;
        finishing = false;
        return ensureMic().then(function () {
            recorderMime = mimeType();
            recorder = recorderMime ? new MediaRecorder(stream, { mimeType: recorderMime }) : new MediaRecorder(stream);
            recorder.ondataavailable = function (event) {
                if (event.data && event.data.size)
                    chunks.push(event.data);
            };
            takeStarted = Date.now();
            recorder.start();
            meterTimer = setInterval(function () {
                var level = sampleLevel();
                samples.push(level);
                setMeter(level.rms);
                var now = Date.now();
                if (level.rms > 0.045)
                    speechHeardAt = now;
                var elapsed = now - takeStarted;
                if (elapsed > 12000 || (speechHeardAt && elapsed > 1200 && now - speechHeardAt > 1100))
                    requestFinish();
            }, 80);
            stopTimer = setTimeout(requestFinish, 14000);
            finishPromise = new Promise(function (resolve) {
                runTake._resolve = resolve;
            });
            return finishPromise;
        }).catch(function (error) {
            var denied = error && (error.name === "NotAllowedError" || error.name === "SecurityError");
            return {
                clear: false,
                reason: denied
                    ? "The microphone is blocked. Allow the microphone for this site, then try again."
                    : (error && error.message) || "The microphone could not be started.",
                seconds: 0,
                ready: false,
                micDenied: !!denied,
                score: 0
            };
        });
    }

    function requestFinish() {
        var pending = finishTake();
        if (!pending || !runTake._resolve)
            return;
        var resolve = runTake._resolve;
        runTake._resolve = null;
        pending.then(resolve).catch(function (error) {
            resolve({
                clear: false,
                reason: (error && error.message) || "That take could not be saved.",
                seconds: 0,
                ready: false,
                micDenied: false,
                score: 0
            });
        });
    }

    function cloneApi() {
        if (window.mmVoiceClone)
            return Promise.resolve(window.mmVoiceClone);
        return new Promise(function (resolve, reject) {
            var started = Date.now();
            var timer = setInterval(function () {
                if (window.mmVoiceClone) {
                    clearInterval(timer);
                    resolve(window.mmVoiceClone);
                } else if (Date.now() - started > 15000) {
                    clearInterval(timer);
                    reject(new Error("The voice model did not load."));
                }
            }, 50);
        });
    }

    function speakPhraseNow(text) {
        if (!boundUser)
            return Promise.resolve(null);
        return readKey(spokenKey(boundUser, text)).then(function (cached) {
            if (cached)
                return rememberPhrase(text, cached);
            return readProfile(boundUser).then(function (profile) {
                if (!profile || !profile.ready || !profile.wav)
                    return null;
                var revision = profile.revision;
                return cloneApi().then(function (clone) {
                    return clone.synthesize(profile.wav, text, revision);
                }).then(function (blob) {
                    if (!blob)
                        return null;
                    return readProfile(boundUser).then(function (current) {
                        if (!current || current.revision !== revision)
                            return null;
                        return writeKey(spokenKey(boundUser, text), blob).then(function () { return rememberPhrase(text, blob); });
                    });
                });
            });
        });
    }

    function speakPhrase(text) {
        var run = synthChain.then(function () { return speakPhraseNow(text); }, function () { return speakPhraseNow(text); });
        synthChain = run.then(function () { }, function () { });
        return run;
    }

    function prewarm(userId, revision) {
        var token = ++prewarmToken;
        helpLines().reduce(function (chain, line) {
            return chain.then(function () {
                if (token !== prewarmToken || boundUser !== userId)
                    return null;
                return readProfile(userId).then(function (profile) {
                    if (!profile || profile.revision !== revision)
                        return null;
                    return speakPhrase(line);
                });
            });
        }, Promise.resolve()).catch(function () { });
    }

    function bindRecord(element, userId, dotnet) {
        if (!element || !dotnet)
            return;
        element.onclick = function (event) {
            event.preventDefault();
            event.stopPropagation();
            if (recorder && recorder.state === "recording") {
                requestFinish();
                return;
            }
            dotnet.invokeMethodAsync("Listening");
            runTake(userId).then(function (result) {
                if (result && result.discarded)
                    return;
                return dotnet.invokeMethodAsync("TakeFinished", result);
            }).catch(function (error) {
                return dotnet.invokeMethodAsync("TakeFinished", {
                    clear: false,
                    reason: (error && error.message) || "That take could not be saved.",
                    seconds: 0,
                    ready: false,
                    micDenied: false,
                    score: 0
                });
            });
        };
    }

    function playBlob(blob) {
        if (window.mmAudio)
            return window.mmAudio.play(blob);
        var audio = new Audio(URL.createObjectURL(blob));
        audio.volume = 1;
        var sink = (window.mmDevices && window.mmDevices.applySink)
            ? window.mmDevices.applySink(audio)
            : Promise.resolve();
        return Promise.resolve(sink).catch(function () { }).then(function () {
            return audio.play().then(function () { return true; }).catch(function () { return false; });
        });
    }

    function bindPreview(element, userId) {
        if (!element)
            return;
        element.onclick = function (event) {
            event.preventDefault();
            event.stopPropagation();
            var note = document.querySelector("[data-voice-prepare]");
            if (note)
                note.textContent = "Preparing Please help in your voice.";
            var previous = boundUser;
            boundUser = userId;
            speakPhrase("Please help.").then(function (blob) {
                boundUser = previous || userId;
                if (!blob) {
                    if (note)
                        note.textContent = "Your voice is saved, but this browser could not speak with it yet.";
                    return;
                }
                if (note)
                    note.textContent = "That was Please help, in your voice.";
                playBlob(blob);
            }).catch(function (error) {
                boundUser = previous || userId;
                if (note)
                    note.textContent = (error && error.message) || "The voice could not be prepared.";
            });
        };
    }

    function status(userId) {
        return readProfile(userId).then(function (profile) {
            var seconds = profile && profile.seconds ? profile.seconds : 0;
            if (userId === boundUser)
                voiceReady = !!(profile && profile.ready);
            return {
                seconds: Math.round(seconds * 10) / 10,
                needed: NEEDED,
                ready: !!(profile && profile.ready),
                prompts: PROMPTS.map(function (line) {
                    return { id: line.id, label: line.label, phrase: line.phrase };
                })
            };
        });
    }

    function abortTake() {
        resetGeneration += 1;
        if (meterTimer) {
            clearInterval(meterTimer);
            meterTimer = null;
        }
        if (stopTimer) {
            clearTimeout(stopTimer);
            stopTimer = null;
        }
        if (recorder) {
            try {
                recorder.onstop = null;
                if (recorder.state !== "inactive")
                    recorder.stop();
            } catch (e) { }
            recorder = null;
        }
        finishing = false;
        chunks = [];
        setMeter(0);
        if (runTake._resolve) {
            var resolve = runTake._resolve;
            runTake._resolve = null;
            resolve({ discarded: true, clear: false, reason: "", seconds: 0, ready: false, micDenied: false, score: 0 });
        }
    }

    function reset(userId) {
        phraseBlobs.clear();
        prewarmToken += 1;
        abortTake();
        if (userId === boundUser)
            voiceReady = false;
        if (window.mmVoiceClone)
            window.mmVoiceClone.clearReference();
        var note = document.querySelector("[data-voice-prepare]");
        if (note)
            note.textContent = "";
        return openDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction("clips", "readwrite");
                var store = tx.objectStore("clips");
                var request = store.getAllKeys();
                request.onsuccess = function () {
                    (request.result || []).forEach(function (key) {
                        if (typeof key === "string" && key.indexOf(userId + "\n") === 0)
                            store.delete(key);
                    });
                };
                tx.oncomplete = function () { resolve(); };
                tx.onerror = function () { reject(tx.error); };
            });
        });
    }

    function cachedPhrase(phrase) {
        if (!boundUser || !voiceReady)
            return Promise.resolve(null);
        return readKey(spokenKey(boundUser, phrase)).then(function (blob) {
            return rememberPhrase(phrase, blob);
        });
    }

    function hasCachedPhrase(phrase) {
        return cachedPhrase(phrase).then(function (blob) { return !!blob; });
    }

    function hasVoice() {
        return voiceReady;
    }

    function bindUser(userId) {
        boundUser = userId || "";
        voiceReady = false;
        if (!boundUser)
            return Promise.resolve(false);
        return readProfile(boundUser).then(function (profile) {
            voiceReady = !!(profile && profile.ready && profile.wav);
            if (voiceReady) {
                prewarm(boundUser, profile.revision);
                warmPhraseCache(boundUser);
            }
            return voiceReady;
        });
    }

    function ensureVoice() {
        if (voiceReady)
            return Promise.resolve(true);
        if (!boundUser)
            return Promise.resolve(false);
        return bindUser(boundUser);
    }

    function watchOwnVoice(dotnet) {
        if (watchOwnVoice.handler)
            document.removeEventListener("mm-own-voice", watchOwnVoice.handler);
        watchOwnVoice.handler = function () {
            dotnet.invokeMethodAsync("OwnVoiceStarted");
        };
        document.addEventListener("mm-own-voice", watchOwnVoice.handler);
    }

    function unwatchOwnVoice() {
        if (watchOwnVoice.handler)
            document.removeEventListener("mm-own-voice", watchOwnVoice.handler);
        watchOwnVoice.handler = null;
    }

    function release() {
        if (recorder && recorder.state === "recording") {
            try { recorder.stop(); } catch (e) { }
        }
        recorder = null;
        if (meterTimer) {
            clearInterval(meterTimer);
            meterTimer = null;
        }
        if (stopTimer) {
            clearTimeout(stopTimer);
            stopTimer = null;
        }
        if (stream) {
            stream.getTracks().forEach(function (track) { track.stop(); });
            stream = null;
        }
        if (audioCtx) {
            audioCtx.close();
            audioCtx = null;
        }
        analyser = null;
        setMeter(0);
    }

    window.mmVoice = {
        bindUser: bindUser,
        bindRecord: bindRecord,
        bindPreview: bindPreview,
        status: status,
        reset: reset,
        speakPhrase: speakPhrase,
        cachedPhrase: cachedPhrase,
        peekPhrase: peekPhrase,
        hasCachedPhrase: hasCachedPhrase,
        hasVoice: hasVoice,
        ensureVoice: ensureVoice,
        lines: speechLines,
        saveLine: saveSpeechLine,
        watchOwnVoice: watchOwnVoice,
        unwatchOwnVoice: unwatchOwnVoice,
        release: release,
        needed: NEEDED
    };
})();
