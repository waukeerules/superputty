using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DarkModeForms;
using log4net;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Highlighted Output: a session's output shown in a Monaco editor with syntax highlighting (e.g. Cisco interfaces
    /// that are up drawn green), next to the PuTTY tab it comes from. PuTTY keeps running as always; this panel only
    /// reads the session log PuTTY writes (Utils.SessionLog) and hands its bytes to a page in a WebView2
    /// (web\output\index.html), which rebuilds the screen with xterm.js and shows it. Opened from the tab menu
    /// (ctlPuttyPanel) when Tools -> Options -> Highlighting is on. Rule and theme files come from
    /// Utils.OutputHighlighting. Not saved in layouts. See docs/MonacoTerminal.md.
    /// </summary>
    public class HighlightedOutputPanel : ToolWindow
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(HighlightedOutputPanel));

        /// <summary>How often the log is checked for new output</summary>
        private const int PollMs = 250;

        /// <summary>The most bytes sent to the page at once; the next chunk waits for the page's ack</summary>
        private const int ChunkBytes = 512 * 1024;

        /// <summary>The open panel for each session tab, so the menu item shows the existing one</summary>
        private static readonly Dictionary<ctlPuttyPanel, HighlightedOutputPanel> open = new Dictionary<ctlPuttyPanel, HighlightedOutputPanel>();

        /// <summary>The browser control showing the page</summary>
        private readonly WebView2 webView;

        /// <summary>Checks the log for new output</summary>
        private readonly Timer timer;

        /// <summary>The session tab being followed; null once the user opened a file instead</summary>
        private ctlPuttyPanel session;

        /// <summary>The log or file being shown</summary>
        private string sourcePath;

        /// <summary>How many bytes of <see cref="sourcePath"/> have been sent to the page</summary>
        private long position;

        /// <summary>Whether the page has loaded and can take output</summary>
        private bool pageReady;

        /// <summary>Whether a chunk was sent and the page hasn't said it's done with it yet</summary>
        private bool waitingForAck;

        /// <summary>
        /// Show the panel for a session tab: the open one, or a new one docked to the right of the tab. Tells the user
        /// when the WebView2 runtime is missing instead.
        /// </summary>
        /// <param name="panel">The PuTTY session tab; it must have a session log</param>
        public static void ShowFor(ctlPuttyPanel panel)
        {
            HighlightedOutputPanel existing;
            if (open.TryGetValue(panel, out existing) && !existing.IsDisposed)
            {
                existing.Activate();
                return;
            }
            if (OutputHighlighting.RuntimeVersion() == null)
            {
                Messenger.MessageBox("Highlighted Output needs the Microsoft Edge WebView2 Runtime, which isn't installed.\n\n"
                    + "It comes with current Windows 10 and 11; otherwise it can be installed from "
                    + "https://developer.microsoft.com/microsoft-edge/webview2/",
                    "Highlighted Output", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            HighlightedOutputPanel output = new HighlightedOutputPanel(panel);
            open[panel] = output;
            if (panel.Pane != null && panel.DockState == DockState.Document)
            {
                output.Show(panel.Pane, DockAlignment.Right, 0.5);
            }
            else
            {
                output.Show(SuperPuTTY.MainForm.DockPanel, DockState.Document);
            }
        }

        /// <summary>Create the panel for a session tab; the page loads once the panel's handle exists</summary>
        /// <param name="panel">The session tab to follow</param>
        private HighlightedOutputPanel(ctlPuttyPanel panel)
        {
            this.session = panel;
            this.sourcePath = panel.SessionLogPath;
            this.Text = "Output: " + panel.Text;
            this.TabText = this.Text;
            this.ToolTipText = "Highlighted output of " + panel.Text;

            this.webView = new WebView2 { Dock = DockStyle.Fill };
            this.webView.DefaultBackgroundColor = IsDark() ? System.Drawing.Color.FromArgb(30, 30, 30) : System.Drawing.Color.White;
            this.Controls.Add(this.webView);

            this.timer = new Timer { Interval = PollMs };
            this.timer.Tick += (s, e) => this.SendNewOutput();

            panel.FormClosed += this.OnSessionClosed;
        }

        /// <summary>Whether SuperPuTTY uses a dark theme, so the page starts with the dark theme too</summary>
        /// <returns>true for the dark themes</returns>
        private static bool IsDark()
        {
            return SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme;
        }

        /// <summary>Not restored from layouts: the session's log is only valid while its tab is open</summary>
        /// <returns>A string RestoreLayoutFromPersistString doesn't recognise, so the panel is skipped</returns>
        protected override string GetPersistString()
        {
            return typeof(HighlightedOutputPanel).FullName;
        }

        /// <summary>Start loading the page once the panel has a window</summary>
        /// <param name="e">Not used</param>
        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                CoreWebView2Environment environment = await OutputHighlighting.GetEnvironment();
                await this.webView.EnsureCoreWebView2Async(environment);
                if (this.IsDisposed)
                    return;
                CoreWebView2 core = this.webView.CoreWebView2;
#if DEBUG
                core.Settings.AreDevToolsEnabled = true;
#else
                core.Settings.AreDevToolsEnabled = false;
#endif
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsZoomControlEnabled = true;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.SetVirtualHostNameToFolderMapping(OutputHighlighting.HostName, OutputHighlighting.WebFolder, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += this.OnNavigationStarting;
                core.NewWindowRequested += (s, args) => args.Handled = true;
                core.WebMessageReceived += this.OnWebMessage;
                core.Navigate(OutputHighlighting.PageUrl);
            }
            catch (Exception ex)
            {
                Log.Error("Could not start Highlighted Output", ex);
                Messenger.MessageBox("Highlighted Output couldn't start: " + ex.Message, "Highlighted Output", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Keep the page on its own files; nothing else may be loaded in the panel</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">The navigation, cancelled unless it's to the page's host</param>
        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            Uri uri;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) || !string.Equals(uri.Host, OutputHighlighting.HostName, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        }

        /// <summary>Handle a message from the page: ready, ack, save, open, folder or reload</summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">The message, a JSON object with a "type"</param>
        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            JObject message;
            try
            {
                message = JObject.Parse(e.WebMessageAsJson);
            }
            catch (JsonException ex)
            {
                Log.Warn("Unreadable message from the Highlighted Output page", ex);
                return;
            }
            switch ((string)message["type"])
            {
                case "ready":
                    this.SendConfig();
                    this.pageReady = true;
                    this.position = 0;
                    this.waitingForAck = false;
                    this.timer.Start();
                    this.SendNewOutput();
                    break;
                case "ack":
                    this.waitingForAck = false;
                    this.SendNewOutput();
                    break;
                case "save":
                    this.SaveText((string)message["text"]);
                    break;
                case "open":
                    this.OpenFile();
                    break;
                case "folder":
                    this.OpenRulesFolder();
                    break;
                case "reload":
                    this.SendConfig();
                    break;
            }
        }

        /// <summary>Send a message object to the page as JSON</summary>
        /// <param name="message">An object with a "type" property</param>
        private void Post(object message)
        {
            if (this.webView.CoreWebView2 != null)
            {
                this.webView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(message));
            }
        }

        /// <summary>Send the page the rule sets and themes, the source's name and whether SuperPuTTY is dark</summary>
        private void SendConfig()
        {
            JArray ruleSets, themes;
            List<string> problems = OutputHighlighting.Load(out ruleSets, out themes);
            this.Post(new { type = "config", ruleSets, themes, dark = IsDark(), title = this.SourceTitle() });
            if (problems.Count > 0)
            {
                Log.Warn("Highlighting files skipped: " + string.Join("; ", problems));
                this.Post(new { type = "status", text = "Some rule or theme files were skipped: " + string.Join("; ", problems) });
            }
        }

        /// <summary>The name shown in the page's status: the session's tab text, or the opened file's name</summary>
        /// <returns>The name</returns>
        private string SourceTitle()
        {
            return this.session != null ? this.session.Text : Path.GetFileName(this.sourcePath);
        }

        /// <summary>
        /// Send the next chunk of output, if there is one and the page has finished the last. A log that got shorter
        /// was started again (PuTTY's Restart Session); the page keeps what it has and continues below a separator.
        /// </summary>
        private void SendNewOutput()
        {
            if (!this.pageReady || this.waitingForAck || string.IsNullOrEmpty(this.sourcePath))
                return;
            try
            {
                long length = SessionLog.LengthOf(this.sourcePath);
                if (length < this.position)
                {
                    this.Post(new { type = "restart" });
                    this.position = 0;
                }
                if (length <= this.position)
                    return;
                byte[] bytes = SessionLog.ReadBytesFrom(this.sourcePath, this.position, ChunkBytes);
                if (bytes.Length == 0)
                    return;
                this.position += bytes.Length;
                this.waitingForAck = true;
                this.Post(new { type = "data", data = Convert.ToBase64String(bytes) });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // PuTTY may be rotating or deleting the log; try again next tick
                Log.Debug("Could not read " + this.sourcePath, ex);
            }
        }

        /// <summary>
        /// The session tab closed: stop following it. Its temporary log is deleted a few seconds later, so the
        /// output already shown stays but nothing more is read.
        /// </summary>
        /// <param name="sender">The session tab</param>
        /// <param name="e">Not used</param>
        private void OnSessionClosed(object sender, FormClosedEventArgs e)
        {
            ctlPuttyPanel panel = sender as ctlPuttyPanel;
            if (panel != null)
            {
                open.Remove(panel);
            }
            if (this.session == panel && !this.IsDisposed)
            {
                // read what's left now, before a temporary log is deleted
                this.waitingForAck = false;
                this.SendNewOutput();
                this.session = null;
                this.Text = this.Text + " (closed)";
                this.TabText = this.Text;
            }
        }

        /// <summary>Save the page's text (what the session showed) to a file the user picks</summary>
        /// <param name="text">The text, with any line endings</param>
        private void SaveText(string text)
        {
            using (SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save Output",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = string.Join("_", (this.SourceTitle() ?? "output").Split(Path.GetInvalidFileNameChars())) + ".txt"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    File.WriteAllText(dialog.FileName, (text ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n"));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Messenger.MessageBox("Could not save: " + ex.Message, "Save Output", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>Show a log or text file instead of the session (e.g. a kept session log, or to try rules out)</summary>
        private void OpenFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Open Log or Text File",
                Filter = "Logs and text files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
                InitialDirectory = SessionLog.KeptLogFolder
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                if (this.session != null)
                {
                    open.Remove(this.session);
                    this.session.FormClosed -= this.OnSessionClosed;
                    this.session = null;
                }
                this.sourcePath = dialog.FileName;
                this.position = 0;
                this.waitingForAck = false;
                this.Text = "Output: " + Path.GetFileName(this.sourcePath);
                this.TabText = this.Text;
                this.Post(new { type = "clear", title = this.SourceTitle() });
                this.SendNewOutput();
            }
        }

        /// <summary>Open the folder for the user's own rules and themes in Explorer, creating it with a README</summary>
        private void OpenRulesFolder()
        {
            string folder = OutputHighlighting.PrepareFolder();
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Messenger.MessageBox("Could not open " + folder + ": " + ex.Message, "Highlighted Output", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Stop reading and release the browser when the panel closes</summary>
        /// <param name="e">Not used</param>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            this.timer.Stop();
            if (this.session != null)
            {
                this.session.FormClosed -= this.OnSessionClosed;
                open.Remove(this.session);
            }
            base.OnFormClosed(e);
        }

        /// <summary>Dispose of the timer and the browser control</summary>
        /// <param name="disposing">true when called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.timer.Dispose();
                this.webView.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
