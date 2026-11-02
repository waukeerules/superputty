/*
 * Built-in terminal page (see index.html and docs/MonacoTerminal.md, phase 2).
 *
 * Gui/TerminalControl runs plink in a pseudo console and sends its output here as base64 chunks, one at a time (each
 * answered with "ack"). xterm.js draws it; typing goes back as "input". It is meant to feel like PuTTY: select to copy,
 * Shift+Insert and middle-click paste, a blinking cursor only while focused, and a right-click menu (Copy, Paste,
 * Find..., Select All, Clear Scrollback, Clear Screen...). Lines are coloured live by the Highlighted Output rules,
 * using xterm.js decorations, so the bytes themselves never change.
 *
 * The session log is written from what this page shows (the pseudo console rewrites the stream, see the design doc):
 * a line is sent to TerminalControl ("log") once the cursor has left it, and the cursor's line is sent as "current",
 * so SPSL's WAITFOR can match a prompt before Enter is pressed.
 *
 * Messages from TerminalControl: config, data, notice, paste, focus, action, cursorColor. To it: ready, size, input,
 * ack, log, copy, pasteRequest (every paste, so TerminalControl can ask first; with "text" for one not from the
 * clipboard), document, bell, tab, openInPutty, openLink (a link Ctrl+clicked), pref, cleared (the screen was cleared
 * here, so the pseudo console's own copy of it is cleared too and a resize can't bring the text back).
 *
 * xterm.js add-ons: web links (Ctrl+click opens) and WebGL drawing (Options > Terminal > Drawing; the DOM renderer
 * takes over without it).
 */
(function () {
    'use strict';

    /** Width of xterm.js's scroll bar, left out when fitting the columns (as its fit addon does) */
    var SCROLLBAR_WIDTH = 14;
    /** Longest part of a line the rules look at */
    var MAX_MATCH_LENGTH = 2000;
    /** Most find matches marked at once */
    var MAX_FIND_MATCHES = 2000;
    /** Wait after output before highlighting and logging, so a burst is handled once */
    var SETTLE_MS = 30;

    var host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
    var term;
    var ruleSets = [];
    var themes = [];
    var theme = null;
    var autoSet = null;
    var darkDefault = true;
    var bellFromProfile = 'visual'; // the PuTTY profile's bell, from TerminalControl
    var cursorFromProfile = { style: 'block', blink: false }; // the PuTTY profile's cursor, from TerminalControl
    var localShell = false;         // cmd, PowerShell or WSL rather than a connection through plink
    var serialSession = false;      // a serial port: Open in PuTTY replaces this tab, as the port can only be open once
    var menuKeys = [];              // Send Key's keys ({key, label, hint}), from TerminalKeys.MenuKeys
    var keysFromProfile = { backspace: 'del', homeEnd: 'standard', functionKeys: 'tilde' }; // the PuTTY profile's keys, from TerminalControl
    var sessionRules = '';          // the session's or its folder's highlighting rules; empty: Options > Terminal's
    var tabRules = null;            // rules chosen in this tab's menu, for this tab only; null: the session's or default
    var unlimitedScrollback = 1000000; // scrollback when the preference is 0, from TerminalControl
    var webgl = null;               // xterm.js's WebGL renderer while it's drawing the terminal
    var cursorColor = null;         // the session's environment colour (#rrggbb) for the cursor, or null
    var settleTimer = null;
    var scanFrom = null;        // marker: first line to look at for highlighting next time
    var logged = null;          // marker: first line not yet sent to the log
    var lastCurrent = null;     // the cursor's line as last sent to the log
    var entries = [];           // highlighted lines: {marker, decorations, text}
    var find = { matches: [], index: -1, decorations: [], current: null };
    var menuSelection = null;   // the selection when the right button went down: {text, position}
    var selectionMatches = [];  // marks ({marker, decoration}) on the other places the selected text appears
    var flushMarker = null;     // the marker flushDecorations adds its throwaway decoration on
    var sized = false;

    /** Send a message to TerminalControl (ignored when the page is opened in a plain browser to test it) */
    function post(message) {
        if (host) {
            host.postMessage(message);
        } else if (window.terminalDev) {
            window.terminalDev.posted.push(message);
        }
    }

    /**
     * The terminal preferences (Utils/TerminalPreferences.cs): rule set, theme, font, cursor, bell, copy on select,
     * marking matches. SuperPuTTY keeps them, so Tools > Options > Terminal and every tab share them; they arrive in
     * "config" and again in "prefs" whenever they change.
     */
    var prefs = {};

    /** One preference, or null when it isn't set (empty counts as not set) */
    function loadPref(name) {
        var value = prefs[name];
        return value === undefined || value === null || value === '' ? null : String(value);
    }

    /** Change a preference here and have SuperPuTTY save it for every terminal tab */
    function savePref(name, value) {
        prefs[name] = String(value);
        post({ type: 'pref', name: name, value: String(value) });
    }

    // ---- size -------------------------------------------------------------------------------------------------

    /**
     * Fit the columns and rows to the tab, as xterm.js's fit addon does (it reads the same cell size), and tell
     * TerminalControl the first time and on every change
     */
    function fit() {
        var dimensions = term._core && term._core._renderService && term._core._renderService.dimensions;
        if (!dimensions || !dimensions.css.cell.width || !dimensions.css.cell.height) {
            return;
        }
        var element = document.getElementById('terminal');
        // a hidden tab has no size yet; wait for the ResizeObserver rather than start the session at 2x1
        if (element.clientWidth < SCROLLBAR_WIDTH + dimensions.css.cell.width * 2 || element.clientHeight < dimensions.css.cell.height) {
            return;
        }
        var cols = Math.max(2, Math.floor((element.clientWidth - SCROLLBAR_WIDTH) / dimensions.css.cell.width));
        var rows = Math.max(1, Math.floor(element.clientHeight / dimensions.css.cell.height));
        if (cols !== term.cols || rows !== term.rows) {
            term.resize(cols, rows);
        }
        if (!sized) {
            sized = true;
            post({ type: 'size', cols: term.cols, rows: term.rows });
        }
    }

    // ---- output, logging and highlighting -----------------------------------------------------------------------

    /** Base64 from TerminalControl to bytes; xterm.js decodes UTF-8 across chunks itself */
    function decode(base64) {
        var binary = window.atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) { bytes[i] = binary.charCodeAt(i); }
        return bytes;
    }

    /** Write output and ask for the next chunk once it's drawn */
    function write(bytes) {
        term.write(bytes, function () {
            post({ type: 'ack' });
            scheduleSettle();
        });
    }

    /** Highlight and log shortly after output stops arriving */
    function scheduleSettle() {
        if (settleTimer === null) {
            settleTimer = window.setTimeout(function () {
                settleTimer = null;
                highlight(false);
                sendLog();
            }, SETTLE_MS);
        }
    }

    /** A marker on an absolute buffer line (registerMarker counts from the cursor's line) */
    function markLine(line) {
        var buffer = term.buffer.active;
        return term.registerMarker(line - (buffer.baseY + buffer.cursorY));
    }

    /** The buffer line where the logical line holding a buffer line starts (wrapped lines continue the one above) */
    function logicalStart(line) {
        var buffer = term.buffer.active;
        while (line > 0 && buffer.getLine(line) && buffer.getLine(line).isWrapped) {
            line--;
        }
        return line;
    }

    /** Text of buffer lines [from, to), wrapped lines joined back into one */
    function readLines(from, to) {
        var buffer = term.buffer.active;
        var lines = [];
        for (var i = from; i < to; i++) {
            var line = buffer.getLine(i);
            var text = line ? line.translateToString(true) : '';
            if (line && line.isWrapped && lines.length) {
                lines[lines.length - 1] += text;
            } else {
                lines.push(text);
            }
        }
        return lines;
    }

    /**
     * Send the session log what changed: lines the cursor has left, and the cursor's own line. A line is sent once,
     * when the cursor first moves below it, so "--More--" (erased on the same line) never reaches the log, and a
     * screen repainted after a resize isn't logged twice.
     */
    function sendLog() {
        var buffer = term.buffer.active;
        var cursorStart = logicalStart(buffer.baseY + buffer.cursorY);
        var from = logged && !logged.isDisposed ? logged.line : 0;
        var lines = cursorStart > from ? readLines(from, cursorStart) : [];
        if (cursorStart > from || !logged || logged.isDisposed) {
            if (logged) { logged.dispose(); }
            logged = markLine(Math.max(cursorStart, from));
        }
        var current = readLines(cursorStart, buffer.baseY + buffer.cursorY + 1).join('');
        if (lines.length || current !== lastCurrent) {
            lastCurrent = current;
            post({ type: 'log', lines: lines, current: current });
        }
        if (!autoSet) {
            lines.concat([current]).some(function (text) {
                autoSet = SpRules.detect(ruleSets, text);
                return autoSet !== null;
            });
            if (autoSet) { highlight(true); }
        }
    }

    /** The rules this tab uses: its menu's choice, else the session's or its folder's, else Options > Terminal's */
    function chosenRules() {
        return tabRules || sessionRules || loadPref('ruleSet') || 'auto';
    }

    /** A rules choice's name for the menu: Auto (with its pick), None, or the rule set's name */
    function rulesName(id) {
        if (id === 'auto') { return 'Auto' + (autoSet ? ': ' + autoSet.name : ''); }
        if (id === 'none') { return 'None'; }
        for (var i = 0; i < ruleSets.length; i++) {
            if (ruleSets[i].id === id) { return ruleSets[i].name; }
        }
        return id;
    }

    /** The rule set in use: the chosen one, Auto's pick, or none */
    function currentSet() {
        var chosen = chosenRules();
        if (chosen === 'auto') { return autoSet; }
        for (var i = 0; i < ruleSets.length; i++) {
            if (ruleSets[i].id === chosen) { return ruleSets[i]; }
        }
        return null;
    }

    /** Remove highlighted lines' decorations and markers (see unmark for the order) */
    function disposeEntries(list) {
        list.forEach(function (entry) { entry.decorations.forEach(function (d) { d.dispose(); }); });
        flushDecorations();
        list.forEach(function (entry) { entry.marker.dispose(); });
    }

    /**
     * Colour lines by the rules: everything when <paramref>all</paramref> (rules or theme changed), otherwise the
     * lines that may have changed since last time (the screen, and lines that scrolled off it since)
     */
    function highlight(all) {
        var buffer = term.buffer.active;
        var set = currentSet();
        if (all) {
            disposeEntries(entries);
            entries = [];
        }
        var from = all || !scanFrom || scanFrom.isDisposed ? 0 : scanFrom.line;
        var to = buffer.length;
        entries = entries.filter(function (e) { return !e.marker.isDisposed; });
        var byLine = {};
        entries.forEach(function (e) { if (e.marker.line >= from) { byLine[e.marker.line] = e; } });

        for (var i = from; i < to; i++) {
            var line = buffer.getLine(i);
            var text = line ? line.translateToString(true) : '';
            var entry = byLine[i];
            if (entry && entry.text === text && entry.set === set) { continue; }
            if (entry) {
                disposeEntries([entry]);
                entries.splice(entries.indexOf(entry), 1);
            }
            var found = set ? SpRules.matches(set, text, MAX_MATCH_LENGTH) : [];
            if (!found.length) { continue; }
            var marker = markLine(i);
            if (!marker) { continue; }
            var decorations = [];
            found.forEach(function (m) {
                var style = styleFor(m.style);
                if (!style || (!style.color && !style.background)) { return; }
                var options = {
                    marker: marker,
                    x: m.start,
                    width: Math.max(1, Math.min(term.cols, m.line ? term.cols : m.end) - m.start),
                    layer: 'top'
                };
                if (style.color) { options.foregroundColor = style.color; }
                if (style.background) { options.backgroundColor = style.background; }
                var decoration = term.registerDecoration(options);
                if (decoration) { decorations.push(decoration); }
            });
            entries.push({ marker: marker, decorations: decorations, text: text, set: set });
        }
        if (scanFrom) { scanFrom.dispose(); }
        // next time, start at the top of the screen: lines above it can no longer change
        scanFrom = markLine(buffer.baseY);
    }

    // ---- clipboard ----------------------------------------------------------------------------------------------

    /** Copy the selection to the clipboard (TerminalControl sets it, the page has no clipboard access) */
    function copy() {
        var text = selectedText();
        if (text) {
            post({ type: 'copy', text: text });
        }
    }

    /** The selection, or while the menu is open, the one there was when the right button went down (rememberSelection) */
    function selectedText() {
        if (term.hasSelection()) { return term.getSelection(); }
        return menuSelection ? menuSelection.text : '';
    }

    /**
     * Remember the selection as the right button goes down. In SuperPuTTY the click also moves the keyboard focus to
     * the tab, and that can clear the selection before the menu opens; the menu then puts it back and copies it.
     */
    function rememberSelection() {
        menuSelection = term.hasSelection()
            ? { text: term.getSelection(), position: term.getSelectionPosition() }
            : null;
    }

    /** Select again what was selected when the right button went down, if it has been cleared since */
    function restoreSelection() {
        if (!menuSelection || term.hasSelection() || !menuSelection.position) { return; }
        var p = menuSelection.position;
        var length = (p.end.y - p.start.y) * term.cols + (p.end.x - p.start.x);
        if (length > 0) { term.select(p.start.x, p.start.y, length); }
    }

    // ---- selection: copy on select, and marking the other matches -------------------------------------------------

    /** Whether an on/off preference is on; both default to on, as in PuTTY and Notepad++ */
    function prefOn(name) {
        return loadPref(name) !== 'false';
    }

    /**
     * Put a background on part of a buffer line (find and selection matches)
     * @returns {{marker, decoration}|null} to pass to unmark
     */
    function mark(row, x, width, colour, textColour) {
        var marker = markLine(row);
        if (!marker) { return null; }
        var options = {
            marker: marker, x: x, width: Math.max(1, Math.min(width, term.cols - x)), backgroundColor: colour, layer: 'top'
        };
        // a bright mark needs dark text, whatever colour the rules gave it
        if (textColour) { options.foregroundColor = textColour; }
        var decoration = term.registerDecoration(options);
        return { marker: marker, decoration: decoration };
    }

    /**
     * Remove marks: every decoration, then flushDecorations, then the markers. xterm.js 6.0.0 keeps decorations in a
     * list sorted by their marker's line, and a disposed marker's line becomes -1. A decoration disposed before its
     * marker is only queued for removal and stays in the list until later; if its marker is disposed meanwhile, the
     * list holds a -1 among the real lines, the next removal's binary search misses its decoration, and that one stays
     * for good under the wrong line: lookups for other lines then fail and their colours disappear.
     */
    function unmark(marks) {
        marks = marks.filter(Boolean);
        marks.forEach(function (m) { if (m.decoration) { m.decoration.dispose(); } });
        flushDecorations();
        marks.forEach(function (m) { m.marker.dispose(); });
    }

    /**
     * Make xterm.js take queued decoration removals out of its list now. There's no API for it, but adding a
     * decoration does it first; the one added here is removed again at once, on a marker that's never disposed by us.
     */
    function flushDecorations() {
        if (!flushMarker || flushMarker.isDisposed) {
            flushMarker = markLine(term.buffer.active.baseY + term.buffer.active.cursorY);
        }
        var decoration = flushMarker && term.registerDecoration({ marker: flushMarker });
        if (decoration) { decoration.dispose(); }
    }

    /** Remove the marks on the selection's other matches */
    function clearSelectionMatches() {
        unmark(selectionMatches);
        selectionMatches = [];
    }

    /**
     * Mark everywhere else the selected text appears, as Notepad++ does: case-insensitive, and as a whole word when
     * a whole word is selected. Only for a selection within one line, of 2 to 200 characters.
     */
    function markSelectionMatches() {
        clearSelectionMatches();
        // the find bar marks its own matches
        if (!prefOn('markMatches') || !term.hasSelection() || !document.getElementById('find').hidden) { return; }
        var text = term.getSelection();
        if (text.indexOf('\n') >= 0 || text.trim().length < 2 || text.length > 200) { return; }
        var escaped = text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        var word = /^\w+$/.test(text);
        var regex = new RegExp(word ? '\\b' + escaped + '\\b' : escaped, 'gi');
        var colour = (theme && theme.colors && theme.colors.selectionMatch)
            || (theme && theme.base === 'vs' ? '#c8e6a0' : '#4a5a28');
        var buffer = term.buffer.active;
        var selected = term.getSelectionPosition();
        for (var row = 0; row < buffer.length && selectionMatches.length < MAX_FIND_MATCHES; row++) {
            var line = buffer.getLine(row).translateToString(true);
            var match;
            regex.lastIndex = 0;
            while ((match = regex.exec(line)) !== null && selectionMatches.length < MAX_FIND_MATCHES) {
                // not the selected text itself: the mark would hide the selection
                if (selected && row === selected.start.y && match.index === selected.start.x) { continue; }
                var m = mark(row, match.index, match[0].length, colour);
                if (m) { selectionMatches.push(m); }
            }
        }
    }

    /** Ask TerminalControl for the clipboard; it answers with "paste" */
    function paste() {
        post({ type: 'pasteRequest' });
    }

    // ---- clearing -----------------------------------------------------------------------------------------------

    /**
     * After clearing, start the log again at the cursor's line: what a clear moves or redraws was the screen, not
     * output, and is already in the log
     */
    function skipLog() {
        var buffer = term.buffer.active;
        if (logged) { logged.dispose(); }
        logged = markLine(logicalStart(buffer.baseY + buffer.cursorY));
    }

    /** Clear Scrollback: drop the lines above the screen, keep the screen */
    function clearScrollback() {
        term.write('\x1b[3J', function () { skipLog(); highlight(true); });
    }

    /**
     * Clear Screen: move what's on screen into the scrollback and leave the cursor's line (the prompt) at the top,
     * the cursor where it was in it
     */
    function clearScreen() {
        var buffer = term.buffer.active;
        var x = buffer.cursorX;
        var line = readLines(logicalStart(buffer.baseY + buffer.cursorY), buffer.baseY + buffer.cursorY + 1).join('');
        term.write('\x1b[2J\x1b[H\x1b[2K' + line + '\x1b[' + (x + 1) + 'G', function () { skipLog(); highlight(true); });
        post({ type: 'cleared' });
    }

    /** Clear Screen and Scrollback: only the cursor's line stays */
    function clearAll() {
        term.clear();
        skipLog();
        highlight(true);
        post({ type: 'cleared' });
    }

    // ---- find -----------------------------------------------------------------------------------------------------

    /** Open the find bar with the selection (if any) as the text */
    function openFind() {
        var bar = document.getElementById('find');
        var input = document.getElementById('findText');
        bar.hidden = false;
        if (term.hasSelection() && term.getSelection().indexOf('\n') < 0) {
            input.value = term.getSelection();
        }
        input.focus();
        input.select();
        runFind();
    }

    /** Close the find bar, remove its marks and go back to typing in the terminal */
    function closeFind() {
        document.getElementById('find').hidden = true;
        clearFindMarks();
        term.clearSelection();
        term.focus();
    }

    /** Remove the marks on every match, and on the current one */
    function clearFindMarks() {
        unmark(find.decorations.filter(Boolean).concat(find.current && find.current.mark ? [find.current.mark] : []));
        find.decorations = [];
        find.current = null;
    }

    /** Find every match in the scrollback and screen, mark them, and select the one nearest the end */
    function runFind() {
        clearFindMarks();
        var input = document.getElementById('findText');
        var text = input.value;
        var caseOn = document.getElementById('findCase').getAttribute('aria-pressed') === 'true';
        var regexOn = document.getElementById('findRegex').getAttribute('aria-pressed') === 'true';
        find.matches = [];
        find.index = -1;
        input.classList.remove('nomatch');
        if (!text) {
            document.getElementById('findCount').textContent = '';
            return;
        }
        var regex;
        try {
            regex = new RegExp(regexOn ? text : text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), caseOn ? 'g' : 'gi');
        } catch (e) {
            input.classList.add('nomatch');
            document.getElementById('findCount').textContent = 'bad regex';
            return;
        }
        var buffer = term.buffer.active;
        for (var row = 0; row < buffer.length && find.matches.length < MAX_FIND_MATCHES; row++) {
            var line = buffer.getLine(row).translateToString(true);
            var match;
            regex.lastIndex = 0;
            while ((match = regex.exec(line)) !== null && find.matches.length < MAX_FIND_MATCHES) {
                if (!match[0].length) { regex.lastIndex++; continue; }
                find.matches.push({ row: row, col: match.index, length: match[0].length });
            }
        }
        if (!find.matches.length) {
            input.classList.add('nomatch');
            document.getElementById('findCount').textContent = 'No results';
            return;
        }
        // one mark per match (find.decorations[i] is match i's; null while it's the current one)
        find.decorations = find.matches.map(function (m) { return mark(m.row, m.col, m.length, FIND_MATCH_COLOUR); });
        showMatch(find.matches.length - 1);
    }

    /** Background of every find match, and of the current one */
    var FIND_MATCH_COLOUR = '#806000', FIND_CURRENT_COLOUR = '#ffb000';

    /** Select a match and scroll it into the middle of the view */
    function showMatch(index) {
        if (!find.matches.length) { return; }
        find.index = (index + find.matches.length) % find.matches.length;
        var m = find.matches[find.index];
        term.select(m.col, m.row, m.length);
        // marks cover the selection, so the current match gets a brighter mark instead of its usual one (with both,
        // xterm.js draws the one registered first on top, which is the usual one)
        if (find.current) {
            unmark([find.current.mark]);
            var previous = find.matches[find.current.index];
            find.decorations[find.current.index] = mark(previous.row, previous.col, previous.length, FIND_MATCH_COLOUR);
        }
        if (find.decorations[find.index]) {
            unmark([find.decorations[find.index]]);
            find.decorations[find.index] = null;
        }
        find.current = { index: find.index, mark: mark(m.row, m.col, m.length, FIND_CURRENT_COLOUR, '#000000') };
        term.scrollToLine(Math.max(0, m.row - Math.floor(term.rows / 2)));
        document.getElementById('findCount').textContent = (find.index + 1) + ' of ' + find.matches.length;
    }

    /** Wire up the find bar's text box and buttons */
    function bindFind() {
        var input = document.getElementById('findText');
        input.addEventListener('input', runFind);
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { showMatch(find.index + (e.shiftKey ? -1 : 1)); e.preventDefault(); }
            else if (e.key === 'Escape') { closeFind(); e.preventDefault(); }
        });
        ['findCase', 'findRegex'].forEach(function (id) {
            document.getElementById(id).addEventListener('click', function () {
                var button = document.getElementById(id);
                button.setAttribute('aria-pressed', button.getAttribute('aria-pressed') === 'true' ? 'false' : 'true');
                runFind();
                input.focus();
            });
        });
        document.getElementById('findPrev').addEventListener('click', function () { showMatch(find.index - 1); });
        document.getElementById('findNext').addEventListener('click', function () { showMatch(find.index + 1); });
        document.getElementById('findClose').addEventListener('click', closeFind);
    }

    // ---- right-click menu -----------------------------------------------------------------------------------------

    /** The menu's items: {label, key, action, enabled, checked, items (a submenu)} or '-' for a separator */
    function menuItems() {
        var selected = selectedText() !== '';
        // a choice here is for this tab only; the session's (Edit Session), folder's or Options' rules are the default
        var defaultRules = sessionRules || loadPref('ruleSet') || 'auto';
        var ruleItems = [{ label: 'Default (' + rulesName(defaultRules) + ')', checked: tabRules === null, action: function () { chooseRules(null); } }, '-']
            .concat([{ label: 'Auto' + (autoSet ? ' (' + autoSet.name + ')' : ''), checked: tabRules === 'auto', action: function () { chooseRules('auto'); } }])
            .concat(ruleSets.map(function (set) {
                return { label: set.name, checked: tabRules === set.id, action: function () { chooseRules(set.id); } };
            }))
            .concat([{ label: 'None', checked: tabRules === 'none', action: function () { chooseRules('none'); } }]);
        var themeItems = themes.map(function (t) {
            return { label: t.name, checked: theme && theme.name === t.name, action: function () { savePref('theme', t.name); applyTheme(); } };
        });
        var chosenFamily = loadPref('fontFamily') || '';
        var setFamily = function (value) { return function () { savePref('fontFamily', value); applyFont(); }; };
        var fontItems = [
            { label: 'Larger', key: 'Ctrl+Wheel', action: function () { stepFontSize(1); } },
            { label: 'Smaller', key: 'Ctrl+Wheel', action: function () { stepFontSize(-1); } },
            { label: 'Default Size (' + chosenFontDefaultSize() + ' px)', checked: !(parseInt(loadPref('fontSize'), 10) > 0), action: function () { savePref('fontSize', '0'); applyFont(); } },
            '-',
            { label: 'From Theme (' + themeFont().family.split(',')[0].replace(/"/g, '') + ')', checked: chosenFamily === '', action: setFamily('') },
            { label: 'From PuTTY Profile (' + profileFont.family + ', ' + profileFont.points + ' pt)', checked: chosenFamily === 'profile', action: setFamily('profile') },
            '-'
        ].concat(installedFonts.map(function (name) {
            return { label: name, checked: chosenFamily === name, action: setFamily(name) };
        }));
        var shapeNames = { block: 'Block', underline: 'Underline', bar: 'Vertical Line' };
        var chosenShape = loadPref('cursorShape') || 'profile';
        var chosenBlink = loadPref('cursorBlink') || 'profile';
        var setCursor = function (name, value) { return function () { savePref(name, value); applyCursor(); }; };
        var cursorItems = [['profile', 'From PuTTY Profile (' + shapeNames[cursorFromProfile.style] + ')'], ['block', 'Block'], ['underline', 'Underline'], ['bar', 'Vertical Line']]
            .map(function (c) { return { label: c[1], checked: chosenShape === c[0], action: setCursor('cursorShape', c[0]) }; })
            .concat(['-'])
            .concat([['profile', 'Blink: From PuTTY Profile (' + (cursorFromProfile.blink ? 'On' : 'Off') + ')'], ['on', 'Blink'], ['off', 'Don\'t Blink']]
                .map(function (c) { return { label: c[1], checked: chosenBlink === c[0], action: setCursor('cursorBlink', c[0]) }; }));
        var chosenBell = loadPref('bell') || 'profile';
        var bellNames = { visual: 'Flash', sound: 'Sound', none: 'None' };
        var bellItems = [['profile', 'From PuTTY Profile (' + bellNames[bellFromProfile] + ')'], ['visual', 'Flash'], ['sound', 'Sound'], ['none', 'None']]
            .map(function (choice) {
                return { label: choice[1], checked: chosenBell === choice[0], action: function () { savePref('bell', choice[0]); } };
            });
        return [
            { label: 'Copy', key: 'Ctrl+Insert', enabled: selected, action: copy },
            { label: 'Paste', key: 'Shift+Insert', action: paste },
            // through TerminalControl, like every paste, so several lines are asked about first
            { label: 'Copy and Paste', enabled: selected, action: function () { var text = selectedText(); copy(); post({ type: 'pasteRequest', text: text }); } },
            '-',
            { label: 'Find...', key: 'Ctrl+Shift+F', action: openFind },
            { label: 'Select All', action: function () { term.selectAll(); } },
            '-',
            { label: 'Clear Scrollback', action: clearScrollback },
            { label: 'Clear Screen', action: clearScreen },
            { label: 'Clear Screen and Scrollback', action: clearAll },
            '-',
            { label: 'Open as Document', action: function () { post({ type: 'document' }); } },
            { label: 'Highlighting', items: ruleItems },
            { label: 'Theme', items: themeItems },
            { label: 'Bell', items: bellItems },
            { label: 'Font', items: fontItems },
            { label: 'Cursor', items: cursorItems },
            '-',
            { label: 'Copy on Select', checked: prefOn('copyOnSelect'), action: function () { togglePref('copyOnSelect'); } },
            { label: 'Mark Matches of Selection', checked: prefOn('markMatches'), action: function () { togglePref('markMatches'); markSelectionMatches(); } },
            '-',
            // typed through the tab (ctlPuttyPanel.SendKey), as SENDKEY types them; Other Key... asks there
            { label: 'Send Key', items: menuKeys.map(function (k) {
                return { label: k.label, key: k.hint, action: function () { post({ type: 'sendKey', key: k.key }); } };
            }).concat([
                // a Break is a signal on the line, not a key: plink can't send one, PuTTY's Special Command can
                { label: 'Break', key: 'needs PuTTY', enabled: false },
                '-',
                { label: 'Other Key...', action: function () { post({ type: 'sendKey' }); } }
            ]) },
            '-'
        ].concat(localShell ? [] : [
            // plink can't send PuTTY's Special Commands (Break, signals...); PuTTY's own window can
            { label: serialSession ? 'Open in PuTTY (Special Commands, replaces this tab)' : 'Open in PuTTY (Special Commands)',
                action: function () { post({ type: 'openInPutty' }); } },
            '-'
        ]).concat([
            { label: 'Close Tab', action: function () { post({ type: 'tab', command: 'close' }); } },
            { label: 'Close Other Tabs', action: function () { post({ type: 'tab', command: 'closeOthers' }); } },
            { label: 'Close Tabs to the Right', action: function () { post({ type: 'tab', command: 'closeRight' }); } },
            { label: 'Close All Tabs', action: function () { post({ type: 'tab', command: 'closeAll' }); } }
        ]);
    }

    /** Build a menu element from items; submenus open on hover. isSubmenu: the rows are a submenu's */
    function buildMenu(element, items, isSubmenu) {
        element.innerHTML = '';
        items.forEach(function (item) {
            if (item === '-') {
                var separator = document.createElement('div');
                separator.className = 'separator';
                element.appendChild(separator);
                return;
            }
            var row = document.createElement('div');
            row.className = 'item' + (item.enabled === false ? ' disabled' : '') + (item.checked ? ' checked' : '');
            var label = document.createElement('span');
            label.textContent = item.label;
            row.appendChild(label);
            var key = document.createElement('span');
            key.className = 'key';
            key.textContent = item.items ? '▸' : (item.key || '');
            row.appendChild(key);
            if (item.items) {
                row.addEventListener('mouseenter', function () { openSubmenu(row, item.items); });
            } else {
                // a row of the main menu closes an open submenu, after a moment: the way to the submenu often
                // crosses the row below. A submenu's own rows leave it open.
                if (!isSubmenu) {
                    row.addEventListener('mouseenter', scheduleCloseSubmenu);
                }
                row.addEventListener('mousedown', function (e) { e.preventDefault(); });
                row.addEventListener('click', function () {
                    if (item.enabled === false) { return; }
                    // before hiding: hiding forgets the selection remembered for Copy
                    item.action();
                    hideMenu();
                    if (document.getElementById('find').hidden) { term.focus(); }
                });
            }
            element.appendChild(row);
        });
    }

    /** Show a submenu beside its row, kept on screen */
    function openSubmenu(row, items) {
        closeSubmenu();
        row.classList.add('open');
        var sub = document.createElement('div');
        sub.className = 'menu submenu';
        sub.id = 'submenu';
        buildMenu(sub, items, true);
        sub.addEventListener('mouseenter', cancelCloseSubmenu);
        document.body.appendChild(sub);
        var r = row.getBoundingClientRect();
        // overlapping the main menu a little leaves no gap to cross
        var left = r.right - 4;
        if (left + sub.offsetWidth > window.innerWidth) { left = r.left - sub.offsetWidth + 4; }
        sub.style.left = Math.max(0, left) + 'px';
        sub.style.top = Math.max(0, Math.min(r.top - 5, window.innerHeight - sub.offsetHeight)) + 'px';
    }

    /** Pending close of the submenu, from scheduleCloseSubmenu */
    var submenuCloseTimer = null;

    /** Close the submenu shortly, unless the mouse reaches it first */
    function scheduleCloseSubmenu() {
        cancelCloseSubmenu();
        submenuCloseTimer = window.setTimeout(closeSubmenu, 250);
    }

    /** The mouse reached the submenu (or another submenu opened): keep it */
    function cancelCloseSubmenu() {
        window.clearTimeout(submenuCloseTimer);
        submenuCloseTimer = null;
    }

    /** Close the open submenu, if any */
    function closeSubmenu() {
        cancelCloseSubmenu();
        var sub = document.getElementById('submenu');
        if (sub) { sub.remove(); }
        Array.prototype.forEach.call(document.querySelectorAll('.menu .item.open'), function (r) { r.classList.remove('open'); });
    }

    /** Show the right-click menu at the mouse, kept on screen */
    function showMenu(x, y) {
        var menu = document.getElementById('menu');
        buildMenu(menu, menuItems());
        menu.hidden = false;
        menu.style.left = Math.max(0, Math.min(x, window.innerWidth - menu.offsetWidth - 2)) + 'px';
        menu.style.top = Math.max(0, Math.min(y, window.innerHeight - menu.offsetHeight - 2)) + 'px';
    }

    /** Hide the right-click menu and its submenu, and forget the selection remembered for it */
    function hideMenu() {
        menuSelection = null;
        closeSubmenu();
        document.getElementById('menu').hidden = true;
    }

    /** Turn an on/off preference over, for every terminal tab */
    function togglePref(name) {
        savePref(name, prefOn(name) ? 'false' : 'true');
    }

    /** Choose this tab's highlighting rules; null goes back to the session's or default */
    function chooseRules(id) {
        tabRules = id;
        highlight(true);
    }

    // ---- theme ----------------------------------------------------------------------------------------------------

    /** Apply the chosen theme (PuTTY's colours by default): terminal colours, font and the highlighting colours */
    function applyTheme() {
        var wanted = loadPref('theme');
        theme = themes.filter(function (t) { return t.name === wanted; })[0]
            || themes.filter(function (t) { return t.name === 'PuTTY'; })[0]
            || themes.filter(function (t) { return (t.base === 'vs-dark') === darkDefault; })[0]
            || themes[0] || null;
        if (!theme) { return; }
        var colors = theme.colors || {};
        var names = ['black', 'red', 'green', 'yellow', 'blue', 'magenta', 'cyan', 'white',
            'brightBlack', 'brightRed', 'brightGreen', 'brightYellow', 'brightBlue', 'brightMagenta', 'brightCyan', 'brightWhite'];
        var xtermTheme = {
            background: colors.background,
            foreground: colors.foreground,
            // the session's environment colour (red for production) when it has one, as on its tab
            cursor: cursorColor || colors.cursor || colors.foreground,
            cursorAccent: colors.background,
            selectionBackground: colors.selection
        };
        (colors.ansi || []).slice(0, 16).forEach(function (c, i) { xtermTheme[names[i]] = c; });
        term.options.theme = xtermTheme;
        applyFont();
        document.documentElement.style.setProperty('--bg', colors.background || '#000000');
        window.setTimeout(fit, 0);
        highlight(true);
    }

    // ---- messages and start ---------------------------------------------------------------------------------------

    /** A highlighting style from the theme */
    function styleFor(name) {
        return theme && theme.styles ? theme.styles[name] : null;
    }

    // ---- bell -----------------------------------------------------------------------------------------------------

    /** Bell times within the over-use window, and when the bell went quiet for over-use */
    var bellTimes = [], bellMutedUntil = 0;

    /** The PuTTY profile's over-use settings (Terminal > Bell), from TerminalControl; PuTTY's defaults until then */
    var bellOverload = { on: true, count: 5, windowMs: 2000, quietMs: 5000 };

    /** What the bell does: the menu's choice, else the PuTTY profile's ("none", "visual" or "sound") */
    function bellStyle() {
        var chosen = loadPref('bell') || 'profile';
        return chosen === 'profile' ? bellFromProfile : chosen;
    }

    /**
     * The device rang the bell (e.g. Backspace at the start of a line): flash the terminal or play the system's
     * alert sound, as PuTTY does. As in PuTTY's profile (5 bells in 2 seconds by default), over-use silences it
     * until that many quiet seconds; the bell that reaches the count is the first one silenced, as in PuTTY.
     */
    function ring() {
        var now = Date.now();
        if (bellOverload.on) {
            if (now < bellMutedUntil) {
                bellMutedUntil = now + bellOverload.quietMs;
                return;
            }
            bellTimes = bellTimes.filter(function (t) { return now - t < bellOverload.windowMs; });
            bellTimes.push(now);
            if (bellTimes.length >= bellOverload.count) {
                bellMutedUntil = now + bellOverload.quietMs;
                bellTimes = [];
                return;
            }
        }
        var style = bellStyle();
        if (style === 'sound') {
            post({ type: 'bell' });
        } else if (style === 'visual') {
            var element = document.getElementById('terminal');
            element.classList.remove('bell');
            // restart the animation even when bells come quickly
            void element.offsetWidth;
            element.classList.add('bell');
        }
    }

    // ---- font -----------------------------------------------------------------------------------------------------

    /** The PuTTY profile's font (Window > Appearance), from TerminalControl */
    var profileFont = { family: 'Courier New', points: 10 };

    /** The installed monospaced fonts, from TerminalControl */
    var installedFonts = [];

    /** Smallest and largest font size, in pixels */
    var MIN_FONT = 6, MAX_FONT = 48;

    /** The theme's font, used when the preference doesn't name one */
    function themeFont() {
        var font = theme && theme.font ? theme.font : {};
        return { family: font.family || 'Consolas, "Courier New", monospace', size: font.size || 14 };
    }

    /**
     * The font to draw with: the preference's family (the theme's, the PuTTY profile's, or a named font) and size
     * (the preference's, else the profile's points as pixels at 96 dpi, else the theme's)
     */
    function chosenFont() {
        var family = loadPref('fontFamily');
        var size = parseInt(loadPref('fontSize'), 10) || 0;
        var font;
        if (family === 'profile') {
            font = { family: '"' + profileFont.family + '", monospace', size: Math.round(profileFont.points * 96 / 72) };
        } else if (family) {
            font = { family: '"' + family + '", monospace', size: themeFont().size };
        } else {
            font = themeFont();
        }
        if (size > 0) { font.size = size; }
        return font;
    }

    /** Draw with the chosen font, and refit the columns and rows to it */
    function applyFont() {
        var font = chosenFont();
        term.options.fontFamily = font.family;
        term.options.fontSize = Math.max(MIN_FONT, Math.min(MAX_FONT, font.size));
        window.setTimeout(fit, 0);
    }

    /** The size the font has when no size is chosen: the profile's or the theme's */
    function chosenFontDefaultSize() {
        var saved = prefs.fontSize;
        prefs.fontSize = '0';
        var size = chosenFont().size;
        prefs.fontSize = saved;
        return size;
    }

    /** Make the font a step larger or smaller (the menu, Ctrl+mouse wheel), for every terminal tab */
    function stepFontSize(step) {
        var size = Math.max(MIN_FONT, Math.min(MAX_FONT, term.options.fontSize + step));
        if (size !== term.options.fontSize) {
            savePref('fontSize', size);
            applyFont();
        }
    }

    // ---- cursor ---------------------------------------------------------------------------------------------------

    /** The cursor's shape: the menu's choice, else the PuTTY profile's ("block", "underline" or "bar") */
    function cursorShape() {
        var chosen = loadPref('cursorShape') || 'profile';
        return chosen === 'profile' ? cursorFromProfile.style : chosen;
    }

    /** Whether the cursor blinks: the menu's choice, else the PuTTY profile's */
    function cursorBlinks() {
        var chosen = loadPref('cursorBlink') || 'profile';
        return chosen === 'profile' ? !!cursorFromProfile.blink : chosen === 'on';
    }

    /**
     * Shape and blink the cursor as the PuTTY profile (Window > Appearance) or the menu says. Without the keyboard it
     * shows as an outline for a block, as PuTTY does, and as the same line otherwise.
     */
    function applyCursor() {
        var shape = cursorShape();
        term.options.cursorStyle = shape;
        term.options.cursorInactiveStyle = shape === 'block' ? 'outline' : shape;
        term.options.cursorBlink = cursorBlinks();
    }

    /**
     * The preferences changed (this tab's menu, another tab's, or Tools > Options > Terminal): apply them all. The
     * theme also applies the font and redoes the highlighting.
     */
    function applyPrefs(changed) {
        prefs = changed || {};
        applyScrollback();
        applyRenderer();
        applyTheme();
        applyCursor();
        markSelectionMatches();
    }

    /** Keep the preference's number of lines (Options > Terminal); 0 or less: as many as unlimitedScrollback */
    function applyScrollback() {
        var lines = parseInt(loadPref('scrollback'), 10);
        var wanted = lines > 0 ? lines : unlimitedScrollback;
        if (term.options.scrollback !== wanted) { term.options.scrollback = wanted; }
    }

    /**
     * Draw with xterm.js's WebGL renderer (the default; much faster with a lot of output) or its DOM renderer
     * (renderer "dom" in Options > Terminal). Without WebGL, or when the graphics driver drops its context, the DOM
     * renderer takes over.
     */
    function applyRenderer() {
        var wanted = loadPref('renderer') !== 'dom';
        if (wanted && !webgl && window.WebglAddon) {
            try {
                var addon = new window.WebglAddon.WebglAddon();
                addon.onContextLoss(dropWebgl);
                term.loadAddon(addon);
                webgl = addon;
            } catch (e) {
                webgl = null;
            }
            fit();
        } else if (!wanted && webgl) {
            dropWebgl();
        }
    }

    /** Stop using the WebGL renderer; xterm.js goes back to the DOM one */
    function dropWebgl() {
        if (!webgl) { return; }
        var addon = webgl;
        webgl = null;
        try { addon.dispose(); } catch (e) { /* already gone with its context */ }
        fit();
    }

    /** Apply a config message: rule sets, themes and whether SuperPuTTY is dark; then fit and report the size */
    function configure(config) {
        darkDefault = !!config.dark;
        prefs = config.prefs || {};
        if (config.profileFont) { profileFont = config.profileFont; }
        installedFonts = config.fonts || [];
        bellFromProfile = config.bell || 'visual';
        if (config.bellOverload) { bellOverload = config.bellOverload; }
        if (config.cursor) { cursorFromProfile = config.cursor; }
        localShell = !!config.local;
        serialSession = !!config.serial;
        menuKeys = config.menuKeys || [];
        if (config.keys) { keysFromProfile = config.keys; }
        sessionRules = config.sessionRules || '';
        if (config.unlimitedScrollback) { unlimitedScrollback = config.unlimitedScrollback; }
        applyCursor();
        cursorColor = config.cursorColor || null;
        themes = config.themes || [];
        var compiled = SpRules.compile(config.ruleSets || []);
        ruleSets = compiled.sets;
        autoSet = null;
        applyScrollback();
        applyRenderer();
        applyTheme();
        fit();
        if (compiled.problems.length) {
            notice('Some highlighting rules were skipped: ' + compiled.problems.join('; '), 'info');
        }
    }

    /** Show a message from SuperPuTTY in the terminal, on its own line, in red (errors) or cyan */
    function notice(text, kind) {
        var colour = kind === 'error' ? '1;31' : '36';
        var buffer = term.buffer.active;
        var lead = buffer.cursorX > 0 ? '\r\n' : '';
        term.write(lead + '\x1b[' + colour + 'm' + text + '\x1b[0m\r\n');
    }

    /** Handle a message from TerminalControl */
    function onMessage(message) {
        if (!message || !message.type) { return; }
        switch (message.type) {
            case 'config': configure(message); break;
            case 'data': write(decode(message.data)); break;
            case 'notice': notice(message.text, message.kind); break;
            case 'paste': if (message.text) { term.paste(message.text); } break;
            case 'focus': term.focus(); break;
            case 'action': runAction(message.name); break;
            case 'cursorColor': cursorColor = message.color || null; applyTheme(); break;
            case 'prefs': applyPrefs(message.prefs); break;
        }
    }

    /** The tab menu's PuTTY items (Clear Scrollback, Reset Terminal, Copy All to Clipboard), sent by TerminalControl */
    function runAction(name) {
        switch (name) {
            case 'clearScrollback': clearScrollback(); break;
            case 'reset':
                term.reset();
                skipLog();
                highlight(true);
                post({ type: 'cleared' });
                break;
            case 'copyAll':
                term.selectAll();
                copy();
                term.clearSelection();
                break;
        }
    }

    /** PuTTY's keys that the terminal itself doesn't know; anything else goes to the session as typed */
    function onKey(e) {
        if (e.type !== 'keydown') { return true; }
        var key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
        if (e.shiftKey && !e.ctrlKey && key === 'Insert') { paste(); return false; }
        if (e.ctrlKey && !e.shiftKey && key === 'Insert') { copy(); return false; }
        if (e.ctrlKey && e.shiftKey && key === 'c') { copy(); return false; }
        if (e.ctrlKey && e.shiftKey && key === 'v') { paste(); return false; }
        if (e.ctrlKey && e.shiftKey && key === 'f') { openFind(); return false; }
        var sequence = profileKey(e);
        if (sequence !== null) {
            // also stops the browser's own F5 (reload) and F12
            e.preventDefault();
            post({ type: 'input', data: sequence });
            return false;
        }
        return true;
    }

    /** xterm's codes for F1-F12 in PuTTY's default "ESC[n~" mode (Terminal > Keyboard > Function keys) */
    var TILDE_CODES = [11, 12, 13, 14, 15, 17, 18, 19, 20, 21, 23, 24];

    /**
     * What PuTTY would send for Backspace, Home, End or F1-F12 with this session's profile (Terminal > Keyboard), where
     * that differs from xterm.js: Backspace as Control-H, Home/End as ESC[1~ / ESC[4~ (or rxvt's), function keys in the
     * profile's style. Only for connections through plink, and not when Options > Terminal says to use xterm's keys.
     */
    function profileKey(e) {
        if (localShell || loadPref('keys') === 'xterm' || e.altKey || e.metaKey) { return null; }
        var keys = keysFromProfile;
        if (e.key === 'Backspace' && !e.shiftKey && keys.backspace === 'bs') { return e.ctrlKey ? '\x7f' : '\x08'; }
        if (e.ctrlKey || e.shiftKey) { return null; }
        if (e.key === 'Home') { return keys.homeEnd === 'rxvt' ? '\x1b[H' : '\x1b[1~'; }
        if (e.key === 'End') { return keys.homeEnd === 'rxvt' ? '\x1bOw' : '\x1b[4~'; }
        var f = /^F(\d+)$/.exec(e.key);
        var n = f ? parseInt(f[1], 10) : 0;
        if (n < 1 || n > 12) { return null; }
        switch (keys.functionKeys) {
            case 'linux': if (n <= 5) { return '\x1b[[' + 'ABCDE'.charAt(n - 1); } break;
            case 'xtermr6':
            case 'xterm216': if (n <= 4) { return '\x1bO' + 'PQRS'.charAt(n - 1); } break;
            case 'vt100': return '\x1bO' + 'PQRSTUVWXYZ['.charAt(n - 1);
            case 'sco': return '\x1b[' + 'MNOPQRSTUVWX'.charAt(n - 1);
        }
        return '\x1b[' + TILDE_CODES[n - 1] + '~';
    }

    /** Create the terminal, wire up input, mouse, menu and messages, then tell TerminalControl the page is ready */
    function start() {
        term = new Terminal({
            allowProposedApi: true,
            cursorBlink: true,
            cursorInactiveStyle: 'outline',
            scrollback: 10000,
            scrollOnEraseInDisplay: true,
            rightClickSelectsWord: false,
            fontFamily: 'Consolas, "Courier New", monospace',
            fontSize: 14,
            windowsPty: { backend: 'conpty' }
        });
        var element = document.getElementById('terminal');
        term.open(element);
        term.attachCustomKeyEventHandler(onKey);
        term.onData(function (data) { post({ type: 'input', data: data }); });
        term.onResize(function (size) { if (sized) { post({ type: 'size', cols: size.cols, rows: size.rows }); } });
        term.onScroll(function () { scheduleSettle(); });
        term.onBell(ring);
        // links in the output open with Ctrl+click, as in Windows Terminal, so selecting text never opens a browser
        if (window.WebLinksAddon) {
            term.loadAddon(new window.WebLinksAddon.WebLinksAddon(function (event, uri) {
                if (event.ctrlKey) { post({ type: 'openLink', uri: uri }); }
            }));
        }

        // PuTTY copies as soon as text is selected (an option in the menu), and the selection's other matches are marked
        var selectionTimer = null;
        term.onSelectionChange(function () {
            window.clearTimeout(selectionTimer);
            selectionTimer = window.setTimeout(function () {
                markSelectionMatches();
                if (document.getElementById('find').hidden && prefOn('copyOnSelect') && term.hasSelection()) { copy(); }
            }, 150);
        });

        // the browser's own paste (Ctrl+V, Shift+Insert on the text box) goes through TerminalControl too, which
        // asks before several lines in a production session (capture: before xterm.js pastes it)
        element.addEventListener('paste', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var text = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
            if (text) { post({ type: 'pasteRequest', text: text }); }
        }, true);
        // capture phase: before xterm.js or SuperPuTTY's focus handling sees the click
        element.addEventListener('mousedown', function (e) {
            if (e.button === 2) { rememberSelection(); }
        }, true);
        // Ctrl+mouse wheel makes the font larger or smaller, as in Windows Terminal (capture: before xterm.js scrolls)
        element.addEventListener('wheel', function (e) {
            if (!e.ctrlKey) { return; }
            e.preventDefault();
            e.stopPropagation();
            stepFontSize(e.deltaY < 0 ? 1 : -1);
        }, { capture: true, passive: false });
        element.addEventListener('contextmenu', function (e) {
            e.preventDefault();
            restoreSelection();
            showMenu(e.clientX, e.clientY);
        });
        // middle-click pastes, as in PuTTY when the right button opens a menu
        element.addEventListener('auxclick', function (e) {
            if (e.button === 1) { e.preventDefault(); paste(); }
        });
        element.addEventListener('mousedown', function (e) { if (e.button === 1) { e.preventDefault(); } });
        document.addEventListener('mousedown', function (e) {
            // a right-click reopens the menu itself; closing it here would forget the selection just remembered
            if (e.button !== 2 && !(e.target.closest && e.target.closest('.menu'))) { hideMenu(); }
        });
        window.addEventListener('blur', hideMenu);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !document.getElementById('menu').hidden) { hideMenu(); term.focus(); }
        });
        bindFind();

        new ResizeObserver(function () { fit(); }).observe(element);
        if (host) {
            host.addEventListener('message', function (event) { onMessage(event.data); });
        }
        // for testing the page in a plain browser: terminalDev.message({...}) and terminalDev.feedText('...')
        window.terminalDev = {
            message: onMessage,
            feedText: function (text) { write(new TextEncoder().encode(text)); },
            term: term,
            posted: [],
            matchCount: function (text) { return SpRules.matches(currentSet(), text, MAX_MATCH_LENGTH).length; }
        };
        term.focus();
        post({ type: 'ready' });
    }

    start();
})();
