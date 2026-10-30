/*
 * Copyright (c) 2009 - 2015 Jim Radford http://www.jimradford.com
 * Copyright (c) 2012 John Peterson
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using log4net;
using System.Diagnostics;
using System.Web;
using System.Collections.Specialized;
using SuperPutty.Data;
using WeifenLuo.WinFormsUI.Docking;
using SuperPutty.Utils;
using System.Threading;
using System.Configuration;
using SuperPutty.Gui;
using log4net.Core;
using DarkModeForms;
using SuperPuTTY.Scripting;


namespace SuperPutty
{
    /// <summary>A control that hosts a putty window</summary>
    public partial class ctlPuttyPanel : ToolWindowDocument
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ctlPuttyPanel));

        private static int RefocusAttempts = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.RefocusAttempts"] ?? "5");
        private static int RefocusIntervalMs = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.RefocusIntervalMs"] ?? "80");

        private PuttyStartInfo m_puttyStartInfo;
        private PuttyClosedCallback m_ApplicationExit;

        public ctlPuttyPanel(SessionData session, PuttyClosedCallback callback)
        {
            Session = session;
            m_ApplicationExit = callback;
            m_puttyStartInfo = new PuttyStartInfo(session);

            InitializeComponent();

            this.Text = session.SessionName;
            this.TabText = session.SessionName;
            this.TextOverride = session.SessionName;

            CreatePanel();
            AdjustMenu();

            aboutPuttyToolStripMenuItem.Text = "About " + SuperPuTTY.PuTTYAppName;
        }

        /// <summary>Gets or sets the text displayed on a tab.</summary>
        public override string Text
        {
            get
            {
                return base.Text;
            }
            set
            {
                this.TabText = value != null ? value.Replace("&", "&&") : null;
                base.Text = value;

                if (Log.Logger.IsEnabledFor(Level.Trace))
                {
                    Log.DebugFormat("SetText: text={0}", value);
                }
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            this.ToolTipText = this.Text;
        }

        private void CreatePanel()
        {
            this.AppPanel = new ApplicationPanel(this.Session.Proto);
            this.SuspendLayout();            
            this.AppPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AppPanel.ApplicationName = this.m_puttyStartInfo.Executable;
            this.AppPanel.ApplicationParameters = this.m_puttyStartInfo.Args;
            this.AppPanel.ApplicationParametersForSize = this.m_puttyStartInfo.ArgsForSize;
            if (this.Session.Proto == ConnectionProtocol.RDP && RdpControl.ShouldEmbed(this.m_puttyStartInfo.Executable))
            {
                // host the Remote Desktop ActiveX control in the tab, sized to it, instead of capturing an mstsc window
                SessionData resolved = SessionVariables.Resolve(this.Session);
                this.AppPanel.EmbeddedApplicationFactory = () => new RdpControl(resolved);
            }
            this.AppPanel.ApplicationWorkingDirectory = this.m_puttyStartInfo.WorkingDir;
            this.AppPanel.ApplicationCloseWithDestroy = this.Session.Proto == ConnectionProtocol.Mintty ? false : true;
            this.AppPanel.Location = new System.Drawing.Point(0, 0);
            this.AppPanel.Name = this.Session.SessionId; // "applicationControl1";
            this.AppPanel.Size = new System.Drawing.Size(this.Width, this.Height);
            this.AppPanel.TabIndex = 0;            
            this.AppPanel.m_CloseCallback = this.m_ApplicationExit;
            // Adding the panel raises OnVisibleChanged, which would start PuTTY right here at this tab's
            // placeholder size. SuperPuTTY.OpenProtoSession starts it once the tab has been shown and laid out.
            this.AppPanel.StartOnVisible = false;
            this.Controls.Add(this.AppPanel);

            this.ResumeLayout();
        }

        void AdjustMenu()
        {
            // For certain protocols, disable the putty menu items
            if (Session.Proto == ConnectionProtocol.VNC ||
                Session.Proto == ConnectionProtocol.Mintty ||
                Session.Proto == ConnectionProtocol.RDP ||
                Session.Proto == ConnectionProtocol.WINCMD ||
                Session.Proto == ConnectionProtocol.PS ||
                Session.Proto == ConnectionProtocol.WSL)
            {
                toolStripPuttySep1.Visible = false;
                eventLogToolStripMenuItem.Visible = false;
                toolStripPuttySep2.Visible = false;
                changeSettingsToolStripMenuItem.Visible = false;
                copyAllToClipboardToolStripMenuItem.Visible = false;
                restartSessionToolStripMenuItem.Visible = false;
                clearScrollbackToolStripMenuItem.Visible = false;
                resetTerminalToolStripMenuItem.Visible = false;
                resetAndClearScrollbackToolStripMenuItem.Visible = false;
                aboutPuttyToolStripMenuItem.Visible = false;
                toolStripSeparator4.Visible = false;
            }
        }

        private void InitNewSessionToolStripMenuItems()
        {
            List<ToolStripMenuItem> tsmi = new List<ToolStripMenuItem>();
            foreach (SessionData session in SuperPuTTY.GetAllSessions())
            {
                ToolStripMenuItem tsmiParent = null;
                foreach (string part in SessionData.GetSessionNameParts(session.SessionId))
                {
                    if (part == session.SessionName)
                    {
                        ToolStripMenuItem newSessionTSMI = new ToolStripMenuItem
                        {
                            Tag = session,
                            Text = session.SessionName
                        };
                        newSessionTSMI.Click += new System.EventHandler(newSessionTSMI_Click);
                        newSessionTSMI.ToolTipText = session.ToString();
                        if (tsmiParent == null)
                            tsmi.Add(newSessionTSMI);
                        else
                            tsmiParent.DropDownItems.Add(newSessionTSMI);
                    }
                    else
                    {
                        if (tsmiParent == null)
                        {
                            tsmiParent = tsmi.FirstOrDefault((item) => string.Equals(item.Name, part));
                            if (tsmiParent == null)
                            {
                                tsmiParent = new ToolStripMenuItem(part) { Name = part };
                                tsmi.Add(tsmiParent);
                            }
                        }
                        else
                        {
                            if (tsmiParent.DropDownItems.ContainsKey(part))
                            {
                                tsmiParent = (ToolStripMenuItem)tsmiParent.DropDownItems[part];
                            }
                            else
                            {
                                ToolStripMenuItem newSessionFolder = new ToolStripMenuItem(part) { Name = part };
                                tsmiParent.DropDownItems.Add(newSessionFolder);
                                tsmiParent = newSessionFolder;
                            }
                        }
                    }
                }
            }

            if (InvokeRequired)
                Invoke(new Action(() => {
                    if (newSessionToolStripMenuItem.DropDownItems.Count == 0)
                        newSessionToolStripMenuItem.DropDownItems.AddRange(tsmi.ToArray());
                }));
            else
                if (newSessionToolStripMenuItem.DropDownItems.Count == 0)
                    newSessionToolStripMenuItem.DropDownItems.AddRange(tsmi.ToArray());
        }

        void CreateMenu()
        {
            this.newSessionToolStripMenuItem.Enabled = SuperPuTTY.Settings.PuttyPanelShowNewSessionMenu;
            if (SuperPuTTY.Settings.PuttyPanelShowNewSessionMenu)
            {
                newSessionToolStripMenuItem.DropDownItems.Clear();

                new Thread(InitNewSessionToolStripMenuItems).Start();
            }

            DockPane pane = GetDockPane();
            if (pane != null)
            {
                this.closeOthersToTheRightToolStripMenuItem.Enabled =
                    pane.Contents.IndexOf(this) != pane.Contents.Count - 1;
            }
            this.closeOthersToolStripMenuItem.Enabled = this.DockPanel.DocumentsCount > 1;
            this.closeAllToolStripMenuItem.Enabled = this.DockPanel.DocumentsCount > 1;
        }

        private void closeSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void closeOthersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var docs = from doc in this.DockPanel.DocumentsToArray()
                       where doc is ToolWindowDocument && doc != this
                       select doc as ToolWindowDocument;
            CloseDocs("Close Others", docs);
        }

        private void closeAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var docs = from doc in this.DockPanel.DocumentsToArray()
                       where doc is ToolWindowDocument
                       select doc as ToolWindowDocument;
            CloseDocs("Close All", docs);
        }

        private void closeOthersToTheRightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // find the dock pane with this window
            DockPane pane = GetDockPane();
            if (pane != null)
            {
                // found the pane
                List<ToolWindowDocument> docsToClose = new List<ToolWindowDocument>();
                bool close = false;
                foreach (IDockContent content in new List<IDockContent>(pane.Contents))
                {
                    if (content == this)
                    {
                        close = true;
                        continue;
                    }
                    if (close)
                    {
                        ToolWindowDocument win = content as ToolWindowDocument;
                        if (win != null)
                        {
                            docsToClose.Add(win);
                        }
                    }
                }
                if (docsToClose.Count > 0)
                {
                    CloseDocs("Close Other To the Right", docsToClose);
                }
            }
        }

        void CloseDocs(string source, IEnumerable<ToolWindowDocument> docsToClose)
        {
            int n = docsToClose.Count();
            Log.InfoFormat("Closing mulitple docs: source={0}, count={1}, conf={2}", source, n, SuperPuTTY.Settings.MultipleTabCloseConfirmation);

            bool okToClose = true;
            if (SuperPuTTY.Settings.MultipleTabCloseConfirmation && n > 1)
            {
                okToClose = DialogResult.Yes == Messenger.MessageBox(string.Format("Close {0} Tabs?", n), source, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }

            if (okToClose)
            {
                foreach (ToolWindowDocument doc in docsToClose)
                {
                    doc.Close();
                }
            }
        }

        DockPane GetDockPane()
        {
            return this.DockPanel.Panes.FirstOrDefault(pane => pane.Contents.Contains(this));
        }

        /// <summary>
        /// Reset the focus to the child application window
        /// </summary>
        /// <summary>
        /// Whether Send Commands, the button bar and SPSL can type into this tab: a captured terminal window, or an
        /// embedded application that accepts keystrokes (an RDP session)
        /// </summary>
        public bool CanReceiveCommands
        {
            get { return this.AppPanel.ExternalProcessCaptured || this.AppPanel.EmbeddedApplication is ICommandReceiver; }
        }

        /// <summary>
        /// Type a command into this tab: as window messages to a terminal, or as keystrokes to an embedded application
        /// such as an RDP session. Safe to call from any thread.
        /// </summary>
        /// <param name="command">The text, keys and delay to send</param>
        public void SendCommand(CommandData command)
        {
            if (command.WaitFor != null)
            {
                this.WaitForOutput(command.WaitFor, command.WaitTimeout);
                return;
            }

            // a following WAITFOR looks at what the session prints after this
            this.MarkLogPosition();
            ICommandReceiver receiver = this.AppPanel.EmbeddedApplication as ICommandReceiver;
            if (receiver != null)
            {
                receiver.SendCommand(command);
            }
            else
            {
                command.SendToTerminal(this.AppPanel.AppWindowHandle.ToInt32());
            }
        }

        /// <summary>The file PuTTY logs this session's output to (see SessionLog); null for RDP, VNC, cmd and other non-PuTTY tabs</summary>
        public string SessionLogPath
        {
            get { return this.m_puttyStartInfo.SessionLogPath; }
        }

        /// <summary>How long WAITFOR waits between looks at the log</summary>
        private const int WaitForPollMs = 100;

        /// <summary>Byte position in the session log where the next WAITFOR starts looking</summary>
        private long logReadPosition;

        /// <summary>How far back to look for the start of the current line</summary>
        private const int CurrentLineLookBack = 4096;

        /// <summary>
        /// Make the next WAITFOR look at the current (last) line of output and anything after it, e.g. a prompt that is
        /// on screen now, but not at earlier lines
        /// </summary>
        private void MarkLogPositionAtCurrentLine()
        {
            if (this.SessionLogPath == null)
                return;
            try
            {
                long length = SessionLog.LengthOf(this.SessionLogPath);
                long start = Math.Max(0, length - CurrentLineLookBack);
                string tail = SessionLog.ReadFrom(this.SessionLogPath, start);
                int lineStart = tail.LastIndexOf('\n') + 1;
                this.logReadPosition = start + System.Text.Encoding.UTF8.GetByteCount(tail.Substring(0, lineStart));
            }
            catch (System.IO.IOException ex)
            {
                Log.Debug("Could not read the session log", ex);
                this.logReadPosition = SessionLog.LengthOf(this.SessionLogPath);
            }
        }

        /// <summary>Make the next WAITFOR look only at output that comes after this point</summary>
        private void MarkLogPosition()
        {
            if (this.SessionLogPath != null)
            {
                this.logReadPosition = SessionLog.LengthOf(this.SessionLogPath);
            }
        }

        /// <summary>
        /// Wait until <paramref name="text"/> appears in the session's output after the last command sent (or since
        /// the session started), by reading the session log. Must not be called on the UI thread.
        /// </summary>
        /// <param name="text">The text to wait for, matched exactly (case-sensitive)</param>
        /// <param name="timeout">How long to wait</param>
        /// <returns>true if the text appeared; false on timeout, or when this tab has no session log (non-PuTTY tabs)</returns>
        public bool WaitForOutput(string text, TimeSpan timeout)
        {
            return this.WaitForOutput(text, timeout, CancellationToken.None);
        }

        /// <summary>As <see cref="WaitForOutput(string, TimeSpan)"/>, but returns false as soon as <paramref name="cancel"/> is cancelled</summary>
        /// <param name="text">The text to wait for, matched exactly (case-sensitive)</param>
        /// <param name="timeout">How long to wait</param>
        /// <param name="cancel">Stops the wait, e.g. when the user stops the script</param>
        /// <returns>true if the text appeared</returns>
        public bool WaitForOutput(string text, TimeSpan timeout, CancellationToken cancel)
        {
            if (this.SessionLogPath == null)
            {
                Log.WarnFormat("WAITFOR needs a session log, which only PuTTY sessions have; {0} has none", this.Session.SessionId);
                return false;
            }

            DateTime giveUp = DateTime.Now + timeout;
            while (DateTime.Now < giveUp && !this.IsDisposed && !cancel.IsCancellationRequested)
            {
                try
                {
                    string output = SessionLog.ReadFrom(this.SessionLogPath, this.logReadPosition);
                    int found = output.IndexOf(text, StringComparison.Ordinal);
                    if (found >= 0)
                    {
                        // the next WAITFOR starts after this match
                        this.logReadPosition += System.Text.Encoding.UTF8.GetByteCount(output.Substring(0, found + text.Length));
                        return true;
                    }
                }
                catch (System.IO.IOException ex)
                {
                    Log.Debug("Could not read the session log yet", ex);
                }
                // returns early if the script is stopped
                cancel.WaitHandle.WaitOne(WaitForPollMs);
            }
            if (cancel.IsCancellationRequested)
                return false;
            Log.WarnFormat("WAITFOR \"{0}\" timed out after {1} s in {2}", text, timeout.TotalSeconds, this.Session.SessionId);
            return false;
        }

        /// <summary>
        /// Run an SPSL script in this tab on a background thread, one line at a time. A WAITFOR that times out, or a
        /// line that fails, stops the script, so nothing is typed into the wrong prompt. Used for the session's own
        /// script when it opens and for button bar scripts.
        /// </summary>
        /// <param name="script">The script; lines starting with # (including #!/bin/spsl) are comments</param>
        /// <param name="fromSessionStart">
        /// true for the session's own script: the first WAITFOR looks at everything since the session started.
        /// false (a button): it looks at the current line and anything after it, so a prompt already on screen
        /// matches but older output (e.g. the login prompt of an already logged-in session) doesn't.
        /// </param>
        /// <param name="name">What the script is, shown in the status-bar indicator, e.g. the button's label</param>
        /// <param name="source">What started it, e.g. the button bar's CommandButton, which then shows a spinner</param>
        public void RunScript(string script, bool fromSessionStart = false, string name = "Script", object source = null)
        {
            if (fromSessionStart)
            {
                this.logReadPosition = 0;
            }
            else
            {
                this.MarkLogPositionAtCurrentLine();
            }
            // only command lines count towards progress; blank lines and # comments (including #!/bin/spsl) don't
            string[] lines = (script ?? string.Empty).Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                .ToArray();
            ScriptRun run = new ScriptRun(name, this, this.Session.SessionName, lines.Length, source);
            run.Begin();
            new Thread(delegate ()
            {
                try
                {
                    this.RunScriptLines(run, lines);
                }
                finally
                {
                    run.End();
                }
            }) { IsBackground = true, Name = "SPSL " + this.Session.SessionId }.Start();
        }

        /// <summary>SLEEP lines, run here rather than by SPSL so a Stop can interrupt them</summary>
        private static readonly System.Text.RegularExpressions.Regex SleepLine = new System.Text.RegularExpressions.Regex(@"^\s*SLEEP\s+(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Run a script's command lines in order on the calling (background) thread, recording progress in
        /// <paramref name="run"/>. Stops when the user stops it, when a WAITFOR times out, or when a line fails, and
        /// reports why in the status bar.
        /// </summary>
        /// <param name="run">The script's progress record, with its stop token</param>
        /// <param name="lines">The command lines (no blank or comment lines)</param>
        private void RunScriptLines(ScriptRun run, string[] lines)
        {
            string where = $"\"{run.Name}\" in {this.Session.SessionName}";
            for (int i = 0; i < lines.Length; i++)
            {
                if (run.IsCancelled)
                    break;

                string line = lines[i];
                run.LineNumber = i + 1;
                // show the command word, and the text for WAITFOR, but never what's typed (it may be a secret)
                string keyword = line.TrimStart().Split(' ')[0].ToUpperInvariant();
                run.CurrentStep = keyword == "WAITFOR" ? line.Trim() : keyword;
                try
                {
                    System.Text.RegularExpressions.Match sleep = SleepLine.Match(line);
                    if (sleep.Success)
                    {
                        run.Token.WaitHandle.WaitOne(int.Parse(sleep.Groups[1].Value));
                        continue;
                    }

                    CommandData step;
                    SPSL.TryParseScriptLine(line, out step);
                    if (step == null)
                        continue;
                    if (step.WaitFor != null)
                    {
                        if (!this.WaitForOutput(step.WaitFor, step.WaitTimeout, run.Token))
                        {
                            if (run.IsCancelled)
                                break;
                            SuperPuTTY.ReportStatus($"Script {where} stopped: \"{step.WaitFor}\" didn't appear within {step.WaitTimeout.TotalSeconds:0} s");
                            return;
                        }
                    }
                    else
                    {
                        this.SendCommand(step);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Script " + where + " stopped at line " + (i + 1) + " (" + keyword + ")", ex);
                    SuperPuTTY.ReportStatus($"Script {where} stopped at line {i + 1}: {ex.Message}");
                    return;
                }
            }

            if (run.IsCancelled)
            {
                Log.InfoFormat("Script {0} stopped by the user at line {1}", where, run.LineNumber);
                SuperPuTTY.ReportStatus($"Script {where} stopped by you at line {run.LineNumber} of {run.LineCount}");
            }
            else
            {
                SuperPuTTY.ReportStatus($"Script {where} finished");
            }
        }

        /// <summary>The tab's icon before a script started, restored when it ends</summary>
        private System.Drawing.Icon iconBeforeScript;

        /// <summary>Whether the tab's icon is currently the script spinner</summary>
        private bool showingScriptSpinner;

        /// <summary>
        /// While a script runs in this tab, show a spinner as the tab's icon, advancing one frame per call; restore the
        /// normal icon when it ends. Called by frmSuperPutty's script timer. Tab icons only show with
        /// Options -> GUI -> Show tab icons on.
        /// </summary>
        /// <param name="running">Whether a script is running in this tab</param>
        public void ShowScriptRunning(bool running)
        {
            if (running)
            {
                if (!this.showingScriptSpinner)
                {
                    this.iconBeforeScript = this.Icon;
                    this.showingScriptSpinner = true;
                }
                this.Icon = Spinner.CurrentIcon();
            }
            else if (this.showingScriptSpinner)
            {
                this.Icon = this.iconBeforeScript;
                this.showingScriptSpinner = false;
            }
            else
            {
                return;
            }
            // the tab strip draws the icon, so repaint it
            if (this.Pane != null)
            {
                this.Pane.Invalidate(true);
            }
        }

        /// <summary>Delete this session's temporary log once the tab has closed (kept logs stay)</summary>
        /// <param name="e">Not used</param>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // scripts can't type into a closed tab
            ScriptRun.CancelAll(this);
            SessionLog.DeleteLaterIfTemporary(this.SessionLogPath);
        }

        internal void SetFocusToChildApplication(string caller)
        {
            if (!this.AppPanel.ExternalProcessCaptured && !this.AppPanel.HasEmbeddedApplication) { return; }

            bool success = false;
            for (int i = 0; i < RefocusAttempts; i++)
            {
                Thread.Sleep(RefocusIntervalMs);
                if (this.AppPanel.ReFocusPuTTY(caller))
                {
                    if (i > 0)
                    {
                        Log.DebugFormat("SetFocusToChildApplication success after {0} attempts", i + 1);
                    }
                    success = true;
                    break;
                }
            }

            if (!success)
            {
                Log.WarnFormat("Unable to SetFocusToChildApplication, {0}", this.Text);
            }
        }

        protected override string GetPersistString()
        {
            string str = String.Format("{0}?SessionId={1}&TabName={2}", 
                this.GetType().FullName, 
                HttpUtility.UrlEncode(this.Session.SessionId), 
                HttpUtility.UrlEncode(this.TextOverride));
            return str;
        }

        /// <summary>Restore sessions from a string containing previous sessions</summary>
        /// <param name="persistString">A string containing the sessions to restore</param>
        /// <returns>The <seealso cref="ctlPuttyPanel"/> object which is the parent of the hosted putty application, null if unable to start session</returns>
        public static ctlPuttyPanel FromPersistString(String persistString)
        {
            ctlPuttyPanel panel = null;
            if (persistString.StartsWith(typeof(ctlPuttyPanel).FullName))
            {
                int idx = persistString.IndexOf("?");
                if (idx != -1)
                {
                    NameValueCollection data = HttpUtility.ParseQueryString(persistString.Substring(idx + 1));
                    string sessionId = data["SessionId"] ?? data["SessionName"];
                    string tabName = data["TabName"];

                    Log.InfoFormat("Restoring putty session, sessionId={0}, tabName={1}", sessionId, tabName);

                    SessionData session = SuperPuTTY.GetSessionById(sessionId);
                    if (session != null)
                    {
                        panel = SuperPuTTY.OpenProtoSession(session);
                        if (panel == null)
                        {
                            Log.WarnFormat("Could not restore putty session, sessionId={0}", sessionId);
                        }
                        else
                        {
                            panel.Icon = SuperPuTTY.GetIconForSession(session);
                            panel.Text = tabName;
                            panel.TextOverride = tabName;
                        }
                    }
                    else
                    {
                        Log.WarnFormat("Session not found, sessionId={0}", sessionId);
                    }
                }
                else
                {
                    idx = persistString.IndexOf(":");
                    if (idx != -1)
                    {
                        string sessionId = persistString.Substring(idx + 1);
                        Log.InfoFormat("Restoring putty session, sessionId={0}", sessionId);
                        SessionData session = SuperPuTTY.GetSessionById(sessionId);
                        if (session != null)
                        {
                            panel = SuperPuTTY.OpenProtoSession(session);
                        }
                        else
                        {
                            Log.WarnFormat("Session not found, sessionId={0}", sessionId);
                        }
                    }
                }
            }
            return panel;
        }

        private void aboutPuttyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SuperPuTTY.PuTTYAppName == "PuTTY Plus")
            {
                Process.Start("https://github.com/SilverGreen93/putty");
            }
            else if (SuperPuTTY.PuTTYAppName == "KiTTY")
            {
                Process.Start("https://www.9bis.net/kitty/index.html");
            }
            else if (SuperPuTTY.PuTTYAppName == "PuTTY")
            {
                Process.Start("http://www.chiark.greenend.org.uk/~sgtatham/putty/");
            }
            else
            {
                Messenger.MessageBox("No information available.", "About " + SuperPuTTY.PuTTYAppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            
        }

 
        private void duplicateSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SuperPuTTY.OpenProtoSession(this.Session);
        }

        private void renameTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            dlgRenameItem dialog = new dlgRenameItem
            {
                ItemName = this.Text,
                DetailName = this.Session.SessionId,
                Text = "Rename tab"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                this.Text = dialog.ItemName;
                this.TextOverride = dialog.ItemName;
            }
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.AppPanel != null)
            {
                this.AppPanel.RefreshAppWindow();
            }
        }

        public SessionData Session { get; set; }
        public ApplicationPanel AppPanel { get; private set; }
        public ctlPuttyPanel previousPanel { get; set; }
        public ctlPuttyPanel nextPanel { get; set; }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            CreateMenu();
        }

        private void newSessionTSMI_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = (ToolStripMenuItem) sender;
            SessionData session = menuItem.Tag as SessionData;
            if (session != null)
            {
                SuperPuTTY.OpenProtoSession(session);
            }
        }

        private void puTTYMenuTSMI_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = (ToolStripMenuItem) sender;
            string[] tags = ((ToolStripMenuItem)sender).Tag.ToString().Split(';');

            for (int i = 0; i < tags.Length; ++i)
            {
                uint command = Convert.ToUInt32(tags[i], 16);
                Log.DebugFormat("Sending Putty Command from: menu={2}, tag={0}, command={1}", tags[i], command, menuItem.Text);
                SendPuttyCommand(command);
            }
        }

        internal void SendPuttyCommand(uint command)
        {
            Log.DebugFormat("Sending Putty Command: command={0}", command);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    SetFocusToChildApplication("SendPuttyCommand");
                    NativeMethods.SendMessage(AppPanel.AppWindowHandle, (uint)NativeMethods.WM.SYSCOMMAND, command, 0);
                }
                catch (Exception ex)
                {
                    Log.ErrorFormat("Error sending putty command to embedded putty", ex);
                }
            });
        }

        public bool AcceptCommands
        {
            get { return this.acceptCommandsToolStripMenuItem.Checked;  }
            set { this.acceptCommandsToolStripMenuItem.Checked = value; }
        }

        public string TextOverride { get; set; }

        private void copyHostNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Session.Port != 0)
            {
                Clipboard.SetText(Session.Host + " " + Session.Port);
            }
            else
            {
                Clipboard.SetText(Session.Host);
            }
        }

        private void saveSessionAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionData session = (SessionData)Session.Clone();
            session.SessionName = Text;
            // Get an instance of the SessionTreeview to call the non-static method  
            var mainForm = Application.OpenForms.OfType<frmSuperPutty>().FirstOrDefault();
            if (mainForm != null)
            {
                var sessionTree = mainForm.SessionTreeviewInstance;
                sessionTree?.CreateOrEditSession(1, session);
            }
        }
    }
}
