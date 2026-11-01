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
            this.TabText = TabIcons.WithPad(session.SessionName);
            this.TextOverride = session.SessionName;

            CreatePanel();
            ApplyEnvironmentColor();
            AdjustMenu();
            AddOutputMenuItems();
            // the black check glyph is hard to see on the dark menu
            MenuCheckMarks.Apply(this.contextMenuStrip1);

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
                // padded to make room for the larger tab icon (see TabIcons)
                this.TabText = TabIcons.WithPad(value != null ? value.Replace("&", "&&") : null);
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

        /// <summary>The tab's own background, put back when its session loses its environment colour</summary>
        private System.Drawing.Color? normalBackColor;

        /// <summary>
        /// Show the session's environment colour (Gui.EnvironmentColors, e.g. red for production) as a frame around
        /// the terminal, so it isn't mistaken for another; no frame when it has none. The tab's stripe is drawn by
        /// TabIcons. frmSuperPutty calls this again whenever a colour may have changed.
        /// </summary>
        public void ApplyEnvironmentColor()
        {
            System.Drawing.Color? color = EnvironmentColors.For(this.Session);
            // the built-in terminal also draws its cursor in it
            this.Terminal?.SetCursorColor(color);
            if (this.normalBackColor == null)
            {
                this.normalBackColor = this.BackColor;
            }
            // the terminal is docked to fill, so padding shows the background around it as a frame
            int width = color == null ? 0 : this.LogicalToDeviceUnits(3);
            if (this.Padding.All != width)
            {
                this.Padding = new Padding(width);
            }
            this.environmentColor = color;
            this.BackColor = color ?? this.normalBackColor.Value;
        }

        /// <summary>The frame's colour while the session has one (<see cref="ApplyEnvironmentColor"/>); null for none</summary>
        private System.Drawing.Color? environmentColor;

        /// <summary>
        /// Keep the frame's colour when something else sets the background. The dark theme (DarkModeCS) recolours every
        /// control added to the window, and a tab opened after start-up is added after its colour was set, so its frame
        /// took the theme's grey. The colour set over it becomes the tab's own, for when the session loses its colour.
        /// </summary>
        /// <param name="e">Not used</param>
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if (this.environmentColor.HasValue && this.BackColor != this.environmentColor.Value)
            {
                this.normalBackColor = this.BackColor;
                this.BackColor = this.environmentColor.Value;
            }
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
            else if (TerminalControl.ShouldUse(this.Session))
            {
                // the built-in terminal: plink in a pseudo console, drawn and highlighted by SuperPuTTY, not a PuTTY window.
                // It writes the same session log PuTTY would have.
                SessionData resolved = SessionVariables.Resolve(this.Session);
                // cmd, PowerShell and WSL have no log of their own; in the built-in terminal they get one like PuTTY's
                string logPath = this.m_puttyStartInfo.SessionLogPath ?? SessionLog.NewLogPath(this.Session);
                this.AppPanel.EmbeddedApplicationFactory = () =>
                {
                    TerminalControl terminal = new TerminalControl(resolved, logPath);
                    terminal.StateChanged += this.OnTerminalStateChanged;
                    terminal.SetCursorColor(EnvironmentColors.For(this.Session));
                    return terminal;
                };
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

        /// <summary>
        /// The tab menu's Close, Close others, Close others to the right and Close all, for the built-in terminal's
        /// own right-click menu (TerminalControl), so both close tabs the same way
        /// </summary>
        /// <param name="command">"close", "closeOthers", "closeRight" or "closeAll"</param>
        public void RunTabCommand(string command)
        {
            switch (command)
            {
                case "close": this.closeSessionToolStripMenuItem_Click(this, EventArgs.Empty); break;
                case "closeOthers": this.closeOthersToolStripMenuItem_Click(this, EventArgs.Empty); break;
                case "closeRight": this.closeOthersToTheRightToolStripMenuItem_Click(this, EventArgs.Empty); break;
                case "closeAll": this.closeAllToolStripMenuItem_Click(this, EventArgs.Empty); break;
            }
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
            // the built-in terminal writes the log, also for cmd, PowerShell and WSL, which have none in their own window
            get { return this.Terminal?.LogPath ?? this.m_puttyStartInfo.SessionLogPath; }
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
            if (this.Terminal != null)
            {
                this.Terminal.MarkOutput(true);
                return;
            }
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
            if (this.Terminal != null)
            {
                this.Terminal.MarkOutput(false);
            }
            else if (this.SessionLogPath != null)
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
                TerminalControl terminal = this.Terminal;
                if (terminal != null)
                {
                    // the built-in terminal also has the line the cursor is on, which isn't in the log until it's finished
                    string shown = terminal.OutputSinceMark();
                    int at = shown.IndexOf(text, StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        terminal.AdvanceMark(at + text.Length);
                        return true;
                    }
                    cancel.WaitHandle.WaitOne(WaitForPollMs);
                    continue;
                }
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

        /// <summary>The tab's own icon, restored when no spinner or alert badge is showing; null while it's showing</summary>
        private System.Drawing.Icon normalIcon;

        /// <summary>Whether a script is running in this tab (set by <see cref="ShowScriptRunning"/>)</summary>
        private bool scriptRunning;

        /// <summary>
        /// While a script runs in this tab, show a spinner as the tab's icon, advancing one frame per call; restore the
        /// normal icon when it ends. Called by frmSuperPutty's script timer. Tab icons only show with
        /// Options -> GUI -> Show tab icons on.
        /// </summary>
        /// <param name="running">Whether a script is running in this tab</param>
        public void ShowScriptRunning(bool running)
        {
            if (!running && !this.scriptRunning)
                return;
            this.scriptRunning = running;
            this.UpdateTabIcon();
        }

        /// <summary>
        /// Show the tab's icon by priority: the script spinner, then the disconnected badge, then an activity badge, then
        /// a silence badge, then the tab's own icon
        /// </summary>
        private void UpdateTabIcon()
        {
            System.Drawing.Icon badge = this.scriptRunning ? Spinner.CurrentIcon()
                : this.connection != ConnectionState.Connected ? TabBadges.Disconnected
                : this.outputAlert == OutputAlert.Activity ? TabBadges.Activity
                : this.outputAlert == OutputAlert.Silence ? TabBadges.Silence
                : null;
            if (badge != null)
            {
                if (this.normalIcon == null)
                {
                    this.normalIcon = TabIcons.Get(this);
                }
                if (TabIcons.Get(this) == badge)
                    return;
                TabIcons.Set(this, badge);
            }
            else if (this.normalIcon != null)
            {
                TabIcons.Set(this, this.normalIcon);
                this.normalIcon = null;
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

        /// <summary>The alert a watched tab is showing</summary>
        private enum OutputAlert { None, Activity, Silence }

        /// <summary>The alert showing now; cleared when the user looks at the tab</summary>
        private OutputAlert outputAlert = OutputAlert.None;

        /// <summary>Alert when this tab prints something while the user isn't looking at it (tab menu -> Watch for Activity)</summary>
        private bool watchActivity;

        /// <summary>Alert when this tab prints nothing for this long; 0 when not watching (tab menu -> Watch for Silence)</summary>
        private int watchSilenceSeconds;

        /// <summary>The session log's length at the last check</summary>
        private long watchedLogLength;

        /// <summary>When the session last printed something</summary>
        private DateTime lastOutputTime = DateTime.Now;

        /// <summary>Whether the silence alert has fired since the last output, so it fires once per quiet spell</summary>
        private bool silenceAlerted;

        /// <summary>Whether this tab is watched for activity or silence</summary>
        public bool IsWatched
        {
            get { return this.watchActivity || this.watchSilenceSeconds > 0; }
        }

        /// <summary>
        /// Check a watched tab's session log for new output (called about once a second by frmSuperPutty). New output
        /// while the user isn't looking raises an activity alert; no output for the silence time, after the tab has
        /// printed something, raises a silence alert. Looking at the tab clears the alert.
        /// </summary>
        /// <param name="seen">Whether the user is looking at this tab: it's the active tab and SuperPuTTY is in front</param>
        /// <param name="appInFront">Whether SuperPuTTY is in front; if not, a new alert also flashes its taskbar button</param>
        public void CheckOutputAlerts(bool seen, bool appInFront)
        {
            if (this.SessionLogPath == null || !this.IsWatched)
                return;

            long length = SessionLog.LengthOf(this.SessionLogPath);
            bool newOutput = length != this.watchedLogLength;
            if (newOutput)
            {
                this.watchedLogLength = length;
                this.lastOutputTime = DateTime.Now;
                this.silenceAlerted = false;
            }

            if (seen)
            {
                this.SetOutputAlert(OutputAlert.None, appInFront);
            }
            else if (newOutput && this.watchActivity)
            {
                this.SetOutputAlert(OutputAlert.Activity, appInFront);
            }
            else if (this.watchSilenceSeconds > 0 && !this.silenceAlerted && length > 0
                && (DateTime.Now - this.lastOutputTime).TotalSeconds >= this.watchSilenceSeconds)
            {
                this.silenceAlerted = true;
                this.SetOutputAlert(OutputAlert.Silence, appInFront);
            }
        }

        /// <summary>Show or clear an alert: the tab badge, and for a new alert a status message and a taskbar flash</summary>
        /// <param name="alert">The alert to show, or None to clear</param>
        /// <param name="appInFront">Whether SuperPuTTY is in front; if not, the taskbar button flashes</param>
        private void SetOutputAlert(OutputAlert alert, bool appInFront = true)
        {
            if (alert == this.outputAlert)
                return;
            this.outputAlert = alert;
            this.UpdateTabIcon();
            if (alert == OutputAlert.None)
                return;

            SuperPuTTY.ReportStatus(alert == OutputAlert.Activity
                ? $"New output in {this.Text}"
                : $"{this.Text} has been quiet for {this.watchSilenceSeconds} s");
            if (!appInFront)
            {
                // flash the taskbar button until SuperPuTTY comes to the front
                NativeMethods.FlashWindow(SuperPuTTY.MainForm.Handle, NativeMethods.FLASHW_TRAY | NativeMethods.FLASHW_TIMERNOFG);
            }
        }

        /// <summary>Start or stop watching for activity, from the tab menu</summary>
        /// <param name="watch">true to watch</param>
        private void WatchForActivity(bool watch)
        {
            this.watchActivity = watch;
            this.StartWatching();
        }

        /// <summary>Start or stop watching for silence, from the tab menu</summary>
        /// <param name="seconds">How long a quiet spell must last; 0 to stop watching</param>
        private void WatchForSilence(int seconds)
        {
            this.watchSilenceSeconds = seconds;
            this.StartWatching();
        }

        /// <summary>Count only output from now on, and clear any alert</summary>
        private void StartWatching()
        {
            this.watchedLogLength = this.SessionLogPath != null ? SessionLog.LengthOf(this.SessionLogPath) : 0;
            this.lastOutputTime = DateTime.Now;
            this.silenceAlerted = false;
            this.SetOutputAlert(OutputAlert.None);
        }

        /// <summary>Where a PuTTY tab's session stands, as far as auto-reconnect knows</summary>
        private enum ConnectionState
        {
            /// <summary>Connected, or never seen to drop</summary>
            Connected,
            /// <summary>Dropped or ended; the tab shows the disconnected badge</summary>
            Disconnected,
            /// <summary>Restart Session was sent; waiting for the session to print something</summary>
            Reconnecting
        }

        /// <summary>The session's state; see <see cref="CheckConnection"/></summary>
        private ConnectionState connection = ConnectionState.Connected;

        /// <summary>Automatic reconnects tried since the session last dropped</summary>
        private int reconnectAttempts;

        /// <summary>When the next reconnect is due; DateTime.MinValue when none is scheduled</summary>
        private DateTime nextReconnectAt = DateTime.MinValue;

        /// <summary>When Restart Session was last sent</summary>
        private DateTime reconnectSentAt;

        /// <summary>The session log's length when Restart Session was sent; more output means the session is back</summary>
        private long logLengthAtReconnect;

        /// <summary>
        /// Whether PuTTY has shown the session as inactive since it dropped. Until it has, an active title just means
        /// PuTTY hasn't caught up after its error box closed, not that the session was restarted from PuTTY's own menu.
        /// </summary>
        private bool seenInactive;

        /// <summary>The error box already handled, so it isn't reported again while it stays open</summary>
        private IntPtr handledErrorBox;

        /// <summary>How often, while reconnecting, Restart Session is sent again in case PuTTY wasn't ready for it</summary>
        private const int ResendRestartSeconds = 5;

        /// <summary>How long a reconnected session may print nothing before it's taken to be back anyway</summary>
        private const int QuietReconnectSeconds = 15;

        /// <summary>Whether the session has dropped or ended and isn't back yet</summary>
        public bool IsDisconnected
        {
            get { return this.connection != ConnectionState.Connected; }
        }

        /// <summary>
        /// Check whether this PuTTY tab's session has dropped, and reconnect it (called about once a second by
        /// frmSuperPutty). A drop shows PuTTY's "Fatal Error" box: the tab then gets the disconnected badge and a status
        /// message, and with Options -> Advanced -> Dropped sessions on, the box is closed and Restart Session is sent
        /// after the set delay, up to the set number of attempts. A session that ends without an error (still open,
        /// title "(inactive)") gets the badge but isn't reconnected automatically. The session is back once it prints
        /// something again. Only PuTTY protocols are checked; see <see cref="PuttyWindow"/>.
        /// </summary>
        /// <param name="appInFront">Whether SuperPuTTY is in front; if not, a drop also flashes its taskbar button</param>
        public void CheckConnection(bool appInFront)
        {
            IntPtr window = this.AppPanel.AppWindowHandle;
            if (this.SessionLogPath == null || window == IntPtr.Zero || this.AppPanel.HasEmbeddedApplication)
                return;

            string error;
            IntPtr box = PuttyWindow.FindFatalErrorBox(window, out error);
            bool inactive = PuttyWindow.IsInactive(window);
            bool autoReconnect = SuperPuTTY.Settings.AutoReconnect;
            int maxAttempts = Math.Max(1, SuperPuTTY.Settings.AutoReconnectAttempts);
            int delay = Math.Max(1, SuperPuTTY.Settings.AutoReconnectDelaySeconds);

            if (box != IntPtr.Zero && box != this.handledErrorBox)
            {
                // the connection dropped, or a reconnect failed
                this.handledErrorBox = box;
                bool dropped = this.connection == ConnectionState.Connected;
                this.SetConnection(ConnectionState.Disconnected);
                if (dropped)
                {
                    this.reconnectAttempts = 0;
                    Log.InfoFormat("Session {0} dropped: {1}", this.Text, error);
                    if (!appInFront)
                    {
                        NativeMethods.FlashWindow(SuperPuTTY.MainForm.Handle, NativeMethods.FLASHW_TRAY | NativeMethods.FLASHW_TIMERNOFG);
                    }
                }

                if (!autoReconnect)
                {
                    // PuTTY's own box stays up; Reconnect in the tab menu closes it
                    SuperPuTTY.ReportStatus($"{this.Text} disconnected: {error}");
                }
                else if (this.reconnectAttempts < maxAttempts)
                {
                    PuttyWindow.DismissFatalError(box);
                    this.nextReconnectAt = DateTime.Now.AddSeconds(delay);
                    SuperPuTTY.ReportStatus(dropped
                        ? $"{this.Text} disconnected: {error}. Reconnecting in {delay} s"
                        : $"Reconnect {this.reconnectAttempts} of {maxAttempts} to {this.Text} failed: {error}. Trying again in {delay} s");
                }
                else
                {
                    PuttyWindow.DismissFatalError(box);
                    this.nextReconnectAt = DateTime.MinValue;
                    Log.InfoFormat("Gave up reconnecting {0} after {1} attempts", this.Text, maxAttempts);
                    SuperPuTTY.ReportStatus($"Gave up reconnecting {this.Text} after {maxAttempts} attempts ({error}). Right-click the tab -> Reconnect to try again");
                }
                return;
            }
            if (box != IntPtr.Zero)
                return;

            switch (this.connection)
            {
                case ConnectionState.Connected:
                    if (inactive)
                    {
                        // ended without an error, e.g. a non-zero exit status: not a drop, so don't reconnect
                        this.SetConnection(ConnectionState.Disconnected);
                        SuperPuTTY.ReportStatus($"{this.Text} ended. Right-click the tab -> Reconnect to start it again");
                    }
                    break;

                case ConnectionState.Disconnected:
                    this.seenInactive |= inactive;
                    if (this.nextReconnectAt != DateTime.MinValue && DateTime.Now >= this.nextReconnectAt)
                    {
                        this.reconnectAttempts++;
                        SuperPuTTY.ReportStatus($"Reconnecting {this.Text} ({this.reconnectAttempts} of {maxAttempts})...");
                        this.SendRestart();
                    }
                    else if (!inactive && this.seenInactive && this.nextReconnectAt == DateTime.MinValue)
                    {
                        // restarted from PuTTY's own menu
                        this.SendRestart();
                    }
                    break;

                case ConnectionState.Reconnecting:
                    if (inactive)
                    {
                        if ((DateTime.Now - this.reconnectSentAt).TotalSeconds >= ResendRestartSeconds)
                        {
                            // PuTTY ignores Restart Session until the session is inactive, so the first may have been early
                            this.SendRestart();
                        }
                    }
                    else if (SessionLog.LengthOf(this.SessionLogPath) > this.logLengthAtReconnect
                        || (DateTime.Now - this.reconnectSentAt).TotalSeconds >= QuietReconnectSeconds)
                    {
                        this.reconnectAttempts = 0;
                        this.SetConnection(ConnectionState.Connected);
                        Log.InfoFormat("Session {0} reconnected", this.Text);
                        SuperPuTTY.ReportStatus($"{this.Text} reconnected");
                    }
                    break;
            }
        }

        /// <summary>Reconnect now, from the tab menu: close PuTTY's error box if it's up, then restart the session</summary>
        public void Reconnect()
        {
            if (this.Terminal != null)
            {
                this.Terminal.Reconnect();
                return;
            }
            IntPtr window = this.AppPanel.AppWindowHandle;
            if (window == IntPtr.Zero)
                return;
            this.reconnectAttempts = 0;
            string error;
            IntPtr box = PuttyWindow.FindFatalErrorBox(window, out error);
            if (box != IntPtr.Zero)
            {
                // PuTTY can't restart until its box is closed and it has marked the session inactive; the next check
                // sends Restart Session
                this.handledErrorBox = box;
                PuttyWindow.DismissFatalError(box);
                this.nextReconnectAt = DateTime.Now.AddSeconds(1);
            }
            else
            {
                this.SendRestart();
            }
            SuperPuTTY.ReportStatus($"Reconnecting {this.Text}...");
        }

        /// <summary>Stop the automatic reconnects scheduled for this tab, from the tab menu</summary>
        private void StopReconnecting()
        {
            this.Terminal?.StopReconnecting();
            this.nextReconnectAt = DateTime.MinValue;
            SuperPuTTY.ReportStatus($"Stopped reconnecting {this.Text}");
        }

        /// <summary>Send PuTTY's Restart Session and wait for the session to come back</summary>
        private void SendRestart()
        {
            this.nextReconnectAt = DateTime.MinValue;
            if (this.connection != ConnectionState.Reconnecting)
            {
                this.logLengthAtReconnect = SessionLog.LengthOf(this.SessionLogPath);
                this.SetConnection(ConnectionState.Reconnecting);
            }
            this.reconnectSentAt = DateTime.Now;
            PuttyWindow.Restart(this.AppPanel.AppWindowHandle);
        }

        /// <summary>Change the connection state and the tab badge with it</summary>
        /// <param name="state">The new state</param>
        private void SetConnection(ConnectionState state)
        {
            if (state == this.connection)
                return;
            this.connection = state;
            if (state == ConnectionState.Disconnected)
            {
                this.seenInactive = false;
            }
            this.UpdateTabIcon();
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raised when any tab's session drops, starts reconnecting or comes back, so the Sessions tree can update the
        /// session's dot (SessionTreeview)
        /// </summary>
        public static event EventHandler ConnectionChanged;

        /// <summary>Silence times offered in the tab menu, in seconds</summary>
        private static readonly int[] SilenceChoices = { 10, 30, 60, 300 };

        /// <summary>The tab menu items added by <see cref="AddOutputMenuItems"/></summary>
        private ToolStripMenuItem menuSearchOutput, menuHighlightedOutput, menuWatchActivity, menuWatchSilence;

        /// <summary>"Reconnect" and "Stop Reconnecting" at the top of the tab menu, shown while the session is down</summary>
        private ToolStripMenuItem menuReconnect, menuStopReconnecting;

        /// <summary>The separator under <see cref="menuReconnect"/></summary>
        private ToolStripSeparator menuReconnectSeparator;

        /// <summary>
        /// Add "Search Output...", "Highlighted Output", "Watch for Activity" and "Watch for Silence" to the tab's menu,
        /// after "Accept Commands". They need a session log, so they're only enabled for PuTTY sessions. Highlighted
        /// Output is only shown when it's turned on in Tools -> Options -> Terminal.
        /// </summary>
        private void AddOutputMenuItems()
        {
            this.menuSearchOutput = new ToolStripMenuItem("Search &Output...", null, (s, e) => dlgSearchOutput.ShowFor(this));
            this.menuHighlightedOutput = new ToolStripMenuItem("&Highlighted Output", null, (s, e) => HighlightedOutputPanel.ShowFor(this));
            this.menuWatchActivity = new ToolStripMenuItem("Watch for &Activity", null, (s, e) => this.WatchForActivity(!this.watchActivity));
            this.menuWatchSilence = new ToolStripMenuItem("Watch for &Silence");
            this.menuWatchSilence.DropDownItems.Add(new ToolStripMenuItem("Off", null, (s, e) => this.WatchForSilence(0)) { Tag = 0 });
            foreach (int seconds in SilenceChoices)
            {
                int choice = seconds;
                string label = seconds < 60 ? seconds + " seconds" : seconds == 60 ? "1 minute" : seconds / 60 + " minutes";
                this.menuWatchSilence.DropDownItems.Add(new ToolStripMenuItem(label, null, (s, e) => this.WatchForSilence(choice)) { Tag = seconds });
            }

            int index = this.contextMenuStrip1.Items.IndexOf(this.acceptCommandsToolStripMenuItem) + 1;
            this.contextMenuStrip1.Items.Insert(index, new ToolStripSeparator());
            this.contextMenuStrip1.Items.Insert(index + 1, this.menuSearchOutput);
            this.contextMenuStrip1.Items.Insert(index + 2, this.menuHighlightedOutput);
            this.contextMenuStrip1.Items.Insert(index + 3, this.menuWatchActivity);
            this.contextMenuStrip1.Items.Insert(index + 4, this.menuWatchSilence);

            this.menuReconnect = new ToolStripMenuItem("&Reconnect", null, (s, e) => this.Reconnect());
            this.menuReconnect.Font = new System.Drawing.Font(this.menuReconnect.Font, System.Drawing.FontStyle.Bold);
            this.menuStopReconnecting = new ToolStripMenuItem("Stop Reconnecting", null, (s, e) => this.StopReconnecting());
            this.menuReconnectSeparator = new ToolStripSeparator();
            this.contextMenuStrip1.Items.Insert(0, this.menuReconnect);
            this.contextMenuStrip1.Items.Insert(1, this.menuStopReconnecting);
            this.contextMenuStrip1.Items.Insert(2, this.menuReconnectSeparator);
        }

        /// <summary>Tick and enable the output menu items to match this tab before the menu opens</summary>
        private void UpdateOutputMenuItems()
        {
            bool hasLog = this.SessionLogPath != null;
            this.menuSearchOutput.Enabled = hasLog;
            this.menuHighlightedOutput.Visible = SuperPuTTY.Settings.HighlightedOutputEnabled;
            this.menuHighlightedOutput.Enabled = hasLog;
            this.menuWatchActivity.Enabled = hasLog;
            this.menuWatchSilence.Enabled = hasLog;
            this.menuWatchActivity.Checked = this.watchActivity;
            this.menuWatchSilence.Checked = this.watchSilenceSeconds > 0;
            foreach (ToolStripMenuItem item in this.menuWatchSilence.DropDownItems.OfType<ToolStripMenuItem>())
            {
                item.Checked = (int)item.Tag == this.watchSilenceSeconds;
            }
            this.menuReconnect.Visible = this.IsDisconnected;
            this.menuReconnectSeparator.Visible = this.IsDisconnected;
            this.menuStopReconnecting.Visible = this.Terminal != null
                ? this.IsDisconnected && this.Terminal.ReconnectScheduled
                : this.connection == ConnectionState.Disconnected && this.nextReconnectAt != DateTime.MinValue;
            if (this.Terminal != null)
            {
                // the built-in terminal has no PuTTY window: no Event Log, Change Settings or About PuTTY
                this.eventLogToolStripMenuItem.Visible = false;
                this.changeSettingsToolStripMenuItem.Visible = false;
                this.aboutPuttyToolStripMenuItem.Visible = false;
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
                            TabIcons.Set(panel, SuperPuTTY.GetIconForSession(session));
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
            UpdateOutputMenuItems();
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
            if (this.Terminal != null)
            {
                this.RunTerminalMenuCommand(tags);
                return;
            }

            for (int i = 0; i < tags.Length; ++i)
            {
                uint command = Convert.ToUInt32(tags[i], 16);
                Log.DebugFormat("Sending Putty Command from: menu={2}, tag={0}, command={1}", tags[i], command, menuItem.Text);
                SendPuttyCommand(command);
            }
        }

        /// <summary>
        /// The built-in terminal's version of PuTTY's menu commands (the tags are PuTTY's IDM_ values): Restart Session
        /// reconnects, Clear Scrollback, Reset Terminal and Copy All to Clipboard are done by the terminal page
        /// </summary>
        /// <param name="tags">The menu item's commands, as hex strings</param>
        private void RunTerminalMenuCommand(string[] tags)
        {
            foreach (string tag in tags)
            {
                switch (Convert.ToUInt32(tag, 16))
                {
                    case 0x0040: this.Terminal.Reconnect(); break;
                    case 0x0060: this.Terminal.RunAction("clearScrollback"); break;
                    case 0x0070: this.Terminal.RunAction("reset"); break;
                    case 0x0170: this.Terminal.RunAction("copyAll"); break;
                }
            }
        }

        /// <summary>The built-in terminal shown in this tab instead of PuTTY; null for PuTTY and other programs</summary>
        public TerminalControl Terminal
        {
            get { return this.AppPanel?.EmbeddedApplication as TerminalControl; }
        }

        /// <summary>The built-in terminal's session dropped, is reconnecting or came back: show it on the tab and in the tree</summary>
        /// <param name="sender">The terminal</param>
        /// <param name="e">Not used</param>
        private void OnTerminalStateChanged(object sender, EventArgs e)
        {
            TerminalControl terminal = (TerminalControl)sender;
            this.SetConnection(terminal.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected);
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
