/* Microphone, speaker, and camera chosen on Profile. Device ids belong to this
   browser, so the choice stays in localStorage and the next capture or playback
   on this machine uses it. */
(function () {
    var KEY = "mm-media-devices";
    var watchHandler = null;
    var watchRef = null;
    var previewStream = null;

    function emptySelection() {
        return { audioInputId: "", audioOutputId: "", videoInputId: "" };
    }

    function read() {
        try {
            var raw = localStorage.getItem(KEY);
            var parsed = raw ? JSON.parse(raw) : null;
            if (!parsed || typeof parsed !== "object")
                return emptySelection();
            return {
                audioInputId: parsed.audioInputId || "",
                audioOutputId: parsed.audioOutputId || "",
                videoInputId: parsed.videoInputId || ""
            };
        } catch (e) {
            return emptySelection();
        }
    }

    function applySpeech(outputId) {
        var id = outputId || "";
        try {
            if (window.speechSynthesis && typeof window.speechSynthesis.setSinkId === "function")
                window.speechSynthesis.setSinkId(id);
        } catch (e) { /* this browser keeps the system voice */ }
    }

    function save(selection) {
        selection = selection || {};
        var next = {
            audioInputId: selection.audioInputId || "",
            audioOutputId: selection.audioOutputId || "",
            videoInputId: selection.videoInputId || ""
        };
        try {
            localStorage.setItem(KEY, JSON.stringify(next));
        } catch (e) {
            throw new Error("This browser could not store the device choice.");
        }
        applySpeech(next.audioOutputId);
        return next;
    }

    function mapKind(devices, kind) {
        var seen = {};
        var result = [];
        for (var i = 0; i < devices.length; i++) {
            var device = devices[i];
            if (!device || device.kind !== kind || !device.deviceId || seen[device.deviceId])
                continue;
            seen[device.deviceId] = true;
            result.push({ deviceId: device.deviceId, label: device.label || "" });
        }
        return result;
    }

    function catalogFrom(devices) {
        var audioInputs = mapKind(devices, "audioinput");
        var audioOutputs = mapKind(devices, "audiooutput");
        var videoInputs = mapKind(devices, "videoinput");
        var named = audioInputs.concat(videoInputs);
        return {
            audioInputs: audioInputs,
            audioOutputs: audioOutputs,
            videoInputs: videoInputs,
            labelsHidden: named.length > 0 && named.every(function (device) { return !device.label; }),
            sinkSupported: typeof HTMLMediaElement !== "undefined" && !!HTMLMediaElement.prototype.setSinkId
        };
    }

    function emptyCatalog() {
        return catalogFrom([]);
    }

    function list() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices)
            return Promise.resolve(emptyCatalog());
        return navigator.mediaDevices.enumerateDevices().then(catalogFrom).catch(function () {
            return emptyCatalog();
        });
    }

    function stopStream(stream) {
        if (!stream)
            return;
        stream.getTracks().forEach(function (track) { track.stop(); });
    }

    function friendly(error, fallback) {
        if (error && typeof error.message === "string" && /^(Allow |No |This browser|The |Camera |That )/.test(error.message))
            throw error;
        if (error && (error.name === "NotAllowedError" || error.name === "SecurityError"))
            throw new Error(fallback || "Allow the microphone and camera in the browser prompt, then try again.");
        if (error && (error.name === "NotFoundError" || error.name === "OverconstrainedError"))
            throw new Error("That device is not available. Plug it in and refresh the list.");
        throw new Error(fallback || "The browser could not use that device. Refresh the list and try again.");
    }

    function requestAccess() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia)
            return Promise.reject(new Error("This browser cannot list cameras and microphones."));

        function open(audio, video) {
            return navigator.mediaDevices.getUserMedia({ audio: audio, video: video });
        }

        return open(true, true).then(function (stream) {
            stopStream(stream);
            return list();
        }).catch(function (bothFailed) {
            return Promise.all([
                open(true, false).then(function (stream) { return stream; }).catch(function (error) { return error; }),
                open(false, true).then(function (stream) { return stream; }).catch(function (error) { return error; })
            ]).then(function (results) {
                var errors = [bothFailed];
                var opened = false;
                results.forEach(function (result) {
                    if (result instanceof MediaStream) {
                        opened = true;
                        stopStream(result);
                    } else {
                        errors.push(result);
                    }
                });
                if (!opened)
                    friendly(errors[0], "Allow the microphone and camera in the browser prompt, then refresh the list.");
                return list();
            });
        });
    }

    function mediaConstraint(extra, deviceId) {
        if (!extra)
            return false;
        var base = typeof extra === "object" ? Object.assign({}, extra) : {};
        if (deviceId) {
            base.deviceId = { ideal: deviceId };
            delete base.facingMode;
        }
        return Object.keys(base).length ? base : true;
    }

    function openStream(requested) {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia)
            return Promise.reject(new Error("This browser cannot use a microphone or camera."));
        var selection = read();
        requested = requested || {};
        return navigator.mediaDevices.getUserMedia({
            audio: mediaConstraint(requested.audio, selection.audioInputId),
            video: mediaConstraint(requested.video, selection.videoInputId)
        });
    }

    function applySink(element, outputId) {
        if (!element || typeof element.setSinkId !== "function")
            return Promise.resolve(false);
        var id = outputId === undefined || outputId === null ? read().audioOutputId : String(outputId);
        if (!id)
            return Promise.resolve(true);
        return element.setSinkId(id).then(function () { return true; }).catch(function () { return false; });
    }

    function applyContext(audioContext) {
        if (!audioContext || typeof audioContext.setSinkId !== "function")
            return Promise.resolve(false);
        var id = read().audioOutputId;
        if (!id)
            return Promise.resolve(true);
        return audioContext.setSinkId(id).then(function () { return true; }).catch(function () { return false; });
    }

    function testSpeaker(outputId) {
        var Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx)
            return Promise.reject(new Error("This browser cannot play a test tone."));
        var audio = new Ctx();
        var oscillator = audio.createOscillator();
        var gain = audio.createGain();
        var destination = audio.createMediaStreamDestination();
        var element = new Audio();
        oscillator.type = "sine";
        oscillator.frequency.value = 880;
        oscillator.connect(gain);
        gain.connect(destination);
        element.srcObject = destination.stream;
        gain.gain.setValueAtTime(0.0001, audio.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.05, audio.currentTime + 0.04);
        gain.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + 0.7);
        oscillator.start();
        oscillator.stop(audio.currentTime + 0.75);

        var chosen = outputId || "";
        var unsupported = !!chosen && typeof element.setSinkId !== "function";
        var route = !chosen || unsupported
            ? Promise.resolve(true)
            : applySink(element, chosen);

        return route.then(function (routed) {
            if (chosen && !unsupported && !routed)
                throw new Error("That speaker is not available. Plug it in and refresh the list.");
            return element.play().then(function () {
                return new Promise(function (resolve) { setTimeout(resolve, 800); });
            }).then(function () { return unsupported ? "default" : "played"; });
        }).catch(function (error) {
            friendly(error, "The speaker test did not play. Pick another speaker and try again.");
        }).finally(function () {
            try { oscillator.stop(); } catch (e) { /* already stopped */ }
            try { element.pause(); } catch (e) { /* already quiet */ }
            element.srcObject = null;
            stopStream(destination.stream);
            audio.close().catch(function () { });
        });
    }

    var previewToken = 0;

    function startPreview(video, videoId) {
        var token = ++previewToken;
        stopStream(previewStream);
        previewStream = null;
        if (!(video instanceof HTMLVideoElement))
            return Promise.reject(new Error("Camera preview is not ready yet. Try again."));
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia)
            return Promise.reject(new Error("This browser cannot use a camera."));
        var videoConstraints = videoId ? { deviceId: { ideal: videoId } } : true;
        return navigator.mediaDevices.getUserMedia({ audio: false, video: videoConstraints }).then(function (stream) {
            if (token !== previewToken) {
                stopStream(stream);
                return;
            }
            previewStream = stream;
            video.muted = true;
            video.playsInline = true;
            video.srcObject = stream;
            return video.play();
        }).catch(function (error) {
            if (token !== previewToken)
                return;
            stopStream(previewStream);
            previewStream = null;
            friendly(error, "The camera did not start. Plug it in and refresh the list.");
        });
    }

    function stopPreview() {
        previewToken++;
        stopStream(previewStream);
        previewStream = null;
    }

    function watch(dotNetRef) {
        unwatch();
        if (!navigator.mediaDevices || !navigator.mediaDevices.addEventListener || !dotNetRef)
            return;
        watchRef = dotNetRef;
        watchHandler = function () {
            if (watchRef)
                watchRef.invokeMethodAsync("OnMediaDevicesChanged").catch(function () { });
        };
        navigator.mediaDevices.addEventListener("devicechange", watchHandler);
    }

    function unwatch() {
        if (watchHandler && navigator.mediaDevices && navigator.mediaDevices.removeEventListener)
            navigator.mediaDevices.removeEventListener("devicechange", watchHandler);
        watchHandler = null;
        watchRef = null;
    }

    window.mmDevices = {
        read: read,
        save: save,
        list: list,
        requestAccess: requestAccess,
        openStream: openStream,
        applySink: applySink,
        applyContext: applyContext,
        testSpeaker: testSpeaker,
        startPreview: startPreview,
        stopPreview: stopPreview,
        watch: watch,
        unwatch: unwatch
    };

    applySpeech(read().audioOutputId);
})();
