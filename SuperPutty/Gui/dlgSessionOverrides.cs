using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Data;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Edits the current user's local overrides (Data.SessionOverrides) for one session or several: a host, port,
    /// username, PuTTY profile, extra arguments or jump host used instead of the stored one whenever a session opens,
    /// without changing it. Each field has a box to tick, and shows the stored value beside it. For several sessions
    /// the box has three states: ticked sets the value on all of them, unticked removes that override from all, and
    /// filled (where they differ) leaves each as it is. <see cref="Merge"/> turns the choices into one session's new
    /// overrides. Opened from the Sessions tree's "Local overrides..." and Edit Session's Local overrides button,
    /// which save the result. Built in code like dlgFolderSettings.
    /// </summary>
    public class dlgSessionOverrides : Form
    {
        /// <summary>One overridable field: how to read and write it, and its row's controls</summary>
        private class Field
        {
            /// <summary>Its name, as SessionOverrides.Override.Fields gives it</summary>
            public string Name;

            /// <summary>Its editor</summary>
            public Control Editor;

            /// <summary>An override's value for it, as text; null when not overridden</summary>
            public Func<SessionOverrides.Override, string> Get;

            /// <summary>Put a value (or null to remove the override) into an override</summary>
            public Action<SessionOverrides.Override, string> Set;

            /// <summary>A session's stored value, as text</summary>
            public Func<SessionData, string> Stored;

            /// <summary>Ticked to set the value, unticked to remove it, filled to leave each session as it is</summary>
            public CheckBox Use;
        }

        private readonly TextBox textHost = new TextBox();
        private readonly NumericUpDown numericPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 22 };
        private readonly TextBox textUsername = new TextBox();
        private readonly ComboBox comboProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly TextBox textExtraArgs = new TextBox();
        private readonly TextBox textJumpHost = new TextBox();

        /// <summary>The fields, in the order shown</summary>
        private readonly List<Field> fields;

        /// <summary>The overrides as edited, read after OK, for a single session; null when there are none</summary>
        public SessionOverrides.Override Result
        {
            get { return this.Merge(null); }
        }

        /// <summary>Build the dialog for a session</summary>
        /// <param name="session">The stored session, for its name and values; it isn't changed</param>
        /// <param name="current">Its overrides now; null if it has none</param>
        public dlgSessionOverrides(SessionData session, SessionOverrides.Override current)
            : this(new List<SessionData> { session }, new List<SessionOverrides.Override> { current })
        {
        }

        /// <summary>Build the dialog for one session or several</summary>
        /// <param name="sessions">The stored sessions, for their names and values; they aren't changed</param>
        /// <param name="current">Their overrides now, in the same order; null for one that has none</param>
        public dlgSessionOverrides(List<SessionData> sessions, List<SessionOverrides.Override> current)
        {
            bool several = sessions.Count > 1;
            this.Text = several ? string.Format("Local overrides: {0} sessions", sessions.Count) : "Local overrides: " + sessions[0].SessionName;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = new Padding(this.LogicalToDeviceUnits(12));

            this.comboProfile.Items.AddRange(PuttyDataHelper.GetSessionNames().Where(n => !string.IsNullOrEmpty(n)).ToArray());
            HintText.Set(this.textJumpHost, "empty: no jump host, even if the session or folder has one");
            foreach (Control box in new Control[] { this.textHost, this.textUsername, this.comboProfile, this.textExtraArgs, this.textJumpHost })
            {
                box.Width = this.LogicalToDeviceUnits(280);
            }
            this.numericPort.Width = this.LogicalToDeviceUnits(90);

            this.fields = new List<Field>
            {
                new Field { Name = "Host", Editor = this.textHost, Get = o => o.Host, Set = (o, v) => o.Host = v, Stored = s => s.Host },
                new Field { Name = "Port", Editor = this.numericPort, Get = o => o.Port > 0 ? o.Port.ToString() : null,
                    Set = (o, v) => o.Port = v == null ? 0 : int.Parse(v), Stored = s => s.Port.ToString() },
                new Field { Name = "Username", Editor = this.textUsername, Get = o => o.Username, Set = (o, v) => o.Username = v, Stored = s => s.Username },
                new Field { Name = "PuTTY profile", Editor = this.comboProfile, Get = o => o.PuttyProfile, Set = (o, v) => o.PuttyProfile = v, Stored = s => s.PuttySession },
                // a -pw password in the stored extra arguments is never shown
                new Field { Name = "Extra args", Editor = this.textExtraArgs, Get = o => o.ExtraArgs, Set = (o, v) => o.ExtraArgs = v,
                    Stored = s => Regex.Replace(s.ExtraArgs ?? string.Empty, @"(-pw\s+)(""[^""]*""|\S+)", "$1****") },
                new Field { Name = "Jump host", Editor = this.textJumpHost, Get = o => o.JumpHost, Set = (o, v) => o.JumpHost = v, Stored = s => s.JumpHost }
            };

            TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
            Label intro = new Label
            {
                Text = (several
                        ? string.Format("For these {0} sessions: a ticked field is set on all of them, an unticked one is removed from all, "
                            + "and a filled box (where they differ) leaves each as it is. ", sessions.Count)
                        : "Tick a field to use your own value whenever this session opens, instead of the stored one. ")
                    + "Only you get these (they're kept beside your settings, not in the session), so they work for locked database "
                    + "fields and shared sessions. The sessions themselves aren't changed.",
                AutoSize = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(560), 0),
                Margin = new Padding(0, 0, 0, this.LogicalToDeviceUnits(8))
            };
            grid.Controls.Add(intro, 0, 0);
            grid.SetColumnSpan(intro, 3);
            List<SessionOverrides.Override> overrides = current.Select(o => o ?? new SessionOverrides.Override()).ToList();
            for (int i = 0; i < this.fields.Count; i++)
            {
                this.AddRow(grid, i + 1, this.fields[i], sessions, overrides, several);
            }

            FlowLayoutPanel close = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, this.LogicalToDeviceUnits(8), 0, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            Button clear = new Button { Text = "Clear all", AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            clear.Click += (s, e) => { foreach (Field field in this.fields) field.Use.CheckState = CheckState.Unchecked; };
            close.Controls.AddRange(new Control[] { cancel, ok, clear });
            this.AcceptButton = ok;
            this.CancelButton = cancel;
            this.Controls.Add(grid);
            this.Controls.Add(close);

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>
        /// Add a field as a row: its tick box (three states for several sessions), its editor (enabled while ticked)
        /// and the stored value. The box starts ticked when every session has the same override, unticked when none has
        /// one, and filled otherwise; the editor starts with the shared override, or else the first session's value.
        /// </summary>
        /// <param name="grid">The layout</param>
        /// <param name="row">The row</param>
        /// <param name="field">The field</param>
        /// <param name="sessions">The sessions</param>
        /// <param name="overrides">Their overrides, in the same order</param>
        /// <param name="several">Whether there's more than one session</param>
        private void AddRow(TableLayoutPanel grid, int row, Field field, List<SessionData> sessions, List<SessionOverrides.Override> overrides, bool several)
        {
            List<string> values = overrides.Select(field.Get).ToList();
            List<string> stored = sessions.Select(field.Stored).Distinct().ToList();
            CheckState state = values.All(v => v == null) ? CheckState.Unchecked
                : values.All(v => v != null && v == values[0]) ? CheckState.Checked
                : CheckState.Indeterminate;
            SetEditorText(field.Editor, values.FirstOrDefault(v => v != null) ?? field.Stored(sessions[0]));

            field.Use = new CheckBox
            {
                Text = field.Name,
                AutoSize = true,
                ThreeState = several,
                CheckState = state,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            };
            field.Editor.Enabled = state == CheckState.Checked;
            field.Use.CheckStateChanged += (s, e) => field.Editor.Enabled = field.Use.CheckState == CheckState.Checked;
            Label storedLabel = new Label
            {
                Text = "Stored: " + (stored.Count > 1 ? "(varies)" : string.IsNullOrEmpty(stored[0]) ? "(none)" : stored[0]),
                AutoSize = true,
                AutoEllipsis = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(220), 0),
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 6, 0, 0)
            };
            grid.Controls.Add(field.Use, 0, row);
            grid.Controls.Add(field.Editor, 1, row);
            grid.Controls.Add(storedLabel, 2, row);
        }

        /// <summary>Put a value in an editor: the port box takes a number, the others text</summary>
        /// <param name="editor">The editor</param>
        /// <param name="value">The value; an unreadable port leaves 22</param>
        private static void SetEditorText(Control editor, string value)
        {
            NumericUpDown number = editor as NumericUpDown;
            int port;
            if (number != null)
                number.Value = int.TryParse(value, out port) ? Math.Max(1, Math.Min(65535, port)) : 22;
            else
                editor.Text = value ?? string.Empty;
        }

        /// <summary>
        /// One session's overrides after the choices made here: a ticked field gets the value, an unticked one is
        /// removed, and a filled one keeps the session's own
        /// </summary>
        /// <param name="current">The session's overrides now; null if it has none</param>
        /// <returns>Its new overrides; null when none are left</returns>
        public SessionOverrides.Override Merge(SessionOverrides.Override current)
        {
            SessionOverrides.Override result = current == null ? new SessionOverrides.Override() : current.Clone();
            foreach (Field field in this.fields)
            {
                if (field.Use.CheckState == CheckState.Checked)
                    field.Set(result, field.Editor is NumericUpDown ? ((int)((NumericUpDown)field.Editor).Value).ToString() : field.Editor.Text.Trim());
                else if (field.Use.CheckState == CheckState.Unchecked)
                    field.Set(result, null);
            }
            return result.IsEmpty ? null : result;
        }
    }
}
