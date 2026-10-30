using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DarkModeForms;
using log4net;
using SuperPutty.Utils;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Finds text in what a tab has printed, by searching its session log (SessionLog), since PuTTY itself has no
    /// scrollback search. Lists the matching lines with their line numbers; double-click copies a line, and
    /// "Open log" opens the whole log. Opened from the tab menu -> "Search Output..."; modeless, one per tab.
    /// </summary>
    public partial class dlgSearchOutput : Form
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(dlgSearchOutput));

        /// <summary>Most matching lines listed, so a huge log can't freeze the window</summary>
        private const int MaxResults = 5000;

        /// <summary>The open search window for each tab</summary>
        private static readonly Dictionary<ctlPuttyPanel, dlgSearchOutput> open = new Dictionary<ctlPuttyPanel, dlgSearchOutput>();

        /// <summary>The tab being searched</summary>
        private readonly ctlPuttyPanel panel;

        /// <summary>Create the window for a tab; use <see cref="ShowFor"/></summary>
        /// <param name="panel">The tab to search</param>
        private dlgSearchOutput(ctlPuttyPanel panel)
        {
            InitializeComponent();
            this.panel = panel;
            this.Text = "Search Output: " + panel.Text;

            if (SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme)
            {
                new DarkModeCS(this)
                {
                    ColorMode = DarkModeCS.DisplayMode.DarkMode,
                    ColorizeIcons = false
                };
            }
        }

        /// <summary>Show the search window for a tab, or bring its open one to the front</summary>
        /// <param name="panel">The tab to search; it must have a session log</param>
        public static void ShowFor(ctlPuttyPanel panel)
        {
            dlgSearchOutput dialog;
            if (!open.TryGetValue(panel, out dialog) || dialog.IsDisposed)
            {
                dialog = new dlgSearchOutput(panel);
                open[panel] = dialog;
                dialog.FormClosed += (s, e) => open.Remove(panel);
                // close it with its tab
                panel.FormClosed += (s, e) => { if (!dialog.IsDisposed) dialog.Close(); };
                dialog.Show(SuperPuTTY.MainForm);
            }
            dialog.Activate();
            dialog.textBoxFind.Focus();
            dialog.textBoxFind.SelectAll();
        }

        /// <summary>Search the tab's output for the text and list the matching lines</summary>
        /// <param name="sender">The Find button, or Enter in the text box</param>
        /// <param name="e">Not used</param>
        private void btnFind_Click(object sender, EventArgs e)
        {
            string find = this.textBoxFind.Text;
            if (string.IsNullOrEmpty(find))
                return;

            Func<string, bool> isMatch;
            try
            {
                isMatch = MakeMatcher(find, this.checkMatchCase.Checked, this.checkRegex.Checked);
            }
            catch (ArgumentException ex)
            {
                this.labelSummary.Text = "Not a valid regular expression: " + ex.Message;
                return;
            }

            string[] lines;
            try
            {
                lines = SessionLog.CleanForDisplay(SessionLog.ReadFrom(this.panel.SessionLogPath, 0));
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the session log " + this.panel.SessionLogPath, ex);
                this.labelSummary.Text = "Could not read this tab's output: " + ex.Message;
                return;
            }

            this.listResults.BeginUpdate();
            this.listResults.Items.Clear();
            int matches = 0;
            for (int i = 0; i < lines.Length && matches < MaxResults; i++)
            {
                if (isMatch(lines[i]))
                {
                    matches++;
                    this.listResults.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), lines[i] }));
                }
            }
            this.listResults.EndUpdate();
            if (this.listResults.Items.Count > 0)
            {
                this.listResults.EnsureVisible(this.listResults.Items.Count - 1);
            }
            this.labelSummary.Text = string.Format("{0}{1} matching line(s) in {2} line(s) of output. Double-click a line to copy it.",
                matches, matches >= MaxResults ? "+" : "", lines.Length);
        }

        /// <summary>Build the line test for the search options</summary>
        /// <param name="find">The text or pattern to find</param>
        /// <param name="matchCase">Case-sensitive</param>
        /// <param name="regex">Treat <paramref name="find"/> as a regular expression</param>
        /// <returns>A function telling whether a line matches</returns>
        /// <exception cref="ArgumentException">The regular expression isn't valid</exception>
        public static Func<string, bool> MakeMatcher(string find, bool matchCase, bool regex)
        {
            if (regex)
            {
                Regex pattern = new Regex(find, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                return line => pattern.IsMatch(line);
            }
            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return line => line.IndexOf(find, comparison) >= 0;
        }

        /// <summary>Copy the double-clicked line to the clipboard</summary>
        /// <param name="sender">The results list</param>
        /// <param name="e">Not used</param>
        private void listResults_DoubleClick(object sender, EventArgs e)
        {
            if (this.listResults.SelectedItems.Count == 0)
                return;
            string text = this.listResults.SelectedItems[0].SubItems[1].Text;
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                this.labelSummary.Text = "Copied line " + this.listResults.SelectedItems[0].Text + " to the clipboard.";
            }
        }

        /// <summary>Open the whole session log in the program Windows uses for .log files</summary>
        /// <param name="sender">The Open log button</param>
        /// <param name="e">Not used</param>
        private void btnOpenLog_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(this.panel.SessionLogPath);
            }
            catch (Exception ex)
            {
                Messenger.MessageBox("Could not open the log:\n" + ex.Message, "Search Output", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
