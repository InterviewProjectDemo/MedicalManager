import { SoproTTS } from "/vendor/sopro/index.js";

var enginePromise = null;
var referenceKey = "";
var referencePromise = null;

function note(message) {
    var el = document.querySelector("[data-voice-prepare]");
    if (el)
        el.textContent = message || "";
}

function loadEngine() {
    if (!enginePromise) {
        note("Preparing your voice on this device. The first time can take a minute.");
        enginePromise = SoproTTS.create({
            backend: "auto",
            onProgress: function (update) {
                var total = update && update.total ? update.total : 0;
                var loaded = update && update.loaded ? update.loaded : 0;
                var pct = total ? Math.min(100, Math.round((loaded / total) * 100)) : 0;
                note(total
                    ? "Preparing your voice on this device… " + pct + "%"
                    : "Preparing your voice on this device…");
            }
        }).then(function (tts) {
            note("");
            return tts;
        }).catch(function (error) {
            enginePromise = null;
            note("This device could not prepare your voice. Accessibility will use the device voice until then.");
            throw error;
        });
    }
    return enginePromise;
}

function wavBlob(samples, sampleRate) {
    var buffer = new ArrayBuffer(44 + samples.length * 2);
    var view = new DataView(buffer);
    function write(offset, text) {
        for (var i = 0; i < text.length; i++)
            view.setUint8(offset + i, text.charCodeAt(i));
    }
    write(0, "RIFF");
    view.setUint32(4, 36 + samples.length * 2, true);
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
    view.setUint32(40, samples.length * 2, true);
    var offset = 44;
    for (var i = 0; i < samples.length; i++) {
        var sample = Math.max(-1, Math.min(1, samples[i]));
        view.setInt16(offset, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true);
        offset += 2;
    }
    return new Blob([buffer], { type: "audio/wav" });
}

function synthesize(referenceBlob, text, revision) {
    var key = String(revision || referenceBlob.size);
    return loadEngine().then(function (tts) {
        if (referenceKey !== key) {
            referenceKey = key;
            referencePromise = tts.prepareReference(referenceBlob);
        }
        return referencePromise.then(function (reference) {
            note("Speaking that line in your voice…");
            return tts.synthesize(String(text), reference, { language: "en" }).then(function (pcm) {
                note("");
                return wavBlob(pcm, tts.sampleRate);
            });
        });
    });
}

function clearReference() {
    referenceKey = "";
    referencePromise = null;
}

window.mmVoiceClone = {
    synthesize: synthesize,
    clearReference: clearReference
};
