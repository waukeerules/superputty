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
 * Messages from TerminalControl: config, data, notice, paste, focus. To it: ready, size, input, ack, log, copy,
 * pasteRequest, document.
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
    var settleTimer = null;
    var scanFrom = null;        // marker: first line to look at for highlighting next time
    var logged = null;          // marker: first line not yet sent to the log
    var lastCurrent = null;     // the cursor's line as last sent to the log
    var entries = [];           // highlighted lines: {marker, decorations, text}
    var find = { matches: [], index: -1, decorations: [] };
    var menuSelection = null;   // the selection when the right button went down: {text, position}
    var selectionMatches = [];  // markers on the other places the selected text appears
    var sized = false;

    /** Send a message to TerminalControl (ignored when the page is opened in a plain browser to test it) */
    function post(message) {
        if (host) {
            host.postMessage(message);
        } else if (window.terminalDev) {
            window.terminalDev.posted.push(message);
        }
    }

    /** Read a per-user preference from the WebView2 profile; null when there's none */
    function loadPref(name) {
        try { return window.localStorage.getItem('terminal.' + name); } catch (e) { return null; }
    }

    /** Remember a per-user preference (rule set, theme) for every terminal tab */
    function savePref(name, value) {
        try { window.localStorage.setItem('terminal.' + name, value); } catch (e) { /* not essential */ }
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

    /** The rule set in use: the chosen one, Auto's pick, or none */
    function currentSet() {
        var chosen = loadPref('ruleSet') || 'auto';
        if (chosen === 'auto') { return autoSet; }
        for (var i = 0; i < ruleSets.length; i++) {
            if (ruleSets[i].id === chosen) { return ruleSets[i]; }
        }
        return null;
    }

    /** Remove one highlighted line's decorations */
    function disposeEntry(entry) {
        entry.decorations.forEach(function (d) { d.dispose(); });
        entry.marker.dispose();
    }

    /**
     * Colour lines by the rules: everything when <paramref>all</paramref> (rules or theme changed), otherwise the
     * lines that may have changed since last time (the screen, and lines that scrolled off it since)
     */
    function highlight(all) {
        var buffer = term.buffer.active;
        var set = currentSet();
        if (all) {
            entries.forEach(disposeEntry);
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
                disposeEntry(entry);
                entries.splice(entries.indexOf(entry), 1);
            }
            var found = set ? SpRules.matches(set, text, MAX_MATCH_LENGTH) : [];
            if (!found.length) { continue; }
            var marker = markLine(i);
            if (!marker) { continue; }
            var decorations = [];
            found.forEach(function (m) {
                var style = theme && theme.styles ? theme.styles[m.style] : null;
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

    /** Remove the marks on the selection's other matches */
    function clearSelectionMatches() {
        selectionMatches.forEach(function (marker) { marker.dispose(); });
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
        for (var row = 0; row < buffer.length && selectionMatches.length < MAX_FIND_MATCHES; row++) {
            var line = buffer.getLine(row).translateToString(true);
            var match;
            regex.lastIndex = 0;
            while ((match = regex.exec(line)) !== null && selectionMatches.length < MAX_FIND_MATCHES) {
                var marker = markLine(row);
                if (!marker) { break; }
                term.registerDecoration({
                    marker: marker, x: match.index, width: Math.min(match[0].length, term.cols - match.index),
                    backgroundColor: colour, layer: 'top'
                });
                // disposing the marker removes its decoration too
                selectionMatches.push(marker);
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
    }

    /** Clear Screen and Scrollback: only the cursor's line stays */
    function clearAll() {
        term.clear();
        skipLog();
        highlight(true);
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

    /** Remove the marks on every match */
    function clearFindMarks() {
        find.decorations.forEach(function (d) { d.dispose(); });
        find.decorations = [];
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
        find.matches.forEach(function (m) {
            var marker = markLine(m.row);
            if (!marker) { return; }
            var decoration = term.registerDecoration({
                marker: marker, x: m.col, width: Math.min(m.length, term.cols - m.col),
                backgroundColor: '#806000', layer: 'top'
            });
            // disposing the marker removes its decoration too
            find.decorations.push(marker);
        });
        showMatch(find.matches.length - 1);
    }

    /** Select a match and scroll it into the middle of the view */
    function showMatch(index) {
        if (!find.matches.length) { return; }
        find.index = (index + find.matches.length) % find.matches.length;
        var m = find.matches[find.index];
        term.select(m.col, m.row, m.length);
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
        var chosenRules = loadPref('ruleSet') || 'auto';
        var ruleItems = [{ label: 'Auto' + (autoSet ? ' (' + autoSet.name + ')' : ''), checked: chosenRules === 'auto', action: function () { chooseRules('auto'); } }]
            .concat(ruleSets.map(function (set) {
                return { label: set.name, checked: chosenRules === set.id, action: function () { chooseRules(set.id); } };
            }))
            .concat([{ label: 'None', checked: chosenRules === 'none', action: function () { chooseRules('none'); } }]);
        var themeItems = themes.map(function (t) {
            return { label: t.name, checked: theme && theme.name === t.name, action: function () { savePref('theme', t.name); applyTheme(); } };
        });
        return [
            { label: 'Copy', key: 'Ctrl+Insert', enabled: selected, action: copy },
            { label: 'Paste', key: 'Shift+Insert', action: paste },
            { label: 'Copy and Paste', enabled: selected, action: function () { var text = selectedText(); copy(); term.paste(text); } },
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
            '-',
            { label: 'Copy on Select', checked: prefOn('copyOnSelect'), action: function () { togglePref('copyOnSelect'); } },
            { label: 'Mark Matches of Selection', checked: prefOn('markMatches'), action: function () { togglePref('markMatches'); markSelectionMatches(); } }
        ];
    }

    /** Build a menu element from items; submenus open on hover */
    function buildMenu(element, items) {
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
                row.addEventListener('mouseenter', function () { closeSubmenu(); });
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
        buildMenu(sub, items);
        document.body.appendChild(sub);
        var r = row.getBoundingClientRect();
        var left = r.right + 2;
        if (left + sub.offsetWidth > window.innerWidth) { left = r.left - sub.offsetWidth - 2; }
        sub.style.left = Math.max(0, left) + 'px';
        sub.style.top = Math.max(0, Math.min(r.top - 5, window.innerHeight - sub.offsetHeight)) + 'px';
    }

    /** Close the open submenu, if any */
    function closeSubmenu() {
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

    /** Choose the highlighting rules for every terminal tab */
    function chooseRules(id) {
        savePref('ruleSet', id);
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
            cursor: colors.cursor || colors.foreground,
            cursorAccent: colors.background,
            selectionBackground: colors.selection
        };
        (colors.ansi || []).slice(0, 16).forEach(function (c, i) { xtermTheme[names[i]] = c; });
        term.options.theme = xtermTheme;
        var font = theme.font || {};
        term.options.fontFamily = font.family || 'Consolas, "Courier New", monospace';
        term.options.fontSize = font.size || 14;
        document.documentElement.style.setProperty('--bg', colors.background || '#000000');
        window.setTimeout(fit, 0);
        highlight(true);
    }

    // ---- messages and start ---------------------------------------------------------------------------------------

    /** Apply a config message: rule sets, themes and whether SuperPuTTY is dark; then fit and report the size */
    function configure(config) {
        darkDefault = !!config.dark;
        themes = config.themes || [];
        var compiled = SpRules.compile(config.ruleSets || []);
        ruleSets = compiled.sets;
        autoSet = null;
        if (config.scrollback) { term.options.scrollback = config.scrollback; }
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
        return true;
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

        // PuTTY copies as soon as text is selected (an option in the menu), and the selection's other matches are marked
        var selectionTimer = null;
        term.onSelectionChange(function () {
            window.clearTimeout(selectionTimer);
            selectionTimer = window.setTimeout(function () {
                markSelectionMatches();
                if (document.getElementById('find').hidden && prefOn('copyOnSelect') && term.hasSelection()) { copy(); }
            }, 150);
        });

        // capture phase: before xterm.js or SuperPuTTY's focus handling sees the click
        element.addEventListener('mousedown', function (e) {
            if (e.button === 2) { rememberSelection(); }
        }, true);
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
            posted: []
        };
        term.focus();
        post({ type: 'ready' });
    }

    start();
})();
