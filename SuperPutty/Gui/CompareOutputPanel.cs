using System;
using System.Collections.Generic;
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
    /// Compare Output, as SuperXTerminal's (its be6efa5): two tabs' output side by side in Monaco's diff editor, the
    /// differences marked, e.g. one switch's config against another's, or before and after a change. Opened from a
    /// tab's menu, Compare Output With (ctlPuttyPanel). It reads both tabs' session logs (Utils.SessionLog), cleaned of
    /// escape sequences as Search Output shows them, and sends them to web\compare\index.html in a WebView2 whenever
    /// the page asks: when it loads, Refresh, and every 2 seconds with Follow. Docked beside the first tab; not saved in
    /// layouts; closes with either tab.
    /// </summary>
    public class CompareOutputPanel : ToolWindow
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(CompareOutputPanel));

        /// <summary>The page's address, under the host the output pages are served from</summary>
        private const string PageUrl = "https://" + OutputHighlighting.HostName + "/compare/index.html";

        /// <summary>The most of each log compared, from its end: Monaco's diff gets slow on huge texts</summary>
        private const int MaxBytes = 4 * 1024 * 1024;

        /// <summary>The open comparison for each pair of tabs, so the menu shows the existing one</summary>
        private static readonly Dictionary<Tuple<ctlPuttyPanel, ctlPuttyPanel>, CompareOutputPanel> open = new Dictionary<Tuple<ctlPuttyPanel, ctlPuttyPanel>, CompareOutputPanel>();

        /// <summary>The browser control showing the page</summary>
        private readonly WebView2 webView;

        /// <summary>The tab on the left (the original side)</summary>
        private readonly ctlPuttyPanel left;

        /// <summary>The tab on the right (the changed side)</summary>
        private readonly ctlPuttyPanel right;

        /// <summary>
        /// Show the comparison of two tabs: the open one, or a new one docked to the right of the first. Tells the user
        /// when the WebView2 runtime is missing instead.
        /// </summary>
        /// <param name="left">The tab whose menu it was opened from; both must have a session log</param>
        /// <param name="right">The tab it's compared with</param>
        public static void ShowFor(ctlPuttyPanel left, ctlPuttyPanel right)
        {
            Tuple<ctlPuttyPanel, ctlPuttyPanel> key = Tuple.Create(left, right);
            CompareOutputPanel existing;
            if (open.TryGetValue(key, out existing) && !existing.IsDisposed)
            {
                existing.Activate();
                return;
            }
            if (OutputHighlighting.RuntimeVersion() == null)
            {
                Messenger.MessageBox("Compare Output needs the Microsoft Edge WebView2 Runtime, which isn't installed.\n\n"
                    + "It comes with current Windows 10 and 11; otherwise it can be installed from "
                    + "https://developer.microsoft.com/microsoft-edge/webview2/",
                    "Compare Output", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CompareOutputPanel compare = new CompareOutputPanel(left, right);
            open[key] = compare;
            compare.FormClosed += (s, e) => open.Remove(key);
            if (left.Pane != null && left.DockState == DockState.Document)
            {
                compare.Show(left.Pane, DockAlignment.Right, 0.5);
            }
            else
            {
                compare.Show(SuperPuTTY.MainForm.DockPanel, DockState.Document);
            }
        }

        /// <summary>Create the panel for two tabs; the page loads once the panel has a window</summary>
        /// <param name="left">The tab on the left</param>
        /// <param name="right">The tab on the right</param>
        private CompareOutputPanel(ctlPuttyPanel left, ctlPuttyPanel right)
        {
            this.left = left;
            this.right = right;
            this.Text = "Compare: " + left.Text + " ↔ " + right.Text;
            this.TabText = this.Text;
            this.ToolTipText = "The output of " + left.Text + " and " + right.Text + " side by side";
            this.webView = new WebView2 { Dock = DockStyle.Fill };
            this.webView.DefaultBackgroundColor = IsDark() ? System.Drawing.Color.FromArgb(30, 30, 30) : System.Drawing.Color.White;
            this.Controls.Add(this.webView);
            // either tab closing ends the comparison: its log is only valid while it's open
            left.FormClosed += this.OnTabClosed;
            right.FormClosed += this.OnTabClosed;
        }

        /// <summary>Whether SuperPuTTY uses a dark theme, so the page is dark too</summary>
        /// <returns>true for the dark themes</returns>
        private static bool IsDark()
        {
            return SuperPuTTY.Settings.InterfaceTheme < (int)InterfaceTheme.LightTheme;
        }

        /// <summary>Not restored from layouts: the logs are only valid while their tabs are open</summary>
        /// <returns>A string RestoreLayoutFromPersistString doesn't recognise, so the panel is skipped</returns>
        protected override string GetPersistString()
        {
            return typeof(CompareOutputPanel).FullName;
        }

        /// <summary>One of the tabs closed: close the comparison</summary>
        /// <param name="sender">The tab</param>
        /// <param name="e">Not used</param>
        private void OnTabClosed(object sender, FormClosedEventArgs e)
        {
            if (!this.IsDisposed)
            {
                this.Close();
            }
        }

        /// <summary>Stop listening to the tabs once closed</summary>
        /// <param name="e">Not used</param>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            this.left.FormClosed -= this.OnTabClosed;
            this.right.FormClosed -= this.OnTabClosed;
            base.OnFormClosed(e);
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
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.SetVirtualHostNameToFolderMapping(OutputHighlighting.HostName, OutputHighlighting.WebFolder, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += this.OnNavigationStarting;
                core.NewWindowRequested += (s, args) => args.Handled = true;
                core.WebMessageReceived += this.OnWebMessage;
                core.Navigate(PageUrl);
            }
            catch (Exception ex)
            {
                Log.Error("Could not start Compare Output", ex);
                Messenger.MessageBox("Compare Output couldn't start: " + ex.Message, "Compare Output", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        /// <summary>The page asked for both outputs ("refresh"): send them</summary>
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
                Log.Warn("Unreadable message from the Compare Output page", ex);
                return;
            }
            if ((string)message["type"] == "refresh" && this.webView.CoreWebView2 != null)
            {
                object compare = new
                {
                    type = "compare",
                    dark = IsDark(),
                    left = new { title = this.left.Text, text = OutputOf(this.left) },
                    right = new { title = this.right.Text, text = OutputOf(this.right) }
                };
                this.webView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(compare));
            }
        }

        /// <summary>
        /// A tab's output as compared: the end of its session log (up to <see cref="MaxBytes"/>), escape sequences and
        /// control characters taken out as Search Output shows it
        /// </summary>
        /// <param name="panel">The tab</param>
        /// <returns>The lines, joined with line breaks; "" when there's no log or it can't be read</returns>
        private static string OutputOf(ctlPuttyPanel panel)
        {
            string path = panel.SessionLogPath;
            if (path == null)
                return string.Empty;
            try
            {
                long from = Math.Max(0, SessionLog.LengthOf(path) - MaxBytes);
                return string.Join("\n", SessionLog.CleanForDisplay(SessionLog.ReadFrom(path, from)));
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read the session log " + path, ex);
                return string.Empty;
            }
        }
    }
}
