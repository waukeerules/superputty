using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DarkModeForms;

namespace SuperPutty.Utils
{
    /// <summary>
    /// Values asked for when a snippet is sent, so one button works for any port or VLAN: "interface {prompt:Interface}"
    /// asks for Interface and sends "interface Gi1/0/5". {prompt:Name=default} offers a default; {secret:Name} is typed
    /// masked and never remembered or logged. Each name is asked once however often it appears, and the last value
    /// given is offered next time. Gui.ButtonBarStrip.RunButton and frmSuperPutty's Send Commands toolbar call
    /// <see cref="Resolve"/> after SessionVariables has filled {key} placeholders (whose names have no colon, so the
    /// two never mix) and before SendStringParser.
    /// </summary>
    public static class SnippetPrompts
    {
        /// <summary>{prompt:Name}, {prompt:Name=default} or {secret:Name}</summary>
        private static readonly Regex Pattern = new Regex(@"\{(?<kind>prompt|secret):(?<name>[^{}=]+?)(=(?<default>[^{}]*))?\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The last value given for each name this run (not secrets), offered next time</summary>
        private static readonly Dictionary<string, string> lastValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>One value to ask for</summary>
        public class Field
        {
            /// <summary>Its name, as shown, e.g. "Interface"</summary>
            public string Name { get; set; }

            /// <summary>The default from {prompt:Name=default}, or null</summary>
            public string Default { get; set; }

            /// <summary>Whether it's typed masked ({secret:...})</summary>
            public bool Secret { get; set; }
        }

        /// <summary>The values a text asks for, each name once, in the order they first appear</summary>
        /// <param name="text">The snippet</param>
        /// <returns>The fields; empty if it asks for none</returns>
        public static List<Field> Find(string text)
        {
            List<Field> fields = new List<Field>();
            foreach (Match match in Pattern.Matches(text ?? string.Empty))
            {
                string name = match.Groups["name"].Value.Trim();
                Field existing = fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    fields.Add(new Field
                    {
                        Name = name,
                        Default = match.Groups["default"].Success ? match.Groups["default"].Value : null,
                        Secret = match.Groups["kind"].Value.Equals("secret", StringComparison.OrdinalIgnoreCase)
                    });
                }
                else if (match.Groups["kind"].Value.Equals("secret", StringComparison.OrdinalIgnoreCase))
                {
                    // masked if any use of the name is secret
                    existing.Secret = true;
                }
            }
            return fields;
        }

        /// <summary>Put the values in place of every {prompt:...} and {secret:...}</summary>
        /// <param name="text">The snippet</param>
        /// <param name="values">The values, by name (case-insensitive)</param>
        /// <returns>The text to send; a name without a value is left as written</returns>
        public static string Fill(string text, IDictionary<string, string> values)
        {
            Dictionary<string, string> byName = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            return Pattern.Replace(text ?? string.Empty, match =>
            {
                string value;
                return byName.TryGetValue(match.Groups["name"].Value.Trim(), out value) ? value : match.Value;
            });
        }

        /// <summary>
        /// Ask for a snippet's values, if it has any, and return the text with them filled in
        /// </summary>
        /// <param name="text">The snippet</param>
        /// <param name="owner">The window the question belongs to</param>
        /// <param name="title">What's being sent, e.g. "Button \"Shut port\""</param>
        /// <returns>The text to send; null if the user cancelled</returns>
        public static string Resolve(string text, IWin32Window owner, string title)
        {
            List<Field> fields = Find(text);
            if (fields.Count == 0)
                return text;
            Dictionary<string, string> values = Ask(fields, owner, title);
            if (values == null)
                return null;
            foreach (Field field in fields.Where(f => !f.Secret))
            {
                lastValues[field.Name] = values[field.Name];
            }
            return Fill(text, values);
        }

        /// <summary>Show one small form with a box per field</summary>
        /// <param name="fields">The fields</param>
        /// <param name="owner">The owner window</param>
        /// <param name="title">The form's title</param>
        /// <returns>The values by name, or null if cancelled</returns>
        private static Dictionary<string, string> Ask(List<Field> fields, IWin32Window owner, string title)
        {
            using (Form form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            })
            {
                form.Padding = new Padding(form.LogicalToDeviceUnits(12));
                TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
                List<TextBox> boxes = new List<TextBox>();
                foreach (Field field in fields)
                {
                    string last;
                    TextBox box = new TextBox
                    {
                        Width = form.LogicalToDeviceUnits(260),
                        UseSystemPasswordChar = field.Secret,
                        Text = !field.Secret && lastValues.TryGetValue(field.Name, out last) ? last : field.Default ?? string.Empty
                    };
                    grid.Controls.Add(new Label { Text = field.Name + ":", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, boxes.Count);
                    grid.Controls.Add(box, 1, boxes.Count);
                    boxes.Add(box);
                }
                FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(0, form.LogicalToDeviceUnits(8), 0, 0) };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(form.LogicalToDeviceUnits(80), 0) };
                Button ok = new Button { Text = "Send", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(form.LogicalToDeviceUnits(80), 0) };
                buttons.Controls.AddRange(new Control[] { cancel, ok });
                form.Controls.Add(grid);
                form.Controls.Add(buttons);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.Shown += (s, e) => { boxes[0].Focus(); boxes[0].SelectAll(); };
                if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
                {
                    new DarkModeCS(form)
                    {
                        ColorMode = DarkModeCS.DisplayMode.DarkMode,
                        ColorizeIcons = false
                    };
                }
                if (form.ShowDialog(owner) != DialogResult.OK)
                    return null;
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < fields.Count; i++)
                {
                    values[fields[i].Name] = boxes[i].Text;
                }
                return values;
            }
        }
    }
}
