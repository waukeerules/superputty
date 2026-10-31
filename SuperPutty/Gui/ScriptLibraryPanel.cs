using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DarkModeForms;
using SuperPuTTY.Scripting;

namespace SuperPutty.Gui
{
    /// <summary>
    /// The saved-scripts list (ScriptLibrary): insert a saved script into the script being edited (double-click or
    /// Insert), save the current one under a name (Save as...), delete one. Shown on the left of the Script Editor
    /// (dlgScriptEditor), and as the "Saved scripts" tab of the SPSL side panel in the button editor (SpslHelpPanel).
    /// </summary>
    public class ScriptLibraryPanel : UserControl
    {
        /// <summary>The saved scripts</summary>
        private readonly ListBox listScripts = new ListBox();

        /// <summary>Adds the selected script to the script being edited</summary>
        private readonly Button buttonInsert = new Button();

        /// <summary>Saves the script being edited</summary>
        private readonly Button buttonSave = new Button();

        /// <summary>Deletes the selected script</summary>
        private readonly Button buttonDelete = new Button();

        /// <summary>Where scripts are kept, or what was just done</summary>
        private readonly Label labelInfo = new Label();

        /// <summary>The script box this panel inserts into and saves from</summary>
        public TextBoxBase Editor { get; set; }

        /// <summary>The kind of script being edited, which Save as... saves it as; SPSL if not set</summary>
        public Func<ScriptKind> CurrentKind { get; set; }

        /// <summary>Only list scripts of this kind (the button editor lists SPSL scripts); null lists both</summary>
        public ScriptKind? OnlyKind { get; set; }

        /// <summary>
        /// Raised after a saved script is inserted into an empty script box, so the Script Editor can switch its
        /// Run as choice to the script's kind
        /// </summary>
        public event Action<ScriptKind> InsertedIntoEmpty;

        /// <summary>Build the panel</summary>
        public ScriptLibraryPanel()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label heading = new Label { Text = "Saved scripts", AutoSize = true, Margin = new Padding(0, 0, 0, 3) };
            this.listScripts.Dock = DockStyle.Fill;
            this.listScripts.IntegralHeight = false;
            this.listScripts.DoubleClick += (s, e) => this.InsertSelected();
            this.listScripts.SelectedIndexChanged += (s, e) => this.UpdateButtons();

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 3)
            };
            foreach (Button button in new[] { this.buttonInsert, this.buttonSave, this.buttonDelete })
            {
                button.AutoSize = true;
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.Padding = new Padding(4, 2, 4, 2);
                button.Margin = new Padding(0, 0, 6, 3);
                buttons.Controls.Add(button);
            }
            this.buttonInsert.Text = "Insert";
            this.buttonInsert.Click += (s, e) => this.InsertSelected();
            this.buttonSave.Text = "Save as...";
            this.buttonSave.Click += (s, e) => this.SaveCurrent();
            this.buttonDelete.Text = "Delete";
            this.buttonDelete.Click += (s, e) => this.DeleteSelected();
            this.labelInfo.AutoSize = true;

            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(this.listScripts, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            layout.Controls.Add(this.labelInfo, 0, 3);
            this.Controls.Add(layout);
            this.Resize += (s, e) => this.labelInfo.MaximumSize = new Size(Math.Max(50, this.ClientSize.Width - 6), 0);
        }

        /// <summary>List the scripts, and restyle the buttons once the dialog has applied its theme</summary>
        /// <param name="e">Not used</param>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            this.RefreshList(null);
            this.BeginInvoke(new Action(() => DarkButtons.Style(this, this.buttonInsert, this.buttonSave, this.buttonDelete)));
        }

        /// <summary>List the saved scripts</summary>
        /// <param name="select">The name to select afterwards; null keeps the current selection</param>
        public void RefreshList(string select)
        {
            ScriptLibrary.Entry selected = this.listScripts.SelectedItem as ScriptLibrary.Entry;
            select = select ?? selected?.Name;
            this.listScripts.BeginUpdate();
            this.listScripts.Items.Clear();
            foreach (ScriptLibrary.Entry entry in ScriptLibrary.List().Where(e => this.OnlyKind == null || e.Kind == this.OnlyKind))
            {
                this.listScripts.Items.Add(entry);
                if (string.Equals(entry.Name, select, StringComparison.OrdinalIgnoreCase))
                {
                    this.listScripts.SelectedItem = entry;
                }
            }
            this.listScripts.EndUpdate();
            this.labelInfo.Text = this.listScripts.Items.Count == 0
                ? "None yet: Save as... keeps the script here, in " + ScriptLibrary.Folder
                : "Double-click to insert. Kept in " + ScriptLibrary.Folder;
            this.UpdateButtons();
        }

        /// <summary>Enable Insert and Delete only when a script is selected</summary>
        private void UpdateButtons()
        {
            bool selected = this.listScripts.SelectedItem != null;
            this.buttonInsert.Enabled = selected;
            this.buttonDelete.Enabled = selected;
        }

        /// <summary>Add the selected script to the script being edited (see <see cref="ScriptText.Insert"/>)</summary>
        private void InsertSelected()
        {
            ScriptLibrary.Entry entry = this.listScripts.SelectedItem as ScriptLibrary.Entry;
            if (entry == null || this.Editor == null)
                return;
            string text;
            try
            {
                text = ScriptLibrary.Load(entry);
            }
            catch (Exception ex)
            {
                Messenger.MessageBox("Couldn't read the script \"" + entry.Name + "\": " + ex.Message, "Saved scripts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (ScriptText.Insert(this.Editor, text) && this.InsertedIntoEmpty != null)
            {
                this.InsertedIntoEmpty(entry.Kind);
            }
        }

        /// <summary>Save the script being edited under a name, as the kind being edited, asking before replacing one</summary>
        private void SaveCurrent()
        {
            if (this.Editor == null || this.Editor.Text.Trim().Length == 0)
            {
                this.labelInfo.Text = "Write a script first; Save as... then keeps it here.";
                return;
            }
            ScriptKind kind = this.CurrentKind != null ? this.CurrentKind() : ScriptKind.Spsl;
            dlgRenameItem.ItemNameValidationHandler validator = delegate (string candidate, out string error)
            {
                return ScriptLibrary.ValidateName(candidate, out error);
            };
            using (dlgRenameItem naming = new dlgRenameItem
            {
                Text = kind == ScriptKind.Spsl ? "Save SPSL script" : "Save lines to send",
                ItemName = (this.listScripts.SelectedItem as ScriptLibrary.Entry)?.Name ?? string.Empty,
                DetailName = "Saved in " + ScriptLibrary.Folder,
                ItemNameValidator = validator
            })
            {
                if (naming.ShowDialog(this) != DialogResult.OK)
                    return;
                string name = naming.ItemName.Trim();
                if (ScriptLibrary.Exists(name) && Messenger.MessageBox("Replace the saved script \"" + name + "\"?", "Save script",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                try
                {
                    ScriptLibrary.Save(name, kind, this.Editor.Text);
                }
                catch (Exception ex)
                {
                    Messenger.MessageBox("Couldn't save the script: " + ex.Message, "Save script", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                this.RefreshList(name);
                this.labelInfo.Text = "Saved \"" + name + "\".";
            }
        }

        /// <summary>Delete the selected script, after asking</summary>
        private void DeleteSelected()
        {
            ScriptLibrary.Entry entry = this.listScripts.SelectedItem as ScriptLibrary.Entry;
            if (entry == null || Messenger.MessageBox("Delete the saved script \"" + entry.Name + "\"?", "Saved scripts",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                ScriptLibrary.Delete(entry);
            }
            catch (Exception ex)
            {
                Messenger.MessageBox("Couldn't delete the script: " + ex.Message, "Saved scripts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            this.RefreshList(null);
        }
    }

    /// <summary>Adding text to a script box, shared by the script panels (SpslHelpPanel, ScriptLibraryPanel)</summary>
    public static class ScriptText
    {
        /// <summary>
        /// Add text to a script box. An empty box (or one holding only the old #!/bin/spsl line) is replaced; otherwise
        /// the text goes on new lines after the line the cursor is on. A #!/bin/spsl first line in the inserted text is
        /// left out: whether a script is SPSL is chosen in the editor, not marked in the script.
        /// </summary>
        /// <param name="editor">The script box</param>
        /// <param name="block">The text: a line, or a whole saved script</param>
        /// <returns>true if the box was empty and now holds just the inserted text</returns>
        public static bool Insert(TextBoxBase editor, string block)
        {
            if (editor == null || string.IsNullOrEmpty(block))
                return false;
            block = block.Replace("\r\n", "\n");
            if (block.TrimStart().StartsWith(SpslReference.Header, StringComparison.OrdinalIgnoreCase))
            {
                int firstBreak = block.IndexOf('\n');
                block = firstBreak < 0 ? string.Empty : block.Substring(firstBreak + 1);
            }
            block = block.Trim('\n');
            if (block.Length == 0)
                return false;

            string text = editor.Text;
            if (text.Trim().Length == 0 || text.Trim().Equals(SpslReference.Header, StringComparison.OrdinalIgnoreCase))
            {
                editor.Text = block.Replace("\n", Environment.NewLine) + Environment.NewLine;
                editor.Select(editor.Text.Length, 0);
                editor.Focus();
                return true;
            }

            string insert = block.Replace("\n", Environment.NewLine);
            int lineEnd = text.IndexOf('\n', editor.SelectionStart);
            int at;
            if (lineEnd < 0)
            {
                at = text.Length;
                insert = text.EndsWith("\n") ? insert : Environment.NewLine + insert;
            }
            else
            {
                at = lineEnd + 1;
                insert = insert + Environment.NewLine;
            }
            editor.Select(at, 0);
            editor.SelectedText = insert;
            editor.Focus();
            return false;
        }
    }

    /// <summary>Button styling for the script panels on the dark theme</summary>
    internal static class DarkButtons
    {
        /// <summary>
        /// On a dark theme, give buttons a fill a little lighter than their panel: DarkModeCS colours them like the panel
        /// behind them, so they looked like plain text. Call after the dialog has applied its theme.
        /// </summary>
        /// <param name="panel">The panel the buttons are on</param>
        /// <param name="buttons">The buttons</param>
        internal static void Style(Control panel, params Button[] buttons)
        {
            Color background = panel.BackColor;
            if (background.GetBrightness() >= 0.5f)
                return;
            foreach (Button button in buttons)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = ControlPaint.Light(background, 0.4f);
                button.FlatAppearance.BorderColor = ControlPaint.Light(background, 0.9f);
                button.FlatAppearance.BorderSize = 1;
            }
        }
    }
}
