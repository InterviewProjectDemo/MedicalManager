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

    function watchHelp() {
        if (!helpCall || helpCall.stopped || helpCall.waiting)
            return;
        if (!helpCall.usingVoice || helpCall.between)
            return;
        if (helpCall.audio && helpCall.audio.paused)
            helpCall.audio.play().catch(function () { });
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
        if (!ready)
            return false;
        if (window.mmVoice.cachedPhrase) {
            try {
                var clip = await window.mmVoice.cachedPhrase(phrase);
                if (clip)
                    return await beginVoiceLoop(phrase, clip);
            } catch (e) { /* the saved voice is generated next */ }
        }
        helpCall = {
            phrase: String(phrase),
            stopped: false,
            pending: true,
            quietSince: 0,
            generation: ++helpGeneration,
            usingVoice: false,
            waiting: true
        };
        if (!helpWatch)
            helpWatch = setInterval(watchHelp, 1000);
        window.mmVoice.speakPhrase(phrase).then(function (blob) {
            if (!blob || !helpCall || helpCall.stopped || helpCall.phrase !== String(phrase))
                return;
            helpCall.usingVoice = true;
            helpCall.waiting = false;
            helpCall.pending = false;
            helpCall.between = false;
            return playOwnVoice(blob).then(function (played) {
                if (played)
                    document.dispatchEvent(new CustomEvent("mm-own-voice"));
            });
        }).catch(function () { });
        return true;
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
    }

    function helpCallPhrase() {
        return helpCall && !helpCall.stopped ? helpCall.phrase : "";
    }

    function speak(text) {
        if (doctorView() || helpCall || !text)
            return;
        if (document.documentElement.classList.contains("a11y-motion"))
            return;
        if (!window.mmVoice || !window.mmVoice.ensureVoice)
            return;
        window.mmVoice.ensureVoice().then(function (ready) {
            if (!ready || helpCall)
                return;
            return window.mmVoice.speakPhrase(text).then(function (blob) {
                if (!blob || helpCall)
                    return;
                var audio = new Audio(URL.createObjectURL(blob));
                audio.volume = 1;
                audio.play().catch(function () { });
            });
        }).catch(function () { });
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

    var LAYOUT_KEY = "mm-a11y-layout";

    function readLayout() {
        try {
            var raw = localStorage.getItem(LAYOUT_KEY);
            var data = raw ? JSON.parse(raw) : null;
            if (!data || typeof data !== "object")
                return { tabs: {}, boards: {} };
            data.tabs = data.tabs && typeof data.tabs === "object" ? data.tabs : {};
            data.boards = data.boards && typeof data.boards === "object" ? data.boards : {};
            return data;
        } catch (e) {
            return { tabs: {}, boards: {} };
        }
    }

    function writeLayout(data) {
        try { localStorage.setItem(LAYOUT_KEY, JSON.stringify(data)); } catch (e) { /* this browser may block storage */ }
    }

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function watchLayout(root) {
        if (!root || root.dataset.a11yWatch === "1")
            return;
        root.dataset.a11yWatch = "1";
        bindGestures(root);
        var frame = 0;
        var applySoon = function () {
            if (root.dataset.a11yDragging === "1")
                return;
            cancelAnimationFrame(frame);
            frame = requestAnimationFrame(function () { layoutRoot(root); });
        };
        var observer = new MutationObserver(applySoon);
        observer.observe(root, { childList: true, subtree: true });
        if (window.ResizeObserver) {
            var resize = new ResizeObserver(applySoon);
            resize.observe(root);
        }
        applySoon();
    }

    function layoutRoot(root) {
        if (!root || !root.isConnected || root.dataset.a11yDragging === "1")
            return;
        var data = readLayout();
        applyTabWeights(root, data);
        var board = root.querySelector("[data-a11y-board]");
        if (!board)
            return;
        if (board.dataset.a11yBoardWatch !== "1" && window.ResizeObserver) {
            board.dataset.a11yBoardWatch = "1";
            var boardResize = new ResizeObserver(function () {
                if (root.dataset.a11yDragging === "1")
                    return;
                layoutRoot(root);
            });
            boardResize.observe(board);
        }
        var tiles = Array.prototype.slice.call(board.querySelectorAll("[data-a11y-tile]"));
        if (tiles.length === 0)
            return;
        var width = board.clientWidth;
        var height = board.clientHeight;
        if (width < 80 || height < 80)
            return;
        var boardId = board.dataset.a11yBoard || "general";
        var saved = data.boards[boardId];
        if (!saved || typeof saved !== "object" || !sameTiles(tiles, saved))
            saved = {};
        var changed = placeMissing(board, tiles, saved, width, height);
        tiles.forEach(function (tile) {
            applyBox(tile, saved[tile.dataset.a11yTile], width, height);
        });
        if (changed) {
            data.boards[boardId] = saved;
            writeLayout(data);
        }
    }

    function applyTabWeights(root, data) {
        var tabs = root.querySelectorAll("[data-a11y-tab]");
        Array.prototype.forEach.call(tabs, function (tab) {
            var weight = Number(data.tabs[tab.dataset.a11yTab]);
            if (!(weight > 0))
                weight = 1;
            tab.style.flex = weight + " 1 0px";
            tab.style.maxWidth = "none";
            tab.style.minWidth = "0";
        });
    }

    function sameTiles(tiles, saved) {
        var ids = tiles.map(function (tile) { return tile.dataset.a11yTile; });
        var keys = Object.keys(saved);
        if (keys.length !== ids.length)
            return false;
        return ids.every(function (id) { return !!saved[id]; });
    }

    function columnCount(board, count, width, height) {
        if (board.dataset.a11yFill === "row")
            return Math.max(1, count);
        return Math.max(1, Math.round(Math.sqrt(count * (width / Math.max(height, 1)))));
    }

    function placeMissing(board, tiles, saved, width, height) {
        var missing = tiles.filter(function (tile) { return !saved[tile.dataset.a11yTile]; });
        if (missing.length === 0)
            return false;
        var gap = 8;
        if (missing.length === tiles.length) {
            var cols = columnCount(board, tiles.length, width, height);
            var rows = Math.ceil(tiles.length / cols);
            var tw = (width - gap * (cols + 1)) / cols;
            var th = (height - gap * (rows + 1)) / rows;
            tiles.forEach(function (tile, index) {
                var column = index % cols;
                var row = Math.floor(index / cols);
                saved[tile.dataset.a11yTile] = {
                    x: (gap + column * (tw + gap)) / width,
                    y: (gap + row * (th + gap)) / height,
                    w: tw / width,
                    h: th / height
                };
            });
            return true;
        }

        var columns = columnCount(board, tiles.length, width, height);
        var cellWidth = (width - gap * (columns + 1)) / columns;
        var cellHeight = (height - gap * (Math.ceil(tiles.length / columns) + 1)) / Math.ceil(tiles.length / columns);
        var start = tiles.length - missing.length;
        missing.forEach(function (tile, index) {
            var slot = start + index;
            var column = slot % columns;
            var row = Math.floor(slot / columns);
            saved[tile.dataset.a11yTile] = {
                x: (gap + column * (cellWidth + gap)) / width,
                y: (gap + row * (cellHeight + gap)) / height,
                w: cellWidth / width,
                h: cellHeight / height
            };
        });
        return true;
    }

    function applyBox(tile, box, width, height) {
        if (!box)
            return;
        var w = clamp(box.w * width, 72, width);
        var h = clamp(box.h * height, 56, height);
        var x = clamp(box.x * width, 0, Math.max(0, width - w));
        var y = clamp(box.y * height, 0, Math.max(0, height - h));
        tile.style.left = x + "px";
        tile.style.top = y + "px";
        tile.style.width = w + "px";
        tile.style.height = h + "px";
    }

    function bindGestures(root) {
        var drag = null;

        root.addEventListener("dragstart", function (event) {
            if (event.target instanceof Element && event.target.closest("[data-tab-resize], [data-tile-resize]"))
                event.preventDefault();
        }, true);

        root.addEventListener("pointerdown", function (event) {
            if (event.button !== undefined && event.button !== 0)
                return;
            var target = event.target;
            if (!(target instanceof Element))
                return;
            if (target.closest(".a11y-tab-x, .studio-float-close, .studio-float-resize"))
                return;

            var tabResize = target.closest("[data-tab-resize]");
            var tab = target.closest("[data-a11y-tab]");
            if (tabResize && tab) {
                event.preventDefault();
                event.stopPropagation();
                var data = readLayout();
                var weights = {};
                root.querySelectorAll("[data-a11y-tab]").forEach(function (item) {
                    var weight = Number(data.tabs[item.dataset.a11yTab]);
                    weights[item.dataset.a11yTab] = weight > 0 ? weight : 1;
                });
                drag = {
                    kind: "tab",
                    pointerId: event.pointerId,
                    tab: tab,
                    startX: event.clientX,
                    startWeight: weights[tab.dataset.a11yTab],
                    weights: weights,
                    barWidth: tab.parentElement ? tab.parentElement.clientWidth : 1,
                    moved: false
                };
                root.dataset.a11yDragging = "1";
                try { tab.setPointerCapture(event.pointerId); } catch (e) { /* the window listeners still follow the pointer */ }
                return;
            }

            var tile = target.closest("[data-a11y-tile]");
            if (!tile)
                return;
            var board = tile.closest("[data-a11y-board]");
            if (!board)
                return;
            var resizeHandle = target.closest("[data-tile-resize]");
            var resizing = !!resizeHandle;
            if (resizing) {
                event.preventDefault();
                event.stopPropagation();
            }
            var boardRect = board.getBoundingClientRect();
            var rect = tile.getBoundingClientRect();
            drag = {
                kind: "tile",
                mode: resizing ? "resize" : "move",
                pointerId: event.pointerId,
                tile: tile,
                board: board,
                startX: event.clientX,
                startY: event.clientY,
                origX: rect.left - boardRect.left,
                origY: rect.top - boardRect.top,
                origW: rect.width,
                origH: rect.height,
                moved: false
            };
            if (resizing) {
                try { resizeHandle.setPointerCapture(event.pointerId); } catch (e) { /* drag still follows while the pointer stays down */ }
            }
        });

        root.addEventListener("pointermove", function (event) {
            if (!drag || event.pointerId !== drag.pointerId)
                return;
            if (drag.kind === "tab") {
                if (Math.abs(event.clientX - drag.startX) < 4)
                    return;
                var total = Object.keys(drag.weights).reduce(function (sum, key) { return sum + drag.weights[key]; }, 0);
                var next = clamp(drag.startWeight + ((event.clientX - drag.startX) / Math.max(drag.barWidth, 1)) * total, 0.45, 4);
                drag.weights[drag.tab.dataset.a11yTab] = next;
                drag.tab.style.flex = next + " 1 0px";
                drag.moved = true;
                return;
            }

            var dx = event.clientX - drag.startX;
            var dy = event.clientY - drag.startY;
            if (!drag.moved && Math.hypot(dx, dy) < 6)
                return;
            if (!drag.moved && drag.mode === "move") {
                try { drag.tile.setPointerCapture(event.pointerId); } catch (e) { /* the window listener still follows the pointer */ }
            }
            drag.moved = true;
            root.dataset.a11yDragging = "1";
            var width = drag.board.clientWidth;
            var height = drag.board.clientHeight;
            if (drag.mode === "resize") {
                var w = clamp(drag.origW + dx, 72, width);
                var h = clamp(drag.origH + dy, 56, height);
                var x = clamp(drag.origX, 0, width - w);
                var y = clamp(drag.origY, 0, height - h);
                drag.tile.style.left = x + "px";
                drag.tile.style.top = y + "px";
                drag.tile.style.width = w + "px";
                drag.tile.style.height = h + "px";
                return;
            }
            var left = clamp(drag.origX + dx, 0, Math.max(0, width - drag.origW));
            var top = clamp(drag.origY + dy, 0, Math.max(0, height - drag.origH));
            drag.tile.style.left = left + "px";
            drag.tile.style.top = top + "px";
            drag.tile.style.width = drag.origW + "px";
            drag.tile.style.height = drag.origH + "px";
        });

        var finish = function (event) {
            if (!drag || event.pointerId !== drag.pointerId)
                return;
            var current = drag;
            drag = null;
            delete root.dataset.a11yDragging;
            if (current.kind === "tab") {
                if (current.moved)
                    current.tab.dataset.suppressClick = "1";
                var data = readLayout();
                data.tabs[current.tab.dataset.a11yTab] = current.weights[current.tab.dataset.a11yTab];
                writeLayout(data);
                return;
            }
            if (!current.moved)
                return;
            current.tile.dataset.suppressClick = "1";
            var board = current.board;
            var width = board.clientWidth;
            var height = board.clientHeight;
            if (width < 1 || height < 1)
                return;
            var rect = current.tile.getBoundingClientRect();
            var boardRect = board.getBoundingClientRect();
            var stored = readLayout();
            var boardId = board.dataset.a11yBoard || "general";
            var boxes = stored.boards[boardId];
            if (!boxes || typeof boxes !== "object")
                boxes = {};
            boxes[current.tile.dataset.a11yTile] = {
                x: (rect.left - boardRect.left) / width,
                y: (rect.top - boardRect.top) / height,
                w: rect.width / width,
                h: rect.height / height
            };
            stored.boards[boardId] = boxes;
            writeLayout(stored);
        };
        root.addEventListener("pointerup", finish);
        root.addEventListener("pointercancel", finish);

        root.addEventListener("click", function (event) {
            var host = event.target instanceof Element
                ? event.target.closest("[data-a11y-tile], [data-a11y-tab]")
                : null;
            if (!host || host.dataset.suppressClick !== "1")
                return;
            event.preventDefault();
            event.stopPropagation();
            delete host.dataset.suppressClick;
        }, true);
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
        helpCallPhrase: helpCallPhrase,
        watchLayout: watchLayout
    };
    apply(doctorView() ? null : load());
})();
