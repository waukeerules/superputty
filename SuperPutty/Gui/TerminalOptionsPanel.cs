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
    /// font size, cursor, blinking, bell, copy on select and marking the selection's matches. These are the same
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
        private readonly ComboBox theme = NewCombo();
        private readonly ComboBox fontFamily = NewCombo();
        private readonly NumericUpDown fontSize = new NumericUpDown { Minimum = 0, Maximum = 48, Width = 80 };
        private readonly ComboBox cursorShape = NewCombo();
        private readonly ComboBox cursorBlink = NewCombo();
        private readonly ComboBox bell = NewCombo();
        private readonly CheckBox copyOnSelect = new CheckBox { Text = "Copy on select (as PuTTY does)", AutoSize = true };
        private readonly CheckBox markMatches = new CheckBox { Text = "Mark the other matches of the selected text (as Notepad++ does)", AutoSize = true };

        /// <summary>Lay out the rows; <see cref="LoadPreferences"/> fills them</summary>
        public TerminalOptionsPanel()
        {
            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(4)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(table, "Highlighting rules:", this.ruleSet);
            AddRow(table, "Theme:", this.theme);
            AddRow(table, "Font:", this.fontFamily);
            FlowLayoutPanel size = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            size.Controls.Add(this.fontSize);
            size.Controls.Add(new Label { Text = "pixels (0: the theme's or PuTTY profile's size)", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
            AddRow(table, "Font size:", size);
            AddRow(table, "Cursor:", this.cursorShape);
            AddRow(table, "Cursor blinks:", this.cursorBlink);
            AddRow(table, "Bell:", this.bell);
            table.Controls.Add(this.copyOnSelect, 1, table.RowCount++);
            table.Controls.Add(this.markMatches, 1, table.RowCount++);
            this.Controls.Add(table);
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

        /// <summary>Show the saved preferences, with the rule sets, themes and fonts there are to choose from</summary>
        public void LoadPreferences()
        {
            Dictionary<string, string> prefs = TerminalPreferences.All();
            JArray ruleSets, themes;
            OutputHighlighting.Load(out ruleSets, out themes);

            Fill(this.ruleSet, new[] { C("Auto (picked from the prompt)", "auto"), C("None", "none") }
                .Concat(ruleSets.Select(r => C((string)r["name"] ?? (string)r["id"], (string)r["id"]))), prefs["ruleSet"]);
            Fill(this.theme, new[] { C("PuTTY (default)", "") }
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
        }

        /// <summary>Save the choices; open terminal tabs apply them at once (TerminalPreferences.Changed)</summary>
        public void SavePreferences()
        {
            Func<ComboBox, string> value = combo => (combo.SelectedItem as Choice)?.Value;
            TerminalPreferences.SetMany(new Dictionary<string, string>
            {
                { "ruleSet", value(this.ruleSet) },
                { "theme", value(this.theme) },
                { "fontFamily", value(this.fontFamily) },
                { "fontSize", ((int)this.fontSize.Value).ToString() },
                { "cursorShape", value(this.cursorShape) },
                { "cursorBlink", value(this.cursorBlink) },
                { "bell", value(this.bell) },
                { "copyOnSelect", this.copyOnSelect.Checked ? "true" : "false" },
                { "markMatches", this.markMatches.Checked ? "true" : "false" },
            });
        }
    }
}
