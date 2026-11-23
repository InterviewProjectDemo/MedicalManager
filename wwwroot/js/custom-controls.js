(function () {
    var KEY = "mm-custom-controls";

    function storageKey(scope) {
        return KEY + ":" + (scope || "patient");
    }

    function empty() {
        return { housed: [], draft: [], editing: false, boxes: {} };
    }

    function read(scope) {
        try {
            var raw = localStorage.getItem(storageKey(scope));
            var data = raw ? JSON.parse(raw) : null;
            if (!data || typeof data !== "object")
                return empty();
            data.housed = Array.isArray(data.housed) ? data.housed.filter(Boolean) : [];
            data.draft = Array.isArray(data.draft) ? data.draft.filter(Boolean) : [];
            data.editing = !!data.editing;
            data.boxes = data.boxes && typeof data.boxes === "object" ? data.boxes : {};
            return data;
        } catch (e) {
            return empty();
        }
    }

    function write(scope, data) {
        try { localStorage.setItem(storageKey(scope), JSON.stringify(data)); } catch (e) { /* this browser may block storage */ }
    }

    function save(scope, partial) {
        var data = read(scope);
        if (!partial)
            return data;
        if (Array.isArray(partial.housed))
            data.housed = partial.housed;
        if (Array.isArray(partial.draft))
            data.draft = partial.draft;
        if (Object.prototype.hasOwnProperty.call(partial, "editing"))
            data.editing = !!partial.editing;
        write(scope, data);
        return data;
    }

    function retainBoxes(scope, ids) {
        var data = read(scope);
        var keep = {};
        (ids || []).forEach(function (id) {
            if (data.boxes[id])
                keep[id] = data.boxes[id];
        });
        data.boxes = keep;
        write(scope, data);
    }

    function tilesOf(board) {
        return Array.prototype.slice.call(board.querySelectorAll("[data-controls-tile]"));
    }

    function currentBox(tile, board, saved) {
        var stored = saved[tile.dataset.controlsTile];
        if (stored)
            return stored;
        var boardRect = board.getBoundingClientRect();
        var rect = tile.getBoundingClientRect();
        var width = Math.max(board.clientWidth, 1);
        var height = Math.max(board.clientHeight, 1);
        return {
            x: (rect.left - boardRect.left) / width,
            y: (rect.top - boardRect.top) / height,
            w: rect.width / width,
            h: rect.height / height
        };
    }

    function logoName(tile) {
        var name = tile.querySelector(".controls-logo-name");
        return ((name ? name.textContent : tile.textContent) || "").trim();
    }

    function defaultSize(board) {
        if (!board)
            return;
        var tiles = tilesOf(board);
        var width = board.clientWidth;
        var height = board.clientHeight;
        if (tiles.length === 0 || width < 80 || height < 80)
            return;
        var scope = board.dataset.controlsScope || "";
        var data = read(scope);
        var gap = 10;
        var tw = (width - gap * (tiles.length + 1)) / tiles.length;
        var th = height - gap * 2;
        tiles.forEach(function (tile) {
            var current = currentBox(tile, board, data.boxes);
            var w = tw / width;
            var h = th / height;
            data.boxes[tile.dataset.controlsTile] = {
                x: clamp(current.x, 0, Math.max(0, 1 - w)),
                y: clamp(current.y, 0, Math.max(0, 1 - h)),
                w: w,
                h: h
            };
        });
        write(scope, data);
        layout(board);
    }

    function defaultPosition(board) {
        if (!board)
            return;
        var tiles = tilesOf(board);
        var width = board.clientWidth;
        var height = board.clientHeight;
        if (tiles.length === 0 || width < 80 || height < 80)
            return;
        tiles.sort(function (a, b) {
            return logoName(a).localeCompare(logoName(b), undefined, { sensitivity: "base" });
        });
        var scope = board.dataset.controlsScope || "";
        var data = read(scope);
        var gap = 10;
        var sizes = tiles.map(function (tile) {
            var box = currentBox(tile, board, data.boxes);
            return {
                id: tile.dataset.controlsTile,
                w: Math.max(96, box.w * width),
                h: Math.min(Math.max(96, box.h * height), height - gap * 2)
            };
        });
        var total = sizes.reduce(function (sum, item) { return sum + item.w; }, 0);
        var room = width - gap * (sizes.length + 1);
        var scale = total > room && total > 0 ? room / total : 1;
        var x = gap;
        sizes.forEach(function (item) {
            var w = item.w * scale;
            data.boxes[item.id] = {
                x: x / width,
                y: gap / height,
                w: w / width,
                h: item.h / height
            };
            x += w + gap;
        });
        write(scope, data);
        layout(board);
    }

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function watch(board, scope) {
        if (!board)
            return;
        board.dataset.controlsScope = scope || "";
        if (board.dataset.controlsWatch === "1") {
            layout(board);
            return;
        }
        board.dataset.controlsWatch = "1";
        bind(board);
        var frame = 0;
        var applySoon = function () {
            if (board.dataset.controlsDragging === "1")
                return;
            cancelAnimationFrame(frame);
            frame = requestAnimationFrame(function () { layout(board); });
        };
        var observer = new MutationObserver(applySoon);
        observer.observe(board, { childList: true, subtree: true });
        if (window.ResizeObserver) {
            var resize = new ResizeObserver(applySoon);
            resize.observe(board);
        }
        applySoon();
    }

    function layout(board) {
        if (!board || !board.isConnected || board.dataset.controlsDragging === "1")
            return;
        var tiles = Array.prototype.slice.call(board.querySelectorAll("[data-controls-tile]"));
        if (tiles.length === 0)
            return;
        var width = board.clientWidth;
        var height = board.clientHeight;
        if (width < 80 || height < 80)
            return;
        var scope = board.dataset.controlsScope || "";
        var data = read(scope);
        var saved = data.boxes;
        if (!saved || typeof saved !== "object")
            saved = {};
        var changed = placeMissing(board, tiles, saved, width, height);
        tiles.forEach(function (tile) {
            applyBox(tile, saved[tile.dataset.controlsTile], width, height);
        });
        if (changed) {
            data.boxes = saved;
            write(scope, data);
        }
    }

    function placeMissing(board, tiles, saved, width, height) {
        var missing = tiles.filter(function (tile) { return !saved[tile.dataset.controlsTile]; });
        if (missing.length === 0)
            return false;
        var gap = 10;
        var cols = Math.max(1, Math.min(tiles.length, 5));
        var rows = Math.ceil(tiles.length / cols);
        var tw = (width - gap * (cols + 1)) / cols;
        var th = Math.min((height - gap * (rows + 1)) / rows, Math.max(148, tw * 1.2));
        if (missing.length === tiles.length) {
            tiles.forEach(function (tile, index) {
                var column = index % cols;
                var row = Math.floor(index / cols);
                saved[tile.dataset.controlsTile] = {
                    x: (gap + column * (tw + gap)) / width,
                    y: (gap + row * (th + gap)) / height,
                    w: tw / width,
                    h: th / height
                };
            });
            return true;
        }

        var cellWidth = tw;
        var cellHeight = th;
        var start = tiles.length - missing.length;
        missing.forEach(function (tile, index) {
            var slot = start + index;
            var column = slot % cols;
            var row = Math.floor(slot / cols);
            saved[tile.dataset.controlsTile] = {
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
        var w = clamp(box.w * width, 96, width);
        var h = clamp(box.h * height, 96, height);
        var x = clamp(box.x * width, 0, Math.max(0, width - w));
        var y = clamp(box.y * height, 0, Math.max(0, height - h));
        tile.style.left = x + "px";
        tile.style.top = y + "px";
        tile.style.width = w + "px";
        tile.style.height = h + "px";
    }

    function bind(board) {
        var drag = null;

        board.addEventListener("dragstart", function (event) {
            event.preventDefault();
        }, true);

        board.addEventListener("pointerdown", function (event) {
            if (event.button !== undefined && event.button !== 0)
                return;
            var target = event.target;
            if (!(target instanceof Element))
                return;
            var tile = target.closest("[data-controls-tile]");
            if (!tile || !board.contains(tile))
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
                mode: resizing ? "resize" : "move",
                pointerId: event.pointerId,
                tile: tile,
                startX: event.clientX,
                startY: event.clientY,
                origX: rect.left - boardRect.left,
                origY: rect.top - boardRect.top,
                origW: rect.width,
                origH: rect.height,
                moved: false
            };
            if (resizing) {
                try { resizeHandle.setPointerCapture(event.pointerId); } catch (e) { /* the window listener still follows the pointer */ }
            }
        });

        board.addEventListener("pointermove", function (event) {
            if (!drag || event.pointerId !== drag.pointerId)
                return;
            var dx = event.clientX - drag.startX;
            var dy = event.clientY - drag.startY;
            var slop = event.pointerType === "mouse" ? 6 : 28;
            if (!drag.moved && Math.hypot(dx, dy) < slop)
                return;
            if (!drag.moved && drag.mode === "move") {
                try { drag.tile.setPointerCapture(event.pointerId); } catch (e) { /* the window listener still follows the pointer */ }
            }
            drag.moved = true;
            board.dataset.controlsDragging = "1";
            var width = board.clientWidth;
            var height = board.clientHeight;
            if (drag.mode === "resize") {
                var w = clamp(drag.origW + dx, 96, width);
                var h = clamp(drag.origH + dy, 96, height);
                drag.tile.style.left = clamp(drag.origX, 0, Math.max(0, width - w)) + "px";
                drag.tile.style.top = clamp(drag.origY, 0, Math.max(0, height - h)) + "px";
                drag.tile.style.width = w + "px";
                drag.tile.style.height = h + "px";
                return;
            }
            drag.tile.style.left = clamp(drag.origX + dx, 0, Math.max(0, width - drag.origW)) + "px";
            drag.tile.style.top = clamp(drag.origY + dy, 0, Math.max(0, height - drag.origH)) + "px";
            drag.tile.style.width = drag.origW + "px";
            drag.tile.style.height = drag.origH + "px";
        });

        var finish = function (event) {
            if (!drag || event.pointerId !== drag.pointerId)
                return;
            var current = drag;
            drag = null;
            delete board.dataset.controlsDragging;
            if (!current.moved)
                return;
            current.tile.dataset.suppressClick = "1";
            var width = board.clientWidth;
            var height = board.clientHeight;
            if (width < 1 || height < 1)
                return;
            var rect = current.tile.getBoundingClientRect();
            var boardRect = board.getBoundingClientRect();
            var scope = board.dataset.controlsScope || "";
            var data = read(scope);
            data.boxes[current.tile.dataset.controlsTile] = {
                x: (rect.left - boardRect.left) / width,
                y: (rect.top - boardRect.top) / height,
                w: rect.width / width,
                h: rect.height / height
            };
            write(scope, data);
        };
        board.addEventListener("pointerup", finish);
        board.addEventListener("pointercancel", finish);

        board.addEventListener("click", function (event) {
            var host = event.target instanceof Element ? event.target.closest("[data-controls-tile]") : null;
            if (!host || host.dataset.suppressClick !== "1")
                return;
            event.preventDefault();
            event.stopPropagation();
            delete host.dataset.suppressClick;
        }, true);
    }

    window.mmCustomControls = {
        load: read,
        save: save,
        retainBoxes: retainBoxes,
        watch: watch,
        defaultSize: defaultSize,
        defaultPosition: defaultPosition
    };
})();
