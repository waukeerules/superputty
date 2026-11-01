/*
 * Highlighting rules shared by the Highlighted Output panel (output/output.js) and the built-in terminal
 * (terminal/terminal.js). The rule and theme files are read by Utils/OutputHighlighting.cs and passed in as objects;
 * their format is in docs/MonacoTerminal.md and the README the Rules Folder button writes.
 */
(function () {
    'use strict';

    /** Turn a style name into a CSS class part */
    function styleClass(style) {
        return String(style || '').toLowerCase().replace(/[^a-z0-9-]/g, '-');
    }

    /**
     * Compile rule sets. A set may "include" others (their rules come first); a bad regex is skipped and reported
     * rather than stopping the others.
     * @returns {{sets: Array, problems: Array<string>}}
     */
    function compile(raw) {
        var byId = {};
        var problems = [];
        (raw || []).forEach(function (set) { if (set && set.id) { byId[set.id] = set; } });

        function compileRule(rule, setName) {
            try {
                return {
                    regex: new RegExp(rule.match, 'g' + (rule.ignoreCase ? 'i' : '')),
                    style: styleClass(rule.style),
                    line: rule.scope === 'line'
                };
            } catch (e) {
                problems.push(setName + ': ' + e.message);
                return null;
            }
        }

        function rulesOf(set, seen) {
            var result = [];
            (set.include || []).forEach(function (id) {
                if (byId[id] && seen.indexOf(id) < 0) {
                    result = result.concat(rulesOf(byId[id], seen.concat([id])));
                }
            });
            (set.rules || []).forEach(function (rule) {
                var compiled = compileRule(rule, set.name || set.id);
                if (compiled) { result.push(compiled); }
            });
            return result;
        }

        var sets = (raw || []).filter(function (set) { return set && set.id; }).map(function (set) {
            return {
                id: set.id,
                name: set.name || set.id,
                detect: (set.detect || []).map(function (d) {
                    try { return new RegExp(d, 'm'); } catch (e) { problems.push((set.name || set.id) + ': ' + e.message); return null; }
                }).filter(Boolean),
                rules: rulesOf(set, [set.id])
            };
        });
        return { sets: sets, problems: problems };
    }

    /** The first rule set whose detect pattern matches the line, or null */
    function detect(sets, line) {
        for (var s = 0; s < sets.length; s++) {
            for (var d = 0; d < sets[s].detect.length; d++) {
                if (sets[s].detect[d].test(line)) { return sets[s]; }
            }
        }
        return null;
    }

    /**
     * Where a rule set colours a line: whole-line rules first, then matches, so a match drawn later wins over its
     * line's colour.
     * @returns {Array<{start: number, end: number, style: string, line: boolean}>}
     */
    function matches(set, text, maxLength) {
        var found = [];
        if (!set || !text) { return found; }
        var subject = maxLength && text.length > maxLength ? text.substring(0, maxLength) : text;
        set.rules.forEach(function (rule) {
            rule.regex.lastIndex = 0;
            if (rule.line) {
                if (rule.regex.test(subject)) {
                    found.unshift({ start: 0, end: text.length, style: rule.style, line: true });
                }
                return;
            }
            var match;
            while ((match = rule.regex.exec(subject)) !== null) {
                if (match[0].length === 0) {
                    rule.regex.lastIndex++;
                    continue;
                }
                found.push({ start: match.index, end: match.index + match[0].length, style: rule.style, line: false });
            }
        });
        return found;
    }

    window.SpRules = { styleClass: styleClass, compile: compile, detect: detect, matches: matches };
})();
