using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Data;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Edits the current user's local overrides for one session (Data.SessionOverrides): a host, port, username,
    /// PuTTY profile, extra arguments or jump host used instead of the stored one whenever the session opens, without
    /// changing the session. Each field has a box to tick, and shows the stored value beside it. Opened from the
    /// Sessions tree's "Local overrides...", which saves the result. Built in code like dlgFolderSettings.
    /// </summary>
    public class dlgSessionOverrides : Form
    {
        /// <summary>One field: its tick box and its editor</summary>
        private class Row
        {
            /// <summary>Ticked to use the user's value</summary>
            public CheckBox Use;

            /// <summary>The user's value</summary>
            public Control Editor;
        }

        private readonly TextBox textHost = new TextBox();
        private readonly NumericUpDown numericPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 22 };
        private readonly TextBox textUsername = new TextBox();
        private readonly ComboBox comboProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly TextBox textExtraArgs = new TextBox();
        private readonly TextBox textJumpHost = new TextBox();

        /// <summary>The rows, by field</summary>
        private readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();

        /// <summary>The overrides as edited, read after OK; null when there are none</summary>
        public SessionOverrides.Override Result { get; private set; }

        /// <summary>Build the dialog for a session</summary>
        /// <param name="session">The stored session, for its name and values; it isn't changed</param>
        /// <param name="current">Its overrides now; null if it has none</param>
        public dlgSessionOverrides(SessionData session, SessionOverrides.Override current)
        {
            current = current ?? new SessionOverrides.Override();
            this.Text = "Local overrides: " + session.SessionName;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = new Padding(this.LogicalToDeviceUnits(12));

            this.comboProfile.Items.AddRange(PuttyDataHelper.GetSessionNames().Where(n => !string.IsNullOrEmpty(n)).ToArray());
            this.textHost.Text = current.Host ?? session.Host;
            this.numericPort.Value = Math.Max(1, Math.Min(65535, current.Port > 0 ? current.Port : session.Port > 0 ? session.Port : 22));
            this.textUsername.Text = current.Username ?? session.Username;
            this.comboProfile.Text = current.PuttyProfile ?? session.PuttySession;
            this.textExtraArgs.Text = current.ExtraArgs ?? session.ExtraArgs;
            this.textJumpHost.Text = current.JumpHost ?? session.JumpHost;
            HintText.Set(this.textJumpHost, "empty: no jump host, even if the session or folder has one");
            foreach (Control box in new Control[] { this.textHost, this.textUsername, this.comboProfile, this.textExtraArgs, this.textJumpHost })
            {
                box.Width = this.LogicalToDeviceUnits(280);
            }
            this.numericPort.Width = this.LogicalToDeviceUnits(90);

            TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
            Label intro = new Label
            {
                Text = "Tick a field to use your own value whenever this session opens, instead of the stored one. Only you get "
                    + "these (they're kept beside your settings, not in the session), so they work for locked database fields "
                    + "and shared sessions. The session itself isn't changed.",
                AutoSize = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(560), 0),
                Margin = new Padding(0, 0, 0, this.LogicalToDeviceUnits(8))
            };
            grid.Controls.Add(intro, 0, 0);
            grid.SetColumnSpan(intro, 3);
            this.AddRow(grid, 1, "Host", this.textHost, current.Host != null, session.Host);
            this.AddRow(grid, 2, "Port", this.numericPort, current.Port > 0, session.Port.ToString());
            this.AddRow(grid, 3, "Username", this.textUsername, current.Username != null, session.Username);
            this.AddRow(grid, 4, "PuTTY profile", this.comboProfile, current.PuttyProfile != null, session.PuttySession);
            // the stored extra arguments can hold a -pw password: never shown
            this.AddRow(grid, 5, "Extra args", this.textExtraArgs, current.ExtraArgs != null,
                System.Text.RegularExpressions.Regex.Replace(session.ExtraArgs ?? string.Empty, @"(-pw\s+)(""[^""]*""|\S+)", "$1****"));
            this.AddRow(grid, 6, "Jump host", this.textJumpHost, current.JumpHost != null, session.JumpHost);

            FlowLayoutPanel close = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, this.LogicalToDeviceUnits(8), 0, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            Button clear = new Button { Text = "Clear all", AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            ok.Click += (s, e) => this.ReadBack();
            clear.Click += (s, e) => { foreach (Row row in this.rows.Values) row.Use.Checked = false; };
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

        /// <summary>Add a field as a row: its tick box, its editor (enabled while ticked) and the stored value</summary>
        /// <param name="grid">The layout</param>
        /// <param name="row">The row</param>
        /// <param name="field">The field's name</param>
        /// <param name="editor">The editor</param>
        /// <param name="overridden">Whether the user has a value for it now</param>
        /// <param name="stored">The session's own value, shown beside it</param>
        private void AddRow(TableLayoutPanel grid, int row, string field, Control editor, bool overridden, string stored)
        {
            CheckBox use = new CheckBox { Text = field, AutoSize = true, Checked = overridden, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
            editor.Enabled = overridden;
            use.CheckedChanged += (s, e) => editor.Enabled = use.Checked;
            Label storedLabel = new Label
            {
                Text = "Stored: " + (string.IsNullOrEmpty(stored) ? "(none)" : stored),
                AutoSize = true,
                AutoEllipsis = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(220), 0),
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 6, 0, 0)
            };
            grid.Controls.Add(use, 0, row);
            grid.Controls.Add(editor, 1, row);
            grid.Controls.Add(storedLabel, 2, row);
            this.rows[field] = new Row { Use = use, Editor = editor };
        }

        /// <summary>Put the ticked fields in <see cref="Result"/>; none ticked leaves it null</summary>
        private void ReadBack()
        {
            SessionOverrides.Override result = new SessionOverrides.Override();
            if (this.rows["Host"].Use.Checked) result.Host = this.textHost.Text.Trim();
            if (this.rows["Port"].Use.Checked) result.Port = (int)this.numericPort.Value;
            if (this.rows["Username"].Use.Checked) result.Username = this.textUsername.Text.Trim();
            if (this.rows["PuTTY profile"].Use.Checked) result.PuttyProfile = this.comboProfile.Text.Trim();
            if (this.rows["Extra args"].Use.Checked) result.ExtraArgs = this.textExtraArgs.Text.Trim();
            if (this.rows["Jump host"].Use.Checked) result.JumpHost = this.textJumpHost.Text.Trim();
            this.Result = result.IsEmpty ? null : result;
        }
    }
}
