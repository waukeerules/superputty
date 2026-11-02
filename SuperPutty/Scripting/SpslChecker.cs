using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SuperPutty.Utils;

namespace SuperPuTTY.Scripting
{
    /// <summary>
    /// Checks an SPSL script without running it: unknown commands (with a suggestion for a near miss), missing or
    /// malformed arguments, unknown SENDKEY keys, saved sessions that don't exist, and placeholders with no value.
    /// Used by the reference panel's "Check script" button (SpslHelpPanel) and before a script is saved or sent.
    /// </summary>
    public static class SpslChecker
    {
        /// <summary>One problem found in a script</summary>
        public class Issue
        {
            /// <summary>The line it's on, from 1</summary>
            public int Line { get; set; }

            /// <summary>true for a line that won't work; false for one that may not do what's meant</summary>
            public bool IsError { get; set; }

            /// <summary>What's wrong, for the user</summary>
            public string Message { get; set; }
        }

        /// <summary>WAITFOR's optional timeout: "/t:10 text"</summary>
        private static readonly Regex WaitForTimeout = new Regex(@"^/t:(\d+)\s+(.*)$", RegexOptions.IgnoreCase);

        /// <summary>A key in braces with an optional repeat count: "{ENTER}", "{TAB 3}"</summary>
        private static readonly Regex BracedKey = new Regex(@"^\{(?<name>[^}\s]+)(\s+(?<count>\d+))?\}$");

        /// <summary>Check a script</summary>
        /// <param name="script">The script text</param>
        /// <returns>The problems, in line order; empty if none</returns>
        public static List<Issue> Check(string script)
        {
            List<Issue> issues = new List<Issue>();
            string[] lines = (script ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            Dictionary<string, string> values = SessionVariables.Load(true);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int space = line.IndexOf(' ');
                string command = space > 0 ? line.Substring(0, space) : line;
                string args = space > 0 ? line.Substring(space + 1).Trim() : string.Empty;
                SpslReference.Entry entry = SpslReference.Find(command);
                if (entry == null)
                {
                    string suggestion = Suggest(command);
                    // shell text pasted into a script is the usual cause, so show how to type it
                    issues.Add(Error(i, "\"" + command + "\" isn't an SPSL command"
                        + (suggestion != null
                            ? "; did you mean " + suggestion + "?"
                            : ". Lines start with a command; to type this line, write SENDLINE " + line)));
                    continue;
                }

                string problem = CheckArguments(entry.Name, args);
                if (problem != null)
                {
                    issues.Add(problem.StartsWith("!") ? Warning(i, problem.Substring(1)) : Error(i, problem));
                }

                // SENDKEY's braces are key names, not placeholders
                if (entry.Name != "SENDKEY")
                {
                    foreach (string missing in SessionVariables.FindMissing(args, values))
                    {
                        issues.Add(Warning(i, missing + " has no value in Tools > Environment Variables, so it's typed as it is."));
                    }
                }
            }
            return issues;
        }

        /// <summary>Check a command's arguments</summary>
        /// <param name="command">The command, in upper case</param>
        /// <param name="args">Everything after the command</param>
        /// <returns>null if they're fine; an error message; or a warning, starting with "!"</returns>
        private static string CheckArguments(string command, string args)
        {
            switch (command)
            {
                case "SENDLINE":
                    return null;
                case "SENDCHAR":
                    return args.Length == 0 ? "SENDCHAR needs the text to type, e.g. SENDCHAR y" : null;
                case "SENDKEY":
                    return CheckKey(args);
                case "SLEEP":
                    int ms;
                    if (!int.TryParse(args, out ms) || ms <= 0)
                        return "SLEEP needs a number of milliseconds, e.g. SLEEP 500";
                    return ms > 60000 ? "!SLEEP " + ms + " waits " + (ms / 1000) + " seconds." : null;
                case "WAITFOR":
                    string text = args;
                    if (args.StartsWith("/t:", StringComparison.OrdinalIgnoreCase))
                    {
                        Match match = WaitForTimeout.Match(args);
                        if (!match.Success)
                            return "WAITFOR's timeout is /t: and a number of seconds, then the text, e.g. WAITFOR /t:10 Password:";
                        text = match.Groups[2].Value;
                    }
                    return text.Trim().Length == 0 ? "WAITFOR needs the text to wait for, e.g. WAITFOR Password:" : null;
                case "OPENSESSION":
                    if (args.Length == 0)
                        return "OPENSESSION needs a session ID, e.g. OPENSESSION Lab/core-sw1";
                    return SuperPutty.SuperPuTTY.GetSessionById(args) == null
                        ? "!There's no saved session with the ID \"" + args + "\"; the line will do nothing."
                        : null;
                case "CLOSESESSION":
                    return "CLOSESESSION isn't available: SPSL lists it but doesn't implement it, so the script would stop here.";
                default:
                    return null;
            }
        }

        /// <summary>Check a SENDKEY argument: one key name or character, with optional ^ % + modifiers</summary>
        /// <param name="args">The argument</param>
        /// <returns>null if it's fine, or an error message</returns>
        private static string CheckKey(string args)
        {
            string key = args.TrimStart('^', '%', '+');
            if (args.Length == 0)
                return "SENDKEY needs a key, e.g. SENDKEY ENTER or SENDKEY ^C";
            // a brace in braces, as in SendKeys, presses the brace key
            if (key == "{{}" || key == "{}}")
                return null;
            if (key == "{" || key == "}")
                return "SENDKEY " + args + ": braces are SENDKEY's own syntax; to press one, write {{} or {}}";
            Match braced = BracedKey.Match(key);
            string name = braced.Success ? braced.Groups["name"].Value : key;
            if (!braced.Success && key.Length > 1 && key.StartsWith("{"))
                return "SENDKEY " + args + ": a key in braces is written {NAME} or {NAME count}, e.g. {TAB 3}";
            if (name.Length == 0)
                return "SENDKEY " + args + ": ^, % and + are Ctrl, Alt and Shift, so a key must follow; to type the "
                    + "character itself, use SENDCHAR " + args;
            if (name.Length == 1)
                return SpslReference.CanTypeKey(name[0]) ? null
                    : "SENDKEY " + args + ": no key on this keyboard types \"" + name + "\"; use SENDCHAR to type it.";
            if (SpslReference.IsKeyName(name))
                return null;
            return "SENDKEY presses one key; \"" + name + "\" isn't a key name. Use ENTER, TAB, ESC, UP, DOWN, F1-F16 and "
                + "so on (see \"Keys for SENDKEY\"), or a single character. To type text, use SENDCHAR or SENDLINE.";
        }

        /// <summary>The command closest to a misspelt one, if it's close enough to suggest</summary>
        /// <param name="word">The word that isn't a command</param>
        /// <returns>The command, or null</returns>
        private static string Suggest(string word)
        {
            string upper = word.ToUpperInvariant();
            return SpslReference.Entries.Where(e => e.IsCommand && e.Available)
                .Select(e => new { e.Name, Distance = Distance(upper, e.Name) })
                .Where(c => c.Distance <= Math.Max(2, c.Name.Length / 3))
                .OrderBy(c => c.Distance)
                .Select(c => c.Name)
                .FirstOrDefault();
        }

        /// <summary>The Levenshtein distance between two words: how many single-letter edits turn one into the other</summary>
        /// <param name="a">One word</param>
        /// <param name="b">The other</param>
        /// <returns>The distance</returns>
        public static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[a.Length, b.Length];
        }

        /// <summary>An error on a line</summary>
        private static Issue Error(int index, string message)
        {
            return new Issue { Line = index + 1, IsError = true, Message = message };
        }

        /// <summary>A warning on a line</summary>
        private static Issue Warning(int index, string message)
        {
            return new Issue { Line = index + 1, IsError = false, Message = message };
        }
    }
}
