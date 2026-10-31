using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuperPuTTY.Scripting;

namespace SuperPutty.Gui
{
    /// <summary>
    /// A side panel for writing SPSL scripts, used by the Script Editor in SPSL mode (dlgScriptEditor) and by the button
    /// editor in SPSL mode (dlgEditCommandButton). A Commands tab lists each command's syntax, description and an example
    /// (SpslReference), with Insert example; the button editor also gets a Saved scripts tab (ScriptLibraryPanel), which
    /// the Script Editor shows on its left instead. Below the tabs, "Check script" lists the script's problems
    /// (SpslChecker) and selects a problem's line when it's clicked.
    /// </summary>
    public class SpslHelpPanel : UserControl
    {
        /// <summary>The list of commands</summary>
        private readonly ListBox listCommands = new ListBox();

        /// <summary>The selected command's syntax, description and example</summary>
        private readonly TextBox textDetails = new TextBox();

        /// <summary>Adds the selected command's example to the script</summary>
        private readonly Button buttonInsert = new Button();

        /// <summary>Checks the script</summary>
        private readonly Button buttonCheck = new Button();

        /// <summary>The check's summary</summary>
        private readonly Label labelResult = new Label();

        /// <summary>The problems found, one per row</summary>
        private readonly ListView listIssues = new ListView();

        /// <summary>The Commands and Saved scripts tabs</summary>
        private readonly TabControl tabs = new TabControl();

        /// <summary>The Saved scripts tab, added by <see cref="ShowSavedScripts"/></summary>
        private readonly TabPage savedTab = new TabPage("Saved scripts") { Padding = new Padding(3) };

        /// <summary>The saved scripts, on <see cref="savedTab"/></summary>
        private readonly ScriptLibraryPanel library = new ScriptLibraryPanel { Dock = DockStyle.Fill, OnlyKind = ScriptKind.Spsl };

        /// <summary>The script box this panel inserts into and checks</summary>
        private TextBoxBase editor;

        /// <summary>The script box this panel inserts into and checks</summary>
        public TextBoxBase Editor
        {
            get { return this.editor; }
            set
            {
                this.editor = value;
                this.library.Editor = value;
            }
        }

        /// <summary>
        /// Whether to show a Saved scripts tab (SPSL scripts only). The button editor shows it; the Script Editor has
        /// its own saved-scripts panel on the left.
        /// </summary>
        public bool ShowSavedScripts
        {
            get { return this.tabs.TabPages.Contains(this.savedTab); }
            set
            {
                if (value && !this.ShowSavedScripts)
                {
                    this.tabs.TabPages.Add(this.savedTab);
                }
                else if (!value && this.ShowSavedScripts)
                {
                    this.tabs.TabPages.Remove(this.savedTab);
                }
            }
        }

        /// <summary>Build the panel</summary>
        public SpslHelpPanel()
        {
            // Commands tab: the list, the selected command's details, Insert example
            TableLayoutPanel commandsLayout = NewColumn(3);
            commandsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            commandsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            commandsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.listCommands.Dock = DockStyle.Fill;
            this.listCommands.IntegralHeight = false;
            this.listCommands.Items.AddRange(SpslReference.Entries.Cast<object>().ToArray());
            this.listCommands.SelectedIndexChanged += (s, e) => this.ShowDetails();
            this.listCommands.DoubleClick += (s, e) => this.InsertExample();
            this.textDetails.Dock = DockStyle.Fill;
            this.textDetails.Multiline = true;
            this.textDetails.ReadOnly = true;
            this.textDetails.ScrollBars = ScrollBars.Vertical;
            this.buttonInsert.Text = "Insert example";
            this.buttonInsert.Click += (s, e) => this.InsertExample();
            commandsLayout.Controls.Add(this.listCommands, 0, 0);
            commandsLayout.Controls.Add(this.textDetails, 0, 1);
            commandsLayout.Controls.Add(ButtonRow(this.buttonInsert), 0, 2);

            this.tabs.Dock = DockStyle.Fill;
            TabPage commandsTab = new TabPage("Commands") { Padding = new Padding(3) };
            commandsTab.Controls.Add(commandsLayout);
            this.tabs.TabPages.Add(commandsTab);
            this.savedTab.Controls.Add(this.library);
            this.library.CurrentKind = () => ScriptKind.Spsl;

            // below the tabs: Check script and its results
            TableLayoutPanel layout = NewColumn(4);
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            this.buttonCheck.Text = "Check script";
            this.buttonCheck.Click += (s, e) => this.Check();
            this.labelResult.AutoSize = true;
            this.labelResult.Text = "Checks commands, arguments, keys, session IDs and placeholders.";
            this.labelResult.MaximumSize = new Size(10000, 0);
            this.listIssues.Dock = DockStyle.Fill;
            this.listIssues.View = View.Details;
            this.listIssues.FullRowSelect = true;
            this.listIssues.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            this.listIssues.ShowItemToolTips = true;
            this.listIssues.Columns.Add("Line", 44, HorizontalAlignment.Right);
            this.listIssues.Columns.Add("Problem", 400);
            this.listIssues.SelectedIndexChanged += (s, e) => this.SelectIssueLine();
            layout.Controls.Add(this.tabs, 0, 0);
            layout.Controls.Add(ButtonRow(this.buttonCheck), 0, 1);
            layout.Controls.Add(this.labelResult, 0, 2);
            layout.Controls.Add(this.listIssues, 0, 3);
            this.Controls.Add(layout);
            this.Resize += (s, e) => this.FitColumns();
            this.listIssues.Resize += (s, e) => this.FitColumns();
            this.listCommands.SelectedIndex = 0;
        }

        /// <summary>A one-column layout as wide as its parent</summary>
        /// <param name="rows">How many rows</param>
        /// <returns>The layout; add its row styles</returns>
        private static TableLayoutPanel NewColumn(int rows)
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Margin = new Padding(0) };
            // the column is the parent's width; by default it grows to its widest control and runs off the edge
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        /// <summary>A row of buttons, each sized to its text</summary>
        /// <param name="buttons">The buttons</param>
        /// <returns>The row</returns>
        private static FlowLayoutPanel ButtonRow(params Button[] buttons)
        {
            FlowLayoutPanel row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 3, 0, 3)
            };
            foreach (Button button in buttons)
            {
                button.AutoSize = true;
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.Padding = new Padding(6, 2, 6, 2);
                button.Margin = new Padding(0, 0, 6, 0);
                row.Controls.Add(button);
            }
            return row;
        }

        /// <summary>Restyle the buttons once the dialog has applied its theme</summary>
        /// <param name="e">Not used</param>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            this.BeginInvoke(new Action(() => DarkButtons.Style(this, this.buttonInsert, this.buttonCheck)));
        }

        /// <summary>Wrap the result text to the panel, and let the problem column fill the list's width</summary>
        private void FitColumns()
        {
            this.labelResult.MaximumSize = new Size(Math.Max(50, this.ClientSize.Width - 6), 0);
            int width = this.listIssues.ClientSize.Width - this.listIssues.Columns[0].Width;
            if (width > 40)
            {
                this.listIssues.Columns[1].Width = width;
            }
        }

        /// <summary>Show the selected command's syntax, description and example</summary>
        private void ShowDetails()
        {
            SpslReference.Entry entry = this.listCommands.SelectedItem as SpslReference.Entry;
            if (entry == null)
                return;
            this.textDetails.Text = entry.Syntax + Environment.NewLine + Environment.NewLine + entry.Description
                + Environment.NewLine + Environment.NewLine + "Example:" + Environment.NewLine + entry.Example;
            this.buttonInsert.Enabled = entry.Available;
        }

        /// <summary>Add the selected command's example to the script (see <see cref="ScriptText.Insert"/>)</summary>
        private void InsertExample()
        {
            SpslReference.Entry entry = this.listCommands.SelectedItem as SpslReference.Entry;
            if (entry != null && entry.Available)
            {
                ScriptText.Insert(this.Editor, entry.Example);
            }
        }

        /// <summary>Check the script and list its problems</summary>
        /// <returns>The problems found</returns>
        public List<SpslChecker.Issue> Check()
        {
            List<SpslChecker.Issue> issues = SpslChecker.Check(this.Editor != null ? this.Editor.Text : string.Empty);
            this.listIssues.BeginUpdate();
            this.listIssues.Items.Clear();
            foreach (SpslChecker.Issue issue in issues)
            {
                ListViewItem item = new ListViewItem(new[] { issue.Line.ToString(), (issue.IsError ? "Error: " : "Warning: ") + issue.Message })
                {
                    Tag = issue,
                    ToolTipText = issue.Message,
                    ForeColor = issue.IsError ? Color.IndianRed : Color.DarkGoldenrod
                };
                this.listIssues.Items.Add(item);
            }
            this.listIssues.EndUpdate();
            this.FitColumns();

            int errors = issues.Count(i => i.IsError);
            int warnings = issues.Count - errors;
            this.labelResult.Text = issues.Count == 0 ? "No problems found."
                : string.Format("{0} error{1}, {2} warning{3}. Click one to go to its line.",
                    errors, errors == 1 ? "" : "s", warnings, warnings == 1 ? "" : "s");
            return issues;
        }

        /// <summary>Select the line of the clicked problem in the script box</summary>
        private void SelectIssueLine()
        {
            if (this.Editor == null || this.listIssues.SelectedItems.Count == 0)
                return;
            SpslChecker.Issue issue = (SpslChecker.Issue)this.listIssues.SelectedItems[0].Tag;
            // the list cuts long messages short, so show the whole one above it
            this.labelResult.Text = "Line " + issue.Line + ": " + issue.Message;
            // counted by line breaks, not GetFirstCharIndexFromLine, which counts word-wrapped lines too
            string text = this.Editor.Text;
            int start = 0;
            for (int line = 1; line < issue.Line; line++)
            {
                int next = text.IndexOf('\n', start);
                if (next < 0)
                    return;
                start = next + 1;
            }
            int end = text.IndexOf('\n', start);
            this.Editor.Select(start, (end < 0 ? this.Editor.Text.Length : end) - start);
            this.Editor.ScrollToCaret();
            this.Editor.Focus();
        }
    }
}
