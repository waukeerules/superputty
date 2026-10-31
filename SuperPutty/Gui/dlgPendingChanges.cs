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
    /// Lists the changes to database sessions that haven't reached the table (DatabaseOutbox): waiting while it can't
    /// be reached, or refused (someone else's change, a lock, a name clash). Send again retries the selected ones,
    /// Overwrite theirs saves an edit over someone else's change, Discard drops them. Opened from the Database
    /// sessions root's menu in SessionTreeview; built in code. Updates itself as the background sync sends changes.
    /// </summary>
    public class dlgPendingChanges : Form
    {
        /// <summary>The changes: what, when, and why it wasn't saved</summary>
        private readonly ListView list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            ShowItemToolTips = true,
            Size = new Size(620, 220),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        /// <summary>Clears the selected changes' errors and sends them</summary>
        private readonly Button buttonRetry = new Button { Text = "Send again", AutoSize = true };

        /// <summary>Saves the selected edits over someone else's change</summary>
        private readonly Button buttonOverwrite = new Button { Text = "Overwrite theirs", AutoSize = true };

        /// <summary>Drops the selected changes</summary>
        private readonly Button buttonDiscard = new Button { Text = "Discard", AutoSize = true };

        /// <summary>Closes the list</summary>
        private readonly Button buttonClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };

        /// <summary>The database the changes are for</summary>
        private readonly string source = DatabaseSessions.FromSettings().ToString();

        /// <summary>Build the dialog and list the changes</summary>
        public dlgPendingChanges()
        {
            this.Text = "Pending changes";
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Padding = new Padding(12);
            this.CancelButton = this.buttonClose;
            this.list.Columns.Add("Change", 230);
            this.list.Columns.Add("Queued", 90);
            this.list.Columns.Add("Status", 420);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            buttons.Controls.AddRange(new Control[] { this.buttonRetry, this.buttonOverwrite, this.buttonDiscard, this.buttonClose });
            this.list.Dock = DockStyle.Fill;
            this.Controls.Add(this.list);
            this.Controls.Add(buttons);
            this.ClientSize = new Size(800, 320);
            this.MinimumSize = new Size(480, 240);

            ToolTip tips = new ToolTip();
            tips.SetToolTip(this.buttonRetry, "Try the selected changes again; they're sent now if the database can be reached");
            tips.SetToolTip(this.buttonOverwrite, "For a change refused because someone else changed the session: save yours over theirs");
            tips.SetToolTip(this.buttonDiscard, "Drop the selected changes; the sessions go back to what the database has");

            this.list.SelectedIndexChanged += (s, e) => this.UpdateButtons();
            this.buttonRetry.Click += (s, e) => this.Retry(false);
            this.buttonOverwrite.Click += (s, e) => this.Retry(true);
            this.buttonDiscard.Click += (s, e) => this.Discard();
            DatabaseSessions.StatusChanged += this.DatabaseSessions_StatusChanged;
            this.FillList();

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>Stop following the sync</summary>
        /// <param name="e">Not used</param>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DatabaseSessions.StatusChanged -= this.DatabaseSessions_StatusChanged;
            base.OnFormClosed(e);
        }

        /// <summary>Refill the list as the sync sends or refuses changes</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        private void DatabaseSessions_StatusChanged(object sender, EventArgs e)
        {
            if (!this.IsDisposed)
            {
                this.FillList();
            }
        }

        /// <summary>List the changes, oldest first, keeping the selection</summary>
        private void FillList()
        {
            HashSet<string> selected = new HashSet<string>(this.SelectedChanges().Select(c => c.Id));
            this.list.BeginUpdate();
            this.list.Items.Clear();
            foreach (DatabaseOutbox.PendingChange change in DatabaseOutbox.Changes(this.source))
            {
                ListViewItem item = new ListViewItem(new[]
                {
                    change.ToString(),
                    change.QueuedAt.ToString("g"),
                    change.Error == null ? (change.Force ? "Waiting (will overwrite)" : "Waiting to be sent") : "Not saved: " + change.Error
                });
                item.Tag = change;
                item.ToolTipText = change.Error == null ? change.ToString() : change.ToString() + ": " + change.Error;
                item.Selected = selected.Contains(change.Id);
                if (change.Error != null)
                {
                    item.ForeColor = Color.FromArgb(0xE5, 0x48, 0x48);
                }
                this.list.Items.Add(item);
            }
            this.list.EndUpdate();
            this.UpdateButtons();
        }

        /// <summary>The selected changes</summary>
        /// <returns>The changes</returns>
        private List<DatabaseOutbox.PendingChange> SelectedChanges()
        {
            return this.list.SelectedItems.Cast<ListViewItem>().Select(i => (DatabaseOutbox.PendingChange)i.Tag).ToList();
        }

        /// <summary>Enable the buttons for what's selected: Overwrite only for refused edits</summary>
        private void UpdateButtons()
        {
            List<DatabaseOutbox.PendingChange> changes = this.SelectedChanges();
            this.buttonRetry.Enabled = changes.Count > 0;
            this.buttonDiscard.Enabled = changes.Count > 0;
            this.buttonOverwrite.Enabled = changes.Any(c => c.Kind == DatabaseOutbox.ChangeKind.Update && c.Error != null);
        }

        /// <summary>Send the selected changes again, optionally over someone else's edits</summary>
        /// <param name="overwrite">Save edits even though the session changed since it was read</param>
        private void Retry(bool overwrite)
        {
            foreach (DatabaseOutbox.PendingChange change in this.SelectedChanges())
            {
                bool force = overwrite && change.Kind == DatabaseOutbox.ChangeKind.Update;
                if (!overwrite || force)
                {
                    DatabaseOutbox.SetError(this.source, change.Id, null, force);
                }
            }
            DatabaseSessions.RefreshPending();
            DatabaseSessions.BeginSync();
        }

        /// <summary>Drop the selected changes, after asking</summary>
        private void Discard()
        {
            List<DatabaseOutbox.PendingChange> changes = this.SelectedChanges();
            if (changes.Count == 0 || Messenger.MessageBox(this, string.Format("Discard {0} change{1}? The sessions go back to what the database has.",
                    changes.Count, changes.Count == 1 ? "" : "s"), "Pending changes", MessageBoxButtons.YesNo, MsgIcon.Question) != DialogResult.Yes)
                return;
            foreach (DatabaseOutbox.PendingChange change in changes)
            {
                DatabaseOutbox.Remove(this.source, change.Id);
            }
            DatabaseSessions.RefreshPending();
        }
    }
}
