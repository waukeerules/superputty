using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using Newtonsoft.Json;

namespace SuperPutty.Utils
{
    /// <summary>
    /// The built-in terminal's preferences, the same for every terminal tab: highlighting rules, theme, font, cursor,
    /// bell, copy on select and marking the selection's matches. Kept as one JSON string in SuperPuTTY's settings
    /// ("TerminalPreferences"), so Tools -> Options -> Terminal (dlgFindPutty) and each terminal's right-click menu
    /// (Gui.TerminalControl, web\terminal\terminal.js) edit the same values; <see cref="Changed"/> lets every open
    /// terminal follow a change at once. Values are strings, as the page uses them; a missing one means the default.
    /// </summary>
    public static class TerminalPreferences
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TerminalPreferences));

        /// <summary>
        /// The preferences and their defaults: ruleSet ("auto", "none" or a rule set's id), theme (a theme's name;
        /// empty for PuTTY's), fontFamily (empty for the theme's, "profile" for the PuTTY profile's, or a font's name),
        /// fontSize (in pixels; "0" for the theme's or profile's), cursorShape and cursorBlink and bell ("profile" for the
        /// PuTTY profile's, or block/underline/bar, on/off, visual/sound/none), copyOnSelect and markMatches ("true" or
        /// "false"), pasteCheck (ask before pasting several lines: "production" for sessions coloured red, "always" or
        /// "never"), scrollback (lines kept; "0" for as many as <see cref="UnlimitedScrollback"/>), keys ("profile" for the
        /// PuTTY profile's Backspace, Home/End and function keys, "xterm" for xterm's), logTimestamps (seconds between
        /// time stamps in the session log; "0" for none) and renderer ("webgl", or "dom" for xterm.js's slower one)
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "ruleSet", "auto" },
            { "theme", "" },
            { "fontFamily", "" },
            { "fontSize", "0" },
            { "cursorShape", "profile" },
            { "cursorBlink", "profile" },
            { "bell", "profile" },
            { "copyOnSelect", "true" },
            { "markMatches", "true" },
            { "pasteCheck", "production" },
            { "scrollback", "10000" },
            { "keys", "profile" },
            { "logTimestamps", "30" },
            { "renderer", "webgl" },
        };

        /// <summary>
        /// The scrollback kept when the preference is 0 (or negative), "unlimited". xterm.js needs a number; a line
        /// takes about 12 bytes per column, so a million 120-column lines is over a gigabyte, reached only by a tab
        /// that really prints that much.
        /// </summary>
        public const int UnlimitedScrollback = 1000000;

        /// <summary>Raised on the UI thread after any preference changes, so open terminals can apply it</summary>
        public static event EventHandler Changed;

        /// <summary>All the preferences, defaults filled in for those not set</summary>
        /// <returns>A new dictionary, one entry per key of <see cref="Defaults"/></returns>
        public static Dictionary<string, string> All()
        {
            Dictionary<string, string> stored = Read();
            return Defaults.ToDictionary(d => d.Key, d => stored.TryGetValue(d.Key, out string value) && value != null ? value : d.Value);
        }

        /// <summary>One preference</summary>
        /// <param name="name">A key of <see cref="Defaults"/></param>
        /// <returns>Its value, or its default</returns>
        public static string Get(string name)
        {
            string value;
            return All().TryGetValue(name, out value) ? value : null;
        }

        /// <summary>Change one preference and save it; names that aren't a preference are ignored</summary>
        /// <param name="name">A key of <see cref="Defaults"/></param>
        /// <param name="value">The new value; null for the default</param>
        public static void Set(string name, string value)
        {
            SetMany(new Dictionary<string, string> { { name, value } });
        }

        /// <summary>Change several preferences, save them once and raise <see cref="Changed"/> once</summary>
        /// <param name="values">Names and values; a null value goes back to the default</param>
        public static void SetMany(IDictionary<string, string> values)
        {
            Dictionary<string, string> stored = Read();
            bool changed = false;
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!Defaults.ContainsKey(pair.Key))
                    continue;
                string old;
                stored.TryGetValue(pair.Key, out old);
                if (old == pair.Value)
                    continue;
                if (pair.Value == null)
                    stored.Remove(pair.Key);
                else
                    stored[pair.Key] = pair.Value;
                changed = true;
            }
            if (!changed)
                return;
            SuperPuTTY.Settings.TerminalPreferences = JsonConvert.SerializeObject(stored);
            SuperPuTTY.Settings.Save();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>The stored preferences; an unreadable setting counts as none</summary>
        /// <returns>The stored values only</returns>
        private static Dictionary<string, string> Read()
        {
            string json = SuperPuTTY.Settings.TerminalPreferences;
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string>();
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch (JsonException ex)
            {
                Log.Warn("Unreadable terminal preferences; using the defaults", ex);
                return new Dictionary<string, string>();
            }
        }
    }
}
