using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// The built-in terminal's defaults on Tools -> Options -> Terminal (dlgFindPutty): highlighting rules, theme, font,
    /// font size, cursor, blinking, bell, copy on select, marking the selection's matches, showing a paste of several
    /// lines first, scrollback, keys, session log time stamps, the renderer, word delimiters, wrapping lines, whether
    /// the built-in highlighting rules are used, the terminal's own colours over the theme's, and the anti-idle string. These are the same
    /// preferences each terminal's right-click menu changes (Utils.TerminalPreferences), so a change in either shows in
    /// the other and in every open terminal tab. Built in code: a label and a control per row.
    /// </summary>
    public class TerminalOptionsPanel : UserControl
    {
        /// <summary>A choice in one of the drop-downs: what it shows, and the preference value it stands for</summary>
        private class Choice
        {
            /// <summary>The text shown</summary>
            public string Text;

            /// <summary>The preference's value</summary>
            public string Value;

            /// <summary>Show the text in the drop-down</summary>
            /// <returns>The text</returns>
            public override string ToString()
            {
                return this.Text;
            }
        }

        private readonly ComboBox ruleSet = NewCombo();
        private readonly CheckBox builtInRules = new CheckBox { Text = "Built-in highlighting rules (Cisco, Junos, Generic)", AutoSize = true };
        private readonly ComboBox theme = NewCombo();
        private readonly ComboBox fontFamily = NewCombo();
        private readonly NumericUpDown fontSize = new NumericUpDown { Minimum = 0, Maximum = 48, Width = 80 };
        private readonly ComboBox cursorShape = NewCombo();
        private readonly ComboBox cursorBlink = NewCombo();
        private readonly ComboBox bell = NewCombo();
        private readonly CheckBox copyOnSelect = new CheckBox { Text = "Copy on select (as PuTTY does)", AutoSize = true };
        private readonly CheckBox markMatches = new CheckBox { Text = "Mark the other matches of the selected text (as Notepad++ does)", AutoSize = true };
        private readonly NumericUpDown pasteLines = new NumericUpDown { Minimum = 0, Maximum = 10000, Width = 80 };
        private readonly NumericUpDown scrollback = new NumericUpDown { Minimum = 0, Maximum = TerminalPreferences.UnlimitedScrollback, Increment = 1000, Width = 110, ThousandsSeparator = true };
        private readonly ComboBox keys = NewCombo();
        private readonly NumericUpDown logTimestamps = new NumericUpDown { Minimum = 0, Maximum = 3600, Increment = 10, Width = 80 };
        private readonly ComboBox renderer = NewCombo();
        private readonly TextBox wordSeparators = new TextBox { Width = 120 };
        /// <summary>The colour preferences shown in the Colours row, by name, with their label</summary>
        private static readonly string[][] ColourPrefs =
        {
            new[] { "colorBackground", "Background" },
            new[] { "colorForeground", "Text" },
            new[] { "colorSelection", "Selection" },
            new[] { "colorCursor", "Cursor" },
        };

        /// <summary>The colours chosen in the Colours row, by preference name; "" for the theme's</summary>
        private readonly Dictionary<string, string> colours = new Dictionary<string, string>();

        /// <summary>The Colours row's swatches, by preference name</summary>
        private readonly Dictionary<string, PictureBox> colourSwatches = new Dictionary<string, PictureBox>();

        /// <summary>Tooltips for the rows whose notes don't fit beside them (anti-idle, word delimiters)</summary>
        private readonly ToolTip tips = new ToolTip();

        private readonly TextBox antiIdle = new TextBox { Width = 120 };
        private readonly NumericUpDown antiIdleSeconds = new NumericUpDown { Minimum = 1, Maximum = 86400, Increment = 30, Width = 90 };
        private readonly CheckBox wrap = new CheckBox { Text = "Wrap lines at the tab's width (off: 300 columns, panned sideways)", AutoSize = true };

        /// <summary>Lay out the rows; <see cref="LoadPreferences"/> fills them</summary>
        public TerminalOptionsPanel()
        {
            TableLayoutPanel table = new TableLayoutPanel
            {
                // as tall as its rows, so the Options page can size the box around it at any DPI
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(table, "Highlighting rules:", this.ruleSet);
            table.Controls.Add(this.builtInRules, 1, table.RowCount++);
            // the drop-down offers the sets there would be with the box as it is now
            this.builtInRules.CheckedChanged += (s, e) => this.FillRuleSets((this.ruleSet.SelectedItem as Choice)?.Value);
            AddRow(table, "Theme:", this.theme);
            AddRow(table, "Font:", this.fontFamily);
            FlowLayoutPanel size = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            size.Controls.Add(this.fontSize);
            size.Controls.Add(new Label { Text = "pixels (0: the theme's or PuTTY profile's size)", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            AddRow(table, "Font size:", size);
            AddRow(table, "Cursor:", this.cursorShape);
            AddRow(table, "Cursor blinks:", this.cursorBlink);
            AddRow(table, "Bell:", this.bell);
            AddRow(table, "Check pastes of:", WithNote(this.pasteLines, "lines or more before typing them (0: never)"));
            AddRow(table, "Scrollback:", WithNote(this.scrollback, "lines (0: unlimited)"));
            AddRow(table, "Keys:", this.keys);
            AddRow(table, "Log time stamps:", WithNote(this.logTimestamps, "seconds apart, with output (0: none)"));
            AddRow(table, "Drawing:", this.renderer);
            AddRow(table, "Word delimiters:", this.WordSeparatorsRow());
            AddRow(table, "Colours:", this.ColoursRow());
            AddRow(table, "Anti-idle:", this.AntiIdleRow());
            table.Controls.Add(this.copyOnSelect, 1, table.RowCount++);
            table.Controls.Add(this.markMatches, 1, table.RowCount++);
            table.Controls.Add(this.wrap, 1, table.RowCount++);
            this.Controls.Add(table);
        }

        /// <summary>
        /// The word delimiters box (as Notepad++'s Preferences > Delimiter), a Default link that puts xterm.js's own
        /// back, and a note that a space always ends a word. Links rather than buttons here: the dark theme (DarkModeCS)
        /// gives a button this panel's own colour, so it showed as plain text.
        /// </summary>
        /// <returns>The row's control</returns>
        private Control WordSeparatorsRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            this.wordSeparators.Margin = new Padding(0, 3, 0, 0);
            row.Controls.Add(this.wordSeparators);
            LinkLabel reset = new LinkLabel { Text = "Default", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
            reset.LinkClicked += (s, e) => this.wordSeparators.Text = TerminalPreferences.DefaultWordSeparators;
            row.Controls.Add(reset);
            row.Controls.Add(new Label { Text = "and a space", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            this.tips.SetToolTip(this.wordSeparators, "Where a double-clicked word ends, besides a space. Add # so a double-click on \"sw1#show\" selects \"show\".");
            return row;
        }

        /// <summary>
        /// The Colours row, as SuperXTerminal's Settings > Terminal > Colours: Background, Text, Selection and Cursor,
        /// each a swatch of its colour and a link that opens the colour picker, and a "Theme's" link that goes back to
        /// the theme's colour. Two by two, so the row fits the Options page. A session's environment colour still wins
        /// for the cursor.
        /// </summary>
        /// <returns>The row's control</returns>
        private Control ColoursRow()
        {
            TableLayoutPanel row = new TableLayoutPanel { AutoSize = true, Margin = new Padding(0), ColumnCount = 6, RowCount = 2 };
            foreach (string[] pref in ColourPrefs)
            {
                string name = pref[0];
                PictureBox swatch = new PictureBox { Size = new Size(14, 14), Margin = new Padding(0, 6, 4, 0) };
                swatch.Click += (s, e) => this.PickColour(name);
                LinkLabel pick = new LinkLabel { Text = pref[1], AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
                pick.LinkClicked += (s, e) => this.PickColour(name);
                this.tips.SetToolTip(pick, "Choose the " + pref[1].ToLowerInvariant() + " colour");
                LinkLabel reset = new LinkLabel { Text = "(theme's)", AutoSize = true, Margin = new Padding(0, 6, 18, 0) };
                reset.LinkClicked += (s, e) => this.SetColour(name, string.Empty);
                this.colourSwatches[name] = swatch;
                row.Controls.Add(swatch);
                row.Controls.Add(pick);
                row.Controls.Add(reset);
            }
            return row;
        }

        /// <summary>
        /// The anti-idle row, as KiTTY's: the string typed after a quiet spell (KiTTY's escapes, e.g. \x05\ for
        /// Ctrl+E), and after how many seconds without typing
        /// </summary>
        /// <returns>The row's control</returns>
        private Control AntiIdleRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            this.antiIdle.Margin = new Padding(0, 3, 0, 0);
            row.Controls.Add(this.antiIdle);
            row.Controls.Add(new Label { Text = "after", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            this.antiIdleSeconds.Margin = new Padding(6, 3, 0, 0);
            row.Controls.Add(this.antiIdleSeconds);
            row.Controls.Add(new Label { Text = "s without typing", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            this.tips.SetToolTip(this.antiIdle, "Typed into remote sessions after that long without typing, so a device's idle timeout doesn't log them out. KiTTY's escapes: \\x05\\ is Ctrl+E, \\r Enter, \\k08 Backspace. Empty: none.");
            return row;
        }

        /// <summary>Open the colour picker for a colour preference, starting at its colour</summary>
        /// <param name="name">The preference's name</param>
        private void PickColour(string name)
        {
            using (ColorDialog dialog = new ColorDialog { FullOpen = true })
            {
                string current;
                if (this.colours.TryGetValue(name, out current) && TerminalPreferences.IsColour(current))
                {
                    dialog.Color = ColorTranslator.FromHtml(current);
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.SetColour(name, TerminalPreferences.ColourText(dialog.Color));
                }
            }
        }

        /// <summary>Keep a colour choice and show it in its swatch: the colour, or a crossed-out square for the theme's</summary>
        /// <param name="name">The preference's name</param>
        /// <param name="value">"#rrggbb", or "" for the theme's</param>
        private void SetColour(string name, string value)
        {
            this.colours[name] = TerminalPreferences.IsColour(value) ? value : string.Empty;
            PictureBox swatch = this.colourSwatches[name];
            Image old = swatch.Image;
            swatch.Image = Swatch(this.colours[name].Length > 0 ? ColorTranslator.FromHtml(this.colours[name]) : (Color?)null);
            old?.Dispose();
        }

        /// <summary>
        /// A small square of a colour with a grey edge, for a swatch; with no colour, an empty square crossed out (the
        /// theme's). An image, as the dark theme recolours a control's background.
        /// </summary>
        /// <param name="colour">The colour, or null for the theme's</param>
        /// <returns>A new bitmap; the swatch's owner disposes of it</returns>
        private static Image Swatch(Color? colour)
        {
            Bitmap bitmap = new Bitmap(14, 14);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                if (colour.HasValue)
                {
                    using (Brush fill = new SolidBrush(colour.Value))
                    {
                        g.FillRectangle(fill, 0, 0, 14, 14);
                    }
                }
                else
                {
                    g.DrawLine(Pens.Gray, 0, 13, 13, 0);
                }
                g.DrawRectangle(Pens.Gray, 0, 0, 13, 13);
            }
            return bitmap;
        }

        /// <summary>Dispose of the tooltips with the panel</summary>
        /// <param name="disposing">true when called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.tips.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>A number box followed by a note, as one row's control</summary>
        /// <param name="box">The number box</param>
        /// <param name="note">What the number means</param>
        /// <returns>The pair, laid out side by side</returns>
        private static Control WithNote(NumericUpDown box, string note)
        {
            FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            row.Controls.Add(box);
            row.Controls.Add(new Label { Text = note, AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            return row;
        }

        /// <summary>A drop-down that only takes its own choices</summary>
        /// <returns>The combo box</returns>
        private static ComboBox NewCombo()
        {
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        }

        /// <summary>Add a labelled row</summary>
        /// <param name="table">The table</param>
        /// <param name="label">The label's text</param>
        /// <param name="control">The control</param>
        private static void AddRow(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) }, 0, row);
            control.Margin = new Padding(3);
            if (control is ComboBox)
            {
                // the drop-downs fill the row, whatever the DPI
                control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            }
            table.Controls.Add(control, 1, row);
        }

        /// <summary>Fill a drop-down and select the choice with the value (or the first)</summary>
        /// <param name="combo">The drop-down</param>
        /// <param name="choices">(text, value) pairs</param>
        /// <param name="value">The value to select</param>
        private static void Fill(ComboBox combo, IEnumerable<KeyValuePair<string, string>> choices, string value)
        {
            combo.Items.Clear();
            foreach (KeyValuePair<string, string> choice in choices)
            {
                combo.Items.Add(new Choice { Text = choice.Key, Value = choice.Value });
            }
            Choice selected = combo.Items.OfType<Choice>().FirstOrDefault(c => c.Value == value);
            combo.SelectedItem = selected ?? combo.Items.OfType<Choice>().FirstOrDefault();
        }

        /// <summary>A (text, value) pair for <see cref="Fill"/></summary>
        /// <param name="text">The text shown</param>
        /// <param name="value">The value</param>
        /// <returns>The pair</returns>
        private static KeyValuePair<string, string> C(string text, string value)
        {
            return new KeyValuePair<string, string>(text, value);
        }

        /// <summary>
        /// Fill the highlighting drop-down with Auto, None and the rule sets offered with the Built-in box as it is; a
        /// choice no longer offered goes back to Auto, as SuperXTerminal's
        /// </summary>
        /// <param name="selected">The value to select</param>
        private void FillRuleSets(string selected)
        {
            JArray ruleSets, themes;
            // the box as it is now, saved or not
            OutputHighlighting.Load(out ruleSets, out themes, this.builtInRules.Checked);
            Fill(this.ruleSet, new[] { C("Auto (picked from the prompt)", "auto"), C("None", "none") }
                .Concat(OutputHighlighting.Offered(ruleSets).Select(r => C((string)r["name"] ?? (string)r["id"], (string)r["id"]))), selected);
            if ((this.ruleSet.SelectedItem as Choice)?.Value != selected)
            {
                this.ruleSet.SelectedItem = this.ruleSet.Items.OfType<Choice>().First(c => c.Value == "auto");
            }
        }

        /// <summary>Show the saved preferences, with the rule sets, themes and fonts there are to choose from</summary>
        public void LoadPreferences()
        {
            Dictionary<string, string> prefs = TerminalPreferences.All();
            JArray ruleSets, themes;
            OutputHighlighting.Load(out ruleSets, out themes);

            this.builtInRules.Checked = prefs["builtInRules"] != "false";
            this.FillRuleSets(prefs["ruleSet"]);
            Fill(this.theme, new[] { C("PuTTY (default)", ""), C("Follow SuperXPuTTY's look (Light or Dark)", "@look") }
                .Concat(themes.Select(t => (string)t["name"]).Where(n => n != "PuTTY").Select(n => C(n, n))), prefs["theme"]);
            Fill(this.fontFamily, new[] { C("From the theme", ""), C("From the PuTTY profile", "profile") }
                .Concat(TerminalControl.MonospaceFonts().Select(f => C(f, f))), prefs["fontFamily"]);
            int size;
            this.fontSize.Value = int.TryParse(prefs["fontSize"], out size) ? Math.Max(0, Math.Min(48, size)) : 0;
            Fill(this.cursorShape, new[] { C("From the PuTTY profile", "profile"), C("Block", "block"), C("Underline", "underline"), C("Vertical line", "bar") }, prefs["cursorShape"]);
            Fill(this.cursorBlink, new[] { C("From the PuTTY profile", "profile"), C("Blink", "on"), C("Don't blink", "off") }, prefs["cursorBlink"]);
            Fill(this.bell, new[] { C("From the PuTTY profile", "profile"), C("Flash", "visual"), C("Sound", "sound"), C("None", "none") }, prefs["bell"]);
            this.copyOnSelect.Checked = prefs["copyOnSelect"] != "false";
            this.markMatches.Checked = prefs["markMatches"] != "false";
            int pasteLines;
            this.pasteLines.Value = int.TryParse(prefs["pasteLines"], out pasteLines) ? Math.Max(0, Math.Min(10000, pasteLines)) : 0;
            int lines;
            this.scrollback.Value = int.TryParse(prefs["scrollback"], out lines) && lines > 0 ? Math.Min(lines, TerminalPreferences.UnlimitedScrollback) : 0;
            Fill(this.keys, new[] { C("Backspace, Home/End and F1-F12 from the PuTTY profile", "profile"), C("xterm's (as Windows Terminal)", "xterm") }, prefs["keys"]);
            int seconds;
            this.logTimestamps.Value = int.TryParse(prefs["logTimestamps"], out seconds) ? Math.Max(0, Math.Min(3600, seconds)) : 0;
            Fill(this.renderer, new[] { C("Fast (WebGL, uses the graphics card)", "webgl"), C("Compatible (slower with a lot of output)", "dom") }, prefs["renderer"]);
            this.wordSeparators.Text = prefs["wordSeparators"];
            this.wrap.Checked = prefs["wrap"] != "false";
            this.antiIdle.Text = prefs["antiIdle"];
            int idleSeconds;
            this.antiIdleSeconds.Value = int.TryParse(prefs["antiIdleSeconds"], out idleSeconds) && idleSeconds > 0 ? Math.Min(idleSeconds, 86400) : AntiIdle.DefaultSeconds;
            foreach (string[] pref in ColourPrefs)
            {
                this.SetColour(pref[0], prefs[pref[0]]);
            }
        }

        /// <summary>Save the choices; open terminal tabs apply them at once (TerminalPreferences.Changed)</summary>
        public void SavePreferences()
        {
            Func<ComboBox, string> value = combo => (combo.SelectedItem as Choice)?.Value;
            TerminalPreferences.SetMany(new Dictionary<string, string>
            {
                { "ruleSet", value(this.ruleSet) },
                { "builtInRules", this.builtInRules.Checked ? "true" : "false" },
                { "theme", value(this.theme) },
                { "fontFamily", value(this.fontFamily) },
                { "fontSize", ((int)this.fontSize.Value).ToString() },
                { "cursorShape", value(this.cursorShape) },
                { "cursorBlink", value(this.cursorBlink) },
                { "bell", value(this.bell) },
                { "copyOnSelect", this.copyOnSelect.Checked ? "true" : "false" },
                { "markMatches", this.markMatches.Checked ? "true" : "false" },
                { "pasteLines", ((int)this.pasteLines.Value).ToString() },
                { "scrollback", ((int)this.scrollback.Value).ToString() },
                { "keys", value(this.keys) },
                { "logTimestamps", ((int)this.logTimestamps.Value).ToString() },
                { "renderer", value(this.renderer) },
                { "wordSeparators", TerminalPreferences.NormalizeWordSeparators(this.wordSeparators.Text) },
                { "wrap", this.wrap.Checked ? "true" : "false" },
                { "antiIdle", this.antiIdle.Text },
                { "antiIdleSeconds", ((int)this.antiIdleSeconds.Value).ToString() },
                { "colorBackground", this.colours["colorBackground"] },
                { "colorForeground", this.colours["colorForeground"] },
                { "colorSelection", this.colours["colorSelection"] },
                { "colorCursor", this.colours["colorCursor"] },
            });
        }
    }
}
