using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SuperPutty.Data;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Environment colours, e.g. production in red, so you notice before typing into the wrong device. A session's
    /// colour is, in order: its own (SessionData.Color, from its menu), its folder's or the nearest folder above it
    /// with one (FolderSettings, from the folder's menu), or the first rule (Tools -> Environment colours,
    /// Settings.EnvironmentColorRules) that matches one of its folders' names (deepest first) or its own name.
    /// Shown as a stripe on the tab (TabIcons), a frame around the terminal (ctlPuttyPanel.ApplyEnvironmentColor)
    /// and on the folder or session in the Sessions tree (SessionTreeview). <see cref="Changed"/> tells them to redraw.
    /// </summary>
    public static class EnvironmentColors
    {
        /// <summary>One rule: a name pattern and its colour</summary>
        public class Rule
        {
            /// <summary>A folder or session name, with * and ? wildcards, matched ignoring case, e.g. "Prod*"</summary>
            public string Pattern { get; set; }

            /// <summary>The colour, "#RRGGBB"</summary>
            public string Color { get; set; }

            /// <summary>The pattern as a regex, made on first use</summary>
            private Regex regex;

            /// <summary>Whether a folder or session name matches the pattern</summary>
            /// <param name="name">The name</param>
            /// <returns>true if it matches</returns>
            public bool Matches(string name)
            {
                if (this.regex == null)
                {
                    string pattern = "^" + Regex.Escape(this.Pattern ?? string.Empty).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    this.regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                return this.regex.IsMatch(name ?? string.Empty);
            }
        }

        /// <summary>The rules a new install starts with: production red, staging and UAT orange</summary>
        public const string DefaultRules = "Prod=#C62828;Production=#C62828;Staging=#EF6C00;UAT=#EF6C00";

        /// <summary>The colours offered in the Colour menus, with what they're usually for</summary>
        public static readonly KeyValuePair<string, string>[] Presets =
        {
            new KeyValuePair<string, string>("Red (production)", "#C62828"),
            new KeyValuePair<string, string>("Orange (staging)", "#EF6C00"),
            new KeyValuePair<string, string>("Yellow", "#F9A825"),
            new KeyValuePair<string, string>("Green (lab)", "#2E7D32"),
            new KeyValuePair<string, string>("Blue", "#1565C0"),
            new KeyValuePair<string, string>("Purple", "#6A1B9A")
        };

        /// <summary>Raised when a colour may have changed: a rule, a folder's or a session's</summary>
        public static event EventHandler Changed;

        /// <summary>The rules as last read from the setting, and the setting's text they came from</summary>
        private static List<Rule> rules;
        private static string rulesText;

        /// <summary>Hook up to folder changes, so they redraw too</summary>
        static EnvironmentColors()
        {
            FolderSettings.Changed += (s, e) => RaiseChanged();
        }

        /// <summary>Tell the tabs and the tree to redraw their colours</summary>
        public static void RaiseChanged()
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>The rules, from Settings.EnvironmentColorRules</summary>
        public static List<Rule> Rules
        {
            get
            {
                string text = SuperPuTTY.Settings.EnvironmentColorRules ?? string.Empty;
                if (rules == null || text != rulesText)
                {
                    rules = ParseRules(text);
                    rulesText = text;
                }
                return rules;
            }
        }

        /// <summary>Read rules from their stored form</summary>
        /// <param name="text">e.g. "Prod=#C62828;Staging=#EF6C00"</param>
        /// <returns>The rules with a pattern and a valid colour, in order</returns>
        public static List<Rule> ParseRules(string text)
        {
            List<Rule> list = new List<Rule>();
            foreach (string part in (text ?? string.Empty).Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.LastIndexOf('=');
                if (eq <= 0)
                    continue;
                string pattern = part.Substring(0, eq).Trim(), color = Normalize(part.Substring(eq + 1));
                if (pattern.Length > 0 && color != null)
                    list.Add(new Rule { Pattern = pattern, Color = color });
            }
            return list;
        }

        /// <summary>Rules in their stored form</summary>
        /// <param name="list">The rules</param>
        /// <returns>e.g. "Prod=#C62828;Staging=#EF6C00"</returns>
        public static string FormatRules(IEnumerable<Rule> list)
        {
            return string.Join(";", list.Where(r => !string.IsNullOrWhiteSpace(r.Pattern) && Normalize(r.Color) != null)
                .Select(r => r.Pattern.Trim().Replace(";", string.Empty) + "=" + Normalize(r.Color)));
        }

        /// <summary>A colour as stored: "#RRGGBB" in capitals</summary>
        /// <param name="text">e.g. "#c62828", "C62828"</param>
        /// <returns>The colour, or null if it isn't one</returns>
        public static string Normalize(string text)
        {
            string hex = (text ?? string.Empty).Trim().TrimStart('#');
            int value;
            return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) ? "#" + hex.ToUpperInvariant() : null;
        }

        /// <summary>A stored colour as a Color</summary>
        /// <param name="text">"#RRGGBB"</param>
        /// <returns>The colour, or null if there's none or it isn't valid</returns>
        public static Color? Parse(string text)
        {
            string hex = Normalize(text);
            return hex == null ? (Color?)null : Color.FromArgb(int.Parse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture) | unchecked((int)0xFF000000));
        }

        /// <summary>A colour as stored</summary>
        /// <param name="color">The colour</param>
        /// <returns>"#RRGGBB"</returns>
        public static string Format(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        /// <summary>
        /// A session's environment colour: its own, else its folder's or the nearest folder above with one, else the
        /// first rule matching one of its folders (deepest first) or its name
        /// </summary>
        /// <param name="session">The session; null gives none</param>
        /// <returns>The colour, or null for none</returns>
        public static Color? For(SessionData session)
        {
            if (session == null)
                return null;
            Color? own = Parse(session.Color);
            if (own != null)
                return own;
            string folder = session.IsDatabase ? DatabaseSessions.FolderOf(session.SessionId) : SessionData.GetSessionParentId(session.SessionId) ?? string.Empty;
            FolderSetting set = FolderSettings.Along(FolderSettings.StoreOf(session), folder).FirstOrDefault(f => Parse(f.Color) != null);
            if (set != null)
                return Parse(set.Color);
            List<string> names = folder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Reverse().ToList();
            names.Insert(0, session.SessionName);
            return FirstRule(names);
        }

        /// <summary>
        /// The colour a folder shows in the tree: its own setting, else a rule matching its own name. Not inherited,
        /// so only the folder where a colour starts is coloured, not every folder below it.
        /// </summary>
        /// <param name="store">Its root</param>
        /// <param name="path">Its path, e.g. "Lab/Prod"</param>
        /// <returns>The colour, or null for none</returns>
        public static Color? ForFolder(FolderSettings.Store store, string path)
        {
            FolderSetting own = FolderSettings.Get(store, path);
            if (own != null && Parse(own.Color) != null)
                return Parse(own.Color);
            string name = (path ?? string.Empty).Trim('/');
            int slash = name.LastIndexOf('/');
            return name.Length == 0 ? null : FirstRule(new[] { slash < 0 ? name : name.Substring(slash + 1) });
        }

        /// <summary>The colour of the first rule matching any of the names, trying names in order</summary>
        /// <param name="names">Names, most specific first</param>
        /// <returns>The colour, or null</returns>
        private static Color? FirstRule(IEnumerable<string> names)
        {
            List<Rule> list = Rules;
            foreach (string name in names)
            {
                Rule rule = list.FirstOrDefault(r => r.Matches(name));
                if (rule != null)
                    return Parse(rule.Color);
            }
            return null;
        }

        /// <summary>A colour made readable as text on the tree's background: lightened on a dark one</summary>
        /// <param name="color">The environment colour</param>
        /// <param name="background">The tree's background</param>
        /// <returns>The text colour</returns>
        public static Color ForText(Color color, Color background)
        {
            if (background.GetBrightness() >= 0.5f)
                return color;
            return Color.FromArgb((color.R + 2 * 255) / 3, (color.G + 2 * 255) / 3 - 20, (color.B + 2 * 255) / 3 - 20);
        }

        /// <summary>A small square of a colour, for menus</summary>
        /// <param name="color">The colour</param>
        /// <returns>The image</returns>
        private static Image Swatch(Color color)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, 1, 1, 14, 14);
                g.DrawRectangle(Pens.Gray, 1, 1, 13, 13);
            }
            return bitmap;
        }

        /// <summary>
        /// Fill a Colour menu: the presets, Other colour..., and None, with the current one ticked. Used for folders
        /// and sessions in the Sessions tree.
        /// </summary>
        /// <param name="menu">The menu to fill (its items are replaced)</param>
        /// <param name="current">The colour set now, "#RRGGBB", or empty</param>
        /// <param name="noneText">What None means here, e.g. "None (use the folder's)"</param>
        /// <param name="choose">Called with the chosen colour, or "" for none</param>
        public static void FillMenu(ToolStripMenuItem menu, string current, string noneText, Action<string> choose)
        {
            menu.DropDownItems.Clear();
            current = Normalize(current);
            foreach (KeyValuePair<string, string> preset in Presets)
            {
                string hex = preset.Value;
                menu.DropDownItems.Add(new ToolStripMenuItem(preset.Key, Swatch(Parse(hex).Value), (s, e) => choose(hex)) { Checked = current == hex });
            }
            bool custom = current != null && !Presets.Any(p => p.Value == current);
            menu.DropDownItems.Add(new ToolStripMenuItem(custom ? "Other colour (" + current + ")..." : "Other colour...", custom ? Swatch(Parse(current).Value) : null, (s, e) =>
            {
                using (ColorDialog dialog = new ColorDialog { FullOpen = true, Color = Parse(current) ?? Color.Red })
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                        choose(Format(dialog.Color));
                }
            }) { Checked = custom });
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(new ToolStripMenuItem(noneText, null, (s, e) => choose(string.Empty)) { Checked = current == null });
        }
    }
}
