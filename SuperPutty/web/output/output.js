/*
 * Highlighted Output page (see index.html and docs/MonacoTerminal.md).
 *
 * Gui/HighlightedOutputPanel sends this page a PuTTY session log in chunks. The raw log holds what the device sent,
 * including backspaces, "--More--" erasing and cursor moves, so it can't be read line by line. The chunks are fed to
 * an xterm.js Terminal that is never shown, which rebuilds the screen the way PuTTY drew it. Lines that have scrolled
 * off its screen are final and are appended to the Monaco document; the lines still on its screen are rewritten in the
 * document after every chunk. Highlighting is Monaco decorations from regex rules (rules/*.rules.json and the user's
 * own), worked out for the lines in view only, so a long log stays fast.
 *
 * Messages from the panel: config, data (base64 log bytes, answered with ack), restart (the log started again), clear
 * (a new source) and status. Messages to the panel: ready, ack, save, open, folder, reload.
 */
(function () {
    'use strict';

    /** Width of the hidden terminal. Wider than PuTTY's, so long lines aren't wrapped; devices rarely position by column. */
    var COLS = 200;
    /** Height of the hidden terminal's screen: the lines that may still change */
    var ROWS = 50;
    /** Lines the hidden terminal keeps after they scroll off; they're copied to Monaco long before that */
    var SCROLLBACK = 10000;
    /** Bytes written to the hidden terminal at a time, so no more than SCROLLBACK lines scroll off between copies */
    var PIECE = 8192;
    /** Lines above and below the view that get highlighted too, so scrolling a little doesn't show plain text */
    var MARGIN = 60;
    /** Longest part of a line the rules look at; very long lines are rare and slow to match */
    var MAX_MATCH_LENGTH = 4000;

    var host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
    var editor, model, term;
    var committed = 0;          // document lines that are final (copied from the terminal's scrollback)
    var marker = null;          // terminal line where the next final lines start
    var queue = [];             // chunks waiting to be written to the terminal
    var writing = false;
    var ruleSets = [];          // {id, name, detect: [RegExp], rules: [{regex, style, line}]}
    var themes = [];            // from the panel: {name, base, colors, font, styles}
    var autoSet = null;         // the rule set Auto picked, once a detect pattern matched
    var decorations = null;
    var highlightPending = false;
    var programmaticScroll = false;
    var sourceName = '';
    var darkDefault = false;

    /** Send a message to the panel (ignored when the page is opened in a plain browser to test it) */
    function post(message) {
        if (host) {
            host.postMessage(message);
        }
    }

    /** Read a per-user preference from the WebView2 profile; null when there's none or storage is blocked */
    function loadPref(name) {
        try { return window.localStorage.getItem('output.' + name); } catch (e) { return null; }
    }

    /** Remember a per-user preference (rule set, theme, wrap) */
    function savePref(name, value) {
        try { window.localStorage.setItem('output.' + name, value); } catch (e) { /* not essential */ }
    }

    /** Show a short note at the right of the toolbar */
    function setStatus(text) {
        document.getElementById('status').textContent = text;
    }

    /** Update the status with the source, line count and the rules in use */
    function showSummary() {
        var set = currentSet();
        var rules = document.getElementById('ruleSet').value === 'auto'
            ? 'Auto' + (set ? ': ' + set.name : ' (no match yet)')
            : (set ? set.name : 'None');
        setStatus(sourceName + ' · ' + model.getLineCount().toLocaleString() + ' lines · ' + rules);
    }

    // ---- rules ------------------------------------------------------------------------------------------------

    /** Turn a style name into a CSS class part (shared/rules.js) */
    var styleClass = SpRules.styleClass;

    /** Compile the rule sets the panel sent (shared/rules.js); returns the problems to show in the status */
    function compileRuleSets(raw) {
        var compiled = SpRules.compile(raw);
        ruleSets = compiled.sets;
        return compiled.problems;
    }

    /** The rule set in use: the chosen one, or the one Auto picked */
    function currentSet() {
        var chosen = document.getElementById('ruleSet').value;
        if (chosen === 'auto') {
            return autoSet;
        }
        for (var i = 0; i < ruleSets.length; i++) {
            if (ruleSets[i].id === chosen) { return ruleSets[i]; }
        }
        return null;
    }

    /** For Auto: pick the first rule set whose detect pattern matches one of these new lines */
    function detect(lines) {
        if (autoSet) {
            return;
        }
        for (var i = 0; i < lines.length && !autoSet; i++) {
            autoSet = SpRules.detect(ruleSets, lines[i]);
        }
        if (autoSet) {
            scheduleHighlight();
            showSummary();
        }
    }

    /** Fill the rule set list: Auto, every set, None */
    function fillRuleSetList() {
        var select = document.getElementById('ruleSet');
        var wanted = loadPref('ruleSet') || select.value || 'auto';
        select.innerHTML = '';
        select.appendChild(new Option('Auto', 'auto'));
        ruleSets.forEach(function (set) { select.appendChild(new Option(set.name, set.id)); });
        select.appendChild(new Option('None', 'none'));
        select.value = wanted;
        if (select.selectedIndex < 0) { select.value = 'auto'; }
    }

    // ---- highlighting -----------------------------------------------------------------------------------------

    /** Work out the highlighting again on the next frame (after scrolling or new output) */
    function scheduleHighlight() {
        if (highlightPending) { return; }
        highlightPending = true;
        window.requestAnimationFrame(function () {
            highlightPending = false;
            highlight();
        });
    }

    /** Decorate the lines in view (and a margin) with the current rule set's styles */
    function highlight() {
        var set = currentSet();
        var found = [];
        if (set && set.rules.length) {
            var ranges = editor.getVisibleRanges();
            if (ranges.length) {
                var first = Math.max(1, ranges[0].startLineNumber - MARGIN);
                var last = Math.min(model.getLineCount(), ranges[ranges.length - 1].endLineNumber + MARGIN);
                for (var n = first; n <= last; n++) {
                    decorateLine(set, n, model.getLineContent(n), found);
                }
            }
        }
        decorations.set(found);
    }

    /**
     * Add one line's decorations. Whole-line rules come first and use "hl-line-" classes, which the theme's CSS
     * puts before the match classes, so a match inside a coloured line keeps its own colour.
     */
    function decorateLine(set, lineNumber, text, found) {
        SpRules.matches(set, text, MAX_MATCH_LENGTH).forEach(function (m) {
            found.push({
                range: new monaco.Range(lineNumber, m.start + 1, lineNumber, m.end + 1),
                options: { inlineClassName: (m.line ? 'hl-line-' : 'hl-') + m.style }
            });
        });
    }

    // ---- themes -----------------------------------------------------------------------------------------------

    /** CSS for one style: { color, background, bold, italic, underline } */
    function styleCss(style) {
        var css = '';
        if (style.color) { css += 'color:' + style.color + ' !important;'; }
        if (style.background) { css += 'background-color:' + style.background + ';'; }
        if (style.bold) { css += 'font-weight:bold;'; }
        if (style.italic) { css += 'font-style:italic;'; }
        if (style.underline) { css += 'text-decoration:underline;'; }
        return css;
    }

    /** Fill the theme list; the remembered choice, else Dark or Light to match SuperPuTTY */
    function fillThemeList() {
        var select = document.getElementById('theme');
        var wanted = loadPref('theme') || select.value;
        select.innerHTML = '';
        themes.forEach(function (theme) { select.appendChild(new Option(theme.name, theme.name)); });
        select.value = wanted || '';
        if (select.selectedIndex < 0) {
            var fallback = themes.filter(function (t) { return (t.base === 'vs-dark') === darkDefault; })[0] || themes[0];
            if (fallback) { select.value = fallback.name; }
        }
    }

    /** Apply the chosen theme: Monaco colours, font, the toolbar and the highlighting classes */
    function applyTheme() {
        var name = document.getElementById('theme').value;
        var theme = themes.filter(function (t) { return t.name === name; })[0];
        if (!theme) { return; }
        var colors = theme.colors || {};
        var monacoColors = {};
        if (colors.background) { monacoColors['editor.background'] = colors.background; }
        if (colors.foreground) { monacoColors['editor.foreground'] = colors.foreground; }
        if (colors.selection) { monacoColors['editor.selectionBackground'] = colors.selection; }
        if (colors.lineNumbers) { monacoColors['editorLineNumber.foreground'] = colors.lineNumbers; }
        if (colors.currentLine) { monacoColors['editor.lineHighlightBackground'] = colors.currentLine; }
        monaco.editor.defineTheme('sp-theme', {
            base: theme.base === 'vs-dark' ? 'vs-dark' : 'vs',
            inherit: true,
            rules: [],
            colors: monacoColors
        });
        monaco.editor.setTheme('sp-theme');
        var font = theme.font || {};
        editor.updateOptions({
            fontFamily: font.family || 'Consolas, "Courier New", monospace',
            fontSize: font.size || 13
        });

        var root = document.documentElement.style;
        var dark = theme.base === 'vs-dark';
        root.setProperty('--toolbar-bg', colors.toolbar || (dark ? '#2d2d30' : '#f3f3f3'));
        root.setProperty('--toolbar-fg', colors.toolbarText || (dark ? '#e0e0e0' : '#1e1e1e'));
        root.setProperty('--toolbar-border', dark ? '#3f3f46' : '#d0d0d0');
        root.setProperty('--control-bg', colors.background || (dark ? '#1e1e1e' : '#ffffff'));

        var styles = theme.styles || {};
        var lineCss = '', matchCss = '';
        Object.keys(styles).forEach(function (key) {
            var css = styleCss(styles[key]);
            lineCss += '.hl-line-' + styleClass(key) + '{' + css + '}\n';
            matchCss += '.hl-' + styleClass(key) + '{' + css + '}\n';
        });
        var sheet = document.getElementById('theme-styles');
        if (!sheet) {
            sheet = document.createElement('style');
            sheet.id = 'theme-styles';
            document.head.appendChild(sheet);
        }
        sheet.textContent = lineCss + matchCss;
    }

    // ---- feeding the log --------------------------------------------------------------------------------------

    /** Base64 from the panel to bytes for the terminal, which decodes UTF-8 across chunk boundaries itself */
    function decode(base64) {
        var binary = window.atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) { bytes[i] = binary.charCodeAt(i); }
        return bytes;
    }

    /** Queue log bytes; ack tells the panel to send the next chunk once these are written */
    function feed(bytes, ack) {
        for (var offset = 0; offset < bytes.length; offset += PIECE) {
            queue.push(bytes.subarray(offset, Math.min(bytes.length, offset + PIECE)));
        }
        if (ack) { queue.push('ack'); }
        pump();
    }

    /** Write the queued pieces one at a time, copying lines to the document after each */
    function pump() {
        if (writing) { return; }
        var next = queue.shift();
        if (next === undefined) { return; }
        if (next === 'ack') {
            post({ type: 'ack' });
            pump();
            return;
        }
        writing = true;
        term.write(next, function () {
            writing = false;
            sync();
            pump();
        });
    }

    /** One terminal line as text, without trailing blanks */
    function lineText(line) {
        return line ? line.translateToString(true) : '';
    }

    /** Lines from the terminal buffer, joining lines the terminal wrapped back into one */
    function readLines(buffer, from, to) {
        var lines = [];
        for (var i = from; i < to; i++) {
            var line = buffer.getLine(i);
            if (line && line.isWrapped && lines.length) {
                lines[lines.length - 1] += lineText(line);
            } else {
                lines.push(lineText(line));
            }
        }
        return lines;
    }

    /**
     * Copy the terminal into the document: lines that scrolled off its screen since last time become final, and the
     * lines on its screen replace the document's last lines
     */
    function sync() {
        var buffer = term.buffer.active;
        var base = buffer.baseY;
        var start = marker && !marker.isDisposed ? marker.line : 0;
        var final = readLines(buffer, Math.max(0, start), base);
        var tail = readLines(buffer, base, buffer.length);
        while (tail.length && tail[tail.length - 1] === '') { tail.pop(); }
        replaceTail(final, tail);
        if (marker) { marker.dispose(); }
        // registerMarker counts from the cursor's line; this puts it on the first screen line
        marker = term.registerMarker(-buffer.cursorY);
        detect(final.concat(tail));
    }

    /** Replace everything after the final lines with these final lines and the live screen lines */
    function replaceTail(final, tail) {
        var text = final.concat(tail.length ? tail : ['']).join('\n');
        var last = model.getLineCount();
        var from = committed + 1;
        model.applyEdits([{ range: new monaco.Range(from, 1, last, model.getLineMaxColumn(last)), text: text }]);
        committed += final.length;
        if (document.getElementById('follow').checked) {
            programmaticScroll = true;
            editor.revealLine(model.getLineCount());
            programmaticScroll = false;
        }
        scheduleHighlight();
        showSummary();
    }

    /** The log started again (PuTTY restarted the session): keep what's shown and continue under a separator */
    function restart() {
        queue = [];
        writing = false;
        var lines = model.getLineCount();
        model.applyEdits([{
            range: new monaco.Range(lines, model.getLineMaxColumn(lines), lines, model.getLineMaxColumn(lines)),
            text: '\n──── log restarted ────\n'
        }]);
        committed = model.getLineCount() - 1;
        if (marker) { marker.dispose(); marker = null; }
        term.reset();
    }

    /** Start over with a new source (a file the user opened) */
    function clear(name) {
        queue = [];
        writing = false;
        if (marker) { marker.dispose(); marker = null; }
        term.reset();
        model.setValue('');
        committed = 0;
        autoSet = null;
        sourceName = name || sourceName;
        showSummary();
    }

    /** Apply a config message: rule sets, themes, the source's name and whether SuperPuTTY is dark */
    function configure(config) {
        darkDefault = !!config.dark;
        sourceName = config.title || sourceName;
        themes = config.themes || [];
        var problems = compileRuleSets(config.ruleSets || []);
        autoSet = null;
        fillRuleSetList();
        fillThemeList();
        applyTheme();
        detect(model.getLinesContent().slice(0, 2000));
        scheduleHighlight();
        showSummary();
        if (problems.length) {
            setStatus('Some rules were skipped: ' + problems.join('; '));
        }
    }

    /** Handle a message from the panel */
    function onMessage(message) {
        if (!message || !message.type) { return; }
        switch (message.type) {
            case 'config': configure(message); break;
            case 'data': feed(decode(message.data), true); break;
            case 'restart': restart(); break;
            case 'clear': clear(message.title); break;
            case 'status': setStatus(message.text); break;
        }
    }

    // ---- start ------------------------------------------------------------------------------------------------

    /** Wire up the toolbar */
    function bindToolbar() {
        var ruleSet = document.getElementById('ruleSet');
        ruleSet.addEventListener('change', function () {
            savePref('ruleSet', ruleSet.value);
            scheduleHighlight();
            showSummary();
        });
        var theme = document.getElementById('theme');
        theme.addEventListener('change', function () {
            savePref('theme', theme.value);
            applyTheme();
        });
        var wrap = document.getElementById('wrap');
        wrap.checked = loadPref('wrap') === 'true';
        wrap.addEventListener('change', function () {
            savePref('wrap', String(wrap.checked));
            editor.updateOptions({ wordWrap: wrap.checked ? 'on' : 'off' });
        });
        editor.updateOptions({ wordWrap: wrap.checked ? 'on' : 'off' });
        var follow = document.getElementById('follow');
        follow.addEventListener('change', function () {
            if (follow.checked) { editor.revealLine(model.getLineCount()); }
        });
        document.getElementById('find').addEventListener('click', function () {
            editor.focus();
            editor.getAction('actions.find').run();
        });
        document.getElementById('save').addEventListener('click', function () {
            post({ type: 'save', text: model.getValue() });
        });
        document.getElementById('open').addEventListener('click', function () { post({ type: 'open' }); });
        document.getElementById('folder').addEventListener('click', function () { post({ type: 'folder' }); });
        document.getElementById('reload').addEventListener('click', function () { post({ type: 'reload' }); });
    }

    /** Create the hidden terminal, the editor and the message handlers, then tell the panel the page is ready */
    function start() {
        term = new Terminal({ cols: COLS, rows: ROWS, scrollback: SCROLLBACK, allowProposedApi: true });
        model = monaco.editor.createModel('', 'plaintext');
        editor = monaco.editor.create(document.getElementById('editor'), {
            model: model,
            readOnly: true,
            automaticLayout: true,
            scrollBeyondLastLine: false,
            folding: true,
            foldingStrategy: 'indentation',
            renderLineHighlight: 'line',
            unicodeHighlight: { ambiguousCharacters: false, invisibleCharacters: false },
            occurrencesHighlight: 'off',
            links: false,
            contextmenu: true,
            minimap: { enabled: true }
        });
        decorations = editor.createDecorationsCollection();
        editor.onDidScrollChange(function (e) {
            scheduleHighlight();
            // new output only changes the scroll height; only the user moving the view changes following
            if (!programmaticScroll && e.scrollTopChanged) {
                // scrolling up stops following; scrolling back to the end starts it again
                var bottom = editor.getScrollTop() + editor.getLayoutInfo().height >= editor.getScrollHeight() - 4;
                document.getElementById('follow').checked = bottom;
            }
        });
        bindToolbar();

        if (host) {
            host.addEventListener('message', function (event) { onMessage(event.data); });
        }
        // for testing the page in a plain browser: outputDev.message({...}) and outputDev.feedText('...')
        window.outputDev = {
            message: onMessage,
            feedText: function (text) { feed(new TextEncoder().encode(text), false); }
        };
        post({ type: 'ready' });
    }

    window.require.config({ paths: { vs: '../lib/monaco/vs' } });
    window.require(['vs/editor/editor.main'], start);
})();
