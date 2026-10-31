using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using DarkModeForms;
using SuperPutty.Data;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Settings for sessions shared through a Postgres table (DatabaseSessions): the connection, the database, schema
    /// and table, read-only, plus Test connection and Create / upgrade schema, which never run on their own. Opened from
    /// Tools -> Database sessions... and the Database sessions root's menu; OK saves the Settings.Database* values and
    /// restarts DatabaseSessions. Built in code rather than with the designer, as a simple two-column form.
    /// </summary>
    public class dlgDatabaseSettings : Form
    {
        /// <summary>Turns database sessions on</summary>
        private readonly CheckBox checkEnabled = new CheckBox { Text = "Show sessions from a PostgreSQL table", AutoSize = true };

        /// <summary>Server name or address</summary>
        private readonly TextBox textHost = new TextBox();

        /// <summary>Server port</summary>
        private readonly NumericUpDown numericPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 5432 };

        /// <summary>Database name</summary>
        private readonly TextBox textDatabase = new TextBox();

        /// <summary>Schema name</summary>
        private readonly TextBox textSchema = new TextBox();

        /// <summary>Table name</summary>
        private readonly TextBox textTable = new TextBox();

        /// <summary>Login name</summary>
        private readonly TextBox textUser = new TextBox();

        /// <summary>Password, or a {key} placeholder</summary>
        private readonly TextBox textPassword = new TextBox();

        /// <summary>Who this user is in the table's updated_by; required, since a team often shares one login</summary>
        private readonly TextBox textNickname = new TextBox();

        /// <summary>SSL mode</summary>
        private readonly ComboBox comboSsl = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        /// <summary>How often to check the table for changes made by others, in seconds; 0 never</summary>
        private readonly NumericUpDown numericCheck = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = 30 };

        /// <summary>Never create or change anything</summary>
        private readonly CheckBox checkReadOnly = new CheckBox { Text = "Read only: never create or change anything in the database", AutoSize = true };

        /// <summary>Connect and look at the table</summary>
        private readonly Button buttonTest = new Button { Text = "Test connection", AutoSize = true };

        /// <summary>Create the database, schema and table, or add what's missing</summary>
        private readonly Button buttonSchema = new Button { Text = "Create / upgrade schema", AutoSize = true, Enabled = false };

        /// <summary>What Test connection or Create found</summary>
        private readonly Label labelResult = new Label { AutoSize = true };

        /// <summary>Saves and closes</summary>
        private readonly Button buttonOK = new Button { Text = "OK", DialogResult = DialogResult.None, AutoSize = true };

        /// <summary>Closes without saving</summary>
        private readonly Button buttonCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

        /// <summary>The password as stored when the dialog opened (encrypted, or a placeholder)</summary>
        private readonly string storedPassword;

        /// <summary>Whether the password box was typed in; otherwise the stored password is kept</summary>
        private bool passwordChanged;

        /// <summary>The layout holding the fields and the Test and Create buttons</summary>
        private readonly TableLayoutPanel grid;

        /// <summary>Holds <see cref="grid"/>, filling the window above OK and Cancel; scrolls if the window is too short</summary>
        private readonly Panel fields;

        /// <summary>OK and Cancel, docked to the bottom</summary>
        private readonly FlowLayoutPanel closeButtons;

        /// <summary>SSL modes offered, as Npgsql names them</summary>
        private static readonly string[] SslModes = { "Disable", "Allow", "Prefer", "Require", "VerifyCA", "VerifyFull" };

        /// <summary>Build the dialog and fill it from the settings</summary>
        public dlgDatabaseSettings()
        {
            this.Text = "Database sessions";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Padding = new Padding(12);
            this.AcceptButton = this.buttonOK;
            this.CancelButton = this.buttonCancel;

            TableLayoutPanel grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(12, 12) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.comboSsl.Items.AddRange(SslModes);
            // sizes are given for 100% and converted for the monitor: a form built in code isn't scaled by WinForms,
            // and at 150% fixed pixel sizes (the 88 x 23 OK button) were too small for their text
            foreach (TextBox box in new[] { this.textHost, this.textDatabase, this.textSchema, this.textTable, this.textUser, this.textPassword, this.textNickname })
            {
                box.Width = this.LogicalToDeviceUnits(300);
            }
            this.numericPort.Width = this.LogicalToDeviceUnits(80);
            this.numericCheck.Width = this.LogicalToDeviceUnits(80);
            this.comboSsl.Width = this.LogicalToDeviceUnits(140);
            this.labelResult.MaximumSize = new Size(this.LogicalToDeviceUnits(460), 0);
            foreach (Button button in new[] { this.buttonOK, this.buttonCancel })
            {
                button.MinimumSize = new Size(this.LogicalToDeviceUnits(88), 0);
            }

            AddRow(grid, null, this.checkEnabled);
            AddRow(grid, "Host:", this.textHost);
            AddRow(grid, "Port:", this.numericPort);
            AddRow(grid, "Database:", this.textDatabase);
            AddRow(grid, "Schema:", this.textSchema);
            AddRow(grid, "Table:", this.textTable);
            AddRow(grid, "Username:", this.textUser);
            AddRow(grid, "Password:", this.textPassword);
            AddRow(grid, "Nickname:", this.textNickname);
            AddRow(grid, "SSL:", this.comboSsl);
            FlowLayoutPanel check = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            check.Controls.Add(this.numericCheck);
            check.Controls.Add(new Label { Text = "seconds (0 = never)", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
            AddRow(grid, "Check every:", check);
            AddRow(grid, null, this.checkReadOnly);
            FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            actions.Controls.AddRange(new Control[] { this.buttonTest, this.buttonSchema });
            AddRow(grid, null, actions);
            AddRow(grid, null, this.labelResult);
            // OK and Cancel docked to the bottom of the window, so they show even if the window comes out too short
            // for the fields (which then scroll): sizing the window to its contents alone still cut them off on
            // some displays
            FlowLayoutPanel close = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Padding = new Padding(0, 12, 0, 0)
            };
            close.Controls.AddRange(new Control[] { this.buttonCancel, this.buttonOK });
            this.closeButtons = close;
            this.fields = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            grid.Location = new Point(0, 0);
            this.fields.Controls.Add(grid);
            this.grid = grid;
            this.Controls.Add(this.fields);
            this.Controls.Add(close);

            ToolTip tips = new ToolTip();
            tips.SetToolTip(this.textPassword, "Saved encrypted for your Windows user. Or type a {key} placeholder from Tools > Environment Variables.");
            tips.SetToolTip(this.buttonSchema, "Creates the database, schema and table if they're missing, and adds missing columns and the change triggers. Safe to run again.");
            tips.SetToolTip(this.textSchema, "The table's schema; created with the table if missing");
            tips.SetToolTip(this.textNickname, "Shown as who last changed a session, so people sharing one database login can be told apart");
            tips.SetToolTip(this.numericCheck, "How often to ask the table whether someone else (or a script) changed it; the sessions are only read again when it did");
            HintText.Set(this.textHost, "e.g. 192.168.3.2");
            HintText.Set(this.textUser, "e.g. postgres");
            HintText.Set(this.textNickname, "e.g. Tim (required)");

            this.checkEnabled.Checked = SuperPuTTY.Settings.DatabaseEnabled;
            this.textHost.Text = SuperPuTTY.Settings.DatabaseHost;
            this.numericPort.Value = Math.Max(1, Math.Min(65535, SuperPuTTY.Settings.DatabasePort));
            this.textDatabase.Text = SuperPuTTY.Settings.DatabaseName;
            this.textSchema.Text = SuperPuTTY.Settings.DatabaseSchema;
            this.textTable.Text = SuperPuTTY.Settings.DatabaseTable;
            this.textUser.Text = SuperPuTTY.Settings.DatabaseUser;
            this.storedPassword = SuperPuTTY.Settings.DatabasePassword ?? string.Empty;
            if (this.storedPassword.StartsWith("dpapi:", StringComparison.Ordinal))
            {
                // the saved password isn't shown; leaving the box empty keeps it
                HintText.Set(this.textPassword, "saved (type to replace)");
            }
            else
            {
                this.textPassword.Text = this.storedPassword;
                HintText.Set(this.textPassword, "password, or {key}");
            }
            this.ShowPasswordMasked();
            this.comboSsl.SelectedItem = Array.Find(SslModes, m => string.Equals(m, SuperPuTTY.Settings.DatabaseSslMode, StringComparison.OrdinalIgnoreCase)) ?? "Prefer";
            this.checkReadOnly.Checked = SuperPuTTY.Settings.DatabaseReadOnly;
            this.textNickname.Text = SuperPuTTY.Settings.DatabaseNickname;
            this.numericCheck.Value = Math.Max(0, Math.Min(3600, SuperPuTTY.Settings.DatabaseCheckSeconds));

            this.textPassword.TextChanged += (s, e) => { this.passwordChanged = true; this.ShowPasswordMasked(); };
            this.checkReadOnly.CheckedChanged += (s, e) => this.buttonSchema.Enabled = false;
            this.buttonTest.Click += (s, e) => this.Test();
            this.buttonSchema.Click += (s, e) => this.CreateSchema();
            this.buttonOK.Click += (s, e) => this.Save();

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
        /// Size the window to its contents once they're laid out, themed and scaled for the display. The form's own
        /// AutoSize cut off the OK and Cancel row at some display scalings.
        /// </summary>
        /// <param name="e">Not used</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.FitToContents();
        }

        /// <summary>Fit again once shown, after the window has been placed on its monitor</summary>
        /// <param name="e">Not used</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.FitToContents();
        }

        /// <summary>
        /// Fit again when the window moves to a monitor with another scaling: Windows resizes the window there, and
        /// the contents then no longer match its size
        /// </summary>
        /// <param name="e">The old and new DPI</param>
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            this.BeginInvoke((MethodInvoker)this.FitToContents);
        }

        /// <summary>Make the client area just big enough for the layout and the padding around it</summary>
        private void FitToContents()
        {
            this.grid.PerformLayout();
            Size needed = this.grid.GetPreferredSize(Size.Empty);
            this.grid.Size = needed;
            Size buttons = this.closeButtons.GetPreferredSize(Size.Empty);
            this.ClientSize = new Size(Math.Max(needed.Width, buttons.Width) + this.Padding.Horizontal,
                needed.Height + buttons.Height + this.Padding.Vertical);
        }

        /// <summary>Add a label and a control as the next row</summary>
        /// <param name="grid">The layout</param>
        /// <param name="label">The label text; null for a control spanning both columns</param>
        /// <param name="control">The control</param>
        private static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (label == null)
            {
                grid.Controls.Add(control, 0, row);
                grid.SetColumnSpan(control, 2);
            }
            else
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, row);
                grid.Controls.Add(control, 1, row);
            }
        }

        /// <summary>Mask a typed password, but show a {key} placeholder as it is</summary>
        private void ShowPasswordMasked()
        {
            this.textPassword.UseSystemPasswordChar = !this.textPassword.Text.StartsWith("{", StringComparison.Ordinal);
        }

        /// <summary>The connection as filled in, with the password encrypted if it was typed</summary>
        /// <returns>The connection</returns>
        private DatabaseSessions.Connection Current()
        {
            return new DatabaseSessions.Connection
            {
                Host = this.textHost.Text.Trim(),
                Port = (int)this.numericPort.Value,
                Database = this.textDatabase.Text.Trim(),
                Schema = this.textSchema.Text.Trim(),
                Table = this.textTable.Text.Trim(),
                User = this.textUser.Text.Trim(),
                StoredPassword = this.passwordChanged ? DatabaseSessions.ProtectPassword(this.textPassword.Text) : this.storedPassword,
                SslMode = (string)this.comboSsl.SelectedItem,
                ReadOnly = this.checkReadOnly.Checked,
                Nickname = this.textNickname.Text.Trim()
            };
        }

        /// <summary>Check the fields that must be filled in a certain way</summary>
        /// <param name="c">The connection</param>
        /// <returns>The first problem, or null</returns>
        private static string Problem(DatabaseSessions.Connection c)
        {
            string error;
            if (string.IsNullOrEmpty(c.Host))
                return "Enter the server's host name or address.";
            if (string.IsNullOrEmpty(c.User))
                return "Enter the username.";
            if (!DatabaseSessions.ValidateIdentifier(c.Database, "Database", out error)
                || !DatabaseSessions.ValidateIdentifier(c.Schema, "Schema", out error)
                || !DatabaseSessions.ValidateIdentifier(c.Table, "Table", out error))
                return error + ".";
            return null;
        }

        /// <summary>Show a message in the result line, in red for problems</summary>
        /// <param name="text">The message</param>
        /// <param name="problem">Whether it's a problem</param>
        private void ShowResult(string text, bool problem)
        {
            this.labelResult.Text = text;
            this.labelResult.ForeColor = problem ? Color.FromArgb(0xE5, 0x48, 0x48) : this.ForeColor;
            if (this.IsHandleCreated)
            {
                // a longer message takes more lines
                this.FitToContents();
            }
        }

        /// <summary>Connect and look at the table, off the UI thread; enables Create / upgrade when there's something to do</summary>
        private async void Test()
        {
            DatabaseSessions.Connection c = this.Current();
            string problem = Problem(c);
            if (problem != null)
            {
                this.ShowResult(problem, true);
                return;
            }
            this.SetBusy(true, "Connecting to " + c.Host + "...");
            DatabaseSessions.ProbeResult result = await Task.Run(() => DatabaseSessions.Probe(c));
            this.SetBusy(false, null);
            this.ShowResult(result.ToString() + (result.NeedsSchema && c.ReadOnly ? " (Read only is on, so nothing is created.)" : ""),
                !result.Connected || result.Error != null || (!result.Ready && c.ReadOnly));
            this.buttonSchema.Enabled = result.NeedsSchema && !c.ReadOnly;
        }

        /// <summary>Create or upgrade the schema, off the UI thread, then test again</summary>
        private async void CreateSchema()
        {
            DatabaseSessions.Connection c = this.Current();
            this.SetBusy(true, "Creating...");
            string message;
            bool failed = false;
            try
            {
                message = await Task.Run(() => DatabaseSessions.CreateOrUpgradeSchema(c));
            }
            catch (Exception ex)
            {
                message = ex.Message;
                failed = true;
            }
            this.SetBusy(false, null);
            this.buttonSchema.Enabled = false;
            if (failed)
            {
                this.ShowResult("Couldn't create the schema: " + message, true);
                return;
            }
            DatabaseSessions.ProbeResult result = await Task.Run(() => DatabaseSessions.Probe(c));
            this.ShowResult(message + " " + result, false);
        }

        /// <summary>Disable the buttons while connecting</summary>
        /// <param name="busy">Whether a connection is running</param>
        /// <param name="text">What's happening, shown in the result line</param>
        private void SetBusy(bool busy, string text)
        {
            this.buttonTest.Enabled = !busy;
            this.buttonOK.Enabled = !busy;
            if (busy)
            {
                this.buttonSchema.Enabled = false;
                this.ShowResult(text, false);
            }
            this.UseWaitCursor = busy;
        }

        /// <summary>Save the settings, and restart the database sessions so they take effect</summary>
        private void Save()
        {
            DatabaseSessions.Connection c = this.Current();
            string problem = this.checkEnabled.Checked ? Problem(c) : null;
            if (problem == null && this.checkEnabled.Checked && string.IsNullOrEmpty(c.Nickname))
            {
                problem = "Enter a nickname: it shows who changed a session, even when several people share one database login.";
            }
            if (problem != null)
            {
                this.ShowResult(problem, true);
                if (string.IsNullOrEmpty(c.Nickname))
                {
                    this.textNickname.Focus();
                }
                return;
            }
            SuperPuTTY.Settings.DatabaseEnabled = this.checkEnabled.Checked;
            SuperPuTTY.Settings.DatabaseHost = c.Host;
            SuperPuTTY.Settings.DatabasePort = c.Port;
            SuperPuTTY.Settings.DatabaseName = c.Database;
            SuperPuTTY.Settings.DatabaseSchema = c.Schema;
            SuperPuTTY.Settings.DatabaseTable = c.Table;
            SuperPuTTY.Settings.DatabaseUser = c.User;
            SuperPuTTY.Settings.DatabasePassword = c.StoredPassword;
            SuperPuTTY.Settings.DatabaseSslMode = c.SslMode;
            SuperPuTTY.Settings.DatabaseReadOnly = c.ReadOnly;
            SuperPuTTY.Settings.DatabaseNickname = c.Nickname;
            SuperPuTTY.Settings.DatabaseCheckSeconds = (int)this.numericCheck.Value;
            SuperPuTTY.Settings.Save();
            DatabaseSessions.Start();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
