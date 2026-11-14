/*
 * Compare Output: Monaco's diff editor over two outputs Gui/CompareOutputPanel sends, as SuperXTerminal's compare.js.
 * Messages from the panel: { type: 'compare', dark, left: { title, text }, right: { title, text } }. To it:
 * { type: 'refresh' } to ask for both outputs again (when the page is ready, Refresh, and every 2 s with Follow).
 */
(function () {
    'use strict';

    /** The WebView2 host; null when the page is opened in a plain browser to test it */
    var host = window.chrome && window.chrome.webview ? window.chrome.webview : null;
    /** Monaco's diff editor, once it's loaded */
    var editor = null;
    /** The left output's model (the original side) */
    var left = null;
    /** The right output's model (the changed side) */
    var right = null;
    /** Follow's timer, asking for the outputs every 2 seconds; null when Follow is off */
    var followTimer = null;

    /** An element by its id */
    function $(id) { return document.getElementById(id); }

    /** Send a message to CompareOutputPanel (kept for tests when there's no host) */
    function post(message) {
        if (host) {
            host.postMessage(message);
        } else if (window.compareDev) {
            window.compareDev.posted.push(message);
        }
    }

    /** Ask the panel for both outputs again */
    function ask() {
        post({ type: 'refresh' });
    }

    /** The output as compared: the session log's time stamp lines left out (they'd differ every time) */
    function clean(text) {
        return String(text || '').replace(/\r\n/g, '\n').split('\n')
            .filter(function (line) { return !/^--- \d{4}-\d\d-\d\d \d\d:\d\d:\d\d ---$/.test(line); })
            .join('\n');
    }

    /** Show the two outputs and their titles, changing only a side whose text changed (so the view stays where it is) */
    function show(message) {
        $('titles').textContent = message.left.title + '  \u2194  ' + message.right.title;
        document.documentElement.classList.toggle('light', message.dark === false);
        monaco.editor.setTheme(message.dark === false ? 'vs' : 'vs-dark');
        var a = clean(message.left.text);
        var b = clean(message.right.text);
        if (!left) {
            left = monaco.editor.createModel(a, 'plaintext');
            right = monaco.editor.createModel(b, 'plaintext');
            editor.setModel({ original: left, modified: right });
        } else {
            if (left.getValue() !== a) { left.setValue(a); }
            if (right.getValue() !== b) { right.setValue(b); }
        }
        window.setTimeout(count, 300);
    }

    /** Say in the toolbar how many differences there are */
    function count() {
        var changes = editor.getLineChanges() || [];
        $('status').textContent = changes.length === 0 ? 'The same' : changes.length + ' difference' + (changes.length === 1 ? '' : 's');
    }

    /** A message from the panel */
    function onMessage(data) {
        if (data && data.type === 'compare' && data.left && data.right) { show(data); }
    }

    /** Make the diff editor, wire up the toolbar and the panel's messages, then ask for the outputs */
    function start() {
        editor = monaco.editor.createDiffEditor($('editor'), {
            readOnly: true,
            originalEditable: false,
            automaticLayout: true,
            renderSideBySide: true,
            ignoreTrimWhitespace: true,
            minimap: { enabled: false },
            fontFamily: 'Consolas, "Courier New", monospace',
            fontSize: 13,
            scrollBeyondLastLine: false
        });
        editor.onDidUpdateDiff(count);
        $('inline').addEventListener('change', function () { editor.updateOptions({ renderSideBySide: !this.checked }); });
        $('spaces').addEventListener('change', function () { editor.updateOptions({ ignoreTrimWhitespace: this.checked }); });
        $('refresh').addEventListener('click', ask);
        $('next').addEventListener('click', function () { editor.goToDiff('next'); });
        $('previous').addEventListener('click', function () { editor.goToDiff('previous'); });
        $('follow').addEventListener('change', function () {
            if (followTimer) { window.clearInterval(followTimer); }
            followTimer = this.checked ? window.setInterval(ask, 2000) : null;
        });
        if (host) {
            host.addEventListener('message', function (event) { onMessage(event.data); });
        }
        // for testing the page in a plain browser: compareDev.message({...}), and what it asked for in posted
        window.compareDev = {
            message: onMessage,
            posted: window.compareDev ? window.compareDev.posted : [],
            status: function () { return $('status').textContent; },
            sides: function () { return { left: left ? left.getValue() : null, right: right ? right.getValue() : null }; }
        };
        ask();
    }

    window.compareDev = { posted: [] };
    window.require.config({ paths: { vs: '../lib/monaco/vs' } });
    window.require(['vs/editor/editor.main'], start);
})();
