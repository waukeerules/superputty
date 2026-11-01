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
    /// Edit sessions: change the same fields on many sessions at once, e.g. the username for every device at a site.
    /// Lists a folder's sessions (with its subfolders'), all ticked; only the fields ticked are changed. Opened from a
    /// folder's menu in SessionTreeview; the change is made by Data.SessionBulkEdit. Built in code like dlgDatabaseSettings.
    /// </summary>
    public class dlgBulkEdit : Form
    {
        /// <summary>The sessions, ticked to be changed</summary>
        private readonly CheckedListBox listSessions = new CheckedListBox { CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };

        /// <summary>The sessions in the order listed</summary>
        private readonly List<SessionData> sessions;

        /// <summary>One field: the box that says it's changed, and its editor</summary>
        private class Field
        {
            /// <summary>Ticked when the field is to be changed</summary>
            public CheckBox Use;

            /// <summary>The editor</summary>
            public Control Editor;
        }

        /// <summary>The fields offered</summary>
        private readonly Field username, profile, port, protocol, icon, color, extraArgs, jumpHost, highlightRules;

        /// <summary>The icon chosen, as a key</summary>
        private string iconKey;

        /// <summary>The colour choices: a name and "#RRGGBB" ("" for none)</summary>
        private static readonly KeyValuePair<string, string>[] ColorChoices =
            EnvironmentColors.Presets.Concat(new[] { new KeyValuePair<string, string>("None (use the folder's or a rule's)", "") }).ToArray();

        /// <summary>Build the dialog for a folder's sessions</summary>
        /// <param name="folderName">The folder's name, for the title</param>
        /// <param name="sessions">Its sessions, with those in its subfolders</param>
        public dlgBulkEdit(string folderName, List<SessionData> sessions)
        {
            this.sessions = sessions.OrderBy(s => s.SessionId, StringComparer.OrdinalIgnoreCase).ToList();
            this.Text = "Edit sessions in " + folderName;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Padding = new Padding(this.LogicalToDeviceUnits(12));
            this.ClientSize = new Size(this.LogicalToDeviceUnits(560), this.LogicalToDeviceUnits(560));
            this.MinimumSize = new Size(this.LogicalToDeviceUnits(460), this.LogicalToDeviceUnits(460));

            foreach (SessionData session in this.sessions)
            {
                string where = session.IsDatabase ? DatabaseSessions.TreePath(session.SessionId) : session.SessionId;
                this.listSessions.Items.Add(where + (string.IsNullOrEmpty(session.Host) ? "" : "   (" + session.Host + ")"), true);
            }

            TextBox textUsername = new TextBox();
            HintText.Set(textUsername, "e.g. admin, or {user}");
            ComboBox comboProfile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            comboProfile.Items.AddRange(PuttyDataHelper.GetSessionNames().Where(n => !string.IsNullOrEmpty(n)).ToArray());
            NumericUpDown numericPort = new NumericUpDown { Minimum = 0, Maximum = 65535, Value = 22 };
            ComboBox comboProtocol = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            comboProtocol.Items.AddRange(Enum.GetNames(typeof(ConnectionProtocol)));
            comboProtocol.SelectedItem = ConnectionProtocol.SSH.ToString();
            Button buttonIcon = new Button { Text = "Choose...", AutoSize = true };
            ComboBox comboColor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            comboColor.Items.AddRange(ColorChoices.Select(c => c.Key).ToArray());
            comboColor.SelectedIndex = 0;
            TextBox textArgs = new TextBox();

            TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Bottom, AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            this.username = this.AddField(grid, "Username", textUsername);
            this.profile = this.AddField(grid, "PuTTY profile", comboProfile);
            this.port = this.AddField(grid, "Port (0 = usual)", numericPort);
            this.protocol = this.AddField(grid, "Protocol", comboProtocol);
            this.icon = this.AddField(grid, "Icon", buttonIcon);
            this.color = this.AddField(grid, "Colour", comboColor);
            this.extraArgs = this.AddField(grid, "Extra args", textArgs);
            TextBox textJump = new TextBox();
            HintText.Set(textJump, "e.g. Bastions/bastion1, or admin@10.0.0.1:22 (empty for none)");
            this.jumpHost = this.AddField(grid, "Jump host", textJump);
            ComboBox comboRules = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Utils.OutputHighlighting.FillRuleChoices(comboRules, "The folder's or the default", null);
            this.highlightRules = this.AddField(grid, "Highlighting", comboRules);
            numericPort.Width = this.LogicalToDeviceUnits(90);
            buttonIcon.Anchor = AnchorStyles.Left;
            buttonIcon.Click += (s, e) =>
            {
                using (dlgIconPicker picker = new dlgIconPicker(this.iconKey))
                {
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        this.iconKey = picker.SelectedKey;
                        buttonIcon.Text = this.iconKey;
                        this.icon.Use.Checked = true;
                    }
                }
            };

            Label intro = new Label
            {
                Text = "Tick the fields to change; they're set on every ticked session. Fields left unticked keep each session's own value.",
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(this.LogicalToDeviceUnits(536), 0),
                Padding = new Padding(0, 0, 0, this.LogicalToDeviceUnits(6))
            };
            FlowLayoutPanel pick = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            Button all = new Button { Text = "All", AutoSize = true };
            Button none = new Button { Text = "None", AutoSize = true };
            all.Click += (s, e) => this.TickAll(true);
            none.Click += (s, e) => this.TickAll(false);
            pick.Controls.AddRange(new Control[] { all, none, new Label { Text = this.sessions.Count + " sessions", AutoSize = true, Margin = new Padding(8, 8, 0, 0) } });
            this.listSessions.Dock = DockStyle.Fill;

            FlowLayoutPanel close = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, this.LogicalToDeviceUnits(8), 0, 0) };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            Button ok = new Button { Text = "Change", AutoSize = true, MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0) };
            ok.Click += (s, e) => this.Save();
            close.Controls.AddRange(new Control[] { cancel, ok });
            this.AcceptButton = ok;
            this.CancelButton = cancel;

            this.Controls.Add(this.listSessions);
            this.Controls.Add(pick);
            this.Controls.Add(intro);
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

        /// <summary>Add a field's row: a box to tick, and its editor, which ticks the box when it's changed</summary>
        /// <param name="grid">The layout</param>
        /// <param name="label">The field's name</param>
        /// <param name="editor">The editor</param>
        /// <returns>The field</returns>
        private Field AddField(TableLayoutPanel grid, string label, Control editor)
        {
            Field field = new Field { Use = new CheckBox { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, Editor = editor };
            if (!(editor is Button))
            {
                editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            }
            EventHandler tick = (s, e) => field.Use.Checked = true;
            if (editor is NumericUpDown)
                ((NumericUpDown)editor).ValueChanged += tick;
            else if (editor is ComboBox)
            {
                ((ComboBox)editor).SelectedIndexChanged += tick;
                ((ComboBox)editor).TextUpdate += tick;
            }
            else if (!(editor is Button))
                editor.TextChanged += tick;
            int row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(field.Use, 0, row);
            grid.Controls.Add(editor, 1, row);
            return field;
        }

        /// <summary>Tick or untick every session</summary>
        /// <param name="ticked">Whether to tick</param>
        private void TickAll(bool ticked)
        {
            for (int i = 0; i < this.listSessions.Items.Count; i++)
            {
                this.listSessions.SetItemChecked(i, ticked);
            }
        }

        /// <summary>The change from the ticked fields</summary>
        /// <returns>The change; empty if no field is ticked</returns>
        private SessionBulkEdit.Change ReadChange()
        {
            SessionBulkEdit.Change change = new SessionBulkEdit.Change();
            if (this.username.Use.Checked) change.Username = this.username.Editor.Text.Trim();
            if (this.profile.Use.Checked) change.PuttyProfile = this.profile.Editor.Text;
            if (this.port.Use.Checked) change.Port = (int)((NumericUpDown)this.port.Editor).Value;
            if (this.protocol.Use.Checked) change.Protocol = (ConnectionProtocol)Enum.Parse(typeof(ConnectionProtocol), (string)((ComboBox)this.protocol.Editor).SelectedItem);
            if (this.icon.Use.Checked) change.Icon = this.iconKey ?? string.Empty;
            if (this.color.Use.Checked) change.Color = ColorChoices[((ComboBox)this.color.Editor).SelectedIndex].Value;
            if (this.extraArgs.Use.Checked) change.ExtraArgs = this.extraArgs.Editor.Text;
            if (this.jumpHost.Use.Checked) change.JumpHost = this.jumpHost.Editor.Text.Trim();
            if (this.highlightRules.Use.Checked) change.HighlightRules = Utils.OutputHighlighting.SelectedRuleChoice((ComboBox)this.highlightRules.Editor);
            return change;
        }

        /// <summary>Make the change to the ticked sessions, and say what happened</summary>
        private void Save()
        {
            SessionBulkEdit.Change change = this.ReadChange();
            List<SessionData> chosen = this.listSessions.CheckedIndices.Cast<int>().Select(i => this.sessions[i]).ToList();
            if (change.Columns().Count == 0 || chosen.Count == 0)
            {
                Messenger.MessageBox(chosen.Count == 0 ? "Tick at least one session." : "Tick at least one field to change.",
                    this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            SessionBulkEdit.Result result;
            try
            {
                result = SessionBulkEdit.Apply(chosen, change);
            }
            finally
            {
                Cursor.Current = previous;
            }
            EnvironmentColors.RaiseChanged();
            SuperPuTTY.ReportStatus("Changed {0} on {1} sessions{2}", string.Join(", ", change.Columns()).Replace("_", " "), result.Changed,
                result.Skipped.Count > 0 ? string.Format(" ({0} left alone)", result.Skipped.Count) : "");
            if (result.Skipped.Count > 0)
            {
                Messenger.MessageBox("Changed " + result.Changed + " sessions. These were left alone:\n\n" + string.Join("\n", result.Skipped.Take(15))
                    + (result.Skipped.Count > 15 ? "\n... and " + (result.Skipped.Count - 15) + " more" : ""),
                    this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
